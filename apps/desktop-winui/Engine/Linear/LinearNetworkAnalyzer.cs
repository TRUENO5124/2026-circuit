// ============================================================================
// File: apps/desktop-winui/Engine/Linear/LinearNetworkAnalyzer.cs
// Namespace: CircuitAssistant.Engine.Linear
// Description: General-purpose linear-network analyzer (MNA 2.0) used by
//              MnaLinearStrategy. Builds a complex Modified Nodal Analysis
//              system from an ElectricalNetlist, solves it in DC mode or in
//              sinusoidal steady-state AC (phasor) mode, and exposes
//              arbitrary-port Thevenin/Norton equivalents plus frequency
//              sweeps. All models are strictly linear; nonlinear devices are
//              rejected explicitly instead of being silently dropped.
// ============================================================================
//
// 符号与方向约定（与 SPICE / 教材 MNA 一致，MnaLinearStrategy 的界面文字同样引用本文）：
//
// 1. 节点电压 v_n 以参考地网（NetId = 0 的地网）为 0 V；地网不产生矩阵变量。
//
// 2. 支路电流变量 i_k 的正方向定义为其所在元件"正端 -> 负端"穿过元件的电流：
//    * 独立/受控电压源：pos(OUT1) -> neg(OUT2)；
//    * 电感：引脚 1 -> 引脚 2；
//    MNA 印记：KCL 在正端网 +i_k、负端网 -i_k；约束行为 v_pos - v_neg = 支路电压。
//
// 3. 独立电流源沿用既有 MnaLinearStrategy 的方向约定：电流从 out 端流入 in 端
//    （画布上的向上箭头），即 KCL 在 in 网 +I、out 网 -I。矩阵右端项按此符号装配。
//
// 4. 电压控制电压源 VCVS(μ)：输出端新增支路电流变量，约束
//    V(OUT1) - V(OUT2) = μ · (V(IN1) - V(IN2))。
//
// 5. 电压控制电流源 VCCS(gm)：输出电流 I(OUT1 -> OUT2) = gm · (V(IN1) - V(IN2))，
//    即 KCL 在 OUT1 网 +I_out、OUT2 网 -I_out（电流从 OUT1 穿过元件流向 OUT2）。
//
// 6. 电流控制电压源 CCVS(r) / 电流控制电流源 CCCS(β)：控制端为 0 V 电压支路，
//    约束 V(IN1) = V(IN2)，并新增控制支路电流变量 I_ctrl = 从 IN1 经控制支路
//    流向 IN2 的电流。CCVS 输出支路约束 V(OUT1) - V(OUT2) = r · I_ctrl；
//    CCCS 输出电流 I(OUT1 -> OUT2) = β · I_ctrl。
//
// 7. 直流（DC）模式：电容开路（不产生印记）、电感按 0 V 支路短路（保留支路电流
//    变量并令支路电压为 0）；独立源取直流值，交流源置零（电压源短路、电流源开路）。
//
// 8. 交流（AC）正弦稳态相量模式：电容导纳 jωC、电感阻抗 jωL；独立交流源取幅值
//    实数相量（不随频率变化），独立直流源置零（电压源短路、电流源开路）。相量以
//    cos(ωt) 为参考，输出量均为峰值相量。
//
// 9. 端口等效电阻 R_eq：所有独立源置零（电压源短路、电流源开路），电容开路、电感
//    短路，在端口注入 1 A 直流测试电流，R_eq = (V_A - V_B) / 1 A。端口在直流下
//    无通路时报告"无直流通路"。
//
// 10. 端口等效开路电压 V_oc = 独立源保留时的直流开路电压 (V_A - V_B)。
//
// 数值安全：带部分选主元的复数高斯消元；按行均衡后主元幅度低于相对阈值判为
// 奇异/病态；回代后做相对残差复核；任何非有限量都返回失败原因。矩阵变量上限 256。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine.Linear;

/// <summary>
/// Linear device classes recognized by <see cref="LinearNetworkAnalyzer"/>.
/// </summary>
public enum LinearDeviceKind
{
    Resistor,
    Capacitor,
    Inductor,
    DcVoltageSource,
    AcVoltageSource,
    DcCurrentSource,
    AcCurrentSource,
    Vcvs,
    Vccs,
    Ccvs,
    Cccs
}

/// <summary>
/// One non-ground electrical node of the linear model (matrix node variable).
/// </summary>
public sealed class LinearNetNode
{
    public int NetId { get; init; }
    public string Name { get; init; } = string.Empty;

    public override string ToString() => Name;
}

/// <summary>
/// One stamped linear device. Terminal conventions are documented in the file
/// header of <see cref="LinearNetworkAnalyzer"/>.
/// </summary>
public sealed class LinearDevice
{
    public CircuitElement Element { get; init; } = null!;
    public string ElementId => Element.Id;
    public LinearDeviceKind Kind { get; init; }

    /// <summary>Base-SI value (Ω, F, H, V, A) or the controlled-source gain.</summary>
    public double ValueSi { get; init; }

    public ElectricalNet PositiveNet { get; init; } = null!;
    public ElectricalNet NegativeNet { get; init; } = null!;
    public ElectricalNet? ControlPositiveNet { get; init; }
    public ElectricalNet? ControlNegativeNet { get; init; }

    /// <summary>Matrix row/column of this device's own branch-current variable (-1 if none).</summary>
    public int BranchVariableIndex { get; internal set; } = -1;

    /// <summary>Matrix row/column of the control branch current variable (CCVS/CCCS only).</summary>
    public int ControlBranchVariableIndex { get; internal set; } = -1;

    public bool HasOwnBranchVariable => BranchVariableIndex >= 0;
    public bool IsFourTerminal => Kind is LinearDeviceKind.Vcvs or LinearDeviceKind.Vccs or LinearDeviceKind.Ccvs or LinearDeviceKind.Cccs;
    public bool IsControlled => IsFourTerminal;
    public bool IsIndependentSource => Kind is LinearDeviceKind.DcVoltageSource or LinearDeviceKind.AcVoltageSource
        or LinearDeviceKind.DcCurrentSource or LinearDeviceKind.AcCurrentSource;
    public bool IsVoltageSource => Kind is LinearDeviceKind.DcVoltageSource or LinearDeviceKind.AcVoltageSource;
    public bool IsCurrentSource => Kind is LinearDeviceKind.DcCurrentSource or LinearDeviceKind.AcCurrentSource;
    public bool IsAcSource => Kind is LinearDeviceKind.AcVoltageSource or LinearDeviceKind.AcCurrentSource;

    public string KindDisplayName => Kind switch
    {
        LinearDeviceKind.Resistor => "电阻 R",
        LinearDeviceKind.Capacitor => "电容 C",
        LinearDeviceKind.Inductor => "电感 L",
        LinearDeviceKind.DcVoltageSource => "独立直流电压源",
        LinearDeviceKind.AcVoltageSource => "独立交流电压源",
        LinearDeviceKind.DcCurrentSource => "独立直流电流源",
        LinearDeviceKind.AcCurrentSource => "独立交流电流源",
        LinearDeviceKind.Vcvs => "压控电压源 VCVS",
        LinearDeviceKind.Vccs => "压控电流源 VCCS",
        LinearDeviceKind.Ccvs => "流控电压源 CCVS",
        LinearDeviceKind.Cccs => "流控电流源 CCCS",
        _ => Kind.ToString()
    };

    public override string ToString() => $"{ElementId} [{KindDisplayName}] {PositiveNet.Name}->{NegativeNet.Name}";
}

/// <summary>
/// One solved branch-current variable.
/// </summary>
public sealed class LinearBranchCurrent
{
    public string ElementId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsControlBranch { get; init; }
    public Complex Value { get; init; }

    public override string ToString() => $"{ElementId} {Description} = {Value}";
}

/// <summary>
/// Numerical solution of one MNA solve (DC values are stored as real phasors,
/// i.e. with zero imaginary part).
/// </summary>
public sealed class LinearSolution
{
    private readonly Dictionary<int, Complex> _nodePhasors = new();
    private readonly Dictionary<int, string> _nodeNames = new();
    private readonly Dictionary<string, Complex> _branchPhasors = new(StringComparer.Ordinal);
    private readonly List<LinearBranchCurrent> _branchCurrents = new();

    public bool IsAc { get; internal set; }
    public double FrequencyHz { get; internal set; }
    public int MatrixDimension { get; internal set; }
    public int GroundNetId { get; internal set; }

    public IReadOnlyDictionary<int, Complex> NetPhasors => _nodePhasors;
    public IReadOnlyList<LinearBranchCurrent> BranchCurrents => _branchCurrents;

    internal void AddNode(int netId, string name, Complex value)
    {
        _nodePhasors[netId] = value;
        _nodeNames[netId] = name;
    }

    internal void AddBranch(string elementId, string description, bool isControlBranch, Complex value)
    {
        _branchCurrents.Add(new LinearBranchCurrent
        {
            ElementId = elementId,
            Description = description,
            IsControlBranch = isControlBranch,
            Value = value
        });
        if (!isControlBranch)
        {
            _branchPhasors[elementId] = value;
        }
    }

    public string NetName(int netId) =>
        _nodeNames.TryGetValue(netId, out var name) ? name : (netId == GroundNetId ? "GND" : $"N{netId}");

    public Complex NodePhasor(int netId)
    {
        if (netId == GroundNetId) return Complex.Zero;
        if (_nodePhasors.TryGetValue(netId, out var value)) return value;
        throw new ArgumentException($"节点 NetId={netId} 不在本次求解结果中。", nameof(netId));
    }

    public double NodeMagnitude(int netId) => NodePhasor(netId).Magnitude;

