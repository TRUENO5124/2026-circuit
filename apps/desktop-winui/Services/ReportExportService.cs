// ============================================================================
// File: apps/desktop-winui/Services/ReportExportService.cs
// Namespace: CircuitAssistant.Services
// Description: Native, pure in-memory analysis-report export for CircuitAssistant.
//
//   * Markdown        - human readable text report with $$...$$ math fences.
//   * LaTeX           - ctexart document with amsmath/amssymb and an
//                       auto-generated circuitikz schematic of the netlist.
//   * Word (.docx)    - hand-written minimal OOXML package (no NuGet
//                       dependency, only System.IO.Compression + System.Xml.Linq).
//   * HTML            - self-contained page (inline CSS, no network resources).
//
// The service never writes files, never opens a file picker and has no WinUI
// dependency: callers receive a string or a byte[] and persist it themselves.
// PDF is not produced directly (HTML can be printed to PDF by a browser, LaTeX
// can be compiled with xelatex outside the app).
//
// The four output formats are rendered from one neutral document model so that
// the report semantics (header, metrics, derivation steps, structured linear
// network, SPICE deck) stay identical across formats.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;

namespace CircuitAssistant.Services;

/// <summary>Supported report export formats. PDF is intentionally not a format.</summary>
public enum ReportFormat
{
    Markdown,
    Latex,
    Docx,
    Html
}

/// <summary>
/// Input payload for one analysis-report export. All report content is derived
/// from the extracted netlist and the pedagogical solution; nothing is read from
/// disk.
/// </summary>
public sealed class AnalysisReportContent
{
    public string ProjectName { get; set; } = "";

    public string? ProjectFilePath { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public ElectricalNetlist Netlist { get; set; } = null!;

    public PedagogicalSolution Solution { get; set; } = null!;
}

/// <summary>
/// Stateless report factory. Every method is a pure function of its argument:
/// it builds the report in memory and returns it. Missing/partial input data
/// (failed solution, empty steps, no AC or structured data) yields a structurally
/// valid report instead of an exception.
/// </summary>
public static class ReportExportService
{
    /// <summary>Builds the report as a Markdown document.</summary>
    public static string BuildMarkdown(AnalysisReportContent content) =>
        MarkdownReportRenderer.Render(ReportComposer.Compose(content));

    /// <summary>Builds the report as a complete LaTeX source file (ctexart + circuitikz).</summary>
    public static string BuildLatex(AnalysisReportContent content) =>
        LatexReportRenderer.Render(ReportComposer.Compose(content));

    /// <summary>Builds the report as a minimal but valid Word .docx OOXML package.</summary>
    public static byte[] BuildDocx(AnalysisReportContent content) =>
        WordReportRenderer.Render(ReportComposer.Compose(content));

    /// <summary>Builds the report as a self-contained HTML page.</summary>
    public static string BuildHtml(AnalysisReportContent content) =>
        HtmlReportRenderer.Render(ReportComposer.Compose(content));

    /// <summary>Returns the file extension (including the leading dot) for the format.</summary>
    public static string GetFileExtension(ReportFormat format) => format switch
    {
        ReportFormat.Markdown => ".md",
        ReportFormat.Latex => ".tex",
        ReportFormat.Docx => ".docx",
        ReportFormat.Html => ".html",
        _ => ".txt"
    };

    /// <summary>Returns a UI-friendly display name for the format.</summary>
    public static string GetFormatDisplayName(ReportFormat format) => format switch
    {
        ReportFormat.Markdown => "Markdown 文档 (*.md)",
        ReportFormat.Latex => "LaTeX + Circuitikz 源文件 (*.tex)",
        ReportFormat.Docx => "Word 文档 (*.docx)",
        ReportFormat.Html => "HTML 网页 (*.html)",
        _ => "未知格式"
    };

