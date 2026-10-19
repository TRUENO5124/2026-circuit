// ============================================================================
// File: apps/desktop-winui/Controls/MathFormulaView.xaml.cs
// Description: Win2D (CanvasControl) based renderer for the teaching engine's
//              LaTeX formulas.
//
//              The control parses the LaTeX string into a MathNode tree
//              (Math/MathLatexParser.cs), lays it out for the current width
//              (Math/MathLayoutEngine.cs) and draws true sub/superscripts,
//              stacked fractions, radicals, matrices and auto-wrapped lines
//              with DirectWrite text. There is no horizontal clipping: the
//              formula wraps at the available width and the control grows
//              vertically to fit the content.
//
//              The copy-LaTeX button floats in the top-right corner and the
//              colours follow the current theme accent foreground.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CircuitAssistant.MathRendering;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace CircuitAssistant.Controls;

public sealed partial class MathFormulaView : UserControl
{
    /// <summary>Base formula font size in DIPs (visibly larger than the old 13.5 px text).</summary>
    public const float BaseFontSize = 16f;

    private const double FallbackWidth = 640.0;

    /// <summary>Grid padding (4,2) contributes 8 DIPs horizontally.</summary>
    private const float HorizontalPadding = 8f;

    private static readonly string[] MathFontCandidates = { "Cambria Math", "Latin Modern Math", "STIX Two Math", "Cambria", "Segoe UI" };
    private static readonly string[] CjkFontCandidates = { "Microsoft YaHei UI", "Microsoft YaHei", "SimSun", "Segoe UI" };

    public static readonly DependencyProperty FormulaProperty =
        DependencyProperty.Register(
            nameof(Formula),
            typeof(string),
            typeof(MathFormulaView),
            new PropertyMetadata(string.Empty, OnFormulaChanged));

    private IMathTextMeasurer _measurer = EstimateMathTextMeasurer.Instance;
    private Win2DMathTextMeasurer? _win2DMeasurer;
    private CanvasDevice? _device;
    private MathLayoutEngine _engine = new(EstimateMathTextMeasurer.Instance);

    private MathLayoutResult? _layout;
    private MathNode? _ast;
    private string _parsedFormula = string.Empty;
    private string _lastLayoutFormula = string.Empty;
    private float _lastLayoutWidth = -1f;

    private bool _loaded;
    private bool _selfCollapsed;
    private double _controlWidth = FallbackWidth;
    private float _copyReserve = 56f;

    public MathFormulaView()
    {
        InitializeComponent();
    }

    /// <summary>The LaTeX source formula to render.</summary>
    public string Formula
    {
        get => (string)GetValue(FormulaProperty);
        set => SetValue(FormulaProperty, value);
    }

