using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
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
// TODO：动态比例导引常数的距离逻辑中，将最大距离从2500改到发射点距离目标的距离。
// TODO: 方块质量估算修修复：需要找到一个包括装甲块的办法
// TODO: 方块质量估算修修复：尝试加入有库存的方块的库存质量估计
// TODO: 移动平均长度的时间参数化
namespace IngameScript
{

    public partial class Program : MyGridProgram
    {
        #region 参数管理
        // 参数管理器实例
        private 参数管理器 参数们;

        private 导弹识别器 导弹编组;

        #endregion

        #region 状态变量
        private int 更新计数器 = 0;
        private long 当前时间戳ms { get { return (long)Math.Round(更新计数器 * 参数们.时间常数 * 1000); } }
        private 导弹状态量 导弹状态信息;
        private long 上次目标更新时间 = 0;
        private int 预测开始帧数 = 0;
        private int 热发射开始帧数 = 0;
        private short 初始化计数器 = 0;
        MovingAverageQueue<Vector3D> 外源扰动缓存 = new MovingAverageQueue<Vector3D>(
            20,
            (a, b) => a + b,
            (a, b) => a - b,
            (a, n) => a / n   // VRageMath.Vector3D 已重载 / double
        );
        MovingAverageQueue<Vector3D> 敌加速度缓存 = new MovingAverageQueue<Vector3D>(
            5,
            (a, b) => a + b,
            (a, b) => a - b,
            (a, n) => a / n   // VRageMath.Vector3D 已重载 / double
        );

        #endregion

        #region 硬件组件
        private bool 已经初始化 = false;
        private IMyBlockGroup 方块组 = null;

        // AI组件
        private IMyFlightMovementBlock 飞行块;
        private IMyOffensiveCombatBlock 战斗块;

        // 控制组件
        private IMyControllerCompat 控制器;
        private 推进系统 推进器系统;
        private 陀螺仪瞄准系统 陀螺仪;
        private List<IMyGravityGeneratorBase> 重力发生器列表 = new List<IMyGravityGeneratorBase>();
        // 引爆系统组件（可选）
        private List<IMySensorBlock> 传感器列表 = new List<IMySensorBlock>();
        private List<IMyWarhead> 激发雷管组 = new List<IMyWarhead>();
        private List<IMyWarhead> 引爆雷管组 = new List<IMyWarhead>();
        private bool 引爆系统可用 = false;

        // 挂架系统组件（可选）
        private List<IMyShipConnector> 连接器列表 = new List<IMyShipConnector>();
        private List<IMyShipMergeBlock> 合并方块列表 = new List<IMyShipMergeBlock>();
        private List<IMyMotorStator> 转子列表 = new List<IMyMotorStator>();
        private bool 挂架系统可用 = false;

        // 氢气罐系统组件（可选）
        private List<IMyGasTank> 氢气罐列表 = new List<IMyGasTank>();
        private bool 氢气罐系统可用 = false;

        #endregion

        #region 目标跟踪系统

        private TargetTracker 目标跟踪器;

        #endregion

        #region 性能统计
        private StringBuilder 初始化消息 = new StringBuilder();
        private StringBuilder 性能统计信息 = new StringBuilder();
        private StringBuilder 比例导航诊断信息 = new StringBuilder();
        private string 打印信息缓存 = string.Empty;
        private double 总运行时间毫秒 = 0;
        private double 最大运行时间毫秒 = 0;
        private int 运行次数 = 0;

        #endregion

        #region 构造函数和主循环

