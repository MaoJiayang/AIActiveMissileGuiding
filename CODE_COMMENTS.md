# 代码注释说明 (Code Comments Guide)

## 概述 (Overview)

本文档解释代码中的关键注释和复杂逻辑，帮助理解系统实现细节。

## Program.cs - 主程序

### 状态机控制

#### 更新导弹状态函数
```csharp
private void 更新导弹状态(Vector3D 当前目标位置, string argument = "")
```

**关键逻辑：**
1. **目标有效性检查**
   ```csharp
   bool 有有效目标 = !当前目标位置.Equals(Vector3D.NegativeInfinity);
   ```
   使用 `Vector3D.NegativeInfinity` 作为"无目标"的标记值。

2. **保险解除逻辑**
   ```csharp
   bool 导弹保险解除 = 
       参数们.手动保险超控 ||
       导弹状态信息.当前状态 != 导弹状态机.待机状态 && ...
   ```
   保险机制防止在待机状态意外引爆。只有离开待机状态或手动解除保险才能引爆。

3. **引爆条件检查**
   - 传感器触发：传感器检测到目标
   - 距离触发：距离小于引爆阈值
   - 碰撞触发：检测到异常大的减速度

### 比例导航制导算法

```csharp
private Vector3D 比例导航制导(IMyControllerCompat 控制器, SimpleTargetInfo 目标信息)
```

**算法步骤详解：**

#### 步骤1: 获取基本状态
```csharp
Vector3D 视线 = 目标位置 - 导弹位置;
double 距离 = 视线.Length();
Vector3D 视线单位向量 = 视线 / 距离;
Vector3D 相对速度 = 目标速度 - 导弹速度;
```
- `视线`: 从导弹指向目标的向量
- `相对速度`: 目标相对于导弹的速度

#### 步骤2: 计算视线角速度
```csharp
Vector3D 视线角速度 = Vector3D.Cross(视线, 相对速度) / 
                    Math.Max(视线.LengthSquared(), 参数们.最小向量长度);
```
**物理意义：** 视线方向的旋转速率，是比例导航的核心。

**公式推导：**
```
ω_los = (R × V_rel) / |R|²
```
其中叉积给出垂直于视线平面的角速度向量。

#### 步骤3: 标准比例导航项
```csharp
Vector3D 比例导航加速度 = 导弹状态信息.导航常数 * 相对速度大小 * 
                         Vector3D.Cross(视线角速度, 视线单位向量);
```
**公式：** `a = N * V_c * ω_los × r_los`

这产生一个垂直于视线的横向加速度，目标是使视线角速度归零（即保持视线方向不变，这会导致碰撞）。

#### 步骤4: 微分补偿项
```csharp
敌加速度缓存.AddFirst(目标信息.Acceleration);
Vector3D 目标加速度 = 敌加速度缓存.Average;
Vector3D 横向加速度 = Vector3D.Cross(视线单位向量, 目标加速度);
比例导航加速度 += 0.5 * 导弹状态信息.导航常数 * 横向加速度;
```
**目的：** 预测并补偿目标的机动动作。

使用移动平均平滑目标加速度估计，提高稳定性。

#### 步骤5: 外力干扰补偿
```csharp
if (参数们.启用外力干扰 && 控制器 != null)
{
    // 计算外源扰动
    Vector3D 导弹加速度 = (导弹速度 - 上次导弹速度) / 参数们.时间常数;
    Vector3D 理论加速度 = 推进器系统.计算当前推力() / 推进器系统.估算质量();
    Vector3D 外源扰动 = 导弹加速度 - 理论加速度 - 控制器.GetNaturalGravity();
    
    // 移动平均平滑
    外源扰动缓存.Enqueue(外源扰动);
    Vector3D 平均外源扰动 = 外源扰动缓存.GetAverage();
    
    // 限幅并补偿
    if (平均外源扰动.Length() < 参数们.最大外力干扰)
    {
        比例导航加速度 += 平均外源扰动;
    }
}
```

**原理：**
- 测量实际加速度（从速度变化计算）
- 计算理论加速度（推进器推力÷质量）
- 差值即为外力干扰（如风、碰撞等）
- 补偿这个干扰以保持制导精度

#### 步骤6: 攻击角度约束
```csharp
if (参数们.启用攻击角度约束)
{
    Vector3D 攻击角度约束加速度 = 计算攻击角度约束加速度(导弹速度, 距离);
    比例导航加速度 += 攻击角度约束加速度;
}
```

优化攻击角度，使导弹从更有利的角度接近目标。

