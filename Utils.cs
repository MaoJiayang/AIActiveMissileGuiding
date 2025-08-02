using System;
using VRageMath;
namespace IngameScript
{
    public class PID
    {
        double _kp, _ki, _kd;
        double _prevError, _integral;
        double _dt;

        // 输出限制
        private double _outputMax = double.MaxValue;
        private double _outputMin = double.MinValue;

        // 积分限制
        private double _integralMax = double.MaxValue;
        private double _integralMin = double.MinValue;

        // 抗饱和机制
        private bool _useBackCalculation = false;
        private double _backCalculationFactor = 0.1;

        public PID(double kp, double ki, double kd, double dt)
        {
            _kp = kp; _ki = ki; _kd = kd; _dt = dt;
            _prevError = 0;
            _integral = 0;
        }

        // 设置输出限制
        public void SetOutputLimits(double min, double max)
        {
            _outputMin = min;
            _outputMax = max;
            _useBackCalculation = true;  // 启用抗饱和
        }

        // 设置积分限制
        public void SetIntegralLimits(double min, double max)
        {
            _integralMin = min;
            _integralMax = max;
        }

        // 设置回馈系数
        public void SetBackCalculationFactor(double factor)
        {
            _backCalculationFactor = factor;
        }

        public double GetOutput(double error)
        {
            // 计算积分项
            _integral += error * _dt;

            // 应用积分限制
            if (_integral > _integralMax) _integral = _integralMax;
            if (_integral < _integralMin) _integral = _integralMin;

            // 计算微分项
            double derivative = (error - _prevError) / _dt;
            _prevError = error;

            // 计算PID输出
            double output = _kp * error + _ki * _integral + _kd * derivative;

            // 应用输出限制和抗饱和
            if (_useBackCalculation)
            {
                double unclampedOutput = output;

                // 限制输出
                if (output > _outputMax)
                    output = _outputMax;
                else if (output < _outputMin)
                    output = _outputMin;

                // 抗饱和积分调整(Back-calculation) - 当输出饱和时调整积分项
                if (output != unclampedOutput && Math.Abs(_ki) > 1e-10)
                {
                    double windupError = (output - unclampedOutput) * _backCalculationFactor;
                    _integral += windupError / _ki;

                    // 二次检查积分限制
                    if (_integral > _integralMax) _integral = _integralMax;
                    if (_integral < _integralMin) _integral = _integralMin;
                }
            }

            // 如果output为Nan，重置控制器
            if (double.IsNaN(output) || double.IsInfinity(output))
            {
                Reset();
            }
            return output;
        }

        // 重置控制器状态
        public void Reset()
        {
            _prevError = 0;
            _integral = 0;
        }
    }

    /// <summary>
    /// 三轴PID控制器，管理X、Y、Z三个独立的PID控制器
    /// </summary>
    public class PID3
    {
        private readonly PID _pidX;
        private readonly PID _pidY;
        private readonly PID _pidZ;
        public PID3(double kp, double ki, double kd, double dt)
        {
            _pidX = new PID(kp, ki, kd, dt);
            _pidY = new PID(kp, ki, kd, dt);
            _pidZ = new PID(kp, ki, kd, dt);
        }

        /// <summary>
        /// 使用不同参数初始化三个轴的PID控制器
        /// </summary>
        public PID3(double kpX, double kiX, double kdX,
                    double kpY, double kiY, double kdY,
                    double kpZ, double kiZ, double kdZ,
                    double dt)
        {
            _pidX = new PID(kpX, kiX, kdX, dt);
            _pidY = new PID(kpY, kiY, kdY, dt);
            _pidZ = new PID(kpZ, kiZ, kdZ, dt);
        }

        /// <summary>
        /// 设置所有轴的输出限制
        /// </summary>
        public void SetOutputLimits(double min, double max)
        {
            _pidX.SetOutputLimits(min, max);
            _pidY.SetOutputLimits(min, max);
            _pidZ.SetOutputLimits(min, max);
        }

        /// <summary>
        /// 分别设置各轴的输出限制
        /// </summary>
        public void SetOutputLimits(Vector3D min, Vector3D max)
        {
            _pidX.SetOutputLimits(min.X, max.X);
            _pidY.SetOutputLimits(min.Y, max.Y);
            _pidZ.SetOutputLimits(min.Z, max.Z);
        }

        /// <summary>
        /// 设置所有轴的积分限制
        /// </summary>
        public void SetIntegralLimits(double min, double max)
        {
            _pidX.SetIntegralLimits(min, max);
            _pidY.SetIntegralLimits(min, max);
            _pidZ.SetIntegralLimits(min, max);
        }

        /// <summary>
        /// 分别设置各轴的积分限制
        /// </summary>
        public void SetIntegralLimits(Vector3D min, Vector3D max)
        {
            _pidX.SetIntegralLimits(min.X, max.X);
            _pidY.SetIntegralLimits(min.Y, max.Y);
            _pidZ.SetIntegralLimits(min.Z, max.Z);
        }

        /// <summary>
        /// 设置所有轴的回馈系数
        /// </summary>
        public void SetBackCalculationFactor(double factor)
        {
            _pidX.SetBackCalculationFactor(factor);
            _pidY.SetBackCalculationFactor(factor);
            _pidZ.SetBackCalculationFactor(factor);
        }

        /// <summary>
        /// 分别设置各轴的回馈系数
        /// </summary>
        public void SetBackCalculationFactor(Vector3D factors)
        {
            _pidX.SetBackCalculationFactor(factors.X);
            _pidY.SetBackCalculationFactor(factors.Y);
            _pidZ.SetBackCalculationFactor(factors.Z);
        }

