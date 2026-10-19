using System;
using System.IO;
using System.Linq;
using CircuitModels;

namespace CircuitModels.BjtBias;

/// <summary>
/// BJT 偏置/工作点电路工程文件生成器（契约 §8/§9，B01–B06）。
/// 默认运行：生成 6 个 .circuit + 结构自检；`-- --verify`：只读校验磁盘上已有文件
/// （分类 / 子类型 / 求解结果与 §9 预期对比）。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 控制台不支持时忽略 */ }

        var outputDirectory = ResolveOutputDirectory();
        var specs = BjtBiasDefinitions.All;

        if (args.Any(a => string.Equals(a, "--verify", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[CircuitModels.BjtBias] 只读校验目录: {outputDirectory}");
            Console.WriteLine("[CircuitModels.BjtBias] 预期: BjtAmplifier ×6 (FixedBias / VoltageDividerSelfBias ×3 / CollectorFeedbackBias / CommonBase)，全部可解。");
            // 使用带 CircuitSpec 期望值的重载：同时核对工程名、Topology、Subtype 与求解结果。
            return Verification.Verify(outputDirectory, specs);
        }

        Console.WriteLine($"[CircuitModels.BjtBias] 生成 {specs.Count} 个 BJT 偏置电路工程文件到: {outputDirectory}");
        SchematicBuilder.Generate(specs, outputDirectory);
        foreach (var spec in specs)
        {
            Console.WriteLine($"  [OK] {spec.FileName}");
        }
        Console.WriteLine($"[CircuitModels.BjtBias] 完成：{specs.Count} 个文件已写入并通过结构自检。");
        return 0;
    }

    /// <summary>
    /// 输出目录 = 仓库根/examples/circuit-models。从程序目录向上查找包含
    /// apps/desktop-winui 的目录作为仓库根（不依赖调用时的当前工作目录）。
    /// </summary>
    private static string ResolveOutputDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "apps", "desktop-winui")))
        {
            dir = dir.Parent;
        }
        var repoRoot = dir?.FullName ?? Directory.GetCurrentDirectory();
        return Path.Combine(repoRoot, "examples", "circuit-models");
    }
}
