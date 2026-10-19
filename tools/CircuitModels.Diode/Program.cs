// ============================================================================
// File: tools/CircuitModels.Diode/Program.cs
// Description: D01–D07 二极管电路工程文件生成器入口。
//
//   默认模式（无参数）：        Build 全部声明式定义 -> 结构自检 -> 写入
//                               examples/circuit-models/D0*.circuit
//   --verify（只读）：          加载磁盘上的 7 个文件 -> Validate -> ToModel ->
//                               NetlistExtractor -> TopologyClassifier -> Solve，
//                               与 Expected 比对并打印 [PASS]/[FAIL] 汇总。
//
//   run:
//     dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj
//     dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CircuitModels.Diode;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may be locked down */ }

        bool verifyOnly = args.Any(arg => string.Equals(arg, "--verify", StringComparison.OrdinalIgnoreCase));
        try
        {
            string outputDirectory = ResolveOutputDirectory();
            IReadOnlyList<CircuitSpec> specs = DiodeCircuits.All();

            Console.WriteLine("================================================================================");
            Console.WriteLine("二极管教学电路工程文件生成器（D01–D07，原生 .circuit JSON，版本 1.2.0）");
            Console.WriteLine($"输出目录: {outputDirectory}");
            Console.WriteLine("================================================================================");

            if (verifyOnly)
            {
                Console.WriteLine("校验模式(--verify)：只读取磁盘文件，执行 加载 -> 校验 -> 网表 -> 分类 -> 求解 比对。");
                Console.WriteLine();
                // 使用带 CircuitSpec 的重载：同时比对 Expected.Topology/Subtype 与求解预期。
                int failed = Verification.Verify(outputDirectory, specs);
                return failed == 0 ? 0 : 1;
            }

            Console.WriteLine($"声明式生成 + 结构自检：{specs.Count} 个电路");
            Console.WriteLine();
            SchematicBuilder.Generate(specs, outputDirectory);
            Console.WriteLine();
            Console.WriteLine($"[OK] 已生成 {specs.Count} 个 .circuit 文件到 {outputDirectory}；全部通过结构自检。");
            Console.WriteLine("     下一步：等待引擎（二极管策略 + 分类器接线）就绪后运行 --verify 校验分类/求解预期。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"[FAIL] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 输出目录固定为仓库根 examples/circuit-models/。从当前工作目录与程序目录
    /// 分别向上查找仓库标记，避免 dotnet run 的工作目录差异导致生成到错误位置。
    /// </summary>
    private static string ResolveOutputDirectory()
    {
        string repositoryRoot = FindRepositoryRoot();
        return Path.Combine(repositoryRoot, "examples", "circuit-models");
    }

    private static string FindRepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "apps", "desktop-winui", "CircuitAssistant.csproj")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }

        // 兜底：按契约相对于当前工作目录（生成器通常在仓库根运行）。
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", ".."));
    }
}
