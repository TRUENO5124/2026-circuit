using System;
using System.Collections.ObjectModel;
using System.Numerics;
using Windows.Foundation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CircuitAssistant.Models;

public enum ElementType
{
    Resistor,
    Capacitor,
    Inductor,
    Transformer,
    BjtNpn,
    BjtPnp,
    Mosfet,
    MosfetNEnh,
    MosfetPEnh,
    MosfetNDep,
    MosfetPDep,
    Jfet,
    JfetN,
    JfetP,
    Diode,
    DcVoltageSource,
    DcCurrentSource,
    AcVoltageSource,
    AcCurrentSource,
    Vcvs,
    Vccs,
    Ccvs,
    Cccs,
    Ground
}

public partial class CircuitPin : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private float _relativeX;

    [ObservableProperty]
    private float _relativeY;

    [ObservableProperty]
    private int _connectedNetId = -1;
}

public partial class CircuitWire : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _startElementId = string.Empty;

    [ObservableProperty]
    private string _startPinName = string.Empty;

    [ObservableProperty]
    private string _endElementId = string.Empty;

    [ObservableProperty]
    private string _endPinName = string.Empty;

    [ObservableProperty]
    private float _startX;

    [ObservableProperty]
    private float _startY;

    [ObservableProperty]
    private float _endX;

    [ObservableProperty]
    private float _endY;

    [ObservableProperty]
    private int _netId;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _flipCorner;

    [ObservableProperty]
    private float _customMid;

    public System.Collections.Generic.List<Vector2> GetPathPoints()
    {
        var pts = new System.Collections.Generic.List<Vector2>();
        var p1 = new Vector2(StartX, StartY);
        var p2 = new Vector2(EndX, EndY);
        pts.Add(p1);

        if (Math.Abs(p1.X - p2.X) > 1.0f && Math.Abs(p1.Y - p2.Y) > 1.0f)
        {
            if (CustomMid != 0)
            {
                // Custom draggable bend
                if (FlipCorner)
                {
                    pts.Add(new Vector2(p1.X, CustomMid));
                    pts.Add(new Vector2(p2.X, CustomMid));
                }
                else
                {
                    pts.Add(new Vector2(CustomMid, p1.Y));
                    pts.Add(new Vector2(CustomMid, p2.Y));
                }
            }
            else
            {
                if (FlipCorner)
                {
                    pts.Add(new Vector2(p1.X, p2.Y));
                }
                else
                {
                    pts.Add(new Vector2(p2.X, p1.Y));
                }
            }
        }

        pts.Add(p2);
        return pts;
    }

    public int HitTestCorner(Vector2 point, float tolerance = 8f)
    {
        var pts = GetPathPoints();
        for (int i = 1; i < pts.Count - 1; i++)
        {
            if (Vector2.Distance(point, pts[i]) <= tolerance)
            {
                return i;
            }
        }
        return -1;
    }

    public int HitTestSegment(Vector2 point, float tolerance = 6f)
    {
        var pts = GetPathPoints();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (DistanceToSegment(point, pts[i], pts[i + 1]) <= tolerance)
            {
                return i;
            }
        }
        return -1;
    }

    public bool HitTestSegment(Vector2 point, out int segmentIndex, out bool isHorizontal, float tolerance = 6f)
    {
        var pts = GetPathPoints();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (DistanceToSegment(point, pts[i], pts[i + 1]) <= tolerance)
            {
                segmentIndex = i;
                isHorizontal = MathF.Abs(pts[i].Y - pts[i + 1].Y) < 1f;
                return true;
            }
        }
        segmentIndex = -1;
        isHorizontal = false;
        return false;
    }

    public bool HitTest(Vector2 worldPos, float tolerance = 10f)
    {
        var pts = GetPathPoints();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (DistanceToSegment(worldPos, pts[i], pts[i + 1]) <= tolerance)
            {
                return true;
            }
        }
        return false;
    }

    public System.Collections.Generic.List<(Vector2 Start, Vector2 End)> GetSegments()
    {
        var segs = new System.Collections.Generic.List<(Vector2, Vector2)>();
        var pts = GetPathPoints();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            segs.Add((pts[i], pts[i + 1]));
        }
        return segs;
    }

    public Vector2 ProjectTapPerpendicular(Vector2 branchPoint)
    {
        var segments = GetSegments();
        if (segments.Count == 0) return branchPoint;

        float bestDistSq = float.MaxValue;
        Vector2 bestProj = branchPoint;

        foreach (var (a, b) in segments)
        {
            bool isHorizontal = MathF.Abs(a.Y - b.Y) < 1e-3f;
            bool isVertical = MathF.Abs(a.X - b.X) < 1e-3f;

            Vector2 proj;
            if (isHorizontal)
            {
                float minX = MathF.Min(a.X, b.X);
                float maxX = MathF.Max(a.X, b.X);
                float snappedX = MathF.Round(branchPoint.X / 10f) * 10f;
                float clampedX = Math.Clamp(snappedX, minX, maxX);
                proj = new Vector2(clampedX, a.Y);
            }
            else if (isVertical)
            {
                float minY = MathF.Min(a.Y, b.Y);
                float maxY = MathF.Max(a.Y, b.Y);
                float snappedY = MathF.Round(branchPoint.Y / 10f) * 10f;
                float clampedY = Math.Clamp(snappedY, minY, maxY);
                proj = new Vector2(a.X, clampedY);
            }
            else
            {
                var ab = b - a;
                float lenSq = ab.LengthSquared();
                if (lenSq < 1e-4f) continue;
                float t = Math.Clamp(Vector2.Dot(branchPoint - a, ab) / lenSq, 0f, 1f);
                proj = a + t * ab;
            }

            float distSq = Vector2.DistanceSquared(branchPoint, proj);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestProj = proj;
            }
        }

        return bestProj;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lenSq = ab.LengthSquared();
        if (lenSq < 1e-4f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lenSq, 0f, 1f);
        var proj = a + t * ab;
        return Vector2.Distance(p, proj);
    }
}

