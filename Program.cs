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
// TODO：动态比例导引常数的距离逻辑中，将最大距离从2500改到发射点距离目标的距离。
// TODO: 参数管理器版本控制，自动删除过期版本存储的配置数据并添加新的
// TODO: 方块飞船质量计算查重
// TODO: 方块角速度估算修修复：需要找到一个包括装甲块的办法
// TODO: 方块角速度估算修修复：尝试加入有库存的方块的库存质量估计
namespace IngameScript
{

    public partial class Program : MyGridProgram
    {
        #region 参数管理
        // 参数管理器实例
        private 参数管理器 参数们 = new 参数管理器();

        #endregion

        #region 状态变量
        private 导弹状态量 导弹状态信息;
        private long 上次目标更新时间 = 0;
        private int 预测开始帧数 = 0;
        private int 热发射开始帧数 = 0;
        private int 更新计数器 = 0;
        private long 当前时间戳ms { get { return (long)Math.Round(更新计数器 * 参数们.时间常数 * 1000); } }
        #endregion

        #region 硬件组件
        private bool 已经初始化 = false;
        private IMyBlockGroup 方块组 = null;

        // AI组件
        private IMyFlightMovementBlock 飞行块;
        private IMyOffensiveCombatBlock 战斗块;

        // 控制组件
        private IMyControllerCompat 控制器;
        private List<IMyThrust> 推进器列表 = new List<IMyThrust>();
        private List<IMyGyro> 陀螺仪列表 = new List<IMyGyro>();
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

        // 分离推进器（可选）
        private List<IMyThrust> 分离推进器列表 = new List<IMyThrust>();

        // 推进器分组映射表
        private Dictionary<string, List<IMyThrust>> 推进器方向组 = new Dictionary<string, List<IMyThrust>>
        {
            {"XP", new List<IMyThrust>()}, {"XN", new List<IMyThrust>()},
            {"YP", new List<IMyThrust>()}, {"YN", new List<IMyThrust>()},
            {"ZP", new List<IMyThrust>()}, {"ZN", new List<IMyThrust>()}
        };
        private Dictionary<string, double> 轴向最大推力 = new Dictionary<string, double>
        {
            {"XP", 0}, {"XN", 0}, {"YP", 0}, {"YN", 0}, {"ZP", 0}, {"ZN", 0}
        };
        private Dictionary<IMyGyro, Vector3D> 陀螺仪各轴点积 = new Dictionary<IMyGyro, Vector3D>();
        private bool 推进器已分类 = false;

        #endregion

        #region PID控制系统

        // PID控制器 - 外环(角度误差->期望角速度)
        private PID3 外环PID控制器PYR = null;

        // PID控制器 - 内环(角速度误差->陀螺仪设定)
        private PID3 内环PID控制器PYR = null;

        #endregion

        #region 目标跟踪系统

        private TargetTracker 目标跟踪器;

        #endregion

        #region 性能统计

        private StringBuilder 性能统计信息 = new StringBuilder();
        private StringBuilder 比例导航诊断信息 = new StringBuilder();
        private string 性能统计缓存 = string.Empty;
        private double 总运行时间毫秒 = 0;
        private double 最大运行时间毫秒 = 0;
        private int 运行次数 = 0;

        #endregion

        #region 构造函数和主循环

        public Program()
        {
            // 初始化导弹状态数据
            导弹状态信息 = new 导弹状态量();

            // 初始化参数管理器（可以从Me.CustomData读取配置）
            if (!string.IsNullOrWhiteSpace(Me.CustomData))
            {
                参数们 = new 参数管理器(Me.CustomData);
            }
            else Me.CustomData = 参数们.生成配置字符串();

            // 初始化PID控制器
            初始化PID控制器();

            // 初始化目标跟踪器
            目标跟踪器 = new TargetTracker(参数们.目标历史最大长度);

            // 初始化导航常数
            导弹状态信息.导航常数 = 参数们.导航常数初始值;

            // 陀螺仪的最大命令值
            导弹状态信息.陀螺仪最高转速 = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large ? Math.PI : 2 * Math.PI;

            // 设置更新频率为每tick执行
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            已经初始化 = 初始化硬件();
            if (!挂架系统可用)
            {
                导弹状态信息.当前状态 = 导弹状态机.搜索目标;
                导弹状态信息.上次状态 = 导弹状态机.搜索目标;
                激活导弹系统();
            }
        }

