// ============================================================================
// File: apps/desktop-winui/Math/EstimateMathTextMeasurer.cs
// Description: Font-free approximation of run widths. Used as the fallback
//              measurer before the Win2D canvas device is ready (and whenever
//              DirectWrite layout creation fails), so a formula still lays out
//              with a sensible height instead of collapsing to zero.
// ============================================================================

namespace CircuitAssistant.MathRendering;

/// <summary>Heuristic measurer based on average glyph advance widths (em units).</summary>
public sealed class EstimateMathTextMeasurer : IMathTextMeasurer
{
    public static EstimateMathTextMeasurer Instance { get; } = new();

    public MathTextMetrics Measure(string text, float fontSize, bool italic, bool bold)
    {
        if (fontSize <= 0f)
        {
            return new MathTextMetrics(0f, 0f, 0f);
        }

        float ascent = fontSize * 0.78f;
        float descent = fontSize * 0.24f;
        if (string.IsNullOrEmpty(text))
        {
            return new MathTextMetrics(0f, ascent, descent);
        }

        float em = 0f;
        foreach (char c in text)
        {
            em += CharWidthEm(c);
        }

        if (italic)
        {
            em += 0.02f;
        }

        if (bold)
        {
            em += 0.03f * text.Length;
        }

        return new MathTextMetrics(em * fontSize, ascent, descent);
    }

    /// <summary>Average advance of a glyph in em units. Empirically close to Cambria Math / Segoe UI.</summary>
    public static float CharWidthEm(char c)
    {
        switch (c)
        {
            case ' ':
            case '\u00A0':
                return 0.30f;
            case '\t':
                return 0.60f;
            case '.':
            case ',':
            case ':':
            case ';':
            case '!':
            case '\'':
            case '\u2032': // prime
            case '\u2033': // double prime
                return 0.28f;
            case '+':
            case '-':
            case '\u2212': // minus
            case '\u00B1': // plus-minus
            case '\u2213': // minus-plus
            case '\u00D7': // times
            case '\u00B7': // cdot
            case '\u00F7': // divide
                return 0.60f;
            case '=':
            case '<':
            case '>':
            case '\u2264': // leq
            case '\u2265': // geq
            case '\u2260': // neq
            case '\u2261': // equiv
            case '\u221D': // propto
            case '\u2225': // parallel
            case '\u22C5': // cdot operator
                return 0.64f;
            case '/':
                return 0.32f;
            case '(':
            case ')':
            case '[':
            case ']':
            case '|':
                return 0.34f;
            case '{':
            case '}':
                return 0.42f;
            case '\u221E': // infinity
                return 0.72f;
            case '\u00B0': // degree
                return 0.36f;
            case '\u2202': // partial
            case '\u222B': // integral
                return 0.56f;
            case '\u2211': // sum
            case '\u220F': // product
                return 0.78f;
            case '\u2192': // rightarrow
            case '\u2190': // leftarrow
            case '\u21D2': // Rightarrow
            case '\u21D4': // Leftrightarrow
                return 0.78f;
            case '\u2220': // angle
                return 0.68f;
            case '\u22EF': // cdots
            case '\u2026': // ldots
                return 0.85f;
            case '\u22EE': // vdots
            case '\u22F1': // ddots
                return 0.42f;
            case '\u27E8': // langle
            case '\u27E9': // rangle
                return 0.36f;
        }

        if (c >= '0' && c <= '9')
        {
            return 0.52f;
        }

        if (c >= 'a' && c <= 'z')
        {
            return c switch
            {
                'i' or 'l' => 0.26f,
                'j' => 0.30f,
                'm' => 0.86f,
                'w' => 0.78f,
                'f' or 't' or 'r' => 0.36f,
                _ => 0.52f,
            };
        }

        if (c >= 'A' && c <= 'Z')
        {
            return c switch
            {
                'I' => 0.30f,
                'J' => 0.46f,
                'M' => 0.90f,
                'W' => 0.92f,
                'Q' => 0.74f,
                _ => 0.68f,
            };
        }

        // Greek and other symbols: lowercase 0.56, uppercase 0.68.
        if (c >= '\u0391' && c <= '\u03A9')
        {
            return 0.68f;
        }

        if (c >= '\u03B1' && c <= '\u03C9')
        {
            return 0.56f;
        }

        // CJK ideographs and full-width punctuation are roughly one em wide.
        if (c >= '\u2E80')
        {
            return 1.0f;
        }

        return 0.55f;
    }
}
