// ============================================================================
// File: apps/desktop-winui/Controls/CircuitSymbolPreview.xaml.cs
// Description: 符号库 / 检查器中的元件符号卡片预览。
//
// 重构说明（2026-10-18，task-6）
//   * 原实现手写 switch-case + SVG path 字符串，与画布
//     （Controls/CircuitCanvas.xaml.cs）各有一套符号几何且不一致。
//   * 现在统一从 Models/SymbolGeometry.cs（IEC 60617 / IEEE 315 几何库）取
//     元件局部坐标图元，在 100x100 卡片画布内自动缩放、居中后渲染。
//   * 卡片 SymbolId 先映射到 ElementType（passive.* / bjt.* / fet.* /
//     source.* / controlled.* / reference.* / diode.* 等定义 id）。
//   * VCC 电源端口按 Multisim 参考图绘制“电源横杠 + 引线”，不再画向上三角
//     箭头；“VCC” 居中文字由卡片文本层单独绘制（不进入几何库）。
// ============================================================================

using System;
using System.Numerics;
using CircuitAssistant.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CircuitAssistant.Controls;

public sealed partial class CircuitSymbolPreview : UserControl
{
    // 卡片逻辑画布固定为 100 x 100（见 XAML），由外层 Viewbox 等比缩放。
    private const float CardSize = 100f;
    private const float CardMargin = 6f;

    // VCC 端口卡片顶部预留的空间，用于居中绘制 “VCC” 文本（Multisim 风格）。
    private const float VccCaptionReserve = 22f;

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

    /// <summary>按当前 SymbolId / 主题重新渲染卡片符号。</summary>
    public void Render()
    {
        try
        {
            SymbolCanvas.Children.Clear();
            var stroke = GetStrokeBrush();
            var (type, ansiResistor) = ResolveSymbol(SymbolId);
            var primitives = ansiResistor
                ? SymbolGeometryLibrary.BuildAnsiResistor()
                : SymbolGeometryLibrary.Build(type);
            RenderPrimitives(primitives, stroke, type);
        }
        catch (Exception ex)
        {
            App.LogTrace($"[CircuitSymbolPreview Render] failed for '{SymbolId}': {ex.Message}");
        }
    }

    private void RenderPrimitives(
        System.Collections.Generic.IReadOnlyList<SymbolPrimitive> primitives,
        Brush stroke,
        ElementType type)
    {
        var (min, max) = SymbolGeometryLibrary.GetBounds(primitives);
        float contentWidth = MathF.Max(max.X - min.X, 1f);
        float contentHeight = MathF.Max(max.Y - min.Y, 1f);

        // 自动缩放并居中到 100x100 卡片。VCC 端口顶部额外预留文字空间。
        float topReserve = type == ElementType.VccPort ? VccCaptionReserve : CardMargin;
        float availableWidth = CardSize - CardMargin * 2f;
        float availableHeight = CardSize - topReserve - CardMargin;
        float scale = MathF.Min(availableWidth / contentWidth, availableHeight / contentHeight);
        if (!float.IsFinite(scale) || scale <= 0f) scale = 1f;

        var contentCenter = (min + max) * 0.5f;
        var targetCenter = new Vector2(CardMargin + availableWidth / 2f, topReserve + availableHeight / 2f);

        Vector2 ToCanvas(Vector2 localPoint) => targetCenter + (localPoint - contentCenter) * scale;
        double ToCanvasThickness(float localThickness) => Math.Clamp(localThickness * scale, 1.05, 3.0);

        foreach (var primitive in primitives)
        {
            switch (primitive.Kind)
            {
                case SymbolPrimitiveKind.Polyline:
                    AddPolyline(stroke, primitive, ToCanvas, ToCanvasThickness);
                    break;

                case SymbolPrimitiveKind.FilledPolygon:
                    AddPolygon(stroke, primitive, ToCanvas);
                    break;

                case SymbolPrimitiveKind.Circle:
                    AddCircle(stroke, primitive, ToCanvas, ToCanvasThickness);
                    break;
            }
        }

        // Multisim 风格：VCC 端口符号上方居中绘制 “VCC” 文本（卡片文本层，几何库不含文字）。
        if (type == ElementType.VccPort)
        {
            var caption = new TextBlock
            {
                Text = "VCC",
                Width = CardSize,
                TextAlignment = TextAlignment.Center,
                FontSize = 14,
                FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
                Foreground = stroke
            };
            Canvas.SetLeft(caption, 0);
            Canvas.SetTop(caption, 1);
            SymbolCanvas.Children.Add(caption);
        }
    }

    private void AddPolyline(
        Brush stroke,
        SymbolPrimitive primitive,
        Func<Vector2, Vector2> toCanvas,
        Func<float, double> toCanvasThickness)
    {
        if (primitive.Points.Count < 2) return;

        var polyline = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = toCanvasThickness(primitive.Thickness),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        foreach (var point in primitive.Points)
        {
            var canvasPoint = toCanvas(point);
            polyline.Points.Add(new Point(canvasPoint.X, canvasPoint.Y));
        }
        SymbolCanvas.Children.Add(polyline);
    }

