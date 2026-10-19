// ============================================================================
// File: tests/NonlinearCircuitChecks/DiodeChecks.cs
// OWNER: engine-diode.
// Regression checks for the diode nonlinear support described in
// projects/nonlinear-engine-and-circuit-models-design-20261018.md §4 / §10:
//   * DiodeModel: material forward drop (Si 0.7 V / Ge 0.3 V), Shockley
//     Is/n/Vt defaults, clamped exponential, reverse saturation current,
//     Conductance == numeric derivative (relative error <= 1e-6), Region;
//   * DetectSubtype: the nine contract subtype names for the matching topologies
//     and null only when no diode exists;
//   * nine textbook scenarios against hand-computed formulas (1 % tolerance):
//     HalfWaveRectifier / HalfWaveRectifierFiltered / BridgeRectifier /
//     BridgeRectifierFiltered / SeriesLimiter / DoubleLimiter / Clamper /
//     PeakDetector / DiodeBias;
//   * silicon vs germanium forward-drop difference (0.7 V vs 0.3 V);
//   * failure paths (topology mismatch / Vm below VF / no DC source / no ground)
//     with explicit Chinese error text that names the diode limitation.
// Use CheckResult.* for assertions; throw nothing to Program.
// ============================================================================

using System;
using System.Globalization;
using System.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Nonlinear;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class DiodeChecks
{
    private const double Tolerance = 0.01; // project-wide 1 % hand-calculation tolerance

    public static void Run()
    {
        CheckResult.Section("二极管教学策略（engine-diode）");
        RunDiodeModelChecks();
        RunDetectionChecks();
        RunHalfWaveRectifierCheck();
        RunHalfWaveFilteredCheck();
        RunBridgeRectifierCheck();
        RunBridgeFilteredCheck();
        RunSeriesLimiterCheck();
        RunDoubleLimiterCheck();
        RunClamperCheck();
        RunPeakDetectorCheck();
        RunDiodeBiasCheck();
        RunMaterialChecks();
        RunFailureChecks();
    }

    // ======================================================================
    // DiodeModel device-model checks
    // ======================================================================

    private static void RunDiodeModelChecks()
    {
        Console.WriteLine();
        Console.WriteLine("  · DiodeModel 器件模型");

        var parameters = new AnalysisParameters { ThermalVoltageUt = 0.026 };
        var silicon = DiodeModel.FromElement(MakeDiodeElement(SemiconductorMaterial.Silicon), parameters);
        var germanium = DiodeModel.FromElement(MakeDiodeElement(SemiconductorMaterial.Germanium), parameters);
        var customVt = DiodeModel.FromElement(MakeDiodeElement(SemiconductorMaterial.Silicon),
            new AnalysisParameters { ThermalVoltageUt = 0.025 });
        var fallbackVt = DiodeModel.FromElement(MakeDiodeElement(SemiconductorMaterial.Silicon),
            new AnalysisParameters { ThermalVoltageUt = 0.0 });

        CheckResult.Close("模型: 硅管正向压降 VF = 0.7 V", 0.7, silicon.ForwardVoltageDrop);
        CheckResult.Close("模型: 锗管正向压降 VF = 0.3 V", 0.3, germanium.ForwardVoltageDrop);
        CheckResult.Close("模型: 默认饱和电流 Is = 1e-12 A", 1e-12, silicon.SaturationCurrent);
        CheckResult.Close("模型: 默认理想因子 n = 1", 1.0, silicon.IdealityFactor);
        CheckResult.Close("模型: Vt 来自 AnalysisParameters", 0.026, silicon.ThermalVoltage);
        CheckResult.Close("模型: 非法 Vt 回退 0.026 V", 0.026, fallbackVt.ThermalVoltage);
        CheckResult.Close("模型: Vt=0.025 时按参数取值", 0.025, customVt.ThermalVoltage);
        CheckResult.Check(silicon.ThermalVoltageScale > 0 && double.IsFinite(silicon.ThermalVoltageScale),
            "模型: n·Vt 为有限正数");

        // Shockley current at the origin is exactly zero, reverse current saturates at -Is.
        CheckResult.Near("模型: Current(0 V) = 0", 0.0, silicon.Current(0.0), 1e-18, " A");
        CheckResult.Near("模型: 反向电流饱和在 -Is", -1e-12, silicon.Current(-1.0), 1e-20, " A");
        double expectedForward = 1e-12 * (Math.Exp(0.7 / 0.026) - 1.0);
        CheckResult.Close("模型: Shockley 正向电流公式", expectedForward, silicon.Current(0.7), 1e-9);
        CheckResult.Check(silicon.Current(0.3) < silicon.Current(0.5) && silicon.Current(0.5) < silicon.Current(0.7),
            "模型: 正向电流随电压单调增加");

        // Exponential clamping: any finite voltage is safe and saturates at the clamp.
        double clampCurrent = silicon.Current(DiodeModel.MaximumForwardVoltage);
        CheckResult.Check(double.IsFinite(clampCurrent) && clampCurrent > 0,
            "模型: 指数钳位后电流为有限值", $"Is={clampCurrent:G6} A");
        CheckResult.Close("模型: 超过钳位电压后电流保持不变", clampCurrent, silicon.Current(5.0), 1e-12);
        CheckResult.Close("模型: 深度反偏电压被钳位到 -0.99 V", DiodeModel.MaximumReverseVoltage,
            silicon.ClampVoltage(-50.0), 1e-12, " V");
        CheckResult.Close("模型: 正向钳位电压上限 = 0.99 V", DiodeModel.MaximumForwardVoltage,
            silicon.ClampVoltage(50.0), 1e-12, " V");
        CheckResult.Check(double.IsFinite(silicon.ForwardClampCurrent), "模型: 钳位电流有限");

        // Conductance must equal the numerical derivative of Current (<= 1e-6).
        checkConductanceConsistency("模型: Conductance 与数值微分一致 @0.40 V", silicon, 0.40);
        checkConductanceConsistency("模型: Conductance 与数值微分一致 @0.55 V", silicon, 0.55);
        checkConductanceConsistency("模型: Conductance 与数值微分一致 @-0.20 V", silicon, -0.20);
        CheckResult.Near("模型: 钳位区微分电导为 0", 0.0, silicon.Conductance(5.0), 1e-18, " S");

        // Region classification: reverse / blocked forward / conducting.
        CheckResult.Check(silicon.Region(-1.0) == DiodeRegion.Reverse, "模型: Region(-1 V) = Reverse");
        CheckResult.Check(silicon.Region(0.0) == DiodeRegion.Off, "模型: Region(0 V) = Off");
        CheckResult.Check(silicon.Region(0.4) == DiodeRegion.Off, "模型: Region(0.4 V) = Off");
        CheckResult.Check(silicon.Region(0.7) == DiodeRegion.On, "模型: Region(0.7 V) = On");
        CheckResult.Check(germanium.Region(0.4) == DiodeRegion.On, "模型: 锗管 Region(0.4 V) = On（VF=0.3 V）");
        CheckResult.Check(germanium.Region(0.2) == DiodeRegion.Off, "模型: 锗管 Region(0.2 V) = Off");

        // Inverse relation: VoltageForCurrent(Current(v)) == v.
        CheckResult.Close("模型: VoltageForCurrent 与 Current 互逆 @0.5 V",
            0.5, silicon.VoltageForCurrent(silicon.Current(0.5)), 1e-9);

        static void checkConductanceConsistency(string name, DiodeModel model, double voltage)
        {
            const double h = 1e-6;
            double analytic = model.Conductance(voltage);
            double numeric = (model.Current(voltage + h) - model.Current(voltage - h)) / (2.0 * h);
            bool ok = double.IsFinite(analytic) && double.IsFinite(numeric) && analytic > 0
                      && Math.Abs(analytic - numeric) <= 1e-6 * analytic;
            CheckResult.Check(ok, name, $"解析 {analytic:G8} S, 数值 {numeric:G8} S");
        }
    }

    // ======================================================================
    // DetectSubtype checks
    // ======================================================================

    private static void RunDetectionChecks()
    {
        Console.WriteLine();
        Console.WriteLine("  · DetectSubtype 分类");

        CheckDetect("DetectSubtype: 无二极管 → null", BuildNoDiode(), null);
        CheckDetect("DetectSubtype: 半波整流", BuildHalfWave(), "HalfWaveRectifier");
        CheckDetect("DetectSubtype: 半波整流+电容滤波", BuildHalfWaveFiltered(), "HalfWaveRectifierFiltered");
        CheckDetect("DetectSubtype: 桥式整流", BuildBridge(filtered: false), "BridgeRectifier");
        CheckDetect("DetectSubtype: 桥式整流+电容滤波", BuildBridge(filtered: true), "BridgeRectifierFiltered");
        CheckDetect("DetectSubtype: 串联限幅", BuildSeriesLimiter(withBias: false), "SeriesLimiter");
        CheckDetect("DetectSubtype: 串联限幅（含偏置）", BuildSeriesLimiter(withBias: true), "SeriesLimiter");
        CheckDetect("DetectSubtype: 双向限幅", BuildDoubleLimiter(), "DoubleLimiter");
        CheckDetect("DetectSubtype: 钳位电路", BuildClamper(), "Clamper");
        CheckDetect("DetectSubtype: 峰值检波", BuildPeakDetector(), "PeakDetector");
        CheckDetect("DetectSubtype: 直流偏置", BuildDiodeBias(), "DiodeBias");

        // The contract allows any non-null fallback for unsupported diode networks,
        // so long as a diode is present the classifier never sees null.
        var twoSeries = BuildTwoSeriesDiodes();
        string? fallback = DiodeCircuitStrategy.DetectSubtype(twoSeries);
        CheckResult.Check(fallback != null, "DetectSubtype: 无法识别的二极管网络仍返回非 null", $"实际 {fallback ?? "null"}");
    }

    private static void CheckDetect(string name, ElectricalNetlist netlist, string? expected)
    {
        string? actual;
        try
        {
            actual = DiodeCircuitStrategy.DetectSubtype(netlist);
        }
        catch (Exception ex)
        {
            CheckResult.Check(false, name, $"抛出异常 {ex.GetType().Name}: {ex.Message}");
            return;
        }
        CheckResult.Check(actual == expected, name, $"expected {expected ?? "null"}, got {actual ?? "null"}");
    }

    // ======================================================================
    // 1. HalfWaveRectifier
    // ======================================================================

    private static void RunHalfWaveRectifierCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 1/9：半波整流（电阻负载）");
        var solution = Solve("HalfWaveRectifier", BuildHalfWave());
        CheckResult.Check(solution != null, "半波整流: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "HalfWaveRectifier");

        const double vm = 10.0;
        const double vf = 0.7;
        const double rl = 1000.0;
        double vom = vm - vf;
        double vavg = vom / Math.PI;
        CheckResult.Close("半波整流: 输出峰值 Vom = Vm−VF", vom, Metric(solution, "Vom"), Tolerance, " V");
        CheckResult.Close("半波整流: 平均值 Vavg = Vom/π", vavg, Metric(solution, "Vavg"), Tolerance, " V");
        CheckResult.Close("半波整流: 有效值 Vrms = Vom/2", vom / 2.0, Metric(solution, "Vrms"), Tolerance, " V");
        CheckResult.Close("半波整流: 反向峰值 PIV = Vm", vm, Metric(solution, "PIV"), Tolerance, " V");
        CheckResult.Close("半波整流: 输出频率 = 输入频率", 50.0, Metric(solution, "fout"), Tolerance, " Hz");
        CheckResult.Close("半波整流: 平均负载电流 Idc", vavg / rl, Metric(solution, "Idc"), Tolerance, " A");
        CheckResult.Close("半波整流: 峰值负载电流 Ipeak", vom / rl, Metric(solution, "Ipeak"), Tolerance, " A");
        double conductionAngle = 180.0 - 2.0 * Math.Asin(vf / vm) * 180.0 / Math.PI;
        CheckResult.Close("半波整流: 导通角", conductionAngle, Metric(solution, "ConductionAngleDeg"), Tolerance, "°");
        CheckResult.TextContains("半波整流: 摘要含可解析指标", solution.SummaryText, "二极管", "Vavg", "PIV", "fout");
    }

    // ======================================================================
    // 2. HalfWaveRectifierFiltered
    // ======================================================================

    private static void RunHalfWaveFilteredCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 2/9：半波整流 + 电容滤波");
        var solution = Solve("HalfWaveRectifierFiltered", BuildHalfWaveFiltered());
        CheckResult.Check(solution != null, "半波滤波: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "HalfWaveRectifierFiltered");

        const double vm = 10.0;
        const double vf = 0.7;
        const double frequency = 50.0;
        const double rl = 1000.0;
        const double c = 100e-6;
        double vom = vm - vf;
        double vr = vom / (frequency * rl * c);
        double vdc = vom - vr / 2.0;
        CheckResult.Close("半波滤波: 充电峰值 Vom = Vm−VF", vom, Metric(solution, "Vom"), Tolerance, " V");
        CheckResult.Close("半波滤波: 纹波 Vr = Vom/(f·R·C)", vr, Metric(solution, "Vr"), Tolerance, " V");
        CheckResult.Close("半波滤波: 输出直流 Vdc = Vom−Vr/2", vdc, Metric(solution, "Vdc"), Tolerance, " V");
        CheckResult.Close("半波滤波: 纹波系数 γ = Vr/Vdc", vr / vdc, Metric(solution, "RippleFactor"), Tolerance);
        CheckResult.Close("半波滤波: PIV ≈ 2Vm", 2.0 * vm, Metric(solution, "PIV"), Tolerance, " V");
        CheckResult.Close("半波滤波: 输出频率 = 输入频率", frequency, Metric(solution, "fout"), Tolerance, " Hz");
        CheckResult.Close("半波滤波: 平均负载电流 Idc", vdc / rl, Metric(solution, "Idc"), Tolerance, " A");
        CheckResult.TextContains("半波滤波: 摘要含可解析指标", solution.SummaryText, "二极管", "Vdc", "Vr", "纹波系数");
    }

    // ======================================================================
    // 3. BridgeRectifier
    // ======================================================================

    private static void RunBridgeRectifierCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 3/9：桥式全波整流");
        var solution = Solve("BridgeRectifier", BuildBridge(filtered: false));
        CheckResult.Check(solution != null, "桥式整流: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "BridgeRectifier");

        const double vm = 10.0;
        const double vf = 0.7;
        const double rl = 1000.0;
        double vom = vm - 2.0 * vf;
        double vavg = 2.0 * vom / Math.PI;
        CheckResult.Close("桥式整流: 输出峰值 Vom = Vm−2VF", vom, Metric(solution, "Vom"), Tolerance, " V");
        CheckResult.Close("桥式整流: 平均值 Vavg = 2Vom/π", vavg, Metric(solution, "Vavg"), Tolerance, " V");
        CheckResult.Close("桥式整流: 有效值 Vrms = Vom/√2", vom / Math.Sqrt(2.0), Metric(solution, "Vrms"), Tolerance, " V");
        CheckResult.Close("桥式整流: 每只二极管 PIV = Vm", vm, Metric(solution, "PIV"), Tolerance, " V");
        CheckResult.Close("桥式整流: 输出频率 = 2f", 100.0, Metric(solution, "fout"), Tolerance, " Hz");
        CheckResult.Close("桥式整流: 平均负载电流 Idc", vavg / rl, Metric(solution, "Idc"), Tolerance, " A");
        CheckResult.TextContains("桥式整流: 摘要含可解析指标", solution.SummaryText, "二极管", "Vavg", "PIV", "fout");
    }

    // ======================================================================
    // 4. BridgeRectifierFiltered
    // ======================================================================

    private static void RunBridgeFilteredCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 4/9：桥式整流 + 电容滤波");
        var solution = Solve("BridgeRectifierFiltered", BuildBridge(filtered: true));
        CheckResult.Check(solution != null, "桥式滤波: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "BridgeRectifierFiltered");

        const double vm = 10.0;
        const double vf = 0.7;
        const double frequency = 50.0;
        const double rl = 1000.0;
        const double c = 100e-6;
        double vom = vm - 2.0 * vf;
        double vr = vom / (2.0 * frequency * rl * c);
        double vdc = vom - vr / 2.0;
        CheckResult.Close("桥式滤波: 输出峰值 Vom = Vm−2VF", vom, Metric(solution, "Vom"), Tolerance, " V");
        CheckResult.Close("桥式滤波: 纹波 Vr = Vom/(2f·R·C)", vr, Metric(solution, "Vr"), Tolerance, " V");
        CheckResult.Close("桥式滤波: 输出直流 Vdc", vdc, Metric(solution, "Vdc"), Tolerance, " V");
        CheckResult.Close("桥式滤波: 纹波系数 γ", vr / vdc, Metric(solution, "RippleFactor"), Tolerance);
        CheckResult.Close("桥式滤波: 每只二极管 PIV ≈ Vm", vm, Metric(solution, "PIV"), Tolerance, " V");
        CheckResult.Close("桥式滤波: 输出频率 = 2f", 2.0 * frequency, Metric(solution, "fout"), Tolerance, " Hz");
        CheckResult.TextContains("桥式滤波: 摘要含可解析指标", solution.SummaryText, "二极管", "Vdc", "Vr", "PIV");
    }

    // ======================================================================
    // 5. SeriesLimiter
    // ======================================================================

    private static void RunSeriesLimiterCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 5/9：串联限幅（上限幅）");
        var solution = Solve("SeriesLimiter", BuildSeriesLimiter(withBias: false));
        CheckResult.Check(solution != null, "串联限幅: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "SeriesLimiter");

        const double vm = 10.0;
        const double vf = 0.7;
        const double r = 1000.0;
        const double load = 10_000.0;
        double k = load / (r + load); // output divider gain when the diode is off
        // Upper limiter: diode anode at the output node, cathode at ground.
        CheckResult.Close("串联限幅: 门限 Vth = VF", vf, Metric(solution, "Vth"), Tolerance, " V");
        CheckResult.Close("串联限幅: 线性区增益 k = RL/(Rs+RL)", k, Metric(solution, "k"), Tolerance);
        CheckResult.Close("串联限幅: 输出最大值 = Vth", vf, Metric(solution, "VoutMax"), Tolerance, " V");
        CheckResult.Close("串联限幅: 输出最小值 = −k·Vm", -vm * k, Metric(solution, "VoutMin"), Tolerance, " V");
        CheckResult.Close("串联限幅: 峰值限幅电流 Ipeak", (vm - vf) / r, Metric(solution, "Ipeak"), Tolerance, " A");
        CheckResult.TextContains("串联限幅: 摘要含可解析指标", solution.SummaryText, "二极管", "门限", "Vth");

        // Without the optional output load the linear-region slope returns to 1.
        var noLoad = Solve("SeriesLimiter", BuildSeriesLimiter(withBias: false, includeLoad: false));
        CheckResult.Check(noLoad != null, "串联限幅(无负载): 求解不抛异常");
        if (noLoad != null)
        {
            CheckResult.Close("串联限幅(无负载): 输出最大值 = Vth", vf, Metric(noLoad, "VoutMax"), Tolerance, " V");
            CheckResult.Close("串联限幅(无负载): 输出最小值 = −Vm", -vm, Metric(noLoad, "VoutMin"), Tolerance, " V");
            CheckResult.Close("串联限幅(无负载): 线性区增益 k = 1", 1.0, Metric(noLoad, "k"), Tolerance);
        }

        // Optional DC bias shifts the clipping threshold: Vth = Vbias + VF.
        var biased = Solve("SeriesLimiter", BuildSeriesLimiter(withBias: true));
        CheckResult.Check(biased != null, "串联限幅(偏置): 求解不抛异常");
        if (biased == null) return;
        CheckResult.Close("串联限幅(偏置): 门限 Vth = Vbias+VF", 2.5 + 0.7, Metric(biased, "Vth"), Tolerance, " V");
        CheckResult.Close("串联限幅(偏置): 输出最大值 = Vth", 3.2, Metric(biased, "VoutMax"), Tolerance, " V");
        CheckResult.Close("串联限幅(偏置): 输出最小值 = −k·Vm", -vm * k, Metric(biased, "VoutMin"), Tolerance, " V");
    }

    // ======================================================================
    // 6. DoubleLimiter
    // ======================================================================

    private static void RunDoubleLimiterCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 6/9：双向限幅（双管 + 正负偏置）");
        var solution = Solve("DoubleLimiter", BuildDoubleLimiter());
        CheckResult.Check(solution != null, "双向限幅: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "DoubleLimiter");

        // D1: anode at output, cathode at +3 V -> VthHigh = 3 + 0.7 = 3.7 V.
        // D2: cathode at output, anode at -2 V -> VthLow = -2 - 0.7 = -2.7 V.
        CheckResult.Close("双向限幅: 上限幅电平 VthHigh", 3.7, Metric(solution, "VthHigh"), Tolerance, " V");
        CheckResult.Close("双向限幅: 下限幅电平 VthLow", -2.7, Metric(solution, "VthLow"), Tolerance, " V");
        CheckResult.Close("双向限幅: 输出最大值", 3.7, Metric(solution, "VoutMax"), Tolerance, " V");
        CheckResult.Close("双向限幅: 输出最小值", -2.7, Metric(solution, "VoutMin"), Tolerance, " V");
        CheckResult.Close("双向限幅: 输出摆幅", 6.4, Metric(solution, "Swing"), Tolerance, " V");
        CheckResult.Close("双向限幅: 线性区增益 k = RL/(Rs+RL)", 10.0 / 11.0, Metric(solution, "k"), Tolerance);
        CheckResult.TextContains("双向限幅: 摘要含可解析指标", solution.SummaryText, "二极管", "VthHigh", "VthLow", "摆幅");
    }

    // ======================================================================
    // 7. Clamper
    // ======================================================================

    private static void RunClamperCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 7/9：二极管钳位（正钳位）");
        var solution = Solve("Clamper", BuildClamper());
        CheckResult.Check(solution != null, "钳位: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "Clamper");

        const double vm = 10.0;
        const double vf = 0.7;
        // Positive clamper: cathode at the output node clamps the negative peak
        // at -VF; the coupling capacitor then shifts the waveform by Vm - VF.
        CheckResult.Close("钳位: 钳位电平 = −VF", -vf, Metric(solution, "ClampLevel"), Tolerance, " V");
        CheckResult.Close("钳位: 输出直流偏移 Vdc = Vm−VF", vm - vf, Metric(solution, "Vdc"), Tolerance, " V");
        CheckResult.Close("钳位: 输出最大值 = 2Vm−VF", 2.0 * vm - vf, Metric(solution, "VoutMax"), Tolerance, " V");
        CheckResult.Close("钳位: 输出最小值 = −VF", -vf, Metric(solution, "VoutMin"), Tolerance, " V");
        CheckResult.Close("钳位: 峰峰值保持 = 2Vm", 2.0 * vm, Metric(solution, "Vpp"), Tolerance, " V");
        CheckResult.TextContains("钳位: 摘要含可解析指标", solution.SummaryText, "二极管", "钳位", "Vdc", "Vpp");
    }

    // ======================================================================
    // 8. PeakDetector
    // ======================================================================

    private static void RunPeakDetectorCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 8/9：峰值/包络检波");
        var solution = Solve("PeakDetector", BuildPeakDetector());
        CheckResult.Check(solution != null, "峰值检波: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "PeakDetector");

        const double vm = 5.0;
        const double vf = 0.7;
        const double frequency = 1000.0;
        const double rl = 100e3;
        const double c = 10e-6;
        double vpeak = vm - vf;
        double tau = rl * c;
        double vr = vpeak / (frequency * rl * c);
        double vdc = vpeak - vr / 2.0;
        CheckResult.Close("峰值检波: 峰值输出 Vpeak = Vm−VF", vpeak, Metric(solution, "Vpeak"), Tolerance, " V");
        CheckResult.Close("峰值检波: 时间常数 τ = R·C", tau, Metric(solution, "Tau"), Tolerance, " s");
        CheckResult.Close("峰值检波: τ/T = τ·f", tau * frequency, Metric(solution, "TauOverT"), Tolerance);
        CheckResult.Close("峰值检波: 纹波 Vr = Vpeak/(fRC)", vr, Metric(solution, "Vr"), Tolerance, " V");
        CheckResult.Close("峰值检波: 输出直流 Vdc = Vpeak−Vr/2", vdc, Metric(solution, "Vdc"), Tolerance, " V");
        CheckResult.TextContains("峰值检波: 摘要含可解析指标", solution.SummaryText, "二极管", "Vpeak", "时间常数", "判据");
        CheckResult.TextContains("峰值检波: 时间常数判据满足 τ≥10T", solution.SummaryText, "满足");
    }

    // ======================================================================
    // 9. DiodeBias (Shockley + Newton)
    // ======================================================================

    private static void RunDiodeBiasCheck()
    {
        Console.WriteLine();
        Console.WriteLine("  · 场景 9/9：直流偏置工作点（Shockley/Newton）");
        var solution = Solve("DiodeBias", BuildDiodeBias());
        CheckResult.Check(solution != null, "直流偏置: 求解不抛异常");
        if (solution == null) return;

        CheckShape(solution, "DiodeBias");

        const double vs = 5.0;
        const double r = 1000.0;
        const double isat = 1e-12;
        const double vt = 0.026;
        // Independent hand solve: Is*(exp(Vd/Vt)-1)*R + Vd = Vs by bisection.
        double expectedVd = SolveShockleyBisection(vs, r, isat, vt);
        double expectedId = isat * (Math.Exp(expectedVd / vt) - 1.0);
        CheckResult.Close("直流偏置: 二极管电压 VD（手算 Shockley 方程）", expectedVd, Metric(solution, "VD"), Tolerance, " V");
        CheckResult.Close("直流偏置: 二极管电流 ID（手算 Shockley 方程）", expectedId, Metric(solution, "ID"), Tolerance, " A");
        CheckResult.Close("直流偏置: Is 默认 1e-12 A", isat, Metric(solution, "Is"), Tolerance, " A");
        CheckResult.Close("直流偏置: Vt 来自分析参数", vt, Metric(solution, "Vt"), Tolerance, " V");
        CheckResult.Close("直流偏置: 回路总电阻", r, Metric(solution, "R"), Tolerance, " Ω");
        CheckResult.Check(TextValue(solution, "State") == "导通", "直流偏置: 状态 = 导通", $"实际 {TextValue(solution, "State")}");
        double iterations = Metric(solution, "Iterations");
        CheckResult.Check(double.IsFinite(iterations) && iterations >= 1 && iterations <= 200,
            "直流偏置: Newton 迭代次数有效", $"实际 {iterations}");
        CheckResult.TextContains("直流偏置: 摘要含可解析指标", solution.SummaryText, "二极管", "ID", "VD", "状态");
    }

    /// <summary>Independent bisection solve of the diode+resistor DC bias loop (Shockley model).</summary>
    private static double SolveShockleyBisection(double vs, double r, double isat, double vt)
    {
        double low = -20.0;
        double high = 0.99;
        for (int i = 0; i < 400; i++)
        {
            double mid = 0.5 * (low + high);
            double residual = isat * (Math.Exp(mid / vt) - 1.0) * r + mid - vs;
            if (!double.IsFinite(residual)) { high = mid; continue; }
            if (residual > 0) high = mid; else low = mid;
        }
        return 0.5 * (low + high);
    }

    // ======================================================================
    // Silicon vs germanium forward-drop difference
    // ======================================================================

    private static void RunMaterialChecks()
    {
        Console.WriteLine();
        Console.WriteLine("  · 材料差异：Si 0.7 V vs Ge 0.3 V");

        var silicon = Solve("HalfWaveRectifier", BuildHalfWave(germanium: false));
        var germanium = Solve("HalfWaveRectifier", BuildHalfWave(germanium: true));
        CheckResult.Check(silicon != null && germanium != null, "材料差异: Si/Ge 半波整流均可求解");
        if (silicon == null || germanium == null) return;

        double siVom = Metric(silicon, "Vom");
        double geVom = Metric(germanium, "Vom");
        CheckResult.Close("材料差异: Si 输出峰值 Vm−0.7", 9.3, siVom, Tolerance, " V");
        CheckResult.Close("材料差异: Ge 输出峰值 Vm−0.3", 9.7, geVom, Tolerance, " V");
        // Silicon has the larger forward drop, so its rectified peak is 0.4 V lower.
        CheckResult.Close("材料差异: Si 峰值比 Ge 低 0.4 V（VF 差异）", -0.4, siVom - geVom, 1e-9, " V");
        CheckResult.Close("材料差异: Si 平均值 Vavg", 9.3 / Math.PI, Metric(silicon, "Vavg"), Tolerance, " V");
        CheckResult.Close("材料差异: Ge 平均值 Vavg", 9.7 / Math.PI, Metric(germanium, "Vavg"), Tolerance, " V");

        var siLimiter = Solve("SeriesLimiter", BuildSeriesLimiter(withBias: false, germanium: false));
        var geLimiter = Solve("SeriesLimiter", BuildSeriesLimiter(withBias: false, germanium: true));
        CheckResult.Check(siLimiter != null && geLimiter != null, "材料差异: Si/Ge 限幅电路均可求解");
        if (siLimiter == null || geLimiter == null) return;
        CheckResult.Close("材料差异: Si 限幅门限 = 0.7 V", 0.7, Metric(siLimiter, "Vth"), Tolerance, " V");
        CheckResult.Close("材料差异: Ge 限幅门限 = 0.3 V", 0.3, Metric(geLimiter, "Vth"), Tolerance, " V");
        CheckResult.Close("材料差异: 门限差 = 0.4 V", 0.4, Metric(siLimiter, "Vth") - Metric(geLimiter, "Vth"), 1e-9, " V");
    }

    // ======================================================================
    // Failure paths
    // ======================================================================

    private static void RunFailureChecks()
    {
        Console.WriteLine();
        Console.WriteLine("  · 失败路径（拓扑不匹配 / 无解 / 缺地）");

        // Unknown subtype: explicit failure, never an exception.
        var unknown = Solve("NotADiodeSubtype", BuildHalfWave());
        CheckResult.Check(unknown != null && !unknown.IsSuccess, "失败路径: 未知子类型返回 Failure");
        if (unknown != null)
            CheckResult.TextContains("失败路径: 未知子类型消息含二极管", unknown.SummaryText, "二极管", "不支持");

        // Topology mismatch: a bridge netlist requested as a half-wave rectifier.
        var mismatch = Solve("HalfWaveRectifier", BuildBridge(filtered: false));
        CheckResult.Check(mismatch != null && !mismatch.IsSuccess, "失败路径: 拓扑不匹配返回 Failure（桥式网表当半波）");
        if (mismatch != null)
            CheckResult.TextContains("失败路径: 拓扑不匹配消息点名二极管与具体原因", mismatch.SummaryText, "二极管", "桥式");

        // No solution: input peak below the diode forward drop -> never conducts.
        var belowDrop = Solve("HalfWaveRectifier", BuildHalfWave(vm: 0.5));
        CheckResult.Check(belowDrop != null && !belowDrop.IsSuccess, "失败路径: Vm<VF 返回 Failure（无整流输出）");
        if (belowDrop != null)
            CheckResult.TextContains("失败路径: Vm<VF 消息说明二极管截止", belowDrop.SummaryText, "二极管", "截止");

        // DiodeBias on an AC waveform netlist is not a DC bias problem.
        var wrongFamily = Solve("DiodeBias", BuildHalfWave());
        CheckResult.Check(wrongFamily != null && !wrongFamily.IsSuccess, "失败路径: 二极管偏置遇到交流网表返回 Failure");
        if (wrongFamily != null)
            CheckResult.TextContains("失败路径: 交流网表消息点名二极管限制", wrongFamily.SummaryText, "二极管", "交流源");

        // SeriesLimiter on a series-diode rectifier netlist.
        var wrongLimiter = Solve("SeriesLimiter", BuildHalfWave());
        CheckResult.Check(wrongLimiter != null && !wrongLimiter.IsSuccess, "失败路径: 串联限幅拓扑不匹配返回 Failure");
        if (wrongLimiter != null)
            CheckResult.TextContains("失败路径: 串联限幅消息说明二极管未接直流轨", wrongLimiter.SummaryText, "二极管", "直流轨");

        // Missing ground: netlist validation must fail with the diode name present.
        var noGround = Solve("DiodeBias", BuildNoGround());
        CheckResult.Check(noGround != null && !noGround.IsSuccess, "失败路径: 缺少参考地返回 Failure");
        if (noGround != null)
            CheckResult.TextContains("失败路径: 缺少参考地消息含二极管", noGround.SummaryText, "二极管", "地");

        // Bridge with mixed silicon/germanium diodes is explicitly rejected.
        var mixed = Solve("BridgeRectifier", BuildMixedBridge());
        CheckResult.Check(mixed != null && !mixed.IsSuccess, "失败路径: 桥式整流混合材料返回 Failure");
        if (mixed != null)
            CheckResult.TextContains("失败路径: 混合材料消息说明原因", mixed.SummaryText, "二极管", "材料");

        // Result mapping of failures: no partial Q-point / AC data is invented.
        var failure = Solve("HalfWaveRectifier", BuildBridge(filtered: false));
        CheckResult.Check(failure != null && failure.QPoint == null && failure.AcParameters == null,
            "失败路径: Failure 不伪造 QPoint/AcParameters 数据");
    }

    // ======================================================================
    // Shared shape checks
    // ======================================================================

    private static void CheckShape(PedagogicalSolution solution, string subtype, int minSteps = 4)
    {
        CheckResult.Check(solution.IsSuccess, $"{subtype}: 求解成功（IsSuccess）");
        CheckResult.Check(solution.TopologyType == CircuitTopologyType.DiodeCircuit,
            $"{subtype}: TopologyType = DiodeCircuit", $"实际 {solution.TopologyType}");
        CheckResult.Check(solution.SubtypeName == subtype, $"{subtype}: SubtypeName 与构造参数一致",
            $"实际 {solution.SubtypeName}");
        CheckResult.Check(solution.QPoint != null
                           && solution.QPoint.Ibq == "—"
                           && solution.QPoint.Icq == "—"
                           && solution.QPoint.Vceq == "—"
                           && solution.QPoint.WorkingRegion == "二极管非线性电路"
                           && !solution.QPoint.IsActiveRegion,
            $"{subtype}: QPoint 映射（二极管非线性电路 / —）");
        CheckResult.Check(solution.AcParameters == null, $"{subtype}: AcParameters 保持 null");
        CheckResult.Check(solution.Steps.Count >= minSteps, $"{subtype}: 推导步骤 ≥ {minSteps}",
            $"实际 {solution.Steps.Count} 步");
        bool ordered = solution.Steps.Select((step, index) => step.StepIndex == index + 1).All(ok => ok);
        CheckResult.Check(ordered, $"{subtype}: 步骤编号连续");
        bool stepsComplete = solution.Steps.Count > 0 && solution.Steps.All(step =>
            !string.IsNullOrWhiteSpace(step.Title)
            && !string.IsNullOrWhiteSpace(step.Content)
            && !string.IsNullOrWhiteSpace(step.LatexFormula)
            && !string.IsNullOrWhiteSpace(step.SubstitutionFormula)
            && !string.IsNullOrWhiteSpace(step.ResultValue)
            && step.Values.Count > 0);
        CheckResult.Check(stepsComplete, $"{subtype}: 每步 Title/Content/Latex/Substitution/Result/Values 齐全");
        bool summaryHasMetrics = !string.IsNullOrWhiteSpace(solution.SummaryText)
                                 && solution.SummaryText.Contains("二极管", StringComparison.Ordinal)
                                 && solution.SummaryText.Contains('=', StringComparison.Ordinal);
        CheckResult.Check(summaryHasMetrics, $"{subtype}: SummaryText 一行含可解析指标");
    }

    // ======================================================================
    // Fixture builders (TestNetlistBuilder; net 0 is ground)
    // ======================================================================

    private static CircuitElement MakeDiodeElement(SemiconductorMaterial material) => new()
    {
        Id = "D_model",
        Name = "D_model",
        Type = ElementType.Diode,
        DisplayNumber = material == SemiconductorMaterial.Germanium ? 0.3 : 0.7,
        UnitSymbol = "V",
        Material = material
    };

    /// <summary>Half-wave rectifier: AC source → series diode → resistive load to ground.</summary>
    private static ElectricalNetlist BuildHalfWave(double vm = 10.0, bool germanium = false) =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, vm, "V", frequencyHz: 50.0)
            .Element("D1", ElementType.Diode, 0.7, "V",
                material: germanium ? SemiconductorMaterial.Germanium : SemiconductorMaterial.Silicon)
            .Element("RL", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("D1", "A", 1).Pin("D1", "K", 2)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

    /// <summary>Half-wave rectifier with a shunt filter capacitor across the load.</summary>
    private static ElectricalNetlist BuildHalfWaveFiltered() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 50.0)
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("C1", ElementType.Capacitor, 100.0, "μF")
            .Element("RL", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("D1", "A", 1).Pin("D1", "K", 2)
            .Pin("C1", "1", 2).Pin("C1", "2", 0)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

    /// <summary>Bridge rectifier: floating AC source across the bridge, GND = negative output.</summary>
    private static ElectricalNetlist BuildBridge(bool filtered)
    {
        var builder = new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 50.0)
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("D2", ElementType.Diode, 0.7, "V")
            .Element("D3", ElementType.Diode, 0.7, "V")
            .Element("D4", ElementType.Diode, 0.7, "V")
            .Element("RL", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 2)
            .Pin("D1", "A", 1).Pin("D1", "K", 3)
            .Pin("D2", "A", 2).Pin("D2", "K", 3)
            .Pin("D3", "A", 0).Pin("D3", "K", 1)
            .Pin("D4", "A", 0).Pin("D4", "K", 2)
            .Pin("RL", "1", 3).Pin("RL", "2", 0);
        if (filtered)
        {
            builder.Element("C1", ElementType.Capacitor, 100.0, "μF")
                .Pin("C1", "1", 3).Pin("C1", "2", 0);
        }
        return builder.Build();
    }

    private static ElectricalNetlist BuildMixedBridge() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 50.0)
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("D2", ElementType.Diode, 0.7, "V")
            .Element("D3", ElementType.Diode, 0.3, "V", material: SemiconductorMaterial.Germanium)
            .Element("D4", ElementType.Diode, 0.7, "V")
            .Element("RL", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 2)
            .Pin("D1", "A", 1).Pin("D1", "K", 3)
            .Pin("D2", "A", 2).Pin("D2", "K", 3)
            .Pin("D3", "A", 0).Pin("D3", "K", 1)
            .Pin("D4", "A", 0).Pin("D4", "K", 2)
            .Pin("RL", "1", 3).Pin("RL", "2", 0)
            .Build();

    /// <summary>
    /// Textbook upper-limit limiter: AC source → series R → output node, with the
    /// diode from output to ground (or DC bias). An optional 10 kΩ output load R_L
    /// is placed from the output node to ground, matching the D05 .circuit file.
    /// </summary>
    private static ElectricalNetlist BuildSeriesLimiter(bool withBias, bool germanium = false, bool includeLoad = true)
    {
        var material = germanium ? SemiconductorMaterial.Germanium : SemiconductorMaterial.Silicon;
        var builder = new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 50.0)
            .Element("R1", ElementType.Resistor, 1.0, "kΩ")
            .Element("D1", ElementType.Diode, 0.7, "V", material: material)
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2);
        if (withBias)
        {
            builder.Element("V2", ElementType.DcVoltageSource, 2.5, "V")
                .Pin("V2", "pos", 3).Pin("V2", "neg", 0)
                .Pin("D1", "A", 2).Pin("D1", "K", 3);
        }
        else
        {
            builder.Pin("D1", "A", 2).Pin("D1", "K", 0);
        }
        if (includeLoad)
        {
            builder.Element("RL", ElementType.Resistor, 10.0, "kΩ")
                .Pin("RL", "1", 2).Pin("RL", "2", 0);
        }
        return builder.Build();
    }

    /// <summary>
    /// Double limiter: series R + upper diode at +3 V bias + lower diode at -2 V bias,
    /// plus a 10 kΩ output load R_L to ground (standard textbook drawing).
    /// </summary>
    private static ElectricalNetlist BuildDoubleLimiter() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 50.0)
            .Element("R1", ElementType.Resistor, 1.0, "kΩ")
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("V2", ElementType.DcVoltageSource, 3.0, "V")
            .Element("D2", ElementType.Diode, 0.7, "V")
            .Element("V3", ElementType.DcVoltageSource, 2.0, "V")
            .Element("RL", ElementType.Resistor, 10.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("D1", "A", 2).Pin("D1", "K", 3)
            .Pin("V2", "pos", 3).Pin("V2", "neg", 0)
            .Pin("D2", "A", 4).Pin("D2", "K", 2)
            .Pin("V3", "pos", 0).Pin("V3", "neg", 4)
            .Pin("RL", "1", 2).Pin("RL", "2", 0)
            .Build();

    /// <summary>Positive clamper: coupling capacitor in series, diode cathode at the output node to ground.</summary>
    private static ElectricalNetlist BuildClamper() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 10.0, "V", frequencyHz: 1000.0)
            .Element("C1", ElementType.Capacitor, 10.0, "μF")
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("C1", "1", 1).Pin("C1", "2", 2)
            .Pin("D1", "A", 0).Pin("D1", "K", 2)
            .Build();

    /// <summary>Peak detector: source → charging R → series diode → shunt C ∥ load R.</summary>
    private static ElectricalNetlist BuildPeakDetector() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 5.0, "V", frequencyHz: 1000.0)
            .Element("RS", ElementType.Resistor, 1.0, "kΩ")
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("C1", ElementType.Capacitor, 10.0, "μF")
            .Element("RL", ElementType.Resistor, 100.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("RS", "1", 1).Pin("RS", "2", 2)
            .Pin("D1", "A", 2).Pin("D1", "K", 3)
            .Pin("C1", "1", 3).Pin("C1", "2", 0)
            .Pin("RL", "1", 3).Pin("RL", "2", 0)
            .Build();

    /// <summary>DC bias: source → series R → forward diode → ground.</summary>
    private static ElectricalNetlist BuildDiodeBias(double vs = 5.0, double r = 1.0) =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, vs, "V")
            .Element("R1", ElementType.Resistor, r, "kΩ")
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 2)
            .Pin("D1", "A", 2).Pin("D1", "K", 0)
            .Build();

    private static ElectricalNetlist BuildNoDiode() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 5.0, "V", frequencyHz: 1000.0)
            .Element("R1", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("R1", "1", 1).Pin("R1", "2", 0)
            .Build();

    private static ElectricalNetlist BuildTwoSeriesDiodes() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.AcVoltageSource, 5.0, "V", frequencyHz: 50.0)
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Element("D2", ElementType.Diode, 0.7, "V")
            .Element("R1", ElementType.Resistor, 1.0, "kΩ")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("D1", "A", 1).Pin("D1", "K", 2)
            .Pin("D2", "A", 2).Pin("D2", "K", 3)
            .Pin("R1", "1", 3).Pin("R1", "2", 0)
            .Build();

    private static ElectricalNetlist BuildNoGround() =>
        new TestNetlistBuilder()
            .Element("V1", ElementType.DcVoltageSource, 5.0, "V")
            .Element("R1", ElementType.Resistor, 1.0, "kΩ")
            .Element("D1", ElementType.Diode, 0.7, "V")
            .Pin("V1", "pos", 1).Pin("V1", "neg", 2)
            .Pin("R1", "1", 1).Pin("R1", "2", 3)
            .Pin("D1", "A", 3).Pin("D1", "K", 2)
            .Build();

    // ======================================================================
    // Execution / parsing helpers
    // ======================================================================

    private static PedagogicalSolution? Solve(string subtype, ElectricalNetlist netlist)
    {
        try
        {
            return new DiodeCircuitStrategy(subtype)
                .SolveAsync(netlist, new AnalysisParameters())
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [异常] {subtype}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static double Metric(PedagogicalSolution solution, string key)
    {
        foreach (var step in solution.Steps)
        {
            if (step.Values.TryGetValue(key, out string? raw) && raw != null)
                return ParseMetric(raw);
        }
        return double.NaN;
    }

    private static string TextValue(PedagogicalSolution solution, string key)
    {
        foreach (var step in solution.Steps)
        {
            if (step.Values.TryGetValue(key, out string? raw)) return raw ?? string.Empty;
        }
        return string.Empty;
    }

    private static double ParseMetric(string raw)
    {
        if (raw == "∞") return double.PositiveInfinity;
        if (raw == "-∞") return double.NegativeInfinity;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : double.NaN;
    }
}