public partial class CircuitElement : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _definitionId = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private ElementType _type;

    [ObservableProperty]
    private double _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedValueDisplay))]
    private double _displayNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedValueDisplay))]
    private string _unitSymbol = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    private float _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionDisplay))]
    private float _y;

    [ObservableProperty]
    private float _width = 80;

    [ObservableProperty]
    private float _height = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationDisplay))]
    private int _rotationDeg;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationDisplay))]
    private string _orientation = "Horizontal";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<CircuitPin> _pins = [];

    public string FormattedValueDisplay => string.IsNullOrWhiteSpace(UnitSymbol) ? $"{DisplayNumber}" : $"{DisplayNumber} {UnitSymbol}";
    public string PositionDisplay => $"坐标: ({(int)X}, {(int)Y})";
    public string RotationDisplay => $"{RotationDeg}° ({Orientation})";

    public UnitOption[] AvailableUnits => GetUnitsForType(Type);

    public static UnitOption[] GetUnitsForType(ElementType type) => type switch
    {
        ElementType.Resistor => EngineeringUnits.ResistanceUnits,
        ElementType.Capacitor => EngineeringUnits.CapacitanceUnits,
        ElementType.Inductor => EngineeringUnits.InductanceUnits,
        ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.Vcvs or ElementType.Ccvs => EngineeringUnits.VoltageUnits,
        ElementType.DcCurrentSource or ElementType.AcCurrentSource or ElementType.Vccs or ElementType.Cccs => EngineeringUnits.CurrentUnits,
        _ => []
    };

    public Rect GetBounds()
    {
        return new Rect(X - Width / 2f, Y - Height / 2f, Width, Height);
    }

    public Vector2 GetPinWorldPosition(CircuitPin pin)
    {
        float rad = (float)(RotationDeg * Math.PI / 180.0);
        var rel = new Vector2(pin.RelativeX, pin.RelativeY);
        var rotRel = Vector2.Transform(rel, Matrix3x2.CreateRotation(rad));
        return new Vector2(X, Y) + rotRel;
    }

    public Vector2 GetPinWorldDirection(CircuitPin pin)
    {
        float rad = (float)(RotationDeg * Math.PI / 180.0);
        var rel = new Vector2(pin.RelativeX, pin.RelativeY);
        var rotRel = Vector2.Transform(rel, Matrix3x2.CreateRotation(rad));
        if (rotRel.LengthSquared() < 1e-4f) return new Vector2(1, 0);
        return Vector2.Normalize(rotRel);
    }

    public void InitializeDefaultPins()
    {
        Pins.Clear();
        switch (Type)
        {
            case ElementType.Resistor:
                Pins.Add(new CircuitPin { Id = "1", Name = "1", RelativeX = -40, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "2", Name = "2", RelativeX = 40, RelativeY = 0 });
                break;

            case ElementType.Capacitor:
                Pins.Add(new CircuitPin { Id = "1", Name = "1", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "2", Name = "2", RelativeX = 35, RelativeY = 0 });
                break;

            case ElementType.Inductor:
                Pins.Add(new CircuitPin { Id = "1", Name = "1", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "2", Name = "2", RelativeX = 35, RelativeY = 0 });
                break;

            case ElementType.Diode:
                Pins.Add(new CircuitPin { Id = "A", Name = "A", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "K", Name = "K", RelativeX = 35, RelativeY = 0 });
                break;

            case ElementType.DcVoltageSource:
            case ElementType.AcVoltageSource:
                Pins.Add(new CircuitPin { Id = "pos", Name = "pos", RelativeX = 0, RelativeY = -35 });
                Pins.Add(new CircuitPin { Id = "neg", Name = "neg", RelativeX = 0, RelativeY = 35 });
                break;

            case ElementType.DcCurrentSource:
            case ElementType.AcCurrentSource:
                Pins.Add(new CircuitPin { Id = "in", Name = "in", RelativeX = 0, RelativeY = -35 });
                Pins.Add(new CircuitPin { Id = "out", Name = "out", RelativeX = 0, RelativeY = 35 });
                break;

            case ElementType.BjtNpn:
            case ElementType.BjtPnp:
                Pins.Add(new CircuitPin { Id = "B", Name = "B", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "C", Name = "C", RelativeX = 20, RelativeY = -35 });
                Pins.Add(new CircuitPin { Id = "E", Name = "E", RelativeX = 20, RelativeY = 35 });
                break;

            case ElementType.Mosfet:
            case ElementType.MosfetNEnh:
            case ElementType.MosfetPEnh:
            case ElementType.MosfetNDep:
            case ElementType.MosfetPDep:
            case ElementType.Jfet:
            case ElementType.JfetN:
            case ElementType.JfetP:
                Pins.Add(new CircuitPin { Id = "G", Name = "G", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "D", Name = "D", RelativeX = 20, RelativeY = -35 });
                Pins.Add(new CircuitPin { Id = "S", Name = "S", RelativeX = 20, RelativeY = 35 });
                break;

            case ElementType.Ground:
                Pins.Add(new CircuitPin { Id = "GND", Name = "GND", RelativeX = 0, RelativeY = -22 });
                break;

            case ElementType.Transformer:
                Pins.Add(new CircuitPin { Id = "P1", Name = "P1", RelativeX = -35, RelativeY = -15 });
                Pins.Add(new CircuitPin { Id = "P2", Name = "P2", RelativeX = -35, RelativeY = 15 });
                Pins.Add(new CircuitPin { Id = "S1", Name = "S1", RelativeX = 35, RelativeY = -15 });
                Pins.Add(new CircuitPin { Id = "S2", Name = "S2", RelativeX = 35, RelativeY = 15 });
                break;

            case ElementType.Vcvs:
            case ElementType.Vccs:
            case ElementType.Ccvs:
            case ElementType.Cccs:
                Pins.Add(new CircuitPin { Id = "IN1", Name = "IN1", RelativeX = -35, RelativeY = -15 });
                Pins.Add(new CircuitPin { Id = "IN2", Name = "IN2", RelativeX = -35, RelativeY = 15 });
                Pins.Add(new CircuitPin { Id = "OUT1", Name = "OUT1", RelativeX = 35, RelativeY = -15 });
                Pins.Add(new CircuitPin { Id = "OUT2", Name = "OUT2", RelativeX = 35, RelativeY = 15 });
                break;

            default:
                Pins.Add(new CircuitPin { Id = "1", Name = "1", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "2", Name = "2", RelativeX = 35, RelativeY = 0 });
                break;
        }
    }

    public bool HitTest(Vector2 worldPos, float padding = 10f)
    {
        if (RotationDeg != 0)
        {
            float rad = (float)(-RotationDeg * Math.PI / 180.0);
            var rel = worldPos - new Vector2(X, Y);
            var local = Vector2.Transform(rel, Matrix3x2.CreateRotation(rad));
            return Math.Abs(local.X) <= (Width / 2f + padding) && Math.Abs(local.Y) <= (Height / 2f + padding);
        }

        float left = X - Width / 2f - padding;
        float right = X + Width / 2f + padding;
        float top = Y - Height / 2f - padding;
        float bottom = Y + Height / 2f + padding;

        return worldPos.X >= left && worldPos.X <= right && worldPos.Y >= top && worldPos.Y <= bottom;
    }
}
