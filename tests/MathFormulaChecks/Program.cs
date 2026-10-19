// ============================================================================
// File: tests/MathFormulaChecks/Program.cs
// Description: Console regression harness for the pure C# LaTeX math renderer
//              (apps/desktop-winui/Math/). NOT a dotnet-test project. Run with:
//
//                dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
//
//              Prints Total= Passed= Failed= and exits non-zero on failure.
// ============================================================================

using System;
using System.Globalization;

namespace MathFormulaChecks;

internal static class Program
{
    private static int Main()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // Console encoding may be locked down in some hosts.
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("数学公式渲染器回归：LaTeX 解析 / 真角标 / 分式 / 根号 / 矩阵 / 自动换行");
        Console.WriteLine("================================================================================");

        RunSection("ParserChecks", ParserChecks.Run);
        RunSection("LayoutChecks", LayoutChecks.Run);
        RunSection("EngineFormulaChecks", EngineFormulaChecks.Run);

        return CheckResult.Summary();
    }

    private static void RunSection(string name, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            CheckResult.Check(false, $"{name} 段内未处理异常", ex.ToString());
        }
    }
}

/// <summary>Shared assertion / counter helpers (same shape as NonlinearCircuitChecks).</summary>
internal static class CheckResult
{
    public static int Passed { get; private set; }

    public static int Failed { get; private set; }

    public static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {title} ---");
    }

    public static void Check(bool condition, string name, string detail = "")
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            Failed++;
            Console.WriteLine($"  [FAIL] {name}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : " -> " + detail)}");
        }
    }

    public static void Equal(string name, string expected, string actual)
    {
        bool ok = string.Equals(expected, actual, StringComparison.Ordinal);
        Check(ok, name, ok ? string.Empty : $"expected \"{expected}\", got \"{actual}\"");
    }

    public static void Close(string name, double expected, double actual, double tolerance = 0.01)
    {
        bool ok = double.IsFinite(expected) && double.IsFinite(actual) &&
                  Math.Abs(actual - expected) <= Math.Max(tolerance, tolerance * Math.Abs(expected));
        Check(ok, name, ok ? string.Empty : $"expected {Format(expected)}, got {Format(actual)}");
    }

    public static void Less(string name, double left, double right)
    {
        bool ok = left < right;
        Check(ok, name, ok ? string.Empty : $"expected {Format(left)} < {Format(right)}");
    }

    public static void LessOrEqual(string name, double left, double right)
    {
        bool ok = left <= right;
        Check(ok, name, ok ? string.Empty : $"expected {Format(left)} <= {Format(right)}");
    }

    public static void Greater(string name, double left, double right)
    {
        bool ok = left > right;
        Check(ok, name, ok ? string.Empty : $"expected {Format(left)} > {Format(right)}");
    }

    public static int Summary()
    {
        int total = Passed + Failed;
        Console.WriteLine();
        Console.WriteLine($"Total={total} Passed={Passed} Failed={Failed}");
        return Failed == 0 ? 0 : 1;
    }

    public static string Format(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
}