    /// <summary>
    /// Builds a suggested, Windows-safe file name from a project name and the
    /// target format, e.g. "RC低通滤波器_20261017_173000.md".
    /// </summary>
    public static string CreateSuggestedFileName(string projectName, ReportFormat format)
    {
        var stem = SanitizeFileStem(projectName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        return $"{stem}_{timestamp}{GetFileExtension(format)}";
    }

    private static string SanitizeFileStem(string? projectName)
    {
        var raw = (projectName ?? string.Empty).Trim();
        if (raw.Length == 0) return "CircuitAnalysis";

        var invalid = new HashSet<char>("<>:\"/\\|?*".ToCharArray());
        var builder = new StringBuilder(raw.Length);
        foreach (var character in raw)
        {
            if (char.IsControl(character) || invalid.Contains(character) || char.IsWhiteSpace(character))
            {
                if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
                continue;
            }

            builder.Append(character);
        }

        var stem = builder.ToString().Trim('_', ' ', '.');
        const int maxStemLength = 60;
        if (stem.Length > maxStemLength) stem = stem[..maxStemLength].Trim('_', ' ', '.');
        return stem.Length == 0 ? "CircuitAnalysis" : stem;
    }
}

// ============================================================================
// Internal neutral document model - one model, four renderers.
// ============================================================================

internal abstract class ReportBlock
{
}

internal sealed class ReportHeading : ReportBlock
{
    public int Level { get; set; } = 2;
    public string Text { get; set; } = string.Empty;
}

internal sealed class ReportParagraph : ReportBlock
{
    public string Text { get; set; } = string.Empty;
}

internal sealed class ReportList : ReportBlock
{
    public List<string> Items { get; } = new();
}

internal sealed class ReportTable : ReportBlock
{
    public string? Caption { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

internal sealed class ReportFormula : ReportBlock
{
    public string Label { get; set; } = string.Empty;
    public string Latex { get; set; } = string.Empty;
}

internal sealed class ReportCode : ReportBlock
{
    public string Language { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

internal sealed class ReportNote : ReportBlock
{
    public string Text { get; set; } = string.Empty;
}

internal sealed record SchematicNode(string Coordinate, double X, double Y, string NetName);

internal sealed record SchematicWire(string From, string To, string ComponentKey, string Label);

internal sealed class ReportSchematic : ReportBlock
{
    public bool HasNetlist { get; set; }
    public bool HasGround { get; set; }
    public List<SchematicNode> Nodes { get; } = new();
    public List<SchematicWire> Wires { get; } = new();
    public List<(string Coordinate, string NetName)> NodeMap { get; } = new();
    public List<string> Uncovered { get; } = new();
    public HashSet<string> UncoveredIds { get; } = new(StringComparer.Ordinal);
}

internal sealed class ReportDocument
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public List<ReportBlock> Blocks { get; } = new();
}

// ============================================================================
// Report composer - builds the neutral document model.
// ============================================================================

internal static class ReportComposer
{
    public static ReportDocument Compose(AnalysisReportContent? content)
    {
        content ??= new AnalysisReportContent();
        PedagogicalSolution? solution = content.Solution;
        ElectricalNetlist? netlist = content.Netlist;

        var projectName = ReportFormatting.ValueOrFallback(
            ReportFormatting.SingleLine(content.ProjectName), "（未命名项目）");

        var document = new ReportDocument
        {
            Title = "CircuitAssistant 电路分析报告",
            Subtitle = "项目：" + projectName
        };

        AddReportInfo(document, content, solution, projectName);
        AddHeaderNotes(document, solution);
        AddMetrics(document, solution);
        AddSchematic(document, netlist);
        AddSteps(document, solution);
        AddLinearNetwork(document, solution?.LinearNetwork);
        AddSpice(document, netlist);
        AddExportNotes(document);
        return document;
    }

    private static void AddReportInfo(
        ReportDocument document,
        AnalysisReportContent content,
        PedagogicalSolution? solution,
        string projectName)
    {
        document.Blocks.Add(new ReportHeading { Level = 2, Text = "报告信息" });

        var rows = new List<List<string>>
        {
            Row("项目名称", projectName),
            Row("生成时间", ReportFormatting.FormatTimestamp(content.GeneratedAt)),
            Row("生成软件", "CircuitAssistant"),
            Row("电路标题", ReportFormatting.ValueOrDash(solution?.CircuitTitle)),
            Row("拓扑类型", solution == null
                ? ReportFormatting.Dash
                : DescribeTopologyType(solution.TopologyType)),
            Row("子类型", ReportFormatting.ValueOrDash(solution?.SubtypeName)),
            Row("分析状态", solution == null ? "无分析结果" : solution.IsSuccess ? "成功" : "失败")
        };

        if (!string.IsNullOrWhiteSpace(content.ProjectFilePath))
        {
            rows.Add(Row("工程文件", content.ProjectFilePath!));
        }

        document.Blocks.Add(new ReportTable
        {
            Headers = new List<string> { "项目", "内容" },
            Rows = rows
        });
    }

    private static void AddHeaderNotes(ReportDocument document, PedagogicalSolution? solution)
    {
        if (solution == null)
        {
            document.Blocks.Add(new ReportParagraph
            {
                Text = "未提供分析结果，本报告仅包含空模板结构与输入说明。"
            });
        }
        else if (!solution.IsSuccess)
        {
            document.Blocks.Add(new ReportHeading { Level = 3, Text = "失败原因" });
            document.Blocks.Add(new ReportParagraph
            {
                Text = ReportFormatting.ValueOrFallback(
                    solution.SummaryText?.Trim(), "求解未成功，未产生有效的分析结果。")
            });
        }
        else if (!string.IsNullOrWhiteSpace(solution.SummaryText))
        {
            document.Blocks.Add(new ReportHeading { Level = 3, Text = "摘要" });
            document.Blocks.Add(new ReportParagraph { Text = solution.SummaryText.Trim() });
        }

        var boundaryNote = solution?.LinearNetwork?.BoundaryNote;
        if (!string.IsNullOrWhiteSpace(boundaryNote))
        {
            document.Blocks.Add(new ReportHeading { Level = 3, Text = "适用范围与边界" });
            document.Blocks.Add(new ReportParagraph { Text = boundaryNote.Trim() });
        }
    }

    private static void AddMetrics(ReportDocument document, PedagogicalSolution? solution)
    {
        var qPoint = solution?.QPoint;
        var acParameters = solution?.AcParameters;

        document.Blocks.Add(new ReportHeading { Level = 2, Text = "关键指标" });

        document.Blocks.Add(new ReportHeading { Level = 3, Text = "静态工作点（Q 点）" });
        document.Blocks.Add(new ReportTable
        {
            Headers = new List<string> { "参数", "数值" },
            Rows = new List<List<string>>
            {
                Row("IBQ（基极电流）", ReportFormatting.ValueOrDash(qPoint?.Ibq)),
                Row("ICQ（集电极电流）", ReportFormatting.ValueOrDash(qPoint?.Icq)),
                Row("VCEQ（集射极电压）", ReportFormatting.ValueOrDash(qPoint?.Vceq)),
                Row("工作区", ReportFormatting.ValueOrDash(qPoint?.WorkingRegion))
            }
        });

        document.Blocks.Add(new ReportHeading { Level = 3, Text = "交流小信号参数" });
        var acRows = new List<List<string>>
        {
            Row("电压增益 Av", ReportFormatting.ValueOrDash(acParameters?.VoltageGain)),
            Row("输入电阻 Ri", ReportFormatting.ValueOrDash(acParameters?.InputResistance)),
            Row("输出电阻 Ro", ReportFormatting.ValueOrDash(acParameters?.OutputResistance)),
            Row("下限截止频率 fL", ReportFormatting.ValueOrDash(acParameters?.LowerCutoffFreq)),
            Row("上限截止频率 fH", ReportFormatting.ValueOrDash(acParameters?.UpperCutoffFreq)),
            Row("带宽 BW", ReportFormatting.ValueOrDash(acParameters?.Bandwidth))
        };
        if (acParameters?.HasSourceResistance == true)
        {
            acRows.Add(Row("信号源内阻 Rs", ReportFormatting.ValueOrDash(acParameters.SourceResistance)));
            acRows.Add(Row("源电压增益 Aus（含 Rs）", ReportFormatting.ValueOrDash(acParameters.SourceVoltageGain)));
        }
        document.Blocks.Add(new ReportTable
        {
            Headers = new List<string> { "参数", "数值" },
            Rows = acRows
        });
    }

    private static void AddSchematic(ReportDocument document, ElectricalNetlist? netlist)
    {
        document.Blocks.Add(new ReportHeading { Level = 2, Text = "电路原理图（自动生成）" });
        document.Blocks.Add(BuildSchematicBlock(netlist));
    }

    private static ReportSchematic BuildSchematicBlock(ElectricalNetlist? netlist)
    {
        var block = new ReportSchematic();
        if (netlist == null) return block;

        block.HasNetlist = true;

        var orderedNodes = netlist.Nodes.OrderBy(node => node.Index).ToList();
        var groundNode = orderedNodes.FirstOrDefault(node => node.IsReference || node.Net?.IsGround == true);
        block.HasGround = groundNode != null;

        var coordinateByNodeIndex = new Dictionary<int, string>();

        if (groundNode != null)
        {
            var groundNetName = GetNetDisplayName(groundNode);
            block.Nodes.Add(new SchematicNode("gnd", 0, 0, groundNetName));
            block.NodeMap.Add(("gnd", groundNetName));
            coordinateByNodeIndex[groundNode.Index] = "gnd";
        }

        var nonGroundIndex = 0;
        foreach (var node in orderedNodes)
        {
            if (groundNode != null && node.Index == groundNode.Index) continue;

            nonGroundIndex++;
            var coordinate = "n" + nonGroundIndex.ToString(CultureInfo.InvariantCulture);
            var x = 2 + ((nonGroundIndex - 1) % 2) * 5;
            var y = 3 + ((nonGroundIndex - 1) / 2) * 3;
            var netName = GetNetDisplayName(node);

            block.Nodes.Add(new SchematicNode(coordinate, x, y, netName));
            block.NodeMap.Add((coordinate, netName));
            coordinateByNodeIndex[node.Index] = coordinate;
        }

        var drawnElementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var branch in netlist.Branches)
        {
            var branchElement = branch.Element;
            if (branchElement == null)
            {
                continue;
            }

            var componentKey = GetCircuitikzComponentKey(branchElement.Type);
            if (componentKey == null)
            {
                AddUncoveredElement(block, branchElement);
                continue;
            }

            if (branch.PositiveNode == null || branch.NegativeNode == null ||
                !coordinateByNodeIndex.TryGetValue(branch.PositiveNode.Index, out var from) ||
                !coordinateByNodeIndex.TryGetValue(branch.NegativeNode.Index, out var to) ||
                string.Equals(from, to, StringComparison.Ordinal))
            {
                AddUncoveredElement(block, branchElement);
                continue;
            }

            block.Wires.Add(new SchematicWire(from, to, componentKey, BuildElementLabel(branchElement)));
            drawnElementIds.Add(branchElement.Id);
        }

        foreach (var element in netlist.Elements.Values)
        {
            if (element == null) continue;
            if (element.Type is ElementType.Ground or ElementType.VccPort) continue;
            if (drawnElementIds.Contains(element.Id)) continue;
            AddUncoveredElement(block, element);
        }

        return block;
    }

    private static void AddUncoveredElement(ReportSchematic block, CircuitElement? element)
    {
        if (element == null) return;
        if (!block.UncoveredIds.Add(element.Id)) return;
        block.Uncovered.Add($"{element.Id}（{element.Type}）");
    }

    private static string GetNetDisplayName(ElectricalNode node)
    {
        var name = ReportFormatting.SingleLine(node.Net?.Name);
        return name.Length > 0 ? name : "N" + node.Index.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildElementLabel(CircuitElement element)
    {
        var name = string.IsNullOrWhiteSpace(element.Name)
            ? ReportFormatting.SingleLine(element.Id)
            : ReportFormatting.SingleLine(element.Name);
        if (name.Length == 0) name = "?";

        if (double.IsFinite(element.DisplayNumber) && !string.IsNullOrWhiteSpace(element.UnitSymbol))
        {
            return $"{name} {ReportFormatting.FormatNumber(element.DisplayNumber)}{element.UnitSymbol}";
        }

        return name;
    }

    private static string? GetCircuitikzComponentKey(ElementType type) => type switch
    {
        ElementType.Resistor => "R",
        ElementType.Capacitor => "C",
        ElementType.CapacitorPolarized => "C",
        ElementType.Inductor => "L",
        ElementType.Diode => "D",
        ElementType.DcVoltageSource => "V",
        ElementType.AcVoltageSource => "V",
        ElementType.DcCurrentSource => "I",
        ElementType.AcCurrentSource => "I",
        ElementType.Vcvs => "vcvs",
        ElementType.Vccs => "vccs",
        ElementType.Ccvs => "ccvs",
        ElementType.Cccs => "cccs",
        _ => null
    };

    private static void AddSteps(ReportDocument document, PedagogicalSolution? solution)
    {
        document.Blocks.Add(new ReportHeading { Level = 2, Text = "推导步骤" });

        var steps = solution?.Steps;
        if (steps == null || steps.Count == 0)
        {
            document.Blocks.Add(new ReportParagraph
            {
                Text = solution is { IsSuccess: false }
                    ? "本次分析未成功，未产生可用的推导步骤。"
                    : "本次分析没有可用的推导步骤。"
            });
            return;
        }

        foreach (var step in steps)
        {
            var title = ReportFormatting.ValueOrFallback(
                ReportFormatting.SingleLine(step.Title),
                "步骤 " + step.StepIndex.ToString(CultureInfo.InvariantCulture));
            var badge = ReportFormatting.SingleLine(step.BadgeText);
            var headingText = "步骤 " + step.StepIndex.ToString(CultureInfo.InvariantCulture) + "：" + title;
            if (badge.Length > 0 && !string.Equals(badge, title, StringComparison.Ordinal))
            {
                headingText += "（" + badge + "）";
            }

            document.Blocks.Add(new ReportHeading { Level = 3, Text = headingText });

            var method = ReportFormatting.SingleLine(step.MethodName);
            if (method.Length > 0)
            {
                document.Blocks.Add(new ReportParagraph { Text = "求解方法：" + method });
            }

            if (!string.IsNullOrWhiteSpace(step.Content))
            {
                document.Blocks.Add(new ReportParagraph { Text = step.Content.Trim() });
            }

            if (!string.IsNullOrWhiteSpace(step.LatexFormula))
            {
                document.Blocks.Add(new ReportFormula
                {
                    Label = "公式（LaTeX 原文）",
                    Latex = step.LatexFormula
                });
            }

            if (!string.IsNullOrWhiteSpace(step.SubstitutionFormula))
            {
                document.Blocks.Add(new ReportFormula
                {
                    Label = "代入公式（LaTeX 原文）",
                    Latex = step.SubstitutionFormula
                });
            }

            if (!string.IsNullOrWhiteSpace(step.ResultValue))
            {
                document.Blocks.Add(new ReportParagraph { Text = "计算结果：" + step.ResultValue.Trim() });
            }
        }
    }

    private static void AddLinearNetwork(ReportDocument document, LinearNetworkInfo? network)
    {
        if (network == null) return;

        document.Blocks.Add(new ReportHeading { Level = 2, Text = "结构化线性网络" });

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(network.ModelSummary))
        {
            meta.Add("模型：" + ReportFormatting.SingleLine(network.ModelSummary));
        }

        if (!string.IsNullOrWhiteSpace(network.OperatingMode))
        {
            meta.Add("工作模式：" + ReportFormatting.SingleLine(network.OperatingMode));
        }

        if (network.MatrixDimension > 0)
        {
            meta.Add("矩阵阶数：" + network.MatrixDimension.ToString(CultureInfo.InvariantCulture) +
                     "×" + network.MatrixDimension.ToString(CultureInfo.InvariantCulture));
        }

        if (meta.Count > 0)
        {
            document.Blocks.Add(new ReportParagraph { Text = string.Join("；", meta) + "。" });
        }

        // --- Solved DC node potentials ---------------------------------------
        document.Blocks.Add(new ReportHeading { Level = 3, Text = "节点电位" });
        var nodePotentials = network.NodePotentials;
        if (nodePotentials == null || nodePotentials.Count == 0)
        {
            document.Blocks.Add(new ReportParagraph { Text = "无可用节点电位数据。" });
        }
        else
        {
            var rows = new List<List<string>>();
            foreach (var node in nodePotentials)
            {
                var nodeName = ReportFormatting.ValueOrFallback(
                    ReportFormatting.SingleLine(node.NetName),
                    "节点 " + node.NetId.ToString(CultureInfo.InvariantCulture));
                rows.Add(Row(
                    nodeName,
                    ReportFormatting.FormatValueWithUnit(node.Display, node.VoltageV, "V")));
            }

            document.Blocks.Add(new ReportTable
            {
                Headers = new List<string> { "节点网络", "节点电位" },
                Rows = rows
            });
        }

        // --- Arbitrary-port Thevenin equivalents -----------------------------
        document.Blocks.Add(new ReportHeading { Level = 3, Text = "任意端口戴维南等效" });
        var portEquivalents = network.PortEquivalents;
        if (portEquivalents == null || portEquivalents.Count == 0)
        {
            document.Blocks.Add(new ReportParagraph { Text = "无可用端口等效数据。" });
        }
        else
        {
            var rows = new List<List<string>>();
            foreach (var port in portEquivalents)
            {
                var openCircuitVoltage = port.HasOpenCircuitVoltage
                    ? port.OpenCircuitVoltageV
                    : double.NaN;
                rows.Add(Row(
                    ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(port.PortLabel), ReportFormatting.Dash),
                    ReportFormatting.FormatValueWithUnit(port.ResistanceDisplay, port.ResistanceOhm, "Ω"),
                    ReportFormatting.FormatValueWithUnit(port.OpenCircuitVoltageDisplay, openCircuitVoltage, "V")));
            }

            document.Blocks.Add(new ReportTable
            {
                Headers = new List<string> { "端口", "戴维南电阻", "开路电压" },
                Rows = rows
            });
        }

        // --- Frequency response summary + sweep table ------------------------
        document.Blocks.Add(new ReportHeading { Level = 3, Text = "频率响应" });
        var frequencyResponse = network.FrequencyResponse;
        if (frequencyResponse == null)
        {
            document.Blocks.Add(new ReportParagraph { Text = "无可用 AC 频率响应数据。" });
        }
        else
        {
            document.Blocks.Add(new ReportTable
            {
                Headers = new List<string> { "参数", "数值" },
                Rows = new List<List<string>>
                {
                    Row("输入端口", ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(frequencyResponse.InputPortLabel), ReportFormatting.Dash)),
                    Row("输出端口", ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(frequencyResponse.OutputPortLabel), ReportFormatting.Dash)),
                    Row("工作频率", ReportFormatting.FormatValueWithUnit(
                        null, frequencyResponse.OperatingFrequencyHz, "Hz")),
                    Row("电压增益比", ReportFormatting.FormatValueWithUnit(
                        frequencyResponse.GainRatioDisplay, frequencyResponse.GainRatio, string.Empty)),
                    Row("增益（dB）", ReportFormatting.FormatValueWithUnit(
                        frequencyResponse.GainDbDisplay, frequencyResponse.GainDb, "dB")),
                    Row("相位", ReportFormatting.FormatValueWithUnit(
                        frequencyResponse.PhaseDisplay, frequencyResponse.PhaseDeg, "°")),
                    Row("下限截止频率", ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(frequencyResponse.LowerCutoffDisplay), ReportFormatting.Dash)),
                    Row("上限截止频率", ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(frequencyResponse.UpperCutoffDisplay), ReportFormatting.Dash)),
                    Row("带宽", ReportFormatting.ValueOrFallback(
                        ReportFormatting.SingleLine(frequencyResponse.BandwidthDisplay), ReportFormatting.Dash))
                }
            });