    private static void OnFormulaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MathFormulaView view)
        {
            view.InvalidateFormula(force: true);
        }
    }

    // ==================================================================
    // Layout orchestration
    // ==================================================================
    private void MathFormulaView_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        if (ActualWidth > 1.0)
        {
            _controlWidth = ActualWidth;
        }

        _copyReserve = MeasureCopyButtonReserve();
        InvalidateFormula(force: true);
    }

    private void MathFormulaView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double width = e.NewSize.Width;
        if (width <= 1.0)
        {
            return;
        }

        if (Math.Abs(width - _controlWidth) < 0.5)
        {
            // Height-only change caused by our own canvas height update: no relayout.
            return;
        }

        _controlWidth = width;
        InvalidateFormula(force: false);
    }

    private void MathFormulaView_ActualThemeChanged(FrameworkElement sender, object args)
    {
        // Colours are resolved from the theme brush probe at draw time.
        try
        {
            FormulaCanvas?.Invalidate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MathFormulaView] theme invalidate failed: {ex.Message}");
        }
    }

    /// <summary>Recomputes the layout for the current width and updates the canvas size.</summary>
    private void InvalidateFormula(bool force)
    {
        if (!_loaded)
        {
            return;
        }

        double controlWidth = _controlWidth;
        if (double.IsNaN(controlWidth) || controlWidth < 16.0)
        {
            controlWidth = FallbackWidth;
        }

        float canvasWidth = (float)Math.Max(60.0, controlWidth - HorizontalPadding);
        float reserve = Math.Max(30f, _copyReserve);
        float formulaWidth = Math.Max(80f, canvasWidth - reserve);

        string formula = Formula ?? string.Empty;
        bool empty = string.IsNullOrWhiteSpace(formula);

        if (!force && !empty && _layout is not null &&
            Math.Abs(formulaWidth - _lastLayoutWidth) < 0.5f &&
            string.Equals(formula, _lastLayoutFormula, StringComparison.Ordinal))
        {
            return;
        }

        _lastLayoutFormula = formula;
        _lastLayoutWidth = formulaWidth;

        if (empty)
        {
            _layout = null;
            _ast = null;
            _parsedFormula = string.Empty;
            FormulaCanvas.Visibility = Visibility.Collapsed;
            CopyButton.Visibility = Visibility.Collapsed;
            FormulaCanvas.Width = 0;
            FormulaCanvas.Height = 0;
            // Collapse the whole control so an empty formula leaves no blank strip
            // (and no extra StackPanel spacing) behind.
            if (!_selfCollapsed)
            {
                _selfCollapsed = true;
                Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_selfCollapsed)
        {
            _selfCollapsed = false;
            Visibility = Visibility.Visible;
        }

        try
        {
            if (_ast is null || !string.Equals(formula, _parsedFormula, StringComparison.Ordinal))
            {
                _ast = MathLatexParser.Parse(formula);
                _parsedFormula = formula;
            }

            _layout = _engine.Layout(_ast, formulaWidth, BaseFontSize);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MathFormulaView] layout failed: {ex}");
            _layout = null;
        }

        FormulaCanvas.Visibility = Visibility.Visible;
        CopyButton.Visibility = Visibility.Visible;
        FormulaCanvas.Width = canvasWidth;

        float height = _layout is null ? 0f : (float)Math.Ceiling(_layout.Height);
        if (height < 1f)
        {
            height = (float)Math.Ceiling(BaseFontSize * 1.7f);
        }

        FormulaCanvas.Height = height;
        FormulaCanvas.Invalidate();
    }

    private float MeasureCopyButtonReserve()
    {
        try
        {
            CopyButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = CopyButton.DesiredSize.Width;
            if (width <= 0.0)
            {
                width = CopyButton.ActualWidth;
            }

            if (width <= 0.0)
            {
                return 56f;
            }

            return (float)Math.Clamp(Math.Ceiling(width) + 8.0, 34.0, 140.0);
        }
        catch
        {
            return 56f;
        }
    }

    // ==================================================================
    // Win2D resources / measurer
    // ==================================================================
    private void FormulaCanvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
        try
        {
            _device = sender.Device;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MathFormulaView] canvas device unavailable: {ex.Message}");
            _device = null;
        }

        if (_device is not null)
        {
            string mathFont = ProbeFontFamily(_device, MathFontCandidates, "x+1");
            string cjkFont = ProbeFontFamily(_device, CjkFontCandidates, "\u72B6\u6001");
            _win2DMeasurer = new Win2DMathTextMeasurer(_device, mathFont, cjkFont);
            _measurer = _win2DMeasurer;
            _engine = new MathLayoutEngine(_measurer);
            _ast = null; // force re-parse under the real measurer
            _lastLayoutWidth = -1f;
        }

        InvalidateFormula(force: true);
        FormulaCanvas.Invalidate();
    }

    private static string ProbeFontFamily(CanvasDevice device, string[] candidates, string sampleText)
    {
        foreach (string candidate in candidates)
        {
            try
            {
                var format = new CanvasTextFormat
                {
                    FontFamily = candidate,
                    FontSize = 16f,
                    WordWrapping = CanvasWordWrapping.NoWrap,
                };
                _ = new CanvasTextLayout(device, sampleText, format, 0f, 0f);
                return candidate;
            }
            catch
            {
                // Try the next family.
            }
        }

        return candidates[^1];
    }

    // ==================================================================
    // Drawing
    // ==================================================================
    private void FormulaCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        MathLayoutResult? layout = _layout;
        if (layout is null || layout.IsEmpty)
        {
            return;
        }

        CanvasDrawingSession ds = args.DrawingSession;
        ds.TextAntialiasing = CanvasTextAntialiasing.Auto;
        Color color = ResolveFormulaColor();
        Win2DMathTextMeasurer? textMeasurer = _win2DMeasurer;

        foreach (MathTextFragment fragment in layout.Fragments)
        {
            if (string.IsNullOrEmpty(fragment.Text))
            {
                continue;
            }

            try
            {
                MathTextMetrics metrics = _measurer.Measure(fragment.Text, fragment.FontSize, fragment.Italic, fragment.Bold);
                float top = fragment.BaselineY - metrics.Ascent;
                CanvasTextFormat? format = textMeasurer?.GetFormat(
                    fragment.FontSize,
                    fragment.Italic,
                    fragment.Bold,
                    Win2DMathTextMeasurer.ContainsCjk(fragment.Text));
                if (format is null)
                {
                    continue;
                }

                ds.DrawText(fragment.Text, fragment.X, top, color, format);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MathFormulaView] draw text '{fragment.Text}' failed: {ex.Message}");
            }
        }

        foreach (MathRule rule in layout.Rules)
        {
            try
            {
                DrawRule(ds, rule, color);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MathFormulaView] draw rule {rule.Kind} failed: {ex.Message}");
            }
        }

        foreach (MathDelimiter delimiter in layout.Delimiters)
        {
            try
            {
                DrawDelimiter(ds, delimiter, color);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MathFormulaView] draw delimiter {delimiter.Style} failed: {ex.Message}");
            }
        }
    }

    private Color ResolveFormulaColor()
    {
        if (ColorProbe.Foreground is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(255, 118, 185, 237)
            : Color.FromArgb(255, 0, 95, 184);
    }

    private static void DrawRule(CanvasDrawingSession ds, MathRule rule, Color color)
    {
        switch (rule.Kind)
        {
            case MathRuleKind.FractionBar:
            case MathRuleKind.SqrtBar:
            case MathRuleKind.MatrixRule:
                ds.DrawLine(rule.X, rule.Y, rule.X + rule.Width, rule.Y, color, rule.Thickness);
                return;

            case MathRuleKind.SqrtRadical:
            {
                // Radical sign: small down-stroke then the long ascending stroke.
                float x = rule.X;
                float y = rule.Y;
                float w = rule.Width;
                float h = rule.Height;
                ds.DrawLine(x + w * 0.10f, y + h * 0.30f, x + w * 0.42f, y + h * 0.22f, color, rule.Thickness);
                ds.DrawLine(x + w * 0.42f, y + h * 0.22f, x + w * 0.58f, y + h, color, rule.Thickness);
                ds.DrawLine(x + w * 0.58f, y + h, x + w, y, color, rule.Thickness);
                return;
            }

            case MathRuleKind.Hat:
            {
                float x = rule.X;
                float y = rule.Y;
                float w = rule.Width;
                float h = rule.Height;
                ds.DrawLine(x, y + h, x + w * 0.5f, y, color, rule.Thickness);
                ds.DrawLine(x + w * 0.5f, y, x + w, y + h, color, rule.Thickness);
                return;
            }

            case MathRuleKind.Dot:
                ds.FillCircle(rule.X, rule.Y, Math.Max(0.8f, rule.Width * 0.5f), color);
                return;
        }
    }

    private static void DrawDelimiter(CanvasDrawingSession ds, MathDelimiter delimiter, Color color)
    {
        float x = delimiter.X;
        float y = delimiter.Y;
        float w = Math.Max(2f, delimiter.Width);
        float h = Math.Max(4f, delimiter.Height);
        float thickness = Math.Max(1.1f, w * 0.16f);

        switch (delimiter.Style)
        {
            case MathDelimiterStyle.Bar:
                ds.DrawLine(x + w * 0.5f, y, x + w * 0.5f, y + h, color, thickness);
                return;

            case MathDelimiterStyle.SquareBracket:
            {
                if (delimiter.IsClosing)
                {
                    float xr = x + w * 0.72f;
                    ds.DrawLine(xr, y, xr, y + h, color, thickness);
                    ds.DrawLine(x + w * 0.18f, y, xr, y, color, thickness);
                    ds.DrawLine(x + w * 0.18f, y + h, xr, y + h, color, thickness);
                }
                else
                {
                    float xl = x + w * 0.28f;
                    ds.DrawLine(xl, y, xl, y + h, color, thickness);
                    ds.DrawLine(xl, y, x + w * 0.82f, y, color, thickness);
                    ds.DrawLine(xl, y + h, x + w * 0.82f, y + h, color, thickness);
                }

                return;
            }

            case MathDelimiterStyle.Parenthesis:
            {
                // Sample a sinusoidal arc; opening bulges left, closing bulges right.
                const int segments = 12;
                float previousX = 0f;
                float previousY = y;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float px = delimiter.IsClosing
                        ? x + w * (float)Math.Sin(Math.PI * t)
                        : x + w * (1f - (float)Math.Sin(Math.PI * t));
                    float py = y + h * t;
                    if (i > 0)
                    {
                        ds.DrawLine(previousX, previousY, px, py, color, thickness);
                    }

                    previousX = px;
                    previousY = py;
                }

                return;
            }

            case MathDelimiterStyle.Brace:
            {
                // Left curly brace drawn as a scalable polyline (no glyph scaling artefacts).
                float midX = x + w * 0.45f;
                float leftX = x + w * 0.05f;
                float rightX = x + w * 0.95f;
                float[] xs =
                {
                    rightX, midX, midX, leftX, midX, midX, rightX,
                };
                float[] ys =
                {
                    y,
                    y + h * 0.13f,
                    y + h * 0.42f,
                    y + h * 0.5f,
                    y + h * 0.58f,
                    y + h * 0.87f,
                    y + h,
                };
                for (int i = 1; i < xs.Length; i++)
                {
                    ds.DrawLine(xs[i - 1], ys[i - 1], xs[i], ys[i], color, thickness);
                }

                return;
            }
        }
    }

    // ==================================================================
    // Copy LaTeX
    // ==================================================================
    private DispatcherTimer? _copyResetTimer;

    private void CopyLatex_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Formula))
        {
            return;
        }

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(Formula);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            CopyIcon.Glyph = "\uE73E"; // checkmark
            CopyText.Text = "已复制";

            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyResetTimer.Tick += (s, args) =>
            {
                CopyIcon.Glyph = "\uE8C8";
                CopyText.Text = "LaTeX";
                _copyResetTimer?.Stop();
            };
            _copyResetTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MathFormulaView] copy LaTeX failed: {ex.Message}");
        }
    }

    // ==================================================================
    // Win2D text measurer
    // ==================================================================
    /// <summary>
    /// DirectWrite backed measurer. Falls back to <see cref="EstimateMathTextMeasurer"/>
    /// per run if layout creation fails, so a missing font can never crash the UI.
    /// CJK runs use a CJK-capable family so \text{饱和区} does not render tofu boxes.
    /// </summary>
    private sealed class Win2DMathTextMeasurer : IMathTextMeasurer
    {
        private readonly CanvasDevice _device;
        private readonly Dictionary<(int SizeKey, bool Italic, bool Bold, bool Cjk), CanvasTextFormat> _formats = new();
        private readonly Dictionary<(string Text, int SizeKey, bool Italic, bool Bold), MathTextMetrics> _cache = new();

        public Win2DMathTextMeasurer(CanvasDevice device, string mathFontFamily, string cjkFontFamily)
        {
            _device = device;
            MathFontFamily = mathFontFamily;
            CjkFontFamily = cjkFontFamily;
        }

        public string MathFontFamily { get; }

        public string CjkFontFamily { get; }

        public static bool ContainsCjk(string text)
        {
            foreach (char c in text)
            {
                if (c >= '\u2E80')
                {
                    return true;
                }
            }

            return false;
        }

        public CanvasTextFormat GetFormat(float fontSize, bool italic, bool bold, bool cjk)
        {
            int sizeKey = Math.Max(1, (int)MathF.Round(fontSize * 100f));
            var key = (sizeKey, italic, bold, cjk);
            if (_formats.TryGetValue(key, out CanvasTextFormat? cached))
            {
                return cached!;
            }

            var format = new CanvasTextFormat
            {
                FontFamily = cjk ? CjkFontFamily : MathFontFamily,
                FontSize = sizeKey / 100f,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
                FontWeight = new FontWeight { Weight = (ushort)(bold ? 700 : 400) },
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            _formats[key] = format;
            return format;
        }

        public MathTextMetrics Measure(string text, float fontSize, bool italic, bool bold)
        {
            if (fontSize <= 0f)
            {
                return new MathTextMetrics(0f, 0f, 0f);
            }

            float fallbackAscent = fontSize * 0.78f;
            float fallbackDescent = fontSize * 0.24f;
            if (string.IsNullOrEmpty(text))
            {
                return new MathTextMetrics(0f, fallbackAscent, fallbackDescent);
            }

            int sizeKey = Math.Max(1, (int)MathF.Round(fontSize * 100f));
            var key = (text, sizeKey, italic, bold);
            if (_cache.TryGetValue(key, out MathTextMetrics cached))
            {
                return cached;
            }

            MathTextMetrics metrics;
            try
            {
                CanvasTextFormat format = GetFormat(fontSize, italic, bold, ContainsCjk(text));
                var layout = new CanvasTextLayout(_device, text, format, 0f, 0f);
                CanvasLineMetrics[] lines = layout.LineMetrics;
                MathTextMetrics estimate = EstimateMathTextMeasurer.Instance.Measure(text, fontSize, italic, bold);

                // An unconstrained layout is a single line. If DirectWrite wrapped
                // anyway (defensive: unexpected requested-size semantics), the
                // reported width would be a per-line sliver, so trust the estimate.
                if (lines.Length > 1)
                {
                    metrics = estimate;
                }
                else
                {
                    float width = (float)layout.LayoutBoundsIncludingTrailingWhitespace.Width;
                    float ascent = fallbackAscent;
                    float descent = fallbackDescent;
                    if (lines.Length == 1)
                    {
                        CanvasLineMetrics line = lines[0];
                        if (float.IsFinite(line.Baseline) && line.Baseline > 0f)
                        {
                            ascent = line.Baseline;
                        }

                        if (float.IsFinite(line.Height) && line.Height > ascent)
                        {
                            descent = line.Height - ascent;
                        }
                    }

                    if (!float.IsFinite(width) || width <= 0f)
                    {
                        width = estimate.Width;
                    }

                    metrics = new MathTextMetrics(width, ascent, descent);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MathFormulaView] measure '{text}' failed: {ex.Message}");
                metrics = EstimateMathTextMeasurer.Instance.Measure(text, fontSize, italic, bold);
            }

            _cache[key] = metrics;
            return metrics;
        }
    }
}
