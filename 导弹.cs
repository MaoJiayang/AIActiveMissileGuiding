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
        初始化状态,
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
        private 导弹状态机 _当前状态;
        public 导弹状态机 当前状态
        {
            get { return _当前状态; }
            set
            {
                上次状态 = _当前状态;
                _当前状态 = value;
            }
        }
        public 导弹状态机 上次状态 { get; private set; }
        public Vector3D 上次真实目标位置;// AI块的“每帧”
        public bool 角度误差在容忍范围内;

        // 计算密集型，每隔一段时间（动力系统更新间隔）更新或按需更新
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
            导弹最大过载 = 114514;
            // 导弹世界力学加速度 = Vector3D.Zero;
            角度误差在容忍范围内 = false;
            导航常数 = 114514.0; // 默认值，会在初始化时设置
            等待二阶段引爆 = false;
            导弹状态信息 = new StringBuilder();
            上次预测目标位置 = Vector3D.Zero;
        }

        public StringBuilder 获取导弹诊断信息()
        {
            导弹状态信息.Clear();
            // 导弹状态信息.AppendLine($"[导弹状态] 当前状态: {当前状态转文字()}");
            // 导弹状态信息.AppendLine($"[导弹状态] 力学加速度: {导弹世界力学加速度.Length():F2}");
            导弹状态信息.AppendLine($"导航常数: {导航常数:F2}");
            导弹状态信息.AppendLine($"可用过载: {导弹最大过载:F2}");
            导弹状态信息.AppendLine($"需用过载: {制导命令.Length():F2}");
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

    // #region 导弹硬件
    // public class 导弹硬件
    // {
    //     #region 状态变量
    //     public 导弹状态量 导弹状态信息;
    //     public 参数管理器 参数们;
    //     private long 上次目标更新时间 = 0;
    //     private int 预测开始帧数 = 0;
    //     private int 热发射开始帧数 = 0;
    //     private int 更新计数器 = 0;
    //     private short 初始化计数器 = 0;
    //     MovingAverageQueue<Vector3D> 外源扰动缓存 = new MovingAverageQueue<Vector3D>(
    //         20,
    //         (a, b) => a + b,
    //         (a, b) => a - b,
    //         (a, n) => a / n   // VRageMath.Vector3D 已重载 / double
    //     );
    //     MovingAverageQueue<Vector3D> 敌加速度缓存 = new MovingAverageQueue<Vector3D>(
    //         5,
    //         (a, b) => a + b,
    //         (a, b) => a - b,
    //         (a, n) => a / n   // VRageMath.Vector3D 已重载 / double
    //     );

    //     #endregion

    //     #region 硬件组件
    //     private bool 已经初始化 = false;
    //     private IMyBlockGroup 方块组 = null;
    //     // 控制组件
    //     private IMyControllerCompat 控制器;
    //     private List<IMyThrust> 推进器列表 = new List<IMyThrust>();
    //     private List<IMyGyro> 陀螺仪列表 = new List<IMyGyro>();
    //     private List<IMyGravityGeneratorBase> 重力发生器列表 = new List<IMyGravityGeneratorBase>();
    //     // 引爆系统组件（可选）
    //     private List<IMySensorBlock> 传感器列表 = new List<IMySensorBlock>();
    //     private List<IMyWarhead> 激发雷管组 = new List<IMyWarhead>();
    //     private List<IMyWarhead> 引爆雷管组 = new List<IMyWarhead>();
    //     private bool 引爆系统可用 = false;

    //     // 挂架系统组件（可选）
    //     private List<IMyShipConnector> 连接器列表 = new List<IMyShipConnector>();
    //     private List<IMyShipMergeBlock> 合并方块列表 = new List<IMyShipMergeBlock>();
    //     private List<IMyMotorStator> 转子列表 = new List<IMyMotorStator>();
    //     private bool 挂架系统可用 = false;

    //     // 氢气罐系统组件（可选）
    //     private List<IMyGasTank> 氢气罐列表 = new List<IMyGasTank>();
    //     private bool 氢气罐系统可用 = false;

    //     // 分离推进器（可选）
    //     private List<IMyThrust> 分离推进器列表 = new List<IMyThrust>();

    //     // 推进器分组映射表
    //     private Dictionary<string, List<IMyThrust>> 推进器推力方向组 = new Dictionary<string, List<IMyThrust>>
    //     {
    //         {"XP", new List<IMyThrust>()}, {"XN", new List<IMyThrust>()},
    //         {"YP", new List<IMyThrust>()}, {"YN", new List<IMyThrust>()},
    //         {"ZP", new List<IMyThrust>()}, {"ZN", new List<IMyThrust>()}
    //     };
    //     private Dictionary<string, double> 轴向最大推力 = new Dictionary<string, double>
    //     {
    //         {"XP", 0}, {"XN", 0}, {"YP", 0}, {"YN", 0}, {"ZP", 0}, {"ZN", 0}
    //     };
    //     private Dictionary<IMyGyro, Vector3D> 陀螺仪各轴点积 = new Dictionary<IMyGyro, Vector3D>();
    //     private bool 推进器已分类 = false;

    //     #endregion

    //     #region PID控制系统

    //     // PID控制器 - 外环(角度误差->期望角速度)
    //     private PID3 外环PID控制器PYR = null;

    //     // PID控制器 - 内环(角速度误差->陀螺仪设定)
    //     private PID3 内环PID控制器PYR = null;

    //     #endregion

    //     #region 飞行控制系统

    //     /// <summary>
    //     /// 控制陀螺仪实现所需转向
    //     /// </summary>
    //     private void 应用陀螺仪控制(Vector3D 目标角度PYR)
    //     {
    //         // 检查角度误差是否在阈值范围内
    //         double 角度误差大小 = 目标角度PYR.Length();
    //         if (角度误差大小 < 参数们.角度误差最小值 && !导弹状态信息.角度误差在容忍范围内)
    //         {
    //             // 角度误差很小，停止陀螺仪以减少抖动
    //             foreach (var 陀螺仪 in 陀螺仪列表)
    //             {
    //                 Vector3D 陀螺仪本地命令 = Vector3D.Zero;
    //                 陀螺仪本地命令 = 加入本地滚转(陀螺仪, 陀螺仪本地命令, 参数们.常驻滚转转速);
    //                 施加本地转速指令(陀螺仪, 陀螺仪本地命令);
    //             }
    //             // 重置所有PID控制器
    //             外环PID控制器PYR.Reset();
    //             内环PID控制器PYR.Reset();
    //             导弹状态信息.角度误差在容忍范围内 = true;
    //             return;
    //         }
    //         导弹状态信息.角度误差在容忍范围内 = false;
    //         // 仅在指定更新间隔执行，减少过度控制
    //         if (更新计数器 % 参数们.动力系统更新间隔 != 0)
    //             return;
    //         // ----------------- 外环：角度误差 → 期望角速度 (世界坐标系) -----------------
    //         // 使用PD控制器将角度误差转换为期望角速度
    //         Vector3D 期望角速度PYR = 外环PID控制器PYR.GetOutput(目标角度PYR);
    //         // ----------------- 内环：角速度误差 → 最终指令 (世界坐标系) -----------------
    //         // 获取飞船当前角速度（单位：弧度/秒），已在世界坐标系下
    //         Vector3D 当前角速度 = 控制器.GetShipVelocities().AngularVelocity;
    //         // 计算各轴角速度误差
    //         Vector3D 速率误差PYR = 期望角速度PYR - 当前角速度;
    //         // 内环PD：将角速度误差转换为最终下发指令
    //         Vector3D 最终旋转命令PYR = 内环PID控制器PYR.GetOutput(速率误差PYR);
    //         // ----------------- 应用到各陀螺仪 -----------------
    //         foreach (var 陀螺仪 in 陀螺仪列表)
    //         {
    //             // 使用陀螺仪世界矩阵将世界坐标的角速度转换为陀螺仪局部坐标系
    //             Vector3D 陀螺仪本地转速命令 = Vector3D.TransformNormal(最终旋转命令PYR, MatrixD.Transpose(陀螺仪.WorldMatrix));
    //             陀螺仪本地转速命令 = 加入本地滚转(陀螺仪, 陀螺仪本地转速命令, 参数们.常驻滚转转速);
    //             施加本地转速指令(陀螺仪, 陀螺仪本地转速命令);
    //         }
    //     }

    //     /// <summary>
    //     /// 将本地指令实际应用到陀螺仪，带懒惰更新
    //     /// 仅在指令有变化时更新陀螺仪的转速
    //     /// </summary>
    //     private void 施加本地转速指令(IMyGyro 陀螺仪, Vector3D 本地指令)
    //     {
    //         陀螺仪.GyroOverride = true;
    //         // 注意陀螺仪的轴向定义与游戏世界坐标系的差异，需要取负
    //         if (陀螺仪命令需更新(陀螺仪.Pitch, -(float)本地指令.X)) 陀螺仪.Pitch = -(float)本地指令.X;
    //         if (陀螺仪命令需更新(陀螺仪.Yaw, -(float)本地指令.Y)) 陀螺仪.Yaw = -(float)本地指令.Y;
    //         if (陀螺仪命令需更新(陀螺仪.Roll, -(float)本地指令.Z)) 陀螺仪.Roll = -(float)本地指令.Z;
    //     }

    //     /// <summary>
    //     /// 找出滚转轴，并返回陀螺仪本地命令加上正确的滚转向量
    //     /// </summary>
    //     /// <param name="陀螺仪">陀螺仪块</param>
    //     /// <param name="陀螺仪本地命令">陀螺仪的本地命令向量</param>
    //     /// <param name="弧度每秒">滚转速度（弧度/秒）包含方向</param>
    //     /// <returns>应施加的本地命令向量 包含滚转轴的命令</returns>
    //     private Vector3D 加入本地滚转(IMyGyro 陀螺仪, Vector3D 陀螺仪本地命令, double 弧度每秒 = 0.0)
    //     {
    //         Vector3D 该陀螺仪点积 = 陀螺仪对轴(陀螺仪, 控制器);
    //         // if (弧度每秒 < 参数们.最小向量长度)
    //         // {
    //         //     return 陀螺仪本地命令; // 不需要滚转
    //         // }
    //         // 检查各轴与导弹Z轴的点积，判断是否同向
    //         double X轴点积 = Math.Abs(该陀螺仪点积.X);
    //         double Y轴点积 = Math.Abs(该陀螺仪点积.Y);
    //         double Z轴点积 = Math.Abs(该陀螺仪点积.Z);

    //         if (X轴点积 > 参数们.推进器方向容差 && X轴点积 >= Y轴点积 && X轴点积 >= Z轴点积)
    //         {
    //             陀螺仪本地命令.X = Math.Sign(该陀螺仪点积.X) * 弧度每秒;
    //         }
    //         else if (Y轴点积 > 参数们.推进器方向容差 && Y轴点积 >= X轴点积 && Y轴点积 >= Z轴点积)
    //         {
    //             陀螺仪本地命令.Y = Math.Sign(该陀螺仪点积.Y) * 弧度每秒;
    //         }
    //         else if (Z轴点积 > 参数们.推进器方向容差 && Z轴点积 >= X轴点积 && Z轴点积 >= Y轴点积)
    //         {
    //             陀螺仪本地命令.Z = -Math.Sign(该陀螺仪点积.Z) * 弧度每秒;
    //             // 备注：已知se旋转绕负轴，所以指令传入的时候已经全部取负
    //             // 又因为，该方法一般都用于直接覆盖已经计算好的陀螺仪本地命令，
    //             // 所以这里需要根据转速再取一次负
    //         }
    //         return 陀螺仪本地命令;
    //     }

    //     /// <summary>
    //     /// 计算陀螺仪各轴与导弹Z轴的点积并缓存
    //     /// 如果有缓存则直接取出
    //     /// 目的：找出滚转轴
    //     /// </summary>
    //     private Vector3D 陀螺仪对轴(IMyGyro 陀螺仪, IMyControllerCompat 控制器)
    //     {
    //         // 获取导弹Z轴方向（控制器的前进方向）
    //         Vector3D 导弹Z轴方向 = 控制器.WorldMatrix.Forward;

    //         Vector3D 该陀螺仪点积;
    //         if (!陀螺仪各轴点积.TryGetValue(陀螺仪, out 该陀螺仪点积))
    //         {
    //             // 获取陀螺仪的三个本地轴在世界坐标系中的方向
    //             Vector3D 陀螺仪X轴世界方向 = 陀螺仪.WorldMatrix.Right;    // 对应本地X轴（Pitch）
    //             Vector3D 陀螺仪Y轴世界方向 = 陀螺仪.WorldMatrix.Up;       // 对应本地Y轴（Yaw）
    //             Vector3D 陀螺仪Z轴世界方向 = 陀螺仪.WorldMatrix.Forward;   // 对应本地Z轴（Roll）
    //             该陀螺仪点积 = new Vector3D(
    //                 Vector3D.Dot(陀螺仪X轴世界方向, 导弹Z轴方向),
    //                 Vector3D.Dot(陀螺仪Y轴世界方向, 导弹Z轴方向),
    //                 Vector3D.Dot(陀螺仪Z轴世界方向, 导弹Z轴方向)
    //             );
    //             陀螺仪各轴点积[陀螺仪] = 该陀螺仪点积;
    //         }
    //         return 该陀螺仪点积;
    //     }

    //     /// <summary>
    //     /// 判断是否需要更新陀螺仪命令
    //     /// 如果当前值已经接近最大值，且新命令在同方向且更大，则不更新
    //     /// 如果差异很小，也不更新
    //     /// 目的：减少陀螺仪频繁更新导致出力不足
    //     /// </summary>
    //     private bool 陀螺仪命令需更新(double 当前值, double 新值, double 容差 = 1e-3)
    //     {

    //         if (Math.Abs(当前值) > 导弹状态信息.陀螺仪最高转速 - 容差)
    //         {
    //             // 当前值接近最大值
    //             if (Math.Sign(当前值) == Math.Sign(新值 + 参数们.最小向量长度) && Math.Abs(新值) >= Math.Abs(当前值))
    //             {
    //                 return false; // 不更新
    //             }
    //         }

    //         // 如果差异很小，也不更新
    //         if (Math.Abs(当前值 - 新值) < 容差)
    //         {
    //             return false;
    //         }
    //         return true;
    //     }

    //     /// <summary>
    //     /// 控制推进器产生所需加速度
    //     /// </summary>
    //     private void 控制推进器(Vector3D 绝对加速度, IMyControllerCompat 控制器)
    //     {
    //         if (更新计数器 % 参数们.动力系统更新间隔 != 0)
    //             return;
    //         // 获取飞船质量（单位：kg）
    //         double 飞船质量 = 控制器.CalculateShipMass().PhysicalMass;

    //         // 将绝对加速度转换为飞船本地坐标系（单位：m/s²）
    //         Vector3D 本地加速度 = Vector3D.TransformNormal(绝对加速度, MatrixD.Transpose(控制器.WorldMatrix));

    //         // 仅在第一次调用或推进器列表发生变化时进行分类
    //         if (!推进器已分类 || 更新计数器 % 参数们.推进器重新分类间隔 == 0)
    //         {
    //             分类推进器(控制器);
    //             推进器已分类 = true;
    //         }

    //         // 针对每个轴应用推力控制
    //         Vector3D 实际应用推力;
    //         实际应用推力.X = 应用轴向推力(本地加速度.X, "XP", "XN", 飞船质量);
    //         实际应用推力.Y = 应用轴向推力(本地加速度.Y, "YP", "YN", 飞船质量);
    //         实际应用推力.Z = 应用轴向推力(本地加速度.Z, "ZP", "ZN", 飞船质量);

    //         // // 将本地加速度转换回世界坐标系
    //         // 导弹状态信息.导弹世界力学加速度 = Vector3D.TransformNormal(实际应用推力 / 飞船质量, 控制器.WorldMatrix);
    //         // Echo($"[推进器] 动力学加速度夹角 {Vector3D.Angle(导弹状态信息.导弹世界力学加速度,控制器.GetAcceleration()):n2}");

    //     }


    //     /// <summary>
    //     /// 将推进器按方向分类并保存各轴向最大推力
    //     /// </summary>
    //     private void 分类推进器(IMyControllerCompat 控制器)
    //     {
    //         // 清空所有组中的推进器和推力记录
    //         foreach (var key in 推进器推力方向组.Keys.ToList())
    //         {
    //             推进器推力方向组[key].Clear();
    //             轴向最大推力[key] = 0;
    //         }

    //         // 遍历所有推进器，根据其局部推进方向归类
    //         foreach (var 推进器 in 推进器列表)
    //         {
    //             // 将推进器的推力方向（正面是喷口，反方向是推力方向）转换到飞船本地坐标系
    //             Vector3D 推进器推力方向 = 推进器.WorldMatrix.Backward;
    //             // Vector3D 本地推力方向 = Vector3D.TransformNormal(推进器.WorldMatrix.Backward, MatrixD.Transpose(控制器.WorldMatrix));
    //             // 根据推进方向分类并累加推力
    //             string 分类轴向 = null;
    //             if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Right) > 参数们.推进器方向容差)
    //                 分类轴向 = "XP";
    //             else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Right) > 参数们.推进器方向容差)
    //                 分类轴向 = "XN";
    //             else if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Up) > 参数们.推进器方向容差)
    //                 分类轴向 = "YP";
    //             else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Up) > 参数们.推进器方向容差)
    //                 分类轴向 = "YN";
    //             else if (Vector3D.Dot(推进器推力方向, 控制器.WorldMatrix.Forward) > 参数们.推进器方向容差)
    //                 分类轴向 = "ZN";
    //             else if (Vector3D.Dot(推进器推力方向, -控制器.WorldMatrix.Forward) > 参数们.推进器方向容差)
    //                 分类轴向 = "ZP";
    //             Echo($"[推进器] 推进器 {推进器.CustomName} 分类为 {分类轴向}");
    //             if (分类轴向 != null)
    //             {
    //                 推进器推力方向组[分类轴向].Add(推进器);
    //                 轴向最大推力[分类轴向] += 推进器.MaxEffectiveThrust;
    //             }
    //         }

    //         // 对每个方向组内的推进器按最大有效推力从大到小排序
    //         foreach (var 组 in 推进器推力方向组.Values)
    //         {
    //             组.Sort((a, b) => b.MaxEffectiveThrust.CompareTo(a.MaxEffectiveThrust));
    //         }

    //         推进器已分类 = true;

    //     }

    //     /// <summary>
    //     /// 为指定轴应用推力控制
    //     /// </summary>
    //     private double 应用轴向推力(double 本地加速度, string 正方向组, string 负方向组, double 质量)
    //     {
    //         // 计算所需力（牛顿）
    //         double 需要的力 = 质量 * Math.Abs(本地加速度);

    //         // 确定应该使用哪个方向的推进器组
    //         string 激活组名 = 本地加速度 >= 0 ? 正方向组 : 负方向组;
    //         string 关闭组名 = 本地加速度 >= 0 ? 负方向组 : 正方向组;

    //         // 首先关闭反方向的推进器组
    //         List<IMyThrust> 关闭组;
    //         if (推进器推力方向组.TryGetValue(关闭组名, out 关闭组))
    //         {
    //             foreach (var 推进器 in 关闭组)
    //             {
    //                 推进器.ThrustOverride = 0f;
    //             }
    //         }
    //         // 获取对应方向的推进器组（已排序）
    //         List<IMyThrust> 推进器组;
    //         if (!推进器推力方向组.TryGetValue(激活组名, out 推进器组) || 推进器组.Count == 0)
    //         {
    //             return 0.0; // 该方向没有推进器
    //         }

    //         // 推进器已在分类时排序，无需重新排序
    //         // 追踪剩余需要分配的推力和已分配的推力
    //         double 剩余力 = 需要的力;
    //         double 总分配力 = 0;

    //         // 将推进器按照相似推力分组处理
    //         int 当前索引 = 0;
    //         while (当前索引 < 推进器组.Count && 剩余力 > 0.001)
    //         {
    //             // 当前组的参考推力值
    //             double 参考推力 = 推进器组[当前索引].MaxEffectiveThrust;

    //             // 查找相似推力的推进器并计算组总推力
    //             int 组大小 = 0;
    //             double 组最大推力 = 0;
    //             int i = 当前索引;

    //             // 识别具有相似最大推力的推进器组
    //             while (i < 推进器组.Count &&
    //                 Math.Abs(推进器组[i].MaxEffectiveThrust - 参考推力) < 0.001)
    //             {
    //                 组最大推力 += 推进器组[i].MaxEffectiveThrust;
    //                 组大小++;
    //                 i++;
    //             }

    //             // 确定分配给该组的推力
    //             double 分配给组的推力 = Math.Min(组最大推力, 剩余力);

    //             // 在组内均匀分配推力
    //             double 每个推进器推力 = 分配给组的推力 / 组大小;
    //             for (int j = 当前索引; j < 当前索引 + 组大小; j++)
    //             {
    //                 double 实际分配推力 = Math.Min(每个推进器推力, 推进器组[j].MaxEffectiveThrust);
    //                 推进器组[j].ThrustOverride = (float)实际分配推力;
    //                 剩余力 -= 实际分配推力;
    //                 总分配力 += 实际分配推力;
    //             }

    //             // 移动到下一组推进器
    //             当前索引 = i;
    //         }
    //         // // 输出调试信息
    //         // if (需要的力 > 总分配力 + 0.001)
    //         // {
    //         //     Echo($"[推进器] 轴向 {激活组名}推力缺口");
    //         // }
    //         // 关闭剩余的推进器
    //         for (int i = 当前索引; i < 推进器组.Count; i++)
    //         {
    //             推进器组[i].ThrustOverride = 0f;
    //         }
    //         return 本地加速度 > 0 ? 总分配力 : -总分配力;
    //     }

    //     /// <summary>
    //     /// 测试陀螺仪控制
    //     /// </summary>
    //     private void 旋转控制测试(string argument)
    //     {
    //         // 根据参数更新目标到跟踪器中
    //         if (!string.IsNullOrEmpty(argument))
    //         {
    //             Vector3D 测试目标位置 = Vector3D.Zero;
    //             bool 需要更新目标 = false;

    //             if (argument == "test")
    //             {
    //                 // 生成随机向量作为测试目标
    //                 Random random = new Random();
    //                 double x = random.NextDouble() * 2 - 1;  // -1到1之间
    //                 double y = random.NextDouble() * 2 - 1;
    //                 double z = random.NextDouble() * 2 - 1;

    //                 Vector3D 测试加速度命令 = new Vector3D(x, y, z) * 100; // 放大以便观察效果

    //                 if (控制器 != null)
    //                 {
    //                     测试目标位置 = 控制器.GetPosition() + 测试加速度命令.Normalized() * 1000; // 1000米外的测试目标
    //                     需要更新目标 = true;
    //                 }
    //             }
    //             else if (argument == "testbackward")
    //             {
    //                 // 测试前向控制
    //                 if (控制器 != null)
    //                 {
    //                     测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Backward * 1000;
    //                     测试目标位置.X += 1; // 轻微偏移以避免与前向重合
    //                     需要更新目标 = true;
    //                 }
    //             }
    //             else if (argument == "testup")
    //             {
    //                 // 测试上向控制
    //                 if (控制器 != null)
    //                 {
    //                     测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Up * 1000;
    //                     需要更新目标 = true;
    //                 }
    //             }
    //             else if (argument == "testright")
    //             {
    //                 // 测试右向控制
    //                 if (控制器 != null)
    //                 {
    //                     测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Right * 1000;
    //                     需要更新目标 = true;
    //                 }
    //             }
    //             else if (argument == "testforward")
    //             {
    //                 // 测试前向控制
    //                 if (控制器 != null)
    //                 {
    //                     测试目标位置 = 控制器.GetPosition() + 控制器.WorldMatrix.Forward * 1000;
    //                     需要更新目标 = true;
    //                 }
    //             }
    //             else if (argument == "testouter")
    //             {
    //                 // 测试外源扰动方向
    //                 测试目标位置 = 控制器.GetPosition() + 控制器.GetAcceleration() * 114514.0;
    //                 需要更新目标 = true;
    //             }
    //             else if (argument == "stop")
    //             {
    //                 // 停止陀螺仪覆盖
    //                 foreach (var 陀螺仪 in 陀螺仪列表)
    //                 {
    //                     陀螺仪.GyroOverride = false;
    //                 }
    //                 // 停止推进器
    //                 foreach (var 推进器 in 推进器列表)
    //                 {
    //                     推进器.ThrustOverride = 0f;
    //                 }
    //                 return; // 停止命令直接返回，不执行后续控制逻辑
    //             }


    //             // 更新目标到跟踪器
    //             if (需要更新目标)
    //             {
    //                 目标跟踪器.UpdateTarget(测试目标位置, Vector3D.Zero, 当前时间戳ms);
    //                 陀螺仪列表.ForEach(g => g.Enabled = true); // 确保陀螺仪开启
    //             }
    //         }

    //         // 统一使用目标跟踪器的最新位置进行控制（在所有分支之外）
    //         if (目标跟踪器.GetHistoryCount() > 0 && 控制器 != null)
    //         {

    //             Vector3D 最新目标位置 = 目标跟踪器.GetLatestPosition();
    //             Vector3D 导弹位置 = 控制器.GetPosition();
    //             Vector3D 到目标向量 = 最新目标位置 - 导弹位置;
    //             SimpleTargetInfo 假目标 = new SimpleTargetInfo();
    //             假目标.Position = 最新目标位置;
    //             比例导航制导(控制器, 假目标);
    //             if (到目标向量.Length() > 参数们.最小向量长度)
    //             {
    //                 Vector3D 目标角度 = 计算陀螺仪目标角度(到目标向量 * 10, 控制器);
    //                 应用陀螺仪控制(目标角度);
    //                 Echo($"测试状态控制中");
    //                 Echo($"加速与前向夹角: {Vector3D.Angle(控制器.WorldMatrix.Forward, 控制器.GetAcceleration()):n2}");
    //                 Echo($"运动学加速度: {控制器.GetAcceleration().Length():n2}");
    //                 Echo($"自身质量: {控制器.CalculateShipMass().PhysicalMass:n1} kg");
    //                 // Echo($"PID外环参数: P={参数们.外环参数.P系数}, I={参数们.外环参数.I系数}, D={参数们.外环参数.D系数}");
    //                 // Echo($"PID内环参数: P={参数们.内环参数.P系数}, I={参数们.内环参数.I系数}, D={参数们.内环参数.D系数}");
    //             }
    //             else
    //             {
    //                 Echo("目标距离过近，停止控制");
    //             }
    //         }
    //         else
    //         {
    //             Echo("没有可用的目标历史记录进行测试");
    //         }
    //     }

    //     #endregion
    // }
    // #endregion
}