#### 步骤7: 重力补偿
```csharp
Vector3D 重力 = 控制器.GetNaturalGravity();
比例导航加速度 -= 重力;
```
抵消重力影响，使制导命令仅反映所需的机动加速度。

#### 步骤8: 距离衰减
```csharp
if (距离 < 参数们.补偿项失效距离)
{
    double 衰减因子 = 距离 / 参数们.补偿项失效距离;
    // 仅对补偿项应用衰减，不影响基本PN项
}
```
近距离时降低补偿项权重，避免过度校正导致振荡。

#### 步骤9: 过载限制
```csharp
double 导弹最大过载 = 推进器系统.计算各轴最大加速度().Length();
if (比例导航加速度.Length() > 导弹最大过载)
{
    比例导航加速度 = 比例导航加速度.Normalized() * 导弹最大过载;
}
```
确保制导命令不超过导弹物理能力。

### 挂架分离检测

```csharp
private bool 检查挂架分离()
{
    // 检查连接器
    foreach (var 连接器 in 连接器列表)
    {
        if (!连接器.Enabled || 连接器.Status != MyShipConnectorStatus.Connected)
            return true; // 连接器断开或禁用
    }
    
    // 检查合并方块
    foreach (var 合并块 in 合并方块列表)
    {
        if (!合并块.Enabled)
            return true; // 合并块禁用
    }
    
    // 检查转子
    foreach (var 转子 in 转子列表)
    {
        if (!转子.IsAttached)
            return true; // 转子头分离
    }
    
    return false; // 所有挂架仍连接
}
```

**逻辑说明：**
- 连接器：检查Enabled状态和连接状态
- 合并方块：仅检查Enabled（禁用即分离）
- 转子：检查IsAttached（转子头是否连接）

### 引爆逻辑

#### 距离引爆
```csharp
private bool 检查距离触发()
{
    if (目标跟踪器.GetHistoryCount() == 0) return false;
    
    Vector3D 目标位置 = 目标跟踪器.GetLatestTarget().Position;
    double 距离 = Vector3D.Distance(控制器.GetPosition(), 目标位置);
    
    return 距离 < 参数们.引爆距离阈值;
}
```
简单的距离检测，触发近炸引信。

#### 碰撞引爆
```csharp
private bool 检查碰撞触发()
{
    // 只在近距离启用碰炸
    double 距离 = Vector3D.Distance(控制器.GetPosition(), 目标位置);
    if (距离 > 参数们.碰炸解锁距离) return false;
    
    // 计算实际加速度
    Vector3D 当前速度 = 控制器.GetShipVelocities().LinearVelocity;
    Vector3D 实际加速度 = (当前速度 - 上次导弹速度) / 参数们.时间常数;
    double 实际加速度大小 = 实际加速度.Length();
    
    // 理论最大加速度
    double 理论最大 = 导弹状态信息.导弹最大过载;
    
    // 碰撞判定：实际加速度远超理论值
    return 实际加速度大小 > Math.Sqrt(参数们.碰炸迟缓度) * 理论最大;
}
```

**原理：**
碰撞时，导弹会受到冲击力，产生远超推进器能力的减速度。通过检测这个异常加速度判断碰撞。

`碰炸迟缓度` 参数控制灵敏度：
- 值越小，越容易触发（更灵敏）
- 值越大，需要更大冲击才触发（更钝感）

## TargetTracker.cs - 目标跟踪器

### 圆周运动检测

```csharp
public CircularMotionParams DetectCircularMotion()
{
    if (targetHistory.Count < 3) 
        return CircularMotionParams.Invalid;
    
    // 取最近三个点
    var p1 = targetHistory[targetHistory.Count - 3].Position;
    var p2 = targetHistory[targetHistory.Count - 2].Position;
    var p3 = targetHistory[targetHistory.Count - 1].Position;
    
    // 计算平面法向量
    Vector3D v1 = p2 - p1;
    Vector3D v2 = p3 - p2;
    Vector3D normal = Vector3D.Cross(v1, v2);
    
    // 检查是否共线（叉积接近零）
    if (normal.LengthSquared() < 1e-10)
        return CircularMotionParams.Invalid;
    
    normal.Normalize();
    
    // 计算圆心（两条中垂线的交点）
    Vector3D center = CalculateCircleCenter(p1, p2, p3, normal);
    
    // 计算半径
    double radius = Vector3D.Distance(center, p2);
    
    // 计算角速度
    double angularVelocity = CalculateAngularVelocity();
    
    return new CircularMotionParams(center, radius, angularVelocity, normal);
}
```

