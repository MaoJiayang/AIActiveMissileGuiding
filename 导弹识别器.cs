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
// using IMyShipMergeBlock = SpaceEngineers.Game.ModAPI.Ingame.IMyShipMergeBlock;

namespace IngameScript
{
    /// <summary>
    /// 位置信息结构体，包含位置坐标和对应的终端方块（如果有的话）
    /// </summary>
    public struct 位置信息
    {
        public Vector3I 位置;
        public IMyTerminalBlock 终端方块; // 如果是装甲块或非功能方块则为null
        
        public 位置信息(Vector3I position, IMyTerminalBlock terminalBlock = null)
        {
            位置 = position;
            终端方块 = terminalBlock;
        }
    }

    public class 导弹识别器
    {
        private 参数管理器 参数们;
        private Action<string> Echo;
        private IMyTerminalBlock 起点方块;
        private Vector3D MeGridMax;
        private Vector3D MeGridMin;
        private IMyCubeGrid grid;

        // 持久化的BFS状态
        private Queue<位置信息> queue;
        private HashSet<Vector3I> visited;
        private Vector3I[] directions;
        private int blockCount;
        private bool isInitialized;

        // 遍历过程中发现的终端方块集合
        private HashSet<IMyTerminalBlock> 遍历结果列表;

        /// <summary>
        /// 检查是否已完成遍历
        /// </summary>
        /// <returns>true表示遍历完成，false表示还有待处理的位置</returns>
        public bool IsTraversalComplete
        {
            get { return queue == null || queue.Count == 0; } // 添加null检查
        }
        /// <summary>
        /// 获取当前遍历进度信息
        /// </summary>
        /// <returns>包含队列大小、已访问位置数、已发现方块数的信息</returns>
        public string 遍历状态
        {
            get
            {
                int discoveredCount = 遍历结果列表?.Count ?? 0;
                return $"队列中位置数: {queue.Count}\n已访问: {visited.Count}\n格子数: {blockCount}\n终端方块: {discoveredCount}";
            }
        }

        /// <summary>
        /// 获取遍历过程中发现的所有终端方块
        /// 只有在遍历完成后才返回结果，否则不添加任何方块到列表
        /// </summary>
        /// <param name="blocks">结果列表，符合条件的方块将被添加到此列表</param>
        /// <param name="collect">可选的筛选条件，如果为null则获取所有终端方块</param>
        public void GetBlocks(List<IMyTerminalBlock> blocks, Func<IMyTerminalBlock, bool> collect = null)
        {
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));

            // 确保完全初始化且遍历完成
            if (!isInitialized || queue == null || queue.Count > 0)
            {
                return; // 遍历未完成，不添加任何结果
            }

