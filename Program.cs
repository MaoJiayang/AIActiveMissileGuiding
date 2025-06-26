using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        #region 枚举定义

        /// <summary>
        /// 导弹状态枚举
        /// </summary>
        private enum 导弹状态
        {
            搜索目标,    // 搜索目标
            跟踪目标,    // 跟踪目标
            预测制导     // 预测制导
        }

        #endregion

        #region 常量定义

        // 制导相关常量
        private const double 激进比例 = 10.0;                      // 大数值会让导弹更考虑接近而不是匹配目标速度
        private const double 导航常数 = 7.5;                     // 比例导航常数
        private const double 最小向量长度 = 1e-6;                 // 向量最小有效长度
        private const double 最小接近加速度 = 2.5;                // 最小接近加速度(m/s)
        private const double 时间常数 = 1f / 60f;                // 时间常数(秒)
        private const int 陀螺仪更新间隔 = 3;                     // 陀螺仪更新间隔(ticks)

        // 状态切换时间常量
        private const int 目标丢失超时帧数 = 180;                 // 目标丢失判定时间
        private const int 预测制导持续帧数 = 300;                 // 预测制导持续时间

        #endregion

        #region 状态变量

        private 导弹状态 当前状态 = 导弹状态.搜索目标;
        private Vector3D 上次目标位置 = Vector3D.Zero;
        private long 上次目标更新时间 = 0;
        private int 预测开始帧数 = 0;
        private int 更新计数器 = 0;

        #endregion

        #region 硬件组件

        private string 组名 = "导弹";
        
        // AI组件
        private IMyFlightMovementBlock 飞行块;
        private IMyOffensiveCombatBlock 战斗块;
        
        // 控制组件
        private IMyShipController 控制器;
        private List<IMyThrust> 推进器列表 = new List<IMyThrust>();
        private List<IMyGyro> 陀螺仪列表 = new List<IMyGyro>();
        
        // 推进器分组映射表
        private Dictionary<string, List<IMyThrust>> 推进器方向组 = new Dictionary<string, List<IMyThrust>>
        {
            {"XP", new List<IMyThrust>()}, {"XN", new List<IMyThrust>()},
            {"YP", new List<IMyThrust>()}, {"YN", new List<IMyThrust>()},
            {"ZP", new List<IMyThrust>()}, {"ZN", new List<IMyThrust>()}
        };

        private bool 推进器已分类 = false;

        #endregion

        #region PID控制系统

        // PID控制器 - 外环(角度误差->期望角速度)
        private PID 偏航外环PD = new PID(32, 0, 1.3, 时间常数 * 陀螺仪更新间隔);
        private PID 俯仰外环PD = new PID(32, 0, 1.3, 时间常数 * 陀螺仪更新间隔);
        private PID 横滚外环PD = new PID(32, 0, 1.3, 时间常数 * 陀螺仪更新间隔);

        // PID控制器 - 内环(角速度误差->陀螺仪设定)
        private PID 偏航内环PD = new PID(16, 0, 1.1, 时间常数 * 陀螺仪更新间隔);
        private PID 俯仰内环PD = new PID(16, 0, 1.1, 时间常数 * 陀螺仪更新间隔);
        private PID 横滚内环PD = new PID(16, 0, 1.1, 时间常数 * 陀螺仪更新间隔);

        #endregion

        #region 目标跟踪系统

        private TargetTracker 目标跟踪器 = new TargetTracker();

        #endregion

        #region 性能统计

        private StringBuilder 性能统计信息 = new StringBuilder();
        private double 总运行时间毫秒 = 0;
        private double 最大运行时间毫秒 = 0;
        private int 运行次数 = 0;

        #endregion

        #region 构造函数和主循环

        public Program()
        {
            // 设置更新频率为每tick执行
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            更新计数器 = (更新计数器 + 1) % int.MaxValue;
            
            // 初始化硬件（如果需要）
            if (!初始化硬件())
            {
                Echo("硬件初始化失败");
                return;
            }
            
            // 获取目标位置
            Vector3D 当前目标位置 = 从飞行块获取目标();
            
            // 更新目标状态
            更新目标状态(当前目标位置);
            
            // 根据当前状态执行相应逻辑
            switch (当前状态)
            {
                case 导弹状态.搜索目标:
                    处理搜索状态();
                    break;
                    
                case 导弹状态.跟踪目标:
                    处理跟踪状态(当前目标位置);
                    break;
                    
                case 导弹状态.预测制导:
                    处理预测状态();
                    break;
            }
            
            // 更新性能统计
            更新性能统计信息();
        }

        #endregion

        #region 状态机控制

        /// <summary>
        /// 更新目标状态和状态机转换
        /// </summary>
        private void 更新目标状态(Vector3D 当前目标位置)
        {
            bool 有有效目标 = !当前目标位置.Equals(Vector3D.NegativeInfinity);
            bool 目标位置已改变 = !当前目标位置.Equals(上次目标位置);

            switch (当前状态)
            {
                case 导弹状态.搜索目标:
                    if (有有效目标)
                    {
                        当前状态 = 导弹状态.跟踪目标;
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                        Echo("目标锁定 - 切换到跟踪模式");
                    }
                    break;

                case 导弹状态.跟踪目标:
                    if (!有有效目标 || !目标位置已改变)
                    {
                        // 检查目标是否丢失
                        if (更新计数器 - 上次目标更新时间 > 目标丢失超时帧数)
                        {
                            当前状态 = 导弹状态.预测制导;
                            预测开始帧数 = 更新计数器;
                            Echo("目标丢失 - 切换到预测模式");
                        }
                    }
                    else
                    {
                        // 目标位置更新
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                    }
                    break;

                case 导弹状态.预测制导:
                    if (有有效目标 && 目标位置已改变)
                    {
                        当前状态 = 导弹状态.跟踪目标;
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                        Echo("目标重新锁定 - 切换到跟踪模式");
                    }
                    else if (更新计数器 - 预测开始帧数 > 预测制导持续帧数)
                    {
                        当前状态 = 导弹状态.搜索目标;
                        Echo("预测时间结束 - 切换到搜索模式");
                    }
                    break;
            }
        }

        /// <summary>
        /// 处理搜索状态
        /// </summary>
        private void 处理搜索状态()
        {
            Echo("状态: 搜索目标中...");
            
            // 清空目标历史
            目标跟踪器.ClearHistory();
            
            // 停止所有推进器
            foreach (var 推进器 in 推进器列表)
            {
                推进器.ThrustOverride = 0f;
            }
            
            // 停止陀螺仪覆盖
            foreach (var 陀螺仪 in 陀螺仪列表)
            {
                陀螺仪.GyroOverride = false;
            }
        }

        /// <summary>
        /// 处理跟踪状态
        /// </summary>
        private void 处理跟踪状态(Vector3D 目标位置)
        {
            Echo("状态: 跟踪目标");
            Echo($"目标位置: {目标位置}");
            
            // 更新目标跟踪器
            if (控制器 != null)
            {
                Vector3D 导弹位置 = 控制器.GetPosition();
                Vector3D 导弹速度 = 控制器.GetShipVelocities().LinearVelocity;
                long 当前时间戳 = (long)Math.Round(更新计数器 * 时间常数 * 1000);
                // 添加到目标跟踪器
                目标跟踪器.UpdateTarget(目标位置, Vector3D.Zero, 当前时间戳);

                // 获取最新目标信息进行制导
                var 目标信息 = 目标跟踪器.PredictFutureTargetInfo(0);
                Vector3D 制导命令 = 比例导航制导(控制器, 目标信息);
                应用制导命令(制导命令, 控制器);
            }
        }

        /// <summary>
        /// 处理预测状态
        /// </summary>
        private void 处理预测状态()
        {
            Echo("状态: 预测制导");
            Echo($"预测剩余时间: {预测制导持续帧数 - (更新计数器 - 预测开始帧数)} ticks");

            if (控制器 != null && 目标跟踪器.GetHistoryCount() > 0)
            {
                // 使用预测位置进行制导
                long 预测时间毫秒 = (long)Math.Round((更新计数器 - 上次目标更新时间) * 时间常数 * 1000);
                var 预测目标 = 目标跟踪器.PredictFutureTargetInfo(预测时间毫秒);

                SimpleTargetInfo 目标信息 = new SimpleTargetInfo(预测目标.Position, 预测目标.Velocity, 预测目标.TimeStamp);
                Vector3D 制导命令 = 比例导航制导(控制器, 目标信息);
                应用制导命令(制导命令, 控制器);

                Echo($"预测位置: {预测目标.Position}");
            }
        }

        #endregion

        #region 硬件初始化

        /// <summary>
        /// 初始化硬件组件
        /// </summary>
        private bool 初始化硬件()
        {
            // 如果已经初始化且硬件完整，直接返回
            if (控制器 != null && 推进器列表.Count > 0 && 陀螺仪列表.Count > 0)
                return true;

            var 方块组 = GridTerminalSystem.GetBlockGroupWithName(组名);
            if (方块组 == null)
            {
                Echo($"未找到组名为{组名}的方块组");
                return false;
            }

            // 获取控制器
            List<IMyShipController> 控制器列表 = new List<IMyShipController>();
            方块组.GetBlocksOfType(控制器列表);
            if (控制器列表.Count > 0)
                控制器 = 控制器列表[0];

            // 获取推进器
            推进器列表.Clear();
            方块组.GetBlocksOfType(推进器列表);

            // 获取陀螺仪
            陀螺仪列表.Clear();
            方块组.GetBlocksOfType(陀螺仪列表);

            // 配置AI组件
            配置AI组件();

            bool 初始化完整 = 控制器 != null && 推进器列表.Count > 0 && 陀螺仪列表.Count > 0;

            if (初始化完整)
            {
                Echo($"硬件初始化完成: 控制器={控制器?.CustomName}, 推进器={推进器列表.Count}, 陀螺仪={陀螺仪列表.Count}");
                // 重置推进器分类标志，强制重新分类
                推进器已分类 = false;
            }
            else
            {
                Echo("硬件初始化不完整");
            }

            return 初始化完整;
        }

        /// <summary>
        /// 配置AI组件
        /// </summary>
        private void 配置AI组件()
        {
            var 方块组 = GridTerminalSystem.GetBlockGroupWithName(组名);
            if (方块组 == null)
            {
                Echo($"未找到组名为{组名}的方块组。");
                return;
            }

            // 获取单个飞行块和战斗块
            List<IMyFlightMovementBlock> 飞行块列表 = new List<IMyFlightMovementBlock>();
            List<IMyOffensiveCombatBlock> 战斗块列表 = new List<IMyOffensiveCombatBlock>();

            方块组.GetBlocksOfType(飞行块列表);
            方块组.GetBlocksOfType(战斗块列表);

            // 确保至少找到一个飞行块和战斗块
            if (飞行块列表.Count == 0)
            {
                Echo($"缺少飞行方块。");
                return;
            }

            // 只使用第一个找到的块
            飞行块 = 飞行块列表[0];
            if (战斗块列表.Count > 0)
                战斗块 = 战斗块列表[0];

            Echo("配置AI组件...");

            // 配置飞行AI
            if (飞行块 != null)
            {
                飞行块.SpeedLimit = 200f;       // 最大速度
                飞行块.AlignToPGravity = false; // 不与重力对齐
                飞行块.Enabled = false;         // 关闭方块
                Echo($"配置飞行块: {飞行块.CustomName}");
            }

            // 配置战斗AI
            if (战斗块 != null)
            {
                战斗块.TargetPriority = OffensiveCombatTargetPriority.Largest;
                战斗块.UpdateTargetInterval = 0;
                战斗块.Enabled = true;
                战斗块.SelectedAttackPattern = 3; // 拦截模式

                IMyAttackPatternComponent 攻击模式;
                if (战斗块.TryGetSelectedAttackPattern(out 攻击模式))
                {
                    IMyOffensiveCombatIntercept 拦截模式 = 攻击模式 as IMyOffensiveCombatIntercept;
                    if (拦截模式 != null)
                    {
                        拦截模式.GuidanceType = GuidanceType.Basic;
                    }
                }
                Echo($"配置战斗块: {战斗块.CustomName}");
            }
        }

        #endregion

        #region 目标获取

        /// <summary>
        /// 从单个飞行AI块获取目标坐标,并写入该块的CustomData中，返回目标位置
        /// </summary>
        private Vector3D 从飞行块获取目标()
        {
            // 检查飞行块是否可用
            if (飞行块 == null)
            {
                Echo("飞行块不可用");
                return Vector3D.NegativeInfinity;
            }

            List<IMyAutopilotWaypoint> 路径点列表 = new List<IMyAutopilotWaypoint>();
            飞行块.GetWaypoints(路径点列表);

            if (路径点列表.Count > 0)
            {
                // 只获取最后一个路径点作为目标
                var 路径点 = 路径点列表[路径点列表.Count - 1];

                // 从世界矩阵中直接提取位置坐标
                MatrixD 矩阵 = 路径点.Matrix;
                Vector3D 位置 = new Vector3D(矩阵.M41, 矩阵.M42, 矩阵.M43);

                // 在CustomData中只保存GPS坐标
                string GPS字符串 = $"GPS:{路径点.Name}目标:{位置.X:0.##}:{位置.Y:0.##}:{位置.Z:0.##}:#FF75C9F1:";
                飞行块.CustomData = GPS字符串;

                return 位置;
            }
            else
            {
                Echo("飞行块未找到任何路径点");
                return Vector3D.NegativeInfinity;
            }
        }

        #endregion

        #region 制导算法

        /// <summary>
        /// 应用制导命令，控制陀螺仪和推进器
        /// </summary>
        private void 应用制导命令(Vector3D 加速度命令, IMyShipController 控制器)
        {
            // 计算目标角度
            Vector3D 目标角度PYR = 计算目标角度(加速度命令, 控制器);

            // 控制陀螺仪
            应用陀螺仪控制(目标角度PYR);

            // 控制推进器
            控制推进器(加速度命令, 控制器);
        }

        /// <summary>
        /// 比例导航制导算法 - 结合接近速度控制
        /// </summary>
        /// <param name="控制器">飞船控制器</param>
        /// <param name="目标信息">目标信息</param>
        /// <returns>制导加速度命令(世界坐标系)</returns>
        private Vector3D 比例导航制导(IMyShipController 控制器, SimpleTargetInfo? 目标信息)
        {
            // ----- 步骤1: 获取基本状态信息 -----

            // 获取导弹状态
            Vector3D 导弹位置 = 控制器.GetPosition();
            Vector3D 导弹速度 = 控制器.GetShipVelocities().LinearVelocity;

            // 目标信息
            Vector3D 目标位置 = 目标信息.Value.Position;
            Vector3D 目标速度 = 目标信息.Value.Velocity;

            // 计算导弹到目标的视线向量
            Vector3D 导弹到目标 = 目标位置 - 导弹位置;
            double 距离 = 导弹到目标.Length();

            if (距离 < 最小向量长度)
                return Vector3D.Zero; // 防止除零错误

            Vector3D 视线单位向量 = 导弹到目标 / 距离;

            // ----- 步骤2: 估计目标加速度 (用于接近速度补偿部分) -----
            SimpleTargetInfo? 较新信息 = 目标跟踪器.GetTargetInfoAt(0);
            SimpleTargetInfo? 较旧信息 = 目标跟踪器.GetTargetInfoAt(1);
            SimpleTargetInfo? 最旧信息 = 目标跟踪器.GetTargetInfoAt(2);
            Vector3D 目标加速度 = 计算加速度(较新信息.Value, 较旧信息.Value, 最旧信息.Value);

            // ----- 步骤3: 计算相对速度和闭合速度 -----
            Vector3D 相对速度 = 目标速度 - 导弹速度;
            double 接近速度 = -Vector3D.Dot(相对速度, 视线单位向量);

            // ----- 步骤4: 计算视线角速率 -----
            // 视线角速度 = (导弹到目标 × 相对速度) / |导弹到目标|²
            Vector3D 角速度 = Vector3D.Cross(导弹到目标, 相对速度) /
                            Math.Max(导弹到目标.LengthSquared(), 1); // 防止除零

            // ----- 步骤5: 计算真比例导航基础控制量 -----
            // 计算基础比例导航加速度 (真PN核心公式)
            double 相对速度大小 = 相对速度.Length();
            Vector3D 比例导航加速度 = 导航常数 * 相对速度大小 * Vector3D.Cross(角速度, 视线单位向量);

            // // 可选: 添加目标加速度补偿(增强比例导航)
            // Vector3D 横向目标加速度 = 目标加速度 - Vector3D.Dot(目标加速度, 视线单位向量) * 视线单位向量;
            // 比例导航加速度 += 0.5 * 导航常数 * 横向目标加速度;

            // ----- 步骤6: 计算直接接近分量 -----
            if (导弹到目标.Length() > 最小向量长度)
            {
                // 使用无界接近速度计算
                Vector3D 接近加速度 = 计算无界接近加速度(相对速度, 导弹到目标);

                // 合并到接近速度修正中
                比例导航加速度 += 接近加速度;
            }

            // ----- 步骤7: 添加重力补偿 -----
            Vector3D 重力加速度 = 控制器.GetNaturalGravity();
            
            // ----- 步骤8: 合成最终加速度命令 -----
            // 最终命令 = 制导加速度 + 重力补偿
            Vector3D 最终加速度命令 = 比例导航加速度 - 重力加速度;
            
            // 输出诊断信息
            Echo($"[比例导航] 距离: {距离:n1} m");
            Echo($"[比例导航] 视线角速率: {角速度.Length():n6} rad/s");
            Echo($"[比例导航] 接近速度: {接近速度:n1} m/s");
            Echo($"[重力补偿] 重力: {重力加速度.Length():n1} m/s²");
            
            return 最终加速度命令;
        }

        /// <summary>
        /// 基于视线角速率的无界接近速度计算 - 优化版
        /// </summary>
        private Vector3D 计算无界接近加速度(Vector3D 相对速度, Vector3D 视线)
        {
            // 计算横向速率（垂直于视线的相对速度分量）
            Vector3D 视线单位向量 = Vector3D.Normalize(视线);
            Vector3D 横向速度 = 相对速度 - Vector3D.Dot(相对速度, 视线单位向量) * 视线单位向量;
            double 横向频率 = 横向速度.Length() / Math.Max(视线.Length(), 最小向量长度);

            // 基于双曲线映射计算接近加速度大小
            double 加速度大小 = 激进比例 / (横向频率 + 最小向量长度);
            加速度大小 = Math.Max(加速度大小, 最小接近加速度);

            // 关键改进：计算预测位置方向
            // 使用简单的线性预测：预测时间 = 当前距离 / 接近速度
            double 当前距离 = 视线.Length();
            double 接近速度 = -Vector3D.Dot(相对速度, 视线单位向量); // 负号因为相对速度是目标-导弹
            
            // 计算合理的预测时间
            double 预测时间 = 0;
            if (接近速度 > 1.0) // 确保有足够的接近速度
            {
                预测时间 = 当前距离 / 接近速度;
                // 限制预测时间，避免过度预测
                预测时间 = Math.Min(预测时间, 20.0); // 最多预测20秒
            }
            else
            {
                // 如果接近速度很小，使用固定的小预测时间
                预测时间 = 1.0;
            }
            
            // 计算目标预测位移（基于相对速度）
            // 注意：这里的相对速度是 目标速度 - 导弹速度
            // 所以目标的预测位移是 -相对速度 * 预测时间
            Vector3D 目标预测位移 = -相对速度 * 预测时间;
            
            // 计算指向预测位置的方向向量
            Vector3D 预测位置方向 = Vector3D.Normalize(视线 + 目标预测位移);
            
            // 应用接近加速度
            Vector3D 接近加速度 = 加速度大小 * 预测位置方向;
            
            // 输出诊断信息
            Echo($"[接近加速度] 大小: {加速度大小:n1} m/s²");
            // Echo($"[预测时间] {预测时间:n2} s");
            // Echo($"[接近速度] {接近速度:n1} m/s");

            return 接近加速度;
        }

        /// <summary>
        /// 只使用位置信息计算三点加速度
        /// </summary>
        private Vector3D 计算加速度(SimpleTargetInfo 最新, SimpleTargetInfo 中间, SimpleTargetInfo 最旧)
        {
            // 计算时间间隔（转换为秒）
            double t1 = 最旧.TimeStamp / 1000.0;
            double t2 = 中间.TimeStamp / 1000.0;
            double t3 = 最新.TimeStamp / 1000.0;
            
            // 检查时间间隔是否有效
            double dt12 = t2 - t1;
            double dt23 = t3 - t2;
            double dt13 = t3 - t1;
            if (dt12 <= 0 || dt23 <= 0 || dt13 <= 0) return Vector3D.Zero;

            // 获取位置向量
            Vector3D P1 = 最旧.Position;
            Vector3D P2 = 中间.Position;
            Vector3D P3 = 最新.Position;

            Vector3D 加速度;

            double 系数1 = 2.0 / (dt12 * dt13);
            double 系数2 = -2.0 / (dt12 * dt23);
            double 系数3 = 2.0 / (dt23 * dt13);
            
            加速度 = 系数1 * P1 + 系数2 * P2 + 系数3 * P3;
            return 加速度;
        }
        private Vector3D 计算速度(SimpleTargetInfo 最新, SimpleTargetInfo 最旧)
        {
            // 计算时间间隔（转换为秒）
            double t1 = 最旧.TimeStamp / 1000.0;
            double t2 = 最新.TimeStamp / 1000.0;

            // 检查时间间隔是否有效
            if (t2 <= t1) return Vector3D.Zero;

            // 获取位置向量
            Vector3D P1 = 最旧.Position;
            Vector3D P2 = 最新.Position;

            // 计算速度向量
            Vector3D 速度 = (P2 - P1) / (t2 - t1);
            return 速度;
        }
        /// <summary>
        /// 计算目标转向角度
        /// </summary>
        private Vector3D 计算目标角度(Vector3D 加速度命令, IMyShipController 控制器)
        {
            // 计算加速度方向作为期望的转向（世界坐标系）
            Vector3D 期望方向 = Vector3D.Normalize(加速度命令);

            // 获取导弹当前前向（世界坐标系）
            Vector3D 当前前向 = 控制器.WorldMatrix.Forward;

            // 计算目标与当前前向的夹角（误差，单位弧度）
            double 点积 = Vector3D.Dot(当前前向, 期望方向);
            点积 = Math.Max(-1, Math.Min(1, 点积)); // 限制范围防止数值误差
            double 角度误差 = Math.Acos(点积);

            // 计算旋转轴向（世界坐标系）
            Vector3D 旋转轴 = Vector3D.Cross(当前前向, 期望方向);
            if (旋转轴.LengthSquared() < 1e-8)
                旋转轴 = Vector3D.Zero;
            else
                旋转轴 = Vector3D.Normalize(旋转轴);

            // 得到目标角偏差向量（世界坐标系下），单位弧度
            Vector3D 目标角度PYR = 旋转轴 * 角度误差;
            Echo($"角度误差: {角度误差 * 180.0 / Math.PI:n1} 度");
            return 目标角度PYR;
        }

        #endregion

        #region 飞行控制系统

        /// <summary>
        /// 控制陀螺仪实现所需转向
        /// </summary>
        private void 应用陀螺仪控制(Vector3D 目标角度PYR)
        {
            // 仅在指定更新间隔执行，减少过度控制
            if (更新计数器 % 陀螺仪更新间隔 != 0)
                return;

            // ----------------- 外环：角度误差 → 期望角速度 (世界坐标系) -----------------
            // 使用PD控制器将角度误差转换为期望角速度
            double 期望俯仰速率 = 俯仰外环PD.GetOutput(目标角度PYR.X);
            double 期望偏航速率 = 偏航外环PD.GetOutput(目标角度PYR.Y);
            double 期望横滚速率 = 横滚外环PD.GetOutput(目标角度PYR.Z);

            // ----------------- 内环：角速度误差 → 最终指令 (世界坐标系) -----------------
            // 获取飞船当前角速度（单位：弧度/秒），已在世界坐标系下
            Vector3D 当前角速度 = 控制器.GetShipVelocities().AngularVelocity;

            // 计算各轴角速度误差
            double 俯仰速率误差 = 期望俯仰速率 - 当前角速度.X;
            double 偏航速率误差 = 期望偏航速率 - 当前角速度.Y;
            double 横滚速率误差 = 期望横滚速率 - 当前角速度.Z;

            // 内环PD：将角速度误差转换为最终下发指令
            double 最终俯仰命令 = 俯仰内环PD.GetOutput(俯仰速率误差);
            double 最终偏航命令 = 偏航内环PD.GetOutput(偏航速率误差);
            double 最终横滚命令 = 横滚内环PD.GetOutput(横滚速率误差);

            // 构造最终期望角速度（单位：弧度/秒），仍处于世界坐标系
            Vector3D 最终角速度命令 = new Vector3D(最终俯仰命令, 最终偏航命令, 最终横滚命令);

            // ----------------- 应用到各陀螺仪 -----------------
            foreach (var 陀螺仪 in 陀螺仪列表)
            {
                // 使用陀螺仪世界矩阵将世界坐标的角速度转换为陀螺仪局部坐标系
                Vector3D 陀螺仪本地命令 = Vector3D.TransformNormal(最终角速度命令, MatrixD.Transpose(陀螺仪.WorldMatrix));

                // 注意陀螺仪的轴向定义与游戏世界坐标系的差异，需要取负
                陀螺仪.Pitch = -(float)陀螺仪本地命令.X;
                陀螺仪.Yaw = -(float)陀螺仪本地命令.Y;
                陀螺仪.Roll = -(float)陀螺仪本地命令.Z;
                陀螺仪.GyroOverride = true;
            }
        }

        /// <summary>
        /// 控制推进器产生所需加速度
        /// </summary>
        private void 控制推进器(Vector3D 绝对加速度, IMyShipController 控制器)
        {
            // 获取飞船质量（单位：kg）
            double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;

            // 将绝对加速度转换为飞船本地坐标系（单位：m/s²）
            Vector3D 本地加速度 = Vector3D.TransformNormal(绝对加速度, MatrixD.Transpose(控制器.WorldMatrix));

            // 仅在第一次调用或推进器列表发生变化时进行分类
            if (!推进器已分类)
            {
                分类推进器(控制器);
            }

            // 将所有推进器的推力覆盖设置为0（清除旧指令）
            foreach (var 推进器 in 推进器列表)
            {
                推进器.ThrustOverridePercentage = 0f;
            }

            // 针对每个轴应用推力控制
            应用轴向推力(本地加速度.X, "XP", "XN", 飞船质量);
            应用轴向推力(本地加速度.Y, "YP", "YN", 飞船质量);
            应用轴向推力(本地加速度.Z, "ZP", "ZN", 飞船质量);
        }

        /// <summary>
        /// 将推进器按方向分类
        /// </summary>
        private void 分类推进器(IMyShipController 控制器)
        {
            // 清空所有组中的推进器
            foreach (var key in 推进器方向组.Keys.ToList())
            {
                推进器方向组[key].Clear();
            }

            // 遍历所有推进器，根据其局部推进方向归类
            const double 方向容差 = 0.8; // 方向余弦相似度阈值
            foreach (var 推进器 in 推进器列表)
            {
                // 将推进器的世界方向转换到飞船本地坐标系
                Vector3D 本地推力方向 = Vector3D.TransformNormal(推进器.WorldMatrix.Forward, MatrixD.Transpose(控制器.WorldMatrix));

                // 根据推进方向分类
                if (Vector3D.Dot(本地推力方向, -Vector3D.UnitX) > 方向容差)
                    推进器方向组["XP"].Add(推进器);
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitX) > 方向容差)
                    推进器方向组["XN"].Add(推进器);
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitY) > 方向容差)
                    推进器方向组["YP"].Add(推进器);
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitY) > 方向容差)
                    推进器方向组["YN"].Add(推进器);
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitZ) > 方向容差)
                    推进器方向组["ZP"].Add(推进器);
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitZ) > 方向容差)
                    推进器方向组["ZN"].Add(推进器);
                // 对于不整轴对齐的推进器，当前暂不作处理
            }
            
            // 对每个方向组内的推进器按最大有效推力从大到小排序
            foreach (var 组 in 推进器方向组.Values)
            {
                组.Sort((a, b) => b.MaxEffectiveThrust.CompareTo(a.MaxEffectiveThrust));
            }

            推进器已分类 = true;
        }

        /// <summary>
        /// 为指定轴应用推力控制
        /// </summary>
        private void 应用轴向推力(double 本地加速度, string 正方向组, string 负方向组, double 质量)
        {
            // 计算所需力（牛顿）
            double 需要的力 = 质量 * Math.Abs(本地加速度);

            // 确定应该使用哪个方向的推进器组
            string 激活组名 = 本地加速度 >= 0 ? 正方向组 : 负方向组;

            // 获取对应方向的推进器组（已排序）
            List<IMyThrust> 推进器组;
            if (!推进器方向组.TryGetValue(激活组名, out 推进器组) || 推进器组.Count == 0)
            {
                return; // 该方向没有推进器
            }

            // 推进器已在分类时排序，无需重新排序
            // 追踪剩余需要分配的推力和已分配的推力
            double 剩余力 = 需要的力;
            double 总分配力 = 0;

            // 将推进器按照相似推力分组处理
            int 当前索引 = 0;
            while (当前索引 < 推进器组.Count && 剩余力 > 0.001)
            {
                // 当前组的参考推力值
                double 参考推力 = 推进器组[当前索引].MaxEffectiveThrust;

                // 查找相似推力的推进器并计算组总推力
                int 组大小 = 0;
                double 组最大推力 = 0;
                int i = 当前索引;

                // 识别具有相似最大推力的推进器组
                while (i < 推进器组.Count &&
                    Math.Abs(推进器组[i].MaxEffectiveThrust - 参考推力) < 0.001)
                {
                    组最大推力 += 推进器组[i].MaxEffectiveThrust;
                    组大小++;
                    i++;
                }

                // 确定分配给该组的推力
                double 分配给组的推力 = Math.Min(组最大推力, 剩余力);

                // 在组内均匀分配推力
                double 每个推进器推力 = 分配给组的推力 / 组大小;
                for (int j = 当前索引; j < 当前索引 + 组大小; j++)
                {
                    double 实际分配推力 = Math.Min(每个推进器推力, 推进器组[j].MaxEffectiveThrust);
                    推进器组[j].ThrustOverride = (float)实际分配推力;
                    剩余力 -= 实际分配推力;
                    总分配力 += 实际分配推力;
                }

                // 移动到下一组推进器
                当前索引 = i;
            }

            // 输出调试信息
            if (需要的力 > 总分配力 + 0.001)
            {
                Echo($"轴向 {激活组名}推力缺口");
            }

            // 关闭剩余的推进器
            for (int i = 当前索引; i < 推进器组.Count; i++)
            {
                推进器组[i].ThrustOverride = 0f;
            }
        }

        #endregion

        #region 性能统计

        /// <summary>
        /// 更新运行性能统计信息
        /// </summary>
        private void 更新性能统计信息()
        {
            // 更新运行时间统计
            double 上次运行时间毫秒 = Runtime.LastRunTimeMs;
            
            总运行时间毫秒 += 上次运行时间毫秒;
            运行次数 = (运行次数 + 1) % int.MaxValue;
            if (运行次数 % 600 == 0)
            {
                // 每600次运行重置统计信息
                总运行时间毫秒 = 0;
                运行次数 = 0;
                最大运行时间毫秒 = 0;
            }
                最大运行时间毫秒 = 上次运行时间毫秒; // 初始化最大运行时间
            if (上次运行时间毫秒 > 最大运行时间毫秒)
                最大运行时间毫秒 = 上次运行时间毫秒;
            
            // 计算平均运行时间
            double 平均运行时间毫秒 = 总运行时间毫秒 / 运行次数;
            
            // 清空并重新构建性能统计信息
            性能统计信息.Clear();
            性能统计信息.AppendLine("=== 性能统计 ===");
            性能统计信息.AppendLine($"上次运行: {上次运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"平均运行: {平均运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"最大运行: {最大运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"运行次数: {运行次数}");
            性能统计信息.AppendLine($"指令使用: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount}");
            性能统计信息.AppendLine("================");
            
            // 在Echo输出之前显示性能统计
            Echo(性能统计信息.ToString());
        }

        #endregion
    }
}