**几何原理：**
1. 三点确定一个圆
2. 计算平面法向量：v1 × v2
3. 在该平面上，圆心是两条中垂线的交点
4. 半径是圆心到任一点的距离
5. 角速度从历史位置的角度变化率估算

### 混合预测权重计算

```csharp
private void UpdatePredictionWeights()
{
    // 计算预测误差
    double linearError = Vector3D.Distance(actualPosition, linearPrediction);
    double circularError = Vector3D.Distance(actualPosition, circularPrediction);
    
    // 基于误差的权重
    double linearInvError = 1.0 / (linearError + 0.1);  // +0.1避免除零
    double circularInvError = 1.0 / (circularError + 0.1);
    double totalInv = linearInvError + circularInvError;
    
    double newLinearWeight = linearInvError / totalInv;
    double newCircularWeight = circularInvError / totalInv;
    
    // 平滑权重变化（低通滤波）
    linearWeight = 0.9 * linearWeight + 0.1 * newLinearWeight;
    circularWeight = 0.9 * circularWeight + 0.1 * newCircularWeight;
}
```

**算法说明：**
- 误差小的模型获得更高权重
- 使用指数移动平均平滑权重变化
- 避免权重剧烈跳动导致预测不稳定

## 陀螺仪系统.cs - 姿态控制

### 双环PID控制

```csharp
public void 瞄准(Vector3D 世界方向, long 当前时间戳)
{
    // 外环：角度误差 → 期望角速度
    Vector3 角度误差PYR = 计算角度误差(世界方向);
    Vector3 期望角速度PYR = 外环PID控制器PYR.GetOutput(角度误差PYR);
    
    // 内环：角速度误差 → 陀螺仪输出
    Vector3 当前角速度PYR = 获取当前角速度();
    Vector3 角速度误差PYR = 期望角速度PYR - 当前角速度PYR;
    Vector3 陀螺仪设定PYR = 内环PID控制器PYR.GetOutput(角速度误差PYR);
    
    // 应用到陀螺仪
    应用陀螺仪设置(陀螺仪设定PYR);
}
```

**为什么使用双环？**
- **外环（角度）：** 确保达到目标姿态
- **内环（角速度）：** 提供阻尼，防止振荡

类似于位置-速度双环控制，提高响应速度和稳定性。

### 坐标系转换

```csharp
private void 应用陀螺仪设置(Vector3 陀螺仪设定PYR)
{
    foreach (var 陀螺仪 in 陀螺仪列表)
    {
        // 将控制器坐标系的设定转换到陀螺仪本地坐标系
        Vector3D 本地设定 = Vector3D.TransformNormal(
            陀螺仪设定PYR, 
            陀螺仪.WorldMatrix * 控制器.WorldMatrix.Invert()
        );
        
        陀螺仪.Pitch = (float)本地设定.X;
        陀螺仪.Yaw = (float)本地设定.Y;
        陀螺仪.Roll = (float)本地设定.Z;
    }
}
```

**必要性：**
每个陀螺仪可能有不同的安装方向，需要将统一的控制命令转换到各自的本地坐标系。

## 推进系统.cs - 推进器管理

### 推进器分类

```csharp
private void 分类推进器(IMyControllerCompat 控制器)
{
    foreach (var 推进器 in 推进器列表)
    {
        // 获取推力方向（推进器坐标系）
        Vector3D 推力方向本地 = 推进器.WorldMatrix.Backward;
        
        // 转换到控制器坐标系
        Vector3D 推力方向 = Vector3D.TransformNormal(
            推力方向本地,
            推进器.WorldMatrix * 控制器.WorldMatrix.Invert()
        );
        
        // 根据主方向分类
        if (Math.Abs(推力方向.X) > 0.9)
            推进器推力方向组[推力方向.X > 0 ? "XP" : "XN"].Add(推进器);
        else if (Math.Abs(推力方向.Y) > 0.9)
            推进器推力方向组[推力方向.Y > 0 ? "YP" : "YN"].Add(推进器);
        else if (Math.Abs(推力方向.Z) > 0.9)
            推进器推力方向组[推力方向.Z > 0 ? "ZP" : "ZN"].Add(推进器);
    }
}
```

**分类逻辑：**
- 计算推力方向在控制器坐标系中的表示
- 根据最大分量（>0.9）确定主方向
- 分为六组：±X, ±Y, ±Z

### 方向推力输出

