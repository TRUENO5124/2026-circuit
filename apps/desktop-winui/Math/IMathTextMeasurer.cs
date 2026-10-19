// ============================================================================
// File: apps/desktop-winui/Math/IMathTextMeasurer.cs
// Description: Text measurement abstraction for the math layout engine.
//              Implemented for production by the Win2D measurer inside
//              Controls/MathFormulaView.xaml.cs and by deterministic fakes in
//              tests/MathFormulaChecks so layout logic stays unit-testable
//              without a UI thread / DirectWrite device.
// ============================================================================

namespace CircuitAssistant.MathRendering;

/// <summary>Measured metrics of one text run at a given font size (all values in DIPs).</summary>
public readonly struct MathTextMetrics
{
    public MathTextMetrics(float width, float ascent, float descent)
    {
        Width = width;
        Ascent = ascent;
        Descent = descent;
    }

    /// <summary>Advance width of the run.</summary>
    public float Width { get; }

    /// <summary>Distance from the run baseline up to the top of the line box.</summary>
    public float Ascent { get; }

    /// <summary>Distance from the run baseline down to the bottom of the line box.</summary>
    public float Descent { get; }

    /// <summary>Total line height (ascent + descent).</summary>
    public float Height => Ascent + Descent;

    public override string ToString() => $"w={Width:0.##} asc={Ascent:0.##} desc={Descent:0.##}";
}

/// <summary>
/// Measures math text runs. Implementations must be deterministic for a given
/// input and must never throw (fall back to estimates on failure).
/// </summary>
public interface IMathTextMeasurer
{
    /// <summary>
    /// Measures <paramref name="text"/> laid out as a single unwrapped line.
    /// An empty string returns zero width with the line metrics of the requested size.
    /// </summary>
    MathTextMetrics Measure(string text, float fontSize, bool italic, bool bold);
}
