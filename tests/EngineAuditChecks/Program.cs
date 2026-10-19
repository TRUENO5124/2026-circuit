// ============================================================================
// File: tests/EngineAuditChecks/Program.cs
// Description: Independent adversarial numerical audit of the MNA 2.0 general
//              linear-network solver:
//                  apps/desktop-winui/Engine/Linear/LinearNetworkAnalyzer.cs
//                  apps/desktop-winui/Engine/Strategies/MnaLinearStrategy.cs
//
//              This harness intentionally does NOT trust the engine's own checks.
//              It contains a from-scratch complex MNA reference solver
//              (ReferenceSolve / SolveDense) written only for this audit, and it
//              compares the engine against:
//                * closed-form textbook answers (hand-derived in each section),
//                * the independent reference solver on randomized networks,
//                * engine-internal consistency (assembled matrix re-solved by the
//                  audit solver, branch currents vs. KCL, structured contract).
//
//              The reference implementation never calls LinearNetworkAnalyzer to
//              obtain an expected value. It only consumes the produced netlists.
//
//              Output contract: every check prints [PASS]/[FAIL]; failures print
//              expected/actual/relative error and the reproducible seed; the last
//              line is "Total=... Passed=... Failed=..." and the exit code is
//              non-zero when anything fails. Run with:
//                  dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CircuitAssistant.Engine;
using CircuitAssistant.Engine.Linear;
using CircuitAssistant.Engine.Strategies;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace EngineAuditChecks;

internal static class Program
{
    private static int _total;
    private static int _passed;
    private static int _failed;
    private static readonly List<string> _failures = new();
    private static readonly List<(string Name, int Total, int Passed, int Failed)> _sectionStats = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Base seed for randomized cross-validation (reproducible failures).</summary>
    private const int RandomSeedBase = 20261017;

