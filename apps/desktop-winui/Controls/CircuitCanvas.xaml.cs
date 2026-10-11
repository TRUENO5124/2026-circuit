using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CircuitAssistant.Models;

namespace CircuitAssistant.Controls;

public sealed partial class CircuitCanvas : UserControl
{
    private float _scale = 1.0f;
    private Vector2 _translation = new(100f, 40f);

    // Canvas panning state
    private bool _isPanningCanvas;
    private Vector2 _lastPointerPos;

    // Element interaction state
    private bool _isDraggingElement;
    private CircuitElement? _draggedElement;
    private Vector2 _dragStartPointerPos;
    private Vector2 _dragStartElementPos;

    public event Action<float, float>? CursorCoordinatesChanged;
    public event Action<CircuitElement?>? ElementSelected;
    public event Action<CircuitElement>? ElementMoved;
    public event Action<string, Vector2>? ElementDroppedFromLibrary;

    // Elements Collection Dependency Property
    public static readonly DependencyProperty ElementsProperty =
        DependencyProperty.Register(
            nameof(Elements),
            typeof(ObservableCollection<CircuitElement>),
            typeof(CircuitCanvas),
            new PropertyMetadata(null, OnElementsPropertyChanged));

    public ObservableCollection<CircuitElement> Elements
    {
        get => (ObservableCollection<CircuitElement>)GetValue(ElementsProperty);
        set => SetValue(ElementsProperty, value);
    }

    public static readonly DependencyProperty SelectedElementProperty =
        DependencyProperty.Register(
            nameof(SelectedElement),
            typeof(CircuitElement),
            typeof(CircuitCanvas),
            new PropertyMetadata(null, OnSelectedElementPropertyChanged));

    public CircuitElement? SelectedElement
    {
        get => (CircuitElement?)GetValue(SelectedElementProperty);
        set => SetValue(SelectedElementProperty, value);
    }

