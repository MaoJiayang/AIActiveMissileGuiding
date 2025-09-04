using System;
using VRage.Game.ModAPI.Ingame;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using System.Text;
using System.Collections.Generic;

namespace IngameScript
{
    /// <summary>
    /// 导弹参数管理器 - 统一管理所有超参数
    /// </summary>
    public class 参数管理器
    {
        public string 版本号 { get; } = "1.1.4";
        #region 制导相关参数
        /// <summary>
        /// 向量最小有效长度
        /// </summary>
        public double 最小向量长度 { get; set; } = 1e-6;

        /// <summary>
        /// 最小接近加速度(m/s)
        /// </summary>
        public double 最小接近加速度 { get; set; } = 9.8;

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
        public double 导航常数最大值 { get; set; } = 5;

        /// <summary>
        /// 是否启用攻击角度约束
        /// </summary>
        public bool 启用攻击角度约束 { get; set; } = true;

        /// <summary>
        /// 是否启用外力干扰计算
        /// </summary>
        public bool 启用外力干扰 { get; set; } = true;
        /// <summary>
        /// 允许参与制导量计算的最大外力干扰量(m/s^2)
        /// 11.8约等于1.2g
        /// </summary>
        public double 最大外力干扰 { get; set; } = 11.8;

        /// <summary>
        /// 导弹将会向着预测时间后的目标位置接近，取决于预估最近时间
        /// 该参数决定最长的预测时间
        /// </summary>
        public long 最长接近预测时间 { get; set; } = 2000; // 最长预测时间(毫秒)

        /// <summary>
        ///  补偿项失效距离(米)
        /// 当目标距离小于此值时，补偿项将不再生效
        /// </summary>
        public double 补偿项失效距离 { get; set; } = 200.0;

        #endregion

        #region 引爆相关参数

        /// <summary>
        /// 接近引爆距离阈值(米)
        /// </summary>
        public double 引爆距离阈值 { get; set; } = 5.0;

        /// <summary>
        /// 碰炸解锁距离(米)
        /// 大于此距离不允许碰炸
        /// </summary>
        public double 碰炸解锁距离 { get; set; } = 50.0;

        /// <summary>
        /// 碰炸迟缓度
        /// 如果导弹的当前加速度
        /// 超过自己能提供的sqrt(迟缓度)倍
        /// 则会触发碰炸
        /// </summary>
        public double 碰炸迟缓度 { get; set; } = 4;

        /// <summary>
        /// 手动保险超控
        /// 如果为true，则导弹可以在任何状态下引爆
        /// </summary>
        public bool 手动保险超控 { get; set; } = false;

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
        public int 动力系统更新间隔 { get; set; } = 5;

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

        #region 网格识别参数
        public Vector3D? MeGridMax = null;
        public Vector3D? MeGridMin = null;
        // 排除标签列表，范围内但排除特定标签的方块（允许遍历但不加入方块列表）
        public string ExcludeTags = "排除";
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
        /// 战斗块更新目标间隔(0)
        /// </summary>
        public int 战斗块更新间隔_搜索 { get; set; } = 0;

        /// <summary>
        /// 战斗块更新目标间隔(专注目标-30+)
        /// </summary>
        public int 战斗块更新间隔_专注 { get; set; } = 30;

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

        #region 委托注册系统
        /// <summary>
        /// 参数注册表 - 存储所有参数的访问委托
        /// </summary>
        private Dictionary<string, 参数描述符> 参数注册表;

        /// <summary>
        /// 注册所有参数到注册表中
        /// 添加新参数在此方法中添加注册代码
        /// 并添加相关解析和格式化方法
        /// </summary>
        private void 注册所有参数()
        {
            参数注册表 = new Dictionary<string, 参数描述符>();

            // 制导相关参数
            注册参数("最小向量长度",
                () => 最小向量长度.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 最小向量长度 = val; },
                "向量最小有效长度");

            注册参数("最小接近加速度",
                () => 最小接近加速度.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 最小接近加速度 = val; },
                "最小接近加速度(m/s)");

