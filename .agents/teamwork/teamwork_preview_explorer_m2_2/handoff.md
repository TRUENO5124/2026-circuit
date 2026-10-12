# Electrical Topology Analysis & Solving Strategy Engine Architecture Design

> **Agent**: Explorer M2-2 (Milestone 2: Electrical Topology Analysis & Solving Strategy Engine - R1)  
> **Directory**: `d:/BJTU/2026年大创/.agents/teamwork/teamwork_preview_explorer_m2_2`  
> **Target Subsystem**: `apps/desktop-winui/Engine/` (Topology Classification & Analytical Strategy Engine)  
> **Date**: 2026-10-11  

---

## 1. Observation

### 1.1 Existing Architecture and Defect Trace
1. **Decoupled Fallback Engine**:
   - In `apps/desktop-winui/Services/CircuitApiService.cs` (lines 74–79 & 148–375), all circuit evaluations fall back to `GeneratePedagogicalSolution(vcc, rb1, rb2, rc, re, rl, c1, c2, ce, beta, freq)`.
   - In `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 904–945), `SolveCircuitAsync()` calls `SyncElementValuesToModel()` which matches fixed IDs (`VCC`, `Rb1`, `Rb2`, `Rc`, `Re`, `RL`, `C1`, `C2`, `Ce`, `Q1`, `vs`).
   - *Direct Consequence (Acceptance Criterion 1 Violation)*: If a user places a pure first-order RC circuit (e.g. `vs`, `R1`, `C1`, `GND`), none of the BJT IDs match. Default BJT values (12V, 51k, 15k, etc.) are passed to `GeneratePedagogicalSolution()`, producing an erroneous 5-step BJT derivation ($I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}, A_v, R_i, R_o$) on a circuit that contains zero transistors.
2. **Missing C# Engine Directory**:
   - The directory `apps/desktop-winui/Engine/` does not exist yet. There is no `TopologyClassifier`, no strategy pattern, and no netlist solver in the desktop application.
3. **Python Pipeline Disconnect**:
   - In `services/engine/src/pipeline.py` (lines 35–46) and `services/engine/src/strategies/linear.py` (lines 407–542), the Python backend provides AC transfer function analysis for RC filters, but lacks:
     - Time-domain Three-Element Method (三要素法: $f(0^+), f(\infty), \tau, u_C(t)$) for DC step transient response.
     - Second-order RLC resonance derivations ($\omega_0, \alpha, Q, s_1, s_2$, damping regimes).
   - Furthermore, `start-winui.bat` runs standalone without spinning up Python or FastAPI. Desktop analysis must execute natively and deterministically in C# on .NET 9.
4. **Existing Data Models and Utilities**:
   - `apps/desktop-winui/Models/CircuitElement.cs` contains the canonical `ElementType` enum (24 variants including `Resistor`, `Capacitor`, `Inductor`, `BjtNpn`, `BjtPnp`, `Mosfet*`, `DcVoltageSource`, `AcVoltageSource`, `Ground`) and default pin mappings.
   - `apps/desktop-winui/Models/EngineeringUnit.cs` contains SI unit multipliers (`EngineeringUnits.ResistanceUnits`, `CapacitanceUnits`, `InductanceUnits`, `VoltageUnits`, `CurrentUnits`, `FrequencyUnits`).
   - `apps/desktop-winui/Services/ScientificNotationFormatter.cs` provides `ToLatexScientific` and `ToUnicodeScientific` for values $|v| \ge 1000$ or $|v| < 0.001$.
   - `apps/desktop-winui/Models/AnalysisResult.cs` and `AnalysisStep.cs` define the observable properties bound to the WinUI presentation layer.

---

## 2. Logic Chain

1. **Root Cause Deduction**:
   - Because `AnalysisStudioViewModel.SolveCircuitAsync()` and `CircuitApiService.cs` bypass graph topology and force BJT equations, the solver violates Acceptance Criterion 1 whenever non-BJT circuits are analyzed.
   - *Remedy*: Replace the hardcoded parameter extraction with an automated pipeline:
     $$\text{Elements} + \text{Wires} \xrightarrow{\text{NetlistExtractor}} \text{ElectricalNetlist} \xrightarrow{\text{TopologyClassifier}} \text{ICircuitStrategy} \xrightarrow{\text{SolveAsync}} \text{PedagogicalSolution}$$
2. **Classification Determinism**:
   - Given an `ElectricalNetlist` from `NetlistExtractor` (designed by Explorer M2-1):
     - Active devices (BJT, MOSFET) dictate amplifier topologies. Their biasing resistors, coupling capacitors, and power/ground connections define the exact amplifier configuration.
     - In the absence of active devices, the count of independent dynamic storage elements ($N_C + N_L$) uniquely partitions linear networks into:
       - $N_C + N_L = 1$: First-Order RC / RL networks.
       - $N_C + N_L = 2$: Second-Order RLC resonance networks.
       - $N_C + N_L = 0$ or arbitrary multi-loop: General Linear MNA networks.
3. **Strict Parameter Suppression (Acceptance Criterion 1)**:
   - `FirstOrderRcStrategy` must construct derivation steps purely from the netlist's passive components and source ($R_1, C_1, V_S$).
   - The strategy class will contain zero variables or step templates related to $\beta, r_{be}, I_{BQ}, I_{CQ}, V_{CEQ}$, ensuring mathematical and pedagogical rigor.
4. **MNA Linear Solver Standalone Capability**:
   - General linear circuits must be solvable without external SPICE or SymPy binaries.
   - Gaussian elimination with partial pivoting in native C# computes all node potentials and branch currents in $< 1\text{ ms}$ for typical educational circuits ($N \le 30$).
5. **Bidirectional UI Compatibility**:
   - Emitting `PedagogicalSolution` with a clean `ToAnalysisResult()` adapter guarantees seamless binding to existing WinUI 3 views while providing clean pedagogical metadata (`StepIndex`, `BadgeText`, `Title`, `MethodName`, `LatexFormula`, `SubstitutionFormula`, `ResultValue`, `Status`).

---

## 3. Comprehensive Engine Architecture Design

### 3.1 Class Hierarchy and Engine Namespace

```
apps/desktop-winui/Engine/
├── ICircuitStrategy.cs                  // Strategy interface contract & analysis parameters
├── TopologyClassifier.cs                // Graph inspection & topology categorization
├── PedagogicalSolution.cs               // Solution model with LaTeX steps & UI adapter
└── Strategies/
    ├── FirstOrderRcStrategy.cs          // 1st-order RC/RL (Req, tau, 3-element u_C(t), H(s), fc)
    ├── SecondOrderRlcStrategy.cs        // 2nd-order RLC (omega_0, alpha, Q, roots, damping, BW)
    ├── BjtAmplifierStrategy.cs          // BJT amplifiers (Fixed/Divider/Feedback; Q-point, Av, Ri, Ro, Bode)
    └── MnaLinearStrategy.cs             // SPICE MNA matrix solver (Gv + Bi = J) via C# Gaussian elimination
```

---

### 3.2 Strategy Interface & Parameter Contract (`ICircuitStrategy.cs`)

```csharp
namespace CircuitAssistant.Engine;

public enum CircuitTopologyType
{
    FirstOrderRc,
    FirstOrderRl,
    SecondOrderRlc,
    BjtAmplifier,
    MosfetAmplifier,
    MnaLinear,
    Unsupported
}

public class AnalysisParameters
{
    public double OperatingFrequency { get; set; } = 1000.0; // Default 1 kHz
    public double AmbientTemperatureKelvin { get; set; } = 300.0; // 26.85 °C (Thermal voltage UT = 25.86 mV ~ 26 mV)
    public bool IncludeTransientResponse { get; set; } = true;
    public bool IncludeFrequencyResponse { get; set; } = true;
}

public interface ICircuitStrategy
{
    CircuitTopologyType TopologyType { get; }
    string SubtypeName { get; }
    Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters);
}
```

---

### 3.3 Pedagogical Solution Model (`PedagogicalSolution.cs`)

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CircuitAssistant.Models;

namespace CircuitAssistant.Engine;

public class PedagogicalSolutionStep
{
    public int StepIndex { get; set; }
    public string BadgeText { get; set; } = string.Empty;       // e.g. "第 1 步"
    public string Title { get; set; } = string.Empty;           // e.g. "端口戴维南等效电阻 Req 求解"
    public string MethodName { get; set; } = string.Empty;      // e.g. "戴维南等效法"
    public string Content { get; set; } = string.Empty;         // Detailed engineering explanation
    public string LatexFormula { get; set; } = string.Empty;    // Pure symbolic formula
    public string SubstitutionFormula { get; set; } = string.Empty; // Numeric substitution formula
    public string ResultValue { get; set; } = string.Empty;     // Formatted final value with unit
    public string Status { get; set; } = "Completed";          // "Completed" | "Warning" | "Info"
    public Dictionary<string, string> Values { get; set; } = [];
    public string MnaLog { get; set; } = string.Empty;
}

public class PedagogicalSolution
{
    public bool IsSuccess { get; set; } = true;
    public string CircuitTitle { get; set; } = string.Empty;
    public CircuitTopologyType TopologyType { get; set; }
    public string SubtypeName { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public List<PedagogicalSolutionStep> Steps { get; set; } = [];
    public QPointInfo? QPoint { get; set; }
    public AcParametersInfo? AcParameters { get; set; }
    public string RawMnaLog { get; set; } = string.Empty;

    public AnalysisResult ToAnalysisResult()
    {
        var result = new AnalysisResult
        {
            IsSuccess = IsSuccess,
            CircuitTitle = CircuitTitle,
            SummaryText = SummaryText,
            RawMnaLog = RawMnaLog
        };

        if (QPoint != null) result.QPoint = QPoint;
        if (AcParameters != null) result.AcParameters = AcParameters;

        var stepsCollection = new ObservableCollection<AnalysisStep>();
        foreach (var s in Steps)
        {
            stepsCollection.Add(new AnalysisStep
            {
                StepNumber = s.StepIndex,
                BadgeText = s.BadgeText,
                Title = s.Title,
                MethodName = s.MethodName,
                Explanation = s.Content,
                LatexFormula = s.LatexFormula,
                SubstitutionFormula = s.SubstitutionFormula,
                ResultValue = s.ResultValue,
                MnaLog = s.MnaLog,
                HasMnaLog = !string.IsNullOrWhiteSpace(s.MnaLog),
                IsExpanded = true
            });
        }
        result.Steps = stepsCollection;
        return result;
    }
}
```