            var sweepPoints = frequencyResponse.Points;
            if (sweepPoints != null && sweepPoints.Count > 0)
            {
                document.Blocks.Add(new ReportHeading { Level = 4, Text = "频率扫描点" });
                var rows = new List<List<string>>();
                foreach (var point in sweepPoints)
                {
                    rows.Add(Row(
                        ReportFormatting.FormatValueWithUnit(point.FrequencyDisplay, point.FrequencyHz, "Hz"),
                        ReportFormatting.FormatValueWithUnit(point.GainDbDisplay, point.GainDb, "dB"),
                        ReportFormatting.FormatValueWithUnit(point.PhaseDisplay, point.PhaseDeg, "°")));
                }

                document.Blocks.Add(new ReportTable
                {
                    Headers = new List<string> { "频率", "增益（dB）", "相位" },
                    Rows = rows
                });
            }

            if (!string.IsNullOrWhiteSpace(frequencyResponse.BoundaryNote))
            {
                document.Blocks.Add(new ReportNote
                {
                    Text = "频响边界说明：" + frequencyResponse.BoundaryNote.Trim()
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(network.BoundaryNote))
        {
            document.Blocks.Add(new ReportNote
            {
                Text = "适用范围与边界：" + network.BoundaryNote.Trim()
            });
        }
    }

    private static void AddSpice(ReportDocument document, ElectricalNetlist? netlist)
    {
        document.Blocks.Add(new ReportHeading { Level = 2, Text = "SPICE 网表" });

        string spiceText;
        try
        {
            spiceText = netlist != null ? netlist.ToSpiceNetlist() : "* 无网表数据，无法生成 SPICE 网表。";
        }
        catch (Exception exception)
        {
            spiceText = "* SPICE 网表生成失败：" + exception.Message;
        }

        document.Blocks.Add(new ReportCode { Language = "spice", Text = spiceText });
    }

    private static void AddExportNotes(ReportDocument document)
    {
        document.Blocks.Add(new ReportHeading { Level = 2, Text = "导出说明" });
        document.Blocks.Add(new ReportParagraph
        {
            Text = "本报告由 CircuitAssistant 在内存中生成，未写入本地文件。PDF 不在本版本直接生成：" +
                   "可在浏览器打印 HTML 报告为 PDF，或使用 xelatex 编译本 LaTeX 源文件（含自动生成的 " +
                   "Circuitikz 原理图）。本机未安装 TeX 环境，LaTeX 源码未做编译验证。"
        });
        document.Blocks.Add(new ReportParagraph
        {
            Text = "Word(.docx) 导出为手写最小 OOXML 包，仅包含文档正文与基础样式，未包含图片与公式对象。"
        });
    }

    private static List<string> Row(params string[] values) => new(values);

    private static string DescribeTopologyType(CircuitTopologyType topologyType) => topologyType switch
    {
        CircuitTopologyType.FirstOrderRc => "一阶 RC 电路（FirstOrderRc）",
        CircuitTopologyType.FirstOrderRl => "一阶 RL 电路（FirstOrderRl）",
        CircuitTopologyType.SecondOrderRlc => "二阶 RLC 电路（SecondOrderRlc）",
        CircuitTopologyType.BjtAmplifier => "BJT 放大电路（BjtAmplifier）",
        CircuitTopologyType.MosfetAmplifier => "MOSFET 放大电路（MosfetAmplifier）",
        CircuitTopologyType.MnaLinear => "通用线性网络（MnaLinear）",
        CircuitTopologyType.Disconnected => "电路不连通（Disconnected）",
        CircuitTopologyType.OpenCircuit => "开路（OpenCircuit）",
        CircuitTopologyType.Unsupported => "不支持/未识别（Unsupported）",
        _ => topologyType.ToString()
    };
}

// ============================================================================
// Markdown renderer.
// ============================================================================

internal static class MarkdownReportRenderer
{
    public static string Render(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.Append("# ").Append(document.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.Append(document.Subtitle).Append("\n\n");
        }

        foreach (var block in document.Blocks)
        {
            RenderBlock(builder, block);
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    private static void RenderBlock(StringBuilder builder, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                builder.Append(new string('#', Math.Clamp(heading.Level, 2, 6)))
                       .Append(' ').Append(heading.Text).Append("\n\n");
                break;
            case ReportParagraph paragraph:
                if (!string.IsNullOrWhiteSpace(paragraph.Text))
                {
                    builder.Append(paragraph.Text.Trim()).Append("\n\n");
                }
                break;
            case ReportList list:
                foreach (var item in list.Items)
                {
                    builder.Append("- ").Append(item.Trim()).Append('\n');
                }
                builder.Append('\n');
                break;
            case ReportTable table:
                RenderTable(builder, table);
                break;
            case ReportFormula formula:
                if (!string.IsNullOrWhiteSpace(formula.Label))
                {
                    builder.Append("**").Append(formula.Label).Append("**\n\n");
                }
                builder.Append("$$\n")
                       .Append(ReportFormatting.StripMathDelimiters(formula.Latex))
                       .Append("\n$$\n\n");
                break;
            case ReportCode code:
                builder.Append("```").Append(code.Language).Append('\n')
                       .Append(code.Text.TrimEnd()).Append("\n```\n\n");
                break;
            case ReportNote note:
                builder.Append("> ").Append(note.Text.Trim().Replace("\r\n", "\n").Replace("\n", "\n> "))
                       .Append("\n\n");
                break;
            case ReportSchematic schematic:
                RenderSchematic(builder, schematic);
                break;
        }
    }

    private static void RenderTable(StringBuilder builder, ReportTable table)
    {
        if (table.Headers.Count == 0) return;

        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            builder.Append("**").Append(table.Caption).Append("**\n\n");
        }

        builder.Append("| ")
               .Append(string.Join(" | ", table.Headers.Select(ReportFormatting.EscapeMarkdownCell)))
               .Append(" |\n");
        builder.Append('|')
               .Append(string.Join('|', table.Headers.Select(_ => " --- ")))
               .Append("|\n");

        foreach (var row in table.Rows)
        {
            var cells = Enumerable.Range(0, table.Headers.Count)
                .Select(index => index < row.Count ? row[index] : string.Empty);
            builder.Append("| ")
                   .Append(string.Join(" | ", cells.Select(ReportFormatting.EscapeMarkdownCell)))
                   .Append(" |\n");
        }

        builder.Append('\n');
    }

