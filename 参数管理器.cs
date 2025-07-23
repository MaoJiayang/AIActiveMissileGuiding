using System;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace IngameScript
{
    /// <summary>
    /// 导弹参数管理器 - 统一管理所有超参数
    /// </summary>
    public class 参数管理器
    {
        #region 制导相关参数

        /// <summary>
        /// 向量最小有效长度
        /// </summary>
        public double 最小向量长度 { get; set; } = 1e-6;

        /// <summary>
        /// 最小接近加速度(m/s)
        /// </summary>
        public double 最小接近加速度 { get; set; } = 2.5;

        /// <summary>
        /// 时间常数(秒)
        /// </summary>
        public double 时间常数 { get; set; } = 1f / 60f;

        /// <summary>
        /// 角度误差下限，小于此值视为对准
        /// </summary>
        public double 角度误差最小值 { get; set; } = Math.PI / 180.0 * 0.2;

        /// <summary>
        /// 导航常数初始值
        /// </summary>
        public double 导航常数初始值 { get; set; } = 3;

        /// <summary>
        /// 导航常数最小值
        /// </summary>
        public double 导航常数最小值 { get; set; } = 3;

        /// <summary>
        /// 导航常数最大值
        /// </summary>
        public double 导航常数最大值 { get; set; } = 15;
        /// <summary>
        /// 是否启用攻击角度约束
        /// </summary>
        public bool 启用攻击角度约束 { get; set; } = true;

        /// <summary>
        ///  补偿项失效距离(米)
        /// 当目标距离小于此值时，补偿项将不再生效
        /// </summary>
        public double 补偿项失效距离 { get; set; } = 100.0;

        #endregion

        #region 引爆相关参数

        /// <summary>
        /// 接近引爆距离阈值(米)
        /// </summary>
        public double 引爆距离阈值 { get; set; } = 5.0;

        #endregion

        #region 热发射阶段相关参数

        /// <summary>
        /// 热发射阶段持续时间(帧数，2秒)
        /// </summary>
        public int 热发射持续帧数 { get; set; } = 120;

        /// <summary>
        /// 分离推进器识别名称
        /// </summary>
        public string 分离推进器名称 { get; set; } = "分离";

        #endregion

        #region 状态切换时间参数

        /// <summary>
        /// 陀螺仪(现在是动力系统）更新间隔(ticks)
        /// </summary>
        public int 陀螺仪更新间隔 { get; set; } = 5;

        /// <summary>
        /// 推进器重新分类间隔(ticks)
        /// </summary>
        public int 推进器重新分类间隔 { get; set; } = 180;

        /// <summary>
        /// 目标位置不变最大帧数(帧数)
        /// 在这段时间内目标位置不更新视为目标仍存在
        /// </summary>
        public int 目标位置不变最大帧数 { get; set; } = 180;

        /// <summary>
        /// 丢失目标后，预测制导持续时间(帧数)
        /// </summary>
        public int 预测制导持续帧数 { get; set; } = 300;

        /// <summary>
        /// 重新初始化方块的间隔(帧数)
        /// </summary>
        public int 方块更新间隔 { get; set; } = 12000;

        #endregion

        #region PID控制器参数

        /// <summary>
        /// 外环PID参数结构
        /// </summary>
        public class PID参数
        {
            public double P系数 { get; set; }
            public double I系数 { get; set; }
            public double D系数 { get; set; }

            public PID参数(double p, double i, double d)
            {
                P系数 = p;
                I系数 = i;
                D系数 = d;
            }
        }

        /// <summary>
        /// 外环PID参数
        /// </summary>
        public PID参数 外环参数 { get; set; } = new PID参数(5, 0, 0);

        /// <summary>
        /// 内环PID参数
        /// </summary>
        public PID参数 内环参数 { get; set; } = new PID参数(21, 0.01, 0.9);

        #endregion

        #region 目标跟踪器参数

        /// <summary>
        /// 目标历史记录最大长度
        /// </summary>
        public int 目标历史最大长度 { get; set; } = 4;

        #endregion

        #region AI参数

        /// <summary>
        /// 最大速度限制
        /// </summary>
        public float 最大速度限制 { get; set; } = 200f;

        /// <summary>
        /// 战斗块更新目标间隔(正常状态)
        /// </summary>
        public int 战斗块更新间隔正常 { get; set; } = 0;

        /// <summary>
        /// 战斗块更新目标间隔(跟踪状态)
        /// </summary>
        public int 战斗块更新间隔跟踪 { get; set; } = 30;

        /// <summary>
        /// 战斗块攻击模式
        /// </summary>
        public int 战斗块攻击模式 { get; set; } = 3; // 拦截模式
        /// <summary>
        /// 战斗块目标优先级
        /// </summary>
        public OffensiveCombatTargetPriority 目标优先级 { get; set; } = OffensiveCombatTargetPriority.Largest;

        #endregion

        #region 飞控硬件参数

        /// <summary>
        /// 推进器和陀螺仪的方向容差
        /// </summary>
        public double 推进器方向容差 { get; set; } = 0.9;

        public double 常驻滚转转速 { get; set; } = 0.0; // 常驻滚转转速(弧度/秒)

        #endregion

        #region 性能统计参数

        /// <summary>
        /// 性能统计重置间隔(帧数)
        /// </summary>
        public int 性能统计重置间隔 { get; set; } = 600;

        #endregion

        #region 组名配置

        /// <summary>
        /// 导弹方块组名前缀
        /// </summary>
        public string 组名前缀 { get; set; } = "导弹";
        public string 代理控制器前缀 { get; set; } = "代理";

        #endregion

        #region 构造函数

        /// <summary>
        /// 默认构造函数，使用默认参数
        /// </summary>
        public 参数管理器()
        {
            // 所有参数已在属性声明时设置默认值
        }

        /// <summary>
        /// 从自定义数据字符串加载参数配置
        /// </summary>
        /// <param name="配置字符串">包含参数配置的字符串</param>
        public 参数管理器(string 配置字符串)
        {
            解析配置字符串(配置字符串);
        }

        #endregion

        #region 参数获取方法

        /// <summary>
        /// 获取计算后的时间常数乘以陀螺仪更新间隔
        /// </summary>
        public double 获取PID时间常数()
        {
            return 时间常数 * 陀螺仪更新间隔;
        }

        /// <summary>
        /// 根据当前推进器能力动态计算导航常数
        /// </summary>
        /// <param name="最大加速度">导弹最大加速度</param>
        /// <returns>计算后的导航常数</returns>
        public double 计算导航常数(double 最大加速度, double 目标距离)
        {
            double 计算值 = 最大加速度 / 10.0;
            计算值 *= Math.Max(目标距离 / 1000, 0.618); // 距离越远，导航常数越大
            return Math.Min(Math.Max(计算值, 导航常数最小值), 导航常数最大值);
        }

        #endregion

        #region 配置解析方法

        /// <summary>
        /// 从配置字符串解析参数
        /// </summary>
        /// <param name="配置字符串">配置字符串</param>
        private void 解析配置字符串(string 配置字符串)
        {
            if (string.IsNullOrWhiteSpace(配置字符串))
                return;

            string[] 行数组 = 配置字符串.Split('\n');
            foreach (string 行 in 行数组)
            {
                string 处理行 = 行.Trim();
                if (string.IsNullOrEmpty(处理行) || 处理行.StartsWith("//") || 处理行.StartsWith("#"))
                    continue;

                string[] 键值对 = 处理行.Split('=');
                if (键值对.Length != 2)
                    continue;

                string 键 = 键值对[0].Trim();
                string 值 = 键值对[1].Trim();

                尝试设置参数(键, 值);
            }
        }
        // 并在类内添加解析方法：
        private PID参数 解析PID参数(string 参数值)
        {
            // 支持格式: "P,I,D"
            var arr = 参数值.Split(',');
            if (arr.Length == 3)
            {
                double p = double.Parse(arr[0]);
                double i = double.Parse(arr[1]);
                double d = double.Parse(arr[2]);
                return new PID参数(p, i, d);
            }
            return new PID参数(0, 0, 0);
        }
        /// <summary>
        /// 尝试设置指定的参数
        /// </summary>
        /// <param name="参数名">参数名</param>
        /// <param name="参数值">参数值字符串</param>
        private void 尝试设置参数(string 参数名, string 参数值)
        {
            try
            {
                switch (参数名)
                {
                    case "最小向量长度":
                        最小向量长度 = double.Parse(参数值);
                        break;
                    case "最小接近加速度":
                        最小接近加速度 = double.Parse(参数值);
                        break;
                    case "时间常数":
                        时间常数 = double.Parse(参数值);
                        break;
                    case "角度误差最小值":
                        角度误差最小值 = double.Parse(参数值) * Math.PI / 180.0; // 转换为弧度
                        break;
                    case "导航常数初始值":
                        导航常数初始值 = double.Parse(参数值);
                        break;
                    case "引爆距离阈值":
                        引爆距离阈值 = double.Parse(参数值);
                        break;
                    case "热发射持续帧数":
                        热发射持续帧数 = int.Parse(参数值);
                        break;
                    case "分离推进器名称":
                        分离推进器名称 = 参数值;
                        break;
                    case "陀螺仪更新间隔":
                        陀螺仪更新间隔 = int.Parse(参数值);
                        break;
                    case "推进器重新分类间隔":
                        推进器重新分类间隔 = int.Parse(参数值);
                        break;
                    case "目标位置不变最大帧数":
                        目标位置不变最大帧数 = int.Parse(参数值);
                        break;
                    case "预测制导持续帧数":
                        预测制导持续帧数 = int.Parse(参数值);
                        break;
                    case "方块更新间隔":
                        方块更新间隔 = int.Parse(参数值);
                        break;
                    case "目标历史最大长度":
                        目标历史最大长度 = int.Parse(参数值);
                        break;
                    case "最大速度限制":
                        最大速度限制 = float.Parse(参数值);
                        break;
                    case "战斗块攻击模式":
                        战斗块攻击模式 = int.Parse(参数值);
                        break;
                    case "目标优先级":
                        try
                        {
                            目标优先级 = (OffensiveCombatTargetPriority)Enum.Parse(
                                typeof(OffensiveCombatTargetPriority), 参数值);
                        }
                        catch { }
                        break;
                    case "性能统计重置间隔":
                        性能统计重置间隔 = int.Parse(参数值);
                        break;
                    case "组名前缀":
                        组名前缀 = 参数值;
                        break;
                    case "外环PID3":
                        外环参数 = 解析PID参数(参数值);
                        break;
                    case "内环PID3":
                        内环参数 = 解析PID参数(参数值);
                        break;
                    case "常驻滚转转速":
                        常驻滚转转速 = double.Parse(参数值);
                        break;
                    case "战斗块更新间隔正常":
                        战斗块更新间隔正常 = int.Parse(参数值);
                        break;
                    case "战斗块更新间隔跟踪":
                        战斗块更新间隔跟踪 = int.Parse(参数值);
                        break;
                    case "代理控制器前缀":
                        代理控制器前缀 = 参数值;
                        break;
                }
            }
            catch (Exception)
            {
                // 参数解析失败时忽略，保持默认值
            }
        }

        #endregion

        #region 配置输出方法

        /// <summary>
        /// 生成当前参数配置的字符串
        /// </summary>
        /// <returns>参数配置字符串</returns>
        public string 生成配置字符串()
        {
            var 配置 = new System.Text.StringBuilder();
            配置.AppendLine("// 导弹参数配置文件");
            配置.AppendLine("// 不要修改任何参数，除非你知道以下三件事：");
            配置.AppendLine("// 是什么，如何工作，可能的影响。");
            配置.AppendLine("// 制导相关参数");
            配置.AppendLine($"最小向量长度={最小向量长度}");
            配置.AppendLine($"最小接近加速度={最小接近加速度}");
            配置.AppendLine($"时间常数={时间常数}");
            配置.AppendLine($"角度误差最小值={角度误差最小值 * 180.0 / Math.PI}"); // 转换为度数显示
            配置.AppendLine($"导航常数初始值={导航常数初始值}");
            配置.AppendLine();
            配置.AppendLine("// 引爆相关参数");
            配置.AppendLine($"引爆距离阈值={引爆距离阈值}");
            配置.AppendLine();
            配置.AppendLine("// 热发射阶段相关参数");
            配置.AppendLine($"热发射持续帧数={热发射持续帧数}");
            配置.AppendLine($"分离推进器名称={分离推进器名称}");
            配置.AppendLine();
            配置.AppendLine("// 状态切换时间参数");
            配置.AppendLine($"陀螺仪更新间隔={陀螺仪更新间隔}");
            配置.AppendLine($"推进器重新分类间隔={推进器重新分类间隔}");
            配置.AppendLine($"目标位置不变最大帧数={目标位置不变最大帧数}");
            配置.AppendLine($"预测制导持续帧数={预测制导持续帧数}");
            配置.AppendLine($"方块更新间隔={方块更新间隔}");
            配置.AppendLine();
            配置.AppendLine("// 目标跟踪器参数");
            配置.AppendLine($"目标历史最大长度={目标历史最大长度}");
            配置.AppendLine();
            配置.AppendLine("// 飞行AI参数");
            配置.AppendLine($"战斗块更新间隔正常={战斗块更新间隔正常}");
            配置.AppendLine($"战斗块更新间隔跟踪={战斗块更新间隔跟踪}");
            配置.AppendLine($"最大速度限制={最大速度限制}");
            配置.AppendLine($"战斗块攻击模式={战斗块攻击模式}");
            配置.AppendLine($"目标优先级={目标优先级}");
            配置.AppendLine("// 目标优先级可选项:Closest,Largest,Smallest");
            配置.AppendLine();
            配置.AppendLine();
            配置.AppendLine("// 性能统计参数");
            配置.AppendLine($"性能统计重置间隔={性能统计重置间隔}");
            配置.AppendLine();
            配置.AppendLine($"组名前缀={组名前缀}");
            配置.AppendLine($"代理控制器前缀={代理控制器前缀}");
            配置.AppendLine();
            配置.AppendLine("// 三轴一致外环PID参数");
            配置.AppendLine($"外环PID3={外环参数.P系数},{外环参数.I系数},{外环参数.D系数}");
            配置.AppendLine("// 三轴一致内环PID参数");
            配置.AppendLine($"内环PID3={内环参数.P系数},{内环参数.I系数},{内环参数.D系数}");
            配置.AppendLine();
            配置.AppendLine("// 飞控硬件参数");
            配置.AppendLine($"常驻滚转转速={常驻滚转转速}"); // 常驻滚转转速(弧度/秒)
            配置.AppendLine("// 常驻滚转转速可能会影响导弹的方向稳定性");
            return 配置.ToString();
        }

        #endregion
    }
}
