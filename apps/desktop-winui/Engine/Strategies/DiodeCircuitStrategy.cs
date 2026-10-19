// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/DiodeCircuitStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical strategy for diode circuits (rectifiers, limiters,
//              clampers, peak detectors and generic diode DC bias). Subtype
//              names are part of the classification contract:
//                HalfWaveRectifier, HalfWaveRectifierFiltered, BridgeRectifier,
//                BridgeRectifierFiltered, SeriesLimiter, DoubleLimiter,
//                Clamper, PeakDetector, DiodeBias.
//
//              Waveform circuits use the piecewise-linear constant-voltage-drop
//              model (VF from the element material: Si 0.7 V / Ge 0.3 V) and
//              textbook analytic formulas. The generic DC bias subtype solves the
//              Shockley equation with a clamped-exponent Newton iteration.
//
// OWNER: engine-diode.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine.Nonlinear;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace CircuitAssistant.Engine.Strategies;

/// <summary>
/// Diode teaching strategy. Waveform circuits use the piecewise-linear constant
/// drop model and textbook analytic formulas; arbitrary resistor-diode-DC-source
/// networks use the Shockley model with a Newton-Raphson operating point.
/// </summary>
public class DiodeCircuitStrategy : ICircuitStrategy
{
    private readonly string _subtype;

    public DiodeCircuitStrategy(string subtype)
    {
        _subtype = string.IsNullOrWhiteSpace(subtype) ? "DiodeBias" : subtype;
    }

    public CircuitTopologyType TopologyType => CircuitTopologyType.DiodeCircuit;

    public string SubtypeName => _subtype;

    /// <summary>
    /// Classifies the diode circuit family from the extracted netlist. Called by
    /// <see cref="TopologyClassifier"/>. Returns null only when no diode exists.
    /// The detection order is most-specific first:
    /// bridge (≥4 diodes) → DC-only bias → series-capacitor clamper → double
    /// limiter → single shunt-diode limiter → single series-diode rectifier /
    /// peak detector family. Unknown diode networks fall back to DiodeBias, whose
    /// SolveAsync then reports an explicit diode-specific failure.
    /// </summary>
    public static string? DetectSubtype(ElectricalNetlist netlist)
    {
        ArgumentNullException.ThrowIfNull(netlist);
        var scan = new CircuitScan(netlist);
        if (scan.Diodes.Count == 0) return null;

        // 1) Bridge rectifier family: four diodes, one optional filter capacitor.
        if (scan.Diodes.Count >= 4)
            return scan.Capacitors.Count > 0 ? "BridgeRectifierFiltered" : "BridgeRectifier";

        // 2) A purely DC network is solved as a generic Shockley bias point.
        if (scan.AcSources.Count == 0)
            return "DiodeBias";

        // 3) Series coupling capacitor + shunt diode = clamper.
        if (HasClamperCapacitor(scan))
            return "Clamper";

        // 4) Two complementary shunt diodes on one signal node = double limiter.
        if (scan.Diodes.Count == 2 && IsDoubleLimiter(scan))
            return "DoubleLimiter";

        // 5) A single shunt diode = series limiter (clipping).
        if (scan.Diodes.Count == 1 && IsShuntDiode(scan, scan.Diodes[0]))
            return "SeriesLimiter";

        // 6) A single series diode = rectifier or peak detector.
        if (scan.Diodes.Count == 1 && IsSeriesDiode(scan, scan.Diodes[0]))
        {
            var (anode, cathode) = scan.DiodeNets(scan.Diodes[0]);
            bool hasShuntFilterCap = scan.Capacitors.Any(cap => ShuntsToRail(scan, cap, cathode));
            if (!hasShuntFilterCap) return "HalfWaveRectifier";

            var sourceStarts = scan.AcSources.Select(source => scan.PinNet(source, "pos")).ToList();
            var reachable = scan.ReachableThroughResistors(sourceStarts);
            bool hasSeriesResistor = anode != null && reachable.TryGetValue(anode, out int resistorCount) && resistorCount >= 1;
            bool hasLoadToRail = scan.Resistors.Any(res => HasTerminal(scan, res, cathode) && HasRailTerminal(scan, res));

            // A peak/envelope detector is distinguished from a filtered rectifier
            // by the charging resistor in front of the diode (or by the absence of
            // a discharge/load resistor, i.e. a pure peak hold).
            return hasSeriesResistor || !hasLoadToRail ? "PeakDetector" : "HalfWaveRectifierFiltered";
        }

        // 7) Anything else (e.g. two series diodes): the generic bias solver
        //    reports an explicit failure naming the diode limitation.
        return "DiodeBias";
    }