    private static void RenderSchematic(StringBuilder builder, ReportSchematic schematic)
    {
        if (!schematic.HasNetlist)
        {
            builder.Append("（无网表数据，无法生成原理图。）\n\n");
            return;
        }

        builder.Append("Circuitikz 原理图包含在 LaTeX 导出中；本格式列出节点映射与器件覆盖情况。\n\n");

        if (schematic.NodeMap.Count > 0)
        {
            builder.Append("**节点映射：**\n\n");
            foreach (var (coordinate, netName) in schematic.NodeMap)
            {
                builder.Append("- `").Append(coordinate).Append("` = ").Append(netName).Append('\n');
            }
            builder.Append('\n');
        }

        if (schematic.Uncovered.Count > 0)
        {
            builder.Append("**原理图未覆盖器件：**")
                   .Append(string.Join("，", schematic.Uncovered))
                   .Append("（已跳过绘制）\n\n");
        }
        else
        {
            builder.Append("**原理图覆盖：**全部二端器件均已绘制于 Circuitikz 源文件。\n\n");
        }
    }
}

// ============================================================================
// LaTeX renderer - ctexart + amsmath/amssymb + circuitikz.
// ============================================================================

internal static class LatexReportRenderer
{
    public static string Render(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("\\documentclass[UTF8]{ctexart}");
        builder.AppendLine("\\usepackage{geometry}");
        builder.AppendLine("\\usepackage{amsmath,amssymb}");
        builder.AppendLine("\\usepackage{circuitikz}");
        builder.AppendLine("\\geometry{a4paper,margin=2.2cm}");
        builder.AppendLine("\\begin{document}");
        builder.AppendLine("\\begin{center}");
        builder.AppendLine("{\\LARGE\\bfseries " + ReportFormatting.EscapeLatex(document.Title) + "}\\\\[6pt]");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.AppendLine("{\\large " + ReportFormatting.EscapeLatex(document.Subtitle) + "}");
        }
        builder.AppendLine("\\end{center}");
        builder.AppendLine();

