// ============================================================================
// File: apps/desktop-winui/Services/TeachingDisplay.cs
// Namespace: CircuitAssistant.Services
// Description: Textbook-style engineering display formatting for teaching results.
//
// User convention (2026-10-18): the software targets undergraduate learning, so
// EVERY displayed computed result keeps AT MOST TWO decimal places ("结果最多保留
// 到小数点后两位"). The approximation is a display rounding convention, not an
// algebraic simplification: formulas keep all terms (for example (1+β) is never
// reduced to β, and no term is dropped). Textbook hand calculations round an
// intermediate result and feed the rounded number into the next formula, so this
// helper returns both the display text and the rounded SI value that the shown
// number represents (see RoundTo) for strategies that reproduce that workflow.
//
// The display unit is chosen the way textbooks choose it for a given quantity:
// currents in μA/mA/A, resistances in Ω/kΩ/MΩ, frequencies in Hz/kHz/MHz, etc.
// ============================================================================

using System;
using System.Globalization;

namespace CircuitAssistant.Services;

/// <summary>Engineering quantity families with textbook display-unit conventions.</summary>
public enum QuantityKind
{
    Voltage,
    Current,
    Resistance,
    Capacitance,
    Inductance,
    Frequency,
    Time,
    Ratio
}

/// <summary>
/// A teaching display quantity: the rendered text (number + unit) and the rounded
/// SI value the rendered number actually represents.
/// </summary>
public readonly struct DisplayQuantity
{
    public DisplayQuantity(double si, string number, string unit)
    {
        Si = si;
        Number = number;
        Unit = unit;
    }

    /// <summary>Rounded value in SI base unit (V, A, Ω, F, H, Hz, s) — or a pure ratio.</summary>
    public double Si { get; }

    /// <summary>Rendered number, at most two decimal places (trailing zeros trimmed).</summary>
    public string Number { get; }

    /// <summary>Rendered unit ("V", "μA", "kΩ", …); empty for ratios.</summary>
    public string Unit { get; }

    /// <summary>Rendered text: "53 μA", "11.82 V", "48.97 kΩ", "−45.6".</summary>
    public string Text => Unit.Length == 0 ? Number : $"{Number} {Unit}";
}

/// <summary>
/// Textbook hand-calculation display formatting: select the conventional display
/// unit, round to at most two decimal places, and expose the rounded SI value so a
/// derivation can continue with the number the student actually sees.
/// </summary>
public static class TeachingDisplay
{
    private const int MaxDecimals = 2;

    /// <summary>Formats a value using the conventional textbook display unit.</summary>
    public static DisplayQuantity Format(double siValue, QuantityKind kind)
    {
        if (double.IsNaN(siValue)) return new DisplayQuantity(double.NaN, "NaN", string.Empty);
        if (double.IsPositiveInfinity(siValue)) return new DisplayQuantity(double.PositiveInfinity, "∞", string.Empty);
        if (double.IsNegativeInfinity(siValue)) return new DisplayQuantity(double.NegativeInfinity, "-∞", string.Empty);
        if (siValue == 0) return new DisplayQuantity(0, "0", kind == QuantityKind.Ratio ? string.Empty : UnitOf(kind, 1));

        (double scale, string unit) = SelectUnit(siValue, kind);
        return Build(siValue, scale, unit);
    }

    /// <summary>
    /// Formats and returns only the rendered text, e.g. "0.53 μA".
    /// </summary>
    public static string Text(double siValue, QuantityKind kind) => Format(siValue, kind).Text;

    /// <summary>
    /// Returns the SI value that the displayed (rounded) number represents, so a
    /// textbook hand-calculation chain can continue with the shown value, e.g.
    /// 5.38462e-5 A displayed as "53.85 μA" → RoundTo → 5.385e-5 A.
    /// </summary>
    public static double RoundTo(double siValue, QuantityKind kind)
    {
        if (!double.IsFinite(siValue) || siValue == 0) return siValue;
        (double scale, _) = SelectUnit(siValue, kind);
        return Round(siValue, scale);
    }

