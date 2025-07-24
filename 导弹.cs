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
    /// 导弹状态数据结构体 - 包含所有状态相关变量
    /// </summary>
    public struct 导弹状态量
    {
        public 导弹状态机 当前状态;
        public 导弹状态机 上次状态;
        public SimpleTargetInfo 上帧运动学信息;
        public Vector3D 当前加速度;
        public Vector3D 上次目标位置;
        public bool 角度误差在容忍范围内;
        public double 导航常数;
        public bool 等待二阶段引爆;
        public double 陀螺仪最高转速;

        /// <summary>
        /// 初始化导弹状态数据
        /// </summary>
        public static 导弹状态量 创建初始状态()
        {
            return new 导弹状态量
            {
                当前状态 = 导弹状态机.待机状态,
                上次状态 = 导弹状态机.待机状态,
                上帧运动学信息 = new SimpleTargetInfo(Vector3D.Zero, Vector3D.Zero, 0),
                当前加速度 = Vector3D.Zero,
                上次目标位置 = Vector3D.Zero,
                角度误差在容忍范围内 = false,
                导航常数 = 3.0, // 默认值，会在初始化时设置
                等待二阶段引爆 = false,
                陀螺仪最高转速 = 2 * Math.PI
            };
        }           
    }
    #endregion
}