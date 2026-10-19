using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Engine.Nonlinear;
using CircuitAssistant.Models;

namespace NonlinearCircuitChecks;

internal static class OperatingRegionChecks
{
    public static void Run()
    {
        CheckResult.Section("器件工作原理：接地射极、工作区边界与非有限数");
        var combinedNet = Bjt(false, 50, 33000, 10000);
        ReverseCap(combinedNet, "C3");
        var combined = Solve(combinedNet);
        CheckResult.Check(!combined.IsSuccess && combined.SummaryText.Contains("饱和区") &&
            combined.SummaryText.Contains("C3 极性反接") && combined.SummaryText.Contains("2 项错误"),
            "截图双错误：饱和与 C3 反接同时列出", combined.SummaryText);
        CheckResult.Check(combined.AcParameters == null && combined.Steps.All(s => s.Phase == SolutionPhase.DcEquivalent),
            "多个错误不生成交流结果");
        // The invalid active-region collector candidate is negative; it must
        // never become evidence for a false reversed output-capacitor diagnosis.
        SetPolarCap(combinedNet, "C2", 3, 4);
        var dependent = Solve(combinedNet);
        CheckResult.Check(!dependent.SummaryText.Contains("C2 极性反接") &&
            dependent.Steps.Any(s => s.Title.Contains("C2") && s.Status == "Warning"),
            "工作点失效时不拿负集电极试算值误判输出电容", dependent.SummaryText);
        SetPolarCap(combinedNet, "CX", 0, 1);
        var triple = Solve(combinedNet);
        CheckResult.Check(triple.SummaryText.Contains("3 项错误") && triple.SummaryText.Contains("C3 极性反接") &&
            triple.SummaryText.Contains("CX 极性反接"), "三项独立错误全部汇总", triple.SummaryText);
        var twoCaps = Bjt(false, 20, 100000, 10000);
        ReverseCap(twoCaps, "C3");
        SetPolarCap(twoCaps, "C2", 4, 3);
        var twoCapsResult = Solve(twoCaps);
        CheckResult.Check(!twoCapsResult.IsSuccess && twoCapsResult.SummaryText.Contains("2 项错误") &&
            twoCapsResult.SummaryText.Contains("C3 极性反接") && twoCapsResult.SummaryText.Contains("C2 极性反接"),
            "放大区偏置下两只反接电容全部列出", twoCapsResult.SummaryText);
        CheckResult.Check(twoCapsResult.Steps.Count(s => s.Status == "Error") == 2 &&
            twoCapsResult.ToAnalysisResult().SummaryText.Contains("\n2."),
            "底部绑定结果保留多条编号错误和独立诊断步骤");
        foreach (bool pnp in new[] { false, true })
        {
            string name = pnp ? "PNP" : "NPN";
            var saturated = Solve(Bjt(pnp, 50, 33000, 10000));
            CheckResult.Check(!saturated.IsSuccess && saturated.SummaryText.Contains("饱和区") &&
                !saturated.SummaryText.Contains("截止区"), name + " 截图参数射极直连电源轨应为饱和", saturated.SummaryText);
            CheckResult.Check(saturated.AcParameters == null, name + " 饱和时不输出放大区增益");
            var reversedSaturatedNet = Bjt(pnp, 50, 33000, 10000);
            ReverseCap(reversedSaturatedNet, "C3");
            var reversedSaturated = Solve(reversedSaturatedNet);
            CheckResult.Check(reversedSaturated.SummaryText.Contains("2 项错误") &&
                reversedSaturated.SummaryText.Contains("C3 极性反接"),
                name + " 镜像电路饱和与反接错误均保留", reversedSaturated.SummaryText);
            var active = Solve(Bjt(pnp, 20, 100000, 10000));
            CheckResult.Check(active.IsSuccess, name + " 无 Re 分压偏置可在放大区求解", active.SummaryText);
            if (active.IsSuccess)
            {
                // KCL: (12-.7)/100k - .7/10k = 43uA; Ic=.86mA; Vce=11.14V.
                // Teaching chain: Uth=1.09V, Rth=9.09k, Ib=.39/9090 ->42.9uA.
                CheckResult.Near(name + " 无 Re 的教学舍入基极电流", 42.9e-6,
                    TestQuantity.Parse(active.QPoint?.Ibq), 1e-9);
                CheckResult.Near(name + " 无 Re 的未舍入加载基极电流", 43e-6,
                    TestQuantity.Parse(active.QPoint?.ExactIbq), 1e-9);
                CheckResult.Near(name + " 无 Re 的管压降", 11.14,
                    TestQuantity.Parse(active.QPoint?.Vceq), 0.001);
                CheckResult.Check(!active.Steps[0].LatexFormula.Contains(@"\frac{U_B-V_{BE}}{R_E}"),
                    name + " 推导不再除以零射极电阻");
            }
            var cutoff = Solve(Bjt(pnp, 50, 1000000, 1000));
            CheckResult.Check(!cutoff.IsSuccess && cutoff.SummaryText.Contains("截止区"),
                name + " 真实欠偏置应为截止", cutoff.SummaryText);
            var invalid = Solve(Bjt(pnp, double.PositiveInfinity, 33000, 10000));
            CheckResult.Check(!invalid.IsSuccess && !invalid.SummaryText.Contains("截止区"),
                name + " 非有限 beta 不得诊断成截止", invalid.SummaryText);
        }
        // Active candidate VCE=.5V (>old .2V guard) still forward-biases BC.
        // Ib=43uA; beta chosen so Ic=11.5mA.
        var boundary = Solve(Bjt(false, .0115 / .000043, 100000, 10000));
        CheckResult.Check(!boundary.IsSuccess && boundary.SummaryText.Contains("集电结"),
            "BJT 集电结正偏不能仅凭 VCE>.2V 判放大区", boundary.SummaryText);
        foreach (bool pChannel in new[] { false, true })
        {
            var rounded = Solve(Fet(pChannel, 1.001, 1000));
            CheckResult.Check(!rounded.IsSuccess && rounded.SummaryText.Contains("舍入") &&
                !rounded.SummaryText.Contains("处于截止区"), "MOS 微小正过驱动不能被舍入判截止", rounded.SummaryText);
            var edge = Solve(Fet(pChannel, 2, 11000)); // Vov=1, ID=1mA, VDS=1
            CheckResult.Check(edge.IsSuccess, "MOS VDS=Vov 饱和边界允许求解", edge.SummaryText);
            if (edge.IsSuccess)
            {
                var vals = edge.Steps[0];
                CheckResult.Near("MOS 独立负载线电流", .001,
                    BjtVdEstimateChecks.StepValue(vals, "ID_physical_A"), 1e-10);
                CheckResult.Near("MOS 独立负载线管压降", 1,
                    BjtVdEstimateChecks.StepValue(vals, "VDS_physical_V"), 1e-8);
            }
            var triode = Solve(Fet(pChannel, 2, 20000));
            CheckResult.Check(!triode.IsSuccess && triode.SummaryText.Contains("三极管区") &&
                triode.SummaryText.Contains("负载线"), "MOS 非饱和时以分段模型及负载线判定", triode.SummaryText);
        }
        var mos = MosfetModel.FromElement(new CircuitElement { Type=ElementType.MosfetNEnh,
            DisplayNumber=1, MosfetK=.001 }, new AnalysisParameters());
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            CheckResult.Check(double.IsNaN(mos.DrainCurrentMagnitude(bad, 2)), "FET 非有限输入不伪造零漏极电流");
            CheckResult.Check(mos.Region(bad, 2).ToString() == "Invalid", "FET 非有限过驱动不属于物理工作区");
            CheckResult.Check(mos.Region(1, bad).ToString() == "Invalid", "FET 非有限漏源电压不属于物理工作区");
            var diode = DiodeModel.FromElement(new CircuitElement { Type=ElementType.Diode }, new AnalysisParameters());
            CheckResult.Check(double.IsNaN(diode.PiecewiseLinearCurrent(bad, 1000)), "二极管非有限输入不伪造零电流");
            CheckResult.Check(diode.Region(bad).ToString() == "Invalid", "二极管非有限电压不属于导通或截止");
        }
        CheckResult.Check(mos.Region(1, 1) == MosfetRegion.Saturation, "FET 饱和边界 VDS=Vov 一致归属饱和");
        var openDiode = DiodeModel.FromElement(new CircuitElement { Type=ElementType.Diode }, new AnalysisParameters());
        CheckResult.Check(openDiode.PiecewiseLinearCurrent(.8, double.PositiveInfinity) == 0,
            "二极管显式开路电阻仍给出零电流");
    }

    private static PedagogicalSolution Solve(ElectricalNetlist net) =>
        TopologyClassifier.Classify(net).Strategy.SolveAsync(net, new AnalysisParameters()).GetAwaiter().GetResult();

    private static void ReverseCap(ElectricalNetlist net, string id)
    {
        int plus = net.FindNetForPin(id, "+")!.NetId, minus = net.FindNetForPin(id, "-")!.NetId;
        SetPolarCap(net, id, minus, plus);
    }

    private static void SetPolarCap(ElectricalNetlist net, string id, int plus, int minus)
    {
        if (!net.Elements.TryGetValue(id, out var cap))
        {
            cap = new CircuitElement { Id=id, Name=id, DisplayNumber=10, UnitSymbol="μF" };
            net.Elements[id] = cap;
        }
        cap.Type = ElementType.CapacitorPolarized;
        cap.InitializeDefaultPins();
        foreach (var n in net.Nets) n.ConnectedPins.RemoveAll(p => p.ElementId == id);
        net.Nets.Single(n => n.NetId == plus).ConnectedPins.Add(new ComponentPinRef(id, "+", "+"));
        net.Nets.Single(n => n.NetId == minus).ConnectedPins.Add(new ComponentPinRef(id, "-", "-"));
    }

    private static ElectricalNetlist Fet(bool pChannel, double drive, double rd)
    {
        double gate = pChannel ? 12-drive : drive;
        int source = pChannel ? 1 : 0, rail = pChannel ? 0 : 1;
        return new TestNetlistBuilder()
            .Element("V", ElementType.VccPort, 12, "V").Element("G", ElementType.Ground, 0)
            .Element("M", pChannel ? ElementType.MosfetPEnh : ElementType.MosfetNEnh, pChannel ? -1 : 1, "V")
            .Element("RT", ElementType.Resistor, (12-gate)*10000, "Ω")
            .Element("RB", ElementType.Resistor, gate*10000, "Ω")
            .Element("RD", ElementType.Resistor, rd, "Ω")
            .Pin("V", "VCC", 1).Pin("G", "GND", 0)
            .Pin("M", "G", 2).Pin("M", "S", source).Pin("M", "D", 3)
            .Pin("RT", "1", 1).Pin("RT", "2", 2)
            .Pin("RB", "1", 2).Pin("RB", "2", 0)
            .Pin("RD", "1", rail).Pin("RD", "2", 3)
            .Net(0, "GND", isGround:true).Net(1, "VCC", isPower:true).Build();
    }

    private static ElectricalNetlist Bjt(bool pnp, double beta, double driveR, double railR)
    {
        // Mirror every terminal, including the bias divider, about VCC/2.
        int e = pnp ? 1 : 0, supply = pnp ? 0 : 1;
        return new TestNetlistBuilder()
            .Element("V", ElementType.VccPort, 12, "V").Element("G", ElementType.Ground, 0)
            .Element("Q", pnp ? ElementType.BjtPnp : ElementType.BjtNpn, beta, "β")
            .Element("RD", ElementType.Resistor, driveR, "Ω")
            .Element("RB", ElementType.Resistor, railR, "Ω")
            .Element("RC", ElementType.Resistor, 1000, "Ω")
            .Element("C2", ElementType.Capacitor, 10, "μF")
            .Element("RL", ElementType.Resistor, 1000, "Ω")
            .Element("C3", ElementType.CapacitorPolarized, 47, "μF")
            .Element("R6", ElementType.Resistor, 50, "Ω")
            .Element("VS", ElementType.AcVoltageSource, 10, "mV")
            .Pin("C2", "1", 3).Pin("C2", "2", 4)
            .Pin("RL", "1", 4).Pin("RL", "2", 0)
            .Pin("C3", "+", 2).Pin("C3", "-", 5)
            .Pin("R6", "1", 5).Pin("R6", "2", 6)
            .Pin("VS", "pos", 6).Pin("VS", "neg", 0)
            .Pin("V", "VCC", 1).Pin("G", "GND", 0)
            .Pin("Q", "E", e).Pin("Q", "B", 2).Pin("Q", "C", 3)
            .Pin("RD", "1", supply).Pin("RD", "2", 2)
            .Pin("RB", "1", 2).Pin("RB", "2", e)
            .Pin("RC", "1", supply).Pin("RC", "2", 3)
            .Net(0, "GND", isGround:true).Net(1, "VCC", isPower:true).Build();
    }
}