            注册参数("时间常数",
                () => 时间常数.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 时间常数 = val; },
                "时间常数(秒)");

            注册参数("角度误差最小值",
                () => 格式化角度(角度误差最小值),
                v => 角度误差最小值 = 解析角度(v),
                "角度误差下限，小于此值视为对准(度)");

            注册参数("导航常数初始值",
                () => 导航常数初始值.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 导航常数初始值 = val; },
                "导航常数初始值");

            注册参数("导航常数最小值",
                () => 导航常数最小值.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 导航常数最小值 = val; },
                "导航常数最小值");

            注册参数("导航常数最大值",
                () => 导航常数最大值.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 导航常数最大值 = val; },
                "导航常数最大值");

            注册参数("启用攻击角度约束",
                () => 启用攻击角度约束.ToString(),
                v => { bool val; if (bool.TryParse(v, out val)) 启用攻击角度约束 = val; },
                "是否启用攻击角度约束");

            注册参数("启用外力干扰",
                () => 启用外力干扰.ToString(),
                v => { bool val; if (bool.TryParse(v, out val)) 启用外力干扰 = val; },
                "是否启用外力干扰计算");

            注册参数("最大外力干扰",
                () => 最大外力干扰.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 最大外力干扰 = val; },
                "允许参与制导量计算的最大外力干扰量(m/s^2)");

            注册参数("最长接近预测时间",
                () => 最长接近预测时间.ToString(),
                v => { long val; if (long.TryParse(v, out val)) 最长接近预测时间 = val; },
                "导弹预测目标位置的最长时间(毫秒)");

            注册参数("补偿项失效距离",
                () => 补偿项失效距离.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 补偿项失效距离 = val; },
                "补偿项失效距离(米)");

            // 引爆相关参数
            注册参数("引爆距离阈值",
                () => 引爆距离阈值.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 引爆距离阈值 = val; },
                "接近引爆距离阈值(米)");

            注册参数("碰炸解锁距离",
                () => 碰炸解锁距离.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 碰炸解锁距离 = val; },
                "碰炸解锁距离(米)");

            注册参数("碰炸迟缓度",
                () => 碰炸迟缓度.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 碰炸迟缓度 = val; },
                "碰炸迟缓度");

            注册参数("手动保险超控",
                () => 手动保险超控.ToString(),
                v => { bool val; if (bool.TryParse(v, out val)) 手动保险超控 = val; },
                "手动保险超控");

            // 热发射阶段相关参数
            注册参数("热发射持续帧数",
                () => 热发射持续帧数.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 热发射持续帧数 = val; },
                "热发射阶段持续时间(帧数)");

            注册参数("分离推进器名称",
                () => 分离推进器名称,
                v => 分离推进器名称 = v,
                "分离推进器识别名称");

            // 状态切换时间参数
            注册参数("动力系统更新间隔",
                () => 动力系统更新间隔.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 动力系统更新间隔 = val; },
                "动力系统更新间隔(ticks)");

            注册参数("推进器重新分类间隔",
                () => 推进器重新分类间隔.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 推进器重新分类间隔 = val; },
                "推进器重新分类间隔(ticks)");

            注册参数("目标位置不变最大帧数",
                () => 目标位置不变最大帧数.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 目标位置不变最大帧数 = val; },
                "目标位置不变最大帧数");

            注册参数("预测制导持续帧数",
                () => 预测制导持续帧数.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 预测制导持续帧数 = val; },
                "丢失目标后预测制导持续时间(帧数)");

            注册参数("方块更新间隔",
                () => 方块更新间隔.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 方块更新间隔 = val; },
                "重新初始化方块的间隔(帧数)");

            // PID控制器参数
            注册参数("外环PID3",
                () => 格式化PID参数(外环参数),
                v => 外环参数 = 解析PID参数(v),
                "外环PID参数(P,I,D)");

            注册参数("内环PID3",
                () => 格式化PID参数(内环参数),
                v => 内环参数 = 解析PID参数(v),
                "内环PID参数(P,I,D)");

            // 目标跟踪器参数
            注册参数("目标历史最大长度",
                () => 目标历史最大长度.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 目标历史最大长度 = val; },
                "目标历史记录最大长度");

            // AI参数
            注册参数("最大速度限制",
                () => 最大速度限制.ToString(),
                v => { float val; if (float.TryParse(v, out val)) 最大速度限制 = val; },
                "最大速度限制");

            注册参数("战斗块更新间隔正常",
                () => 战斗块更新间隔_搜索.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 战斗块更新间隔_搜索 = val; },
                "战斗块更新目标间隔(搜索模式)");

            注册参数("战斗块更新间隔跟踪",
                () => 战斗块更新间隔_专注.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 战斗块更新间隔_专注 = val; },
                "战斗块更新目标间隔(专注模式)");

            注册参数("战斗块攻击模式",
                () => 战斗块攻击模式.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 战斗块攻击模式 = val; },
                "战斗块攻击模式");

            注册参数("目标优先级",
                () => 格式化目标优先级(目标优先级),
                v => 目标优先级 = 解析目标优先级(v, 目标优先级),
                "战斗块目标优先级(Closest,Largest,Smallest)");

            // 飞控硬件参数
            注册参数("推进器方向容差",
                () => 推进器方向容差.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 推进器方向容差 = val; },
                "推进器和陀螺仪的方向容差");

            注册参数("常驻滚转转速",
                () => 常驻滚转转速.ToString(),
                v => { double val; if (double.TryParse(v, out val)) 常驻滚转转速 = val; },
                "常驻滚转转速(弧度/秒)");

            // 性能统计参数
            注册参数("性能统计重置间隔",
                () => 性能统计重置间隔.ToString(),
                v => { int val; if (int.TryParse(v, out val)) 性能统计重置间隔 = val; },
                "性能统计重置间隔(帧数)");

            // 组名配置
            注册参数("组名前缀",
                () => 组名前缀,
                v => 组名前缀 = v,
                "导弹方块组名前缀");

            注册参数("代理控制器前缀",
                () => 代理控制器前缀,
                v => 代理控制器前缀 = v,
                "代理控制器前缀");
                
            // 注册 MeGridMax 参数
            注册参数("MeGridMax",
                获取值: () => MeGridMax.HasValue ? 格式化Vector3D(MeGridMax.Value) : "",
                设置值: v => MeGridMax = 解析Vector3D(v),
                描述: "网格最大边界点（相对于参考方块）",
                空值时隐藏: true);

            // 注册 MeGridMin 参数
            注册参数("MeGridMin",
                获取值: () => MeGridMin.HasValue ? 格式化Vector3D(MeGridMin.Value) : "",
                设置值: v => MeGridMin = 解析Vector3D(v),
                描述: "网格最小边界点（相对于参考方块）",
                空值时隐藏: true);

            // 注册 ExcludeTags 参数
            注册参数("ExcludeTags",
                获取值: () => ExcludeTags ?? "",
                设置值: v => ExcludeTags = 解析字符串(v),
                描述: "排除标签（列表），包含这些标签的方块将被排除在识别外",
                空值时隐藏: true);
        }

        #endregion

        #region 参数获取方法

        /// <summary>
        /// 获取计算后的时间常数乘以陀螺仪更新间隔
        /// </summary>
        public double 获取PID时间常数()
        {
            return 时间常数 * 动力系统更新间隔;
        }

        /// <summary>
        /// 根据当前推进器能力动态计算导航常数
        /// </summary>
        /// <param name="最大加速度">导弹最大加速度</param>
        /// <returns>计算后的导航常数</returns>
        public double 计算导航常数(double 最大加速度, double 目标距离)
        {
            double 计算值 = 最大加速度 / 10.0;
            计算值 *= Math.Max(目标距离 / 1500, 0.1); // 距离越远，导航常数越大
            return Math.Min(Math.Max(计算值, 导航常数最小值), 导航常数最大值);
        }

        /// <summary>
        /// 获取排除标签列表
        /// </summary>
        /// <returns>排除标签的字符串数组</returns>
        public string[] 获取排除标签列表()
        {
            if (string.IsNullOrWhiteSpace(ExcludeTags))
                return new string[0];
            
            var tags = ExcludeTags.Split(',');
            var result = new List<string>();
            
            foreach (var tag in tags)
            {
                var trimmed = tag.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }
            
            return result.ToArray();
        }

        /// <summary>
        /// 检查方块名称是否包含任何排除标签
        /// </summary>
        /// <param name="blockName">方块名称</param>
        /// <returns>true表示应该排除，false表示不排除</returns>
        public bool 应该排除方块(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName))
                return false;
            
            var excludeTags = 获取排除标签列表();
            if (excludeTags.Length == 0)
                return false;
            
            foreach (var tag in excludeTags)
            {
                if (blockName.Contains(tag))
                    return true;
            }
            
            return false;
        }
        #endregion

        #region 参数辅助方法
        
        /// <summary>
        /// 从字符串解析字符串（处理空值和trim）
        /// </summary>
        private string 解析字符串(string 值字符串)
        {
            if (string.IsNullOrWhiteSpace(值字符串))
                return "";

            return 值字符串.Trim();
        }

        /// <summary>
        /// 将Vector3D格式化为字符串
        /// </summary>
        private string 格式化Vector3D(Vector3D vector)
        {
            return $"{vector.X}, {vector.Y}, {vector.Z}";
        }

        /// <summary>
        /// 从字符串解析Vector3D
        /// </summary>
        private Vector3D? 解析Vector3D(string 值字符串)
        {
            if (string.IsNullOrWhiteSpace(值字符串))
                return null;

            try
            {
                var parts = 值字符串.Split(',');
                if (parts.Length == 3)
                {
                    return new Vector3D(
                        double.Parse(parts[0].Trim()),
                        double.Parse(parts[1].Trim()),
                        double.Parse(parts[2].Trim()));
                }
            }
            catch (Exception)
            {
                // 解析失败时返回null
            }
            return null;
        }

        /// <summary>
        /// 将PID参数格式化为字符串
        /// </summary>
        private string 格式化PID参数(PID参数 参数)
        {
            return $"{参数.P系数},{参数.I系数},{参数.D系数}";
        }

        /// <summary>
        /// 从字符串解析PID参数
        /// </summary>
        private PID参数 解析PID参数(string 参数值)
        {
            if (string.IsNullOrWhiteSpace(参数值))
                return new PID参数(0, 0, 0);

            try
            {
                // 支持格式: "P,I,D"
                var arr = 参数值.Split(',');
                if (arr.Length == 3)
                {
                    double p = double.Parse(arr[0].Trim());
                    double i = double.Parse(arr[1].Trim());
                    double d = double.Parse(arr[2].Trim());
                    return new PID参数(p, i, d);
                }
            }
            catch (Exception)
            {
                // 解析失败时返回默认值
            }
            return new PID参数(0, 0, 0);
        }

        /// <summary>
        /// 角度从弧度转换为度数显示
        /// </summary>
        private string 格式化角度(double 弧度)
        {
            return (弧度 * 180.0 / Math.PI).ToString();
        }

        /// <summary>
        /// 角度从度数字符串解析为弧度
        /// </summary>
        private double 解析角度(string 度数字符串)
        {
            double 度数;
            if (double.TryParse(度数字符串, out 度数))
            {
                return 度数 * Math.PI / 180.0;
            }
            return 0;
        }

        /// <summary>
        /// 枚举类型格式化
        /// </summary>
        private string 格式化目标优先级(OffensiveCombatTargetPriority 枚举值)
        {
            return 枚举值.ToString();
        }

        /// <summary>
        /// 枚举类型解析
        /// </summary>
        private OffensiveCombatTargetPriority 解析目标优先级(string 值字符串, OffensiveCombatTargetPriority 默认值)
        {
            OffensiveCombatTargetPriority 结果;
            if (Enum.TryParse(值字符串, out 结果))
            {
                return 结果;
            }
            return 默认值;
        }
        
        #endregion
        