        /// <summary>
        /// 获取Vector3D形式的PID输出
        /// </summary>
        /// <param name="error">误差向量</param>
        /// <returns>控制输出向量</returns>
        public Vector3D GetOutput(Vector3D error)
        {
            double outputX = _pidX.GetOutput(error.X);
            double outputY = _pidY.GetOutput(error.Y);
            double outputZ = _pidZ.GetOutput(error.Z);

            return new Vector3D(outputX, outputY, outputZ);
        }

        /// <summary>
        /// 重置所有PID控制器状态
        /// </summary>
        public void Reset()
        {
            _pidX.Reset();
            _pidY.Reset();
            _pidZ.Reset();
        }

        /// <summary>
        /// 获取X轴PID控制器的引用
        /// </summary>
        public PID PidX => _pidX;

        /// <summary>
        /// 获取Y轴PID控制器的引用
        /// </summary>
        public PID PidY => _pidY;

        /// <summary>
        /// 获取Z轴PID控制器的引用
        /// </summary>
        public PID PidZ => _pidZ;
    }

    public class CircularQueue<T>
    {
        private readonly T[] _items;
        private int _head; // 指向最新元素
        private int _size;
        private readonly int _capacity;
        public bool HasError { get; private set; } // 错误状态标志

        public CircularQueue(int capacity)
        {
            _items = new T[capacity];
            _capacity = capacity;
            _head = -1;
            _size = 0;
            HasError = false;
        }

        /// <summary>
        /// 添加新元素到队列头部。如果队列已满，则覆盖最旧的元素
        /// </summary>
        public void AddFirst(T item)
        {
            _head = (_head + 1) % _capacity;
            _items[_head] = item;

            if (_size < _capacity)
                _size++;

            HasError = false; // 重置错误状态
        }

        /// <summary>
        /// 清空历史记录
        /// </summary>
        public void Clear()
        {
            _size = 0;
            _head = -1;
            HasError = false;
        }

        /// <summary>
        /// 获取索引位置的元素，如果索引无效则返回默认值
        /// </summary>
        public T GetItemAt(int index)
        {
            if (index < 0 || index >= _size)
            {
                HasError = true;
                return default(T); // 索引越界时返回默认值
            }

            // 计算实际数组索引
            int actualIndex = (_head - index + _capacity) % _capacity;
            return _items[actualIndex];
        }

        /// <summary>
        /// 尝试获取指定索引的元素
        /// </summary>
        /// <returns>获取成功返回true</returns>
        public bool TryGetItemAt(int index, out T item)
        {
            if (index < 0 || index >= _size)
            {
                item = default(T);
                HasError = true;
                return false;
            }

            int actualIndex = (_head - index + _capacity) % _capacity;
            item = _items[actualIndex];
            return true;
        }

        /// <summary>
        /// 获取第一个元素
        /// </summary>
        public T First
        {
            get
            {
                if (_size == 0)
                {
                    HasError = true;
                    return default(T);
                }
                return _items[_head];
            }
        }

        /// <summary>
        /// 尝试获取第一个元素
        /// </summary>
        public bool TryGetFirst(out T item)
        {
            if (_size == 0)
            {
                item = default(T);
                HasError = true;
                return false;
            }

            item = _items[_head];
            return true;
        }

        /// <summary>
        /// 获取最后一个元素
        /// </summary>
        public T Last
        {
            get
            {
                if (_size == 0)
                {
                    HasError = true;
                    return default(T);
                }
                int lastIndex = (_head - (_size - 1) + _capacity) % _capacity;
                return _items[lastIndex];
            }
        }

        /// <summary>
        /// 尝试获取最后一个元素
        /// </summary>
        public bool TryGetLast(out T item)
        {
            if (_size == 0)
            {
                item = default(T);
                HasError = true;
                return false;
            }

            int lastIndex = (_head - (_size - 1) + _capacity) % _capacity;
            item = _items[lastIndex];
            return true;
        }

        /// <summary>
        /// 检查队列是否为空
        /// </summary>
        public bool IsEmpty => _size == 0;

        /// <summary>
        /// 当前队列中的元素数量
        /// </summary>
        public int Count => _size;
    }

    /// <summary>
    /// 泛型滑动平均队列，O(1) 更新和查询。
    /// 通过构造时传入运算委托，支持任意 T 类型。
    /// var maVec = new MovingAverageQueue<Vector3D>(
    ///     5,
    ///     (a, b) => a + b,
    ///     (a, b) => a - b,
    ///     (a, n) => a / n
    /// );
    /// </summary>
    public class MovingAverageQueue<T> : CircularQueue<T>
    {
        private readonly int _capacity;
        private readonly Func<T, T, T> _add;
        private readonly Func<T, T, T> _subtract;
        private readonly Func<T, int, T> _divide;
        private T _sum;

        public MovingAverageQueue(
            int capacity,
            Func<T, T, T> add,
            Func<T, T, T> subtract,
            Func<T, int, T> divide
        ) : base(capacity)
        {
            _capacity = capacity;
            _add = add;
            _subtract = subtract;
            _divide = divide;
            _sum = default(T);
        }

        public new void AddFirst(T item)
        {
            // 如果满了，取出将被覆盖的最旧值
            T removed = default(T);
            if (Count == _capacity)
                removed = GetItemAt(Count - 1);

            base.AddFirst(item);

            // 更新累加和：sum = sum + item - removed
            _sum = _subtract(_add(_sum, item), removed);
        }

        /// <summary> 当前窗口所有元素之和 </summary>
        public T Sum => _sum;

        /// <summary> 当前窗口移动平均值 = sum / Count </summary>
        public T Average => Count > 0 ? _divide(_sum, Count) : default(T);
    }
}