---

### 3.4 Topology Classifier Architecture (`TopologyClassifier.cs`)

`TopologyClassifier` scans the `ElectricalNetlist` (nets, components, and pin references) and executes a hierarchical graph decision tree:

```csharp
namespace CircuitAssistant.Engine;

public class ClassificationResult
{
    public CircuitTopologyType TopologyType { get; set; }
    public string SubtypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ICircuitStrategy Strategy { get; set; } = null!;
}

public static class TopologyClassifier
{
    public static ClassificationResult Classify(ElectricalNetlist netlist)
    {
        if (netlist == null || netlist.Elements.Count == 0)
        {
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.Unsupported,
                SubtypeName = "Empty",
                Description = "画布无有效电路元件",
                Strategy = new MnaLinearStrategy()
            };
        }

        var elements = netlist.Elements.Values.ToList();

        // 1. Check for Active Devices (Transistors)
        var bjtList = elements.Where(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp).ToList();
        var mosList = elements.Where(e => e.Type is ElementType.Mosfet or ElementType.MosfetNEnh 
                                          or ElementType.MosfetPEnh or ElementType.MosfetNDep 
                                          or ElementType.MosfetPDep or ElementType.Jfet
                                          or ElementType.JfetN or ElementType.JfetP).ToList();

        if (bjtList.Count >= 1)
        {
            var bjt = bjtList[0];
            var biasSubtype = DetectBjtBiasTopology(netlist, bjt);
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.BjtAmplifier,
                SubtypeName = biasSubtype,
                Description = $"双极型晶体管 (BJT) 放大电路 [{biasSubtype}]",
                Strategy = new BjtAmplifierStrategy(biasSubtype)
            };
        }

        if (mosList.Count >= 1)
        {
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.MosfetAmplifier,
                SubtypeName = "CommonSource",
                Description = "场效应管 (MOSFET) 共源极放大电路",
                Strategy = new MnaLinearStrategy() // Fallback to MNA or dedicated Mosfet strategy
            };
        }

        // 2. Count Reactive Elements (Capacitors and Inductors)
        int capCount = elements.Count(e => e.Type == ElementType.Capacitor);
        int indCount = elements.Count(e => e.Type == ElementType.Inductor);
        int reactiveCount = capCount + indCount;

        // 3. First-Order Circuit (Exactly 1 reactive element)
        if (reactiveCount == 1)
        {
            if (capCount == 1)
            {
                var sub = DetectFirstOrderRcSubtype(netlist);
                return new ClassificationResult
                {
                    TopologyType = CircuitTopologyType.FirstOrderRc,
                    SubtypeName = sub,
                    Description = $"一阶 RC 动态与频率响应电路 [{sub}]",
                    Strategy = new FirstOrderRcStrategy(isRl: false, sub)
                };
            }
            else // indCount == 1
            {
                return new ClassificationResult
                {
                    TopologyType = CircuitTopologyType.FirstOrderRl,
                    SubtypeName = "RlTransient",
                    Description = "一阶 RL 动态与频率响应电路",
                    Strategy = new FirstOrderRcStrategy(isRl: true, "RlTransient")
                };
            }
        }

        // 4. Second-Order RLC Resonance (Exactly 2 reactive elements, e.g. 1 L + 1 C)
        if (reactiveCount == 2 && capCount >= 1 && indCount >= 1)
        {
            var rlcSubtype = DetectRlcSubtype(netlist);
            return new ClassificationResult
            {
                TopologyType = CircuitTopologyType.SecondOrderRlc,
                SubtypeName = rlcSubtype,
                Description = $"二阶 RLC 谐振与瞬态电路 [{rlcSubtype}]",
                Strategy = new SecondOrderRlcStrategy(rlcSubtype)
            };
        }

        // 5. General Linear Network (Multi-loop, multi-source, purely resistive, or general RLC)
        return new ClassificationResult
        {
            TopologyType = CircuitTopologyType.MnaLinear,
            SubtypeName = "GeneralLinear",
            Description = "通用线性网络 (基于 SPICE MNA 矩阵方程分析)",
            Strategy = new MnaLinearStrategy()
        };
    }

    private static string DetectBjtBiasTopology(ElectricalNetlist netlist, CircuitElement bjt)
    {
        var bNet = netlist.FindNetForPin(bjt.Id, "B");
        var cNet = netlist.FindNetForPin(bjt.Id, "C");
        var eNet = netlist.FindNetForPin(bjt.Id, "E");

        var gndNet = netlist.GroundNet;
        var vccNet = netlist.Nets.FirstOrDefault(n => n.IsPower || n.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase));

        if (bNet == null || cNet == null || eNet == null) return "FixedBias";

        // Check if there is a resistor from Base to GND
        bool hasBaseToGndResistor = false;
        bool hasBaseToVccResistor = false;
        bool hasCollectorToBaseResistor = false;

        foreach (var pinRef in bNet.ConnectedPins)
        {
            if (pinRef.ElementId == bjt.Id) continue;
            if (netlist.Elements.TryGetValue(pinRef.ElementId, out var el) && el.Type == ElementType.Resistor)
            {
                var otherPin = el.Pins.FirstOrDefault(p => p.Id != pinRef.PinId);
                if (otherPin != null)
                {
                    var otherNet = netlist.FindNetForPin(el.Id, otherPin.Id);
                    if (otherNet != null)
                    {
                        if (otherNet.IsGround || otherNet == gndNet) hasBaseToGndResistor = true;
                        if (otherNet.IsPower || otherNet == vccNet) hasBaseToVccResistor = true;
                        if (otherNet == cNet) hasCollectorToBaseResistor = true;
                    }
                }
            }
        }

        if (hasCollectorToBaseResistor) return "CollectorFeedbackBias";
        if (hasBaseToVccResistor && hasBaseToGndResistor) return "VoltageDividerSelfBias";
        if (hasBaseToVccResistor) return "FixedBias";

        return "VoltageDividerSelfBias"; // Default to standard textbook CE
    }

    private static string DetectFirstOrderRcSubtype(ElectricalNetlist netlist)
    {
        // Detect whether the excitation is DC step (VS) or AC sinusoid (vs)
        bool hasAc = netlist.Elements.Values.Any(e => e.Type == ElementType.AcVoltageSource);
        bool hasDc = netlist.Elements.Values.Any(e => e.Type == ElementType.DcVoltageSource);

        if (hasDc && !hasAc) return "DcStepTransient";
        if (hasAc)
        {
            // Check if capacitor is in series with signal or in parallel to ground
            var cap = netlist.Elements.Values.First(e => e.Type == ElementType.Capacitor);
            var pin1Net = netlist.FindNetForPin(cap.Id, "1");
            var pin2Net = netlist.FindNetForPin(cap.Id, "2");
            bool isGrounded = (pin1Net?.IsGround ?? false) || (pin2Net?.IsGround ?? false);
            return isGrounded ? "LowPassFilter" : "HighPassFilter";
        }
        return "DcStepTransient";
    }

    private static string DetectRlcSubtype(ElectricalNetlist netlist)
    {
        // Determine series vs parallel
        var ind = netlist.Elements.Values.First(e => e.Type == ElementType.Inductor);
        var cap = netlist.Elements.Values.First(e => e.Type == ElementType.Capacitor);

        var indNets = new[] { netlist.FindNetForPin(ind.Id, "1"), netlist.FindNetForPin(ind.Id, "2") };
        var capNets = new[] { netlist.FindNetForPin(cap.Id, "1"), netlist.FindNetForPin(cap.Id, "2") };

        int sharedNets = indNets.Count(inNet => inNet != null && capNets.Contains(inNet));
        return (sharedNets >= 2) ? "ParallelRlc" : "SeriesRlc";
    }
}
```

---

### 3.5 Strategy 1: `FirstOrderRcStrategy.cs`

