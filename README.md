# AI主动弹制导系统 (AI Active Missile Guidance System)

## 项目简介 (Project Overview)

这是一个用于《太空工程师》(Space Engineers) 游戏的AI主动制导导弹脚本系统。该脚本实现了先进的导弹制导算法，能够自动追踪和拦截移动目标。

This is an AI-powered active guidance missile system script for the game Space Engineers. The script implements advanced missile guidance algorithms that can automatically track and intercept moving targets.

**版本 (Version):** 1.1.4  
**最后更新 (Last Update):** 2025-07-28

## 核心特性 (Key Features)

### 1. 智能制导系统 (Intelligent Guidance System)
- **比例导航制导 (Proportional Navigation Guidance):** 使用动态导航常数的比例导引法
- **目标预测 (Target Prediction):** 基于目标运动模型的轨迹预测
- **外力补偿 (Disturbance Compensation):** 自动计算和补偿重力等外部干扰
- **攻击角度约束 (Attack Angle Constraints):** 可选的攻击角度优化

### 2. 目标跟踪 (Target Tracking)
- **线性运动预测 (Linear Motion Prediction):** 处理直线飞行目标
- **圆周运动识别 (Circular Motion Detection):** 识别和预测盘旋机动的目标
- **混合预测模型 (Hybrid Prediction Model):** 结合线性和圆周运动的智能预测

### 3. 控制系统 (Control Systems)
- **陀螺仪姿态控制 (Gyroscope Attitude Control):** 双环PID控制系统
  - 外环：角度误差 → 期望角速度
  - 内环：角速度误差 → 陀螺仪设定
- **推进器管理 (Thruster Management):** 自动分类和控制六个方向的推进器
- **动力分配优化 (Power Distribution Optimization):** 根据可用过载智能分配推力

### 4. 引爆系统 (Detonation System)
- **近炸引信 (Proximity Fuse):** 基于距离的自动引爆
- **碰炸引信 (Impact Fuse):** 检测碰撞并引爆
- **传感器支持 (Sensor Support):** 可选的传感器触发引爆
- **分级引爆 (Staged Detonation):** 激发雷管组和引爆雷管组的序列控制

### 5. 发射系统 (Launch System)
- **挂架发射 (Rack Launch):** 支持连接器、合并方块、转子挂架
- **热发射模式 (Hot Launch Mode):** 离架后的安全清除阶段
- **分离推进器 (Separation Thrusters):** 可选的助推分离推进器

## 系统要求 (System Requirements)

### 必需组件 (Required Components)
1. **AI飞行模块 (AI Flight Movement Block)** - 提供目标信息和飞行控制
2. **AI攻击模块 (AI Offensive Combat Block)** - 提供攻击目标选择
3. **遥控器 (Remote Control)** - 提供导弹坐标系统（推荐）
4. **陀螺仪 (Gyroscopes)** - 姿态控制
5. **推进器 (Thrusters)** - 机动能力
6. **编程块 (Programmable Block)** - 运行此脚本

### 可选组件 (Optional Components)
- **弹头 (Warheads)** - 自动配置引爆序列
- **传感器 (Sensors)** - 增强目标检测（需手动配置）
- **连接器/合并方块/转子 (Connectors/Merge Blocks/Rotors)** - 挂架发射
- **氢气罐 (Hydrogen Tanks)** - 氢推进器支持
- **重力发生器 (Gravity Generators)** - 特殊用途

### 特殊说明 (Special Notes)
- 支持无遥控器导弹，但质量估算和重力读数可能不准确，仅推荐小型导弹使用
- 默认使用编程块坐标系作为前向方向
- 可通过在方块名称中添加"代理"来指定自定义坐标系方块

## 系统架构 (System Architecture)

### 主要类结构 (Main Class Structure)

```
Program (主程序)
├── 参数管理器 (Parameter Manager)
│   └── 管理所有制导参数和配置
├── 导弹识别器 (Missile Identifier)
│   └── 使用BFS算法遍历和识别导弹方块
├── 导弹状态量 (Missile State)
│   └── 管理导弹状态机和状态数据
├── TargetTracker (目标跟踪器)
│   ├── 线性运动预测
│   ├── 圆周运动检测
│   └── 目标轨迹预测
├── 推进系统 (Propulsion System)
│   ├── 推进器分类和管理
│   └── 推力输出控制
└── 陀螺仪瞄准系统 (Gyroscope Aiming System)
    ├── 双环PID控制
    └── 姿态稳定控制
```

### 导弹状态机 (Missile State Machine)

```
初始化状态 → 待机状态 → 热发射阶段 → 搜索目标 → 跟踪目标 → 预测制导 → 引爆激发 → 引爆最终
```

1. **初始化状态:** 系统启动和组件识别
2. **待机状态:** 等待挂架分离信号
3. **热发射阶段:** 离架后的安全清除
4. **搜索目标:** 主动搜索可攻击目标
5. **跟踪目标:** 锁定并跟踪目标
6. **预测制导:** 使用预测轨迹进行精确制导
7. **引爆激发:** 触发第一阶段雷管
8. **引爆最终:** 触发最终雷管组

## 核心算法 (Core Algorithms)

### 比例导航制导 (Proportional Navigation)

脚本使用增强的比例导航法，公式为：

```
a_cmd = N * V_closing * ω_los + 补偿项
```

