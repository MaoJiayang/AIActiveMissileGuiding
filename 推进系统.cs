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
using Sandbox.Game.GUI.DebugInputComponents;

namespace IngameScript
{
    class 推进系统
    {
        private 参数管理器 参数们;
        private List<IMyThrust> 推进器列表 = new List<IMyThrust>();
        // 分离推进器（可选）
        private List<IMyThrust> 分离推进器列表 = new List<IMyThrust>();

        // 推进器分组映射表
        private Dictionary<string, List<IMyThrust>> 推进器推力方向组 = new Dictionary<string, List<IMyThrust>>
        {
            {"XP", new List<IMyThrust>()}, {"XN", new List<IMyThrust>()},
            {"YP", new List<IMyThrust>()}, {"YN", new List<IMyThrust>()},
            {"ZP", new List<IMyThrust>()}, {"ZN", new List<IMyThrust>()}
        };
        public Dictionary<string, double> 轴向最大推力 { get; private set; } = new Dictionary<string, double>
        {
            {"XP", 0}, {"XN", 0}, {"YP", 0}, {"YN", 0}, {"ZP", 0}, {"ZN", 0}
        };
        private IMyProgrammableBlock Me;
        private bool 推进器已分类 = false;
        private long 内部时钟 = 0;
        public IMyControllerCompat 参考驾驶舱 { get; private set; }
        public bool 已初始化
        {
            get { return 推进器列表.Count > 0 && 参考驾驶舱 != null; }
        }
        public string 初始化消息
        {
            get { return $"推进器状态：参考驾驶舱: {参考驾驶舱?.CustomName}，推进器数量: {推进器列表.Count}"; }
        }
        #region 构造与初始化
        public 推进系统(参数管理器 参数管理器, IMyProgrammableBlock Me)
        {
            参数们 = 参数管理器;
            this.Me = Me;
        }
        public void 初始化(IMyBlockGroup 方块组, IMyControllerCompat 控制器)
        {
            // 获取参考驾驶舱
            参考驾驶舱 = 控制器;
            // 获取推进器列表
            方块组.GetBlocksOfType<IMyThrust>(推进器列表);
            // 可选：获取分离推进器
            初始化分离推进器();
            分类推进器(控制器);
        }
        public void 初始化(List<IMyThrust> 推进器列表, IMyControllerCompat 控制器)
        {
            // 获取参考驾驶舱
            参考驾驶舱 = 控制器;
            // 获取推进器列表
            this.推进器列表 = 推进器列表;
            // 可选：获取分离推进器
            初始化分离推进器();
            分类推进器(控制器);
        }
        /// <summary>
        /// 初始化分离推进器（可选功能，通过名称识别）
        /// </summary>
        private void 初始化分离推进器()
        {
            // 清空分离推进器列表
            分离推进器列表.Clear();

            // 根据名称识别分离推进器
            foreach (var 推进器 in 推进器列表)
            {
                string 名称 = 推进器.CustomName.ToLower();
                if (名称.Contains(参数们.分离推进器名称))
                {
                    分离推进器列表.Add(推进器);
                }
            }
            if (分离推进器列表.Count == 0)
            {
                分离推进器列表 = 推进器推力方向组["ZN"]; // 默认使用ZN推力方向的推进器
            }
        }
        #endregion

        #region 公共方法
        public void 分离推进()
        {
            if (内部时钟 != 0) return;
            // 只启用分离推进器，其他推进器保持关闭
            foreach (var 推进器 in 推进器列表)
            {
                推进器.Enabled = false;
                推进器.ThrustOverride = 0f;
            }
            foreach (var 分离推进器 in 分离推进器列表)
            {
                分离推进器.Enabled = true;
                分离推进器.ThrustOverride = 分离推进器.MaxThrust; // 全力推进
            }
            内部时钟++;
        }
        public void 开机()
        {
            // 启用所有推进器
            foreach (var 推进器 in 推进器列表)
            {
                推进器.Enabled = true;
                推进器.ThrustOverride = 0f; // 重置推力覆盖
            }
        }
        public void 关机()
        {
            if (内部时钟 == 0) return;
            // 停用所有推进器
            foreach (var 推进器 in 推进器列表)
            {
                推进器.Enabled = false;
                推进器.ThrustOverride = 0f; // 重置推力覆盖
            }
            内部时钟 = 0;
        }

