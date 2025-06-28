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
            预测制导,     // 预测制导
            测试状态,
            引爆激发,     // 引爆激发雷管组
            引爆最终      // 引爆最终雷管组
        }

        #endregion

        #region 常量定义

        // 制导相关常量
        private const bool 滑翔模式 = false;
        private double 导航常数 = 3.5;                     // 默认比例导航常数
        private const double 最小向量长度 = 1e-6;                 // 向量最小有效长度
        private const double 最小接近加速度 = 5;                // 最小接近加速度(m/s)
        private const double 时间常数 = 1f / 60f;                // 时间常数(秒)
        private const int 陀螺仪更新间隔 = 5;                     // 陀螺仪更新间隔(ticks)
        private const double 角度误差阈值 = Math.PI / 180.0 * 0.25;  // 0.15度的角度误差阈值(弧度制)

        // 引爆相关常量
        private const double 引爆距离阈值 = 5.0;                   // 接近引爆距离阈值(米)

        // 状态切换时间常量
        private const int 目标丢失超时帧数 = 180;                 // 目标丢失判定时间
        private const int 预测制导持续帧数 = 300;                 // 预测制导持续时间
        private const int 方块更新间隔 = 1200;                     // 重新初始化方块的间隔

        #endregion

        #region 状态变量

        private 导弹状态 当前状态 = 导弹状态.搜索目标;
        private Vector3D 上次目标位置 = Vector3D.Zero;
        private long 上次目标更新时间 = 0;
        private int 预测开始帧数 = 0;
        private int 更新计数器 = 0;
        private bool 等待二阶段引爆 = false;

        #endregion

        #region 硬件组件
        private bool 已经初始化 = false;
        private IMyBlockGroup 方块组 = null;
        private string 组名 = "导弹";

        // AI组件
        private IMyFlightMovementBlock 飞行块;
        private IMyOffensiveCombatBlock 战斗块;

        // 控制组件
        private IMyShipController 控制器;
        private List<IMyThrust> 推进器列表 = new List<IMyThrust>();
        private List<IMyGyro> 陀螺仪列表 = new List<IMyGyro>();

        // 引爆系统组件（可选）
        private List<IMySensorBlock> 传感器列表 = new List<IMySensorBlock>();
        private List<IMyWarhead> 激发雷管组 = new List<IMyWarhead>();
        private List<IMyWarhead> 引爆雷管组 = new List<IMyWarhead>();
        private bool 引爆系统可用 = false;
        
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
        private bool 推进器已分类 = false;
        private bool 陀螺仪已清零 = false;

        #endregion

        #region PID控制系统

        // PID控制器 - 外环(角度误差->期望角速度)
        private PID 偏航外环 = new PID(4, 0, 0, 时间常数 * 陀螺仪更新间隔);// );//
        private PID 俯仰外环 = new PID(4, 0, 0, 时间常数 * 陀螺仪更新间隔);
        private PID 横滚外环 = new PID(4, 0, 0, 时间常数 * 陀螺仪更新间隔);

        // PID控制器 - 内环(角速度误差->陀螺仪设定)
        private PID 偏航内环PD = new PID(12, 0.005, 0.2, 时间常数 * 陀螺仪更新间隔);
        private PID 俯仰内环PD = new PID(12, 0.005, 0.2, 时间常数 * 陀螺仪更新间隔);
        private PID 横滚内环PD = new PID(12, 0.005, 0.2, 时间常数 * 陀螺仪更新间隔);

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
            已经初始化 = 初始化硬件();
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
            if (更新计数器 % 方块更新间隔 == 0)
            {
                已经初始化 = 初始化硬件();
            }
            // 获取目标位置
            Vector3D 当前目标位置 = 从飞行块获取目标();
            
            // 更新目标状态
            更新目标状态(当前目标位置, argument);

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
                    
                case 导弹状态.测试状态:
                    TestRotationControl(argument);
                    break;
                    
                case 导弹状态.引爆激发:
                    处理引爆激发状态();
                    break;
                    
                case 导弹状态.引爆最终:
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
        private void 更新目标状态(Vector3D 当前目标位置, string argument = "")
        {
            bool 有有效目标 = !当前目标位置.Equals(Vector3D.NegativeInfinity);
            bool 目标位置已改变 = !当前目标位置.Equals(上次目标位置);
            Echo($"存在目标：{有有效目标}, 位置更新：{目标位置已改变}");

            // 处理测试命令 - 进入测试状态
            if (!string.IsNullOrEmpty(argument))
            {
                if (argument.StartsWith("test"))
                {
                    当前状态 = 导弹状态.测试状态;
                }
                else if (argument == "normal" || argument == "exit")
                {
                    // 退出测试状态，返回搜索状态
                    当前状态 = 导弹状态.搜索目标;
                }
                else if (argument.ToLower() == "detonate")
                {
                    // 直接引爆命令
                    当前状态 = 导弹状态.引爆激发;
                    return;
                }
            }

            // 检查引爆条件（传感器和距离触发）
            if (引爆系统可用 && (当前状态 == 导弹状态.跟踪目标 || 当前状态 == 导弹状态.预测制导))
            {
                bool 传感器触发 = 检查传感器触发();
                bool 距离触发 = 检查距离触发();
                Echo($"引爆触发: 传感器={传感器触发}, 距离={距离触发}");
                if (传感器触发 || 距离触发)
                {
                    当前状态 = 导弹状态.引爆激发;
                    return;
                }
            }

            switch (当前状态)
            {
                case 导弹状态.搜索目标:
                    if (有有效目标)
                    {
                        // 搜索到第一个目标，切换到跟踪状态
                        当前状态 = 导弹状态.跟踪目标;
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                        战斗块.UpdateTargetInterval = 5; // 降低战斗块更新目标间隔
                    }
                    break;

                case 导弹状态.跟踪目标:
                    if (!有有效目标)
                    {
                        // 目标完全丢失，立即切换到预测制导
                        当前状态 = 导弹状态.预测制导;
                        预测开始帧数 = 更新计数器;
                    }
                    else if (目标位置已改变)
                    {
                        // 目标位置更新，继续跟踪
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                    }
                    else
                    {
                        // 目标存在但位置未更新，检查超时
                        if (更新计数器 - 上次目标更新时间 <= 目标丢失超时帧数)
                        {
                            当前状态 = 导弹状态.预测制导;
                            预测开始帧数 = 更新计数器;
                        }
                    }
                    break;

                case 导弹状态.预测制导:
                    if (有有效目标 && 目标位置已改变)
                    {
                        // 目标重新出现且位置更新，切换回跟踪状态
                        当前状态 = 导弹状态.跟踪目标;
                        上次目标位置 = 当前目标位置;
                        上次目标更新时间 = 更新计数器;
                    }
                    else if (更新计数器 - 预测开始帧数 > 预测制导持续帧数)
                    {
                        // 预测制导超时，返回搜索状态
                        当前状态 = 导弹状态.搜索目标;
                    }
                    break;

                case 导弹状态.测试状态:
                    break;
                    
                case 导弹状态.引爆激发:
                case 导弹状态.引爆最终:
                    // 引爆状态由对应的处理方法管理状态转换
                    break;
            }
        }

        /// <summary>
        /// 处理搜索状态
        /// </summary>
        private void 处理搜索状态()
        {
            Echo("状态: 搜索目标中...");
            if(目标跟踪器.GetHistoryCount() > 0 || 更新计数器 < 60)
            {
                战斗块.UpdateTargetInterval = 0; // 恢复战斗块更新目标间隔          
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
            目标跟踪器.ClearHistory();// 清空目标历史
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
            if (控制器 != null && 目标跟踪器.GetHistoryCount() > 0)
            {
                // 使用预测位置进行制导
                long 预测时间毫秒 = (long)Math.Round((更新计数器 - 上次目标更新时间) * 时间常数 * 1000);
                var 预测目标 = 目标跟踪器.PredictFutureTargetInfo(预测时间毫秒);

                SimpleTargetInfo 目标信息 = new SimpleTargetInfo(预测目标.Position, 预测目标.Velocity, 预测目标.TimeStamp);
                Vector3D 制导命令 = 比例导航制导(控制器, 目标信息);
                应用制导命令(制导命令, 控制器);
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
                    if (组.Name.StartsWith(组名))
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
                    Echo($"未找到包含当前可编程块的、以'{组名}'开头的方块组");
                    return false;
                }
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

            // 初始化引爆系统（可选）
            初始化引爆系统(方块组);

            bool 初始化完整 = 配置AI组件(方块组) && 控制器 != null && 推进器列表.Count > 0 && 陀螺仪列表.Count > 0;

            if (初始化完整)
            {
                Echo($"硬件初始化完成: 控制器={控制器?.CustomName}, 推进器={推进器列表.Count}, 陀螺仪={陀螺仪列表.Count}");
                if (引爆系统可用)
                {
                    Echo($"引爆系统: 传感器={传感器列表.Count}, 激发雷管={激发雷管组.Count}, 引爆雷管={引爆雷管组.Count}");
                }
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
        /// 配置AI组件
        /// </summary>
        private bool 配置AI组件(IMyBlockGroup 方块组)
        {
            if (方块组 == null)
            {
                Echo($"未找到组名为{组名}的方块组。");
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
                飞行块.SpeedLimit = 200f;       // 最大速度
                飞行块.AlignToPGravity = false; // 不与重力对齐
                飞行块.Enabled = false;         // 关闭方块
                飞行块.ApplyAction("ActivateBehavior_On");
                Echo($"配置飞行块: {飞行块.CustomName}");
            }

            // 配置战斗AI
            if (战斗块 != null)
            {
                战斗块.TargetPriority = OffensiveCombatTargetPriority.Largest;
                战斗块.UpdateTargetInterval = 0;
                战斗块.Enabled = true;
                战斗块.SelectedAttackPattern = 3; // 拦截模式
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
            double 导弹速度长度 = 导弹速度.Length();
            if (距离 < 最小向量长度)
                return Vector3D.Zero; // 防止除零错误

            Vector3D 视线单位向量 = 导弹到目标 / 距离;

            // ----- 步骤3: 计算相对速度 -----
            Vector3D 相对速度 = 目标速度 - 导弹速度;

            // ----- 步骤4: 计算视线角速率 -----
            // 视线角速度 = (导弹到目标 × 相对速度) / |导弹到目标|²
            Vector3D 视线角速度 = Vector3D.Cross(导弹到目标, 相对速度) /
                            Math.Max(导弹到目标.LengthSquared(), 1); // 防止除零

            // ----- 步骤5: 计算真比例导航基础控制量 -----
            // 计算基础比例导航加速度 (PN核心公式)
            Vector3D 导弹速度单位向量;
            if (导弹速度长度 < 最小向量长度)
            {
                // 当导弹速度过小时，使用默认方向
                导弹速度单位向量 = new Vector3D(1, 1, 1).Normalized();
            }
            else
            {
                // 正常情况下使用导弹速度的单位向量
                导弹速度单位向量 = 导弹速度 / 导弹速度长度;
            }

            double 相对速度大小 = 相对速度.Length();
            Vector3D 比例导航加速度;
            if (滑翔模式)
            {
                比例导航加速度 = 导航常数 * 相对速度大小 * Vector3D.Cross(视线角速度, 导弹速度单位向量);
            }
            else
            {
                比例导航加速度 = 导航常数 * 相对速度大小 * Vector3D.Cross(视线角速度, 视线单位向量);
            }

            // ----- 步骤6: 计算直接接近分量 -----
            if (导弹到目标.Length() > 最小向量长度)
            {
                // 使用无界接近速度计算
                Vector3D 接近加速度 = 计算接近加速度(导弹到目标, 比例导航加速度, 控制器);

                // 合并到接近速度修正中
                比例导航加速度 += 接近加速度;
            }

            // ----- 步骤7: 添加重力补偿 -----
            Vector3D 重力加速度 = 控制器.GetNaturalGravity();
            
            // ----- 步骤8: 合成最终加速度命令 -----
            // 最终命令 = 制导加速度 + 重力补偿
            Vector3D 最终加速度命令 = 比例导航加速度 - 重力加速度;
            
            // 输出诊断信息
            Echo($"[比例导航] 目标距离: {距离:n1} m");
            Echo($"[比例导航] 加速度指令: {比例导航加速度.Length():n1} m/s²");
            // Echo($"[比例导航] 视线角速率: {角速度.Length():n3} rad/s");
            // Echo($"[比例导航] 目标加速度：{目标加速度.Length():n1} m/s^2");

            return 最终加速度命令;
        }

        /// <summary>
        /// 基于推进器能力的接近加速度计算
        /// </summary>
        /// <param name="视线">导弹到目标的视线向量</param>
        /// <param name="比例导航加速度">比例导航计算的加速度，用于构建直角三角形</param>
        /// <param name="控制器">飞船控制器，用于获取质量和坐标变换</param>
        /// <returns>修正后的接近加速度向量</returns>
        private Vector3D 计算接近加速度(Vector3D 视线, Vector3D 比例导航加速度, IMyShipController 控制器)
        {
            Vector3D 视线单位向量 = Vector3D.Normalize(视线);
            
            // ----- 步骤1: 将比例导航加速度投影到本地坐标系 -----
            double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;
            Vector3D 本地比例导航加速度 = Vector3D.TransformNormal(比例导航加速度, MatrixD.Transpose(控制器.WorldMatrix));
            
            // ----- 步骤2: 根据符号确定对应轴向推进器，计算本地加速度向量 -----
            Vector3D 本地最大加速度向量 = Vector3D.Zero;
        
            // 负方向，使用ZN推进器，加上负号
            本地最大加速度向量.Z = -(轴向最大推力["ZN"] / 飞船质量);
            if(本地比例导航加速度.X < 0)
            {
                // 负X方向，使用XN推进器，加上负号
                本地最大加速度向量.X = -(轴向最大推力["XN"] / 飞船质量);
            }
            else if(本地比例导航加速度.X > 0)
            {
                // 正X方向，使用XP推进器
                本地最大加速度向量.X = (轴向最大推力["XP"] / 飞船质量);
            }
            if(本地比例导航加速度.Y < 0)
            {
                // 负Y方向，使用YN推进器，加上负号
                本地最大加速度向量.Y = -(轴向最大推力["YN"] / 飞船质量);
            }
            else if(本地比例导航加速度.Y > 0)
            {
                // 正Y方向，使用YP推进器
                本地最大加速度向量.Y = (轴向最大推力["YP"] / 飞船质量);
            }
            // ----- 步骤2.5: 将本地最大加速度向量转换到世界坐标系 -----
            Vector3D 世界最大加速度向量 = Vector3D.TransformNormal(本地最大加速度向量, 控制器.WorldMatrix);
            
            // ----- 步骤3: 在世界坐标系中计算直角三角形的径向分量 -----
            double 世界最大加速度模长平方 = 世界最大加速度向量.LengthSquared();
            double 比例导航加速度模长平方 = 比例导航加速度.LengthSquared();
            
            // 计算差值
            double 径向分量平方 = 世界最大加速度模长平方 - 比例导航加速度模长平方;

            // 输出诊断信息
            Echo($"[推进器] 导弹主过载: {Math.Sqrt(世界最大加速度模长平方):n1} m/s²");   
            导航常数 = Math.Sqrt(世界最大加速度模长平方) / 10.0;
            导航常数 = Math.Min(Math.Max(导航常数, 2), 6); // 确保常数在合理范围内
            Echo($"[推进器] 比例导航常数: {导航常数:n1}");
            // 如果差值为负，说明推进器能力不足，返回零向量
            if (径向分量平方 <= 0)
            {
                return 最小接近加速度 * 视线单位向量;
            }
            
            // ----- 步骤4: 计算接近加速度 -----
            double 径向加速度大小 = Math.Sqrt(径向分量平方);
            
            // 确保最小接近加速度
            径向加速度大小 = Math.Max(径向加速度大小, 最小接近加速度);
            
            // 应用视线方向
            Vector3D 接近加速度 = 径向加速度大小 * 视线单位向量;
    
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
            Echo($"[陀螺仪] 角度误差: {角度误差 * 180.0 / Math.PI:n1} 度");
            return 目标角度PYR;
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
            if (角度误差大小 < 角度误差阈值)
            {
                // 角度误差很小，停止陀螺仪以减少抖动
                foreach (var 陀螺仪 in 陀螺仪列表)
                {
                    陀螺仪.Pitch = 0f;
                    陀螺仪.Yaw = 0f;
                    陀螺仪.Roll = 0f;
                    陀螺仪.GyroOverride = true; // 保持覆盖状态但输出为零
                }
                return;
            }

            // 仅在指定更新间隔执行，减少过度控制
            if (更新计数器 % 陀螺仪更新间隔 != 0)
                return;
            // ----------------- 外环：角度误差 → 期望角速度 (世界坐标系) -----------------
            // 使用PD控制器将角度误差转换为期望角速度
            double 期望俯仰速率 = 俯仰外环.GetOutput(目标角度PYR.X);
            double 期望偏航速率 = 偏航外环.GetOutput(目标角度PYR.Y);
            double 期望横滚速率 = 横滚外环.GetOutput(目标角度PYR.Z);

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
                推进器已分类 = true;
            }

            // // 将所有推进器的推力覆盖设置为0（清除旧指令）
            // foreach (var 推进器 in 推进器列表)
            // {
            //     推进器.ThrustOverridePercentage = 0f;
            // }

            // 针对每个轴应用推力控制
            应用轴向推力(本地加速度.X, "XP", "XN", 飞船质量);
            应用轴向推力(本地加速度.Y, "YP", "YN", 飞船质量);
            应用轴向推力(本地加速度.Z, "ZP", "ZN", 飞船质量);
        }


        /// <summary>
        /// 将推进器按方向分类并保存各轴向最大推力
        /// </summary>
        private void 分类推进器(IMyShipController 控制器)
        {
            // 清空所有组中的推进器和推力记录
            foreach (var key in 推进器方向组.Keys.ToList())
            {
                推进器方向组[key].Clear();
                轴向最大推力[key] = 0;
            }

            // 遍历所有推进器，根据其局部推进方向归类
            const double 方向容差 = 0.8; // 方向余弦相似度阈值
            foreach (var 推进器 in 推进器列表)
            {
                // 将推进器的世界方向转换到飞船本地坐标系
                Vector3D 本地推力方向 = Vector3D.TransformNormal(推进器.WorldMatrix.Forward, MatrixD.Transpose(控制器.WorldMatrix));

                // 根据推进方向分类并累加推力
                string 分类轴向 = null;
                if (Vector3D.Dot(本地推力方向, -Vector3D.UnitX) > 方向容差)
                    分类轴向 = "XP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitX) > 方向容差)
                    分类轴向 = "XN";
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitY) > 方向容差)
                    分类轴向 = "YP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitY) > 方向容差)
                    分类轴向 = "YN";
                else if (Vector3D.Dot(本地推力方向, -Vector3D.UnitZ) > 方向容差)
                    分类轴向 = "ZP";
                else if (Vector3D.Dot(本地推力方向, Vector3D.UnitZ) > 方向容差)
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
                Echo($"[推进器] 轴向 {激活组名}推力缺口");
            }

            // 关闭剩余的推进器
            for (int i = 当前索引; i < 推进器组.Count; i++)
            {
                推进器组[i].ThrustOverride = 0f;
            }
        }
        
        /// <summary>
        /// 测试陀螺仪控制
        /// </summary>
        private void TestRotationControl(string argument)
        {
            // 根据参数更新目标到跟踪器中
            if (!string.IsNullOrEmpty(argument))
            {
                long 当前时间戳 = (long)Math.Round(更新计数器 * 时间常数 * 1000);
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
                }
            }

            // 统一使用目标跟踪器的最新位置进行控制（在所有分支之外）
            if (目标跟踪器.GetHistoryCount() > 0 && 控制器 != null)
            {
                Vector3D 最新目标位置 = 目标跟踪器.GetLatestPosition();
                Vector3D 导弹位置 = 控制器.GetPosition();
                Vector3D 到目标向量 = 最新目标位置 - 导弹位置;

                if (到目标向量.Length() > 最小向量长度)
                {
                    Vector3D 目标角度 = 计算目标角度(到目标向量 * 10, 控制器);
                    应用陀螺仪控制(目标角度);
                    Echo($"测试状态控制中");
                    Echo($"目标位置: {最新目标位置}");
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
            if (上次目标位置.Equals(Vector3D.Zero)) return false;
            
            Vector3D 当前位置 = 控制器.GetPosition();
            double 距离 = Vector3D.Distance(当前位置, 上次目标位置);
            
            return 距离 <= 引爆距离阈值;
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
            等待二阶段引爆 = true;
            当前状态 = 导弹状态.引爆最终;
        }

        /// <summary>
        /// 处理引爆最终状态
        /// </summary>
        private void 处理引爆最终状态()
        {
            Echo("状态: 引爆二阶段...");
            
            // 如果上一帧已触发激发雷管组，本帧触发引爆雷管组
            if (等待二阶段引爆)
            {
                foreach (var 弹头 in 引爆雷管组)
                {
                    弹头.IsArmed = true;
                    弹头.Detonate();
                }
                等待二阶段引爆 = false;
                
                // 引爆完成，返回搜索状态
                当前状态 = 导弹状态.搜索目标;
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