        foreach (var block in document.Blocks)
        {
            RenderBlock(builder, block);
        }

        builder.AppendLine("\\end{document}");
        return builder.ToString();
    }

    private static void RenderBlock(StringBuilder builder, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                var command = heading.Level switch
                {
                    <= 2 => "\\section*",
                    3 => "\\subsection*",
                    _ => "\\subsubsection*"
                };
                builder.AppendLine(command + "{" + ReportFormatting.EscapeLatex(heading.Text) + "}");
                break;
            case ReportParagraph paragraph:
                foreach (var line in ReportFormatting.SplitLines(paragraph.Text))
                {
                    builder.AppendLine("\\noindent " + ReportFormatting.EscapeLatex(line) + "\\par");
                }
                builder.AppendLine();
                break;
            case ReportList list:
                builder.AppendLine("\\begin{itemize}");
                foreach (var item in list.Items)
                {
                    builder.AppendLine("\\item " + ReportFormatting.EscapeLatex(ReportFormatting.SingleLine(item)));
                }
                builder.AppendLine("\\end{itemize}");
                builder.AppendLine();
                break;
            case ReportTable table:
                RenderTable(builder, table);
                break;
            case ReportFormula formula:
                builder.AppendLine("\\[");
                builder.AppendLine(ReportFormatting.StripMathDelimiters(formula.Latex));
                builder.AppendLine("\\]");
                builder.AppendLine();
                break;
            case ReportCode code:
                builder.AppendLine("\\begin{verbatim}");
                builder.AppendLine(SanitizeVerbatim(code.Text.TrimEnd()));
                builder.AppendLine("\\end{verbatim}");
                builder.AppendLine();
                break;
            case ReportNote note:
                builder.AppendLine("\\begin{quote}");
                foreach (var line in ReportFormatting.SplitLines(note.Text))
                {
                    builder.AppendLine("\\noindent " + ReportFormatting.EscapeLatex(line) + "\\par");
                }
                builder.AppendLine("\\end{quote}");
                builder.AppendLine();
                break;
            case ReportSchematic schematic:
                RenderSchematic(builder, schematic);
                break;
        }
    }

    private static void RenderTable(StringBuilder builder, ReportTable table)
    {
        if (table.Headers.Count == 0) return;

        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            builder.AppendLine("\\noindent\\textbf{" + ReportFormatting.EscapeLatex(table.Caption) + "}\\par");
        }

        var columnCount = table.Headers.Count;
        builder.AppendLine("\\begin{center}");
        builder.AppendLine("\\begin{tabular}{|" + string.Join("|", Enumerable.Repeat("l", columnCount)) + "|}");
        builder.AppendLine("\\hline");
        builder.AppendLine(string.Join(" & ", table.Headers.Select(header =>
            ReportFormatting.EscapeLatex(ReportFormatting.SingleLine(header)))) + " \\\\");
        builder.AppendLine("\\hline");

        foreach (var row in table.Rows)
        {
            var cells = Enumerable.Range(0, columnCount)
                .Select(index => ReportFormatting.EscapeLatex(index < row.Count ? row[index] : string.Empty));
            builder.AppendLine(string.Join(" & ", cells) + " \\\\");
        }

        builder.AppendLine("\\hline");
        builder.AppendLine("\\end{tabular}");
        builder.AppendLine("\\end{center}");
        builder.AppendLine();
    }

    private static void RenderSchematic(StringBuilder builder, ReportSchematic schematic)
    {
        builder.AppendLine("\\begin{circuitikz}");

        if (!schematic.HasNetlist)
        {
            builder.AppendLine("% 无网表数据：原理图未生成");
        }
        else
        {
            foreach (var node in schematic.Nodes)
            {
                builder.AppendLine("\\coordinate (" + node.Coordinate + ") at (" +
                                   FormatCoordinate(node.X) + "," + FormatCoordinate(node.Y) + ");");
            }

            if (schematic.HasGround)
            {
                builder.AppendLine("\\draw (gnd) node[ground] {};");
            }

            foreach (var wire in schematic.Wires)
            {
                builder.AppendLine("\\draw (" + wire.From + ") to[" + wire.ComponentKey +
                                   ", l={" + ReportFormatting.EscapeLatex(wire.Label) + "}] (" +
                                   wire.To + ");");
            }
        }

        builder.AppendLine("\\end{circuitikz}");
        builder.AppendLine();

        if (!schematic.HasNetlist) return;

        if (schematic.NodeMap.Count > 0)
        {
            var nodeMapText = string.Join("；", schematic.NodeMap.Select(entry =>
                entry.Coordinate + " = " + entry.NetName));
            builder.AppendLine("\\noindent\\textit{节点映射：}" +
                               ReportFormatting.EscapeLatex(nodeMapText) + "\\par");
            builder.AppendLine();
        }

        if (schematic.Uncovered.Count > 0)
        {
            builder.AppendLine("\\noindent\\textbf{原理图未覆盖器件：}" +
                               ReportFormatting.EscapeLatex(string.Join("，", schematic.Uncovered)) +
                               "，已跳过绘制。\\par");
        }
        else
        {
            builder.AppendLine("\\noindent 原理图覆盖：全部二端器件均已绘制。\\par");
        }

        builder.AppendLine();
    }

    private static string FormatCoordinate(double value) =>
        double.IsFinite(value) ? value.ToString("0.##", CultureInfo.InvariantCulture) : "0";

    private static string SanitizeVerbatim(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("\\end{verbatim}", "\\end {verbatim}", StringComparison.Ordinal);
    }
}