        /// <summary>
        /// 初始化PID控制器
        /// </summary>
        private void 初始化PID控制器()
        {
            double pid时间常数 = 参数们.获取PID时间常数();

            // 初始化外环PID控制器
            外环PID控制器PYR = new PID3(参数们.外环参数.P系数, 参数们.外环参数.I系数, 参数们.外环参数.D系数, pid时间常数);

            // 初始化内环PID控制器
            内环PID控制器PYR = new PID3(参数们.内环参数.P系数, 参数们.内环参数.I系数, 参数们.内环参数.D系数, pid时间常数);

        }

        public void Main(string argument, UpdateType updateSource)
        {
            // 初始化硬件（如果需要）
            if (!已经初始化)
            {
                Echo("硬件初始化失败");
                已经初始化 = 初始化硬件();
                return;
            }
            更新计数器 = (更新计数器 + 1) % int.MaxValue;

            bool 导弹存活 = !(控制器.GetPosition() == Vector3D.Zero) || 控制器.IsFunctional;
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
            bool 导弹保险解除 = 导弹状态信息.当前状态 != 导弹状态机.待机状态 &&
                            导弹状态信息.当前状态 != 导弹状态机.热发射阶段 &&
                            导弹状态信息.当前状态 != 导弹状态机.引爆激发 &&
                            导弹状态信息.当前状态 != 导弹状态机.引爆最终;
            bool 控制器需更新 = 导弹保险解除 || 导弹状态信息.当前状态 == 导弹状态机.热发射阶段;
            导弹状态信息.上次状态 = 导弹状态信息.当前状态;
            Echo($"存在目标：{有有效目标}, 位置更新：{目标位置已改变}");
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
                else if (argument.ToLower() == "detonate")
                {
                    // 直接引爆命令
                    导弹状态信息.当前状态 = 导弹状态机.引爆激发;
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
            }

            // 检查引爆条件（传感器和距离触发）- 只在活跃状态检查
            if (引爆系统可用 && 导弹保险解除)
            {
                bool 传感器触发 = 检查传感器触发();
                bool 距离触发 = 检查距离触发();
                bool 碰撞触发 = 导弹状态信息.当前加速度.LengthSquared() > 4 * 导弹状态信息.导弹世界主过载.LengthSquared();
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
                        战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔跟踪; // 降低战斗块更新目标间隔
                    }
                    break;

                case 导弹状态机.跟踪目标:
                    if (!有有效目标)
                    {
                        // 目标完全丢失，立即切换到预测制导
                        导弹状态信息.当前状态 = 导弹状态机.预测制导;
                        预测开始帧数 = 更新计数器;
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

                // 关闭所有推进器
                foreach (var 推进器 in 推进器列表)
                {
                    推进器.ThrustOverride = 0f;
                    推进器.Enabled = false;
                }

                // 关闭陀螺仪
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    陀螺仪.GyroOverride = false;
                    陀螺仪.Enabled = false;
                }

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

                // 启用陀螺仪但不给指令
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    陀螺仪.Enabled = true;
                    陀螺仪.GyroOverride = true;
                    陀螺仪.Pitch = 0f;
                    陀螺仪.Yaw = 0f;
                    陀螺仪.Roll = 0f;
                }

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
            foreach (var 推进器 in 推进器列表)
            {
                推进器.Enabled = true;
                推进器.ThrustOverride = 0f; // 重置推力覆盖
            }

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
        }