其中：
- `N`: 导航常数（动态调整，范围3-5）
- `V_closing`: 接近速度
- `ω_los`: 视线角速度
- `补偿项`: 包括重力补偿和外力干扰补偿

### 目标运动预测 (Target Motion Prediction)

系统使用混合预测模型：

1. **线性预测:** 适用于直线飞行目标
   ```
   P_predicted = P_current + V * t + 0.5 * a * t²
   ```

2. **圆周运动预测:** 识别盘旋目标并预测其轨迹
   - 使用历史位置数据拟合圆周运动参数
   - 计算圆心、半径和角速度
   - 预测未来位置

3. **加权融合:** 根据预测误差动态调整两种模型的权重

### PID控制 (PID Control)

陀螺仪系统使用双环PID控制：

- **外环PID:** 将角度误差转换为期望角速度
- **内环PID:** 将角速度误差转换为陀螺仪控制输出
- **抗饱和机制:** 使用反馈计算法防止积分饱和

## 参数配置 (Parameter Configuration)

### 制导参数 (Guidance Parameters)
- `导航常数初始值`: 3.0（范围：3-5）
- `最小接近加速度`: 9.8 m/s²
- `角度误差最小值`: 0.2°
- `最长接近预测时间`: 2000 ms
- `补偿项失效距离`: 200 m

### 引爆参数 (Detonation Parameters)
- `引爆距离阈值`: 5.0 m
- `碰炸解锁距离`: 50.0 m
- `碰炸迟缓度`: 4.0

### 控制参数 (Control Parameters)
- `陀螺仪外环PID`: Kp=5.0, Ki=0.0, Kd=4.0
- `陀螺仪内环PID`: Kp=2.0, Ki=0.3, Kd=0.15
- `推进器控制PID`: Kp=1.0, Ki=0.0, Kd=0.0

## 使用说明 (Usage Instructions)

### 1. 导弹构建 (Missile Construction)
1. 构建导弹结构，确保包含所有必需组件
2. 将所有导弹方块放入同一方块组
3. 配置AI模块的目标类型和攻击距离
4. 安装弹头并按需要排列（脚本会自动配置引爆序列）

### 2. 脚本部署 (Script Deployment)
1. 将脚本复制到编程块
2. 编译确认无错误
3. 脚本会自动初始化并识别所有组件

### 3. 发射前准备 (Pre-Launch)
- 确保导弹通过连接器、合并方块或转子连接到发射架
- 脚本处于"待机状态"
- AI模块已激活

### 4. 发射 (Launch)
1. 禁用（Disable）连接器/合并方块/转子
2. 导弹自动进入热发射阶段
3. 清除发射架后开始搜索目标
4. 锁定目标后自动跟踪和制导

### 5. 特殊功能 (Special Features)

#### 分离推进器 (Separation Thrusters)
在推进器名称中添加"分离"字样，这些推进器将作为助推器，仅在离架时工作。

#### 自定义坐标系 (Custom Coordinate System)
在某个方块名称中添加"代理"，将使用该方块作为导弹坐标系统。注意方块朝向。

#### 殉爆箱支持 (Blast Box Support)
脚本自动支持殉爆箱式弹头配置，无需额外设置。

## 性能优化 (Performance Optimization)

脚本采用多项优化措施：
- 分帧处理密集计算任务
- 缓存频繁使用的计算结果
- 移动平均队列减少噪声
- 条件性更新减少不必要计算

## 故障排除 (Troubleshooting)

### 常见问题 (Common Issues)

**问题：导弹不转向目标**
- 检查陀螺仪是否启用且有电
- 确认遥控器方向正确
- 查看陀螺仪是否被手动超控

**问题：导弹不追踪目标**
- 确认AI攻击模块已启用
- 检查目标是否在AI模块探测范围内
- 验证目标类型是否在AI模块配置中

**问题：导弹推力不足**
- 检查推进器是否正常工作
- 确认氢气罐有充足燃料（如使用氢推进器）
- 验证质量与推力比是否合理

**问题：弹头不引爆**
- 检查弹头是否武装（Armed）
- 确认引爆距离参数设置
- 查看传感器配置（如使用传感器）

## 技术细节 (Technical Details)

### 方块识别算法 (Block Identification)
使用广度优先搜索（BFS）算法遍历导弹网格：
- 从编程块开始
- 探索所有相邻方块
- 识别功能方块并分类
- 支持分帧处理大型导弹

### 质量估算 (Mass Estimation)
脚本自动估算导弹质量：
- 基于方块类型和尺寸
- 考虑货物和库存重量
- 动态更新质量参数

### 坐标系统 (Coordinate System)
- 默认：编程块坐标系
- 前向：编程块正向
- 可自定义：使用"代理"方块

## 开发计划 (Development Roadmap)

### 待完成功能 (TODO)
- [ ] 动态比例导引常数的距离逻辑优化
- [ ] 方块质量估算修复（包括装甲块）
- [ ] 库存质量估计改进
- [ ] 移动平均长度的时间参数化
- [ ] 摄像头支持

## 许可证 (License)

本项目供《太空工程师》玩家社区使用和修改。

## 贡献 (Contributing)

欢迎提交问题报告和改进建议！

## 致谢 (Acknowledgments)

感谢《太空工程师》社区的支持和反馈。

---

**注意：** 本脚本仅适用于《太空工程师》游戏环境，需要游戏的编程块系统支持。
