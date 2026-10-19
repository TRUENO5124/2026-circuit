// ============================================================================
// File: tests/NonlinearCircuitChecks/Program.cs
// Description: Console regression harness for the nonlinear engine additions:
//              diode circuits, MOSFET amplifiers, BJT common-collector/common-base,
//              the BJT textbook-estimation + DC/AC phase contract and the reserved
//              multi-stage rejection contract.
//
//              NOT a dotnet-test project. Run with:
//                dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
//
//              Each owner writes one check file:
//                DiodeChecks.cs            (engine-diode)
//                MosChecks.cs              (engine-mos)
//                BjtCcChecks.cs            (engine-integration)
//                BjtVdEstimateChecks.cs    (engine-bjt: Q3-12 textbook estimation)
//                BjtDcAcPhaseChecks.cs     (engine-bjt: DC/AC phase + display units)
//                BjtSourceResistanceChecks.cs (engine-bjt: Rs + electrolytic input coupling)
//              and uses the shared CheckResult helpers below.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class Program
{
    private static int Main()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may be locked down */ }
        Console.WriteLine("================================================================================");
        Console.WriteLine("非线性引擎回归：二极管 / MOSFET / BJT 共集电极 / BJT 教材估算法 + DC/AC 分组 / 多级预留接口");
        Console.WriteLine("================================================================================");

        try { DiodeChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "DiodeChecks 段内未处理异常", ex.ToString()); }
        try { MosChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "MosChecks 段内未处理异常", ex.ToString()); }
        try { BjtCcChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "BjtCcChecks 段内未处理异常", ex.ToString()); }
        try { BjtVdEstimateChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "BjtVdEstimateChecks 段内未处理异常", ex.ToString()); }
        try { BjtDcAcPhaseChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "BjtDcAcPhaseChecks 段内未处理异常", ex.ToString()); }
        try { BjtSourceResistanceChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "BjtSourceResistanceChecks 段内未处理异常", ex.ToString()); }
        try { BjtSmallSignalParameterChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "BjtSmallSignalParameterChecks 段内未处理异常", ex.ToString()); }

        try { OperatingRegionChecks.Run(); }
        catch (Exception ex) { CheckResult.Check(false, "OperatingRegionChecks", ex.ToString()); }
        return CheckResult.Summary();
    }
}

/// <summary>
/// Shared assertion/counter helpers for the nonlinear checks. Every check file
/// records PASS/FAIL through this class so Program can print one summary line.
/// </summary>
internal static class CheckResult
{
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }

    /// <summary>Any decimal number with three or more digits after the point violates the display convention.</summary>
    private static readonly Regex ExcessDecimalRegex = new(@"[0-9]+\.[0-9]{3,}", RegexOptions.Compiled);

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
            Console.WriteLine($"  [FAIL] {name}{(string.IsNullOrWhiteSpace(detail) ? "" : " -> " + detail)}");
        }
    }

    /// <summary>Relative-error comparison with the project-wide 1 % default.</summary>
    public static void Close(string name, double expected, double actual, double relativeTolerance = 0.01, string unit = "")
    {
        bool ok = double.IsFinite(expected) && double.IsFinite(actual) &&
                  Math.Abs(actual - expected) <= relativeTolerance * Math.Max(1e-12, Math.Abs(expected));
        Check(ok, name, $"expected {Format(expected)}{unit}, got {Format(actual)}{unit}");
    }

    /// <summary>Absolute comparison for quantities whose expected value is exactly zero or very small.</summary>
    public static void Near(string name, double expected, double actual, double absoluteTolerance, string unit = "")
    {
        bool ok = double.IsFinite(expected) && double.IsFinite(actual) &&
                  Math.Abs(actual - expected) <= absoluteTolerance;
        Check(ok, name, $"expected {Format(expected)}{unit}, got {Format(actual)}{unit}");
    }

    public static void TextContains(string name, string? text, params string[] tokens)
    {
        string value = text ?? string.Empty;
        bool ok = true;
        foreach (string token in tokens) ok &= value.Contains(token, StringComparison.Ordinal);
        Check(ok, name, $"实际文本：{(string.IsNullOrWhiteSpace(value) ? "（空）" : value)}");
    }

    public static void TextNotContains(string name, string? text, params string[] tokens)
    {
        string value = text ?? string.Empty;
        string? hit = tokens.FirstOrDefault(token => value.Contains(token, StringComparison.Ordinal));
        Check(hit == null, name, hit == null ? string.Empty : $"实际文本含 “{hit}”：{value}");
    }

    /// <summary>
    /// Every user-visible display text of a teaching solution (Q point panel, AC
    /// parameter panel, summary line and every derivation step field). Backend-only
    /// exact fields (QPointInfo.Exact*) are intentionally excluded: they keep more
    /// precision for diagnostics and are never rendered by the UI.
    /// </summary>
    public static IEnumerable<string?> DisplayTexts(PedagogicalSolution solution)
    {
        yield return solution.CircuitTitle;
        yield return solution.SummaryText;
        if (solution.QPoint != null)
        {
            yield return solution.QPoint.Ibq;
            yield return solution.QPoint.Icq;
            yield return solution.QPoint.Vceq;
            yield return solution.QPoint.WorkingRegion;
        }
        if (solution.AcParameters != null)
        {
            yield return solution.AcParameters.VoltageGain;
            yield return solution.AcParameters.InputResistance;
            yield return solution.AcParameters.OutputResistance;
            yield return solution.AcParameters.LowerCutoffFreq;
            yield return solution.AcParameters.UpperCutoffFreq;
            yield return solution.AcParameters.Bandwidth;
        }
        foreach (PedagogicalSolutionStep step in solution.Steps)
        {
            yield return step.Title;
            yield return step.MethodName;
            yield return step.Content;
            yield return step.LatexFormula;
            yield return step.SubstitutionFormula;
            yield return step.ResultValue;
        }
    }

    /// <summary>Fails when any display text contains a number with three or more decimal places.</summary>
    public static void NoExcessDecimals(string name, IEnumerable<string?> texts)
    {
        var offenders = new List<string>();
        foreach (string? text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (Match match in ExcessDecimalRegex.Matches(text))
                offenders.Add($"{match.Value} in «{Shorten(text)}»");
        }
        Check(offenders.Count == 0, name, string.Join(" ; ", offenders.Take(5)));
    }

    /// <summary>Displayed-value convention: at most two decimal places, no dual-caliber (Thévenin vs textbook) wording.</summary>
    public static void AssertDisplayConventions(string name, PedagogicalSolution solution)
    {
        NoExcessDecimals($"{name}：所有显示值最多两位小数", DisplayTexts(solution));
        TextNotContains($"{name}：显示文本不含“戴维南”等双口径文案",
            string.Join(" | ", DisplayTexts(solution)), "戴维南", "教材估算对照", "精确解", "11.1%");
    }

    public static int Summary()
    {
        int total = Passed + Failed;
        Console.WriteLine();
        Console.WriteLine($"Total={total} Passed={Passed} Failed={Failed}");
        return Failed == 0 ? 0 : 1;
    }

    public static string Format(double value) =>
        double.IsNaN(value) ? "NaN"
        : double.IsPositiveInfinity(value) ? "+∞"
        : double.IsNegativeInfinity(value) ? "-∞"
        : value.ToString("G8", CultureInfo.InvariantCulture);

    private static string Shorten(string text) => text.Length <= 180 ? text : text[..180] + "…";
}

