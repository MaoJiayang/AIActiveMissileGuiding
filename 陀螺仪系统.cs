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
using IngameScript;

namespace IngameScript
{
    /// <summary>
    /// 一个通过超控飞船陀螺仪进行轴炮辅助瞄准的系统
    /// </summary>
    class 陀螺仪瞄准系统
    {
        private 参数管理器 参数们;
        private List<IMyGyro> 陀螺仪列表 = new List<IMyGyro>();
        public IMyControllerCompat 参考驾驶舱 { get; private set; }
        private IMyProgrammableBlock Me;
        private Dictionary<IMyGyro, Vector3D> 陀螺仪各轴点积 = new Dictionary<IMyGyro, Vector3D>();
        public bool 已初始化
        {
            get { return 陀螺仪列表.Count > 0 && 参考驾驶舱 != null; }
        }
        public string 初始化消息
        {
            get { return $"SAS状态：参考驾驶舱: {参考驾驶舱?.CustomName}，陀螺仪数量: {陀螺仪列表.Count}"; }
        }
        private long 内部时钟;
        private bool 角度误差在容忍范围内 = false;
        private double 陀螺仪最高转速
        {
            get { return 参考驾驶舱.CubeGrid.GridSizeEnum == MyCubeSize.Large ? Math.PI : 2 * Math.PI; }
        }
        #region PID控制系统

        // PID控制器 - 外环(角度误差->期望角速度)
        private PID3 外环PID控制器PYR = null;

        // PID控制器 - 内环(角速度误差->陀螺仪设定)
        private PID3 内环PID控制器PYR = null;

        #endregion
        
        #region 构造与初始化
        public 陀螺仪瞄准系统(参数管理器 参数管理器, IMyProgrammableBlock Me)
        {
            参数们 = 参数管理器;
            this.Me = Me;
        }
        public void 初始化(IMyBlockGroup 方块组, IMyControllerCompat 参考驾驶舱)
        {
            方块组.GetBlocksOfType(陀螺仪列表);
            this.参考驾驶舱 = 参考驾驶舱;
            初始化PID控制器();
            内部时钟 = -1; // 初始化时钟为-1，第一次调用Update时会自动更新
            重置();          
        }
        private void 初始化PID控制器()
        {
            double pid时间常数 = 参数们.获取PID时间常数();

            // 初始化外环PID控制器
            外环PID控制器PYR = new PID3(参数们.外环参数.P系数, 参数们.外环参数.I系数, 参数们.外环参数.D系数, pid时间常数);

            // 初始化内环PID控制器
            内环PID控制器PYR = new PID3(参数们.内环参数.P系数, 参数们.内环参数.I系数, 参数们.内环参数.D系数, pid时间常数);
        }
        #endregion
        #region 公共方法
        /// <summary>
        /// 工作期间每帧调用，有且仅有一次
        /// 方向瞄准和点瞄准是互斥的，每帧只能且必须调用一个
        /// </summary>
        public void 方向瞄准(Vector3D 方向)
        {
            瞄准(方向);
        }
        /// <summary>
        /// 工作期间每帧调用，有且仅有一次
        /// 方向瞄准和点瞄准是互斥的，每帧只能且必须调用一个
        /// </summary>
        public void 点瞄准(Vector3D 目标点)
        {
            瞄准(目标点 - 参考驾驶舱.GetPosition());
        }
        public void 重置()
        {
            if(内部时钟 == 0) return; // 如果已经重置过则不再执行
            // 重置所有PID控制器
            外环PID控制器PYR.Reset();
            内环PID控制器PYR.Reset();
            内部时钟 = 0;
            角度误差在容忍范围内 = false;
            // 停止所有陀螺仪
            foreach (var 陀螺仪 in 陀螺仪列表)
            {
                陀螺仪.GyroOverride = false;
                陀螺仪.Pitch = 0;
                陀螺仪.Yaw = 0;
                陀螺仪.Roll = 0;
            }
        }
        #endregion

        private void 瞄准(Vector3D 方向)
        {
            if (!已初始化) return;
            if (内部时钟 == 0)
            {
                陀螺仪列表.ForEach(g => { g.Enabled = true; g.GyroOverride = true; }); // 确保陀螺仪开启
            }
            内部时钟++;
            // 计算陀螺仪目标角度
            Vector3D 目标角度PYR = 计算陀螺仪目标角度(方向, 参考驾驶舱);
            应用陀螺仪控制(目标角度PYR, 参考驾驶舱);
        }

        #region 方向计算
        /// <summary>
        /// 计算陀螺仪的目标转向角度，使其指向加速度命令
        /// </summary>
        private Vector3D 计算陀螺仪目标角度(Vector3D 方向, IMyControllerCompat 控制器)
        {
            // 计算加速度方向作为期望的转向（世界坐标系）
            Vector3D 期望方向 = Vector3D.Normalize(方向);

            // 获取飞船当前前向（世界坐标系）
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
            // Echo($"[陀螺仪] 角度误差: {角度误差 * 180.0 / Math.PI:n1} 度");
            return 目标角度PYR;
        }
        #endregion

