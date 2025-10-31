# 制导算法详解 (Guidance Algorithm Documentation)

## 概述 (Overview)

本文档详细描述AI主动弹制导系统中使用的核心算法，包括制导法、预测算法和控制算法。

## 1. 比例导航制导法 (Proportional Navigation Guidance)

### 1.1 基本原理

比例导航（PN）是一种经典的制导方法，广泛应用于导弹制导系统。其核心思想是：导弹的加速度命令与视线角速度成正比。

### 1.2 数学模型

#### 基本公式：
```
a_cmd = N * V_c * ω_los
```

其中：
- `a_cmd`: 制导加速度命令（m/s²）
- `N`: 导航常数（通常为3-5）
- `V_c`: 接近速度（closing velocity）（m/s）
- `ω_los`: 视线角速度（line-of-sight angular rate）（rad/s）

#### 增强型公式（本系统实现）：
```
a_cmd = N * V_c * ω_los + a_compensation
```

补偿项包括：
- 重力补偿
- 外力干扰补偿
- 目标加速度估计

### 1.3 关键计算

#### 1.3.1 视线向量（Line of Sight）
```
R = P_target - P_missile
r_los = R / |R|  (归一化)
```

#### 1.3.2 接近速度（Closing Velocity）
```
V_rel = V_target - V_missile
V_c = -V_rel · r_los  (投影到视线方向)
```

注意：V_c为正表示目标接近，为负表示目标远离。

#### 1.3.3 视线角速度（LOS Rate）
```
ω_los = (R × V_rel) / |R|²
```

这是三维向量，方向垂直于视线和相对速度构成的平面。

#### 1.3.4 制导命令
```
a_lateral = N * V_c * ω_los
```

这给出垂直于视线方向的横向加速度命令。

### 1.4 动态导航常数

系统根据接战条件动态调整导航常数 N：

```csharp
double 距离 = Vector3D.Distance(导弹位置, 目标位置);
double 距离因子 = Math.Min(距离 / 2500.0, 1.0);
N = 导航常数最小值 + (导航常数最大值 - 导航常数最小值) * 距离因子;
```

- 远距离：N → 最大值（5）- 更激进的机动
- 近距离：N → 最小值（3）- 更稳定的追踪

### 1.5 补偿项计算

#### 1.5.1 重力补偿
```csharp
Vector3D 重力加速度 = 控制器.GetNaturalGravity();
a_compensation += -重力加速度;
```

#### 1.5.2 外力干扰补偿
```csharp
// 测量实际加速度
Vector3D a_measured = (V_current - V_previous) / Δt;

// 预期加速度（仅来自推进器）
Vector3D a_expected = F_thrust / m_missile;

// 外力干扰
Vector3D a_disturbance = a_measured - a_expected;

// 使用移动平均平滑
外源扰动缓存.Enqueue(a_disturbance);
Vector3D a_disturbance_avg = 外源扰动缓存.GetAverage();

// 限制补偿量
if (|a_disturbance_avg| < 最大外力干扰)
    a_compensation += a_disturbance_avg;
```

#### 1.5.3 目标加速度估计
```csharp
// 从目标跟踪器获取估计的目标加速度
Vector3D a_target_est = 目标跟踪器.GetTargetAcceleration();
a_compensation += 0.5 * N * a_target_est;
```

### 1.6 距离衰减

在近距离时，补偿项权重降低以避免过度校正：

```csharp
if (距离 < 补偿项失效距离)
{
    double 衰减因子 = 距离 / 补偿项失效距离;
    a_compensation *= 衰减因子;
}
```

## 2. 目标运动预测算法 (Target Motion Prediction)

### 2.1 线性运动模型

#### 2.1.1 恒速模型
```
P(t) = P₀ + V₀ * t
```

#### 2.1.2 恒加速度模型
```
P(t) = P₀ + V₀ * t + 0.5 * a₀ * t²
V(t) = V₀ + a₀ * t
```

#### 2.1.3 实现
```csharp
public Vector3D PredictLinearPosition(double predictionTime)
{
    Vector3D predictedPosition = currentPosition + 
                                 currentVelocity * predictionTime +
                                 0.5 * estimatedAcceleration * predictionTime * predictionTime;
    return predictedPosition;
}
```

### 2.2 圆周运动检测与预测

#### 2.2.1 圆周运动参数估计

使用最近的位置历史数据拟合圆周运动：

**步骤：**
1. 收集历史位置点（至少3个）
2. 计算平面法向量（使用叉积）
3. 估计圆心位置
4. 计算半径
5. 确定角速度

**圆心估计算法：**
```
对于平面上三个点 P1, P2, P3：
1. 计算中垂线
   - L12: P1和P2的中垂线
   - L23: P2和P3的中垂线
2. 圆心 = L12 ∩ L23（中垂线交点）
```