// ============================================================================
// Word (.docx) renderer - hand-written minimal OOXML package.
// ============================================================================

internal static class WordReportRenderer
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    public static byte[] Render(ReportDocument document)
    {
        var documentXml = BuildDocumentXml(document);
        var stylesXml = BuildStylesXml();
        var contentTypesXml = BuildContentTypesXml();
        var packageRelsXml = BuildPackageRelsXml();
        var documentRelsXml = BuildDocumentRelsXml();

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", SerializeXml(contentTypesXml));
            AddEntry(archive, "_rels/.rels", SerializeXml(packageRelsXml));
            AddEntry(archive, "word/document.xml", SerializeXml(documentXml));
            AddEntry(archive, "word/_rels/document.xml.rels", SerializeXml(documentRelsXml));
            AddEntry(archive, "word/styles.xml", SerializeXml(stylesXml));
        }

        return buffer.ToArray();
    }

    private static XDocument BuildDocumentXml(ReportDocument document)
    {
        var body = new XElement(W + "body");

        body.Add(Paragraph(document.Title, bold: true, sizeHalfPoints: 36, centered: true));
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            body.Add(Paragraph(document.Subtitle, italic: true, centered: true));
        }
        body.Add(Paragraph(string.Empty));

        foreach (var block in document.Blocks)
        {
            foreach (var element in RenderBlock(block))
            {
                body.Add(element);
            }
        }

        body.Add(new XElement(W + "sectPr",
            new XElement(W + "pgSz",
                new XAttribute(W + "w", "11906"),
                new XAttribute(W + "h", "16838")),
            new XElement(W + "pgMar",
                new XAttribute(W + "top", "1134"),
                new XAttribute(W + "right", "1134"),
                new XAttribute(W + "bottom", "1134"),
                new XAttribute(W + "left", "1134"),
                new XAttribute(W + "header", "567"),
                new XAttribute(W + "footer", "567"),
                new XAttribute(W + "gutter", "0"))));

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                body));
    }

    private static IEnumerable<XElement> RenderBlock(ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                return new[]
                {
                    Paragraph(heading.Text, styleId: heading.Level switch
                    {
                        <= 2 => "Heading1",
                        3 => "Heading2",
                        _ => "Heading3"
                    })
                };
            case ReportParagraph paragraph:
                return ReportFormatting.SplitLines(paragraph.Text)
                    .Select(line => Paragraph(line))
                    .DefaultIfEmpty(Paragraph(string.Empty))
                    .ToArray();
            case ReportList list:
                return list.Items
                    .Select(item => Paragraph("• " + item.Trim()))
                    .DefaultIfEmpty(Paragraph(string.Empty))
                    .ToArray();
            case ReportTable table:
                return RenderTable(table);
            case ReportFormula formula:
                var runs = new List<XElement>();
                if (!string.IsNullOrWhiteSpace(formula.Label))
                {
                    runs.Add(Run(formula.Label + "：", bold: true));
                }
                runs.Add(Run(ReportFormatting.StripMathDelimiters(formula.Latex), font: "Consolas"));
                return new[] { Paragraph(runs.ToArray()) };
            case ReportCode code:
                return ReportFormatting.SplitLinePreservingEmpty(code.Text.TrimEnd())
                    .Select(line => Paragraph(Run(line, font: "Consolas")))
                    .ToArray();
            case ReportNote note:
                var noteLines = ReportFormatting.SplitLines(note.Text);
                if (noteLines.Count == 0) return Array.Empty<XElement>();
                var noteElements = new List<XElement>();
                for (var index = 0; index < noteLines.Count; index++)
                {
                    noteElements.Add(index == 0
                        ? Paragraph(Run("说明：", bold: true), Run(noteLines[index], italic: true))
                        : Paragraph(Run(noteLines[index], italic: true)));
                }
                return noteElements;
            case ReportSchematic schematic:
                return RenderSchematic(schematic);
            default:
                return Array.Empty<XElement>();
        }
    }

    private static IEnumerable<XElement> RenderTable(ReportTable table)
    {
        if (table.Headers.Count == 0 || table.Rows.Count == 0) return Array.Empty<XElement>();

        var elements = new List<XElement>();
        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            elements.Add(Paragraph(table.Caption!, bold: true));
        }

        var columnCount = table.Headers.Count;
        var tableElement = new XElement(W + "tbl",
            new XElement(W + "tblPr",
                new XElement(W + "tblW",
                    new XAttribute(W + "w", "0"),
                    new XAttribute(W + "type", "auto")),
                new XElement(W + "tblBorders",
                    Border("top"), Border("left"), Border("bottom"),
                    Border("right"), Border("insideH"), Border("insideV"))),
            new XElement(W + "tblGrid",
                Enumerable.Range(0, columnCount)
                    .Select(_ => new XElement(W + "gridCol", new XAttribute(W + "w", "3000")))));

        var headerCells = new List<XElement>();
        for (var index = 0; index < columnCount; index++)
        {
            var text = index < table.Headers.Count ? table.Headers[index] : string.Empty;
            headerCells.Add(TableCell(text, bold: true));
        }
        tableElement.Add(new XElement(W + "tr",
            new XElement(W + "trPr", new XElement(W + "tblHeader")),
            headerCells));

        foreach (var row in table.Rows)
        {
            var cells = new List<XElement>();
            for (var index = 0; index < columnCount; index++)
            {
                var text = index < row.Count ? row[index] : string.Empty;
                cells.Add(TableCell(text));
            }
            tableElement.Add(new XElement(W + "tr", cells));
        }

        elements.Add(tableElement);
        return elements;
    }

    private static IEnumerable<XElement> RenderSchematic(ReportSchematic schematic)
    {
        var elements = new List<XElement>();
        if (!schematic.HasNetlist)
        {
            elements.Add(Paragraph("（无网表数据，无法生成原理图。）"));
            return elements;
        }

        elements.Add(Paragraph(
            "自动原理图（Circuitikz）包含在 LaTeX 导出中；本 Word 报告保留节点映射与器件覆盖说明。"));

        if (schematic.NodeMap.Count > 0)
        {
            var nodeMapText = string.Join("；", schematic.NodeMap.Select(entry =>
                entry.Coordinate + " = " + entry.NetName));
            elements.Add(Paragraph(Run("节点映射：", bold: true), Run(nodeMapText)));
        }

        if (schematic.Uncovered.Count > 0)
        {
            elements.Add(Paragraph(
                Run("原理图未覆盖器件：", bold: true),
                Run(string.Join("，", schematic.Uncovered) + "，已跳过绘制。")));
        }
        else
        {
            elements.Add(Paragraph("原理图覆盖：全部二端器件均已绘制。"));
        }

        return elements;
    }

    private static XElement TableCell(string text, bool bold = false)
    {
        return new XElement(W + "tc",
            new XElement(W + "tcPr",
                new XElement(W + "tcW",
                    new XAttribute(W + "w", "3000"),
                    new XAttribute(W + "type", "dxa"))),
            Paragraph(Run(text, bold: bold)));
    }

    private static XElement Border(string name) =>
        new(W + name,
            new XAttribute(W + "val", "single"),
            new XAttribute(W + "sz", "4"),
            new XAttribute(W + "space", "0"),
            new XAttribute(W + "color", "auto"));

    private static XElement Paragraph(
        string text,
        string? styleId = null,
        bool bold = false,
        bool italic = false,
        string? font = null,
        int? sizeHalfPoints = null,
        bool centered = false)
    {
        return Paragraph(
            Run(text, bold: bold, italic: italic, font: font, sizeHalfPoints: sizeHalfPoints),
            styleId: styleId,
            centered: centered);
    }

    private static XElement Paragraph(params XElement[] runs) => Paragraph(runs, null, false);

    private static XElement Paragraph(XElement run, string? styleId, bool centered) =>
        Paragraph(new[] { run }, styleId, centered);

    private static XElement Paragraph(IReadOnlyList<XElement> runs, string? styleId, bool centered)
    {
        var paragraph = new XElement(W + "p");

        var properties = new XElement(W + "pPr");
        if (!string.IsNullOrWhiteSpace(styleId))
        {
            properties.Add(new XElement(W + "pStyle", new XAttribute(W + "val", styleId)));
        }
        if (centered)
        {
            properties.Add(new XElement(W + "jc", new XAttribute(W + "val", "center")));
        }
        if (properties.HasElements) paragraph.Add(properties);

        foreach (var run in runs)
        {
            paragraph.Add(run);
        }

        return paragraph;
    }

    private static XElement Run(
        string text,
        bool bold = false,
        bool italic = false,
        string? font = null,
        int? sizeHalfPoints = null)
    {
        var run = new XElement(W + "r");
        var properties = new XElement(W + "rPr");

        if (!string.IsNullOrWhiteSpace(font))
        {
            properties.Add(new XElement(W + "rFonts",
                new XAttribute(W + "ascii", font),
                new XAttribute(W + "hAnsi", font),
                new XAttribute(W + "eastAsia", font)));
        }
        if (bold) properties.Add(new XElement(W + "b"));
        if (italic) properties.Add(new XElement(W + "i"));
        if (sizeHalfPoints.HasValue)
        {
            var halfPoints = sizeHalfPoints.Value.ToString(CultureInfo.InvariantCulture);
            properties.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            properties.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPoints)));
        }

        if (properties.HasElements) run.Add(properties);

        var textElement = new XElement(W + "t", text);
        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
        {
            textElement.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
        }
        run.Add(textElement);
        return run;
    }

    private static XDocument BuildStylesXml()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(W + "styles",
                new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                new XElement(W + "docDefaults",
                    new XElement(W + "rPrDefault",
                        new XElement(W + "rPr",
                            new XElement(W + "rFonts",
                                new XAttribute(W + "ascii", "Calibri"),
                                new XAttribute(W + "hAnsi", "Calibri"),
                                new XAttribute(W + "eastAsia", "宋体")),
                            new XElement(W + "sz", new XAttribute(W + "val", "21")),
                            new XElement(W + "szCs", new XAttribute(W + "val", "21")))),
                    new XElement(W + "pPrDefault",
                        new XElement(W + "pPr",
                            new XElement(W + "spacing",
                                new XAttribute(W + "after", "120"),
                                new XAttribute(W + "line", "276"),
                                new XAttribute(W + "lineRule", "auto"))))),
                new XElement(W + "style",
                    new XAttribute(W + "type", "paragraph"),
                    new XAttribute(W + "default", "1"),
                    new XAttribute(W + "styleId", "Normal"),
                    new XElement(W + "name", new XAttribute(W + "val", "Normal")),
                    new XElement(W + "qFormat")),
                HeadingStyle("Heading1", "heading 1", "0", "36"),
                HeadingStyle("Heading2", "heading 2", "1", "30"),
                HeadingStyle("Heading3", "heading 3", "2", "26")));
    }

    private static XElement HeadingStyle(
        string styleId,
        string name,
        string outlineLevel,
        string sizeHalfPoints)
    {
        return new XElement(W + "style",
            new XAttribute(W + "type", "paragraph"),
            new XAttribute(W + "styleId", styleId),
            new XElement(W + "name", new XAttribute(W + "val", name)),
            new XElement(W + "basedOn", new XAttribute(W + "val", "Normal")),
            new XElement(W + "next", new XAttribute(W + "val", "Normal")),
            new XElement(W + "qFormat"),
            new XElement(W + "pPr",
                new XElement(W + "keepNext"),
                new XElement(W + "spacing",
                    new XAttribute(W + "before", "240"),
                    new XAttribute(W + "after", "80")),
                new XElement(W + "outlineLvl", new XAttribute(W + "val", outlineLevel))),
            new XElement(W + "rPr",
                new XElement(W + "b"),
                new XElement(W + "sz", new XAttribute(W + "val", sizeHalfPoints)),
                new XElement(W + "szCs", new XAttribute(W + "val", sizeHalfPoints))));
    }

    private static XDocument BuildContentTypesXml()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ContentTypes + "Types",
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType",
                        "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypes + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/word/document.xml"),
                    new XAttribute("ContentType",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
                new XElement(ContentTypes + "Override",
                    new XAttribute("PartName", "/word/styles.xml"),
                    new XAttribute("ContentType",
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));
    }

    private static XDocument BuildPackageRelsXml()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "word/document.xml"))));
    }

    private static XDocument BuildDocumentRelsXml()
    {
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(PackageRelationships + "Relationships",
                new XElement(PackageRelationships + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));
    }

    private static void AddEntry(ZipArchive archive, string entryName, byte[] payload)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(payload, 0, payload.Length);
    }

    private static byte[] SerializeXml(XDocument document)
    {
        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false,
            Indent = false
        };
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }
        return buffer.ToArray();
    }
}

