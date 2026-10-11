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
    Jfet,
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

    public Rect GetBounds()
    {
        return new Rect(X - Width / 2f, Y - Height / 2f, Width, Height);
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