**代码实现：**
```csharp
public CircularMotionParams DetectCircularMotion(List<SimpleTargetInfo> history)
{
    if (history.Count < 3) return CircularMotionParams.Invalid;
    
    // 取最近三个点
    var p1 = history[history.Count - 3].Position;
    var p2 = history[history.Count - 2].Position;
    var p3 = history[history.Count - 1].Position;
    
    // 计算平面法向量
    Vector3D v1 = p2 - p1;
    Vector3D v2 = p3 - p2;
    Vector3D normal = Vector3D.Cross(v1, v2);
    normal.Normalize();
    
    // 计算圆心（通过中垂线交点）
    Vector3D center = CalculateCircleCenter(p1, p2, p3, normal);
    
    // 计算半径
    double radius = Vector3D.Distance(center, p2);
    
    // 计算角速度
    double angularVelocity = CalculateAngularVelocity(history, center, normal);
    
    return new CircularMotionParams(center, radius, angularVelocity, normal);
}
```

#### 2.2.2 圆周位置预测

```csharp
public Vector3D PredictCircularPosition(CircularMotionParams circleParams, double predictionTime)
{
    // 当前在圆周上的角度
    Vector3D toCurrentPos = currentPosition - circleParams.Center;
    
    // 建立圆平面坐标系
    Vector3D u = toCurrentPos;
    u.Normalize();
    Vector3D v = Vector3D.Cross(circleParams.PlaneNormal, u);
    v.Normalize();
    
    // 角度增量
    double deltaTheta = circleParams.AngularVelocity * predictionTime;
    
    // 预测位置
    Vector3D predictedPos = circleParams.Center + 
                           circleParams.Radius * (Math.Cos(deltaTheta) * u + 
                                                  Math.Sin(deltaTheta) * v);
    
    return predictedPos;
}
```

### 2.3 混合预测模型

#### 2.3.1 误差评估
```csharp
// 线性预测误差
double linearError = Vector3D.Distance(actualPosition, linearPrediction);

// 圆周预测误差
double circularError = Vector3D.Distance(actualPosition, circularPrediction);
```

#### 2.3.2 权重计算

使用误差的倒数作为权重基础：
```csharp
double linearWeight, circularWeight;

if (linearError < 0.01 && circularError < 0.01)
{
    // 两者都很准确，保持当前权重
}
else
{
    // 基于误差调整权重
    double linearInvError = 1.0 / (linearError + 0.1);
    double circularInvError = 1.0 / (circularError + 0.1);
    double totalInv = linearInvError + circularInvError;
    
    linearWeight = linearInvError / totalInv;
    circularWeight = circularInvError / totalInv;
}

// 平滑权重变化
linearWeight = 0.9 * previousLinearWeight + 0.1 * linearWeight;
circularWeight = 0.9 * previousCircularWeight + 0.1 * circularWeight;
```

#### 2.3.3 最终预测
```csharp
Vector3D finalPrediction = linearWeight * linearPrediction + 
                          circularWeight * circularPrediction;
```

### 2.4 预测时间计算

系统根据当前几何关系自适应计算预测时间：

```csharp
public double CalculatePredictionTime()
{
    double distance = Vector3D.Distance(missilePos, targetPos);
    Vector3D relativeVelocity = targetVel - missileVel;
    double closingSpeed = -Vector3D.Dot(relativeVelocity, 
                                        (targetPos - missilePos).Normalized());
    
    if (closingSpeed > 最小接近加速度)
    {
        double timeToIntercept = distance / closingSpeed;
        return Math.Min(timeToIntercept, 最长接近预测时间 / 1000.0);
    }
    else
    {
        return 1.0; // 默认1秒预测
    }
}
```

## 3. 姿态控制算法 (Attitude Control)

### 3.1 双环PID控制架构

```
[目标方向] → [外环PID] → [期望角速度] → [内环PID] → [陀螺仪输出]
              (角度误差)                  (角速度误差)
```

### 3.2 角度误差计算

#### 3.2.1 方向向量到欧拉角
```csharp
private Vector3 计算角度误差(Vector3D 目标世界方向)
{
    // 将目标方向转换到导弹坐标系
    MatrixD worldToShip = MatrixD.Transpose(参考驾驶舱.WorldMatrix);
    Vector3D 目标导弹系方向 = Vector3D.TransformNormal(目标世界方向, worldToShip);
    
    // 计算欧拉角误差（俯仰、偏航、滚转）
    double pitch = Math.Atan2(目标导弹系方向.Y, 目标导弹系方向.Z);
    double yaw = Math.Atan2(目标导弹系方向.X, 目标导弹系方向.Z);
    double roll = 0; // 通常不控制滚转
    
    return new Vector3((float)pitch, (float)yaw, (float)roll);
}
```

### 3.3 外环PID控制器