    // Component label properties (kept for backwards compatibility / binding convenience)
    public static readonly DependencyProperty VccTextProperty =
        DependencyProperty.Register(nameof(VccText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("+12 V", OnRedrawNeeded));

    public static readonly DependencyProperty Rb1TextProperty =
        DependencyProperty.Register(nameof(Rb1Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("51 kΩ", OnRedrawNeeded));

    public static readonly DependencyProperty Rb2TextProperty =
        DependencyProperty.Register(nameof(Rb2Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("15 kΩ", OnRedrawNeeded));

    public static readonly DependencyProperty RcTextProperty =
        DependencyProperty.Register(nameof(RcText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("3 kΩ", OnRedrawNeeded));

    public static readonly DependencyProperty ReTextProperty =
        DependencyProperty.Register(nameof(ReText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("1 kΩ", OnRedrawNeeded));

    public static readonly DependencyProperty RlTextProperty =
        DependencyProperty.Register(nameof(RlText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 kΩ", OnRedrawNeeded));

    public static readonly DependencyProperty C1TextProperty =
        DependencyProperty.Register(nameof(C1Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 μF", OnRedrawNeeded));

    public static readonly DependencyProperty C2TextProperty =
        DependencyProperty.Register(nameof(C2Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 μF", OnRedrawNeeded));

    public static readonly DependencyProperty CeTextProperty =
        DependencyProperty.Register(nameof(CeText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("47 μF", OnRedrawNeeded));

    public static readonly DependencyProperty BetaTextProperty =
        DependencyProperty.Register(nameof(BetaText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("β = 100", OnRedrawNeeded));

    public string VccText { get => (string)GetValue(VccTextProperty); set => SetValue(VccTextProperty, value); }
    public string Rb1Text { get => (string)GetValue(Rb1TextProperty); set => SetValue(Rb1TextProperty, value); }
    public string Rb2Text { get => (string)GetValue(Rb2TextProperty); set => SetValue(Rb2TextProperty, value); }
    public string RcText { get => (string)GetValue(RcTextProperty); set => SetValue(RcTextProperty, value); }
    public string ReText { get => (string)GetValue(ReTextProperty); set => SetValue(ReTextProperty, value); }
    public string RlText { get => (string)GetValue(RlTextProperty); set => SetValue(RlTextProperty, value); }
    public string C1Text { get => (string)GetValue(C1TextProperty); set => SetValue(C1TextProperty, value); }
    public string C2Text { get => (string)GetValue(C2TextProperty); set => SetValue(C2TextProperty, value); }
    public string CeText { get => (string)GetValue(CeTextProperty); set => SetValue(CeTextProperty, value); }
    public string BetaText { get => (string)GetValue(BetaTextProperty); set => SetValue(BetaTextProperty, value); }

    public CircuitCanvas()
    {
        InitializeComponent();
        IsTabStop = true;
        Elements = new ObservableCollection<CircuitElement>();
        KeyDown += CircuitCanvas_KeyDown;
    }

    private static void OnElementsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            if (e.OldValue is ObservableCollection<CircuitElement> oldCol)
            {
                oldCol.CollectionChanged -= canvas.OnElementsCollectionChanged;
                foreach (var item in oldCol)
                {
                    item.PropertyChanged -= canvas.OnElementPropertyChanged;
                }
            }
            if (e.NewValue is ObservableCollection<CircuitElement> newCol)
            {
                newCol.CollectionChanged += canvas.OnElementsCollectionChanged;
                foreach (var item in newCol)
                {
                    item.PropertyChanged += canvas.OnElementPropertyChanged;
                }
            }
            canvas.Canvas.Invalidate();
        }
    }

    private void OnElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (CircuitElement el in e.NewItems)
            {
                el.PropertyChanged += OnElementPropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (CircuitElement el in e.OldItems)
            {
                el.PropertyChanged -= OnElementPropertyChanged;
            }
        }
        Canvas.Invalidate();
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Canvas.Invalidate();
        if (sender is CircuitElement el && el.IsSelected)
        {
            UpdateSelectionBar(el);
        }
    }

    private static void OnSelectedElementPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            var selected = e.NewValue as CircuitElement;
            foreach (var el in canvas.Elements)
            {
                el.IsSelected = (el == selected);
            }
            canvas.UpdateSelectionBar(selected);
            canvas.Canvas.Invalidate();
        }
    }

    private void UpdateSelectionBar(CircuitElement? el)
    {
        if (el != null)
        {
            SelectionActionBar.Visibility = Visibility.Visible;
            SelectionBarText.Text = $"已选中: {el.Name} ({el.FormattedValueDisplay})";
        }
        else
        {
            SelectionActionBar.Visibility = Visibility.Collapsed;
        }
    }

    private static void OnRedrawNeeded(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            canvas.Canvas.Invalidate();
        }
    }

    private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        return (screenPos - _translation) / _scale;
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        return worldPos * _scale + _translation;
    }

    public Vector2 GetViewportCenterInWorld()
    {
        var screenCenter = new Vector2((float)ActualWidth / 2f, (float)ActualHeight / 2f);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            screenCenter = new Vector2(400f, 300f);
        }
        return ScreenToWorld(screenCenter);
    }

    private CircuitElement? FindElement(string id)
    {
        return Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        try
        {
            var ds = args.DrawingSession;
            bool isDark = ActualTheme == ElementTheme.Dark;

            // Theme colors
            var wireColor = isDark ? Color.FromArgb(255, 76, 194, 255) : Color.FromArgb(255, 0, 95, 184); // Fluent Blue
            var componentColor = isDark ? Color.FromArgb(255, 235, 235, 235) : Color.FromArgb(255, 30, 30, 30);
            var labelColor = isDark ? Color.FromArgb(255, 200, 200, 200) : Color.FromArgb(255, 60, 60, 60);
            var gridColor = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0);
            var dotColor = isDark ? Color.FromArgb(255, 0, 164, 239) : Color.FromArgb(255, 0, 120, 215);
            var accentColor = isDark ? Color.FromArgb(255, 76, 194, 255) : Color.FromArgb(255, 0, 120, 215);
            var selectionFillColor = isDark ? Color.FromArgb(45, 0, 164, 239) : Color.FromArgb(35, 0, 120, 215);

            // World Transform
            var transform = Matrix3x2.CreateScale(_scale) * Matrix3x2.CreateTranslation(_translation);
            ds.Transform = transform;

            // 1. Grid
            DrawGrid(ds, gridColor);

            // 2. Dynamic Wires & Interconnections for amplifier circuit
            DrawCircuitWires(ds, wireColor, dotColor, componentColor, labelColor);

            // 3. Render all CircuitElements
            foreach (var el in Elements)
            {
                DrawElement(ds, el, componentColor, labelColor, wireColor, dotColor);

                // 4. Draw interactive selection highlight if selected
                if (el.IsSelected)
                {
                    DrawSelectionBox(ds, el, accentColor, selectionFillColor);
                }
            }
        }
        catch (Exception ex)
        {
            App.LogTrace($"[Canvas_Draw Exception] {ex.Message}");
        }
    }

    private void DrawCircuitWires(CanvasDrawingSession ds, Color wireColor, Color dotColor, Color componentColor, Color labelColor)
    {
        var vcc = FindElement("VCC");
        var gnd = FindElement("GND");
        var vs = FindElement("vs") ?? FindElement("VS");
        var rb1 = FindElement("Rb1");
        var rb2 = FindElement("Rb2");
        var rc = FindElement("Rc");
        var re = FindElement("Re");
        var rl = FindElement("RL") ?? FindElement("Rl");
        var c1 = FindElement("C1");
        var c2 = FindElement("C2");
        var ce = FindElement("Ce");
        var q1 = FindElement("Q1") ?? FindElement("BJT");

        // If classic BJT circuit components exist, draw schematic wires linking them
        if (rb1 != null && rb2 != null && q1 != null)
        {
            float vccY = vcc != null ? vcc.Y : 80f;
            float gndY = gnd != null ? gnd.Y : 440f;
            float baseNodeY = q1.Y;

            float minX = 70f;
            if (vs != null) minX = Math.Min(minX, vs.X - 20f);
            if (c1 != null) minX = Math.Min(minX, c1.X - 25f);
            minX = Math.Min(minX, rb1.X - 30f);

            float maxX = 540f;
            if (rl != null) maxX = Math.Max(maxX, rl.X + 40f);
            if (c2 != null) maxX = Math.Max(maxX, c2.X + 80f);

            // Supply Rail (Top: Y = vccY)
            ds.DrawLine(Math.Min(rb1.X - 20f, minX + 60f), vccY, maxX, vccY, wireColor, 2.5f);
            if (vcc != null)
            {
                ds.DrawText($"VCC ({vcc.FormattedValueDisplay})", maxX + 10f, vccY - 8, componentColor,
                    new CanvasTextFormat { FontSize = 13, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
            }

            // Ground Rail (Bottom: Y = gndY)
            ds.DrawLine(minX, gndY, maxX, gndY, wireColor, 2.5f);

            // Rb1 branch: (Rb1.X, vccY) -> Rb1 -> (Rb1.X, baseNodeY)
            ds.DrawLine(rb1.X, vccY, rb1.X, rb1.Y - 20, wireColor, 2f);
            ds.DrawLine(rb1.X, rb1.Y + 20, rb1.X, baseNodeY, wireColor, 2f);

            // Rb2 branch: (Rb2.X, baseNodeY) -> Rb2 -> (Rb2.X, gndY)
            ds.DrawLine(rb2.X, baseNodeY, rb2.X, rb2.Y - 20, wireColor, 2f);
            ds.DrawLine(rb2.X, rb2.Y + 20, rb2.X, gndY, wireColor, 2f);

            // Base wire connection to Q1
            float bjtBaseX = q1.X - 30;
            float bjtBaseY = q1.Y;
            ds.DrawLine(rb1.X, baseNodeY, bjtBaseX, bjtBaseY, wireColor, 2f);
            DrawJunctionDot(ds, new Vector2(rb1.X, baseNodeY), dotColor);

            // Input AC source vs and C1
            if (c1 != null)
            {
                ds.DrawLine(c1.X + 22, c1.Y, rb1.X, baseNodeY, wireColor, 2f);
                if (vs != null)
                {
                    ds.DrawLine(vs.X, vs.Y - 22, vs.X, c1.Y, wireColor, 2f);
                    ds.DrawLine(vs.X, c1.Y, c1.X - 22, c1.Y, wireColor, 2f);
                    ds.DrawLine(vs.X, vs.Y + 22, vs.X, gndY, wireColor, 2f);
                    DrawJunctionDot(ds, new Vector2(vs.X, gndY), dotColor);
                }
            }

            // Rc branch
            if (rc != null)
            {
                ds.DrawLine(rc.X, vccY, rc.X, rc.Y - 20, wireColor, 2f);
                DrawJunctionDot(ds, new Vector2(rc.X, vccY), dotColor);

                float collNodeY = rc.Y + 20 + 20;
                ds.DrawLine(rc.X, rc.Y + 20, rc.X, collNodeY, wireColor, 2f);
                ds.DrawLine(rc.X, collNodeY, q1.X, q1.Y - 30, wireColor, 2f);
                DrawJunctionDot(ds, new Vector2(rc.X, collNodeY), dotColor);

                // C2 connection from collector
                if (c2 != null)
                {
                    ds.DrawLine(rc.X, collNodeY, c2.X - 22, c2.Y, wireColor, 2f);
                }
            }

            // Re & Ce Emitter branches
            float emitNodeY = q1.Y + 30 + 20;
            ds.DrawLine(q1.X, q1.Y + 30, q1.X, emitNodeY, wireColor, 2f);
            DrawJunctionDot(ds, new Vector2(q1.X, emitNodeY), dotColor);

            if (re != null)
            {
                ds.DrawLine(q1.X, emitNodeY, re.X, emitNodeY, wireColor, 2f);
                ds.DrawLine(re.X, emitNodeY, re.X, re.Y - 20, wireColor, 2f);
                ds.DrawLine(re.X, re.Y + 20, re.X, gndY, wireColor, 2f);
                DrawJunctionDot(ds, new Vector2(re.X, gndY), dotColor);
            }

            if (ce != null)
            {
                ds.DrawLine(q1.X, emitNodeY, ce.X, emitNodeY, wireColor, 2f);
                ds.DrawLine(ce.X, emitNodeY, ce.X, ce.Y - 20, wireColor, 2f);
                ds.DrawLine(ce.X, ce.Y + 20, ce.X, gndY, wireColor, 2f);
                DrawJunctionDot(ds, new Vector2(ce.X, gndY), dotColor);
            }

            // C2 and RL branch
            if (c2 != null)
            {
                float outNodeX = rl != null ? rl.X : c2.X + 60;
                float outNodeY = c2.Y;
                ds.DrawLine(c2.X + 22, c2.Y, outNodeX, outNodeY, wireColor, 2f);
                DrawJunctionDot(ds, new Vector2(outNodeX, outNodeY), dotColor);

                // Output probe
                ds.DrawLine(outNodeX, outNodeY, outNodeX + 45, outNodeY, wireColor, 2f);
                ds.DrawCircle(outNodeX + 50, outNodeY, 4, dotColor);
                ds.DrawText("vo (Output)", outNodeX + 60, outNodeY - 8, componentColor,
                    new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });

                if (rl != null)
                {
                    ds.DrawLine(rl.X, outNodeY, rl.X, rl.Y - 20, wireColor, 2f);
                    ds.DrawLine(rl.X, rl.Y + 20, rl.X, gndY, wireColor, 2f);
                    DrawJunctionDot(ds, new Vector2(rl.X, gndY), dotColor);
                }
            }
        }
    }

    private void DrawElement(CanvasDrawingSession ds, CircuitElement el, Color componentColor, Color labelColor, Color wireColor, Color dotColor)
    {
        var center = new Vector2(el.X, el.Y);
        var originalTransform = ds.Transform;

        if (el.RotationDeg != 0)
        {
            var rot = Matrix3x2.CreateRotation((float)(el.RotationDeg * Math.PI / 180.0), center);
            ds.Transform = rot * originalTransform;
        }

        switch (el.Type)
        {
            case ElementType.Resistor:
                if (el.Orientation == "Vertical" || el.RotationDeg % 180 != 0)
                {
                    DrawResistorVertical(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                }
                else
                {
                    DrawResistorHorizontal(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                }
                break;

            case ElementType.Capacitor:
                if (el.Orientation == "Vertical" || el.RotationDeg % 180 != 0)
                {
                    DrawCapacitorVertical(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                }
                else
                {
                    DrawCapacitorHorizontal(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                }
                break;

            case ElementType.Inductor:
                DrawInductor(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.Transformer:
                DrawTransformer(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.BjtNpn:
                DrawBjtNpn(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.BjtPnp:
                DrawBjtPnp(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.Mosfet:
                DrawMosfet(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.Jfet:
                DrawJfet(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.Diode:
                DrawDiode(ds, center, componentColor, $"{el.Id}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.DcVoltageSource:
                DrawDcSource(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.DcCurrentSource:
                DrawDcCurrentSource(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.AcVoltageSource:
                DrawAcSource(ds, center, componentColor, el.Name, labelColor);
                break;

            case ElementType.AcCurrentSource:
                DrawAcCurrentSource(ds, center, componentColor, el.FormattedValueDisplay, labelColor);
                break;

            case ElementType.Vcvs:
                DrawControlledSource(ds, center, isVoltageOut: true, isVoltageControl: true, componentColor, $"{el.Name}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.Vccs:
                DrawControlledSource(ds, center, isVoltageOut: false, isVoltageControl: true, componentColor, $"{el.Name}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.Ccvs:
                DrawControlledSource(ds, center, isVoltageOut: true, isVoltageControl: false, componentColor, $"{el.Name}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.Cccs:
                DrawControlledSource(ds, center, isVoltageOut: false, isVoltageControl: false, componentColor, $"{el.Name}\n{el.FormattedValueDisplay}", labelColor);
                break;

            case ElementType.Ground:
                DrawGroundSymbol(ds, center, componentColor);
                break;

            default:
                // Fallback generic symbol
                DrawGenericBox(ds, center, el.Width, el.Height, componentColor, el.Name, labelColor);
                break;
        }

        ds.Transform = originalTransform;
    }

    private void DrawSelectionBox(CanvasDrawingSession ds, CircuitElement el, Color accentColor, Color fillColor)
    {
        var originalTransform = ds.Transform;
        var center = new Vector2(el.X, el.Y);

        if (el.RotationDeg != 0)
        {
            var rot = Matrix3x2.CreateRotation((float)(el.RotationDeg * Math.PI / 180.0), center);
            ds.Transform = rot * originalTransform;
        }

        var bounds = el.GetBounds();
        float x = (float)bounds.X;
        float y = (float)bounds.Y;
        float w = (float)bounds.Width;
        float h = (float)bounds.Height;

        // Soft highlight fill
        ds.FillRoundedRectangle(x, y, w, h, 6, 6, fillColor);

        // Crisp accent border
        ds.DrawRoundedRectangle(x, y, w, h, 6, 6, accentColor, 1.8f);

        // 4 corner handles
        float handleRadius = 3.5f;
        ds.FillCircle(x, y, handleRadius, accentColor);
        ds.FillCircle(x + w, y, handleRadius, accentColor);
        ds.FillCircle(x, y + h, handleRadius, accentColor);
        ds.FillCircle(x + w, y + h, handleRadius, accentColor);

        // Floating small label badge
        string badgeText = $"{el.Id}";
        ds.FillRoundedRectangle(x, y - 18, 40, 16, 3, 3, accentColor);
        ds.DrawText(badgeText, x + 5, y - 17, Colors.White, new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });

        ds.Transform = originalTransform;
    }

    private void DrawGrid(CanvasDrawingSession ds, Color color)
    {
        for (int x = -500; x <= 1600; x += 40)
        {
            for (int y = -300; y <= 1100; y += 40)
            {
                ds.FillCircle(x, y, 1.2f, color);
            }
        }
    }

    private void DrawResistorVertical(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        float w = 14f;
        float h = 40f;
        ds.DrawRectangle(center.X - w / 2, center.Y - h / 2, w, h, stroke, 2f);
        ds.DrawText(label, center.X + 12, center.Y - 14, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawResistorHorizontal(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        float w = 40f;
        float h = 14f;
        ds.DrawRectangle(center.X - w / 2, center.Y - h / 2, w, h, stroke, 2f);
        ds.DrawText(label, center.X - 18, center.Y - 28, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawCapacitorHorizontal(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        float gap = 8f;
        float plateHeight = 24f;
        ds.DrawLine(center.X - gap / 2, center.Y - plateHeight / 2, center.X - gap / 2, center.Y + plateHeight / 2, stroke, 2.5f);
        ds.DrawLine(center.X + gap / 2, center.Y - plateHeight / 2, center.X + gap / 2, center.Y + plateHeight / 2, stroke, 2.5f);
        ds.DrawText(label, center.X - 20, center.Y - 28, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawCapacitorVertical(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        float gap = 8f;
        float plateWidth = 24f;
        ds.DrawLine(center.X - plateWidth / 2, center.Y - gap / 2, center.X + plateWidth / 2, center.Y - gap / 2, stroke, 2.5f);
        ds.DrawLine(center.X - plateWidth / 2, center.Y + gap / 2, center.X + plateWidth / 2, center.Y + gap / 2, stroke, 2.5f);
        ds.DrawText(label, center.X + 16, center.Y - 10, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawInductor(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawLine(center.X - 30, center.Y, center.X - 20, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y, center.X + 30, center.Y, stroke, 2f);
        // 3 loops
        ds.DrawCircle(center.X - 12, center.Y - 6, 8, stroke, 2f);
        ds.DrawCircle(center.X, center.Y - 6, 8, stroke, 2f);
        ds.DrawCircle(center.X + 12, center.Y - 6, 8, stroke, 2f);
        ds.DrawText(label, center.X - 20, center.Y - 32, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawBjtNpn(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 32, stroke, 1.5f);
        ds.DrawLine(center.X - 12, center.Y - 20, center.X - 12, center.Y + 20, stroke, 3.5f);
        ds.DrawLine(center.X - 30, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 10, center.X + 20, center.Y - 30, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y + 10, center.X + 20, center.Y + 30, stroke, 2f);
        Vector2 arrowTip = new(center.X + 10, center.Y + 24);
        ds.FillCircle(arrowTip, 3.5f, stroke);
        ds.DrawText(label, center.X + 18, center.Y - 26, labelColor, new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
    }

    private void DrawBjtPnp(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 32, stroke, 1.5f);
        ds.DrawLine(center.X - 12, center.Y - 20, center.X - 12, center.Y + 20, stroke, 3.5f);
        ds.DrawLine(center.X - 30, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 10, center.X + 20, center.Y - 30, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y + 10, center.X + 20, center.Y + 30, stroke, 2f);
        Vector2 arrowTip = new(center.X - 2, center.Y + 16);
        ds.FillCircle(arrowTip, 3.5f, stroke);
        ds.DrawText(label, center.X + 18, center.Y - 26, labelColor, new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
    }

    private void DrawAcSource(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X - 10, center.Y, center.X, center.Y - 6, stroke, 1.5f);
        ds.DrawLine(center.X, center.Y - 6, center.X + 10, center.Y + 6, stroke, 1.5f);
        ds.DrawText(label, center.X - 45, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawDcSource(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X - 6, center.Y - 8, center.X + 6, center.Y - 8, stroke, 1.5f);
        ds.DrawLine(center.X, center.Y - 14, center.X, center.Y - 2, stroke, 1.5f);
        ds.DrawLine(center.X - 6, center.Y + 8, center.X + 6, center.Y + 8, stroke, 1.5f);
        ds.DrawText(label, center.X + 24, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawGroundSymbol(CanvasDrawingSession ds, Vector2 pos, Color stroke)
    {
        ds.DrawLine(pos.X - 16, pos.Y, pos.X + 16, pos.Y, stroke, 2.5f);
        ds.DrawLine(pos.X - 10, pos.Y + 5, pos.X + 10, pos.Y + 5, stroke, 2f);
        ds.DrawLine(pos.X - 4, pos.Y + 10, pos.X + 4, pos.Y + 10, stroke, 1.5f);
    }

    private void DrawDiode(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawLine(center.X - 25, center.Y, center.X - 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 25, center.Y, stroke, 2f);
        // Triangle
        ds.DrawLine(center.X - 10, center.Y - 12, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 10, center.Y + 12, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 10, center.Y - 12, center.X - 10, center.Y + 12, stroke, 2f);
        // Cathode bar
        ds.DrawLine(center.X + 10, center.Y - 12, center.X + 10, center.Y + 12, stroke, 2.5f);
        ds.DrawText(label, center.X - 18, center.Y - 30, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawGenericBox(CanvasDrawingSession ds, Vector2 center, float w, float h, Color stroke, string label, Color labelColor)
    {
        ds.DrawRoundedRectangle(center.X - w / 2, center.Y - h / 2, w, h, 4, 4, stroke, 2f);
        ds.DrawText(label, center.X - w / 2 + 8, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 11 });
    }

    private void DrawTransformer(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        // Primary and secondary leads
        ds.DrawLine(center.X - 25, center.Y - 15, center.X - 15, center.Y - 15, stroke, 2f);
        ds.DrawLine(center.X - 25, center.Y + 15, center.X - 15, center.Y + 15, stroke, 2f);
        ds.DrawLine(center.X + 15, center.Y - 15, center.X + 25, center.Y - 15, stroke, 2f);
        ds.DrawLine(center.X + 15, center.Y + 15, center.X + 25, center.Y + 15, stroke, 2f);

        // Primary coil arcs
        ds.DrawCircle(center.X - 15, center.Y - 8, 7, stroke, 2f);
        ds.DrawCircle(center.X - 15, center.Y + 8, 7, stroke, 2f);

        // Core lines
        ds.DrawLine(center.X - 3, center.Y - 20, center.X - 3, center.Y + 20, stroke, 2f);
        ds.DrawLine(center.X + 3, center.Y - 20, center.X + 3, center.Y + 20, stroke, 2f);

        // Secondary coil arcs
        ds.DrawCircle(center.X + 15, center.Y - 8, 7, stroke, 2f);
        ds.DrawCircle(center.X + 15, center.Y + 8, 7, stroke, 2f);

        // Polarity dots
        ds.FillCircle(center.X - 20, center.Y - 22, 2.5f, stroke);
        ds.FillCircle(center.X + 20, center.Y - 22, 2.5f, stroke);

        ds.DrawText(label, center.X + 28, center.Y - 10, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawDcCurrentSource(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        // Vertical arrow inside
        ds.DrawLine(center.X, center.Y + 9, center.X, center.Y - 9, stroke, 2f);
        ds.DrawLine(center.X - 5, center.Y - 3, center.X, center.Y - 9, stroke, 2f);
        ds.DrawLine(center.X + 5, center.Y - 3, center.X, center.Y - 9, stroke, 2f);
        // Leads
        ds.DrawLine(center.X, center.Y - 18, center.X, center.Y - 28, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 28, stroke, 2f);
        ds.DrawText(label, center.X + 24, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawAcCurrentSource(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        // Sine wave inside
        ds.DrawLine(center.X - 10, center.Y, center.X, center.Y - 6, stroke, 1.5f);
        ds.DrawLine(center.X, center.Y - 6, center.X + 10, center.Y + 6, stroke, 1.5f);
        // Arrow
        ds.DrawLine(center.X - 24, center.Y + 8, center.X - 24, center.Y - 8, stroke, 1.5f);
        ds.DrawLine(center.X - 27, center.Y - 3, center.X - 24, center.Y - 8, stroke, 1.5f);
        ds.DrawLine(center.X - 21, center.Y - 3, center.X - 24, center.Y - 8, stroke, 1.5f);
        // Leads
        ds.DrawLine(center.X, center.Y - 18, center.X, center.Y - 28, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 28, stroke, 2f);
        ds.DrawText(label, center.X + 24, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawMosfet(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        // Gate lead and bar
        ds.DrawLine(center.X - 28, center.Y, center.X - 14, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 14, center.Y - 18, center.X - 14, center.Y + 18, stroke, 3f);

        // 3 channel segments (Drain, Substrate, Source)
        ds.DrawLine(center.X - 6, center.Y - 18, center.X - 6, center.Y - 8, stroke, 2.5f);
        ds.DrawLine(center.X - 6, center.Y - 4, center.X - 6, center.Y + 4, stroke, 2.5f);
        ds.DrawLine(center.X - 6, center.Y + 8, center.X - 6, center.Y + 18, stroke, 2.5f);

        // Drain lead (top)
        ds.DrawLine(center.X - 6, center.Y - 14, center.X + 14, center.Y - 14, stroke, 2f);
        ds.DrawLine(center.X + 14, center.Y - 14, center.X + 14, center.Y - 28, stroke, 2f);

        // Substrate with arrow pointing in (NMOS)
        ds.DrawLine(center.X - 6, center.Y, center.X + 14, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 1, center.Y - 4, center.X - 6, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 1, center.Y + 4, center.X - 6, center.Y, stroke, 2f);

        // Source lead (bottom)
        ds.DrawLine(center.X - 6, center.Y + 14, center.X + 14, center.Y + 14, stroke, 2f);
        ds.DrawLine(center.X + 14, center.Y, center.X + 14, center.Y + 28, stroke, 2f);

        ds.DrawText(label, center.X + 20, center.Y - 10, labelColor, new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
    }

    private void DrawJfet(CanvasDrawingSession ds, Vector2 center, Color stroke, string label, Color labelColor)
    {
        // Channel bar
        ds.DrawLine(center.X - 2, center.Y - 18, center.X - 2, center.Y + 18, stroke, 3.5f);

        // Drain lead (top)
        ds.DrawLine(center.X - 2, center.Y - 14, center.X + 14, center.Y - 14, stroke, 2f);
        ds.DrawLine(center.X + 14, center.Y - 14, center.X + 14, center.Y - 28, stroke, 2f);

        // Source lead (bottom)
        ds.DrawLine(center.X - 2, center.Y + 14, center.X + 14, center.Y + 14, stroke, 2f);
        ds.DrawLine(center.X + 14, center.Y + 14, center.X + 14, center.Y + 28, stroke, 2f);

        // Gate lead with arrow pointing in (N-channel JFET)
        ds.DrawLine(center.X - 28, center.Y, center.X - 2, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 8, center.Y - 4, center.X - 2, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 8, center.Y + 4, center.X - 2, center.Y, stroke, 2f);

        ds.DrawText(label, center.X + 20, center.Y - 10, labelColor, new CanvasTextFormat { FontSize = 12, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
    }

    private void DrawControlledSource(CanvasDrawingSession ds, Vector2 center, bool isVoltageOut, bool isVoltageControl, Color stroke, string label, Color labelColor)
    {
        // Diamond vertices
        float r = 20f;
        ds.DrawLine(center.X, center.Y - r, center.X + r, center.Y, stroke, 2f);
        ds.DrawLine(center.X + r, center.Y, center.X, center.Y + r, stroke, 2f);
        ds.DrawLine(center.X, center.Y + r, center.X - r, center.Y, stroke, 2f);
        ds.DrawLine(center.X - r, center.Y, center.X, center.Y - r, stroke, 2f);

        // Output leads
        ds.DrawLine(center.X, center.Y - r, center.X, center.Y - r - 10, stroke, 2f);
        ds.DrawLine(center.X, center.Y + r, center.X, center.Y + r + 10, stroke, 2f);

        // Internal symbol
        if (isVoltageOut)
        {
            // + and - signs
            ds.DrawLine(center.X - 4, center.Y - 8, center.X + 4, center.Y - 8, stroke, 1.5f);
            ds.DrawLine(center.X, center.Y - 12, center.X, center.Y - 4, stroke, 1.5f);
            ds.DrawLine(center.X - 4, center.Y + 8, center.X + 4, center.Y + 8, stroke, 1.5f);
        }
        else
        {
            // Upward arrow
            ds.DrawLine(center.X, center.Y + 9, center.X, center.Y - 9, stroke, 2f);
            ds.DrawLine(center.X - 4, center.Y - 3, center.X, center.Y - 9, stroke, 2f);
            ds.DrawLine(center.X + 4, center.Y - 3, center.X, center.Y - 9, stroke, 2f);
        }

        ds.DrawText(label, center.X + r + 8, center.Y - 8, labelColor, new CanvasTextFormat { FontSize = 12 });
    }

    private void DrawJunctionDot(CanvasDrawingSession ds, Vector2 pos, Color color)
    {
        ds.FillCircle(pos, 4.5f, color);
    }

    // Pointer Interaction Handlers
    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);

        var pt = e.GetCurrentPoint(Canvas);
        var screenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
        var worldPos = ScreenToWorld(screenPos);

        // Check if hit testing any component
        CircuitElement? hitElement = null;
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            if (Elements[i].HitTest(worldPos))
            {
                hitElement = Elements[i];
                break;
            }
        }

        if (hitElement != null)
        {
            // Select component
            SelectedElement = hitElement;
            foreach (var el in Elements)
            {
                el.IsSelected = (el == hitElement);
            }
            ElementSelected?.Invoke(hitElement);

            // Start dragging component
            _isDraggingElement = true;
            _draggedElement = hitElement;
            _dragStartPointerPos = worldPos;
            _dragStartElementPos = new Vector2(hitElement.X, hitElement.Y);

            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            Canvas.Invalidate();
        }
        else
        {
            // Clicked empty space: deselect component and start panning canvas
            if (SelectedElement != null)
            {
                SelectedElement = null;
                foreach (var el in Elements)
                {
                    el.IsSelected = false;
                }
                ElementSelected?.Invoke(null);
            }

            _isPanningCanvas = true;
            _lastPointerPos = screenPos;
            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            Canvas.Invalidate();
        }
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);
        var currentScreenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
        var worldPos = ScreenToWorld(currentScreenPos);
        CursorCoordinatesChanged?.Invoke(worldPos.X, worldPos.Y);

        if (_isDraggingElement && _draggedElement != null)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

            // Drag element with 10px grid snap
            var delta = worldPos - _dragStartPointerPos;
            float newX = MathF.Round((_dragStartElementPos.X + delta.X) / 10f) * 10f;
            float newY = MathF.Round((_dragStartElementPos.Y + delta.Y) / 10f) * 10f;

            _draggedElement.X = newX;
            _draggedElement.Y = newY;
            ElementMoved?.Invoke(_draggedElement);
            Canvas.Invalidate();
        }
        else if (_isPanningCanvas)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

            // Pan canvas viewport (1:1 mouse tracking)
            var delta = currentScreenPos - _lastPointerPos;
            _lastPointerPos = currentScreenPos;
            _translation += delta;
            Canvas.Invalidate();
        }
        else
        {
            bool isOverElement = false;
            for (int i = Elements.Count - 1; i >= 0; i--)
            {
                if (Elements[i].HitTest(worldPos))
                {
                    isOverElement = true;
                    break;
                }
            }
            ProtectedCursor = isOverElement
                ? InputSystemCursor.Create(InputSystemCursorShape.Hand)
                : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingElement = false;
        _draggedElement = null;
        _isPanningCanvas = false;
        Canvas.ReleasePointerCapture(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        Canvas.Invalidate();
    }

    private void Canvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);
        var screenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
        int delta = pt.Properties.MouseWheelDelta;
        float factor = delta > 0 ? 1.15f : 0.85f;
        ApplyZoom(factor, screenPos);
    }

    private void ApplyZoom(float factor, Vector2? center = null)
    {
        float oldScale = _scale;
        _scale = Math.Clamp(_scale * factor, 0.4f, 3.0f);
        float actualFactor = _scale / oldScale;

        Vector2 pivot = center ?? new Vector2((float)ActualWidth / 2f, (float)ActualHeight / 2f);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            pivot = new Vector2(400f, 300f);
        }

        _translation = pivot - (pivot - _translation) * actualFactor;
        ZoomText.Text = $"{(int)(_scale * 100)}%";
        Canvas.Invalidate();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(1.15f);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(0.85f);
    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        _scale = 1.0f;
        _translation = new Vector2(100f, 40f);
        ZoomText.Text = "100%";
        Canvas.Invalidate();
    }

    // Keyboard Shortcuts
    private void CircuitCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (SelectedElement != null)
        {
            if (e.Key == VirtualKey.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.R)
            {
                RotateSelected();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                DeselectSelected();
                e.Handled = true;
            }
        }
    }

    public void RotateSelected()
    {
        if (SelectedElement != null)
        {
            SelectedElement.RotationDeg = (SelectedElement.RotationDeg + 90) % 360;
            SelectedElement.Orientation = (SelectedElement.Orientation == "Vertical") ? "Horizontal" : "Vertical";
            Canvas.Invalidate();
        }
    }

    public void DeleteSelected()
    {
        if (SelectedElement != null)
        {
            var el = SelectedElement;
            Elements.Remove(el);
            SelectedElement = null;
            ElementSelected?.Invoke(null);
            Canvas.Invalidate();
        }
    }

    public void DeselectSelected()
    {
        SelectedElement = null;
        foreach (var el in Elements)
        {
            el.IsSelected = false;
        }
        ElementSelected?.Invoke(null);
        Canvas.Invalidate();
    }

    private void RotateSelected_Click(object sender, RoutedEventArgs e) => RotateSelected();
    private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteSelected();
    private void Deselect_Click(object sender, RoutedEventArgs e) => DeselectSelected();

    // Drag & Drop from Symbol Library
    private void Canvas_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "放置元件至画布";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void Canvas_Drop(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Text))
        {
            var text = await e.DataView.GetTextAsync();
            var screenPos = e.GetPosition(Canvas);
            var worldPos = ScreenToWorld(new Vector2((float)screenPos.X, (float)screenPos.Y));

            ElementDroppedFromLibrary?.Invoke(text, worldPos);
        }
    }
}
