using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Components.Interfaces;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRageMath;
namespace IngameScript
{
    // 1. 定义兼容接口
    /// <summary>
    /// 控制器兼容接口，统一抽象真实控制器和BlockMotionTracker的常用功能。
    /// </summary>
    public interface IMyControllerCompat
    {
        /// <summary>
        /// 获取自然重力加速度（如行星引力）。
        /// </summary>
        /// <returns>自然重力加速度向量（m/s²）。无重力环境返回零向量。</returns>
        Vector3D GetNaturalGravity();

        /// <summary>
        /// 获取人工重力加速度（如重力发生器）。
        /// </summary>
        /// <returns>人工重力加速度向量（m/s²）。无人工重力返回零向量。</returns>
        Vector3D GetArtificialGravity();

        /// <summary>
        /// 获取总重力加速度（自然+人工）。
        /// </summary>
        /// <returns>总重力加速度向量（m/s²）。</returns>
        Vector3D GetTotalGravity();

        /// <summary>
        /// 获取飞船当前速度大小（m/s）。
        /// </summary>
        /// <returns>速度标量。</returns>
        double GetShipSpeed();

        /// <summary>
        /// 获取飞船当前线速度和角速度。
        /// </summary>
        /// <returns>MyShipVelocities结构体。</returns>
        MyShipVelocities GetShipVelocities();

        /// <summary>
        /// 估算飞船质量信息。
        /// </summary>
        /// <returns>MyShipMass结构体。</returns>
        MyShipMass CalculateShipMass();

        /// <summary>
        /// 获取飞船或方块的世界坐标。
        /// </summary>
        /// <returns>世界坐标（Vector3D）。</returns>
        Vector3D GetPosition();

        /// <summary>
        /// 控制器或方块的自定义名称。
        /// </summary>
        string CustomName { get; }

        /// <summary>
        /// 控制器或方块的世界矩阵（位置和朝向）。
        /// </summary>
        MatrixD WorldMatrix { get; }

        /// <summary>
        /// 控制器是否能够正常工作(耐久)
        /// </summary>        
        bool IsFunctional { get; }

        bool Closed { get; }

        /// <summary>
        /// 更新位置和速度（BlockMotionTracker需定期调用，真实控制器可为空实现）。
        /// </summary>
        void Update();
    }

    // 2. BlockMotionTracker 实现接口
    public class BlockMotionTracker : IMyControllerCompat
    {
        private IMyTerminalBlock block;
        private double updateIntervalSeconds;
        private MatrixD lastWorldMatrix;
        private Vector3D LinearVelocity;
        private Vector3D AngularVelocity;
        private Action<string> Echo;
        private MyShipMass _缓存质量 = new MyShipMass(-1, -1, -1);
        public string CustomName { get { return block.CustomName; } }
        public MatrixD WorldMatrix { get { return block.WorldMatrix; } }

        public BlockMotionTracker(IMyTerminalBlock block, double updateIntervalSeconds = 0.0166666667, Action<string> Echo = null)
        {
            this.block = block;
            this.updateIntervalSeconds = Math.Max(1e-6, updateIntervalSeconds);
            this.lastWorldMatrix = block.WorldMatrix;
            this.AngularVelocity = Vector3D.Zero;
            this.Echo = Echo ?? (msg => { }); // 默认空实现，避免空引用异常
        }

        /// <summary>
        /// 更新并计算角速度。
        /// </summary>
        public void Update()
        {
            MatrixD currentWorldMatrix = block.WorldMatrix;
            MatrixD deltaRotation = MatrixD.Multiply(
                currentWorldMatrix.GetOrientation(),
                MatrixD.Transpose(lastWorldMatrix.GetOrientation())
            );
            QuaternionD deltaQuat = QuaternionD.CreateFromRotationMatrix(deltaRotation);
            Vector3D axis;
            double angle;
            deltaQuat.GetAxisAngle(out axis, out angle);
            if (angle > Math.PI)
                angle = (angle + Math.PI) % (2 * Math.PI) - Math.PI;
            Vector3D localAngularVelocity = axis * (angle / updateIntervalSeconds);

            // 转换到世界坐标系
            Vector3D worldAngularVelocity = Vector3D.TransformNormal(localAngularVelocity, block.WorldMatrix);

            AngularVelocity = worldAngularVelocity;
            lastWorldMatrix = currentWorldMatrix;
        }

        public Vector3D GetNaturalGravity() { return Vector3D.Zero; }
        public Vector3D GetArtificialGravity() { return Vector3D.Zero; }
        public Vector3D GetTotalGravity() { return GetNaturalGravity() + GetArtificialGravity(); }
        public double GetShipSpeed() { return LinearVelocity.Length(); }
        public MyShipVelocities GetShipVelocities() { return new MyShipVelocities(block.CubeGrid.LinearVelocity, AngularVelocity); }
        public MyShipMass CalculateShipMass()
        {
            // 如果缓存有效，直接返回
            if (_缓存质量.PhysicalMass >= 0)
                return _缓存质量;
            IMyCubeGrid grid = block.CubeGrid;
            Vector3I min = grid.Min;
            Vector3I max = grid.Max;
            float totalMass = 0f;
            var visited = new HashSet<IMySlimBlock>();
            for (int x = min.X; x <= max.X; x++)
                for (int y = min.Y; y <= max.Y; y++)
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        Vector3I pos = new Vector3I(x, y, z);
                        IMySlimBlock slim = grid.GetCubeBlock(pos);
                        if (slim != null && visited.Add(slim))
                        {
                            totalMass += slim.Mass;
                        }
                        if (slim == null)
                        {
                            totalMass += 20f; // 假设每个空位置的质量为20kg,一个轻甲块的质量
                        }
                    }
            _缓存质量 = new MyShipMass(totalMass, totalMass, totalMass);
            return _缓存质量;
        }
        public Vector3D GetPosition() { return block.GetPosition(); }
        public bool IsFunctional { get { return block.IsFunctional; } }
        public bool Closed { get { return block.Closed; } }
    }

    // 3. 控制器适配器
    public class ShipControllerAdapter : IMyControllerCompat
    {
        private IMyShipController ctrl;
        public ShipControllerAdapter(IMyShipController ctrl) { this.ctrl = ctrl; }
        public Vector3D GetNaturalGravity() { return ctrl.GetNaturalGravity(); }
        public Vector3D GetArtificialGravity() { return ctrl.GetArtificialGravity(); }
        public Vector3D GetTotalGravity() { return ctrl.GetTotalGravity(); }
        public double GetShipSpeed() { return ctrl.GetShipSpeed(); }
        public MyShipVelocities GetShipVelocities() { return ctrl.GetShipVelocities(); }
        public MyShipMass CalculateShipMass() { return ctrl.CalculateShipMass(); }
        public Vector3D GetPosition() { return ctrl.GetPosition(); }
        public string CustomName { get { return ctrl.CustomName; } }
        public MatrixD WorldMatrix { get { return ctrl.WorldMatrix; } }
        public bool IsFunctional { get { return ctrl.IsFunctional; } }
        public bool Closed { get { return ctrl.Closed; } }
        public void Update()
        {
            // IMyShipController 不需要手动更新位置和速度
        }
    }
}