    private void AddPolygon(Brush fill, SymbolPrimitive primitive, Func<Vector2, Vector2> toCanvas)
    {
        if (primitive.Points.Count < 3) return;

        var polygon = new Polygon
        {
            Fill = fill,
            Stroke = fill,
            StrokeThickness = 0.5,
            StrokeLineJoin = PenLineJoin.Round
        };
        foreach (var point in primitive.Points)
        {
            var canvasPoint = toCanvas(point);
            polygon.Points.Add(new Point(canvasPoint.X, canvasPoint.Y));
        }
        SymbolCanvas.Children.Add(polygon);
    }

    private void AddCircle(
        Brush stroke,
        SymbolPrimitive primitive,
        Func<Vector2, Vector2> toCanvas,
        Func<float, double> toCanvasThickness)
    {
        if (primitive.Points.Count < 2) return;

        var canvasCenter = toCanvas(primitive.Points[0]);
        var canvasEdge = toCanvas(primitive.Points[1]);
        float radius = Vector2.Distance(canvasCenter, canvasEdge);
        if (radius <= 0.1f) return;

        var ellipse = new Ellipse
        {
            Width = radius * 2f,
            Height = radius * 2f,
            Stroke = stroke,
            StrokeThickness = toCanvasThickness(primitive.Thickness),
            Fill = primitive.Filled ? stroke : null
        };
        Canvas.SetLeft(ellipse, canvasCenter.X - radius);
        Canvas.SetTop(ellipse, canvasCenter.Y - radius);
        SymbolCanvas.Children.Add(ellipse);
    }

    /// <summary>
    /// 卡片 SymbolId → ElementType 映射。SymbolId 来自
    /// ViewModels/AnalysisStudioViewModel.InitializeSymbols() 的 CircuitSymbolItem.Id
    /// （也即 CircuitElement.DefinitionId）。
    /// </summary>
    private static (ElementType Type, bool AnsiResistor) ResolveSymbol(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return (ElementType.Resistor, false);
        string s = id.Trim().ToLowerInvariant();

        // 参考地 / 电源端口
        if (s.Contains("ground") || s == "gnd" || s == "0") return (ElementType.Ground, false);
        if (s.Contains("vcc_port")) return (ElementType.VccPort, false);

        // 无源器件
        if (s.Contains("transformer")) return (ElementType.Transformer, false);
        if (s.Contains("resistor_ansi") || s.Contains("_ansi") || s == "ansi") return (ElementType.Resistor, true);

        // 受控源（必须在 passive 的首字母启发式与 vcc 判断之前，
        // 否则 controlled.vcvs / controlled.vccs 会被误判为电容或电源）
        if (s.Contains("vcvs")) return (ElementType.Vcvs, false);
        if (s.Contains("vccs")) return (ElementType.Vccs, false);
        if (s.Contains("ccvs")) return (ElementType.Ccvs, false);
        if (s.Contains("cccs")) return (ElementType.Cccs, false);

        // 场效应管
        if (s.Contains("mosfet_n_enh") || s.Contains("n_enh") || s.Contains("nmos")) return (ElementType.MosfetNEnh, false);
        if (s.Contains("mosfet_p_enh") || s.Contains("p_enh") || s.Contains("pmos")) return (ElementType.MosfetPEnh, false);
        if (s.Contains("mosfet_n_dep") || s.Contains("n_dep")) return (ElementType.MosfetNDep, false);
        if (s.Contains("mosfet_p_dep") || s.Contains("p_dep")) return (ElementType.MosfetPDep, false);
        if (s.Contains("jfet_n")) return (ElementType.JfetN, false);
        if (s.Contains("jfet_p")) return (ElementType.JfetP, false);
        if (s.Contains("mosfet")) return (ElementType.Mosfet, false);
        if (s.Contains("jfet")) return (ElementType.Jfet, false);

        // 双极型三极管
        if (s.Contains("pnp")) return (ElementType.BjtPnp, false);
        if (s.Contains("npn") || s.Contains("bjt") || s.StartsWith("q")) return (ElementType.BjtNpn, false);

        // 独立源
        if (s.Contains("i_ac") || s == "iac") return (ElementType.AcCurrentSource, false);
        if (s.Contains("v_ac") || s.Contains("vac")) return (ElementType.AcVoltageSource, false);
        if (s.Contains("i_dc") || s == "idc") return (ElementType.DcCurrentSource, false);
        if (s.Contains("v_dc") || s.Contains("vdc")) return (ElementType.DcVoltageSource, false);
        if (s.Contains("vcc")) return (ElementType.DcVoltageSource, false);

        // 二极管
        if (s.Contains("diode") || s.StartsWith("d")) return (ElementType.Diode, false);

        // 无源器件（首字母启发式放在最后，避免与 semiconductor / source / controlled 冲突）
        if (s.Contains("capacitor_polarized") || s.Contains("polarized") || s.Contains("electrolytic"))
            return (ElementType.CapacitorPolarized, false);
        if (s.Contains("capacitor") || s.StartsWith("c")) return (ElementType.Capacitor, false);
        if (s.Contains("inductor") || s.StartsWith("l")) return (ElementType.Inductor, false);
        if (s.Contains("resistor") || s.StartsWith("r")) return (ElementType.Resistor, false);

        return (ElementType.Resistor, false);
    }
}
