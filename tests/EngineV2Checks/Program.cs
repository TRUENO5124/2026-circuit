// ============================================================================
// File: tests/EngineV2Checks/Program.cs
// Description: Empirical verification harness for the MNA 2.0 linear-network
//              engine (Engine/Linear/LinearNetworkAnalyzer.cs and the rewritten
//              MnaLinearStrategy). Runs with:
//                  dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
//              Every check prints [PASS]/[FAIL]; the last line is
//              "Total=x Passed=y Failed=z" and the exit code is non-zero on
//              any failure. The harness links production sources explicitly
//              (same pattern as the other console harnesses in tests/).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Models;

namespace EngineV2Checks;

internal static class Program
{
    private static int _total;
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = new();

    private static int Main()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* console may not allow it */ }
        Console.WriteLine("================================================================================");
        Console.WriteLine("ENGINE V2 CHECKS: MNA 2.0 通用线性网络分析器 / 受控源 / 戴维南等效 / 相量频响");
        Console.WriteLine("================================================================================");

        RunSection("DC 直流工作点", CheckDcMode);
        RunSection("AC 正弦稳态相量", CheckAcPhasors);
        RunSection("AC 频率扫描", CheckAcSweep);
        RunSection("端口等效 / 戴维南", CheckEquivalents);
        RunSection("错误路径与数值边界", CheckErrorPaths);
        RunSection("取消语义", CheckCancellation);
        RunSection("MnaLinearStrategy 结构化结果", CheckStrategyContract);
        RunSection("对抗审计回归（越界截止 / 激励源优先级）", CheckAuditRegressions);
        RunSection("分类器 MNA 文案", CheckClassifierText);
        RunSection("PNP 与硅/锗压降（BJT 教学策略）", CheckPnpAndMaterialSemantics);

        Console.WriteLine();
        Console.WriteLine($"Total={_total} Passed={_passed} Failed={_failed}");
        if (_failed > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (var failure in _failures) Console.WriteLine("  " + failure);
            return 1;
        }
        return 0;
    }

    private static void RunSection(string name, Action section)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {name} ---");
        try
        {
            section();
        }
        catch (Exception ex)
        {
            Assert(false, $"{name}: 段内未处理异常", ex.ToString());
        }
    }

    // =====================================================================
    // Assertions
    // =====================================================================

    private static void Assert(bool condition, string name, string details = "")
    {
        _total++;
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failed++;
            _failures.Add($"{name}: {details}");
            Console.WriteLine($"  [FAIL] {name} -> {details}");
        }
    }

    private static void AssertNear(double actual, double expected, double absoluteTolerance, string name, string unit = "")
    {
        bool ok = double.IsFinite(actual) && Math.Abs(actual - expected) <= absoluteTolerance;
        Assert(ok, name, $"expected {expected.ToString("G8", CultureInfo.InvariantCulture)}{unit}, got {actual.ToString("G8", CultureInfo.InvariantCulture)}");
    }

    private static void AssertRel(double actual, double expected, double relativeTolerance, string name, string unit = "")
    {
        bool ok = double.IsFinite(actual) && double.IsFinite(expected) &&
                  Math.Abs(actual - expected) <= relativeTolerance * Math.Max(1e-12, Math.Abs(expected));
        Assert(ok, name, $"expected {expected.ToString("G8", CultureInfo.InvariantCulture)}{unit}, got {actual.ToString("G8", CultureInfo.InvariantCulture)}");
    }

    private static void AssertPhase(double actualDeg, double expectedDeg, double toleranceDeg, string name)
    {
        double difference = Math.Abs(((actualDeg - expectedDeg + 540.0) % 360.0) - 180.0);
        Assert(difference <= toleranceDeg, name,
            $"expected {expectedDeg.ToString("0.###", CultureInfo.InvariantCulture)}°, got {actualDeg.ToString("0.###", CultureInfo.InvariantCulture)}° (diff {difference:G4}°)");
    }

    // =====================================================================
    // Netlist fixture builder
    // =====================================================================

    private sealed class NetBuilder
    {
        private readonly ElectricalNetlist _netlist = new();

        public NetBuilder Element(string id, ElementType type, double value, string unit = "", double frequencyHz = 1000.0,
            SemiconductorMaterial material = SemiconductorMaterial.Silicon)
        {
            var element = new CircuitElement
            {
                Id = id,
                Name = id,
                Type = type,
                DisplayNumber = value,
                UnitSymbol = unit,
                FrequencyHz = frequencyHz,
                Material = material
            };
            element.InitializeDefaultPins();
            _netlist.Elements[id] = element;
            return this;
        }

        public NetBuilder Pin(string elementId, string pinId, int netId)
        {
            var net = EnsureNet(netId);
            net.ConnectedPins.Add(new ComponentPinRef(elementId, pinId, pinId));
            return this;
        }

        public NetBuilder Name(int netId, string name)
        {
            EnsureNet(netId).Name = name;
            return this;
        }

        private ElectricalNet EnsureNet(int netId)
        {
            var existing = _netlist.Nets.FirstOrDefault(n => n.NetId == netId);
            if (existing != null) return existing;
            var net = new ElectricalNet
            {
                NetId = netId,
                Name = netId == 0 ? "GND" : "N" + netId,
                IsGround = netId == 0
            };
            _netlist.Nets.Add(net);
            return net;
        }

        public ElectricalNetlist Build()
        {
            foreach (var net in _netlist.Nets.OrderBy(n => n.NetId))
            {
                _netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });
            }
            return _netlist;
        }
    }

    // ----- reusable fixtures -------------------------------------------------

    private static ElectricalNetlist Divider(double sourceVoltage = 12.0, double r1KiloOhm = 1.0, double r2KiloOhm = 1.0)
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, sourceVoltage, "V");
        builder.Element("R1", ElementType.Resistor, r1KiloOhm, "kΩ");
        builder.Element("R2", ElementType.Resistor, r2KiloOhm, "kΩ");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("R2", "1", 2).Pin("R2", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist VcvsAmplifier(double mu = 2.0, bool ac = false)
    {
        var builder = new NetBuilder();
        builder.Element("V1", ac ? ElementType.AcVoltageSource : ElementType.DcVoltageSource, 1.0, "V");
        builder.Element("E1", ElementType.Vcvs, mu, "倍");
        builder.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("E1", "IN1", 1).Pin("E1", "IN2", 0)
               .Pin("E1", "OUT1", 2).Pin("E1", "OUT2", 0)
               .Pin("RL", "1", 2).Pin("RL", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist ControlledSourceNetlist(ElementType type, double gain, string unit)
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        builder.Element("X1", type, gain, unit);
        builder.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("X1", "IN1", 1).Pin("X1", "IN2", 0)
               .Pin("X1", "OUT1", 2).Pin("X1", "OUT2", 0)
               .Pin("RL", "1", 2).Pin("RL", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist MixedControlledNetwork()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        builder.Element("E1", ElementType.Vcvs, 2.0, "倍");
        builder.Element("G1", ElementType.Vccs, 1.0, "mS");
        builder.Element("R3", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("E1", "IN1", 1).Pin("E1", "IN2", 0)
               .Pin("E1", "OUT1", 2).Pin("E1", "OUT2", 0)
               .Pin("G1", "IN1", 2).Pin("G1", "IN2", 0)
               .Pin("G1", "OUT1", 0).Pin("G1", "OUT2", 3)
               .Pin("R3", "1", 3).Pin("R3", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist RcLowPass()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.AcVoltageSource, 1.0, "V");
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 1.0, "μF");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("C1", "1", 2).Pin("C1", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist RcHighPass()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.AcVoltageSource, 1.0, "V");
        builder.Element("C1", ElementType.Capacitor, 1.0, "μF");
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("C1", "1", 1).Pin("C1", "2", 2)
               .Pin("R1", "1", 2).Pin("R1", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist RlLowPasslike()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.AcVoltageSource, 1.0, "V");
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("L1", ElementType.Inductor, 100.0, "mH");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("L1", "1", 2).Pin("L1", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist SeriesRlc()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.AcVoltageSource, 1.0, "V");
        builder.Element("R1", ElementType.Resistor, 50.0, "Ω");
        builder.Element("L1", ElementType.Inductor, 10.0, "mH");
        builder.Element("C1", ElementType.Capacitor, 1.0, "μF");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("L1", "1", 2).Pin("L1", "2", 3)
               .Pin("C1", "1", 3).Pin("C1", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist WheatstoneBridge()
    {
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 10.0, "V");
        builder.Element("R1", ElementType.Resistor, 100.0, "Ω");
        builder.Element("R2", ElementType.Resistor, 100.0, "Ω");
        builder.Element("R3", ElementType.Resistor, 200.0, "Ω");
        builder.Element("R4", ElementType.Resistor, 200.0, "Ω");
        builder.Element("R5", ElementType.Resistor, 50.0, "Ω");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("R2", "1", 1).Pin("R2", "2", 3)
               .Pin("R3", "1", 2).Pin("R3", "2", 0)
               .Pin("R4", "1", 3).Pin("R4", "2", 0)
               .Pin("R5", "1", 2).Pin("R5", "2", 3);
        return builder.Build();
    }

    private static ElectricalNetlist ResistorLadder(int resistorCount)
    {
        // V1 from N1 to GND, then a chain R1..RN through N1..N(resistorCount) to GND.
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0);
        for (int i = 1; i <= resistorCount; i++)
        {
            builder.Element("R" + i, ElementType.Resistor, 1.0, "kΩ");
            builder.Pin("R" + i, "1", i);
            builder.Pin("R" + i, "2", i == resistorCount ? 0 : i + 1);
        }
        return builder.Build();
    }

    /// <summary>
    /// Audit regression fixture: RC low-pass whose true -3 dB cutoff (≈0.1 Hz) lies far
    /// BELOW the standard 10 Hz sweep start. Inside the window the response is already
    /// ~-40 dB, so a window-relative -3 dB rule would fabricate a cutoff near 14 Hz.
    /// </summary>
    private static ElectricalNetlist CutoffOutsideWindowLowPass(bool includeSecondCapacitor)
    {
        // fc = 1/(2π·1kΩ·1.5915mF) ≈ 0.1000 Hz.
        var builder = new NetBuilder();
        builder.Element("V1", ElementType.AcVoltageSource, 1.0, "V", frequencyHz: 0.1);
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 1.5915, "mF");
        builder.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 2)
               .Pin("C1", "1", 2).Pin("C1", "2", 0);
        if (includeSecondCapacitor)
        {
            // A second (negligible) parallel capacitor forces the MNA classification so
            // the same scenario can be exercised through MnaLinearStrategy.
            builder.Element("C2", ElementType.Capacitor, 1.0, "μF");
            builder.Pin("C2", "1", 2).Pin("C2", "2", 0);
        }
        return builder.Build();
    }

    /// <summary>
    /// Audit regression fixture: two independent single-source subnetworks sharing only
    /// ground. "I1" sorts before "V1" (and "A_V1"), so excitation-port selection must not
    /// depend on element-id ordering.
    /// </summary>
    private static ElectricalNetlist DualSubnetworkSources(string voltageSourceId)
    {
        var builder = new NetBuilder();
        builder.Element("I1", ElementType.DcCurrentSource, 1.0, "mA");
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element(voltageSourceId, ElementType.DcVoltageSource, 5.0, "V");
        builder.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("I1", "in", 1).Pin("I1", "out", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 0)
               .Pin(voltageSourceId, "pos", 2).Pin(voltageSourceId, "neg", 0)
               .Pin("R2", "1", 2).Pin("R2", "2", 0);
        return builder.Build();
    }

    private static ElectricalNetlist DualSubnetworkAcSources()
    {
        var builder = new NetBuilder();
        builder.Element("I1", ElementType.AcCurrentSource, 1.0, "mA");
        builder.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("V1", ElementType.AcVoltageSource, 2.0, "V");
        builder.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        builder.Pin("I1", "in", 1).Pin("I1", "out", 0)
               .Pin("R1", "1", 1).Pin("R1", "2", 0)
               .Pin("V1", "pos", 2).Pin("V1", "neg", 0)
               .Pin("R2", "1", 2).Pin("R2", "2", 0);
        return builder.Build();
    }

    // =====================================================================
    // Section 1: DC
    // =====================================================================

    private static void CheckDcMode()
    {
        // 1.1 12 V divider, 1k/1k -> 6 V, matrix 3x3 (2 nodes + 1 voltage-source branch).
        var divider = Divider();
        bool created = LinearNetworkAnalyzer.TryCreate(divider, out var model, out string error);
        Assert(created && model != null, "DC 分压网络 MNA 建模成功", error);
        Assert(model != null && model.MatrixDimension == 3, "DC 分压网络矩阵阶数 = 3（2 节点 + 1 支路电流）",
            $"dim={model?.MatrixDimension}");
        bool solved = model!.TrySolveDc(out var dividerSolution, out error);
        Assert(solved && dividerSolution != null, "DC 分压网络求解成功", error);
        AssertNear(dividerSolution!.NodeVoltage(1), 12.0, 1e-9, "DC 分压源节点 N1 = 12 V", " V");
        AssertNear(dividerSolution.NodeVoltage(2), 6.0, 1e-9, "DC 分压中点 N2 = 6 V", " V");
        AssertNear(dividerSolution.BranchCurrentAmpere("V1"), -0.006, 1e-12,
            "DC 电压源支路电流（pos→neg）= -6 mA", " A");

        // 1.2 Independent current source: 2 mA into 3 kΩ -> 6 V (legacy convention).
        var currentSource = new NetBuilder();
        currentSource.Element("I1", ElementType.DcCurrentSource, 2.0, "mA");
        currentSource.Element("R1", ElementType.Resistor, 3.0, "kΩ");
        currentSource.Pin("I1", "in", 1).Pin("I1", "out", 0)
                     .Pin("R1", "1", 1).Pin("R1", "2", 0);
        var currentNetlist = currentSource.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(currentNetlist, out var currentModel, out error) && currentModel != null,
            "独立电流源网络建模成功", error);
        Assert(currentModel!.TrySolveDc(out var currentSolution, out error), "独立电流源网络求解成功", error);
        AssertNear(currentSolution!.NodeVoltage(1), 6.0, 1e-9,
            "独立电流源 2 mA × 3 kΩ → N1 = 6 V（out→in 方向约定）", " V");

        // 1.3 Two independent sources + KCL: the 10 V source feeds N1 through 1 kΩ,
        //     and a 4 mA source injects into N1 which also has 1 kΩ to ground:
        //     (V-10)/1k + V/1k = 4 mA -> V = 7 V.
        var twoSources = new NetBuilder();
        twoSources.Element("V1", ElementType.DcVoltageSource, 10.0, "V");
        twoSources.Element("R0", ElementType.Resistor, 1.0, "kΩ");
        twoSources.Element("I1", ElementType.DcCurrentSource, 4.0, "mA");
        twoSources.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        twoSources.Pin("V1", "pos", 2).Pin("V1", "neg", 0)
                  .Pin("R0", "1", 2).Pin("R0", "2", 1)
                  .Pin("I1", "in", 1).Pin("I1", "out", 0)
                  .Pin("R1", "1", 1).Pin("R1", "2", 0);
        var twoSourceNetlist = twoSources.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(twoSourceNetlist, out var twoSourceModel, out error) && twoSourceModel != null,
            "双独立源网络建模成功", error);
        Assert(twoSourceModel!.TrySolveDc(out var twoSourceSolution, out error), "双独立源网络 KCL 求解成功", error);
        AssertNear(twoSourceSolution!.NodeVoltage(2), 10.0, 1e-9, "双独立源电压源节点 N2 = 10 V", " V");
        AssertNear(twoSourceSolution.NodeVoltage(1), 7.0, 1e-9, "双独立源节点电位 N1 = 7 V（KCL 校验）", " V");

        // 1.4 VCVS ×2: V(N2) = 2 V, output branch current -2 mA.
        Assert(LinearNetworkAnalyzer.TryCreate(VcvsAmplifier(2.0), out var vcvsModel, out error) && vcvsModel != null,
            "VCVS 网络建模成功", error);
        Assert(vcvsModel!.TrySolveDc(out var vcvsSolution, out error), "VCVS 网络求解成功", error);
        AssertNear(vcvsSolution!.NodeVoltage(1), 1.0, 1e-9, "VCVS 控制端电位 N1 = 1 V", " V");
        AssertNear(vcvsSolution.NodeVoltage(2), 2.0, 1e-9, "VCVS 增益 μ=2 → 输出 N2 = 2 V", " V");
        AssertNear(vcvsSolution.BranchCurrentAmpere("E1"), -0.002, 1e-12,
            "VCVS 输出支路电流 = -2 mA（KCL 校验）", " A");

        // 1.5 VCCS gm = 2 mS: I(OUT1→OUT2) = 2 mA into 1 kΩ -> 2 V.
        var vccs = new NetBuilder();
        vccs.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        vccs.Element("G1", ElementType.Vccs, 2.0, "mS");
        vccs.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        vccs.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
            .Pin("G1", "IN1", 1).Pin("G1", "IN2", 0)
            .Pin("G1", "OUT1", 0).Pin("G1", "OUT2", 2)
            .Pin("RL", "1", 2).Pin("RL", "2", 0);
        var vccsNetlist = vccs.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(vccsNetlist, out var vccsModel, out error) && vccsModel != null,
            "VCCS 网络建模成功", error);
        Assert(vccsModel!.TrySolveDc(out var vccsSolution, out error), "VCCS 网络求解成功", error);
        AssertNear(vccsSolution!.NodeVoltage(2), 2.0, 1e-9,
            "VCCS gm=2mS × Vc=1V × 1kΩ → N2 = 2 V", " V");

        // 1.6 CCVS r = 2 kΩ with 1 mA control current -> V(N2) = 2 V.
        var ccvs = new NetBuilder();
        ccvs.Element("I1", ElementType.DcCurrentSource, 1.0, "mA");
        ccvs.Element("H1", ElementType.Ccvs, 2.0, "kΩ");
        ccvs.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        ccvs.Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("H1", "IN1", 1).Pin("H1", "IN2", 0)
            .Pin("H1", "OUT1", 2).Pin("H1", "OUT2", 0)
            .Pin("RL", "1", 2).Pin("RL", "2", 0);
        var ccvsNetlist = ccvs.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(ccvsNetlist, out var ccvsModel, out error) && ccvsModel != null,
            "CCVS 网络建模成功", error);
        Assert(ccvsModel!.TrySolveDc(out var ccvsSolution, out error), "CCVS 网络求解成功", error);
        AssertNear(ccvsSolution!.NodeVoltage(2), 2.0, 1e-9,
            "CCVS r=2kΩ × Ictrl=1mA → N2 = 2 V", " V");

        // 1.7 CCCS β = 5 with 1 mA control current -> 5 mA into 1 kΩ -> 5 V.
        var cccs = new NetBuilder();
        cccs.Element("I1", ElementType.DcCurrentSource, 1.0, "mA");
        cccs.Element("F1", ElementType.Cccs, 5.0, "倍");
        cccs.Element("RL", ElementType.Resistor, 1.0, "kΩ");
        cccs.Pin("I1", "in", 1).Pin("I1", "out", 0)
            .Pin("F1", "IN1", 1).Pin("F1", "IN2", 0)
            .Pin("F1", "OUT1", 0).Pin("F1", "OUT2", 2)
            .Pin("RL", "1", 2).Pin("RL", "2", 0);
        var cccsNetlist = cccs.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(cccsNetlist, out var cccsModel, out error) && cccsModel != null,
            "CCCS 网络建模成功", error);
        Assert(cccsModel!.TrySolveDc(out var cccsSolution, out error), "CCCS 网络求解成功", error);
        AssertNear(cccsSolution!.NodeVoltage(2), 5.0, 1e-9,
            "CCCS β=5 × Ictrl=1mA × 1kΩ → N2 = 5 V", " V");

        // 1.8 Capacitor is an open circuit in DC.
        var capDc = new NetBuilder();
        capDc.Element("V1", ElementType.DcVoltageSource, 5.0, "V");
        capDc.Element("C1", ElementType.Capacitor, 1.0, "μF");
        capDc.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        capDc.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
             .Pin("C1", "1", 1).Pin("C1", "2", 2)
             .Pin("R1", "1", 2).Pin("R1", "2", 0);
        var capDcNetlist = capDc.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(capDcNetlist, out var capDcModel, out error) && capDcModel != null,
            "含电容网络建模成功", error);
        Assert(capDcModel!.TrySolveDc(out var capDcSolution, out error), "含电容网络 DC 求解成功", error);
        AssertNear(capDcSolution!.NodeVoltage(2), 0.0, 1e-12,
            "DC 模式下电容开路 → 电容右侧节点由电阻拉到 0 V", " V");

        // 1.9 Inductor is a short circuit in DC, with a real branch current.
        var indDc = new NetBuilder();
        indDc.Element("V1", ElementType.DcVoltageSource, 10.0, "V");
        indDc.Element("L1", ElementType.Inductor, 1.0, "mH");
        indDc.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        indDc.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
             .Pin("L1", "1", 1).Pin("L1", "2", 2)
             .Pin("R1", "1", 2).Pin("R1", "2", 0);
        var indDcNetlist = indDc.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(indDcNetlist, out var indDcModel, out error) && indDcModel != null,
            "含电感网络建模成功", error);
        Assert(indDcModel!.TrySolveDc(out var indDcSolution, out error), "含电感网络 DC 求解成功", error);
        AssertNear(indDcSolution!.NodeVoltage(2), 10.0, 1e-9,
            "DC 模式下电感短路 → N2 = 10 V", " V");
        AssertNear(indDcSolution.BranchCurrentAmpere("L1"), 0.01, 1e-12,
            "DC 电感支路电流 = 10 mA", " A");

        // 1.10 Mixed controlled-source network (VCVS then VCCS).
        Assert(LinearNetworkAnalyzer.TryCreate(MixedControlledNetwork(), out var mixedModel, out error) && mixedModel != null,
            "混合受控源网络建模成功", error);
        Assert(mixedModel!.MatrixDimension == 5, "混合受控源网络矩阵阶数 = 5（3 节点 + 2 支路电流）",
            $"dim={mixedModel.MatrixDimension}");
        Assert(mixedModel.TrySolveDc(out var mixedSolution, out error), "混合受控源网络求解成功", error);
        AssertNear(mixedSolution!.NodeVoltage(2), 2.0, 1e-9, "混合网络 VCVS 中间节点 N2 = 2 V", " V");
        AssertNear(mixedSolution.NodeVoltage(3), 2.0, 1e-9, "混合网络 VCCS 输出节点 N3 = 2 V", " V");

        // 1.11 Controlled-source gain unit table: gain = DisplayNumber × unit multiplier.
        var gainUnits = new (string Unit, double Multiplier)[]
        {
            ("倍", 1.0), ("", 1.0),
            ("S", 1.0), ("mS", 1e-3), ("μS", 1e-6), ("uS", 1e-6),
            ("Ω", 1.0), ("kΩ", 1e3), ("MΩ", 1e6),
            ("mV", 1e-3), ("kV", 1e3), ("mA", 1e-3), ("μA", 1e-6)
        };
        int unitFailures = 0;
        string unitFailureDetail = string.Empty;
        foreach (var (unit, multiplier) in gainUnits)
        {
            bool ok = LinearNetworkAnalyzer.TryCreate(ControlledSourceNetlist(ElementType.Vcvs, 3.0, unit),
                out var unitModel, out _);
            var device = unitModel?.Devices.FirstOrDefault(d => d.ElementId == "X1");
            double expectedGain = 3.0 * multiplier;
            if (!ok || device == null || Math.Abs(device.ValueSi - expectedGain) > 1e-15)
            {
                unitFailures++;
                unitFailureDetail += $"[{unit}:{device?.ValueSi.ToString("G8", CultureInfo.InvariantCulture) ?? "null"}vs{expectedGain.ToString("G8", CultureInfo.InvariantCulture)}]";
            }
        }
        Assert(unitFailures == 0,
            "受控源增益单位换算正确（倍/空/S/mS/μS/uS/Ω/kΩ/MΩ/mV/kV/mA/μA）", unitFailureDetail);

        bool unknownUnitCreated = LinearNetworkAnalyzer.TryCreate(
            ControlledSourceNetlist(ElementType.Vcvs, 2.5, "dB"), out var unknownUnitModel, out _);
        var unknownUnitDevice = unknownUnitModel?.Devices.FirstOrDefault(d => d.ElementId == "X1");
        Assert(unknownUnitCreated && unknownUnitDevice != null && Math.Abs(unknownUnitDevice.ValueSi - 2.5) < 1e-12 &&
               unknownUnitModel!.BoundaryNote.Contains("未识别", StringComparison.Ordinal),
            "未知受控源增益单位按 ×1 处理并写入 boundary note", unknownUnitModel?.BoundaryNote ?? "null");
    }

    // =====================================================================
    // Section 2: AC phasors
    // =====================================================================

    private static void CheckAcPhasors()
    {
        // 2.1 RC low-pass at fc: |H| = 1/sqrt(2), phase = -45°, input node = 1 V.
        var lowPass = RcLowPass();
        Assert(LinearNetworkAnalyzer.TryCreate(lowPass, out var lowPassModel, out string error) && lowPassModel != null,
            "RC 低通网络建模成功", error);
        double lowPassFc = 1.0 / (2.0 * Math.PI * 1000.0 * 1e-6);
        Assert(lowPassModel!.TrySolveAc(lowPassFc, out var lowPassAtFc, out error), "RC 低通在截止频率处 AC 求解成功", error);
        AssertRel(lowPassAtFc!.NodeMagnitude(2), 1.0 / Math.Sqrt(2.0), 1e-9,
            "RC 低通在 fc 处 |H| = 0.7071", " V");
        AssertPhase(lowPassAtFc.NodePhaseDeg(2), -45.0, 0.05, "RC 低通在 fc 处相角 = -45°");
        AssertNear(lowPassAtFc.NodeMagnitude(1), 1.0, 1e-12, "AC 激励源节点 |V1| = 1 V", " V");

        // 2.2 RC high-pass at fc: |H| = 0.7071, phase = +45°.
        Assert(LinearNetworkAnalyzer.TryCreate(RcHighPass(), out var highPassModel, out error) && highPassModel != null,
            "RC 高通网络建模成功", error);
        Assert(highPassModel!.TrySolveAc(lowPassFc, out var highPassAtFc, out error), "RC 高通在截止频率处 AC 求解成功", error);
        AssertRel(highPassAtFc!.NodeMagnitude(2), 1.0 / Math.Sqrt(2.0), 1e-9,
            "RC 高通在 fc 处 |H| = 0.7071", " V");
        AssertPhase(highPassAtFc.NodePhaseDeg(2), 45.0, 0.05, "RC 高通在 fc 处相角 = +45°");

        // 2.3 RL divider at fc = R/(2πL): |H| = 0.7071, phase = +45°.
        Assert(LinearNetworkAnalyzer.TryCreate(RlLowPasslike(), out var rlModel, out error) && rlModel != null,
            "RL 网络建模成功", error);
        double rlFc = 1000.0 / (2.0 * Math.PI * 0.1);
        Assert(rlModel!.TrySolveAc(rlFc, out var rlAtFc, out error), "RL 网络在截止频率处 AC 求解成功", error);
        AssertRel(rlAtFc!.NodeMagnitude(2), 1.0 / Math.Sqrt(2.0), 1e-9, "RL 网络在 fc 处 |H| = 0.7071", " V");
        AssertPhase(rlAtFc.NodePhaseDeg(2), 45.0, 0.05, "RL 网络在 fc 处相角 = +45°");

        // 2.4 Series RLC resonance: Q = (1/R)·sqrt(L/C) = 2 → |H_C| = 2 at f0, phase -90°.
        Assert(LinearNetworkAnalyzer.TryCreate(SeriesRlc(), out var rlcModel, out error) && rlcModel != null,
            "串联 RLC 网络建模成功", error);
        double f0 = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
        Assert(rlcModel!.TrySolveAc(f0, out var rlcAtF0, out error), "串联 RLC 在谐振频率处 AC 求解成功", error);
        AssertRel(rlcAtF0!.NodeMagnitude(3), 2.0, 1e-9, "串联 RLC 谐振点 |H_C| = Q = 2", " V");
        AssertPhase(rlcAtF0.NodePhaseDeg(3), -90.0, 0.05, "串联 RLC 谐振点输出相角 = -90°");
        // Well below resonance the capacitor is open and the output follows the input.
        Assert(rlcModel.TrySolveAc(f0 / 1000.0, out var rlcLow, out error), "串联 RLC 低频 AC 求解成功", error);
        AssertNear(rlcLow!.NodeMagnitude(3), 1.0, 1e-5, "串联 RLC 远低于谐振时 |H| → 1", " V");

        // 2.5 Independent AC current source: 1 mA × 1 kΩ -> |V| = 1 V.
        var acCurrent = new NetBuilder();
        acCurrent.Element("I1", ElementType.AcCurrentSource, 1.0, "mA");
        acCurrent.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        acCurrent.Pin("I1", "in", 1).Pin("I1", "out", 0)
                 .Pin("R1", "1", 1).Pin("R1", "2", 0);
        var acCurrentNetlist = acCurrent.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(acCurrentNetlist, out var acCurrentModel, out error) && acCurrentModel != null,
            "交流电流源网络建模成功", error);
        Assert(acCurrentModel!.TrySolveAc(1000.0, out var acCurrentSolution, out error), "交流电流源网络求解成功", error);
        AssertRel(acCurrentSolution!.NodeMagnitude(1), 1.0, 1e-9, "交流电流源 1 mA × 1 kΩ → |V| = 1 V", " V");

        // 2.6 DC source is zeroed in AC mode (shorted): a DC 5 V source must not bias the phasor.
        var dcBias = new NetBuilder();
        dcBias.Element("V1", ElementType.DcVoltageSource, 5.0, "V");
        dcBias.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        dcBias.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        dcBias.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
              .Pin("R1", "1", 1).Pin("R1", "2", 2)
              .Pin("R2", "1", 2).Pin("R2", "2", 0);
        var dcBiasNetlist = dcBias.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(dcBiasNetlist, out var dcBiasModel, out error) && dcBiasModel != null,
            "含 DC 源网络建模成功", error);
        Assert(dcBiasModel!.TrySolveAc(1000.0, out var dcBiasAc, out error), "含 DC 源网络 AC 求解成功", error);
        AssertNear(dcBiasAc!.NodeMagnitude(1), 0.0, 1e-12, "AC 模式下 DC 电压源置零（短路）→ N1 相量为 0", " V");
    }

    // =====================================================================
    // Section 3: AC sweeps
    // =====================================================================

    private static void CheckAcSweep()
    {
        double lowPassFc = 1.0 / (2.0 * Math.PI * 1000.0 * 1e-6);

        // 3.1 RC low-pass sweep: 10 Hz–10 MHz, 10 points/decade -> 61 points.
        var lowPass = RcLowPass();
        Assert(LinearNetworkAnalyzer.TryCreate(lowPass, out var model, out string error) && model != null,
            "RC 低通扫频网络建模成功", error);
        bool swept = model!.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out var sweep, out error);
        Assert(swept && sweep != null, "RC 低通频率扫描成功", error);
        Assert(sweep!.Points.Count == 61, "RC 低通扫频点数 = 61（6 个十倍频程 × 10 点 + 1）",
            $"count={sweep.Points.Count}");
        bool monotonic = true;
        for (int i = 1; i < sweep.Points.Count; i++)
        {
            if (sweep.Points[i].GainDb > sweep.Points[i - 1].GainDb + 1e-9) { monotonic = false; break; }
        }
        Assert(monotonic, "RC 低通扫频 dB 单调不增");
        AssertRel(sweep.UpperCutoffHz ?? double.NaN, lowPassFc, 0.01,
            "RC 低通 -3 dB 上限截止频率 ≈ 1/(2πRC)", " Hz");
        Assert(!sweep.LowerCutoffHz.HasValue, "RC 低通低频端无 -3 dB 截止（通带延伸至扫描下限）",
            $"lower={sweep.LowerCutoffHz}");
        Assert(sweep.BandwidthHz.HasValue && Math.Abs(sweep.BandwidthHz.Value - (sweep.UpperCutoffHz ?? 0)) < 1e-6,
            "RC 低通带宽以 DC 为低频起点 ≈ 上限截止频率", $"bw={sweep.BandwidthHz}");

        // 3.2 RC high-pass sweep: lower cutoff only, upper null.
        Assert(LinearNetworkAnalyzer.TryCreate(RcHighPass(), out var highPassModel, out error) && highPassModel != null,
            "RC 高通扫频网络建模成功", error);
        Assert(highPassModel!.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out var highPassSweep, out error),
            "RC 高通频率扫描成功", error);
        AssertRel(highPassSweep!.LowerCutoffHz ?? double.NaN, lowPassFc, 0.01,
            "RC 高通 -3 dB 下限截止频率 ≈ 1/(2πRC)", " Hz");
        Assert(!highPassSweep.UpperCutoffHz.HasValue, "RC 高通高频端无 -3 dB 截止",
            $"upper={highPassSweep.UpperCutoffHz}");

        // 3.3 Flat resistive divider has no cutoff at all and is explicitly noted.
        var divider = Divider();
        Assert(LinearNetworkAnalyzer.TryCreate(divider, out var dividerModel, out error) && dividerModel != null,
            "平坦分压扫频网络建模成功", error);
        Assert(dividerModel!.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out var flatSweep, out error),
            "平坦分压网络频率扫描成功", error);
        bool flat = flatSweep!.Points.All(p => Math.Abs(p.GainMagnitude - 0.5) < 1e-9);
        Assert(flat, "平坦电阻网络全频段增益恒为 0.5");
        Assert(!flatSweep.LowerCutoffHz.HasValue && !flatSweep.UpperCutoffHz.HasValue,
            "平坦网络无 -3 dB 截止频率（上下限均为 null）",
            $"lower={flatSweep.LowerCutoffHz}, upper={flatSweep.UpperCutoffHz}");
        Assert(flatSweep.Notes.Any(n => n.Contains("截止", StringComparison.Ordinal)),
            "平坦网络在 BoundaryNote 中说明未检测到截止频率", string.Join("；", flatSweep.Notes));

        // 3.4 Generic 1 V test-source injection when the input port has no source.
        var passiveDivider = new NetBuilder();
        passiveDivider.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        passiveDivider.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        passiveDivider.Pin("R1", "1", 1).Pin("R1", "2", 2)
                      .Pin("R2", "1", 2).Pin("R2", "2", 0);
        var passiveNetlist = passiveDivider.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(passiveNetlist, out var passiveModel, out error) && passiveModel != null,
            "无源网络建模成功（扫描注入路径）", error);
        Assert(passiveModel!.TrySweep(1, 0, 2, 0, 10.0, 1e6, 10, out var injectedSweep, out error),
            "无源网络注入 1 V 测试源扫频成功", error);
        AssertNear(injectedSweep!.Points[0].GainMagnitude, 0.5, 1e-9,
            "注入 1 V 测试源时分压比 = 0.5", " V/V");

        // 3.5 Series RLC sweep detects a resonant band with both -3 dB edges.
        Assert(LinearNetworkAnalyzer.TryCreate(SeriesRlc(), out var rlcModel, out error) && rlcModel != null,
            "串联 RLC 扫频网络建模成功", error);
        Assert(rlcModel!.TrySweep(1, 0, 3, 0, 10.0, 1e7, 20, out var rlcSweep, out error),
            "串联 RLC 频率扫描成功", error);
        Assert(rlcSweep!.LowerCutoffHz.HasValue && rlcSweep.UpperCutoffHz.HasValue,
            "串联 RLC 检出上下 -3 dB 截止频率",
            $"lower={rlcSweep.LowerCutoffHz}, upper={rlcSweep.UpperCutoffHz}");
        double rlcF0 = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
        Assert(rlcSweep.LowerCutoffHz!.Value < rlcF0 && rlcSweep.UpperCutoffHz!.Value > rlcF0,
            "串联 RLC 谐振频率位于上下截止频率之间", $"f0={rlcF0:G6}");
        Assert(rlcSweep.BandwidthHz.HasValue && rlcSweep.BandwidthHz.Value > 0,
            "串联 RLC 带宽为正", $"bw={rlcSweep.BandwidthHz}");
    }

    // =====================================================================
    // Section 4: equivalents
    // =====================================================================

    private static void CheckEquivalents()
    {
        // 4.1 Divider Thevenin at the output node: Voc = 6 V, Req = 500 Ω.
        Assert(LinearNetworkAnalyzer.TryCreate(Divider(), out var dividerModel, out string error) && dividerModel != null,
            "分压网络戴维南建模成功", error);
        bool thevenin = dividerModel!.TryGetThevenin(2, 0, out double voc, out double req, out error);
        Assert(thevenin, "分压网络输出端口戴维南求解成功", error);
        AssertNear(voc, 6.0, 1e-9, "分压网络输出端口开路电压 Voc = 6 V", " V");
        AssertNear(req, 500.0, 1e-6, "分压网络输出端口等效电阻 Req = 500 Ω", " Ω");
        // String/name based overload.
        Assert(dividerModel.TryGetThevenin("N2", "GND", out double vocByName, out double reqByName, out error),
            "按节点名调用戴维南 API 成功", error);
        AssertNear(vocByName, 6.0, 1e-9, "按节点名 Voc = 6 V", " V");
        AssertNear(reqByName, 500.0, 1e-6, "按节点名 Req = 500 Ω", " Ω");

        // 4.2 Source port Thevenin: ideal voltage source zeroed -> Req = 0.
        Assert(dividerModel.TryGetEquivalentResistance(1, 0, out double sourcePortReq, out error),
            "激励源端口等效电阻求解成功", error);
        AssertNear(sourcePortReq, 0.0, 1e-9, "理想电压源端口置零后 Req = 0 Ω", " Ω");

        // 4.3 Input resistance seen by the excitation source: 1k + 1k = 2 kΩ.
        var excitation = dividerModel.Devices.First(d => d.ElementId == "V1");
        Assert(dividerModel.TryGetDrivingPointImpedance(excitation, isAc: false, frequencyHz: 0.0,
                out var inputImpedance, out error),
            "驱动点输入阻抗求解成功", error);
        AssertNear(inputImpedance.Real, 2000.0, 1e-6, "独立源置零后输入电阻 = 2 kΩ", " Ω");
        AssertNear(inputImpedance.Imaginary, 0.0, 1e-12, "直流驱动点输入阻抗无虚部", " Ω");

        // 4.4 Wheatstone bridge detector-port Req: 50 || 200 || 400 = 36.3636 Ω.
        Assert(LinearNetworkAnalyzer.TryCreate(WheatstoneBridge(), out var bridgeModel, out error) && bridgeModel != null,
            "桥式网络建模成功", error);
        Assert(bridgeModel!.TrySolveDc(out var bridgeSolution, out error), "桥式网络 DC 求解成功", error);
        AssertNear(bridgeSolution!.NodeVoltage(2), 10.0 * 200.0 / 300.0, 1e-9, "桥式网络 V2 = 6.6667 V", " V");
        AssertNear(bridgeSolution.NodeVoltage(3), 10.0 * 200.0 / 300.0, 1e-9, "桥式网络 V3 = 6.6667 V", " V");
        Assert(bridgeModel.TryGetEquivalentResistance(2, 3, out double bridgeReq, out error),
            "桥式网络检测端等效电阻求解成功", error);
        AssertNear(bridgeReq, 1.0 / (1.0 / 50.0 + 1.0 / 200.0 + 1.0 / 400.0), 1e-9,
            "桥式网络检测端 Req = 50∥200∥400 = 36.3636 Ω", " Ω");

        // 4.5 Port enumeration through the strategy is complete for small networks.
        var mixed = MixedControlledNetwork();
        var classification = TopologyClassifier.Classify(mixed);
        Assert(classification.TopologyType == CircuitTopologyType.MnaLinear,
            "混合受控源网络被分类为 MnaLinear", $"got {classification.TopologyType}");
        var mixedSolution = classification.Strategy.SolveAsync(mixed, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(mixedSolution.IsSuccess && mixedSolution.LinearNetwork != null,
            "混合受控源网络结构化求解成功", mixedSolution.SummaryText);
        // 3 non-ground nodes + GND = C(4,2) = 6 port pairs, all fit below the 14-entry cap.
        Assert(mixedSolution.LinearNetwork!.PortEquivalents.Count == 6,
            "3 节点 + GND 的任意节点对等效表共 6 条（全表未截断）",
            $"count={mixedSolution.LinearNetwork.PortEquivalents.Count}");

        // 4.6 Large network truncates the port table and explains it in the boundary note.
        var ladder = ResistorLadder(5);
        var ladderClassification = TopologyClassifier.Classify(ladder);
        var ladderSolution = ladderClassification.Strategy.SolveAsync(ladder, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(ladderSolution.IsSuccess && ladderSolution.LinearNetwork != null,
            "5 节点梯形网络结构化求解成功", ladderSolution.SummaryText);
        // 5 non-ground nodes + GND = 15 pairs > 14-entry cap.
        Assert(ladderSolution.LinearNetwork!.PortEquivalents.Count == 14,
            "节点对超过上限时端口表截断为 14 条",
            $"count={ladderSolution.LinearNetwork.PortEquivalents.Count}");
        Assert(ladderSolution.LinearNetwork.BoundaryNote.Contains("省略", StringComparison.Ordinal),
            "端口表截断在 BoundaryNote 中说明", ladderSolution.LinearNetwork.BoundaryNote);
    }

    // =====================================================================
    // Section 5: error paths
    // =====================================================================

    private static void CheckErrorPaths()
    {
        // 5.1 Diode is rejected with its element ID.
        var diode = new NetBuilder();
        diode.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        diode.Element("D1", ElementType.Diode, 1.0, "");
        diode.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        diode.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
             .Pin("D1", "A", 1).Pin("D1", "K", 0)
             .Pin("R1", "1", 1).Pin("R1", "2", 0);
        bool diodeCreated = LinearNetworkAnalyzer.TryCreate(diode.Build(), out _, out string diodeError);
        Assert(!diodeCreated && diodeError.Contains("D1", StringComparison.Ordinal) &&
               diodeError.Contains("二极管", StringComparison.Ordinal),
            "二极管被明确拒绝并给出器件 ID", diodeError);

        // 5.2 Transformer is rejected with its element ID.
        var transformer = new NetBuilder();
        transformer.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        transformer.Element("T1", ElementType.Transformer, 2.0, "倍");
        transformer.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        transformer.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        transformer.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
                   .Pin("T1", "P1", 1).Pin("T1", "P2", 0)
                   .Pin("T1", "S1", 2).Pin("T1", "S2", 0)
                   .Pin("R1", "1", 1).Pin("R1", "2", 0)
                   .Pin("R2", "1", 2).Pin("R2", "2", 0);
        bool transformerCreated = LinearNetworkAnalyzer.TryCreate(transformer.Build(), out _, out string transformerError);
        Assert(!transformerCreated && transformerError.Contains("T1", StringComparison.Ordinal) &&
               transformerError.Contains("变压器", StringComparison.Ordinal),
            "变压器被明确拒绝并给出器件 ID", transformerError);

        // 5.3 MOSFET is rejected.
        var mosfet = new NetBuilder();
        mosfet.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        mosfet.Element("Q1", ElementType.MosfetNEnh, 1.0, "");
        mosfet.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        mosfet.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        mosfet.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
              .Pin("Q1", "G", 1).Pin("Q1", "D", 2).Pin("Q1", "S", 0)
              .Pin("R1", "1", 1).Pin("R1", "2", 0)
              .Pin("R2", "1", 2).Pin("R2", "2", 0);
        Assert(!LinearNetworkAnalyzer.TryCreate(mosfet.Build(), out _, out string mosfetError) &&
               mosfetError.Contains("Q1", StringComparison.Ordinal),
            "MOSFET 被明确拒绝并给出器件 ID", mosfetError);

        // 5.4 VCC port is rejected (it is a symbol, not a linear element).
        var vcc = new NetBuilder();
        vcc.Element("P1", ElementType.VccPort, 12.0, "V");
        vcc.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        vcc.Pin("P1", "VCC", 1).Pin("R1", "1", 1).Pin("R1", "2", 0);
        Assert(!LinearNetworkAnalyzer.TryCreate(vcc.Build(), out _, out string vccError) &&
               vccError.Contains("P1", StringComparison.Ordinal),
            "VCC 端口被明确拒绝", vccError);

        // 5.5 Missing ground reference.
        var noGround = new NetBuilder();
        noGround.Element("V1", ElementType.DcVoltageSource, 10.0, "V");
        noGround.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        noGround.Pin("V1", "pos", 1).Pin("V1", "neg", 2)
                .Pin("R1", "1", 2).Pin("R1", "2", 1);
        Assert(!LinearNetworkAnalyzer.TryCreate(noGround.Build(), out _, out string noGroundError) &&
               noGroundError.Contains("参考地", StringComparison.Ordinal),
            "缺少参考地 GND 时明确失败", noGroundError);

        // 5.6 Non-finite element value.
        var nonFinite = new NetBuilder();
        nonFinite.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        nonFinite.Element("R1", ElementType.Resistor, double.NaN, "kΩ");
        nonFinite.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        nonFinite.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
                 .Pin("R1", "1", 1).Pin("R1", "2", 2)
                 .Pin("R2", "1", 2).Pin("R2", "2", 0);
        Assert(!LinearNetworkAnalyzer.TryCreate(nonFinite.Build(), out _, out string nonFiniteError) &&
               nonFiniteError.Contains("有限", StringComparison.Ordinal),
            "非有限元件值被拒绝", nonFiniteError);

        // 5.7 Non-positive resistance.
        var zeroResistance = new NetBuilder();
        zeroResistance.Element("V1", ElementType.DcVoltageSource, 12.0, "V");
        zeroResistance.Element("R1", ElementType.Resistor, 0.0, "kΩ");
        zeroResistance.Element("R2", ElementType.Resistor, 1.0, "kΩ");
        zeroResistance.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
                      .Pin("R1", "1", 1).Pin("R1", "2", 2)
                      .Pin("R2", "1", 2).Pin("R2", "2", 0);
        Assert(!LinearNetworkAnalyzer.TryCreate(zeroResistance.Build(), out _, out string zeroResistanceError) &&
               zeroResistanceError.Contains("大于 0", StringComparison.Ordinal),
            "零/负电阻被拒绝", zeroResistanceError);

        // 5.8 Contradictory ideal voltage sources are singular.
        var contradictory = new NetBuilder();
        contradictory.Element("V1", ElementType.DcVoltageSource, 5.0, "V");
        contradictory.Element("V2", ElementType.DcVoltageSource, 10.0, "V");
        contradictory.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
                      .Pin("V2", "pos", 1).Pin("V2", "neg", 0);
        var contradictoryNetlist = contradictory.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(contradictoryNetlist, out var contradictoryModel, out string contradictoryError)
               && contradictoryModel != null, "矛盾电压源网络可建模（检查点是求解失败）", contradictoryError);
        bool contradictorySolved = contradictoryModel!.TrySolveDc(out _, out string singularError);
        Assert(!contradictorySolved && singularError.Contains("奇异", StringComparison.Ordinal),
            "矛盾理想电压源网络被判为奇异", singularError);
        var contradictoryStrategy = TopologyClassifier.Classify(contradictoryNetlist).Strategy
            .SolveAsync(contradictoryNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!contradictoryStrategy.IsSuccess, "矛盾电压源经 MnaLinearStrategy 返回失败",
            contradictoryStrategy.SummaryText);

        // 5.9 Floating node fed only by an ideal current source -> singular.
        var floating = new NetBuilder();
        floating.Element("I1", ElementType.DcCurrentSource, 2.0, "mA");
        floating.Pin("I1", "in", 1).Pin("I1", "out", 0);
        var floatingNetlist = floating.Build();
        Assert(LinearNetworkAnalyzer.TryCreate(floatingNetlist, out var floatingModel, out string floatingError)
               && floatingModel != null, "浮空电流源网络可建模（检查点是求解失败）", floatingError);
        Assert(!floatingModel!.TrySolveDc(out _, out string floatingSolveError) &&
               floatingSolveError.Contains("奇异", StringComparison.Ordinal),
            "悬空节点导致的奇异网络被拒绝", floatingSolveError);

        // 5.10 Pure-capacitor DC network after C opens has no DC path -> singular.
        var capOnly = new NetBuilder();
        capOnly.Element("V1", ElementType.DcVoltageSource, 5.0, "V");
        capOnly.Element("C1", ElementType.Capacitor, 1.0, "μF");
        capOnly.Element("C2", ElementType.Capacitor, 1.0, "μF");
        capOnly.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
               .Pin("C1", "1", 1).Pin("C1", "2", 2)
               .Pin("C2", "1", 2).Pin("C2", "2", 0);
        var capOnlyNetlist = capOnly.Build();
        var capOnlyStrategy = TopologyClassifier.Classify(capOnlyNetlist).Strategy
            .SolveAsync(capOnlyNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!capOnlyStrategy.IsSuccess && capOnlyStrategy.SummaryText.Contains("电容", StringComparison.Ordinal),
            "纯电容网络在 DC 模式下因开路悬空节点被拒绝", capOnlyStrategy.SummaryText);

        // 5.11 Matrix variable limit 256.
        var oversized = ResistorLadder(257);
        bool oversizedCreated = LinearNetworkAnalyzer.TryCreate(oversized, out _, out string oversizedError);
        Assert(!oversizedCreated && oversizedError.Contains("256", StringComparison.Ordinal),
            "矩阵变量超过 256 时明确失败", oversizedError);
    }

    // =====================================================================
    // Section 6: cancellation
    // =====================================================================

    private static void CheckCancellation()
    {
        var divider = Divider();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool threwOnCreate = false;
        try { LinearNetworkAnalyzer.TryCreate(divider, out _, out _, cancelled.Token); }
        catch (OperationCanceledException) { threwOnCreate = true; }
        Assert(threwOnCreate, "TryCreate 已取消 token 触发 OperationCanceledException");

        Assert(LinearNetworkAnalyzer.TryCreate(divider, out var model, out _) && model != null,
            "取消测试用分压网络建模成功");
        bool threwOnSolve = false;
        try { model!.TrySolveDc(out _, out _, cancelled.Token); }
        catch (OperationCanceledException) { threwOnSolve = true; }
        Assert(threwOnSolve, "TrySolveDc 已取消 token 触发 OperationCanceledException");

        bool threwOnSweep = false;
        try { model!.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out _, out _, cancelled.Token); }
        catch (OperationCanceledException) { threwOnSweep = true; }
        Assert(threwOnSweep, "TrySweep 已取消 token 触发 OperationCanceledException");

        bool threwOnStrategy = false;
        try
        {
            var strategy = new CircuitAssistant.Engine.Strategies.MnaLinearStrategy();
            strategy.SolveAsync(divider, new AnalysisParameters(), cancelled.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { threwOnStrategy = true; }
        Assert(threwOnStrategy, "MnaLinearStrategy 已取消 token 触发 OperationCanceledException");
    }

    // =====================================================================
    // Section 7: structured strategy contract
    // =====================================================================

    private static void CheckStrategyContract()
    {
        // 7.1 DC divider: structured LinearNetwork filled, legacy DC log preserved.
        var divider = Divider();
        var dividerClassification = TopologyClassifier.Classify(divider);
        Assert(dividerClassification.TopologyType == CircuitTopologyType.MnaLinear,
            "分压网络分类为 MnaLinear", $"got {dividerClassification.TopologyType}");
        var dividerSolution = dividerClassification.Strategy
            .SolveAsync(divider, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(dividerSolution.IsSuccess, "分压网络 MnaLinearStrategy 求解成功", dividerSolution.SummaryText);
        var dividerInfo = dividerSolution.LinearNetwork;
        Assert(dividerInfo != null, "分压网络返回非空 LinearNetwork", "LinearNetwork is null");
        Assert(dividerInfo!.MatrixDimension == 3, "分压网络 LinearNetwork.MatrixDimension = 3",
            $"dim={dividerInfo.MatrixDimension}");
        Assert(dividerInfo.OperatingMode.Contains("DC", StringComparison.OrdinalIgnoreCase) ||
               dividerInfo.OperatingMode.Contains("直流", StringComparison.Ordinal),
            "分压网络 OperatingMode 标注直流", dividerInfo.OperatingMode);
        Assert(dividerInfo.ModelSummary.Contains("MNA", StringComparison.Ordinal),
            "分压网络 ModelSummary 描述 MNA 模型", dividerInfo.ModelSummary);
        Assert(dividerInfo.NodePotentials.Count == 2, "分压网络 NodePotentials 覆盖 2 个非地节点",
            $"count={dividerInfo.NodePotentials.Count}");
        var n2Potential = dividerInfo.NodePotentials.FirstOrDefault(n => n.NetName == "N2");
        Assert(n2Potential != null && Math.Abs(n2Potential.VoltageV - 6.0) < 1e-9,
            "分压网络 NodePotentials.N2 = 6 V", n2Potential?.VoltageV.ToString("G8", CultureInfo.InvariantCulture) ?? "missing");
        Assert(n2Potential != null && n2Potential.Display.Contains('V') && n2Potential.LatexFormula.Contains("v_{"),
            "分压网络节点电位带 Display 与 LaTeX 公式", n2Potential?.LatexFormula ?? "missing");
        Assert(dividerInfo.PortEquivalents.Count == 3,
            "分压网络端口等效表 3 条（N1-N2 / N1-GND / N2-GND）",
            $"count={dividerInfo.PortEquivalents.Count}");
        var outputPort = dividerInfo.PortEquivalents.FirstOrDefault(p => p.PositiveNet == "N2" && p.NegativeNet == "GND");
        Assert(outputPort != null && outputPort.HasOpenCircuitVoltage &&
               Math.Abs(outputPort.OpenCircuitVoltageV - 6.0) < 1e-9 &&
               Math.Abs(outputPort.ResistanceOhm - 500.0) < 1e-6,
            "分压网络输出端口戴维南 (Voc=6V, Req=500Ω)",
            outputPort == null ? "missing" : $"Voc={outputPort.OpenCircuitVoltageV}, Req={outputPort.ResistanceOhm}");
        var sourcePort = dividerInfo.PortEquivalents.FirstOrDefault(p => p.IsAcSourcePort);
        Assert(sourcePort == null, "DC 网络不应把独立源端口标记为 AC 源端口", "IsAcSourcePort=true");
        Assert(dividerInfo.FrequencyResponse == null,
            "无交流激励时 FrequencyResponse 为 null", "FrequencyResponse not null");
        Assert(dividerInfo.BoundaryNote.Contains("交流", StringComparison.Ordinal),
            "无交流激励在 BoundaryNote 中说明", dividerInfo.BoundaryNote);
        Assert(dividerSolution.Steps.Count == 5, "分压网络推导步骤 = 5", $"count={dividerSolution.Steps.Count}");
        Assert(dividerSolution.Steps.Any(s => s.Title.Contains("MNA", StringComparison.OrdinalIgnoreCase)),
            "步骤中包含 MNA 矩阵方程步骤", string.Join("|", dividerSolution.Steps.Select(s => s.Title)));
        Assert(dividerSolution.Steps.Any(s => s.Title.Contains("戴维南", StringComparison.Ordinal)),
            "步骤中包含戴维南等效步骤", string.Join("|", dividerSolution.Steps.Select(s => s.Title)));
        // Display convention (2026-10-18): the raw MNA log keeps its legacy DC text
        // format but every shown number now has at most two decimals ("6 V").
        Assert(dividerSolution.RawMnaLog.Contains("=== MNA 求解日志 ===", StringComparison.Ordinal) &&
               dividerSolution.RawMnaLog.Contains("节点 N2 电位 v_2 = 6 V", StringComparison.Ordinal),
            "RawMnaLog 保持既有 DC 日志格式（节点电位行，显示最多两位小数）", dividerSolution.RawMnaLog);
        Assert(dividerSolution.AcParameters != null &&
               dividerSolution.AcParameters.VoltageGain.Contains("DC 电压比", StringComparison.Ordinal) &&
               dividerSolution.AcParameters.InputResistance.Contains("Ω", StringComparison.Ordinal) &&
               dividerSolution.AcParameters.OutputResistance.Contains("Ω", StringComparison.Ordinal),
            "DC 网络 AcParameters 填入 DC 电压比与输入/输出电阻",
            $"gain={dividerSolution.AcParameters?.VoltageGain}, rin={dividerSolution.AcParameters?.InputResistance}, rout={dividerSolution.AcParameters?.OutputResistance}");
        var mapped = dividerSolution.ToAnalysisResult();
        Assert(mapped.LinearNetwork != null && mapped.NodePotentials.Count == 2 && mapped.PortEquivalents.Count == 3,
            "ToAnalysisResult 映射 LinearNetwork / 节点电位 / 端口等效",
            $"nodes={mapped.NodePotentials.Count}, ports={mapped.PortEquivalents.Count}");

        // 7.2 Controlled-source network: non-null structure, >= 3 steps, no exception.
        var mixed = MixedControlledNetwork();
        var mixedClassification = TopologyClassifier.Classify(mixed);
        var mixedSolution = mixedClassification.Strategy
            .SolveAsync(mixed, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(mixedSolution.IsSuccess && mixedSolution.LinearNetwork != null,
            "受控源网络返回 LinearNetwork 且非空", mixedSolution.SummaryText);
        Assert(mixedSolution.Steps.Count >= 3, "受控源网络推导步骤 ≥ 3", $"count={mixedSolution.Steps.Count}");
        Assert(mixedSolution.LinearNetwork!.NodePotentials.Count == 3,
            "受控源网络节点电位覆盖 3 个非地节点",
            $"count={mixedSolution.LinearNetwork.NodePotentials.Count}");
        Assert(mixedSolution.LinearNetwork.PortEquivalents.Count >= 6,
            "受控源网络端口等效包含源端口/输出端口与任意节点对",
            $"count={mixedSolution.LinearNetwork.PortEquivalents.Count}");

        // 7.3 AC VCVS amplifier: FrequencyResponse filled, gain = 2, 61 sweep points.
        var acAmplifier = VcvsAmplifier(2.0, ac: true);
        var acClassification = TopologyClassifier.Classify(acAmplifier);
        Assert(acClassification.TopologyType == CircuitTopologyType.MnaLinear,
            "交流 VCVS 放大网络分类为 MnaLinear", $"got {acClassification.TopologyType}");
        var acSolution = acClassification.Strategy.SolveAsync(acAmplifier, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(acSolution.IsSuccess && acSolution.LinearNetwork != null,
            "交流 VCVS 网络结构化求解成功", acSolution.SummaryText);
        var response = acSolution.LinearNetwork!.FrequencyResponse;
        Assert(response != null, "交流网络生成 FrequencyResponse", "FrequencyResponse is null");
        Assert(response!.OperatingFrequencyHz == 1000.0, "频响工作频率取自交流源 FrequencyHz = 1000 Hz",
            $"op={response.OperatingFrequencyHz}");
        AssertRel(response.GainRatio, 2.0, 1e-9, "频响工作频率增益 |H| = 2", " V/V");
        AssertNear(response.GainDb, 20.0 * Math.Log10(2.0), 1e-9, "频响增益 = 6.0206 dB", " dB");
        AssertPhase(response.PhaseDeg, 0.0, 0.05, "频响相位 = 0°");
        Assert(response.Points.Count == 61, "频响扫频点数 = 61（10 Hz–10 MHz，10 点/十倍频程）",
            $"count={response.Points.Count}");
        Assert(response.InputPortLabel.Contains("N1", StringComparison.Ordinal) &&
               response.OutputPortLabel.Contains("N2", StringComparison.Ordinal),
            "频响输入/输出端口标签正确", $"{response.InputPortLabel} -> {response.OutputPortLabel}");
        Assert(!response.LowerCutoffHz.HasValue && !response.UpperCutoffHz.HasValue,
            "全频段平坦增益无 -3 dB 截止频率",
            $"lower={response.LowerCutoffHz}, upper={response.UpperCutoffHz}");
        Assert(response.BoundaryNote.Contains("截止", StringComparison.Ordinal),
            "频响 BoundaryNote 说明未检测到截止频率", response.BoundaryNote);
        Assert(acSolution.AcParameters != null &&
               acSolution.AcParameters.VoltageGain.Contains("2", StringComparison.Ordinal),
            "交流网络 AcParameters.VoltageGain 含增益值", acSolution.AcParameters?.VoltageGain ?? "missing");
        Assert(acSolution.LinearNetwork.PortEquivalents.Any(p => p.IsAcSourcePort),
            "交流网络端口等效表标记交流激励源端口",
            string.Join("|", acSolution.LinearNetwork.PortEquivalents.Select(p => $"{p.PositiveNet}-{p.NegativeNet}:{p.IsAcSourcePort}")));
        var acMapped = acSolution.ToAnalysisResult();
        Assert(acMapped.FrequencyResponse != null && acMapped.AcSweepPoints.Count == 61,
            "ToAnalysisResult 映射频响与 61 个扫频点",
            $"points={acMapped.AcSweepPoints.Count}");
        Assert(acSolution.RawMnaLog.Contains("=== MNA 求解日志", StringComparison.Ordinal),
            "交流网络 RawMnaLog 非空", acSolution.RawMnaLog);

        // 7.4 A diode network is now routed to the diode teaching strategy (DiodeCircuit),
        //     NOT to MNA. This particular fixture is a parallel DC loop (1 V source, 1 kΩ
        //     and D1 all across the same two nets), which the DiodeBias teaching model
        //     explicitly rejects instead of fabricating an operating point. The generic
        //     MnaLinearStrategy negative checks remain in 5.1/5.3 (direct TryCreate).
        var diode = new NetBuilder();
        diode.Element("V1", ElementType.DcVoltageSource, 1.0, "V");
        diode.Element("D1", ElementType.Diode, 1.0, "");
        diode.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        diode.Pin("V1", "pos", 1).Pin("V1", "neg", 0)
             .Pin("D1", "A", 1).Pin("D1", "K", 0)
             .Pin("R1", "1", 1).Pin("R1", "2", 0);
        var diodeNetlist = diode.Build();
        var diodeClassification = TopologyClassifier.Classify(diodeNetlist);
        Assert(diodeClassification.TopologyType == CircuitTopologyType.DiodeCircuit &&
               diodeClassification.SubtypeName == "DiodeBias",
            "二极管网络分类为 DiodeCircuit/DiodeBias（不再落入 MNA）",
            $"{diodeClassification.TopologyType}/{diodeClassification.SubtypeName}");
        Assert(diodeClassification.Strategy is CircuitAssistant.Engine.Strategies.DiodeCircuitStrategy,
            "二极管网络选择 DiodeCircuitStrategy 教学策略",
            diodeClassification.Strategy.GetType().Name);
        var diodeSolution = diodeClassification.Strategy
            .SolveAsync(diodeNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!diodeSolution.IsSuccess &&
               diodeSolution.SummaryText.Contains("二极管", StringComparison.Ordinal) &&
               (diodeSolution.SummaryText.Contains("并联", StringComparison.Ordinal) ||
                diodeSolution.SummaryText.Contains("分支", StringComparison.Ordinal)),
            "二极管并联直流回路被教学策略明确拒绝并点名二极管/并联分支限制", diodeSolution.SummaryText);
    }

    // =====================================================================
    // Section 8: adversarial audit regressions
    //   (out-of-window cutoff estimate / excitation-port priority)
    // =====================================================================

    private static void CheckAuditRegressions()
    {
        // ---- 8.1 Sweep window entirely inside the stopband must not fabricate cutoffs.
        var outsideWindow = CutoffOutsideWindowLowPass(includeSecondCapacitor: false);
        Assert(LinearNetworkAnalyzer.TryCreate(outsideWindow, out var outsideModel, out string error) && outsideModel != null,
            "越界截止回归：RC 低通网络建模成功", error);
        bool swept = outsideModel!.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out var outsideSweep, out error);
        Assert(swept && outsideSweep != null, "越界截止回归：扫频成功", error);
        Assert(outsideSweep!.Points[0].GainDb < -39.0,
            "越界截止回归：扫描下限 10 Hz 处增益已约 -40 dB（窗口完全位于阻带）",
            $"gainDb={outsideSweep.Points[0].GainDb.ToString("G6", CultureInfo.InvariantCulture)}");
        AssertNear(outsideSweep.ReferenceGainDb, 0.0, 0.5,
            "越界截止回归：通带参考增益由窗口外探针确定为 ≈0 dB", " dB");
        Assert(!outsideSweep.PassbandInWindow, "越界截止回归：扫描窗口内不含通带",
            $"refDb={outsideSweep.ReferenceGainDb.ToString("G6", CultureInfo.InvariantCulture)}");
        Assert(!outsideSweep.CutoffEstimateAvailable, "越界截止回归：截止估计标记为不可用",
            string.Join("；", outsideSweep.Notes));
        Assert(!outsideSweep.LowerCutoffHz.HasValue && !outsideSweep.UpperCutoffHz.HasValue &&
               !outsideSweep.BandwidthHz.HasValue,
            "越界截止回归：LowerCutoffHz/UpperCutoffHz/BandwidthHz 全部为 null（不再伪造 ≈14.1 Hz）",
            $"lower={outsideSweep.LowerCutoffHz}, upper={outsideSweep.UpperCutoffHz}, bw={outsideSweep.BandwidthHz}");
        Assert(outsideSweep.Notes.Any(n => n.Contains("扫频窗口未覆盖真实", StringComparison.Ordinal)) &&
               outsideSweep.Notes.Any(n => n.Contains("估计不可用", StringComparison.Ordinal)),
            "越界截止回归：BoundaryNote 明确说明扫频窗口未覆盖真实截止、估计不可用",
            string.Join("；", outsideSweep.Notes));
        Assert(!outsideSweep.Notes.Any(n => n.Contains("仍处于通带", StringComparison.Ordinal)),
            "越界截止回归：不再基于错误基准声称窗口边缘“仍处于通带”",
            string.Join("；", outsideSweep.Notes));

        // ---- 8.2 Same scenario through MnaLinearStrategy (2 parallel caps -> MNA classification).
        var outsideWindowMna = CutoffOutsideWindowLowPass(includeSecondCapacitor: true);
        var outsideClassification = TopologyClassifier.Classify(outsideWindowMna);
        Assert(outsideClassification.TopologyType == CircuitTopologyType.MnaLinear,
            "越界截止回归：并联双电容网络分类为 MnaLinear", $"got {outsideClassification.TopologyType}");
        var outsideSolution = outsideClassification.Strategy
            .SolveAsync(outsideWindowMna, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(outsideSolution.IsSuccess && outsideSolution.LinearNetwork != null,
            "越界截止回归：MnaLinearStrategy 求解成功", outsideSolution.SummaryText);
        var outsideResponse = outsideSolution.LinearNetwork!.FrequencyResponse;
        Assert(outsideResponse != null, "越界截止回归：仍生成工作频率频响", "FrequencyResponse is null");
        Assert(!outsideResponse!.LowerCutoffHz.HasValue && !outsideResponse.UpperCutoffHz.HasValue &&
               !outsideResponse.BandwidthHz.HasValue,
            "越界截止回归：结构化频响的截止/带宽字段全部为 null",
            $"lower={outsideResponse.LowerCutoffHz}, upper={outsideResponse.UpperCutoffHz}, bw={outsideResponse.BandwidthHz}");
        Assert(outsideResponse.LowerCutoffDisplay == "未检测到（超出扫描范围）" &&
               outsideResponse.UpperCutoffDisplay == "未检测到（超出扫描范围）" &&
               outsideResponse.BandwidthDisplay == "未检测到（超出扫描范围）",
            "越界截止回归：Display 明确写“未检测到（超出扫描范围）”",
            $"{outsideResponse.LowerCutoffDisplay} / {outsideResponse.UpperCutoffDisplay} / {outsideResponse.BandwidthDisplay}");
        Assert(outsideResponse.BoundaryNote.Contains("扫频窗口未覆盖真实", StringComparison.Ordinal),
            "越界截止回归：FrequencyResponse.BoundaryNote 说明窗口未覆盖真实截止",
            outsideResponse.BoundaryNote);
        Assert(outsideSolution.LinearNetwork.BoundaryNote.Contains("截止估计不可用", StringComparison.Ordinal),
            "越界截止回归：LinearNetwork.BoundaryNote 说明截止估计不可用",
            outsideSolution.LinearNetwork.BoundaryNote);
        Assert(outsideSolution.AcParameters != null &&
               outsideSolution.AcParameters.UpperCutoffFreq == "未检测到（超出扫描范围）" &&
               outsideSolution.AcParameters.Bandwidth == "未检测到（超出扫描范围）",
            "越界截止回归：AcParameters 截止/带宽显示字符串同步更新",
            $"upper={outsideSolution.AcParameters?.UpperCutoffFreq}, bw={outsideSolution.AcParameters?.Bandwidth}");
        // Negative control: a cutoff inside the window is still detected normally.
        Assert(LinearNetworkAnalyzer.TryCreate(RcLowPass(), out var inWindowModel, out error) && inWindowModel != null &&
               inWindowModel.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out var inWindowSweep, out error) &&
               inWindowSweep != null && inWindowSweep.CutoffEstimateAvailable &&
               inWindowSweep.UpperCutoffHz.HasValue,
            "越界截止回归：窗口内截止的正常 RC 低通仍正常检出上限截止（对照组）", error);

        // ---- 8.3 Excitation port: DC voltage source is preferred regardless of element id.
        foreach (var voltageId in new[] { "V1", "A_V1" })
        {
            var network = DualSubnetworkSources(voltageId);
            var classification = TopologyClassifier.Classify(network);
            Assert(classification.TopologyType == CircuitTopologyType.MnaLinear,
                $"激励源优先级回归：{voltageId} 网络分类为 MnaLinear", $"got {classification.TopologyType}");
            var solution = classification.Strategy.SolveAsync(network, new AnalysisParameters()).GetAwaiter().GetResult();
            Assert(solution.IsSuccess && solution.LinearNetwork != null,
                $"激励源优先级回归：{voltageId} 网络求解成功", solution.SummaryText);
            var sourcePort = solution.LinearNetwork!.PortEquivalents
                .FirstOrDefault(p => p.PortLabel.Contains("激励源", StringComparison.Ordinal));
            Assert(sourcePort != null && sourcePort.PositiveNet == "N2" && sourcePort.NegativeNet == "GND",
                $"激励源优先级回归：{voltageId} 网络激励端口取电压源端口 N2–GND（与 Id 顺序无关）",
                sourcePort?.PortLabel ?? "missing");
            string voltageGain = solution.AcParameters?.VoltageGain ?? "missing";
            Assert(voltageGain.Contains("DC 电压比", StringComparison.Ordinal) &&
                   voltageGain.Contains("0.2", StringComparison.Ordinal),
                $"激励源优先级回归：{voltageId} 网络 DC 电压比 = 1V/5V = 0.2", voltageGain);
            Assert(!voltageGain.Contains("电流源", StringComparison.Ordinal),
                $"激励源优先级回归：{voltageId} 网络不再误报“激励为电流源”", voltageGain);
        }

        // Deterministic: "V1" and "A_V1" orderings must report identical gain text.
        var orderedNetwork = DualSubnetworkSources("V1");
        var orderedSolution = TopologyClassifier.Classify(orderedNetwork).Strategy
            .SolveAsync(orderedNetwork, new AnalysisParameters()).GetAwaiter().GetResult();
        var reorderedNetwork = DualSubnetworkSources("A_V1");
        var reorderedSolution = TopologyClassifier.Classify(reorderedNetwork).Strategy
            .SolveAsync(reorderedNetwork, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(orderedSolution.AcParameters?.VoltageGain == reorderedSolution.AcParameters?.VoltageGain,
            "激励源优先级回归：V1 / A_V1 两种 Id 顺序得到相同 DC 增益文本（确定性）",
            $"{orderedSolution.AcParameters?.VoltageGain} vs {reorderedSolution.AcParameters?.VoltageGain}");

        // ---- 8.4 Current source is used as excitation only when no voltage source exists.
        var currentOnly = new NetBuilder();
        currentOnly.Element("I1", ElementType.DcCurrentSource, 1.0, "mA");
        currentOnly.Element("R1", ElementType.Resistor, 1.0, "kΩ");
        currentOnly.Pin("I1", "in", 1).Pin("I1", "out", 0)
                   .Pin("R1", "1", 1).Pin("R1", "2", 0);
        var currentOnlyNetlist = currentOnly.Build();
        var currentOnlySolution = TopologyClassifier.Classify(currentOnlyNetlist).Strategy
            .SolveAsync(currentOnlyNetlist, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(currentOnlySolution.IsSuccess &&
               (currentOnlySolution.AcParameters?.VoltageGain.Contains("电流源", StringComparison.Ordinal) ?? false),
            "激励源优先级回归：无电压源时才使用电流源作为激励端口",
            currentOnlySolution.AcParameters?.VoltageGain ?? "missing");

        // ---- 8.5 AC mode already prefers the AC voltage source over the AC current source.
        var acDual = DualSubnetworkAcSources();
        var acDualClassification = TopologyClassifier.Classify(acDual);
        Assert(acDualClassification.TopologyType == CircuitTopologyType.MnaLinear,
            "激励源优先级回归：双交流源网络分类为 MnaLinear", $"got {acDualClassification.TopologyType}");
        var acDualSolution = acDualClassification.Strategy
            .SolveAsync(acDual, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(acDualSolution.IsSuccess && acDualSolution.LinearNetwork?.FrequencyResponse != null,
            "激励源优先级回归：双交流源网络生成频响", acDualSolution.SummaryText);
        Assert(acDualSolution.LinearNetwork!.FrequencyResponse!.InputPortLabel.Contains("V1", StringComparison.Ordinal) &&
               acDualSolution.LinearNetwork.FrequencyResponse.InputPortLabel.Contains("N2", StringComparison.Ordinal),
            "激励源优先级回归：交流模式同样优先选择交流电压源端口（V1 / N2）",
            acDualSolution.LinearNetwork.FrequencyResponse.InputPortLabel);
    }

    // =====================================================================
    // Section 9: classifier text
    // =====================================================================

    private static void CheckClassifierText()
    {
        var classification = TopologyClassifier.Classify(Divider());
        Assert(classification.TopologyType == CircuitTopologyType.MnaLinear,
            "通用线性网络分类类型不变", $"got {classification.TopologyType}");
        Assert(classification.Description.Contains("受控源", StringComparison.Ordinal) &&
               classification.Description.Contains("频响", StringComparison.Ordinal),
            "MNA 分类描述文案更新为通用线性网络（受控源/动态元件/戴维南等效/频响）",
            classification.Description);
        Assert(classification.Strategy is CircuitAssistant.Engine.Strategies.MnaLinearStrategy,
            "MNA 分类仍返回 MnaLinearStrategy 实例", classification.Strategy.GetType().Name);
    }

    // =====================================================================
    // Section 10: PNP transistors + silicon/germanium forward drop.
    // Every expected numeric value below comes from an independent hand
    // calculation: DC node-equation KCL solving and a hybrid-pi small-signal
    // nodal solve (not from the solver under test). Derivation:
    //   VCC=12 V, fixed bias PNP: IB=(12-0.7)/470k≈24.0426 µA, IC=β·IB≈2.4043 mA,
    //   VEC=12-IC·2k≈7.1915 V;  ge: (12-0.3)/470k≈24.8936 µA -> VEC≈7.0213 V.
    //   divider PNP (Rb1 15k B-VCC / Rb2 51k B-GND / Re 1k E-VCC / Rc 3k C-GND):
    //   IB=(VCC-Vth-0.7)/(Rth+101·1k) with Vth=9.2727 V, Rth=11.5909 kΩ -> 18.0057 µA,
    //   IC=1.80057 mA, VEC=4.7797 V; Av=-β(Rc||RL)/rbe=-140.371, Ri=1.43978 kΩ, Ro=3 kΩ.
    //   collector feedback PNP (Rb 200k B-C / Rc 2k C-GND): IB=11.3/(200k+101·2k)=28.1095 µA,
    //   IC=2.81095 mA, VEC=6.32189 V; Av=(1/Rb-β/rbe)/(1/Rc+1/Rb)=-176.015,
    //   Ri=1/(1/rbe+(1-Av)/Rb)=563.699 Ω, Ro=Rc||Rb=1980.198 Ω.
    //   common-base PNP (Rb1 15k / Rb2 30k / Re 2k / Rc 3k / RL 3k, β=50):
    //   IB=(12-8-0.7)/(10k+51·2k)=29.4643 µA, IC=1.47321 mA, VEC=4.575 V,
    //   hie=932.424 Ω, Au=80.4355, ri'=Re||hib=18.1172 Ω, ro'=3 kΩ.
    // =====================================================================

    private static void CheckPnpAndMaterialSemantics()
    {
        // ---------- 10.1 Material premise: silicon 0.7 V / germanium 0.3 V ----------
        var siProbe = new CircuitElement { Id = "Qsi", Type = ElementType.BjtPnp, DisplayNumber = 100 };
        siProbe.InitializeDefaultPins();
        var geProbe = new CircuitElement
        {
            Id = "Qge",
            Type = ElementType.BjtPnp,
            DisplayNumber = 100,
            Material = SemiconductorMaterial.Germanium
        };
        geProbe.InitializeDefaultPins();
        Assert(Math.Abs(siProbe.ForwardVoltageDrop - 0.7) < 1e-12 &&
               Math.Abs(geProbe.ForwardVoltageDrop - 0.3) < 1e-12,
            "材料前置：硅管 ForwardVoltageDrop=0.7 V、锗管=0.3 V",
            $"{siProbe.ForwardVoltageDrop} / {geProbe.ForwardVoltageDrop}");
        Assert(siProbe.SupportsSemiconductorMaterial && geProbe.SupportsSemiconductorMaterial,
            "材料前置：BJT 元件声明支持半导体材料选择", $"{siProbe.SupportsSemiconductorMaterial}");

        // ---------- 10.2 Classification: single PNP -> BjtAmplifier (+PNP wording) ----------
        var pnpFixedSiNet = PnpFixedBias(SemiconductorMaterial.Silicon);
        var pnpFixedSiClass = TopologyClassifier.Classify(pnpFixedSiNet);
        Assert(pnpFixedSiClass.TopologyType == CircuitTopologyType.BjtAmplifier,
            "PNP 固定偏置：分类为 BjtAmplifier", pnpFixedSiClass.TopologyType.ToString());
        Assert(pnpFixedSiClass.SubtypeName == "FixedBias",
            "PNP 固定偏置：子类型仍为 FixedBias", pnpFixedSiClass.SubtypeName);
        Assert(pnpFixedSiClass.Description.Contains("PNP", StringComparison.Ordinal),
            "PNP 固定偏置：分类描述含 PNP 极性", pnpFixedSiClass.Description);
        Assert(pnpFixedSiClass.Strategy is CircuitAssistant.Engine.Strategies.BjtAmplifierStrategy,
            "PNP 固定偏置：仍返回 BjtAmplifierStrategy", pnpFixedSiClass.Strategy.GetType().Name);

        var pnpDividerSiNet = PnpVoltageDivider(SemiconductorMaterial.Silicon);
        var pnpDividerSiClass = TopologyClassifier.Classify(pnpDividerSiNet);
        Assert(pnpDividerSiClass.SubtypeName == "VoltageDividerSelfBias",
            "PNP 分压偏置：子类型识别为 VoltageDividerSelfBias", pnpDividerSiClass.SubtypeName);
        var pnpFeedbackSiNet = PnpCollectorFeedback(SemiconductorMaterial.Silicon);
        var pnpFeedbackSiClass = TopologyClassifier.Classify(pnpFeedbackSiNet);
        Assert(pnpFeedbackSiClass.SubtypeName == "CollectorFeedbackBias",
            "PNP 集电极反馈偏置：子类型识别为 CollectorFeedbackBias", pnpFeedbackSiClass.SubtypeName);
        var pnpCbSiNet = PnpCommonBase(SemiconductorMaterial.Silicon);
        var pnpCbSiClass = TopologyClassifier.Classify(pnpCbSiNet);
        Assert(pnpCbSiClass.SubtypeName == "CommonBase",
            "PNP 共基极：子类型识别为 CommonBase", pnpCbSiClass.SubtypeName);
        Assert(pnpCbSiClass.Description.Contains("PNP", StringComparison.Ordinal),
            "PNP 共基极：分类描述含 PNP 极性", pnpCbSiClass.Description);
        var npnDividerSiNet = NpnVoltageDivider(SemiconductorMaterial.Silicon);
        var npnDividerSiClass = TopologyClassifier.Classify(npnDividerSiNet);
        Assert(npnDividerSiClass.TopologyType == CircuitTopologyType.BjtAmplifier &&
               !npnDividerSiClass.Description.Contains("PNP", StringComparison.Ordinal),
            "NPN 分类描述回归：原有文案不含 PNP（未改写 NPN 分支）", npnDividerSiClass.Description);

        // ---------- 10.3 PNP fixed bias, silicon ----------
        var fixedSi = pnpFixedSiClass.Strategy.SolveAsync(pnpFixedSiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(fixedSi.IsSuccess, "PNP 固定偏置（硅管）：求解成功", fixedSi.SummaryText);
        Assert(fixedSi.Steps.Count == 5, "PNP 固定偏置（硅管）：5 步教学推导", $"count={fixedSi.Steps.Count}");
        var fixedSiQ = fixedSi.QPoint;
        Assert(fixedSiQ != null && fixedSiQ.IsActiveRegion,
            "PNP 固定偏置（硅管）：Q 点位于放大区", fixedSiQ?.WorkingRegion ?? "QPoint is null");
        Assert(fixedSiQ != null && fixedSiQ.WorkingRegion.Contains("PNP", StringComparison.Ordinal) &&
               fixedSiQ.WorkingRegion.Contains("U_CE", StringComparison.Ordinal),
            "PNP 固定偏置：WorkingRegion 注明 PNP 的 U_CE 方向约定", fixedSiQ?.WorkingRegion ?? "null");
        // 教材估算法（用户口径 2026-10-18）：每个中间量按显示单位保留两位小数后代入
        // 下一步，因此 Q 点/交流指标与精确戴维南解有可见差异，期望值按手算舍入链给出。
        AssertRel(ParseMetric(fixedSiQ!.Ibq), 24.04e-6, 1e-3,
            "PNP 固定偏置（硅管）IBQ=(12-0.7)/470k≈24.04 µA（教材估算法）", " A");
        AssertRel(ParseMetric(fixedSiQ.Icq), 2.4e-3, 1e-3,
            "PNP 固定偏置（硅管）ICQ=β·IBQ≈2.4 mA（教材估算法）", " A");
        AssertRel(ParseMetric(fixedSiQ.Vceq), 7.2, 1e-3,
            "PNP 固定偏置（硅管）|U_CE|≈7.2 V（教材估算法）", " V");
        AssertRel(ParseMetric(fixedSiQ.Icq) / ParseMetric(fixedSiQ.Ibq), 100.0, 2e-3,
            "PNP 固定偏置：ICQ/IBQ = β = 100");
        Assert(!fixedSiQ.Vceq.Contains('-', StringComparison.Ordinal) && ParseMetric(fixedSiQ.Vceq) > 0,
            "PNP 固定偏置：QPoint.Vceq 显示 |U_CE| 正值（不带负号）", fixedSiQ.Vceq);
        Assert(fixedSi.SummaryText.Contains("PNP", StringComparison.Ordinal) &&
               fixedSi.SummaryText.Contains("U_CE = -", StringComparison.Ordinal) &&
               fixedSi.SummaryText.Contains("I_E", StringComparison.Ordinal),
            "PNP 固定偏置：摘要说明 PNP 电压/电流方向", fixedSi.SummaryText);
        Assert(fixedSi.SummaryText.Contains("硅管", StringComparison.Ordinal) &&
               fixedSi.SummaryText.Contains("0.7 V", StringComparison.Ordinal),
            "PNP 固定偏置（硅管）：摘要注明硅管与 0.7 V 压降（教材写法，≤2 位小数）", fixedSi.SummaryText);
        AssertRel(ParseMetric(fixedSi.AcParameters!.VoltageGain), -156.25, 1e-3,
            "PNP 固定偏置（硅管）Av=-β·Rc/rbe≈-156.25（教材估算法）");
        AssertRel(ParseMetric(fixedSi.AcParameters.InputResistance), 1280.0, 1e-3,
            "PNP 固定偏置（硅管）Ri=RB||rbe≈1.28 kΩ（教材估算法）", " Ω");
        AssertRel(ParseMetric(fixedSi.AcParameters.OutputResistance), 2000.0, 1e-3,
            "PNP 固定偏置（硅管）Ro=RC=2 kΩ", " Ω");

        // ---------- 10.4 PNP fixed bias, germanium: 0.3 V changes the Q-point ----------
        var pnpFixedGeNet = PnpFixedBias(SemiconductorMaterial.Germanium);
        var fixedGe = TopologyClassifier.Classify(pnpFixedGeNet).Strategy
            .SolveAsync(pnpFixedGeNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(fixedGe.IsSuccess, "PNP 固定偏置（锗管）：求解成功", fixedGe.SummaryText);
        var fixedGeQ = fixedGe.QPoint!;
        AssertRel(ParseMetric(fixedGeQ.Ibq), 24.89e-6, 1e-3,
            "PNP 固定偏置（锗管）IBQ=(12-0.3)/470k≈24.89 µA（教材估算法）", " A");
        AssertRel(ParseMetric(fixedGeQ.Icq), 2.49e-3, 1e-3,
            "PNP 固定偏置（锗管）ICQ≈2.49 mA（教材估算法）", " A");
        AssertRel(ParseMetric(fixedGeQ.Vceq), 7.02, 1e-3,
            "PNP 固定偏置（锗管）|U_CE|≈7.02 V（教材估算法）", " V");
        Assert(ParseMetric(fixedGeQ.Ibq) > ParseMetric(fixedSiQ.Ibq),
            "硅/锗压降：锗管 V_EB=0.3 V < 0.7 V，IBQ 增大",
            $"{ParseMetric(fixedSiQ.Ibq):G6} -> {ParseMetric(fixedGeQ.Ibq):G6}");
        Assert(ParseMetric(fixedGeQ.Vceq) < ParseMetric(fixedSiQ.Vceq),
            "硅/锗压降：锗管 ICQ 增大导致 |U_CE| 减小");
        Assert(fixedGe.SummaryText.Contains("锗管", StringComparison.Ordinal) &&
               fixedGe.SummaryText.Contains("0.3 V", StringComparison.Ordinal),
            "PNP 固定偏置（锗管）：摘要注明锗管与 0.3 V 压降（教材写法，≤2 位小数）", fixedGe.SummaryText);
        AssertRel(ParseMetric(fixedGe.AcParameters!.VoltageGain), -160.0, 1e-3,
            "PNP 固定偏置（锗管）Av≈-160（rbe 随 IEQ 变化，教材估算法）");

        // ---------- 10.5 PNP voltage-divider self bias, silicon + germanium ----------
        var dividerSi = pnpDividerSiClass.Strategy.SolveAsync(pnpDividerSiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(dividerSi.IsSuccess, "PNP 分压偏置（硅管）：求解成功", dividerSi.SummaryText);
        Assert(dividerSi.Steps.Count == 5, "PNP 分压偏置（硅管）：5 步教学推导", $"count={dividerSi.Steps.Count}");
        Assert(dividerSi.QPoint != null && dividerSi.QPoint.IsActiveRegion,
            "PNP 分压偏置（硅管）：Q 点位于放大区", dividerSi.QPoint?.WorkingRegion ?? "null");
        AssertRel(ParseMetric(dividerSi.QPoint!.Ibq), 20.1e-6, 1e-3,
            "PNP 分压偏置（硅管）IBQ≈20.1 µA（教材估算法：U_B 先舍入两位）", " A");
        AssertRel(ParseMetric(dividerSi.QPoint.Icq), 2.01e-3, 1e-3,
            "PNP 分压偏置（硅管）ICQ≈2.01 mA（教材估算法）", " A");
        AssertRel(ParseMetric(dividerSi.QPoint.Vceq), 3.94, 1e-3,
            "PNP 分压偏置（硅管）|U_CE|≈3.94 V（教材估算法）", " V");
        AssertRel(ParseMetric(dividerSi.AcParameters!.VoltageGain), -155.03, 1e-3,
            "PNP 分压偏置（硅管）Av=-β(Rc||RL)/rbe≈-155.03（教材估算法）");
        AssertRel(ParseMetric(dividerSi.AcParameters.InputResistance), 1320.0, 1e-3,
            "PNP 分压偏置（硅管）Ri=Rb1||Rb2||rbe≈1.32 kΩ（教材估算法）", " Ω");
        AssertRel(ParseMetric(dividerSi.AcParameters.OutputResistance), 3000.0, 1e-3,
            "PNP 分压偏置（硅管）Ro=RC=3 kΩ", " Ω");
        Assert(ParseMetric(dividerSi.AcParameters.LowerCutoffFreq) > 0 &&
               Math.Abs(ParseMetric(dividerSi.AcParameters.LowerCutoffFreq) - 31.92) < 0.32,
            "PNP 分压偏置（硅管）：耦合/旁路电容识别后 fL≈31.92 Hz（教材估算法）",
            dividerSi.AcParameters.LowerCutoffFreq);
        Assert(ParseMetric(dividerSi.AcParameters.Bandwidth) > 0,
            "PNP 分压偏置（硅管）：通频带 BW 为正", dividerSi.AcParameters.Bandwidth);

        var pnpDividerGeNet = PnpVoltageDivider(SemiconductorMaterial.Germanium);
        var dividerGe = TopologyClassifier.Classify(pnpDividerGeNet).Strategy
            .SolveAsync(pnpDividerGeNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(dividerGe.IsSuccess, "PNP 分压偏置（锗管）：求解成功", dividerGe.SummaryText);
        AssertRel(ParseMetric(dividerGe.QPoint!.Ibq), 24.06e-6, 1e-3,
            "PNP 分压偏置（锗管）IBQ≈24.06 µA（教材估算法）", " A");
        AssertRel(ParseMetric(dividerGe.QPoint.Icq), 2.41e-3, 1e-3,
            "PNP 分压偏置（锗管）ICQ≈2.41 mA（教材估算法）", " A");
        AssertRel(ParseMetric(dividerGe.QPoint.Vceq), 2.34, 1e-3,
            "PNP 分压偏置（锗管）|U_CE|≈2.34 V（教材估算法）", " V");
        Assert(ParseMetric(dividerGe.QPoint.Ibq) > ParseMetric(dividerSi.QPoint.Ibq),
            "硅/锗压降：分压偏置锗管 IBQ 大于硅管");
        AssertRel(ParseMetric(dividerGe.AcParameters!.VoltageGain), -180.47, 1e-3,
            "PNP 分压偏置（锗管）Av≈-180.47（教材估算法）");

        // ---------- 10.6 PNP collector-feedback bias, silicon ----------
        var feedbackSi = pnpFeedbackSiClass.Strategy.SolveAsync(pnpFeedbackSiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(feedbackSi.IsSuccess, "PNP 集电极反馈偏置（硅管）：求解成功", feedbackSi.SummaryText);
        Assert(feedbackSi.Steps.Count == 5, "PNP 集电极反馈偏置：5 步教学推导", $"count={feedbackSi.Steps.Count}");
        Assert(feedbackSi.QPoint != null && feedbackSi.QPoint.IsActiveRegion,
            "PNP 集电极反馈偏置：Q 点位于放大区", feedbackSi.QPoint?.WorkingRegion ?? "null");
        AssertRel(ParseMetric(feedbackSi.QPoint!.Ibq), 28.10945274e-6, 1e-3,
            "PNP 集电极反馈偏置 IBQ=(12-0.7)/(200k+101·2k)≈28.1095 µA", " A");
        AssertRel(ParseMetric(feedbackSi.QPoint.Icq), 2.810945274e-3, 1e-3,
            "PNP 集电极反馈偏置 ICQ≈2.81095 mA", " A");
        AssertRel(ParseMetric(feedbackSi.QPoint.Vceq), 6.321890547, 1e-3,
            "PNP 集电极反馈偏置 |U_CE|≈6.3219 V", " V");
        AssertRel(ParseMetric(feedbackSi.AcParameters!.VoltageGain), -176.79, 1e-3,
            "PNP 集电极反馈偏置 Av=(1/Rb-β/rbe)/(1/Rc+1/Rb)≈-176.79（教材估算法）");
        AssertRel(ParseMetric(feedbackSi.AcParameters.InputResistance), 561.23, 1e-3,
            "PNP 集电极反馈偏置 Ri=1/(1/rbe+(1-Av)/Rb)≈561.23 Ω（教材估算法）", " Ω");
        AssertRel(ParseMetric(feedbackSi.AcParameters.OutputResistance), 1980.1980198, 1e-3,
            "PNP 集电极反馈偏置 Ro=Rc||Rb≈1980.2 Ω", " Ω");

        // ---------- 10.7 PNP common-base, silicon + germanium ----------
        var cbSi = pnpCbSiClass.Strategy.SolveAsync(pnpCbSiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(cbSi.IsSuccess, "PNP 共基极（硅管）：求解成功", cbSi.SummaryText);
        Assert(cbSi.Steps.Count == 5, "PNP 共基极（硅管）：5 步教学推导", $"count={cbSi.Steps.Count}");
        Assert(cbSi.QPoint != null && cbSi.QPoint.IsActiveRegion,
            "PNP 共基极（硅管）：Q 点位于放大区", cbSi.QPoint?.WorkingRegion ?? "null");
        AssertRel(ParseMetric(cbSi.QPoint!.Ibq), 32.35e-6, 1e-3,
            "PNP 共基极（硅管）IBQ≈32.35 µA（教材估算法：U_B=8 V 先舍入）", " A");
        AssertRel(ParseMetric(cbSi.QPoint.Icq), 1.62e-3, 1e-3,
            "PNP 共基极（硅管）ICQ≈1.62 mA（教材估算法）", " A");
        AssertRel(ParseMetric(cbSi.QPoint.Vceq), 3.84, 1e-3,
            "PNP 共基极（硅管）|U_CE|≈3.84 V（教材估算法）", " V");
        AssertRel(ParseMetric(cbSi.AcParameters!.VoltageGain), 87.86, 1e-3,
            "PNP 共基极（硅管）Au=β(Rc||RL)/hie≈87.86（同相，教材估算法）");
        AssertRel(ParseMetric(cbSi.AcParameters.InputResistance), 16.6, 1e-3,
            "PNP 共基极（硅管）ri'=Re||hib≈16.6 Ω（教材估算法）", " Ω");
        AssertRel(ParseMetric(cbSi.AcParameters.OutputResistance), 3000.0, 1e-3,
            "PNP 共基极（硅管）ro'=Rc=3 kΩ", " Ω");

        var pnpCbGeNet = PnpCommonBase(SemiconductorMaterial.Germanium);
        var cbGe = TopologyClassifier.Classify(pnpCbGeNet).Strategy
            .SolveAsync(pnpCbGeNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(cbGe.IsSuccess, "PNP 共基极（锗管）：求解成功", cbGe.SummaryText);
        AssertRel(ParseMetric(cbGe.QPoint!.Ibq), 36.27e-6, 1e-3,
            "PNP 共基极（锗管）IBQ≈36.27 µA（教材估算法）", " A");
        AssertRel(ParseMetric(cbGe.QPoint.Vceq), 2.87, 1e-3,
            "PNP 共基极（锗管）|U_CE|≈2.87 V（教材估算法）", " V");
        AssertRel(ParseMetric(cbGe.AcParameters!.VoltageGain), 97.81, 1e-3,
            "PNP 共基极（锗管）Au≈97.81（教材估算法）");

        // ---------- 10.8 NPN regression: same circuit, silicon vs germanium ----------
        var npnSi = npnDividerSiClass.Strategy.SolveAsync(npnDividerSiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(npnSi.IsSuccess && npnSi.QPoint != null && npnSi.QPoint.IsActiveRegion,
            "NPN 回归（硅管）：分压偏置仍在放大区求解成功", npnSi.SummaryText);
        AssertRel(ParseMetric(npnSi.QPoint!.Ibq), 20.1e-6, 1e-3,
            "NPN 回归（硅管）IBQ≈20.1 µA（VBE=0.7 V，教材估算法）", " A");
        AssertRel(ParseMetric(npnSi.QPoint.Icq), 2.01e-3, 1e-3,
            "NPN 回归（硅管）ICQ≈2.01 mA（教材估算法）", " A");
        AssertRel(ParseMetric(npnSi.QPoint.Vceq), 3.94, 1e-3,
            "NPN 回归（硅管）VCEQ≈3.94 V（教材估算法）", " V");
        var npnGeNet = NpnVoltageDivider(SemiconductorMaterial.Germanium);
        var npnGe = TopologyClassifier.Classify(npnGeNet).Strategy
            .SolveAsync(npnGeNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(npnGe.IsSuccess && npnGe.QPoint != null && npnGe.QPoint.IsActiveRegion,
            "NPN 回归（锗管）：分压偏置仍在放大区求解成功", npnGe.SummaryText);
        AssertRel(ParseMetric(npnGe.QPoint!.Ibq), 24.06e-6, 1e-3,
            "NPN 回归（锗管）IBQ≈24.06 µA（VBE=0.3 V，教材估算法）", " A");
        AssertRel(ParseMetric(npnGe.QPoint.Icq), 2.41e-3, 1e-3,
            "NPN 回归（锗管）ICQ≈2.41 mA（教材估算法）", " A");
        AssertRel(ParseMetric(npnGe.QPoint.Vceq), 2.34, 1e-3,
            "NPN 回归（锗管）VCEQ≈2.34 V（教材估算法）", " V");
        Assert(ParseMetric(npnGe.QPoint.Ibq) > ParseMetric(npnSi.QPoint.Ibq),
            "NPN 回归：VBE 0.7 V -> 0.3 V 使 IBQ 增大，证明材料压降已接入（不再硬编码 0.7）");
        Assert(ParseMetric(npnGe.QPoint.Vceq) < ParseMetric(npnSi.QPoint.Vceq),
            "NPN 回归：锗管电流增大使 VCEQ 下降",
            $"{ParseMetric(npnSi.QPoint.Vceq):G6} -> {ParseMetric(npnGe.QPoint.Vceq):G6}");

        // ---------- 10.9 Explicit rejections: multi-stage / mixed / nonlinear / cutoff / saturation ----------
        var multiNet = TwoBjtMultiStage();
        var multiClass = TopologyClassifier.Classify(multiNet);
        Assert(multiClass.TopologyType == CircuitTopologyType.MultiStageAmplifier &&
               multiClass.SubtypeName == "Cascade",
            "多级拒绝：≥2 只 BJT 分类为 MultiStageAmplifier/Cascade（不再套用单管公式）",
            $"{multiClass.TopologyType}/{multiClass.SubtypeName}");
        Assert(multiClass.Strategy is CircuitAssistant.Engine.Strategies.MultiStageAmplifierStrategy,
            "多级拒绝：选择 MultiStageAmplifierStrategy（预留接口实现）",
            multiClass.Strategy.GetType().Name);
        var multiSolution = multiClass.Strategy.SolveAsync(multiNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!multiSolution.IsSuccess, "多级拒绝：两个 BJT 的多级网络被明确拒绝", multiSolution.SummaryText);
        Assert(!string.IsNullOrWhiteSpace(multiSolution.SummaryText) &&
               multiSolution.SummaryText.Contains("多级", StringComparison.Ordinal) &&
               multiSolution.SummaryText.Contains("后续开发规划", StringComparison.Ordinal) &&
               multiSolution.SummaryText.Contains("预留", StringComparison.Ordinal) &&
               multiSolution.SummaryText.Contains("只支持单管", StringComparison.Ordinal),
            "多级拒绝：失败信息说明多级/后续开发规划/预留接口/只支持单管", multiSolution.SummaryText);
        Assert(!multiSolution.SummaryText.Contains("支持单个 NPN 放大电路", StringComparison.Ordinal) &&
               !multiSolution.SummaryText.Contains("PNP、多级与混合非线性网络尚未支持", StringComparison.Ordinal),
            "多级拒绝：失败信息不再套用旧 PNP/混合文案", multiSolution.SummaryText);
        // IMultiStageAnalysis.TryPlan 的阶段分解由 tests/NonlinearCircuitChecks/BjtCcChecks 覆盖
        // （本文件的 NetBuilder 夹具只建网表分区，不填充 ElectricalNetlist.Transistors）。

        var mixedNet = NpnPnpMixed();
        var mixedClass = TopologyClassifier.Classify(mixedNet);
        Assert(mixedClass.TopologyType == CircuitTopologyType.MultiStageAmplifier &&
               mixedClass.SubtypeName == "Cascade",
            "混合拒绝：NPN+PNP 混合网络按晶体管总数优先分类为 MultiStageAmplifier/Cascade",
            $"{mixedClass.TopologyType}/{mixedClass.SubtypeName}");
        var mixedSolution = mixedClass.Strategy.SolveAsync(mixedNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!mixedSolution.IsSuccess, "混合拒绝：NPN+PNP 混合网络被明确拒绝", mixedSolution.SummaryText);
        Assert(mixedSolution.SummaryText.Contains("多级", StringComparison.Ordinal) &&
               mixedSolution.SummaryText.Contains("后续开发规划", StringComparison.Ordinal) &&
               mixedSolution.SummaryText.Contains("预留", StringComparison.Ordinal),
            "混合拒绝：失败信息归入多级/后续开发规划/预留接口，而不是单管公式", mixedSolution.SummaryText);

        var cutoffSolution = TopologyClassifier.Classify(PnpCutoff()).Strategy
            .SolveAsync(PnpCutoff(), new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!cutoffSolution.IsSuccess && cutoffSolution.SummaryText.Contains("截止", StringComparison.Ordinal),
            "PNP 截止拒绝：IBQ≤0 时明确说明截止区", cutoffSolution.SummaryText);

        var satNet = PnpSaturated();
        var satSolution = TopologyClassifier.Classify(satNet).Strategy
            .SolveAsync(satNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!satSolution.IsSuccess && satSolution.SummaryText.Contains("饱和", StringComparison.Ordinal),
            "PNP 饱和拒绝：U_EC≤0.2 V 时明确说明饱和区", satSolution.SummaryText);

        var diodeNet = PnpWithDiode();
        var diodeClass = TopologyClassifier.Classify(diodeNet);
        Assert(diodeClass.TopologyType == CircuitTopologyType.BjtAmplifier,
            "非线性拒绝：含二极管的 PNP 网络仍先分类为 BjtAmplifier", diodeClass.TopologyType.ToString());
        var diodeSolution = diodeClass.Strategy.SolveAsync(diodeNet, new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!diodeSolution.IsSuccess && diodeSolution.SummaryText.Contains("非线性", StringComparison.Ordinal),
            "非线性拒绝：含二极管网络说明非线性器件尚未支持", diodeSolution.SummaryText);

        var missingBiasSolution = TopologyClassifier.Classify(PnpMissingBaseBias()).Strategy
            .SolveAsync(PnpMissingBaseBias(), new AnalysisParameters()).GetAwaiter().GetResult();
        Assert(!missingBiasSolution.IsSuccess && missingBiasSolution.SummaryText.Contains("偏置", StringComparison.Ordinal),
            "缺失偏置拒绝：PNP 基极无正确偏置电阻时说明偏置网络不完整", missingBiasSolution.SummaryText);
    }

    // =====================================================================
    // Section 10 fixtures: BJT teaching circuits (PNP mirrored from NPN)
    // =====================================================================

    /// <summary>PNP fixed bias: E tied to VCC, Rb from B to GND, Rc from C to GND.</summary>
    private static ElectricalNetlist PnpFixedBias(SemiconductorMaterial material,
        double rbKiloOhm = 470.0, double rcKiloOhm = 2.0)
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "", material: material);
        builder.Element("Rb", ElementType.Resistor, rbKiloOhm, "kΩ");
        builder.Element("Rc", ElementType.Resistor, rcKiloOhm, "kΩ");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 1)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 0)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0);
        return builder.Build();
    }

    /// <summary>
    /// PNP voltage-divider self bias (mirror of the NPN CE teaching template):
    /// Rb1 15k B-VCC, Rb2 51k B-GND, Re 1k E-VCC, Rc 3k C-GND, RL 10k,
    /// C1 10µF input coupling, C2 10µF output coupling, Ce 47µF emitter bypass (E-VCC).
    /// </summary>
    private static ElectricalNetlist PnpVoltageDivider(SemiconductorMaterial material)
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "", material: material);
        builder.Element("Rb1", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("RL", ElementType.Resistor, 10.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        builder.Element("Ce", ElementType.Capacitor, 47.0, "μF");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 1)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0)
               .Pin("RL", "1", 6).Pin("RL", "2", 0)
               .Pin("C1", "1", 5).Pin("C1", "2", 2)
               .Pin("C2", "1", 3).Pin("C2", "2", 6)
               .Pin("Ce", "1", 4).Pin("Ce", "2", 1);
        return builder.Build();
    }

    /// <summary>PNP collector-feedback bias: Rb from B to C, Rc from C to GND, E tied to VCC.</summary>
    private static ElectricalNetlist PnpCollectorFeedback(SemiconductorMaterial material)
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "", material: material);
        builder.Element("Rb", ElementType.Resistor, 200.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 1)
               .Pin("Rb", "1", 3).Pin("Rb", "2", 2)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0);
        return builder.Build();
    }

    /// <summary>
    /// PNP common-base (mirror of the NPN "Question 13" fixture):
    /// Rb1 15k B-VCC, Rb2 30k B-GND, Re 2k E-VCC, Rc 3k C-GND, RL 3k,
    /// C1 10µF output coupling, Cb 10µF base bypass (B-GND), Ci 10µF input coupling (E).
    /// </summary>
    private static ElectricalNetlist PnpCommonBase(SemiconductorMaterial material)
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtPnp, 50.0, "", material: material);
        builder.Element("Rb1", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 30.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("RL", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("Cb", ElementType.Capacitor, 10.0, "μF");
        builder.Element("Ci", ElementType.Capacitor, 10.0, "μF");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 1)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0)
               .Pin("RL", "1", 6).Pin("RL", "2", 0)
               .Pin("C1", "1", 3).Pin("C1", "2", 6)
               .Pin("Cb", "1", 2).Pin("Cb", "2", 0)
               .Pin("Ci", "1", 5).Pin("Ci", "2", 4);
        return builder.Build();
    }

    /// <summary>
    /// NPN regression fixture: standard voltage-divider self bias CE amplifier
    /// (VCC=12, Rb1 51k, Rb2 15k, Re 1k, Rc 3k, RL 10k, C1/C2 10µF, Ce 47µF, β=100).
    /// </summary>
    private static ElectricalNetlist NpnVoltageDivider(SemiconductorMaterial material)
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("VS", ElementType.AcVoltageSource, 10.0, "mV");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "", material: material);
        builder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("RL", ElementType.Resistor, 10.0, "kΩ");
        builder.Element("C1", ElementType.Capacitor, 10.0, "μF");
        builder.Element("C2", ElementType.Capacitor, 10.0, "μF");
        builder.Element("Ce", ElementType.Capacitor, 47.0, "μF");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("VS", "pos", 5).Pin("VS", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Rc", "1", 1).Pin("Rc", "2", 3)
               .Pin("RL", "1", 6).Pin("RL", "2", 0)
               .Pin("C1", "1", 5).Pin("C1", "2", 2)
               .Pin("C2", "1", 3).Pin("C2", "2", 6)
               .Pin("Ce", "1", 4).Pin("Ce", "2", 0);
        return builder.Build();
    }

    /// <summary>Two cascaded NPN devices -> must be rejected as multi-stage.</summary>
    private static ElectricalNetlist TwoBjtMultiStage()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Q2", ElementType.BjtNpn, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rc1", ElementType.Resistor, 3.0, "kΩ");
        builder.Element("Rc2", ElementType.Resistor, 2.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Q2", "B", 3).Pin("Q2", "C", 5).Pin("Q2", "E", 0)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Rc1", "1", 1).Pin("Rc1", "2", 3)
               .Pin("Rc2", "1", 1).Pin("Rc2", "2", 5);
        return builder.Build();
    }

    /// <summary>One NPN plus one PNP -> mixed-polarity network must be rejected.</summary>
    private static ElectricalNetlist NpnPnpMixed()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtNpn, 100.0, "");
        builder.Element("Q2", ElementType.BjtPnp, 80.0, "");
        builder.Element("Rb1", ElementType.Resistor, 51.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 15.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rc1", ElementType.Resistor, 3.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Q2", "B", 3).Pin("Q2", "C", 0).Pin("Q2", "E", 1)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 0)
               .Pin("Rc1", "1", 1).Pin("Rc1", "2", 3);
        return builder.Build();
    }

    /// <summary>PNP divider whose base drive (VCC-Vth) is smaller than V_EB -> cutoff.</summary>
    private static ElectricalNetlist PnpCutoff()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "");
        builder.Element("Rb1", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rb2", ElementType.Resistor, 100.0, "kΩ");
        builder.Element("Re", ElementType.Resistor, 1.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 3.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 4)
               .Pin("Rb1", "1", 1).Pin("Rb1", "2", 2)
               .Pin("Rb2", "1", 2).Pin("Rb2", "2", 0)
               .Pin("Re", "1", 4).Pin("Re", "2", 1)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0);
        return builder.Build();
    }

    /// <summary>PNP fixed bias with an oversized Rc: V_EC = 12 - IC·5k < 0.2 V -> saturation.</summary>
    private static ElectricalNetlist PnpSaturated()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "");
        builder.Element("Rb", ElementType.Resistor, 470.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 5.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 1)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 0)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0);
        return builder.Build();
    }

    /// <summary>Valid PNP bias network plus a diode branch: must be rejected as nonlinear.</summary>
    private static ElectricalNetlist PnpWithDiode()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "");
        builder.Element("Rb", ElementType.Resistor, 470.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.0, "kΩ");
        builder.Element("D1", ElementType.Diode, 0.7, "V");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 1)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 0)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0)
               .Pin("D1", "A", 3).Pin("D1", "K", 0);
        return builder.Build();
    }

    /// <summary>PNP with the base resistor tied to VCC only: no valid base-current path -> missing bias.</summary>
    private static ElectricalNetlist PnpMissingBaseBias()
    {
        var builder = new NetBuilder();
        builder.Element("VCC", ElementType.DcVoltageSource, 12.0, "V");
        builder.Element("Q1", ElementType.BjtPnp, 100.0, "");
        builder.Element("Rb", ElementType.Resistor, 470.0, "kΩ");
        builder.Element("Rc", ElementType.Resistor, 2.0, "kΩ");
        builder.Name(1, "VCC");
        builder.Pin("VCC", "pos", 1).Pin("VCC", "neg", 0)
               .Pin("Q1", "B", 2).Pin("Q1", "C", 3).Pin("Q1", "E", 1)
               .Pin("Rb", "1", 2).Pin("Rb", "2", 1)
               .Pin("Rc", "1", 3).Pin("Rc", "2", 0);
        return builder.Build();
    }

    private static readonly Regex MetricRegex = new(
        @"^\s*([-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)\s*(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Parses a displayed engineering value ("24.043 μA", "1.98 kΩ", "7.191 V",
    /// "-176.01", "31.27 Hz") back to base SI units for numeric assertions.
    /// </summary>
    private static double ParseMetric(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        var match = MetricRegex.Match(text);
        if (!match.Success) return double.NaN;
        double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string suffix = match.Groups[2].Value.Trim();
        if (suffix.Length == 0) return value;
        char prefix = suffix[0];
        double scale = suffix switch
        {
            _ when suffix.Contains("Ω", StringComparison.Ordinal) => prefix switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'm' => 1e-3,
                _ => 1.0
            },
            _ when suffix.Contains("A", StringComparison.Ordinal) => prefix switch
            {
                'μ' or 'µ' or 'u' => 1e-6,
                'n' => 1e-9,
                'm' => 1e-3,
                'k' => 1e3,
                _ => 1.0
            },
            _ when suffix.Contains("Hz", StringComparison.Ordinal) => prefix switch
            {
                'k' => 1e3,
                'M' => 1e6,
                'G' => 1e9,
                _ => 1.0
            },
            _ => 1.0
        };
        return value * scale;
    }
}