    public Task<PedagogicalSolution> SolveAsync(
        ElectricalNetlist netlist,
        AnalysisParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (netlist == null) return Task.FromResult(Fail("网表为空，无法执行二极管电路分析。"));
            parameters ??= new AnalysisParameters();

            var solution = _subtype switch
            {
                "HalfWaveRectifier" => SolveHalfWaveRectifier(netlist, parameters),
                "HalfWaveRectifierFiltered" => SolveHalfWaveRectifierFiltered(netlist, parameters),
                "BridgeRectifier" => SolveBridgeRectifier(netlist, parameters),
                "BridgeRectifierFiltered" => SolveBridgeRectifierFiltered(netlist, parameters),
                "SeriesLimiter" => SolveSeriesLimiter(netlist, parameters),
                "DoubleLimiter" => SolveDoubleLimiter(netlist, parameters),
                "Clamper" => SolveClamper(netlist, parameters),
                "PeakDetector" => SolvePeakDetector(netlist, parameters),
                "DiodeBias" => SolveDiodeBias(netlist, parameters),
                _ => Fail("当前二极管教学模型不支持子类型 '" + _subtype +
                          "'（支持：HalfWaveRectifier、HalfWaveRectifierFiltered、BridgeRectifier、" +
                          "BridgeRectifierFiltered、SeriesLimiter、DoubleLimiter、Clamper、PeakDetector、DiodeBias）。")
            };
            return Task.FromResult(solution);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The teaching strategy must never leak an exception to the UI.
            return Task.FromResult(Fail($"求解过程中出现数值异常（{ex.GetType().Name}: {ex.Message}）。"));
        }
    }

    // ======================================================================
    // Half-wave rectifier (resistive load)
    // ======================================================================

    private PedagogicalSolution SolveHalfWaveRectifier(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1)
            return Fail($"二极管半波整流需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只（桥式整流请使用 BridgeRectifier 子类型）。");
        if (scan.AcSources.Count != 1) return Fail("二极管半波整流需要恰好 1 个交流电压源作为被整流的输入信号。");
        if (scan.DcSources.Count != 0) return Fail("纯电阻负载的半波整流电路不应包含直流偏置源。");
        if (scan.Capacitors.Count != 0) return Fail("检测到电容：半波整流 + 电容滤波请使用 HalfWaveRectifierFiltered 子类型。");
        if (scan.Resistors.Count != 1) return Fail("半波整流需要恰好 1 只负载电阻 R_L（接在输出节点与地之间）。");

        var diode = scan.Diodes[0];
        var ac = scan.AcSources[0];
        var (anode, cathode) = scan.DiodeNets(diode);
        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能形成整流输出的返回通路。");
        if (sourcePos == null || anode != sourcePos) return Fail("二极管阳极必须直接接在交流源正端（串联整流方向）。");
        if (cathode == null || IsGroundNet(scan, cathode)) return Fail("二极管阴极必须接整流输出节点，而不是地。");
        var load = scan.Resistors[0];
        if (!(HasTerminal(scan, load, cathode) && HasRailTerminal(scan, load)))
            return Fail("负载电阻必须连接在整流输出节点（二极管阴极）与地之间。");

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double rl = GetSiValue(netlist, load);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(rl) || rl <= 0) return Fail("负载电阻必须为有限正数。");
        if (vm <= vf)
            return Fail($"输入峰值 Vm={VfText(vm)} V 不超过二极管正向压降 VF={VfText(vf)} V，二极管在整个周期内截止，无法产生整流输出。");

        double vom = vm - vf;
        double vavg = vom / Math.PI;
        double vrms = vom / 2.0;
        double piv = vm;
        double idc = vavg / rl;
        double ipeak = vom / rl;
        double conductionAngleDeg = 180.0 - 2.0 * Math.Asin(Math.Clamp(vf / vm, 0.0, 1.0)) * 180.0 / Math.PI;
        string vfText = VfText(vf);

        var solution = NewSolution("二极管半波整流电路（电阻负载）教学分析");

        AddStep(solution,
            "交流输入峰值与二极管恒压降模型等效",
            "分段线性恒压降模型",
            $"将二极管等效为理想开关串联一个 V_F={vfText} V 的恒压源：正半周 v_S>V_F 时导通，其余时间截止；负半周承受反向电压而阻断。",
            @"V_{om}=V_m-V_F",
            $@"V_{{om}}={VfText(vm)}-{vfText}={VfText(vom)}\text{{ V}}",
            $"输出峰值 Vom = {Fmt(vom, "V")}",
            ("Vm", vm), ("VF", vf), ("Vom", vom));

        AddStep(solution,
            "半波整流波形、导通角与输出频率",
            "正弦波分段波形分析",
            $"仅正半周中 v_S>V_F 的区间有输出，导通角小于 180°；输出波形每周期只有一个半波，因此输出频率等于输入频率 {VfText(frequency)} Hz。",
            @"\theta=\pi-2\arcsin(V_F/V_m),\quad f_{out}=f",
            $@"\theta={VfText(conductionAngleDeg)}^\circ,\quad f_{{out}}={VfText(frequency)}\text{{ Hz}}",
            $"导通角 θ = {conductionAngleDeg:F1}°，输出频率 fout = {Fmt(frequency, "Hz")}",
            ("ConductionAngleDeg", conductionAngleDeg), ("fout", frequency));

        AddStep(solution,
            "输出平均值 Vavg 与有效值 Vrms",
            "半波正弦波积分平均",
            "对半波整流的输出波形积分求平均值和有效值：平均值 Vavg=Vom/π，有效值 Vrms=Vom/2。",
            @"V_{avg}=\frac{V_{om}}{\pi},\quad V_{rms}=\frac{V_{om}}{2}",
            $@"V_{{avg}}=\frac{{{VfText(vom)}}}{{\pi}}={VfText(vavg)}\text{{ V}},\quad V_{{rms}}=\frac{{{VfText(vom)}}}{{2}}={VfText(vrms)}\text{{ V}}",
            $"Vavg = {Fmt(vavg, "V")}，Vrms = {Fmt(vrms, "V")}",
            ("Vavg", vavg), ("Vrms", vrms));

        AddStep(solution,
            "二极管反向峰值电压 PIV 与负载电流",
            "反向峰值与负载电流校验",
            "负半周二极管截止时两端承受交流源负峰，PIV=Vm；输出平均值与负载电阻决定平均电流与导通峰值电流。",
            @"PIV=V_m,\quad I_{dc}=\frac{V_{avg}}{R_L},\quad I_{peak}=\frac{V_{om}}{R_L}",
            $@"PIV={VfText(vm)}\text{{ V}},\quad I_{{dc}}=\frac{{{VfText(vavg)}}}{{{VfText(rl)}}}={VfText(idc)}\text{{ A}},\quad I_{{peak}}={VfText(ipeak)}\text{{ A}}",
            $"PIV = {Fmt(piv, "V")}，Idc = {Fmt(idc, "A")}，Ipeak = {Fmt(ipeak, "A")}",
            ("PIV", piv), ("Idc", idc), ("Ipeak", ipeak), ("RL", rl));

        solution.SummaryText =
            $"二极管半波整流电路（{MaterialName(diode)}，VF={vfText} V）求解完成：输入峰值 Vm={Fmt(vm, "V")}，输出峰值 Vom=Vm−VF={Fmt(vom, "V")}，" +
            $"输出平均值 Vavg={Fmt(vavg, "V")}，有效值 Vrms={Fmt(vrms, "V")}，二极管反向峰值 PIV={Fmt(piv, "V")}，" +
            $"输出频率 fout={Fmt(frequency, "Hz")}（等于输入频率），平均负载电流 Idc={Fmt(idc, "A")}。";
        return solution;
    }

    // ======================================================================
    // Half-wave rectifier with capacitor filter
    // ======================================================================

    private PedagogicalSolution SolveHalfWaveRectifierFiltered(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1)
            return Fail($"半波整流 + 电容滤波需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只（桥式整流请使用 BridgeRectifierFiltered 子类型）。");
        if (scan.AcSources.Count != 1) return Fail("半波整流 + 电容滤波需要恰好 1 个交流电压源。");
        if (scan.DcSources.Count != 0) return Fail("半波整流 + 电容滤波电路不应包含直流偏置源。");
        if (scan.Capacitors.Count != 1) return Fail("半波整流 + 电容滤波需要恰好 1 只滤波电容；无电容时请使用 HalfWaveRectifier 子类型。");
        if (scan.Resistors.Count != 1) return Fail("滤波整流电路需要恰好 1 只负载电阻 R_L（与滤波电容并联）。");

        var diode = scan.Diodes[0];
        var ac = scan.AcSources[0];
        var capacitor = scan.Capacitors[0];
        var (anode, cathode) = scan.DiodeNets(diode);
        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能形成整流输出的返回通路。");
        if (sourcePos == null || anode != sourcePos) return Fail("二极管阳极必须直接接在交流源正端（串联整流方向）。");
        if (cathode == null || IsGroundNet(scan, cathode)) return Fail("二极管阴极必须接整流输出节点，而不是地。");
        if (!ShuntsToRail(scan, capacitor, cathode)) return Fail("滤波电容必须连接在整流输出节点与地之间。");
        var load = scan.Resistors[0];
        if (!(HasTerminal(scan, load, cathode) && HasRailTerminal(scan, load)))
            return Fail("负载电阻必须与滤波电容并联，接在整流输出节点与地之间。");

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double rl = GetSiValue(netlist, load);
        double c = GetSiValue(netlist, capacitor);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(rl) || rl <= 0) return Fail("负载电阻必须为有限正数。");
        if (!double.IsFinite(c) || c <= 0) return Fail("滤波电容必须为有限正数。");
        if (vm <= vf)
            return Fail($"输入峰值 Vm={VfText(vm)} V 不超过二极管正向压降 VF={VfText(vf)} V，二极管在整个周期内截止，电容无法被充电。");

        double vom = vm - vf;
        double vr = vom / (frequency * rl * c);
        double vdc = vom - vr / 2.0;
        if (!double.IsFinite(vr) || vdc <= 0)
            return Fail($"滤波时间常数 R_L·C={Fmt(rl * c, "s")} 相对输入周期过小（纹波已超过输出峰值），超出教材近似公式的适用范围。");

        double rippleFactor = vr / vdc;
        double piv = 2.0 * vm;
        double fout = frequency;
        double idc = vdc / rl;
        string vfText = VfText(vf);

        var solution = NewSolution("二极管半波整流 + 电容滤波电路教学分析");

        AddStep(solution,
            "带电容滤波的半波整流拓扑识别",
            "电容输入式滤波拓扑分析",
            $"交流源正半周经二极管向电容充电，负半周二极管截止、电容向负载放电。输出峰值由交流峰值与二极管压降决定：Vom=Vm−VF={VfText(vom)} V。",
            @"V_{om}=V_m-V_F",
            $@"V_{{om}}={VfText(vm)}-{vfText}={VfText(vom)}\text{{ V}}",
            $"电容充电峰值 Vom = {Fmt(vom, "V")}",
            ("Vm", vm), ("VF", vf), ("Vom", vom), ("C", c));

        AddStep(solution,
            "放电过程与纹波电压 Vr",
            "RC 放电近似（教材公式）",
            "二极管截止期间电容经负载电阻放电，放电时间近似为输入周期 T=1/f；当 R_L·C≫T 时纹波峰峰值近似为 Vr≈Vom/(f·R_L·C)。",
            @"V_r\approx\frac{V_{om}}{f R_L C}",
            $@"V_r=\frac{{{VfText(vom)}}}{{{VfText(frequency)}\times{VfText(rl)}\times{VfText(c)}}}={VfText(vr)}\text{{ V}}",
            $"纹波电压 Vr = {Fmt(vr, "V")}",
            ("Vr", vr), ("RL", rl), ("fout", fout));

        AddStep(solution,
            "输出直流电压 Vdc 与纹波系数",
            "平均值近似计算",
            "考虑纹波后输出直流电压近似为峰值减去半个纹波峰峰值；纹波系数 γ=Vr/Vdc 反映滤波效果。",
            @"V_{dc}=V_{om}-\frac{V_r}{2},\quad \gamma=\frac{V_r}{V_{dc}}",
            $@"V_{{dc}}={VfText(vom)}-\frac{{{VfText(vr)}}}{{2}}={VfText(vdc)}\text{{ V}},\quad \gamma=\frac{{{VfText(vr)}}}{{{VfText(vdc)}}}={VfText(rippleFactor)}",
            $"输出直流 Vdc = {Fmt(vdc, "V")}，纹波系数 γ = {VfText(rippleFactor)}",
            ("Vdc", vdc), ("RippleFactor", rippleFactor), ("Idc", idc));

        AddStep(solution,
            "二极管 PIV 与输出频率",
            "反向峰值与输出频率",
            "滤波电容使输出保持在 Vdc 附近，二极管截止时承受交流负峰与电容电压之和，反向峰值近似为 PIV≈2Vm；半波整流输出频率仍等于输入频率。",
            @"PIV\approx 2V_m,\quad f_{out}=f",
            $@"PIV\approx 2\times{VfText(vm)}={VfText(piv)}\text{{ V}},\quad f_{{out}}={VfText(fout)}\text{{ Hz}}",
            $"PIV ≈ {Fmt(piv, "V")}，输出频率 fout = {Fmt(fout, "Hz")}，平均负载电流 Idc = {Fmt(idc, "A")}",
            ("PIV", piv), ("Idc", idc));

        solution.SummaryText =
            $"二极管半波整流 + 电容滤波电路（{MaterialName(diode)}，VF={vfText} V）求解完成：输入峰值 Vm={Fmt(vm, "V")}，充电峰值 Vom={Fmt(vom, "V")}，" +
            $"纹波 Vr={Fmt(vr, "V")}，输出直流 Vdc={Fmt(vdc, "V")}，纹波系数 γ={VfText(rippleFactor)}，PIV≈{Fmt(piv, "V")}，输出频率 fout={Fmt(fout, "Hz")}。";
        return solution;
    }

    // ======================================================================
    // Bridge (full-wave) rectifier, with or without capacitor filter
    // ======================================================================

    private PedagogicalSolution SolveBridgeRectifier(ElectricalNetlist netlist, AnalysisParameters parameters)
        => SolveBridgeRectifierCore(netlist, parameters, filtered: false);

    private PedagogicalSolution SolveBridgeRectifierFiltered(ElectricalNetlist netlist, AnalysisParameters parameters)
        => SolveBridgeRectifierCore(netlist, parameters, filtered: true);

    private PedagogicalSolution SolveBridgeRectifierCore(ElectricalNetlist netlist, AnalysisParameters parameters, bool filtered)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (!TryValidateBridge(scan, filtered, out var outPositive, out var ac, out var load, out string? bridgeError))
            return Fail(bridgeError!);

        var diode = scan.Diodes.OrderBy(d => d.Id, StringComparer.Ordinal).First();
        double vm = GetSiValue(netlist, ac!);
        double frequency = ac!.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double rl = GetSiValue(netlist, load!);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(rl) || rl <= 0) return Fail("负载电阻必须为有限正数。");

        double vom = vm - 2.0 * vf;
        if (vom <= 0)
            return Fail($"输入峰值 Vm={VfText(vm)} V 不足以克服桥式整流两只串联二极管的正向压降 2VF={VfText(2 * vf)} V，输出为零。");

        CircuitElement? capacitor = null;
        double c = 0;
        if (filtered)
        {
            capacitor = scan.Capacitors[0];
            if (!ShuntsToRail(scan, capacitor, outPositive)) return Fail("滤波电容必须连接在桥式整流的正输出端与地之间。");
            c = GetSiValue(netlist, capacitor);
            if (!double.IsFinite(c) || c <= 0) return Fail("滤波电容必须为有限正数。");
        }

        double vavg = 2.0 * vom / Math.PI;
        double vrms = vom / Math.Sqrt(2.0);
        double fout = 2.0 * frequency;
        double piv = vm;
        double vr = filtered ? vom / (2.0 * frequency * rl * c) : 0.0;
        double vdc = filtered ? vom - vr / 2.0 : vom;
        double rippleFactor = filtered ? vr / vdc : 0.0;
        double idc = filtered ? vdc / rl : vavg / rl;
        double ipeak = vom / rl;
        if (filtered && (!double.IsFinite(vr) || vdc <= 0))
            return Fail($"滤波时间常数 R_L·C={Fmt(rl * c, "s")} 相对纹波周期过小（纹波已超过输出峰值），超出教材近似公式的适用范围。");
        string vfText = VfText(vf);
        string title = filtered ? "二极管桥式整流 + 电容滤波电路教学分析" : "二极管桥式全波整流电路教学分析";

        var solution = NewSolution(title);

        AddStep(solution,
            "桥式整流拓扑与两只二极管串联导通路径",
            "桥式整流换流分析",
            $"四只二极管组成桥式整流：每个半周有 2 只二极管串联导通，另 2 只承受反向电压。导通路径上的总压降为 2VF={VfText(2 * vf)} V，输出峰值 Vom=Vm−2VF。",
            @"V_{om}=V_m-2V_F",
            $@"V_{{om}}={VfText(vm)}-2\times{vfText}={VfText(vom)}\text{{ V}}",
            $"输出峰值 Vom = {Fmt(vom, "V")}（每只二极管 VF={vfText} V）",
            ("Vm", vm), ("VF", vf), ("Vom", vom));

        if (!filtered)
        {
            AddStep(solution,
                "全波整流输出平均值 Vavg 与有效值 Vrms",
                "全波正弦波积分",
                "桥式整流把输入负半周翻转为正，输出为周期为 T/2 的全波整流波形，因此平均值和有效值分别是半波情形的 2 倍与 √2 倍（相对输出峰值）。",
                @"V_{avg}=\frac{2V_{om}}{\pi},\quad V_{rms}=\frac{V_{om}}{\sqrt{2}}",
                $@"V_{{avg}}=\frac{{2\times{VfText(vom)}}}{{\pi}}={VfText(vavg)}\text{{ V}},\quad V_{{rms}}=\frac{{{VfText(vom)}}}{{\sqrt{{2}}}}={VfText(vrms)}\text{{ V}}",
                $"Vavg = {Fmt(vavg, "V")}，Vrms = {Fmt(vrms, "V")}",
                ("Vavg", vavg), ("Vrms", vrms));

            AddStep(solution,
                "每只二极管的 PIV 与二倍输出频率",
                "反向峰值与倍频输出",
                "桥式整流中截止二极管的反向电压被限制在交流峰值附近，每只二极管 PIV=Vm；全波整流输出频率是输入频率的 2 倍。",
                @"PIV=V_m,\quad f_{out}=2f",
                $@"PIV={VfText(vm)}\text{{ V}},\quad f_{{out}}=2\times{VfText(frequency)}={VfText(fout)}\text{{ Hz}}",
                $"每只二极管 PIV = {Fmt(piv, "V")}，输出频率 fout = {Fmt(fout, "Hz")}",
                ("PIV", piv), ("fout", fout));

            AddStep(solution,
                "负载电流与输出功率校核",
                "负载电流与功率校核",
                "输出平均值与负载电阻决定平均电流；导通峰值电流由输出峰值与负载电阻决定。",
                @"I_{dc}=\frac{V_{avg}}{R_L},\quad I_{peak}=\frac{V_{om}}{R_L}",
                $@"I_{{dc}}=\frac{{{VfText(vavg)}}}{{{VfText(rl)}}}={VfText(idc)}\text{{ A}},\quad I_{{peak}}={VfText(ipeak)}\text{{ A}}",
                $"Idc = {Fmt(idc, "A")}，Ipeak = {Fmt(ipeak, "A")}",
                ("Idc", idc), ("Ipeak", ipeak), ("RL", rl));
        }
        else
        {
            AddStep(solution,
                "滤波电容充电峰值与纹波电压",
                "电容输入式滤波近似",
                "桥式整流每个半周（周期 T/2）给电容充电一次，二极管截止期间电容向负载放电；全波滤波纹波峰峰值近似为 Vr≈Vom/(2f·R_L·C)。",
                @"V_r\approx\frac{V_{om}}{2f R_L C}",
                $@"V_r=\frac{{{VfText(vom)}}}{{2\times{VfText(frequency)}\times{VfText(rl)}\times{VfText(c)}}}={VfText(vr)}\text{{ V}}",
                $"纹波电压 Vr = {Fmt(vr, "V")}",
                ("Vr", vr), ("C", c), ("RL", rl));

            AddStep(solution,
                "输出直流电压 Vdc 与纹波系数",
                "平均值近似计算",
                "输出直流电压近似为峰值减去半个纹波峰峰值；纹波系数 γ=Vr/Vdc 反映滤波效果。",
                @"V_{dc}=V_{om}-\frac{V_r}{2},\quad \gamma=\frac{V_r}{V_{dc}}",
                $@"V_{{dc}}={VfText(vom)}-\frac{{{VfText(vr)}}}{{2}}={VfText(vdc)}\text{{ V}},\quad \gamma=\frac{{{VfText(vr)}}}{{{VfText(vdc)}}}={VfText(rippleFactor)}",
                $"输出直流 Vdc = {Fmt(vdc, "V")}，纹波系数 γ = {VfText(rippleFactor)}",
                ("Vdc", vdc), ("RippleFactor", rippleFactor));

            AddStep(solution,
                "每只二极管 PIV 与二倍输出频率",
                "反向峰值与倍频输出",
                "桥式滤波整流中每只二极管反向电压仍约为交流峰值，PIV=Vm；全波整流输出频率为输入频率的 2 倍。",
                @"PIV\approx V_m,\quad f_{out}=2f",
                $@"PIV\approx{VfText(vm)}\text{{ V}},\quad f_{{out}}=2\times{VfText(frequency)}={VfText(fout)}\text{{ Hz}}",
                $"每只二极管 PIV ≈ {Fmt(piv, "V")}，输出频率 fout = {Fmt(fout, "Hz")}，平均负载电流 Idc = {Fmt(idc, "A")}",
                ("PIV", piv), ("fout", fout), ("Idc", idc));
        }

        solution.SummaryText = filtered
            ? $"二极管桥式整流 + 电容滤波电路（{MaterialName(diode)}，每只 VF={vfText} V）求解完成：输入峰值 Vm={Fmt(vm, "V")}，输出峰值 Vom=Vm−2VF={Fmt(vom, "V")}，" +
              $"纹波 Vr={Fmt(vr, "V")}，输出直流 Vdc={Fmt(vdc, "V")}，纹波系数 γ={VfText(rippleFactor)}，每只二极管 PIV≈{Fmt(piv, "V")}，输出频率 fout={Fmt(fout, "Hz")}。"
            : $"二极管桥式全波整流电路（{MaterialName(diode)}，每只 VF={vfText} V）求解完成：输入峰值 Vm={Fmt(vm, "V")}，输出峰值 Vom=Vm−2VF={Fmt(vom, "V")}，" +
              $"输出平均值 Vavg={Fmt(vavg, "V")}，有效值 Vrms={Fmt(vrms, "V")}，每只二极管 PIV={Fmt(piv, "V")}，输出频率 fout={Fmt(fout, "Hz")}，平均负载电流 Idc={Fmt(idc, "A")}。";
        return solution;
    }

    private bool TryValidateBridge(
        CircuitScan scan,
        bool filtered,
        out ElectricalNet? outPositive,
        out CircuitElement? acSource,
        out CircuitElement? load,
        out string? error)
    {
        outPositive = null;
        acSource = null;
        load = null;
        error = null;

        if (scan.Diodes.Count != 4)
        {
            error = $"桥式整流需要恰好 4 只二极管，当前检测到 {scan.Diodes.Count} 只（半波整流请使用 HalfWaveRectifier 子类型）。";
            return false;
        }
        if (scan.AcSources.Count != 1) { error = "桥式整流需要恰好 1 个交流电压源。"; return false; }
        if (scan.DcSources.Count != 0) { error = "桥式整流器不应包含直流偏置源。"; return false; }
        if (filtered)
        {
            if (scan.Capacitors.Count != 1)
            {
                error = "桥式整流 + 电容滤波需要恰好 1 只滤波电容；无电容时请使用 BridgeRectifier 子类型。";
                return false;
            }
        }
        else if (scan.Capacitors.Count != 0)
        {
            error = "检测到滤波电容：请使用 BridgeRectifierFiltered 子类型。";
            return false;
        }
        if (scan.Resistors.Count != 1) { error = "桥式整流需要恰好 1 只负载电阻接在正输出端与地之间。"; return false; }
        if (scan.Diodes.Select(d => d.ForwardVoltageDrop).Distinct().Count() != 1)
        {
            error = "桥式整流四只二极管材料应一致（当前混合硅管/锗管，教学公式不适用）。";
            return false;
        }

        acSource = scan.AcSources[0];
        var sourcePos = scan.PinNet(acSource, "pos");
        var sourceNeg = scan.PinNet(acSource, "neg");
        if (sourcePos == null || sourceNeg == null || sourcePos == sourceNeg)
        {
            error = "交流源引脚未形成有效连接。";
            return false;
        }
        if (IsGroundNet(scan, sourcePos) || IsGroundNet(scan, sourceNeg))
        {
            error = "桥式整流的交流输入应浮接在桥的两个交流端之间，不能直接接地。";
            return false;
        }
        var acNets = new[] { sourcePos, sourceNeg };

        load = scan.Resistors[0];
        var (r1, r2) = scan.TwoTerminalNets(load);
        if (r1 == null || r2 == null)
        {
            error = "负载电阻引脚未形成有效连接。";
            return false;
        }
        ElectricalNet? candidateOut = IsGroundNet(scan, r1) && !IsGroundNet(scan, r2) ? r2
            : IsGroundNet(scan, r2) && !IsGroundNet(scan, r1) ? r1
            : null;
        if (candidateOut == null)
        {
            error = "负载电阻必须连接在桥式整流正输出端与地之间。";
            return false;
        }
        if (acNets.Contains(candidateOut))
        {
            error = "负载电阻不能直接接在交流源的交流端上。";
            return false;
        }

        var toPositive = scan.Diodes.Where(d => scan.DiodeNets(d).Cathode == candidateOut).ToList();
        var fromGround = scan.Diodes.Where(d => IsGroundNet(scan, scan.DiodeNets(d).Anode)).ToList();
        bool ok = toPositive.Count == 2 && fromGround.Count == 2;
        if (ok)
        {
            var anodes = toPositive.Select(d => scan.DiodeNets(d).Anode).ToList();
            var cathodes = fromGround.Select(d => scan.DiodeNets(d).Cathode).ToList();
            ok = anodes.All(n => n != null && acNets.Contains(n)) && anodes.Distinct().Count() == 2
                 && cathodes.All(n => n != null && acNets.Contains(n)) && cathodes.Distinct().Count() == 2;
        }
        if (!ok)
        {
            error = "未识别出标准桥式整流连接：每个交流端各应接 1 只二极管到正输出端、1 只二极管从地到该交流端，共 4 只。";
            return false;
        }
        outPositive = candidateOut;
        return true;
    }

    // ======================================================================
    // Series limiter (single diode to a DC rail)
    // ======================================================================

    private PedagogicalSolution SolveSeriesLimiter(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1)
            return Fail($"二极管串联限幅需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只（双向限幅请使用 DoubleLimiter 子类型）。");
        if (scan.AcSources.Count != 1) return Fail("二极管串联限幅需要恰好 1 个交流电压源。");
        if (scan.Capacitors.Count != 0) return Fail("二极管串联限幅电路不应包含电容（钳位电路请使用 Clamper 子类型）。");
        if (scan.Resistors.Count is < 1 or > 2)
            return Fail("二极管串联限幅需要恰好 1 只串联限流电阻 R（可选 1 只输出负载电阻 R_L）。");
        if (scan.DcSources.Count > 1) return Fail("二极管串联限幅最多支持 1 个直流偏置源。");

        var diode = scan.Diodes[0];
        var ac = scan.AcSources[0];
        var (anode, cathode) = scan.DiodeNets(diode);
        bool anodeRail = scan.IsRail(anode);
        bool cathodeRail = scan.IsRail(cathode);
        if (anodeRail && cathodeRail) return Fail("二极管两端都接在直流轨上，没有可限幅的信号节点。");
        if (!anodeRail && !cathodeRail)
            return Fail("二极管没有连接任何直流轨，属于串联整流结构，不是串联限幅电路。");

        ElectricalNet? limiterNode = anodeRail ? cathode : anode;
        ElectricalNet? railNet = anodeRail ? anode : cathode;
        if (limiterNode == null || railNet == null || limiterNode == railNet) return Fail("二极管引脚未形成有效的限幅节点。");
        if (IsGroundNet(scan, limiterNode)) return Fail("限幅节点不能直接接地，否则二极管被短路。");

        // Anode at the signal node conducts when v_out >= V_rail + VF: upper
        // limiter. Cathode at the signal node conducts when v_out <= V_rail - VF:
        // lower limiter.
        bool upperLimiter = !anodeRail;
        double bias = scan.RailVoltage(railNet);
        if (!IsGroundNet(scan, railNet) && scan.DcSources.Count != 1)
            return Fail("非地偏置轨需要恰好 1 个直流偏置源提供偏置电压。");
        if (IsGroundNet(scan, railNet) && scan.DcSources.Count != 0)
            return Fail("存在未接入限幅支路的直流偏置源，当前教学模型无法确定其作用。");

        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能形成限幅输出的返回通路。");
        if (sourcePos == null) return Fail("交流源正端未形成有效连接。");
        // Standard textbook limiter: one series resistor R_s between the source and
        // the output node, plus an optional output load R_L from that node to a rail.
        var seriesCandidates = scan.Resistors.Where(resistor =>
        {
            var (n1, n2) = scan.TwoTerminalNets(resistor);
            return (n1 == sourcePos && n2 == limiterNode) || (n2 == sourcePos && n1 == limiterNode);
        }).ToList();
        if (seriesCandidates.Count != 1)
            return Fail("无法唯一识别串联限流电阻 R（应接在交流源正端与限幅输出节点之间）。");
        var seriesResistor = seriesCandidates[0];
        var otherResistors = scan.Resistors.Where(resistor => resistor.Id != seriesResistor.Id).ToList();
        if (otherResistors.Count > 1)
            return Fail("二极管串联限幅除串联电阻 R 外最多支持 1 只输出负载电阻 R_L。");
        CircuitElement? load = otherResistors.FirstOrDefault();
        if (load != null)
        {
            var (l1, l2) = scan.TwoTerminalNets(load);
            ElectricalNet? loadOther = l1 == limiterNode ? l2 : l2 == limiterNode ? l1 : null;
            if (loadOther == null || !scan.IsRail(loadOther))
                return Fail("输出负载电阻 R_L 必须连接在限幅输出节点与直流轨（地/偏置）之间。");
        }

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double resistance = GetSiValue(netlist, seriesResistor);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(resistance) || resistance <= 0) return Fail("串联限流电阻必须为有限正数。");
        double loadResistance = double.PositiveInfinity;
        if (load != null)
        {
            loadResistance = GetSiValue(netlist, load);
            if (!double.IsFinite(loadResistance) || loadResistance <= 0)
                return Fail("输出负载电阻 R_L 必须为有限正数。");
        }

        // Diode off: the output follows the input through the R_s / R_L divider.
        double dividerGain = double.IsPositiveInfinity(loadResistance)
            ? 1.0
            : loadResistance / (resistance + loadResistance);
        double linearMax = vm * dividerGain;
        double linearMin = -vm * dividerGain;
        double threshold = upperLimiter ? bias + vf : bias - vf;
        // Upper limiter: vo = min(k*vs, Vth); lower limiter: vo = max(k*vs, Vth).
        double voutMax = upperLimiter ? Math.Min(linearMax, threshold) : Math.Max(linearMax, threshold);
        double voutMin = upperLimiter ? Math.Min(linearMin, threshold) : Math.Max(linearMin, threshold);
        bool clippingActive = upperLimiter ? linearMax > threshold : linearMin < threshold;
        double ipeak = clippingActive
            ? Math.Abs((upperLimiter ? vm : -vm) - threshold) / resistance
            : 0.0;
        double inputOnset = dividerGain > 0 ? threshold / dividerGain : threshold;
        string direction = upperLimiter ? "上限幅" : "下限幅";
        string vfText = VfText(vf);

        var solution = NewSolution("二极管串联限幅电路教学分析");

        AddStep(solution,
            "串联限幅拓扑识别与二极管方向判定",
            "限幅电路拓扑分析",
            $"串联电阻 R_s={Fmt(resistance, "Ω")} 接在交流源与限幅输出节点之间，二极管一端接直流轨（偏置 {Fmt(bias, "V")}），另一端接输出节点" +
            (double.IsPositiveInfinity(loadResistance)
                ? "；输出端无并联负载，二极管截止时输出跟随输入。"
                : $"；输出端并联负载 R_L={Fmt(loadResistance, "Ω")}，二极管截止时输出按 k=R_L/(R_s+R_L)={VfText(dividerGain)} 分压跟随输入。") +
            (upperLimiter
                ? "二极管阳极接输出节点、阴极接直流轨：输出升高到门限时导通，属于上限幅。"
                : "二极管阴极接输出节点、阳极接直流轨：输出降低到门限时导通，属于下限幅。"),
            upperLimiter
                ? @"V_o=kV_s\ (kV_s<V_{th});\quad V_o=V_{th}\ (kV_s\ge V_{th})"
                : @"V_o=kV_s\ (kV_s>V_{th});\quad V_o=V_{th}\ (kV_s\le V_{th})",
            upperLimiter
                ? $@"V_{{th}}=V_{{bias}}+V_{{F}}={VfText(bias)}+{vfText}={VfText(threshold)}\text{{ V}},\quad k=\frac{{R_L}}{{R_s+R_L}}={VfText(dividerGain)}"
                : $@"V_{{th}}=V_{{bias}}-V_{{F}}={VfText(bias)}-{vfText}={VfText(threshold)}\text{{ V}},\quad k=\frac{{R_L}}{{R_s+R_L}}={VfText(dividerGain)}",
            $"{direction}门限 Vth = {Fmt(threshold, "V")}，线性区增益 k = {VfText(dividerGain)}",
            ("Vm", vm), ("VF", vf), ("Bias", bias), ("Vth", threshold), ("k", dividerGain));

        AddStep(solution,
            "门限电平与限幅方向",
            "恒压降模型门限计算",
            upperLimiter
                ? "上限幅时二极管在 v_S−V_bias≥VF 时导通，把输出钳位在 Vth=V_bias+VF。"
                : "下限幅时二极管在 V_bias−v_S≥VF 时导通，把输出钳位在 Vth=V_bias−VF。",
            upperLimiter ? @"V_{th}=V_{bias}+V_F" : @"V_{th}=V_{bias}-V_F",
            upperLimiter
                ? $@"V_{{th}}={VfText(bias)}+{vfText}={VfText(threshold)}\text{{ V}}"
                : $@"V_{{th}}={VfText(bias)}-{vfText}={VfText(threshold)}\text{{ V}}",
            $"门限电平 Vth = {Fmt(threshold, "V")}（{direction}）",
            ("Vth", threshold));

        AddStep(solution,
            "输出最大值与最小值",
            "限幅输出摆幅分析",
            clippingActive
                ? $"输入峰值经分压后 k·Vm={VfText(linearMax)} V 已超过门限 {VfText(threshold)} V，输出被限幅；未达门限的部分按斜率 k 线性变化。"
                : $"输入峰值经分压后 k·Vm={VfText(linearMax)} V 未超过门限 {VfText(threshold)} V，二极管始终截止，输出无限幅作用。",
            upperLimiter
                ? @"V_{out,max}=\min(kV_m,V_{th}),\quad V_{out,min}=k(-V_m)"
                : @"V_{out,max}=kV_m,\quad V_{out,min}=\max(k(-V_m),V_{th})",
            $@"V_{{out,max}}={VfText(voutMax)}\text{{ V}},\quad V_{{out,min}}={VfText(voutMin)}\text{{ V}}",
            $"输出最大值 = {Fmt(voutMax, "V")}，输出最小值 = {Fmt(voutMin, "V")}",
            ("VoutMax", voutMax), ("VoutMin", voutMin), ("k", dividerGain));

        AddStep(solution,
            "传输特性分段与串联电阻限流",
            "分段线性传输特性",
            "传输特性分为线性区（斜率 k=R_L/(R_s+R_L)）与限幅区（输出恒为门限）两段；限幅区内串联电阻承受剩余电压并限制峰值电流，避免二极管过流损坏。",
            upperLimiter
                ? @"V_o=\begin{cases}kV_s, & V_s\le V_{th}/k\\ V_{th}, & V_s>V_{th}/k\end{cases}"
                : @"V_o=\begin{cases}kV_s, & V_s\ge V_{th}/k\\ V_{th}, & V_s<V_{th}/k\end{cases}",
            $@"I_{{peak}}=\frac{{|V_m-V_{{th}}|}}{{R_s}}={VfText(ipeak)}\text{{ A}},\quad V_{{in,onset}}={VfText(inputOnset)}\text{{ V}}",
            $"峰值限幅电流 Ipeak = {Fmt(ipeak, "A")}，门限对应的输入临界电平 = {Fmt(inputOnset, "V")}，线性区斜率 k = {VfText(dividerGain)}",
            ("Ipeak", ipeak), ("R", resistance), ("RL", loadResistance), ("k", dividerGain), ("VinOnset", inputOnset));

        solution.SummaryText =
            $"二极管串联限幅电路（{MaterialName(diode)}，VF={vfText} V，{direction}）求解完成：偏置 Vbias={Fmt(bias, "V")}，门限电平 Vth={Fmt(threshold, "V")}，" +
            $"输出最大值={Fmt(voutMax, "V")}，输出最小值={Fmt(voutMin, "V")}，线性区增益 k=R_L/(R_s+R_L)={VfText(dividerGain)}，峰值限幅电流={Fmt(ipeak, "A")}；" +
            $"传输特性：线性区内 Vo=k·Vs，越过门限后 Vo=Vth（{(clippingActive ? "当前输入峰值已达门限" : "当前输入峰值未达门限")}）。";
        return solution;
    }

    // ======================================================================
    // Double limiter (two complementary diodes + optional ± bias)
    // ======================================================================

    private PedagogicalSolution SolveDoubleLimiter(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 2)
            return Fail($"二极管双向限幅需要恰好 2 只方向相反的二极管，当前检测到 {scan.Diodes.Count} 只（单管限幅请使用 SeriesLimiter 子类型）。");
        if (scan.AcSources.Count != 1) return Fail("二极管双向限幅需要恰好 1 个交流电压源。");
        if (scan.Capacitors.Count != 0) return Fail("二极管双向限幅电路不应包含电容。");
        if (scan.Resistors.Count is < 1 or > 2)
            return Fail("二极管双向限幅需要恰好 1 只串联限流电阻 R（可选 1 只输出负载电阻 R_L）。");
        if (scan.DcSources.Count > 2) return Fail("二极管双向限幅最多支持 2 个直流偏置源（正、负偏置各 1 个）。");

        var first = scan.Diodes[0];
        var second = scan.Diodes[1];
        var (a1, k1) = scan.DiodeNets(first);
        var (a2, k2) = scan.DiodeNets(second);
        if (a1 == null || k1 == null || a2 == null || k2 == null) return Fail("二极管引脚未形成有效连接。");

        ElectricalNet? common = null;
        foreach (var candidate in new[] { a1, k1 })
        {
            if (candidate == null || scan.IsRail(candidate)) continue;
            if (candidate == a2 || candidate == k2) { common = candidate; break; }
        }
        if (common == null) return Fail("未找到两只二极管共用的限幅输出节点，无法构成双向限幅。");

        ElectricalNet? other1 = a1 == common ? k1 : a1;
        ElectricalNet? other2 = a2 == common ? k2 : a2;
        if (!scan.IsRail(other1) || !scan.IsRail(other2))
            return Fail("双向限幅的两只二极管必须分别接到直流轨（地或正/负偏置）。");
        bool firstIsUpper = a1 == common;
        bool secondIsUpper = a2 == common;
        if (firstIsUpper == secondIsUpper)
            return Fail("双向限幅需要两只方向相反的二极管（一只上限幅、一只下限幅）。");

        var upperDiode = firstIsUpper ? first : second;
        var lowerDiode = firstIsUpper ? second : first;
        var (upperAnode, upperCathode) = scan.DiodeNets(upperDiode);
        var (lowerAnode, lowerCathode) = scan.DiodeNets(lowerDiode);
        if (upperCathode == null || lowerAnode == null) return Fail("二极管引脚未形成有效连接。");

        double biasHigh = scan.RailVoltage(upperCathode);
        double biasLow = scan.RailVoltage(lowerAnode);
        double vfHigh = upperDiode.ForwardVoltageDrop;
        double vfLow = lowerDiode.ForwardVoltageDrop;
        double vthHigh = biasHigh + vfHigh;
        double vthLow = biasLow - vfLow;
        if (vthHigh <= vthLow)
            return Fail($"上、下限幅门限交叉（VthHigh={VfText(vthHigh)} V ≤ VthLow={VfText(vthLow)} V），偏置设置无效。");

        var biasNets = new HashSet<ElectricalNet>();
        if (!IsGroundNet(scan, upperCathode)) biasNets.Add(upperCathode);
        if (!IsGroundNet(scan, lowerAnode)) biasNets.Add(lowerAnode);
        if (scan.DcSources.Count != biasNets.Count)
            return Fail("双向限幅的直流偏置源数量应与非地偏置轨数量一致（每路偏置各 1 个源）。");

        var ac = scan.AcSources[0];
        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能形成限幅输出的返回通路。");
        if (sourcePos == null) return Fail("交流源正端未形成有效连接。");
        // Standard textbook double limiter: one series resistor R_s plus an optional
        // output load R_L from the common output node to a rail.
        var seriesCandidates = scan.Resistors.Where(resistor =>
        {
            var (n1, n2) = scan.TwoTerminalNets(resistor);
            return (n1 == sourcePos && n2 == common) || (n2 == sourcePos && n1 == common);
        }).ToList();
        if (seriesCandidates.Count != 1)
            return Fail("无法唯一识别串联限流电阻 R（应接在交流源正端与双向限幅输出节点之间）。");
        var seriesResistor = seriesCandidates[0];
        var otherResistors = scan.Resistors.Where(resistor => resistor.Id != seriesResistor.Id).ToList();
        if (otherResistors.Count > 1)
            return Fail("二极管双向限幅除串联电阻 R 外最多支持 1 只输出负载电阻 R_L。");
        CircuitElement? load = otherResistors.FirstOrDefault();
        if (load != null)
        {
            var (l1, l2) = scan.TwoTerminalNets(load);
            ElectricalNet? loadOther = l1 == common ? l2 : l2 == common ? l1 : null;
            if (loadOther == null || !scan.IsRail(loadOther))
                return Fail("输出负载电阻 R_L 必须连接在限幅输出节点与直流轨（地/偏置）之间。");
        }

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double resistance = GetSiValue(netlist, seriesResistor);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(resistance) || resistance <= 0) return Fail("串联限流电阻必须为有限正数。");
        double loadResistance = double.PositiveInfinity;
        if (load != null)
        {
            loadResistance = GetSiValue(netlist, load);
            if (!double.IsFinite(loadResistance) || loadResistance <= 0)
                return Fail("输出负载电阻 R_L 必须为有限正数。");
        }
        double dividerGain = double.IsPositiveInfinity(loadResistance)
            ? 1.0
            : loadResistance / (resistance + loadResistance);
        double linearMax = vm * dividerGain;
        double linearMin = -vm * dividerGain;
        double voutMax = Math.Min(linearMax, vthHigh);
        double voutMin = Math.Max(linearMin, vthLow);
        double swing = voutMax - voutMin;
        bool highClipping = linearMax > vthHigh;
        bool lowClipping = linearMin < vthLow;

        var solution = NewSolution("二极管双向限幅电路教学分析");

        AddStep(solution,
            "双向限幅拓扑识别（两只反接二极管 + 正负偏置）",
            "双向限幅拓扑分析",
            $"输出节点同时接上限幅二极管（阳极接输出、阴极接偏置 {Fmt(biasHigh, "V")}）与下限幅二极管（阴极接输出、阳极接偏置 {Fmt(biasLow, "V")}），" +
            (double.IsPositiveInfinity(loadResistance)
                ? "输出端无并联负载；"
                : $"输出端并联负载 R_L={Fmt(loadResistance, "Ω")}，二极管截止时输出按 k={VfText(dividerGain)} 跟随输入；") +
            "正半周由上限幅管钳位，负半周由下限幅管钳位。",
            @"V_{th,high}=V_{bias+}+V_{F},\quad V_{th,low}=V_{bias-}-V_{F}",
            $@"V_{{bias+}}={VfText(biasHigh)}\text{{ V}},\quad V_{{bias-}}={VfText(biasLow)}\text{{ V}},\quad k=\frac{{R_L}}{{R_s+R_L}}={VfText(dividerGain)}",
            $"正偏置 = {Fmt(biasHigh, "V")}，负偏置 = {Fmt(biasLow, "V")}，线性区增益 k = {VfText(dividerGain)}",
            ("Vm", vm), ("BiasHigh", biasHigh), ("BiasLow", biasLow), ("k", dividerGain));

        AddStep(solution,
            "上限幅电平 VthHigh 计算",
            "恒压降模型上限幅门限",
            "输入正半周升高到 Vbias+ + VF 时，上限幅二极管导通，把输出钳位在该电平。",
            @"V_{th,high}=V_{bias+}+V_{F}",
            $@"V_{{th,high}}={VfText(biasHigh)}+{VfText(vfHigh)}={VfText(vthHigh)}\text{{ V}}",
            $"上限幅电平 VthHigh = {Fmt(vthHigh, "V")}",
            ("VthHigh", vthHigh), ("VFHigh", vfHigh));

        AddStep(solution,
            "下限幅电平 VthLow 计算",
            "恒压降模型下限幅门限",
            "输入负半周降低到 Vbias− − VF 时，下限幅二极管导通，把输出钳位在该电平。",
            @"V_{th,low}=V_{bias-}-V_{F}",
            $@"V_{{th,low}}={VfText(biasLow)}-{VfText(vfLow)}={VfText(vthLow)}\text{{ V}}",
            $"下限幅电平 VthLow = {Fmt(vthLow, "V")}",
            ("VthLow", vthLow), ("VFLow", vfLow));

        AddStep(solution,
            "输出摆幅与传输特性分段",
            "限幅输出摆幅分析",
            (highClipping || lowClipping)
                ? $"输入峰值经分压后 k·Vm={VfText(linearMax)} V 已触及限幅门限，输出在 [{VfText(vthLow)}, {VfText(vthHigh)}] V 范围内被钳位。"
                : $"输入峰值经分压后 k·Vm={VfText(linearMax)} V 未触及限幅门限，两只二极管均截止，输出按斜率 k 跟随输入（无限幅作用）。",
            @"V_{out,max}=\min(kV_m,V_{th,high}),\quad V_{out,min}=\max(k(-V_m),V_{th,low})",
            $@"V_{{out,max}}={VfText(voutMax)}\text{{ V}},\quad V_{{out,min}}={VfText(voutMin)}\text{{ V}},\quad V_{{swing}}={VfText(swing)}\text{{ V}}",
            $"输出最大值 = {Fmt(voutMax, "V")}，输出最小值 = {Fmt(voutMin, "V")}，输出摆幅 = {Fmt(swing, "V")}",
            ("VoutMax", voutMax), ("VoutMin", voutMin), ("Swing", swing), ("R", resistance), ("RL", loadResistance), ("k", dividerGain));

        solution.SummaryText =
            $"二极管双向限幅电路（上限幅管 {MaterialName(upperDiode)} VF={VfText(vfHigh)} V，下限幅管 {MaterialName(lowerDiode)} VF={VfText(vfLow)} V）求解完成：" +
            $"正偏置={Fmt(biasHigh, "V")}，负偏置={Fmt(biasLow, "V")}，上限幅电平 VthHigh={Fmt(vthHigh, "V")}，下限幅电平 VthLow={Fmt(vthLow, "V")}，" +
            $"输出最大值={Fmt(voutMax, "V")}，输出最小值={Fmt(voutMin, "V")}，输出摆幅={Fmt(swing, "V")}，线性区增益 k=R_L/(R_s+R_L)={VfText(dividerGain)}；" +
            "传输特性：线性区内 Vo=k·Vs，超出上/下限幅电平时分别钳位于 VthHigh / VthLow。";
        return solution;
    }

    // ======================================================================
    // Clamper (series capacitor + shunt diode)
    // ======================================================================

    private PedagogicalSolution SolveClamper(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1) return Fail($"二极管钳位电路需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只。");
        if (scan.AcSources.Count != 1) return Fail("二极管钳位电路需要恰好 1 个交流电压源。");
        if (scan.Capacitors.Count != 1) return Fail("二极管钳位电路需要恰好 1 只串联耦合电容。");
        if (scan.Resistors.Count > 1) return Fail("二极管钳位电路最多支持 1 只负载电阻（按理想钳位近似分析）。");
        if (scan.DcSources.Count > 1) return Fail("二极管钳位电路最多支持 1 个直流偏置源。");

        var diode = scan.Diodes[0];
        var ac = scan.AcSources[0];
        var capacitor = scan.Capacitors[0];
        var (anode, cathode) = scan.DiodeNets(diode);
        bool anodeRail = scan.IsRail(anode);
        bool cathodeRail = scan.IsRail(cathode);
        if (anodeRail && cathodeRail) return Fail("二极管两端都接在直流轨上，没有可钳位的信号节点。");
        if (!anodeRail && !cathodeRail)
            return Fail("二极管必须一端接直流轨（地或偏置），当前结构更像串联整流/检波，不是钳位电路。");

        ElectricalNet? clampNode = anodeRail ? cathode : anode;
        ElectricalNet? railNet = anodeRail ? anode : cathode;
        if (clampNode == null || railNet == null || clampNode == railNet) return Fail("二极管引脚未形成有效的钳位节点。");
        if (IsGroundNet(scan, clampNode)) return Fail("钳位节点不能直接接地。");

        // The coupling capacitor must sit between the AC source (through resistors
        // only) and the clamp node: this is what separates a clamper from a
        // rectifier/peak detector where the capacitor is a shunt element.
        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能定义输出波形的参考电位。");
        if (sourcePos == null) return Fail("交流源正端未形成有效连接。");
        var (c1, c2) = scan.TwoTerminalNets(capacitor);
        if (c1 == null || c2 == null) return Fail("耦合电容引脚未形成有效连接。");
        ElectricalNet? capacitorInputSide = c1 == clampNode ? c2 : c1;
        ElectricalNet? capacitorClampSide = c1 == clampNode ? c1 : c2;
        if (capacitorClampSide != clampNode)
            return Fail("串联耦合电容必须一端接钳位输出节点。");
        var reachableFromSource = scan.ReachableThroughResistors(new[] { sourcePos });
        if (capacitorInputSide == null || !reachableFromSource.ContainsKey(capacitorInputSide))
            return Fail("串联耦合电容必须接在交流源与钳位输出节点之间（串联信号通路）。");

        if (scan.Resistors.Count == 1)
        {
            var load = scan.Resistors[0];
            if (!(HasTerminal(scan, load, clampNode) && HasRailTerminal(scan, load)))
                return Fail("钳位电路中的电阻必须是从输出节点到直流轨的负载电阻（理想钳位近似忽略其放电）。");
        }
        bool hasLoad = scan.Resistors.Count == 1;
        if (!IsGroundNet(scan, railNet) && scan.DcSources.Count != 1)
            return Fail("非地偏置轨需要恰好 1 个直流偏置源提供偏置电压。");
        if (IsGroundNet(scan, railNet) && scan.DcSources.Count != 0)
            return Fail("存在未接入钳位支路的直流偏置源，当前教学模型无法确定其作用。");

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double bias = scan.RailVoltage(railNet);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");

        // Positive clamper: cathode at the signal node (conducts at the negative
        // peak); negative clamper: anode at the signal node (conducts at the
        // positive peak).
        bool positiveClamper = cathode == clampNode;
        bool clampingActive = positiveClamper ? bias - vf > -vm : bias + vf < vm;
        double clampLevel = positiveClamper ? bias - vf : bias + vf;
        double offset;
        if (positiveClamper)
            offset = clampingActive ? clampLevel + vm : 0.0;
        else
            offset = clampingActive ? clampLevel - vm : 0.0;
        double voutMax = vm + offset;
        double voutMin = -vm + offset;
        double vpp = 2.0 * vm;
        double capVoltage = offset;
        string direction = positiveClamper ? "正钳位（输出上移）" : "负钳位（输出下移）";
        string vfText = VfText(vf);

        var solution = NewSolution("二极管钳位电路教学分析");

        AddStep(solution,
            "钳位拓扑识别与二极管方向判定",
            "二极管钳位拓扑分析",
            $"耦合电容串联在交流源与输出节点之间，二极管从输出节点接到直流轨（偏置 {Fmt(bias, "V")}）。" +
            (positiveClamper
                ? "二极管阴极接输出节点：输出到达负向最大时二极管导通，把该点钳位在 Vbias−VF。"
                : "二极管阳极接输出节点：输出到达正向最大时二极管导通，把该点钳位在 Vbias+VF。") +
            (hasLoad ? "（电路含负载电阻 R_L，按教材理想钳位近似分析，忽略电容经 R_L 的放电。）" : string.Empty),
            positiveClamper ? @"V_{clamp}=V_{bias}-V_F" : @"V_{clamp}=V_{bias}+V_F",
            positiveClamper
                ? $@"V_{{clamp}}={VfText(bias)}-{vfText}={VfText(clampLevel)}\text{{ V}}"
                : $@"V_{{clamp}}={VfText(bias)}+{vfText}={VfText(clampLevel)}\text{{ V}}",
            $"钳位电平 Vclamp = {Fmt(clampLevel, "V")}（{direction}）",
            ("Vm", vm), ("VF", vf), ("Bias", bias), ("ClampLevel", clampLevel));

        AddStep(solution,
            "稳态耦合电容电压推导",
            "稳态电荷平衡分析",
            clampingActive
                ? "稳态时电容被充电到使二极管恰在输入极值处临界导通：电容电压等于钳位电平与输入最小值之差，输出整体平移该直流电压。"
                : $"输入峰值 {VfText(vm)} V 不足使二极管导通，电容不充电，无钳位作用。",
            positiveClamper
                ? @"V_C=V_{clamp}-(-V_m)=V_{clamp}+V_m"
                : @"V_C=V_{clamp}-V_m",
            positiveClamper
                ? $@"V_C={VfText(clampLevel)}+{VfText(vm)}={VfText(capVoltage)}\text{{ V}}"
                : $@"V_C={VfText(clampLevel)}-{VfText(vm)}={VfText(capVoltage)}\text{{ V}}",
            $"电容电压 VC = {Fmt(capVoltage, "V")}（钳位输出直流偏移）",
            ("Vdc", capVoltage));

        AddStep(solution,
            "输出直流偏移 Vdc",
            "输出波形直流分量",
            "稳态电容电压就是输出波形叠加的直流偏移量：输出电压等于输入波形加上（或减去）该偏移，从而整体上下平移。",
            @"V_o(t)=v_s(t)+V_C",
            $@"V_{{dc}}=V_C={VfText(capVoltage)}\text{{ V}}",
            $"输出直流偏移 Vdc = {Fmt(capVoltage, "V")}",
            ("Vdc", capVoltage));

        AddStep(solution,
            "输出最大值 / 最小值与峰峰值保持",
            "钳位输出波形分析",
            "理想钳位不改变波形形状与峰峰值（仍为 2Vm），只平移直流电平；输出最大值与最小值由输入峰值与偏移确定。",
            @"V_{pp}=2V_m,\quad V_{out,max}=V_m+V_C,\quad V_{out,min}=-V_m+V_C",
            $@"V_{{pp}}=2\times{VfText(vm)}={VfText(vpp)}\text{{ V}},\quad V_{{out,max}}={VfText(voutMax)}\text{{ V}},\quad V_{{out,min}}={VfText(voutMin)}\text{{ V}}",
            $"输出最大值 = {Fmt(voutMax, "V")}，输出最小值 = {Fmt(voutMin, "V")}，峰峰值 = {Fmt(vpp, "V")}",
            ("Vpp", vpp), ("VoutMax", voutMax), ("VoutMin", voutMin));

        solution.SummaryText =
            $"二极管钳位电路（{MaterialName(diode)}，VF={vfText} V，{direction}）求解完成：偏置 Vbias={Fmt(bias, "V")}，钳位电平={Fmt(clampLevel, "V")}，" +
            $"输出直流偏移 Vdc={Fmt(capVoltage, "V")}，输出最大值={Fmt(voutMax, "V")}，输出最小值={Fmt(voutMin, "V")}，峰峰值保持 Vpp={Fmt(vpp, "V")}；" +
            $"{(clampingActive ? "稳态电容按钳位电平完成充电，输出波形整体平移。" : "输入峰值不足，二极管未导通，输出等于输入。")}" +
            (hasLoad ? "（含负载 R_L，为理想钳位近似，忽略 RC 放电。）" : string.Empty);
        return solution;
    }

    // ======================================================================
    // Peak / envelope detector (series diode + shunt RC)
    // ======================================================================

    private PedagogicalSolution SolvePeakDetector(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1) return Fail($"二极管峰值检波需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只。");
        if (scan.AcSources.Count != 1) return Fail("二极管峰值检波需要恰好 1 个交流电压源。");
        if (scan.Capacitors.Count != 1) return Fail("二极管峰值检波需要恰好 1 只检波电容 C。");
        if (scan.DcSources.Count != 0) return Fail("二极管峰值检波电路不应包含直流偏置源。");
        if (scan.Resistors.Count > 2) return Fail("二极管峰值检波最多支持 2 只电阻（串联充电电阻与负载/放电电阻）。");

        var diode = scan.Diodes[0];
        var ac = scan.AcSources[0];
        var capacitor = scan.Capacitors[0];
        var (anode, cathode) = scan.DiodeNets(diode);
        var sourcePos = scan.PinNet(ac, "pos");
        var sourceNeg = scan.PinNet(ac, "neg");
        if (!IsGroundNet(scan, sourceNeg)) return Fail("交流电压源负端必须接地，才能形成检波充电回路。");
        if (sourcePos == null || anode == null || cathode == null) return Fail("交流源或二极管引脚未形成有效连接。");
        if (anode == cathode) return Fail("二极管两端短路，无法构成检波器。");
        if (IsGroundNet(scan, cathode)) return Fail("二极管阴极不能直接接地（检波输出应取自阴极节点）。");
        if (!ShuntsToRail(scan, capacitor, cathode)) return Fail("检波电容必须连接在输出节点（二极管阴极）与地之间。");

        // Optional series charging resistor between the AC source and the diode anode.
        CircuitElement? seriesResistor = scan.Resistors.FirstOrDefault(res =>
        {
            var (n1, n2) = scan.TwoTerminalNets(res);
            return (n1 == sourcePos && n2 == anode) || (n2 == sourcePos && n1 == anode);
        });
        if (anode != sourcePos && seriesResistor == null)
            return Fail("二极管阳极与交流源之间必须通过电阻连接（充电电阻），当前拓扑无法识别。");
        CircuitElement? load = scan.Resistors.FirstOrDefault(res =>
        {
            if (res == seriesResistor) return false;
            var (n1, n2) = scan.TwoTerminalNets(res);
            return (n1 == cathode && scan.IsRail(n2)) || (n2 == cathode && scan.IsRail(n1));
        });
        foreach (var resistor in scan.Resistors)
        {
            if (resistor == seriesResistor) continue;
            if (load == null || resistor.Id != load.Id)
                return Fail("存在教学模型未覆盖的电阻支路（检波器仅支持充电电阻与负载/放电电阻）。");
        }
        if (!(anode == sourcePos) && seriesResistor == null)
            return Fail("缺少串联充电电阻。");

        double vm = GetSiValue(netlist, ac);
        double frequency = ac.FrequencyHz;
        double vf = diode.ForwardVoltageDrop;
        double c = GetSiValue(netlist, capacitor);
        if (!double.IsFinite(vm) || vm <= 0) return Fail("交流源峰值必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency <= 0) return Fail("交流源频率必须为有限正数。");
        if (!double.IsFinite(c) || c <= 0) return Fail("检波电容必须为有限正数。");
        if (vm <= vf)
            return Fail($"输入峰值 Vm={VfText(vm)} V 不超过二极管正向压降 VF={VfText(vf)} V，检波二极管无法导通，无检波输出。");

        double rl = double.PositiveInfinity;
        if (load != null)
        {
            rl = GetSiValue(netlist, load);
            if (!double.IsFinite(rl) || rl <= 0) return Fail("负载/放电电阻必须为有限正数。");
        }

        double vpeak = vm - vf;
        double tau = double.IsPositiveInfinity(rl) ? double.PositiveInfinity : rl * c;
        double vr = double.IsPositiveInfinity(rl) ? 0.0 : vpeak / (frequency * rl * c);
        double vdc = vpeak - vr / 2.0;
        double tauOverT = double.IsPositiveInfinity(rl) ? double.PositiveInfinity : tau * frequency;
        bool criterionMet = double.IsPositiveInfinity(tauOverT) || tauOverT >= 10.0;
        string criterionText = criterionMet
            ? $"满足（τ/T={Fmt(tauOverT, "")}≥10，输出基本保持在峰值）"
            : $"不满足（τ/T={Fmt(tauOverT, "")}<10，纹波较大，不适合作为峰值保持）";
        string vfText = VfText(vf);

        var solution = NewSolution("二极管峰值检波电路教学分析");

        AddStep(solution,
            "峰值检波拓扑与充电过程",
            "二极管峰值检波原理",
            $"串联二极管在输入正半周导通，向检波电容充电至 Vm−VF；输入下降后二极管截止，电容通过负载电阻缓慢放电，" +
            "放电时间常数远大于输入周期时输出近似保持输入峰值。",
            @"V_{peak}=V_m-V_F",
            $@"V_{{peak}}={VfText(vm)}-{vfText}={VfText(vpeak)}\text{{ V}}",
            $"峰值输出电压 Vpeak = {Fmt(vpeak, "V")}",
            ("Vm", vm), ("VF", vf), ("Vpeak", vpeak), ("C", c));

        AddStep(solution,
            "峰值输出电压与二极管压降",
            "恒压降模型峰值计算",
            "二极管导通时把电容充电到输入峰值减去一个正向压降，因此检波输出峰值比输入峰值低 VF。",
            @"V_{peak}=V_m-V_F",
            $@"V_{{peak}}={VfText(vm)}-{vfText}={VfText(vpeak)}\text{{ V}}",
            $"检波峰值 Vpeak = {Fmt(vpeak, "V")}",
            ("Vpeak", vpeak));

        AddStep(solution,
            "放电时间常数 τ 与纹波电压 Vr",
            "RC 放电近似",
            "二极管截止期间电容经负载电阻放电，放电周期近似为输入周期 T=1/f；纹波峰峰值近似为 Vr≈Vpeak/(f·R_L·C)。",
            @"\tau=R_L C,\quad V_r\approx\frac{V_{peak}}{f R_L C}",
            double.IsPositiveInfinity(rl)
                ? @"\tau\rightarrow\infty,\quad V_r\approx 0"
                : $@"\tau={VfText(rl)}\times{VfText(c)}={VfText(tau)}\text{{ s}},\quad V_r=\frac{{{VfText(vpeak)}}}{{{VfText(frequency)}\times{VfText(rl)}\times{VfText(c)}}}={VfText(vr)}\text{{ V}}",
            $"时间常数 τ = {Fmt(tau, "s")}，纹波 Vr = {Fmt(vr, "V")}",
            ("Vr", vr), ("Tau", tau), ("RL", rl));

        AddStep(solution,
            "时间常数判据与输出直流电压",
            "峰值保持工程判据",
            "工程上要求放电时间常数至少为输入周期的 10 倍（τ≥10T）才能近似保持峰值；输出直流电压近似为峰值减去半个纹波。",
            @"\tau\ge 10T,\quad V_{dc}=V_{peak}-\frac{V_r}{2}",
            double.IsPositiveInfinity(rl)
                ? $@"\tau\ge 10T\ ({criterionText}),\quad V_{{dc}}={VfText(vdc)}\text{{ V}}"
                : $@"\frac{{\tau}}{{T}}=\tau f={VfText(tauOverT)},\quad V_{{dc}}={VfText(vpeak)}-\frac{{{VfText(vr)}}}{{2}}={VfText(vdc)}\text{{ V}}",
            $"时间常数判据：{criterionText}；输出直流 Vdc = {Fmt(vdc, "V")}",
            ("Vdc", vdc), ("TauOverT", tauOverT), ("f", frequency));

        solution.SummaryText =
            $"二极管峰值检波电路（{MaterialName(diode)}，VF={vfText} V）求解完成：输入峰值 Vm={Fmt(vm, "V")}，峰值输出 Vpeak={Fmt(vpeak, "V")}，" +
            $"纹波 Vr={Fmt(vr, "V")}，输出直流 Vdc={Fmt(vdc, "V")}，时间常数 τ={Fmt(tau, "s")}，τ/T={Fmt(tauOverT, "")}，判据 {criterionText}。";
        return solution;
    }

    // ======================================================================
    // Generic diode DC bias (Shockley + clamped Newton-Raphson)
    // ======================================================================

    private PedagogicalSolution SolveDiodeBias(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        if (!TryScan(netlist, out var scan, out string? error)) return Fail(error!);
        if (scan.Diodes.Count != 1)
            return Fail($"二极管直流偏置分析需要恰好 1 只二极管，当前检测到 {scan.Diodes.Count} 只（波形电路请使用对应整流/限幅子类型）。");
        if (scan.AcSources.Count != 0)
            return Fail("二极管直流偏置分析只接受直流源、电阻与二极管，不接受交流源（波形分析请使用整流/限幅/钳位/检波子类型）。");
        if (scan.DcSources.Count != 1) return Fail("二极管直流偏置分析需要恰好 1 个直流电压源。");
        if (scan.Capacitors.Count != 0)
            return Fail("二极管直流偏置分析不接受电容（直流稳态下电容应视为开路，请移除以避免歧义）。");
        if (scan.Resistors.Count == 0) return Fail("二极管直流偏置分析需要至少 1 只串联电阻来限制电流。");

        var diode = scan.Diodes[0];
        var source = scan.DcSources[0];
        var sourcePos = scan.PinNet(source, "pos");
        var sourceNeg = scan.PinNet(source, "neg");
        if (sourcePos == null || sourceNeg == null || sourcePos == sourceNeg)
            return Fail("直流电压源引脚未形成有效连接。");

        // Walk the unique series chain from the source positive terminal to the
        // negative terminal: exactly one R/D branch at every node.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visitedNets = new HashSet<ElectricalNet> { sourcePos };
        double totalResistance = 0.0;
        bool? forwardAlongPath = null;
        ElectricalNet current = sourcePos;
        int guard = 0;
        while (current != sourceNeg)
        {
            var candidates = new List<CircuitElement>();
            foreach (var candidateElement in netlist.Elements.Values)
            {
                if (candidateElement.Type is not (ElementType.Resistor or ElementType.Diode)) continue;
                if (visited.Contains(candidateElement.Id)) continue;
                var (n1, n2) = scan.TwoTerminalNets(candidateElement);
                if (n1 == current || n2 == current) candidates.Add(candidateElement);
            }
            if (candidates.Count == 0) return Fail("直流回路在某个节点断开，无法形成源—电阻—二极管的闭合回路。");
            if (candidates.Count > 1)
                return Fail("直流偏置回路存在并联/分支结构；当前教学模型只支持源—电阻—二极管的单一串联回路。");

            var element = candidates[0];
            visited.Add(element.Id);
            var (first, second) = scan.TwoTerminalNets(element);
            ElectricalNet? next = first == current ? second : first;
            if (element.Type == ElementType.Diode)
            {
                if (forwardAlongPath != null) return Fail("串联回路中出现多只二极管，当前教学模型只支持单只二极管的直流偏置。");
                // For a diode the first terminal is the anode: entering the anode
                // means the traversal follows the forward direction.
                forwardAlongPath = first == current;
            }
            else
            {
                double resistance = GetSiValue(netlist, element);
                if (!double.IsFinite(resistance) || resistance < 0) return Fail("串联电阻值必须为有限非负数。");
                totalResistance += resistance;
            }
            if (next == null || visitedNets.Contains(next)) return Fail("直流回路存在短路或异常连接，无法列写唯一回路方程。");
            visitedNets.Add(next);
            current = next;
            if (++guard > netlist.Elements.Count + 2) return Fail("直流回路遍历超出预期，可能存在异常连接。");
        }

        if (forwardAlongPath == null) return Fail("未在直流回路中找到二极管，无法建立非线性方程。");
        if (visited.Count != scan.Resistors.Count + 1)
            return Fail("存在未参与串联回路的电阻或二极管支路（不支持并联直流网络）。");
        if (totalResistance <= 0) return Fail("直流偏置回路缺少有效串联电阻，电流将被理想模型放大到无意义的值。");

        double vs = GetSiValue(netlist, source);
        if (!double.IsFinite(vs)) return Fail("直流电压源数值无效。");
        double vf = diode.ForwardVoltageDrop;
        var model = DiodeModel.FromElement(diode, parameters);
        double s = forwardAlongPath.Value ? 1.0 : -1.0;
        double target = s * vs;

        double vd;
        int iterations = 0;
        double residual = double.NaN;
        bool reverseBiased = target <= 0.0;
        if (reverseBiased)
        {
            // No forward drive: solve the (linearised) reverse branch directly.
            // The Shockley current is essentially -Is, so the diode voltage is the
            // source voltage with the tiny reverse drop across R.
            vd = target - model.Current(target) * totalResistance;
        }
        else
        {
            vd = 0.6;
            for (iterations = 1; iterations <= 200; iterations++)
            {
                double currentValue = model.Current(vd);
                double g = currentValue * totalResistance + vd - target;
                double dg = model.Conductance(vd) * totalResistance + 1.0;
                if (!double.IsFinite(g) || !double.IsFinite(dg) || dg <= 0)
                    return Fail("Newton-Raphson 迭代出现非有限值，二极管模型进入数值保护区间。");
                double step = g / dg;
                vd -= step;
                if (vd > DiodeModel.MaximumForwardVoltage) vd = DiodeModel.MaximumForwardVoltage;
                if (vd < DiodeModel.MaximumReverseVoltage) vd = DiodeModel.MaximumReverseVoltage;
                if (Math.Abs(step) <= 1e-12 * Math.Max(1.0, Math.Abs(vd))) break;
            }
            residual = model.Current(vd) * totalResistance + vd - target;
            if (!double.IsFinite(residual) || Math.Abs(residual) > 1e-9 * Math.Max(1.0, Math.Abs(target)))
                return Fail("Newton-Raphson 迭代未收敛，无法确定二极管直流工作点（请检查电源/电阻参数）。");
            if (vd >= DiodeModel.MaximumForwardVoltage - 1e-9)
                return Fail("二极管电压达到指数钳位上限，电流过大，超出 Shockley 教学模型的适用范围。");
        }

        double id = model.Current(vd);
        if (!double.IsFinite(id)) return Fail("二极管电流计算结果非有限，无法输出工作点。");
        bool conducting = id > 1e-9;
        string state = conducting ? "导通" : reverseBiased ? "截止（反向偏置）" : "截止（正向未导通）";
        string regionText = conducting ? "导通（正向偏置）" : reverseBiased ? "截止（反向偏置）" : "截止";
        double vdPwl = vf;
        double idPwl = forwardAlongPath.Value ? Math.Max(0.0, (vs - vf) / totalResistance) : 0.0;
        string vfText = VfText(vf);

        var solution = NewSolution("二极管直流偏置工作点（Shockley/Newton）教学分析");

        AddStep(solution,
            "直流通路识别与二极管方向判定",
            "直流回路基尔霍夫电压定律",
            $"电路为直流源、串联电阻与二极管的单一回路（R={Fmt(totalResistance, "Ω")}，Vs={Fmt(vs, "V")}）。" +
            (forwardAlongPath.Value
                ? "沿电源正端到负端的回路方向，电流从二极管阳极流入、阴极流出，二极管正向偏置。"
                : "沿电源正端到负端的回路方向，电流从二极管阴极流入，二极管反向偏置。"),
            forwardAlongPath.Value ? @"V_S=I_D R+V_D" : @"-V_S=I_D R+V_D",
            $@"V_S={VfText(vs)}\text{{ V}},\quad R={VfText(totalResistance)}\ \Omega",
            $"回路总电阻 R = {Fmt(totalResistance, "Ω")}，电源电压 Vs = {Fmt(vs, "V")}（{(forwardAlongPath.Value ? "正向" : "反向")}偏置）",
            ("Vs", vs), ("R", totalResistance), ("VF", vf));

        AddStep(solution,
            "Shockley 方程与 Newton 迭代方程建立",
            "Shockley 指数模型",
            "二极管采用 Shockley 指数模型，代入回路 KVL 得到关于二极管电压 VD 的非线性方程；指数超出 ±0.99 V 时做钳位保护，保证迭代不溢出。",
            @"I_D=I_S\left(e^{V_D/(nV_T)}-1\right),\quad f(V_D)=I_D R+V_D-sV_S=0",
            $@"I_S={ScientificNotationFormatter.ToUnicodeScientific(model.SaturationCurrent)}\text{{ A}},\quad n={model.IdealityFactor:0.##},\quad V_T={VfText(model.ThermalVoltage)}\text{{ V}}",
            $"模型参数：Is={ScientificNotationFormatter.ToUnicodeScientific(model.SaturationCurrent)} A，n={model.IdealityFactor:0.##}，Vt={Fmt(model.ThermalVoltage, "V")}",
            ("Is", model.SaturationCurrent), ("n", model.IdealityFactor), ("Vt", model.ThermalVoltage));

        AddStep(solution,
            "Newton-Raphson 数值求解（指数钳位保护）",
            "Newton-Raphson 迭代法",
            reverseBiased
                ? "由于电源极性与二极管方向不构成正向驱动，工作点位于反向截止区，直接由回路方程得到反向电压（反向电流仅为 -Is 量级）。"
                : $"从初值 VD=0.6 V 开始迭代：VD←VD−f(VD)/f'(VD)，f'(VD)=1+G_D·R；迭代 {iterations} 次后满足 |f(VD)|≤1e-9·max(1,|Vs|)。",
            @"V_D^{(k+1)}=V_D^{(k)}-\frac{f(V_D^{(k)})}{1+G_D R},\quad G_D=\frac{dI_D}{dV_D}",
            $@"V_D={VfText(vd)}\text{{ V}},\quad \left|f(V_D)\right|={ScientificNotationFormatter.ToUnicodeScientific(Math.Abs(double.IsNaN(residual) ? 0.0 : residual))}",
            $"Newton 求解二极管电压 VD = {Fmt(vd, "V")}，迭代次数 = {iterations}",
            ("VD", vd), ("Iterations", iterations), ("State", state));

        AddStep(solution,
            "工作状态判定与恒压降模型交叉校核",
            "工作区判定与模型对比",
            $"Shockley 解给出 ID={Fmt(id, "A")}、VD={Fmt(vd, "V")}，判定状态为{regionText}。" +
            (forwardAlongPath.Value && vs > vf
                ? $"作为教学对比，恒压降模型（VF={vfText} V）估算 ID≈(Vs−VF)/R={Fmt(idPwl, "A")}，两者数量级一致。"
                : $"恒压降模型给出 VF={vfText} V 的近似判据，反向/截止区电流近似为 0。"),
            @"I_D=I_S(e^{V_D/(nV_T)}-1),\quad \text{state}=\begin{cases}\text{导通}, & I_D>0\\ \text{截止}, & I_D\le 0\end{cases}",
            $@"I_D={VfText(id)}\text{{ A}},\quad V_D={VfText(vd)}\text{{ V}},\quad \text{{状态}}={state}",
            $"二极管电流 ID = {Fmt(id, "A")}，导通压降 VD = {Fmt(vd, "V")}，状态 = {state}",
            ("ID", id), ("VD", vd), ("State", state), ("IdPwl", idPwl), ("VdPwl", vdPwl));

        solution.SummaryText =
            $"二极管直流偏置工作点（Shockley 模型 + Newton 迭代，{MaterialName(diode)}）求解完成：直流源 Vs={Fmt(vs, "V")}，串联电阻 R={Fmt(totalResistance, "Ω")}，" +
            $"二极管电流 ID={Fmt(id, "A")}，导通压降 VD={Fmt(vd, "V")}，状态={state}；" +
            $"恒压降模型交叉校核：VF={vfText} V 时 ID≈{Fmt(idPwl, "A")}。";
        return solution;
    }

    // ======================================================================
    // Shared scanning / helper infrastructure
    // ======================================================================

    /// <summary>Netlist structural scan shared by detection and the analytic solvers.</summary>
    private sealed class CircuitScan
    {
        private readonly ElectricalNetlist _netlist;

        public CircuitScan(ElectricalNetlist netlist)
        {
            _netlist = netlist;
            Ground = netlist.GroundNet;
            Diodes = netlist.Elements.Values.Where(e => e.Type == ElementType.Diode)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            Resistors = netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            Capacitors = netlist.Elements.Values
                .Where(e => e.Type is ElementType.Capacitor or ElementType.CapacitorPolarized)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            AcSources = netlist.Elements.Values.Where(e => e.Type == ElementType.AcVoltageSource)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            DcSources = netlist.Elements.Values.Where(e => e.Type == ElementType.DcVoltageSource)
                .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            ComputeDcPotentials();
        }

        public ElectricalNet? Ground { get; }
        public List<CircuitElement> Diodes { get; }
        public List<CircuitElement> Resistors { get; }
        public List<CircuitElement> Capacitors { get; }
        public List<CircuitElement> AcSources { get; }
        public List<CircuitElement> DcSources { get; }

        /// <summary>DC potential of every net fixed by the DC source network (ground = 0 V).</summary>
        public Dictionary<ElectricalNet, double> DcPotentials { get; } = new();

        public ElectricalNet? PinNet(CircuitElement element, string pinId) =>
            _netlist.FindNetForPin(element.Id, pinId);

        public (ElectricalNet? Anode, ElectricalNet? Cathode) DiodeNets(CircuitElement diode) =>
            (PinNet(diode, "A"), PinNet(diode, "K"));

        public (ElectricalNet? First, ElectricalNet? Second) TwoTerminalNets(CircuitElement element) =>
            element.Type == ElementType.Diode ? DiodeNets(element) : (PinNet(element, "1"), PinNet(element, "2"));

        public bool IsRail(ElectricalNet? net) =>
            net != null && (net.IsGround || net == Ground || DcPotentials.ContainsKey(net));

        public double RailVoltage(ElectricalNet? net)
        {
            if (net == null) return 0.0;
            if (DcPotentials.TryGetValue(net, out double value)) return value;
            return net.IsGround || net == Ground ? 0.0 : 0.0;
        }

        /// <summary>Nets reachable from the given start nets through resistor branches only (distance = resistor count).</summary>
        public Dictionary<ElectricalNet, int> ReachableThroughResistors(IEnumerable<ElectricalNet?> starts)
        {
            var distance = new Dictionary<ElectricalNet, int>();
            var queue = new Queue<(ElectricalNet Net, int Count)>();
            foreach (var start in starts)
            {
                if (start == null || distance.ContainsKey(start)) continue;
                distance[start] = 0;
                queue.Enqueue((start, 0));
            }
            while (queue.Count > 0)
            {
                var (net, count) = queue.Dequeue();
                foreach (var resistor in Resistors)
                {
                    var (first, second) = TwoTerminalNets(resistor);
                    if (first == null || second == null) continue;
                    ElectricalNet? next = first == net ? second : second == net ? first : null;
                    if (next == null || distance.ContainsKey(next)) continue;
                    distance[next] = count + 1;
                    queue.Enqueue((next, count + 1));
                }
            }
            return distance;
        }

        private void ComputeDcPotentials()
        {
            DcPotentials.Clear();
            if (Ground == null) return;
            DcPotentials[Ground] = 0.0;
            var queue = new Queue<ElectricalNet>();
            queue.Enqueue(Ground);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 10000)
            {
                var net = queue.Dequeue();
                double netVoltage = DcPotentials[net];
                foreach (var source in DcSources)
                {
                    var pos = PinNet(source, "pos");
                    var neg = PinNet(source, "neg");
                    if (pos == null || neg == null || pos == neg) continue;
                    double vs = DiodeCircuitStrategy.GetSiValue(_netlist, source);
                    if (!double.IsFinite(vs)) continue;
                    if (pos == net && !DcPotentials.ContainsKey(neg))
                    {
                        DcPotentials[neg] = netVoltage - vs;
                        queue.Enqueue(neg);
                    }
                    else if (neg == net && !DcPotentials.ContainsKey(pos))
                    {
                        DcPotentials[pos] = netVoltage + vs;
                        queue.Enqueue(pos);
                    }
                }
            }
        }
    }

    private static bool HasTerminal(CircuitScan scan, CircuitElement element, ElectricalNet? net)
    {
        if (net == null) return false;
        var (first, second) = scan.TwoTerminalNets(element);
        return first == net || second == net;
    }

    private static bool HasRailTerminal(CircuitScan scan, CircuitElement element)
    {
        var (first, second) = scan.TwoTerminalNets(element);
        return scan.IsRail(first) || scan.IsRail(second);
    }

    private static bool ShuntsToRail(CircuitScan scan, CircuitElement capacitor, ElectricalNet? node)
    {
        if (node == null) return false;
        var (first, second) = scan.TwoTerminalNets(capacitor);
        return (first == node && scan.IsRail(second)) || (second == node && scan.IsRail(first));
    }

    private static bool IsShuntDiode(CircuitScan scan, CircuitElement diode)
    {
        var (anode, cathode) = scan.DiodeNets(diode);
        if (anode == null || cathode == null || anode == cathode) return false;
        bool anodeRail = scan.IsRail(anode);
        bool cathodeRail = scan.IsRail(cathode);
        return anodeRail != cathodeRail;
    }

    private static bool IsSeriesDiode(CircuitScan scan, CircuitElement diode)
    {
        var (anode, cathode) = scan.DiodeNets(diode);
        return anode != null && cathode != null && anode != cathode
               && !scan.IsRail(anode) && !scan.IsRail(cathode);
    }

    private static bool HasClamperCapacitor(CircuitScan scan)
    {
        if (scan.Capacitors.Count == 0) return false;
        var sourceStarts = scan.AcSources.Select(source => scan.PinNet(source, "pos")).ToList();
        if (sourceStarts.All(start => start == null)) return false;
        var reachable = scan.ReachableThroughResistors(sourceStarts);
        foreach (var capacitor in scan.Capacitors)
        {
            var (first, second) = scan.TwoTerminalNets(capacitor);
            if (first == null || second == null) continue;
            if (reachable.ContainsKey(first) && HasShuntDiodeAt(scan, second)) return true;
            if (reachable.ContainsKey(second) && HasShuntDiodeAt(scan, first)) return true;
        }
        return false;
    }

    private static bool HasShuntDiodeAt(CircuitScan scan, ElectricalNet net)
    {
        if (scan.IsRail(net)) return false;
        foreach (var diode in scan.Diodes)
        {
            var (anode, cathode) = scan.DiodeNets(diode);
            if (anode == net && cathode != null && scan.IsRail(cathode)) return true;
            if (cathode == net && anode != null && scan.IsRail(anode)) return true;
        }
        return false;
    }

    private static bool IsDoubleLimiter(CircuitScan scan)
    {
        if (scan.Diodes.Count != 2) return false;
        var (a1, k1) = scan.DiodeNets(scan.Diodes[0]);
        var (a2, k2) = scan.DiodeNets(scan.Diodes[1]);
        if (a1 == null || k1 == null || a2 == null || k2 == null) return false;

        ElectricalNet? common = null;
        foreach (var candidate in new[] { a1, k1 })
        {
            if (candidate == null || scan.IsRail(candidate)) continue;
            if (candidate == a2 || candidate == k2) { common = candidate; break; }
        }
        if (common == null) return false;

        ElectricalNet? other1 = a1 == common ? k1 : a1;
        ElectricalNet? other2 = a2 == common ? k2 : a2;
        if (!scan.IsRail(other1) || !scan.IsRail(other2)) return false;
        return (a1 == common) != (a2 == common);
    }

    private bool TryScan(ElectricalNetlist netlist, out CircuitScan scan, out string? error)
    {
        scan = new CircuitScan(netlist);
        error = null;
        if (netlist.Transistors.Count > 0)
        {
            error = "二极管教学策略仅支持不含晶体管的二极管电路，请改用对应的 BJT/MOSFET 教学策略。";
            return false;
        }
        if (scan.Diodes.Count == 0)
        {
            error = "网表中没有二极管，无法执行二极管电路分析。";
            return false;
        }
        var unsupported = netlist.Elements.Values
            .Where(e => e.Type is not (ElementType.Resistor or ElementType.Capacitor or ElementType.CapacitorPolarized
                or ElementType.Diode or ElementType.AcVoltageSource or ElementType.DcVoltageSource or ElementType.Ground))
            .Select(e => $"{e.Id}({e.Type})")
            .ToList();
        if (unsupported.Count > 0)
        {
            error = $"二极管教学策略不支持以下元件类型：{string.Join("、", unsupported)}。";
            return false;
        }
        var validation = netlist.Validate();
        if (!validation.IsValid)
        {
            var messages = validation.Issues
                .Where(issue => issue.Severity == NetlistValidationSeverity.Error)
                .Select(issue => issue.Message);
            error = "网表电气校验未通过：" + string.Join("；", messages);
            return false;
        }
        return true;
    }

    private PedagogicalSolution NewSolution(string title) => new()
    {
        IsSuccess = true,
        TopologyType = CircuitTopologyType.DiodeCircuit,
        SubtypeName = _subtype,
        CircuitTitle = title,
        QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = "二极管非线性电路",
            IsActiveRegion = false
        },
        AcParameters = null
    };

    private PedagogicalSolution Fail(string reason) =>
        PedagogicalSolution.Failure(CircuitTopologyType.DiodeCircuit, $"二极管电路（{_subtype}）教学分析失败：{reason}");

    private static void AddStep(
        PedagogicalSolution solution,
        string title,
        string method,
        string content,
        string latexFormula,
        string substitutionFormula,
        string resultValue,
        params (string Key, object Value)[] values)
    {
        int index = solution.Steps.Count + 1;
        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            dictionary[key] = value switch
            {
                double doubleValue => FormatRaw(doubleValue),
                float floatValue => FormatRaw(floatValue),
                int intValue => intValue.ToString(CultureInfo.InvariantCulture),
                null => string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = index,
            BadgeText = $"第 {index} 步",
            Title = title,
            MethodName = method,
            Content = content,
            LatexFormula = latexFormula,
            SubstitutionFormula = substitutionFormula,
            ResultValue = resultValue,
            Status = "Completed",
            Values = dictionary
        });
    }

    private static string FormatRaw(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";
        return value.ToString("G9", CultureInfo.InvariantCulture);
    }

    private static string Fmt(double value, string unit) =>
        string.IsNullOrEmpty(unit)
            ? ScientificNotationFormatter.ToUnicodeScientific(value)
            : ScientificNotationFormatter.FormatWithUnit(value, unit);

    // Display helper: teaching convention is at most two decimals (AwayFromZero).
    private static string VfText(double value) =>
        double.IsFinite(value)
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture)
            : FormatRaw(value);

    private static string MaterialName(CircuitElement element) =>
        element.Material == SemiconductorMaterial.Germanium ? "锗管 Ge" : "硅管 Si";

    private static bool IsGroundNet(CircuitScan scan, ElectricalNet? net) =>
        net != null && (net.IsGround || net == scan.Ground);

    private static double GetSiValue(ElectricalNetlist netlist, CircuitElement element)
    {
        var branch = netlist.Branches.FirstOrDefault(b => b.Element != null && b.Element.Id == element.Id);
        if (branch != null && double.IsFinite(branch.ValueSI)) return branch.ValueSI;
        try
        {
            return NetlistExtractor.ConvertToBaseSi(element.Type, element.DisplayNumber, element.UnitSymbol);
        }
        catch (ArgumentException)
        {
            // Fixtures may omit the unit symbol; treat the display number as SI.
            return element.DisplayNumber;
        }
    }
}
