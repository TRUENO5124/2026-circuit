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

    // Element dragging state
    private bool _isDraggingElement;
    private CircuitElement? _draggedElement;
    private Vector2 _dragStartPointerPos;
    private Vector2 _dragStartElementPos;

    // Wire dragging / corner adjustment state
    private bool _isDraggingWire;
    private CircuitWire? _draggedWire;
    private int _draggedCornerIndex = -1;
    private int _draggedSegmentIndex = -1;
    private bool _draggedSegmentIsHorizontal;

    // Wire connection state
    private bool _isDrawingWire;
    private CircuitElement? _wireStartElement;
    private CircuitPin? _wireStartPin;
    private CircuitWire? _wireStartParentWire;
    private Vector2 _wireStartPos;
    private Vector2 _currentWireEnd;
    private (CircuitElement Element, CircuitPin Pin)? _hoveredPin;
    private (CircuitWire Wire, Vector2 SnapPos)? _hoveredWireTap;

    public event Action<float, float>? CursorCoordinatesChanged;
    public event Action<CircuitElement?>? ElementSelected;
    public event Action<CircuitElement>? ElementMoved;
    public event Action<string, Vector2>? ElementDroppedFromLibrary;
    public event Action<CircuitWire>? WireCreated;
    public event Action<string>? StatusMessageChanged;

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

    // Selected Element Dependency Property
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

    // Wires Collection Dependency Property
    public static readonly DependencyProperty WiresProperty =
        DependencyProperty.Register(
            nameof(Wires),
            typeof(ObservableCollection<CircuitWire>),
            typeof(CircuitCanvas),
            new PropertyMetadata(null, OnWiresPropertyChanged));

    public ObservableCollection<CircuitWire> Wires
    {
        get => (ObservableCollection<CircuitWire>)GetValue(WiresProperty);
        set => SetValue(WiresProperty, value);
    }

    // Selected Wire Dependency Property
    public static readonly DependencyProperty SelectedWireProperty =
        DependencyProperty.Register(
            nameof(SelectedWire),
            typeof(CircuitWire),
            typeof(CircuitCanvas),
            new PropertyMetadata(null, OnSelectedWirePropertyChanged));

    public CircuitWire? SelectedWire
    {
        get => (CircuitWire?)GetValue(SelectedWireProperty);
        set => SetValue(SelectedWireProperty, value);
    }

    // IsWireMode Dependency Property
    public static readonly DependencyProperty IsWireModeProperty =
        DependencyProperty.Register(
            nameof(IsWireMode),
            typeof(bool),
            typeof(CircuitCanvas),
            new PropertyMetadata(false, OnIsWireModePropertyChanged));

    public bool IsWireMode
    {
        get => (bool)GetValue(IsWireModeProperty);
        set => SetValue(IsWireModeProperty, value);
    }

    // Compatibility properties for teaching labels
    public static readonly DependencyProperty VccTextProperty = DependencyProperty.Register(nameof(VccText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("+12 V", OnRedrawNeeded));
    public static readonly DependencyProperty Rb1TextProperty = DependencyProperty.Register(nameof(Rb1Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("51 kΩ", OnRedrawNeeded));
    public static readonly DependencyProperty Rb2TextProperty = DependencyProperty.Register(nameof(Rb2Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("15 kΩ", OnRedrawNeeded));
    public static readonly DependencyProperty RcTextProperty = DependencyProperty.Register(nameof(RcText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("3 kΩ", OnRedrawNeeded));
    public static readonly DependencyProperty ReTextProperty = DependencyProperty.Register(nameof(ReText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("1 kΩ", OnRedrawNeeded));
    public static readonly DependencyProperty RlTextProperty = DependencyProperty.Register(nameof(RlText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 kΩ", OnRedrawNeeded));
    public static readonly DependencyProperty C1TextProperty = DependencyProperty.Register(nameof(C1Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 μF", OnRedrawNeeded));
    public static readonly DependencyProperty C2TextProperty = DependencyProperty.Register(nameof(C2Text), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("10 μF", OnRedrawNeeded));
    public static readonly DependencyProperty CeTextProperty = DependencyProperty.Register(nameof(CeText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("47 μF", OnRedrawNeeded));
    public static readonly DependencyProperty BetaTextProperty = DependencyProperty.Register(nameof(BetaText), typeof(string), typeof(CircuitCanvas), new PropertyMetadata("β = 100", OnRedrawNeeded));

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
        Elements = [];
        Wires = [];
        KeyDown += CircuitCanvas_KeyDown;
    }

    public void InvalidateCanvas()
    {
        Canvas?.Invalidate();
    }

    private static void OnElementsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            if (e.OldValue is ObservableCollection<CircuitElement> oldCol)
            {
                oldCol.CollectionChanged -= canvas.OnElementsCollectionChanged;
                foreach (var item in oldCol) item.PropertyChanged -= canvas.OnElementPropertyChanged;
            }
            if (e.NewValue is ObservableCollection<CircuitElement> newCol)
            {
                newCol.CollectionChanged += canvas.OnElementsCollectionChanged;
                foreach (var item in newCol)
                {
                    item.PropertyChanged += canvas.OnElementPropertyChanged;
                    if (item.Pins.Count == 0) item.InitializeDefaultPins();
                }
            }
            canvas.Canvas?.Invalidate();
        }
    }

    private void OnElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (CircuitElement el in e.NewItems)
            {
                el.PropertyChanged += OnElementPropertyChanged;
                if (el.Pins.Count == 0) el.InitializeDefaultPins();
            }
        }
        if (e.OldItems != null)
        {
            foreach (CircuitElement el in e.OldItems)
            {
                el.PropertyChanged -= OnElementPropertyChanged;
            }
        }
        Canvas?.Invalidate();
    }

    private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Canvas?.Invalidate();
        if (sender is CircuitElement el && el.IsSelected)
        {
            UpdateSelectionBar(el);
        }
    }

    private static void OnWiresPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            if (e.OldValue is ObservableCollection<CircuitWire> oldCol)
            {
                oldCol.CollectionChanged -= canvas.OnWiresCollectionChanged;
            }
            if (e.NewValue is ObservableCollection<CircuitWire> newCol)
            {
                newCol.CollectionChanged += canvas.OnWiresCollectionChanged;
            }
            canvas.Canvas?.Invalidate();
        }
    }

    private void OnWiresCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Canvas?.Invalidate();
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
            if (selected != null)
            {
                canvas.SelectedWire = null;
            }
            canvas.UpdateSelectionBar(selected);
            canvas.Canvas?.Invalidate();
        }
    }

    private static void OnSelectedWirePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas)
        {
            var selected = e.NewValue as CircuitWire;
            foreach (var w in canvas.Wires)
            {
                w.IsSelected = (w == selected);
            }
            if (selected != null)
            {
                canvas.SelectedElement = null;
            }
            canvas.UpdateSelectionBar(null);
            canvas.Canvas?.Invalidate();
        }
    }

    private static void OnIsWireModePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CircuitCanvas canvas && e.NewValue is bool isWire)
        {
            if (canvas.WireModeButton != null && canvas.WireModeButton.IsChecked != isWire)
            {
                canvas.WireModeButton.IsChecked = isWire;
            }
            canvas.StatusMessageChanged?.Invoke(isWire ? "连线模式已激活 · 单击元件引脚端子拉出导线，点击另一引脚完成连接" : "已退出连线模式");
            canvas.Canvas?.Invalidate();
        }
    }

    private void UpdateSelectionBar(CircuitElement? el)
    {
        if (el != null)
        {
            SelectionActionBar.Visibility = Visibility.Visible;
            SelectionBarText.Text = $"{el.Id} ({el.FormattedValueDisplay})";
            if (RotateButton != null) RotateButton.Visibility = Visibility.Visible;
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
            canvas.Canvas?.Invalidate();
        }
    }

    private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args)
    {
    }

    public Vector2 ScreenToWorld(Vector2 screenPos) => (screenPos - _translation) / _scale;
    public Vector2 WorldToScreen(Vector2 worldPos) => worldPos * _scale + _translation;

    public Vector2 GetViewportCenterInWorld()
    {
        var screenCenter = new Vector2((float)ActualWidth / 2f, (float)ActualHeight / 2f);
        if (ActualWidth <= 0 || ActualHeight <= 0) screenCenter = new Vector2(400f, 300f);
        return ScreenToWorld(screenCenter);
    }

    private CircuitElement? FindElement(string id)
    {
        return Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================================
    // Core Rendering (Direct2D Win2D)
    // =========================================================================
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

            // 1. Grid Points
            DrawGrid(ds, gridColor);

            // 2. Draw user wires in Wires collection
            // Collect all pin world positions for continuous Euclidean keep-out (> 5.0px)
            var allPinWorldPositions = new List<Vector2>();
            foreach (var el in Elements)
            {
                foreach (var pin in el.Pins)
                {
                    allPinWorldPositions.Add(el.GetPinWorldPosition(pin));
                }
            }

            bool IsOutsidePinKeepOut(Vector2 pt)
            {
                for (int i = 0; i < allPinWorldPositions.Count; i++)
                {
                    if (Vector2.Distance(pt, allPinWorldPositions[i]) <= 5.0f)
                    {
                        return false;
                    }
                }
                return true;
            }

            var rawJunctionPoints = new List<Vector2>();
            var allSegments = new List<(Vector2 A, Vector2 B, CircuitWire Wire)>();

            foreach (var wire in Wires)
            {
                var pts = wire.GetPathPoints();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    allSegments.Add((pts[i], pts[i + 1], wire));
                }
            }

            // A) Detect true T-junctions: endpoints landing strictly on another wire's segment
            foreach (var wire in Wires)
            {
                var endpoints = new[] { new Vector2(wire.StartX, wire.StartY), new Vector2(wire.EndX, wire.EndY) };
                foreach (var ep in endpoints)
                {
                    if (!IsOutsidePinKeepOut(ep)) continue;

                    foreach (var (sa, sb, sw) in allSegments)
                    {
                        if (sw == wire) continue;
                        if (IsPointOnSegmentStrict(ep, sa, sb, 3.5f))
                        {
                            rawJunctionPoints.Add(ep);
                            break;
                        }
                    }
                }

                // Explicit WireTap endpoints
                if (wire.StartPinName == "WireTap")
                {
                    var p = new Vector2(wire.StartX, wire.StartY);
                    if (IsOutsidePinKeepOut(p)) rawJunctionPoints.Add(p);
                }
                if (wire.EndPinName == "WireTap")
                {
                    var p = new Vector2(wire.EndX, wire.EndY);
                    if (IsOutsidePinKeepOut(p)) rawJunctionPoints.Add(p);
                }
            }

            // B) 3/4-way intersections: 3 or more wire endpoints meeting within 3px
            var allWireEndpoints = new List<Vector2>();
            foreach (var wire in Wires)
            {
                allWireEndpoints.Add(new Vector2(wire.StartX, wire.StartY));
                allWireEndpoints.Add(new Vector2(wire.EndX, wire.EndY));
            }

            for (int i = 0; i < allWireEndpoints.Count; i++)
            {
                var ep1 = allWireEndpoints[i];
                if (!IsOutsidePinKeepOut(ep1)) continue;

                int meetingCount = 0;
                for (int j = 0; j < allWireEndpoints.Count; j++)
                {
                    if (Vector2.Distance(ep1, allWireEndpoints[j]) <= 3.0f)
                    {
                        meetingCount++;
                    }
                }
                if (meetingCount >= 3)
                {
                    rawJunctionPoints.Add(ep1);
                }
            }

            // C) Deduplicate/cluster raw junction points within 2.5px
            var uniqueSolderDots = new List<Vector2>();
            foreach (var pt in rawJunctionPoints)
            {
                bool exists = false;
                for (int k = 0; k < uniqueSolderDots.Count; k++)
                {
                    if (Vector2.Distance(pt, uniqueSolderDots[k]) <= 2.5f)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    uniqueSolderDots.Add(pt);
                }
            }

            // Draw wire segments
            foreach (var wire in Wires)
            {
                var pts = wire.GetPathPoints();
                float strokeWidth = wire.IsSelected ? 3.0f : 2.0f;
                Color wColor = wire.IsSelected ? accentColor : wireColor;

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    ds.DrawLine(pts[i], pts[i + 1], wColor, strokeWidth);
                }

                // If selected, draw interactive editing handles at the bend corner(s)
                if (wire.IsSelected)
                {
                    for (int i = 1; i < pts.Count - 1; i++)
                    {
                        var pt = pts[i];
                        ds.FillCircle(pt, 5.5f, accentColor);
                        ds.DrawCircle(pt, 5.5f, isDark ? Colors.White : Colors.Black, 1.2f);
                    }
                }
            }

            // Draw true EDA junction solder dots (crisp 3.8px solid dots: radius 1.9px, theme adaptive #1E1E1E / #E6E6E6)
            var solderDotColor = isDark ? Color.FromArgb(255, 0xE6, 0xE6, 0xE6) : Color.FromArgb(255, 0x1E, 0x1E, 0x1E);
            foreach (var dot in uniqueSolderDots)
            {
                ds.FillCircle(dot, 1.9f, solderDotColor);
            }

            // 4. In-progress rubber-band wire & hover snap markers
            if (_isDrawingWire)
            {
                var start = _wireStartPos;
                var end = _currentWireEnd;
                float strokeWidth = 2.0f;
                var rubberColor = accentColor;

                // Orthogonal routing for rubber-band preview
                if (Math.Abs(start.X - end.X) > 1.0f && Math.Abs(start.Y - end.Y) > 1.0f)
                {
                    bool flip = _wireStartElement != null && _wireStartPin != null
                        ? Math.Abs(_wireStartElement.GetPinWorldDirection(_wireStartPin).Y) > Math.Abs(_wireStartElement.GetPinWorldDirection(_wireStartPin).X)
                        : false;
                    var corner = flip ? new Vector2(start.X, end.Y) : new Vector2(end.X, start.Y);
                    ds.DrawLine(start, corner, rubberColor, strokeWidth, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash });
                    ds.DrawLine(corner, end, rubberColor, strokeWidth, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash });
                }
                else
                {
                    ds.DrawLine(start, end, rubberColor, strokeWidth, new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash });
                }

                ds.FillCircle(start, 4f, rubberColor);

                if (_hoveredWireTap.HasValue)
                {
                    ds.FillCircle(_hoveredWireTap.Value.SnapPos, 1.9f, solderDotColor);
                    ds.DrawCircle(_hoveredWireTap.Value.SnapPos, 4.0f, rubberColor, 1.5f);
                }
                else
                {
                    ds.FillCircle(end, 4f, rubberColor);
                }
            }
            else if (_hoveredWireTap.HasValue)
            {
                // 8px hover snap marker (radius 4.0px circle + 1.9px solder dot)
                ds.DrawCircle(_hoveredWireTap.Value.SnapPos, 4.0f, accentColor, 1.5f);
                ds.FillCircle(_hoveredWireTap.Value.SnapPos, 1.9f, solderDotColor);
            }

            // 5. Render all CircuitElements
            foreach (var el in Elements)
            {
                if (el.Pins.Count == 0) el.InitializeDefaultPins();
                DrawElement(ds, el, componentColor, labelColor, wireColor, dotColor);

                if (el.IsSelected)
                {
                    DrawSelectionBox(ds, el, accentColor, selectionFillColor);
                }
            }

            // 6. Draw Hovered Pin Highlight
            if (_hoveredPin.HasValue)
            {
                var hPos = _hoveredPin.Value.Element.GetPinWorldPosition(_hoveredPin.Value.Pin);
                ds.DrawCircle(hPos, 7.5f, accentColor, 2.2f);
                ds.FillCircle(hPos, 3.5f, accentColor);

                // Small badge with pin name
                ds.DrawText(_hoveredPin.Value.Pin.Name, hPos.X + 8, hPos.Y - 14, accentColor,
                    new CanvasTextFormat { FontSize = 10, FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 } });
            }
        }
        catch (Exception ex)
        {
            App.LogTrace($"[Canvas_Draw Exception] {ex.Message}");
        }
    }


    private void DrawElement(CanvasDrawingSession ds, CircuitElement el, Color componentColor, Color labelColor, Color wireColor, Color dotColor)
    {
        var center = new Vector2(el.X, el.Y);
        var originalTransform = ds.Transform;

        // Rotate coordinate system around component center
        if (el.RotationDeg != 0)
        {
            var rot = Matrix3x2.CreateRotation((float)(el.RotationDeg * Math.PI / 180.0), center);
            ds.Transform = rot * originalTransform;
        }

        // Draw canonical component symbol with leads and pin nodes in local space
        switch (el.Type)
        {
            case ElementType.Resistor:
                DrawResistor(ds, center, componentColor, dotColor);
                break;

            case ElementType.Capacitor:
                DrawCapacitor(ds, center, componentColor, dotColor);
                break;

            case ElementType.Inductor:
                DrawInductor(ds, center, componentColor, dotColor);
                break;

            case ElementType.Transformer:
                DrawTransformer(ds, center, componentColor, dotColor);
                break;

            case ElementType.BjtNpn:
                DrawBjtNpn(ds, center, componentColor, dotColor);
                break;

            case ElementType.BjtPnp:
                DrawBjtPnp(ds, center, componentColor, dotColor);
                break;

            case ElementType.MosfetNEnh:
                DrawMosfetNEnh(ds, center, componentColor, dotColor);
                break;

            case ElementType.MosfetPEnh:
                DrawMosfetPEnh(ds, center, componentColor, dotColor);
                break;

            case ElementType.MosfetNDep:
                DrawMosfetNDep(ds, center, componentColor, dotColor);
                break;

            case ElementType.MosfetPDep:
                DrawMosfetPDep(ds, center, componentColor, dotColor);
                break;

            case ElementType.JfetN:
                DrawJfetN(ds, center, componentColor, dotColor);
                break;

            case ElementType.JfetP:
                DrawJfetP(ds, center, componentColor, dotColor);
                break;

            case ElementType.Mosfet:
            case ElementType.Jfet:
                DrawMosfetNEnh(ds, center, componentColor, dotColor);
                break;

            case ElementType.Diode:
                DrawDiode(ds, center, componentColor, dotColor);
                break;

            case ElementType.DcVoltageSource:
                DrawDcSource(ds, center, componentColor, dotColor);
                break;

            case ElementType.DcCurrentSource:
                DrawDcCurrentSource(ds, center, componentColor, dotColor);
                break;

            case ElementType.AcVoltageSource:
                DrawAcSource(ds, center, componentColor, dotColor);
                break;

            case ElementType.AcCurrentSource:
                DrawAcCurrentSource(ds, center, componentColor, dotColor);
                break;

            case ElementType.Vcvs:
            case ElementType.Vccs:
            case ElementType.Ccvs:
            case ElementType.Cccs:
                DrawControlledSource(ds, center, isVoltageOut: el.Type is ElementType.Vcvs or ElementType.Ccvs, componentColor, dotColor);
                break;

            case ElementType.Ground:
                DrawGroundSymbol(ds, center, componentColor, dotColor);
                break;

            default:
                DrawGenericBox(ds, center, el.Width, el.Height, componentColor, dotColor);
                break;
        }

        // Restore original unrotated world transform for text rendering
        ds.Transform = originalTransform;

        // Draw beautifully positioned, non-overlapping component labels
        DrawElementLabels(ds, el, center, labelColor, ActualTheme == ElementTheme.Dark);
    }

    private void DrawElementLabels(CanvasDrawingSession ds, CircuitElement el, Vector2 center, Color labelColor, bool isDark)
    {
        string id = el.Id;
        string val = el.FormattedValueDisplay;

        var idFormat = new CanvasTextFormat
        {
            FontSize = 11,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            FontFamily = "Segoe UI Variable Text, Segoe UI, sans-serif"
        };

        var valFormat = new CanvasTextFormat
        {
            FontSize = 9.5f,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 400 },
            FontFamily = "Segoe UI Variable Text, Segoe UI, sans-serif"
        };

        Color idColor = el.IsSelected
            ? (isDark ? Color.FromArgb(255, 76, 194, 255) : Color.FromArgb(255, 0, 120, 215))
            : labelColor;
        Color valColor = isDark ? Color.FromArgb(200, 160, 160, 160) : Color.FromArgb(200, 100, 100, 100);

        bool isVertical = el.RotationDeg % 180 != 0;

        switch (el.Type)
        {
            case ElementType.Resistor:
            case ElementType.Inductor:
            case ElementType.Diode:
                if (!isVertical)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    ds.DrawText(id, center.X, center.Y - 28, idColor, idFormat);
                    ds.DrawText(val, center.X, center.Y + 16, valColor, valFormat);
                }
                else
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    ds.DrawText(id, center.X + 18, center.Y - 14, idColor, idFormat);
                    ds.DrawText(val, center.X + 18, center.Y + 2, valColor, valFormat);
                }
                break;

            case ElementType.Capacitor:
                if (!isVertical)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    ds.DrawText(id, center.X, center.Y - 34, idColor, idFormat);
                    ds.DrawText(val, center.X, center.Y + 22, valColor, valFormat);
                }
                else
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    ds.DrawText(id, center.X + 24, center.Y - 14, idColor, idFormat);
                    ds.DrawText(val, center.X + 24, center.Y + 2, valColor, valFormat);
                }
                break;

            case ElementType.DcVoltageSource:
            case ElementType.AcVoltageSource:
            case ElementType.DcCurrentSource:
            case ElementType.AcCurrentSource:
            case ElementType.Vcvs:
            case ElementType.Vccs:
            case ElementType.Ccvs:
            case ElementType.Cccs:
                if (!isVertical)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    ds.DrawText(id, center.X + 26, center.Y - 14, idColor, idFormat);
                    ds.DrawText(val, center.X + 26, center.Y + 2, valColor, valFormat);
                }
                else
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    ds.DrawText(id, center.X, center.Y - 34, idColor, idFormat);
                    ds.DrawText(val, center.X, center.Y + 22, valColor, valFormat);
                }
                break;

            case ElementType.Ground:
                idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                ds.DrawText(id, center.X + 22, center.Y - 8, idColor, idFormat);
                if (!string.IsNullOrWhiteSpace(val) && val != "0 V")
                {
                    ds.DrawText(val, center.X + 22, center.Y + 6, valColor, valFormat);
                }
                break;

            case ElementType.BjtNpn:
            case ElementType.BjtPnp:
            case ElementType.MosfetNEnh:
            case ElementType.MosfetPEnh:
            case ElementType.MosfetNDep:
            case ElementType.MosfetPDep:
            case ElementType.JfetN:
            case ElementType.JfetP:
            case ElementType.Mosfet:
            case ElementType.Jfet:
                idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                ds.DrawText(id, center.X - 18, center.Y - 36, idColor, idFormat);
                ds.DrawText(val, center.X - 18, center.Y + 24, valColor, valFormat);
                break;

            default:
                idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                ds.DrawText(id, center.X, center.Y - (el.Height / 2f + 18), idColor, idFormat);
                ds.DrawText(val, center.X, center.Y + (el.Height / 2f + 6), valColor, valFormat);
                break;
        }
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

        ds.FillRoundedRectangle(x, y, w, h, 6, 6, fillColor);
        ds.DrawRoundedRectangle(x, y, w, h, 6, 6, accentColor, 1.8f);

        // 4 corner handles
        float handleRadius = 3.5f;
        ds.FillCircle(x, y, handleRadius, accentColor);
        ds.FillCircle(x + w, y, handleRadius, accentColor);
        ds.FillCircle(x, y + h, handleRadius, accentColor);
        ds.FillCircle(x + w, y + h, handleRadius, accentColor);

        ds.Transform = originalTransform;
    }

    private void DrawPinNode(CanvasDrawingSession ds, Vector2 pos, Color dotColor)
    {
        ds.DrawCircle(pos, 3.8f, dotColor, 1.5f);
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

    // Canonical Component Symbol Drawers (0° Standard Orientation)
    private void DrawResistor(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        // Leads
        ds.DrawLine(center.X - 40, center.Y, center.X - 20, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y, center.X + 40, center.Y, stroke, 2f);
        // Resistor body rectangle
        ds.DrawRectangle(center.X - 20, center.Y - 10, 40, 20, stroke, 2f);
        // Connection pin nodes
        DrawPinNode(ds, new Vector2(center.X - 40, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 40, center.Y), dotColor);
    }

    private void DrawCapacitor(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        // Leads
        ds.DrawLine(center.X - 35, center.Y, center.X - 6, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 6, center.Y, center.X + 35, center.Y, stroke, 2f);
        // Plates
        ds.DrawLine(center.X - 6, center.Y - 18, center.X - 6, center.Y + 18, stroke, 2.5f);
        ds.DrawLine(center.X + 6, center.Y - 18, center.X + 6, center.Y + 18, stroke, 2.5f);
        // Pins
        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y), dotColor);
    }

    private void DrawInductor(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        // Leads
        ds.DrawLine(center.X - 35, center.Y, center.X - 22, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 22, center.Y, center.X + 35, center.Y, stroke, 2f);
        // 3 loops
        ds.DrawCircle(center.X - 14, center.Y - 5, 8, stroke, 2f);
        ds.DrawCircle(center.X, center.Y - 5, 8, stroke, 2f);
        ds.DrawCircle(center.X + 14, center.Y - 5, 8, stroke, 2f);
        // Pins
        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y), dotColor);
    }

    private void DrawBjtNpn(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Base bar
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3.5f);
        // Base lead
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        // Collector lead
        ds.DrawLine(center.X - 12, center.Y - 9, center.X + 20, center.Y - 24, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 24, center.X + 20, center.Y - 35, stroke, 2f);
        // Emitter lead with arrow
        ds.DrawLine(center.X - 12, center.Y + 9, center.X + 20, center.Y + 24, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 24, center.X + 20, center.Y + 35, stroke, 2f);
        ds.FillCircle(new Vector2(center.X + 11, center.Y + 20), 3.5f, stroke);

        // Terminal pins
        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Base
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Collector
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Emitter
    }

    private void DrawBjtPnp(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3.5f);
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 9, center.X + 20, center.Y - 24, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 24, center.X + 20, center.Y - 35, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y + 9, center.X + 20, center.Y + 24, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 24, center.X + 20, center.Y + 35, stroke, 2f);
        ds.FillCircle(new Vector2(center.X - 2, center.Y + 14), 3.5f, stroke);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor);
    }

    private void DrawMosfetNEnh(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Gate lead & plate
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3f);

        // Segmented channel (3 broken segments)
        ds.DrawLine(center.X - 4, center.Y - 18, center.X - 4, center.Y - 8, stroke, 3.2f);
        ds.DrawLine(center.X - 4, center.Y - 5, center.X - 4, center.Y + 5, stroke, 3.2f);
        ds.DrawLine(center.X - 4, center.Y + 8, center.X - 4, center.Y + 18, stroke, 3.2f);

        // Drain lead
        ds.DrawLine(center.X - 4, center.Y - 13, center.X + 20, center.Y - 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 13, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X - 4, center.Y + 13, center.X + 20, center.Y + 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 13, center.X + 20, center.Y + 35, stroke, 2f);

        // Substrate connection
        ds.DrawLine(center.X - 4, center.Y, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 10, center.Y + 13, stroke, 2f);

        // N-channel substrate arrow pointing IN towards channel (leftwards)
        ds.DrawLine(center.X + 6, center.Y - 5, center.X - 2, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X + 6, center.Y + 5, center.X - 2, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawMosfetPEnh(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Gate lead & plate
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3f);

        // Segmented channel (3 broken segments)
        ds.DrawLine(center.X - 4, center.Y - 18, center.X - 4, center.Y - 8, stroke, 3.2f);
        ds.DrawLine(center.X - 4, center.Y - 5, center.X - 4, center.Y + 5, stroke, 3.2f);
        ds.DrawLine(center.X - 4, center.Y + 8, center.X - 4, center.Y + 18, stroke, 3.2f);

        // Drain lead
        ds.DrawLine(center.X - 4, center.Y - 13, center.X + 20, center.Y - 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 13, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X - 4, center.Y + 13, center.X + 20, center.Y + 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 13, center.X + 20, center.Y + 35, stroke, 2f);

        // Substrate connection
        ds.DrawLine(center.X - 4, center.Y, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 10, center.Y + 13, stroke, 2f);

        // P-channel substrate arrow pointing OUT away from channel (rightwards)
        ds.DrawLine(center.X - 2, center.Y - 5, center.X + 6, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X - 2, center.Y + 5, center.X + 6, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawMosfetNDep(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Gate lead & plate
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3f);

        // Solid continuous channel
        ds.DrawLine(center.X - 4, center.Y - 18, center.X - 4, center.Y + 18, stroke, 3.8f);

        // Drain lead
        ds.DrawLine(center.X - 4, center.Y - 13, center.X + 20, center.Y - 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 13, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X - 4, center.Y + 13, center.X + 20, center.Y + 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 13, center.X + 20, center.Y + 35, stroke, 2f);

        // Substrate connection
        ds.DrawLine(center.X - 4, center.Y, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 10, center.Y + 13, stroke, 2f);

        // N-channel substrate arrow pointing IN towards channel (leftwards)
        ds.DrawLine(center.X + 6, center.Y - 5, center.X - 2, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X + 6, center.Y + 5, center.X - 2, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawMosfetPDep(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Gate lead & plate
        ds.DrawLine(center.X - 35, center.Y, center.X - 12, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 12, center.Y - 18, center.X - 12, center.Y + 18, stroke, 3f);

        // Solid continuous channel
        ds.DrawLine(center.X - 4, center.Y - 18, center.X - 4, center.Y + 18, stroke, 3.8f);

        // Drain lead
        ds.DrawLine(center.X - 4, center.Y - 13, center.X + 20, center.Y - 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 13, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X - 4, center.Y + 13, center.X + 20, center.Y + 13, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 13, center.X + 20, center.Y + 35, stroke, 2f);

        // Substrate connection
        ds.DrawLine(center.X - 4, center.Y, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 10, center.Y + 13, stroke, 2f);

        // P-channel substrate arrow pointing OUT away from channel (rightwards)
        ds.DrawLine(center.X - 2, center.Y - 5, center.X + 6, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X - 2, center.Y + 5, center.X + 6, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawJfetN(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Vertical Channel
        ds.DrawLine(center.X + 8, center.Y - 18, center.X + 8, center.Y + 18, stroke, 3.5f);

        // Drain lead
        ds.DrawLine(center.X + 8, center.Y - 18, center.X + 20, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 18, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X + 8, center.Y + 18, center.X + 20, center.Y + 18, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 18, center.X + 20, center.Y + 35, stroke, 2f);

        // Gate lead
        ds.DrawLine(center.X - 35, center.Y, center.X + 8, center.Y, stroke, 2f);

        // N-channel arrow on Gate lead pointing IN (rightwards toward channel)
        ds.DrawLine(center.X - 1, center.Y - 5, center.X + 7, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X - 1, center.Y + 5, center.X + 7, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawJfetP(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 30, stroke, 1.5f);
        // Vertical Channel
        ds.DrawLine(center.X + 8, center.Y - 18, center.X + 8, center.Y + 18, stroke, 3.5f);

        // Drain lead
        ds.DrawLine(center.X + 8, center.Y - 18, center.X + 20, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y - 18, center.X + 20, center.Y - 35, stroke, 2f);

        // Source lead
        ds.DrawLine(center.X + 8, center.Y + 18, center.X + 20, center.Y + 18, stroke, 2f);
        ds.DrawLine(center.X + 20, center.Y + 18, center.X + 20, center.Y + 35, stroke, 2f);

        // Gate lead
        ds.DrawLine(center.X - 35, center.Y, center.X + 8, center.Y, stroke, 2f);

        // P-channel arrow on Gate lead pointing OUT (leftwards away from channel)
        ds.DrawLine(center.X + 3, center.Y - 5, center.X - 5, center.Y, stroke, 2.2f);
        ds.DrawLine(center.X + 3, center.Y + 5, center.X - 5, center.Y, stroke, 2.2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor); // Gate
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y - 35), dotColor); // Drain
        DrawPinNode(ds, new Vector2(center.X + 20, center.Y + 35), dotColor); // Source
    }

    private void DrawDcSource(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y - 35, center.X, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 35, stroke, 2f);
        // + and - signs
        ds.DrawLine(center.X - 5, center.Y - 8, center.X + 5, center.Y - 8, stroke, 1.5f);
        ds.DrawLine(center.X, center.Y - 13, center.X, center.Y - 3, stroke, 1.5f);
        ds.DrawLine(center.X - 5, center.Y + 8, center.X + 5, center.Y + 8, stroke, 1.5f);

        DrawPinNode(ds, new Vector2(center.X, center.Y - 35), dotColor);
        DrawPinNode(ds, new Vector2(center.X, center.Y + 35), dotColor);
    }

    private void DrawDcCurrentSource(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y - 35, center.X, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 35, stroke, 2f);
        // Arrow
        ds.DrawLine(center.X, center.Y + 8, center.X, center.Y - 8, stroke, 2f);
        ds.DrawLine(center.X - 4, center.Y - 3, center.X, center.Y - 8, stroke, 2f);
        ds.DrawLine(center.X + 4, center.Y - 3, center.X, center.Y - 8, stroke, 2f);

        DrawPinNode(ds, new Vector2(center.X, center.Y - 35), dotColor);
        DrawPinNode(ds, new Vector2(center.X, center.Y + 35), dotColor);
    }

    private void DrawAcSource(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y - 35, center.X, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 35, stroke, 2f);
        // Sine curve
        ds.DrawLine(center.X - 10, center.Y, center.X, center.Y - 6, stroke, 1.5f);
        ds.DrawLine(center.X, center.Y - 6, center.X + 10, center.Y + 6, stroke, 1.5f);

        DrawPinNode(ds, new Vector2(center.X, center.Y - 35), dotColor);
        DrawPinNode(ds, new Vector2(center.X, center.Y + 35), dotColor);
    }

    private void DrawAcCurrentSource(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawCircle(center, 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y - 35, center.X, center.Y - 18, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 18, center.X, center.Y + 35, stroke, 2f);
        ds.DrawLine(center.X, center.Y + 8, center.X, center.Y - 8, stroke, 2f);

        DrawPinNode(ds, new Vector2(center.X, center.Y - 35), dotColor);
        DrawPinNode(ds, new Vector2(center.X, center.Y + 35), dotColor);
    }

    private void DrawGroundSymbol(CanvasDrawingSession ds, Vector2 pos, Color stroke, Color dotColor)
    {
        // Top lead and pin node
        ds.DrawLine(pos.X, pos.Y - 22, pos.X, pos.Y, stroke, 2f);
        DrawPinNode(ds, new Vector2(pos.X, pos.Y - 22), dotColor);

        // Ground bars
        ds.DrawLine(pos.X - 16, pos.Y, pos.X + 16, pos.Y, stroke, 2.5f);
        ds.DrawLine(pos.X - 10, pos.Y + 5, pos.X + 10, pos.Y + 5, stroke, 2f);
        ds.DrawLine(pos.X - 4, pos.Y + 10, pos.X + 4, pos.Y + 10, stroke, 1.5f);
    }

    private void DrawDiode(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawLine(center.X - 35, center.Y, center.X - 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X + 10, center.Y, center.X + 35, center.Y, stroke, 2f);
        // Triangle
        ds.DrawLine(center.X - 10, center.Y - 12, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 10, center.Y + 12, center.X + 10, center.Y, stroke, 2f);
        ds.DrawLine(center.X - 10, center.Y - 12, center.X - 10, center.Y + 12, stroke, 2f);
        // Cathode bar
        ds.DrawLine(center.X + 10, center.Y - 12, center.X + 10, center.Y + 12, stroke, 2.5f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y), dotColor);
    }

    private void DrawControlledSource(CanvasDrawingSession ds, Vector2 center, bool isVoltageOut, Color stroke, Color dotColor)
    {
        float r = 20f;
        // Diamond shape
        ds.DrawLine(center.X, center.Y - r, center.X + r, center.Y, stroke, 2f);
        ds.DrawLine(center.X + r, center.Y, center.X, center.Y + r, stroke, 2f);
        ds.DrawLine(center.X, center.Y + r, center.X - r, center.Y, stroke, 2f);
        ds.DrawLine(center.X - r, center.Y, center.X, center.Y - r, stroke, 2f);

        // Leads
        ds.DrawLine(center.X - 35, center.Y - 15, center.X - r, center.Y - 15, stroke, 1.5f);
        ds.DrawLine(center.X - 35, center.Y + 15, center.X - r, center.Y + 15, stroke, 1.5f);
        ds.DrawLine(center.X + r, center.Y - 15, center.X + 35, center.Y - 15, stroke, 1.5f);
        ds.DrawLine(center.X + r, center.Y + 15, center.X + 35, center.Y + 15, stroke, 1.5f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y - 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X - 35, center.Y + 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y - 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y + 15), dotColor);
    }

    private void DrawTransformer(CanvasDrawingSession ds, Vector2 center, Color stroke, Color dotColor)
    {
        ds.DrawLine(center.X - 35, center.Y - 15, center.X - 15, center.Y - 15, stroke, 2f);
        ds.DrawLine(center.X - 35, center.Y + 15, center.X - 15, center.Y + 15, stroke, 2f);
        ds.DrawLine(center.X + 15, center.Y - 15, center.X + 35, center.Y - 15, stroke, 2f);
        ds.DrawLine(center.X + 15, center.Y + 15, center.X + 35, center.Y + 15, stroke, 2f);

        ds.DrawCircle(center.X - 15, center.Y - 8, 7, stroke, 2f);
        ds.DrawCircle(center.X - 15, center.Y + 8, 7, stroke, 2f);
        ds.DrawLine(center.X - 3, center.Y - 20, center.X - 3, center.Y + 20, stroke, 2f);
        ds.DrawLine(center.X + 3, center.Y - 20, center.X + 3, center.Y + 20, stroke, 2f);
        ds.DrawCircle(center.X + 15, center.Y - 8, 7, stroke, 2f);
        ds.DrawCircle(center.X + 15, center.Y + 8, 7, stroke, 2f);

        DrawPinNode(ds, new Vector2(center.X - 35, center.Y - 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X - 35, center.Y + 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y - 15), dotColor);
        DrawPinNode(ds, new Vector2(center.X + 35, center.Y + 15), dotColor);
    }

    private void DrawGenericBox(CanvasDrawingSession ds, Vector2 center, float w, float h, Color stroke, Color dotColor)
    {
        ds.DrawRoundedRectangle(center.X - w / 2, center.Y - h / 2, w, h, 4, 4, stroke, 2f);
        ds.DrawLine(center.X - w / 2 - 15, center.Y, center.X - w / 2, center.Y, stroke, 2f);
        ds.DrawLine(center.X + w / 2, center.Y, center.X + w / 2 + 15, center.Y, stroke, 2f);
        DrawPinNode(ds, new Vector2(center.X - w / 2 - 15, center.Y), dotColor);
        DrawPinNode(ds, new Vector2(center.X + w / 2 + 15, center.Y), dotColor);
    }

    // =========================================================================
    // Pointer and Interactive Event Handlers
    // =========================================================================
    private (CircuitElement Element, CircuitPin Pin)? FindPinAt(Vector2 worldPos, float radius = 14f)
    {
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            var el = Elements[i];
            foreach (var pin in el.Pins)
            {
                var pPos = el.GetPinWorldPosition(pin);
                if (Vector2.Distance(worldPos, pPos) <= radius)
                {
                    return (el, pin);
                }
            }
        }
        return null;
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);

        var pt = e.GetCurrentPoint(Canvas);
        var screenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
        var worldPos = ScreenToWorld(screenPos);

        // 1. Check if clicked near a Pin or WireTap
        var hitPin = FindPinAt(worldPos);
        var hitTap = FindWireTapAt(worldPos, 8.0f);

        if (_isDrawingWire)
        {
            if (hitPin != null)
            {
                // Completing wire at a Pin!
                var endP = hitPin.Value.Element.GetPinWorldPosition(hitPin.Value.Pin);

                if (_wireStartParentWire != null)
                {
                    // Wire-to-Pin!
                    var wire = new CircuitWire
                    {
                        Id = $"W_{Guid.NewGuid():N}"[..8],
                        StartElementId = _wireStartParentWire.Id,
                        StartPinName = "WireTap",
                        EndElementId = hitPin.Value.Element.Id,
                        EndPinName = hitPin.Value.Pin.Name,
                        StartX = _wireStartPos.X,
                        StartY = _wireStartPos.Y,
                        EndX = endP.X,
                        EndY = endP.Y,
                        FlipCorner = false,
                        NetId = _wireStartParentWire.NetId
                    };
                    Wires.Add(wire);
                    WireCreated?.Invoke(wire);
                    StatusMessageChanged?.Invoke($"已接入导线支路 (Wire-to-Pin): {wire.Id}");
                }
                else if (_wireStartElement != null && _wireStartPin != null)
                {
                    if (hitPin.Value.Element != _wireStartElement || hitPin.Value.Pin != _wireStartPin)
                    {
                        // Pin-to-Pin!
                        var startDir = _wireStartElement.GetPinWorldDirection(_wireStartPin);
                        bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);
                        var wire = new CircuitWire
                        {
                            Id = $"W_{Guid.NewGuid():N}"[..8],
                            StartElementId = _wireStartElement.Id,
                            StartPinName = _wireStartPin.Name,
                            EndElementId = hitPin.Value.Element.Id,
                            EndPinName = hitPin.Value.Pin.Name,
                            StartX = _wireStartPos.X,
                            StartY = _wireStartPos.Y,
                            EndX = endP.X,
                            EndY = endP.Y,
                            FlipCorner = startIsVertical
                        };
                        Wires.Add(wire);
                        WireCreated?.Invoke(wire);
                        StatusMessageChanged?.Invoke($"已成功连通导线: {_wireStartElement.Id}.{_wireStartPin.Name} ⟷ {hitPin.Value.Element.Id}.{hitPin.Value.Pin.Name}");
                    }
                }

                _isDrawingWire = false;
                _wireStartElement = null;
                _wireStartPin = null;
                _wireStartParentWire = null;
                Canvas.ReleasePointerCapture(e.Pointer);
                Canvas.Invalidate();
                return;
            }
            else if (hitTap.HasValue)
            {
                // Completing wire at a WireTap!
                var targetWire = hitTap.Value.Wire;
                var tapPos = hitTap.Value.SnapPos;

                if (_wireStartParentWire != null && _wireStartParentWire != targetWire)
                {
                    // Wire-to-Wire!
                    var wire = new CircuitWire
                    {
                        Id = $"W_{Guid.NewGuid():N}"[..8],
                        StartElementId = _wireStartParentWire.Id,
                        StartPinName = "WireTap",
                        EndElementId = targetWire.Id,
                        EndPinName = "WireTap",
                        StartX = _wireStartPos.X,
                        StartY = _wireStartPos.Y,
                        EndX = tapPos.X,
                        EndY = tapPos.Y,
                        FlipCorner = false,
                        NetId = targetWire.NetId
                    };
                    Wires.Add(wire);
                    WireCreated?.Invoke(wire);
                    StatusMessageChanged?.Invoke($"已建立母线间跳线 (Wire-to-Wire): {wire.Id}");
                }
                else if (_wireStartElement != null && _wireStartPin != null)
                {
                    // Pin-to-Wire!
                    var startDir = _wireStartElement.GetPinWorldDirection(_wireStartPin);
                    bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);
                    var wire = new CircuitWire
                    {
                        Id = $"W_{Guid.NewGuid():N}"[..8],
                        StartElementId = _wireStartElement.Id,
                        StartPinName = _wireStartPin.Name,
                        EndElementId = targetWire.Id,
                        EndPinName = "WireTap",
                        StartX = _wireStartPos.X,
                        StartY = _wireStartPos.Y,
                        EndX = tapPos.X,
                        EndY = tapPos.Y,
                        FlipCorner = startIsVertical,
                        NetId = targetWire.NetId
                    };
                    Wires.Add(wire);
                    WireCreated?.Invoke(wire);
                    StatusMessageChanged?.Invoke($"已接入导线支路 (T形节点): {wire.Id}");
                }

                _isDrawingWire = false;
                _wireStartElement = null;
                _wireStartPin = null;
                _wireStartParentWire = null;
                Canvas.ReleasePointerCapture(e.Pointer);
                Canvas.Invalidate();
                return;
            }
            else
            {
                // Clicked empty space while drawing wire: cancel
                _isDrawingWire = false;
                _wireStartElement = null;
                _wireStartPin = null;
                _wireStartParentWire = null;
                Canvas.ReleasePointerCapture(e.Pointer);
                Canvas.Invalidate();
                return;
            }
        }

        // Not currently drawing wire: check if starting to draw a wire
        if (hitPin != null)
        {
            // Start from Pin
            _isDrawingWire = true;
            _wireStartElement = hitPin.Value.Element;
            _wireStartPin = hitPin.Value.Pin;
            _wireStartParentWire = null;
            _wireStartPos = hitPin.Value.Element.GetPinWorldPosition(hitPin.Value.Pin);
            _currentWireEnd = _wireStartPos;
            Canvas.CapturePointer(e.Pointer);
            StatusMessageChanged?.Invoke($"正在连接导线: 从 {hitPin.Value.Element.Id}.{hitPin.Value.Pin.Name} 出发，点击目标引脚或母线完成连接，按 Esc 取消");
            Canvas.Invalidate();
            return;
        }
        else if (IsWireMode && hitTap.HasValue)
        {
            // Start from WireTap (WireMode active)
            _isDrawingWire = true;
            _wireStartElement = null;
            _wireStartPin = null;
            _wireStartParentWire = hitTap.Value.Wire;
            _wireStartPos = hitTap.Value.SnapPos;
            _currentWireEnd = _wireStartPos;
            Canvas.CapturePointer(e.Pointer);
            StatusMessageChanged?.Invoke($"正在引出母线支路: 从 {hitTap.Value.Wire.Id} ({hitTap.Value.SnapPos.X}, {hitTap.Value.SnapPos.Y}) 出发，点击目标引脚或母线完成连接");
            Canvas.Invalidate();
            return;
        }

        // 2. Check if hit testing any Wire
        CircuitWire? hitWire = null;
        int hitCorner = -1;
        int hitSegment = -1;
        bool hitSegHoriz = false;

        for (int i = Wires.Count - 1; i >= 0; i--)
        {
            var w = Wires[i];
            int c = w.HitTestCorner(worldPos, 8f);
            if (c >= 0)
            {
                hitWire = w;
                hitCorner = c;
                break;
            }
            if (w.HitTestSegment(worldPos, out int s, out bool h, 6f))
            {
                hitWire = w;
                hitSegment = s;
                hitSegHoriz = h;
                break;
            }
            if (w.HitTest(worldPos, 8f))
            {
                hitWire = w;
                break;
            }
        }

        if (hitWire != null)
        {
            SelectedWire = hitWire;
            SelectedElement = null;
            ElementSelected?.Invoke(null);
            UpdateSelectionBar(null);

            // Directly start interactive drag on canvas
            _isDraggingWire = true;
            _draggedWire = hitWire;
            _draggedCornerIndex = hitCorner;
            _draggedSegmentIndex = hitSegment;
            _draggedSegmentIsHorizontal = hitSegHoriz;
            _dragStartPointerPos = worldPos;

            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            StatusMessageChanged?.Invoke($"已选中导线 · 拖拽拐角或线段可直接调整走线 · 按 [Del] 键删除");
            Canvas.Invalidate();
            return;
        }

        // 3. Check if hit testing any CircuitElement
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
            SelectedElement = hitElement;
            SelectedWire = null;
            ElementSelected?.Invoke(hitElement);

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
            // Clicked empty space: deselect and pan canvas
            SelectedElement = null;
            SelectedWire = null;
            ElementSelected?.Invoke(null);

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

        if (_isDraggingWire && _draggedWire != null)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

            var p1 = new Vector2(_draggedWire.StartX, _draggedWire.StartY);
            var p2 = new Vector2(_draggedWire.EndX, _draggedWire.EndY);

            if (_draggedCornerIndex >= 0)
            {
                // Dragging a corner handle
                float snappedX = MathF.Round(worldPos.X / 10f) * 10f;
                float snappedY = MathF.Round(worldPos.Y / 10f) * 10f;

                var cornerA = new Vector2(p1.X, p2.Y); // FlipCorner = true
                var cornerB = new Vector2(p2.X, p1.Y); // FlipCorner = false

                bool newFlip = Vector2.Distance(worldPos, cornerA) < Vector2.Distance(worldPos, cornerB);
                if (_draggedWire.FlipCorner != newFlip)
                {
                    _draggedWire.FlipCorner = newFlip;
                }

                if (_draggedWire.FlipCorner)
                {
                    _draggedWire.CustomMid = snappedY;
                }
                else
                {
                    _draggedWire.CustomMid = snappedX;
                }

                Canvas.Invalidate();
            }
            else if (_draggedSegmentIndex >= 0)
            {
                // Dragging an intermediate segment perpendicularly
                if (_draggedSegmentIsHorizontal)
                {
                    float snappedY = MathF.Round(worldPos.Y / 10f) * 10f;
                    _draggedWire.CustomMid = snappedY;
                }
                else
                {
                    float snappedX = MathF.Round(worldPos.X / 10f) * 10f;
                    _draggedWire.CustomMid = snappedX;
                }
                Canvas.Invalidate();
            }
            else
            {
                // General wire drag: toggle flip corner based on pointer proximity
                var cornerA = new Vector2(p1.X, p2.Y);
                var cornerB = new Vector2(p2.X, p1.Y);
                bool newFlip = Vector2.Distance(worldPos, cornerA) < Vector2.Distance(worldPos, cornerB);
                if (_draggedWire.FlipCorner != newFlip)
                {
                    _draggedWire.FlipCorner = newFlip;
                    Canvas.Invalidate();
                }
            }
        }
        else if (_isDraggingElement && _draggedElement != null)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

            var delta = worldPos - _dragStartPointerPos;
            float newX = MathF.Round((_dragStartElementPos.X + delta.X) / 10f) * 10f;
            float newY = MathF.Round((_dragStartElementPos.Y + delta.Y) / 10f) * 10f;

            _draggedElement.X = newX;
            _draggedElement.Y = newY;
            ElementMoved?.Invoke(_draggedElement);

            // Dynamically update connected wires
            SyncConnectedWiresToElement(_draggedElement);

            Canvas.Invalidate();
        }
        else if (_isPanningCanvas)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            var delta = currentScreenPos - _lastPointerPos;
            _lastPointerPos = currentScreenPos;
            _translation += delta;
            Canvas.Invalidate();
        }
        else if (_isDrawingWire)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            _hoveredPin = FindPinAt(worldPos);
            _hoveredWireTap = FindWireTapAt(worldPos, 8.0f);

            if (_hoveredPin.HasValue)
            {
                _currentWireEnd = _hoveredPin.Value.Element.GetPinWorldPosition(_hoveredPin.Value.Pin);
            }
            else if (_hoveredWireTap.HasValue)
            {
                _currentWireEnd = _hoveredWireTap.Value.SnapPos;
            }
            else
            {
                // Micro-snap free wire endpoint to 10px grid
                _currentWireEnd = new Vector2(
                    MathF.Round(worldPos.X / 10f) * 10f,
                    MathF.Round(worldPos.Y / 10f) * 10f);
            }
            Canvas.Invalidate();
        }
        else
        {
            // Hover detection: Pin vs WireTap vs Element vs Wire vs Canvas
            _hoveredPin = FindPinAt(worldPos);
            _hoveredWireTap = FindWireTapAt(worldPos, 8.0f);

            if (_hoveredPin.HasValue)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            }
            else if (_hoveredWireTap.HasValue)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            }
            else
            {
                bool isOverElement = Elements.Any(el => el.HitTest(worldPos));
                bool isOverWire = Wires.Any(w => w.HitTest(worldPos, 6f));
                ProtectedCursor = (isOverElement || isOverWire)
                    ? InputSystemCursor.Create(InputSystemCursorShape.Hand)
                    : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            }
            Canvas.Invalidate();
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingWire = false;
        _draggedWire = null;
        _draggedCornerIndex = -1;
        _draggedSegmentIndex = -1;
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
        if (ActualWidth <= 0 || ActualHeight <= 0) pivot = new Vector2(400f, 300f);

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

    private void WireModeButton_Click(object sender, RoutedEventArgs e)
    {
        IsWireMode = WireModeButton.IsChecked == true;
    }

    // =========================================================================
    // Keyboard and Manipulation Operations
    // =========================================================================
    private void CircuitCanvas_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.R)
        {
            RotateSelected();
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            DeselectSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.W)
        {
            IsWireMode = !IsWireMode;
            e.Handled = true;
        }
    }

    public void RotateSelected()
    {
        if (SelectedElement != null)
        {
            SelectedElement.RotationDeg = (SelectedElement.RotationDeg + 90) % 360;
            SelectedElement.Orientation = (SelectedElement.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
            SyncConnectedWiresToElement(SelectedElement);
            UpdateSelectionBar(SelectedElement);
            StatusMessageChanged?.Invoke($"元件 {SelectedElement.Id} 已顺时针旋转至 {SelectedElement.RotationDeg}° ({SelectedElement.Orientation})");
            Canvas.Invalidate();
        }
    }

    public void DeleteSelected()
    {
        if (SelectedElement != null)
        {
            var el = SelectedElement;
            // Remove all wires connected to this element
            var attachedWires = Wires.Where(w => w.StartElementId == el.Id || w.EndElementId == el.Id).ToList();
            foreach (var w in attachedWires) Wires.Remove(w);

            Elements.Remove(el);
            SelectedElement = null;
            ElementSelected?.Invoke(null);
            StatusMessageChanged?.Invoke($"已从画布移除元件 {el.Name}");
            Canvas.Invalidate();
        }
        else if (SelectedWire != null)
        {
            var w = SelectedWire;
            Wires.Remove(w);
            SelectedWire = null;
            StatusMessageChanged?.Invoke("已删除导线连接");
            Canvas.Invalidate();
        }
    }

    public void DeselectSelected()
    {
        _isDrawingWire = false;
        _wireStartElement = null;
        _wireStartPin = null;

        SelectedElement = null;
        SelectedWire = null;
        foreach (var el in Elements) el.IsSelected = false;
        foreach (var w in Wires) w.IsSelected = false;

        ElementSelected?.Invoke(null);
        Canvas.Invalidate();
    }

    private void SyncConnectedWiresToElement(CircuitElement el)
    {
        var movedWireIds = new System.Collections.Generic.HashSet<string>();

        // Phase 1: Update direct pin-connected wires with 10px grid snap
        foreach (var w in Wires)
        {
            if (w.StartElementId == el.Id && w.StartPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.StartX = pPos.X;
                    w.StartY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
            if (w.EndElementId == el.Id && w.EndPinName != "WireTap")
            {
                var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                if (pin != null)
                {
                    var pPos = el.GetPinWorldPosition(pin);
                    w.EndX = pPos.X;
                    w.EndY = pPos.Y;
                    movedWireIds.Add(w.Id);
                }
            }
        }

        // Phase 2A: Branch wires starting or ending at el and tapping into a parent rail
        // Protect parent trunk bus rails from distortion by sliding tap perpendicularly along rail
        foreach (var w in Wires)
        {
            if (w.StartElementId == el.Id && w.EndPinName == "WireTap")
            {
                var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.StartX = pPos.X;
                        w.StartY = pPos.Y;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
            else if (w.EndElementId == el.Id && w.StartPinName == "WireTap")
            {
                var parent = Wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                if (parent != null)
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.EndX = pPos.X;
                        w.EndY = pPos.Y;
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                    }
                }
            }
        }

        // Phase 2B: Child wires tapping into parent wires that moved in Phase 1 or 2A
        bool anyUpdated;
        int maxPasses = 3;
        do
        {
            anyUpdated = false;
            foreach (var w in Wires)
            {
                // Pure trunk rails or wires ending with _RAIL or _BUS are never child branches of feeder lines
                if (w.Id.EndsWith("_RAIL", StringComparison.OrdinalIgnoreCase) ||
                    w.Id.EndsWith("_BUS", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrEmpty(w.StartElementId) && string.IsNullOrEmpty(w.EndElementId)))
                {
                    continue;
                }

                if (w.EndPinName == "WireTap" && movedWireIds.Contains(w.EndElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
                else if (w.StartPinName == "WireTap" && movedWireIds.Contains(w.StartElementId) && !movedWireIds.Contains(w.Id))
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        movedWireIds.Add(w.Id);
                        anyUpdated = true;
                    }
                }
            }
            maxPasses--;
        } while (anyUpdated && maxPasses > 0);
    }

    private (CircuitWire Wire, Vector2 SnapPos)? FindWireTapAt(Vector2 worldPos, float tolerance = 8.0f)
    {
        // Must be strictly outside pin keep-out zones (> 5.0px)
        foreach (var el in Elements)
        {
            foreach (var pin in el.Pins)
            {
                if (Vector2.Distance(worldPos, el.GetPinWorldPosition(pin)) <= 5.0f)
                    return null;
            }
        }

        CircuitWire? bestWire = null;
        Vector2 bestSnap = worldPos;
        float bestDist = float.MaxValue;

        for (int i = Wires.Count - 1; i >= 0; i--)
        {
            var wire = Wires[i];
            if (wire.HitTest(worldPos, tolerance))
            {
                var nearest = GetNearestPointOnWire(wire, worldPos);
                float dist = Vector2.Distance(worldPos, nearest);
                if (dist <= tolerance && dist < bestDist)
                {
                    bestDist = dist;
                    bestWire = wire;
                    bestSnap = nearest;
                }
            }
        }

        if (bestWire != null)
        {
            // Verify snap position is also outside pin keep-out (> 5.0px)
            foreach (var el in Elements)
            {
                foreach (var pin in el.Pins)
                {
                    if (Vector2.Distance(bestSnap, el.GetPinWorldPosition(pin)) <= 5.0f)
                        return null;
                }
            }
            return (bestWire, bestSnap);
        }

        return null;
    }

    private static bool IsPointOnSegmentStrict(Vector2 p, Vector2 a, Vector2 b, float tolerance = 3.5f)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-4f) return false;
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var proj = a + t * ab;
        if (Vector2.Distance(p, proj) > tolerance) return false;
        // Must not be at the endpoints themselves (at least 4px away from endpoints of the parent segment)
        if (Vector2.Distance(p, a) < 4f || Vector2.Distance(p, b) < 4f) return false;
        return true;
    }

    private static Vector2 GetNearestPointOnWire(CircuitWire wire, Vector2 p)
    {
        var pts = wire.GetPathPoints();
        float bestDist = float.MaxValue;
        Vector2 bestProj = p;

        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var ab = b - a;
            float lenSq = ab.LengthSquared();
            if (lenSq < 1e-4f) continue;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
            var proj = a + t * ab;

            if (Math.Abs(ab.X) > Math.Abs(ab.Y)) // Horizontal segment
            {
                float minX = MathF.Min(a.X, b.X);
                float maxX = MathF.Max(a.X, b.X);
                float snappedX = MathF.Round(proj.X / 10f) * 10f;
                proj = new Vector2(Math.Clamp(snappedX, minX, maxX), a.Y);
            }
            else // Vertical segment
            {
                float minY = MathF.Min(a.Y, b.Y);
                float maxY = MathF.Max(a.Y, b.Y);
                float snappedY = MathF.Round(proj.Y / 10f) * 10f;
                proj = new Vector2(a.X, Math.Clamp(snappedY, minY, maxY));
            }

            float d = Vector2.Distance(p, proj);
            if (d < bestDist)
            {
                bestDist = d;
                bestProj = proj;
            }
        }
        return bestProj;
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
