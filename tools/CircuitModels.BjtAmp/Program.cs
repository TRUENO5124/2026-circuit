using System;
using System.IO;
using System.Linq;
using CircuitModels;

namespace CircuitModels.BjtAmp;

/// <summary>
/// BJT 放大/频率响应电路工程文件生成器（契约 §8/§9，B07–B16）。
/// 默认运行：生成 + 结构自检；`-- --verify`：只读校验磁盘上已有文件。
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 控制台不支持时忽略 */ }

        var outputDirectory = ResolveOutputDirectory();
        var specs = BjtAmpDefinitions.All;

        if (args.Any(a => string.Equals(a, "--verify", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[CircuitModels.BjtAmp] 只读校验目录: {outputDirectory}");
            return Verification.Verify(outputDirectory, specs);
        }

        Console.WriteLine($"[CircuitModels.BjtAmp] 生成 {specs.Count} 个 BJT 放大电路工程文件到: {outputDirectory}");
        SchematicBuilder.Generate(specs, outputDirectory);
        Console.WriteLine($"[CircuitModels.BjtAmp] 完成：{specs.Count} 个文件已写入并通过结构自检。");
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