**输入：** 角度误差 θ_error（俯仰、偏航、滚转）  
**输出：** 期望角速度 ω_desired

```csharp
Vector3 期望角速度PYR = 外环PID控制器PYR.GetOutput(角度误差PYR);
```

**PID方程：**
```
ω_desired = Kp * θ_error + Ki * ∫θ_error dt + Kd * dθ_error/dt
```

**参数：**
- Kp = 5.0 (比例增益)
- Ki = 0.0 (积分增益 - 通常不用)
- Kd = 4.0 (微分增益)

### 3.4 内环PID控制器

**输入：** 角速度误差 ω_error = ω_desired - ω_actual  
**输出：** 陀螺仪控制信号

```csharp
Vector3 陀螺仪设定PYR = 内环PID控制器PYR.GetOutput(角速度误差PYR);
```

**参数：**
- Kp = 2.0
- Ki = 0.3 (小积分增益消除稳态误差)
- Kd = 0.15

### 3.5 积分抗饱和

防止积分项在控制饱和时持续累积：

```csharp
// Back-calculation 方法
if (output != unclampedOutput)
{
    double excessError = (unclampedOutput - output) / _ki;
    _integral -= excessError * _backCalculationFactor;
}
```

### 3.6 陀螺仪输出转换

```csharp
private void 应用陀螺仪设置(Vector3 陀螺仪设定PYR)
{
    foreach (var 陀螺仪 in 陀螺仪列表)
    {
        // 转换到陀螺仪本地坐标系
        Vector3D 本地设定 = 转换到陀螺仪坐标系(陀螺仪设定PYR, 陀螺仪);
        
        // 应用到陀螺仪
        陀螺仪.Pitch = (float)本地设定.X;
        陀螺仪.Yaw = (float)本地设定.Y;
        陀螺仪.Roll = (float)本地设定.Z;
    }
}
```

## 4. 引爆逻辑算法 (Detonation Logic)

### 4.1 近炸引信

```csharp
double 目标距离 = Vector3D.Distance(导弹位置, 目标位置);

if (目标距离 < 引爆距离阈值)
{
    触发引爆();
}
```

### 4.2 碰炸引信

检测导弹是否发生碰撞：

```csharp
// 计算实际加速度
Vector3D 实际加速度 = (当前速度 - 上次速度) / 时间间隔;
double 实际加速度大小 = 实际加速度.Length();

// 计算理论最大加速度
double 理论最大加速度 = 导弹最大过载;

// 碰撞检测
if (目标距离 < 碰炸解锁距离 && 
    实际加速度大小 > Math.Sqrt(碰炸迟缓度) * 理论最大加速度)
{
    触发碰炸();
}
```

原理：碰撞时导弹会受到很大的冲击力，导致实际加速度远超推进器能提供的加速度。

### 4.3 分级引爆

```
第一阶段：激发雷管组
  ↓ (延迟约1帧)
第二阶段：引爆雷管组
```

这种设计模拟真实弹头的两级引爆机制，提高可靠性。

## 5. 数值方法与优化 (Numerical Methods)

### 5.1 移动平均滤波

用于平滑噪声数据：

```csharp
public class MovingAverageQueue<T>
{
    private Queue<T> queue;
    private T sum;
    private int maxSize;
    
    public T GetAverage()
    {
        return Divide(sum, queue.Count);
    }
    
    public void Enqueue(T value)
    {
        queue.Enqueue(value);
        sum = Add(sum, value);
        
        if (queue.Count > maxSize)
        {
            T removed = queue.Dequeue();
            sum = Subtract(sum, removed);
        }
    }
}
```

### 5.2 向量归一化安全处理

```csharp
public static Vector3D SafeNormalize(Vector3D vector, double minLength = 1e-6)
{
    double length = vector.Length();
    if (length < minLength)
        return Vector3D.Zero;
    return vector / length;
}
```

### 5.3 数值稳定性

- 除零保护
- 最小向量长度检查
- 输出限幅
- 积分限幅

## 6. 性能分析 (Performance Analysis)

### 6.1 计算复杂度

| 算法模块 | 时间复杂度 | 每帧调用次数 |
|---------|-----------|------------|
| 比例导航 | O(1) | 1 |
| 线性预测 | O(1) | 1 |
| 圆周检测 | O(n) | 按需 |
| PID控制 | O(1) | 1 |
| 移动平均 | O(1) | 多次 |

### 6.2 优化策略

1. **缓存重复计算**
   - 归一化向量
   - 距离计算
   - 坐标变换矩阵

2. **分帧处理**
   - 圆周运动检测不是每帧都做
   - BFS遍历分多帧

3. **早期退出**
   - 条件不满足时跳过计算
   - 状态机门控执行

---

本文档提供了系统核心算法的数学基础和实现细节。如需进一步了解，请参考源代码中的注释。