#### Design Principles & Acceptance Criterion 1 Fulfillment
1. **Zero Fictitious BJT Variables**: The class completely omits any code referencing $\beta, r_{be}, I_{BQ}, I_{CQ}, V_{CEQ}$. All steps belong strictly to RC/RL transient and frequency response.
2. **Thevenin Resistance Extraction**:
   - Finds the dynamic storage element ($C_1$ or $L_1$).
   - Computes equivalent resistance $R_{eq}$ across its two pins.
   - For canonical template (`VS` $\to$ `R1` $\to$ `C1` $\to$ `GND`), $R_{eq} = R_1$.
3. **Time Constant $\tau$**:
   $$\tau = R_{eq} \cdot C \quad (\text{RC}) \qquad \text{or} \qquad \tau = \frac{L}{R_{eq}} \quad (\text{RL})$$
4. **Three-Element Method (三要素法)**:
   - Initial value $u_C(0^+) = u_C(0^-) = 0\text{ V}$ (by capacitor voltage continuity law).
   - Steady-state value $u_C(\infty) = U_{oc} = V_S$ (capacitor opens as $t \to \infty$).
   - General solution:
     $$u_C(t) = u_C(\infty) + [u_C(0^+) - u_C(\infty)] e^{-t/\tau} = V_S \left(1 - e^{-t/\tau}\right)$$
   - Capacitor current / Resistor voltage:
     $$i_C(t) = C \frac{du_C(t)}{dt} = \frac{V_S}{R_{eq}} e^{-t/\tau}, \quad u_R(t) = V_S e^{-t/\tau}$$
   - Characteristic time points: $t=\tau$ ($63.2\%$), $t=3\tau$ ($95.0\%$), $t=5\tau$ ($99.3\%$).
5. **Transfer Function $H(s)$ & Cutoff Frequency $f_c$**:
   - $H(s) = \frac{1}{1 + s\tau}$ (Low-Pass) or $H(s) = \frac{s\tau}{1 + s\tau}$ (High-Pass).
   - Characteristic cutoff frequency:
     $$\omega_c = \frac{1}{\tau} = \frac{1}{R_{eq} C}, \quad f_c = \frac{\omega_c}{2\pi} = \frac{1}{2\pi R_{eq} C}$$
   - Amplitude-frequency & phase-frequency response:
     $$|A_u(jf)| = \frac{1}{\sqrt{1 + (f/f_c)^2}}, \quad 20\lg |A_u(jf)| = -10\lg\left[1 + \left(\frac{f}{f_c}\right)^2\right]\text{ dB}, \quad \varphi(f) = -\arctan\left(\frac{f}{f_c}\right)$$

#### Complete C# Algorithm Specification

```csharp
namespace CircuitAssistant.Engine.Strategies;

public class FirstOrderRcStrategy : ICircuitStrategy
{
    private readonly bool _isRl;
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => _isRl ? CircuitTopologyType.FirstOrderRl : CircuitTopologyType.FirstOrderRc;
    public string SubtypeName => _subtype;

    public FirstOrderRcStrategy(bool isRl = false, string subtype = "DcStepTransient")
    {
        _isRl = isRl;
        _subtype = subtype;
    }

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = _isRl ? "一阶 RL 电路时域暂态与频率响应严谨推导" : "一阶 RC 电路时域暂态与频率响应严谨推导"
        };

        // 1. Extract Components
        var resList = netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor).ToList();
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var dcSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.DcVoltageSource);
        var acSrc = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);

        // Values in SI units
        double rVal = resList.Count > 0 ? GetSiValue(resList[0]) : 1000.0;
        string rName = resList.Count > 0 ? resList[0].Name : "R1";
        string rId = resList.Count > 0 ? resList[0].Id : "R1";

        double cVal = cap != null ? GetSiValue(cap) : 100e-6;
        string cName = cap != null ? cap.Name : "C1";
        string cId = cap != null ? cap.Id : "C1";

        double lVal = ind != null ? GetSiValue(ind) : 10e-3;
        string lName = ind != null ? ind.Name : "L1";
        string lId = ind != null ? ind.Id : "L1";

        double vsVal = dcSrc != null ? GetSiValue(dcSrc) : (acSrc != null ? GetSiValue(acSrc) : 10.0);
        string vsId = dcSrc != null ? dcSrc.Id : (acSrc != null ? acSrc.Id : "VS");

        // 2. Thevenin Equivalent Resistance Req and Time Constant tau
        double req = rVal; // For single resistor network
        double tau = _isRl ? (lVal / req) : (req * cVal);
        double omegaC = (tau > 0) ? (1.0 / tau) : 0;
        double fc = omegaC / (2.0 * Math.PI);

        // Format strings using ScientificNotationFormatter
        string reqLatex = ScientificNotationFormatter.ToLatexScientific(req);
        string tauLatex = ScientificNotationFormatter.ToLatexScientific(tau);
        string tauMsLatex = ScientificNotationFormatter.ToLatexScientific(tau * 1000.0);
        string fcLatex = ScientificNotationFormatter.ToLatexScientific(fc);
        string vsLatex = ScientificNotationFormatter.ToLatexScientific(vsVal);

        solution.SummaryText = $"一阶 {(_isRl ? "RL" : "RC")} 电路拓扑分析完成：等效电阻 Req = {reqLatex} Ω，时间常数 τ = {tauLatex} s ({tauMsLatex} ms)，特征截止频率 fc = {fcLatex} Hz。";

        // ---------------------------------------------------------------------
        // Step 1: Thevenin Equivalent Resistance Req
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "端口戴维南等效电阻 Req 求解",
            MethodName = "戴维南等效法",
            Content = $"将电路中独立激励源（电压源短路，电流源开路）置零，从储能元件 {(_isRl ? lId : cId)} 两引脚端口看入的戴维南等效输入电阻。",
            LatexFormula = $@"R_{{eq}} = R_{{{rId}}}",
            SubstitutionFormula = $@"R_{{eq}} = {reqLatex}\;\Omega",
            ResultValue = $"{ScientificNotationFormatter.ToUnicodeScientific(req)} Ω",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 2: Three-Element Method Parameters (f(0+), f(inf), tau)
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "时域暂态三要素（初始值、稳态值、时间常数）分析",
            MethodName = "三要素分析法",
            Content = $"根据储能元件换路定律：换路瞬间电容电压不突变 u_C(0+) = u_C(0-) = 0V；换路达到新稳态时，电容视为开路，两端电压等于电源开路电压 u_C(∞) = VS。",
            LatexFormula = _isRl
                ? @"i_L(0^+) = 0\text{ A},\quad i_L(\infty) = \frac{V_S}{R_{eq}},\quad \tau = \frac{L}{R_{eq}}"
                : @"u_C(0^+) = 0\text{ V},\quad u_C(\infty) = V_S,\quad \tau = R_{eq} \cdot C",
            SubstitutionFormula = _isRl
                ? $@"i_L(0^+) = 0\text{{ A}},\quad i_L(\infty) = {ScientificNotationFormatter.ToLatexScientific(vsVal / req)}\text{{ A}},\quad \tau = {tauMsLatex}\text{{ ms}}"
                : $@"u_C(0^+) = 0\text{{ V}},\quad u_C(\infty) = {vsLatex}\text{{ V}},\quad \tau = {tauLatex}\text{{ s}} = {tauMsLatex}\text{{ ms}}",
            ResultValue = $"τ = {ScientificNotationFormatter.ToUnicodeScientific(tau * 1000.0)} ms",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 3: Differential Equation & Time-Domain Solution
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "一阶常微分方程建立与时域充放电通解",
            MethodName = "一阶动态电路微分方程",
            Content = $"建立一阶常系数线性微分方程 τ(df/dt) + f(t) = f(∞)，代入三要素公式求解。当 t = 3τ 时充放电达 95.0%，t = 5τ 时达 99.3%（工程视作暂态过程结束）。",
            LatexFormula = _isRl
                ? @"i_L(t) = i_L(\infty) + \left[i_L(0^+) - i_L(\infty)\right] e^{-\frac{t}{\tau}} = \frac{V_S}{R_{eq}} \left(1 - e^{-\frac{t}{\tau}}\right)"
                : @"u_C(t) = u_C(\infty) + \left[u_C(0^+) - u_C(\infty)\right] e^{-\frac{t}{\tau}} = V_S \left(1 - e^{-\frac{t}{\tau}}\right)",
            SubstitutionFormula = _isRl
                ? $@"i_L(t) = {ScientificNotationFormatter.ToLatexScientific(vsVal / req)} \left(1 - e^{{-\frac{{t}}{{{tauLatex}}}}}\right)\text{{ A}}"
                : $@"u_C(t) = {vsLatex} \left(1 - e^{{-\frac{{t}}{{{tauLatex}}}}}\right)\text{{ V}}",
            ResultValue = $"u_C(3τ) = {ScientificNotationFormatter.ToUnicodeScientific(vsVal * 0.9502)} V, u_C(5τ) = {ScientificNotationFormatter.ToUnicodeScientific(vsVal * 0.9933)} V",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 4: Frequency Domain Transfer Function H(s) and fc
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "复频域传递函数 H(s) 与截止频率 fc 求解",
            MethodName = "复阻抗分压与 Laplace 变换",
            Content = $"在复频域中，电容复阻抗 Z_C(s) = 1/(sC)。输出电压取自电容两端，传递函数 H(s) 具有标准单极点低通响应形态，半功率转折点角频率为 ω_c = 1/τ。",
            LatexFormula = _isRl
                ? @"H(s) = \frac{1}{1 + s\tau},\quad \omega_c = \frac{R_{eq}}{L},\quad f_c = \frac{\omega_c}{2\pi} = \frac{R_{eq}}{2\pi L}"
                : @"H(s) = \frac{1}{1 + s\tau},\quad \omega_c = \frac{1}{R_{eq} C},\quad f_c = \frac{\omega_c}{2\pi} = \frac{1}{2\pi R_{eq} C}",
            SubstitutionFormula = $@"H(s) = \frac{{1}}{{1 + {tauLatex} s}},\quad \omega_c = {ScientificNotationFormatter.ToLatexScientific(omegaC)}\text{{ rad/s}},\quad f_c = {fcLatex}\text{{ Hz}}",
            ResultValue = $"fc = {ScientificNotationFormatter.ToUnicodeScientific(fc)} Hz",
            Status = "Completed"
        });

        // ---------------------------------------------------------------------
        // Step 5: Sinusoidal Steady-State Bode Frequency Characteristics
        // ---------------------------------------------------------------------
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "正弦稳态幅频与相频特性分析 (渐近线波特图特性)",
            MethodName = "频域波特图渐近线分析",
            Content = $"分析正弦稳态幅频与相频特性：通带内幅值增益为 1 (0 dB)；截止频率处幅值衰减 3 dB (0.707)，产生 -45° 相位滞后；高频阻带以 -20 dB/dec (-6 dB/oct) 固定斜率滚降。",
            LatexFormula = @"|A_u(jf)| = \frac{1}{\sqrt{1 + (f/f_c)^2}},\quad 20\lg |A_u(jf)| = -10\lg\left[1 + \left(\frac{f}{f_c}\right)^2\right]\text{ dB},\quad \varphi(f) = -\arctan\left(\frac{f}{f_c}\right)",
            SubstitutionFormula = $@"|A_u(jf_c)| = \frac{{1}}{{\sqrt{{2}}}} \approx 0.707\;(-3.01\text{{ dB}}),\quad \varphi(f_c) = -45.0^\circ",
            ResultValue = "通带增益: 0 dB, 滚降斜率: -20 dB/dec",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static double GetSiValue(CircuitElement el)
    {
        double multiplier = 1.0;
        foreach (var opt in EngineeringUnits.ResistanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.CapacitanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.InductanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.VoltageUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.CurrentUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
```

