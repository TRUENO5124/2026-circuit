// ============================================================================
// File: tests/NonlinearCircuitChecks/TestQuantity.cs
// Description: Parses textbook engineering display strings ("0.53 μA",
//              "11.82 V", "10.31 kΩ", "415.24 kHz", "4 mS", "-1.11") back to
//              base SI units so phase/estimation checks can compare numbers.
// ============================================================================

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NonlinearCircuitChecks;

internal static class TestQuantity
{
    private static readonly Regex MetricRegex = new(
        @"^\s*([-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)\s*(.*)$", RegexOptions.Compiled);

    /// <summary>Parses a display string to base SI units (V, A, Ω, Hz, S) or a bare ratio.</summary>
    public static double Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        var match = MetricRegex.Match(text);
        if (!match.Success) return double.NaN;
        double value = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        string suffix = match.Groups[2].Value.Trim();
        if (suffix.Length == 0) return value;

        bool isSiemens = suffix.EndsWith("S", StringComparison.Ordinal)
                         && suffix.IndexOf('V') < 0 && suffix.IndexOf('A') < 0;
        if (isSiemens) return value * PrefixScale(suffix);
        if (suffix.IndexOf('Ω') >= 0) return value * PrefixScale(suffix);
        if (suffix.IndexOf("Hz", StringComparison.Ordinal) >= 0)
        {
            double scale = suffix[0] switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'G' => 1e9,
                _ => 1.0
            };
            return value * scale;
        }
        if (suffix.IndexOf('A') >= 0) return value * PrefixScale(suffix);
        if (suffix.IndexOf('V') >= 0) return value * PrefixScale(suffix);
        return value;
    }

    /// <summary>"kΩ" → 1e3, "μA" → 1e-6, "mV" → 1e-3, "MΩ" → 1e6.</summary>
    private static double PrefixScale(string suffix) => suffix.Length == 0 ? 1.0 : suffix[0] switch
    {
        'k' => 1e3,
        'M' => 1e6,
        'G' => 1e9,
        'm' => 1e-3,
        'μ' or 'u' => 1e-6,
        'n' => 1e-9,
        'p' => 1e-12,
        _ => 1.0
    };
}