// ============================================================================
// HTML renderer - self-contained page, inline CSS, no network resources.
// ============================================================================

internal static class HtmlReportRenderer
{
    private const string InlineStyle = """
        :root { color-scheme: light; }
        body { margin: 0; padding: 24px; background: #f4f6f9; color: #20242c;
               font-family: "Segoe UI", "Microsoft YaHei", system-ui, sans-serif; line-height: 1.65; }
        main.report { max-width: 980px; margin: 0 auto; background: #ffffff; padding: 32px 40px;
                      border-radius: 12px; box-shadow: 0 2px 14px rgba(0, 0, 0, 0.08); }
        h1 { font-size: 26px; margin: 0 0 6px; }
        p.subtitle { color: #5a6472; margin: 0 0 22px; }
        h2 { font-size: 20px; border-bottom: 2px solid #e3e7ee; padding-bottom: 6px; margin-top: 28px; }
        h3 { font-size: 16px; margin-top: 20px; }
        table { border-collapse: collapse; width: 100%; margin: 10px 0 16px; }
        th, td { border: 1px solid #d6dbe4; padding: 6px 10px; text-align: left; vertical-align: top; }
        th { background: #eef2f8; }
        code, pre { font-family: Consolas, "Cascadia Mono", monospace; font-size: 13px; }
        pre { background: #f3f5f9; border: 1px solid #e1e6ee; border-radius: 6px;
              padding: 10px 12px; overflow-x: auto; white-space: pre-wrap; word-break: break-word; }
        div.formula { margin: 10px 0; }
        p.formula-label { font-weight: 600; margin: 0 0 4px; color: #374151; }
        div.formula pre { background: #f8f9fc; }
        div.note { border-left: 4px solid #7c8db5; background: #f6f8fc; padding: 10px 14px;
                   margin: 16px 0; color: #3b4352; }
        ul.nodes { margin: 8px 0 12px; }
        """;

