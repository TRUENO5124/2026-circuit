using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using Windows.Foundation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CircuitAssistant.Models;

public enum ElementType
{
    Resistor,
    Capacitor,
    CapacitorPolarized,
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
    Ground,
    VccPort
}

/// <summary>
/// Semiconductor material selection. The forward voltage drop is a teaching
/// approximation: silicon ≈ 0.7 V, germanium ≈ 0.3 V (diode VF and BJT |VBE|).
/// </summary>
public enum SemiconductorMaterial
{
    Silicon = 0,
    Germanium = 1
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

    public CircuitPin Clone()
    {
        return new CircuitPin
        {
            Id = Id,
            Name = Name,
            RelativeX = RelativeX,
            RelativeY = RelativeY,
            ConnectedNetId = ConnectedNetId
        };
    }
}

public partial class CircuitWire : ObservableObject
{
    public static void RemoveWithDescendants(System.Collections.Generic.ICollection<CircuitWire> wires,
        System.Collections.Generic.IEnumerable<string> ids)
    {
        var removed = ids.ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var wire in wires)
                if (((wire.StartPinName == "WireTap" && removed.Contains(wire.StartElementId)) ||
                     (wire.EndPinName == "WireTap" && removed.Contains(wire.EndElementId))) && removed.Add(wire.Id))
                    changed = true;
        } while (changed);
        foreach (var wire in wires.Where(w => removed.Contains(w.Id)).ToList()) wires.Remove(wire);
    }

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

    [ObservableProperty]
    private System.Collections.Generic.List<Vector2> _waypoints = new();

    public CircuitWire Clone()
    {
        return new CircuitWire
        {
            Id = Id,
            StartElementId = StartElementId,
            StartPinName = StartPinName,
            EndElementId = EndElementId,
            EndPinName = EndPinName,
            StartX = StartX,
            StartY = StartY,
            EndX = EndX,
            EndY = EndY,
            NetId = NetId,
            IsSelected = IsSelected,
            FlipCorner = FlipCorner,
            CustomMid = CustomMid,
            Waypoints = new System.Collections.Generic.List<Vector2>(Waypoints)
        };
    }

    public System.Collections.Generic.List<Vector2> GetPathPoints()
    {
        var p1 = new Vector2(StartX, StartY);
        var p2 = new Vector2(EndX, EndY);

        if (Waypoints != null && Waypoints.Count > 0)
        {
            var pts = new System.Collections.Generic.List<Vector2>();
            pts.Add(p1);
            foreach (var wp in Waypoints)
            {
                pts.Add(wp);
            }
            pts.Add(p2);
            return EnforceOrthogonalPoints(pts, FlipCorner);
        }

        var defaultPts = new System.Collections.Generic.List<Vector2>();
        defaultPts.Add(p1);

        if (Math.Abs(p1.X - p2.X) > 1.0f && Math.Abs(p1.Y - p2.Y) > 1.0f)
        {
            if (CustomMid != 0)
            {
                if (FlipCorner)
                {
                    defaultPts.Add(new Vector2(p1.X, CustomMid));
                    defaultPts.Add(new Vector2(p2.X, CustomMid));
                }
                else
                {
                    defaultPts.Add(new Vector2(CustomMid, p1.Y));
                    defaultPts.Add(new Vector2(CustomMid, p2.Y));
                }
            }
            else
            {
                if (FlipCorner)
                {
                    defaultPts.Add(new Vector2(p1.X, p2.Y));
                }
                else
                {
                    defaultPts.Add(new Vector2(p2.X, p1.Y));
                }
            }
        }

        defaultPts.Add(p2);
        return SimplifyPathPoints(defaultPts);
    }

    public static System.Collections.Generic.List<Vector2> EnforceOrthogonalPoints(System.Collections.Generic.List<Vector2> points, bool flipCorner = false)
    {
        if (points == null || points.Count < 2) return points ?? new System.Collections.Generic.List<Vector2>();
        var result = new System.Collections.Generic.List<Vector2> { points[0] };

        for (int i = 0; i < points.Count - 1; i++)
        {
            var a = result[^1];
            var b = points[i + 1];

            // If a and b are not orthogonal (both dx and dy > 0.5f), insert a right-angle corner
            if (MathF.Abs(a.X - b.X) > 0.5f && MathF.Abs(a.Y - b.Y) > 0.5f)
            {
                var corner = flipCorner ? new Vector2(a.X, b.Y) : new Vector2(b.X, a.Y);
                result.Add(corner);
            }
            result.Add(b);
        }

        return SimplifyPathPoints(result);
    }

    public static System.Collections.Generic.List<Vector2> SimplifyPathPoints(System.Collections.Generic.List<Vector2> raw)
    {
        if (raw == null || raw.Count <= 2) return raw ?? new System.Collections.Generic.List<Vector2>();
        var simplified = new System.Collections.Generic.List<Vector2>();
        simplified.Add(raw[0]);

        for (int i = 1; i < raw.Count; i++)
        {
            var prev = simplified[^1];
            var curr = raw[i];
            if (Vector2.Distance(prev, curr) < 0.5f) continue;
            simplified.Add(curr);
        }

        int idx = 1;
        while (idx < simplified.Count - 1)
        {
            var pPrev = simplified[idx - 1];
            var pCurr = simplified[idx];
            var pNext = simplified[idx + 1];

            bool colHoriz = MathF.Abs(pPrev.Y - pCurr.Y) < 0.5f && MathF.Abs(pCurr.Y - pNext.Y) < 0.5f;
            bool colVert = MathF.Abs(pPrev.X - pCurr.X) < 0.5f && MathF.Abs(pCurr.X - pNext.X) < 0.5f;

            if (colHoriz || colVert)
            {
                simplified.RemoveAt(idx);
            }
            else
            {
                idx++;
            }
        }

        return simplified;
    }

    public void MoveSegmentFromBaseline(System.Collections.Generic.List<Vector2> baseline, int segmentIndex, bool isHorizontal, float newOffset)
    {
        if (baseline == null || segmentIndex < 0 || segmentIndex >= baseline.Count - 1) return;

        var pts = new System.Collections.Generic.List<Vector2>(baseline);
        int pA = segmentIndex;
        int pB = segmentIndex + 1;

        if (isHorizontal)
        {
            if (pA == 0)
            {
                if (MathF.Abs(newOffset - StartY) > 0.5f)
                {
                    pts.Insert(1, new Vector2(baseline[0].X, newOffset));
                    pB++;
                }
            }
            else
            {
                pts[pA] = new Vector2(pts[pA].X, newOffset);
            }

            if (pB == pts.Count - 1)
            {
                if (MathF.Abs(newOffset - EndY) > 0.5f)
                {
                    pts.Insert(pB, new Vector2(baseline[^1].X, newOffset));
                }
            }
            else
            {
                pts[pB] = new Vector2(pts[pB].X, newOffset);
            }
        }
        else
        {
            if (pA == 0)
            {
                if (MathF.Abs(newOffset - StartX) > 0.5f)
                {
                    pts.Insert(1, new Vector2(newOffset, baseline[0].Y));
                    pB++;
                }
            }
            else
            {
                pts[pA] = new Vector2(newOffset, pts[pA].Y);
            }

            if (pB == pts.Count - 1)
            {
                if (MathF.Abs(newOffset - EndX) > 0.5f)
                {
                    pts.Insert(pB, new Vector2(newOffset, baseline[^1].Y));
                }
            }
            else
            {
                pts[pB] = new Vector2(newOffset, pts[pB].Y);
            }
        }

        var orthogonal = EnforceOrthogonalPoints(pts, FlipCorner);
        var newWaypoints = new System.Collections.Generic.List<Vector2>();
        for (int i = 1; i < orthogonal.Count - 1; i++)
        {
            newWaypoints.Add(orthogonal[i]);
        }
        Waypoints = newWaypoints;
    }

    public void MoveSegment(int segmentIndex, bool isHorizontal, float newOffset)
    {
        MoveSegmentFromBaseline(GetPathPoints(), segmentIndex, isHorizontal, newOffset);
    }

    public void MoveCorner(int cornerIndex, Vector2 newPos)
    {
        var pts = GetPathPoints();
        if (cornerIndex <= 0 || cornerIndex >= pts.Count - 1) return;

        var newPts = new System.Collections.Generic.List<Vector2>(pts);
        var prev = newPts[cornerIndex - 1];
        var next = newPts[cornerIndex + 1];

        bool prevIsHoriz = MathF.Abs(prev.Y - pts[cornerIndex].Y) < 1f;
        if (prevIsHoriz)
        {
            newPts[cornerIndex - 1] = new Vector2(prev.X, newPos.Y);
            newPts[cornerIndex] = newPos;
            newPts[cornerIndex + 1] = new Vector2(newPos.X, next.Y);
        }
        else
        {
            newPts[cornerIndex - 1] = new Vector2(newPos.X, prev.Y);
            newPts[cornerIndex] = newPos;
            newPts[cornerIndex + 1] = new Vector2(next.X, newPos.Y);
        }

        var simplified = SimplifyPathPoints(newPts);
        var newWaypoints = new System.Collections.Generic.List<Vector2>();
        for (int i = 1; i < simplified.Count - 1; i++)
        {
            newWaypoints.Add(simplified[i]);
        }
        Waypoints = newWaypoints;
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

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
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
    [NotifyPropertyChangedFor(nameof(IsAcSource))]
    [NotifyPropertyChangedFor(nameof(IsMosfet))]
    [NotifyPropertyChangedFor(nameof(IsJfet))]
    [NotifyPropertyChangedFor(nameof(SupportsFetParameters))]
    [NotifyPropertyChangedFor(nameof(SupportsSemiconductorMaterial))]
    [NotifyPropertyChangedFor(nameof(ParameterLabel))]
    [NotifyPropertyChangedFor(nameof(ParameterDescription))]
    [NotifyPropertyChangedFor(nameof(HasParameterDescription))]
    [NotifyPropertyChangedFor(nameof(FormattedParameterDisplay))]
    private ElementType _type;

    [ObservableProperty]
    private double _value;

    // AC amplitude stays in DisplayNumber/UnitSymbol; frequency is independent.
    [ObservableProperty]
    private double _frequencyHz = 1000.0;

    public bool IsAcSource => Type is ElementType.AcVoltageSource or ElementType.AcCurrentSource;

    /// <summary>True for every MOSFET variant (generic Mosfet included).</summary>
    public bool IsMosfet => Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh
        or ElementType.MosfetNDep or ElementType.MosfetPDep;

    /// <summary>True for N-channel MOSFETs (generic Mosfet is treated as N enhancement).</summary>
    public bool IsNMos => Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetNDep;

    /// <summary>True for enhancement-mode MOSFETs.</summary>
    public bool IsEnhancementMosfet => Type is ElementType.Mosfet or ElementType.MosfetNEnh or ElementType.MosfetPEnh;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedValueDisplay))]
    [NotifyPropertyChangedFor(nameof(FormattedParameterDisplay))]
    private double _displayNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedValueDisplay))]
    [NotifyPropertyChangedFor(nameof(FormattedParameterDisplay))]
    private string _unitSymbol = string.Empty;

    /// <summary>
    /// Semiconductor material for diodes and BJTs. Silicon uses a ~0.7 V
    /// forward drop, germanium a ~0.3 V forward drop.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaterialDisplayName))]
    [NotifyPropertyChangedFor(nameof(MaterialIndex))]
    [NotifyPropertyChangedFor(nameof(ForwardVoltageDrop))]
    [NotifyPropertyChangedFor(nameof(FormattedParameterDisplay))]
    private SemiconductorMaterial _material = SemiconductorMaterial.Silicon;

    /// <summary>
    /// MOSFET square-law transconductance parameter K = ½·μn·Cox·(W/L) in A/V².
    /// The editable <see cref="DisplayNumber"/> stays the threshold voltage
    /// (Vth / VGS(off)); K is the second device parameter required by the native
    /// square-law model. Older project files omit it, so the default 1 mA/V² is
    /// used for backward compatibility.
    /// </summary>
    [ObservableProperty]
    private double _mosfetK = 1e-3;

    /// <summary>
    /// MOSFET channel-length modulation coefficient λ in 1/V. 0 means the
    /// teaching square-law model ignores channel-length modulation (ro → ∞).
    /// </summary>
    [ObservableProperty]
    private double _mosfetLambda;

    /// <summary>JFET zero-gate-bias saturation current in A. 0 means not supplied.</summary>
    [ObservableProperty]
    private double _jfetIdss;

    public bool IsJfet => Type is ElementType.Jfet or ElementType.JfetN or ElementType.JfetP;
    public bool SupportsFetParameters => IsMosfet || IsJfet;

    /// <summary>
    /// BJT base spreading resistance r_bb' in Ω used by the low-frequency hybrid
    /// model r_be = r_bb' + (1+β)·U_T/I_EQ. 0 keeps the teaching default (200 Ω for
    /// common emitter / common collector, 50 Ω for common base) so older circuits
    /// and textbook templates keep their previous numbers; a positive value is an
    /// explicit problem parameter chosen in the inspector.
    /// </summary>
    [ObservableProperty]
    private double _bjtRbb;

    /// <summary>
    /// Optional directly-given small-signal input resistance r_be (identical to the
    /// hybrid parameter h_ie) in Ω. 0 means "compute from the Q point with
    /// r_bb' + (1+β)·U_T/I_EQ"; a positive value overrides that formula (many
    /// textbook problems state h_ie or r_be directly).
    /// </summary>
    [ObservableProperty]
    private double _bjtRbe;

    partial void OnBjtRbbChanged(double value)
    {
        // Empty NumberBox yields NaN / negative input is invalid: treat both as "auto".
        if (!double.IsFinite(value) || value < 0) BjtRbb = 0;
    }

    partial void OnBjtRbeChanged(double value)
    {
        if (!double.IsFinite(value) || value < 0) BjtRbe = 0;
    }

    /// <summary>True for NPN/PNP transistors (the r_bb'/r_be/h_ie parameters apply).</summary>
    public bool SupportsBjtSmallSignalParameters => Type is ElementType.BjtNpn or ElementType.BjtPnp;

    /// <summary>
    /// Display text for the editable r_bb' value: "自动" when 0 (teaching default),
    /// otherwise the explicit resistance.
    /// </summary>
    public string BjtRbbDisplay => BjtRbb > 0
        ? $"{BjtRbb:0.##} Ω"
        : "自动（共射/共集 200 Ω，共基 50 Ω）";

    /// <summary>
    /// Display text for the editable r_be/h_ie value: "按 Q 点计算" when 0,
    /// otherwise the directly-given resistance.
    /// </summary>
    public string BjtRbeDisplay => BjtRbe > 0
        ? $"{BjtRbe:0.##} Ω（题目给定）"
        : "按 Q 点计算（r_be=r_bb'+(1+β)U_T/I_EQ）";

    public static readonly string[] SemiconductorMaterialOptions = { "硅管 (≈0.7 V)", "锗管 (≈0.3 V)" };

    /// <summary>Material choices for the inspector combo box.</summary>
    public string[] MaterialOptions => SemiconductorMaterialOptions;

    public string MaterialDisplayName => Material == SemiconductorMaterial.Germanium ? "锗管 (≈0.3 V)" : "硅管 (≈0.7 V)";

    public int MaterialIndex
    {
        get => (int)Material;
        set => Material = value == 1 ? SemiconductorMaterial.Germanium : SemiconductorMaterial.Silicon;
    }

    public bool SupportsSemiconductorMaterial =>
        Type is ElementType.Diode or ElementType.BjtNpn or ElementType.BjtPnp;

    /// <summary>Silicon ≈ 0.7 V, germanium ≈ 0.3 V (diode VF or BJT |VBE|).</summary>
    public double ForwardVoltageDrop => Material == SemiconductorMaterial.Germanium ? 0.3 : 0.7;

    /// <summary>
    /// Human-readable meaning of DisplayNumber for the selected element type.
    /// The inspector and canvas labels use this so MOSFET's value is explicitly
    /// labelled as the threshold voltage instead of a bare voltage number.
    /// </summary>
    public string ParameterLabel => Type switch
    {
        ElementType.Resistor => "电阻值 R:",
        ElementType.Capacitor or ElementType.CapacitorPolarized => "电容值 C:",
        ElementType.Inductor => "电感量 L:",
        ElementType.Transformer => "变比 n:",
        ElementType.BjtNpn or ElementType.BjtPnp => "电流放大系数 β:",
        ElementType.MosfetNEnh or ElementType.MosfetPEnh => "阈值电压 Vth:",
        ElementType.MosfetNDep or ElementType.MosfetPDep => "夹断电压 VGS(off):",
        ElementType.JfetN or ElementType.JfetP => "夹断电压 VGS(off):",
        ElementType.Mosfet or ElementType.Jfet => "阈值/夹断电压:",
        ElementType.Diode => "正向压降 VF:",
        ElementType.DcVoltageSource or ElementType.AcVoltageSource => "电压值/幅值 U:",
        ElementType.DcCurrentSource or ElementType.AcCurrentSource => "电流值/幅值 I:",
        ElementType.Vcvs => "电压增益 μ:",
        ElementType.Vccs => "跨导 gm:",
        ElementType.Ccvs => "互阻 rm:",
        ElementType.Cccs => "电流增益 β:",
        ElementType.Ground => "参考电位:",
        ElementType.VccPort => "电源标称电压:",
        _ => "元件数值:"
    };

    /// <summary>
    /// Clarifies the physical meaning (and units) of the editable value. This is
    /// shown under the value editor and exposed as a tooltip.
    /// </summary>
    public string ParameterDescription => Type switch
    {
        ElementType.BjtNpn or ElementType.BjtPnp =>
            "β 为共射电流放大系数；硅管 VBE≈0.7 V、锗管 VBE≈0.3 V，可在下方选择材料。",
        ElementType.MosfetNEnh or ElementType.MosfetPEnh =>
            "Vth 为增强型 MOSFET 的阈值电压（|VGS| 超过该值后导通），不是漏源电压或击穿电压；方形律电流模型另需跨导参数 K（默认 1 mA/V²，可随工程文件保存）。",
        ElementType.MosfetNDep or ElementType.MosfetPDep =>
            "VGS(off) 为耗尽型 MOSFET 的夹断电压（栅源电压达到该值时沟道夹断）；方形律电流模型另需跨导参数 K（默认 1 mA/V²，可随工程文件保存）。",
        ElementType.JfetN or ElementType.JfetP =>
            "VGS(off) 为结型场效应管的截止电压，N 沟道为负、P 沟道为正；还需在下方填写 IDSS（A），求解使用 Shockley 方程并校验栅结反偏。",
        ElementType.Mosfet or ElementType.Jfet =>
            "该符号的数值表示阈值/夹断电压，具体含义由所选元件类型决定。",
        ElementType.Diode =>
            "VF 为正向导通压降；硅管≈0.7 V、锗管≈0.3 V，可在下方选择材料。",
        ElementType.Vcvs => "μ 为电压控制电压源的开路电压增益（V/V，无量纲）。",
        ElementType.Vccs => "gm 为电压控制电流源的跨导（A/V，单位 S/mS）。",
        ElementType.Ccvs => "rm 为电流控制电压源的互阻（V/A，单位 Ω）。",
        ElementType.Cccs => "β 为电流控制电流源的电流增益（A/A，无量纲）。",
        _ => string.Empty
    };

    public bool HasParameterDescription => !string.IsNullOrWhiteSpace(ParameterDescription);

    partial void OnMaterialChanged(SemiconductorMaterial value)
    {
        // For a diode the editable value *is* the forward drop, so switching the
        // material keeps the numeric field and unit in sync (silicon 0.7 V,
        // germanium 0.3 V). For a BJT the value is beta and only VBE changes.
        if (Type == ElementType.Diode)
        {
            DisplayNumber = value == SemiconductorMaterial.Germanium ? 0.3 : 0.7;
            UnitSymbol = "V";
        }
    }

    /// <summary>
    /// Parameter display used on the canvas/labels: includes the physical symbol
    /// (β, Vth, VGS(off), VF) so a bare "1.5 V" is not mistaken for a rail voltage.
    /// </summary>
    public string FormattedParameterDisplay => Type switch
    {
        ElementType.BjtNpn or ElementType.BjtPnp => $"β = {DisplayNumber:g}",
        ElementType.MosfetNEnh or ElementType.MosfetPEnh => $"Vth = {DisplayNumber:g} {UnitSymbol}".TrimEnd(),
        ElementType.MosfetNDep or ElementType.MosfetPDep => $"VGS(off) = {DisplayNumber:g} {UnitSymbol}".TrimEnd(),
        ElementType.JfetN or ElementType.JfetP => $"VGS(off) = {DisplayNumber:g} {UnitSymbol}".TrimEnd(),
        ElementType.Mosfet or ElementType.Jfet => $"Vth/VGS(off) = {DisplayNumber:g} {UnitSymbol}".TrimEnd(),
        ElementType.Diode => $"VF = {DisplayNumber:g} {UnitSymbol}".TrimEnd(),
        _ => FormattedValueDisplay
    };

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
    [NotifyPropertyChangedFor(nameof(TransformDisplay))]
    private int _rotationDeg;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationDisplay))]
    [NotifyPropertyChangedFor(nameof(TransformDisplay))]
    private string _orientation = "Horizontal";

    /// <summary>
    /// Horizontal mirror (ALT+X): flips the symbol and its pins about the vertical
    /// axis through the element centre before rotation is applied. Electrical pin
    /// names stay unchanged; only their world positions are transformed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransformDisplay))]
    private bool _mirrorX;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<CircuitPin> _pins = [];

    public string FormattedValueDisplay => string.IsNullOrWhiteSpace(UnitSymbol) ? $"{DisplayNumber}" : $"{DisplayNumber} {UnitSymbol}";
    public string PositionDisplay => $"坐标: ({(int)X}, {(int)Y})";
    public string RotationDisplay => $"{RotationDeg}° ({Orientation})";

    /// <summary>Rotation + mirror state shown in the canvas card and inspector.</summary>
    public string TransformDisplay => $"{RotationDeg}° ({(MirrorX ? "水平镜像" : Orientation)})";

    public UnitOption[] AvailableUnits => GetUnitsForType(Type);

    public static UnitOption[] GetUnitsForType(ElementType type) => type switch
    {
        ElementType.Resistor => EngineeringUnits.ResistanceUnits,
        ElementType.Capacitor or ElementType.CapacitorPolarized => EngineeringUnits.CapacitanceUnits,
        ElementType.Inductor => EngineeringUnits.InductanceUnits,
        ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.Vcvs or ElementType.Ccvs or ElementType.VccPort => EngineeringUnits.VoltageUnits,
        ElementType.DcCurrentSource or ElementType.AcCurrentSource or ElementType.Vccs or ElementType.Cccs => EngineeringUnits.CurrentUnits,
        _ => []
    };

    public CircuitElement Clone()
    {
        var clone = new CircuitElement
        {
            Id = Id,
            Name = Name,
            DefinitionId = DefinitionId,
            Category = Category,
            Description = Description,
            Type = Type,
            Material = Material,
            MosfetK = MosfetK,
            MosfetLambda = MosfetLambda,
            JfetIdss = JfetIdss,
            Value = Value,
            FrequencyHz = FrequencyHz,
            DisplayNumber = DisplayNumber,
            UnitSymbol = UnitSymbol,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            RotationDeg = RotationDeg,
            Orientation = Orientation,
            MirrorX = MirrorX,
            IsSelected = IsSelected
        };
        clone.Pins.Clear();
        foreach (var pin in Pins)
        {
            clone.Pins.Add(pin.Clone());
        }
        return clone;
    }

    public Rect GetBounds()
    {
        return new Rect(X - Width / 2f, Y - Height / 2f, Width, Height);
    }

    /// <summary>
    /// World-space transform for rendering symbol geometry that is drawn at
    /// <c>center + localPoint</c>: mirror X about the centre, then rotate about
    /// the centre. The canvas applies this matrix to the existing world transform.
    /// Pin positions use <see cref="GetPinLocalToWorldMatrix"/> instead.
    /// </summary>
    public Matrix3x2 GetSymbolTransformMatrix()
    {
        var center = new Vector2(X, Y);
        float rad = (float)(RotationDeg * Math.PI / 180.0);
        return Matrix3x2.CreateTranslation(-center)
               * Matrix3x2.CreateScale(MirrorX ? -1f : 1f, 1f)
               * Matrix3x2.CreateRotation(rad)
               * Matrix3x2.CreateTranslation(center);
    }

    /// <summary>
    /// Centre-relative symbol coordinates (pin RelativeX/Y and SymbolGeometry
    /// primitives) -> world coordinates. Mirrors about the vertical axis first,
    /// then rotates, then translates to the element centre.
    /// </summary>
    public Matrix3x2 GetPinLocalToWorldMatrix()
    {
        var center = new Vector2(X, Y);
        float rad = (float)(RotationDeg * Math.PI / 180.0);
        return Matrix3x2.CreateScale(MirrorX ? -1f : 1f, 1f)
               * Matrix3x2.CreateRotation(rad)
               * Matrix3x2.CreateTranslation(center);
    }

    public Vector2 GetPinWorldPosition(CircuitPin pin)
    {
        var local = new Vector2(pin.RelativeX, pin.RelativeY);
        return Vector2.Transform(local, GetPinLocalToWorldMatrix());
    }

    public Vector2 GetPinWorldDirection(CircuitPin pin)
    {
        float rad = (float)(RotationDeg * Math.PI / 180.0);
        var rel = new Vector2(pin.RelativeX, pin.RelativeY);
        if (MirrorX) rel.X = -rel.X;
        var rotRel = Vector2.Transform(rel, Matrix3x2.CreateRotation(rad));
        if (rotRel.LengthSquared() < 1e-4f) return new Vector2(1, 0);
        return Vector2.Normalize(rotRel);
    }

    /// <summary>
    /// Re-apply the canonical local pin offsets for this element type without
    /// clearing the pin collection (used to migrate legacy project files whose
    /// PNP collector/emitter defaults changed, and by tests).
    /// </summary>
    public void ApplyDefaultPinPositions()
    {
        var defaults = new CircuitElement { Type = Type };
        defaults.InitializeDefaultPins();
        foreach (var pin in Pins)
        {
            var source = defaults.Pins.FirstOrDefault(p => p.Id == pin.Id)
                         ?? defaults.Pins.FirstOrDefault(p => p.Name == pin.Name);
            if (source == null) continue;
            pin.RelativeX = source.RelativeX;
            pin.RelativeY = source.RelativeY;
        }
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

            case ElementType.CapacitorPolarized:
                Pins.Add(new CircuitPin { Id = "+", Name = "+", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "-", Name = "-", RelativeX = 35, RelativeY = 0 });
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
                // NPN default symbol: collector on top, emitter at the bottom.
                Pins.Add(new CircuitPin { Id = "B", Name = "B", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "C", Name = "C", RelativeX = 20, RelativeY = -35 });
                Pins.Add(new CircuitPin { Id = "E", Name = "E", RelativeX = 20, RelativeY = 35 });
                break;

            case ElementType.BjtPnp:
                // PNP default symbol (2026-10-18 user request): emitter on top,
                // collector at the bottom, matching the drawing in SymbolGeometry.
                Pins.Add(new CircuitPin { Id = "B", Name = "B", RelativeX = -35, RelativeY = 0 });
                Pins.Add(new CircuitPin { Id = "C", Name = "C", RelativeX = 20, RelativeY = 35 });
                Pins.Add(new CircuitPin { Id = "E", Name = "E", RelativeX = 20, RelativeY = -35 });
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

            case ElementType.VccPort:
                Pins.Add(new CircuitPin { Id = "VCC", Name = "VCC", RelativeX = 0, RelativeY = 22 });
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
        if (RotationDeg != 0 || MirrorX)
        {
            float rad = (float)(-RotationDeg * Math.PI / 180.0);
            var rel = worldPos - new Vector2(X, Y);
            var local = Vector2.Transform(rel, Matrix3x2.CreateRotation(rad));
            if (MirrorX) local.X = -local.X;
            return Math.Abs(local.X) <= (Width / 2f + padding) && Math.Abs(local.Y) <= (Height / 2f + padding);
        }

        float left = X - Width / 2f - padding;
        float right = X + Width / 2f + padding;
        float top = Y - Height / 2f - padding;
        float bottom = Y + Height / 2f + padding;

        return worldPos.X >= left && worldPos.X <= right && worldPos.Y >= top && worldPos.Y <= bottom;
    }
}