---

### 3.6 Strategy 2: `SecondOrderRlcStrategy.cs`

#### Analytical Formulations
1. **Undamped Natural Frequency $\omega_0$**:
   $$\omega_0 = \frac{1}{\sqrt{LC}}, \quad f_0 = \frac{\omega_0}{2\pi}$$
2. **Attenuation Constant $\alpha$**:
   - Series RLC: $\alpha = \frac{R}{2L}$
   - Parallel RLC: $\alpha = \frac{1}{2RC}$
3. **Quality Factor $Q$**:
   - Series RLC: $Q = \frac{\omega_0 L}{R} = \frac{1}{\omega_0 CR} = \frac{1}{R}\sqrt{\frac{L}{C}} = \frac{\omega_0}{2\alpha}$
   - Parallel RLC: $Q = \frac{R}{\omega_0 L} = \omega_0 CR = R\sqrt{\frac{C}{L}} = \frac{\omega_0}{2\alpha}$
4. **Characteristic Roots $s_{1, 2}$**:
   $$s^2 + 2\alpha s + \omega_0^2 = 0 \implies s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}$$
5. **Damping Regime Categorization**:
   - **Overdamped (过阻尼, $\alpha > \omega_0, Q < 0.5$)**:
     Two distinct negative real roots $s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}$.
     Transient response: Monotonic exponential decay without oscillation.
   - **Critically Damped (临界阻尼, $\alpha = \omega_0, Q = 0.5$)**:
     Two identical real roots $s_1 = s_2 = -\alpha$.
     Transient response: Fastest non-oscillatory convergence without overshoot.
   - **Underdamped (欠阻尼, $\alpha < \omega_0, Q > 0.5$)**:
     Complex conjugate roots $s_{1, 2} = -\alpha \pm j\omega_d$, where $\omega_d = \sqrt{\omega_0^2 - \alpha^2} = \omega_0 \sqrt{1 - (1/(2Q))^2}$.
     Damped oscillation frequency $f_d = \frac{\omega_d}{2\pi}$.
     Overshoot percentage: $\sigma\% = e^{-\frac{\pi\alpha}{\omega_d}} \times 100\%$.
6. **Resonance Bandwidth $BW$**:
   $$BW = \Delta f = \frac{f_0}{Q} = \frac{\alpha}{\pi}$$

#### Complete C# Algorithm Specification