        #region 陀螺仪驱动
        /// <summary>
        /// 控制陀螺仪实现所需转向
        /// </summary>
        private void 应用陀螺仪控制(Vector3D 目标角度PYR, IMyControllerCompat 控制器)
        {
            // 检查角度误差是否在阈值范围内
            double 角度误差大小 = 目标角度PYR.Length();
            double 滚转输入 = 控制器.RollIndicator > 0 ? 陀螺仪最高转速 :
                              控制器.RollIndicator < 0 ? -陀螺仪最高转速 : 0.0;
            double 俯仰输入 = 控制器.RotationIndicator.X > 0.9 ? -陀螺仪最高转速 :
                              控制器.RotationIndicator.X < -0.9 ? 陀螺仪最高转速 : 0.0;
            double 偏航输入 = 控制器.RotationIndicator.Y > 0.9 ? -陀螺仪最高转速 :
                              控制器.RotationIndicator.Y < -0.9 ? 陀螺仪最高转速 : 0.0;
            Vector3D 手动输入 = new Vector3D(俯仰输入, 偏航输入, 0);
            if (手动输入 != Vector3D.Zero) 手动输入 = Vector3D.TransformNormal(手动输入, 控制器.WorldMatrix); // 转换到世界坐标系

            if (角度误差大小 < 参数们.角度误差最小值 && !角度误差在容忍范围内)
            {
                // 角度误差很小，停止陀螺仪以减少抖动
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    Vector3D 陀螺仪本地转速命令 = Vector3D.TransformNormal(手动输入, MatrixD.Transpose(陀螺仪.WorldMatrix));
                    陀螺仪本地转速命令 = 加入本地滚转(陀螺仪, 陀螺仪本地转速命令, 控制器, 参数们.常驻滚转转速 + 滚转输入);
                    施加本地转速指令(陀螺仪, 陀螺仪本地转速命令);
                }
                // 重置所有PID控制器
                外环PID控制器PYR.Reset();
                内环PID控制器PYR.Reset();
                角度误差在容忍范围内 = true;
                return;
            }
            角度误差在容忍范围内 = false;
            // 仅在指定更新间隔执行，减少过度控制
            if (内部时钟 % 参数们.动力系统更新间隔 != 0)
                return;
            // ----------------- 外环：角度误差 → 期望角速度 (世界坐标系) -----------------
            // 使用PD控制器将角度误差转换为期望角速度
            Vector3D 期望角速度PYR = 外环PID控制器PYR.GetOutput(目标角度PYR);
            // ----------------- 内环：角速度误差 → 最终指令 (世界坐标系) -----------------
            // 获取飞船当前角速度（单位：弧度/秒），已在世界坐标系下
            Vector3D 当前角速度 = 控制器.GetShipVelocities().AngularVelocity;
            // 计算各轴角速度误差
            Vector3D 速率误差PYR = 期望角速度PYR - 当前角速度;
            // 内环PD：将角速度误差转换为最终下发指令
            Vector3D 最终旋转命令PYR = 内环PID控制器PYR.GetOutput(速率误差PYR);
            // ----------------- 应用到各陀螺仪 -----------------
            foreach (var 陀螺仪 in 陀螺仪列表)
            {
                // 使用陀螺仪世界矩阵将世界坐标的角速度转换为陀螺仪局部坐标系
                最终旋转命令PYR += 手动输入;
                Vector3D 陀螺仪本地转速命令 = Vector3D.TransformNormal(最终旋转命令PYR, MatrixD.Transpose(陀螺仪.WorldMatrix));
                陀螺仪本地转速命令 = 加入本地滚转(陀螺仪, 陀螺仪本地转速命令, 控制器, 参数们.常驻滚转转速 + 滚转输入);
                施加本地转速指令(陀螺仪, 陀螺仪本地转速命令);
            }
        }

        /// <summary>
        /// 将本地指令实际应用到陀螺仪，带懒惰更新
        /// 仅在指令有变化时更新陀螺仪的转速
        /// </summary>
        private void 施加本地转速指令(IMyGyro 陀螺仪, Vector3D 本地指令)
        {
            陀螺仪.GyroOverride = true;
            // 注意陀螺仪的轴向定义与游戏世界坐标系的差异，需要取负
            if (陀螺仪命令需更新(陀螺仪.Pitch, -(float)本地指令.X)) 陀螺仪.Pitch = -(float)本地指令.X;
            if (陀螺仪命令需更新(陀螺仪.Yaw, -(float)本地指令.Y)) 陀螺仪.Yaw = -(float)本地指令.Y;
            if (陀螺仪命令需更新(陀螺仪.Roll, -(float)本地指令.Z)) 陀螺仪.Roll = -(float)本地指令.Z;
        }