    private static int Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console may forbid it */ }
        Console.WriteLine("================================================================================");
        Console.WriteLine("ENGINE AUDIT CHECKS：MNA 2.0 通用线性网络求解器 独立对抗性数值审计");
        Console.WriteLine("（独立复数 MNA 参考求解器 + 解析解 + 随机网络 + 结构化契约；不信任实现自测）");
        Console.WriteLine("================================================================================");

        RunSection("S0 审计基础设施与参考求解器自检", CheckInfrastructure);
        RunSection("S1 DC 解析基准与逐节点 KCL 残差", CheckDcAnalytic);
        RunSection("S2 随机线性网络独立 MNA 交叉验证", CheckRandomCrossValidation);
        RunSection("S3 任意端口 Req / 戴维南 / 串并联解析", CheckPortEquivalents);
        RunSection("S4 AC 正弦稳态相量与复数 MNA 交叉验证", CheckAcPhasors);
        RunSection("S5 AC 扫频 61 点一致性与截止频率", CheckAcSweep);
        RunSection("S6 边界、奇异矩阵与拒绝行为", CheckBoundaryRejection);
        RunSection("S7 取消语义与矩阵规模上限", CheckCancellationAndScale);
        RunSection("S8 MnaLinearStrategy 结构化结果契约", CheckStructuredResults);
        RunSection("S9 NetlistExtractor 端到端集成路径", CheckExtractorIntegration);

        Console.WriteLine();
        Console.WriteLine("--- 覆盖矩阵（逐段检查数 / 通过 / 失败）---");
        foreach ((string name, int total, int passed, int failed) in _sectionStats)
            Console.WriteLine($"  {name}: Total={total}, Passed={passed}, Failed={failed}");
        Console.WriteLine("独立参考实现：tests/EngineAuditChecks/Program.cs 内的 ReferenceSolve/SolveDense");
        Console.WriteLine("（自写复数 MNA + 缩放选主元高斯消元 + 反向误差残差门；期望值不调用 LinearNetworkAnalyzer）");

        Console.WriteLine();
        Console.WriteLine($"Total={_total} Passed={_passed} Failed={_failed}");
        if (_failed > 0)
        {
            Console.WriteLine("FAILURES:");
            foreach (string failure in _failures) Console.WriteLine("  " + failure);
            return 1;
        }
        return 0;
    }

    private static void RunSection(string name, Action section)
    {
        int beforeTotal = _total;
        int beforePassed = _passed;
        int beforeFailed = _failed;
        Console.WriteLine();
        Console.WriteLine($"--- {name} ---");
        try
        {
            section();
        }
        catch (Exception ex)
        {
            Check(false, $"{name}: 段内未处理异常", ex.GetType().Name + ": " + ex.Message);
        }
        _sectionStats.Add((name, _total - beforeTotal, _passed - beforePassed, _failed - beforeFailed));
    }

    // =====================================================================
    // Assertions (failure output always carries expected/actual/relative error)
    // =====================================================================

    private static void Check(bool ok, string name, string details = "")
    {
        _total++;
        if (ok)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {name}");
        }
        else
        {
            _failed++;
            string line = string.IsNullOrEmpty(details) ? name : name + " -> " + details;
            _failures.Add(line);
            Console.WriteLine($"  [FAIL] {line}");
        }
    }

    private static string Fmt(double value) => double.IsFinite(value)
        ? value.ToString("G10", Inv)
        : value.ToString(Inv);

    private static string Cfmt(Complex value) =>
        $"({Fmt(value.Real)}{(value.Imaginary >= 0 ? "+" : "-")}j{Fmt(Math.Abs(value.Imaginary))})";

    /// <summary>Relative comparison. tolerance = relTol * max(|expected|, floor).</summary>
    private static void CheckRel(double actual, double expected, double relTol, string name,
        string unit = "", double floor = 0.0)
    {
        double tolerance = relTol * Math.Max(Math.Abs(expected), floor);
        double error = Math.Abs(actual - expected);
        bool ok = double.IsFinite(actual) && double.IsFinite(expected) && error <= tolerance;
        Check(ok, name,
            $"expected {Fmt(expected)}{unit}, actual {Fmt(actual)}{unit}, |err|={Fmt(error)}, " +
            $"rel={(Math.Abs(expected) > 0 ? Fmt(error / Math.Abs(expected)) : "n/a")}, tol={Fmt(tolerance)}");
    }

    private static void CheckAbs(double actual, double expected, double absTol, string name, string unit = "")
    {
        double error = Math.Abs(actual - expected);
        bool ok = double.IsFinite(actual) && double.IsFinite(expected) && error <= absTol;
        Check(ok, name,
            $"expected {Fmt(expected)}{unit}, actual {Fmt(actual)}{unit}, |err|={Fmt(error)}, tol={Fmt(absTol)}");
    }

    private static double PhaseDeg(Complex value) =>
        value.Magnitude < 1e-300 ? 0.0 : Math.Atan2(value.Imaginary, value.Real) * 180.0 / Math.PI;

    private static double NormalizeDeg(double angle)
    {
        double wrapped = ((angle + 540.0) % 360.0) - 180.0;
        return wrapped;
    }

    private static void CheckPhase(double actualDeg, double expectedDeg, double toleranceDeg, string name)
    {
        double diff = Math.Abs(NormalizeDeg(actualDeg - expectedDeg));
        Check(diff <= toleranceDeg, name,
            $"expected {Fmt(expectedDeg)}°, actual {Fmt(actualDeg)}°, diff={Fmt(diff)}°, tol={Fmt(toleranceDeg)}°");
    }

    private static void CheckComplexRel(Complex actual, Complex expected, double relTol, string name)
    {
        double scale = Math.Max(expected.Magnitude, 1e-12);
        double error = (actual - expected).Magnitude;
        bool ok = double.IsFinite(actual.Real) && double.IsFinite(actual.Imaginary) && error <= relTol * scale;
        Check(ok, name,
            $"expected {Cfmt(expected)}, actual {Cfmt(actual)}, |err|={Fmt(error)}, " +
            $"rel={Fmt(error / scale)}, tol={Fmt(relTol * scale)}");
    }

    private static void CheckNoNaNinText(string text, string name)
    {
        bool ok = !string.IsNullOrEmpty(text) &&
                  !text.Contains("NaN", StringComparison.OrdinalIgnoreCase) &&
                  !text.Contains("Infinity", StringComparison.OrdinalIgnoreCase) &&
                  !text.Contains("非数字", StringComparison.OrdinalIgnoreCase);
        Check(ok, name, $"text='{text}'");
    }

    // =====================================================================
    // Independent reference solver (from scratch, unrelated to the engine's
    // implementation: own device model, own stamping, own scaled-pivot complex
    // Gaussian elimination with its own residual verification).
    // =====================================================================

    private enum RefKind { R, C, L, Vdc, Vac, Idc, Iac, Vcvs, Vccs, Ccvs, Cccs }

    private sealed class RefDevice
    {
        public string Id = string.Empty;
        public RefKind Kind;
        /// <summary>Base SI value (Ω, F, H, V, A, or controlled-source gain).</summary>
        public double Value;
        public int P;
        public int N;
        public int Cp;
        public int Cn;
        public bool IsAcSource => Kind is RefKind.Vac or RefKind.Iac;
        public bool IsDcSource => Kind is RefKind.Vdc or RefKind.Idc;
    }

    private sealed class RefNetwork
    {
        public int NodeCount = 1; // node 0 is the reference ground
        public readonly List<RefDevice> Devices = new();

        public RefDevice Add(RefKind kind, string id, double value, int p, int n)
        {
            var device = new RefDevice { Id = id, Kind = kind, Value = value, P = p, N = n };
            Devices.Add(device);
            return device;
        }

        public RefDevice AddControlled(RefKind kind, string id, double value, int cp, int cn, int op, int on)
        {
            var device = new RefDevice { Id = id, Kind = kind, Value = value, Cp = cp, Cn = cn, P = op, N = on };
            Devices.Add(device);
            return device;
        }
    }

    private sealed class RefSolveOptions
    {
        public bool IsAc;
        public double FrequencyHz;
        public bool ZeroIndependentSources;
        public Dictionary<string, Complex>? Overrides;
        public (int Positive, int Negative)? Injection;
        public bool AddTestVoltageSource;
        public (int Positive, int Negative)? TestPort;
        public Complex TestValue = Complex.One;
    }

    private sealed class RefSolveResult
    {
        public bool Success;
        public string Error = string.Empty;
        public Complex[] NodeVoltage = Array.Empty<Complex>();
        public Dictionary<string, Complex> BranchCurrent = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Audit reference MNA solve. Conventions follow the documented SPICE contract:
    /// node 0 = ground; independent voltage source branch current positive from
    /// positive to negative terminal; independent current source injects current
    /// into its "in" node (stored as P) and removes it from its "out" node (N);
    /// VCVS V(OUT1)-V(OUT2)=mu*(V(IN1)-V(IN2)); VCCS I(OUT1->OUT2)=gm*(V(IN1)-V(IN2));
    /// CCVS/CCCS use a 0 V control branch IN1->IN2 with current I_ctrl.
    /// </summary>
    private static RefSolveResult ReferenceSolve(RefNetwork network, RefSolveOptions options)
    {
        var result = new RefSolveResult();
        int nodeVariables = network.NodeCount - 1;
        int dimension = nodeVariables;

        var branchColumn = new Dictionary<string, int>(StringComparer.Ordinal);
        var controlColumn = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (RefDevice device in network.Devices)
        {
            if (device.Kind is RefKind.Vdc or RefKind.Vac or RefKind.L or RefKind.Vcvs or RefKind.Ccvs)
                branchColumn[device.Id] = dimension++;
        }
        foreach (RefDevice device in network.Devices)
        {
            if (device.Kind is RefKind.Ccvs or RefKind.Cccs) controlColumn[device.Id] = dimension++;
        }
        if (options.AddTestVoltageSource) dimension++;

        var a = new Complex[dimension, dimension];
        var b = new Complex[dimension];

        int Column(int node) => node == 0 ? -1 : node - 1;

        void Kcl(int node, int column, Complex value)
        {
            int row = Column(node);
            if (row >= 0) a[row, column] += value;
        }

        void ConstraintEntry(int row, int node, Complex value)
        {
            int column = Column(node);
            if (column >= 0) a[row, column] += value;
        }

        void StampAdmittance(int positive, int negative, Complex admittance)
        {
            int p = Column(positive);
            int n = Column(negative);
            if (p >= 0) a[p, p] += admittance;
            if (n >= 0) a[n, n] += admittance;
            if (p >= 0 && n >= 0)
            {
                a[p, n] -= admittance;
                a[n, p] -= admittance;
            }
        }

        Complex ResolveSource(RefDevice device)
        {
            if (options.Overrides != null && options.Overrides.TryGetValue(device.Id, out Complex overridden))
                return overridden;
            if (options.ZeroIndependentSources) return Complex.Zero;
            bool naturalValueApplies = options.IsAc ? device.IsAcSource : device.IsDcSource;
            return naturalValueApplies ? new Complex(device.Value, 0.0) : Complex.Zero;
        }

        double omega = options.IsAc ? 2.0 * Math.PI * options.FrequencyHz : 0.0;

        foreach (RefDevice device in network.Devices)
        {
            int p = Column(device.P);
            int n = Column(device.N);
            int cp = Column(device.Cp);
            int cn = Column(device.Cn);
            switch (device.Kind)
            {
                case RefKind.R:
                    StampAdmittance(device.P, device.N, new Complex(1.0 / device.Value, 0.0));
                    break;

                case RefKind.C:
                    if (options.IsAc) StampAdmittance(device.P, device.N, new Complex(0.0, omega * device.Value));
                    break;

                case RefKind.L:
                {
                    int branch = branchColumn[device.Id];
                    Kcl(device.P, branch, Complex.One);
                    Kcl(device.N, branch, -Complex.One);
                    ConstraintEntry(branch, device.P, Complex.One);
                    ConstraintEntry(branch, device.N, -Complex.One);
                    if (options.IsAc) a[branch, branch] -= new Complex(0.0, omega * device.Value);
                    break;
                }

                case RefKind.Vdc:
                case RefKind.Vac:
                {
                    int branch = branchColumn[device.Id];
                    Kcl(device.P, branch, Complex.One);
                    Kcl(device.N, branch, -Complex.One);
                    ConstraintEntry(branch, device.P, Complex.One);
                    ConstraintEntry(branch, device.N, -Complex.One);
                    b[branch] += ResolveSource(device);
                    break;
                }

                case RefKind.Idc:
                case RefKind.Iac:
                {
                    Complex value = ResolveSource(device);
                    if (p >= 0) b[p] += value;
                    if (n >= 0) b[n] -= value;
                    break;
                }

                case RefKind.Vcvs:
                {
                    int branch = branchColumn[device.Id];
                    Kcl(device.P, branch, Complex.One);
                    Kcl(device.N, branch, -Complex.One);
                    ConstraintEntry(branch, device.P, Complex.One);
                    ConstraintEntry(branch, device.N, -Complex.One);
                    ConstraintEntry(branch, device.Cp, new Complex(-device.Value, 0.0));
                    ConstraintEntry(branch, device.Cn, new Complex(device.Value, 0.0));
                    break;
                }

                case RefKind.Vccs:
                {
                    var gain = new Complex(device.Value, 0.0);
                    if (p >= 0 && cp >= 0) a[p, cp] += gain;
                    if (p >= 0 && cn >= 0) a[p, cn] -= gain;
                    if (n >= 0 && cp >= 0) a[n, cp] -= gain;
                    if (n >= 0 && cn >= 0) a[n, cn] += gain;
                    break;
                }

                case RefKind.Ccvs:
                {
                    int control = controlColumn[device.Id];
                    Kcl(device.Cp, control, Complex.One);
                    Kcl(device.Cn, control, -Complex.One);
                    ConstraintEntry(control, device.Cp, Complex.One);
                    ConstraintEntry(control, device.Cn, -Complex.One);
                    int branch = branchColumn[device.Id];
                    Kcl(device.P, branch, Complex.One);
                    Kcl(device.N, branch, -Complex.One);
                    ConstraintEntry(branch, device.P, Complex.One);
                    ConstraintEntry(branch, device.N, -Complex.One);
                    a[branch, control] -= new Complex(device.Value, 0.0);
                    break;
                }

                case RefKind.Cccs:
                {
                    int control = controlColumn[device.Id];
                    Kcl(device.Cp, control, Complex.One);
                    Kcl(device.Cn, control, -Complex.One);
                    ConstraintEntry(control, device.Cp, Complex.One);
                    ConstraintEntry(control, device.Cn, -Complex.One);
                    var gain = new Complex(device.Value, 0.0);
                    if (p >= 0) a[p, control] += gain;
                    if (n >= 0) a[n, control] -= gain;
                    break;
                }

                default:
                    result.Error = "参考求解器未处理的器件类型 " + device.Kind;
                    return result;
            }
        }

        if (options.Injection is { } injection)
        {
            int p = Column(injection.Positive);
            int n = Column(injection.Negative);
            if (p >= 0) b[p] += Complex.One;
            if (n >= 0) b[n] -= Complex.One;
        }

        if (options.AddTestVoltageSource && options.TestPort is { } testPort)
        {
            int row = dimension - 1;
            Kcl(testPort.Positive, row, Complex.One);
            Kcl(testPort.Negative, row, -Complex.One);
            ConstraintEntry(row, testPort.Positive, Complex.One);
            ConstraintEntry(row, testPort.Negative, -Complex.One);
            b[row] += options.TestValue;
        }

        (bool ok, Complex[] solution, string error) = SolveDense(a, b, dimension);
        if (!ok)
        {
            result.Error = error;
            return result;
        }

        result.NodeVoltage = new Complex[network.NodeCount];
        for (int node = 1; node < network.NodeCount; node++) result.NodeVoltage[node] = solution[node - 1];
        foreach (KeyValuePair<string, int> entry in branchColumn) result.BranchCurrent[entry.Key] = solution[entry.Value];
        foreach (KeyValuePair<string, int> entry in controlColumn) result.BranchCurrent["ctrl:" + entry.Key] = solution[entry.Value];
        result.Success = true;
        return result;
    }

    /// <summary>
    /// Independent dense complex Gaussian elimination with scaled partial pivoting
    /// and its own backward-error residual gate. Returns a human-readable failure
    /// reason instead of a bogus solution when the system is singular/ill-posed.
    /// </summary>
    private static (bool ok, Complex[] solution, string error) SolveDense(Complex[,] matrix, Complex[] rhs, int dimension)
    {
        var a = (Complex[,])matrix.Clone();
        var b = (Complex[])rhs.Clone();
        var rowScale = new double[dimension];
        for (int row = 0; row < dimension; row++)
        {
            double max = 0.0;
            for (int column = 0; column < dimension; column++) max = Math.Max(max, a[row, column].Magnitude);
            rowScale[row] = max > 0 ? max : 1.0;
        }

        for (int column = 0; column < dimension; column++)
        {
            int pivotRow = -1;
            double bestRatio = -1.0;
            for (int row = column; row < dimension; row++)
            {
                double ratio = a[row, column].Magnitude / rowScale[row];
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    pivotRow = row;
                }
            }
            if (pivotRow < 0 || bestRatio < 1e-13)
            {
                return (false, Array.Empty<Complex>(),
                    $"参考求解器判定矩阵奇异/病态：第 {column} 列缩放主元比 {bestRatio:G4} < 1e-13");
            }
            if (pivotRow != column)
            {
                for (int k = column; k < dimension; k++) (a[column, k], a[pivotRow, k]) = (a[pivotRow, k], a[column, k]);
                (b[column], b[pivotRow]) = (b[pivotRow], b[column]);
                (rowScale[column], rowScale[pivotRow]) = (rowScale[pivotRow], rowScale[column]);
            }
            Complex pivot = a[column, column];
            for (int row = column + 1; row < dimension; row++)
            {
                Complex factor = a[row, column] / pivot;
                if (factor == Complex.Zero) continue;
                a[row, column] = Complex.Zero;
                for (int k = column + 1; k < dimension; k++) a[row, k] -= factor * a[column, k];
                b[row] -= factor * b[column];
            }
        }

        var x = new Complex[dimension];
        for (int row = dimension - 1; row >= 0; row--)
        {
            Complex sum = b[row];
            for (int column = row + 1; column < dimension; column++) sum -= a[row, column] * x[column];
            Complex diagonal = a[row, row];
            if (diagonal.Magnitude <= 1e-300)
                return (false, Array.Empty<Complex>(), $"参考求解器回代主元过小（第 {row} 行）");
            x[row] = sum / diagonal;
            if (!double.IsFinite(x[row].Real) || !double.IsFinite(x[row].Imaginary))
                return (false, Array.Empty<Complex>(), "参考求解器解出现非有限值");
        }

        // Backward-error gate on the ORIGINAL (uneliminated) system.
        double residualNormSquared = 0.0;
        double matrixNorm = 0.0;
        double rhsNorm = 0.0;
        double solutionNorm = 0.0;
        for (int row = 0; row < dimension; row++)
        {
            Complex accumulated = Complex.Zero;
            for (int column = 0; column < dimension; column++)
            {
                accumulated += matrix[row, column] * x[column];
                matrixNorm = Math.Max(matrixNorm, matrix[row, column].Magnitude);
            }
            Complex residual = accumulated - rhs[row];
            residualNormSquared += residual.Magnitude * residual.Magnitude;
            rhsNorm = Math.Max(rhsNorm, rhs[row].Magnitude);
            solutionNorm = Math.Max(solutionNorm, x[row].Magnitude);
        }
        double residualNorm = Math.Sqrt(residualNormSquared);
        double denominator = matrixNorm * solutionNorm + rhsNorm + 1e-300;
        if (!double.IsFinite(residualNorm) || residualNorm / denominator > 1e-8)
        {
            return (false, Array.Empty<Complex>(),
                $"参考求解器残差校验未通过：relative residual {residualNorm / denominator:G4} > 1e-8");
        }

        return (true, x, string.Empty);
    }

    // =====================================================================
    // Circuit fixture builder: constructs BOTH the production ElectricalNetlist
    // (CircuitElement + CircuitPin wiring / net ids) and the independent RefNetwork
    // from the same topology description, so every expected value comes from the
    // audit's own solver instead of the engine.
    // =====================================================================

    private sealed class Fixture
    {
        private readonly List<CircuitElement> _elements = new();
        private readonly List<(string ElementId, string PinId, int Net)> _pins = new();

        public int NodeCount { get; private set; } = 1; // node 0 = GND
        public RefNetwork Model { get; } = new() { NodeCount = 1 };
        public IReadOnlyList<CircuitElement> Elements => _elements;

        public int NewNode()
        {
            NodeCount++;
            Model.NodeCount = NodeCount;
            return NodeCount - 1;
        }

        public CircuitElement Element(string id, ElementType type, double displayNumber, string unitSymbol,
            double frequencyHz = 1000.0)
        {
            if (_elements.Any(e => string.Equals(e.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException("duplicate element id " + id);
            var element = new CircuitElement
            {
                Id = id,
                Name = id,
                Type = type,
                Value = displayNumber,
                DisplayNumber = displayNumber,
                UnitSymbol = unitSymbol,
                FrequencyHz = frequencyHz
            };
            element.InitializeDefaultPins();
            _elements.Add(element);
            return element;
        }

        public void Pin(string elementId, string pinId, int net)
        {
            if (net < 0 || net >= NodeCount) throw new InvalidOperationException($"net {net} out of range for {elementId}.{pinId}");
            _pins.Add((elementId, pinId, net));
        }

        // ---- two-terminal base-SI convenience helpers (unit ×1) ----------
        public void R(string id, int a, int b, double ohm)
        {
            Element(id, ElementType.Resistor, ohm, "Ω");
            Pin(id, "1", a); Pin(id, "2", b);
            Model.Add(RefKind.R, id, ohm, a, b);
        }

        public void C(string id, int a, int b, double farad)
        {
            Element(id, ElementType.Capacitor, farad, "F");
            Pin(id, "1", a); Pin(id, "2", b);
            Model.Add(RefKind.C, id, farad, a, b);
        }

        public void L(string id, int a, int b, double henry)
        {
            Element(id, ElementType.Inductor, henry, "H");
            Pin(id, "1", a); Pin(id, "2", b);
            Model.Add(RefKind.L, id, henry, a, b);
        }

        public void Vdc(string id, int pos, int neg, double volts)
        {
            Element(id, ElementType.DcVoltageSource, volts, "V");
            Pin(id, "pos", pos); Pin(id, "neg", neg);
            Model.Add(RefKind.Vdc, id, volts, pos, neg);
        }

        public void Vac(string id, int pos, int neg, double amplitudeVolt, double frequencyHz)
        {
            Element(id, ElementType.AcVoltageSource, amplitudeVolt, "V", frequencyHz);
            Pin(id, "pos", pos); Pin(id, "neg", neg);
            Model.Add(RefKind.Vac, id, amplitudeVolt, pos, neg);
        }

        /// <summary>Independent current source: current is injected into the "in" net.</summary>
        public void Idc(string id, int inNet, int outNet, double amps)
        {
            Element(id, ElementType.DcCurrentSource, amps, "A");
            Pin(id, "in", inNet); Pin(id, "out", outNet);
            Model.Add(RefKind.Idc, id, amps, inNet, outNet);
        }

        public void Iac(string id, int inNet, int outNet, double amplitudeAmp, double frequencyHz)
        {
            Element(id, ElementType.AcCurrentSource, amplitudeAmp, "A", frequencyHz);
            Pin(id, "in", inNet); Pin(id, "out", outNet);
            Model.Add(RefKind.Iac, id, amplitudeAmp, inNet, outNet);
        }

        // ---- four-terminal controlled sources (base-SI gain) --------------
        public void Vcvs(string id, int inPos, int inNeg, int outPos, int outNeg, double mu)
            => Controlled(id, ElementType.Vcvs, mu, "倍", inPos, inNeg, outPos, outNeg, RefKind.Vcvs, mu);

        public void Vccs(string id, int inPos, int inNeg, int outPos, int outNeg, double siemens)
            => Controlled(id, ElementType.Vccs, siemens, "S", inPos, inNeg, outPos, outNeg, RefKind.Vccs, siemens);

        public void Ccvs(string id, int inPos, int inNeg, int outPos, int outNeg, double ohm)
            => Controlled(id, ElementType.Ccvs, ohm, "Ω", inPos, inNeg, outPos, outNeg, RefKind.Ccvs, ohm);

        public void Cccs(string id, int inPos, int inNeg, int outPos, int outNeg, double beta)
            => Controlled(id, ElementType.Cccs, beta, "倍", inPos, inNeg, outPos, outNeg, RefKind.Cccs, beta);

        /// <summary>Controlled source with an arbitrary display/unit pair; refSi is the independent base-SI gain.</summary>
        public void ControlledRaw(string id, ElementType type, double displayNumber, string unitSymbol,
            int inPos, int inNeg, int outPos, int outNeg, double refSi)
        {
            RefKind kind = type switch
            {
                ElementType.Vcvs => RefKind.Vcvs,
                ElementType.Vccs => RefKind.Vccs,
                ElementType.Ccvs => RefKind.Ccvs,
                _ => RefKind.Cccs
            };
            Controlled(id, type, displayNumber, unitSymbol, inPos, inNeg, outPos, outNeg, kind, refSi);
        }

        private void Controlled(string id, ElementType type, double displayNumber, string unitSymbol,
            int inPos, int inNeg, int outPos, int outNeg, RefKind kind, double refSi)
        {
            Element(id, type, displayNumber, unitSymbol);
            Pin(id, "IN1", inPos); Pin(id, "IN2", inNeg);
            Pin(id, "OUT1", outPos); Pin(id, "OUT2", outNeg);
            Model.AddControlled(kind, id, refSi, inPos, inNeg, outPos, outNeg);
        }

        public ElectricalNetlist Build(bool markGround = true)
        {
            var netlist = new ElectricalNetlist();
            foreach (CircuitElement element in _elements) netlist.Elements[element.Id] = element;

            for (int netId = 0; netId < NodeCount; netId++)
            {
                var net = new ElectricalNet
                {
                    NetId = netId,
                    Name = netId == 0 ? "GND" : "N" + netId.ToString(Inv),
                    IsGround = markGround && netId == 0
                };
                foreach ((string elementId, string pinId, int netIndex) in _pins)
                {
                    if (netIndex != netId) continue;
                    net.ConnectedPins.Add(new ComponentPinRef(elementId, pinId, pinId));
                }
                netlist.Nets.Add(net);
            }

            foreach (ElectricalNet net in netlist.Nets)
                netlist.Nodes.Add(new ElectricalNode { Index = net.NetId, Name = net.Name, Net = net });

            return netlist;
        }

        public ElectricalNetlist BuildWithExtraResistor(string extraId, int a, int b, double ohm)
        {
            R(extraId, a, b, ohm);
            return Build();
        }
    }

    // =====================================================================
    // Analysis helpers shared by sections
    // =====================================================================

    private static LinearNetworkAnalyzer? TryBuildModel(ElectricalNetlist netlist, out string error)
    {
        LinearNetworkAnalyzer.TryCreate(netlist, out LinearNetworkAnalyzer? model, out error);
        return model;
    }

    private static Complex NaturalSourceValue(LinearDevice device, bool isAc)
    {
        bool natural = isAc ? device.IsAcSource : !device.IsAcSource;
        return natural ? new Complex(device.ValueSi, 0.0) : Complex.Zero;
    }

    /// <summary>
    /// Per-node KCL residual computed ONLY from the engine's node voltages and branch
    /// currents plus the documented element equations. The pass criterion is a
    /// network-relative check: max|residual| ≤ 1e-9 × max node current scale, which is
    /// the "相对电流量级 ≤1e-9" contract (absolute residuals at nearly current-free
    /// nodes are reported separately).
    /// </summary>
    private static (double networkRelative, string detail) MeasureKcl(
        LinearNetworkAnalyzer model, LinearSolution solution, bool isAc, double frequencyHz)
    {
        int count = model.Nodes.Count;
        var rowByNet = new Dictionary<int, int>();
        for (int i = 0; i < model.Nodes.Count; i++) rowByNet[model.Nodes[i].NetId] = i;
        var leaving = new Complex[count];
        var scale = new double[count];
        var activity = new double[count];

        void AddLeaving(int netId, Complex current, double termActivity)
        {
            if (netId == model.GroundNetId) return;
            if (!rowByNet.TryGetValue(netId, out int row)) return;
            leaving[row] += current;
            scale[row] += current.Magnitude;
            activity[row] += termActivity;
        }

        Complex V(int netId) => solution.NodePhasor(netId);
        Complex Vc(LinearDevice device, bool controlPositive)
        {
            ElectricalNet? net = controlPositive ? device.ControlPositiveNet : device.ControlNegativeNet;
            return net == null ? Complex.Zero : V(net.NetId);
        }

        Complex ControlCurrent(LinearDevice device) => solution.BranchCurrents
            .First(b => b.IsControlBranch && string.Equals(b.ElementId, device.ElementId, StringComparison.Ordinal))
            .Value;

        double omega = isAc ? 2.0 * Math.PI * frequencyHz : 0.0;

        foreach (LinearDevice device in model.Devices)
        {
            Complex vp = V(device.PositiveNet.NetId);
            Complex vn = V(device.NegativeNet.NetId);
            switch (device.Kind)
            {
                case LinearDeviceKind.Resistor:
                {
                    double conductance = 1.0 / device.ValueSi;
                    // Round-off of a conductance term is ~eps*G*(|Vp|+|Vn|); track that
                    // magnitude so heavily cancelled nodes are judged fairly.
                    double termActivity = conductance * (vp.Magnitude + vn.Magnitude);
                    Complex current = new Complex(conductance, 0.0) * (vp - vn);
                    AddLeaving(device.PositiveNet.NetId, current, termActivity);
                    AddLeaving(device.NegativeNet.NetId, -current, termActivity);
                    break;
                }
                case LinearDeviceKind.Capacitor:
                    if (isAc)
                    {
                        double susceptance = omega * device.ValueSi;
                        double termActivity = susceptance * (vp.Magnitude + vn.Magnitude);
                        Complex current = new Complex(0.0, susceptance) * (vp - vn);
                        AddLeaving(device.PositiveNet.NetId, current, termActivity);
                        AddLeaving(device.NegativeNet.NetId, -current, termActivity);
                    }
                    break;

                case LinearDeviceKind.Inductor:
                case LinearDeviceKind.DcVoltageSource:
                case LinearDeviceKind.AcVoltageSource:
                case LinearDeviceKind.Vcvs:
                {
                    Complex current = solution.BranchPhasor(device.ElementId);
                    AddLeaving(device.PositiveNet.NetId, current, current.Magnitude);
                    AddLeaving(device.NegativeNet.NetId, -current, current.Magnitude);
                    break;
                }
                case LinearDeviceKind.Ccvs:
                {
                    // Own output branch current (OUT1 -> OUT2) plus the 0 V control branch
                    // current (IN1 -> IN2) that the implementation stores separately.
                    Complex output = solution.BranchPhasor(device.ElementId);
                    AddLeaving(device.PositiveNet.NetId, output, output.Magnitude);
                    AddLeaving(device.NegativeNet.NetId, -output, output.Magnitude);
                    Complex control = ControlCurrent(device);
                    AddLeaving(device.ControlPositiveNet!.NetId, control, control.Magnitude);
                    AddLeaving(device.ControlNegativeNet!.NetId, -control, control.Magnitude);
                    break;
                }
                case LinearDeviceKind.DcCurrentSource:
                case LinearDeviceKind.AcCurrentSource:
                {
                    Complex injected = NaturalSourceValue(device, isAc);
                    AddLeaving(device.PositiveNet.NetId, -injected, injected.Magnitude);
                    AddLeaving(device.NegativeNet.NetId, injected, injected.Magnitude);
                    break;
                }
                case LinearDeviceKind.Vccs:
                {
                    Complex controlVoltage = Vc(device, true) - Vc(device, false);
                    double termActivity = Math.Abs(device.ValueSi) *
                                          (Vc(device, true).Magnitude + Vc(device, false).Magnitude);
                    Complex current = new Complex(device.ValueSi, 0.0) * controlVoltage;
                    AddLeaving(device.PositiveNet.NetId, current, termActivity);
                    AddLeaving(device.NegativeNet.NetId, -current, termActivity);
                    break;
                }
                case LinearDeviceKind.Cccs:
                {
                    Complex control = ControlCurrent(device);
                    AddLeaving(device.ControlPositiveNet!.NetId, control, control.Magnitude);
                    AddLeaving(device.ControlNegativeNet!.NetId, -control, control.Magnitude);
                    double termActivity = Math.Abs(device.ValueSi) * control.Magnitude;
                    Complex current = new Complex(device.ValueSi, 0.0) * control;
                    AddLeaving(device.PositiveNet.NetId, current, termActivity);
                    AddLeaving(device.NegativeNet.NetId, -current, termActivity);
                    break;
                }
            }
        }

        // Pass criterion: |KCL residual| ≤ 1e-9 × network current scale, plus the
        // unavoidable double-precision round-off of the cancellation terms
        // (≈ 64·eps·Σ|G·V| and |I| term magnitudes). A physical KCL defect (e.g. a
        // mis-stamped controlled-source current) is orders of magnitude above this.
        const double roundoffFactor = 1e-13;
        double globalScale = 0.0;
        for (int row = 0; row < count; row++) globalScale = Math.Max(globalScale, scale[row]);
        double worstRatio = 0.0;
        double maxAbsolute = 0.0;
        string worstDetail = string.Empty;
        for (int row = 0; row < count; row++)
        {
            double residual = leaving[row].Magnitude;
            maxAbsolute = Math.Max(maxAbsolute, residual);
            double bound = 1e-9 * globalScale + roundoffFactor * activity[row];
            double ratio = residual <= 0 ? 0.0 : residual / Math.Max(bound, 1e-300);
            if (ratio > worstRatio)
            {
                worstRatio = ratio;
                worstDetail = $"{model.Nodes[row].Name}: |residual|={Fmt(residual)} A, node current scale={Fmt(scale[row])} A, " +
                              $"network current scale={Fmt(globalScale)} A, cancellation activity={Fmt(activity[row])} A, allowed={Fmt(bound)} A";
            }
        }
        return (worstRatio,
            $"max|residual|={Fmt(maxAbsolute)} A, worst node {worstDetail}");
    }

    /// <summary>
    /// Compares every engine node potential against the independent reference for one
    /// solve mode, using a network-scale relative norm: |Δv| / max|v| ≤ tolerance.
    /// </summary>
    private static (double networkRelative, string detail) CompareNodeVoltages(
        LinearNetworkAnalyzer model, LinearSolution solution, RefSolveResult reference, bool isAc)
    {
        double maxReference = 0.0;
        for (int node = 1; node < reference.NodeVoltage.Length; node++)
            maxReference = Math.Max(maxReference, reference.NodeVoltage[node].Magnitude);
        double scale = Math.Max(maxReference, 1e-30);

        double worst = 0.0;
        string worstDetail = string.Empty;
        foreach (LinearNetNode node in model.Nodes)
        {
            Complex engine = solution.NodePhasor(node.NetId);
            Complex expected = reference.NodeVoltage[node.NetId];
            double error = (engine - expected).Magnitude;
            double relative = error / scale;
            if (relative > worst)
            {
                worst = relative;
                worstDetail = $"{node.Name}: expected {Cfmt(expected)} actual {Cfmt(engine)}, |err|={Fmt(error)}";
            }
        }
        return (worst, worstDetail);
    }

    /// <summary>Independent reference for the Req test-current definition used by the engine.</summary>
    private static (bool ok, double value, string error) ReferenceEquivalentResistance(RefNetwork network, int netA, int netB)
    {
        var options = new RefSolveOptions
        {
            ZeroIndependentSources = true,
            Injection = (netA, netB)
        };
        RefSolveResult solved = ReferenceSolve(network, options);
        if (!solved.Success) return (false, double.NaN, solved.Error);
        double value = (solved.NodeVoltage[netA] - solved.NodeVoltage[netB]).Real;
        return (double.IsFinite(value), value, double.IsFinite(value) ? string.Empty : "参考 Req 非有限");
    }

    /// <summary>Independent reference for the natural DC operating point (AC sources zeroed).</summary>
    private static RefSolveResult ReferenceDc(RefNetwork network)
        => ReferenceSolve(network, new RefSolveOptions { IsAc = false });

    /// <summary>Independent reference for the natural AC phasor operating point at one frequency.</summary>
    private static RefSolveResult ReferenceAc(RefNetwork network, double frequencyHz)
        => ReferenceSolve(network, new RefSolveOptions { IsAc = true, FrequencyHz = frequencyHz });

    /// <summary>Independent reference for a normalized excitation (1 V or 1 A) at a port.</summary>
    private static RefSolveResult ReferenceNormalized(RefNetwork network, double frequencyHz,
        int inputPositiveNet, int inputNegativeNet)
    {
        // Prefer an existing independent source on the port; otherwise inject a 1 V test source.
        RefDevice? voltageSource = network.Devices.FirstOrDefault(d =>
            d.Kind is RefKind.Vac or RefKind.Vdc && d.P == inputPositiveNet && d.N == inputNegativeNet);
        RefDevice? currentSource = network.Devices.FirstOrDefault(d =>
            d.Kind is RefKind.Iac or RefKind.Idc && d.P == inputPositiveNet && d.N == inputNegativeNet);
        if (voltageSource != null)
        {
            return ReferenceSolve(network, new RefSolveOptions
            {
                IsAc = true,
                FrequencyHz = frequencyHz,
                ZeroIndependentSources = true,
                Overrides = new Dictionary<string, Complex> { [voltageSource.Id] = Complex.One }
            });
        }
        if (currentSource != null)
        {
            return ReferenceSolve(network, new RefSolveOptions
            {
                IsAc = true,
                FrequencyHz = frequencyHz,
                ZeroIndependentSources = true,
                Overrides = new Dictionary<string, Complex> { [currentSource.Id] = Complex.One }
            });
        }
        return ReferenceSolve(network, new RefSolveOptions
        {
            IsAc = true,
            FrequencyHz = frequencyHz,
            ZeroIndependentSources = true,
            AddTestVoltageSource = true,
            TestPort = (inputPositiveNet, inputNegativeNet)
        });
    }

    // =====================================================================
    // S0: audit infrastructure self-check (reference solver + fixture builder)
    // =====================================================================

    private static void CheckInfrastructure()
    {
        // Reference solver vs hand answer: 12 V, 1 k + 1 k divider.
        var divider = new Fixture();
        divider.NewNode(); divider.NewNode();
        divider.Vdc("V1", 1, 0, 12.0);
        divider.R("R1", 1, 2, 1e3);
        divider.R("R2", 2, 0, 1e3);
        RefSolveResult reference = ReferenceDc(divider.Model);
        Check(reference.Success, "S0 参考求解器：分压网络 DC 求解成功", reference.Error);
        CheckRel(reference.NodeVoltage[1].Real, 12.0, 1e-12, "S0 参考求解器：N1 = 12 V", " V");
        CheckRel(reference.NodeVoltage[2].Real, 6.0, 1e-12, "S0 参考求解器：N2 = 6 V", " V");
        CheckRel(reference.BranchCurrent["V1"].Real, -0.006, 1e-12, "S0 参考求解器：V1 支路电流 = -6 mA", " A");

        // Reference solver vs hand answer: VCVS mu = 2.
        var amplifier = new Fixture();
        amplifier.NewNode(); amplifier.NewNode();
        amplifier.Vdc("V1", 1, 0, 1.0);
        amplifier.Vcvs("E1", 1, 0, 2, 0, 2.0);
        amplifier.R("RL", 2, 0, 1e3);
        RefSolveResult amp = ReferenceDc(amplifier.Model);
        Check(amp.Success, "S0 参考求解器：VCVS 网络 DC 求解成功", amp.Error);
        CheckRel(amp.NodeVoltage[2].Real, 2.0, 1e-12, "S0 参考求解器：VCVS 输出 = 2 V", " V");

        // Reference solver vs hand answer: RC low-pass at fc -> 1/sqrt(2) magnitude, -45 deg.
        var rc = new Fixture();
        rc.NewNode(); rc.NewNode();
        double fc = 1.0 / (2.0 * Math.PI * 1e3 * 1e-6);
        rc.Vac("V1", 1, 0, 1.0, fc);
        rc.R("R1", 1, 2, 1e3);
        rc.C("C1", 2, 0, 1e-6);
        RefSolveResult ac = ReferenceAc(rc.Model, fc);
        Check(ac.Success, "S0 参考求解器：RC 低通 AC 相量求解成功", ac.Error);
        CheckRel(ac.NodeVoltage[2].Magnitude, 1.0 / Math.Sqrt(2.0), 1e-12, "S0 参考求解器：|H(fc)| = 1/sqrt(2)");
        CheckPhase(PhaseDeg(ac.NodeVoltage[2]), -45.0, 1e-9, "S0 参考求解器：∠H(fc) = -45°");

        // Fixture builder + production engine smoke check (values are hand-derived).
        ElectricalNetlist netlist = divider.Build();
        Check(netlist.Elements.Count == 3 && netlist.Nets.Count == 3, "S0 Fixture：网表元素/网络计数正确",
            $"elements={netlist.Elements.Count}, nets={netlist.Nets.Count}");
        Check(netlist.GroundNet != null && netlist.GroundNet.NetId == 0, "S0 Fixture：参考地网络为 NetId=0");
        LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
        Check(model != null, "S0 引擎冒烟：TryCreate 成功", createError);
        if (model != null)
        {
            bool solved = model.TrySolveDc(out LinearSolution? dc, out string solveError);
            Check(solved && dc != null, "S0 引擎冒烟：TrySolveDc 成功", solveError);
            if (dc != null)
            {
                CheckRel(dc.NodeVoltage(1), 12.0, 1e-12, "S0 引擎冒烟：N1 = 12 V", " V");
                CheckRel(dc.NodeVoltage(2), 6.0, 1e-12, "S0 引擎冒烟：N2 = 6 V", " V");
                Check(model.MatrixDimension == 3, "S0 引擎冒烟：矩阵阶数 = 3（2 节点 + 1 支路电流）",
                    $"dim={model.MatrixDimension}");
            }
        }

        // Independent dense solver check on the engine's assembled matrix.
        string assembleError = string.Empty;
        if (model != null && model.TryAssembleDcSystem(out Complex[,]? matrix, out Complex[]? rhs, out assembleError)
            && matrix != null && rhs != null)
        {
            (bool ok, Complex[] x, string error) = SolveDense(matrix, rhs, model.MatrixDimension);
            Check(ok, "S0 审计求解器可解引擎装配矩阵（装配/消元可分离验证）", error);
            if (ok)
            {
                CheckRel(x[0].Real, 12.0, 1e-12, "S0 引擎装配矩阵独立求解：v(N1) = 12 V", " V");
                CheckRel(x[1].Real, 6.0, 1e-12, "S0 引擎装配矩阵独立求解：v(N2) = 6 V", " V");
            }
        }
        else
        {
            Check(false, "S0 引擎装配矩阵获取成功", assembleError);
        }
    }

    // =====================================================================
    // Shared analytic-DC helpers
    // =====================================================================

    private static double[] Expected(int nodeCount, params (int Net, double Value)[] values)
    {
        var expected = new double[nodeCount];
        for (int i = 0; i < nodeCount; i++) expected[i] = double.NaN;
        expected[0] = 0.0;
        foreach ((int net, double value) in values) expected[net] = value;
        return expected;
    }

    /// <summary>
    /// Runs one DC analytic benchmark: engine node voltages vs. hand answer, engine vs.
    /// independent reference MNA, per-node KCL residual and selected branch-current signs.
    /// </summary>
    private static bool RunDcAnalyticCase(string label, Fixture fixture, double[] expectedByNet,
        params (string ElementId, double ExpectedAmpere)[] expectedBranchCurrents)
    {
        ElectricalNetlist netlist = fixture.Build();
        LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
        if (model == null)
        {
            Check(false, label + "：TryCreate 成功", createError);
            return false;
        }
        Check(true, label + "：TryCreate 成功");

        if (!model.TrySolveDc(out LinearSolution? solution, out string solveError) || solution == null)
        {
            Check(false, label + "：DC 求解成功", solveError);
            return false;
        }

        double worstHandRelative = 0.0;
        string worstHandDetail = string.Empty;
        for (int net = 1; net < expectedByNet.Length; net++)
        {
            if (double.IsNaN(expectedByNet[net])) continue;
            double actual = solution.NodeVoltage(net);
            double error = Math.Abs(actual - expectedByNet[net]);
            double relative = error / Math.Max(Math.Abs(expectedByNet[net]), 1e-9);
            if (relative > worstHandRelative)
            {
                worstHandRelative = relative;
                worstHandDetail = $"N{net} expected {Fmt(expectedByNet[net])} V, actual {Fmt(actual)} V, |err|={Fmt(error)}";
            }
        }
        Check(worstHandRelative <= 1e-6, label + "：节点电位 vs 手算（相对误差 ≤1e-6）",
            $"worst rel={Fmt(worstHandRelative)}; {worstHandDetail}");

        RefSolveResult reference = ReferenceDc(fixture.Model);
        if (!reference.Success)
        {
            Check(false, label + "：独立参考 MNA DC 求解成功", reference.Error);
        }
        else
        {
            (double maxRelative, string worstNode) = CompareNodeVoltages(model, solution, reference, false);
            Check(maxRelative <= 1e-6, label + "：引擎 vs 独立参考 MNA（相对误差 ≤1e-6）",
                $"worst rel={Fmt(maxRelative)}; {worstNode}");
        }

        (double kclRatio, string kclDetail) = MeasureKcl(model, solution, isAc: false, frequencyHz: 0);
        Check(kclRatio <= 1.0, label + "：逐节点 KCL 残差 ≤1e-9（相对电流量级）", kclDetail);

        foreach ((string elementId, double expectedAmpere) in expectedBranchCurrents)
        {
            double actual = solution.BranchCurrentAmpere(elementId);
            CheckRel(actual, expectedAmpere, 1e-6, label + $"：支路电流 i({elementId}) = {Fmt(expectedAmpere)} A", " A", 1e-9);
        }
        return true;
    }

    // =====================================================================
    // S1: DC analytic benchmarks + per-node KCL residuals
    // =====================================================================

    private static void CheckDcAnalytic()
    {
        // 1. 12 V divider, 1 k / 1 k.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            RunDcAnalyticCase("DC 分压 12V/1k/1k", f, Expected(f.NodeCount, (1, 12.0), (2, 6.0)),
                ("V1", -0.006));
        }

        // 2. Loaded divider: 12 V, R1=1k, R2=2k, RL=3k -> V2 = 12*(2k||3k)/(1k+2k||3k).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3); f.R("RL", 2, 0, 3e3);
            double r2l = 2e3 * 3e3 / (2e3 + 3e3);
            double v2 = 12.0 * r2l / (1e3 + r2l);
            double iTotal = 12.0 / (1e3 + r2l);
            RunDcAnalyticCase("DC 带载分压 12V/1k/(2k||3k)", f, Expected(f.NodeCount, (1, 12.0), (2, v2)),
                ("V1", -iTotal));
        }

        // 3. Balanced Wheatstone bridge: V2 = V3 = 10*200/300.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 10.0);
            f.R("R1", 1, 2, 100); f.R("R2", 1, 3, 100);
            f.R("R3", 2, 0, 200); f.R("R4", 3, 0, 200); f.R("R5", 2, 3, 50);
            RunDcAnalyticCase("DC 平衡惠斯通电桥", f,
                Expected(f.NodeCount, (1, 10.0), (2, 10.0 * 200 / 300), (3, 10.0 * 200 / 300)));
        }

        // 4. Unbalanced bridge: R3 = 150 -> V2 = 10*150/250 = 6 V, V3 = 10*200/300.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 10.0);
            f.R("R1", 1, 2, 100); f.R("R2", 1, 3, 100);
            f.R("R3", 2, 0, 150); f.R("R4", 3, 0, 200); f.R("R5", 2, 3, 50);
            RunDcAnalyticCase("DC 非平衡惠斯通电桥", f,
                Expected(f.NodeCount, (1, 10.0), (2, 3630.0 / 583.0), (3, 340.0 / 53.0)));
        }

        // 5. Two voltage sources joined by one resistor: I = (10-5)/1k.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 10.0); f.Vdc("V2", 2, 0, 5.0); f.R("R1", 1, 2, 1e3);
            RunDcAnalyticCase("DC 多源 (10V/5V 经 1k 互联)", f, Expected(f.NodeCount, (1, 10.0), (2, 5.0)),
                ("V1", -0.005), ("V2", 0.005));
        }

        // 6. 1 mA current source into 1 k: V = +1 V (injection at "in" pin).
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); f.R("R1", 1, 0, 1e3);
            RunDcAnalyticCase("DC 电流源 1mA/1k (in→out 方向)", f, Expected(f.NodeCount, (1, 1.0)));
        }

        // 7. Negative current source: V = -2 V.
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, -2e-3); f.R("R1", 1, 0, 1e3);
            RunDcAnalyticCase("DC 负电流源 -2mA/1k", f, Expected(f.NodeCount, (1, -2.0)));
        }

        // 8. 2 mA current source into 1k + 1k series: V1 = 4 V, V2 = 2 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Idc("I1", 1, 0, 2e-3); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            RunDcAnalyticCase("DC 电流源 2mA/串联 1k+1k", f, Expected(f.NodeCount, (1, 4.0), (2, 2.0)));
        }

        // 9. VCVS mu=3 with 1k load: Vout = 3 V, source branch current = -3 mA.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.Vcvs("E1", 1, 0, 2, 0, 3.0); f.R("RL", 2, 0, 1e3);
            RunDcAnalyticCase("DC VCVS mu=3", f, Expected(f.NodeCount, (1, 1.0), (2, 3.0)),
                ("E1", -0.003));
        }

        // 10. VCCS gm=2 mS controlled by 1 V: Vout = -gm*Vin*RL = -2 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.Vccs("G1", 1, 0, 2, 0, 2e-3); f.R("RL", 2, 0, 1e3);
            RunDcAnalyticCase("DC VCCS gm=2mS", f, Expected(f.NodeCount, (1, 1.0), (2, -2.0)));
        }

        // 11. CCVS r=2k, control current = 1 V / 1k = 1 mA: Vout = r*Ictrl = 2 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e3);
            f.Ccvs("X1", 2, 0, 3, 0, 2e3); f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCVS r=2kΩ", f,
                Expected(f.NodeCount, (1, 1.0), (2, 0.0), (3, 2.0)), ("X1", -0.002));
        }

        // 12. CCCS beta=3, control current = 1 mA: Vout = -beta*Ictrl*RL = -3 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e3);
            f.Cccs("X1", 2, 0, 3, 0, 3.0); f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCCS β=3", f,
                Expected(f.NodeCount, (1, 1.0), (2, 0.0), (3, -3.0)));
        }

        // 13. Two-loop controlled cascade: VCVS mu=2 then VCCS gm=1 mS.
        //     V2 = 2 V, V3 = -gm*V2*1k = -2 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vcvs("E1", 1, 0, 2, 0, 2.0); f.R("R2", 2, 0, 1e4);
            f.Vccs("G1", 2, 0, 3, 0, 1e-3); f.R("R3", 3, 0, 1e3);
            RunDcAnalyticCase("DC 受控源双环 VCVS(μ=2)+VCCS(gm=1mS)", f,
                Expected(f.NodeCount, (1, 1.0), (2, 2.0), (3, -2.0)));
        }

        // 14. Two-loop current-controlled cascade:
        //     V1=1 V -> R1=1k -> CCVS control (Ictrl1 = 1 mA) -> V3 = 2 V
        //     -> R2=1k -> CCCS control (Ictrl2 = 2 mA) -> V5 = -β*Ictrl2*1k = -4 V.
        {
            var f = new Fixture();
            for (int i = 1; i <= 5; i++) f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e3);
            f.Ccvs("X1", 2, 0, 3, 0, 2e3); f.R("R2", 3, 4, 1e3);
            f.Cccs("X2", 4, 0, 5, 0, 2.0); f.R("R3", 5, 0, 1e3);
            RunDcAnalyticCase("DC 受控源双环 CCVS(r=2kΩ)+CCCS(β=2)", f,
                Expected(f.NodeCount, (1, 1.0), (2, 0.0), (3, 2.0), (4, 0.0), (5, -4.0)));
        }

        // 15. VCVS with a feedback/load resistor between output and input:
        //     V2 = 2 V (ideal output), i(E1) = -(V2-V1)/1k = -1 mA.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.Vcvs("E1", 1, 0, 2, 0, 2.0); f.R("R2", 2, 1, 1e3);
            RunDcAnalyticCase("DC VCVS 反馈电阻 (2V-1V)/1k", f,
                Expected(f.NodeCount, (1, 1.0), (2, 2.0)), ("E1", -0.001));
        }

        // 16. DC inductor = 0 V branch: 1 mA through L gives V = 0, i(L) = +1 mA.
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); f.L("L1", 1, 0, 10e-3);
            RunDcAnalyticCase("DC 电感短路 (1mA 电流源)", f, Expected(f.NodeCount, (1, 0.0)), ("L1", 1e-3));
        }

        // 17. DC inductor in series with 1k: V2 = 5 V, i(L) = 5 mA.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 5.0); f.L("L1", 1, 2, 10e-3); f.R("R1", 2, 0, 1e3);
            RunDcAnalyticCase("DC 电感串联 5V/10mH/1k", f, Expected(f.NodeCount, (1, 5.0), (2, 5.0)),
                ("L1", 5e-3), ("V1", -5e-3));
        }

        // 18. DC capacitor is an open circuit: V2 = 10*1k/(1k+1k) = 5 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 10.0); f.R("R1", 1, 2, 1e3);
            f.C("C1", 2, 0, 1e-6); f.R("R2", 2, 0, 1e3);
            RunDcAnalyticCase("DC 电容开路 (10V/1k/1k)", f, Expected(f.NodeCount, (1, 10.0), (2, 5.0)));
        }

        // 19. Superposition-shaped network with a current source and a voltage source.
        //     I=1 mA into N1, R1=1k N1–N2, V1=2 V N2–GND: N1 = 2 + 1 = 3 V.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); f.R("R1", 1, 2, 1e3); f.Vdc("V1", 2, 0, 2.0);
            RunDcAnalyticCase("DC 电流源+电压源叠加 (1mA,2V,1k)", f,
                Expected(f.NodeCount, (1, 3.0), (2, 2.0)));
        }

        // 20. Engine-assembled DC system re-solved by the audit solver for a controlled network.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vcvs("E1", 1, 0, 2, 0, 2.0); f.R("R2", 2, 0, 1e4);
            f.Vccs("G1", 2, 0, 3, 0, 1e-3); f.R("R3", 3, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "DC 受控源网络：TryCreate 成功", createError);
            LinearSolution? dc = null;
            string solveError = string.Empty;
            bool dcSolved = model != null && model.TrySolveDc(out dc, out solveError) && dc != null;
            if (dcSolved && model != null && dc != null)
            {
                Check(true, "DC 受控源网络：TrySolveDc 成功");
                string assembleError = string.Empty;
                if (model.TryAssembleDcSystem(out Complex[,]? matrix, out Complex[]? rhs, out assembleError)
                    && matrix != null && rhs != null)
                {
                    (bool ok, Complex[] x, string error) = SolveDense(matrix, rhs, model.MatrixDimension);
                    Check(ok, "DC 受控源网络：审计求解器解引擎装配矩阵", error);
                    if (ok)
                    {
                        double worst = 0.0;
                        string detail = string.Empty;
                        for (int i = 0; i < model.Nodes.Count; i++)
                        {
                            double engineValue = dc.NodeVoltage(model.Nodes[i].NetId);
                            double errorValue = Math.Abs(x[i].Real - engineValue);
                            double relative = errorValue / Math.Max(Math.Abs(engineValue), 1e-9);
                            if (relative > worst)
                            {
                                worst = relative;
                                detail = $"{model.Nodes[i].Name}: matrix-solve {Fmt(x[i].Real)} V vs engine {Fmt(engineValue)} V";
                            }
                        }
                        Check(worst <= 1e-9, "DC 受控源网络：装配矩阵独立解与引擎解一致（≤1e-9）",
                            $"worst rel={Fmt(worst)}; {detail}");
                    }
                }
                else
                {
                    Check(false, "DC 受控源网络：TryAssembleDcSystem 成功", assembleError);
                }
            }
            else
            {
                Check(false, "DC 受控源网络：TrySolveDc 成功", solveError);
            }
        }

        // 21. Controlled-source sign conventions with reversed control/output terminals.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vcvs("E1", 0, 1, 2, 0, 2.0); // V(IN1)-V(IN2) = -V(N1)
            f.R("RL", 2, 0, 1e3);
            RunDcAnalyticCase("DC VCVS 反相控制端", f, Expected(f.NodeCount, (1, 1.0), (2, -2.0)));
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vccs("G1", 1, 0, 0, 2, 2e-3); // I(OUT1->OUT2) = gm*Vin -> injects +2 mA into N2
            f.R("RL", 2, 0, 1e3);
            RunDcAnalyticCase("DC VCCS 输出端反接（OUT1=GND）", f, Expected(f.NodeCount, (1, 1.0), (2, 2.0)));
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e3);
            f.Ccvs("X1", 2, 0, 0, 3, 2e3); // V(OUT1)-V(OUT2) = r*Ictrl, OUT1 = GND
            f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCVS 输出端反接（OUT1=GND）", f,
                Expected(f.NodeCount, (1, 1.0), (2, 0.0), (3, -2.0)));
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e3);
            f.Cccs("X1", 2, 0, 0, 3, 2.0); // I(OUT1->OUT2) = beta*Ictrl, OUT1 = GND
            f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCCS 输出端反接（OUT1=GND）", f,
                Expected(f.NodeCount, (1, 1.0), (2, 0.0), (3, 2.0)));
        }

        // 25. Negative control current sign check for current-controlled sources.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, -1.0); f.R("R1", 1, 2, 1e3);
            f.Ccvs("X1", 2, 0, 3, 0, 2e3); f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCVS 负控制电流", f, Expected(f.NodeCount, (1, -1.0), (2, 0.0), (3, -2.0)));
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, -1.0); f.R("R1", 1, 2, 1e3);
            f.Cccs("X1", 2, 0, 3, 0, 2.0); f.R("RL", 3, 0, 1e3);
            RunDcAnalyticCase("DC CCCS 负控制电流", f, Expected(f.NodeCount, (1, -1.0), (2, 0.0), (3, 2.0)));
        }

        // 27. Negative VCVS gain (inverting amplifier) with a load.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vcvs("E1", 1, 0, 2, 0, -2.5); f.R("RL", 2, 0, 1e3);
            RunDcAnalyticCase("DC VCVS 负增益 μ=-2.5", f, Expected(f.NodeCount, (1, 1.0), (2, -2.5)),
                ("E1", 0.0025));
        }

        // 28. Reversed independent voltage source orientation: pos = GND, neg = node.
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 0, 1, 12.0); f.R("R1", 1, 0, 1e3);
            RunDcAnalyticCase("DC 电压源反向接法（pos=GND）", f, Expected(f.NodeCount, (1, -12.0)),
                ("V1", -0.012));
        }

        // 29. Reversed current source orientation: in = GND, out = node (current pulled out).
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 0, 1, 1e-3); f.R("R1", 1, 0, 1e3);
            RunDcAnalyticCase("DC 电流源反向接法（out→节点）", f, Expected(f.NodeCount, (1, -1.0)));
        }
    }
    // =====================================================================
    // S2: randomized resistive networks, cross-validated against the audit's
    //     independent complex MNA solver (DC potentials, per-node KCL, arbitrary
    //     port Req, Thevenin equivalent and loaded-Thevenin formula).
    // =====================================================================

    /// <summary>
    /// Deterministic random circuit family: 3–8 nets (including GND), connected
    /// resistive spanning tree plus random chords, 0–2 independent sources and
    /// 0–2 controlled sources. Resistance values are log-uniform in 1e-3..1e7 Ω with
    /// deliberate 1e-3 Ω / 1e7 Ω extremes. Candidates whose natural DC system is
    /// singular (e.g. parallel ideal voltage sources) are rejected by the AUDIT
    /// reference solver and re-drawn; that keeps every compared network well posed.
    /// </summary>
    private static Fixture? TryGenerateRandomNetwork(int seed, out string skipReason)
    {
        skipReason = string.Empty;
        var rng = new Random(seed);
        var fixture = new Fixture();
        int totalNodes = rng.Next(3, 9); // 3..8 electrical nets including GND
        for (int node = 1; node < totalNodes; node++) fixture.NewNode();
        int nonGround = totalNodes - 1;

        double RandomResistance()
        {
            double roll = rng.NextDouble();
            if (roll < 0.08) return 1e-3;
            if (roll < 0.16) return 1e7;
            return Math.Pow(10.0, -3.0 + rng.NextDouble() * 10.0);
        }

        var resistorPairs = new HashSet<(int, int)>();
        (int, int) PairKey(int a, int b) => a < b ? (a, b) : (b, a);

        // Spanning tree: guarantees a resistive path from every net to ground.
        for (int node = 1; node <= nonGround; node++)
        {
            int parent = rng.Next(0, node);
            resistorPairs.Add(PairKey(node, parent));
            fixture.R("RT" + node.ToString(Inv), node, parent, RandomResistance());
        }

        // Extra chords create real multi-loop topology.
        int extraResistors = rng.Next(0, 4);
        for (int k = 0; k < extraResistors; k++)
        {
            int a = rng.Next(1, totalNodes);
            int b = rng.Next(0, totalNodes);
            if (a == b) continue;
            if (!resistorPairs.Add(PairKey(a, b))) continue;
            fixture.R("RX" + k.ToString(Inv), a, b, RandomResistance());
        }

        // 0–2 independent sources.
        int independentCount = rng.Next(0, 3);
        var voltageSourceNodes = new HashSet<int>();
        for (int s = 0; s < independentCount; s++)
        {
            bool useVoltageSource = rng.NextDouble() < 0.6;
            if (useVoltageSource)
            {
                int node = rng.Next(1, totalNodes);
                int guard = 0;
                while (!voltageSourceNodes.Add(node) && guard++ < 32) node = rng.Next(1, totalNodes);
                if (!voltageSourceNodes.Contains(node)) continue;
                double value = (0.1 + rng.NextDouble() * 9.9) * (rng.Next(2) == 0 ? 1.0 : -1.0);
                fixture.Vdc("VS" + s.ToString(Inv), node, 0, value);
            }
            else
            {
                int inNet = rng.Next(1, totalNodes);
                int outNet = rng.Next(0, totalNodes);
                if (inNet == outNet) outNet = 0;
                double value = (1e-5 + rng.NextDouble() * 2e-3) * (rng.Next(2) == 0 ? 1.0 : -1.0);
                fixture.Idc("IS" + s.ToString(Inv), inNet, outNet, value);
            }
        }

        // 0–2 controlled sources (all four kinds).
        int controlledCount = rng.Next(0, 3);
        for (int c = 0; c < controlledCount; c++)
        {
            int cp = rng.Next(0, totalNodes);
            int cn = rng.Next(0, totalNodes);
            while (cn == cp) cn = rng.Next(0, totalNodes);
            int op = rng.Next(0, totalNodes);
            int on = rng.Next(0, totalNodes);
            while (on == op) on = rng.Next(0, totalNodes);
            double sign = rng.Next(2) == 0 ? 1.0 : -1.0;
            string id = "XC" + c.ToString(Inv);
            switch (rng.Next(0, 4))
            {
                case 0:
                    fixture.Vcvs(id, cp, cn, op, on, (0.2 + rng.NextDouble() * 8.0) * sign);
                    break;
                case 1:
                    fixture.Vccs(id, cp, cn, op, on, Math.Pow(10.0, -4.0 + rng.NextDouble() * 2.0) * sign);
                    break;
                case 2:
                    fixture.Ccvs(id, cp, cn, op, on, Math.Pow(10.0, 1.0 + rng.NextDouble() * 3.0) * sign);
                    break;
                default:
                    fixture.Cccs(id, cp, cn, op, on, (0.2 + rng.NextDouble() * 4.8) * sign);
                    break;
            }
        }

        RefSolveResult natural = ReferenceDc(fixture.Model);
        if (!natural.Success)
        {
            skipReason = "参考求解器判定自然 DC 系统奇异/病态：" + natural.Error;
            return null;
        }
        double maxMagnitude = 0.0;
        for (int node = 1; node < natural.NodeVoltage.Length; node++)
            maxMagnitude = Math.Max(maxMagnitude, natural.NodeVoltage[node].Magnitude);
        if (!double.IsFinite(maxMagnitude) || maxMagnitude > 1e6)
        {
            skipReason = "参考解量级不可用 max|V|=" + Fmt(maxMagnitude);
            return null;
        }
        return fixture;
    }

    private static void CheckRandomCrossValidation()
    {
        const int target = 48;
        const int maxAttempts = 600;
        int accepted = 0;
        int skipped = 0;
        int withZeroIndependent = 0;
        int withTwoIndependent = 0;
        int withZeroControlled = 0;
        int withTwoControlled = 0;
        int withExtremeLow = 0;
        int withExtremeHigh = 0;
        var skipReasons = new List<string>();

        for (int attempt = 0; attempt < maxAttempts && accepted < target; attempt++)
        {
            int seed = RandomSeedBase + attempt;
            Fixture? fixture = TryGenerateRandomNetwork(seed, out string skipReason);
            if (fixture == null)
            {
                skipped++;
                if (skipReasons.Count < 6) skipReasons.Add($"seed={seed}: {skipReason}");
                continue;
            }

            ElectricalNetlist netlist = fixture.Build();
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            if (model == null)
            {
                Check(false, $"随机网络 seed={seed}：TryCreate 成功（独立参考已解出，说明电路可解）", createError);
                accepted++;
                continue;
            }

            int index = accepted + 1;
            accepted++;

            int independentCount = fixture.Elements.Count(e => e.Type is ElementType.DcVoltageSource
                or ElementType.AcVoltageSource or ElementType.DcCurrentSource or ElementType.AcCurrentSource);
            int controlledCount = fixture.Elements.Count(e => e.Type is ElementType.Vcvs
                or ElementType.Vccs or ElementType.Ccvs or ElementType.Cccs);
            if (independentCount == 0) withZeroIndependent++;
            if (independentCount == 2) withTwoIndependent++;
            if (controlledCount == 0) withZeroControlled++;
            if (controlledCount == 2) withTwoControlled++;
            if (fixture.Elements.Any(e => e.Type == ElementType.Resistor && e.DisplayNumber == 1e-3)) withExtremeLow++;
            if (fixture.Elements.Any(e => e.Type == ElementType.Resistor && e.DisplayNumber == 1e7)) withExtremeHigh++;

            string summary = $"{fixture.NodeCount} 节点, {fixture.Elements.Count} 元件, 独立源 {independentCount}, 受控源 {controlledCount}";

            LinearSolution? dc = null;
            string solveError = string.Empty;
            bool dcSolved = model.TrySolveDc(out dc, out solveError) && dc != null;
            if (!dcSolved || dc == null)
            {
                Check(false, $"随机网络 #{index} seed={seed}：DC 求解成功（{summary}）", solveError);
                continue;
            }
            Check(true, $"随机网络 #{index} seed={seed}：DC 求解成功（{summary}）");

            RefSolveResult referenceDc = ReferenceDc(fixture.Model);
            (double dcRelative, string dcDetail) = CompareNodeVoltages(model, dc, referenceDc, false);
            Check(dcRelative <= 1e-6, $"随机网络 #{index} seed={seed}：DC 节点电位 vs 独立参考 MNA（相对误差 ≤1e-6）",
                $"worst rel={Fmt(dcRelative)}; {dcDetail}");

            (double kclRatio, string kclDetail) = MeasureKcl(model, dc, isAc: false, frequencyHz: 0);
            Check(kclRatio <= 1.0, $"随机网络 #{index} seed={seed}：逐节点 KCL 残差 ≤1e-9", kclDetail);

            // ----- arbitrary port Req (reference test-current method) -----
            var localRng = new Random(unchecked(seed * 486187739));
            int checkedPorts = 0;
            double worstPortRelative = 0.0;
            string portDetail = string.Empty;
            double worstSymmetryRelative = 0.0;
            string symmetryDetail = string.Empty;
            for (int p = 0; p < 4; p++)
            {
                int a = localRng.Next(1, fixture.NodeCount);
                int b = localRng.Next(0, fixture.NodeCount);
                if (b == a) b = 0;
                (bool refOk, double refValue, string refError) = ReferenceEquivalentResistance(fixture.Model, a, b);
                bool engineOk = model.TryGetEquivalentResistance(a, b, out double actual, out string engineError);
                if (!refOk)
                {
                    if (engineOk)
                    {
                        worstPortRelative = double.PositiveInfinity;
                        portDetail = $"Req({a},{b}): 参考求解器奇异（{refError}）但引擎返回 {Fmt(actual)} Ω";
                    }
                    continue;
                }
                if (!engineOk)
                {
                    worstPortRelative = double.PositiveInfinity;
                    portDetail = $"Req({a},{b}): 引擎失败（{engineError}），独立参考值 {Fmt(refValue)} Ω";
                    continue;
                }
                double relative = Math.Abs(actual - refValue) / Math.Max(Math.Abs(refValue), 1e-9);
                if (relative > worstPortRelative)
                {
                    worstPortRelative = relative;
                    portDetail = $"Req({a},{b}) expected {Fmt(refValue)} Ω actual {Fmt(actual)} Ω";
                }
                checkedPorts++;
                if (model.TryGetEquivalentResistance(b, a, out double reverse, out _))
                {
                    double symmetry = Math.Abs(actual - reverse) / Math.Max(Math.Abs(actual), 1e-12);
                    if (symmetry > worstSymmetryRelative)
                    {
                        worstSymmetryRelative = symmetry;
                        symmetryDetail = $"Req({a},{b})={Fmt(actual)} Ω vs Req({b},{a})={Fmt(reverse)} Ω";
                    }
                }
            }
            Check(checkedPorts >= 1 && worstPortRelative <= 1e-6,
                $"随机网络 #{index} seed={seed}：任意端口 Req vs 独立参考（≤1e-6，{checkedPorts} 个端口）", portDetail);
            Check(checkedPorts >= 1 && worstSymmetryRelative <= 1e-9,
                $"随机网络 #{index} seed={seed}：Req(A,B)=Req(B,A) 对称性（≤1e-9）", symmetryDetail);

            // ----- Thevenin equivalent + loaded-Thevenin formula -----
            int ta = localRng.Next(1, fixture.NodeCount);
            int tb = localRng.Next(0, fixture.NodeCount);
            if (tb == ta) tb = 0;
            (bool theveninRefOk, double theveninRefReq, string theveninRefError) =
                ReferenceEquivalentResistance(fixture.Model, ta, tb);
            double theveninExpectedVoc = (referenceDc.NodeVoltage[ta] - referenceDc.NodeVoltage[tb]).Real;
            if (!theveninRefOk)
            {
                Console.WriteLine($"    [INFO] 随机网络 #{index} seed={seed}: 端口 ({ta},{tb}) 直流等效奇异，跳过戴维南检查（{theveninRefError}）");
            }
            else
            {
                bool theveninOk = model.TryGetThevenin(ta, tb, out double vocEngine, out double reqEngine, out string theveninError);
                Check(theveninOk, $"随机网络 #{index} seed={seed}：TryGetThevenin 成功", theveninError);
                if (theveninOk)
                {
                    CheckRel(vocEngine, theveninExpectedVoc, 1e-6,
                        $"随机网络 #{index} seed={seed}：Thévenin Voc 端口({ta},{tb}) vs 独立参考", " V",
                        1e-9 * Math.Max(1.0, Math.Abs(theveninExpectedVoc)));
                    CheckRel(reqEngine, theveninRefReq, 1e-6,
                        $"随机网络 #{index} seed={seed}：Thévenin Req 端口({ta},{tb}) vs 独立参考", " Ω",
                        1e-9 * Math.Max(1.0, Math.Abs(theveninRefReq)));

                    // Add a random load RL and verify V_load = Voc * RL / (Req + RL) with the
                    // independent Voc/Req (engine DC solve of the loaded netlist is compared).
                    double rl = theveninRefReq >= 0
                        ? theveninRefReq * (0.7 + localRng.NextDouble() * 1.5)
                        : Math.Abs(theveninRefReq) * (2.0 + localRng.NextDouble() * 2.0);
                    if (Math.Abs(rl) < 1e-9) rl = 1000.0;
                    if (Math.Abs(theveninRefReq + rl) < 1e-9 * Math.Max(1.0, Math.Abs(theveninRefReq)))
                        rl = Math.Abs(theveninRefReq) * 3.0 + 1.0;
                    double predictedLoad = theveninExpectedVoc * rl / (theveninRefReq + rl);

                    ElectricalNetlist loadedNetlist = fixture.BuildWithExtraResistor("LOAD", ta, tb, rl);
                    LinearNetworkAnalyzer? loadedModel = TryBuildModel(loadedNetlist, out string loadedCreateError);
                    LinearSolution? loadedSolution = null;
                    string loadedSolveError = string.Empty;
                    bool loadedSolved = loadedModel != null
                                        && loadedModel.TrySolveDc(out loadedSolution, out loadedSolveError)
                                        && loadedSolution != null;
                    if (!loadedSolved || loadedSolution == null)
                    {
                        Check(false, $"随机网络 #{index} seed={seed}：加载 RL 后 DC 求解成功",
                            loadedCreateError + " " + loadedSolveError);
                    }
                    else
                    {
                        double actualLoad = loadedSolution.NodeVoltage(ta) - loadedSolution.NodeVoltage(tb);
                        CheckRel(actualLoad, predictedLoad, 1e-6,
                            $"随机网络 #{index} seed={seed}：V_load = Voc·RL/(Req+RL)（RL={Fmt(rl)} Ω）", " V",
                            1e-9 * Math.Max(1.0, Math.Abs(predictedLoad)));
                    }
                }
            }
        }

        Console.WriteLine($"    [INFO] 随机网络：候选={accepted + skipped}，接受={accepted}，参考奇异跳过={skipped}");
        foreach (string reason in skipReasons) Console.WriteLine("    [INFO] 跳过候选 " + reason);

        Check(accepted >= 40, "S2 随机网络交叉验证覆盖数 ≥ 40", $"accepted={accepted}, skipped={skipped}");
        Check(withZeroIndependent >= 1 && withTwoIndependent >= 1,
            "S2 随机网络覆盖 0 个与 2 个独立源",
            $"0源={withZeroIndependent}, 2源={withTwoIndependent}");
        Check(withZeroControlled >= 1 && withTwoControlled >= 1,
            "S2 随机网络覆盖 0 个与 2 个受控源",
            $"0受控={withZeroControlled}, 2受控={withTwoControlled}");
        Check(withExtremeLow >= 1 && withExtremeHigh >= 1,
            "S2 随机网络包含 1e-3 Ω 与 1e7 Ω 极端阻值",
            $"1e-3Ω 网络={withExtremeLow}, 1e7Ω 网络={withExtremeHigh}");
    }
    // =====================================================================
    // S3: arbitrary-port Req / Thevenin / series-parallel analytic values
    // =====================================================================

    private static void CheckPortEquivalents()
    {
        // 1. Series chain: Req = 1k + 2k + 3k = 6k.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 3, 2e3); f.R("R3", 3, 0, 3e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "端口 Req 串联链：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetEquivalentResistance(1, 0, out double re, out string reqError);
                Check(ok, "端口 Req 串联链：求解成功", reqError);
                if (ok) CheckRel(re, 6e3, 1e-6, "端口 Req 串联链 Req(1,0) = 6 kΩ", " Ω");
                bool sym = model.TryGetEquivalentResistance(0, 1, out double reverse, out _);
                Check(sym && Math.Abs(reverse - re) <= 1e-9 * Math.Max(1.0, Math.Abs(re)),
                    "端口 Req 对称性 Req(0,1) = Req(1,0)");
            }
        }

        // 2. Parallel: 1k || 2k || 3k = 6/11 kΩ.
        {
            var f = new Fixture(); f.NewNode();
            f.R("R1", 1, 0, 1e3); f.R("R2", 1, 0, 2e3); f.R("R3", 1, 0, 3e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "端口 Req 并联组：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetEquivalentResistance(1, 0, out double re, out string reqError);
                Check(ok, "端口 Req 并联组：求解成功", reqError);
                if (ok) CheckRel(re, 6000.0 / 11.0, 1e-6, "端口 Req 并联组 Req(1,0) = 6/11 kΩ", " Ω");
            }
        }

        // 3. Series-parallel: (1k + 2k) || 3k = 1.5k.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3); f.R("R3", 1, 0, 3e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "端口 Req 串并联：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetEquivalentResistance(1, 0, out double re, out string reqError);
                Check(ok, "端口 Req 串并联：求解成功", reqError);
                if (ok) CheckRel(re, 1.5e3, 1e-6, "端口 Req 串并联 Req(1,0) = 1.5 kΩ", " Ω");
            }
        }

        // 4. Thevenin of loaded divider: Voc = 8 V, Req = 1k||2k = 666.67 Ω.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "戴维南分压网络：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetThevenin(2, 0, out double voc, out double re, out string thError);
                Check(ok, "戴维南分压网络：TryGetThevenin 成功", thError);
                if (ok)
                {
                    CheckRel(voc, 8.0, 1e-6, "戴维南分压网络 Voc(2,0) = 8 V", " V");
                    CheckRel(re, 1e3 * 2e3 / 3e3, 1e-6, "戴维南分压网络 Req(2,0) = 1k||2k", " Ω");
                    CheckRel(voc / re, 0.012, 1e-6, "诺顿电流 I_N = Voc/Req = 12 mA", " A");
                }
                // Source port with the ideal voltage source zeroed must be a 0 Ω short.
                bool srcOk = model.TryGetEquivalentResistance(1, 0, out double sourcePort, out _);
                Check(srcOk && Math.Abs(sourcePort) <= 1e-9, "电压源端口置零后 Req(1,0) = 0 Ω（理想源短路）",
                    $"actual {Fmt(sourcePort)} Ω");
                // Port between the two non-ground nodes: R1 || R2 = 666.67 Ω.
                bool midOk = model.TryGetEquivalentResistance(1, 2, out double mid, out _);
                Check(midOk && Math.Abs(mid - 1e3 * 2e3 / 3e3) <= 1e-6 * 666.667,
                    "Req(1,2) = R1||R2 = 666.67 Ω", $"actual {Fmt(mid)} Ω");
            }
        }

        // 5. Thevenin with current source: Voc = 1 mA × 1 kΩ = 1 V, Req = 1 kΩ.
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); f.R("R1", 1, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "戴维南电流源网络：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetThevenin(1, 0, out double voc, out double re, out string thError);
                Check(ok, "戴维南电流源网络：TryGetThevenin 成功", thError);
                if (ok)
                {
                    CheckRel(voc, 1.0, 1e-6, "戴维南电流源网络 Voc = 1 V", " V");
                    CheckRel(re, 1e3, 1e-6, "戴维南电流源网络 Req = 1 kΩ", " Ω");
                }
            }
        }

        // 6. Loaded-Thevenin formula with engine DC solve: V_load = Voc*RL/(Req+RL).
        {
            Fixture BuildLoadedDivider(double rl)
            {
                var loaded = new Fixture(); loaded.NewNode(); loaded.NewNode();
                loaded.Vdc("V1", 1, 0, 12.0); loaded.R("R1", 1, 2, 1e3); loaded.R("R2", 2, 0, 2e3);
                loaded.R("LOAD", 2, 0, rl);
                return loaded;
            }

            var bare = new Fixture(); bare.NewNode(); bare.NewNode();
            bare.Vdc("V1", 1, 0, 12.0); bare.R("R1", 1, 2, 1e3); bare.R("R2", 2, 0, 2e3);
            LinearNetworkAnalyzer? model = TryBuildModel(bare.Build(), out string error);
            double voc = 0.0;
            double re = 0.0;
            string theveninError = string.Empty;
            bool ok = model != null && model.TryGetThevenin(2, 0, out voc, out re, out theveninError);
            Check(ok, "戴维南带载公式：基准网络与 Thevenin 求解成功", error + theveninError);
            if (ok)
            {
                foreach (double rl in new[] { 100.0, 666.6667, 1e3, 1e4 })
                {
                    LinearNetworkAnalyzer? loadedModel = TryBuildModel(BuildLoadedDivider(rl).Build(), out string loadError);
                    LinearSolution? loadedSolution = null;
                    string loadSolveError = string.Empty;
                    bool loadedOk = loadedModel != null && loadedModel.TrySolveDc(out loadedSolution, out loadSolveError);
                    Check(loadedOk && loadedSolution != null, $"戴维南带载公式 RL={Fmt(rl)} Ω：加载网络 DC 求解成功", loadError + loadSolveError);
                    if (loadedSolution != null)
                    {
                        double actual = loadedSolution.NodeVoltage(2);
                        double predicted = voc * rl / (re + rl);
                        CheckRel(actual, predicted, 1e-6, $"戴维南带载公式 RL={Fmt(rl)} Ω：V_load = Voc·RL/(Req+RL)", " V");
                    }
                }
            }
        }

        // 7. Driving-point impedance seen by the excitation source (DC): divider -> 2 kΩ.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "驱动点阻抗（DC）：TryCreate 成功", error);
            if (model != null)
            {
                LinearDevice? source = model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.DcVoltageSource);
                Complex z = Complex.Zero;
                string zError = string.Empty;
                bool ok = source != null && model.TryGetDrivingPointImpedance(source!, false, 0, out z, out zError);
                Check(ok, "驱动点阻抗（DC）：TryGetDrivingPointImpedance 成功", zError);
                if (ok) CheckRel(z.Real, 2e3, 1e-6, "驱动点阻抗（DC）R_in = R1+R2 = 2 kΩ", " Ω");
            }
        }

        // 8. AC driving-point impedance of a series RC: Z = R + 1/(jωC).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "驱动点阻抗（AC RC）：TryCreate 成功", error);
            if (model != null)
            {
                LinearDevice? source = model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcVoltageSource);
                Complex z = Complex.Zero;
                string zError = string.Empty;
                bool ok = source != null && model.TryGetDrivingPointImpedance(source!, true, frequency, out z, out zError);
                Check(ok, "驱动点阻抗（AC RC）：TryGetDrivingPointImpedance 成功", zError);
                if (ok)
                {
                    Complex expected = new Complex(1e3, 0.0) + 1.0 / new Complex(0.0, 2.0 * Math.PI * frequency * 1e-6);
                    CheckComplexRel(z, expected, 1e-6, "驱动点阻抗（AC RC）Z = R + 1/(jωC)");
                }
            }
        }

        // 9. Negative equivalent resistance through a negative-gm VCCS (active network).
        {
            var f = new Fixture(); f.NewNode();
            f.R("R1", 1, 0, 1e3);
            f.Vccs("G1", 1, 0, 1, 0, -2e-3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "端口 Req 负阻（负 gm VCCS）：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetEquivalentResistance(1, 0, out double re, out string reqError);
                Check(ok, "端口 Req 负阻（负 gm VCCS）：求解成功", reqError);
                if (ok)
                {
                    CheckRel(re, -1000.0, 1e-6, "端口 Req 负阻 = 1/(1/1k - 2mS) = -1 kΩ", " Ω");
                    var referenceNetwork = new RefNetwork { NodeCount = 2 };
                    referenceNetwork.Add(RefKind.R, "R1", 1e3, 1, 0);
                    referenceNetwork.AddControlled(RefKind.Vccs, "G1", -2e-3, 1, 0, 1, 0);
                    (bool refOk, double refValue, string refError) = ReferenceEquivalentResistance(referenceNetwork, 1, 0);
                    Check(refOk, "端口 Req 负阻：独立参考求解成功", refError);
                    if (refOk) CheckRel(re, refValue, 1e-6, "端口 Req 负阻：引擎与独立参考一致", " Ω");
                }
            }
        }

        // 10. String-based port overloads ("network name" / "GND").
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "字符串端口重载：TryCreate 成功", error);
            if (model != null)
            {
                double expectedReq = 1e3 * 2e3 / 3e3;
                bool reqOk = model.TryGetEquivalentResistance("N2", "GND", out double resistance, out string reqError);
                Check(reqOk, "字符串端口重载：Req(\"N2\",\"GND\") 求解成功", reqError);
                if (reqOk)
                    CheckRel(resistance, expectedReq, 1e-6, "字符串端口重载：Req(\"N2\",\"GND\") = 1k||2k", " Ω");
                bool thOk = model.TryGetThevenin("N2", "GND", out double voc, out double req, out string thError);
                Check(thOk, "字符串端口重载：TryGetThevenin(\"N2\",\"GND\") 求解成功", thError);
                if (thOk)
                {
                    CheckRel(voc, 8.0, 1e-6, "字符串端口重载：Voc = 8 V", " V");
                    CheckRel(req, expectedReq, 1e-6, "字符串端口重载：Req = 666.67 Ω", " Ω");
                }
            }
        }

        // 11. Reversed port order: Voc sign flips, Req stays symmetric.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "端口反序戴维南：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TryGetThevenin(2, 1, out double voc, out double req, out string thError);
                Check(ok, "端口反序戴维南：TryGetThevenin(2,1) 成功", thError);
                if (ok)
                {
                    CheckRel(voc, -4.0, 1e-6, "端口反序戴维南：Voc(2,1) = V2-V1 = -4 V", " V");
                    CheckRel(req, 1e3 * 2e3 / 3e3, 1e-6, "端口反序戴维南：Req(2,1) = Req(1,2)", " Ω");
                }
                bool forward = model.TryGetEquivalentResistance(1, 2, out double forwardReq, out _);
                bool backward = model.TryGetEquivalentResistance(2, 1, out double backwardReq, out _);
                Check(forward && backward && Math.Abs(forwardReq - backwardReq) <= 1e-9 * Math.Max(1.0, Math.Abs(forwardReq)),
                    "端口反序戴维南：Req(1,2) = Req(2,1)", $"{Fmt(forwardReq)} vs {Fmt(backwardReq)} Ω");
            }
        }
    }
    // =====================================================================
    // S4: AC sinusoidal steady-state phasors vs closed-form and independent
    //     complex MNA reference solver.
    // =====================================================================

    private static Complex ImpedanceCapacitor(double omega, double capacitance) =>
        new Complex(0.0, -1.0 / (omega * capacitance));

    private static Complex ImpedanceInductor(double omega, double inductance) =>
        new Complex(0.0, omega * inductance);

    private static Complex TransferRcLowPass(double omega, double r, double c) =>
        1.0 / (1.0 + new Complex(0.0, omega * r * c));

    private static Complex TransferRcHighPass(double omega, double r, double c)
    {
        Complex jwrc = new Complex(0.0, omega * r * c);
        return jwrc / (1.0 + jwrc);
    }

    private static Complex TransferRlAcrossInductor(double omega, double r, double l)
    {
        Complex zl = ImpedanceInductor(omega, l);
        return zl / (new Complex(r, 0.0) + zl);
    }

    /// <summary>
    /// One AC analytic benchmark: engine AC solve success, engine vs independent
    /// complex MNA, node phasor magnitude/phase vs closed form, AC KCL residual.
    /// </summary>
    private static bool RunAcAnalyticCase(string label, Fixture fixture, double frequencyHz,
        params (int Net, Complex Expected)[] expectedNodes)
    {
        ElectricalNetlist netlist = fixture.Build();
        LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
        if (model == null)
        {
            Check(false, label + "：TryCreate 成功", createError);
            return false;
        }
        if (!model.TrySolveAc(frequencyHz, out LinearSolution? solution, out string acError) || solution == null)
        {
            Check(false, label + "：TrySolveAc 成功", acError);
            return false;
        }
        Check(true, label + "：TrySolveAc 成功");

        RefSolveResult reference = ReferenceAc(fixture.Model, frequencyHz);
        if (!reference.Success)
        {
            Check(false, label + "：独立参考复数 MNA 求解成功", reference.Error);
        }
        else
        {
            (double networkRelative, string detail) = CompareNodeVoltages(model, solution, reference, true);
            Check(networkRelative <= 1e-6, label + "：引擎 vs 独立复数 MNA（网络尺度相对误差 ≤1e-6）", detail);
        }

        double worstMagnitudeRelative = 0.0;
        string magnitudeDetail = string.Empty;
        double worstPhaseRatio = 0.0;
        string phaseDetail = string.Empty;
        foreach ((int net, Complex expected) in expectedNodes)
        {
            Complex actual = solution.NodePhasor(net);
            double expectedMagnitude = expected.Magnitude;
            double magnitudeRelative = Math.Abs(actual.Magnitude - expectedMagnitude) /
                                       Math.Max(expectedMagnitude, 1e-12);
            if (magnitudeRelative > worstMagnitudeRelative)
            {
                worstMagnitudeRelative = magnitudeRelative;
                magnitudeDetail = $"N{net}: expected |V|={Fmt(expectedMagnitude)} V ({Cfmt(expected)}), " +
                                  $"actual |V|={Fmt(actual.Magnitude)} V ({Cfmt(actual)})";
            }
            if (expectedMagnitude > 1e-12)
            {
                double actualPhase = PhaseDeg(actual);
                double expectedPhase = PhaseDeg(expected);
                double phaseDifference = Math.Abs(NormalizeDeg(actualPhase - expectedPhase));
                double allowed = 1e-4 * Math.Max(1.0, Math.Abs(expectedPhase));
                double ratio = phaseDifference / allowed;
                if (ratio > worstPhaseRatio)
                {
                    worstPhaseRatio = ratio;
                    phaseDetail = $"N{net}: expected {Fmt(expectedPhase)}°, actual {Fmt(actualPhase)}°, " +
                                  $"diff={Fmt(phaseDifference)}° (allowed {Fmt(allowed)}°)";
                }
            }
        }
        Check(worstMagnitudeRelative <= 1e-4, label + "：节点相量幅值 vs 解析（相对误差 ≤1e-4）", magnitudeDetail);
        Check(worstPhaseRatio <= 1.0, label + "：节点相量相位 vs 解析（相对误差 ≤1e-4）", phaseDetail);

        (double kclRatio, string kclDetail) = MeasureKcl(model, solution, true, frequencyHz);
        Check(kclRatio <= 1.0, label + "：交流相量逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
        return true;
    }

    private static void CheckAcPhasors()
    {
        const double rcCutoff = 1.0 / (2.0 * Math.PI * 1e3 * 1e-6); // 159.1549 Hz

        // A. RC low-pass: N1 = 1 V peak, N2 = 1/(1+jωRC).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, rcCutoff); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            foreach (double frequency in new[] { 10.0, rcCutoff, 1e3, 1e4, 1e5 })
            {
                double omega = 2.0 * Math.PI * frequency;
                RunAcAnalyticCase($"AC RC 低通 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, TransferRcLowPass(omega, 1e3, 1e-6)));
            }
            // AC amplitudes are peak phasors: the source node must be exactly 1 V, not 0.707 V RMS.
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            if (model != null && model.TrySolveAc(1e3, out LinearSolution? ac, out _) && ac != null)
            {
                CheckRel(ac.NodeMagnitude(1), 1.0, 1e-9, "AC 幅值约定：源节点 |V| = 1 V 峰值（非 RMS 0.707 V）", " V");
                CheckRel(ac.NodeMagnitude(2), TransferRcLowPass(2 * Math.PI * 1e3, 1e3, 1e-6).Magnitude, 1e-6,
                    "AC 幅值约定：输出节点 |V| = |H(1kHz)| 峰值", " V");
                // Engine-assembled AC matrix re-solved by the independent audit solver.
                string assembleError = string.Empty;
                if (model.TryAssembleAcSystem(1e3, out Complex[,]? matrix, out Complex[]? rhs, out assembleError)
                    && matrix != null && rhs != null)
                {
                    (bool ok, Complex[] x, string solveError) = SolveDense(matrix, rhs, model.MatrixDimension);
                    Check(ok, "AC RC 低通：审计求解器解引擎 AC 装配矩阵", solveError);
                    if (ok)
                    {
                        double worst = 0.0;
                        for (int i = 0; i < model.Nodes.Count; i++)
                        {
                            Complex engineValue = ac.NodePhasor(model.Nodes[i].NetId);
                            double relative = (x[i] - engineValue).Magnitude / Math.Max(engineValue.Magnitude, 1e-12);
                            worst = Math.Max(worst, relative);
                        }
                        Check(worst <= 1e-9, "AC RC 低通：装配矩阵独立解与引擎相量解一致（≤1e-9）",
                            $"worst rel={Fmt(worst)}");
                    }
                }
                else
                {
                    Check(false, "AC RC 低通：TryAssembleAcSystem 成功", assembleError);
                }
            }
            else
            {
                Check(false, "AC 幅值约定：1 kHz AC 求解成功", error);
            }
        }

        // B. RC high-pass: N2 = jωRC/(1+jωRC).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, rcCutoff); f.C("C1", 1, 2, 1e-6); f.R("R1", 2, 0, 1e3);
            foreach (double frequency in new[] { 10.0, rcCutoff, 1e4 })
            {
                double omega = 2.0 * Math.PI * frequency;
                RunAcAnalyticCase($"AC RC 高通 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, TransferRcHighPass(omega, 1e3, 1e-6)));
            }
        }

        // C. RL across the inductor: corner f = R/(2πL).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double corner = 1e3 / (2.0 * Math.PI * 0.1);
            f.Vac("V1", 1, 0, 1.0, corner); f.R("R1", 1, 2, 1e3); f.L("L1", 2, 0, 0.1);
            foreach (double frequency in new[] { 100.0, corner, 1e4 })
            {
                double omega = 2.0 * Math.PI * frequency;
                RunAcAnalyticCase($"AC RL 电感电压 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, TransferRlAcrossInductor(omega, 1e3, 0.1)));
            }
        }

        // D. Series RLC: V(N2) = (jωL+1/(jωC))/Z, V(N3) = (1/(jωC))/Z.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double resonance = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
            f.Vac("V1", 1, 0, 1.0, resonance);
            f.R("R1", 1, 2, 50.0); f.L("L1", 2, 3, 10e-3); f.C("C1", 3, 0, 1e-6);
            foreach (double frequency in new[] { resonance * 0.5, resonance, resonance * 2.0 })
            {
                double omega = 2.0 * Math.PI * frequency;
                Complex zl = ImpedanceInductor(omega, 10e-3);
                Complex zc = ImpedanceCapacitor(omega, 1e-6);
                Complex z = new Complex(50.0, 0.0) + zl + zc;
                RunAcAnalyticCase($"AC RLC 串联 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, (zl + zc) / z), (3, zc / z));
            }
        }

        // E. Parallel RLC driven through R0: H = Zp/(R0+Zp).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double resonance = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
            f.Vac("V1", 1, 0, 1.0, resonance);
            f.R("R0", 1, 2, 50.0); f.L("L1", 2, 0, 10e-3); f.C("C1", 2, 0, 1e-6);
            foreach (double frequency in new[] { resonance * 0.5, resonance, resonance * 2.0 })
            {
                double omega = 2.0 * Math.PI * frequency;
                Complex zl = ImpedanceInductor(omega, 10e-3);
                Complex zc = ImpedanceCapacitor(omega, 1e-6);
                Complex zp = 1.0 / (1.0 / zl + 1.0 / zc);
                Complex h = zp / (new Complex(50.0, 0.0) + zp);
                RunAcAnalyticCase($"AC RLC 并联 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, h));
            }
        }

        // F. AC network with a VCVS: N2 = 2 V (ideal), N3 = 2/(1+jωRC).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency); f.Vcvs("E1", 1, 0, 2, 0, 2.0);
            f.R("R1", 2, 3, 1e3); f.C("C1", 3, 0, 1e-6);
            double omega = 2.0 * Math.PI * frequency;
            RunAcAnalyticCase("AC VCVS(μ=2)+RC f=1kHz", f, frequency,
                (1, Complex.One), (2, new Complex(2.0, 0.0)), (3, 2.0 * TransferRcLowPass(omega, 1e3, 1e-6)));
        }

        // G. AC network with a VCCS: V3 = -gm/(jωC), V2 = V3 - gm*R1.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency); f.Vccs("G1", 1, 0, 2, 0, 1e-3);
            f.R("R1", 2, 3, 1e3); f.C("C1", 3, 0, 1e-6);
            double omega = 2.0 * Math.PI * frequency;
            Complex v3 = new Complex(0.0, 1e-3 / (omega * 1e-6));
            Complex v2 = v3 - new Complex(1e-3 * 1e3, 0.0);
            RunAcAnalyticCase("AC VCCS(gm=1mS)+RC f=1kHz", f, frequency,
                (1, Complex.One), (2, v2), (3, v3));
        }

        // H. Two independent AC sources at the same frequency (superposition).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency);
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            f.Vac("V2", 3, 0, 2.0, frequency); f.R("R3", 2, 3, 1e3);
            // N2 = (V1/R1 + V2/R3)/(1/R1+1/R2+1/R3) = 1 V.
            RunAcAnalyticCase("AC 双独立交流源叠加", f, frequency,
                (1, Complex.One), (2, new Complex(1.0, 0.0)), (3, new Complex(2.0, 0.0)));
        }

        // I. Mixed DC + AC sources: DC zeroes/opens the AC source; AC shorts the DC source.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("Vdc", 1, 0, 10.0); f.R("R1", 1, 2, 1e3); f.Vac("Vac", 2, 0, 1.0, 1000.0);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC/DC 混合激励：TryCreate 成功", error);
            if (model != null)
            {
                bool dcOk = model.TrySolveDc(out LinearSolution? dc, out string dcError) && dc != null;
                Check(dcOk, "AC/DC 混合激励：DC 模式（AC 源置零/短路）求解成功", dcError);
                if (dcOk && dc != null)
                {
                    CheckRel(dc.NodeVoltage(1), 10.0, 1e-9, "AC/DC 混合激励 DC：N1 = 10 V", " V");
                    CheckRel(dc.NodeVoltage(2), 0.0, 1e-9, "AC/DC 混合激励 DC：N2 = 0 V（AC 源短路）", " V", 1e-12);
                }
                bool acOk = model.TrySolveAc(1000.0, out LinearSolution? ac, out string acError) && ac != null;
                Check(acOk, "AC/DC 混合激励：AC 模式（DC 源置零/短路）求解成功", acError);
                if (acOk && ac != null)
                {
                    CheckRel(ac.NodeMagnitude(1), 0.0, 1e-9, "AC/DC 混合激励 AC：N1 = 0 V（DC 源短路）", " V", 1e-12);
                    CheckRel(ac.NodeMagnitude(2), 1.0, 1e-9, "AC/DC 混合激励 AC：N2 = 1 V", " V");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, ac, true, 1000.0);
                    Check(kclRatio <= 1.0, "AC/DC 混合激励 AC：逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
                else if (model != null)
                {
                    Check(false, "AC/DC 混合激励：AC 求解返回解对象", acError);
                }
            }
        }

        // J. AC current source transimpedance: 1 mA into 1 k -> 1 V peak; transfer gain unit V/A.
        {
            var f = new Fixture(); f.NewNode();
            f.Iac("I1", 1, 0, 1e-3, 1000.0); f.R("R1", 1, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 电流源网络：TryCreate 成功", error);
            if (model != null)
            {
                bool acOk = model.TrySolveAc(1000.0, out LinearSolution? ac, out string acError) && ac != null;
                Check(acOk, "AC 电流源网络：TrySolveAc 成功", acError);
                if (acOk && ac != null)
                {
                    CheckRel(ac.NodeMagnitude(1), 1.0, 1e-9, "AC 电流源 1mA/1k：|V1| = 1 V 峰值", " V");
                }
                Complex gain = Complex.Zero;
                string gainUnit = string.Empty;
                string excitation = string.Empty;
                string gainError = string.Empty;
                bool gainOk = model.TryGetTransferGain(1000.0, 1, 0, 1, 0, out gain, out gainUnit,
                    out excitation, out gainError);
                Check(gainOk, "AC 电流源网络：TryGetTransferGain 成功", gainError);
                if (gainOk)
                {
                    CheckComplexRel(gain, new Complex(1e3, 0.0), 1e-6, "AC 电流源网络：跨阻增益 = 1000 Ω（V/A）");
                    Check(gainUnit == "V/A", "AC 电流源网络：增益单位 = V/A", $"actual '{gainUnit}'");
                    Check(!string.IsNullOrWhiteSpace(excitation), "AC 电流源网络：归一化激励描述非空", excitation);
                }
            }
        }

        // K. Transfer-gain normalization: source amplitude 2.5 V -> gain is H, not 2.5·H.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 2.5, frequency); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "传递函数归一化：TryCreate 成功", error);
            if (model != null)
            {
                Complex h = TransferRcLowPass(2.0 * Math.PI * frequency, 1e3, 1e-6);
                Complex gain = Complex.Zero;
                bool gainOk = model.TryGetTransferGain(frequency, 1, 0, 2, 0, out gain, out _, out _, out _);
                Check(gainOk, "传递函数归一化：TryGetTransferGain 成功");
                if (gainOk) CheckComplexRel(gain, h, 1e-6, "传递函数归一化：|H| = 1/(1+jωRC)（激励归一化为 1 V）");
                if (model.TrySolveAc(frequency, out LinearSolution? ac, out _) && ac != null && gainOk)
                {
                    Complex vout = ac.NodePhasor(2);
                    CheckComplexRel(vout, 2.5 * h, 1e-6, "传递函数归一化：自然 AC 解 Vout = 2.5·H");
                    CheckComplexRel(vout / 2.5, gain, 1e-6, "传递函数归一化：Vout/2.5 = H = 归一化传递增益");
                }
                else
                {
                    Check(false, "传递函数归一化：自然 AC 求解成功");
                }
            }
        }

        // L. AC controlled current sources: CCCS and CCVS.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency); f.R("R1", 1, 2, 1e3);
            f.Cccs("X1", 2, 0, 3, 0, 2.0); f.R("RL", 3, 0, 1e3);
            RunAcAnalyticCase("AC CCCS(β=2) 控制支路短路", f, frequency,
                (1, Complex.One), (2, Complex.Zero), (3, new Complex(-2.0, 0.0)));
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency); f.R("R1", 1, 2, 1e3);
            f.Ccvs("X1", 2, 0, 3, 0, 2e3); f.R("RL", 3, 0, 1e3);
            RunAcAnalyticCase("AC CCVS(r=2kΩ) 控制支路短路", f, frequency,
                (1, Complex.One), (2, Complex.Zero), (3, new Complex(2.0, 0.0)));
        }

        // M. Negative AC source amplitude (180° phase shift).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, -1.0, frequency); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            double omega = 2.0 * Math.PI * frequency;
            RunAcAnalyticCase("AC 负幅值源（等效 180° 相移）", f, frequency,
                (1, new Complex(-1.0, 0.0)), (2, -TransferRcLowPass(omega, 1e3, 1e-6)));
        }

        // N. Extreme (finite) frequencies far from the corner.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            foreach (double frequency in new[] { 1e-3, 1e9 })
            {
                double omega = 2.0 * Math.PI * frequency;
                RunAcAnalyticCase($"AC RC 低通极端频率 f={Fmt(frequency)} Hz", f, frequency,
                    (1, Complex.One), (2, TransferRcLowPass(omega, 1e3, 1e-6)));
            }
        }

        // O. High-Q series RLC exactly at resonance: Z = R = 0.01 Ω, Q = 10^4,
        //    V(C) = -j·10000 V; this stresses catastrophic cancellation between jωL and 1/(jωC).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double resonance = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
            f.Vac("V1", 1, 0, 1.0, resonance);
            f.R("R1", 1, 2, 0.01); f.L("L1", 2, 3, 10e-3); f.C("C1", 3, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 高 Q 串联 RLC 谐振点：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySolveAc(resonance, out LinearSolution? ac, out string acError) && ac != null;
                Check(ok, "AC 高 Q 串联 RLC 谐振点：TrySolveAc 成功", acError);
                if (ok && ac != null)
                {
                    RefSolveResult reference = ReferenceAc(f.Model, resonance);
                    Check(reference.Success, "AC 高 Q 串联 RLC 谐振点：独立参考求解成功", reference.Error);
                    if (reference.Success)
                    {
                        (double networkRelative, string detail) = CompareNodeVoltages(model, ac, reference, true);
                        Check(networkRelative <= 1e-6, "AC 高 Q 串联 RLC 谐振点：引擎 vs 独立复数 MNA（≤1e-6）", detail);
                    }
                    CheckRel(ac.NodeMagnitude(1), 1.0, 1e-9, "AC 高 Q 串联 RLC：源节点 |V| = 1 V", " V");
                    CheckRel(ac.NodeMagnitude(3), 10000.0, 1e-4, "AC 高 Q 串联 RLC：|V(C)| = Q·Vin = 10000 V", " V");
                    CheckPhase(ac.NodePhaseDeg(3), -90.0, 0.01, "AC 高 Q 串联 RLC：∠V(C) = -90°");
                    Check(ac.NodeMagnitude(2) <= 1e-6 * 10000.0,
                        "AC 高 Q 串联 RLC：|V(R)| 的抵消残差 ≤ 1e-6·|V(C)|",
                        $"{Fmt(ac.NodeMagnitude(2))} V");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, ac, true, resonance);
                    Check(kclRatio <= 1.0, "AC 高 Q 串联 RLC：逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
            }
        }

        // P. Mixed AC voltage + current excitation in one network (superposition).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double frequency = 1000.0;
            f.Vac("V1", 1, 0, 1.0, frequency);
            f.Iac("I1", 2, 0, 1e-3, frequency);
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 2e3);
            // N2*(1/1k + 1/2k) = V1/1k + 1 mA -> V2 = (1 mA + 1 mA)/(1.5 mS) = 4/3 V.
            RunAcAnalyticCase("AC 电压源+电流源混合激励", f, frequency,
                (1, Complex.One), (2, new Complex(4.0 / 3.0, 0.0)));
        }

        // Q. TryGetTransferGain with NO independent source on the input port: the API injects
        //    a 1 V ideal test source; verify against the independent reference implementation.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3); f.R("R3", 1, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "传递增益测试源注入：TryCreate 成功", error);
            if (model != null)
            {
                Complex gain = Complex.Zero;
                string gainUnit = string.Empty;
                string excitation = string.Empty;
                string gainError = string.Empty;
                bool ok = model.TryGetTransferGain(1000.0, 2, 0, 1, 0, out gain, out gainUnit,
                    out excitation, out gainError);
                Check(ok, "传递增益测试源注入：TryGetTransferGain 成功", gainError);
                if (ok)
                {
                    // 1 V test source at port (2,GND): V2 = 1 V, V1 = V2/2 = 0.5 V.
                    CheckComplexRel(gain, new Complex(0.5, 0.0), 1e-6,
                        "传递增益测试源注入：H = V1/V_test = 0.5");
                    Check(gainUnit == "V/V", "传递增益测试源注入：增益单位 = V/V", $"actual '{gainUnit}'");
                    Check(excitation.Contains("测试", StringComparison.Ordinal),
                        "传递增益测试源注入：激励描述说明理想测试源", excitation);
                    var referenceNetwork = new RefNetwork { NodeCount = 3 };
                    referenceNetwork.Add(RefKind.R, "R1", 1e3, 1, 2);
                    referenceNetwork.Add(RefKind.R, "R2", 1e3, 2, 0);
                    referenceNetwork.Add(RefKind.R, "R3", 1e3, 1, 0);
                    RefSolveResult normalized = ReferenceNormalized(referenceNetwork, 1000.0, 2, 0);
                    Check(normalized.Success, "传递增益测试源注入：独立参考求解成功", normalized.Error);
                    if (normalized.Success)
                        CheckComplexRel(gain, normalized.NodeVoltage[1] - normalized.NodeVoltage[0], 1e-6,
                            "传递增益测试源注入：与独立参考一致");
                }
            }
        }

        // R. Random AC networks (R/L/C + controlled sources) cross-validated against the
        //    independent complex MNA solver at three frequencies.
        {
            const int target = 12;
            const int maxAttempts = 200;
            int accepted = 0;
            int skipped = 0;
            var skipReasons = new List<string>();
            for (int attempt = 0; attempt < maxAttempts && accepted < target; attempt++)
            {
                int seed = 20262000 + attempt;
                Fixture? fixture = TryGenerateRandomAcNetwork(seed, out string skipReason);
                if (fixture == null)
                {
                    skipped++;
                    if (skipReasons.Count < 4) skipReasons.Add($"seed={seed}: {skipReason}");
                    continue;
                }
                ElectricalNetlist netlist = fixture.Build();
                LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
                if (model == null)
                {
                    Check(false, $"随机 AC 网络 seed={seed}：TryCreate 成功（独立参考已解出）", createError);
                    accepted++;
                    continue;
                }
                int index = accepted + 1;
                accepted++;
                string summary = $"{fixture.NodeCount} 节点, {fixture.Elements.Count} 元件";

                double worstRelative = 0.0;
                string relativeDetail = string.Empty;
                double worstKclRatio = 0.0;
                string kclDetail = string.Empty;
                int comparedFrequencies = 0;
                bool engineRejectedValid = false;
                string rejectionDetail = string.Empty;
                foreach (double frequency in new[] { 100.0, 1000.0, 10000.0 })
                {
                    RefSolveResult referenceAc = ReferenceAc(fixture.Model, frequency);
                    if (!referenceAc.Success) continue;
                    bool engineOk = model.TrySolveAc(frequency, out LinearSolution? ac, out string acError) && ac != null;
                    if (!engineOk || ac == null)
                    {
                        engineRejectedValid = true;
                        rejectionDetail = $"f={Fmt(frequency)} Hz 引擎失败而独立参考成功：{acError}";
                        continue;
                    }
                    (double networkRelative, string detail) = CompareNodeVoltages(model, ac, referenceAc, true);
                    if (networkRelative > worstRelative)
                    {
                        worstRelative = networkRelative;
                        relativeDetail = $"f={Fmt(frequency)} Hz " + detail;
                    }
                    (double kclRatio, string currentKclDetail) = MeasureKcl(model, ac, true, frequency);
                    if (kclRatio > worstKclRatio)
                    {
                        worstKclRatio = kclRatio;
                        kclDetail = $"f={Fmt(frequency)} Hz " + currentKclDetail;
                    }
                    comparedFrequencies++;
                }
                Check(!engineRejectedValid && comparedFrequencies == 3,
                    $"随机 AC 网络 #{index} seed={seed}：三个频点 AC 均求解成功（{summary}）", rejectionDetail);
                Check(comparedFrequencies > 0 && worstRelative <= 1e-6,
                    $"随机 AC 网络 #{index} seed={seed}：节点相量 vs 独立复数 MNA（≤1e-6）", relativeDetail);
                Check(comparedFrequencies > 0 && worstKclRatio <= 1.0,
                    $"随机 AC 网络 #{index} seed={seed}：逐节点交流 KCL 残差 ≤1e-9 相对量级", kclDetail);
            }
            Console.WriteLine($"    [INFO] 随机 AC 网络：接受={accepted}，参考奇异跳过={skipped}");
            foreach (string reason in skipReasons) Console.WriteLine("    [INFO] 跳过 AC 候选 " + reason);
            Check(accepted >= 10, "S4 随机 AC 网络交叉验证覆盖数 ≥ 10", $"accepted={accepted}, skipped={skipped}");
        }
    }

    /// <summary>
    /// Deterministic random AC circuit family: resistive tree/chords + 1 AC voltage source
    /// + 1–3 random C/L branches + 0–1 controlled source. Candidates whose AC system is
    /// singular at the audit frequency (or whose solution is numerically unusable) are
    /// rejected by the INDEPENDENT reference solver and re-drawn.
    /// </summary>
    private static Fixture? TryGenerateRandomAcNetwork(int seed, out string skipReason)
    {
        skipReason = string.Empty;
        var rng = new Random(seed);
        var fixture = new Fixture();
        int totalNodes = rng.Next(3, 8); // 3..7 nets including GND
        for (int node = 1; node < totalNodes; node++) fixture.NewNode();
        int nonGround = totalNodes - 1;

        double LogUniform(double low, double high) =>
            Math.Pow(10.0, Math.Log10(low) + rng.NextDouble() * (Math.Log10(high) - Math.Log10(low)));

        var usedPairs = new HashSet<(int, int)>();
        (int, int) PairKey(int a, int b) => a < b ? (a, b) : (b, a);

        for (int node = 1; node <= nonGround; node++)
        {
            int parent = rng.Next(0, node);
            usedPairs.Add(PairKey(node, parent));
            fixture.R("RT" + node.ToString(Inv), node, parent, LogUniform(1.0, 1e5));
        }
        int extraResistors = rng.Next(0, 3);
        for (int k = 0; k < extraResistors; k++)
        {
            int a = rng.Next(1, totalNodes);
            int b = rng.Next(0, totalNodes);
            if (a == b) continue;
            if (!usedPairs.Add(PairKey(a, b))) continue;
            fixture.R("RX" + k.ToString(Inv), a, b, LogUniform(1.0, 1e5));
        }

        int acNode = rng.Next(1, totalNodes);
        fixture.Vac("VAC", acNode, 0, 0.5 + rng.NextDouble() * 4.5, 1000.0);

        int reactiveCount = rng.Next(1, 4);
        for (int k = 0; k < reactiveCount; k++)
        {
            int a = rng.Next(1, totalNodes);
            int b = rng.Next(0, totalNodes);
            if (a == b) continue;
            if (rng.NextDouble() < 0.5) fixture.C("CX" + k.ToString(Inv), a, b, LogUniform(1e-9, 1e-6));
            else fixture.L("LX" + k.ToString(Inv), a, b, LogUniform(1e-3, 1.0));
        }

        if (rng.NextDouble() < 0.5)
        {
            int cp = rng.Next(0, totalNodes);
            int cn = rng.Next(0, totalNodes);
            while (cn == cp) cn = rng.Next(0, totalNodes);
            int op = rng.Next(0, totalNodes);
            int on = rng.Next(0, totalNodes);
            while (on == op) on = rng.Next(0, totalNodes);
            double sign = rng.Next(2) == 0 ? 1.0 : -1.0;
            switch (rng.Next(0, 4))
            {
                case 0:
                    fixture.Vcvs("XC", cp, cn, op, on, (0.2 + rng.NextDouble() * 5.0) * sign);
                    break;
                case 1:
                    fixture.Vccs("XC", cp, cn, op, on, Math.Pow(10.0, -4.0 + rng.NextDouble() * 2.0) * sign);
                    break;
                case 2:
                    fixture.Ccvs("XC", cp, cn, op, on, Math.Pow(10.0, 1.0 + rng.NextDouble() * 2.0) * sign);
                    break;
                default:
                    fixture.Cccs("XC", cp, cn, op, on, (0.2 + rng.NextDouble() * 4.8) * sign);
                    break;
            }
        }

        RefSolveResult reference = ReferenceAc(fixture.Model, 1000.0);
        if (!reference.Success)
        {
            skipReason = "参考 AC 求解奇异/病态：" + reference.Error;
            return null;
        }
        double maxMagnitude = 0.0;
        for (int node = 1; node < reference.NodeVoltage.Length; node++)
            maxMagnitude = Math.Max(maxMagnitude, reference.NodeVoltage[node].Magnitude);
        if (!double.IsFinite(maxMagnitude) || maxMagnitude > 1e6 || maxMagnitude < 1e-12)
        {
            skipReason = "参考 AC 解量级不可用 max|V|=" + Fmt(maxMagnitude);
            return null;
        }
        return fixture;
    }
    // =====================================================================
    // S5: AC frequency sweep (61 points), direct-AC-consistency and -3 dB cutoffs
    // =====================================================================

    private static void CheckAcSweep()
    {
        const double sweepStart = 10.0;
        const double sweepStop = 1e7;
        const int pointsPerDecade = 10;

        // A. RC low-pass sweep: 61 points, sampled points match the direct AC solve
        //    and the closed form; the upper -3 dB cutoff must be 1/(2πRC) within 5 %.
        var rc = new Fixture(); rc.NewNode(); rc.NewNode();
        double rcCutoff = 1.0 / (2.0 * Math.PI * 1e3 * 1e-6);
        rc.Vac("V1", 1, 0, 1.0, rcCutoff); rc.R("R1", 1, 2, 1e3); rc.C("C1", 2, 0, 1e-6);
        LinearNetworkAnalyzer? rcModel = TryBuildModel(rc.Build(), out string rcCreateError);
        Check(rcModel != null, "AC 扫频 RC 低通：TryCreate 成功", rcCreateError);
        if (rcModel != null)
        {
            AcSweepResult? sweep = null;
            bool sweepOk = rcModel.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade, out sweep, out string sweepError);
            Check(sweepOk && sweep != null, "AC 扫频 RC 低通：TrySweep 成功", sweepError);
            if (sweepOk && sweep != null)
            {
                Check(sweep.Points.Count == 61, "AC 扫频 RC 低通：点数 = 61",
                    $"actual {sweep.Points.Count}");
                CheckRel(sweep.Points[0].FrequencyHz, sweepStart, 1e-12, "AC 扫频 RC 低通：首点频率 = 10 Hz", " Hz");
                CheckRel(sweep.Points[^1].FrequencyHz, sweepStop, 1e-12, "AC 扫频 RC 低通：末点频率 = 10 MHz", " Hz");
                bool monotonic = true;
                for (int i = 1; i < sweep.Points.Count; i++)
                    if (sweep.Points[i].FrequencyHz <= sweep.Points[i - 1].FrequencyHz) monotonic = false;
                Check(monotonic, "AC 扫频 RC 低通：频率单调递增");

                int[] samples = { 0, 10, 20, 30, 40, 50, 60 };
                double worstAnalytic = 0.0;
                string analyticDetail = string.Empty;
                double worstDirect = 0.0;
                string directDetail = string.Empty;
                int compared = 0;
                foreach (int index in samples)
                {
                    AcSweepPoint point = sweep.Points[index];
                    double frequency = point.FrequencyHz;
                    Complex expected = TransferRcLowPass(2.0 * Math.PI * frequency, 1e3, 1e-6);
                    double analyticError = (point.Gain - expected).Magnitude / Math.Max(expected.Magnitude, 1e-12);
                    if (analyticError > worstAnalytic)
                    {
                        worstAnalytic = analyticError;
                        analyticDetail = $"f={Fmt(frequency)} Hz expected {Cfmt(expected)} actual {Cfmt(point.Gain)}";
                    }
                    if (rcModel.TrySolveAc(frequency, out LinearSolution? direct, out _) && direct != null)
                    {
                        Complex directGain = direct.NodePhasor(2);
                        double directError = (point.Gain - directGain).Magnitude / Math.Max(directGain.Magnitude, 1e-12);
                        if (directError > worstDirect)
                        {
                            worstDirect = directError;
                            directDetail = $"f={Fmt(frequency)} Hz sweep {Cfmt(point.Gain)} direct-AC {Cfmt(directGain)}";
                        }
                        compared++;
                    }
                }
                Check(compared >= 5, "AC 扫频 RC 低通：至少抽 5 点与直接 AC 解对比", $"compared={compared}");
                Check(worstAnalytic <= 1e-4, "AC 扫频 RC 低通：抽样点 vs 解析 H(jω)（相对误差 ≤1e-4）", analyticDetail);
                Check(worstDirect <= 1e-9, "AC 扫频 RC 低通：抽样点与直接 TrySolveAc 一致（≤1e-9）", directDetail);

                // Derived properties of one point.
                AcSweepPoint probe = sweep.Points[20];
                CheckRel(probe.GainMagnitude, probe.Gain.Magnitude, 1e-12, "AC 扫频 RC 低通：GainMagnitude 与 |Gain| 一致");
                CheckRel(probe.GainDb, 20.0 * Math.Log10(probe.Gain.Magnitude), 1e-12,
                    "AC 扫频 RC 低通：GainDb = 20·log10|Gain|");
                CheckPhase(probe.PhaseDeg, PhaseDeg(probe.Gain), 1e-9, "AC 扫频 RC 低通：PhaseDeg 与 Gain 相位一致");

                Check(sweep.UpperCutoffHz.HasValue, "AC 扫频 RC 低通：检测到 -3 dB 上截止频率");
                if (sweep.UpperCutoffHz.HasValue)
                {
                    CheckRel(sweep.UpperCutoffHz.Value, rcCutoff, 0.05,
                        "AC 扫频 RC 低通：上截止频率 ≈ 1/(2πRC)（误差 ≤5%）", " Hz");
                }
                Check(!sweep.LowerCutoffHz.HasValue, "AC 扫频 RC 低通：低频端无 -3 dB 截止点（通带至 DC）");
                Check(sweep.BandwidthHz.HasValue && sweep.UpperCutoffHz.HasValue &&
                      Math.Abs(sweep.BandwidthHz.Value - sweep.UpperCutoffHz.Value) <= 1e-9 * sweep.UpperCutoffHz.Value,
                    "AC 扫频 RC 低通：带宽 = 上截止频率（低端按 DC 估算）");
            }
        }

        // B. RC high-pass sweep: lower cutoff = 1/(2πRC), no upper cutoff.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, rcCutoff); f.C("C1", 1, 2, 1e-6); f.R("R1", 2, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频 RC 高通：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null, "AC 扫频 RC 高通：TrySweep 成功", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points.Count == 61, "AC 扫频 RC 高通：点数 = 61", $"actual {sweep.Points.Count}");
                    Check(sweep.LowerCutoffHz.HasValue, "AC 扫频 RC 高通：检测到 -3 dB 下截止频率");
                    if (sweep.LowerCutoffHz.HasValue)
                    {
                        CheckRel(sweep.LowerCutoffHz.Value, rcCutoff, 0.05,
                            "AC 扫频 RC 高通：下截止频率 ≈ 1/(2πRC)（误差 ≤5%）", " Hz");
                    }
                    Check(!sweep.UpperCutoffHz.HasValue, "AC 扫频 RC 高通：高频端无 -3 dB 截止点");
                }
            }
        }

        // C. RL across the inductor: high-pass response, lower cutoff ≈ R/(2πL).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            double corner = 1e3 / (2.0 * Math.PI * 0.1);
            f.Vac("V1", 1, 0, 1.0, corner); f.R("R1", 1, 2, 1e3); f.L("L1", 2, 0, 0.1);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频 RL：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null, "AC 扫频 RL：TrySweep 成功", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points.Count == 61, "AC 扫频 RL：点数 = 61", $"actual {sweep.Points.Count}");
                    Check(sweep.LowerCutoffHz.HasValue, "AC 扫频 RL：检测到 -3 dB 下截止频率");
                    if (sweep.LowerCutoffHz.HasValue)
                    {
                        CheckRel(sweep.LowerCutoffHz.Value, corner, 0.05,
                            "AC 扫频 RL：下截止频率 ≈ R/(2πL)（误差 ≤5%）", " Hz");
                    }
                    Check(!sweep.UpperCutoffHz.HasValue, "AC 扫频 RL：高频端无 -3 dB 截止点");
                }
            }
        }

        // D. Series-RLC band-pass: both -3 dB cutoffs and bandwidth from the analytic roots.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            double resonance = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
            f.Vac("V1", 1, 0, 1.0, resonance);
            f.R("R1", 1, 2, 50.0); f.L("L1", 2, 3, 10e-3); f.C("C1", 3, 0, 1e-6);
            double r = 50.0, l = 10e-3, c = 1e-6;
            double root = Math.Sqrt(r * r * c * c + 4.0 * l * c);
            double omegaHigh = (r * c + root) / (2.0 * l * c);
            double omegaLow = (-r * c + root) / (2.0 * l * c);
            double expectedHigh = omegaHigh / (2.0 * Math.PI);
            double expectedLow = omegaLow / (2.0 * Math.PI);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频 RLC 串联带通：TryCreate 成功", error);
            if (model != null)
            {
                // Output port across R1: gain = V(N2) - V(N1) = -V_R.
                bool ok = model.TrySweep(1, 0, 2, 1, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null, "AC 扫频 RLC 串联带通：TrySweep 成功", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points.Count == 61, "AC 扫频 RLC 串联带通：点数 = 61", $"actual {sweep.Points.Count}");
                    double peak = sweep.Points.Max(p => p.GainMagnitude);
                    CheckRel(peak, 1.0, 0.01, "AC 扫频 RLC 串联带通：谐振点峰值 |H| ≈ 1", "");
                    Check(sweep.LowerCutoffHz.HasValue, "AC 扫频 RLC 串联带通：检测到下截止频率");
                    Check(sweep.UpperCutoffHz.HasValue, "AC 扫频 RLC 串联带通：检测到上截止频率");
                    if (sweep.LowerCutoffHz.HasValue)
                    {
                        CheckRel(sweep.LowerCutoffHz.Value, expectedLow, 0.05,
                            "AC 扫频 RLC 串联带通：下截止频率 vs 解析根（≤5%）", " Hz");
                    }
                    if (sweep.UpperCutoffHz.HasValue)
                    {
                        CheckRel(sweep.UpperCutoffHz.Value, expectedHigh, 0.05,
                            "AC 扫频 RLC 串联带通：上截止频率 vs 解析根（≤5%）", " Hz");
                    }
                    if (sweep.BandwidthHz.HasValue)
                    {
                        CheckRel(sweep.BandwidthHz.Value, expectedHigh - expectedLow, 0.05,
                            "AC 扫频 RLC 串联带通：带宽 vs 解析值（≤5%）", " Hz");
                    }
                    else
                    {
                        Check(false, "AC 扫频 RLC 串联带通：带宽可用");
                    }
                }
            }
        }

        // E. Current-source input port: transimpedance sweep with 61 points, gain unit V/A.
        {
            var f = new Fixture(); f.NewNode();
            f.Iac("I1", 1, 0, 1e-3, 1000.0); f.R("R1", 1, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频电流源端口：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 1, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null, "AC 扫频电流源端口：TrySweep 成功", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points.Count == 61, "AC 扫频电流源端口：点数 = 61", $"actual {sweep.Points.Count}");
                    Check(sweep.GainUnit == "V/A", "AC 扫频电流源端口：增益单位 = V/A", $"actual '{sweep.GainUnit}'");
                    Check(sweep.GainQuantityLabel.Contains("跨阻", StringComparison.Ordinal),
                        "AC 扫频电流源端口：增益量说明为跨阻", sweep.GainQuantityLabel);
                    double worst = 0.0;
                    foreach (int index in new[] { 0, 15, 30, 45, 60 })
                    {
                        double gainError = Math.Abs(sweep.Points[index].GainMagnitude - 1000.0);
                        worst = Math.Max(worst, gainError);
                    }
                    Check(worst <= 1e-6, "AC 扫频电流源端口：|跨阻| = 1000 Ω（抽样点）", $"worst |err|={Fmt(worst)} Ω");
                    Check(!sweep.LowerCutoffHz.HasValue && !sweep.UpperCutoffHz.HasValue,
                        "AC 扫频电流源端口：通带覆盖全范围（无截止频率）");
                }
            }
        }

        // F. Invalid sweep requests must fail with a non-empty reason (no silent zero).
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频非法参数：TryCreate 成功", error);
            if (model != null)
            {
                bool zeroStart = model.TrySweep(1, 0, 2, 0, 0.0, 1e6, 10, out _, out string e1);
                Check(!zeroStart && !string.IsNullOrWhiteSpace(e1), "AC 扫频非法参数：起始频率 0 被拒绝且消息非空", e1);
                bool reversed = model.TrySweep(1, 0, 2, 0, 1e6, 10.0, 10, out _, out string e2);
                Check(!reversed && !string.IsNullOrWhiteSpace(e2), "AC 扫频非法参数：终止 ≤ 起始被拒绝且消息非空", e2);
                bool zeroPpd = model.TrySweep(1, 0, 2, 0, 10, 1e6, 0, out _, out string e3);
                Check(!zeroPpd && !string.IsNullOrWhiteSpace(e3), "AC 扫频非法参数：每十倍频程点数 0 被拒绝且消息非空", e3);
                bool tooMany = model.TrySweep(1, 0, 2, 0, 1.0, 1e9, 10000, out _, out string e4);
                Check(!tooMany && !string.IsNullOrWhiteSpace(e4), "AC 扫频非法参数：超过 10000 点被拒绝且消息非空", e4);
                bool badNet = model.TrySweep(1, 0, 99, 0, 10, 1e6, 10, out _, out string e5);
                Check(!badNet && !string.IsNullOrWhiteSpace(e5), "AC 扫频非法参数：不存在的输出节点被拒绝且消息非空", e5);
            }
        }

        // H. Audit regression (fixed engine behaviour): a -3 dB corner far BELOW the fixed
        //    10 Hz sweep start must NOT fabricate a cutoff inside a window that lies entirely
        //    in the stopband. The passband reference is confirmed by out-of-window probes;
        //    when the window does not contain the passband the estimate is marked unavailable.
        {
            double fc = 0.1;
            double capacitance = 1.0 / (2.0 * Math.PI * 1e3 * fc);
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, fc); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, capacitance);
            ElectricalNetlist netlist = f.Build();
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string error);
            Check(model != null, "越界截止回归（fc=0.1 Hz）：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null && sweep.Points.Count == 61,
                    "越界截止回归（fc=0.1 Hz）：扫频成功返回 61 点", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points[0].GainDb <= -39.0,
                        "越界截止回归（fc=0.1 Hz）：扫描下限 10 Hz 处增益已 ≈ -40 dB（窗口全在阻带）",
                        $"10Hz gain={Fmt(sweep.Points[0].GainDb)} dB");
                    CheckAbs(sweep.ReferenceGainDb, 0.0, 0.5,
                        "越界截止回归（fc=0.1 Hz）：通带参考增益由窗口外探针确定为 ≈0 dB", " dB");
                    Check(!sweep.PassbandInWindow, "越界截止回归（fc=0.1 Hz）：窗口内不含通带");
                    Check(!sweep.CutoffEstimateAvailable,
                        "越界截止回归（fc=0.1 Hz）：截止估计标记为不可用", sweep.CutoffUnavailableReason);
                    Check(!sweep.LowerCutoffHz.HasValue && !sweep.UpperCutoffHz.HasValue &&
                          !sweep.BandwidthHz.HasValue,
                        "越界截止回归（fc=0.1 Hz）：Lower/Upper/Bandwidth 全部为 null（不再伪造 ≈14.1 Hz）",
                        $"lower={sweep.LowerCutoffHz}, upper={sweep.UpperCutoffHz}, bw={sweep.BandwidthHz}");
                    Check(sweep.CutoffUnavailableReason.Contains(
                              "扫频窗口未覆盖真实 -3 dB 截止频率，截止频率与带宽估计不可用", StringComparison.Ordinal),
                        "越界截止回归（fc=0.1 Hz）：不可用原因写明窗口未覆盖真实 -3 dB 截止频率",
                        sweep.CutoffUnavailableReason);
                    Check(sweep.Notes.Any(n => n.Contains("扫频窗口未覆盖真实", StringComparison.Ordinal)) &&
                          sweep.Notes.Any(n => n.Contains("估计不可用", StringComparison.Ordinal)),
                        "越界截止回归（fc=0.1 Hz）：Notes 说明窗口未覆盖真实截止/估计不可用",
                        string.Join("；", sweep.Notes));
                    Check(!sweep.Notes.Any(n => n.Contains("仍处于通带", StringComparison.Ordinal)),
                        "越界截止回归（fc=0.1 Hz）：不再声称窗口边缘“仍处于通带”",
                        string.Join("；", sweep.Notes));
                    Console.WriteLine($"    [INFO] 越界截止回归：10Hz增益={sweep.Points[0].GainDb.ToString("0.###", Inv)} dB，" +
                                      $"ReferenceGainDb={sweep.ReferenceGainDb.ToString("0.#####", Inv)} dB，" +
                                      $"PassbandInWindow={sweep.PassbandInWindow}，CutoffEstimateAvailable={sweep.CutoffEstimateAvailable}，" +
                                      $"lower={sweep.LowerCutoffHz?.ToString(Inv) ?? "null"}，upper={sweep.UpperCutoffHz?.ToString(Inv) ?? "null"}，" +
                                      $"bw={sweep.BandwidthHz?.ToString(Inv) ?? "null"}");
                }

                // Same scenario through MnaLinearStrategy: add a second (negligible) parallel
                // capacitor so the general linear MNA strategy path is exercised like the UI.
                var strategyFixture = new Fixture(); strategyFixture.NewNode(); strategyFixture.NewNode();
                strategyFixture.Vac("V1", 1, 0, 1.0, fc); strategyFixture.R("R1", 1, 2, 1e3);
                strategyFixture.C("C1", 2, 0, capacitance); strategyFixture.C("C2", 2, 0, 1e-6);
                PedagogicalSolution outsideSolution = RunStrategy(strategyFixture.Build(), fc);
                Check(outsideSolution.IsSuccess && outsideSolution.LinearNetwork != null,
                    "越界截止回归（结构化）：MnaLinearStrategy 求解成功", outsideSolution.SummaryText);
                FrequencyResponseInfo? outsideResponse = outsideSolution.LinearNetwork?.FrequencyResponse;
                Check(outsideResponse != null, "越界截止回归（结构化）：仍生成工作频率频响",
                    outsideSolution.SummaryText);
                if (outsideResponse != null && outsideSolution.LinearNetwork != null)
                {
                    Check(!outsideResponse.LowerCutoffHz.HasValue && !outsideResponse.UpperCutoffHz.HasValue &&
                          !outsideResponse.BandwidthHz.HasValue,
                        "越界截止回归（结构化）：截止/带宽字段全部为 null",
                        $"lower={outsideResponse.LowerCutoffHz}, upper={outsideResponse.UpperCutoffHz}, bw={outsideResponse.BandwidthHz}");
                    Check(outsideResponse.LowerCutoffDisplay == "未检测到（超出扫描范围）" &&
                          outsideResponse.UpperCutoffDisplay == "未检测到（超出扫描范围）" &&
                          outsideResponse.BandwidthDisplay == "未检测到（超出扫描范围）",
                        "越界截止回归（结构化）：Display = “未检测到（超出扫描范围）”",
                        $"{outsideResponse.LowerCutoffDisplay} / {outsideResponse.UpperCutoffDisplay} / {outsideResponse.BandwidthDisplay}");
                    Check(outsideResponse.BoundaryNote.Contains(
                              "扫频窗口未覆盖真实 -3 dB 截止频率，截止频率与带宽估计不可用", StringComparison.Ordinal),
                        "越界截止回归（结构化）：FrequencyResponse.BoundaryNote 写明窗口未覆盖真实截止",
                        outsideResponse.BoundaryNote);
                    Check(outsideSolution.LinearNetwork.BoundaryNote.Contains("截止估计不可用", StringComparison.Ordinal),
                        "越界截止回归（结构化）：LinearNetwork.BoundaryNote 说明截止估计不可用",
                        outsideSolution.LinearNetwork.BoundaryNote);
                    AcParametersInfo outsideAc = outsideSolution.AcParameters ?? new AcParametersInfo();
                    Check(outsideAc.UpperCutoffFreq == "未检测到（超出扫描范围）" &&
                          outsideAc.Bandwidth == "未检测到（超出扫描范围）",
                        "越界截止回归（结构化）：AcParameters 截止/带宽显示同步",
                        $"upper={outsideAc.UpperCutoffFreq}, bw={outsideAc.Bandwidth}");
                    Console.WriteLine($"    [INFO] 越界截止回归（结构化）：display='{outsideResponse.LowerCutoffDisplay}'/" +
                                      $"'{outsideResponse.UpperCutoffDisplay}'/'{outsideResponse.BandwidthDisplay}'，" +
                                      $"AcParameters.upper='{outsideAc.UpperCutoffFreq}'，BoundaryNote 含“扫频窗口未覆盖真实 -3 dB 截止频率，截止频率与带宽估计不可用”={outsideResponse.BoundaryNote.Contains("扫频窗口未覆盖真实 -3 dB 截止频率，截止频率与带宽估计不可用", StringComparison.Ordinal)}");
                }
            }
        }

        // H2. Negative control: a cutoff INSIDE the sweep window is still detected normally
        //     (guards against a "cutoff detection disabled everywhere" regression).
        {
            double fc = 1.0 / (2.0 * Math.PI * 1e3 * 1e-7); // R=1 kΩ, C=100 nF -> 1591.55 Hz
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, fc); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-7);
            ElectricalNetlist netlist = f.Build();
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string error);
            Check(model != null, "窗口内截止对照组（fc≈1.59 kHz）：TryCreate 成功", error);
            AcSweepResult? inWindowSweep = null;
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null && sweep.Points.Count == 61,
                    "窗口内截止对照组（fc≈1.59 kHz）：扫频成功返回 61 点", sweepError);
                if (ok && sweep != null)
                {
                    inWindowSweep = sweep;
                    Check(sweep.CutoffEstimateAvailable && sweep.PassbandInWindow,
                        "窗口内截止对照组（fc≈1.59 kHz）：截止估计可用且窗口含通带",
                        sweep.CutoffUnavailableReason);
                    Check(sweep.UpperCutoffHz.HasValue && !sweep.LowerCutoffHz.HasValue,
                        "窗口内截止对照组（fc≈1.59 kHz）：检出上截止、无下截止",
                        $"lower={sweep.LowerCutoffHz}, upper={sweep.UpperCutoffHz}");
                    if (sweep.UpperCutoffHz.HasValue)
                        CheckRel(sweep.UpperCutoffHz.Value, fc, 0.05,
                            "窗口内截止对照组（fc≈1.59 kHz）：上截止 ≈ 1/(2πRC)（≤5%）", " Hz");
                    Check(sweep.BandwidthHz.HasValue && sweep.UpperCutoffHz.HasValue &&
                          Math.Abs(sweep.BandwidthHz.Value - sweep.UpperCutoffHz.Value) <=
                          1e-9 * sweep.UpperCutoffHz.Value,
                        "窗口内截止对照组（fc≈1.59 kHz）：带宽 = 上截止（低端至 DC）");
                    Check(!sweep.LowerCutoffBelowWindow && !sweep.UpperCutoffAboveWindow,
                        "窗口内截止对照组（fc≈1.59 kHz）：未被误判为窗口外交叉");
                }
                PedagogicalSolution inWindowSolution = RunStrategy(netlist, fc);
                FrequencyResponseInfo? inWindowResponse = inWindowSolution.LinearNetwork?.FrequencyResponse;
                Check(inWindowSolution.IsSuccess && inWindowResponse != null,
                    "窗口内截止对照组（结构化）：MnaLinearStrategy 求解成功且生成频响",
                    inWindowSolution.SummaryText);
                if (inWindowResponse != null)
                {
                    Check(inWindowResponse.UpperCutoffHz.HasValue,
                        "窗口内截止对照组（结构化）：FrequencyResponse 上截止频率可用",
                        inWindowResponse.UpperCutoffDisplay);
                    if (inWindowResponse.UpperCutoffHz.HasValue)
                        CheckRel(inWindowResponse.UpperCutoffHz.Value, fc, 0.05,
                            "窗口内截止对照组（结构化）：上截止 ≈ 1/(2πRC)（≤5%）", " Hz");
                    Check(inWindowResponse.LowerCutoffDisplay == "未检测到",
                        "窗口内截止对照组（结构化）：低频端显示“未检测到”（通带至 DC，非越界）",
                        inWindowResponse.LowerCutoffDisplay);
                    Check(inWindowResponse.UpperCutoffDisplay.Contains("Hz", StringComparison.Ordinal),
                        "窗口内截止对照组（结构化）：上截止显示带 Hz", inWindowResponse.UpperCutoffDisplay);
                }
                Console.WriteLine($"    [INFO] 窗口内截止对照组：解析 fc={fc.ToString("0.###", Inv)} Hz，" +
                                  $"sweep upper={(inWindowSweep?.UpperCutoffHz?.ToString("0.###", Inv) ?? "null")} Hz，" +
                                  $"sweep display=lower:'{inWindowResponse?.LowerCutoffDisplay}'/" +
                                  $"upper:'{inWindowResponse?.UpperCutoffDisplay}'，" +
                                  $"bw={inWindowSweep?.BandwidthHz?.ToString("0.###", Inv) ?? "null"} Hz");
            }
        }

        // G. Voltage-source input port swept to itself: flat unity gain, no cutoffs.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            if (model != null && model.TrySweep(1, 0, 1, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out _) && sweep != null)
            {
                double worst = sweep.Points.Max(p => Math.Abs(p.GainMagnitude - 1.0));
                Check(worst <= 1e-9, "AC 扫频电压源端口对自身：|H| ≡ 1", $"worst |err|={Fmt(worst)}");
                Check(!sweep.LowerCutoffHz.HasValue && !sweep.UpperCutoffHz.HasValue,
                    "AC 扫频电压源端口对自身：无截止频率");
            }
            else
            {
                Check(false, "AC 扫频电压源端口对自身：TrySweep 成功", error);
            }
        }

        // I. Sweep on a network with NO independent source on the input port: the engine has
        //    to inject a normalized 1 V ideal test source at every frequency.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string error);
            Check(model != null, "AC 扫频测试源注入：TryCreate 成功", error);
            if (model != null)
            {
                bool ok = model.TrySweep(1, 0, 2, 0, sweepStart, sweepStop, pointsPerDecade,
                    out AcSweepResult? sweep, out string sweepError);
                Check(ok && sweep != null, "AC 扫频测试源注入：TrySweep 成功", sweepError);
                if (ok && sweep != null)
                {
                    Check(sweep.Points.Count == 61, "AC 扫频测试源注入：点数 = 61", $"actual {sweep.Points.Count}");
                    Check(sweep.GainUnit == "V/V", "AC 扫频测试源注入：增益单位 V/V", sweep.GainUnit);
                    var referenceNetwork = new RefNetwork { NodeCount = 3 };
                    referenceNetwork.Add(RefKind.R, "R1", 1e3, 1, 2);
                    referenceNetwork.Add(RefKind.R, "R2", 1e3, 2, 0);
                    double worst = 0.0;
                    string detail = string.Empty;
                    int compared = 0;
                    foreach (int index in new[] { 0, 15, 30, 45, 60 })
                    {
                        RefSolveResult normalized = ReferenceNormalized(referenceNetwork,
                            sweep.Points[index].FrequencyHz, 1, 0);
                        if (!normalized.Success) continue;
                        Complex expected = normalized.NodeVoltage[2] - normalized.NodeVoltage[0];
                        Complex actual = Complex.FromPolarCoordinates(sweep.Points[index].GainMagnitude,
                            sweep.Points[index].PhaseDeg * Math.PI / 180.0);
                        double relative = (actual - expected).Magnitude / Math.Max(expected.Magnitude, 1e-12);
                        if (relative > worst)
                        {
                            worst = relative;
                            detail = $"f={Fmt(sweep.Points[index].FrequencyHz)} Hz expected {Cfmt(expected)} actual {Cfmt(actual)}";
                        }
                        compared++;
                    }
                    Check(compared >= 5 && worst <= 1e-6,
                        "AC 扫频测试源注入：抽样点 = 1V 测试源归一化增益（分压 0.5，≤1e-6）", detail);
                    Check(!sweep.LowerCutoffHz.HasValue && !sweep.UpperCutoffHz.HasValue,
                        "AC 扫频测试源注入：平坦通带无截止频率");
                }
            }
        }
    }
    // =====================================================================
    // S6: boundary conditions, singular systems and rejection behaviour
    // =====================================================================

    private static Fixture BuildResistorLadder(int nonGroundNodes, double sourceVolts)
    {
        var fixture = new Fixture();
        for (int node = 1; node <= nonGroundNodes; node++) fixture.NewNode();
        fixture.Vdc("V1", 1, 0, sourceVolts);
        for (int node = 1; node < nonGroundNodes; node++) fixture.R("R" + node.ToString(Inv), node, node + 1, 1e3);
        fixture.R("R" + nonGroundNodes.ToString(Inv), nonGroundNodes, 0, 1e3);
        return fixture;
    }

    private static bool TryCreateFails(string label, ElectricalNetlist netlist, out string error)
    {
        bool created = LinearNetworkAnalyzer.TryCreate(netlist, out LinearNetworkAnalyzer? model, out error);
        Check(!created && model == null && !string.IsNullOrWhiteSpace(error), label, error);
        return !created;
    }

    private static void CheckRejectReason(string label, string error, params string[] keywords)
    {
        bool matches = keywords.Length == 0 || keywords.Any(k => error.Contains(k, StringComparison.Ordinal));
        Check(matches, label + "：失败原因包含预期关键词（" + string.Join("/", keywords) + "）", $"error='{error}'");
    }

    private static void CheckDcSolveFails(string label, LinearNetworkAnalyzer model, params string[] keywords)
    {
        bool ok = model.TrySolveDc(out LinearSolution? solution, out string error);
        Check(!ok && solution == null && !string.IsNullOrWhiteSpace(error),
            label + "：DC 求解明确失败且消息非空", error);
        if (keywords.Length > 0)
        {
            bool matches = keywords.Any(k => error.Contains(k, StringComparison.Ordinal));
            Check(matches, label + "：失败原因包含预期关键词（" + string.Join("/", keywords) + "）", $"error='{error}'");
        }
    }

    private static void CheckAcSolveFails(string label, LinearNetworkAnalyzer model, double frequency,
        params string[] keywords)
    {
        bool ok = model.TrySolveAc(frequency, out LinearSolution? solution, out string error);
        Check(!ok && solution == null && !string.IsNullOrWhiteSpace(error),
            label + "：AC 求解明确失败且消息非空", error);
        if (keywords.Length > 0)
        {
            bool matches = keywords.Any(k => error.Contains(k, StringComparison.Ordinal));
            Check(matches, label + "：失败原因包含预期关键词（" + string.Join("/", keywords) + "）", $"error='{error}'");
        }
    }

    private static void CheckBoundaryRejection()
    {
        // 1. Null / empty / missing-ground netlists.
        TryCreateFails("拒绝：null 网表", null!, out _);
        TryCreateFails("拒绝：空网表", new ElectricalNetlist(), out string emptyError);
        CheckRejectReason("拒绝：空网表", emptyError, "没有", "空");

        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            Check(TryCreateFails("拒绝：缺少参考地", f.Build(markGround: false), out string groundError),
                "拒绝：缺少参考地失败原因非空");
            CheckRejectReason("拒绝：缺少参考地", groundError, "地", "GND");
        }

        {
            var f = new Fixture();
            f.Element("GND1", ElementType.Ground, 0.0, string.Empty);
            f.Pin("GND1", "GND", 0);
            Check(TryCreateFails("拒绝：仅接地符号", f.Build(), out string onlyGroundError),
                "拒绝：仅接地符号失败原因非空");
            CheckRejectReason("拒绝：仅接地符号", onlyGroundError, "元件", "线性");
        }

        // 2. Unsupported nonlinear / port devices are rejected explicitly.
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 0, 1e3);
            f.Element("D1", ElementType.Diode, 0.7, string.Empty);
            f.Pin("D1", "A", 1); f.Pin("D1", "K", 0);
            Check(TryCreateFails("拒绝：二极管（非线性）", f.Build(), out string diodeError),
                "拒绝：二极管失败原因非空");
            CheckRejectReason("拒绝：二极管", diodeError, "二极管", "不支持");
        }
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 0, 1e3);
            f.Element("VCC1", ElementType.VccPort, 5.0, "V");
            f.Pin("VCC1", "VCC", 1);
            Check(TryCreateFails("拒绝：VCC 电源端口", f.Build(), out string vccError),
                "拒绝：VCC 端口失败原因非空");
            CheckRejectReason("拒绝：VCC 端口", vccError, "VCC", "不支持");
        }

        // 3. Missing / invalid device terminal nets.
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 0, 1e3);
            f.Element("R2", ElementType.Resistor, 1e3, "Ω"); // no Pin() registration on purpose
            Check(TryCreateFails("拒绝：元件引脚未连接网络", f.Build(), out string pinError),
                "拒绝：引脚未连接失败原因非空");
            CheckRejectReason("拒绝：引脚未连接", pinError, "未连接", "网络");
        }

        // 4. Singular DC systems: floating current-source node and capacitor-blocked DC path.
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); // injection at node 1 with no DC conductance
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "拒绝：悬空电流源节点 TryCreate 成功（失败发生在求解阶段）", createError);
            if (model != null) CheckDcSolveFails("拒绝：悬空电流源节点", model, "奇异", "病态");
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 10.0); f.C("C1", 1, 2, 1e-6); // node 2 has no DC path
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "拒绝：电容开路致 DC 无通路 TryCreate 成功", createError);
            if (model != null) CheckDcSolveFails("拒绝：电容开路致 DC 无通路", model, "奇异", "病态", "直流通路");
        }

        // 5. Contradictory ideal voltage sources (parallel and anti-series loop).
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 5.0); f.Vdc("V2", 1, 0, 7.0);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "拒绝：矛盾并联电压源 TryCreate 成功", createError);
            if (model != null) CheckDcSolveFails("拒绝：矛盾并联电压源", model, "奇异", "病态", "矛盾");
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 5.0); f.Vdc("V2", 0, 1, 3.0);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "拒绝：矛盾反串电压源回路 TryCreate 成功", createError);
            if (model != null) CheckDcSolveFails("拒绝：矛盾反串电压源回路", model, "奇异", "病态");
        }

        // 6. Invalid R/C/L values and invalid units are rejected at TryCreate.
        var invalidCases = new (string Label, ElementType Type, double Display, string Unit, string[] Keywords)[]
        {
            ("R = 0", ElementType.Resistor, 0.0, "Ω", new[] { "大于 0", "数值" }),
            ("R = -1 kΩ", ElementType.Resistor, -1.0, "kΩ", new[] { "大于 0", "数值" }),
            ("C = -1 μF", ElementType.Capacitor, -1.0, "μF", new[] { "大于 0", "数值" }),
            ("L = 0 H", ElementType.Inductor, 0.0, "H", new[] { "大于 0", "数值" }),
            ("R 单位非法 'X'", ElementType.Resistor, 1.0, "X", new[] { "单位", "无效" }),
            ("C 单位非法 'Ω'", ElementType.Capacitor, 1.0, "Ω", new[] { "单位", "无效" }),
            ("电压源数值 NaN", ElementType.DcVoltageSource, double.NaN, "V", new[] { "有限", "数值" })
        };
        foreach ((string label, ElementType type, double display, string unit, string[] keywords) in invalidCases)
        {
            var f = new Fixture(); f.NewNode();
            f.R("R1", 1, 0, 1e3);
            f.Element("X1", type, display, unit);
            string positivePin = type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "pos" : "1";
            string negativePin = type is ElementType.DcVoltageSource or ElementType.AcVoltageSource ? "neg" : "2";
            f.Pin("X1", positivePin, 1); f.Pin("X1", negativePin, 0);
            Check(TryCreateFails("拒绝：非法元件值 " + label, f.Build(), out string valueError),
                "拒绝：非法元件值 " + label + " 失败原因非空");
            CheckRejectReason("拒绝：非法元件值 " + label, valueError, keywords);
        }

        // 7. Unknown controlled-source unit: warning (non-fatal) + documented ×1 treatment.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.ControlledRaw("G1", ElementType.Vccs, 2.0, "xyz", 1, 0, 2, 0, 2.0);
            f.R("RL", 2, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "受控源未知单位：TryCreate 成功（仅提示）", createError);
            if (model != null)
            {
                Check(model.BoundaryNote.Contains("未识别", StringComparison.Ordinal) &&
                      model.BoundaryNote.Contains("×1", StringComparison.Ordinal),
                    "受控源未知单位：BoundaryNote 含未识别/×1 提示", model.BoundaryNote);
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "受控源未知单位：DC 求解成功", solveError);
                if (ok && dc != null)
                    CheckRel(dc.NodeVoltage(2), -2000.0, 1e-6,
                        "受控源未知单位按 ×1 处理：V2 = -2 S × 1 V × 1 kΩ = -2000 V", " V");
            }
        }

        // 8. Documented controlled-source unit scalings.
        var unitCases = new (string Label, ElementType Type, double Display, string Unit, int OutNode, double Expected)[]
        {
            ("VCCS 2 mS", ElementType.Vccs, 2.0, "mS", 2, -2.0),
            ("VCCS 1 uS", ElementType.Vccs, 1.0, "uS", 2, -1e-3),
            ("VCCS 1 μS", ElementType.Vccs, 1.0, "\u03BCS", 2, -1e-3),
            ("VCCS 2 S", ElementType.Vccs, 2.0, "S", 2, -2000.0),
            ("VCVS 2 倍", ElementType.Vcvs, 2.0, "倍", 2, 2.0),
            ("VCVS 2 V（未识别单位→×1，仅提示）", ElementType.Vcvs, 2.0, "V", 2, 2.0),
            ("CCVS 1 kΩ", ElementType.Ccvs, 1.0, "kΩ", 3, 1.0),
            ("CCCS 2 倍", ElementType.Cccs, 2.0, "倍", 3, -2.0)
        };
        foreach ((string label, ElementType type, double display, string unit, int outNode, double expected) in unitCases)
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            if (type is ElementType.Ccvs or ElementType.Cccs)
            {
                // Current-controlled types need a real control current: 1 V / 1 kΩ = 1 mA.
                f.R("R1", 1, 2, 1e3);
                f.ControlledRaw("X1", type, display, unit, 2, 0, outNode, 0, display);
                f.R("RL", outNode, 0, 1e3);
            }
            else
            {
                f.ControlledRaw("X1", type, display, unit, 1, 0, outNode, 0, display);
                f.R("RL", outNode, 0, 1e3);
            }
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "受控源单位换算 " + label + "：TryCreate 成功", createError);
            if (model == null) continue;
            bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
            Check(ok, "受控源单位换算 " + label + "：DC 求解成功", solveError);
            if (ok && dc != null)
                CheckRel(dc.NodeVoltage(outNode), expected, 1e-6, "受控源单位换算 " + label + "：输出 = " + Fmt(expected) + " V", " V");
        }

        // 9. MNA variable limit: 256 variables accepted, 257 rejected explicitly.
        {
            Fixture over = BuildResistorLadder(256, 10.0); // 256 nodes + 1 source branch = 257
            Check(TryCreateFails("规模：257 个 MNA 变量被拒绝", over.Build(), out string limitError),
                "规模：257 个 MNA 变量失败原因非空");
            CheckRejectReason("规模：257 个 MNA 变量", limitError, "上限", "超过");
        }
        {
            Fixture atLimit = BuildResistorLadder(255, 10.0); // 255 nodes + 1 branch = 256
            LinearNetworkAnalyzer? model = TryBuildModel(atLimit.Build(), out string createError);
            Check(model != null && model.MatrixDimension == 256,
                "规模：256 个 MNA 变量（上限边界）可建模", createError + $" dim={model?.MatrixDimension}");
            if (model != null)
            {
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "规模：256 阶 MNA 求解成功", solveError);
                if (ok && dc != null)
                {
                    int last = 255;
                    double current = 10.0 / (last * 1e3);
                    CheckRel(dc.NodeVoltage(1), 10.0, 1e-9, "规模：256 阶梯形网络 v(1) = 10 V", " V");
                    CheckRel(dc.NodeVoltage(128), 10.0 * (last + 1 - 128) / last, 1e-6, "规模：256 阶梯形网络 v(128)", " V");
                    CheckRel(dc.NodeVoltage(last), current * 1e3, 1e-6, "规模：256 阶梯形网络 v(255) = 10/255 V", " V");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, dc, false, 0);
                    Check(kclRatio <= 1.0, "规模：256 阶 MNA 逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
            }
        }

        // 10. Near-singular controlled-source constraint must be rejected, not solved into nonsense.
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.ControlledRaw("E1", ElementType.Vcvs, 1.0 + 1e-12, "倍", 1, 0, 1, 0, 1.0 + 1e-12);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "病态：近退化 VCVS 约束 TryCreate 成功", createError);
            if (model != null) CheckDcSolveFails("病态：近退化 VCVS 约束", model, "奇异", "病态", "残差");
        }

        // 11. Extreme but well-posed resistance ratio still solves accurately.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 2, 1e-3); f.R("R2", 2, 0, 1e7);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "极端阻值比 1e-3:1e7 分压：TryCreate 成功", createError);
            if (model != null)
            {
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "极端阻值比 1e-3:1e7 分压：DC 求解成功", solveError);
                if (ok && dc != null)
                {
                    double expected = 1e7 / (1e7 + 1e-3);
                    CheckRel(dc.NodeVoltage(2), expected, 1e-6, "极端阻值比 1e-3:1e7 分压：V2 = R2/(R1+R2)", " V");
                }
            }
        }

        // 12. AC frequency validation and ideal-LC resonance singularity.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "AC 频率校验：TryCreate 成功", createError);
            if (model != null)
            {
                bool zero = model.TrySolveAc(0.0, out _, out string e0);
                Check(!zero && !string.IsNullOrWhiteSpace(e0), "AC 频率校验：0 Hz 被拒绝且消息非空", e0);
                bool negative = model.TrySolveAc(-1000.0, out _, out string e1);
                Check(!negative && !string.IsNullOrWhiteSpace(e1), "AC 频率校验：负频率被拒绝且消息非空", e1);
                bool nan = model.TrySolveAc(double.NaN, out _, out string e2);
                Check(!nan && !string.IsNullOrWhiteSpace(e2), "AC 频率校验：NaN 频率被拒绝且消息非空", e2);
            }
        }
        {
            var f = new Fixture(); f.NewNode();
            double resonance = 1.0 / (2.0 * Math.PI * Math.Sqrt(10e-3 * 1e-6));
            f.Iac("I1", 1, 0, 1e-3, resonance); f.L("L1", 1, 0, 10e-3); f.C("C1", 1, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "AC 理想 LC 谐振：TryCreate 成功", createError);
            if (model != null)
            {
                CheckAcSolveFails("AC 理想 LC 谐振点（奇异）", model, resonance, "奇异", "病态", "主元");
                LinearSolution? offResonance = null;
                bool offOk = model.TrySolveAc(resonance * 0.5, out offResonance, out string offError) && offResonance != null;
                Check(offOk, "AC 理想 LC 非谐振点：求解成功", offError);
                if (offOk && offResonance != null)
                {
                    double omega = 2.0 * Math.PI * resonance * 0.5;
                    Complex expected = new Complex(1e-3, 0.0) /
                                        (new Complex(0.0, omega * 1e-6) + 1.0 / new Complex(0.0, omega * 10e-3));
                    CheckComplexRel(offResonance.NodePhasor(1), expected, 1e-6,
                        "AC 理想 LC 非谐振点：V = I/(jωC + 1/(jωL))");
                }
            }
        }

        // 13. API argument validation and net-token resolution.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "API 参数校验：TryCreate 成功", createError);
            if (model != null)
            {
                bool badReq = model.TryGetEquivalentResistance(99, 0, out _, out string reqError);
                Check(!badReq && !string.IsNullOrWhiteSpace(reqError), "API 参数校验：Req 非法节点被拒绝且消息非空", reqError);
                Complex gain = Complex.Zero;
                bool badGain = model.TryGetTransferGain(-1.0, 1, 0, 2, 0, out gain, out _, out _, out string gainError);
                Check(!badGain && !string.IsNullOrWhiteSpace(gainError), "API 参数校验：传递增益非法频率被拒绝且消息非空", gainError);
                bool tokenGround = model.TryResolveNetToken("GND", out int groundId, out _) && groundId == 0;
                Check(tokenGround, "API 参数校验：\"GND\" 解析为参考地 NetId=0");
                bool tokenName = model.TryResolveNetToken("N2", out int net2, out _) && net2 == 2;
                Check(tokenName, "API 参数校验：网络名 \"N2\" 解析成功");
                bool tokenBad = model.TryResolveNetToken("no_such_node", out _, out string tokenError);
                Check(!tokenBad && !string.IsNullOrWhiteSpace(tokenError), "API 参数校验：未知网络名被拒绝且消息非空", tokenError);
            }
        }

        // 14. Reference ground is identified by the IsGround flag, not by NetId = 0.
        {
            var netlist = new ElectricalNetlist();
            var elementV = new CircuitElement
            {
                Id = "V1", Name = "V1", Type = ElementType.DcVoltageSource,
                DisplayNumber = 12.0, UnitSymbol = "V"
            };
            elementV.InitializeDefaultPins();
            var elementR1 = new CircuitElement
            {
                Id = "R1", Name = "R1", Type = ElementType.Resistor,
                DisplayNumber = 1.0, UnitSymbol = "kΩ"
            };
            elementR1.InitializeDefaultPins();
            var elementR2 = new CircuitElement
            {
                Id = "R2", Name = "R2", Type = ElementType.Resistor,
                DisplayNumber = 1.0, UnitSymbol = "kΩ"
            };
            elementR2.InitializeDefaultPins();
            netlist.Elements["V1"] = elementV;
            netlist.Elements["R1"] = elementR1;
            netlist.Elements["R2"] = elementR2;

            ElectricalNet MakeNet(int id, string name, bool ground)
            {
                var net = new ElectricalNet { NetId = id, Name = name, IsGround = ground };
                netlist.Nets.Add(net);
                netlist.Nodes.Add(new ElectricalNode { Index = id, Name = name, Net = net });
                return net;
            }
            ElectricalNet netA = MakeNet(0, "A", false);
            ElectricalNet netB = MakeNet(1, "B", false);
            ElectricalNet netGround = MakeNet(7, "GND", true);
            netA.ConnectedPins.Add(new ComponentPinRef("V1", "pos", "pos"));
            netA.ConnectedPins.Add(new ComponentPinRef("R1", "1", "1"));
            netB.ConnectedPins.Add(new ComponentPinRef("R1", "2", "2"));
            netB.ConnectedPins.Add(new ComponentPinRef("R2", "1", "1"));
            netGround.ConnectedPins.Add(new ComponentPinRef("V1", "neg", "neg"));
            netGround.ConnectedPins.Add(new ComponentPinRef("R2", "2", "2"));

            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "非零 NetId 参考地：TryCreate 成功", createError);
            if (model != null)
            {
                Check(model.GroundNetId == 7, "非零 NetId 参考地：GroundNetId = 7", $"actual {model.GroundNetId}");
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "非零 NetId 参考地：TrySolveDc 成功", solveError);
                if (ok && dc != null)
                {
                    CheckRel(dc.NodeVoltage(0), 12.0, 1e-9, "非零 NetId 参考地：V(A) = 12 V", " V");
                    CheckRel(dc.NodeVoltage(1), 6.0, 1e-9, "非零 NetId 参考地：V(B) = 6 V", " V");
                    bool reqOk = model.TryGetEquivalentResistance("B", "GND", out double re, out string reqError);
                    Check(reqOk, "非零 NetId 参考地：Req(\"B\",\"GND\") 求解成功", reqError);
                    // With the source zeroed (A shorted to GND) the B–GND port sees R1 || R2.
                    if (reqOk) CheckRel(re, 500.0, 1e-6, "非零 NetId 参考地：Req = R1||R2 = 500 Ω", " Ω");
                    bool thOk = model.TryGetThevenin("B", "GND", out double voc, out double req, out string thError);
                    Check(thOk, "非零 NetId 参考地：TryGetThevenin 成功", thError);
                    if (thOk)
                    {
                        CheckRel(voc, 6.0, 1e-6, "非零 NetId 参考地：Voc = 6 V", " V");
                        CheckRel(req, 500.0, 1e-6, "非零 NetId 参考地：Thevenin Req = 500 Ω", " Ω");
                    }
                }
            }
        }

        // 15. Component terminals shorted onto the same net are rejected explicitly.
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.R("R1", 1, 1, 1e3); // both terminals on net 1
            Check(TryCreateFails("拒绝：元件两端短接同一网络", f.Build(), out string shortError),
                "拒绝：元件两端短接失败原因非空");
            CheckRejectReason("拒绝：元件两端短接", shortError, "短路", "同一");
        }

        // 16. AC frequency whose radian frequency overflows double must fail explicitly.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "AC 频率溢出：TryCreate 成功", createError);
            if (model != null)
            {
                bool ok = model.TrySolveAc(1e308, out _, out string overflowError);
                Check(!ok && !string.IsNullOrWhiteSpace(overflowError),
                    "AC 频率溢出（ω=∞）：明确失败且消息非空", overflowError);
            }
        }

        // 17. LinearSolution / ToDecibel API contract guards.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            LinearNetworkAnalyzer? model = TryBuildModel(f.Build(), out string createError);
            Check(model != null, "API 契约：TryCreate 成功", createError);
            if (model != null)
            {
                bool acOk = model.TrySolveAc(1000.0, out LinearSolution? acSolution, out _) && acSolution != null;
                bool dcOk = model.TrySolveDc(out LinearSolution? dcSolution, out _) && dcSolution != null;
                Check(acOk && dcOk, "API 契约：AC/DC 求解成功");
                if (acOk && acSolution != null)
                {
                    CheckThrows<InvalidOperationException>("API 契约：AC 相量解调用 NodeVoltage() 抛出 InvalidOperationException",
                        () => acSolution.NodeVoltage(1));
                    CheckThrows<InvalidOperationException>("API 契约：AC 相量解调用 BranchCurrentAmpere() 抛出 InvalidOperationException",
                        () => acSolution.BranchCurrentAmpere("V1"));
                    Check(acSolution.IsAc && Math.Abs(acSolution.FrequencyHz - 1000.0) <= 1e-12,
                        "API 契约：AC 解标记 IsAc / FrequencyHz");
                }
                if (dcOk && dcSolution != null)
                {
                    Check(!dcSolution.IsAc, "API 契约：DC 解标记 IsAc = false");
                    bool branchOk = true;
                    try { dcSolution.BranchCurrentAmpere("V1"); } catch { branchOk = false; }
                    Check(branchOk, "API 契约：DC 解可用 BranchCurrentAmpere() 读取支路电流");
                }
                CheckRel(LinearNetworkAnalyzer.ToDecibel(10.0), 20.0, 1e-12, "API 契约：ToDecibel(10) = 20 dB");
                CheckRel(LinearNetworkAnalyzer.ToDecibel(0.0), -300.0, 1e-12, "API 契约：ToDecibel(0) = -300 dB");
                CheckRel(LinearNetworkAnalyzer.ToDecibel(-1.0), -300.0, 1e-12, "API 契约：ToDecibel(负数) = -300 dB");
                CheckRel(LinearNetworkAnalyzer.ToDecibel(double.PositiveInfinity), -300.0, 1e-12,
                    "API 契约：ToDecibel(∞) = -300 dB");
            }
        }
    }
    // =====================================================================
    // S7: cooperative cancellation semantics and matrix/element scale limits
    // =====================================================================

    private static void CheckThrows<TException>(string label, Action action) where TException : Exception
    {
        try
        {
            action();
            Check(false, label, "未抛出异常，调用正常返回");
        }
        catch (TException)
        {
            Check(true, label);
        }
        catch (Exception ex)
        {
            Check(false, label, "实际抛出 " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void CheckThrowsCancellation(string label, Action action)    {
        try
        {
            action();
            Check(false, label + "：抛出 OperationCanceledException", "未抛出异常，调用正常返回");
        }
        catch (OperationCanceledException)
        {
            Check(true, label + "：抛出 OperationCanceledException");
        }
        catch (Exception ex)
        {
            Check(false, label + "：抛出 OperationCanceledException", "实际抛出 " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void CheckCancellationAndScale()
    {
        var f = new Fixture(); f.NewNode(); f.NewNode();
        f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
        ElectricalNetlist netlist = f.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // 1. Already-cancelled token must throw at every cooperative boundary, never be
        //    silently converted into a zero/failure result.
        CheckThrowsCancellation("取消语义：已取消 token 的 TryCreate",
            () => LinearNetworkAnalyzer.TryCreate(netlist, out _, out _, cts.Token));

        LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
        Check(model != null, "取消语义：基准网络 TryCreate 成功", createError);
        if (model != null)
        {
            CheckThrowsCancellation("取消语义：已取消 token 的 TrySolveDc",
                () => model.TrySolveDc(out _, out _, cts.Token));
            CheckThrowsCancellation("取消语义：已取消 token 的 TryAssembleDcSystem",
                () => model.TryAssembleDcSystem(out _, out _, out _, cts.Token));
            CheckThrowsCancellation("取消语义：已取消 token 的 TrySolveAc",
                () => model.TrySolveAc(1000.0, out _, out _, cts.Token));
            CheckThrowsCancellation("取消语义：已取消 token 的 TrySweep",
                () => model.TrySweep(1, 0, 2, 0, 10.0, 1e6, 10, out _, out _, cts.Token));
            CheckThrowsCancellation("取消语义：已取消 token 的 TryGetEquivalentResistance",
                () => model.TryGetEquivalentResistance(1, 0, out _, out _, cts.Token));

            // Un-cancelled token must remain a normal success path.
            using var live = new CancellationTokenSource();
            bool liveOk = model.TrySolveDc(out LinearSolution? liveSolution, out _, live.Token) && liveSolution != null;
            Check(liveOk, "取消语义：未取消 token 的 TrySolveDc 正常成功");
        }

        // 2. Strategy cancellation: MnaLinearStrategy.SolveAsync observes the token first.
        {
            var strategy = new MnaLinearStrategy();
            var parameters = new AnalysisParameters { OperatingFrequency = 1000.0 };
            CheckThrowsCancellation("取消语义：已取消 token 的 MnaLinearStrategy.SolveAsync",
                () => strategy.SolveAsync(netlist, parameters, cts.Token).GetAwaiter().GetResult());
        }

        // 3. 512-element scale behaviour: the engine rejects oversized MNA systems with an
        //    explicit variable-limit message instead of hanging or silently truncating.
        {
            Fixture big = BuildResistorLadder(512, 10.0); // 512 nodes + 1 branch = 513 variables
            Check(big.Elements.Count >= 512, "规模：512 元件网络构造成功", $"elements={big.Elements.Count}");
            bool created = LinearNetworkAnalyzer.TryCreate(big.Build(), out LinearNetworkAnalyzer? bigModel, out string bigError);
            Check(!created && bigModel == null && !string.IsNullOrWhiteSpace(bigError),
                "规模：512 元件（513 变量）网络被显式拒绝", bigError);
            Check(bigError.Contains("上限", StringComparison.Ordinal) ||
                  bigError.Contains("超过", StringComparison.Ordinal),
                "规模：512 元件拒绝消息说明变量数上限", bigError);
        }

        // 4. 256-variable AC network scale probe: full MnaLinearStrategy pipeline (AC solve +
        //    61-point sweep + arbitrary-port Req solves). The UI wraps solving in a 15 s
        //    timeout, so elapsed time is measured and reported explicitly.
        {
            var big = new Fixture();
            for (int node = 1; node <= 255; node++) big.NewNode();
            big.Vac("V1", 1, 0, 1.0, 1000.0);
            for (int node = 1; node < 255; node++) big.R("RS" + node.ToString(Inv), node, node + 1, 1e3);
            big.R("RS255", 255, 0, 1e3);
            for (int node = 1; node <= 255; node++) big.C("CP" + node.ToString(Inv), node, 0, 1e-9);
            ElectricalNetlist bigNetlist = big.Build();
            Check(bigNetlist.Elements.Count == 511, "规模：256 变量 AC 网络元素数 = 511", $"actual {bigNetlist.Elements.Count}");

            var createWatch = Stopwatch.StartNew();
            LinearNetworkAnalyzer? bigModel = TryBuildModel(bigNetlist, out string bigCreateError);
            createWatch.Stop();
            Check(bigModel != null && bigModel.MatrixDimension == 256,
                "规模：256 变量 AC 网络 TryCreate 成功", bigCreateError + $" dim={bigModel?.MatrixDimension}");
            if (bigModel != null)
            {
                var sweepWatch = Stopwatch.StartNew();
                bool sweepOk = bigModel.TrySweep(1, 0, 2, 0, 10.0, 1e7, 10, out AcSweepResult? sweep, out string sweepError);
                sweepWatch.Stop();
                Check(sweepOk && sweep != null && sweep.Points.Count == 61,
                    "规模：256 变量 AC 网络 61 点扫频成功", sweepError);
                Console.WriteLine($"    [INFO] 256 变量 AC 网络：TryCreate={createWatch.Elapsed.TotalMilliseconds:F0} ms，" +
                                  $"TrySweep(61 点)={sweepWatch.Elapsed.TotalMilliseconds:F0} ms");

                var strategyWatch = Stopwatch.StartNew();
                PedagogicalSolution strategyResult = RunStrategy(bigNetlist, 1000.0);
                strategyWatch.Stop();
                Check(strategyResult.IsSuccess, "规模：256 变量 AC 网络 MnaLinearStrategy 全流程成功",
                    strategyResult.SummaryText);
                Console.WriteLine($"    [INFO] 256 变量 AC 网络：MnaLinearStrategy 全流程（含 61 点扫频+端口等效）=" +
                                  $"{strategyWatch.Elapsed.TotalMilliseconds:F0} ms（UI 求解超时 15000 ms）");
                Check(strategyWatch.Elapsed.TotalMilliseconds < 15000.0,
                    "规模：256 变量 AC 网络 MnaLinearStrategy 完成时间 < UI 15 s 超时",
                    $"{strategyWatch.Elapsed.TotalMilliseconds:F0} ms");
            }
        }
    }
    // =====================================================================
    // S8: MnaLinearStrategy structured-result contract
    // =====================================================================

    private static string NetNameOf(LinearNetworkAnalyzer model, int netId) =>
        netId == model.GroundNetId
            ? model.GroundNetName
            : model.Nodes.FirstOrDefault(n => n.NetId == netId)?.Name ?? "N" + netId.ToString(Inv);

    private static bool ContainsBadText(string? text) =>
        string.IsNullOrEmpty(text) ||
        text.Contains("NaN", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Infinity", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("非数字", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNaNText(string? text) =>
        text != null &&
        (text.Contains("NaN", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("Infinity", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("非数字", StringComparison.OrdinalIgnoreCase));

    private static PedagogicalSolution RunStrategy(ElectricalNetlist netlist, double operatingFrequency)
    {
        var strategy = new MnaLinearStrategy();
        var parameters = new AnalysisParameters { OperatingFrequency = operatingFrequency };
        return strategy.SolveAsync(netlist, parameters, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Verifies node potentials and the arbitrary-port equivalent table of the strategy
    /// against the audit's independent reference solver.
    /// </summary>
    private static void CheckStrategyStructuredResults(string label, LinearNetworkAnalyzer model,
        LinearNetworkInfo info, LinearSolution primary, bool isAcMode, RefNetwork referenceNetwork)
    {
        // ----- node potentials vs the engine's own solve and the documented display contract
        Check(info.NodePotentials.Count == model.Nodes.Count,
            $"{label}：NodePotentials 数量 = 非地节点数 {model.Nodes.Count}",
            $"actual {info.NodePotentials.Count}");
        var potentialsById = info.NodePotentials.ToDictionary(n => n.NetId);
        double worstNodeRelative = 0.0;
        string nodeDetail = string.Empty;
        bool nodeMissing = false;
        bool nodeNameMismatch = false;
        foreach (LinearNetNode node in model.Nodes)
        {
            if (!potentialsById.TryGetValue(node.NetId, out NodePotentialInfo? potential))
            {
                nodeMissing = true;
                continue;
            }
            if (potential.NetName != node.Name) nodeNameMismatch = true;
            double expected = isAcMode
                ? primary.NodePhasor(node.NetId).Magnitude
                : primary.NodeVoltage(node.NetId);
            double relative = Math.Abs(potential.VoltageV - expected) / Math.Max(Math.Abs(expected), 1e-9);
            if (relative > worstNodeRelative)
            {
                worstNodeRelative = relative;
                nodeDetail = $"{node.Name}: expected {Fmt(expected)} V actual {Fmt(potential.VoltageV)} V";
            }
        }
        Check(!nodeMissing, $"{label}：NodePotentials 覆盖全部非地节点");
        Check(!nodeNameMismatch, $"{label}：NodePotentials NetName 与网表一致");
        Check(!nodeMissing && worstNodeRelative <= 1e-12,
            $"{label}：NodePotentials VoltageV 与引擎求解一致（≤1e-12）", nodeDetail);
        bool nodeDisplayBad = info.NodePotentials.Any(p => ContainsBadText(p.Display) || ContainsBadText(p.LatexFormula));
        Check(!nodeDisplayBad, $"{label}：NodePotentials Display/Latex 无 NaN/Infinity 文本",
            string.Join("；", info.NodePotentials.Select(p => p.Display).Take(6)));
        if (isAcMode)
        {
            bool hasPhasorDisplay = info.NodePotentials.All(p => p.Display.Contains("∠", StringComparison.Ordinal));
            Check(hasPhasorDisplay, $"{label}：AC 模式 NodePotentials Display 含相角标记 ∠");
            bool finiteVoltage = info.NodePotentials.All(p => double.IsFinite(p.VoltageV) && p.VoltageV >= 0);
            Check(finiteVoltage, $"{label}：AC 模式 NodePotentials VoltageV 为有限峰值幅值");
        }

        // ----- independent expected port-equivalent enumeration (documented order/cap)
        LinearDevice? source = isAcMode
            ? model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcVoltageSource)
              ?? model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcCurrentSource)
            : model.Devices.FirstOrDefault(d => d.IsIndependentSource);
        var excluded = new HashSet<int>();
        if (source != null)
        {
            excluded.Add(source.PositiveNet.NetId);
            excluded.Add(source.NegativeNet.NetId);
        }
        model.TryFindOutputNode(primary, excluded, out LinearNetNode? outputNode);
        int outputNetId = outputNode?.NetId ?? -1;

        var pairNets = model.Nodes.Select(n => n.NetId).ToList();
        pairNets.Add(model.GroundNetId);
        var allPairs = new List<(int Positive, int Negative)>();
        for (int i = 0; i < pairNets.Count; i++)
            for (int j = i + 1; j < pairNets.Count; j++)
                allPairs.Add((pairNets[i], pairNets[j]));
        var expectedEntries = new List<(int Positive, int Negative)>();
        var entryKeys = new HashSet<string>(StringComparer.Ordinal);
        void AddExpected(int positive, int negative)
        {
            if (positive == negative) return;
            string key = positive <= negative ? positive + "|" + negative : negative + "|" + positive;
            if (!entryKeys.Add(key)) return;
            expectedEntries.Add((positive, negative));
        }
        if (source != null) AddExpected(source.PositiveNet.NetId, source.NegativeNet.NetId);
        if (outputNetId >= 0 && outputNetId != model.GroundNetId) AddExpected(outputNetId, model.GroundNetId);
        foreach ((int positive, int negative) in allPairs)
        {
            if (expectedEntries.Count >= 14) break;
            AddExpected(positive, negative);
        }

        Check(info.PortEquivalents.Count == expectedEntries.Count,
            $"{label}：PortEquivalents 条目数 = 独立枚举数 {expectedEntries.Count}",
            $"actual {info.PortEquivalents.Count}");
        Check(info.PortEquivalents.Count <= 14, $"{label}：PortEquivalents 条目数 ≤ 14",
            $"actual {info.PortEquivalents.Count}");
        if (source != null && info.PortEquivalents.Count > 0)
        {
            Check(info.PortEquivalents[0].PortLabel.Contains("激励源端口", StringComparison.Ordinal),
                $"{label}：首个端口条目为激励源端口", info.PortEquivalents[0].PortLabel);
            Check(info.PortEquivalents[0].IsAcSourcePort == source.IsAcSource,
                $"{label}：激励源端口 IsAcSourcePort 标记正确",
                $"expected {source.IsAcSource}, actual {info.PortEquivalents[0].IsAcSourcePort}");
        }

        RefSolveResult referenceDc = ReferenceDc(referenceNetwork);
        double worstReqRelative = 0.0;
        string reqDetail = string.Empty;
        double worstVocRelative = 0.0;
        string vocDetail = string.Empty;
        bool reqMismatch = false;
        bool vocMismatch = false;
        bool pairOrderMismatch = false;
        int infiniteEntries = 0;
        int finiteRefEntries = 0;
        int checkedEntries = Math.Min(info.PortEquivalents.Count, expectedEntries.Count);
        for (int i = 0; i < checkedEntries; i++)
        {
            PortEquivalentInfo entry = info.PortEquivalents[i];
            (int positive, int negative) = (expectedEntries[i].Positive, expectedEntries[i].Negative);
            string expectedPositiveName = NetNameOf(model, positive);
            string expectedNegativeName = NetNameOf(model, negative);
            if (entry.PositiveNet != expectedPositiveName || entry.NegativeNet != expectedNegativeName)
                pairOrderMismatch = true;

            (bool refOk, double refReq, string refError) = ReferenceEquivalentResistance(referenceNetwork, positive, negative);
            if (refOk)
            {
                finiteRefEntries++;
                if (!double.IsFinite(entry.ResistanceOhm))
                {
                    reqMismatch = true;
                    reqDetail = $"端口({positive},{negative}) 独立参考 Req={Fmt(refReq)} Ω，但条目 R=∞";
                }
                else
                {
                    double relative = Math.Abs(entry.ResistanceOhm - refReq) / Math.Max(Math.Abs(refReq), 1e-9);
                    if (relative > worstReqRelative)
                    {
                        worstReqRelative = relative;
                        reqDetail = $"端口({positive},{negative}) expected {Fmt(refReq)} Ω actual {Fmt(entry.ResistanceOhm)} Ω";
                    }
                }
            }
            else
            {
                if (double.IsFinite(entry.ResistanceOhm))
                {
                    reqMismatch = true;
                    reqDetail = $"端口({positive},{negative}) 独立参考无解（{refError}），但条目 R={Fmt(entry.ResistanceOhm)} Ω";
                }
                else
                {
                    infiniteEntries++;
                    bool displayOk = entry.ResistanceDisplay == "∞（无直流通路）" &&
                                     !string.IsNullOrWhiteSpace(entry.Note);
                    if (!displayOk)
                    {
                        reqMismatch = true;
                        reqDetail = $"端口({positive},{negative}) ∞ 显示处理异常：display='{entry.ResistanceDisplay}' note='{entry.Note}'";
                    }
                }
            }

            if (referenceDc.Success)
            {
                double expectedVoc = (referenceDc.NodeVoltage[positive] - referenceDc.NodeVoltage[negative]).Real;
                if (!entry.HasOpenCircuitVoltage)
                {
                    vocMismatch = true;
                    vocDetail = $"端口({positive},{negative}) 独立参考 Voc={Fmt(expectedVoc)} V，但条目未提供 Voc";
                }
                else
                {
                    double relative = Math.Abs(entry.OpenCircuitVoltageV - expectedVoc) / Math.Max(Math.Abs(expectedVoc), 1e-9);
                    if (relative > worstVocRelative)
                    {
                        worstVocRelative = relative;
                        vocDetail = $"端口({positive},{negative}) expected Voc {Fmt(expectedVoc)} V actual {Fmt(entry.OpenCircuitVoltageV)} V";
                    }
                }
            }
            else
            {
                if (entry.HasOpenCircuitVoltage)
                {
                    vocMismatch = true;
                    vocDetail = $"端口({positive},{negative}) 独立参考无直流解，但条目给出了 Voc={Fmt(entry.OpenCircuitVoltageV)} V";
                }
                else if (entry.OpenCircuitVoltageDisplay != "—")
                {
                    vocMismatch = true;
                    vocDetail = $"端口({positive},{negative}) 无直流解时 Voc 显示应为 '—'，实际 '{entry.OpenCircuitVoltageDisplay}'";
                }
            }
        }
        Check(!pairOrderMismatch, $"{label}：PortEquivalents 端口节点顺序与独立枚举一致");
        Check(!reqMismatch && worstReqRelative <= 1e-6,
            $"{label}：PortEquivalents Req vs 独立参考（≤1e-6；有限 {finiteRefEntries} 个，∞ {infiniteEntries} 个）", reqDetail);
        Check(!vocMismatch && worstVocRelative <= 1e-6,
            $"{label}：PortEquivalents Voc vs 独立参考（≤1e-6）", vocDetail);
        bool portBadText = info.PortEquivalents.Any(p => ContainsBadText(p.PortLabel));
        Check(!portBadText, $"{label}：PortEquivalents 标签无 NaN/Infinity 文本");
    }

    private static void CheckStrategyFrequencyResponse(string label, LinearNetworkInfo info,
        LinearNetworkAnalyzer model, LinearSolution primary, RefNetwork referenceNetwork)
    {
        FrequencyResponseInfo? response = info.FrequencyResponse;
        Check(response != null, $"{label}：FrequencyResponse 已生成");
        if (response == null) return;

        double expectedOperating = model.PreferredOperatingFrequencyHz ?? 1000.0;
        CheckRel(response.OperatingFrequencyHz, expectedOperating, 1e-12,
            $"{label}：FrequencyResponse 工作频率 = 首个 AC 源频率", " Hz");
        Check(response.Points.Count == 61, $"{label}：FrequencyResponse.Points = 61",
            $"actual {response.Points.Count}");

        Check(!ContainsBadText(response.GainRatioDisplay), $"{label}：GainRatioDisplay 无 NaN/Infinity", response.GainRatioDisplay);
        Check(!ContainsBadText(response.GainDbDisplay), $"{label}：GainDbDisplay 无 NaN/Infinity", response.GainDbDisplay);
        Check(!ContainsBadText(response.PhaseDisplay), $"{label}：PhaseDisplay 无 NaN/Infinity", response.PhaseDisplay);
        Check(response.LowerCutoffDisplay != null && response.UpperCutoffDisplay != null, $"{label}：截止频率显示字段非空");
        bool pointsBad = response.Points.Any(p => ContainsBadText(p.FrequencyDisplay) ||
                                                  ContainsBadText(p.GainDbDisplay) ||
                                                  ContainsBadText(p.PhaseDisplay));
        Check(!pointsBad, $"{label}：频响采样点显示字段无 NaN/Infinity 文本");

        LinearDevice? source = model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcVoltageSource)
                               ?? model.Devices.FirstOrDefault(d => d.Kind == LinearDeviceKind.AcCurrentSource);
        Check(source != null, $"{label}：存在交流激励源");
        if (source == null) return;
        int inputPositive = source.PositiveNet.NetId;
        int inputNegative = source.NegativeNet.NetId;
        var excluded = new HashSet<int> { inputPositive, inputNegative };
        model.TryFindOutputNode(primary, excluded, out LinearNetNode? outputNode);
        Check(outputNode != null, $"{label}：输出端口启发式可确定");
        if (outputNode == null) return;
        int outputNet = outputNode.NetId;

        RefSolveResult normalized = ReferenceNormalized(referenceNetwork, response.OperatingFrequencyHz,
            inputPositive, inputNegative);
        Check(normalized.Success, $"{label}：独立参考归一化激励 AC 求解成功", normalized.Error);
        if (!normalized.Success) return;
        Complex expectedGain = normalized.NodeVoltage[outputNet] -
                               normalized.NodeVoltage[model.GroundNetId];
        CheckComplexRel(Complex.FromPolarCoordinates(response.GainRatio, response.PhaseDeg * Math.PI / 180.0),
            expectedGain, 1e-6, $"{label}：FrequencyResponse 工作频率增益 vs 独立参考");

        int compared = 0;
        double worstPointRelative = 0.0;
        string pointDetail = string.Empty;
        foreach (int index in new[] { 0, 10, 20, 30, 40, 50, 60 })
        {
            if (index >= response.Points.Count) continue;
            AcSweepPointInfo point = response.Points[index];
            RefSolveResult pointReference = ReferenceNormalized(referenceNetwork, point.FrequencyHz,
                inputPositive, inputNegative);
            if (!pointReference.Success) continue;
            Complex pointExpected = pointReference.NodeVoltage[outputNet] -
                                    pointReference.NodeVoltage[model.GroundNetId];
            Complex pointActual = Complex.FromPolarCoordinates(point.GainRatio, point.PhaseDeg * Math.PI / 180.0);
            double relative = (pointActual - pointExpected).Magnitude / Math.Max(pointExpected.Magnitude, 1e-12);
            if (relative > worstPointRelative)
            {
                worstPointRelative = relative;
                pointDetail = $"f={Fmt(point.FrequencyHz)} Hz expected {Cfmt(pointExpected)} actual {Cfmt(pointActual)}";
            }
            compared++;
        }
        Check(compared >= 5, $"{label}：频响至少抽 5 点与独立参考对比", $"compared={compared}");
        Check(worstPointRelative <= 1e-6, $"{label}：频响抽样点 vs 独立参考归一化 AC（≤1e-6）", pointDetail);
    }

    private static void CheckStructuredResults()
    {
        // ================= A. DC network with VCVS + VCCS =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0);
            f.Vcvs("E1", 1, 0, 2, 0, 2.0); f.R("R2", 2, 0, 1e4);
            f.Vccs("G1", 2, 0, 3, 0, 1e-3); f.R("R3", 3, 0, 1e3);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化 DC 受控源网络：MnaLinearStrategy 求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            Check(info != null, "结构化 DC 受控源网络：LinearNetwork 非空");
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化 DC 受控源网络：TryCreate 成功", createError);
            if (info != null && model != null)
            {
                Check(info.OperatingMode.Contains("直流", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：OperatingMode 标明直流", info.OperatingMode);
                Check(info.MatrixDimension == model.MatrixDimension,
                    "结构化 DC 受控源网络：MatrixDimension 与模型一致", $"actual {info.MatrixDimension}");
                Check(info.FrequencyResponse == null, "结构化 DC 受控源网络：无 AC 激励时 FrequencyResponse 为 null");
                Check(!string.IsNullOrWhiteSpace(info.BoundaryNote) &&
                      info.BoundaryNote.Contains("未检测到交流激励", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：BoundaryNote 说明无交流激励", info.BoundaryNote);
                Check(info.BoundaryNote.Contains("适用范围", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：BoundaryNote 含适用范围声明");
                Check(result.Steps.Count == 5, "结构化 DC 受控源网络：推导步骤数 = 5", $"actual {result.Steps.Count}");
                Check(!string.IsNullOrWhiteSpace(result.RawMnaLog) &&
                      result.RawMnaLog.Contains("MNA", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：RawMnaLog 非空且为 MNA 日志");
                AcParametersInfo acParams = result.AcParameters ?? new AcParametersInfo();
                Check(acParams.VoltageGain.Contains("DC 电压比", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：DC 增益说明", acParams.VoltageGain);
                Check(result.QPoint != null && result.QPoint.WorkingRegion.Contains("线性", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：QPoint 工作区说明", result.QPoint?.WorkingRegion ?? string.Empty);
                TopologyClassifier.Classify(netlist, out ICircuitStrategy classifiedStrategy);
                Check(classifiedStrategy is MnaLinearStrategy,
                    "结构化 DC 受控源网络：分类器选择 MnaLinearStrategy", classifiedStrategy.GetType().Name);
                Check(result.Steps[0].SubstitutionFormula.Contains("bmatrix", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：第 1 步给出矩阵方程（bmatrix）");
                bool stepsBad = result.Steps.Any(s => ContainsNaNText(s.LatexFormula) ||
                                                      ContainsNaNText(s.SubstitutionFormula) ||
                                                      ContainsNaNText(s.ResultValue));
                Check(!stepsBad, "结构化 DC 受控源网络：推导步骤公式无 NaN/Infinity 文本");
                Check(result.Steps[4].ResultValue.Contains("适用", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：第 5 步声明适用范围", result.Steps[4].ResultValue);
                Check(result.Steps[3].ResultValue.Contains("不适用", StringComparison.Ordinal),
                    "结构化 DC 受控源网络：第 4 步说明 AC 频响不适用", result.Steps[3].ResultValue);
                if (model.TrySolveDc(out LinearSolution? dc, out _) && dc != null)
                {
                    CheckStrategyStructuredResults("结构化 DC 受控源网络", model, info, dc, false, f.Model);
                    CheckRel(info.NodePotentials.First(p => p.NetId == 2).VoltageV, 2.0, 1e-6,
                        "结构化 DC 受控源网络：v(N2) = 2 V", " V");
                    CheckRel(info.NodePotentials.First(p => p.NetId == 3).VoltageV, -2.0, 1e-6,
                        "结构化 DC 受控源网络：v(N3) = -2 V", " V");
                }
                else
                {
                    Check(false, "结构化 DC 受控源网络：TrySolveDc 基准成功");
                }
            }
        }

        // ================= B. AC RC low-pass =================
        {
            double rcCutoff = 1.0 / (2.0 * Math.PI * 1e3 * 1e-6);
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化 AC RC 低通：MnaLinearStrategy 求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化 AC RC 低通：TryCreate 成功", createError);
            if (info != null && model != null)
            {
                Check(info.OperatingMode.Contains("交流", StringComparison.Ordinal),
                    "结构化 AC RC 低通：OperatingMode 标明交流", info.OperatingMode);
                Check(info.FrequencyResponse != null, "结构化 AC RC 低通：FrequencyResponse 非空");
                if (info.FrequencyResponse != null)
                {
                    Check(info.FrequencyResponse.UpperCutoffHz.HasValue, "结构化 AC RC 低通：检测到上截止频率");
                    if (info.FrequencyResponse.UpperCutoffHz.HasValue)
                        CheckRel(info.FrequencyResponse.UpperCutoffHz.Value, rcCutoff, 0.05,
                            "结构化 AC RC 低通：上截止频率 ≈ 1/(2πRC)（≤5%）", " Hz");
                    Check(!info.FrequencyResponse.LowerCutoffHz.HasValue, "结构化 AC RC 低通：无下截止频率");
                    Check(info.FrequencyResponse.OutputPortLabel.Contains("N2", StringComparison.Ordinal),
                        "结构化 AC RC 低通：输出端口标注 N2", info.FrequencyResponse.OutputPortLabel);
                    // Teaching display convention: at most two decimals, so |H| ≈ 0.157
                    // is rendered as "0.16"; keep the numeric intent of the assertion by
                    // checking the displayed number against the computed gain.
                    Check(double.TryParse(info.FrequencyResponse.GainRatioDisplay, NumberStyles.Float,
                              CultureInfo.InvariantCulture, out double shownGainRatio) &&
                          Math.Abs(shownGainRatio - 0.15718) <= 0.005,
                        "结构化 AC RC 低通：工作频率增益显示 ≈ 0.16（两位小数）",
                        info.FrequencyResponse.GainRatioDisplay);
                }
                AcParametersInfo rcAcParams = result.AcParameters ?? new AcParametersInfo();
                Check(rcAcParams.InputResistance.Contains("Ω", StringComparison.Ordinal) &&
                      rcAcParams.InputResistance.Contains("工作频率", StringComparison.Ordinal),
                    "结构化 AC RC 低通：输入电阻为工作频率驱动点阻抗幅值", rcAcParams.InputResistance);
                Check(rcAcParams.OutputResistance.Contains("Ω", StringComparison.Ordinal),
                    "结构化 AC RC 低通：输出电阻显示带单位", rcAcParams.OutputResistance);
                if (model.TrySolveAc(1000.0, out LinearSolution? ac, out _) && ac != null)
                {
                    CheckStrategyStructuredResults("结构化 AC RC 低通", model, info, ac, true, f.Model);
                    CheckStrategyFrequencyResponse("结构化 AC RC 低通", info, model, ac, f.Model);
                    // Peak-amplitude convention: |V(N1)| must be exactly 1 V, not 0.707 V RMS.
                    CheckRel(info.NodePotentials.First(p => p.NetId == 1).VoltageV, 1.0, 1e-12,
                        "结构化 AC RC 低通：源节点 VoltageV = 1 V 峰值（非 RMS）", " V");
                    double expectedOutput = TransferRcLowPass(2.0 * Math.PI * 1000.0, 1e3, 1e-6).Magnitude;
                    CheckRel(info.NodePotentials.First(p => p.NetId == 2).VoltageV, expectedOutput, 1e-6,
                        "结构化 AC RC 低通：输出节点 VoltageV = |H(1kHz)| 峰值", " V");
                }
                else
                {
                    Check(false, "结构化 AC RC 低通：TrySolveAc 基准成功");
                }
            }
        }

        // ================= C. AC-only capacitor divider (DC solve impossible) =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.C("C1", 1, 2, 1e-6); f.C("C2", 2, 0, 1e-6);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化 AC 电容分压：MnaLinearStrategy 求解成功（DC 无解但不阻塞 AC）", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化 AC 电容分压：TryCreate 成功", createError);
            if (info != null && model != null)
            {
                Check(info.FrequencyResponse != null, "结构化 AC 电容分压：FrequencyResponse 非空");
                if (info.FrequencyResponse != null)
                    Check(info.FrequencyResponse.Points.Count == 61, "结构化 AC 电容分压：频响 61 点",
                        $"actual {info.FrequencyResponse.Points.Count}");
                bool dcSolved = model.TrySolveDc(out _, out string dcError);
                Check(!dcSolved, "结构化 AC 电容分压：DC 求解失败（前置条件：AC 可解但 DC 奇异）", dcError);
                if (model.TrySolveAc(1000.0, out LinearSolution? ac, out _) && ac != null)
                {
                    CheckStrategyStructuredResults("结构化 AC 电容分压", model, info, ac, true, f.Model);
                    CheckRel(info.NodePotentials.First(p => p.NetId == 2).VoltageV, 0.5, 1e-9,
                        "结构化 AC 电容分压：等电容分压 |v(N2)| = 0.5 V", " V");
                    bool allNoVoc = info.PortEquivalents.All(p => !p.HasOpenCircuitVoltage &&
                                                                 p.OpenCircuitVoltageDisplay == "—");
                    Check(allNoVoc, "结构化 AC 电容分压：无直流解时所有端口 Voc 显示 '—'（不伪造 0）");
                    bool anyInfinite = info.PortEquivalents.Any(p => double.IsPositiveInfinity(p.ResistanceOhm));
                    Check(anyInfinite, "结构化 AC 电容分压：无直流通路端口 Req 记为 ∞");
                    Check(info.BoundaryNote.Contains("无直流通路", StringComparison.Ordinal),
                        "结构化 AC 电容分压：BoundaryNote 说明无直流通路", info.BoundaryNote);
                }
                else
                {
                    Check(false, "结构化 AC 电容分压：TrySolveAc 基准成功");
                }
            }
        }

        // ================= D. AC current-source excitation (transimpedance) =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Iac("I1", 1, 0, 1e-3, 1000.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化 AC 电流源：MnaLinearStrategy 求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化 AC 电流源：TryCreate 成功", createError);
            if (info != null && model != null && info.FrequencyResponse != null)
            {
                CheckRel(info.FrequencyResponse.GainRatio, 1000.0, 1e-6,
                    "结构化 AC 电流源：归一化跨阻增益 = 1000 Ω", " Ω");
                Check(info.FrequencyResponse.GainRatioDisplay.Contains("Ω", StringComparison.Ordinal),
                    "结构化 AC 电流源：跨阻增益显示使用 Ω", info.FrequencyResponse.GainRatioDisplay);
                Check(info.FrequencyResponse.BoundaryNote.Contains("跨阻", StringComparison.Ordinal),
                    "结构化 AC 电流源：频响边界说明跨阻（V/A）", info.FrequencyResponse.BoundaryNote);
                Check(model.TrySolveAc(1000.0, out LinearSolution? ac, out _) && ac != null,
                    "结构化 AC 电流源：TrySolveAc 基准成功");
                if (ac != null)
                {
                    CheckStrategyStructuredResults("结构化 AC 电流源", model, info, ac, true, f.Model);
                    CheckRel(info.NodePotentials.First(p => p.NetId == 1).VoltageV, 2.0, 1e-9,
                        "结构化 AC 电流源：自然 1mA 激励下 |v(N1)| = 2 V", " V");
                    CheckRel(info.NodePotentials.First(p => p.NetId == 2).VoltageV, 1.0, 1e-9,
                        "结构化 AC 电流源：自然 1mA 激励下 |v(N2)| = 1 V", " V");
                }
            }
            else if (info != null)
            {
                Check(false, "结构化 AC 电流源：FrequencyResponse 非空");
            }
        }

        // ================= E. AC network with a VCVS (heuristic output = VCVS output) =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0);
            f.Vcvs("E1", 1, 0, 2, 0, 2.0);
            f.R("R1", 2, 3, 1e3); f.C("C1", 3, 0, 1e-6);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化 AC 受控源：MnaLinearStrategy 求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化 AC 受控源：TryCreate 成功", createError);
            if (info != null && model != null && model.TrySolveAc(1000.0, out LinearSolution? ac, out _) && ac != null)
            {
                CheckStrategyStructuredResults("结构化 AC 受控源", model, info, ac, true, f.Model);
                CheckStrategyFrequencyResponse("结构化 AC 受控源", info, model, ac, f.Model);
                CheckRel(info.NodePotentials.First(p => p.NetId == 2).VoltageV, 2.0, 1e-9,
                    "结构化 AC 受控源：|v(N2)| = 2 V（VCVS 输出）", " V");
                CheckRel(info.NodePotentials.First(p => p.NetId == 3).VoltageV,
                    2.0 * TransferRcLowPass(2.0 * Math.PI * 1000.0, 1e3, 1e-6).Magnitude, 1e-6,
                    "结构化 AC 受控源：|v(N3)| = 2·|H(1kHz)|", " V");
            }
            else
            {
                Check(false, "结构化 AC 受控源：前置求解成功");
            }
        }

        // ================= F. AC network without an independent output node =================
        {
            var f = new Fixture(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 0, 1e3);
            PedagogicalSolution result = RunStrategy(f.Build(), 1000.0);
            Check(result.IsSuccess, "结构化 AC 无输出端口：求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            Check(info != null, "结构化 AC 无输出端口：LinearNetwork 非空");
            if (info != null)
            {
                Check(info.FrequencyResponse == null, "结构化 AC 无输出端口：FrequencyResponse 为 null（不伪造频响）");
                Check(info.BoundaryNote.Contains("无法确定独立于激励端口的输出端口", StringComparison.Ordinal),
                    "结构化 AC 无输出端口：BoundaryNote 说明原因", info.BoundaryNote);
                Check(result.Steps.Count == 5, "结构化 AC 无输出端口：推导步骤仍为 5 步", $"actual {result.Steps.Count}");
                AcParametersInfo noOutputAcParams = result.AcParameters ?? new AcParametersInfo();
                Check(noOutputAcParams.LowerCutoffFreq == "—" &&
                      noOutputAcParams.UpperCutoffFreq == "—",
                    "结构化 AC 无输出端口：截止频率字段为 '—'",
                    noOutputAcParams.LowerCutoffFreq + "/" + noOutputAcParams.UpperCutoffFreq);
            }
        }

        // ================= G. DC current-source strategy path =================
        {
            var f = new Fixture(); f.NewNode();
            f.Idc("I1", 1, 0, 1e-3); f.R("R1", 1, 0, 1e3);
            PedagogicalSolution result = RunStrategy(f.Build(), 1000.0);
            Check(result.IsSuccess, "结构化 DC 电流源：求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            Check(info != null, "结构化 DC 电流源：LinearNetwork 非空");
            if (info != null)
            {
                Check(info.FrequencyResponse == null, "结构化 DC 电流源：FrequencyResponse 为 null");
                AcParametersInfo dcCurrentAcParams = result.AcParameters ?? new AcParametersInfo();
                Check(dcCurrentAcParams.VoltageGain.Contains("电流源", StringComparison.Ordinal),
                    "结构化 DC 电流源：无电压增益定义说明", dcCurrentAcParams.VoltageGain);
                Check(info.PortEquivalents.Count == 1, "结构化 DC 电流源：端口条目数 = 1（仅激励源端口）",
                    $"actual {info.PortEquivalents.Count}");
                if (info.PortEquivalents.Count == 1)
                {
                    CheckRel(info.PortEquivalents[0].ResistanceOhm, 1e3, 1e-6,
                        "结构化 DC 电流源：激励源端口 Req = 1 kΩ", " Ω");
                    CheckRel(info.PortEquivalents[0].OpenCircuitVoltageV, 1.0, 1e-6,
                        "结构化 DC 电流源：激励源端口 Voc = 1 V", " V");
                }
            }
        }

        // ================= H. Failure paths: never silently produce zeros =================
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 1.0); f.R("R1", 1, 0, 1e3);
            f.Element("D1", ElementType.Diode, 0.7, "");
            f.Pin("D1", "A", 1); f.Pin("D1", "K", 0);
            PedagogicalSolution result = RunStrategy(f.Build(), 1000.0);
            Check(!result.IsSuccess, "结构化失败：二极管网络返回失败");
            Check(!string.IsNullOrWhiteSpace(result.SummaryText) &&
                  (result.SummaryText.Contains("二极管", StringComparison.Ordinal) ||
                   result.SummaryText.Contains("不支持", StringComparison.Ordinal)),
                "结构化失败：二极管失败消息明确", result.SummaryText);
            Check(result.LinearNetwork == null && result.Steps.Count == 0,
                "结构化失败：不伪造 LinearNetwork/推导步骤");
            AnalysisResult mappedFailure = result.ToAnalysisResult();
            Check(!mappedFailure.IsSuccess && mappedFailure.LinearNetwork == null &&
                  mappedFailure.NodePotentials.Count == 0 && mappedFailure.Steps.Count == 0,
                "结构化失败：ToAnalysisResult 不伪造节点电位/推导步骤");
        }
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vdc("V1", 1, 0, 12.0); f.R("R1", 1, 2, 1e3); f.R("R2", 2, 0, 1e3);
            PedagogicalSolution result = RunStrategy(f.Build(markGround: false), 1000.0);
            Check(!result.IsSuccess, "结构化失败：缺少参考地返回失败");
            Check(!string.IsNullOrWhiteSpace(result.SummaryText) &&
                  (result.SummaryText.Contains("地", StringComparison.Ordinal) ||
                   result.SummaryText.Contains("GND", StringComparison.OrdinalIgnoreCase)),
                "结构化失败：缺少参考地消息明确", result.SummaryText);
            Check(result.LinearNetwork == null, "结构化失败：缺少参考地不伪造结果");
        }
        {
            var f = new Fixture(); f.NewNode();
            f.Vdc("V1", 1, 0, 5.0); f.Vdc("V2", 1, 0, 7.0);
            PedagogicalSolution result = RunStrategy(f.Build(), 1000.0);
            Check(!result.IsSuccess, "结构化失败：矛盾电压源返回失败（不静默为 0）");
            Check(!string.IsNullOrWhiteSpace(result.SummaryText) &&
                  (result.SummaryText.Contains("直流 MNA 求解失败", StringComparison.Ordinal) ||
                   result.SummaryText.Contains("奇异", StringComparison.Ordinal) ||
                   result.SummaryText.Contains("病态", StringComparison.Ordinal)),
                "结构化失败：矛盾电压源失败消息明确", result.SummaryText);
            Check(result.LinearNetwork == null, "结构化失败：矛盾电压源不伪造结果");
        }

        // ================= I. ToAnalysisResult mapping =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            PedagogicalSolution solution = RunStrategy(f.Build(), 1000.0);
            Check(solution.IsSuccess, "结构化映射：AC RC 求解成功");
            AnalysisResult mapped = solution.ToAnalysisResult();
            Check(mapped.IsSuccess && mapped.LinearNetwork != null,
                "结构化映射：ToAnalysisResult 保留 LinearNetwork");
            Check(mapped.NodePotentials.Count == solution.LinearNetwork!.NodePotentials.Count &&
                  mapped.NodePotentials.Count == 2,
                "结构化映射：NodePotentials 映射数量一致",
                $"mapped={mapped.NodePotentials.Count}, source={solution.LinearNetwork.NodePotentials.Count}");
            Check(mapped.FrequencyResponse != null && mapped.AcSweepPoints.Count == 61,
                "结构化映射：AcSweepPoints 映射 61 点",
                $"points={mapped.AcSweepPoints.Count}");
            Check(mapped.PortEquivalents.Count == solution.LinearNetwork.PortEquivalents.Count,
                "结构化映射：PortEquivalents 映射数量一致");
            Check(mapped.Steps.Count == 5, "结构化映射：Steps 映射数量 = 5", $"actual {mapped.Steps.Count}");
        }

        // ================= J. PortEquivalents cap at 14 with more possible node pairs =================
        {
            var f = new Fixture();
            for (int i = 1; i <= 5; i++) f.NewNode();
            f.Vdc("V1", 1, 0, 12.0);
            f.R("R1", 1, 2, 1e3); f.R("R2", 2, 3, 1e3); f.R("R3", 3, 4, 1e3);
            f.R("R4", 4, 5, 1e3); f.R("R5", 5, 0, 1e3);
            f.R("R6", 2, 0, 2e3); f.R("R7", 4, 0, 3e3);
            ElectricalNetlist netlist = f.Build();
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "结构化端口上限：5 节点网络求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化端口上限：TryCreate 成功", createError);
            if (info != null && model != null)
            {
                Check(info.PortEquivalents.Count == 14, "结构化端口上限：PortEquivalents 上限 14 条",
                    $"actual {info.PortEquivalents.Count}");
                Check(info.BoundaryNote.Contains("任意节点对等效", StringComparison.Ordinal) &&
                      info.BoundaryNote.Contains("省略", StringComparison.Ordinal),
                    "结构化端口上限：BoundaryNote 说明端口对截断", info.BoundaryNote);
                if (model.TrySolveDc(out LinearSolution? dc, out _) && dc != null)
                    CheckStrategyStructuredResults("结构化端口上限", model, info, dc, false, f.Model);
                else
                    Check(false, "结构化端口上限：TrySolveDc 基准成功");
            }
        }

        // ================= K. Multiple AC sources: operating frequency comes from the first
        //                   AC source in deterministic element order.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 100.0);
            f.Vac("V2", 2, 0, 2.0, 1000.0);
            f.R("R1", 1, 3, 1e3); f.R("R2", 2, 3, 1e3); f.R("R3", 3, 0, 1e3);
            PedagogicalSolution result = RunStrategy(f.Build(), 777.0);
            Check(result.IsSuccess, "结构化多交流源：求解成功", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            Check(info != null, "结构化多交流源：LinearNetwork 非空");
            if (info != null)
            {
                Check(info.OperatingMode.Contains("100 Hz", StringComparison.Ordinal),
                    "结构化多交流源：工作频率取首个 AC 源 V1 的 100 Hz", info.OperatingMode);
                Check(info.FrequencyResponse != null &&
                      Math.Abs(info.FrequencyResponse.OperatingFrequencyHz - 100.0) <= 1e-12,
                    "结构化多交流源：FrequencyResponse 工作频率 = 100 Hz",
                    info.FrequencyResponse == null ? "null" : Fmt(info.FrequencyResponse.OperatingFrequencyHz));
                if (info.FrequencyResponse != null)
                    Check(info.FrequencyResponse.InputPortLabel.Contains("V1", StringComparison.Ordinal),
                        "结构化多交流源：输入端口标注激励源 V1", info.FrequencyResponse.InputPortLabel);
                // At the common phasor frequency both AC sources act: N3 = (1+2)/3 = 1 V.
                NodePotentialInfo? node3 = info.NodePotentials.FirstOrDefault(p => p.NetId == 3);
                Check(node3 != null && Math.Abs(node3.VoltageV - 1.0) <= 1e-9,
                    "结构化多交流源：|v(N3)| = 1 V",
                    node3 == null ? "条目缺失" : Fmt(node3.VoltageV) + " V");
            }
        }

        // ================= L. Invalid AC-source FrequencyHz: the netlist validator rejects it,
        //                   while the analyzer API can still be driven with an explicit frequency.
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 1.0, 0.0); // FrequencyHz = 0
            f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            ElectricalNetlist netlist = f.Build();
            NetlistValidationResult validation = netlist.Validate();
            Check(!validation.IsValid, "结构化非法 AC 频率：Netlist.Validate 明确拒绝 FrequencyHz=0",
                string.Join("; ", validation.Issues.Select(i => i.Message)));
            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "结构化非法 AC 频率：分析器 TryCreate 成功", createError);
            if (model != null)
            {
                Check(model.PreferredOperatingFrequencyHz == null,
                    "结构化非法 AC 频率：PreferredOperatingFrequencyHz 回退为 null");
                bool ok = model.TrySolveAc(500.0, out LinearSolution? ac, out string acError) && ac != null;
                Check(ok, "结构化非法 AC 频率：显式指定 500 Hz 时 TrySolveAc 成功", acError);
                if (ok && ac != null)
                    CheckRel(ac.NodeMagnitude(2), TransferRcLowPass(2.0 * Math.PI * 500.0, 1e3, 1e-6).Magnitude,
                        1e-6, "结构化非法 AC 频率：500 Hz 响应 vs 解析", " V");
            }
        }

        // ================= M. Zero-amplitude AC source: AC mode with all-zero natural excitation. =================
        {
            var f = new Fixture(); f.NewNode(); f.NewNode();
            f.Vac("V1", 1, 0, 0.0, 1000.0); f.R("R1", 1, 2, 1e3); f.C("C1", 2, 0, 1e-6);
            PedagogicalSolution result = RunStrategy(f.Build(), 1000.0);
            Check(result.IsSuccess, "结构化零幅值 AC 源：求解成功（不崩溃）", result.SummaryText);
            LinearNetworkInfo? info = result.LinearNetwork;
            Check(info != null, "结构化零幅值 AC 源：LinearNetwork 非空");
            if (info != null)
            {
                bool allZero = info.NodePotentials.All(p => Math.Abs(p.VoltageV) <= 1e-300);
                Check(allZero, "结构化零幅值 AC 源：自然 AC 解全部为 0 V");
                bool badDisplay = info.NodePotentials.Any(p => ContainsBadText(p.Display)) ||
                                  (info.FrequencyResponse != null &&
                                   (ContainsBadText(info.FrequencyResponse.GainRatioDisplay) ||
                                    ContainsBadText(info.FrequencyResponse.GainDbDisplay) ||
                                    ContainsBadText(info.FrequencyResponse.PhaseDisplay)));
                Check(!badDisplay, "结构化零幅值 AC 源：显示字段无 NaN/Infinity 文本");
                Check(info.FrequencyResponse != null && info.FrequencyResponse.Points.Count == 61,
                    "结构化零幅值 AC 源：归一化频响仍输出 61 点",
                    info.FrequencyResponse == null ? "null" : $"points={info.FrequencyResponse.Points.Count}");
            }
        }

        // ================= N. DC excitation-port priority (fixed engine behaviour): an
        //                   independent DC voltage source is preferred regardless of element-id
        //                   ordering; a current source is used only when no voltage source
        //                   exists. Aligned with EngineV2Checks 8.3 / 8.4 / 8.5 semantics. =================
        {
            // 8.3 semantics: I1 sorts before the voltage source, yet N2–GND (the voltage
            // source port) must be the excitation port and gain text must be V_out/V_in = 1V/5V.
            string? orderedGainText = null;
            foreach (string voltageId in new[] { "V1", "A_V1" })
            {
                var mixed = new Fixture(); mixed.NewNode(); mixed.NewNode();
                mixed.Idc("I1", 1, 0, 1e-3); mixed.R("R1", 1, 0, 1e3);
                mixed.Vdc(voltageId, 2, 0, 5.0); mixed.R("R2", 2, 0, 1e3);
                PedagogicalSolution mixedResult = RunStrategy(mixed.Build(), 1000.0);
                Check(mixedResult.IsSuccess && mixedResult.LinearNetwork != null,
                    $"结构化混合源（电压源 id={voltageId}）：求解成功", mixedResult.SummaryText);
                LinearNetworkInfo? mixedInfo = mixedResult.LinearNetwork;
                if (mixedInfo == null) continue;
                PortEquivalentInfo? sourcePort = mixedInfo.PortEquivalents
                    .FirstOrDefault(p => p.PortLabel.Contains("激励源", StringComparison.Ordinal));
                Check(sourcePort != null && sourcePort.PositiveNet == "N2" && sourcePort.NegativeNet == "GND",
                    $"结构化混合源（电压源 id={voltageId}）：激励端口 = 电压源端口 N2–GND（与 Id 顺序无关）",
                    sourcePort == null ? "激励源端口缺失" : $"{sourcePort.PositiveNet}–{sourcePort.NegativeNet} / {sourcePort.PortLabel}");
                Check(sourcePort != null && sourcePort.PortLabel.Contains(voltageId, StringComparison.Ordinal),
                    $"结构化混合源（电压源 id={voltageId}）：激励端口标注电压源 {voltageId}",
                    sourcePort?.PortLabel ?? "missing");
                AcParametersInfo mixedAc = mixedResult.AcParameters ?? new AcParametersInfo();
                Check(mixedAc.VoltageGain.Contains("DC 电压比", StringComparison.Ordinal) &&
                      mixedAc.VoltageGain.Contains("0.2", StringComparison.Ordinal),
                    $"结构化混合源（电压源 id={voltageId}）：DC 电压比 = 1V/5V = 0.2", mixedAc.VoltageGain);
                Check(!mixedAc.VoltageGain.Contains("电流源", StringComparison.Ordinal),
                    $"结构化混合源（电压源 id={voltageId}）：不再误报“激励为电流源”", mixedAc.VoltageGain);
                NodePotentialInfo? n1Potential = mixedInfo.NodePotentials.FirstOrDefault(p => p.NetId == 1);
                NodePotentialInfo? n2Potential = mixedInfo.NodePotentials.FirstOrDefault(p => p.NetId == 2);
                Check(n1Potential != null && Math.Abs(n1Potential.VoltageV - 1.0) <= 1e-9 &&
                      n2Potential != null && Math.Abs(n2Potential.VoltageV - 5.0) <= 1e-9,
                    $"结构化混合源（电压源 id={voltageId}）：V(N1)=1 V、V(N2)=5 V（输出 N1 对应 0.2 比值）",
                    $"V(N1)={Fmt(n1Potential?.VoltageV ?? double.NaN)}, V(N2)={Fmt(n2Potential?.VoltageV ?? double.NaN)}");
                orderedGainText ??= mixedAc.VoltageGain;
                if (voltageId == "A_V1")
                    Check(orderedGainText == mixedAc.VoltageGain,
                        "结构化混合源：V1 / A_V1 两种 Id 顺序给出相同 DC 增益文本（确定性）",
                        $"{orderedGainText} vs {mixedAc.VoltageGain}");
                Console.WriteLine($"    [INFO] 结构化混合源（电压源 id={voltageId}）：激励端口='{sourcePort?.PositiveNet}–{sourcePort?.NegativeNet}'" +
                                  $"（{sourcePort?.PortLabel}），VoltageGain='{mixedAc.VoltageGain}'");
            }

            // 8.4 semantics: a pure current-source network still falls back to the current source.
            var currentOnly = new Fixture(); currentOnly.NewNode();
            currentOnly.Idc("I1", 1, 0, 1e-3); currentOnly.R("R1", 1, 0, 1e3);
            PedagogicalSolution currentOnlyResult = RunStrategy(currentOnly.Build(), 1000.0);
            Check(currentOnlyResult.IsSuccess && currentOnlyResult.LinearNetwork != null,
                "结构化纯电流源网络：求解成功（回退电流源激励）", currentOnlyResult.SummaryText);
            if (currentOnlyResult.LinearNetwork != null)
            {
                PortEquivalentInfo? currentPort = currentOnlyResult.LinearNetwork.PortEquivalents.FirstOrDefault();
                Check(currentPort != null && currentPort.PortLabel.Contains("激励源", StringComparison.Ordinal) &&
                      currentPort.PortLabel.Contains("I1", StringComparison.Ordinal),
                    "结构化纯电流源网络：激励端口标注电流源 I1（无电压源时才回退）",
                    currentPort?.PortLabel ?? "missing");
                AcParametersInfo currentAc = currentOnlyResult.AcParameters ?? new AcParametersInfo();
                Check(currentAc.VoltageGain.Contains("电流源", StringComparison.Ordinal),
                    "结构化纯电流源网络：保留“无电压增益定义”说明", currentAc.VoltageGain);
                Console.WriteLine($"    [INFO] 结构化纯电流源网络：激励端口='{currentPort?.PositiveNet}–{currentPort?.NegativeNet}'" +
                                  $"（{currentPort?.PortLabel}），VoltageGain='{currentAc.VoltageGain}'");
            }

            // 8.5 semantics: AC mode already prefers the AC voltage source over the AC current source.
            var acDual = new Fixture(); acDual.NewNode(); acDual.NewNode();
            acDual.Iac("I1", 1, 0, 1e-3, 1000.0); acDual.R("R1", 1, 0, 1e3);
            acDual.Vac("V1", 2, 0, 2.0, 1000.0); acDual.R("R2", 2, 0, 1e3);
            PedagogicalSolution acDualResult = RunStrategy(acDual.Build(), 1000.0);
            Check(acDualResult.IsSuccess && acDualResult.LinearNetwork?.FrequencyResponse != null,
                "结构化双交流源：求解成功且生成频响", acDualResult.SummaryText);
            if (acDualResult.LinearNetwork?.FrequencyResponse != null)
            {
                string inputLabel = acDualResult.LinearNetwork.FrequencyResponse.InputPortLabel;
                Check(inputLabel.Contains("V1", StringComparison.Ordinal) &&
                      inputLabel.Contains("N2", StringComparison.Ordinal),
                    "结构化双交流源：交流模式优先选择交流电压源端口（V1 / N2）", inputLabel);
                Check(!inputLabel.Contains("I1", StringComparison.Ordinal),
                    "结构化双交流源：交流模式不再把交流电流源当激励端口", inputLabel);
                Console.WriteLine("    [INFO] 双交流源激励端口=" + inputLabel);
            }
        }
    }
    // =====================================================================
    // S9: end-to-end integration through NetlistExtractor (CircuitElement +
    //     CircuitPin coordinates / CircuitWire geometry -> ElectricalNetlist ->
    //     LinearNetworkAnalyzer -> MnaLinearStrategy), i.e. the real UI pipeline.
    // =====================================================================

    private static CircuitElement GeoElement(string id, ElementType type, double display, string unit,
        float x, float y, int rotationDeg = 0, double frequencyHz = 1000.0)
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = id,
            Type = type,
            Value = display,
            DisplayNumber = display,
            UnitSymbol = unit,
            FrequencyHz = frequencyHz,
            X = x,
            Y = y,
            RotationDeg = rotationDeg
        };
        element.InitializeDefaultPins();
        return element;
    }

    private static CircuitWire GeoWire(string id, float startX, float startY, float endX, float endY) => new()
    {
        Id = id,
        StartElementId = string.Empty,
        StartPinName = string.Empty,
        EndElementId = string.Empty,
        EndPinName = string.Empty,
        StartX = startX,
        StartY = startY,
        EndX = endX,
        EndY = endY
    };

    private static void CheckExtractorIntegration()
    {
        // A. DC divider built from real element coordinates + two CircuitWires.
        {
            var elements = new List<CircuitElement>
            {
                GeoElement("V1", ElementType.DcVoltageSource, 12.0, "V", 100, 200),
                GeoElement("R1", ElementType.Resistor, 1.0, "kΩ", 300, 165),
                GeoElement("R2", ElementType.Resistor, 2.0, "kΩ", 300, 300),
                GeoElement("GND1", ElementType.Ground, 0, string.Empty, 100, 257),
                GeoElement("GND2", ElementType.Ground, 0, string.Empty, 260, 322)
            };
            var wires = new List<CircuitWire>
            {
                GeoWire("W1", 100, 165, 260, 165),
                GeoWire("W2", 340, 165, 340, 300)
            };
            ElectricalNetlist netlist = NetlistExtractor.ExtractNetlist(elements, wires);
            Check(netlist.Nets.Count == 3, "集成 DC 分压（导线）：ExtractNetlist 得到 3 个网络",
                $"actual {netlist.Nets.Count}");
            Check(netlist.GroundNet != null, "集成 DC 分压（导线）：参考地识别成功");
            NetlistValidationResult validation = netlist.Validate();
            Check(validation.IsValid, "集成 DC 分压（导线）：Netlist.Validate 通过",
                string.Join("; ", validation.Issues.Select(i => i.Message)));

            ElectricalNet netOne = netlist.FindNetForPin("V1", "pos") ?? new ElectricalNet();
            ElectricalNet netMid = netlist.FindNetForPin("R1", "2") ?? new ElectricalNet();
            Check(netlist.FindNetForPin("V1", "pos") != null && netlist.FindNetForPin("R1", "1") != null &&
                  netlist.FindNetForPin("R1", "2") != null && netlist.FindNetForPin("R2", "2") != null,
                "集成 DC 分压（导线）：关键引脚均解析到网络");
            Check(netlist.FindNetForPin("R1", "1") == netlist.FindNetForPin("V1", "pos"),
                "集成 DC 分压（导线）：导线把 V1.pos 连到 R1.1");
            Check(netlist.FindNetForPin("R1", "2") == netlist.FindNetForPin("R2", "2"),
                "集成 DC 分压（导线）：导线把 R1.2 连到 R2.2");
            Check(netlist.FindNetForPin("R2", "1")?.IsGround == true &&
                  netlist.FindNetForPin("V1", "neg")?.IsGround == true,
                "集成 DC 分压（导线）：两个 GND 符号统一合并为参考地");

            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "集成 DC 分压（导线）：TryCreate 成功", createError);
            if (model != null)
            {
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "集成 DC 分压（导线）：TrySolveDc 成功", solveError);
                if (ok && dc != null)
                {
                    CheckRel(dc.NodeVoltage(netOne.NetId), 12.0, 1e-9, "集成 DC 分压（导线）：V(源节点) = 12 V", " V");
                    CheckRel(dc.NodeVoltage(netMid.NetId), 8.0, 1e-9, "集成 DC 分压（导线）：V(中点) = 8 V", " V");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, dc, false, 0);
                    Check(kclRatio <= 1.0, "集成 DC 分压（导线）：逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
            }

            // Independent reference for the same topology.
            var reference = new RefNetwork { NodeCount = 3 };
            reference.Add(RefKind.Vdc, "V1", 12.0, 1, 0);
            reference.Add(RefKind.R, "R1", 1e3, 1, 2);
            reference.Add(RefKind.R, "R2", 2e3, 2, 0);
            RefSolveResult referenceDc = ReferenceDc(reference);
            Check(referenceDc.Success && Math.Abs(referenceDc.NodeVoltage[2].Real - 8.0) <= 1e-12,
                "集成 DC 分压（导线）：独立参考 V(中点) = 8 V");

            TopologyClassifier.Classify(netlist, out ICircuitStrategy strategy);
            Check(strategy is MnaLinearStrategy, "集成 DC 分压（导线）：TopologyClassifier 选择 MnaLinearStrategy",
                strategy.GetType().Name);

            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "集成 DC 分压（导线）：MnaLinearStrategy 求解成功", result.SummaryText);
            if (result.LinearNetwork != null && model != null)
            {
                NodePotentialInfo? midPotential = result.LinearNetwork.NodePotentials
                    .FirstOrDefault(p => p.NetId == netMid.NetId);
                Check(midPotential != null && Math.Abs(midPotential.VoltageV - 8.0) <= 1e-9,
                    "集成 DC 分压（导线）：LinearNetwork 中点电位 = 8 V",
                    midPotential == null ? "条目缺失" : Fmt(midPotential.VoltageV) + " V");
            }
        }

        // B. AC RC low-pass built from abutted (snap-tolerance) element pins, no wires.
        {
            var elements = new List<CircuitElement>
            {
                GeoElement("V1", ElementType.AcVoltageSource, 1.0, "V", 300, 300, 0, 1000.0),
                GeoElement("R1", ElementType.Resistor, 1.0, "kΩ", 300, 225, 90),
                GeoElement("C1", ElementType.Capacitor, 1.0, "μF", 300, 150, 90),
                GeoElement("GND1", ElementType.Ground, 0, string.Empty, 300, 137),
                GeoElement("GND2", ElementType.Ground, 0, string.Empty, 300, 357)
            };
            ElectricalNetlist netlist = NetlistExtractor.ExtractNetlist(elements, new List<CircuitWire>());
            Check(netlist.Nets.Count == 3, "集成 AC RC（引脚贴合）：ExtractNetlist 得到 3 个网络",
                $"actual {netlist.Nets.Count}");
            NetlistValidationResult validation = netlist.Validate();
            Check(validation.IsValid, "集成 AC RC（引脚贴合）：Netlist.Validate 通过",
                string.Join("; ", validation.Issues.Select(i => i.Message)));

            ElectricalNet sourceNet = netlist.FindNetForPin("V1", "pos") ?? new ElectricalNet();
            ElectricalNet outputNet = netlist.FindNetForPin("R1", "1") ?? new ElectricalNet();
            Check(netlist.FindNetForPin("V1", "pos") == netlist.FindNetForPin("R1", "2"),
                "集成 AC RC（引脚贴合）：V1.pos 与 R1.2 贴合为同一网络");
            Check(netlist.FindNetForPin("R1", "1") == netlist.FindNetForPin("C1", "2"),
                "集成 AC RC（引脚贴合）：R1.1 与 C1.2 贴合为同一网络");
            Check(netlist.FindNetForPin("C1", "1")?.IsGround == true &&
                  netlist.FindNetForPin("V1", "neg")?.IsGround == true,
                "集成 AC RC（引脚贴合）：C1.1 / V1.neg 与 GND 符号合并为参考地");

            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "集成 AC RC（引脚贴合）：TryCreate 成功", createError);
            if (model != null)
            {
                double frequency = 1000.0;
                bool ok = model.TrySolveAc(frequency, out LinearSolution? ac, out string solveError) && ac != null;
                Check(ok, "集成 AC RC（引脚贴合）：TrySolveAc 成功", solveError);
                if (ok && ac != null)
                {
                    Complex expected = TransferRcLowPass(2.0 * Math.PI * frequency, 1e3, 1e-6);
                    CheckRel(ac.NodeMagnitude(sourceNet.NetId), 1.0, 1e-9,
                        "集成 AC RC（引脚贴合）：|V(in)| = 1 V 峰值", " V");
                    CheckRel(ac.NodeMagnitude(outputNet.NetId), expected.Magnitude, 1e-6,
                        "集成 AC RC（引脚贴合）：|V(out)| = |H(1kHz)|", " V");
                    CheckPhase(ac.NodePhaseDeg(outputNet.NetId), PhaseDeg(expected), 1e-4,
                        "集成 AC RC（引脚贴合）：∠V(out) = ∠H(1kHz)");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, ac, true, frequency);
                    Check(kclRatio <= 1.0, "集成 AC RC（引脚贴合）：交流 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
            }

            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "集成 AC RC（引脚贴合）：MnaLinearStrategy 求解成功", result.SummaryText);
            if (result.LinearNetwork?.FrequencyResponse != null)
            {
                Check(result.LinearNetwork.FrequencyResponse.Points.Count == 61,
                    "集成 AC RC（引脚贴合）：频响 61 点",
                    $"actual {result.LinearNetwork.FrequencyResponse.Points.Count}");
                double rcCutoff = 1.0 / (2.0 * Math.PI * 1e3 * 1e-6);
                if (result.LinearNetwork.FrequencyResponse.UpperCutoffHz.HasValue)
                {
                    CheckRel(result.LinearNetwork.FrequencyResponse.UpperCutoffHz.Value, rcCutoff, 0.05,
                        "集成 AC RC（引脚贴合）：上截止频率 ≈ 1/(2πRC)（≤5%）", " Hz");
                }
                else
                {
                    Check(false, "集成 AC RC（引脚贴合）：检测到上截止频率");
                }
            }
            else
            {
                Check(false, "集成 AC RC（引脚贴合）：FrequencyResponse 非空");
            }
        }

        // C. VCVS amplifier through the extractor: four-terminal pin networking + wires.
        {
            var elements = new List<CircuitElement>
            {
                GeoElement("V1", ElementType.DcVoltageSource, 1.0, "V", 100, 200),
                GeoElement("E1", ElementType.Vcvs, 2.0, "倍", 400, 200),
                GeoElement("R2", ElementType.Resistor, 1.0, "kΩ", 600, 185),
                GeoElement("GND1", ElementType.Ground, 0, string.Empty, 100, 257),
                GeoElement("GND2", ElementType.Ground, 0, string.Empty, 365, 237),
                GeoElement("GND3", ElementType.Ground, 0, string.Empty, 435, 237),
                GeoElement("GND4", ElementType.Ground, 0, string.Empty, 640, 207)
            };
            var wires = new List<CircuitWire>
            {
                GeoWire("W1", 100, 165, 365, 185),
                GeoWire("W2", 435, 185, 560, 185)
            };
            ElectricalNetlist netlist = NetlistExtractor.ExtractNetlist(elements, wires);
            Check(netlist.Nets.Count == 3, "集成 AC/DC 受控源 VCVS：ExtractNetlist 得到 3 个网络",
                $"actual {netlist.Nets.Count}");
            NetlistValidationResult validation = netlist.Validate();
            Check(validation.IsValid, "集成 VCVS：Netlist.Validate 通过",
                string.Join("; ", validation.Issues.Select(i => i.Message)));
            ElectricalNet inputNet = netlist.FindNetForPin("E1", "IN1") ?? new ElectricalNet();
            ElectricalNet outputNet = netlist.FindNetForPin("E1", "OUT1") ?? new ElectricalNet();
            Check(netlist.FindNetForPin("E1", "IN1") != null &&
                  netlist.FindNetForPin("E1", "IN2") != null &&
                  netlist.FindNetForPin("E1", "OUT1") != null &&
                  netlist.FindNetForPin("E1", "OUT2") != null,
                "集成 VCVS：四个引脚均解析到网络");
            Check(netlist.FindNetForPin("E1", "IN2")?.IsGround == true &&
                  netlist.FindNetForPin("E1", "OUT2")?.IsGround == true,
                "集成 VCVS：IN2/OUT2 接地");
            Check(netlist.FindNetForPin("E1", "IN1") == netlist.FindNetForPin("V1", "pos"),
                "集成 VCVS：W1 把 V1.pos 连到 E1.IN1");
            Check(netlist.FindNetForPin("E1", "OUT1") == netlist.FindNetForPin("R2", "1"),
                "集成 VCVS：W2 把 E1.OUT1 连到 R2.1");

            LinearNetworkAnalyzer? model = TryBuildModel(netlist, out string createError);
            Check(model != null, "集成 VCVS：TryCreate 成功", createError);
            if (model != null)
            {
                bool ok = model.TrySolveDc(out LinearSolution? dc, out string solveError) && dc != null;
                Check(ok, "集成 VCVS：TrySolveDc 成功", solveError);
                if (ok && dc != null)
                {
                    CheckRel(dc.NodeVoltage(inputNet.NetId), 1.0, 1e-9, "集成 VCVS：V(IN1) = 1 V", " V");
                    CheckRel(dc.NodeVoltage(outputNet.NetId), 2.0, 1e-9, "集成 VCVS：V(OUT1) = μ·Vin = 2 V", " V");
                    (double kclRatio, string kclDetail) = MeasureKcl(model, dc, false, 0);
                    Check(kclRatio <= 1.0, "集成 VCVS：逐节点 KCL 残差 ≤1e-9 相对量级", kclDetail);
                }
            }
            PedagogicalSolution result = RunStrategy(netlist, 1000.0);
            Check(result.IsSuccess, "集成 VCVS：MnaLinearStrategy 求解成功", result.SummaryText);
            if (result.LinearNetwork != null)
            {
                NodePotentialInfo? outPotential = result.LinearNetwork.NodePotentials
                    .FirstOrDefault(p => p.NetId == outputNet.NetId);
                Check(outPotential != null && Math.Abs(outPotential.VoltageV - 2.0) <= 1e-9,
                    "集成 VCVS：LinearNetwork 输出电位 = 2 V",
                    outPotential == null ? "条目缺失" : Fmt(outPotential.VoltageV) + " V");
            }
        }
    }
}