// ---------- 基本上，不需要改动以下代码 ----------
        /// <summary>
        /// 注册单个参数到注册表
        /// </summary>
        private void 注册参数(string 参数名, Func<string> 获取值, Action<string> 设置值, string 描述 = "", bool 空值时隐藏 = false)
        {
            参数注册表[参数名] = new 参数描述符(获取值, 设置值, 描述, 空值时隐藏);
        }

        #region 构造函数

        /// <summary>
        /// 默认构造函数，使用默认参数
        /// </summary>
        public 参数管理器(IMyTerminalBlock block)
        {
            // 初始化参数注册系统
            注册所有参数();

            // 初始化参数管理器（可以从Me.CustomData读取配置）
            string 自定义数据 = block.CustomData;
            if (!string.IsNullOrWhiteSpace(自定义数据))
            {
                解析配置字符串(自定义数据);
                block.CustomData = 生成配置字符串();
            }
            else block.CustomData = 生成配置字符串();
        }

        /// <summary>
        /// 从自定义数据字符串加载参数配置
        /// </summary>
        /// <param name="配置字符串">包含参数配置的字符串</param>
        public 参数管理器(string 配置字符串)
        {
            // 初始化参数注册系统
            注册所有参数();

            解析配置字符串(配置字符串);
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

        /// <summary>
        /// 尝试设置指定的参数（基于委托注册系统）
        /// </summary>
        /// <param name="参数名">参数名</param>
        /// <param name="参数值">参数值字符串</param>
        private void 尝试设置参数(string 参数名, string 参数值)
        {
            try
            {
                // 通过参数注册表查找对应的设置委托
                if (参数注册表.ContainsKey(参数名))
                {
                    参数注册表[参数名].设置值(参数值);
                }
                // 未知参数会被自动忽略
            }
            catch (Exception)
            {
                // 参数解析失败时忽略，保持默认值
            }
        }

        #endregion

        #region 配置输出方法

        /// <summary>
        /// 生成当前参数配置的字符串（基于委托注册系统）
        /// </summary>
        /// <returns>参数配置字符串</returns>
        public string 生成配置字符串()
        {
            var 配置 = new StringBuilder();
            配置.AppendLine("// 参数配置文件");
            配置.AppendLine("// 不要修改任何参数，除非你知道以下三件事：");
            配置.AppendLine("// 是什么，如何工作，可能的影响");

            // 遍历参数注册表，生成配置
            foreach (var kvp in 参数注册表)
            {
                string 参数名 = kvp.Key;
                参数描述符 描述符 = kvp.Value;
                string 参数值 = 描述符.获取值();

                // 检查是否应该显示该参数
                bool 应该显示 = !描述符.空值时隐藏 || !string.IsNullOrWhiteSpace(参数值);

                if (应该显示)
                {
                    // 添加参数描述（如果有）
                    if (!string.IsNullOrEmpty(描述符.描述))
                    {
                        配置.AppendLine($"// {描述符.描述}");
                    }

                    // 添加参数配置行
                    配置.AppendLine($"{参数名} = {参数值}");
                    配置.AppendLine();
                }
            }

            return 配置.ToString();
        }

        #endregion
    }    
    /// <summary>
    /// 参数描述符 - 存储参数的访问委托和元数据
    /// </summary>
    public class 参数描述符
    {
        /// <summary>
        /// 获取参数值的委托（转换为字符串）
        /// </summary>
        public Func<string> 获取值 { get; set; }

        /// <summary>
        /// 设置参数值的委托（从字符串解析）
        /// </summary>
        public Action<string> 设置值 { get; set; }

        /// <summary>
        /// 参数描述
        /// </summary>
        public string 描述 { get; set; }

        /// <summary>
        /// 当参数值为null或空时是否隐藏该参数
        /// </summary>
        public bool 空值时隐藏 { get; set; }

        public 参数描述符(Func<string> 获取值, Action<string> 设置值, string 描述 = "", bool 空值时隐藏 = false)
        {
            this.获取值 = 获取值;
            this.设置值 = 设置值;
            this.描述 = 描述;
            this.空值时隐藏 = 空值时隐藏;
        }
    }
}
