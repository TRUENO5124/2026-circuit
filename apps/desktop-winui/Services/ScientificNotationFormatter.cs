using System;
using System.Globalization;

namespace CircuitAssistant.Services;

public static class ScientificNotationFormatter
{
    /// <summary>
    /// Formats a float/double into LaTeX scientific notation without raw 'e' notation.
    /// E.g. 1000 -> "10^{3}", 6280 -> "6.28 \times 10^{3}", 0.00002 -> "2 \times 10^{-5}".
    /// </summary>
    public static string ToLatexScientific(double value, int precision = 3)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (Math.Abs(value) < 1e-15)
        {
            return "0";
        }

        double absVal = Math.Abs(value);

        // For moderate values, return clean decimal
        if (absVal >= 0.01 && absVal < 1000)
        {
            return Math.Round(value, precision).ToString(CultureInfo.InvariantCulture);
        }

        // Scientific format e.g. "6.28e+03"
        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string mantissaStr = mantissa.ToString(CultureInfo.InvariantCulture);

        if (Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10^{{{exponent}}}";
        }
        if (Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10^{{{exponent}}}";
        }

        return $"{mantissaStr} \\times 10^{{{exponent}}}";
    }

    /// <summary>
    /// Formats a float/double into standard Unicode scientific notation.
    /// E.g. 1000 -> "10³", 6280 -> "6.28 × 10³".
    /// </summary>
    public static string ToUnicodeScientific(double value, int precision = 3)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (Math.Abs(value) < 1e-15)
        {
            return "0";
        }

        double absVal = Math.Abs(value);
        if (absVal >= 0.01 && absVal < 1000)
        {
            return Math.Round(value, precision).ToString(CultureInfo.InvariantCulture);
        }

        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string supExp = ToSuperscript(exponent);

        if (Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10{supExp}";
        }
        if (Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10{supExp}";
        }

        return $"{mantissa.ToString(CultureInfo.InvariantCulture)} × 10{supExp}";
    }

    private static string ToSuperscript(int exp)
    {
        string s = exp.ToString(CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '-' => '⁻',
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
