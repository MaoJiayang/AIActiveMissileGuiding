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
    #region 导弹状态

    /// <summary>
    /// 导弹状态枚举
    /// </summary>
    public enum 导弹状态机
    {
        待机状态,     // 待机状态，等待挂架分离
        热发射阶段,   // 热发射阶段，挂架已分离但仍在清除挂架
        搜索目标,    // 搜索目标
        跟踪目标,    // 跟踪目标
        预测制导,     // 预测制导
        测试状态,
        引爆激发,     // 引爆激发雷管组
        引爆最终      // 引爆最终雷管组
    }

    /// <summary>
    /// 导弹状态数据类 - 包含所有状态相关变量
    /// </summary>
    public class 导弹状态量
    {
        // 每帧更新
        public 导弹状态机 当前状态;
        public 导弹状态机 上次状态;
        public Vector3D 上次真实目标位置;// AI块的“每帧”
        public bool 角度误差在容忍范围内; 

        // 只更新一次
        public double 陀螺仪最高转速;

        // 计算密集型，每隔一段时间（动力系统更新间隔）更新或按需更新
        public long 上次更新时间戳ms;
        public Vector3D 上帧视线角速度;
        public Vector3D 制导命令;
        public double 导弹最大过载;
        // public Vector3D 导弹世界力学加速度; // 计算出的导弹世界坐标系下的力学加速度
        public Vector3D 上次预测目标位置; // 更新的“每帧”目标位置
        public double 导航常数;
        public bool 等待二阶段引爆;
        private StringBuilder 导弹状态信息;

        /// <summary>
        /// 初始化导弹状态数据
        /// </summary>
        public 导弹状态量()
        {
            当前状态 = 导弹状态机.待机状态;
            上次状态 = 导弹状态机.待机状态;
            上次真实目标位置 = Vector3D.Zero;
            制导命令 = Vector3D.Zero;
            导弹最大过载 = 0;
            // 导弹世界力学加速度 = Vector3D.Zero;
            角度误差在容忍范围内 = false;
            导航常数 = 3.0; // 默认值，会在初始化时设置
            等待二阶段引爆 = false;
            陀螺仪最高转速 = 2 * Math.PI;
            导弹状态信息 = new StringBuilder();
            上帧视线角速度 = Vector3D.Zero;
            上次预测目标位置 = Vector3D.Zero;
        }

        public StringBuilder 获取导弹诊断信息()
        {
            导弹状态信息.Clear();
            // 导弹状态信息.AppendLine($"[导弹状态] 当前状态: {当前状态转文字()}");
            // 导弹状态信息.AppendLine($"[导弹状态] 力学加速度: {导弹世界力学加速度.Length():F2}");
            导弹状态信息.AppendLine($"[导弹状态] 导航常数: {导航常数:F2}");
            导弹状态信息.AppendLine($"[导弹状态] 可用过载: {导弹最大过载:F2}");
            导弹状态信息.AppendLine($"[导弹状态] 需用过载: {制导命令.Length():F2}");
            return 导弹状态信息;
        }
        private string 当前状态转文字()
        {
            switch (当前状态)
            {
                case 导弹状态机.待机状态: return "待机状态";
                case 导弹状态机.热发射阶段: return "热发射阶段";
                case 导弹状态机.搜索目标: return "搜索目标";
                case 导弹状态机.跟踪目标: return "跟踪目标";
                case 导弹状态机.预测制导: return "预测制导";
                case 导弹状态机.测试状态: return "测试状态";
                case 导弹状态机.引爆激发: return "引爆激发";
                case 导弹状态机.引爆最终: return "引爆最终";
                default: return "未知状态";
            }
        }
    }
    #endregion
}