        public Program()
        {
            // 初始化导弹状态数据
            导弹状态信息 = new 导弹状态量();
            参数们 = new 参数管理器(Me);
            陀螺仪 = new 陀螺仪瞄准系统(参数们, Me);
            推进器系统 = new 推进系统(参数们, Me);
            导弹编组 = new 导弹识别器(参数们, Me, Echo);
            // 初始化目标跟踪器
            目标跟踪器 = new TargetTracker(参数们.目标历史最大长度);

            // 初始化导航常数
            导弹状态信息.导航常数 = 参数们.导航常数初始值;

            // 设置更新频率为每tick执行
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            已经初始化 = 初始化硬件();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            Echo($"AI主动弹制导脚本 - 版本: {参数们.版本号}");
            // 初始化硬件（如果需要）
            if (!已经初始化)
            {
                Echo("尝试硬件初始化...");
                已经初始化 = 初始化硬件();
                if (已经初始化 && !挂架系统可用)
                {
                    导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                    导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                    激活导弹系统();
                }
                return;
            }
            更新计数器 = (更新计数器 + 1) % int.MaxValue;

            bool 导弹存活 = !(控制器.GetPosition() == Vector3D.Zero) &&
                            控制器.IsFunctional;
            if (!导弹存活 || 更新计数器 % 参数们.方块更新间隔 == 0)
            {
                已经初始化 = 初始化硬件();
            }
            if (!导弹存活) 导弹状态信息.当前状态 = 导弹状态机.待机状态;
            // 获取目标位置
            Vector3D 当前目标位置 = 从飞行块获取目标();
            // 更新目标状态
            更新导弹状态(当前目标位置, argument);


            // 根据当前状态执行相应逻辑
            switch (导弹状态信息.当前状态)
            {
                case 导弹状态机.待机状态:
                    处理待机状态();
                    break;

                case 导弹状态机.热发射阶段:
                    处理热发射阶段();
                    break;

                case 导弹状态机.搜索目标:
                    处理搜索状态();
                    break;

                case 导弹状态机.跟踪目标:
                    处理跟踪状态(当前目标位置);
                    break;

                case 导弹状态机.预测制导:
                    处理预测状态();
                    break;

                case 导弹状态机.测试状态:
                    旋转控制测试(argument);
                    break;

                case 导弹状态机.引爆激发:
                    处理引爆激发状态();
                    break;

                case 导弹状态机.引爆最终:
                    处理引爆最终状态();
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
        private void 更新导弹状态(Vector3D 当前目标位置, string argument = "")
        {
            bool 有有效目标 = !当前目标位置.Equals(Vector3D.NegativeInfinity);
            bool 目标位置已改变 = !当前目标位置.Equals(导弹状态信息.上次真实目标位置);

            bool 导弹保险解除 =
            参数们.手动保险超控 ||
            导弹状态信息.当前状态 != 导弹状态机.待机状态 &&
            导弹状态信息.当前状态 != 导弹状态机.热发射阶段 &&
            导弹状态信息.当前状态 != 导弹状态机.引爆激发 &&
            导弹状态信息.当前状态 != 导弹状态机.引爆最终;

            bool 控制器需更新 = 导弹保险解除 ||
            导弹状态信息.当前状态 == 导弹状态机.热发射阶段 ||
            参数们.手动保险超控;

            Echo($"存在目标：{有有效目标}, 位置更新：{目标位置已改变}, 保险解除：{导弹保险解除}");
            if (控制器需更新) 控制器.Update();

            // 处理测试命令 - 进入测试状态
            if (!string.IsNullOrEmpty(argument))
            {
                if (argument.StartsWith("test"))
                {
                    导弹状态信息.当前状态 = 导弹状态机.测试状态;
                    return; // 直接返回，跳过后续状态检查
                }
                else if (argument == "normal" || argument == "exit")
                {
                    // 退出测试状态，返回搜索状态
                    导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                    return;
                }
                else if (argument.ToLower() == "force_detonate")
                {
                    // 直接引爆命令
                    导弹状态信息.当前状态 = 导弹状态机.引爆激发;
                    return;
                }
                else if (argument.ToLower() == "detonate")
                {
                    if(导弹保险解除) 导弹状态信息.当前状态 = 导弹状态机.引爆激发;
                    return;
                }
                else if (argument.ToLower() == "standby")
                {
                    // 强制进入待机状态
                    导弹状态信息.当前状态 = 导弹状态机.待机状态;
                    return;
                }
                else if (argument.ToLower() == "launch")
                {
                    // 强制进入热发射阶段
                    导弹状态信息.当前状态 = 导弹状态机.热发射阶段;
                    热发射开始帧数 = 更新计数器;
                    return;
                }
                else if (argument.ToLower() == "arm")
                {
                    参数们.手动保险超控 = true;
                    return;
                }
            }

            // 检查引爆条件（传感器和距离触发）- 只在活跃状态检查
            if (引爆系统可用 && 导弹保险解除)
            {
                bool 传感器触发 = 检查传感器触发();
                bool 距离触发 = 检查距离触发();
                bool 碰撞触发 = 检查碰撞触发();

                if (传感器触发 || 距离触发 || 碰撞触发)
                {
                    导弹状态信息.当前状态 = 导弹状态机.引爆激发;
                    return;
                }
            }

            switch (导弹状态信息.当前状态)
            {
                case 导弹状态机.待机状态:
                    // 在待机状态检查挂架分离
                    if (检查挂架分离())
                    {
                        导弹状态信息.当前状态 = 导弹状态机.热发射阶段;
                        热发射开始帧数 = 更新计数器;
                    }
                    break;

                case 导弹状态机.热发射阶段:
                    // 热发射阶段超时后进入搜索状态
                    if (更新计数器 - 热发射开始帧数 > 参数们.热发射持续帧数)
                    {
                        导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                    }
                    break;

                case 导弹状态机.搜索目标:
                    if (有有效目标 && 导弹保险解除)
                    {
                        // 搜索到第一个目标，切换到跟踪状态
                        导弹状态信息.当前状态 = 导弹状态机.跟踪目标;
                        导弹状态信息.上次真实目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                        战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔_专注; // 延长战斗块更新目标间隔
                    }
                    break;

                case 导弹状态机.跟踪目标:
                    if (!有有效目标)
                    {
                        // 目标完全丢失，立即切换到预测制导
                        导弹状态信息.当前状态 = 导弹状态机.预测制导;
                        预测开始帧数 = 更新计数器;
                        战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔_搜索; // 降低战斗块更新目标间隔
                    }
                    else if (目标位置已改变)
                    {
                        // 目标位置更新，继续跟踪
                        导弹状态信息.上次真实目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                    }
                    else
                    {
                        // 目标存在但位置未更新，检查超时
                        if (更新计数器 - 上次目标更新时间 <= 参数们.目标位置不变最大帧数)
                        {
                            导弹状态信息.当前状态 = 导弹状态机.预测制导;
                            预测开始帧数 = 更新计数器;
                        }
                    }
                    break;

                case 导弹状态机.预测制导:
                    if (有有效目标 && 目标位置已改变)
                    {
                        // 目标重新出现且位置更新，切换回跟踪状态
                        导弹状态信息.当前状态 = 导弹状态机.跟踪目标;
                        导弹状态信息.上次真实目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                    }
                    if (! 有有效目标) 战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔_搜索; // 降低战斗块更新目标间隔
                    else if (更新计数器 - 预测开始帧数 > 参数们.预测制导持续帧数)
                    {
                        // 预测制导超时，返回搜索状态
                        导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                    }
                    break;

                case 导弹状态机.测试状态:
                    break;

                case 导弹状态机.引爆激发:
                case 导弹状态机.引爆最终:
                    // 引爆状态由对应的处理方法管理状态转换
                    break;
            }
        }

        /// <summary>
        /// 处理待机状态
        /// </summary>
        private void 处理待机状态()
        {
            Echo("状态: 待机中，等待挂架分离...");

            // 以方块更新间隔来处理组件状态设置
            if (导弹状态信息.上次状态 != 导弹状态机.待机状态 || 更新计数器 % 参数们.方块更新间隔 == 1)
            {
                导弹状态信息.当前状态 = 导弹状态机.待机状态; // 再次更新以触发自动更新
                // 关闭飞控AI组件 - 待机状态全部为false
                if (飞行块 != null)
                {
                    飞行块.Enabled = false;
                    飞行块.ApplyAction("ActivateBehavior_Off");
                }
                if (战斗块 != null)
                {
                    战斗块.Enabled = false;
                    战斗块.ApplyAction("ActivateBehavior_Off");
                }

                推进器系统.关机();
                陀螺仪.关机();

                // 关闭重力发生器
                foreach (var 重力发生器 in 重力发生器列表)
                {
                    重力发生器.Enabled = false;
                }

                // 设置气罐为充气模式
                if (氢气罐系统可用)
                {
                    foreach (var 气罐 in 氢气罐列表)
                    {
                        气罐.Stockpile = true; // 启用储存模式（充气）
                    }
                }
            }
        }

        /// <summary>
        /// 处理热发射阶段
        /// </summary>
        private void 处理热发射阶段()
        {
            Echo("状态: 热发射阶段");

            // 以方块更新间隔来处理组件状态设置
            if (导弹状态信息.上次状态 != 导弹状态机.热发射阶段)
            {
                导弹状态信息.当前状态 = 导弹状态机.热发射阶段;// 再次更新以触发自动更新
                // 恢复气罐自动模式
                if (氢气罐系统可用)
                {
                    // NaN氢气罐兼容
                    氢气罐列表.RemoveAll(罐 => double.IsNaN(罐.Capacity) || double.IsNaN(罐.FilledRatio));
                    foreach (var 气罐 in 氢气罐列表)
                    {
                        气罐.Stockpile = false; // 关闭储存模式（自动）
                    }
                }
                陀螺仪.开机();
                // // 启用陀螺仪但不给指令
                // foreach (var 陀螺仪 in 陀螺仪列表)
                // {
                //     陀螺仪.Enabled = true;
                //     陀螺仪.GyroOverride = true;
                //     陀螺仪.Pitch = 0f;
                //     陀螺仪.Yaw = 0f;
                //     陀螺仪.Roll = 0f;
                // }
                // 只启用分离推进器，其他推进器保持关闭
                推进器系统.分离推进();

                foreach (var 挂架组件 in 连接器列表)
                {
                    挂架组件.Enabled = false; // 禁用挂架组件
                }
                foreach (var 挂架组件 in 合并方块列表)
                {
                    挂架组件.Enabled = false; // 禁用挂架组件
                }
                // 转子不动，可能需要超限
            }

            // 检查热发射阶段是否结束
            if (更新计数器 - 热发射开始帧数 >= 参数们.热发射持续帧数)
            {
                激活导弹系统();
            }
        }
        /// <summary>
        /// 激活导弹系统（热发射阶段结束时调用）
        /// </summary>
        private void 激活导弹系统()
        {
            // 启用飞控AI组件 - 正常工作状态：飞行块Enabled为false，战斗块为true
            if (飞行块 != null)
            {
                飞行块.Enabled = false;
                飞行块.ApplyAction("ActivateBehavior_On");
            }
            if (战斗块 != null)
            {
                战斗块.Enabled = true;
                战斗块.ApplyAction("ActivateBehavior_On");
            }

            // 启用所有推进器
            推进器系统.开机();


            // 启用陀螺仪 实际上不需要，热发射时启动过了
            // foreach (var 陀螺仪 in 陀螺仪列表)
            // {
            //     陀螺仪.Enabled = true;
            //     陀螺仪.GyroOverride = true;
            // }

            // 关闭重力发生器
            foreach (var 重力发生器 in 重力发生器列表)
            {
                重力发生器.Enabled = true;
            }

            foreach (var 氢气罐 in 氢气罐列表)
            {
                氢气罐.Stockpile = false; // 关闭储存模式（自动）
            }
            foreach (var 挂架组件 in 连接器列表)
            {
                挂架组件.Enabled = true; // 启用挂架组件
            }
            foreach (var 挂架组件 in 合并方块列表)
            {
                挂架组件.Enabled = true; // 启用挂架组件
            }
        }

        /// <summary>
        /// 处理搜索状态
        /// </summary>
        private void 处理搜索状态()
        {
            Echo("状态: 搜索目标中...");
            if (目标跟踪器.GetHistoryCount() > 0 || 更新计数器 < 60)
            {
                战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔_搜索; // 恢复战斗块更新目标间隔          
                // // 停止所有推进器
                // 推进器系统.关机();
                // // 停止陀螺仪覆盖
                // 陀螺仪.重置();
                目标跟踪器.ClearHistory();// 清空目标历史

            }

        }

        /// <summary>
        /// 处理跟踪状态
        /// </summary>
        private void 处理跟踪状态(Vector3D 目标位置)
        {
            Echo("状态: 跟踪目标");

            // 更新目标跟踪器
            if (控制器 != null)
            {
                long 当前时间戳 = (long)Math.Round(更新计数器 * 参数们.时间常数 * 1000);
                // 添加到目标跟踪器
                目标跟踪器.UpdateTarget(目标位置, Vector3D.Zero, 当前时间戳);
                // 获取最新目标信息进行制导
                SimpleTargetInfo 目标信息 = 目标跟踪器.PredictFutureTargetInfo(0);

                // long 拦截时间 = Math.Min(计算最接近时间(目标信息), 300);
                // 目标信息 = 目标跟踪器.PredictFutureTargetInfo(拦截时间);

                Vector3D 制导命令 = 比例导航制导(控制器, 目标信息);
                导弹状态信息.制导命令 = 制导命令;
                导弹状态信息.上次预测目标位置 = 目标位置;
                应用制导命令(制导命令, 控制器);
            }
        }

        /// <summary>
        /// 处理预测状态
        /// </summary>
        private void 处理预测状态()
        {
            Echo("状态: 预测制导");
            if (控制器 != null && 目标跟踪器.GetHistoryCount() > 0)
            {
                // long 拦截时间 = Math.Min(计算最接近时间(预测目标), 300);
                // 预测目标 = 目标跟踪器.PredictFutureTargetInfo(拦截时间 + 预测时间毫秒);
                if (更新计数器 % 参数们.动力系统更新间隔 == 0)
                {
                    // 使用预测位置进行制导
                    long 预测时间毫秒 = (long)Math.Round((更新计数器 - 上次目标更新时间) * 参数们.时间常数 * 1000);
                    SimpleTargetInfo 预测目标 = 目标跟踪器.PredictFutureTargetInfo(预测时间毫秒);
                    导弹状态信息.制导命令 = 比例导航制导(控制器, 预测目标);
                    导弹状态信息.上次预测目标位置 = 预测目标.Position;
                }
                应用制导命令(导弹状态信息.制导命令, 控制器);
            }
        }

        #endregion

        #region 硬件初始化
        /// <summary>
        /// 分阶段初始化硬件组件，并维持（最后一步跳转）状态
        /// </summary>
        private bool 初始化硬件()
        {
            初始化计数器++;
            导弹状态信息.当前状态 = 导弹状态机.初始化状态;
            switch (初始化计数器)
            {
                case 2: // Stage 1: 寻找包含当前可编程块的方块组
                    已经初始化 = false;
                    if (!已经初始化)
                    {
                        List<IMyBlockGroup> 所有组 = new List<IMyBlockGroup>();
                        GridTerminalSystem.GetBlockGroups(所有组);
                        foreach (var 组 in 所有组)
                        {
                            // 检查组名是否以指定组名开头
                            if (组.Name.StartsWith(参数们.组名前缀))
                            {
                                // 检查该组是否包含当前可编程块
                                List<IMyTerminalBlock> 组内方块 = new List<IMyTerminalBlock>();
                                组.GetBlocks(组内方块);

                                if (组内方块.Contains(Me))
                                {
                                    方块组 = 组;
                                    break;
                                }
                            }
                        }
                        if (方块组 == null)
                        {
                            Echo($"未找到包含当前可编程块的、以'{参数们.组名前缀}'开头的方块组");
                            return false;
                        }
                    }
                    return false;

                case 4: // Stage 2: 获取控制器
                    List<IMyShipController> 控制器列表 = new List<IMyShipController>();
                    方块组.GetBlocksOfType(控制器列表);

                    if (控制器列表.Count > 0)
                        控制器 = new ShipControllerAdapter(控制器列表[0], (1.0 / 60.0));
                    else
                    {
                        控制器 = new BlockMotionTracker(Me, (1.0 / 60.0), Echo);
                        // 如果没有找到控制器，尝试从组中寻找名称包含代理控制器前缀的Terminal方块
                        List<IMyTerminalBlock> 代理控制器列表 = new List<IMyTerminalBlock>();
                        方块组.GetBlocks(代理控制器列表);
                        foreach (var 代理控制器 in 代理控制器列表)
                        {
                            if (代理控制器.CustomName.Contains(参数们.代理控制器前缀))
                            {
                                控制器 = new BlockMotionTracker(代理控制器, (1.0 / 60.0), Echo);
                                break;
                            }
                        }
                        // 备注：如果在更新间隔之间旋转了超过2π，会导致估算角速度不准确（极端情况）
                    }
                    return false;

                case 6: // Stage 3: 获取推进器
                    推进器系统.初始化(方块组, 控制器);
                    return false;

                case 8: // Stage 4: 初始化陀螺仪系统
                    陀螺仪.初始化(方块组, 控制器);
                    return false;

                case 10: // Stage 5: 获取重力发生器
                    重力发生器列表.Clear();
                    方块组.GetBlocksOfType(重力发生器列表);
                    return false;

                case 12: // Stage 6: 初始化引爆系统
                    初始化引爆系统(方块组);
                    return false;

                case 14: // Stage 7: 初始化挂架系统
                    初始化挂架系统(方块组);
                    return false;

                case 16: // Stage 8: 初始化氢气罐系统
                    初始化氢气罐系统(方块组);
                    return false;

                case 18: // Stage 9: 初始化AI组件并完成
                    bool 初始化完整 = 配置AI组件(方块组) && 控制器 != null && 推进器系统.已初始化 && 陀螺仪.已初始化;
                    if (初始化完整)
                    {
                        // 分类推进器(控制器);
                        计算接近加速度并外力补偿(控制器.WorldMatrix.Forward * 1000, 控制器.WorldMatrix.Forward, 控制器);// 无意义，只是为了获取导弹的最大可能加速度
                        // Echo($"视线方向:{控制器.WorldMatrix.Forward * 1000}");
                        // Echo($"虚构(加速度指令):{控制器.WorldMatrix.Forward}");
                        // throw new Exception("测试报错");
                    }
                    else
                    {
                        初始化消息.AppendLine($"硬件初始化不完整: 控制器={控制器?.CustomName}, {推进器系统.初始化消息}, {陀螺仪.初始化消息}");
                        初始化消息.AppendLine($"或缺少AI组件，请检查");
                        Echo(初始化消息.ToString());
                        初始化消息.Clear();
                        比例导航诊断信息.Clear();
                        初始化计数器 = 0; // 归零
                        // throw new Exception("硬件初始化不完整，无法继续运行");
                    }
                    初始化计数器 = 0; // 归零
                    导弹状态信息.当前状态 = 导弹状态机.待机状态;
                    return true; // 最后一步返回true

                default:
                    return false;
            }
        }


        /// <summary>
        /// 初始化引爆系统（可选功能，不满足条件不报错）
        /// </summary>
        private void 初始化引爆系统(IMyBlockGroup 方块组)
        {
            try
            {
                // 获取传感器
                传感器列表.Clear();
                方块组.GetBlocksOfType(传感器列表);

                // 获取所有弹头
                List<IMyWarhead> 所有弹头 = new List<IMyWarhead>();
                方块组.GetBlocksOfType(所有弹头);

                // 清空之前的分组
                激发雷管组.Clear();
                引爆雷管组.Clear();

                if (所有弹头.Count >= 2)
                {
                    // 弹头数量足够，分为两组
                    int 分割点 = 所有弹头.Count / 2;
                    for (int i = 0; i < 所有弹头.Count; i++)
                    {
                        if (i < 分割点)
                            激发雷管组.Add(所有弹头[i]);
                        else
                            引爆雷管组.Add(所有弹头[i]);
                    }
                }
                else if (所有弹头.Count > 0)
                {
                    // 弹头数量不足，全部归入激发雷管组
                    激发雷管组.AddRange(所有弹头);
                    初始化消息.AppendLine("警告：弹头数量不足，全部分配到激发雷管组");
                }

                // 只要有弹头就启用引爆系统
                引爆系统可用 = 所有弹头.Count > 0;

                if (引爆系统可用)
                {
                    初始化消息.AppendLine($"引爆系统已启用: 弹头={所有弹头.Count}个, 传感器={传感器列表.Count}个");
                }
                else
                {
                    初始化消息.AppendLine("引爆系统未启用：无弹头");
                }
            }
            catch (Exception ex)
            {
                初始化消息.AppendLine($"引爆系统初始化警告: {ex.Message}");
                引爆系统可用 = false;
            }
        }

        /// <summary>
        /// 初始化挂架系统（可选功能，不满足条件不报错）
        /// </summary>
        private void 初始化挂架系统(IMyBlockGroup 方块组)
        {
            // 获取连接器
            连接器列表.Clear();
            方块组.GetBlocksOfType(连接器列表);

            // 获取合并方块
            合并方块列表.Clear();
            方块组.GetBlocksOfType(合并方块列表);

            // 获取转子
            转子列表.Clear();
            方块组.GetBlocksOfType(转子列表);

            // 只要有任意一种挂架组件就启用挂架系统
            挂架系统可用 = 连接器列表.Count > 0 || 合并方块列表.Count > 0 || 转子列表.Count > 0;
        }

        /// <summary>
        /// 初始化气罐系统（可选功能，不满足条件不报错）
        /// </summary>
        private void 初始化氢气罐系统(IMyBlockGroup 方块组)
        {
            // 获取所有气罐
            氢气罐列表.Clear();
            方块组.GetBlocksOfType(氢气罐列表);
            氢气罐系统可用 = 氢气罐列表.Count > 0;
        }

        /// <summary>
        /// 检查挂架分离状态
        /// </summary>
        private bool 检查挂架分离()
        {
            if (!挂架系统可用) return false;
            {
                // 检查连接器状态
                foreach (var 连接器 in 连接器列表)
                {
                    if (!连接器.Enabled)
                    {
                        return true; // 连接器断开
                    }
                }

                // 检查合并方块状态
                foreach (var 合并块 in 合并方块列表)
                {
                    if (!合并块.Enabled)
                    {
                        return true; // 合并块断开
                    }
                }

                // 检查转子头分离状态
                foreach (var 转子 in 转子列表)
                {
                    if (!转子.IsAttached)
                    {
                        return true; // 转子头分离
                    }
                }

                return false; // 所有挂架都还连接着
            }
        }

        /// <summary>
        /// 配置AI组件
        /// </summary>
        private bool 配置AI组件(IMyBlockGroup 方块组)
        {
            if (方块组 == null)
            {
                初始化消息.AppendLine($"未找到组名为{参数们.组名前缀}的方块组。");
                return false;
            }

            // 获取单个飞行块和战斗块
            List<IMyFlightMovementBlock> 飞行块列表 = new List<IMyFlightMovementBlock>();
            List<IMyOffensiveCombatBlock> 战斗块列表 = new List<IMyOffensiveCombatBlock>();

            方块组.GetBlocksOfType(飞行块列表);
            方块组.GetBlocksOfType(战斗块列表);

            // 确保至少找到一个飞行块和战斗块
            if (飞行块列表.Count == 0)
            {
                初始化消息.AppendLine($"缺少飞行方块。");
                return false;
            }

            // 只使用第一个找到的块
            飞行块 = 飞行块列表[0];
            if (战斗块列表.Count > 0)
                战斗块 = 战斗块列表[0];

            初始化消息.AppendLine("配置AI组件...");

            // 配置飞行AI
            if (飞行块 != null)
            {
                飞行块.SpeedLimit = 参数们.最大速度限制;       // 最大速度
                飞行块.AlignToPGravity = false; // 不与重力对齐
                飞行块.Enabled = false;         // 关闭方块
                飞行块.ApplyAction("ActivateBehavior_On");
                初始化消息.AppendLine($"配置飞行块: {飞行块.CustomName}");
            }

            // 配置战斗AI
            if (战斗块 != null)
            {
                战斗块.TargetPriority = 参数们.目标优先级;
                战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔_搜索;
                战斗块.Enabled = true;
                战斗块.SelectedAttackPattern = 参数们.战斗块攻击模式; // 拦截模式
                战斗块.ApplyAction("ActivateBehavior_On");

                IMyAttackPatternComponent 攻击模式;
                if (战斗块.TryGetSelectedAttackPattern(out 攻击模式))
                {
                    IMyOffensiveCombatIntercept 拦截模式 = 攻击模式 as IMyOffensiveCombatIntercept;
                    if (拦截模式 != null)
                    {
                        拦截模式.GuidanceType = GuidanceType.Basic;
                    }
                }
                初始化消息.AppendLine($"配置战斗块: {战斗块.CustomName}");
            }
            return 飞行块 != null && 战斗块 != null;
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
                IMyAutopilotWaypoint 路径点 = 路径点列表[路径点列表.Count - 1];

                // 从世界矩阵中直接提取位置坐标
                MatrixD 矩阵 = 路径点.Matrix;
                Vector3D 位置 = new Vector3D(矩阵.M41, 矩阵.M42, 矩阵.M43);

                // // 在CustomData中只保存GPS坐标
                // string GPS字符串 = $"GPS:{路径点.Name}目标:{位置.X:0.##}:{位置.Y:0.##}:{位置.Z:0.##}:#FF75C9F1:";
                // 飞行块.CustomData = GPS字符串;

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
        private void 应用制导命令(Vector3D 加速度命令, IMyControllerCompat 控制器)
        {
            // 控制陀螺仪
            陀螺仪.方向瞄准(加速度命令);
            // 控制推进器
            推进器系统.控制推进器(加速度命令);
        }

        /// <summary>
        /// 比例导航制导算法 - 结合接近速度控制和可选的攻击角度约束
        /// </summary>
        /// <param name="控制器">飞船控制器</param>
        /// <param name="目标信息">目标信息</param>
        /// <returns>制导加速度命令(世界坐标系)</returns>
        private Vector3D 比例导航制导(IMyControllerCompat 控制器, SimpleTargetInfo 目标信息)
        {
            // 更新诊断信息
            比例导航诊断信息.Clear();
            // ----- 步骤1: 获取基本状态信息 -----
            Vector3D 导弹位置 = 控制器.GetPosition();
            Vector3D 导弹速度 = 控制器.GetShipVelocities().LinearVelocity;
            Vector3D 目标位置 = 目标信息.Position;
            Vector3D 目标速度 = 目标信息.Velocity;
            Vector3D 视线 = 目标位置 - 导弹位置;
            double 距离 = 视线.Length();
            double 导弹速度长度 = 导弹速度.Length();

            if (距离 < 参数们.最小向量长度)
                return Vector3D.Zero;

            Vector3D 视线单位向量 = 视线 / 距离;
            Vector3D 相对速度 = 目标速度 - 导弹速度;

            // ----- 步骤2: 计算视线角速度 -----
            Vector3D 视线角速度 = Vector3D.Cross(视线, 相对速度) /
                                Math.Max(视线.LengthSquared(), 参数们.最小向量长度);

            // ----- 步骤4: 计算标准比例导航加速度 -----
            Vector3D 导弹速度单位向量;
            if (导弹速度长度 < 参数们.最小向量长度)
            {
                导弹速度单位向量 = 视线单位向量.Normalized();
            }
            else
            {
                导弹速度单位向量 = 导弹速度 / 导弹速度长度;
            }

            double 相对速度大小 = 相对速度.Length();
            Vector3D 比例导航加速度 = 导弹状态信息.导航常数 * 相对速度大小 * Vector3D.Cross(视线角速度, 视线单位向量);

            // ----- 步骤5: 添加微分补偿项 -----
            敌加速度缓存.AddFirst(目标信息.Acceleration);
            Vector3D 目标加速度 = 敌加速度缓存.Average;
            Vector3D 横向加速度 = Vector3D.Cross(视线单位向量, 目标加速度);
            比例导航加速度 += 0.5 * 导弹状态信息.导航常数 * 横向加速度;// 依赖目标加速度估算

            // ----- 步骤6: 可选的攻击角度约束 -----
            if (参数们.启用攻击角度约束)
            {
                Vector3D 攻击角度约束加速度 = 计算攻击角度约束加速度(导弹速度, 距离);
                比例导航加速度 += 攻击角度约束加速度;
            }
            // 预测未来位置
            long 最接近时间 = Math.Min(计算最接近时间(目标信息),参数们.最长接近预测时间);
            SimpleTargetInfo 最接近位置 = 目标跟踪器.PredictFutureTargetInfo(最接近时间);
            视线 = 最接近位置.Position - 控制器.GetPosition();
            // ----- 步骤7: 计算接近分量并执行重力补偿后处理 -----
            Vector3D 最终加速度命令;
            if (视线.Length() > 参数们.最小向量长度)
            {
                最终加速度命令 = 计算接近加速度并外力补偿(视线, 比例导航加速度, 控制器);
            }
            else
            {
                Vector3D 重力加速度 = 控制器.GetNaturalGravity();
                最终加速度命令 = 比例导航加速度 - 重力加速度;   
            }
            

            比例导航诊断信息.AppendLine($"[比例导航] ETC: {最接近时间 / 1000.0 :n1} s");
            比例导航诊断信息.AppendLine($"[比例导航] 目标距离: {距离:n1} m");
            比例导航诊断信息.AppendLine($"[比例导航] 目标最大过载: {目标跟踪器.maxTargetAcceleration:n1}");
            比例导航诊断信息.AppendLine($"[比例导航] 导航常数: {导弹状态信息.导航常数:n1}");
            // 比例导航诊断信息.AppendLine($"[比例导航] 积分补偿项：{积分补偿项.Length():n1} m/s²");
            // 比例导航诊断信息.AppendLine($"[比例导航] 微分补偿项：{微分补偿项.Length():n1} m/s²");


            return 最终加速度命令;
        }

        /// <summary>
        /// 计算 TAPN 补偿项（K = 0.1 * N）
        /// a_tapn = K * ( 0.5 * R * (λ_ddot × l̂) + a_M,perp )
        /// Augmented proportional navigation guidance law using angular acceleration measurements
        /// https://patents.google.com/patent/US7446291B1/en by LOCKHEED MARTIN CORPORATION
        /// </summary>
        private Vector3D 计算增强比例导航加速度补偿(
            double 距离,
            Vector3D 视线单位向量,     // l̂
            Vector3D 视线角加速度,     // λ_ddot（向量形式）
            Vector3D 导弹当前线加速度, // a_M（世界坐标）
            double 导航常数N           // N
        )
        {
            // 1) 输入完备性检查
            if (距离 < 参数们.最小向量长度 || 导航常数N <= 0)
                return Vector3D.Zero;

            // 2) K = 0.1 * N，magic number
            double K = 0.1 * 导航常数N;

            // 3) 计算导弹在 LOS 垂直方向的加速度分量 a_M,perp
            Vector3D aM_parallel = Vector3D.Dot(导弹当前线加速度, 视线单位向量) * 视线单位向量;
            Vector3D aM_perp = 导弹当前线加速度 - aM_parallel;

            // 4) 把角加速度 λ_ddot 转为线加速度方向量：λ_ddot × l̂
            Vector3D los2Dir = Vector3D.Cross(视线角加速度, 视线单位向量);

            // 5) TAPN补偿项
            Vector3D a_tapn = K * (0.5 * 距离 * los2Dir + aM_perp);

            return a_tapn;
        }

        /// <summary>
        /// 计算接近加速度并执行重力补偿后处理
        /// 同时这里也会更新导弹的最大过载与导航常数
        /// </summary>
        /// <param name="视线">导弹到目标的视线向量</param>
        /// <param name="比例导航加速度">比例导航计算的加速度</param>
        /// <param name="控制器">飞船控制器，用于获取质量和坐标变换</param>
        /// <returns>经过重力补偿后处理的最终加速度命令</returns>
        private Vector3D 计算接近加速度并外力补偿(Vector3D 视线, Vector3D 比例导航加速度, IMyControllerCompat 控制器)
        {
            Vector3D 视线单位向量 = Vector3D.Normalize(视线);
            double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;

            // 外部预计算：用于补偿外源扰动
            Vector3D 运动学加速度 = 控制器.GetAcceleration();
            // Vector3D 本地重力 = 控制器.GetNaturalGravity();

            // 用于输出：最终推进器输出方向（迭代中的最大加速度方向）
            Vector3D 最终最大过载向量 = Vector3D.Zero;
            Vector3D 最终加速度指令 = Vector3D.Zero;
            Vector3D 当前Cmd方向 = Vector3D.Normalize(比例导航加速度 + 参数们.最小接近加速度 * 视线单位向量);
            var 轴向最大推力 = 推进器系统.轴向最大推力;
            for (int i = 0; i < 2; i++)
            {
                // Step1: 当前Cmd方向下的最大加速度
                Vector3D 当前Cmd本地 = Vector3D.TransformNormal(当前Cmd方向, MatrixD.Transpose(控制器.WorldMatrix));
                Vector3D 本地最大加速度向量 = Vector3D.Zero;
                本地最大加速度向量.X = 当前Cmd本地.X >= 0 ? (轴向最大推力["XP"] / 飞船质量) : -(轴向最大推力["XN"] / 飞船质量);
                本地最大加速度向量.Y = 当前Cmd本地.Y >= 0 ? (轴向最大推力["YP"] / 飞船质量) : -(轴向最大推力["YN"] / 飞船质量);
                本地最大加速度向量.Z = 当前Cmd本地.Z >= 0 ? (轴向最大推力["ZP"] / 飞船质量) : -(轴向最大推力["ZN"] / 飞船质量);  
                Vector3D 世界最大加速度向量 = Vector3D.TransformNormal(本地最大加速度向量, 控制器.WorldMatrix);
                double 世界最大加速度模长 = 世界最大加速度向量.Length();
                
                // Step2: 判断是否足以提供ProNav指令
                double ProNav模长 = 比例导航加速度.Length();
                Vector3D 接近加速度 = Vector3D.Zero;

                if (世界最大加速度模长 <= ProNav模长)
                {
                    // 提供不了，直接使用最小接近加速度
                    接近加速度 = 参数们.最小接近加速度 * 视线单位向量;
                }
                else
                {
                    // 使用直角三角形原则分配
                    double 剩余加速度平方 = 世界最大加速度模长 * 世界最大加速度模长 - ProNav模长 * ProNav模长;
                    double 接近加速度大小 = Math.Sqrt(剩余加速度平方);
                    接近加速度大小 = Math.Max(接近加速度大小, 参数们.最小接近加速度);
                    接近加速度 = 接近加速度大小 * 视线单位向量;
                }

                Vector3D 当前Cmd = 比例导航加速度 + 接近加速度;
                Vector3D 新方向 = Vector3D.Normalize(当前Cmd);
                if ((新方向 - 当前Cmd方向).LengthSquared() < 参数们.最小向量长度)
                {
                    最终最大过载向量 = 世界最大加速度向量;
                    最终加速度指令 = 当前Cmd;
                    break;
                }

                当前Cmd方向 = 新方向;
                最终最大过载向量 = 世界最大加速度向量;
                最终加速度指令 = 当前Cmd;

            }
            导弹状态信息.导弹最大过载 = Math.Max(导弹状态信息.导弹最大过载, 最终最大过载向量.Length());
            导弹状态信息.导航常数 = 参数们.计算导航常数(导弹状态信息.导弹最大过载, 视线.Length());

            // Step3: 外力扰动补偿
            外源扰动缓存.AddFirst(运动学加速度 - 最终最大过载向量);
            Vector3D 外源扰动加速度 = 外源扰动缓存.Average;

            Vector3D 飞行方向单位向量 = Vector3D.Normalize(最终加速度指令);
            double 外力平行分量大小 = Vector3D.Dot(外源扰动加速度, 飞行方向单位向量);
            Vector3D 外力垂直分量 = 外源扰动加速度 - (外力平行分量大小 * 飞行方向单位向量);

            if (外力垂直分量.LengthSquared() > 参数们.最大外力干扰 * 参数们.最大外力干扰)
            {
                外力垂直分量 = 外力垂直分量.Normalized() * 参数们.最大外力干扰;
            }

            // 比例导航诊断信息.AppendLine($"[比例导航] 垂直干扰: {外力垂直分量.Length():n2} m/s²");

            if (参数们.启用外力干扰)
            {
                return 最终加速度指令 - 外力垂直分量;
            }
            else
            {
                return 最终加速度指令;
            }
        }

        /// <summary>
        /// 计算攻击角度约束所需的加速度补偿量
        /// </summary>
        /// <param name="导弹速度">当前导弹速度向量</param>
        /// <param name="目标距离">导弹到目标的距离</param>
        /// <returns>攻击角度约束加速度补偿向量</returns>
        private Vector3D 计算攻击角度约束加速度(Vector3D 导弹速度, double 目标距离)
        {
            return Vector3D.Zero;
        }
  
        /// <summary>
        /// 计算最接近时间，考虑恒定加速度，迭代法
        /// </summary>
        /// <param name="目标信息">目标信息</param>
        /// <returns>最接近时间（毫秒）</returns>
        private long 计算最接近时间(SimpleTargetInfo 目标信息)
        {
            // 初始值获取
            Vector3D 相对位置 = 目标信息.Position - 控制器.GetPosition();
            Vector3D 相对速度 = 目标信息.Velocity - 控制器.GetShipVelocities().LinearVelocity;
            Vector3D 相对加速度 = 目标信息.Acceleration - 控制器.GetAcceleration();
            double 最小向量平方 = 参数们.最小向量长度;
            // 特判：如果速度近似为0，直接返回0
            double 相对速度平方 = 相对速度.LengthSquared();
            if (相对速度平方 < 最小向量平方)
                return 0;

            // 初始猜测时间（匀速模型）：t = - (r·v) / |v|²
            double t = -Vector3D.Dot(相对位置, 相对速度) / 相对速度平方;
            if (t < 0) return 0;

            const int 最大迭代次数 = 3;
            const double 精度阈值 = 0.001; // 秒级精度目标（1ms）

            for (int i = 0; i < 最大迭代次数; i++)
            {
                // r(t) = r0 + v0 * t + 0.5 * a * t^2
                // v(t) = v0 + a * t
                Vector3D r_t = 相对位置 + 相对速度 * t + 0.5 * 相对加速度 * t * t;
                Vector3D v_t = 相对速度 + 相对加速度 * t;

                double f = Vector3D.Dot(r_t, v_t); // 目标函数
                double df = Vector3D.Dot(v_t, v_t) + Vector3D.Dot(r_t, 相对加速度); // 导数

                // 避免除0
                if (Math.Abs(df) < 1e-6) break;

                double t_next = t - f / df;

                // 保证非负时间
                if (t_next < 0) t_next = 0;

                if (Math.Abs(t_next - t) < 精度阈值)
                {
                    t = t_next;
                    break;
                }

                t = t_next;
            }

            return (long)Math.Round(t * 1000); // 返回毫秒
        }

        #endregion

        #region 飞行测试系统

        /// <summary>
        /// 测试陀螺仪控制
        /// </summary>
        private void 旋转控制测试(string argument)
        {
            // 根据参数更新目标到跟踪器中
            if (!string.IsNullOrEmpty(argument))
            {
                Vector3D 测试目标位置 = Vector3D.Zero;
                bool 需要更新目标 = false;

                if (argument == "test")
                {
                    // 生成随机向量作为测试目标
                    Random random = new Random();
                    double x = random.NextDouble() * 2 - 1;  // -1到1之间
                    double y = random.NextDouble() * 2 - 1;
                    double z = random.NextDouble() * 2 - 1;

                    Vector3D 测试加速度命令 = new Vector3D(x, y, z) * 100; // 放大以便观察效果

                    if (控制器 != null)
                    {
                        测试目标位置 = 控制器.GetPosition() + 测试加速度命令.Normalized() * 1000; // 1000米外的测试目标
                        需要更新目标 = true;
                    }
                }
                else if (argument == "testbackward")
                {
                    // 测试前向控制
                    if (控制器 != null)
                    {
                        测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Backward * 1000;
                        测试目标位置.X += 1; // 轻微偏移以避免与前向重合
                        需要更新目标 = true;
                    }
                }
                else if (argument == "testup")
                {
                    // 测试上向控制
                    if (控制器 != null)
                    {
                        测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Up * 1000;
                        需要更新目标 = true;
                    }
                }
                else if (argument == "testright")
                {
                    // 测试右向控制
                    if (控制器 != null)
                    {
                        测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Right * 1000;
                        需要更新目标 = true;
                    }
                }
                else if (argument == "testforward")
                {
                    // 测试前向控制
                    if (控制器 != null)
                    {
                        测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Forward * 1000;
                        需要更新目标 = true;
                    }
                }
                else if (argument == "testouter")
                {
                    // 测试外源扰动方向
                    测试目标位置 = 控制器.GetPosition() + 控制器.GetAcceleration() * 114514.0;
                    需要更新目标 = true;
                }
                else if (argument == "stop")
                {
                    // 停止陀螺仪覆盖
                    陀螺仪.关机();
                    // 停止推进器
                    推进器系统.关机();
                    return; // 停止命令直接返回，不执行后续控制逻辑
                }


                // 更新目标到跟踪器
                if (需要更新目标)
                {
                    目标跟踪器.UpdateTarget(测试目标位置, Vector3D.Zero, 当前时间戳ms);
                    // 陀螺仪列表.ForEach(g => g.Enabled = true); // 确保陀螺仪开启
                }
            }

            // 统一使用目标跟踪器的最新位置进行控制（在所有分支之外）
            if (目标跟踪器.GetHistoryCount() > 0 && 控制器 != null)
            {

                Vector3D 最新目标位置 = 目标跟踪器.GetLatestPosition();
                Vector3D 导弹位置 = 控制器.GetPosition();
                Vector3D 到目标向量 = 最新目标位置 - 导弹位置;
                SimpleTargetInfo 假目标 = new SimpleTargetInfo();
                假目标.Position = 最新目标位置;
                比例导航制导(控制器, 假目标);
                if (到目标向量.Length() > 参数们.最小向量长度)
                {
                    // 陀螺仪.方向瞄准(到目标向量);
                    陀螺仪.点瞄准(最新目标位置);
                    Echo($"测试状态控制中");
                    Echo($"加速与前向夹角: {Vector3D.Angle(控制器.WorldMatrix.Forward, 控制器.GetAcceleration()):n2}");
                    Echo($"运动学加速度: {控制器.GetAcceleration().Length():n2}");
                    Echo($"自身质量: {控制器.CalculateShipMass().PhysicalMass:n1} kg");
                    // Echo($"PID外环参数: P={参数们.外环参数.P系数}, I={参数们.外环参数.I系数}, D={参数们.外环参数.D系数}");
                    // Echo($"PID内环参数: P={参数们.内环参数.P系数}, I={参数们.内环参数.I系数}, D={参数们.内环参数.D系数}");
                }
                else
                {
                    Echo("目标距离过近，停止控制");
                }
            }
            else
            {
                Echo("没有可用的目标历史记录进行测试");
            }
        }

        #endregion

        #region 引爆系统

        /// <summary>
        /// 检查传感器是否触发
        /// </summary>
        private bool 检查传感器触发()
        {
            // 引爆系统可用且有传感器才检查传感器触发
            if (!引爆系统可用 || 传感器列表.Count == 0) return false;

            foreach (var 传感器 in 传感器列表)
            {
                if (传感器.IsActive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查距离是否触发引爆
        /// </summary>
        private bool 检查距离触发()
        {
            if (!引爆系统可用 || 控制器 == null) return false;

            // 检查上次目标位置是否有效
            if (导弹状态信息.上次预测目标位置.Equals(Vector3D.Zero)) return false;

            Vector3D 当前位置 = 控制器.GetPosition();
            double 距离 = Vector3D.Distance(当前位置, 导弹状态信息.上次预测目标位置);

            return 距离 <= 参数们.引爆距离阈值;
        }
        private bool 检查碰撞触发()
        {
            if (!引爆系统可用 || 控制器 == null) return false;
            // Echo($"检查引爆条件...当前过载：{导弹状态信息.导弹最大过载:n2}");
            bool 加速度触发 = 控制器.GetAcceleration().LengthSquared() - 参数们.最大外力干扰 * 参数们.最大外力干扰 >
                            参数们.碰炸迟缓度 * 导弹状态信息.导弹最大过载 * 导弹状态信息.导弹最大过载;
            if (!加速度触发) return false;
            if (参数们.手动保险超控) return true;
            // 检查上次目标位置是否有效
            if (导弹状态信息.上次预测目标位置.Equals(Vector3D.Zero)) return false;
            Vector3D 当前位置 = 控制器.GetPosition();
            double 距离 = Vector3D.Distance(当前位置, 导弹状态信息.上次预测目标位置);
            if (距离 > 参数们.碰炸解锁距离) return false;

            return true;
        }
        /// <summary>
        /// 处理引爆激发状态
        /// </summary>
        private void 处理引爆激发状态()
        {
            Echo("状态: 引爆激发中...");

            // 触发激发雷管组
            foreach (var 弹头 in 激发雷管组)
            {
                弹头.IsArmed = true;
                弹头.Detonate();
            }

            // 标记等待二阶段引爆并转入下一状态
            导弹状态信息.等待二阶段引爆 = true;
            导弹状态信息.当前状态 = 导弹状态机.引爆最终;
        }

        /// <summary>
        /// 处理引爆最终状态
        /// </summary>
        private void 处理引爆最终状态()
        {
            Echo("状态: 引爆二阶段...");

            // 如果上一帧已触发激发雷管组，本帧触发引爆雷管组
            if (导弹状态信息.等待二阶段引爆)
            {
                foreach (var 弹头 in 引爆雷管组)
                {
                    弹头.IsArmed = true;
                    弹头.Detonate();
                }
                导弹状态信息.等待二阶段引爆 = false;
                导弹状态信息.当前状态 = 导弹状态机.待机状态;
                已经初始化 = 初始化硬件();
                // 引爆完成，保持当前状态（导弹应该已经销毁）
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
            if (运行次数 % 参数们.性能统计重置间隔 == 0)
            {
                // 每600次运行重置统计信息
                总运行时间毫秒 = 0;
                运行次数 = 0;
                最大运行时间毫秒 = 0;
            }
            if (上次运行时间毫秒 > 最大运行时间毫秒)
                最大运行时间毫秒 = 上次运行时间毫秒;
            // 计算平均运行时间
            double 平均运行时间毫秒 = 总运行时间毫秒 / 运行次数;

            //如果用的是BlockMotionTracker，显示警告信息
            if (控制器 is BlockMotionTracker)
            {
                // 如果是BlockMotionTracker，显示警告信息
                Echo($"代替控制器: {控制器.CustomName}");
            }
            // 清空并重新构建性能统计信息
            性能统计信息.Clear();
            性能统计信息.AppendLine("=== 性能统计 ===");
            性能统计信息.AppendLine($"上次运行: {上次运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"平均运行: {平均运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"最大运行: {最大运行时间毫秒:F3} ms");
            性能统计信息.AppendLine($"运行次数: {运行次数}");
            性能统计信息.AppendLine($"指令使用: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount}");
            性能统计信息.AppendLine("================");

            打印各类状态信息();

        }
        private void 打印各类状态信息()
        {
            if (更新计数器 % 参数们.动力系统更新间隔 == 0)
            {
                打印信息缓存 = 导弹状态信息.获取导弹诊断信息().ToString() +
                    比例导航诊断信息.ToString() +
                    性能统计信息.ToString();
            }
            Echo(打印信息缓存);
        }
        #endregion
    }
}