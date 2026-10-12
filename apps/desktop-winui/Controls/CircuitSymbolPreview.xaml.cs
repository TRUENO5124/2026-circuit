using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace CircuitAssistant.Controls;

public sealed partial class CircuitSymbolPreview : UserControl
{
    public static readonly DependencyProperty SymbolIdProperty =
        DependencyProperty.Register(
            nameof(SymbolId),
            typeof(string),
            typeof(CircuitSymbolPreview),
            new PropertyMetadata(string.Empty, OnSymbolChanged));

    public string SymbolId
    {
        get => (string)GetValue(SymbolIdProperty);
        set => SetValue(SymbolIdProperty, value);
    }

    public static readonly DependencyProperty SymbolColorProperty =
        DependencyProperty.Register(
            nameof(SymbolColor),
            typeof(Brush),
            typeof(CircuitSymbolPreview),
            new PropertyMetadata(null, OnSymbolChanged));

    public Brush? SymbolColor
    {
        get => (Brush?)GetValue(SymbolColorProperty);
        set => SetValue(SymbolColorProperty, value);
    }

    public CircuitSymbolPreview()
    {
        InitializeComponent();
        Loaded += (s, e) => Render();
        ActualThemeChanged += (s, e) => Render();
    }

    private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitSymbolPreview preview)
        {
            preview.Render();
        }
    }

    private Brush GetStrokeBrush()
    {
        if (SymbolColor != null) return SymbolColor;
        bool isDark = ActualTheme == ElementTheme.Dark;
        // High-contrast primary electrical symbol brush (Dark Slate in light theme, Cyan in dark theme)
        var color = isDark ? Color.FromArgb(255, 76, 194, 255) : Color.FromArgb(255, 15, 23, 42);
        return new SolidColorBrush(color);
    }

    public void Render()
    {
        SymbolCanvas.Children.Clear();
        var stroke = GetStrokeBrush();
        string key = NormalizeSymbolKey(SymbolId);

        switch (key)
        {
            case "ground":
                // Reference Ground (IEC 60617 / GB/T 4728)
                AddPath(stroke, "M50,0 V38 M22,38 H78 M32,50 H68 M42,62 H58", 2.8);
                break;

            case "resistor":
                // Rectangular Resistor (IEC / GB standard)
                AddPath(stroke, "M0,50 H28 M72,50 H100", 2.8);
                AddPath(stroke, "M28,36 H72 V64 H28 Z", 2.8);
                break;

            case "resistor_ansi":
                // Zig-zag Resistor (ANSI / IEEE)
                AddPath(stroke, "M0,50 H22 L29,34 L43,66 L57,34 L71,66 L78,50 H100", 2.8);
                break;

            case "capacitor":
                // Parallel Plate Capacitor
                AddPath(stroke, "M0,50 H44 M56,50 H100", 2.8);
                AddPath(stroke, "M44,22 V78 M56,22 V78", 3.0);
                break;

            case "inductor":
                // Arc Coils Inductor
                AddPath(stroke, "M0,50 H20 M80,50 H100", 2.8);
                AddPath(stroke, "M20,50 A7.5,7.5 0 0 1 35,50 A7.5,7.5 0 0 1 50,50 A7.5,7.5 0 0 1 65,50 A7.5,7.5 0 0 1 80,50", 2.8);
                break;

            case "transformer":
                // Ideal Transformer with magnetic core and polarity dots
                AddPath(stroke, "M0,30 H36 M0,70 H36 M64,30 H100 M64,70 H100", 2.4);
                AddPath(stroke, "M36,30 A6.67,6.67 0 0 0 36,43.33 A6.67,6.67 0 0 0 36,56.67 A6.67,6.67 0 0 0 36,70", 2.4);
                AddPath(stroke, "M47,20 V80 M53,20 V80", 2.4);
                AddPath(stroke, "M64,30 A6.67,6.67 0 0 1 64,43.33 A6.67,6.67 0 0 1 64,56.67 A6.67,6.67 0 0 1 64,70", 2.4);
                AddCircle(stroke, 20, 22, 3.2, true);
                AddCircle(stroke, 80, 22, 3.2, true);
                break;

            case "vdc":
                // DC Voltage Source (+ / - polarity)
                AddPath(stroke, "M50,0 V27 M50,73 V100", 2.6);
                AddCircle(stroke, 50, 50, 23, false, 2.6);
                AddPath(stroke, "M42,38 H58 M50,30 V46", 2.4);
                AddPath(stroke, "M42,63 H58", 2.4);
                break;

            case "idc":
                // DC Current Source (arrow)
                AddPath(stroke, "M50,0 V27 M50,73 V100", 2.6);
                AddCircle(stroke, 50, 50, 23, false, 2.6);
                AddPath(stroke, "M50,63 V38", 2.4);
                AddPolygon(stroke, new[] { 50.0, 30.0, 42.0, 42.0, 58.0, 42.0 }, true);
                break;

            case "vac":
                // AC Small-Signal Voltage Source (sine wave)
                AddPath(stroke, "M50,0 V27 M50,73 V100", 2.6);
                AddCircle(stroke, 50, 50, 23, false, 2.6);
                AddPath(stroke, "M35,50 C39,36 45,36 50,50 C55,64 61,64 65,50", 2.6);
                break;

            case "iac":
                // AC Small-Signal Current Source
                AddPath(stroke, "M50,0 V27 M50,73 V100", 2.6);
                AddCircle(stroke, 50, 50, 23, false, 2.6);
                AddPath(stroke, "M50,64 C58,58 42,52 50,46", 2.4);
                AddPolygon(stroke, new[] { 50.0, 28.0, 42.0, 42.0, 58.0, 42.0 }, true);
                break;

            case "diode":
                // Standard Rectifier Diode
                AddPath(stroke, "M0,50 H36 M64,50 H100", 2.8);
                AddPath(stroke, "M36,30 L64,50 L36,70 Z", 2.8, stroke);
                AddPath(stroke, "M64,28 V72", 3.0);
                break;

            case "bjt_npn":
                // NPN Transistor with outward emitter arrow
                AddCircle(stroke, 48, 50, 32, false, 1.8);
                AddPath(stroke, "M0,50 H36", 2.4);
                AddPath(stroke, "M36,26 V74", 3.8); // Base Bar
                AddPath(stroke, "M36,40 L60,22 V0", 2.4); // Collector
                AddPath(stroke, "M36,60 L60,78 V100", 2.4); // Emitter
                // Emitter arrow pointing OUT
                AddPolygon(stroke, new[] { 50.0, 68.0, 62.0, 79.0, 53.0, 82.0 }, true);
                break;

            case "bjt_pnp":
                // PNP Transistor with inward emitter arrow
                AddCircle(stroke, 48, 50, 32, false, 1.8);
                AddPath(stroke, "M0,50 H36", 2.4);
                AddPath(stroke, "M36,26 V74", 3.8);
                AddPath(stroke, "M36,40 L60,22 V0", 2.4);
                AddPath(stroke, "M36,60 L60,78 V100", 2.4);
                // Emitter arrow pointing IN
                AddPolygon(stroke, new[] { 60.0, 78.0, 48.0, 68.0, 54.0, 64.0 }, true);
                break;

            case "mosfet_n_enh":
                // NMOS Enhancement Transistor (segmented channel, arrow pointing IN)
                AddPath(stroke, "M0,50 H28", 2.4);
                AddPath(stroke, "M28,24 V76", 3.4); // Gate plate
                AddPath(stroke, "M44,24 V38 M44,44 V56 M44,62 V76", 3.2); // Segmented channel
                AddPath(stroke, "M44,28 H62 V0", 2.4); // Drain
                AddPath(stroke, "M44,72 H62 V100", 2.4); // Source
                AddPath(stroke, "M44,50 H75", 2.2);
                AddPolygon(stroke, new[] { 46.0, 50.0, 58.0, 44.0, 58.0, 56.0 }, true);
                break;

            case "mosfet_p_enh":
                // PMOS Enhancement Transistor (segmented channel, arrow pointing OUT)
                AddPath(stroke, "M0,50 H28", 2.4);
                AddPath(stroke, "M28,24 V76", 3.4); // Gate plate
                AddPath(stroke, "M44,24 V38 M44,44 V56 M44,62 V76", 3.2); // Segmented channel
                AddPath(stroke, "M44,28 H62 V0", 2.4); // Drain
                AddPath(stroke, "M44,72 H62 V100", 2.4); // Source
                AddPath(stroke, "M44,50 H75", 2.2);
                AddPolygon(stroke, new[] { 60.0, 50.0, 48.0, 44.0, 48.0, 56.0 }, true);
                break;

            case "mosfet_n_dep":
                // NMOS Depletion Transistor (solid channel, arrow pointing IN)
                AddPath(stroke, "M0,50 H28", 2.4);
                AddPath(stroke, "M28,24 V76", 3.4); // Gate plate
                AddPath(stroke, "M44,24 V76", 3.6); // Solid channel
                AddPath(stroke, "M44,28 H62 V0", 2.4); // Drain
                AddPath(stroke, "M44,72 H62 V100", 2.4); // Source
                AddPath(stroke, "M44,50 H75", 2.2);
                AddPolygon(stroke, new[] { 46.0, 50.0, 58.0, 44.0, 58.0, 56.0 }, true);
                break;

            case "mosfet_p_dep":
                // PMOS Depletion Transistor (solid channel, arrow pointing OUT)
                AddPath(stroke, "M0,50 H28", 2.4);
                AddPath(stroke, "M28,24 V76", 3.4); // Gate plate
                AddPath(stroke, "M44,24 V76", 3.6); // Solid channel
                AddPath(stroke, "M44,28 H62 V0", 2.4); // Drain
                AddPath(stroke, "M44,72 H62 V100", 2.4); // Source
                AddPath(stroke, "M44,50 H75", 2.2);
                AddPolygon(stroke, new[] { 60.0, 50.0, 48.0, 44.0, 48.0, 56.0 }, true);
                break;

            case "jfet_n":
                // JFET N-channel (arrow pointing IN)
                AddPath(stroke, "M0,50 H60", 2.4); // Gate
                AddPath(stroke, "M60,0 V100", 3.4); // Channel
                AddPolygon(stroke, new[] { 60.0, 50.0, 46.0, 44.0, 46.0, 56.0 }, true);
                break;

            case "jfet_p":
                // JFET P-channel (arrow pointing OUT)
                AddPath(stroke, "M0,50 H60", 2.4); // Gate
                AddPath(stroke, "M60,0 V100", 3.4); // Channel
                AddPolygon(stroke, new[] { 34.0, 50.0, 48.0, 44.0, 48.0, 56.0 }, true);
                break;

            case "vcvs":
                // Voltage-Controlled Voltage Source (Diamond + / -)
                DrawDiamondSource(stroke, isVoltageOut: true, isVoltageControl: true);
                break;

            case "vccs":
                // Voltage-Controlled Current Source (Diamond arrow)
                DrawDiamondSource(stroke, isVoltageOut: false, isVoltageControl: true);
                break;

            case "ccvs":
                // Current-Controlled Voltage Source
                DrawDiamondSource(stroke, isVoltageOut: true, isVoltageControl: false);
                break;

            case "cccs":
                // Current-Controlled Current Source
                DrawDiamondSource(stroke, isVoltageOut: false, isVoltageControl: false);
                break;

            default:
                // Default fallback: crisp resistor rectangle
                AddPath(stroke, "M0,50 H28 M72,50 H100 M28,36 H72 V64 H28 Z", 2.8);
                break;
        }
    }

    private void DrawDiamondSource(Brush stroke, bool isVoltageOut, bool isVoltageControl)
    {
        // Diamond
        AddPath(stroke, "M56,18 L86,50 L56,82 L26,50 Z", 2.6);
        AddPath(stroke, "M56,0 V18 M56,82 V100", 2.4);
        AddPath(stroke, "M0,34 H26 M0,66 H26", 2.2);

        if (isVoltageOut)
        {
            AddPath(stroke, "M50,36 H62 M56,30 V42 M50,64 H62", 2.4);
        }
        else
        {
            AddPath(stroke, "M56,40 V58", 2.4);
            AddPolygon(stroke, new[] { 56.0, 68.0, 49.0, 56.0, 63.0, 56.0 }, true);
        }

        if (isVoltageControl)
        {
            AddPath(stroke, "M12,26 H24 M18,20 V32 M12,74 H24", 2.2);
        }
        else
        {
            AddPolygon(stroke, new[] { 26.0, 34.0, 16.0, 28.0, 16.0, 40.0 }, true);
        }
    }

    private void AddPath(Brush stroke, string data, double thickness, Brush? fill = null)
    {
        try
        {
            Geometry? geom = null;
            try
            {
                geom = XamlBindingHelper.ConvertValue(typeof(Geometry), data) as Geometry;
            }
            catch { }

            if (geom == null)
            {
                var xaml = $"<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"{data}\" />";
                if (XamlReader.Load(xaml) is Microsoft.UI.Xaml.Shapes.Path parsed)
                {
                    geom = parsed.Data;
                }
            }

            if (geom != null)
            {
                var path = new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = geom,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    Fill = fill,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                SymbolCanvas.Children.Add(path);
            }
        }
        catch (Exception ex)
        {
            App.LogTrace($"[CircuitSymbolPreview AddPath] failed for '{data}': {ex.Message}");
        }
    }

    private void AddCircle(Brush stroke, double cx, double cy, double radius, bool filled = false, double thickness = 2.4)
    {
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = filled ? stroke : null
        };
        Canvas.SetLeft(ellipse, cx - radius);
        Canvas.SetTop(ellipse, cy - radius);
        SymbolCanvas.Children.Add(ellipse);
    }

    private void AddPolygon(Brush fillAndStroke, double[] coords, bool filled = true)
    {
        var polygon = new Polygon
        {
            Fill = filled ? fillAndStroke : null,
            Stroke = fillAndStroke,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        for (int i = 0; i < coords.Length; i += 2)
        {
            polygon.Points.Add(new Windows.Foundation.Point(coords[i], coords[i + 1]));
        }
        SymbolCanvas.Children.Add(polygon);
    }

    private static string NormalizeSymbolKey(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "resistor";
        string s = id.Trim().ToLowerInvariant();

        if (s.Contains("ground") || s == "gnd" || s == "0") return "ground";
        if (s.Contains("transformer")) return "transformer";
        if (s.Contains("resistor_ansi") || s.Contains("ansi")) return "resistor_ansi";
        if (s.Contains("vcvs")) return "vcvs";
        if (s.Contains("vccs")) return "vccs";
        if (s.Contains("ccvs")) return "ccvs";
        if (s.Contains("cccs")) return "cccs";
        if (s.Contains("mosfet_n_enh") || s.Contains("n_enh")) return "mosfet_n_enh";
        if (s.Contains("mosfet_p_enh") || s.Contains("p_enh")) return "mosfet_p_enh";
        if (s.Contains("mosfet_n_dep") || s.Contains("n_dep")) return "mosfet_n_dep";
        if (s.Contains("mosfet_p_dep") || s.Contains("p_dep")) return "mosfet_p_dep";
        if (s.Contains("jfet_n") || s == "jfet") return "jfet_n";
        if (s.Contains("jfet_p")) return "jfet_p";
        if (s.Contains("mosfet") || s.Contains("nmos")) return "mosfet_n_enh";
        if (s.Contains("pmos")) return "mosfet_p_enh";
        if (s.Contains("pnp")) return "bjt_pnp";
        if (s.Contains("npn") || s.Contains("bjt") || s.StartsWith("q")) return "bjt_npn";
        if (s.Contains("v_ac") || s.Contains("vac") || s == "vs") return "vac";
        if (s.Contains("i_ac") || s == "iac") return "iac";
        if (s.Contains("v_dc") || s.Contains("vdc") || s.Contains("vcc")) return "vdc";
        if (s.Contains("i_dc") || s == "idc") return "idc";
        if (s.Contains("diode") || s.StartsWith("d")) return "diode";
        if (s.Contains("inductor") || s.StartsWith("l")) return "inductor";
        if (s.Contains("capacitor") || s.StartsWith("c")) return "capacitor";
        if (s.Contains("resistor") || s.StartsWith("rb") || s.StartsWith("rc") || s.StartsWith("re") || s.StartsWith("rl") || s.StartsWith("r")) return "resistor";

        return "resistor";
    }
}
