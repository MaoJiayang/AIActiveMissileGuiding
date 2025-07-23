using System;
using System.Collections.Generic;
using VRageMath;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    /// <summary>
    /// 用于存储目标数据的结构（因无法直接构造 MyDetectedEntityInfo，因此自定义）
    /// </summary>
    public struct SimpleTargetInfo
    {
        public Vector3D Position;
        public Vector3D Velocity;
        public long TimeStamp; // 以毫秒为单位
        public SimpleTargetInfo(Vector3D position, Vector3D velocity, long timeStamp)
        {
            Position = position;
            Velocity = velocity;
            TimeStamp = timeStamp;
        }

        // 从 MyDetectedEntityInfo 创建
        public static SimpleTargetInfo FromDetectedInfo(MyDetectedEntityInfo info)
        {
            return new SimpleTargetInfo(
                info.Position,
                new Vector3D(info.Velocity),
                info.TimeStamp
            );
        }
    }
    /// <summary>
    /// 圆周运动参数结构
    /// </summary>
    public struct CircularMotionParams
    {
        public Vector3D Center;
        public double Radius;
        public double AngularVelocity;
        public Vector3D PlaneNormal;
        public bool IsValid;
        
        public CircularMotionParams(Vector3D center, double radius, double angularVel, Vector3D normal)
        {
            Center = center;
            Radius = radius;
            AngularVelocity = angularVel;
            PlaneNormal = normal;
            IsValid = true;
        }
        
        public static CircularMotionParams Invalid => new CircularMotionParams { IsValid = false };
    }
    
    public partial class TargetTracker : MyGridProgram
    {
        #region Fields and Constants

        public double circlingRadius = 0; // 目标当前环绕半径
        public double linearWeight, circularWeight;
        public double linearPositionError, circularPositionError, combinationError;
        public double maxTargetAcceleration = 0; // 最大目标加速度（单位：m/s²）
        // 目标历史记录最大长度
        private readonly int _maxHistory;
        // 目标历史记录，最新数据放在链表头部
        private readonly CircularQueue<SimpleTargetInfo> _history;
        private CircularMotionParams _circularMotionParams; // 圆周运动参数
        private int _updateCount = 0; // 更新计数器，用于记录历史记录的更新次数

        // 常量定义
        private const double TimeEpsilon = 1e-6; // 时间差最小值
        private const double LinearThreshold = 0.01; // 线性运动检测阈值 sin θ＝0.01 约等于 θ≈0.57°
        private const double RadiusThreshold = 1e6; // 半径过大阈值
        private const double MaxAccResetThreshold = 20.0; // 误差超过20m/s时重置最大加速度记录

        #endregion

        #region Constructors

        /// <summary>
        /// 默认构造函数
        /// </summary>
        public TargetTracker() : this(30) { }

        /// <summary>
        /// 构造函数，可自定义目标历史最大长度
        /// </summary>
        /// <param name="maxHistory">目标历史记录最大长度</param>
        public TargetTracker(int maxHistory)
        {
            _maxHistory = maxHistory;
            _history = new CircularQueue<SimpleTargetInfo>(_maxHistory);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 添加一条新的目标数据到历史记录中，超过最大长度时自动清除最旧数据
        /// </summary>
        public void UpdateTarget(SimpleTargetInfo target, bool hasVelocityAvailable = false)
        {
            _updateCount = (_updateCount + 1) % 2147483647;
            _history.AddFirst(target);
            EvaluatePredictionPerformance(hasVelocityAvailable);

            int historyLength = _history.Count;
            int index2 = historyLength / 2;
            var p0 = _history.First;
            var p1 = _history.GetItemAt(index2);
            var p2 = _history.Last;
            _circularMotionParams = CalculateCircularMotionParams(p0, p1, p2);

        }

        public void UpdateTarget(MyDetectedEntityInfo target, bool hasVelocityAvailable = false)
        {
            UpdateTarget(SimpleTargetInfo.FromDetectedInfo(target), hasVelocityAvailable);
        }

        public void UpdateTarget(Vector3D position, Vector3D velocity, long timeStamp, bool hasVelocityAvailable = false)
        {
            UpdateTarget(new SimpleTargetInfo(position, velocity, timeStamp), hasVelocityAvailable);
        }

        /// <summary>
        /// 清空目标历史记录
        /// </summary>
        public void ClearHistory()
        {
            maxTargetAcceleration = 0;
            _history.Clear();
        }
        /// <summary>
        /// 获取历史记录中指定索引的目标信息。
        /// 索引<0时返回最新；索引>=Count时返回最旧；无历史时返回null
        /// </summary>
        public SimpleTargetInfo? GetTargetInfoAt(int index)
        {
            if (_history.Count == 0)
                return null;
            if (index < 0)
                return _history.First;
            if (index >= _history.Count)
                return _history.Last;
            return _history.GetItemAt(index);
        }

        /// <summary>
        /// 返回当前历史记录中最新的目标数据（若存在）
        /// </summary>
        public SimpleTargetInfo? GetLatestTargetInfo()
        {
            return _history.Count > 0 ? _history.First : (SimpleTargetInfo?)null;
        }

        public Vector3D GetLatestPosition()
        {
            return _history.Count > 0 ? _history.First.Position : Vector3D.Zero;
        }

        public long GetLatestTimeStamp()
        {
            return _history.Count > 0 ? _history.First.TimeStamp : 0;
        }
        public int GetHistoryCount() => _history.Count;
        /// <summary>
        /// 预测未来目标信息（使用加权组合）
        /// </summary>
        /// <param name="futureTimeMs">预测时间间隔（毫秒）</param>
        /// <param name="hasVelocityAvailable">是否有实测速度信息</param>
        /// <returns>预测的目标信息</returns>
        public SimpleTargetInfo PredictFutureTargetInfo(long futureTimeMs, bool hasVelocityAvailable = false)
        {
            if (_history.Count == 0)
            {
                return new SimpleTargetInfo(Vector3D.Zero, Vector3D.Zero, 0);
            }

            if (_history.Count <= 2)
            {
                // 1-2个数据点，使用匀速运动预测
                return PredictUniformMotion(futureTimeMs);
            }

            // 获取历史数据点
            int historyLength = _history.Count;
            int index2 = historyLength / 2;
            var p0 = _history.First;
            var pm = _history.GetItemAt(index2);
            var pt = _history.Last;
            // var p1 = _history.GetItemAt(1);
            // var p2 = _history.GetItemAt(2);

            // 检查权重，避免不必要的计算
            bool useLinear = Math.Abs(linearWeight) > 1e-3;
            bool useCircular = Math.Abs(circularWeight) > 1e-3;

            SimpleTargetInfo linearPrediction, circularPrediction;

            // 只计算需要的预测
            if (useLinear && useCircular)
            {
                // 两种预测都需要，进行加权组合
                linearPrediction = PredictSecondOrder(futureTimeMs, hasVelocityAvailable, p0, pm, pt);
                circularPrediction = PredictCircularMotion(futureTimeMs, hasVelocityAvailable, p0, pm, pt);

                Vector3D combinedPosition = linearPrediction.Position * linearWeight + circularPrediction.Position * circularWeight;
                Vector3D combinedVelocity = linearPrediction.Velocity * linearWeight + circularPrediction.Velocity * circularWeight;

                return new SimpleTargetInfo(combinedPosition, combinedVelocity, p0.TimeStamp + futureTimeMs);
            }
            else if (useLinear)
            {
                // 只使用线性预测
                return PredictSecondOrder(futureTimeMs, hasVelocityAvailable, p0, pm, pt);
            }
            else if (useCircular)
            {
                // 只使用圆周预测
                return PredictCircularMotion(futureTimeMs, hasVelocityAvailable, p0, pm, pt);
            }
            else
            {
                // 两个权重都很小，使用线性预测作为默认
                return PredictSecondOrder(futureTimeMs, hasVelocityAvailable, p0, pm, pt);
            }
        }
        #endregion

        #region Prediction Models

        /// <summary>
        /// 匀速运动预测（适用于1-2个数据点）
        /// </summary>
        private SimpleTargetInfo PredictUniformMotion(long futureTimeMs)
        {
            var current = _history.First;
            double dt = futureTimeMs * 0.001;
            Vector3D predictedPos = current.Position + current.Velocity * dt;
            return new SimpleTargetInfo(predictedPos, current.Velocity, current.TimeStamp + futureTimeMs);
        }

        /// <summary>
        /// 二阶预测（基于三个或以上数据点）
        /// </summary>
        private SimpleTargetInfo PredictSecondOrder(long futureTimeMs, bool hasVelocityAvailable, SimpleTargetInfo p0, SimpleTargetInfo p1, SimpleTargetInfo p2)
        {
            // 计算时间间隔
            double dt1 = ClampTime((p0.TimeStamp - p1.TimeStamp) * 0.001);
            double dt2 = ClampTime((p1.TimeStamp - p2.TimeStamp) * 0.001);

            Vector3D currentPos = p0.Position;
            Vector3D currentVel, acceleration, jerk = Vector3D.Zero;
            bool useThirdOrder = false; // 是否使用三阶预测

            if (hasVelocityAvailable)
            {
                // 使用实测速度计算加速度
                currentVel = p0.Velocity;

                Vector3D acc1 = (p0.Velocity - p1.Velocity) / dt1;
                Vector3D acc2 = (p1.Velocity - p2.Velocity) / dt2;

                // 计算加速度变化率(jerk)
                jerk = (acc1 - acc2) / (dt1 + dt2) * 0.5;
                useThirdOrder = true;  // 有足够信息使用三阶预测

                // 取当前加速度
                acceleration = acc1;
            }
            else
            {
                // 通过位置计算速度和加速度
                Vector3D vel1 = (p0.Position - p1.Position) / dt1;
                Vector3D vel2 = (p1.Position - p2.Position) / dt2;

                currentVel = vel1;
                acceleration = (vel1 - vel2) / (dt1 + dt2) * 0.5;
            }

            // 预测计算
            double dt_predict = futureTimeMs * 0.001;
            Vector3D predictedPos, predictedVel;

            if (useThirdOrder)
            {
                // 三阶预测：p = p₀ + v₀·t + ½·a₀·t² + ⅙·j·t³
                predictedPos = currentPos +
                            currentVel * dt_predict +
                            0.5 * acceleration * dt_predict * dt_predict +
                            (1.0 / 6.0) * jerk * dt_predict * dt_predict * dt_predict;

                // 预测速度：v = v₀ + a₀·t + ½·j·t²
                predictedVel = currentVel +
                            acceleration * dt_predict +
                            0.5 * jerk * dt_predict * dt_predict;
            }
            else
            {
                // 原有的二阶预测
                predictedPos = currentPos + currentVel * dt_predict + 0.5 * acceleration * dt_predict * dt_predict;
                predictedVel = currentVel + acceleration * dt_predict;
            }
            
            // 根据情况重置/更新最大加速度记录
            if (combinationError >= MaxAccResetThreshold) maxTargetAcceleration = 0; 
            else maxTargetAcceleration = Math.Max(maxTargetAcceleration, acceleration.Length());

            return new SimpleTargetInfo(predictedPos, predictedVel, p0.TimeStamp + futureTimeMs);
        }

        /// <summary>
        /// 计算圆周运动参数
        /// </summary>
        /// <param name="p0">最新位置点</param>
        /// <param name="p1">第二新位置点</param>
        /// <param name="p2">第三新位置点</param>
        /// <returns>圆周运动参数</returns>
        private CircularMotionParams CalculateCircularMotionParams(SimpleTargetInfo p0, SimpleTargetInfo p1, SimpleTargetInfo p2)
        {
            // 检测是否为近似共线（直线运动）
            Vector3D a = p1.Position - p0.Position;
            Vector3D b = p2.Position - p0.Position;
            Vector3D cross = Vector3D.Cross(a, b);

            double a2 = a.LengthSquared();
            double b2 = b.LengthSquared();
            // cross² = |a|²·|b|²·sin²θ，归一化后判断 sin²θ < 阈值²
            if (cross.LengthSquared() < a2 * b2 * LinearThreshold * LinearThreshold)
            {
                return CircularMotionParams.Invalid; // 近似直线
            }

            // 计算外接圆参数
            try
            {
                double crossLenSq = cross.LengthSquared();
                double a_len_sq = a.LengthSquared();
                double b_len_sq = b.LengthSquared();

                Vector3D alpha = Vector3D.Cross(b, cross) * a_len_sq;
                Vector3D beta = Vector3D.Cross(cross, a) * b_len_sq;
                Vector3D center = p0.Position + (alpha + beta) / (2.0 * crossLenSq);

                double radius = (center - p0.Position).Length();
                circlingRadius = radius; // 更新当前环绕半径

                // 半径过大时视为无效圆周运动
                if (radius > RadiusThreshold)
                {
                    return CircularMotionParams.Invalid;
                }

                // 计算角速度
                Vector3D r1 = p0.Position - center;
                Vector3D r2 = p1.Position - center;

                double r1_len = r1.Length();
                double r2_len = r2.Length();

                if (r1_len < TimeEpsilon || r2_len < TimeEpsilon)
                {
                    return CircularMotionParams.Invalid;
                }

                double cosAngle = Math.Max(-1.0, Math.Min(1.0, Vector3D.Dot(r1, r2) / (r1_len * r2_len)));
                double angle = Math.Acos(cosAngle);
                double dt_sample = ClampTime((p0.TimeStamp - p1.TimeStamp) * 0.001);
                double angularVelocity = angle / dt_sample;

                // 计算运动平面法向量
                Vector3D planeNormal = Vector3D.Normalize(cross);

                return new CircularMotionParams(center, radius, angularVelocity, planeNormal);
            }
            catch
            {
                return CircularMotionParams.Invalid;
            }
        }

        /// <summary>
        /// 基于圆周运动模型预测未来目标信息
        /// </summary>
        /// <param name="futureTimeMs">预测时间间隔（毫秒）</param>
        /// <param name="hasVelocityAvailable">是否有实测速度信息</param>
        /// <param name="p0">最新位置点</param>
        /// <param name="p1">第二新位置点</param>
        /// <param name="p2">第三新位置点</param>
        /// <returns>基于圆周运动模型的预测结果</returns>
        private SimpleTargetInfo PredictCircularMotion(long futureTimeMs, bool hasVelocityAvailable, SimpleTargetInfo p0, SimpleTargetInfo p1, SimpleTargetInfo p2)
        {
            // 计算圆周运动参数
            var circularParams = _circularMotionParams;

            // 获取当前速度
            Vector3D currentVel = GetCurrentVelocity(p0, p1, hasVelocityAvailable);

            // 如果不是有效的圆周运动，退化为匀速预测
            if (!circularParams.IsValid) return PredictSecondOrder(futureTimeMs, hasVelocityAvailable, p0, p1, p2);

            // 预测未来位置
            double dt_predict = futureTimeMs * 0.001;
            double angleChange = circularParams.AngularVelocity * dt_predict;

            // 确定旋转方向
            Vector3D currentRadius = p0.Position - circularParams.Center;
            Vector3D expectedVelDirection = Vector3D.Cross(circularParams.PlaneNormal, currentRadius);

            if (Vector3D.Dot(currentVel, expectedVelDirection) < 0)
            {
                angleChange = -angleChange;
            }

            // 使用四元数旋转预测位置
            QuaternionD rotation = QuaternionD.CreateFromAxisAngle(circularParams.PlaneNormal, angleChange);
            Vector3D rotatedRadius = Vector3D.Transform(currentRadius, rotation);
            Vector3D predictedPos = circularParams.Center + rotatedRadius;

            // 预测速度（旋转当前速度向量）
            Vector3D predictedVel = Vector3D.Transform(currentVel, rotation);

            return new SimpleTargetInfo(predictedPos, predictedVel, p0.TimeStamp + futureTimeMs);
        }

        /// <summary>
        /// 衡量预测性能的方法-可学习参数
        /// </summary>
        /// <param name="hasVelocityAvailable">是否有实测速度信息</param>
        /// <returns>预测误差的模长，如果数据不足则返回-1</returns>
        private void EvaluatePredictionPerformance(bool hasVelocityAvailable = false)
        {
            // 历史记录检查（保持原有代码）
            if (_history.Count < 4)
            {
                linearPositionError = 1145140721.0;
                circularPositionError = 1145140721.0;
                combinationError = 1145140721.0;
                return;
            }

            // 获取历史数据点（保持原有代码）
            int historyLength = _history.Count;
            int index3_1 = historyLength / 3;
            int index3_2 = historyLength * 2 / 3;
            if (index3_1 == 0) index3_1 = 1;
            if (index3_2 <= index3_1) index3_2 = index3_1 + 1;

            // historyLength = historyLength / 4; // 取1/4长度，避免过长历史记录影响快速响应
            // int index1 = historyLength / 3;
            // int index2 = historyLength * 2 / 3;
            // if (index1 == 0) index1 = 1;
            // if (index2 <= index1) index2 = index1 + 1;

            var p0 = _history.First; // 最新记录
            // var p1 = _history.GetItemAt(1); // 第二新记录
            // var p2 = _history.GetItemAt(2); // 第三新记录
            // var p3 = _history.GetItemAt(3); // 最旧记录
            var p3_1 = _history.GetItemAt(index3_1); // 大约1/3处
            var p3_2 = _history.GetItemAt(index3_2); // 大约2/3处
            var pt = _history.Last; // 最旧记录

            // 计算预测时间
            long predictionTimeCicular = p0.TimeStamp - p3_1.TimeStamp;
            long predictionTimeLinear = p0.TimeStamp - p3_1.TimeStamp;

            // 进行预测
            SimpleTargetInfo predictedLinearTarget = PredictSecondOrder(predictionTimeLinear, hasVelocityAvailable, p3_1, p3_2, pt);
            _circularMotionParams = CalculateCircularMotionParams(p3_1, p3_2, pt);
            SimpleTargetInfo predictedCircularTarget = PredictCircularMotion(predictionTimeCicular, hasVelocityAvailable, p3_1, p3_2, pt);

            // 计算各自的误差
            linearPositionError = (predictedLinearTarget.Position - p0.Position).Length() / predictionTimeLinear * 1000;
            circularPositionError = (predictedCircularTarget.Position - p0.Position).Length() / predictionTimeCicular * 1000;

            // ----- 增量学习 -----

            // 学习参数
            double learningRate = 0.05;   // 控制每次更新的影响强度

            // 计算误差比率与目标权重
            double targetLinearWeight, targetCircularWeight;

            // 基于误差比例计算目标权重
            double errorSum = linearPositionError + circularPositionError;
            // 误差越小，权重越大（反比关系）
            targetLinearWeight = circularPositionError / errorSum;
            targetCircularWeight = linearPositionError / errorSum;

            // 使用学习率应用增量更新
            linearWeight = linearWeight * (1 - learningRate) + targetLinearWeight * learningRate;
            circularWeight = circularWeight * (1 - learningRate) + targetCircularWeight * learningRate;

            // 归一化确保权重和为1
            double weightSum = linearWeight + circularWeight;
            if (weightSum > 0)
            {
                linearWeight /= weightSum;
                circularWeight /= weightSum;
            }
            else
            {
                // 防御性编程
                linearWeight = 1;
                circularWeight = 0;
            }

            // 计算组合预测误差
            Vector3D combinedPosition = predictedLinearTarget.Position * linearWeight +
                                    predictedCircularTarget.Position * circularWeight;
            combinationError = (combinedPosition - p0.Position).Length();// / predictionTimeCicular * 1000;

            // 异常保护
            double minIndividualError = Math.Min(linearPositionError, circularPositionError);
            if (combinationError > minIndividualError)
            {
                if (linearPositionError <= circularPositionError)
                {
                    linearWeight = 1.0;
                    circularWeight = 0.0;
                    combinationError = linearPositionError;
                }
                else
                {
                    linearWeight = 0.0;
                    circularWeight = 1.0;
                    combinationError = circularPositionError;
                }
            }
        }
        #endregion

        #region Utilities
        /// <summary>
        /// 限制时间差最小值
        /// </summary>
        private double ClampTime(double dt)
        {
            return Math.Abs(dt) < TimeEpsilon ? TimeEpsilon : dt;
        }

        // Sigmoid激活函数
        private double Sigmoid(double x)
        {
            return 1.0 / (1.0 + Math.Exp(-x));
        }

        /// <summary>
        /// 获取当前速度
        /// </summary>
        /// <param name="p0">当前位置点</param>
        /// <param name="p1">前一个位置点</param>
        /// <param name="hasVelocityAvailable">是否有实测速度</param>
        /// <returns>当前速度</returns>
        private Vector3D GetCurrentVelocity(SimpleTargetInfo p0, SimpleTargetInfo p1, bool hasVelocityAvailable)
        {
            if (hasVelocityAvailable)
            {
                return p0.Velocity;
            }
            else
            {
                double dt = ClampTime((p0.TimeStamp - p1.TimeStamp) * 0.001);
                return (p0.Position - p1.Position) / dt;
            }
        }

        #endregion
    }
}