        /// <summary>
        /// 更新方法 - 每次主程序循环都需要调用
        /// 控制推进器产生所需加速度
        /// </summary>
        public void 控制推进器(Vector3D 绝对加速度)
        {
            内部时钟++;
            if (内部时钟 % 参数们.动力系统更新间隔 != 0)
                return;
            // 获取飞船质量（单位：kg）
            double 飞船质量 = 参考驾驶舱.CalculateShipMass().PhysicalMass;

            // 将绝对加速度转换为飞船本地坐标系（单位：m/s²）
            Vector3D 本地加速度 = Vector3D.TransformNormal(绝对加速度, MatrixD.Transpose(参考驾驶舱.WorldMatrix));

            // 仅在第一次调用或推进器列表发生变化时进行分类
            if (!推进器已分类 || 内部时钟 % 参数们.推进器重新分类间隔 == 0)
            {
                分类推进器(参考驾驶舱);
                推进器已分类 = true;
            }

            // 针对每个轴应用推力控制
            Vector3D 实际应用推力;
            实际应用推力.X = 应用轴向推力(本地加速度.X, "XP", "XN", 飞船质量);
            实际应用推力.Y = 应用轴向推力(本地加速度.Y, "YP", "YN", 飞船质量);
            实际应用推力.Z = 应用轴向推力(本地加速度.Z, "ZP", "ZN", 飞船质量);

            // // 将本地加速度转换回世界坐标系
            // 导弹状态信息.导弹世界力学加速度 = Vector3D.TransformNormal(实际应用推力 / 飞船质量, 控制器.WorldMatrix);
            // Echo($"[推进器] 动力学加速度夹角 {Vector3D.Angle(导弹状态信息.导弹世界力学加速度,控制器.GetAcceleration()):n2}");

        }
        #endregion

        #region 硬件驱动
        /// <summary>
        /// 将推进器按方向分类并保存各轴向最大推力
        /// </summary>
        private void 分类推进器(IMyControllerCompat 控制器)
        {
            // 清空所有组中的推进器和推力记录
            foreach (var key in 推进器推力方向组.Keys.ToList())
            {
                推进器推力方向组[key].Clear();
                轴向最大推力[key] = 0;
            }

            // 遍历所有推进器，根据其局部推进方向归类
            foreach (var 推进器 in 推进器列表)
            {
                // 将推进器的推力方向（正面是喷口，反方向是推力方向）转换到飞船本地坐标系
                Vector3D 推进器推力方向 = 推进器.WorldMatrix.Backward;
                // Vector3D 本地推力方向 = Vector3D.TransformNormal(推进器.WorldMatrix.Backward, MatrixD.Transpose(控制器.WorldMatrix));
                // 根据推进方向分类并累加推力
                string 分类轴向 = null;
                if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Right) > 参数们.推进器方向容差)
                    分类轴向 = "XP";
                else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Right) > 参数们.推进器方向容差)
                    分类轴向 = "XN";
                else if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Up) > 参数们.推进器方向容差)
                    分类轴向 = "YP";
                else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Up) > 参数们.推进器方向容差)
                    分类轴向 = "YN";
                else if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Forward) > 参数们.推进器方向容差)
                    分类轴向 = "ZN";
                else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Forward) > 参数们.推进器方向容差)
                    分类轴向 = "ZP";
                // Echo($"[推进器] 推进器 {推进器.CustomName} 分类为 {分类轴向}");
                if (分类轴向 != null)
                {
                    推进器推力方向组[分类轴向].Add(推进器);
                    轴向最大推力[分类轴向] += 推进器.MaxEffectiveThrust;
                }
            }

            // 对每个方向组内的推进器按最大有效推力从大到小排序
            foreach (var 组 in 推进器推力方向组.Values)
            {
                组.Sort((a, b) => b.MaxEffectiveThrust.CompareTo(a.MaxEffectiveThrust));
            }

            推进器已分类 = true;

        }

        /// <summary>
        /// 为指定轴应用推力控制
        /// </summary>
        private double 应用轴向推力(double 本地加速度, string 正方向组, string 负方向组, double 质量)
        {
            // 计算所需力（牛顿）
            double 需要的力 = 质量 * Math.Abs(本地加速度);

            // 确定应该使用哪个方向的推进器组
            string 激活组名 = 本地加速度 >= 0 ? 正方向组 : 负方向组;
            string 关闭组名 = 本地加速度 >= 0 ? 负方向组 : 正方向组;

            // 首先关闭反方向的推进器组
            List<IMyThrust> 关闭组;
            if (推进器推力方向组.TryGetValue(关闭组名, out 关闭组))
            {
                foreach (var 推进器 in 关闭组)
                {
                    推进器.ThrustOverride = 0f;
                }
            }
            // 获取对应方向的推进器组（已排序）
            List<IMyThrust> 推进器组;
            if (!推进器推力方向组.TryGetValue(激活组名, out 推进器组) || 推进器组.Count == 0)
            {
                return 0.0; // 该方向没有推进器
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
            // // 输出调试信息
            // if (需要的力 > 总分配力 + 0.001)
            // {
            //     Echo($"[推进器] 轴向 {激活组名}推力缺口");
            // }
            // 关闭剩余的推进器
            for (int i = 当前索引; i < 推进器组.Count; i++)
            {
                推进器组[i].ThrustOverride = 0f;
            }
            return 本地加速度 > 0 ? 总分配力 : -总分配力;
        }
        #endregion
    } 
}