    public double NodePhaseDeg(int netId)
    {
        var phasor = NodePhasor(netId);
        if (phasor.Magnitude < 1e-300) return 0.0;
        return Math.Atan2(phasor.Imaginary, phasor.Real) * 180.0 / Math.PI;
    }

    /// <summary>Real node voltage for a DC solve; throws for AC phasor solutions.</summary>
    public double NodeVoltage(int netId)
    {
        if (IsAc) throw new InvalidOperationException("交流相量解请使用 NodePhasor()/NodeMagnitude()/NodePhaseDeg()。");
        return NodePhasor(netId).Real;
    }

    /// <summary>Branch current (pos -> neg through the element) as a phasor.</summary>
    public Complex BranchPhasor(string elementId)
    {
        if (_branchPhasors.TryGetValue(elementId, out var value)) return value;
        throw new ArgumentException($"元件 '{elementId}' 没有可用的支路电流变量（电容/电流源/受控电流源没有独立支路电流）。", nameof(elementId));
    }

    public double BranchCurrentAmpere(string elementId)
    {
        if (IsAc) throw new InvalidOperationException("交流相量解请使用 BranchPhasor()。");
        return BranchPhasor(elementId).Real;
    }
}

/// <summary>One computed point of an AC magnitude/phase sweep.</summary>
public sealed class AcSweepPoint
{
    public double FrequencyHz { get; init; }
    public Complex Gain { get; init; }
    public double GainMagnitude => Gain.Magnitude;
    public double GainDb => LinearNetworkAnalyzer.ToDecibel(GainMagnitude);
    public double PhaseDeg => Gain.Magnitude < 1e-300 ? 0.0 : Math.Atan2(Gain.Imaginary, Gain.Real) * 180.0 / Math.PI;
}

/// <summary>Result of an AC frequency sweep between an input and an output port.</summary>
public sealed class AcSweepResult
{
    public int InputPositiveNetId { get; init; }
    public int InputNegativeNetId { get; init; }
    public int OutputPositiveNetId { get; init; }
    public int OutputNegativeNetId { get; init; }
    public string InputPortLabel { get; init; } = string.Empty;
    public string OutputPortLabel { get; init; } = string.Empty;
    public string GainQuantityLabel { get; init; } = "电压增益";
    public string GainUnit { get; init; } = "V/V";
    public double FrequencyStartHz { get; init; }
    public double FrequencyStopHz { get; init; }
    public int PointsPerDecade { get; init; }
    public List<AcSweepPoint> Points { get; } = new();
    public double? LowerCutoffHz { get; internal set; }
    public double? UpperCutoffHz { get; internal set; }
    public double? BandwidthHz { get; internal set; }

    /// <summary>
    /// Passband reference gain in dB determined from the window maximum plus the
    /// out-of-window probe frequencies. Cutoff detection is relative to this value,
    /// never to the maximum of a window that lies entirely in a stopband.
    /// </summary>
    public double ReferenceGainDb { get; internal set; }

    /// <summary>True when at least one swept point lies within 3 dB of <see cref="ReferenceGainDb"/>.</summary>
    public bool PassbandInWindow { get; internal set; } = true;

    /// <summary>False when the sweep window does not contain the passband, so no -3 dB estimate is valid.</summary>
    public bool CutoffEstimateAvailable { get; internal set; } = true;

    /// <summary>Human-readable reason when <see cref="CutoffEstimateAvailable"/> is false.</summary>
    public string CutoffUnavailableReason { get; internal set; } = string.Empty;

    /// <summary>The true lower -3 dB crossing lies below the sweep window; the window cannot estimate it.</summary>
    public bool LowerCutoffBelowWindow { get; internal set; }

    /// <summary>The true upper -3 dB crossing lies above the sweep window; the window cannot estimate it.</summary>
    public bool UpperCutoffAboveWindow { get; internal set; }

    public List<string> Notes { get; } = new();
}

/// <summary>
/// Internal modelling error raised while building the linear device list; it is
/// converted into the public <c>out string error</c> of TryCreate.
/// </summary>
internal sealed class LinearAnalysisException : Exception
{
    public LinearAnalysisException(string message) : base(message) { }
}

/// <summary>
/// General linear-network analyzer: complex MNA formulation for R/C/L,
/// independent DC/AC sources and the four controlled-source types.
/// </summary>
public sealed class LinearNetworkAnalyzer
{
    /// <summary>Hard limit on the number of MNA matrix variables.</summary>
    public const int MaxMatrixDimension = 256;

    private const double PivotRelativeTolerance = 1e-14;
    private const double ResidualRelativeTolerance = 1e-6;

    private readonly ElectricalNetlist _netlist;
    private readonly ElectricalNet _groundNet;
    private readonly List<LinearDevice> _devices = new();
    private readonly List<LinearNetNode> _nodes = new();
    private readonly List<ElectricalNet> _nodeNets = new();
    private readonly Dictionary<ElectricalNet, int> _nodeRowByNet = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, ElectricalNet> _netById = new();
    private readonly List<string> _notes = new();
    private int _branchVariableCount;

    private LinearNetworkAnalyzer(ElectricalNetlist netlist, ElectricalNet groundNet)
    {
        _netlist = netlist;
        _groundNet = groundNet;
    }

    public int MatrixDimension => _nodes.Count + _branchVariableCount;
    public int NodeVariableCount => _nodes.Count;
    public int BranchVariableCount => _branchVariableCount;
    public int GroundNetId => _groundNet.NetId;
    public string GroundNetName => _groundNet.Name;
    public IReadOnlyList<LinearDevice> Devices => _devices;
    public IReadOnlyList<LinearNetNode> Nodes => _nodes;

    /// <summary>LaTeX-ready matrix variable labels, ordered exactly like MNA rows/columns.</summary>
    public IReadOnlyList<string> VariableLabels { get; private set; } = Array.Empty<string>();

    public string ModelSummary { get; private set; } = string.Empty;

    /// <summary>Non-fatal modelling caveats (unknown gain units, truncations, ...).</summary>
    public string BoundaryNote => string.Join("；", _notes.Where(n => !string.IsNullOrWhiteSpace(n)));

    /// <summary>True when at least one independent AC source is present.</summary>
    public bool HasAcExcitation => _devices.Any(d => d.IsAcSource);

    /// <summary>Independent AC voltage sources in deterministic element order.</summary>
    public IReadOnlyList<LinearDevice> AcVoltageSources =>
        _devices.Where(d => d.Kind == LinearDeviceKind.AcVoltageSource).ToList();

    /// <summary>Independent AC current sources in deterministic element order.</summary>
    public IReadOnlyList<LinearDevice> AcCurrentSources =>
        _devices.Where(d => d.Kind == LinearDeviceKind.AcCurrentSource).ToList();