        /// <summary>
        /// 处理搜索状态
        /// </summary>
        private void 处理搜索状态()
        {
            Echo("状态: 搜索目标中...");
            if (目标跟踪器.GetHistoryCount() > 0 || 更新计数器 < 60)
            {
                战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔正常; // 恢复战斗块更新目标间隔          
                // 停止所有推进器
                foreach (var 推进器 in 推进器列表)
                {
                    推进器.ThrustOverride = 0f;
                }
                // 停止陀螺仪覆盖
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    陀螺仪.Pitch = 0f;
                    陀螺仪.Yaw = 0f;
                    陀螺仪.Roll = 0f;
                }
                目标跟踪器.ClearHistory();// 清空目标历史
                内环PID控制器PYR.Reset(); // 重置内环PID控制器
                外环PID控制器PYR.Reset(); // 重置外环PID控制器
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
                }
                应用制导命令(导弹状态信息.制导命令, 控制器);
            }
        }

        #endregion

        #region 硬件初始化
        /// <summary>
        /// 初始化硬件组件
        /// </summary>
        private bool 初始化硬件()
        {
            // 寻找包含当前可编程块(Me)的、以组名开头的方块组
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
            // 获取控制器
            List<IMyShipController> 控制器列表 = new List<IMyShipController>();
            方块组.GetBlocksOfType(控制器列表);

            if (控制器列表.Count > 0)
                控制器 = new ShipControllerAdapter(控制器列表[0]);
            else
            {
                // 控制器 = new BlockMotionTracker(Me, 参数们.动力系统更新间隔 * (1.0 / 60.0), Echo);
                控制器 = new BlockMotionTracker(Me, (1.0 / 60.0), Echo);
                // 如果没有找到控制器，尝试从组中寻找名称包含代理控制器前缀的Terminal方块
                List<IMyTerminalBlock> 代理控制器列表 = new List<IMyTerminalBlock>();
                方块组.GetBlocks(代理控制器列表);
                foreach (var 代理控制器 in 代理控制器列表)
                {
                    if (代理控制器.CustomName.Contains(参数们.代理控制器前缀))
                    {
                        // 控制器 = new BlockMotionTracker(代理控制器, 参数们.动力系统更新间隔 * (1.0 / 60.0), Echo);
                        控制器 = new BlockMotionTracker(代理控制器, (1.0 / 60.0), Echo);
                        break;
                    }
                }
                // 备注：如果在更新间隔之间旋转了超过2π，会导致估算角速度不准确（极端情况）
            }
            导弹状态信息.上帧运动学信息 = new SimpleTargetInfo(控制器.GetPosition(), 控制器.GetShipVelocities().LinearVelocity, 当前时间戳ms);
            // 获取推进器
            推进器列表.Clear();
            方块组.GetBlocksOfType(推进器列表);

            // 获取陀螺仪
            陀螺仪列表.Clear();
            方块组.GetBlocksOfType(陀螺仪列表);

            // 获取重力发生器
            重力发生器列表.Clear();
            方块组.GetBlocksOfType(重力发生器列表);

            // 初始化引爆系统（可选）
            初始化引爆系统(方块组);

            // 初始化挂架系统（可选）
            初始化挂架系统(方块组);

            // 初始化氢气罐系统（可选）
            初始化氢气罐系统(方块组);

            // 初始化分离推进器（可选）
            初始化分离推进器();

            bool 初始化完整 = 配置AI组件(方块组) && 控制器 != null && 推进器列表.Count > 0 && 陀螺仪列表.Count > 0;

            if (初始化完整)
            {
                分类推进器(控制器);
            }
            else
            {
                Echo($"硬件初始化不完整: 控制器={控制器?.CustomName}, 推进器={推进器列表.Count}, 陀螺仪={陀螺仪列表.Count}");
            }
            return 初始化完整;
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
                    Echo("警告：弹头数量不足，全部分配到激发雷管组");
                }

                // 只要有弹头就启用引爆系统
                引爆系统可用 = 所有弹头.Count > 0;

                if (引爆系统可用)
                {
                    Echo($"引爆系统已启用: 弹头={所有弹头.Count}个, 传感器={传感器列表.Count}个");
                }
                else
                {
                    Echo("引爆系统未启用：无弹头");
                }
            }
            catch (Exception ex)
            {
                Echo($"引爆系统初始化警告: {ex.Message}");
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
                分离推进器列表 = 推进器方向组["ZN"]; // 默认使用ZN方向的推进器
            }
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
                Echo($"未找到组名为{参数们.组名前缀}的方块组。");
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
                Echo($"缺少飞行方块。");
                return false;
            }

            // 只使用第一个找到的块
            飞行块 = 飞行块列表[0];
            if (战斗块列表.Count > 0)
                战斗块 = 战斗块列表[0];

            Echo("配置AI组件...");

            // 配置飞行AI
            if (飞行块 != null)
            {
                飞行块.SpeedLimit = 参数们.最大速度限制;       // 最大速度
                飞行块.AlignToPGravity = false; // 不与重力对齐
                飞行块.Enabled = false;         // 关闭方块
                飞行块.ApplyAction("ActivateBehavior_On");
                Echo($"配置飞行块: {飞行块.CustomName}");
            }

            // 配置战斗AI
            if (战斗块 != null)
            {
                战斗块.TargetPriority = 参数们.目标优先级;
                战斗块.UpdateTargetInterval = 参数们.战斗块更新间隔正常;
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
                Echo($"配置战斗块: {战斗块.CustomName}");
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
            // 计算目标角度
            Vector3D 目标角度PYR = 计算陀螺仪目标角度(加速度命令, 控制器);
            // 控制陀螺仪
            应用陀螺仪控制(目标角度PYR);
            // 控制推进器
            控制推进器(加速度命令, 控制器);
        }

        /// <summary>
        /// 比例导航制导算法 - 结合接近速度控制和可选的攻击角度约束
        /// </summary>
        /// <param name="控制器">飞船控制器</param>
        /// <param name="目标信息">目标信息</param>
        /// <returns>制导加速度命令(世界坐标系)</returns>
        private Vector3D 比例导航制导(IMyControllerCompat 控制器, SimpleTargetInfo? 目标信息)
        {
            // ----- 步骤1: 获取基本状态信息 -----
            Vector3D 导弹位置 = 控制器.GetPosition();
            Vector3D 导弹速度 = 控制器.GetShipVelocities().LinearVelocity;
            Vector3D 目标位置 = 目标信息.Value.Position;
            Vector3D 目标速度 = 目标信息.Value.Velocity;
            Vector3D 导弹到目标 = 目标位置 - 导弹位置;
            double 距离 = 导弹到目标.Length();
            double 导弹速度长度 = 导弹速度.Length();

            if (距离 < 参数们.最小向量长度)
                return Vector3D.Zero;

            Vector3D 视线单位向量 = 导弹到目标 / 距离;
            Vector3D 相对速度 = 目标速度 - 导弹速度;

            // ----- 步骤2: 计算视线角速度 -----
            Vector3D 视线角速度 = Vector3D.Cross(导弹到目标, 相对速度) /
                                Math.Max(导弹到目标.LengthSquared(), 参数们.最小向量长度);

            // ----- 步骤2.5 计算加速度（使用上帧数据） -----
            // 计算视线角加速度
            Vector3D 视线角加速度 = (视线角速度 - 导弹状态信息.上帧视线角速度) /
                                (当前时间戳ms - 导弹状态信息.上帧运动学信息.TimeStamp);
            视线角加速度 *= 1000;
            // 计算导弹加速度
            SimpleTargetInfo 导弹当前运动学信息 = new SimpleTargetInfo(导弹位置, 导弹速度, 当前时间戳ms);
            导弹状态信息.当前加速度 = (导弹当前运动学信息.Velocity - 导弹状态信息.上帧运动学信息.Velocity) /
                                (导弹当前运动学信息.TimeStamp - 导弹状态信息.上帧运动学信息.TimeStamp);
            导弹状态信息.当前加速度 *= 1000; // 转换为 m/s²
            // 更新上帧数据（为下一帧准备）
            导弹状态信息.上帧视线角速度 = 视线角速度;
            导弹状态信息.上帧运动学信息 = 导弹当前运动学信息;

            // ----- 步骤3: 计算标准比例导航加速度 -----
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

            // ----- 步骤4: 添加微分补偿项 -----
            // Vector3D 目标加速度 = 目标跟踪器.currentTargetAcceleration;
            // Vector3D 横向加速度 = Vector3D.Cross(视线单位向量, 目标加速度);
            // 比例导航加速度 += 横向加速度;
            Vector3D 微分补偿项 = 计算增强比例导航加速度补偿(距离, 视线单位向量, 视线角加速度, 导弹状态信息.当前加速度, 导弹状态信息.导航常数);
            比例导航加速度 += 微分补偿项;

            // ----- 步骤5: 添加积分补偿项 -----
            // Vector3D 补偿项方向;
            // double 模平方 = 视线角速度.LengthSquared();
            // if (模平方 > 参数们.最小向量长度) // 角速度判0
            //     补偿项方向 = 视线角速度 / Math.Sqrt(模平方);
            // else
            //     补偿项方向 = Vector3D.Zero;
            // Vector3D 积分补偿项 = 比例导航加速度稳态补偿项(目标信息.Value, 补偿项方向);
            // 比例导航加速度 += 积分补偿项;

            // ----- 步骤6: 可选的攻击角度约束 -----
            if (参数们.启用攻击角度约束)
            {
                Vector3D 攻击角度约束加速度 = 计算攻击角度约束加速度(导弹速度, 距离);
                比例导航加速度 += 攻击角度约束加速度;
            }

            // ----- 步骤7: 计算接近分量并执行重力补偿后处理 -----
            Vector3D 最终加速度命令;
            if (导弹到目标.Length() > 参数们.最小向量长度)
            {
                最终加速度命令 = 计算接近加速度并重力补偿(导弹到目标, 比例导航加速度, 控制器);
            }
            else
            {
                Vector3D 重力加速度 = 控制器.GetNaturalGravity();
                最终加速度命令 = 比例导航加速度 - 重力加速度;
            }

            // 更新诊断信息
            比例导航诊断信息.Clear();       
            比例导航诊断信息.AppendLine($"[比例导航] 导弹加速度: {导弹状态信息.当前加速度.Length():n2} m/s²");
            比例导航诊断信息.AppendLine($"[比例导航] 导航常数: {导弹状态信息.导航常数:n1}");
            比例导航诊断信息.AppendLine($"[比例导航] 微分补偿项：{微分补偿项.Length():n1} m/s²");
            比例导航诊断信息.AppendLine($"[比例导航] 目标最大过载: {目标跟踪器.maxTargetAcceleration:n1}");   
            // 比例导航诊断信息.AppendLine($"[比例导航] 积分补偿项：{积分补偿项.Length():n1} m/s²");
            比例导航诊断信息.AppendLine($"[比例导航] 目标距离: {距离:n1} m");

            return 最终加速度命令;
        }

        // /// <summary>
        // /// 比例导航制导算法加速度补偿，或可视为一种积分补偿
        // /// https://doi.org/10.3390/aerospace8080231
        // /// </summary>
        // /// <param name="目标信息">目标信息</param>
        // /// <param name="补偿方向单位向量">视线角速度的单位向量</param>
        // /// <returns>制导加速度补偿命令(世界坐标系)</returns>
        // private Vector3D 比例导航加速度稳态补偿项(SimpleTargetInfo 目标信息, Vector3D 补偿方向单位向量)
        // {
        //     double 目标速度模长 = 目标信息.Velocity.Length(); // 目标速度模长
        //     double 导弹速度模长 = 控制器.GetShipVelocities().LinearVelocity.Length(); // 导弹速度模长
        //     // 获取目标最大加速度 (标量)
        //     double 目标最大加速度 = 目标跟踪器.maxTargetAcceleration; // a_{T,max}

        //     // 计算速度比 (标量)
        //     double 速度比 = 目标速度模长 > 0.1 ? 导弹速度模长 / 目标速度模长 : 1.0; // ν

        //     // 计算 C2 常数 (标量)
        //     double C2 = 0;
        //     if (速度比 > 0.9528) // 只有当导弹速度近似大于目标速度时才应用(101 / 106 ≈ 0.9528)
        //     {
        //         double 根号项 = Math.Sqrt(Math.Max(0, 1.0 - 1.0 / (速度比 * 速度比)));
        //         C2 = 目标最大加速度 * 根号项;
        //     }
        //     C2 = Math.Min(C2, 导弹状态信息.导弹世界主过载.Length()); // 限制最大加速度
        //     return C2 * 补偿方向单位向量; // 返回补偿项            
        // }

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

            // 2) K = 0.12 * N，magic number
            double K = 0.12 * 导航常数N;

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
        /// </summary>
        /// <param name="视线">导弹到目标的视线向量</param>
        /// <param name="比例导航加速度">比例导航计算的加速度</param>
        /// <param name="控制器">飞船控制器，用于获取质量和坐标变换</param>
        /// <returns>经过重力补偿后处理的最终加速度命令</returns>
        private Vector3D 计算接近加速度并重力补偿(Vector3D 视线, Vector3D 比例导航加速度, IMyControllerCompat 控制器)
        {
            Vector3D 视线单位向量 = Vector3D.Normalize(视线);

            // ----- 步骤1: 将比例导航加速度投影到本地坐标系 -----
            double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;
            Vector3D 本地比例导航加速度 = Vector3D.TransformNormal(比例导航加速度, MatrixD.Transpose(控制器.WorldMatrix));

            // ----- 步骤2: 根据符号确定对应轴向推进器，计算本地加速度向量 -----
            Vector3D 本地最大加速度向量 = Vector3D.Zero;

            // 负方向，使用ZN推进器，加上负号
            if (本地比例导航加速度.Z < 0)
            {
                本地最大加速度向量.Z = -(轴向最大推力["ZN"] / 飞船质量);
            }
            else if (推进器方向组["ZP"].Count > 0 && 本地比例导航加速度.Z > 0)
            {
                本地最大加速度向量.Z = (轴向最大推力["ZP"] / 飞船质量);
            }
            else
            {
                本地最大加速度向量.Z = -(轴向最大推力["ZN"] / 飞船质量);
            }
            if (本地比例导航加速度.X < 0)
            {
                // 负X方向，使用XN推进器，加上负号
                本地最大加速度向量.X = -(轴向最大推力["XN"] / 飞船质量);
            }
            else if (本地比例导航加速度.X > 0)
            {
                // 正X方向，使用XP推进器
                本地最大加速度向量.X = (轴向最大推力["XP"] / 飞船质量);
            }
            if (本地比例导航加速度.Y < 0)
            {
                // 负Y方向，使用YN推进器，加上负号
                本地最大加速度向量.Y = -(轴向最大推力["YN"] / 飞船质量);
            }
            else if (本地比例导航加速度.Y > 0)
            {
                // 正Y方向，使用YP推进器
                本地最大加速度向量.Y = (轴向最大推力["YP"] / 飞船质量);
            }

            // ----- 步骤3: 将本地最大加速度向量转换到世界坐标系 -----
            导弹状态信息.导弹世界主过载 = Vector3D.TransformNormal(本地最大加速度向量, 控制器.WorldMatrix);

            // ----- 步骤4: 在世界坐标系中计算直角三角形的径向分量 -----
            double 世界最大加速度模长平方 = 导弹状态信息.导弹世界主过载.LengthSquared();
            double 比例导航加速度模长平方 = 比例导航加速度.LengthSquared();

            // 计算差值
            double 径向分量平方 = 世界最大加速度模长平方 - 比例导航加速度模长平方;

            // ----- 步骤5: 计算接近加速度 -----
            Vector3D 接近加速度;
            if (世界最大加速度模长平方 <= 比例导航加速度模长平方)
            {
                // 推进器能力不足，使用最小接近加速度
                接近加速度 = 参数们.最小接近加速度 * 视线单位向量;
            }
            else
            {
                double 径向加速度大小 = Math.Sqrt(径向分量平方);
                径向加速度大小 = Math.Max(径向加速度大小, 参数们.最小接近加速度);
                接近加速度 = 径向加速度大小 * 视线单位向量;

            }
            导弹状态信息.导航常数 = 参数们.计算导航常数(Math.Sqrt(世界最大加速度模长平方), 视线.Length());
            // ----- 步骤6: 合成飞行方向加速度 -----
            Vector3D 飞行方向加速度 = 比例导航加速度 + 接近加速度;

            // ----- 步骤7: 重力补偿后处理 -----
            Vector3D 重力加速度 = 控制器.GetNaturalGravity();
            Vector3D 最终加速度命令;

            if (飞行方向加速度.LengthSquared() > 参数们.最小向量长度)
            {
                // 1. 以合成后的加速度方向作为导弹飞行方向
                Vector3D 飞行方向单位向量 = Vector3D.Normalize(飞行方向加速度);

                // 2. 将重力分解到飞行方向和垂直飞行方向
                double 重力在飞行方向投影 = Vector3D.Dot(重力加速度, 飞行方向单位向量);
                Vector3D 重力飞行方向分量 = 重力在飞行方向投影 * 飞行方向单位向量;
                Vector3D 重力垂直分量 = (重力加速度 - 重力飞行方向分量) * 1.05; // 多加一点方向

                // 3. 将垂直分量加入最终结果（飞行方向的重力分量不需要补偿，可以利用）
                最终加速度命令 = 飞行方向加速度 - 重力垂直分量;
            }
            else
            {
                // 飞行方向加速度过小，直接进行完整重力补偿
                最终加速度命令 = 飞行方向加速度 - 重力加速度;
            }

            return 最终加速度命令;
        }

        /// <summary>
        /// 计算陀螺仪的目标转向角度，使其指向加速度命令
        /// </summary>
        private Vector3D 计算陀螺仪目标角度(Vector3D 加速度命令, IMyControllerCompat 控制器)
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
            Echo($"[陀螺仪] 角度误差: {角度误差 * 180.0 / Math.PI:n1} 度");
            return 目标角度PYR;
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
        /// 计算最接近时间和距离，匀速假设
        /// </summary>
        /// <param name="目标信息">目标信息</param>
        /// <returns>最接近时间（毫秒）</returns>
        private long 计算最接近时间(SimpleTargetInfo 目标信息)
        {
            // 相对位置和速度
            Vector3D 相对位置 = 目标信息.Position - 控制器.GetPosition();
            Vector3D 相对速度 = 目标信息.Velocity - 控制器.GetShipVelocities().LinearVelocity;

            // 如果相对速度为零，则不会更接近
            double 相对速度平方 = 相对速度.LengthSquared();
            if (相对速度平方 < 参数们.最小向量长度)
            {
                return 0; // 立即就是最接近状态
            }

            // 计算最接近时间：t = -(r·v) / |v|²
            long 最接近时间 = (long)Math.Round(-Vector3D.Dot(相对位置, 相对速度) / 相对速度平方 * 1000); // 转换为毫秒
            Echo($"[预测] 预计拦截: {最接近时间} 毫秒");
            // 如果时间为负，说明最接近点在过去
            if (最接近时间 < 0)
            {
                最接近时间 = 0;
                return 最接近时间;
            }

            return 最接近时间;
        }

        #endregion

        #region 飞行控制系统

        /// <summary>
        /// 控制陀螺仪实现所需转向
        /// </summary>
        private void 应用陀螺仪控制(Vector3D 目标角度PYR)
        {
            // 检查角度误差是否在阈值范围内
            double 角度误差大小 = 目标角度PYR.Length();
            if (角度误差大小 < 参数们.角度误差最小值 && !导弹状态信息.角度误差在容忍范围内)
            {
                // 角度误差很小，停止陀螺仪以减少抖动
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    Vector3D 陀螺仪本地命令 = Vector3D.Zero;
                    陀螺仪本地命令 = 加入本地滚转(陀螺仪, 陀螺仪本地命令, 参数们.常驻滚转转速);
                    施加本地转速指令(陀螺仪, 陀螺仪本地命令);
                }
                // 重置所有PID控制器
                外环PID控制器PYR.Reset();
                内环PID控制器PYR.Reset();
                导弹状态信息.角度误差在容忍范围内 = true;
                return;
            }
            导弹状态信息.角度误差在容忍范围内 = false;
            // 仅在指定更新间隔执行，减少过度控制
            if (更新计数器 % 参数们.动力系统更新间隔 != 0)
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
                Vector3D 陀螺仪本地转速命令 = Vector3D.TransformNormal(最终旋转命令PYR, MatrixD.Transpose(陀螺仪.WorldMatrix));
                陀螺仪本地转速命令 = 加入本地滚转(陀螺仪, 陀螺仪本地转速命令, 参数们.常驻滚转转速);
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
        private Vector3D 加入本地滚转(IMyGyro 陀螺仪, Vector3D 陀螺仪本地命令, double 弧度每秒 = 0.0)
        {
            Vector3D 该陀螺仪点积 = 陀螺仪对轴(陀螺仪, 控制器);
            // if (弧度每秒 < 参数们.最小向量长度)
            // {
            //     return 陀螺仪本地命令; // 不需要滚转
            // }
            // 检查各轴与导弹Z轴的点积，判断是否同向
            double X轴点积 = Math.Abs(该陀螺仪点积.X);
            double Y轴点积 = Math.Abs(该陀螺仪点积.Y);
            double Z轴点积 = Math.Abs(该陀螺仪点积.Z);

            if (X轴点积 > 参数们.推进器方向容差 && X轴点积 >= Y轴点积 && X轴点积 >= Z轴点积)
            {
                陀螺仪本地命令.X = Math.Sign(该陀螺仪点积.X) * 弧度每秒;
            }
            else if (Y轴点积 > 参数们.推进器方向容差 && Y轴点积 >= X轴点积 && Y轴点积 >= Z轴点积)
            {
                陀螺仪本地命令.Y = Math.Sign(该陀螺仪点积.Y) * 弧度每秒;
            }
            else if (Z轴点积 > 参数们.推进器方向容差 && Z轴点积 >= X轴点积 && Z轴点积 >= Y轴点积)
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

            if (Math.Abs(当前值) > 导弹状态信息.陀螺仪最高转速 - 容差)
            {
                // 当前值接近最大值
                if (Math.Sign(当前值) == Math.Sign(新值) && Math.Abs(新值) >= Math.Abs(当前值))
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

        /// <summary>
        /// 控制推进器产生所需加速度
        /// </summary>
        private void 控制推进器(Vector3D 绝对加速度, IMyControllerCompat 控制器)
        {
            if (更新计数器 % 参数们.动力系统更新间隔 != 0)
                return;
            // 获取飞船质量（单位：kg）
            double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;

            // 将绝对加速度转换为飞船本地坐标系（单位：m/s²）
            Vector3D 本地加速度 = Vector3D.TransformNormal(绝对加速度, MatrixD.Transpose(控制器.WorldMatrix));

            // 仅在第一次调用或推进器列表发生变化时进行分类
            if (!推进器已分类 || 更新计数器 % 参数们.推进器重新分类间隔 == 0)
            {
                分类推进器(控制器);
                推进器已分类 = true;
            }

            // 针对每个轴应用推力控制
            应用轴向推力(本地加速度.X, "XP", "XN", 飞船质量);
            应用轴向推力(本地加速度.Y, "YP", "YN", 飞船质量);
            应用轴向推力(本地加速度.Z, "ZP", "ZN", 飞船质量);
        }


        /// <summary>
        /// 将推进器按方向分类并保存各轴向最大推力
        /// </summary>
        private void 分类推进器(IMyControllerCompat 控制器)
        {
            // 清空所有组中的推进器和推力记录
            foreach (var key in 推进器方向组.Keys.ToList())
            {
                推进器方向组[key].Clear();
                轴向最大推力[key] = 0;
            }

            // 遍历所有推进器，根据其局部推进方向归类
            foreach (var 推进器 in 推进器列表)
            {
                // 将推进器的世界方向转换到飞船本地坐标系
                Vector3D 本地推力方向 = Vector3D.TransformNormal(推进器.WorldMatrix.Forward, MatrixD.Transpose(控制器.WorldMatrix));

                // 根据推进方向分类并累加推力
                string 分类轴向 = null;
                if (Vector3D.Dot(本地推力方向, -Vector3D.UnitX) > 参数们.推进器方向容差)
                    分类轴向 = "XP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitX) > 参数们.推进器方向容差)
                    分类轴向 = "XN";
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitY) > 参数们.推进器方向容差)
                    分类轴向 = "YP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitY) > 参数们.推进器方向容差)
                    分类轴向 = "YN";
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitZ) > 参数们.推进器方向容差)
                    分类轴向 = "ZP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitZ) > 参数们.推进器方向容差)
                    分类轴向 = "ZN";

                if (分类轴向 != null)
                {
                    推进器方向组[分类轴向].Add(推进器);
                    轴向最大推力[分类轴向] += 推进器.MaxEffectiveThrust;
                }
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
            string 关闭组名 = 本地加速度 >= 0 ? 负方向组 : 正方向组;

            // 首先关闭反方向的推进器组
            List<IMyThrust> 关闭组;
            if (推进器方向组.TryGetValue(关闭组名, out 关闭组))
            {
                foreach (var 推进器 in 关闭组)
                {
                    推进器.ThrustOverride = 0f;
                }
            }
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
        }

        /// <summary>
        /// 测试陀螺仪控制
        /// </summary>
        private void 旋转控制测试(string argument)
        {
            // 根据参数更新目标到跟踪器中
            if (!string.IsNullOrEmpty(argument))
            {
                long 当前时间戳 = (long)Math.Round(更新计数器 * 参数们.时间常数 * 1000);
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
                else if (argument == "stop")
                {
                    // 停止陀螺仪覆盖
                    foreach (var 陀螺仪 in 陀螺仪列表)
                    {
                        陀螺仪.GyroOverride = false;
                    }
                    // 停止推进器
                    foreach (var 推进器 in 推进器列表)
                    {
                        推进器.ThrustOverride = 0f;
                    }
                    return; // 停止命令直接返回，不执行后续控制逻辑
                }

                // 更新目标到跟踪器
                if (需要更新目标)
                {
                    目标跟踪器.UpdateTarget(测试目标位置, Vector3D.Zero, 当前时间戳);
                    陀螺仪列表.ForEach(g => g.Enabled = true); // 确保陀螺仪开启
                }
            }

            // 统一使用目标跟踪器的最新位置进行控制（在所有分支之外）
            if (目标跟踪器.GetHistoryCount() > 0 && 控制器 != null)
            {
                Vector3D 最新目标位置 = 目标跟踪器.GetLatestPosition();
                Vector3D 导弹位置 = 控制器.GetPosition();
                Vector3D 到目标向量 = 最新目标位置 - 导弹位置;

                if (到目标向量.Length() > 参数们.最小向量长度)
                {
                    Vector3D 目标角度 = 计算陀螺仪目标角度(到目标向量 * 10, 控制器);
                    应用陀螺仪控制(目标角度);
                    Echo($"测试状态控制中");
                    Echo($"自身质量: {控制器.CalculateShipMass().PhysicalMass:n1} kg");
                    Echo($"PID外环参数: P={参数们.外环参数.P系数}, I={参数们.外环参数.I系数}, D={参数们.外环参数.D系数}");
                    Echo($"PID内环参数: P={参数们.内环参数.P系数}, I={参数们.内环参数.I系数}, D={参数们.内环参数.D系数}");
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
            if (导弹状态信息.上次真实目标位置.Equals(Vector3D.Zero)) return false;

            Vector3D 当前位置 = 控制器.GetPosition();
            double 距离 = Vector3D.Distance(当前位置, 导弹状态信息.上次真实目标位置);

            return 距离 <= 参数们.引爆距离阈值;
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
                导弹状态信息.当前状态 = 导弹状态机.搜索目标;
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
                Echo($"无控警告: {控制器.CustomName}");
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
                性能统计缓存 = 导弹状态信息.获取导弹诊断信息().ToString() + '\n' +
                    比例导航诊断信息.ToString() + '\n' +
                    性能统计信息.ToString();
            }
            Echo(性能统计缓存);
        }
        #endregion
    }
}