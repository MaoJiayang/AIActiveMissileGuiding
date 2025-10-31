# API参考文档 (API Reference)

## 概述 (Overview)

本文档提供AI主动弹制导系统各个类和方法的详细API参考。

## 目录 (Table of Contents)

1. [参数管理器 (Parameter Manager)](#参数管理器)
2. [导弹状态量 (Missile State)](#导弹状态量)
3. [TargetTracker (目标跟踪器)](#targettracker)
4. [推进系统 (Propulsion System)](#推进系统)
5. [陀螺仪瞄准系统 (Gyroscope System)](#陀螺仪瞄准系统)
6. [导弹识别器 (Missile Identifier)](#导弹识别器)
7. [Utils (工具类)](#utils)

---

## 参数管理器

### 类：`参数管理器`

统一管理所有系统参数的类。

#### 属性 (Properties)

##### 制导参数

| 属性名 | 类型 | 默认值 | 描述 |
|--------|------|--------|------|
| `版本号` | string | "1.1.4" | 当前版本号 |
| `最小向量长度` | double | 1e-6 | 向量计算的最小有效长度 |
| `最小接近加速度` | double | 9.8 | 最小接近加速度 (m/s) |
| `时间常数` | double | 1/60 | 帧时间常数 (秒) |
| `角度误差最小值` | double | 0.2° | 角度对准阈值 |
| `导航常数初始值` | double | 3 | PN制导导航常数初始值 |
| `导航常数最小值` | double | 3 | 导航常数下限 |
| `导航常数最大值` | double | 5 | 导航常数上限 |
| `启用攻击角度约束` | bool | true | 是否启用攻击角度优化 |
| `启用外力干扰` | bool | true | 是否计算外力补偿 |
| `最大外力干扰` | double | 11.8 | 允许的最大外力补偿 (m/s²) |
| `最长接近预测时间` | long | 2000 | 最长目标位置预测时间 (ms) |
| `补偿项失效距离` | double | 200.0 | 补偿项开始衰减的距离 (m) |

##### 引爆参数

| 属性名 | 类型 | 默认值 | 描述 |
|--------|------|--------|------|
| `引爆距离阈值` | double | 5.0 | 近炸引信触发距离 (m) |
| `碰炸解锁距离` | double | 50.0 | 碰炸引信解锁距离 (m) |
| `碰炸迟缓度` | double | 4.0 | 碰撞检测灵敏度系数 |
| `分离推进器额外推力秒数` | long | 2000 | 分离推进器工作时间 (ms) |
| `热发射额外时长` | long | 1000 | 热发射清除时间 (ms) |

##### 控制参数

| 属性名 | 类型 | 默认值 | 描述 |
|--------|------|--------|------|
| `陀螺仪外环P` | double | 5.0 | 外环比例增益 |
| `陀螺仪外环I` | double | 0.0 | 外环积分增益 |
| `陀螺仪外环D` | double | 4.0 | 外环微分增益 |
| `陀螺仪内环P` | double | 2.0 | 内环比例增益 |
| `陀螺仪内环I` | double | 0.3 | 内环积分增益 |
| `陀螺仪内环D` | double | 0.15 | 内环微分增益 |

#### 方法 (Methods)

```csharp
public 参数管理器(MyIni ini = null)
```
**描述：** 构造函数，可选地从INI配置加载参数。  
**参数：**
- `ini` - MyIni对象，包含配置数据

---

## 导弹状态量

### 枚举：`导弹状态机`

定义导弹的所有可能状态。

| 状态值 | 描述 |
|--------|------|
| `初始化状态` | 系统正在初始化 |
| `待机状态` | 等待发射信号 |
| `热发射阶段` | 离架后的清除阶段 |
| `搜索目标` | 主动搜索目标 |
| `跟踪目标` | 跟踪已锁定目标 |
| `预测制导` | 使用预测轨迹制导 |
| `测试状态` | 测试模式 |
| `引爆激发` | 第一阶段引爆 |
| `引爆最终` | 最终引爆 |

### 类：`导弹状态量`

存储导弹状态和制导数据的类。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `当前状态` | 导弹状态机 | 当前状态 |
| `上次状态` | 导弹状态机 | 上一个状态 |
| `上次真实目标位置` | Vector3D | 上次从AI获取的目标位置 |
| `上次预测目标位置` | Vector3D | 上次预测的目标位置 |
| `角度误差在容忍范围内` | bool | 是否对准目标 |
| `制导命令` | Vector3D | 当前制导加速度命令 |
| `导弹最大过载` | double | 可用最大加速度 |
| `导航常数` | double | 当前导航常数 |
| `等待二阶段引爆` | bool | 是否等待第二阶段引爆 |

#### 方法 (Methods)

```csharp
public 导弹状态量()
```
**描述：** 构造函数，初始化所有状态变量。

```csharp
public StringBuilder 获取导弹诊断信息()
```
**描述：** 返回当前导弹状态的诊断信息。  
**返回：** StringBuilder包含格式化的诊断文本

---

## TargetTracker

### 结构：`SimpleTargetInfo`

存储目标信息的结构体。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `Position` | Vector3D | 目标位置 |
| `Velocity` | Vector3D | 目标速度 |
| `Acceleration` | Vector3D | 目标加速度 |
| `TimeStamp` | long | 时间戳 (ms) |

#### 方法 (Methods)

```csharp
public SimpleTargetInfo(Vector3D position, Vector3D velocity, Vector3D acceleration, long timeStamp)
```
**描述：** 构造函数。

```csharp
public static SimpleTargetInfo FromDetectedInfo(MyDetectedEntityInfo info)
```
**描述：** 从游戏的MyDetectedEntityInfo创建SimpleTargetInfo。  
**参数：**
- `info` - 游戏提供的目标信息  
**返回：** SimpleTargetInfo对象

### 结构：`CircularMotionParams`

存储圆周运动参数。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `Center` | Vector3D | 圆心位置 |
| `Radius` | double | 圆周半径 |
| `AngularVelocity` | double | 角速度 (rad/s) |
| `PlaneNormal` | Vector3D | 圆平面法向量 |
| `IsValid` | bool | 参数是否有效 |

### 类：`TargetTracker`

高级目标跟踪和预测系统。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `circlingRadius` | double | 当前检测到的环绕半径 |
| `linearWeight` | double | 线性预测权重 |
| `circularWeight` | double | 圆周预测权重 |
| `linearError` | double | 线性预测误差 (m/s) |
| `circularError` | double | 圆周预测误差 (m/s) |

#### 方法 (Methods)

```csharp
public TargetTracker(参数管理器 参数管理器, IMyProgrammableBlock Me)
```
**描述：** 构造函数。  
**参数：**
- `参数管理器` - 参数管理器实例
- `Me` - 编程块引用

```csharp
public void UpdateTarget(SimpleTargetInfo newTarget)
```
**描述：** 更新目标信息。  
**参数：**
- `newTarget` - 新的目标信息

```csharp
public Vector3D PredictTargetPosition(Vector3D missilePos, Vector3D missileVel, long currentTime)
```
**描述：** 预测目标未来位置。  
**参数：**
- `missilePos` - 导弹当前位置
- `missileVel` - 导弹当前速度
- `currentTime` - 当前时间戳 (ms)  
**返回：** 预测的目标位置

```csharp
public double CalculateClosingVelocity(Vector3D missilePos, Vector3D missileVel, Vector3D targetPos, Vector3D targetVel)
```
**描述：** 计算接近速度。  
**参数：**
- `missilePos` - 导弹位置
- `missileVel` - 导弹速度
- `targetPos` - 目标位置
- `targetVel` - 目标速度  
**返回：** 接近速度 (m/s)，正值表示接近

```csharp
public CircularMotionParams DetectCircularMotion()
```
**描述：** 检测目标是否进行圆周运动。  
**返回：** 圆周运动参数，如果未检测到则IsValid为false

---

## 推进系统

### 类：`推进系统`

管理导弹推进器的类。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `参考驾驶舱` | IMyControllerCompat | 参考坐标系方块 |
| `已初始化` | bool | 是否完成初始化 |
| `初始化消息` | string | 初始化状态消息 |
| `轴向最大推力` | Dictionary<string, double> | 各方向最大推力 |

#### 方法 (Methods)

```csharp
public 推进系统(参数管理器 参数管理器, IMyProgrammableBlock Me)
```
**描述：** 构造函数。

```csharp
public void 初始化(IMyBlockGroup 方块组, IMyControllerCompat 控制器)
```
**描述：** 从方块组初始化推进器系统。  
**参数：**
- `方块组` - 包含推进器的方块组
- `控制器` - 参考控制器

```csharp
public void 设置推进器方向输出(Vector3D 归一化方向, double 输出百分比)
```
**描述：** 按指定方向和强度设置推进器输出。  
**参数：**
- `归一化方向` - 期望推力方向（导弹坐标系，归一化）
- `输出百分比` - 推力百分比 (0-1)

```csharp
public Vector3D 计算各轴最大加速度()
```
**描述：** 计算各坐标轴的最大可用加速度。  
**返回：** 包含X、Y、Z轴最大加速度的向量

```csharp
public double 估算质量()
```
**描述：** 估算导弹总质量。  
**返回：** 估算质量 (kg)

```csharp
public void 更新推进器状态(long 当前时间戳, bool 已离架)
```
**描述：** 更新推进器状态（处理分离推进器）。  
**参数：**
- `当前时间戳` - 当前时间 (ms)
- `已离架` - 是否已离开发射架

---

## 陀螺仪瞄准系统

### 类：`陀螺仪瞄准系统`

姿态控制系统，使用双环PID控制陀螺仪。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `参考驾驶舱` | IMyControllerCompat | 参考坐标系方块 |
| `已初始化` | bool | 是否完成初始化 |
| `初始化消息` | string | 初始化状态消息 |

#### 方法 (Methods)

```csharp
public 陀螺仪瞄准系统(参数管理器 参数管理器, IMyProgrammableBlock Me)
```
**描述：** 构造函数。

```csharp
public void 初始化(IMyBlockGroup 方块组, IMyControllerCompat 参考驾驶舱)
```
**描述：** 从方块组初始化陀螺仪系统。  
**参数：**
- `方块组` - 包含陀螺仪的方块组
- `参考驾驶舱` - 参考控制器

```csharp
public void 瞄准(Vector3D 世界方向, long 当前时间戳)
```
**描述：** 控制导弹转向指定世界坐标方向。  
**参数：**
- `世界方向` - 目标方向（世界坐标系）
- `当前时间戳` - 当前时间 (ms)

```csharp
public bool 获取角度误差状态()
```
**描述：** 获取当前是否对准目标。  
**返回：** true如果角度误差在容忍范围内

```csharp
public void 关机()
```
**描述：** 关闭所有陀螺仪覆写。

```csharp
public StringBuilder 获取诊断信息()
```
**描述：** 获取陀螺仪系统诊断信息。  
**返回：** StringBuilder包含诊断文本

---

## 导弹识别器

### 类：`导弹识别器`

使用BFS算法识别导弹网格上的所有方块。

#### 属性 (Properties)

| 属性名 | 类型 | 描述 |
|--------|------|------|
| `IsTraversalComplete` | bool | BFS遍历是否完成 |
| `遍历状态` | string | 当前遍历进度信息 |

#### 方法 (Methods)

```csharp
public 导弹识别器(参数管理器 参数管理器, Action<string> Echo)
```
**描述：** 构造函数。  
**参数：**
- `参数管理器` - 参数管理器实例
- `Echo` - 输出函数

```csharp
public void 初始化(IMyTerminalBlock 起点方块)
```
**描述：** 初始化BFS遍历，以指定方块为起点。  
**参数：**
- `起点方块` - BFS起点（通常是编程块）

```csharp
public void 遍历分步执行(int 每帧最大处理数 = 100)
```
**描述：** 执行一步BFS遍历（分帧处理）。  
**参数：**
- `每帧最大处理数` - 本次调用最多处理多少个方块位置

```csharp
public HashSet<IMyTerminalBlock> 获取遍历结果()
```
**描述：** 获取所有发现的终端方块。  
**返回：** 包含所有终端方块的HashSet

---

## Utils

### 类：`PID`

标准PID控制器实现。

#### 方法 (Methods)

```csharp
public PID(double kp, double ki, double kd, double dt)
```
**描述：** 构造函数。  
**参数：**
- `kp` - 比例增益
- `ki` - 积分增益
- `kd` - 微分增益
- `dt` - 时间步长

```csharp
public void SetOutputLimits(double min, double max)
```
**描述：** 设置输出限制范围。  
**参数：**
- `min` - 最小输出值
- `max` - 最大输出值

```csharp
public void SetIntegralLimits(double min, double max)
```
**描述：** 设置积分项限制范围。  
**参数：**
- `min` - 最小积分值
- `max` - 最大积分值

```csharp
public double GetOutput(double error)
```
**描述：** 计算PID输出。  
**参数：**
- `error` - 当前误差  
**返回：** PID控制输出

```csharp
public void Reset()
```
**描述：** 重置PID控制器状态。

### 类：`PID3`

三轴PID控制器（用于俯仰、偏航、滚转）。

#### 方法 (Methods)

```csharp
public PID3(double kp, double ki, double kd, double dt)
```
**描述：** 构造函数，创建三个独立的PID控制器。

```csharp
public Vector3 GetOutput(Vector3 error)
```
**描述：** 计算三轴PID输出。  
**参数：**
- `error` - 三轴误差向量  
**返回：** 三轴控制输出

```csharp
public void SetOutputLimits(double min, double max)
```
**描述：** 设置所有三个轴的输出限制。

```csharp
public void Reset()
```
**描述：** 重置所有三个PID控制器。

### 类：`MovingAverageQueue<T>`

泛型移动平均队列。

#### 方法 (Methods)

```csharp
public MovingAverageQueue(int maxSize, 
                         Func<T, T, T> addFunc,
                         Func<T, T, T> subtractFunc,
                         Func<T, int, T> divideFunc)
```
**描述：** 构造函数。  
**参数：**
- `maxSize` - 队列最大长度
- `addFunc` - 加法函数
- `subtractFunc` - 减法函数
- `divideFunc` - 除法函数

```csharp
public void Enqueue(T value)
```
**描述：** 添加新值到队列。  
**参数：**
- `value` - 要添加的值

```csharp
public T GetAverage()
```
**描述：** 获取当前平均值。  
**返回：** 队列中所有值的平均值

```csharp
public void Clear()
```
**描述：** 清空队列。

---

## 使用示例 (Usage Examples)

### 示例1：初始化系统

```csharp
// 创建参数管理器
参数管理器 参数们 = new 参数管理器();

// 创建目标跟踪器
TargetTracker 跟踪器 = new TargetTracker(参数们, Me);

// 创建推进系统
推进系统 推进器 = new 推进系统(参数们, Me);
推进器.初始化(方块组, 控制器);

// 创建陀螺仪系统
陀螺仪瞄准系统 陀螺仪 = new 陀螺仪瞄准系统(参数们, Me);
陀螺仪.初始化(方块组, 控制器);
```

### 示例2：目标预测和制导

```csharp
// 更新目标信息
SimpleTargetInfo 目标 = SimpleTargetInfo.FromDetectedInfo(战斗块.GetTargetInfo());
跟踪器.UpdateTarget(目标);

// 预测目标位置
Vector3D 预测位置 = 跟踪器.PredictTargetPosition(
    导弹位置, 导弹速度, 当前时间戳);

// 计算制导命令
Vector3D 视线向量 = 预测位置 - 导弹位置;
double 接近速度 = 跟踪器.CalculateClosingVelocity(
    导弹位置, 导弹速度, 预测位置, 目标速度);
Vector3D 制导命令 = 计算比例导航(视线向量, 接近速度);

// 应用控制
陀螺仪.瞄准(制导命令, 当前时间戳);
推进器.设置推进器方向输出(制导命令, 1.0);
```

### 示例3：BFS遍历

```csharp
// 初始化识别器
导弹识别器 识别器 = new 导弹识别器(参数们, Echo);
识别器.初始化(Me);

// 分帧执行遍历
while (!识别器.IsTraversalComplete)
{
    识别器.遍历分步执行(100);
    Echo(识别器.遍历状态);
    // 等待下一帧
}

// 获取结果
var 所有方块 = 识别器.获取遍历结果();
```

---

## 注意事项 (Notes)

1. 所有位置和方向向量均使用世界坐标系，除非特别说明。
2. 时间戳单位为毫秒（ms）。
3. 速度单位为米每秒（m/s）。
4. 加速度单位为米每秒平方（m/s²）。
5. 角度单位为弧度（rad）。
6. PID控制器的时间步长（dt）应与实际帧时间一致。

---

本API参考文档涵盖了系统的主要公共接口。如需了解内部实现细节，请参考源代码。
