// ============================================================================
// File: apps/desktop-winui/Services/ScientificNotationFormatter.cs
// Namespace: CircuitAssistant.Services
// Description: Industrial-Grade Scientific Notation and SI Unit Formatter for EDA.
// ============================================================================

using System;
using System.Globalization;

namespace CircuitAssistant.Services;

/// <summary>
/// Professional scientific notation and engineering unit formatter for electrical EDA.
/// Enforces industrial standard: values >= 1000 or < 0.001 are formatted in scientific notation,
/// with robust handling for NaN, Infinity, and zero.
/// </summary>
public static class ScientificNotationFormatter
{
    /// <summary>
    /// Formats a double into standard LaTeX scientific notation (e.g. 6.28 \times 10^{3}, 2.00 \times 10^{-5}).
    /// </summary>
    public static string ToLatexScientific(double value, int precision = 3, bool fixedDecimals = false)
    {
        if (double.IsNaN(value)) return @"\text{NaN}";
        if (double.IsPositiveInfinity(value)) return @"\infty";
        if (double.IsNegativeInfinity(value)) return @"-\infty";

        if (Math.Abs(value) < 1e-15) return "0";

        double absVal = Math.Abs(value);

        // Moderate numbers (0.001 <= |v| < 1000)
        if (absVal >= 0.001 && absVal < 1000)
        {
            string fmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
            return Math.Round(value, precision).ToString(fmt, CultureInfo.InvariantCulture);
        }

        // Scientific format e.g. "6.28e+03"
        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2) return value.ToString(CultureInfo.InvariantCulture);

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string mantissaFmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
        string mantissaStr = Math.Round(mantissa, precision).ToString(mantissaFmt, CultureInfo.InvariantCulture);

        if (!fixedDecimals && Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10^{{{exponent}}}";
        }
        if (!fixedDecimals && Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10^{{{exponent}}}";
        }

        return $"{mantissaStr} \\times 10^{{{exponent}}}";
    }

    /// <summary>
    /// Formats a double into standard Unicode scientific notation (e.g. 6.28 × 10³, 2.00 × 10⁻⁵).
    /// </summary>
    public static string ToUnicodeScientific(double value, int precision = 3, bool fixedDecimals = false)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";

        if (Math.Abs(value) < 1e-15) return "0";

        double absVal = Math.Abs(value);
        if (absVal >= 0.001 && absVal < 1000)
        {
            string fmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
            return Math.Round(value, precision).ToString(fmt, CultureInfo.InvariantCulture);
        }

        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2) return value.ToString(CultureInfo.InvariantCulture);

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string supExp = ToSuperscript(exponent);

        if (!fixedDecimals && Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10{supExp}";
        }
        if (!fixedDecimals && Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10{supExp}";
        }

        string mantissaFmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
        string mantissaStr = Math.Round(mantissa, precision).ToString(mantissaFmt, CultureInfo.InvariantCulture);
        return $"{mantissaStr} × 10{supExp}";
    }

    /// <summary>
    /// Formats an engineering value with appropriate SI engineering prefix (e.g. 1.25 mA, 10 kΩ, 4.7 μF, 100 kHz).
    /// </summary>
    public static string FormatWithUnit(double value, string baseUnit, int precision = 3, bool useLatex = false)
    {
        if (double.IsNaN(value)) return useLatex ? $@"\text{{NaN {baseUnit}}}" : $"NaN {baseUnit}";
        if (double.IsPositiveInfinity(value)) return useLatex ? $@"\infty\text{{ {baseUnit}}}" : $"∞ {baseUnit}";
        if (double.IsNegativeInfinity(value)) return useLatex ? $@"-\infty\text{{ {baseUnit}}}" : $"-∞ {baseUnit}";

        if (Math.Abs(value) < 1e-15)
        {
            return useLatex ? $@"0\text{{ {baseUnit}}}" : $"0 {baseUnit}";
        }

        double absVal = Math.Abs(value);

        // Find engineering decade: 10^(3*k)
        int exp3 = (int)Math.Floor(Math.Log10(absVal) / 3.0) * 3;

        // Supported prefixes: f (10^-15) to T (10^12)
        if (exp3 < -15 || exp3 > 12)
        {
            return useLatex
                ? $"{ToLatexScientific(value, precision)}\\text{{ {baseUnit}}}"
                : $"{ToUnicodeScientific(value, precision)} {baseUnit}";
        }

        double scaled = value / Math.Pow(10, exp3);
        string prefix = exp3 switch
        {
            12 => "T",
            9 => "G",
            6 => "M",
            3 => "k",
            0 => "",
            -3 => "m",
            -6 => "μ",
            -9 => "n",
            -12 => "p",
            -15 => "f",
            _ => ""
        };

        string scaledStr = Math.Round(scaled, precision).ToString("0.###", CultureInfo.InvariantCulture);
        string unitStr = $"{prefix}{baseUnit}";

        if (useLatex)
        {
            // In LaTeX, replace Greek micro with \mu and Ohm with \Omega
            string latexUnit = unitStr.Replace("μ", @"\mu ").Replace("Ω", @"\Omega");
            return $"{scaledStr}\\text{{ {latexUnit}}}";
        }

        return $"{scaledStr} {unitStr}";
    }

    public static string ToSuperscript(int exp)
    {
        string s = exp.ToString(CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '-' => '⁻',
                '+' => '⁺',
                '0' => '⁰',
                '1' => '¹',
                '2' => '²',
                '3' => '³',
                '4' => '⁴',
                '5' => '⁵',
                '6' => '⁶',
                '7' => '⁷',
                '8' => '⁸',
                '9' => '⁹',
                _ => c
            });
        }
        return sb.ToString();
    }
}