    public static string Render(ReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"zh-CN\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<meta name=\"generator\" content=\"CircuitAssistant\">");
        builder.AppendLine("<title>" + ReportFormatting.EscapeHtml(document.Title) + "</title>");
        builder.AppendLine("<style>");
        builder.AppendLine(InlineStyle);
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"report\">");
        builder.AppendLine("<h1>" + ReportFormatting.EscapeHtml(document.Title) + "</h1>");
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            builder.AppendLine("<p class=\"subtitle\">" + ReportFormatting.EscapeHtml(document.Subtitle) + "</p>");
        }

        foreach (var block in document.Blocks)
        {
            RenderBlock(builder, block);
        }

        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void RenderBlock(StringBuilder builder, ReportBlock block)
    {
        switch (block)
        {
            case ReportHeading heading:
                var tag = heading.Level switch
                {
                    <= 2 => "h2",
                    3 => "h3",
                    _ => "h4"
                };
                builder.AppendLine("<" + tag + ">" + ReportFormatting.EscapeHtml(heading.Text) + "</" + tag + ">");
                break;
            case ReportParagraph paragraph:
                var paragraphText = RenderMultiline(paragraph.Text);
                if (paragraphText.Length > 0)
                {
                    builder.AppendLine("<p>" + paragraphText + "</p>");
                }
                break;
            case ReportList list:
                builder.AppendLine("<ul>");
                foreach (var item in list.Items)
                {
                    builder.AppendLine("<li>" + ReportFormatting.EscapeHtml(item.Trim()) + "</li>");
                }
                builder.AppendLine("</ul>");
                break;
            case ReportTable table:
                RenderTable(builder, table);
                break;
            case ReportFormula formula:
                builder.AppendLine("<div class=\"formula\">");
                if (!string.IsNullOrWhiteSpace(formula.Label))
                {
                    builder.AppendLine("<p class=\"formula-label\">" +
                        ReportFormatting.EscapeHtml(formula.Label) + "</p>");
                }
                builder.AppendLine("<pre class=\"latex\"><code>" +
                    ReportFormatting.EscapeHtml(ReportFormatting.StripMathDelimiters(formula.Latex)) +
                    "</code></pre>");
                builder.AppendLine("</div>");
                break;
            case ReportCode code:
                builder.AppendLine("<pre class=\"code\"><code>" +
                    ReportFormatting.EscapeHtml(code.Text.TrimEnd()) + "</code></pre>");
                break;
            case ReportNote note:
                builder.AppendLine("<div class=\"note\"><p>" + RenderMultiline(note.Text) + "</p></div>");
                break;
            case ReportSchematic schematic:
                RenderSchematic(builder, schematic);
                break;
        }
    }

    private static void RenderTable(StringBuilder builder, ReportTable table)
    {
        if (table.Headers.Count == 0) return;

        if (!string.IsNullOrWhiteSpace(table.Caption))
        {
            builder.AppendLine("<p class=\"table-caption\"><strong>" +
                ReportFormatting.EscapeHtml(table.Caption) + "</strong></p>");
        }

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        foreach (var header in table.Headers)
        {
            builder.AppendLine("<th>" + ReportFormatting.EscapeHtml(header) + "</th>");
        }
        builder.AppendLine("</tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var row in table.Rows)
        {
            builder.AppendLine("<tr>");
            for (var index = 0; index < table.Headers.Count; index++)
            {
                var text = index < row.Count ? row[index] : string.Empty;
                builder.AppendLine("<td>" + ReportFormatting.EscapeHtml(text) + "</td>");
            }
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
    }

    private static void RenderSchematic(StringBuilder builder, ReportSchematic schematic)
    {
        if (!schematic.HasNetlist)
        {
            builder.AppendLine("<p>（无网表数据，无法生成原理图。）</p>");
            return;
        }

        builder.AppendLine("<p>Circuitikz 原理图包含在 LaTeX 导出中；本 HTML 报告列出节点映射与器件覆盖情况。</p>");

        if (schematic.NodeMap.Count > 0)
        {
            builder.AppendLine("<ul class=\"nodes\">");
            foreach (var (coordinate, netName) in schematic.NodeMap)
            {
                builder.AppendLine("<li><code>" + ReportFormatting.EscapeHtml(coordinate) +
                    "</code> = " + ReportFormatting.EscapeHtml(netName) + "</li>");
            }
            builder.AppendLine("</ul>");
        }

        if (schematic.Uncovered.Count > 0)
        {
            builder.AppendLine("<p><strong>原理图未覆盖器件：</strong>" +
                ReportFormatting.EscapeHtml(string.Join("，", schematic.Uncovered)) +
                "（已跳过绘制）</p>");
        }
        else
        {
            builder.AppendLine("<p><strong>原理图覆盖：</strong>全部二端器件均已绘制于 Circuitikz 源文件。</p>");
        }
    }

    private static string RenderMultiline(string? text)
    {
        var lines = ReportFormatting.SplitLines(text);
        return lines.Count == 0
            ? string.Empty
            : string.Join("<br>", lines.Select(ReportFormatting.EscapeHtml));
    }
}

// ============================================================================
// Shared formatting helpers.
// ============================================================================

internal static class ReportFormatting
{
    public const string Dash = "—";

    public static string SingleLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }

            if (pendingSpace && builder.Length > 0) builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }

    public static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Dash : SingleLine(value);

    public static string ValueOrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public static string FormatTimestamp(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    // Teaching display convention (2026-10-18): fallback display numbers keep at
    // most two decimals. Element values keep their plain form ("1000"); values that
    // would collapse to "0" at that precision use ≤2-decimal scientific notation
    // instead of silently losing the magnitude.
    public static string FormatNumber(double value)
    {
        if (!double.IsFinite(value)) return Dash;
        double abs = Math.Abs(value);
        return abs > 0 && abs < 1e-3
            ? ScientificNotationFormatter.ToUnicodeScientific(value)
            : TeachingDisplay.Plain(value);
    }

    public static string FormatValueWithUnit(string? display, double rawValue, string unit)
    {
        var text = SingleLine(display);
        if (text.Length > 0) return text;
        if (!double.IsFinite(rawValue)) return Dash;

        var number = FormatNumber(rawValue);
        return string.IsNullOrEmpty(unit) ? number : number + " " + unit;
    }

    public static string EscapeMarkdownCell(string? value) =>
        SingleLine(value).Replace("|", "\\|", StringComparison.Ordinal);

    public static string EscapeLatex(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length + 16);
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\': builder.Append("\\textbackslash{}"); break;
                case '{': builder.Append("\\{"); break;
                case '}': builder.Append("\\}"); break;
                case '$': builder.Append("\\$"); break;
                case '&': builder.Append("\\&"); break;
                case '#': builder.Append("\\#"); break;
                case '%': builder.Append("\\%"); break;
                case '_': builder.Append("\\_"); break;
                case '~': builder.Append("\\textasciitilde{}"); break;
                case '^': builder.Append("\\textasciicircum{}"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    public static string EscapeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length + 16);
        foreach (var character in value)
        {
            switch (character)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                default: builder.Append(character); break;
            }
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> SplitLines(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<string>();

        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(line => line.TrimEnd())
                    .Where(line => line.Length > 0)
                    .ToList();
    }

    public static IReadOnlyList<string> SplitLinePreservingEmpty(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Array.Empty<string>();

        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n')
                    .Select(line => line.TrimEnd())
                    .ToList();
    }

    public static string StripMathDelimiters(string? latex)
    {
        var text = (latex ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        if (text.StartsWith("$$", StringComparison.Ordinal) &&
            text.EndsWith("$$", StringComparison.Ordinal) &&
            text.Length > 4)
        {
            return text[2..^2].Trim();
        }

        if (text.StartsWith('$') && text.EndsWith('$') && text.Length > 2)
        {
            return text[1..^1].Trim();
        }

        if (text.StartsWith("\\[", StringComparison.Ordinal) &&
            text.EndsWith("\\]", StringComparison.Ordinal) &&
            text.Length > 4)
        {
            return text[2..^2].Trim();
        }

        return text;
    }
}