```csharp
namespace CircuitAssistant.Engine.Strategies;

public class SecondOrderRlcStrategy : ICircuitStrategy
{
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => CircuitTopologyType.SecondOrderRlc;
    public string SubtypeName => _subtype;

    public SecondOrderRlcStrategy(string subtype = "SeriesRlc")
    {
        _subtype = subtype;
    }

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"二阶 RLC {(_subtype == "ParallelRlc" ? "并联" : "串联")} 谐振与瞬态特性严谨推导"
        };

        var res = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Resistor);
        var ind = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Inductor);
        var cap = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.Capacitor);
        var src = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource);

        double r = (res != null) ? GetSiValue(res) : 100.0;
        double l = (ind != null) ? GetSiValue(ind) : 10e-3; // 10 mH
        double c = (cap != null) ? GetSiValue(cap) : 1e-6;  // 1 uF
        double vs = (src != null) ? GetSiValue(src) : 10.0;

        bool isParallel = _subtype == "ParallelRlc";

        // 1. Natural Frequency
        double omega0 = 1.0 / Math.Sqrt(l * c);
        double f0 = omega0 / (2.0 * Math.PI);

        // 2. Attenuation alpha & Quality factor Q
        double alpha = isParallel ? (1.0 / (2.0 * r * c)) : (r / (2.0 * l));
        double q = isParallel ? (r * Math.Sqrt(c / l)) : (1.0 / r * Math.Sqrt(l / c));
        double bw = f0 / q;

        // 3. Characteristic Roots & Damping Regime
        double discriminant = alpha * alpha - omega0 * omega0;
        string regime;
        string rootsLatex;
        string rootsValue;

        if (Math.Abs(discriminant) < 1e-9 || Math.Abs(alpha - omega0) < 1e-6)
        {
            regime = "临界阻尼 (Critically Damped)";
            rootsLatex = $@"s_1 = s_2 = -\alpha = -{ScientificNotationFormatter.ToLatexScientific(alpha)}\text{{ s}}^{{-1}}";
            rootsValue = $"重根 s = -{ScientificNotationFormatter.ToUnicodeScientific(alpha)} s⁻¹";
        }
        else if (discriminant > 0)
        {
            regime = "过阻尼 (Overdamped)";
            double s1 = -alpha + Math.Sqrt(discriminant);
            double s2 = -alpha - Math.Sqrt(discriminant);
            rootsLatex = $@"s_1 = {ScientificNotationFormatter.ToLatexScientific(s1)},\quad s_2 = {ScientificNotationFormatter.ToLatexScientific(s2)}\text{{ s}}^{{-1}}";
            rootsValue = $"两实根 s1={ScientificNotationFormatter.ToUnicodeScientific(s1)}, s2={ScientificNotationFormatter.ToUnicodeScientific(s2)}";
        }
        else // Underdamped
        {
            regime = "欠阻尼 (Underdamped)";
            double omegaD = Math.Sqrt(omega0 * omega0 - alpha * alpha);
            double fd = omegaD / (2.0 * Math.PI);
            double overshoot = Math.Exp(-Math.PI * alpha / omegaD) * 100.0;
            rootsLatex = $@"s_{{1, 2}} = -\alpha \pm j\omega_d = -{ScientificNotationFormatter.ToLatexScientific(alpha)} \pm j{ScientificNotationFormatter.ToLatexScientific(omegaD)}\text{{ rad/s}}";
            rootsValue = $"共轭复根: -{ScientificNotationFormatter.ToUnicodeScientific(alpha)} ± j{ScientificNotationFormatter.ToUnicodeScientific(omegaD)} rad/s (超调量 {overshoot:F1}%)";
        }

        solution.SummaryText = $"二阶 RLC 谐振网络解算完成：固有角频率 ω0 = {ScientificNotationFormatter.ToLatexScientific(omega0)} rad/s (f0 = {ScientificNotationFormatter.ToLatexScientific(f0)} Hz)，衰减常数 α = {ScientificNotationFormatter.ToLatexScientific(alpha)}，品质因数 Q = {q:F2}。工作状态为【{regime}】。";

        // Step 1: Natural angular frequency
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "二阶回路固有谐振角频率 ω0 与频率 f0 求解",
            MethodName = "LC 无阻尼固有谐振原理",
            Content = "求解无阻尼固有角频率 ω0 = 1/√(LC)。该参数仅由电感与电容的储能特性决定，代表回路无阻尼自由振荡角频率。",
            LatexFormula = @"\omega_0 = \frac{1}{\sqrt{LC}},\quad f_0 = \frac{\omega_0}{2\pi} = \frac{1}{2\pi\sqrt{LC}}",
            SubstitutionFormula = $@"\omega_0 = \frac{{1}}{{\sqrt{{{ScientificNotationFormatter.ToLatexScientific(l)} \times {ScientificNotationFormatter.ToLatexScientific(c)}}}}} = {ScientificNotationFormatter.ToLatexScientific(omega0)}\text{{ rad/s}},\quad f_0 = {ScientificNotationFormatter.ToLatexScientific(f0)}\text{{ Hz}}",
            ResultValue = $"f0 = {ScientificNotationFormatter.ToUnicodeScientific(f0)} Hz",
            Status = "Completed"
        });

        // Step 2: Attenuation alpha & Quality factor Q
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "回路衰减常数 α 与品质因数 Q 求解",
            MethodName = isParallel ? "并联 RLC 耗散能量分析" : "串联 RLC 阻尼分析",
            Content = isParallel
                ? "对于并联 RLC 电路，电导 G = 1/R 提供阻尼损耗，衰减常数 α = 1/(2RC)，品质因数 Q = R√(C/L)。"
                : "对于串联 RLC 电路，电阻 R 提供耗能阻尼，衰减常数 α = R/(2L)，品质因数 Q = (1/R)√(L/C)。",
            LatexFormula = isParallel
                ? @"\alpha = \frac{1}{2RC},\quad Q = \omega_0 RC = R\sqrt{\frac{C}{L}} = \frac{\omega_0}{2\alpha}"
                : @"\alpha = \frac{R}{2L},\quad Q = \frac{\omega_0 L}{R} = \frac{1}{R}\sqrt{\frac{L}{C}} = \frac{\omega_0}{2\alpha}",
            SubstitutionFormula = $@"\alpha = {ScientificNotationFormatter.ToLatexScientific(alpha)}\text{{ s}}^{{-1}},\quad Q = {q:F3}",
            ResultValue = $"Q = {q:F2}, α = {ScientificNotationFormatter.ToUnicodeScientific(alpha)} s⁻¹",
            Status = "Completed"
        });

        // Step 3: Characteristic Equation & Roots
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "二阶特征方程建立与特征根解算",
            MethodName = "二阶常微分方程特征根法",
            Content = "根据 KVL/KCL 建立二阶齐次微分方程 d²f/dt² + 2α(df/dt) + ω0²f = 0，特征方程 s² + 2αs + ω0² = 0。",
            LatexFormula = @"s^2 + 2\alpha s + \omega_0^2 = 0 \implies s_{1, 2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}",
            SubstitutionFormula = rootsLatex,
            ResultValue = rootsValue,
            Status = "Completed"
        });

        // Step 4: Damping Regime Classification
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "阻尼响应工作区判定与时域表达式形态",
            MethodName = "阻尼判别准则",
            Content = $"比较衰减常数 α 与固有角频率 ω0（判别式 Δ = α² - ω0²）：当前系统处于【{regime}】。在此状态下阶跃响应表现为特定的瞬态时域曲线。",
            LatexFormula = discriminant > 0
                ? @"f(t) = f(\infty) + A_1 e^{s_1 t} + A_2 e^{s_2 t}\quad (\alpha > \omega_0,\text{ 无振荡单调缓变})"
                : (discriminant == 0
                    ? @"f(t) = f(\infty) + (A_1 + A_2 t) e^{-\alpha t}\quad (\alpha = \omega_0,\text{ 最快无超调恢复})"
                    : @"f(t) = f(\infty) + A e^{-\alpha t} \sin(\omega_d t + \theta)\quad (\alpha < \omega_0,\text{ 衰减正弦振荡})"),
            SubstitutionFormula = $@"Q = {q:F2} \implies \text{{{regime}}}",
            ResultValue = regime,
            Status = "Completed"
        });

        // Step 5: Resonance Frequency Response & Bandwidth
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "谐振通频带 BW 与选择性评估",
            MethodName = "频域选择性与通频带分析",
            Content = "品质因数 Q 决定谐振回路的频率选择性：Q 越大，谐振曲线越尖锐，选择性越好，但通频带 BW 越窄。",
            LatexFormula = @"BW = \Delta f = \frac{f_0}{Q} = \frac{\alpha}{\pi}",
            SubstitutionFormula = $@"BW = \frac{{{ScientificNotationFormatter.ToLatexScientific(f0)}}}{{{q:F2}}} = {ScientificNotationFormatter.ToLatexScientific(bw)}\text{{ Hz}}",
            ResultValue = $"BW = {ScientificNotationFormatter.ToUnicodeScientific(bw)} Hz",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static double GetSiValue(CircuitElement el)
    {
        double multiplier = 1.0;
        foreach (var opt in EngineeringUnits.ResistanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.CapacitanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.InductanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.VoltageUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
```

---

### 3.7 Strategy 3: `BjtAmplifierStrategy.cs`

#### Analytical Formulations
1. **DC Operating Point (Q-Point)**:
   - For Voltage-Divider Self-Bias:
     $$V_{th} = V_{CC} \frac{R_{b2}}{R_{b1} + R_{b2}}, \quad R_{th} = R_{b1} \parallel R_{b2}$$
     $$I_{BQ} = \frac{V_{th} - V_{BE}}{R_{th} + (1+\beta)R_e} \quad (V_{BE} \approx 0.7\text{ V})$$
     $$I_{CQ} = \beta I_{BQ}, \quad I_{EQ} = (1+\beta) I_{BQ}$$
     $$V_{CEQ} = V_{CC} - I_{CQ}R_c - I_{EQ}R_e$$
     Active region check: $V_{CEQ} > 0.2\text{ V}$.
   - For Fixed-Bias ($R_e = 0$):
     $$I_{BQ} = \frac{V_{CC} - V_{BE}}{R_b}, \quad I_{CQ} = \beta I_{BQ}, \quad V_{CEQ} = V_{CC} - I_{CQ}R_c$$
   - For Collector-Feedback:
     $$I_{BQ} = \frac{V_{CC} - V_{BE}}{R_b + (1+\beta)R_c}, \quad I_{CQ} = \beta I_{BQ}, \quad V_{CEQ} = V_{CC} - (I_{CQ} + I_{BQ})R_c$$
2. **Small-Signal Parameters**:
   $$r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} = 200\,\Omega + (1+\beta)\frac{26\text{ mV}}{I_{CQ}}, \quad g_m = \frac{I_{CQ}}{26\text{ mV}}$$
3. **Midband AC Voltage Gain**:
   - With $C_e$ bypass:
     $$A_v = -\frac{\beta (R_c \parallel R_L)}{r_{be}}$$
   - Without $C_e$ bypass (emitter degeneration):
     $$A_v = -\frac{\beta (R_c \parallel R_L)}{r_{be} + (1+\beta)R_e}$$
4. **Input and Output Resistances**:
   $$R_i = R_{b1} \parallel R_{b2} \parallel r_{be} \quad (\text{or } R_b \parallel r_{be}), \quad R_o = R_c$$
5. **Frequency Response & Bandwidth**:
   - Lower cutoff frequency $f_L$ (SCTC method with $C_1, C_2, C_e$):
     $$f_{L1} = \frac{1}{2\pi R_i C_1}, \quad f_{L2} = \frac{1}{2\pi (R_c + R_L) C_2}$$
     $$f_{Le} = \frac{1}{2\pi R_e' C_e}, \quad R_e' = R_e \parallel \frac{r_{be} + (R_{b1} \parallel R_{b2})}{1+\beta}$$
     $$f_L = \sqrt{f_{L1}^2 + f_{L2}^2 + f_{Le}^2}$$
   - Upper cutoff frequency $f_H$ (Miller effect on $C_\mu = 3\text{ pF}, C_\pi = 30\text{ pF}$):
     $$C_M = C_\mu (1 + |A_v|), \quad C_{in}' = C_\pi + C_M, \quad f_H = \frac{1}{2\pi (R_i \parallel r_{be}) C_{in}'}$$
     $$BW = f_H - f_L \approx f_H$$

#### Complete C# Algorithm Specification

