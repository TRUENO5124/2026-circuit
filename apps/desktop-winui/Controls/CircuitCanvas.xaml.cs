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

    // Windows-style Marquee Box Selection state
    private bool _isBoxSelecting;
    private Vector2 _boxSelectStart;
    private Vector2 _boxSelectCurrent;

    // Multi-element / group dragging state
    private bool _isDraggingSelection;
    private Vector2 _dragStartPointerPos;
    private readonly System.Collections.Generic.Dictionary<CircuitElement, Vector2> _dragStartElementPositions = new();
    private readonly System.Collections.Generic.Dictionary<CircuitWire, System.Collections.Generic.List<Vector2>> _dragStartWireWaypoints = new();
    private readonly System.Collections.Generic.Dictionary<CircuitWire, (Vector2 Start, Vector2 End)> _dragStartWireEndpoints = new();

    // Wire dragging state (strictly horizontal or vertical segments)
    private bool _isDraggingWire;
    private CircuitWire? _draggedWire;
    private int _draggedSegmentIndex = -1;
    private bool _draggedSegmentIsHorizontal;
    private System.Collections.Generic.List<Vector2> _dragWireBaselinePoints = new();

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
    public event Action<CircuitWire?>? WireSelected;
    public event Action<CircuitElement>? ElementMoved;
    public event Action<string, Vector2>? ElementDroppedFromLibrary;
    public event Action<CircuitWire>? WireCreated;
    public event Action<string>? StatusMessageChanged;
    public event Action? PushUndoRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;

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
            canvas.WireSelected?.Invoke(selected);
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
            SelectionBarText.Text = $"{el.Id} ({GetElementValueText(el)}) · {el.TransformDisplay}";
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

                // If selected, draw soft accent halo around entire wire path
                if (wire.IsSelected)
                {
                    var haloColor = Color.FromArgb(70, accentColor.R, accentColor.G, accentColor.B);
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        ds.DrawLine(pts[i], pts[i + 1], haloColor, 8.0f);
                    }
                }

                for (int i = 0; i < pts.Count - 1; i++)
                {
                    ds.DrawLine(pts[i], pts[i + 1], wColor, strokeWidth);
                }

                // If selected, draw interactive editing handles at the endpoints and bend corner(s)
                if (wire.IsSelected && pts.Count > 0)
                {
                    // Start & End endpoint nodes
                    ds.FillCircle(pts[0], 4.5f, accentColor);
                    ds.DrawCircle(pts[0], 4.5f, isDark ? Colors.White : Colors.Black, 1.2f);

                    ds.FillCircle(pts[^1], 4.5f, accentColor);
                    ds.DrawCircle(pts[^1], 4.5f, isDark ? Colors.White : Colors.Black, 1.2f);

                    // Mid-segment drag grips
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        var mid = (pts[i] + pts[i + 1]) * 0.5f;
                        bool isH = MathF.Abs(pts[i].Y - pts[i + 1].Y) < 1f;
                        if (isH)
                        {
                            ds.FillRoundedRectangle(mid.X - 6f, mid.Y - 2.5f, 12f, 5f, 2f, 2f, accentColor);
                            ds.DrawRoundedRectangle(mid.X - 6f, mid.Y - 2.5f, 12f, 5f, 2f, 2f, isDark ? Colors.White : Colors.Black, 1.0f);
                        }
                        else
                        {
                            ds.FillRoundedRectangle(mid.X - 2.5f, mid.Y - 6f, 5f, 12f, 2f, 2f, accentColor);
                            ds.DrawRoundedRectangle(mid.X - 2.5f, mid.Y - 6f, 5f, 12f, 2f, 2f, isDark ? Colors.White : Colors.Black, 1.0f);
                        }
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
                DrawElement(ds, el, componentColor, labelColor, dotColor);

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

            // 7. Draw Windows-style marquee box selection rectangle
            if (_isBoxSelecting)
            {
                float minX = MathF.Min(_boxSelectStart.X, _boxSelectCurrent.X);
                float maxX = MathF.Max(_boxSelectStart.X, _boxSelectCurrent.X);
                float minY = MathF.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
                float maxY = MathF.Max(_boxSelectStart.Y, _boxSelectCurrent.Y);
                var box = new Rect(minX, minY, MathF.Max(maxX - minX, 1f), MathF.Max(maxY - minY, 1f));

                var boxFill = Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B);
                var boxStroke = Color.FromArgb(220, accentColor.R, accentColor.G, accentColor.B);
                ds.FillRectangle(box, boxFill);
                ds.DrawRectangle(box, boxStroke, 1.2f);
            }
        }
        catch (Exception ex)
        {
            App.LogTrace($"[Canvas_Draw Exception] {ex.Message}");
        }
    }


    private void DrawElement(CanvasDrawingSession ds, CircuitElement el, Color componentColor, Color labelColor, Color dotColor)
    {
        var center = new Vector2(el.X, el.Y);
        var originalTransform = ds.Transform;

        // Mirror (about the element centre, before rotation) + rotation. The same
        // matrix is used by CircuitElement.GetPinWorldPosition(), so pin dots,
        // wires and solder junctions stay aligned with the drawn symbol.
        if (el.RotationDeg != 0 || el.MirrorX)
        {
            ds.Transform = el.GetSymbolTransformMatrix() * originalTransform;
        }

        // 统一从国际标准几何库（IEC 60617 / IEEE 315）渲染元件符号，
        // 与符号库卡片（Controls/CircuitSymbolPreview.xaml.cs）同源。
        DrawSymbolGeometry(ds, el.Type, center, componentColor);

        // 引脚圆点：位置来自 CircuitElement.InitializeDefaultPins() 的局部坐标
        foreach (var pin in el.Pins)
        {
            DrawPinNode(ds, center + new Vector2(pin.RelativeX, pin.RelativeY), dotColor);
        }

        // Restore original unrotated world transform for text rendering
        ds.Transform = originalTransform;

        // Draw beautifully positioned, non-overlapping component labels
        DrawElementLabels(ds, el, center, labelColor, ActualTheme == ElementTheme.Dark);
    }

    private static readonly CanvasStrokeStyle SymbolStrokeStyle = new()
    {
        LineJoin = CanvasLineJoin.Round,
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round
    };

    /// <summary>
    /// 按 Models/SymbolGeometry.cs 的图元绘制元件符号（与符号库卡片同源）。
    /// 图元坐标为元件局部坐标，绘制时整体平移到元件中心；旋转已由 ds.Transform 处理。
    /// </summary>
    private static void DrawSymbolGeometry(CanvasDrawingSession ds, ElementType type, Vector2 center, Color stroke)
    {
        var primitives = SymbolGeometryLibrary.Build(type);
        foreach (var primitive in primitives)
        {
            switch (primitive.Kind)
            {
                case SymbolPrimitiveKind.Polyline:
                    DrawSymbolPolyline(ds, primitive, center, stroke);
                    break;

                case SymbolPrimitiveKind.FilledPolygon:
                    FillSymbolPolygon(ds, primitive, center, stroke);
                    break;

                case SymbolPrimitiveKind.Circle:
                    DrawSymbolCircle(ds, primitive, center, stroke);
                    break;
            }
        }
    }

    private static void DrawSymbolPolyline(CanvasDrawingSession ds, SymbolPrimitive primitive, Vector2 center, Color stroke)
    {
        var points = primitive.Points;
        if (points.Count < 2) return;

        if (points.Count == 2)
        {
            ds.DrawLine(center + points[0], center + points[1], stroke, primitive.Thickness, SymbolStrokeStyle);
            return;
        }

        using var pathBuilder = new CanvasPathBuilder(ds);
        pathBuilder.BeginFigure(center + points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            pathBuilder.AddLine(center + points[i]);
        }
        pathBuilder.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(pathBuilder);
        ds.DrawGeometry(geometry, stroke, primitive.Thickness, SymbolStrokeStyle);
    }

    private static void FillSymbolPolygon(CanvasDrawingSession ds, SymbolPrimitive primitive, Vector2 center, Color fill)
    {
        var points = primitive.Points;
        if (points.Count < 3) return;

        using var pathBuilder = new CanvasPathBuilder(ds);
        pathBuilder.BeginFigure(center + points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            pathBuilder.AddLine(center + points[i]);
        }
        pathBuilder.EndFigure(CanvasFigureLoop.Closed);
        using var geometry = CanvasGeometry.CreatePath(pathBuilder);
        ds.FillGeometry(geometry, fill);
        // 轻微描边，避免小尺寸/抗锯齿时实心箭头边缘出现缺口
        ds.DrawGeometry(geometry, fill, 0.6f);
    }

    private static void DrawSymbolCircle(CanvasDrawingSession ds, SymbolPrimitive primitive, Vector2 center, Color stroke)
    {
        if (primitive.Points.Count < 2) return;

        var circleCenter = center + primitive.Points[0];
        float radius = Vector2.Distance(primitive.Points[0], primitive.Points[1]);
        if (radius <= 0.05f) return;

        if (primitive.Filled)
        {
            ds.FillCircle(circleCenter, radius, stroke);
        }
        else
        {
            ds.DrawCircle(circleCenter, radius, stroke, primitive.Thickness);
        }
    }

    /// <summary>
    /// 元件数值标签：半导体（BJT / MOSFET / JFET / 二极管）使用
    /// CircuitElement.FormattedParameterDisplay（β = 100 / Vth = 1.5 V /
    /// VGS(off) = -2.5 V / VF = 0.7 V），其余元件沿用 FormattedValueDisplay。
    /// </summary>
    private static string GetElementValueText(CircuitElement el) => el.Type switch
    {
        ElementType.BjtNpn or ElementType.BjtPnp
            or ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh
            or ElementType.MosfetNDep or ElementType.MosfetPDep
            or ElementType.Jfet or ElementType.JfetN or ElementType.JfetP
            or ElementType.Diode => el.FormattedParameterDisplay,
        _ => el.FormattedValueDisplay
    };

    private void DrawElementLabels(CanvasDrawingSession ds, CircuitElement el, Vector2 center, Color labelColor, bool isDark)
    {
        string id = el.Id;
        string val = GetElementValueText(el);

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
        // Labels are drawn in screen space (never mirrored); only the side they
        // sit on follows the symbol mirror so text does not cover the symbol.
        bool mirrored = el.MirrorX;

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
                    if (!mirrored)
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        ds.DrawText(id, center.X + 18, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X + 18, center.Y + 2, valColor, valFormat);
                    }
                    else
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        ds.DrawText(id, center.X - 18, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X - 18, center.Y + 2, valColor, valFormat);
                    }
                }
                break;

            case ElementType.Capacitor:
            case ElementType.CapacitorPolarized:
                if (!isVertical)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                    ds.DrawText(id, center.X, center.Y - 34, idColor, idFormat);
                    ds.DrawText(val, center.X, center.Y + 22, valColor, valFormat);
                }
                else
                {
                    if (!mirrored)
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        ds.DrawText(id, center.X + 24, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X + 24, center.Y + 2, valColor, valFormat);
                    }
                    else
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        ds.DrawText(id, center.X - 24, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X - 24, center.Y + 2, valColor, valFormat);
                    }
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
                    if (!mirrored)
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                        ds.DrawText(id, center.X + 26, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X + 26, center.Y + 2, valColor, valFormat);
                    }
                    else
                    {
                        idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                        ds.DrawText(id, center.X - 26, center.Y - 14, idColor, idFormat);
                        ds.DrawText(val, center.X - 26, center.Y + 2, valColor, valFormat);
                    }
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
                if (!mirrored)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    ds.DrawText(id, center.X + 22, center.Y - 8, idColor, idFormat);
                    if (!string.IsNullOrWhiteSpace(val) && val != "0 V")
                    {
                        ds.DrawText(val, center.X + 22, center.Y + 6, valColor, valFormat);
                    }
                }
                else
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                    ds.DrawText(id, center.X - 22, center.Y - 8, idColor, idFormat);
                    if (!string.IsNullOrWhiteSpace(val) && val != "0 V")
                    {
                        ds.DrawText(val, center.X - 22, center.Y + 6, valColor, valFormat);
                    }
                }
                break;

            case ElementType.VccPort:
                // Multisim 风格电源端口：id 居中绘制在电源横杠上方，数值标注在引线右侧
                idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Center;
                ds.DrawText(id, center.X, center.Y - 24, idColor, idFormat);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    valFormat.HorizontalAlignment = mirrored
                        ? CanvasHorizontalAlignment.Right
                        : CanvasHorizontalAlignment.Left;
                    ds.DrawText(val, center.X + (mirrored ? -8 : 8), center.Y + 6, valColor, valFormat);
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
                if (!mirrored)
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Right;
                    ds.DrawText(id, center.X - 18, center.Y - 36, idColor, idFormat);
                    ds.DrawText(val, center.X - 18, center.Y + 24, valColor, valFormat);
                }
                else
                {
                    idFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    valFormat.HorizontalAlignment = CanvasHorizontalAlignment.Left;
                    ds.DrawText(id, center.X + 18, center.Y - 36, idColor, idFormat);
                    ds.DrawText(val, center.X + 18, center.Y + 24, valColor, valFormat);
                }
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

    // =========================================================================
    // Pointer and Interactive Event Handlers
    // =========================================================================
    private bool IsOutsidePinKeepOut(Vector2 pt, float threshold = 5.0f)
    {
        if (Elements == null) return true;
        for (int i = 0; i < Elements.Count; i++)
        {
            var el = Elements[i];
            for (int j = 0; j < el.Pins.Count; j++)
            {
                if (Vector2.Distance(pt, el.GetPinWorldPosition(el.Pins[j])) <= threshold)
                {
                    return false;
                }
            }
        }
        return true;
    }

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

        // Right or middle mouse button: Pan canvas
        if (pt.Properties.IsRightButtonPressed || pt.Properties.IsMiddleButtonPressed)
        {
            _isPanningCanvas = true;
            _lastPointerPos = screenPos;
            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            return;
        }

        // 1. Check if clicked near a Pin or WireTap
        var hitPin = FindPinAt(worldPos);
        var hitTap = FindWireTapAt(worldPos, 16.0f);

        if (_isDrawingWire)
        {
            if (hitPin != null)
            {
                // Completing wire at a Pin!
                var endP = hitPin.Value.Element.GetPinWorldPosition(hitPin.Value.Pin);

                if (_wireStartParentWire != null)
                {
                    // Wire-to-Pin!
                    PushUndoRequested?.Invoke();
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
                        PushUndoRequested?.Invoke();
                        var startDir = _wireStartElement.GetPinWorldDirection(_wireStartPin);
                        bool startIsVertical = Math.Abs(startDir.Y) > Math.Abs(startDir.X);

                        // Check if target pin already has an existing wire attached to it that we can tap into perpendicularly:
                        CircuitWire? candidateWire = null;
                        Vector2? tapCandidate = null;

                        var targetWires = Wires.Where(w => (w.StartElementId == hitPin.Value.Element.Id && w.StartPinName == hitPin.Value.Pin.Name) ||
                                                           (w.EndElementId == hitPin.Value.Element.Id && w.EndPinName == hitPin.Value.Pin.Name)).ToList();

                        foreach (var tw in targetWires)
                        {
                            var pts = tw.GetPathPoints();
                            for (int si = 0; si < pts.Count - 1; si++)
                            {
                                var sa = pts[si];
                                var sb = pts[si + 1];
                                if (Math.Abs(sa.Y - sb.Y) < 1.0f) // Horizontal segment
                                {
                                    var proj = new Vector2(_wireStartPos.X, sa.Y);
                                    float minX = MathF.Min(sa.X, sb.X);
                                    float maxX = MathF.Max(sa.X, sb.X);
                                    if (proj.X >= minX + 8f && proj.X <= maxX - 8f && IsOutsidePinKeepOut(proj))
                                    {
                                        candidateWire = tw;
                                        tapCandidate = proj;
                                        break;
                                    }
                                }
                                else if (Math.Abs(sa.X - sb.X) < 1.0f) // Vertical segment
                                {
                                    var proj = new Vector2(sa.X, _wireStartPos.Y);
                                    float minY = MathF.Min(sa.Y, sb.Y);
                                    float maxY = MathF.Max(sa.Y, sb.Y);
                                    if (proj.Y >= minY + 8f && proj.Y <= maxY - 8f && IsOutsidePinKeepOut(proj))
                                    {
                                        candidateWire = tw;
                                        tapCandidate = proj;
                                        break;
                                    }
                                }
                            }
                            if (candidateWire != null) break;
                        }

                        if (candidateWire != null && tapCandidate != null)
                        {
                            var wire = new CircuitWire
                            {
                                Id = $"W_{Guid.NewGuid():N}"[..8],
                                StartElementId = _wireStartElement.Id,
                                StartPinName = _wireStartPin.Name,
                                EndElementId = candidateWire.Id,
                                EndPinName = "WireTap",
                                StartX = _wireStartPos.X,
                                StartY = _wireStartPos.Y,
                                EndX = tapCandidate.Value.X,
                                EndY = tapCandidate.Value.Y,
                                FlipCorner = startIsVertical,
                                NetId = candidateWire.NetId
                            };
                            Wires.Add(wire);
                            WireCreated?.Invoke(wire);
                            StatusMessageChanged?.Invoke($"已自动统一至共享导线路径并在节点处接入: {wire.Id}");
                        }
                        else
                        {
                            // Direct connection
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
                    PushUndoRequested?.Invoke();
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
                    PushUndoRequested?.Invoke();
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

        // Not currently drawing wire:
        // A) If explicitly in Wire Mode, allow tapping wires and starting wires from pins
        if (IsWireMode)
        {
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
            else if (hitTap.HasValue)
            {
                // Start from WireTap
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
        }

        // B) Normal Selection & Editing Mode:
        bool isCtrlOrShift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ||
                             Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // Check if cursor hits an element or wire
        CircuitElement? hitElement = null;
        for (int i = Elements.Count - 1; i >= 0; i--)
        {
            if (Elements[i].HitTest(worldPos))
            {
                hitElement = Elements[i];
                break;
            }
        }

        var hitWire = FindWireAt(worldPos, out int hitSegment, out bool hitSegHoriz);

        // 1. Check if user clicked on an ALREADY SELECTED element, or a selected wire in a multi-element selection
        bool clickedOnSelectedElement = hitElement != null && hitElement.IsSelected;
        bool clickedOnSelectedWireWithElements = hitWire != null && hitWire.IsSelected && Elements.Any(el => el.IsSelected);

        if (clickedOnSelectedElement || clickedOnSelectedWireWithElements)
        {
            // Initiate group dragging of the selected elements & internal interconnects
            PushUndoRequested?.Invoke();
            _isDraggingSelection = true;
            _dragStartPointerPos = worldPos;
            _dragStartElementPositions.Clear();
            _dragStartWireWaypoints.Clear();
            _dragStartWireEndpoints.Clear();

            var selectedElements = Elements.Where(el => el.IsSelected).ToList();
            foreach (var el in selectedElements)
            {
                _dragStartElementPositions[el] = new Vector2(el.X, el.Y);
            }

            // Topology Invariant: ONLY internal wires whose BOTH endpoints connect to moving elements may rigidly translate!
            // All boundary wires connecting to stationary elements or bus rails MUST remain anchored!
            var selectedIds = selectedElements.Select(el => el.Id).ToHashSet();
            foreach (var w in Wires)
            {
                if (selectedIds.Contains(w.StartElementId) && selectedIds.Contains(w.EndElementId))
                {
                    _dragStartWireEndpoints[w] = (new Vector2(w.StartX, w.StartY), new Vector2(w.EndX, w.EndY));
                    _dragStartWireWaypoints[w] = w.Waypoints.Select(p => new Vector2(p.X, p.Y)).ToList();
                }
            }

            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            StatusMessageChanged?.Invoke($"正在拖拽所选区域 ({selectedElements.Count} 个元件)");
            Canvas.Invalidate();
            return;
        }

        // 2. Check if clicked directly on a component's Pin to start a wire
        if (hitPin != null)
        {
            _isDrawingWire = true;
            _wireStartElement = hitPin.Value.Element;
            _wireStartPin = hitPin.Value.Pin;
            _wireStartParentWire = null;
            _wireStartPos = hitPin.Value.Element.GetPinWorldPosition(hitPin.Value.Pin);
            _currentWireEnd = _wireStartPos;
            Canvas.CapturePointer(e.Pointer);
            StatusMessageChanged?.Invoke($"正在连接导线: 从 {hitPin.Value.Element.Id}.{hitPin.Value.Pin.Name} 出发，点击目标引脚或导线完成连接，按 Esc 取消");
            Canvas.Invalidate();
            return;
        }

        // 3. User clicked on an UNSELECTED element
        if (hitElement != null)
        {
            if (isCtrlOrShift)
            {
                hitElement.IsSelected = true;
                SelectedElement = hitElement;
            }
            else
            {
                foreach (var el in Elements) el.IsSelected = (el == hitElement);
                foreach (var w in Wires) w.IsSelected = false;
                SelectedElement = hitElement;
                SelectedWire = null;
            }

            PushUndoRequested?.Invoke();
            _isDraggingSelection = true;
            _dragStartPointerPos = worldPos;
            _dragStartElementPositions.Clear();
            _dragStartWireWaypoints.Clear();
            _dragStartWireEndpoints.Clear();

            var currentSelectedElements = Elements.Where(el => el.IsSelected).ToList();
            foreach (var el in currentSelectedElements)
            {
                _dragStartElementPositions[el] = new Vector2(el.X, el.Y);
            }
            var selIds = currentSelectedElements.Select(el => el.Id).ToHashSet();
            foreach (var w in Wires)
            {
                if (selIds.Contains(w.StartElementId) && selIds.Contains(w.EndElementId))
                {
                    _dragStartWireEndpoints[w] = (new Vector2(w.StartX, w.StartY), new Vector2(w.EndX, w.EndY));
                    _dragStartWireWaypoints[w] = w.Waypoints.Select(p => new Vector2(p.X, p.Y)).ToList();
                }
            }

            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            ElementSelected?.Invoke(SelectedElement);
            UpdateSelectionBar(currentSelectedElements.Count == 1 ? SelectedElement : null);
            if (currentSelectedElements.Count > 1)
            {
                SelectionActionBar.Visibility = Visibility.Visible;
                SelectionBarText.Text = $"已选中 {currentSelectedElements.Count} 个元件";
                if (RotateButton != null) RotateButton.Visibility = Visibility.Visible;
            }
            Canvas.Invalidate();
            return;
        }

        // 4. User clicked on a WIRE: always drag wire segment, preserving endpoint pin/rail anchoring!
        if (hitWire != null)
        {
            PushUndoRequested?.Invoke();
            if (isCtrlOrShift)
            {
                hitWire.IsSelected = !hitWire.IsSelected;
                SelectedWire = hitWire.IsSelected ? hitWire : null;
            }
            else
            {
                if (!hitWire.IsSelected)
                {
                    foreach (var el in Elements) el.IsSelected = false;
                    foreach (var w in Wires) w.IsSelected = (w == hitWire);
                    SelectedElement = null;
                }
                SelectedWire = hitWire;
            }
            WireSelected?.Invoke(SelectedWire);
            ElementSelected?.Invoke(SelectedElement);
            UpdateSelectionBar(SelectedElement);

            _isDraggingWire = true;
            _draggedWire = hitWire;
            _draggedSegmentIndex = hitSegment;
            _draggedSegmentIsHorizontal = hitSegHoriz;
            _dragWireBaselinePoints = hitWire.GetPathPoints();
            _dragStartPointerPos = worldPos;

            Canvas.CapturePointer(e.Pointer);
            ProtectedCursor = hitSegHoriz
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth)
                : InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

            StatusMessageChanged?.Invoke($"已选中导线 {hitWire.Id} · 拖拽平移线段，节点始终保持连通");
            Canvas.Invalidate();
            return;
        }

        // 5. Clicked empty space: Windows-style Rubberband Box Selection
        if (!isCtrlOrShift)
        {
            foreach (var el in Elements) el.IsSelected = false;
            foreach (var w in Wires) w.IsSelected = false;
            SelectedElement = null;
            SelectedWire = null;
            ElementSelected?.Invoke(null);
            WireSelected?.Invoke(null);
            UpdateSelectionBar(null);
        }

        _isBoxSelecting = true;
        _boxSelectStart = worldPos;
        _boxSelectCurrent = worldPos;
        Canvas.CapturePointer(e.Pointer);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        Canvas.Invalidate();
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Canvas);
        var currentScreenPos = new Vector2((float)pt.Position.X, (float)pt.Position.Y);
        var worldPos = ScreenToWorld(currentScreenPos);
        CursorCoordinatesChanged?.Invoke(worldPos.X, worldPos.Y);

        if (_isPanningCanvas)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
            var delta = currentScreenPos - _lastPointerPos;
            _lastPointerPos = currentScreenPos;
            _translation += delta;
            Canvas.Invalidate();
        }
        else if (_isDraggingWire && _draggedWire != null && _draggedSegmentIndex >= 0)
        {
            ProtectedCursor = _draggedSegmentIsHorizontal
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth)
                : InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

            float snappedVal = _draggedSegmentIsHorizontal
                ? MathF.Round(worldPos.Y / 10f) * 10f
                : MathF.Round(worldPos.X / 10f) * 10f;

            // If a single straight wire connects to a parent bus rail via WireTap, allow the tap to slide along the rail
            if (_dragWireBaselinePoints.Count == 2)
            {
                if (!_draggedSegmentIsHorizontal)
                {
                    if (_draggedWire.StartPinName == "WireTap")
                    {
                        var parent = Wires.FirstOrDefault(pw => pw.Id == _draggedWire.StartElementId);
                        if (parent != null)
                        {
                            var proj = parent.ProjectTapPerpendicular(new Vector2(snappedVal, _dragWireBaselinePoints[0].Y));
                            if (MathF.Abs(proj.X - snappedVal) < 1.0f)
                            {
                                _draggedWire.StartX = proj.X;
                                _draggedWire.StartY = proj.Y;
                            }
                        }
                    }
                    if (_draggedWire.EndPinName == "WireTap")
                    {
                        var parent = Wires.FirstOrDefault(pw => pw.Id == _draggedWire.EndElementId);
                        if (parent != null)
                        {
                            var proj = parent.ProjectTapPerpendicular(new Vector2(snappedVal, _dragWireBaselinePoints[^1].Y));
                            if (MathF.Abs(proj.X - snappedVal) < 1.0f)
                            {
                                _draggedWire.EndX = proj.X;
                                _draggedWire.EndY = proj.Y;
                            }
                        }
                    }
                }
                else
                {
                    if (_draggedWire.StartPinName == "WireTap")
                    {
                        var parent = Wires.FirstOrDefault(pw => pw.Id == _draggedWire.StartElementId);
                        if (parent != null)
                        {
                            var proj = parent.ProjectTapPerpendicular(new Vector2(_dragWireBaselinePoints[0].X, snappedVal));
                            if (MathF.Abs(proj.Y - snappedVal) < 1.0f)
                            {
                                _draggedWire.StartX = proj.X;
                                _draggedWire.StartY = proj.Y;
                            }
                        }
                    }
                    if (_draggedWire.EndPinName == "WireTap")
                    {
                        var parent = Wires.FirstOrDefault(pw => pw.Id == _draggedWire.EndElementId);
                        if (parent != null)
                        {
                            var proj = parent.ProjectTapPerpendicular(new Vector2(_dragWireBaselinePoints[^1].X, snappedVal));
                            if (MathF.Abs(proj.Y - snappedVal) < 1.0f)
                            {
                                _draggedWire.EndX = proj.X;
                                _draggedWire.EndY = proj.Y;
                            }
                        }
                    }
                }
            }

            _draggedWire.MoveSegmentFromBaseline(_dragWireBaselinePoints, _draggedSegmentIndex, _draggedSegmentIsHorizontal, snappedVal);
            SyncConnectedWiresToWire(_draggedWire);
            Canvas.Invalidate();
        }
        else if (_isDraggingSelection)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

            var rawDelta = worldPos - _dragStartPointerPos;
            float snappedDeltaX = MathF.Round(rawDelta.X / 10f) * 10f;
            float snappedDeltaY = MathF.Round(rawDelta.Y / 10f) * 10f;
            var snappedDelta = new Vector2(snappedDeltaX, snappedDeltaY);

            // 1. Move all selected elements
            foreach (var (el, startPos) in _dragStartElementPositions)
            {
                el.X = startPos.X + snappedDelta.X;
                el.Y = startPos.Y + snappedDelta.Y;
                ElementMoved?.Invoke(el);
            }

            // 2. Rigid translation of internal and selected wires
            foreach (var (w, (startP, endP)) in _dragStartWireEndpoints)
            {
                w.StartX = startP.X + snappedDelta.X;
                w.StartY = startP.Y + snappedDelta.Y;
                w.EndX = endP.X + snappedDelta.X;
                w.EndY = endP.Y + snappedDelta.Y;
                if (_dragStartWireWaypoints.TryGetValue(w, out var initialWps))
                {
                    w.Waypoints = initialWps.Select(wp => wp + snappedDelta).ToList();
                }
            }

            // 3. Synchronize external wires connected to the moved elements
            var movingWires = _dragStartWireEndpoints.Keys.ToHashSet();
            var movedElementIds = _dragStartElementPositions.Keys.Select(el => el.Id).ToHashSet();
            SyncExternalConnectedWires(movedElementIds, movingWires);

            Canvas.Invalidate();
        }
        else if (_isBoxSelecting)
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
            _boxSelectCurrent = worldPos;

            float minX = MathF.Min(_boxSelectStart.X, _boxSelectCurrent.X);
            float maxX = MathF.Max(_boxSelectStart.X, _boxSelectCurrent.X);
            float minY = MathF.Min(_boxSelectStart.Y, _boxSelectCurrent.Y);
            float maxY = MathF.Max(_boxSelectStart.Y, _boxSelectCurrent.Y);
            var box = new Rect(minX, minY, MathF.Max(maxX - minX, 1f), MathF.Max(maxY - minY, 1f));

            int selectedCount = 0;
            CircuitElement? firstSelected = null;
            foreach (var el in Elements)
            {
                bool inBox = RectsIntersect(box, el.GetBounds());
                el.IsSelected = inBox;
                if (inBox)
                {
                    selectedCount++;
                    firstSelected ??= el;
                }
            }
            foreach (var w in Wires)
            {
                w.IsSelected = WireIntersectsBox(w, box);
            }

            SelectedElement = firstSelected;
            UpdateSelectionBar(selectedCount == 1 ? firstSelected : null);
            if (selectedCount > 1)
            {
                SelectionActionBar.Visibility = Visibility.Visible;
                SelectionBarText.Text = $"已选中 {selectedCount} 个元件";
                if (RotateButton != null) RotateButton.Visibility = Visibility.Visible;
            }

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
            // Hover detection: Pin vs WireTap vs Selected Item vs Wire vs Element vs Canvas
            _hoveredPin = FindPinAt(worldPos);
            _hoveredWireTap = FindWireTapAt(worldPos, 8.0f);

            if (_hoveredPin.HasValue || _hoveredWireTap.HasValue)
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
            }
            else
            {
                var hoverEl = Elements.FirstOrDefault(el => el.HitTest(worldPos));
                var hoverWire = FindWireAt(worldPos, out int hSeg, out bool hHoriz);

                if ((hoverEl != null && hoverEl.IsSelected) || (hoverWire != null && hoverWire.IsSelected))
                {
                    // Over any selected item: show SizeAll indicating the selection can be dragged!
                    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
                }
                else if (hoverWire != null)
                {
                    ProtectedCursor = hHoriz
                        ? InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth)
                        : InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
                }
                else if (hoverEl != null)
                {
                    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
                }
                else
                {
                    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
                }
            }
            Canvas.Invalidate();
        }
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingWire && _draggedWire != null)
        {
            var pts = _draggedWire.GetPathPoints();
            if (pts.Count > 2)
            {
                _draggedWire.Waypoints = pts.Skip(1).SkipLast(1).ToList();
            }
            else
            {
                _draggedWire.Waypoints.Clear();
            }
            SyncConnectedWiresToWire(_draggedWire);
        }

        _isDraggingWire = false;
        _draggedWire = null;
        _draggedSegmentIndex = -1;
        _dragWireBaselinePoints.Clear();

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            _dragStartElementPositions.Clear();
            _dragStartWireEndpoints.Clear();
            _dragStartWireWaypoints.Clear();
        }

        if (_isBoxSelecting)
        {
            _isBoxSelecting = false;
            float dist = Vector2.Distance(_boxSelectStart, _boxSelectCurrent);
            if (dist < 4f)
            {
                // Simple click in empty space: deselect all
                DeselectSelected();
            }
        }

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
        if (e.Key is VirtualKey.Space or VirtualKey.R)
        {
            RotateSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Z && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            UndoRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Y && Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            RedoRequested?.Invoke();
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Delete or VirtualKey.Back)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.X &&
                 Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            MirrorSelected();
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

    public void StartBranchFromWire(CircuitWire? wire)
    {
        if (wire == null) return;
        var pts = wire.GetPathPoints();
        Vector2 tapPos = (pts.Count >= 2) ? (pts[0] + pts[^1]) * 0.5f : new Vector2(wire.StartX, wire.StartY);
        _isDrawingWire = true;
        _wireStartElement = null;
        _wireStartPin = null;
        _wireStartParentWire = wire;
        _wireStartPos = tapPos;
        _currentWireEnd = _wireStartPos;
        StatusMessageChanged?.Invoke($"正在引出母线支路: 从 {wire.Id} 出发，点击目标引脚或导线完成连接");
        Canvas?.Invalidate();
    }

    public void RotateSelected()
    {
        var selectedEls = Elements.Where(e => e.IsSelected).ToList();
        if (selectedEls.Count == 0 && SelectedElement != null) selectedEls.Add(SelectedElement);

        if (selectedEls.Count > 0)
        {
            PushUndoRequested?.Invoke();
            foreach (var el in selectedEls)
            {
                el.RotationDeg = (el.RotationDeg + 90) % 360;
                el.Orientation = (el.RotationDeg % 180 == 0) ? "Horizontal" : "Vertical";
                CircuitWireSync.SyncConnectedWiresToElement(Wires, el);
            }
            UpdateSelectionBar(selectedEls.Count == 1 ? selectedEls[0] : null);
            if (selectedEls.Count > 1)
            {
                SelectionActionBar.Visibility = Visibility.Visible;
                SelectionBarText.Text = $"已选中 {selectedEls.Count} 个元件";
                if (RotateButton != null) RotateButton.Visibility = Visibility.Visible;
                if (MirrorButton != null) MirrorButton.Visibility = Visibility.Visible;
            }
            StatusMessageChanged?.Invoke($"已将选中的 {selectedEls.Count} 个元件顺时针旋转 90°");
            Canvas.Invalidate();
        }
    }

    /// <summary>
    /// Toggle horizontal mirror for every selected element (ALT+X). Wires and
    /// WireTap branch nodes are re-projected through the shared
    /// CircuitWireSync helper so solder dots follow the mirrored pins.
    /// </summary>
    public void MirrorSelected()
    {
        var selectedEls = Elements.Where(e => e.IsSelected).ToList();
        if (selectedEls.Count == 0 && SelectedElement != null) selectedEls.Add(SelectedElement);
        if (selectedEls.Count == 0) return;

        PushUndoRequested?.Invoke();
        // All-or-nothing for a multi-selection: if any selected element is still
        // normal, mirror the whole selection; otherwise unmirror it.
        bool targetMirrored = selectedEls.Any(el => !el.MirrorX);
        foreach (var el in selectedEls)
        {
            el.MirrorX = targetMirrored;
            CircuitWireSync.SyncConnectedWiresToElement(Wires, el);
        }

        UpdateSelectionBar(selectedEls.Count == 1 ? selectedEls[0] : null);
        if (selectedEls.Count > 1)
        {
            SelectionActionBar.Visibility = Visibility.Visible;
            SelectionBarText.Text = $"已选中 {selectedEls.Count} 个元件";
            if (RotateButton != null) RotateButton.Visibility = Visibility.Visible;
            if (MirrorButton != null) MirrorButton.Visibility = Visibility.Visible;
        }
        StatusMessageChanged?.Invoke(targetMirrored
            ? $"已将选中的 {selectedEls.Count} 个元件水平镜像（ALT+X 可恢复）"
            : $"已取消选中的 {selectedEls.Count} 个元件的水平镜像");
        Canvas.Invalidate();
    }

    public void DeleteSelected()
    {
        var selectedEls = Elements.Where(e => e.IsSelected).ToList();
        var selectedWires = Wires.Where(w => w.IsSelected).ToList();
        if (selectedEls.Count == 0 && SelectedElement != null) selectedEls.Add(SelectedElement);
        if (selectedWires.Count == 0 && SelectedWire != null) selectedWires.Add(SelectedWire);

        if (selectedEls.Count == 0 && selectedWires.Count == 0) return;

        PushUndoRequested?.Invoke();

        var deletedElIds = selectedEls.Select(e => e.Id).ToHashSet();
        var attachedWires = Wires.Where(w => deletedElIds.Contains(w.StartElementId) || deletedElIds.Contains(w.EndElementId)).ToList();
        foreach (var w in attachedWires)
        {
            Wires.Remove(w);
            selectedWires.Remove(w);
        }
        foreach (var w in selectedWires)
        {
            Wires.Remove(w);
        }
        CircuitWire.RemoveWithDescendants(Wires, attachedWires.Concat(selectedWires).Select(w => w.Id));
        foreach (var el in selectedEls)
        {
            Elements.Remove(el);
        }

        SelectedElement = null;
        SelectedWire = null;
        ElementSelected?.Invoke(null);
        WireSelected?.Invoke(null);
        UpdateSelectionBar(null);
        StatusMessageChanged?.Invoke($"已删除 {selectedEls.Count} 个元件和 {attachedWires.Count + selectedWires.Count} 根导线");
        Canvas.Invalidate();
    }

    public void DeselectSelected()
    {
        _isDrawingWire = false;
        _wireStartElement = null;
        _wireStartPin = null;
        _wireStartParentWire = null;

        SelectedElement = null;
        SelectedWire = null;
        foreach (var el in Elements) el.IsSelected = false;
        foreach (var w in Wires) w.IsSelected = false;

        ElementSelected?.Invoke(null);
        WireSelected?.Invoke(null);
        UpdateSelectionBar(null);
        Canvas.Invalidate();
    }

    private void SyncConnectedWiresToWire(CircuitWire movedWire)
    {
        foreach (var w in Wires)
        {
            if (w == movedWire) continue;
            if (w.EndPinName == "WireTap" && w.EndElementId == movedWire.Id)
            {
                var newTap = movedWire.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                w.EndX = newTap.X;
                w.EndY = newTap.Y;
            }
            else if (w.StartPinName == "WireTap" && w.StartElementId == movedWire.Id)
            {
                var newTap = movedWire.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                w.StartX = newTap.X;
                w.StartY = newTap.Y;
            }
        }
    }

    private CircuitWire? FindWireAt(Vector2 worldPos, out int hitSegment, out bool hitSegHoriz)
    {
        hitSegment = -1;
        hitSegHoriz = false;

        for (int i = Wires.Count - 1; i >= 0; i--)
        {
            var w = Wires[i];
            if (w.HitTestSegment(worldPos, out int s, out bool isH, 6f))
            {
                hitSegment = s;
                hitSegHoriz = isH;
                return w;
            }
            if (w.HitTest(worldPos, 8f))
            {
                var pts = w.GetPathPoints();
                float bestDist = float.MaxValue;
                for (int si = 0; si < pts.Count - 1; si++)
                {
                    float d = CircuitWire.DistanceToSegment(worldPos, pts[si], pts[si + 1]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        hitSegment = si;
                        hitSegHoriz = MathF.Abs(pts[si].Y - pts[si + 1].Y) < 1f;
                    }
                }
                return w;
            }
        }
        return null;
    }

    private void SyncExternalConnectedWires(System.Collections.Generic.HashSet<string> selectedElementIds, System.Collections.Generic.HashSet<CircuitWire>? movingWires = null)
    {
        var movedWireIds = new System.Collections.Generic.HashSet<string>();

        foreach (var w in Wires)
        {
            if (movingWires != null && movingWires.Contains(w)) continue;

            bool startIn = selectedElementIds.Contains(w.StartElementId);
            bool endIn = selectedElementIds.Contains(w.EndElementId);

            if (startIn && endIn) continue; // Internal wire handled rigidly
            if (!startIn && !endIn) continue;

            bool wireEndpointChanged = false;

            if (startIn)
            {
                var el = FindElement(w.StartElementId);
                if (el != null && w.StartPinName != "WireTap")
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.StartPinName || p.Id == w.StartPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.StartX = pPos.X;
                        w.StartY = pPos.Y;
                        wireEndpointChanged = true;
                    }
                }
                if (w.EndPinName == "WireTap")
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.EndElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.StartX, w.StartY));
                        w.EndX = newTap.X;
                        w.EndY = newTap.Y;
                        wireEndpointChanged = true;
                    }
                }
            }

            if (endIn)
            {
                var el = FindElement(w.EndElementId);
                if (el != null && w.EndPinName != "WireTap")
                {
                    var pin = el.Pins.FirstOrDefault(p => p.Name == w.EndPinName || p.Id == w.EndPinName);
                    if (pin != null)
                    {
                        var pPos = el.GetPinWorldPosition(pin);
                        w.EndX = pPos.X;
                        w.EndY = pPos.Y;
                        wireEndpointChanged = true;
                    }
                }
                if (w.StartPinName == "WireTap")
                {
                    var parent = Wires.FirstOrDefault(pw => pw.Id == w.StartElementId);
                    if (parent != null)
                    {
                        var newTap = parent.ProjectTapPerpendicular(new Vector2(w.EndX, w.EndY));
                        w.StartX = newTap.X;
                        w.StartY = newTap.Y;
                        wireEndpointChanged = true;
                    }
                }
            }

            if (wireEndpointChanged)
            {
                movedWireIds.Add(w.Id);
                if (w.Waypoints.Count > 0)
                {
                    if (w.Waypoints.Count <= 2)
                    {
                        w.Waypoints.Clear();
                    }
                    else
                    {
                        w.Waypoints = w.GetPathPoints().Skip(1).SkipLast(1).ToList();
                    }
                }
            }
        }

        // Propagate tap positions for child wires tapping into external wires that moved
        bool anyUpdated;
        int maxPasses = 3;
        do
        {
            anyUpdated = false;
            foreach (var w in Wires)
            {
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

    private static bool RectsIntersect(Rect a, Rect b)
    {
        return a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;
    }

    private static bool WireIntersectsBox(CircuitWire wire, Rect box)
    {
        var pts = wire.GetPathPoints();
        if (pts.Count == 0) return false;

        float minX = (float)box.Left;
        float maxX = (float)box.Right;
        float minY = (float)box.Top;
        float maxY = (float)box.Bottom;

        foreach (var p in pts)
        {
            if (p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY)
                return true;
        }

        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p1 = pts[i];
            var p2 = pts[i + 1];

            bool isH = MathF.Abs(p1.Y - p2.Y) < 1f;
            if (isH)
            {
                float segY = p1.Y;
                if (segY >= minY && segY <= maxY)
                {
                    float segMinX = MathF.Min(p1.X, p2.X);
                    float segMaxX = MathF.Max(p1.X, p2.X);
                    if (MathF.Max(minX, segMinX) <= MathF.Min(maxX, segMaxX))
                        return true;
                }
            }
            else
            {
                float segX = p1.X;
                if (segX >= minX && segX <= maxX)
                {
                    float segMinY = MathF.Min(p1.Y, p2.Y);
                    float segMaxY = MathF.Max(p1.Y, p2.Y);
                    if (MathF.Max(minY, segMinY) <= MathF.Min(maxY, segMaxY))
                        return true;
                }
            }
        }

        return false;
    }

    private (CircuitWire Wire, Vector2 SnapPos)? FindWireTapAt(Vector2 worldPos, float tolerance = 16.0f)
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
    private void MirrorSelected_Click(object sender, RoutedEventArgs e) => MirrorSelected();
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