    /// <summary>First independent source (voltage preferred) usable as the excitation port.</summary>
    public LinearDevice? FirstAcSource =>
        _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcVoltageSource)
        ?? _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcCurrentSource);

    /// <summary>
    /// Preferred operating frequency: the first AC source's FrequencyHz when finite and
    /// positive, otherwise null so callers can fall back to AnalysisParameters.
    /// </summary>
    public double? PreferredOperatingFrequencyHz
    {
        get
        {
            foreach (var device in _devices.Where(d => d.IsAcSource))
            {
                double f = device.Element.FrequencyHz;
                if (double.IsFinite(f) && f > 0) return f;
            }
            return null;
        }
    }

    // =====================================================================
    // Construction
    // =====================================================================

    /// <summary>
    /// Tries to build a linear MNA model from the netlist. Unsupported devices
    /// (diode / transformer / BJT / MOSFET / JFET / VCC port), missing ground,
    /// non-finite or non-positive values and oversized matrices are rejected with
    /// an explicit Chinese reason.
    /// </summary>
    public static bool TryCreate(
        ElectricalNetlist netlist,
        out LinearNetworkAnalyzer? model,
        out string error,
        CancellationToken cancellationToken = default)
    {
        model = null;
        error = string.Empty;

        if (netlist == null) { error = "网表为空，无法建立线性网络模型。"; return false; }
        if (netlist.Elements.Count == 0) { error = "网络中没有任何元件，无法建立 MNA 方程。"; return false; }
        if (netlist.GroundNet == null) { error = "电路缺少参考地 GND，无法建立以地为参考的 MNA 方程。"; return false; }

        var groundNet = netlist.GroundNet;
        var analyzer = new LinearNetworkAnalyzer(netlist, groundNet);
        var unsupported = new List<string>();
        var devices = new List<LinearDevice>();

        foreach (var element in netlist.Elements.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Type == ElementType.Ground) continue;

            if (!IsSupportedElementType(element.Type))
            {
                unsupported.Add($"{element.Id}（{DescribeElementType(element.Type)}）");
                continue;
            }

            try
            {
                devices.Add(BuildDevice(netlist, element, analyzer._notes));
            }
            catch (LinearAnalysisException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        if (unsupported.Count > 0)
        {
            error = "通用线性网络分析不支持以下器件：" + string.Join("、", unsupported) +
                    "。请删除这些器件，或改用支持的无源元件/独立源/四种受控源（VCVS/VCCS/CCVS/CCCS）。";
            return false;
        }

        if (devices.Count == 0)
        {
            error = "网络中没有可分析的线性元件（仅存在接地符号）。";
            return false;
        }

        // Deterministic ordering: passive devices and sources keep element-id order.
        analyzer._devices.AddRange(devices);

        // Collect nodes referenced by at least one device terminal.
        var referencedNets = new List<ElectricalNet>();
        var seenNets = new HashSet<ElectricalNet>(ReferenceEqualityComparer.Instance);
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddReferencedNet(referencedNets, seenNets, device.PositiveNet);
            AddReferencedNet(referencedNets, seenNets, device.NegativeNet);
            if (device.ControlPositiveNet != null) AddReferencedNet(referencedNets, seenNets, device.ControlPositiveNet);
            if (device.ControlNegativeNet != null) AddReferencedNet(referencedNets, seenNets, device.ControlNegativeNet);
        }

        foreach (var net in referencedNets.OrderBy(n => n.NetId))
        {
            if (ReferenceEquals(net, groundNet)) continue;
            analyzer._nodeRowByNet[net] = analyzer._nodes.Count;
            analyzer._nodeNets.Add(net);
            analyzer._nodes.Add(new LinearNetNode { NetId = net.NetId, Name = net.Name });
            // Map by NetId for the public integer API; first registration wins.
            if (!analyzer._netById.ContainsKey(net.NetId)) analyzer._netById[net.NetId] = net;
        }
        analyzer._netById[groundNet.NetId] = groundNet;

        if (analyzer._nodes.Count == 0 && analyzer._devices.All(d => !d.HasOwnBranchVariable))
        {
            error = "网络没有可求解的非地节点或支路电流变量。";
            return false;
        }

        // Assign branch-current variable indices. Two passes keep indices stable.
        foreach (var device in analyzer._devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.Kind is LinearDeviceKind.DcVoltageSource or LinearDeviceKind.AcVoltageSource
                or LinearDeviceKind.Inductor or LinearDeviceKind.Vcvs or LinearDeviceKind.Ccvs)
            {
                device.BranchVariableIndex = analyzer._branchVariableCount++;
            }
        }
        foreach (var device in analyzer._devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.Kind is LinearDeviceKind.Ccvs or LinearDeviceKind.Cccs)
            {
                device.ControlBranchVariableIndex = analyzer._branchVariableCount++;
            }
        }

        if (analyzer.MatrixDimension > MaxMatrixDimension)
        {
            error = $"MNA 矩阵变量数 {analyzer.MatrixDimension} 超过上限 {MaxMatrixDimension}，" +
                    "请简化电路后再求解（当前为受控源/动态元件通用线性求解器）。";
            return false;
        }

        if (analyzer.MatrixDimension == 0)
        {
            error = "网络没有可求解的独立变量。";
            return false;
        }

        analyzer.BuildVariableLabels();
        analyzer.ModelSummary = BuildModelSummary(analyzer);
        model = analyzer;
        return true;
    }

    private void BuildVariableLabels()
    {
        var labels = new List<string>(MatrixDimension);
        foreach (var node in _nodes) labels.Add($"v_{{{node.Name}}}");
        foreach (var device in _devices.Where(d => d.BranchVariableIndex >= 0).OrderBy(d => d.BranchVariableIndex))
        {
            labels.Add($"i_{{{device.ElementId}}}");
        }
        foreach (var device in _devices.Where(d => d.ControlBranchVariableIndex >= 0).OrderBy(d => d.ControlBranchVariableIndex))
        {
            labels.Add($"i_{{ctrl,{device.ElementId}}}");
        }
        VariableLabels = labels;
    }

    private static void AddReferencedNet(List<ElectricalNet> nets, HashSet<ElectricalNet> seen, ElectricalNet net)
    {
        if (seen.Add(net)) nets.Add(net);
    }

    private static string BuildModelSummary(LinearNetworkAnalyzer analyzer)
    {
        var counts = analyzer._devices
            .GroupBy(d => d.Kind)
            .OrderBy(g => (int)g.Key)
            .Select(g => $"{ShortKindName(g.Key)}×{g.Count()}");
        return $"通用线性网络模型：{analyzer._nodes.Count} 个非地节点变量 + {analyzer._branchVariableCount} 个支路电流变量" +
               $" → {analyzer.MatrixDimension}×{analyzer.MatrixDimension} 复数 MNA 矩阵；器件 {string.Join("、", counts)}";
    }

    private static string ShortKindName(LinearDeviceKind kind) => kind switch
    {
        LinearDeviceKind.Resistor => "R",
        LinearDeviceKind.Capacitor => "C",
        LinearDeviceKind.Inductor => "L",
        LinearDeviceKind.DcVoltageSource => "DC 电压源",
        LinearDeviceKind.AcVoltageSource => "AC 电压源",
        LinearDeviceKind.DcCurrentSource => "DC 电流源",
        LinearDeviceKind.AcCurrentSource => "AC 电流源",
        LinearDeviceKind.Vcvs => "VCVS",
        LinearDeviceKind.Vccs => "VCCS",
        LinearDeviceKind.Ccvs => "CCVS",
        LinearDeviceKind.Cccs => "CCCS",
        _ => kind.ToString()
    };

    private static bool IsSupportedElementType(ElementType type) => type is
        ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized or ElementType.Inductor or
        ElementType.DcVoltageSource or ElementType.AcVoltageSource or ElementType.DcCurrentSource or ElementType.AcCurrentSource or
        ElementType.Vcvs or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs or ElementType.Ground;

    private static string DescribeElementType(ElementType type) => type switch
    {
        ElementType.Diode => "二极管（非线性）",
        ElementType.Transformer => "变压器（互感耦合）",
        ElementType.BjtNpn => "NPN 三极管",
        ElementType.BjtPnp => "PNP 三极管",
        ElementType.Mosfet => "MOSFET",
        ElementType.MosfetNEnh => "N 增强型 MOSFET",
        ElementType.MosfetPEnh => "P 增强型 MOSFET",
        ElementType.MosfetNDep => "N 耗尽型 MOSFET",
        ElementType.MosfetPDep => "P 耗尽型 MOSFET",
        ElementType.Jfet => "JFET",
        ElementType.JfetN => "N 沟道 JFET",
        ElementType.JfetP => "P 沟道 JFET",
        ElementType.VccPort => "VCC 电源端口（不参与 MNA 方程）",
        _ => type.ToString()
    };

    // =====================================================================
    // Device construction
    // =====================================================================

    private static LinearDevice BuildDevice(ElectricalNetlist netlist, CircuitElement element, List<string> notes)
    {
        switch (element.Type)
        {
            case ElementType.Resistor:
            case ElementType.Capacitor:
            case ElementType.CapacitorPolarized:
            case ElementType.Inductor:
            {
                var kind = element.Type switch
                {
                    ElementType.Resistor => LinearDeviceKind.Resistor,
                    ElementType.Inductor => LinearDeviceKind.Inductor,
                    _ => LinearDeviceKind.Capacitor
                };
                var positive = ResolveRequiredNet(netlist, element, "1", "+");
                var negative = ResolveRequiredNet(netlist, element, "2", "-");
                EnsureDistinctTerminals(element, positive, negative, "两端");
                double value = ConvertBaseSiValue(element);
                if (!double.IsFinite(value)) throw new LinearAnalysisException($"元件 {element.Id} 的数值不是有限数。");
                if (value <= 0) throw new LinearAnalysisException($"元件 {element.Id} 的数值必须大于 0（R/C/L 取值 {value:G6}）。");
                return new LinearDevice
                {
                    Element = element,
                    Kind = kind,
                    ValueSi = value,
                    PositiveNet = positive,
                    NegativeNet = negative
                };
            }

            case ElementType.DcVoltageSource:
            case ElementType.AcVoltageSource:
            {
                var positive = ResolveRequiredNet(netlist, element, "pos");
                var negative = ResolveRequiredNet(netlist, element, "neg");
                EnsureDistinctTerminals(element, positive, negative, "两端");
                double value = ConvertBaseSiValue(element);
                if (!double.IsFinite(value)) throw new LinearAnalysisException($"电压源 {element.Id} 的电压值不是有限数。");
                return new LinearDevice
                {
                    Element = element,
                    Kind = element.Type == ElementType.DcVoltageSource ? LinearDeviceKind.DcVoltageSource : LinearDeviceKind.AcVoltageSource,
                    ValueSi = value,
                    PositiveNet = positive,
                    NegativeNet = negative
                };
            }

            case ElementType.DcCurrentSource:
            case ElementType.AcCurrentSource:
            {
                // Convention: current flows from the "out" pin into the "in" pin, so the
                // injected (positive) KCL node is the "in" net and the sink is the "out" net.
                var injectionNode = ResolveRequiredNet(netlist, element, "in");
                var sinkNode = ResolveRequiredNet(netlist, element, "out");
                double value = ConvertBaseSiValue(element);
                if (!double.IsFinite(value)) throw new LinearAnalysisException($"电流源 {element.Id} 的电流值不是有限数。");
                return new LinearDevice
                {
                    Element = element,
                    Kind = element.Type == ElementType.DcCurrentSource ? LinearDeviceKind.DcCurrentSource : LinearDeviceKind.AcCurrentSource,
                    ValueSi = value,
                    PositiveNet = injectionNode,
                    NegativeNet = sinkNode
                };
            }

            case ElementType.Vcvs:
            case ElementType.Vccs:
            case ElementType.Ccvs:
            case ElementType.Cccs:
            {
                var kind = element.Type switch
                {
                    ElementType.Vcvs => LinearDeviceKind.Vcvs,
                    ElementType.Vccs => LinearDeviceKind.Vccs,
                    ElementType.Ccvs => LinearDeviceKind.Ccvs,
                    _ => LinearDeviceKind.Cccs
                };
                var controlPositive = ResolveRequiredNet(netlist, element, "IN1");
                var controlNegative = ResolveRequiredNet(netlist, element, "IN2");
                var outputPositive = ResolveRequiredNet(netlist, element, "OUT1");
                var outputNegative = ResolveRequiredNet(netlist, element, "OUT2");

                // Voltage-output controlled sources need a genuine two-node output branch;
                // shorted outputs make the branch current indeterminate.
                if (kind is LinearDeviceKind.Vcvs or LinearDeviceKind.Ccvs)
                {
                    EnsureDistinctTerminals(element, outputPositive, outputNegative, "输出端");
                }
                // Current-controlled types need a determinate control-branch current.
                if (kind is LinearDeviceKind.Ccvs or LinearDeviceKind.Cccs)
                {
                    EnsureDistinctTerminals(element, controlPositive, controlNegative, "控制端");
                }

                double gain = ConvertControlledSourceGain(element, notes);
                return new LinearDevice
                {
                    Element = element,
                    Kind = kind,
                    ValueSi = gain,
                    PositiveNet = outputPositive,
                    NegativeNet = outputNegative,
                    ControlPositiveNet = controlPositive,
                    ControlNegativeNet = controlNegative
                };
            }

            default:
                throw new LinearAnalysisException($"元件 {element.Id}（{DescribeElementType(element.Type)}）不能被线性网络分析器建模。");
        }
    }

    private static ElectricalNet ResolveRequiredNet(ElectricalNetlist netlist, CircuitElement element, params string[] pinIds)
    {
        foreach (var pinId in pinIds)
        {
            var net = netlist.FindNetForPin(element.Id, pinId);
            if (net != null) return net;
        }
        throw new LinearAnalysisException(
            $"元件 {element.Id}（{DescribeElementType(element.Type)}）的引脚 {string.Join("/", pinIds)} 未连接到有效电气网络，无法建立 MNA 方程。");
    }

    private static void EnsureDistinctTerminals(CircuitElement element, ElectricalNet positive, ElectricalNet negative, string terminalName)
    {
        if (ReferenceEquals(positive, negative))
        {
            throw new LinearAnalysisException(
                $"元件 {element.Id} 的{terminalName}短路到同一电气网络 '{positive.Name}'，线性网络分析无法确定支路电流。");
        }
    }

    private static double ConvertBaseSiValue(CircuitElement element)
    {
        try
        {
            return NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
        }
        catch (ArgumentException ex)
        {
            throw new LinearAnalysisException($"元件 {element.Id} 的数值单位无效：{ex.Message}");
        }
    }

    /// <summary>
    /// Controlled-source gain = DisplayNumber × unit multiplier. Recognized units follow
    /// the task contract: "" / "倍" = 1, mS = 1e-3, μS/uS = 1e-6, S = 1, Ω = 1,
    /// kΩ = 1e3, MΩ = 1e6, mV = 1e-3, kV = 1e3, mA = 1e-3, μA = 1e-6.
    /// Unknown units are treated as ×1 and reported through the boundary note.
    /// </summary>
    internal static double ConvertControlledSourceGain(CircuitElement element, List<string> notes)
    {
        string unit = (element.UnitSymbol ?? string.Empty).Trim();
        // Normalize micro sign U+00B5 to Greek mu U+03BC and accept ASCII "u".
        unit = unit.Replace('\u00B5', '\u03BC');
        double multiplier;
        switch (unit)
        {
            case "":
            case "倍":
            case "S":
            case "Ω":
                multiplier = 1.0;
                break;
            case "mS":
            case "mV":
            case "mA":
                multiplier = 1e-3;
                break;
            case "\u03BCS":
            case "uS":
            case "\u03BCA":
            case "uA":
                multiplier = 1e-6;
                break;
            case "kΩ":
            case "kV":
                multiplier = 1e3;
                break;
            case "MΩ":
                multiplier = 1e6;
                break;
            default:
                multiplier = 1.0;
                notes.Add($"受控源 {element.Id} 的增益单位 '{element.UnitSymbol}' 未识别，已按 ×1 处理；如需精确倍率请改用 倍/S/mS/μS/Ω/kΩ/MΩ/V 类单位");
                break;
        }

        double gain = element.DisplayNumber * multiplier;
        if (!double.IsFinite(gain))
        {
            throw new LinearAnalysisException($"受控源 {element.Id} 的增益不是有限数。");
        }
        return gain;
    }

    // =====================================================================
    // DC / AC solving
    // =====================================================================

    /// <summary>Solves the network in DC mode (C open, L short, AC sources zeroed).</summary>
    public bool TrySolveDc(
        out LinearSolution? solution,
        out string error,
        CancellationToken cancellationToken = default)
    {
        return TrySolveCore(new SolveRequest { IsAc = false }, out solution, out error, cancellationToken);
    }

    /// <summary>Solves the network in sinusoidal steady-state AC phasor mode.</summary>
    public bool TrySolveAc(
        double frequencyHz,
        out LinearSolution? solution,
        out string error,
        CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0)
        {
            solution = null;
            error = $"交流相量分析的频率必须是有限正数（当前 {frequencyHz:G6} Hz）。";
            return false;
        }
        return TrySolveCore(new SolveRequest { IsAc = true, FrequencyHz = frequencyHz }, out solution, out error, cancellationToken);
    }

    /// <summary>
    /// Computes the transfer gain H = V(out+) - V(out-) with a normalized 1-unit
    /// excitation at the given input port. If an independent voltage source sits on
    /// the input port it is forced to 1 V; if an independent current source sits on
    /// the port it is forced to 1 A (then <paramref name="gainUnit"/> is V/A);
    /// otherwise a 1 V ideal test source is injected at the port.
    /// </summary>
    public bool TryGetTransferGain(
        double frequencyHz,
        int inputPositiveNetId,
        int inputNegativeNetId,
        int outputPositiveNetId,
        int outputNegativeNetId,
        out Complex gain,
        out string gainUnit,
        out string excitationDescription,
        out string error,
        CancellationToken cancellationToken = default)
    {
        gain = Complex.Zero;
        gainUnit = "V/V";
        excitationDescription = string.Empty;

        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0)
        {
            error = $"交流相量分析的频率必须是有限正数（当前 {frequencyHz:G6} Hz）。";
            return false;
        }

        var request = BuildNormalizedExcitationRequest(
            inputPositiveNetId, inputNegativeNetId, out gainUnit, out excitationDescription, out error);
        if (request == null) return false;
        request.IsAc = true;
        request.FrequencyHz = frequencyHz;

        if (!TrySolveCore(request, out var solution, out error, cancellationToken) || solution == null) return false;

        try
        {
            gain = solution.NodePhasor(outputPositiveNetId) - solution.NodePhasor(outputNegativeNetId);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
        return true;
    }

    private SolveRequest? BuildNormalizedExcitationRequest(
        int inputPositiveNetId,
        int inputNegativeNetId,
        out string gainUnit,
        out string excitationDescription,
        out string error)
    {
        gainUnit = "V/V";
        excitationDescription = string.Empty;
        error = string.Empty;

        if (!ValidateNetId(inputPositiveNetId, out error) || !ValidateNetId(inputNegativeNetId, out error)) return null;

        bool MatchesVoltagePort(LinearDevice d) =>
            d.IsVoltageSource && d.PositiveNet.NetId == inputPositiveNetId && d.NegativeNet.NetId == inputNegativeNetId;
        bool MatchesCurrentPort(LinearDevice d) =>
            d.IsCurrentSource && d.PositiveNet.NetId == inputPositiveNetId && d.NegativeNet.NetId == inputNegativeNetId;

        var excitation = _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcVoltageSource && MatchesVoltagePort(d))
                         ?? _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.DcVoltageSource && MatchesVoltagePort(d))
                         ?? _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcCurrentSource && MatchesCurrentPort(d))
                         ?? _devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.DcCurrentSource && MatchesCurrentPort(d));

        if (excitation != null)
        {
            if (excitation.IsVoltageSource)
            {
                gainUnit = "V/V";
                excitationDescription = $"端口独立电压源 {excitation.ElementId} 归一化为 1 V 交流激励";
            }
            else
            {
                gainUnit = "V/A";
                excitationDescription = $"端口独立电流源 {excitation.ElementId} 归一化为 1 A 交流激励（跨阻）";
            }
            return new SolveRequest
            {
                IsAc = true,
                ZeroIndependentSources = true,
                SourceOverrides = new Dictionary<string, Complex>(StringComparer.Ordinal) { [excitation.ElementId] = Complex.One }
            };
        }

        gainUnit = "V/V";
        excitationDescription = $"输入端口注入 1 V 理想交流测试电压源";
        return new SolveRequest
        {
            IsAc = true,
            ZeroIndependentSources = true,
            AddTestVoltageSource = true,
            TestVoltageValue = Complex.One,
            TestVoltagePositiveNetId = inputPositiveNetId,
            TestVoltageNegativeNetId = inputNegativeNetId
        };
    }

    private bool ValidateNetId(int netId, out string error)
    {
        error = string.Empty;
        if (!_netById.ContainsKey(netId))
        {
            error = $"节点 NetId={netId} 不在本线性网络中。";
            return false;
        }
        return true;
    }

    // =====================================================================
    // Equivalent resistance / Thevenin
    // =====================================================================

    /// <summary>
    /// DC equivalent resistance between two nets: every independent source is
    /// zeroed, capacitors are open, inductors are shorted, 1 A is injected from
    /// <paramref name="netIdB"/> to <paramref name="netIdA"/> and the port voltage
    /// is measured.
    /// </summary>
    public bool TryGetEquivalentResistance(
        int netIdA,
        int netIdB,
        out double resistanceOhm,
        out string error,
        CancellationToken cancellationToken = default)
    {
        resistanceOhm = double.NaN;
        if (!ValidateNetId(netIdA, out error) || !ValidateNetId(netIdB, out error)) return false;

        var request = new SolveRequest
        {
            IsAc = false,
            ZeroIndependentSources = true,
            AddInjectionCurrent = true,
            InjectionCurrent = Complex.One,
            InjectionPositiveNetId = netIdA,
            InjectionNegativeNetId = netIdB
        };
        if (!TrySolveCore(request, out var solution, out error, cancellationToken) || solution == null) return false;

        resistanceOhm = (solution.NodePhasor(netIdA) - solution.NodePhasor(netIdB)).Real;
        if (!double.IsFinite(resistanceOhm))
        {
            error = "端口等效电阻求解得到非有限值。";
            return false;
        }
        return true;
    }

    /// <summary>String-based overload: accepts a net name (e.g. "N1", "GND") or a numeric NetId.</summary>
    public bool TryGetEquivalentResistance(
        string netA,
        string netB,
        out double resistanceOhm,
        out string error,
        CancellationToken cancellationToken = default)
    {
        resistanceOhm = double.NaN;
        if (!TryResolveNetToken(netA, out int idA, out error) || !TryResolveNetToken(netB, out int idB, out error)) return false;
        return TryGetEquivalentResistance(idA, idB, out resistanceOhm, out error, cancellationToken);
    }

    /// <summary>DC Thevenin equivalent: open-circuit voltage with sources active and R_eq with sources zeroed.</summary>
    public bool TryGetThevenin(
        int netIdA,
        int netIdB,
        out double openCircuitVoltageV,
        out double resistanceOhm,
        out string error,
        CancellationToken cancellationToken = default)
    {
        openCircuitVoltageV = double.NaN;
        resistanceOhm = double.NaN;
        if (!TrySolveDc(out var dcSolution, out error, cancellationToken) || dcSolution == null) return false;
        openCircuitVoltageV = (dcSolution.NodePhasor(netIdA) - dcSolution.NodePhasor(netIdB)).Real;
        if (!double.IsFinite(openCircuitVoltageV))
        {
            error = "端口开路电压求解得到非有限值。";
            return false;
        }
        return TryGetEquivalentResistance(netIdA, netIdB, out resistanceOhm, out error, cancellationToken);
    }

    /// <summary>String-based overload of <see cref="TryGetThevenin(int,int,out double,out double,out string,CancellationToken)"/>.</summary>
    public bool TryGetThevenin(
        string netA,
        string netB,
        out double openCircuitVoltageV,
        out double resistanceOhm,
        out string error,
        CancellationToken cancellationToken = default)
    {
        openCircuitVoltageV = double.NaN;
        resistanceOhm = double.NaN;
        if (!TryResolveNetToken(netA, out int idA, out error) || !TryResolveNetToken(netB, out int idB, out error)) return false;
        return TryGetThevenin(idA, idB, out openCircuitVoltageV, out resistanceOhm, out error, cancellationToken);
    }

    /// <summary>
    /// Driving-point impedance seen by one independent excitation source, computed by
    /// normalizing that source to 1 V / 1 A with all other independent sources zeroed.
    /// This is the "input resistance" convention (the excitation source itself is the
    /// port, not a shorted element), e.g. a 12 V source feeding a 1k+1k divider sees 2kΩ.
    /// </summary>
    public bool TryGetDrivingPointImpedance(
        LinearDevice excitation,
        bool isAc,
        double frequencyHz,
        out Complex impedanceOhm,
        out string error,
        CancellationToken cancellationToken = default)
    {
        impedanceOhm = Complex.Zero;
        if (excitation == null) { error = "激励源为空，无法计算驱动点阻抗。"; return false; }
        if (isAc && (!double.IsFinite(frequencyHz) || frequencyHz <= 0))
        {
            error = $"交流相量分析的频率必须是有限正数（当前 {frequencyHz:G6} Hz）。";
            return false;
        }

        var request = new SolveRequest
        {
            IsAc = isAc,
            FrequencyHz = frequencyHz,
            ZeroIndependentSources = true,
            SourceOverrides = new Dictionary<string, Complex>(StringComparer.Ordinal) { [excitation.ElementId] = Complex.One }
        };
        if (!TrySolveCore(request, out var solution, out error, cancellationToken) || solution == null) return false;

        if (excitation.IsVoltageSource)
        {
            var branchCurrent = solution.BranchPhasor(excitation.ElementId);
            if (!double.IsFinite(branchCurrent.Magnitude) || branchCurrent.Magnitude < 1e-15)
            {
                error = "激励端口电流为零，驱动点阻抗为无穷大。";
                return false;
            }
            // The source delivers -i_branch into the network for a 1 V drive.
            impedanceOhm = new Complex(-1.0, 0.0) / branchCurrent;
        }
        else
        {
            impedanceOhm = solution.NodePhasor(excitation.PositiveNet.NetId) - solution.NodePhasor(excitation.NegativeNet.NetId);
        }

        if (!double.IsFinite(impedanceOhm.Real) || !double.IsFinite(impedanceOhm.Imaginary))
        {
            error = "驱动点阻抗求解得到非有限值。";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves a net name or numeric NetId. Names are matched case-insensitively;
    /// "GND" resolves to the reference ground net.
    /// </summary>
    public bool TryResolveNetToken(string token, out int netId, out string error)
    {
        netId = -1;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "端口节点名称不能为空。";
            return false;
        }
        string trimmed = token.Trim();
        if (int.TryParse(trimmed, out int numericId))
        {
            if (_netlist.Nets.Any(n => n.NetId == numericId)) { netId = numericId; return true; }
            error = $"节点 NetId={numericId} 不存在。";
            return false;
        }
        if (string.Equals(trimmed, "GND", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, _groundNet.Name, StringComparison.OrdinalIgnoreCase))
        {
            netId = _groundNet.NetId;
            return true;
        }
        var net = _netlist.Nets.FirstOrDefault(n => string.Equals(n.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (net == null)
        {
            error = $"找不到名为 '{token}' 的节点。";
            return false;
        }
        netId = net.NetId;
        return true;
    }

    // =====================================================================
    // Frequency sweep
    // =====================================================================

    /// <summary>
    /// Sweeps the transfer gain from <paramref name="frequencyStartHz"/> to
    /// <paramref name="frequencyStopHz"/> with <paramref name="pointsPerDecade"/>
    /// points per decade and estimates the -3 dB lower/upper cutoffs and bandwidth.
    /// </summary>
    public bool TrySweep(
        int inputPositiveNetId,
        int inputNegativeNetId,
        int outputPositiveNetId,
        int outputNegativeNetId,
        double frequencyStartHz,
        double frequencyStopHz,
        int pointsPerDecade,
        out AcSweepResult? result,
        out string error,
        CancellationToken cancellationToken = default)
    {
        result = null;
        error = string.Empty;

        if (!ValidateNetId(inputPositiveNetId, out error) || !ValidateNetId(inputNegativeNetId, out error) ||
            !ValidateNetId(outputPositiveNetId, out error) || !ValidateNetId(outputNegativeNetId, out error))
        {
            return false;
        }
        if (!double.IsFinite(frequencyStartHz) || frequencyStartHz <= 0)
        {
            error = $"扫频起始频率必须是有限正数（当前 {frequencyStartHz:G6} Hz）。";
            return false;
        }
        if (!double.IsFinite(frequencyStopHz) || frequencyStopHz <= frequencyStartHz)
        {
            error = $"扫频终止频率必须大于起始频率（当前 {frequencyStartHz:G6} ~ {frequencyStopHz:G6} Hz）。";
            return false;
        }
        if (pointsPerDecade < 1)
        {
            error = "每十倍频程点数必须至少为 1。";
            return false;
        }

        double decades = Math.Log10(frequencyStopHz / frequencyStartHz);
        int stepCount = (int)Math.Round(decades * pointsPerDecade, MidpointRounding.AwayFromZero);
        if (stepCount < 1) stepCount = 1;
        if (stepCount > 10000)
        {
            error = "扫频点数过多（超过 10000 点），请减小频率范围或每十倍频程点数。";
            return false;
        }

        var request = BuildNormalizedExcitationRequest(
            inputPositiveNetId, inputNegativeNetId, out string gainUnit, out string excitationDescription, out error);
        if (request == null) return false;

        string inputLabel = BuildPortLabel(inputPositiveNetId, inputNegativeNetId);
        string outputLabel = BuildPortLabel(outputPositiveNetId, outputNegativeNetId);
        string gainQuantity = gainUnit == "V/A" ? "跨阻（输出电压 / 输入电流）" : "电压增益";

        var sweep = new AcSweepResult
        {
            InputPositiveNetId = inputPositiveNetId,
            InputNegativeNetId = inputNegativeNetId,
            OutputPositiveNetId = outputPositiveNetId,
            OutputNegativeNetId = outputNegativeNetId,
            InputPortLabel = inputLabel,
            OutputPortLabel = outputLabel,
            GainQuantityLabel = gainQuantity,
            GainUnit = gainUnit,
            FrequencyStartHz = frequencyStartHz,
            FrequencyStopHz = frequencyStopHz,
            PointsPerDecade = pointsPerDecade
        };

        for (int i = 0; i <= stepCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double frequency = frequencyStartHz * Math.Pow(10.0, (double)i / pointsPerDecade);
            if (i == stepCount) frequency = frequencyStopHz;
            request.FrequencyHz = frequency;
            if (!TrySolveCore(request, out var solution, out error, cancellationToken) || solution == null)
            {
                error = $"扫频在 f = {frequency:G6} Hz 处失败：{error}";
                return false;
            }
            var gain = solution.NodePhasor(outputPositiveNetId) - solution.NodePhasor(outputNegativeNetId);
            sweep.Points.Add(new AcSweepPoint { FrequencyHz = frequency, Gain = gain });
        }

        if (!string.IsNullOrEmpty(excitationDescription)) sweep.Notes.Add(excitationDescription);

        // Passband-reference probes: sample the transfer gain two to three decades beyond
        // both sweep edges. Without them a low-pass whose real cutoff lies below the sweep
        // start would look "flat near its window maximum", and the relative -3 dB rule
        // would fabricate a cutoff inside a window that is entirely in the stopband.
        var probeMagnitudes = new List<(double FrequencyHz, double Magnitude)>();
        foreach (double factor in new[] { 10.0, 100.0, 1000.0 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (double probeFrequency in new[] { frequencyStartHz / factor, frequencyStopHz * factor })
            {
                if (!double.IsFinite(probeFrequency) || probeFrequency < 1e-12 || probeFrequency > 1e18) continue;
                request.FrequencyHz = probeFrequency;
                if (!TrySolveCore(request, out var probeSolution, out _, cancellationToken) || probeSolution == null) continue;
                var probeGain = probeSolution.NodePhasor(outputPositiveNetId) - probeSolution.NodePhasor(outputNegativeNetId);
                if (double.IsFinite(probeGain.Magnitude))
                {
                    probeMagnitudes.Add((probeFrequency, probeGain.Magnitude));
                }
            }
        }

        EstimateCutoffs(sweep, probeMagnitudes);
        result = sweep;
        error = string.Empty;
        return true;
    }

    private string BuildPortLabel(int positiveNetId, int negativeNetId)
    {
        string positive = _netById.TryGetValue(positiveNetId, out var p) ? p.Name : $"Net{positiveNetId}";
        string negative = _netById.TryGetValue(negativeNetId, out var n) ? n.Name : $"Net{negativeNetId}";
        return $"{positive} – {negative}";
    }

    private static void EstimateCutoffs(
        AcSweepResult sweep,
        IReadOnlyList<(double FrequencyHz, double Magnitude)> probeMagnitudes)
    {
        var points = sweep.Points;
        if (points.Count < 3)
        {
            sweep.CutoffEstimateAvailable = false;
            sweep.CutoffUnavailableReason = "扫频点数不足，截止估计不可用";
            sweep.Notes.Add(sweep.CutoffUnavailableReason);
            return;
        }

        // Reference passband gain: the largest gain among the swept points and the
        // out-of-window probes. Cutoff detection is relative to this reference, so a
        // window entirely inside a stopband can never manufacture a -3 dB point.
        double windowMaxMagnitude = points.Max(p => p.GainMagnitude);
        double referenceMagnitude = windowMaxMagnitude;
        bool referenceRaisedByProbe = false;
        foreach (var probe in probeMagnitudes)
        {
            if (!double.IsFinite(probe.Magnitude)) continue;
            if (probe.Magnitude > referenceMagnitude * (1.0 + 1e-12))
            {
                referenceMagnitude = probe.Magnitude;
                referenceRaisedByProbe = true;
            }
        }
        sweep.ReferenceGainDb = ToDecibel(referenceMagnitude);
        double thresholdDb = sweep.ReferenceGainDb - 3.0000001;

        if (referenceRaisedByProbe)
        {
            sweep.Notes.Add(
                $"通带参考增益 {sweep.ReferenceGainDb.ToString("0.##", CultureInfo.InvariantCulture)} dB 由窗口外探针频率确认" +
                $"（窗口内最高 {ToDecibel(windowMaxMagnitude).ToString("0.##", CultureInfo.InvariantCulture)} dB）");
        }

        int maxIndex = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].GainDb > points[maxIndex].GainDb) maxIndex = i;
        }

        bool anyPointInPassband = false;
        foreach (var point in points)
        {
            if (point.GainDb >= thresholdDb) { anyPointInPassband = true; break; }
        }

        if (!anyPointInPassband)
        {
            sweep.PassbandInWindow = false;
            sweep.CutoffEstimateAvailable = false;
            sweep.LowerCutoffHz = null;
            sweep.UpperCutoffHz = null;
            sweep.BandwidthHz = null;
            sweep.CutoffUnavailableReason =
                $"扫频窗口 {FormatFrequency(sweep.FrequencyStartHz)}–{FormatFrequency(sweep.FrequencyStopHz)} 内没有任何频点达到通带参考增益 " +
                $"{sweep.ReferenceGainDb.ToString("0.##", CultureInfo.InvariantCulture)} dB 的 -3 dB 以内" +
                $"（窗口内最高 {ToDecibel(windowMaxMagnitude).ToString("0.##", CultureInfo.InvariantCulture)} dB）；" +
                "扫频窗口未覆盖真实 -3 dB 截止频率，截止频率与带宽估计不可用";
            sweep.Notes.Add(sweep.CutoffUnavailableReason);
            return;
        }

        sweep.PassbandInWindow = true;

        // Passband = contiguous region around the window maximum whose gain stays within
        // 3 dB of the reference.
        int lowIndex = maxIndex;
        while (lowIndex > 0 && points[lowIndex - 1].GainDb >= thresholdDb) lowIndex--;
        int highIndex = maxIndex;
        while (highIndex < points.Count - 1 && points[highIndex + 1].GainDb >= thresholdDb) highIndex++;

        // Probes tell whether a true -3 dB crossing is hiding outside the window. If a
        // probe outside the window is already below the reference -3 dB, then the
        // crossing exists outside the window and must not be replaced by a window edge.
        bool anyLowerProbeBelow = false;
        bool anyUpperProbeBelow = false;
        foreach (var probe in probeMagnitudes)
        {
            if (!double.IsFinite(probe.Magnitude)) continue;
            if (ToDecibel(probe.Magnitude) >= thresholdDb) continue;
            if (probe.FrequencyHz < sweep.FrequencyStartHz) anyLowerProbeBelow = true;
            else if (probe.FrequencyHz > sweep.FrequencyStopHz) anyUpperProbeBelow = true;
        }

        if (lowIndex > 0)
        {
            sweep.LowerCutoffHz = InterpolateCrossing(points[lowIndex - 1], points[lowIndex], thresholdDb);
        }
        else if (anyLowerProbeBelow)
        {
            sweep.LowerCutoffBelowWindow = true;
            sweep.CutoffEstimateAvailable = false;
            sweep.Notes.Add(
                $"真实低频 -3 dB 交叉位于扫描窗口下限 {FormatFrequency(sweep.FrequencyStartHz)} 以下" +
                "（窗口外探针增益已低于通带参考 -3 dB），窗口内无法估计下限截止频率");
        }
        else
        {
            sweep.Notes.Add("低频端无 -3 dB 交叉：窗口下限处增益仍处于通带参考 -3 dB 以内，通带延伸至扫描下限以下");
        }

        if (highIndex < points.Count - 1)
        {
            sweep.UpperCutoffHz = InterpolateCrossing(points[highIndex], points[highIndex + 1], thresholdDb);
        }
        else if (anyUpperProbeBelow)
        {
            sweep.UpperCutoffAboveWindow = true;
            sweep.CutoffEstimateAvailable = false;
            sweep.Notes.Add(
                $"真实高频 -3 dB 交叉位于扫描窗口上限 {FormatFrequency(sweep.FrequencyStopHz)} 以上" +
                "（窗口外探针增益已低于通带参考 -3 dB），窗口内无法估计上限截止频率");
        }
        else
        {
            sweep.Notes.Add("高频端无 -3 dB 交叉：窗口上限处增益仍处于通带参考 -3 dB 以内，通带延伸至扫描上限以上");
        }

        bool lowerKnown = sweep.LowerCutoffHz.HasValue;
        bool upperKnown = sweep.UpperCutoffHz.HasValue;

        if (!sweep.CutoffEstimateAvailable)
        {
            sweep.BandwidthHz = null;
            sweep.Notes.Add("截止频率位于扫描窗口之外，带宽估计不可用");
        }
        else if (lowerKnown && upperKnown)
        {
            sweep.BandwidthHz = sweep.UpperCutoffHz!.Value - sweep.LowerCutoffHz!.Value;
        }
        else if (!lowerKnown && upperKnown && !sweep.LowerCutoffBelowWindow)
        {
            // Passband reaches the sweep start: treat DC as the low band edge.
            sweep.BandwidthHz = sweep.UpperCutoffHz;
            sweep.Notes.Add("低频端通带延伸至扫描下限以下，带宽以 DC（0 Hz）为低频起点估算");
        }
        else if (lowerKnown && !upperKnown && !sweep.UpperCutoffAboveWindow)
        {
            sweep.Notes.Add("高频端通带延伸至扫描上限以上，带宽无法用有限上下截止频率表示");
        }
        else
        {
            sweep.Notes.Add("通带覆盖整个扫描范围，未检测到 -3 dB 截止频率");
        }
    }

    private static double InterpolateCrossing(AcSweepPoint a, AcSweepPoint b, double thresholdDb)
    {
        double denominator = b.GainDb - a.GainDb;
        if (Math.Abs(denominator) < 1e-15) return a.FrequencyHz;
        double t = (thresholdDb - a.GainDb) / denominator;
        t = Math.Clamp(t, 0.0, 1.0);
        double logA = Math.Log10(a.FrequencyHz);
        double logB = Math.Log10(b.FrequencyHz);
        return Math.Pow(10.0, logA + t * (logB - logA));
    }

    // Display-only helper for sweep-window boundary notes: teaching convention is
    // at most two decimals (see Services/TeachingDisplay.cs); the sweep/cutoff
    // numerical decisions never use this string.
    private static string FormatFrequency(double frequencyHz) => frequencyHz >= 1e6
        ? $"{frequencyHz / 1e6:0.##} MHz"
        : frequencyHz >= 1e3 ? $"{frequencyHz / 1e3:0.##} kHz" : $"{frequencyHz:0.##} Hz";

    /// <summary>20·log10(|H|), clamped to -300 dB for numerical zero.</summary>
    public static double ToDecibel(double magnitude)
    {
        if (!double.IsFinite(magnitude) || magnitude <= 0) return -300.0;
        return 20.0 * Math.Log10(magnitude);
    }

    /// <summary>
    /// Heuristic output-port selection: among non-ground nodes not belonging to the
    /// excitation port, pick the one with the largest phasor magnitude.
    /// </summary>
    public bool TryFindOutputNode(
        LinearSolution solution,
        IEnumerable<int> excludedNetIds,
        out LinearNetNode? output)
    {
        var excluded = new HashSet<int>(excludedNetIds ?? Array.Empty<int>());
        output = null;
        double bestMagnitude = -1.0;
        foreach (var node in _nodes)
        {
            if (excluded.Contains(node.NetId)) continue;
            double magnitude = solution.NodePhasor(node.NetId).Magnitude;
            if (magnitude > bestMagnitude + 1e-15)
            {
                bestMagnitude = magnitude;
                output = node;
            }
        }
        return output != null;
    }

    // =====================================================================
    // Core assembly and solution
    // =====================================================================

    private sealed class SolveRequest
    {
        public bool IsAc;
        public double FrequencyHz;
        /// <summary>Zero every independent source (shorted V source / open I source).</summary>
        public bool ZeroIndependentSources;
        /// <summary>Force specific independent-source values (used for normalized excitation).</summary>
        public Dictionary<string, Complex>? SourceOverrides;
        public bool AddTestVoltageSource;
        public Complex TestVoltageValue = Complex.One;
        public int TestVoltagePositiveNetId = -1;
        public int TestVoltageNegativeNetId = -1;
        public bool AddInjectionCurrent;
        public Complex InjectionCurrent = Complex.One;
        public int InjectionPositiveNetId = -1;
        public int InjectionNegativeNetId = -1;
    }

    private bool TrySolveCore(
        SolveRequest request,
        out LinearSolution? solution,
        out string error,
        CancellationToken cancellationToken)
    {
        solution = null;
        error = string.Empty;

        if (request.IsAc && (!double.IsFinite(request.FrequencyHz) || request.FrequencyHz <= 0))
        {
            error = $"交流相量分析的频率必须是有限正数（当前 {request.FrequencyHz:G6} Hz）。";
            return false;
        }

        if (!TryAssembleSystem(request, cancellationToken, out var a, out var b, out int dimension, out error) ||
            a == null || b == null)
        {
            return false;
        }

        if (!TrySolveComplexLinearSystem(a, b, dimension, cancellationToken, out var x, out error))
        {
            return false;
        }

        var solved = new LinearSolution
        {
            IsAc = request.IsAc,
            FrequencyHz = request.FrequencyHz,
            MatrixDimension = dimension,
            GroundNetId = _groundNet.NetId
        };

        for (int i = 0; i < _nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            solved.AddNode(_nodes[i].NetId, _nodes[i].Name, x[_nodeRowByNet[_nodeNets[i]]]);
        }

        foreach (var device in _devices)
        {
            if (device.BranchVariableIndex >= 0)
            {
                string description = device.Kind switch
                {
                    LinearDeviceKind.DcVoltageSource or LinearDeviceKind.AcVoltageSource => "支路电流（正端→负端）",
                    LinearDeviceKind.Inductor => "电感电流（引脚1→引脚2）",
                    LinearDeviceKind.Vcvs or LinearDeviceKind.Ccvs => "受控电压源输出支路电流（OUT1→OUT2）",
                    _ => "支路电流"
                };
                solved.AddBranch(device.ElementId, description, false, x[_nodes.Count + device.BranchVariableIndex]);
            }
            if (device.ControlBranchVariableIndex >= 0)
            {
                solved.AddBranch(device.ElementId, "控制支路电流（IN1→IN2）", true, x[_nodes.Count + device.ControlBranchVariableIndex]);
            }
        }

        solution = solved;
        return true;
    }

    /// <summary>
    /// Public snapshot of the DC MNA system (independent sources at their natural DC
    /// values) for pedagogical display. Rows/columns follow <see cref="VariableLabels"/>.
    /// </summary>
    public bool TryAssembleDcSystem(
        out Complex[,]? matrix,
        out Complex[]? rightHandSide,
        out string error,
        CancellationToken cancellationToken = default)
    {
        return TryAssembleSystem(new SolveRequest { IsAc = false }, cancellationToken, out matrix, out rightHandSide, out _, out error);
    }

    /// <summary>
    /// Public snapshot of the AC MNA system at one frequency (independent AC sources at
    /// their amplitude phasors, independent DC sources zeroed) for pedagogical display.
    /// </summary>
    public bool TryAssembleAcSystem(
        double frequencyHz,
        out Complex[,]? matrix,
        out Complex[]? rightHandSide,
        out string error,
        CancellationToken cancellationToken = default)
    {
        matrix = null;
        rightHandSide = null;
        if (!double.IsFinite(frequencyHz) || frequencyHz <= 0)
        {
            error = $"交流相量分析的频率必须是有限正数（当前 {frequencyHz:G6} Hz）。";
            return false;
        }
        return TryAssembleSystem(
            new SolveRequest { IsAc = true, FrequencyHz = frequencyHz },
            cancellationToken, out matrix, out rightHandSide, out _, out error);
    }

    private bool TryAssembleSystem(
        SolveRequest request,
        CancellationToken cancellationToken,
        out Complex[,]? matrix,
        out Complex[]? rightHandSide,
        out int dimension,
        out string error)
    {
        matrix = null;
        rightHandSide = null;
        error = string.Empty;

        double omega = request.IsAc ? 2.0 * Math.PI * request.FrequencyHz : 0.0;
        int extraVariables = request.AddTestVoltageSource ? 1 : 0;
        dimension = _nodes.Count + _branchVariableCount + extraVariables;
        if (dimension <= 0)
        {
            error = "网络没有可求解的矩阵变量。";
            return false;
        }
        if (dimension > MaxMatrixDimension)
        {
            error = $"MNA 矩阵变量数 {dimension} 超过上限 {MaxMatrixDimension}。";
            return false;
        }

        var a = new Complex[dimension, dimension];
        var b = new Complex[dimension];

        foreach (var device in _devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int row = device.BranchVariableIndex >= 0 ? _nodes.Count + device.BranchVariableIndex : -1;
            int controlRow = device.ControlBranchVariableIndex >= 0 ? _nodes.Count + device.ControlBranchVariableIndex : -1;
            StampDevice(a, b, device, request, omega, row, controlRow);
        }

        int testRow = _nodes.Count + _branchVariableCount;
        if (request.AddTestVoltageSource)
        {
            if (!TryGetNodeRow(request.TestVoltagePositiveNetId, out int testPositive, out error) ||
                !TryGetNodeRow(request.TestVoltageNegativeNetId, out int testNegative, out error))
            {
                return false;
            }
            StampBranchKcl(a, testRow, testPositive, testNegative);
            StampBranchConstraint(a, testRow, testPositive, testNegative);
            b[testRow] += request.TestVoltageValue;
        }

        if (request.AddInjectionCurrent)
        {
            if (!TryGetNodeRow(request.InjectionPositiveNetId, out int injectionPositive, out error) ||
                !TryGetNodeRow(request.InjectionNegativeNetId, out int injectionNegative, out error))
            {
                return false;
            }
            if (injectionPositive >= 0) b[injectionPositive] += request.InjectionCurrent;
            if (injectionNegative >= 0) b[injectionNegative] -= request.InjectionCurrent;
        }

        matrix = a;
        rightHandSide = b;
        return true;
    }

    private bool TryGetNodeRow(int netId, out int row, out string error)
    {
        row = -1;
        error = string.Empty;
        if (!_netById.TryGetValue(netId, out var net))
        {
            error = $"节点 NetId={netId} 不在本线性网络中。";
            return false;
        }
        if (ReferenceEquals(net, _groundNet)) return true; // ground row is the reference (skipped)
        if (_nodeRowByNet.TryGetValue(net, out row)) return true;
        error = $"节点 '{net.Name}' 未参与任何受支持的线性器件连接，不能作为端口节点。";
        return false;
    }

    private void StampDevice(
        Complex[,] a,
        Complex[] b,
        LinearDevice device,
        SolveRequest request,
        double omega,
        int branchRow,
        int controlRow)
    {
        int n1 = NodeRowOrMinusOne(device.PositiveNet);
        int n2 = NodeRowOrMinusOne(device.NegativeNet);
        int c1 = device.ControlPositiveNet == null ? -1 : NodeRowOrMinusOne(device.ControlPositiveNet);
        int c2 = device.ControlNegativeNet == null ? -1 : NodeRowOrMinusOne(device.ControlNegativeNet);

        switch (device.Kind)
        {
            case LinearDeviceKind.Resistor:
            {
                StampAdmittance(a, n1, n2, new Complex(1.0 / device.ValueSi, 0.0));
                break;
            }
            case LinearDeviceKind.Capacitor:
            {
                if (!request.IsAc) break; // DC: capacitor is an open circuit
                StampAdmittance(a, n1, n2, new Complex(0.0, omega * device.ValueSi));
                break;
            }
            case LinearDeviceKind.Inductor:
            {
                // v_pos - v_neg - Z·i = 0，DC 时 Z = 0（短路支路）。
                EnsureBranchRow(device, branchRow);
                StampBranchKcl(a, branchRow, n1, n2);
                StampBranchConstraint(a, branchRow, n1, n2);
                if (request.IsAc) a[branchRow, branchRow] -= new Complex(0.0, omega * device.ValueSi);
                break;
            }
            case LinearDeviceKind.DcVoltageSource:
            case LinearDeviceKind.AcVoltageSource:
            {
                EnsureBranchRow(device, branchRow);
                StampBranchKcl(a, branchRow, n1, n2);
                StampBranchConstraint(a, branchRow, n1, n2);
                b[branchRow] += ResolveIndependentSourceValue(device, request);
                break;
            }
            case LinearDeviceKind.DcCurrentSource:
            case LinearDeviceKind.AcCurrentSource:
            {
                var value = ResolveIndependentSourceValue(device, request);
                if (n1 >= 0) b[n1] += value;
                if (n2 >= 0) b[n2] -= value;
                break;
            }
            case LinearDeviceKind.Vcvs:
            {
                // 约束：V(OUT1) - V(OUT2) = μ (V(IN1) - V(IN2))
                EnsureBranchRow(device, branchRow);
                StampBranchKcl(a, branchRow, n1, n2);
                StampBranchConstraint(a, branchRow, n1, n2);
                if (c1 >= 0) a[branchRow, c1] -= device.ValueSi;
                if (c2 >= 0) a[branchRow, c2] += device.ValueSi;
                break;
            }
            case LinearDeviceKind.Vccs:
            {
                // I(OUT1 -> OUT2) = gm (V(IN1) - V(IN2))：KCL 在 OUT1 +I、OUT2 -I。
                if (n1 >= 0) { if (c1 >= 0) a[n1, c1] += device.ValueSi; if (c2 >= 0) a[n1, c2] -= device.ValueSi; }
                if (n2 >= 0) { if (c1 >= 0) a[n2, c1] -= device.ValueSi; if (c2 >= 0) a[n2, c2] += device.ValueSi; }
                break;
            }
            case LinearDeviceKind.Ccvs:
            case LinearDeviceKind.Cccs:
            {
                // 控制端 0 V 电压支路：V(IN1) = V(IN2)，新增电流变量 I_ctrl = IN1→IN2。
                StampBranchKcl(a, controlRow, c1, c2);
                StampBranchConstraint(a, controlRow, c1, c2);
                if (device.Kind == LinearDeviceKind.Ccvs)
                {
                    // 输出支路约束：V(OUT1) - V(OUT2) = r · I_ctrl
                    EnsureBranchRow(device, branchRow);
                    StampBranchKcl(a, branchRow, n1, n2);
                    StampBranchConstraint(a, branchRow, n1, n2);
                    a[branchRow, controlRow] -= device.ValueSi;
                }
                else
                {
                    // 输出电流 I(OUT1 -> OUT2) = β · I_ctrl：KCL 在 OUT1 +I、OUT2 -I。
                    if (n1 >= 0) a[n1, controlRow] += device.ValueSi;
                    if (n2 >= 0) a[n2, controlRow] -= device.ValueSi;
                }
                break;
            }
            default:
                throw new InvalidOperationException($"未处理的线性器件类型 {device.Kind}。");
        }
    }

    private static void EnsureBranchRow(LinearDevice device, int branchRow)
    {
        if (branchRow < 0)
        {
            throw new InvalidOperationException($"器件 {device.ElementId} 缺少支路电流变量。");
        }
    }

    private int NodeRowOrMinusOne(ElectricalNet net)
    {
        if (ReferenceEquals(net, _groundNet)) return -1;
        return _nodeRowByNet.TryGetValue(net, out var row) ? row : -1;
    }

    private static void StampAdmittance(Complex[,] a, int n1, int n2, Complex y)
    {
        if (n1 >= 0) a[n1, n1] += y;
        if (n2 >= 0) a[n2, n2] += y;
        if (n1 >= 0 && n2 >= 0)
        {
            a[n1, n2] -= y;
            a[n2, n1] -= y;
        }
    }

    private static void StampBranchKcl(Complex[,] a, int branchRow, int positiveRow, int negativeRow)
    {
        if (positiveRow >= 0) a[positiveRow, branchRow] += Complex.One;
        if (negativeRow >= 0) a[negativeRow, branchRow] -= Complex.One;
    }

    private static void StampBranchConstraint(Complex[,] a, int branchRow, int positiveRow, int negativeRow)
    {
        if (positiveRow >= 0) a[branchRow, positiveRow] += Complex.One;
        if (negativeRow >= 0) a[branchRow, negativeRow] -= Complex.One;
    }

    private Complex ResolveIndependentSourceValue(LinearDevice device, SolveRequest request)
    {
        if (request.SourceOverrides != null && request.SourceOverrides.TryGetValue(device.ElementId, out var overridden))
        {
            return overridden;
        }
        if (request.ZeroIndependentSources) return Complex.Zero;

        bool naturalValueApplies = request.IsAc ? device.IsAcSource : !device.IsAcSource;
        return naturalValueApplies ? new Complex(device.ValueSi, 0.0) : Complex.Zero;
    }

    /// <summary>
    /// Complex Gaussian elimination with partial pivoting, row equilibration and a
    /// relative-residual verification pass.
    /// </summary>
    private static bool TrySolveComplexLinearSystem(
        Complex[,] matrix,
        Complex[] rightHandSide,
        int dimension,
        CancellationToken cancellationToken,
        out Complex[] solution,
        out string error)
    {
        solution = new Complex[dimension];
        error = string.Empty;

        // Row equilibration: divide each equation by its largest coefficient magnitude.
        for (int row = 0; row < dimension; row++)
        {
            double maxMagnitude = 0.0;
            for (int column = 0; column < dimension; column++)
            {
                double magnitude = Complex.Abs(matrix[row, column]);
                if (!double.IsFinite(magnitude))
                {
                    error = "MNA 矩阵中出现非有限系数，无法求解。";
                    return false;
                }
                if (magnitude > maxMagnitude) maxMagnitude = magnitude;
            }
            if (maxMagnitude > 0)
            {
                var scale = new Complex(1.0 / maxMagnitude, 0.0);
                for (int column = 0; column < dimension; column++) matrix[row, column] *= scale;
                rightHandSide[row] *= scale;
            }
        }

        // Snapshot for the residual check (rows are equilibrated; equivalences preserved).
        var matrixCopy = (Complex[,])matrix.Clone();
        var rhsCopy = (Complex[])rightHandSide.Clone();
        var a = matrix;
        var b = rightHandSide;

        for (int column = 0; column < dimension; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int pivotRow = column;
            double bestMagnitude = Complex.Abs(a[column, column]);
            for (int row = column + 1; row < dimension; row++)
            {
                double magnitude = Complex.Abs(a[row, column]);
                if (!double.IsFinite(magnitude))
                {
                    error = "MNA 矩阵中出现非有限系数，无法求解。";
                    return false;
                }
                if (magnitude > bestMagnitude)
                {
                    bestMagnitude = magnitude;
                    pivotRow = row;
                }
            }

            if (!double.IsFinite(bestMagnitude) || bestMagnitude <= PivotRelativeTolerance)
            {
                error = "网络矩阵奇异或病态（主元过小）：请检查悬空节点、缺失直流通路（电容开路造成）、" +
                        "重复/矛盾理想电压源、短路的理想电压源回路或受控源形成的退化约束。";
                return false;
            }

            if (pivotRow != column)
            {
                for (int k = column; k < dimension; k++)
                {
                    (a[column, k], a[pivotRow, k]) = (a[pivotRow, k], a[column, k]);
                }
                (b[column], b[pivotRow]) = (b[pivotRow], b[column]);
            }

            var pivot = a[column, column];
            for (int row = column + 1; row < dimension; row++)
            {
                var factor = a[row, column] / pivot;
                if (factor == Complex.Zero) continue;
                a[row, column] = Complex.Zero;
                for (int k = column + 1; k < dimension; k++)
                {
                    a[row, k] -= factor * a[column, k];
                }
                b[row] -= factor * b[column];
            }
        }

        for (int row = dimension - 1; row >= 0; row--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sum = b[row];
            for (int column = row + 1; column < dimension; column++)
            {
                sum -= a[row, column] * solution[column];
            }
            var diagonal = a[row, row];
            if (Complex.Abs(diagonal) <= PivotRelativeTolerance)
            {
                error = "网络矩阵奇异或病态（回代主元过小），无法得到可信结果。";
                return false;
            }
            solution[row] = sum / diagonal;
            if (!double.IsFinite(solution[row].Real) || !double.IsFinite(solution[row].Imaginary))
            {
                error = "网络求解产生非有限值（NaN/∞），未生成有效结果。";
                return false;
            }
        }

        // Relative residual verification: reject unreliable "solutions" that pass
        // elimination but do not satisfy the original equations.
        double residualNormSquared = 0.0;
        double rhsNormSquared = 0.0;
        double matrixNorm = 0.0;
        double solutionNorm = 0.0;
        for (int row = 0; row < dimension; row++)
        {
            var accumulated = Complex.Zero;
            for (int column = 0; column < dimension; column++)
            {
                accumulated += matrixCopy[row, column] * solution[column];
                matrixNorm = Math.Max(matrixNorm, Complex.Abs(matrixCopy[row, column]));
            }
            var residual = accumulated - rhsCopy[row];
            residualNormSquared += residual.Magnitude * residual.Magnitude;
            rhsNormSquared += rhsCopy[row].Magnitude * rhsCopy[row].Magnitude;
            solutionNorm = Math.Max(solutionNorm, solution[row].Magnitude);
        }
        double residualNorm = Math.Sqrt(residualNormSquared);
        double rhsNorm = Math.Sqrt(rhsNormSquared);
        double denominator = matrixNorm * solutionNorm + rhsNorm + 1e-300;
        if (!double.IsFinite(residualNorm) || residualNorm / denominator > ResidualRelativeTolerance)
        {
            error = "网络矩阵病态（残差校验未通过），数值结果不可信；请检查元件数量级差异或受控源增益。";
            return false;
        }

        return true;
    }

    // =====================================================================
    // Internal comparer: reference equality for ElectricalNet
    // =====================================================================

    private sealed class ReferenceEqualityComparer : IEqualityComparer<ElectricalNet>
    {
        public static readonly ReferenceEqualityComparer Instance = new();
        public bool Equals(ElectricalNet? x, ElectricalNet? y) => ReferenceEquals(x, y);
        public int GetHashCode(ElectricalNet obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