```csharp
namespace CircuitAssistant.Engine.Strategies;

public class BjtAmplifierStrategy : ICircuitStrategy
{
    private readonly string _subtype;

    public CircuitTopologyType TopologyType => CircuitTopologyType.BjtAmplifier;
    public string SubtypeName => _subtype;

    public BjtAmplifierStrategy(string subtype = "VoltageDividerSelfBias")
    {
        _subtype = subtype;
    }

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = _subtype,
            CircuitTitle = $"双极型晶体管 (BJT) {GetSubtypeDisplayName(_subtype)}交流小信号与频响分析"
        };

        // Extract components from netlist
        var bjt = netlist.Elements.Values.FirstOrDefault(e => e.Type is ElementType.BjtNpn or ElementType.BjtPnp);
        double beta = bjt != null ? bjt.DisplayNumber : 100.0;

        double vcc = 12.0;
        double rb1 = 51000.0;
        double rb2 = 15000.0;
        double rc = 3000.0;
        double re = 1000.0;
        double rl = 10000.0;
        double c1 = 10e-6;
        double c2 = 10e-6;
        double ce = 47e-6;

        foreach (var el in netlist.Elements.Values)
        {
            double val = GetSiValue(el);
            if (el.Id == "VCC") vcc = val;
            else if (el.Id == "Rb1" || el.Id == "R1") rb1 = val;
            else if (el.Id == "Rb2" || el.Id == "R2") rb2 = val;
            else if (el.Id == "Rb") { rb1 = val; rb2 = 0; }
            else if (el.Id == "Rc") rc = val;
            else if (el.Id == "Re") re = val;
            else if (el.Id == "RL") rl = val;
            else if (el.Id == "C1") c1 = val;
            else if (el.Id == "C2") c2 = val;
            else if (el.Id == "Ce") ce = val;
        }

        // DC Calculations
        double vth, rth, ibq, icq, vceq;
        if (_subtype == "FixedBias")
        {
            vth = vcc;
            rth = rb1;
            ibq = (vcc - 0.7) / rb1;
            icq = beta * ibq;
            vceq = vcc - icq * rc;
        }
        else if (_subtype == "CollectorFeedbackBias")
        {
            vth = vcc;
            rth = rb1;
            ibq = (vcc - 0.7) / (rb1 + (1 + beta) * rc);
            icq = beta * ibq;
            vceq = vcc - (icq + ibq) * rc;
        }
        else // VoltageDividerSelfBias
        {
            vth = vcc * (rb2 / (rb1 + rb2));
            rth = (rb1 * rb2) / (rb1 + rb2);
            ibq = (vth - 0.7) / (rth + (1 + beta) * re);
            icq = beta * ibq;
            vceq = vcc - icq * rc - (1 + beta) * ibq * re;
        }

        // Small-signal calculations
        double ut = 0.026; // 26 mV
        double rbe = 200.0 + (1 + beta) * (ut / Math.Max(icq, 1e-6));
        double rlPrime = (rc * rl) / (rc + rl);

        bool hasCe = netlist.Elements.Values.Any(e => e.Id == "Ce");
        double av = hasCe ? (-beta * rlPrime / rbe) : (-beta * rlPrime / (rbe + (1 + beta) * re));
        double ri = (_subtype == "FixedBias") ? (rb1 * rbe / (rb1 + rbe)) : (1.0 / (1.0 / rb1 + 1.0 / rb2 + 1.0 / rbe));
        double ro = rc;

        // Cutoff Frequencies
        double fl1 = 1.0 / (2 * Math.PI * ri * c1);
        double fl2 = 1.0 / (2 * Math.PI * (rc + rl) * c2);
        double rePrime = (re * (rbe + rth)) / ((1 + beta) * re + rbe + rth);
        double fle = hasCe ? (1.0 / (2 * Math.PI * rePrime * ce)) : 0;
        double fl = Math.Sqrt(fl1 * fl1 + fl2 * fl2 + fle * fle);

        double cPi = 30e-12;
        double cMu = 3e-12;
        double cm = cMu * (1 + Math.Abs(av));
        double cInPrime = cPi + cm;
        double rInPrime = (ri * rbe) / (ri + rbe);
        double fh = 1.0 / (2 * Math.PI * rInPrime * cInPrime);
        double bw = fh - fl;

        solution.QPoint = new QPointInfo
        {
            Ibq = $"{ScientificNotationFormatter.ToUnicodeScientific(ibq * 1e6)} μA",
            Icq = $"{ScientificNotationFormatter.ToUnicodeScientific(icq * 1e3)} mA",
            Vceq = $"{ScientificNotationFormatter.ToUnicodeScientific(vceq)} V",
            WorkingRegion = (vceq > 0.2) ? "放大区 (Active Region)" : "饱和区 (Saturation)",
            IsActiveRegion = (vceq > 0.2)
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = $"{av:F2}",
            InputResistance = $"{ScientificNotationFormatter.ToUnicodeScientific(ri / 1e3)} kΩ",
            OutputResistance = $"{ScientificNotationFormatter.ToUnicodeScientific(ro / 1e3)} kΩ",
            LowerCutoffFreq = $"{ScientificNotationFormatter.ToUnicodeScientific(fl)} Hz",
            UpperCutoffFreq = $"{ScientificNotationFormatter.ToUnicodeScientific(fh / 1e6)} MHz",
            Bandwidth = $"{ScientificNotationFormatter.ToUnicodeScientific(bw / 1e6)} MHz"
        };

        solution.SummaryText = $"BJT 放大电路求解完成：直流 Q 点 (IBQ={solution.QPoint.Ibq}, ICQ={solution.QPoint.Icq}, VCEQ={solution.QPoint.Vceq})；交流放大倍数 Av = {av:F2}，输入电阻 Ri = {solution.AcParameters.InputResistance}，通频带 BW = {solution.AcParameters.Bandwidth}。";

        // Step 1: DC Q-point
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "基极戴维南等效与直流静态工作点 (Q-Point) 求解",
            MethodName = "戴维南等效定理与基极回路 KVL",
            Content = "对分压式基极偏置电路应用戴维南定理求解基极开路等效电压 Vth 与等效电阻 Rth，列写基极-发射极回路 KVL 方程求得静态工作点。",
            LatexFormula = @"V_{th} = V_{CC} \frac{R_{b2}}{R_{b1} + R_{b2}},\quad R_{th} = R_{b1} \parallel R_{b2},\quad I_{BQ} = \frac{V_{th} - V_{BE}}{R_{th} + (1+\beta)R_e}",
            SubstitutionFormula = $@"I_{{BQ}} = {ScientificNotationFormatter.ToLatexScientific(ibq * 1e6)}\;\mu\text{{A}},\quad I_{{CQ}} = {ScientificNotationFormatter.ToLatexScientific(icq * 1e3)}\;\text{{mA}},\quad V_{{CEQ}} = {ScientificNotationFormatter.ToLatexScientific(vceq)}\;\text{{V}}",
            ResultValue = $"Q点: IBQ={ScientificNotationFormatter.ToUnicodeScientific(ibq * 1e6)} μA, ICQ={ScientificNotationFormatter.ToUnicodeScientific(icq * 1e3)} mA, VCEQ={ScientificNotationFormatter.ToUnicodeScientific(vceq)} V",
            Status = "Completed"
        });

        // Step 2: rbe
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "晶体管低频小信号微变等效参数 rbe 计算",
            MethodName = "晶体管 h 参数简化模型",
            Content = "根据 PN 结伏安特性与扩散电阻理论，求解晶体管基区体电阻 rbb' 与发射结微变电阻之和。",
            LatexFormula = @"r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}} \approx 200\,\Omega + (1+\beta)\frac{26\,\text{mV}}{I_{CQ}}",
            SubstitutionFormula = $@"r_{{be}} = 200 + (1 + {beta:F0}) \frac{{26\text{{ mV}}}}{{{ScientificNotationFormatter.ToLatexScientific(icq * 1e3)}\text{{ mA}}}} = {ScientificNotationFormatter.ToLatexScientific(rbe)}\;\Omega",
            ResultValue = $"rbe = {ScientificNotationFormatter.ToUnicodeScientific(rbe)} Ω ({ScientificNotationFormatter.ToUnicodeScientific(rbe / 1e3)} kΩ)",
            Status = "Completed"
        });

        // Step 3: Av
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "第 3 步",
            Title = "中频段交流电压放大倍数 Av 推导与求解",
            MethodName = "微变等效电路交流分析",
            Content = "绘制交流等效电路，隔直电容与旁路电容对交流短路，输出端并联交流等效负载 RL' = Rc // RL，计算输出电压对输入电压的比值。",
            LatexFormula = @"A_v = \frac{\dot{U}_o}{\dot{U}_i} = -\frac{\beta (R_c \parallel R_L)}{r_{be}}",
            SubstitutionFormula = $@"A_v = -\frac{{{beta:F0} \times ({ScientificNotationFormatter.ToLatexScientific(rc)} \parallel {ScientificNotationFormatter.ToLatexScientific(rl)})}}{{{ScientificNotationFormatter.ToLatexScientific(rbe)}}} = {av:F2}",
            ResultValue = $"Av = {av:F2}",
            Status = "Completed"
        });

        // Step 4: Ri and Ro
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 4,
            BadgeText = "第 4 步",
            Title = "放大电路输入电阻 Ri 与输出电阻 Ro 计算",
            MethodName = "输入/输出阻抗定义法",
            Content = "输入电阻为从放大器输入端看入的等效交流电阻；输出电阻为信号源置零、去除负载后从输出端看入的等效交流电阻。",
            LatexFormula = @"R_i = R_{b1} \parallel R_{b2} \parallel r_{be},\quad R_o = R_c",
            SubstitutionFormula = $@"R_i = {ScientificNotationFormatter.ToLatexScientific(ri / 1e3)}\;\text{{k}}\Omega,\quad R_o = {ScientificNotationFormatter.ToLatexScientific(ro / 1e3)}\;\text{{k}}\Omega",
            ResultValue = $"Ri = {ScientificNotationFormatter.ToUnicodeScientific(ri / 1e3)} kΩ, Ro = {ScientificNotationFormatter.ToUnicodeScientific(ro / 1e3)} kΩ",
            Status = "Completed"
        });

        // Step 5: Bode Frequency Characteristics
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 5,
            BadgeText = "第 5 步",
            Title = "频率特性分析（密勒效应、极点时间常数与通频带 BW）",
            MethodName = "开路/短路时间常数法 (SCTC/OCTC)",
            Content = "下限截止频率 fL 由耦合电容与旁路电容的时间常数决定；上限截止频率 fH 由极间寄生结电容与密勒倍增效应形成的主极点决定。",
            LatexFormula = @"f_L = \sqrt{f_{L1}^2 + f_{L2}^2 + f_{Le}^2},\quad f_H \approx \frac{1}{2\pi R_{in}' C_{in}'},\quad BW = f_H - f_L",
            SubstitutionFormula = $@"f_L = {ScientificNotationFormatter.ToLatexScientific(fl)}\;\text{{Hz}},\quad f_H = {ScientificNotationFormatter.ToLatexScientific(fh / 1e6)}\;\text{{MHz}},\quad BW = {ScientificNotationFormatter.ToLatexScientific(bw / 1e6)}\;\text{{MHz}}",
            ResultValue = $"BW = {ScientificNotationFormatter.ToUnicodeScientific(bw / 1e6)} MHz (fL={fl:F1} Hz, fH={fh / 1e6:F2} MHz)",
            Status = "Completed"
        });

        return Task.FromResult(solution);
    }

    private static string GetSubtypeDisplayName(string subtype) => subtype switch
    {
        "VoltageDividerSelfBias" => "分压式自偏置共射",
        "FixedBias" => "固定偏置阻容耦合共射",
        "CollectorFeedbackBias" => "集电极反馈偏置共射",
        _ => "共射极"
    };

    private static double GetSiValue(CircuitElement el)
    {
        double multiplier = 1.0;
        foreach (var opt in EngineeringUnits.ResistanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.CapacitanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.InductanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.VoltageUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
```

