// ============================================================================
// File: apps/desktop-winui/Engine/Strategies/MnaLinearStrategy.cs
// Namespace: CircuitAssistant.Engine.Strategies
// Description: Pedagogical Solving Strategy for General Linear Networks using
//              SPICE Modified Nodal Analysis (MNA) with Gaussian Elimination.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

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

        solution.QPoint = new QPointInfo
        {
            Ibq = "—",
            Icq = "—",
            Vceq = "—",
            WorkingRegion = "通用线性网络 (MNA)",
            IsActiveRegion = false
        };

        solution.AcParameters = new AcParametersInfo
        {
            VoltageGain = (nNodes > 0) ? $"{x[0]:F2} V" : "0",
            InputResistance = "N/A",
            OutputResistance = "N/A",
            LowerCutoffFreq = "0 Hz",
            UpperCutoffFreq = "N/A",
            Bandwidth = "N/A"
        };

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
            resSb.Append($@"i_{{{vSources[k].Id}}} = {ScientificNotationFormatter.ToLatexScientific(x[nNodes + k] * 1000.0)}\text{{ mA}},\quad ");
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