```csharp
public void 设置推进器方向输出(Vector3D 归一化方向, double 输出百分比)
{
    // 分解到三个轴
    double xComponent = 归一化方向.X;
    double yComponent = 归一化方向.Y;
    double zComponent = 归一化方向.Z;
    
    // 设置正方向推进器
    foreach (var 推进器 in 推进器推力方向组["XP"])
        推进器.ThrustOverridePercentage = xComponent > 0 ? 
            (float)(xComponent * 输出百分比) : 0f;
    
    // 设置负方向推进器
    foreach (var 推进器 in 推进器推力方向组["XN"])
        推进器.ThrustOverridePercentage = xComponent < 0 ? 
            (float)(-xComponent * 输出百分比) : 0f;
    
    // Y和Z轴类似...
}
```

**原理：**
将期望加速度向量分解到各轴，只激活对应方向的推进器。

## 导弹识别器.cs - BFS遍历

### 分帧BFS实现

```csharp
public void 遍历分步执行(int 每帧最大处理数 = 100)
{
    int 处理计数 = 0;
    
    while (queue.Count > 0 && 处理计数 < 每帧最大处理数)
    {
        位置信息 当前 = queue.Dequeue();
        Vector3I 当前位置 = 当前.位置;
        
        // 检查该位置的方块
        IMySlimBlock slimBlock = grid.GetCubeBlock(当前位置);
        if (slimBlock != null)
        {
            blockCount++;
            
            // 如果是终端方块，加入结果集
            if (slimBlock.FatBlock is IMyTerminalBlock terminalBlock)
            {
                遍历结果列表.Add(terminalBlock);
            }
        }
        
        // 将相邻位置加入队列
        foreach (var dir in directions)
        {
            Vector3I 邻居位置 = 当前位置 + dir;
            if (!visited.Contains(邻居位置))
            {
                visited.Add(邻居位置);
                queue.Enqueue(new 位置信息(邻居位置));
            }
        }
        
        处理计数++;
    }
}
```

**分帧策略：**
- 限制每帧处理的方块数量
- 保持队列和访问集在调用间持久化
- 多次调用直到队列为空
- 避免大型导弹一次性遍历导致卡顿

## Utils.cs - 工具类

### PID抗饱和

```csharp
public double GetOutput(double error)
{
    // 计算PID项
    _integral += error * _dt;
    double derivative = (error - _prevError) / _dt;
    _prevError = error;
    
    double output = _kp * error + _ki * _integral + _kd * derivative;
    
    // 抗饱和机制（Back-calculation）
    if (_useBackCalculation)
    {
        double unclampedOutput = output;
        
        // 限幅
        output = Math.Max(_outputMin, Math.Min(_outputMax, output));
        
        // 如果发生饱和，调整积分项
        if (output != unclampedOutput && Math.Abs(_ki) > 1e-10)
        {
            double excessError = (unclampedOutput - output) / _ki;
            _integral -= excessError * _backCalculationFactor;
        }
    }
    
    return output;
}
```

**抗饱和原理：**
- 当输出饱和时，继续累积积分会使系统响应变慢
- Back-calculation方法：检测饱和，减少积分项
- `_backCalculationFactor` 控制调整速度（通常0.1）

### 移动平均队列

```csharp
public class MovingAverageQueue<T>
{
    private Queue<T> queue;
    private T sum;  // 保持总和，避免每次重新计算
    
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
    
    public T GetAverage()
    {
        return Divide(sum, queue.Count);
    }
}
```

**优化：**
- 维护运行总和，O(1)时间获取平均值
- 使用泛型和函数委托支持任意类型
- 适用于Vector3D、double等类型

## 性能注意事项

### 1. 避免每帧重复计算
```csharp
// 不好的做法
for (int i = 0; i < 100; i++)
{
    double result = ExpensiveCalculation();  // 每次都重新计算
    UseResult(result);
}

// 好的做法
double result = ExpensiveCalculation();  // 只计算一次
for (int i = 0; i < 100; i++)
{
    UseResult(result);
}
```

### 2. 使用条件更新
```csharp
// 不是每帧都更新推进器分类
if (更新计数器 % 参数们.动力系统更新间隔 == 0)
{
    推进器系统.更新推力信息();
}
```

### 3. 最小化对象分配
```csharp
// 重用StringBuilder而不是创建新的
诊断信息.Clear();
诊断信息.AppendLine("...");
```

## 总结

本代码实现了一个完整的导弹制导系统，关键特点：
- 模块化设计，职责明确
- 物理基础算法，性能可靠
- 分帧处理，避免卡顿
- 丰富的参数配置
- 鲁棒的错误处理

通过理解这些注释和设计决策，可以更好地维护和扩展系统。