    /// <summary>Formats a pure ratio / gain / coefficient (at most two decimals).</summary>
    public static string Plain(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";
        return Math.Round(value, MaxDecimals, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Rounds a plain ratio to the display precision (two decimals).</summary>
    public static double RoundPlain(double value) =>
        double.IsFinite(value) ? Math.Round(value, MaxDecimals, MidpointRounding.AwayFromZero) : value;

    /// <summary>
    /// LaTeX rendering with unit, e.g. 5.385e-5 A → "53.85\;\mu\text{A}".
    /// </summary>
    public static string Latex(double siValue, QuantityKind kind)
    {
        if (double.IsNaN(siValue)) return @"\text{NaN}";
        if (double.IsPositiveInfinity(siValue)) return @"\infty";
        if (double.IsNegativeInfinity(siValue)) return @"-\infty";
        if (siValue == 0)
        {
            string zeroUnit = kind == QuantityKind.Ratio ? string.Empty : LatexUnit(UnitOf(kind, 1));
            return zeroUnit.Length == 0 ? "0" : $@"0\;{zeroUnit}";
        }

        (double scale, string unit) = SelectUnit(siValue, kind);
        string number = (siValue / scale).ToString("0.##", CultureInfo.InvariantCulture);
        return $@"{number}\;{LatexUnit(unit)}";
    }

    /// <summary>
    /// LaTeX rendering of the number only (no unit), e.g. "-45.6".
    /// </summary>
    public static string LatexNumber(double siValue, QuantityKind kind)
    {
        if (!double.IsFinite(siValue)) return siValue > 0 ? @"\infty" : @"-\infty";
        (double scale, _) = SelectUnit(siValue, kind);
        return (siValue / scale).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static DisplayQuantity Build(double siValue, double scale, string unit)
    {
        double roundedSi = Round(siValue, scale);
        double shown = roundedSi / scale;
        // Avoid negative zero in the display ("-0").
        if (shown == 0) shown = 0;
        string number = shown.ToString("0.##", CultureInfo.InvariantCulture);
        return new DisplayQuantity(roundedSi, number, unit);
    }

    private static double Round(double siValue, double scale) =>
        Math.Round(siValue / scale, MaxDecimals, MidpointRounding.AwayFromZero) * scale;

    private static (double Scale, string Unit) SelectUnit(double siValue, QuantityKind kind)
    {
        double abs = Math.Abs(siValue);
        return kind switch
        {
            QuantityKind.Voltage => abs >= 1 ? (1, "V") : abs >= 1e-3 ? (1e-3, "mV") : (1e-6, "μV"),
            QuantityKind.Current => abs >= 1 ? (1, "A") : abs >= 1e-3 ? (1e-3, "mA") : (1e-6, "μA"),
            QuantityKind.Resistance => abs >= 1e6 ? (1e6, "MΩ") : abs >= 1e3 ? (1e3, "kΩ") : (1, "Ω"),
            QuantityKind.Capacitance => abs >= 1e-3 ? (1e-3, "mF") : abs >= 1e-6 ? (1e-6, "μF") :
                abs >= 1e-9 ? (1e-9, "nF") : (1e-12, "pF"),
            QuantityKind.Inductance => abs >= 1e-3 ? (1e-3, "mH") : abs >= 1e-6 ? (1e-6, "μH") : (1e-9, "nH"),
            QuantityKind.Frequency => abs >= 1e6 ? (1e6, "MHz") : abs >= 1e3 ? (1e3, "kHz") : (1, "Hz"),
            QuantityKind.Time => abs >= 1 ? (1, "s") : abs >= 1e-3 ? (1e-3, "ms") : abs >= 1e-6 ? (1e-6, "μs") : (1e-9, "ns"),
            _ => (1, string.Empty)
        };
    }

    private static string UnitOf(QuantityKind kind, double scale) => kind switch
    {
        QuantityKind.Voltage => "V",
        QuantityKind.Current => "A",
        QuantityKind.Resistance => "Ω",
        QuantityKind.Capacitance => "F",
        QuantityKind.Inductance => "H",
        QuantityKind.Frequency => "Hz",
        QuantityKind.Time => "s",
        _ => string.Empty
    };

    private static string LatexUnit(string unit) => unit switch
    {
        "" => string.Empty,
        "V" => @"\text{V}",
        "mV" => @"\text{mV}",
        "μV" => @"\mu\text{V}",
        "A" => @"\text{A}",
        "mA" => @"\text{mA}",
        "μA" => @"\mu\text{A}",
        "Ω" => @"\Omega",
        "kΩ" => @"\text{k}\Omega",
        "MΩ" => @"\text{M}\Omega",
        "F" => @"\text{F}",
        "mF" => @"\text{mF}",
        "μF" => @"\mu\text{F}",
        "nF" => @"\text{nF}",
        "pF" => @"\text{pF}",
        "H" => @"\text{H}",
        "mH" => @"\text{mH}",
        "μH" => @"\mu\text{H}",
        "nH" => @"\text{nH}",
        "Hz" => @"\text{Hz}",
        "kHz" => @"\text{kHz}",
        "MHz" => @"\text{MHz}",
        "s" => @"\text{s}",
        "ms" => @"\text{ms}",
        "μs" => @"\mu\text{s}",
        "ns" => @"\text{ns}",
        _ => @"\text{" + unit + "}"
    };
}