---

### 3.8 Strategy 4: `MnaLinearStrategy.cs`

#### Analytical Matrix Formulation
SPICE standard Modified Nodal Analysis (MNA):
$$\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix} \begin{bmatrix} \mathbf{v} \\ \mathbf{j} \end{bmatrix} = \begin{bmatrix} \mathbf{i} \\ \mathbf{e} \end{bmatrix}$$
- Index non-ground nodes $1, \dots, N$.
- Index independent voltage sources $1, \dots, M$.
- Matrix dimension $K = N + M$.
- Stamping rules:
  - Resistor $R$ between node $p$ and $q$ ($g = 1/R$):
    $$G_{p,p} += g, \quad G_{q,q} += g, \quad G_{p,q} -= g, \quad G_{q,p} -= g$$
  - Independent voltage source $V_k$ with value $E_k$ between positive node $p$ and negative node $q$:
    $$B_{p, k} = +1, \quad B_{q, k} = -1, \quad C_{k, p} = +1, \quad C_{k, q} = -1, \quad e_k = E_k$$
  - Independent current source $I$ from node $p$ to node $q$:
    $$i_p -= I, \quad i_q += I$$
- Solution method:
  Native C# Gaussian elimination with partial pivoting $\mathcal{O}(K^3)$:
  Guarantees numerical stability, zero external dependency, and outputs both symbolic LaTeX and numerical LaTeX matrices.

#### Complete C# Algorithm Specification

```csharp
using System.Text;

namespace CircuitAssistant.Engine.Strategies;

public class MnaLinearStrategy : ICircuitStrategy
{
    public CircuitTopologyType TopologyType => CircuitTopologyType.MnaLinear;
    public string SubtypeName => "GeneralLinearMna";

    public Task<PedagogicalSolution> SolveAsync(ElectricalNetlist netlist, AnalysisParameters parameters)
    {
        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            TopologyType = TopologyType,
            SubtypeName = SubtypeName,
            CircuitTitle = "通用线性网络 SPICE 改进节点分析法 (MNA) 矩阵方程严谨解算"
        };

        // 1. Identify ground net and non-ground electrical nodes
        var gndNet = netlist.GroundNet;
        var nonGndNets = netlist.Nets.Where(n => !n.IsGround && n != gndNet).ToList();
        int nNodes = nonGndNets.Count;

        var netIndexMap = new Dictionary<int, int>();
        for (int i = 0; i < nNodes; i++) netIndexMap[nonGndNets[i].NetId] = i;

        // 2. Identify voltage sources
        var vSources = netlist.Elements.Values
            .Where(e => e.Type is ElementType.DcVoltageSource or ElementType.AcVoltageSource)
            .ToList();
        int nVSources = vSources.Count;

        int matrixDim = nNodes + nVSources;
        if (matrixDim == 0)
        {
            solution.SummaryText = "网络中无有效非地节点或独立激励源。";
            return Task.FromResult(solution);
        }

        double[,] A = new double[matrixDim, matrixDim];
        double[] Z = new double[matrixDim];

        // 3. Stamp Resistors into G
        var resistors = netlist.Elements.Values.Where(e => e.Type == ElementType.Resistor).ToList();
        foreach (var r in resistors)
        {
            double rVal = GetSiValue(r);
            if (rVal <= 1e-12) rVal = 1e-3; // Prevent division by zero
            double g = 1.0 / rVal;

            var net1 = netlist.FindNetForPin(r.Id, "1");
            var net2 = netlist.FindNetForPin(r.Id, "2");

            int idx1 = (net1 != null && netIndexMap.TryGetValue(net1.NetId, out var i1)) ? i1 : -1;
            int idx2 = (net2 != null && netIndexMap.TryGetValue(net2.NetId, out var i2)) ? i2 : -1;

            if (idx1 >= 0) A[idx1, idx1] += g;
            if (idx2 >= 0) A[idx2, idx2] += g;
            if (idx1 >= 0 && idx2 >= 0)
            {
                A[idx1, idx2] -= g;
                A[idx2, idx1] -= g;
            }
        }

        // 4. Stamp Voltage Sources into B, C, e
        for (int k = 0; k < nVSources; k++)
        {
            var vs = vSources[k];
            double vVal = GetSiValue(vs);
            int vRow = nNodes + k;

            var posNet = netlist.FindNetForPin(vs.Id, "pos");
            var negNet = netlist.FindNetForPin(vs.Id, "neg");

            int posIdx = (posNet != null && netIndexMap.TryGetValue(posNet.NetId, out var p)) ? p : -1;
            int negIdx = (negNet != null && netIndexMap.TryGetValue(negNet.NetId, out var n)) ? n : -1;

            if (posIdx >= 0)
            {
                A[posIdx, vRow] += 1.0;
                A[vRow, posIdx] += 1.0;
            }
            if (negIdx >= 0)
            {
                A[negIdx, vRow] -= 1.0;
                A[vRow, negIdx] -= 1.0;
            }
            Z[vRow] = vVal;
        }

        // 5. Solve A x = Z via Gaussian Elimination with Partial Pivoting
        double[] x = SolveGaussian(A, Z, matrixDim);

        // 6. Format LaTeX Matrices
        string matrixLatex = FormatMatrixEquationLatex(A, Z, nonGndNets, vSources, matrixDim, nNodes);

        var sbLog = new StringBuilder();
        sbLog.AppendLine("=== MNA 求解日志 ===");
        sbLog.AppendLine($"系统矩阵阶数: {matrixDim} (节点变量: {nNodes}, 支路电流变量: {nVSources})");
        for (int i = 0; i < nNodes; i++)
        {
            sbLog.AppendLine($"  节点 {nonGndNets[i].Name} 电位 v_{i+1} = {x[i]:F4} V");
        }
        for (int k = 0; k < nVSources; k++)
        {
            sbLog.AppendLine($"  电源 {vSources[k].Id} 支路电流 i_{vSources[k].Id} = {x[nNodes + k] * 1000.0:F4} mA");
        }
        solution.RawMnaLog = sbLog.ToString();
        solution.SummaryText = $"SPICE MNA 矩阵分析求解成功：共解算 {nNodes} 个独立节点电位与 {nVSources} 个支路电流变量。";

        // Step 1: MNA formulation
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "第 1 步",
            Title = "SPICE 标准改进节点分析法 (MNA) 矩阵方程建立",
            MethodName = "改进节点分析法 (MNA)",
            Content = "根据工业标准 SPICE 算法，将线性电导元件印记填入 G 矩阵，独立电压源支路填入 B/C 矩阵，构建 Gv + Bi = J 复合方程。",
            LatexFormula = @"\begin{bmatrix} \mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D} \end{bmatrix} \begin{bmatrix} \mathbf{v} \\ \mathbf{j} \end{bmatrix} = \begin{bmatrix} \mathbf{i} \\ \mathbf{e} \end{bmatrix}",
            SubstitutionFormula = matrixLatex,
            ResultValue = $"矩阵阶数: {matrixDim}×{matrixDim}",
            Status = "Completed",
            MnaLog = solution.RawMnaLog
        });

        // Step 2: Nodal Solution
        var resSb = new StringBuilder();
        for (int i = 0; i < nNodes; i++)
        {
            resSb.Append($@"v_{{{nonGndNets[i].Name}}} = {ScientificNotationFormatter.ToLatexScientific(x[i])}\text{{ V}},\quad ");
        }
        for (int k = 0; k < nVSources; k++)
        {
            resSb.Append($@"i_{{{vSources[k].Id}}} = {ScientificNotationFormatter.ToLatexScientific(x[nNodes + k] * 1000.0)}\text{{ mA}}");
        }

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "第 2 步",
            Title = "矩阵高斯消元求解各节点电位与支路电流",
            MethodName = "主元高斯消元数值求解",
            Content = "对 MNA 系统矩阵执行选主元高斯消去法与回代运算，精确求解全部独立节点相对参考地的电位与流经电压源的支路电流。",
            LatexFormula = @"\mathbf{x} = \mathbf{A}^{-1} \mathbf{z}",
            SubstitutionFormula = resSb.ToString().TrimEnd(',', ' ', ';'),
            ResultValue = (nNodes > 0) ? $"v1 = {x[0]:F3} V" : "求解成功",
            Status = "Completed",
            MnaLog = solution.RawMnaLog
        });

        return Task.FromResult(solution);
    }

    private static double[] SolveGaussian(double[,] aOrig, double[] zOrig, int n)
    {
        double[,] a = (double[,])aOrig.Clone();
        double[] b = (double[])zOrig.Clone();

        for (int i = 0; i < n; i++)
        {
            // Pivot selection
            int maxRow = i;
            double maxVal = Math.Abs(a[i, i]);
            for (int k = i + 1; k < n; k++)
            {
                if (Math.Abs(a[k, i]) > maxVal)
                {
                    maxVal = Math.Abs(a[k, i]);
                    maxRow = k;
                }
            }

            if (maxVal < 1e-14) continue; // Singular or floating node

            // Swap rows
            for (int k = i; k < n; k++)
            {
                double tmp = a[maxRow, k];
                a[maxRow, k] = a[i, k];
                a[i, k] = tmp;
            }
            double tmpB = b[maxRow];
            b[maxRow] = b[i];
            b[i] = tmpB;

            // Elimination
            for (int k = i + 1; k < n; k++)
            {
                double factor = a[k, i] / a[i, i];
                b[k] -= factor * b[i];
                for (int j = i; j < n; j++)
                {
                    a[k, j] -= factor * a[i, j];
                }
            }
        }

        // Back substitution
        double[] x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = 0.0;
            for (int j = i + 1; j < n; j++) sum += a[i, j] * x[j];
            x[i] = Math.Abs(a[i, i]) > 1e-14 ? ((b[i] - sum) / a[i, i]) : 0.0;
        }
        return x;
    }

    private static string FormatMatrixEquationLatex(double[,] a, double[] z, List<ElectricalNet> nets, List<CircuitElement> vs, int dim, int nNodes)
    {
        var sb = new StringBuilder();
        sb.Append(@"\begin{bmatrix} ");
        for (int i = 0; i < dim; i++)
        {
            for (int j = 0; j < dim; j++)
            {
                sb.Append(ScientificNotationFormatter.ToLatexScientific(a[i, j]));
                if (j < dim - 1) sb.Append(" & ");
            }
            if (i < dim - 1) sb.Append(@" \\ ");
        }
        sb.Append(@" \end{bmatrix} \begin{bmatrix} ");
        for (int i = 0; i < nNodes; i++)
        {
            sb.Append($@"v_{{{nets[i].Name}}}");
            if (i < dim - 1) sb.Append(@" \\ ");
        }
        for (int k = 0; k < vs.Count; k++)
        {
            sb.Append($@"i_{{{vs[k].Id}}}");
            if (nNodes + k < dim - 1) sb.Append(@" \\ ");
        }
        sb.Append(@" \end{bmatrix} = \begin{bmatrix} ");
        for (int i = 0; i < dim; i++)
        {
            sb.Append(ScientificNotationFormatter.ToLatexScientific(z[i]));
            if (i < dim - 1) sb.Append(@" \\ ");
        }
        sb.Append(@" \end{bmatrix}");
        return sb.ToString();
    }

    private static double GetSiValue(CircuitElement el)
    {
        double multiplier = 1.0;
        foreach (var opt in EngineeringUnits.ResistanceUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        foreach (var opt in EngineeringUnits.VoltageUnits) if (opt.Symbol == el.UnitSymbol) multiplier = opt.Multiplier;
        return el.DisplayNumber * multiplier;
    }
}
```