        /// <summary>
        /// 找出滚转轴，并返回陀螺仪本地命令加上正确的滚转向量
        /// </summary>
        /// <param name="陀螺仪">陀螺仪块</param>
        /// <param name="陀螺仪本地命令">陀螺仪的本地命令向量</param>
        /// <param name="弧度每秒">滚转速度（弧度/秒）包含方向</param>
        /// <returns>应施加的本地命令向量 包含滚转轴的命令</returns>
        private Vector3D 加入本地滚转(IMyGyro 陀螺仪, Vector3D 陀螺仪本地命令, IMyControllerCompat 控制器, double 弧度每秒 = 0.0)
        {
            Vector3D 该陀螺仪点积 = 陀螺仪对轴(陀螺仪, 控制器);
            // if (弧度每秒 < 1e-6)
            // {
            //     return 陀螺仪本地命令; // 不需要滚转
            // }
            // 检查各轴与导弹Z轴的点积，判断是否同向
            double X轴点积 = Math.Abs(该陀螺仪点积.X);
            double Y轴点积 = Math.Abs(该陀螺仪点积.Y);
            double Z轴点积 = Math.Abs(该陀螺仪点积.Z);

            if (X轴点积 > 0.9 && X轴点积 >= Y轴点积 && X轴点积 >= Z轴点积)
            {
                陀螺仪本地命令.X = Math.Sign(该陀螺仪点积.X) * 弧度每秒;
            }
            else if (Y轴点积 > 0.9 && Y轴点积 >= X轴点积 && Y轴点积 >= Z轴点积)
            {
                陀螺仪本地命令.Y = Math.Sign(该陀螺仪点积.Y) * 弧度每秒;
            }
            else if (Z轴点积 > 0.9 && Z轴点积 >= X轴点积 && Z轴点积 >= Y轴点积)
            {
                陀螺仪本地命令.Z = -Math.Sign(该陀螺仪点积.Z) * 弧度每秒;
                // 备注：已知se旋转绕负轴，所以指令传入的时候已经全部取负
                // 又因为，该方法一般都用于直接覆盖已经计算好的陀螺仪本地命令，
                // 所以这里需要根据转速再取一次负
            }
            return 陀螺仪本地命令;
        }

        /// <summary>
        /// 计算陀螺仪各轴与导弹Z轴的点积并缓存
        /// 如果有缓存则直接取出
        /// 目的：找出滚转轴
        /// </summary>
        private Vector3D 陀螺仪对轴(IMyGyro 陀螺仪, IMyControllerCompat 控制器)
        {
            // 获取导弹Z轴方向（控制器的前进方向）
            Vector3D 导弹Z轴方向 = 控制器.WorldMatrix.Forward;

            Vector3D 该陀螺仪点积;
            if (!陀螺仪各轴点积.TryGetValue(陀螺仪, out 该陀螺仪点积))
            {
                // 获取陀螺仪的三个本地轴在世界坐标系中的方向
                Vector3D 陀螺仪X轴世界方向 = 陀螺仪.WorldMatrix.Right;    // 对应本地X轴（Pitch）
                Vector3D 陀螺仪Y轴世界方向 = 陀螺仪.WorldMatrix.Up;       // 对应本地Y轴（Yaw）
                Vector3D 陀螺仪Z轴世界方向 = 陀螺仪.WorldMatrix.Forward;   // 对应本地Z轴（Roll）
                该陀螺仪点积 = new Vector3D(
                    Vector3D.Dot(陀螺仪X轴世界方向, 导弹Z轴方向),
                    Vector3D.Dot(陀螺仪Y轴世界方向, 导弹Z轴方向),
                    Vector3D.Dot(陀螺仪Z轴世界方向, 导弹Z轴方向)
                );
                陀螺仪各轴点积[陀螺仪] = 该陀螺仪点积;
            }
            return 该陀螺仪点积;
        }

        /// <summary>
        /// 判断是否需要更新陀螺仪命令
        /// 如果当前值已经接近最大值，且新命令在同方向且更大，则不更新
        /// 如果差异很小，也不更新
        /// 目的：减少陀螺仪频繁更新导致出力不足
        /// </summary>
        private bool 陀螺仪命令需更新(double 当前值, double 新值, double 容差 = 1e-3)
        {

            if (Math.Abs(当前值) > 陀螺仪最高转速 - 容差)
            {
                // 当前值接近最大值
                if (Math.Sign(当前值) == Math.Sign(新值 + 1e-6) && Math.Abs(新值) >= Math.Abs(当前值))
                {
                    return false; // 不更新
                }
            }

            // 如果差异很小，也不更新
            if (Math.Abs(当前值 - 新值) < 容差)
            {
                return false;
            }
            return true;
        }

        #endregion

    }   
}