            // 遍历所有发现的终端方块
            foreach (var terminalBlock in 遍历结果列表)
            {
                // 应用筛选条件（如果有）
                if (collect == null || collect(terminalBlock))
                {
                    blocks.Add(terminalBlock);
                }
            }
        }

        /// <summary>
        /// 获取指定类型的方块并添加到列表中
        /// </summary>
        /// <typeparam name="T">要筛选的方块类型</typeparam>
        /// <param name="blocks">结果列表，符合条件的方块将被添加到此列表</param>
        /// <param name="collect">可选的筛选条件，如果为null则获取所有T类型的方块</param>
        public void GetBlocksOfType<T>(List<T> blocks, Func<T, bool> collect = null) where T : class
        {
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));

            // 确保遍历已完成
            if (!isInitialized || queue == null || queue.Count > 0)
            {
                return; // 遍历未完成，不返回任何结果
            }

            // 遍历所有发现的终端方块
            foreach (var terminalBlock in 遍历结果列表)
            {
                // 尝试转换为目标类型
                T block = terminalBlock as T;
                if (block != null)
                {
                    // 应用筛选条件（如果有）
                    if (collect == null || collect(block))
                    {
                        blocks.Add(block);
                    }
                }
            }
        }

        /// <summary>
        /// 导弹识别器构造函数
        /// </summary>
        /// <param name="指定方块组">方块组，仅在当前组内搜索</param>
        /// <param name="起点">初始化位置方块</param>
        /// <param name="Echo">输出函数</param>
        public 导弹识别器(参数管理器 参数们, IMyTerminalBlock 起点, Action<string> Echo)
        {
            // if (指定方块组 == null) throw new Exception("未指定方块组");
            // if (Me == null) throw new Exception("未指定Me方块");

            this.起点方块 = 起点;
            this.Echo = Echo;
            this.grid = 起点.CubeGrid;
            this.参数们 = 参数们;
            List<IMyTerminalBlock> 组内功能块 = new List<IMyTerminalBlock>();
            // 初始化识别参数
            if (参数们.MeGridMax != null && 参数们.MeGridMin != null)
            {
                MeGridMax = 参数们.MeGridMax.Value;
                MeGridMin = 参数们.MeGridMin.Value;
            }
            else
            {
                重新框定();
            }
            // 初始化发现的终端方块集合
            遍历结果列表 = new HashSet<IMyTerminalBlock>();

            // 初始化BFS状态
            初始化BFS();
        }
        public void 重新框定()
        {
            Vector3I gridMax = 起点方块.CubeGrid.Max;
            Vector3I gridMin = 起点方块.CubeGrid.Min;

            // 使用矩阵变换将网格边界点转换为相对于参考方块的坐标
            MeGridMax = 网格坐标转相对坐标(gridMax);
            MeGridMin = 网格坐标转相对坐标(gridMin);
            
            // 保存到参数管理器（需要确保参数管理器支持Vector3D类型）
            // 这里暂时注释掉，需要根据参数管理器的实际实现来调整
            参数们.MeGridMax = MeGridMax;
            参数们.MeGridMin = MeGridMin;
            起点方块.CustomData = 参数们.生成配置字符串();
        }
        /// <summary>
        /// 初始化BFS状态
        /// </summary>
        private void 初始化BFS()
        {
            queue = new Queue<位置信息>();
            visited = new HashSet<Vector3I>();

            // 六个方向的偏移量
            directions = new Vector3I[]
            {
                new Vector3I(1, 0, 0),   // +X
                new Vector3I(-1, 0, 0),  // -X
                new Vector3I(0, 1, 0),   // +Y
                new Vector3I(0, -1, 0),  // -Y
                new Vector3I(0, 0, 1),   // +Z
                new Vector3I(0, 0, -1)   // -Z
            };

            blockCount = 0;

            Vector3I startPos = 起点方块.Position;
            // 处理初始位置并加入队列
            queue.Enqueue(new 位置信息(startPos, 起点方块));
            visited.Add(startPos);
            isInitialized = true;

            // Echo($"BFS初始化完成，开始位置: {startPos}");
            // Echo($"Grid Size: {grid.GridSizeEnum}");
        }

        /// <summary>
        /// 重置BFS状态，重新开始遍历
        /// </summary>
        public void 重置()
        {
            // 清空发现的终端方块集合
            遍历结果列表?.Clear();
            初始化BFS();
        }

        /// <summary>
        /// 执行限制数量的遍历步骤
        /// </summary>
        /// <param name="maxProcessCount">本次最多处理的位置数量</param>
        /// <returns>true表示遍历完成，false表示还需继续</returns>
        public bool 遍历(int maxProcessCount = 6)
        {
            if (!isInitialized)
            {
                throw new Exception("BFS未初始化");
            }

            int processedCount = 0;

            while (queue.Count > 0 && processedCount < maxProcessCount)
            {
                位置信息 current = queue.Dequeue();
                processedCount++;
                blockCount++;

                // 如果有终端方块，进行处理
                if (current.终端方块 != null)
                {
                    处理终端方块(current.终端方块);
                }

                // 探索六个方向的邻居
                foreach (Vector3I direction in directions)
                {
                    Vector3I nextPos = current.位置 + direction;

                    // 如果没有访问过这个位置
                    if (!visited.Contains(nextPos))
                    {
                        // 检查是否存在方块
                        if (grid.CubeExists(nextPos))
                        {
                            // 处理该位置并决定是否访问
                            处理访问(nextPos);
                        }
                    }
                }
            }

            bool isComplete = queue.Count == 0;
            return isComplete;
        }

        /// <summary>
        /// 处理终端方块：添加到结果列表并标记
        /// </summary>
        /// <param name="termBlock">要处理的终端方块</param>
        private void 处理终端方块(IMyTerminalBlock termBlock)
        {
            // 检查是否应该排除此方块
            if (参数们.应该排除方块(termBlock.CustomName))
            {
                // 方块被排除，不加入到结果列表，但允许继续遍历
                return;
            }
    
            // 将终端方块添加到发现列表中（避免重复添加）
            if (!遍历结果列表.Contains(termBlock))
            {
                遍历结果列表.Add(termBlock);
                //Echo($"已将终端方块添加到发现列表: {termBlock.CustomName}");
            }

            // 为功能块的CustomName末尾加上字符'#'
            if (!termBlock.CustomName.EndsWith("#"))
            {
                termBlock.CustomName += "#";
                //Echo($"已标记功能块: {termBlock.CustomName}");
            }
        }

        /// <summary>
        /// 将绝对坐标转换为相对坐标并判断是否在网格范围内
        /// </summary>
        /// <param name="absolutePos">绝对坐标</param>
        /// <returns>true表示在网格范围内，false表示超出范围</returns>
        private bool 在网格内(Vector3I absolutePos)
        {
            // 将网格坐标转换为相对于参考方块的坐标
            Vector3D relativePos = 网格坐标转相对坐标(absolutePos);
            const double tolerance = 1e-3;
            
            // 检查是否在保存的边界范围内（加入容差）
            return relativePos.X >= Math.Min(MeGridMin.X, MeGridMax.X) - tolerance && relativePos.X <= Math.Max(MeGridMin.X, MeGridMax.X) + tolerance &&
                   relativePos.Y >= Math.Min(MeGridMin.Y, MeGridMax.Y) - tolerance && relativePos.Y <= Math.Max(MeGridMin.Y, MeGridMax.Y) + tolerance &&
                   relativePos.Z >= Math.Min(MeGridMin.Z, MeGridMax.Z) - tolerance && relativePos.Z <= Math.Max(MeGridMin.Z, MeGridMax.Z) + tolerance;
        }

        /// <summary>
        /// 处理指定位置的访问：检查方块类型并决定是否加入队列
        /// </summary>
        /// <param name="pos">要处理的位置</param>
        private void 处理访问(Vector3I pos)
        {
            // 首先检查位置是否在网格范围内
            if (!在网格内(pos))
            {
                //Echo($"位置 {pos} 超出网格范围，跳过访问");
                return;
            }

            // 获取该位置的方块信息
            IMySlimBlock slim = grid.GetCubeBlock(pos);
            
            if (slim == null)
            {
                // 装甲块：CubeExists为true但GetCubeBlock返回null
                // 装甲块允许访问，用于连通性，终端方块为null
                visited.Add(pos);
                queue.Enqueue(new 位置信息(pos, null));
                //Echo($"位置 {pos} 是装甲块，允许访问");
                return;
            }

            // 检查是否有FatBlock
            var fat = slim.FatBlock;
            if (fat == null)
            {
                // 非功能方块：有SlimBlock但没有FatBlock，跳过访问
                //Echo($"位置 {pos} 是非功能方块，跳过访问");
                return;
            }

            // 尝试转换为终端方块
            IMyTerminalBlock termBlock = fat as IMyTerminalBlock;
            if (termBlock == null)
            {
                // 有FatBlock但不是终端方块，跳过访问
                //Echo($"位置 {pos} 的方块无法转换为终端方块，跳过访问");
                return;
            }

            // 终端方块且在指定范围内，加入队列并包含终端方块引用
            visited.Add(pos);
            queue.Enqueue(new 位置信息(pos, termBlock));
            //Echo($"位置 {pos} 的终端方块符合条件，加入队列");
        }

        /// <summary>
        /// 将网格整数坐标转换为相对于参考方块的相对坐标
        /// </summary>
        /// <param name="gridIntPos">网格整数坐标</param>
        /// <returns>相对于参考方块的相对坐标</returns>
        private Vector3D 网格坐标转相对坐标(Vector3I gridIntPos)
        {
            // 将网格整数坐标转换为世界坐标
            Vector3D worldPos = grid.GridIntegerToWorld(gridIntPos);
            
            // 获取参考方块的逆变换矩阵
            MatrixD referenceMatrix = 起点方块.WorldMatrix;
            MatrixD inverseReferenceMatrix = MatrixD.Invert(referenceMatrix);
            
            // 将世界坐标转换为相对于参考方块的局部坐标
            Vector3D relativePos = Vector3D.Transform(worldPos, inverseReferenceMatrix);
            
            return relativePos;
        }
    }
}