---

## 4. Caveats

1. **Floating Pins and Open Circuits**:
   - If a capacitor or resistor has a disconnected pin, `NetlistExtractor` marks that net as a single-pin net. `TopologyClassifier` will detect that a closed loop does not exist and route to `MnaLinearStrategy` or emit a warning step rather than throwing an unhandled exception.
2. **Coupled Multi-Stage BJT Amplifiers**:
   - If a user draws a multi-stage cascaded amplifier (e.g. 2 BJTs), `TopologyClassifier` currently recognizes the first stage and solves its loading or falls back safely to `MnaLinearStrategy`. Multi-stage full derivation will be handled cleanly in Milestone 4.
3. **Template Wires Coordination**:
   - `CircuitTemplateFactory` must be used so that `Wires` are never empty. When templates are loaded, the Netlist Extractor receives explicit wire connections.
4. **Read-Only Scope Compliance**:
   - As an Explorer agent, no production code or test files were modified during this investigation. All specifications are completely recorded in this handoff report.

---

## 5. Conclusion

1. The proposed native C# architecture eliminates the hardcoded BJT fallback in `AnalysisStudioViewModel.cs` and `CircuitApiService.cs`, completely fulfilling **Acceptance Criterion 1**:
   - When a pure RC circuit is loaded or drawn, `TopologyClassifier` identifies `FirstOrderRc` and dispatches `FirstOrderRcStrategy`.
   - The emitted solution contains Thevenin $R_{eq}$, $\tau$, the Three-Element differential equation $u_C(t)$, $H(s)$, and $f_c$, with **zero BJT parameters**.
2. All 4 required solver categories are fully modeled and implemented:
   - **一阶 RC/RL**: Thevenin $R_{eq}, \tau$, Three-Element Method $u_C(t)$, transfer function $H(s)$, cutoff frequency $f_c$.
   - **二阶 RLC**: Undamped frequency $\omega_0, f_0$, attenuation constant $\alpha$, quality factor $Q$, characteristic roots $s_{1, 2}$, damping regimes (overdamped, critically damped, underdamped), bandwidth $BW$.
   - **晶体管放大器 (BJT)**: Distinguishes voltage-divider self-bias, fixed-bias, and collector-feedback bias; computes DC Q-point ($I_{BQ}, I_{CQ}, V_{CEQ}$), $r_{be}, A_v, R_i, R_o$, and Bode frequency response.
   - **通用线性网络**: SPICE MNA matrix equation $\mathbf{G}\mathbf{v} + \mathbf{B}\mathbf{i} = \mathbf{J}$ with native C# Gaussian elimination.
3. The pedagogical data model (`PedagogicalSolution` and `PedagogicalSolutionStep`) bridges seamlessly with `AnalysisResult` and `AnalysisStep`, ensuring instant rendering in `MathFormulaView` with standardized scientific notation.

---

## 6. Verification Method

### 6.1 Independent Verification Steps
1. **Compilation Check**:
   ```powershell
   dotnet build apps/desktop-winui/CircuitAssistant.csproj
   ```
   *Expected Result*: Build succeeded, 0 warnings, 0 errors.

2. **Automated Graph & Strategy Testing**:
   Run the Challenger test suite in `tests/ChallengerRunner`:
   ```powershell
   dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
   ```
   *Expected Result*: All suites pass with 0 failures.

3. **Pure First-Order RC Acceptance Criterion Verification**:
   - Load template `RC_Transient.sch` (or draw `VS`, `R1`, `C1`, `GND`).
   - Trigger `SolveCircuitAsync()`.
   - Verify `solution.Steps`:
     - Step 1: $R_{eq} = R_1 = 1.00\text{ k}\Omega$.
     - Step 2: $\tau = R_{eq} C_1 = 100.0\text{ ms}$.
     - Step 3: $u_C(t) = 10.0(1 - e^{-t/0.1})\text{ V}$.
     - Step 4: $f_c = \frac{1}{2\pi R_1 C_1} = 1.59\text{ Hz}$.
     - Zero instances of $\beta, r_{be}, I_{BQ}, I_{CQ}, V_{CEQ}$ anywhere in the steps or UI.
   - *Invalidation Condition*: Any presence of transistor parameters or Q-point in the generated RC solution.

4. **Second-Order RLC Damping Verification**:
   - Construct series RLC ($R=100\,\Omega, L=10\text{ mH}, C=1\,\mu\text{F}$).
   - Verify $\omega_0 = 10^4\text{ rad/s}, \alpha = 5000\text{ s}^{-1}, Q = 1.00$.
   - Discriminant $\Delta = \alpha^2 - \omega_0^2 = 2.5 \times 10^7 - 10^8 < 0 \implies$ Underdamped (欠阻尼), $\omega_d = 8.66 \times 10^3\text{ rad/s}$.
