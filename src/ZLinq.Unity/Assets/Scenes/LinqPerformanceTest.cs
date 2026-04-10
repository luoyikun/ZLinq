using UnityEngine;
using System;
using System.Linq;
using ZLinq;
using UnityEngine.Profiling;
using NUnit.Framework;
using System.Collections.Generic; // 必须引入 ZLinq 的命名空间

public class LinqPerformanceTest : MonoBehaviour
{
    [Header("测试数据配置")]
    [SerializeField] private int dataSize = 100000;  // 数据量，默认为10万
    [SerializeField] private int testRepetitions = 10; // 重复测试次数，取平均值

    // 测试数据源
    private int[] testArray;
    private System.Diagnostics.Stopwatch stopwatch;

    // 缓存 GC 统计信息
    private long gcMemoryBeforeTest;
    private long gcMemoryAfterTest;
    int m_testTime = 100;
    void Start()
    {
        // 初始化测试数据和计时器
        InitializeTestData();
        stopwatch = new System.Diagnostics.Stopwatch();
        Debug.Log($"测试脚本已启动。\n数据量: {dataSize:N0}, 重复测试次数: {testRepetitions}");
        Debug.Log("按 'T' 键开始性能测试...");
    }

    List<int> m_listLinq = new();
    void Update()
    {
        // 按 T 键触发测试，避免每帧自动执行干扰
        if (Input.GetKeyDown(KeyCode.T))
        {
            RunPerformanceTests();
        }

        {
            Profiler.BeginSample("Linq");
            //for (int i = 0; i < m_testTime; i++)
            {
                var result = testArray
                    .Where(x => x % 2 == 0)   // 筛选偶数
                    //.Select(x => x * 2)       // 映射为自身两倍
                    //.OrderBy(x => x)          // 排序
                    //.Take(1000)               // 取前1000个
                    .ToList();                // 执行并收集结果
            }
            
            Profiler.EndSample();
        }

        {
            Profiler.BeginSample("ZLinq");
            //for (int i = 0; i < m_testTime; i++)
            {
                // 核心区别：通过 AsValueEnumerable() 切换到零分配的 ValueEnumerable
                var result = testArray
                    .AsValueEnumerable()      // 关键一步：将数组转换为 ZLinq 的 ValueEnumerable
                    .Where(x => x % 2 == 0)   // 筛选偶数
                    //.Select(x => x * 2)       // 映射为自身两倍
                    //.OrderBy(x => x)          // 排序
                    //.Take(1000)               // 取前1000个
                    .ToArray();               // 执行并收集结果
            }
           
            Profiler.EndSample();
        }


        {
            Profiler.BeginSample("LinqUseListPool");
            //for (int i = 0; i < m_testTime; i++)
            {
                m_listLinq.Clear();
                for (int i = 0; i < testArray.Length; i++)
                {
                    int x = testArray[i];
                    if (x % 2 == 0)
                    {
                        m_listLinq.Add(x);
                    }
                }
            }

            Profiler.EndSample();
        }
    }

    /// <summary>
    /// 初始化测试用的数据源
    /// </summary>
    void InitializeTestData()
    {
        testArray = new int[dataSize];
        var random = new System.Random(42); // 固定随机种子，确保测试一致性
        for (int i = 0; i < dataSize; i++)
        {
            testArray[i] = random.Next(0, 1000);
        }
    }

    /// <summary>
    /// 执行所有性能测试
    /// </summary>
    void RunPerformanceTests()
    {
        Debug.Log($"\n========== 开始性能测试 (数据量: {dataSize:N0}) ==========");

        // 1. 标准 LINQ 测试
        // 为了公平，使用 ToList() 强制执行，避免延迟执行带来的偏差
        double linqAvgTime = MeasureAverageTime(() =>
        {
            var result = testArray
                .Where(x => x % 2 == 0)   // 筛选偶数
                .Select(x => x * 2)       // 映射为自身两倍
                .OrderBy(x => x)          // 排序
                .Take(1000)               // 取前1000个
                .ToList();                // 执行并收集结果
        }, testRepetitions, out long linqMemoryUsage);

        // 2. ZLinq 测试
        double zlinqAvgTime = MeasureAverageTime(() =>
        {
            // 核心区别：通过 AsValueEnumerable() 切换到零分配的 ValueEnumerable
            var result = testArray
                .AsValueEnumerable()      // 关键一步：将数组转换为 ZLinq 的 ValueEnumerable
                .Where(x => x % 2 == 0)   // 筛选偶数
                .Select(x => x * 2)       // 映射为自身两倍
                .OrderBy(x => x)          // 排序
                .Take(1000)               // 取前1000个
                .ToArray();               // 执行并收集结果
        }, testRepetitions, out long zlinqMemoryUsage);

        // 输出最终对比结果
        PrintComparisonResults(linqAvgTime, zlinqAvgTime, linqMemoryUsage, zlinqMemoryUsage);
    }

    /// <summary>
    /// 通用性能测量方法，返回平均执行时间(毫秒)和单次GC分配内存(字节)
    /// </summary>
    double MeasureAverageTime(Action testAction, int repetitions, out long memoryAllocated)
    {
        // 强制进行一次GC回收，确保测试起点干净
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();

        // 记录测试前的内存使用情况
        gcMemoryBeforeTest = System.GC.GetTotalMemory(true);
        long totalElapsedTicks = 0;

        for (int i = 0; i < repetitions; i++)
        {
            stopwatch.Reset();
            stopwatch.Start();

            // 执行待测操作
            testAction();

            stopwatch.Stop();
            totalElapsedTicks += stopwatch.ElapsedTicks;
        }

        // 记录测试后的内存使用情况
        gcMemoryAfterTest = System.GC.GetTotalMemory(false);
        memoryAllocated = (gcMemoryAfterTest - gcMemoryBeforeTest) / repetitions;
        double averageTimeMs = (totalElapsedTicks / (double)repetitions) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return averageTimeMs;
    }

    /// <summary>
    /// 打印测试结果的对比信息
    /// </summary>
    void PrintComparisonResults(double linqTime, double zlinqTime, long linqMemory, long zlinqMemory)
    {
        Debug.Log("\n========== 测试结果对比 ==========");
        Debug.Log($"| 方法      | 平均执行时间 (ms) | GC 内存分配 (字节) |");
        Debug.Log($"|-----------|-------------------|--------------------|");
        Debug.Log($"| 标准 LINQ | {linqTime,15:F3} | {linqMemory,18:N0} |");
        Debug.Log($"| ZLinq     | {zlinqTime,15:F3} | {zlinqMemory,18:N0} |");
        Debug.Log($"=================================");

        // 计算性能差异百分比
        double timeImprovement = ((linqTime - zlinqTime) / linqTime) * 100.0;
        string timeMessage = timeImprovement > 0 ?
            $"ZLinq 时间上快了 {timeImprovement:F1}% ({linqTime - zlinqTime:F3} ms)" :
            $"ZLinq 时间上慢了 {-timeImprovement:F1}% ({zlinqTime - linqTime:F3} ms)";

        // 内存分配优势几乎总是 ZLinq 更强
        long memorySaved = linqMemory - zlinqMemory;
        string memoryMessage = memorySaved > 0 ?
            $"ZLinq 避免了 {memorySaved:N0} 字节的 GC 分配" :
            $"ZLinq 多分配了 {-memorySaved:N0} 字节";

        Debug.Log($"结论: {timeMessage}。{memoryMessage}。");
        Debug.Log("注意: 内存分配的差异是 ZLinq 的核心优势，对避免 GC 卡顿至关重要。\n");
    }
}
