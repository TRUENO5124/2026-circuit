// ============================================================================
// File: tests/ReportExportChecks/Program.cs
// Namespace: ReportExportChecks
// Description: Console verification harness for the native report export
//              service (Markdown / LaTeX+Circuitikz / Word .docx / HTML).
//
//   dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
//
// Builds a small hand-made R/C/V netlist through NetlistExtractor plus a
// PedagogicalSolution with 3 steps, Q point, AC parameters and structured
// linear-network data, then checks all four report formats. A failed solution,
// an empty/AC-less solution and a null content object are checked for graceful
// degradation. Exit code is non-zero when any assertion fails.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CircuitAssistant.Engine;
using CircuitAssistant.Models;
using CircuitAssistant.Services;

namespace ReportExportChecks;

internal static class Program
{
    private const string ProjectName = "RC 低通滤波器 <测试> & \"V1\" #1";
    private const string Step2Title = "求解 #2 回路电流";
    private const string Step3Title = "求解放大倍数与截止频率";
    private const string Step1Title = "建立 KVL 方程";
    private const string Step1Formula = "V_1 - I_{R1} R_1 - V_C = 0";
    private const string Step2Formula = "I_{R1} = \\frac{V_1}{R_1}";
    private const string Step3Formula = "A_u = \\frac{R_L}{R_1 + R_L}";
    private const string LinearBoundaryNote = "适用范围：线性阻容网络；边界：忽略寄生参数与温度漂移。";

    private static int _total;
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Redirected/piped console: keep default encoding instead of failing.
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("ReportExportChecks: CircuitAssistant 原生报告导出 (Markdown/LaTeX/DOCX/HTML)");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        var successContent = BuildSuccessFixture();

        // ------------------------------------------------------------------
        // Suite 1: Markdown
        // ------------------------------------------------------------------
        Console.WriteLine("[Suite 1] Markdown");
        var markdown = BuildTextSafely(() => ReportExportService.BuildMarkdown(successContent), "Markdown");
        Check(markdown.Length > 0, "Markdown 输出非空");
        Check(markdown.StartsWith("# CircuitAssistant 电路分析报告", StringComparison.Ordinal),
            "Markdown 含报告主标题");
        Check(markdown.Contains(ProjectName, StringComparison.Ordinal), "Markdown 含项目名称");
        Check(markdown.Contains("## 推导步骤", StringComparison.Ordinal), "Markdown 含推导步骤章节");
        Check(markdown.Contains(Step1Title, StringComparison.Ordinal), "Markdown 含步骤 1 标题");
        Check(markdown.Contains(Step2Title, StringComparison.Ordinal), "Markdown 含步骤 2 标题");
        Check(markdown.Contains(Step3Title, StringComparison.Ordinal), "Markdown 含步骤 3 标题");
        Check(markdown.Contains(Step1Formula, StringComparison.Ordinal) &&
              markdown.Contains(Step2Formula, StringComparison.Ordinal) &&
              markdown.Contains(Step3Formula, StringComparison.Ordinal), "Markdown 含全部步骤公式");
        Check(markdown.Contains("$$", StringComparison.Ordinal), "Markdown 公式使用 $$ 围栏");
        Check(markdown.Contains("电压源 <V1>", StringComparison.Ordinal), "Markdown 保留原文（不转义）");
        Check(markdown.Contains("```spice", StringComparison.Ordinal), "Markdown 含 spice 代码围栏");
        Check(markdown.Contains("V_V1", StringComparison.Ordinal) &&
              markdown.Contains("R_R1", StringComparison.Ordinal) &&
              markdown.Contains("C_C1", StringComparison.Ordinal), "Markdown 含 SPICE 器件行");
        Check(markdown.Contains(".END", StringComparison.Ordinal), "Markdown 含 SPICE .END");
        Check(markdown.Contains("20 μA", StringComparison.Ordinal) &&
              markdown.Contains("放大区", StringComparison.Ordinal), "Markdown 含 Q 点指标");
        Check(markdown.Contains("1.2 kΩ", StringComparison.Ordinal) &&
              markdown.Contains("99.99 kHz", StringComparison.Ordinal), "Markdown 含 AC 指标");
        Check(markdown.Contains("频率扫描点", StringComparison.Ordinal) &&
              markdown.Contains("10 Hz", StringComparison.Ordinal), "Markdown 含频率扫描点表");
        Check(markdown.Contains(LinearBoundaryNote, StringComparison.Ordinal),
            "Markdown 含适用范围/边界说明");
        Check(markdown.Contains("原理图未覆盖器件", StringComparison.Ordinal) &&
              markdown.Contains("T1", StringComparison.Ordinal), "Markdown 注明未覆盖器件");
        Check(markdown.Contains("浏览器打印", StringComparison.Ordinal), "Markdown 含 PDF/浏览器打印说明");

        // ------------------------------------------------------------------
        // Suite 2: HTML
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("[Suite 2] HTML");
        var html = BuildTextSafely(() => ReportExportService.BuildHtml(successContent), "HTML");
        Check(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "HTML 以 DOCTYPE 开头");
        Check(html.Contains("<html lang=\"zh-CN\">", StringComparison.Ordinal), "HTML 语言为 zh-CN");
        Check(html.Contains("<meta charset=\"utf-8\">", StringComparison.Ordinal), "HTML 声明 UTF-8");
        Check(html.Contains("<style>", StringComparison.Ordinal) &&
              html.Contains("</style>", StringComparison.Ordinal), "HTML 内联 CSS");
        Check(!html.Contains("http://", StringComparison.Ordinal) &&
              !html.Contains("https://", StringComparison.Ordinal), "HTML 自包含（无外部网络资源）");
        Check(html.Contains("&lt;测试&gt;", StringComparison.Ordinal) &&
              html.Contains("&amp;", StringComparison.Ordinal), "HTML 转义 < 与 &");
        Check(!html.Contains("<测试>", StringComparison.Ordinal), "HTML 无未转义项目名尖括号");
        Check(html.Contains("&lt;V1&gt;", StringComparison.Ordinal) &&
              !html.Contains("<V1>", StringComparison.Ordinal), "HTML 转义步骤内容中的尖括号");
        Check(html.Contains("<code>" + Step2Formula + "</code>", StringComparison.Ordinal),
            "HTML 用 <code> 呈现 LaTeX 公式");
        Check(html.Contains("<table>", StringComparison.Ordinal) &&
              html.Contains("关键指标", StringComparison.Ordinal), "HTML 含指标表格");
        Check(html.Contains("<pre class=\"code\"><code>", StringComparison.Ordinal) &&
              html.Contains("R_R1", StringComparison.Ordinal), "HTML 用 <pre> 呈现 SPICE");
        Check(html.Contains("20 μA", StringComparison.Ordinal), "HTML 含 Q 点数值");
        Check(html.Contains(LinearBoundaryNote, StringComparison.Ordinal), "HTML 含边界说明");
        Check(html.Contains("浏览器打印", StringComparison.Ordinal) &&
              html.Contains("PDF", StringComparison.Ordinal), "HTML 注明浏览器打印可转 PDF");
        Check(html.Contains("原理图未覆盖器件", StringComparison.Ordinal), "HTML 注明未覆盖器件");

        // ------------------------------------------------------------------
        // Suite 3: LaTeX + Circuitikz
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("[Suite 3] LaTeX + Circuitikz");
        var latex = BuildTextSafely(() => ReportExportService.BuildLatex(successContent), "LaTeX");
        Check(latex.Contains("\\documentclass[UTF8]{ctexart}", StringComparison.Ordinal),
            "LaTeX 使用 ctexart 文档类");
        Check(latex.Contains("\\usepackage{amsmath,amssymb}", StringComparison.Ordinal),
            "LaTeX 引入 amsmath/amssymb");
        Check(latex.Contains("\\usepackage{circuitikz}", StringComparison.Ordinal),
            "LaTeX 引入 circuitikz");
        Check(latex.Contains("\\usepackage{geometry}", StringComparison.Ordinal),
            "LaTeX 引入 geometry");
        Check(latex.Contains("\\begin{document}", StringComparison.Ordinal) &&
              latex.Contains("\\end{document}", StringComparison.Ordinal),
            "LaTeX 含 document 环境");
        Check(latex.Contains("\\begin{circuitikz}", StringComparison.Ordinal) &&
              latex.Contains("\\end{circuitikz}", StringComparison.Ordinal),
            "LaTeX 含 circuitikz 环境");
        Check(latex.Contains("\\coordinate (gnd) at (0,0)", StringComparison.Ordinal),
            "LaTeX 地为 (0,0) 且命名为 gnd");
        Check(latex.Contains("\\coordinate (n1)", StringComparison.Ordinal),
            "LaTeX 非地网使用 n1..nN 坐标");
        Check(latex.Contains("node[ground] {}", StringComparison.Ordinal), "LaTeX 绘制接地符号");
        Check(latex.Contains("to[R, l={", StringComparison.Ordinal) &&
              latex.Contains("to[C, l={", StringComparison.Ordinal) &&
              latex.Contains("to[V, l={", StringComparison.Ordinal), "LaTeX 绘制 R/C/V 支路");
        Check(latex.Contains(Step1Title, StringComparison.Ordinal) &&
              latex.Contains("\\#2 回路电流", StringComparison.Ordinal), "LaTeX 含步骤标题（含 # 转义）");
        Check(latex.Contains(Step1Formula, StringComparison.Ordinal) &&
              latex.Contains(Step2Formula, StringComparison.Ordinal) &&
              latex.Contains(Step3Formula, StringComparison.Ordinal), "LaTeX 原文输出步骤公式");
        Check(latex.Contains("占空比 50\\%", StringComparison.Ordinal) &&
              !latex.Contains("占空比 50%", StringComparison.Ordinal), "LaTeX 转义 %");
        Check(latex.Contains("a\\_b", StringComparison.Ordinal) &&
              latex.Contains("c\\%d", StringComparison.Ordinal) &&
              latex.Contains("\\#e", StringComparison.Ordinal), "LaTeX 转义 _ % #");
        Check(latex.Contains("\\{f\\}g", StringComparison.Ordinal) &&
              latex.Contains("\\$h", StringComparison.Ordinal), "LaTeX 转义 { } $");
        Check(latex.Contains("\\textasciitilde{}i", StringComparison.Ordinal) &&
              latex.Contains("\\textasciicircum{}j", StringComparison.Ordinal) &&
              latex.Contains("\\textbackslash{}k", StringComparison.Ordinal), "LaTeX 转义 ~ ^ \\");
        Check(latex.Contains("R\\_1 \\& 50\\%", StringComparison.Ordinal),
            "LaTeX 转义 circuitikz 元件标签特殊字符");
        Check(latex.Count(character => character == '{') ==
              latex.Count(character => character == '}'), "LaTeX 花括号平衡");
        Check(latex.Contains("\\begin{verbatim}", StringComparison.Ordinal) &&
              latex.Contains("\\end{verbatim}", StringComparison.Ordinal) &&
              latex.Contains("R_R1", StringComparison.Ordinal), "LaTeX 用 verbatim 输出 SPICE");
        Check(latex.Contains("原理图未覆盖器件", StringComparison.Ordinal) &&
              latex.Contains("T1", StringComparison.Ordinal), "LaTeX 注明原理图未覆盖器件");
        Check(latex.Contains(LinearBoundaryNote, StringComparison.Ordinal), "LaTeX 含边界说明");
        Check(latex.Contains("频率扫描点", StringComparison.Ordinal), "LaTeX 含频率扫描点表");

        // ------------------------------------------------------------------
        // Suite 4: Word .docx (hand-written OOXML)
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("[Suite 4] Word .docx OOXML");
        var docx = BuildBytesSafely(() => ReportExportService.BuildDocx(successContent), "DOCX");
        Check(docx.Length > 0, "DOCX 字节非空");

        var docxEntries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var docxNames = new List<string>();
        string docxReadError = string.Empty;
        var docxReadable = true;
        try
        {
            using var docxStream = new MemoryStream(docx);
            using var docxArchive = new ZipArchive(docxStream, ZipArchiveMode.Read);
            foreach (var entry in docxArchive.Entries)
            {
                docxNames.Add(entry.FullName);
                using var entryStream = entry.Open();
                using var reader = new StreamReader(entryStream, new UTF8Encoding(false), true);
                docxEntries[entry.FullName] = reader.ReadToEnd();
            }
        }
        catch (Exception exception)
        {
            docxReadable = false;
            docxReadError = exception.Message;
        }

        Check(docxReadable, "DOCX 是可解压的合法 zip", docxReadError);
        Check(docxNames.Count >= 4, "DOCX 含约 4 个必要 package entry",
            "entries=" + docxNames.Count);

        docxEntries.TryGetValue("[Content_Types].xml", out var contentTypesXml);
        docxEntries.TryGetValue("word/document.xml", out var documentXml);
        docxEntries.TryGetValue("word/styles.xml", out var stylesXml);

        Check(contentTypesXml != null, "DOCX 含 [Content_Types].xml");
        Check(docxNames.Contains("_rels/.rels", StringComparer.OrdinalIgnoreCase), "DOCX 含 _rels/.rels");
        Check(documentXml != null, "DOCX 含 word/document.xml");
        Check(stylesXml != null, "DOCX 含 word/styles.xml");
        Check(contentTypesXml != null &&
              contentTypesXml.Contains("/word/document.xml", StringComparison.Ordinal),
            "[Content_Types].xml 注册 word/document.xml");

        var contentTypesWellFormed = TryParseXml(contentTypesXml, out var contentTypesDocument);
        var documentWellFormed = TryParseXml(documentXml, out var documentDocument);
        var stylesWellFormed = TryParseXml(stylesXml, out _);
        Check(contentTypesWellFormed, "[Content_Types].xml 为 well-formed XML");
        Check(documentWellFormed, "word/document.xml 为 well-formed XML");
        Check(stylesWellFormed, "word/styles.xml 为 well-formed XML");
        Check(contentTypesDocument?.Root?.Name.LocalName == "Types", "[Content_Types].xml 根元素为 Types");
        Check(documentDocument?.Root?.Name.LocalName == "document", "document.xml 根元素为 document");

        var documentText = documentDocument?.Root?.Value ?? string.Empty;
        Check(documentText.Contains(ProjectName, StringComparison.Ordinal),
            "DOCX 文本含项目名（& < > 正确解码）");
        Check(documentText.Contains(Step1Title, StringComparison.Ordinal) &&
              documentText.Contains(Step2Title, StringComparison.Ordinal) &&
              documentText.Contains(Step3Title, StringComparison.Ordinal), "DOCX 文本含全部步骤标题");
        Check(documentText.Contains(Step1Formula, StringComparison.Ordinal) &&
              documentText.Contains(Step2Formula, StringComparison.Ordinal),
            "DOCX 文本含 LaTeX 公式原文（Unicode 文本）");
        Check(documentText.Contains("CircuitAssistant Generated SPICE Netlist", StringComparison.Ordinal) &&
              documentText.Contains("R_R1", StringComparison.Ordinal) &&
              documentText.Contains(".END", StringComparison.Ordinal), "DOCX 文本含 SPICE 网表");
        Check(documentText.Contains("20 μA", StringComparison.Ordinal) &&
              documentText.Contains("1.2 kΩ", StringComparison.Ordinal), "DOCX 文本含指标数值");
        Check(documentText.Contains(LinearBoundaryNote, StringComparison.Ordinal), "DOCX 文本含边界说明");
        Check(documentText.Contains("原理图未覆盖器件", StringComparison.Ordinal), "DOCX 文本注明未覆盖器件");
        Check(documentXml != null && documentXml.Contains("&amp;", StringComparison.Ordinal),
            "DOCX document.xml XML 转义 &");
        Check(documentXml != null && !documentXml.Contains("<测试>", StringComparison.Ordinal),
            "DOCX document.xml 无未转义尖括号内容");
        Check(documentXml != null &&
              documentXml.Contains("utf-8", StringComparison.OrdinalIgnoreCase),
            "DOCX XML 声明 UTF-8 编码");
        Check(!documentText.Contains('\uFFFD'), "DOCX UTF-8 中文无乱码替换字符");

        // Optional debug artifacts for external inspection (python zipfile / Word).
        // Nothing is written unless REPORT_EXPORT_ARTIFACTS_DIR is set.
        DumpArtifactsIfRequested(markdown, latex, html, docx);

        // ------------------------------------------------------------------
        // Suite 5: Failed solution / empty steps / no AC data graceful output
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("[Suite 5] 失败结果与空数据降级");
        var failedSolution = new PedagogicalSolution
        {
            IsSuccess = false,
            CircuitTitle = "无法完成分析",
            TopologyType = CircuitTopologyType.Unsupported,
            SubtypeName = "Unsupported",
            SummaryText = "测试失败原因：电路缺少参考地，无法建立 MNA 方程。",
            Steps = new List<PedagogicalSolutionStep>()
        };
        var failureContent = new AnalysisReportContent
        {
            ProjectName = "失败用例",
            GeneratedAt = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local),
            Netlist = successContent.Netlist,
            Solution = failedSolution
        };

        var failureMarkdown = BuildTextSafely(() => ReportExportService.BuildMarkdown(failureContent), "失败 Markdown");
        var failureLatex = BuildTextSafely(() => ReportExportService.BuildLatex(failureContent), "失败 LaTeX");
        var failureHtml = BuildTextSafely(() => ReportExportService.BuildHtml(failureContent), "失败 HTML");
        var failureDocx = BuildBytesSafely(() => ReportExportService.BuildDocx(failureContent), "失败 DOCX");

        Check(failureMarkdown.Contains("失败原因", StringComparison.Ordinal) &&
              failureMarkdown.Contains("缺少参考地", StringComparison.Ordinal), "失败 Solution：Markdown 写明失败原因");
        Check(failureMarkdown.Contains("失败", StringComparison.Ordinal), "失败 Solution：Markdown 标注失败状态");
        Check(failureLatex.Contains("\\documentclass[UTF8]{ctexart}", StringComparison.Ordinal) &&
              failureLatex.Contains("缺少参考地", StringComparison.Ordinal), "失败 Solution：LaTeX 结构合法且含原因");
        Check(failureHtml.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal) &&
              failureHtml.Contains("缺少参考地", StringComparison.Ordinal), "失败 Solution：HTML 结构合法且含原因");
        Check(failureDocx.Length > 0, "失败 Solution：DOCX 非空");
        Check(ReadDocxDocumentText(failureDocx).Contains("缺少参考地", StringComparison.Ordinal),
            "失败 Solution：DOCX 文本含失败原因");

        var bareSolution = new PedagogicalSolution
        {
            IsSuccess = true,
            CircuitTitle = "无 AC / 结构数据用例",
            TopologyType = CircuitTopologyType.MnaLinear,
            SubtypeName = "GeneralLinear",
            SummaryText = string.Empty,
            Steps = new List<PedagogicalSolutionStep>(),
            QPoint = null,
            AcParameters = null,
            LinearNetwork = new LinearNetworkInfo()
        };
        var bareContent = new AnalysisReportContent
        {
            ProjectName = "空数据用例",
            Netlist = successContent.Netlist,
            Solution = bareSolution
        };

        var bareMarkdown = BuildTextSafely(() => ReportExportService.BuildMarkdown(bareContent), "空数据 Markdown");
        var bareLatex = BuildTextSafely(() => ReportExportService.BuildLatex(bareContent), "空数据 LaTeX");
        var bareHtml = BuildTextSafely(() => ReportExportService.BuildHtml(bareContent), "空数据 HTML");
        var bareDocx = BuildBytesSafely(() => ReportExportService.BuildDocx(bareContent), "空数据 DOCX");

        Check(bareMarkdown.Contains("没有可用的推导步骤", StringComparison.Ordinal), "空 Steps：Markdown 说明无步骤");
        Check(bareMarkdown.Contains("无可用节点电位数据", StringComparison.Ordinal), "无结构数据：Markdown 说明节点数据为空");
        Check(bareMarkdown.Contains("无可用 AC 频率响应数据", StringComparison.Ordinal), "无 AC 数据：Markdown 说明频响为空");
        Check(bareMarkdown.Contains("—", StringComparison.Ordinal), "空指标：Markdown 使用 — 占位");
        Check(bareLatex.Count(character => character == '{') ==
              bareLatex.Count(character => character == '}'), "空数据：LaTeX 花括号仍平衡");
        Check(bareHtml.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "空数据：HTML 结构合法");
        Check(bareDocx.Length > 0 && ReadDocxDocumentText(bareDocx).Contains("空数据用例", StringComparison.Ordinal),
            "空数据：DOCX 结构合法");

        var nullContent = (AnalysisReportContent)null!;
        var nullMarkdown = BuildTextSafely(() => ReportExportService.BuildMarkdown(nullContent), "空内容 Markdown");
        var nullLatex = BuildTextSafely(() => ReportExportService.BuildLatex(nullContent), "空内容 LaTeX");
        var nullHtml = BuildTextSafely(() => ReportExportService.BuildHtml(nullContent), "空内容 HTML");
        var nullDocx = BuildBytesSafely(() => ReportExportService.BuildDocx(nullContent), "空内容 DOCX");
        Check(nullMarkdown.Contains("未命名项目", StringComparison.Ordinal), "空内容：Markdown 使用占位项目名");
        Check(nullLatex.Contains("\\begin{circuitikz}", StringComparison.Ordinal), "空内容：LaTeX 结构合法");
        Check(nullHtml.Contains("<!DOCTYPE html>", StringComparison.Ordinal), "空内容：HTML 结构合法");
        Check(nullDocx.Length > 0, "空内容：DOCX 结构合法");

        // ------------------------------------------------------------------
        // Suite 6: Format helpers / suggested file names
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("[Suite 6] 格式辅助 API");
        Check(ReportExportService.GetFileExtension(ReportFormat.Markdown) == ".md", "GetFileExtension(Markdown)=.md");
        Check(ReportExportService.GetFileExtension(ReportFormat.Latex) == ".tex", "GetFileExtension(Latex)=.tex");
        Check(ReportExportService.GetFileExtension(ReportFormat.Docx) == ".docx", "GetFileExtension(Docx)=.docx");
        Check(ReportExportService.GetFileExtension(ReportFormat.Html) == ".html", "GetFileExtension(Html)=.html");
        Check(ReportExportService.GetFormatDisplayName(ReportFormat.Markdown).Contains("Markdown"),
            "GetFormatDisplayName(Markdown)");
        Check(ReportExportService.GetFormatDisplayName(ReportFormat.Latex).Contains("LaTeX"),
            "GetFormatDisplayName(LaTeX)");
        Check(ReportExportService.GetFormatDisplayName(ReportFormat.Docx).Contains("Word"),
            "GetFormatDisplayName(Docx)");
        Check(ReportExportService.GetFormatDisplayName(ReportFormat.Html).Contains("HTML"),
            "GetFormatDisplayName(Html)");

        var suggestedName = ReportExportService.CreateSuggestedFileName("RC 低通滤波器/测试", ReportFormat.Docx);
        Check(suggestedName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase),
            "建议文件名扩展名正确");
        Check(suggestedName.StartsWith("RC_低通滤波器_测试", StringComparison.Ordinal),
            "建议文件名保留并清洗项目名");
        Check(!suggestedName.Any(character => "<>:\"/\\|?*".Contains(character)),
            "建议文件名不含 Windows 非法字符");
        var fallbackName = ReportExportService.CreateSuggestedFileName("   ", ReportFormat.Html);
        Check(fallbackName.StartsWith("CircuitAnalysis_", StringComparison.Ordinal) &&
              fallbackName.EndsWith(".html", StringComparison.Ordinal), "空项目名使用 CircuitAnalysis 回退");
        var longName = new string('A', 100);
        var longFileName = ReportExportService.CreateSuggestedFileName(longName, ReportFormat.Latex);
        Check(longFileName.Split('_')[0].Length <= 60, "超长项目名被截断");

        // ------------------------------------------------------------------
        // Summary
        // ------------------------------------------------------------------
        Console.WriteLine();
        if (Failures.Count > 0)
        {
            Console.WriteLine("Failures:");
            foreach (var failure in Failures)
            {
                Console.WriteLine("  - " + failure);
            }
            Console.WriteLine();
        }

        Console.WriteLine($"Total={_total} Passed={_passed} Failed={_total - _passed}");
        return _total == _passed && _total >= 25 ? 0 : 1;
    }

    // ======================================================================
    // Fixture construction
    // ======================================================================

    private static AnalysisReportContent BuildSuccessFixture()
    {
        var voltageSource = CreateElement("V1", ElementType.DcVoltageSource, 100, 300, 10, "V");
        var resistor = CreateElement("R1", ElementType.Resistor, 300, 300, 1, "kΩ", "R_1 & 50%");
        var capacitor = CreateElement("C1", ElementType.Capacitor, 300, 450, 1, "μF");
        var ground = CreateElement("GND_1", ElementType.Ground, 100, 450, 0, string.Empty);
        // A transformer cannot be drawn by the Circuitikz generator: it must be
        // skipped and reported as "原理图未覆盖器件".
        var transformer = CreateElement("T1", ElementType.Transformer, 700, 700, 0, string.Empty);

        var elements = new List<CircuitElement> { voltageSource, resistor, capacitor, ground, transformer };
        var wires = new List<CircuitWire>
        {
            CreateWire("W1", voltageSource, "pos", resistor, "1"),
            CreateWire("W2", resistor, "2", capacitor, "1"),
            CreateWire("W3", capacitor, "2", ground, "GND"),
            CreateWire("W4", ground, "GND", voltageSource, "neg")
        };

        var netlist = NetlistExtractor.Extract(elements, wires);

        var solution = new PedagogicalSolution
        {
            IsSuccess = true,
            CircuitTitle = "RC 一阶低通滤波电路（报告导出测试夹具）",
            TopologyType = CircuitTopologyType.MnaLinear,
            SubtypeName = "GeneralLinear",
            SummaryText = "SPICE MNA 矩阵分析求解成功：共解算 2 个独立节点电位与 1 个支路电流变量。",
            RawMnaLog = "G v = J",
            QPoint = new QPointInfo
            {
                Ibq = "20 μA",
                Icq = "2 mA",
                Vceq = "6 V",
                WorkingRegion = "放大区",
                IsActiveRegion = true
            },
            AcParameters = new AcParametersInfo
            {
                VoltageGain = "-100",
                InputResistance = "1.2 kΩ",
                OutputResistance = "2 kΩ",
                LowerCutoffFreq = "10 Hz",
                UpperCutoffFreq = "100 kHz",
                Bandwidth = "99.99 kHz"
            },
            LinearNetwork = new LinearNetworkInfo
            {
                ModelSummary = "通用线性网络 MNA 2.0",
                OperatingMode = "DC + AC 小信号",
                MatrixDimension = 3,
                BoundaryNote = LinearBoundaryNote,
                NodePotentials = new List<NodePotentialInfo>
                {
                    new()
                    {
                        NetName = "VCC", NetId = 1, VoltageV = 10, Display = "10 V",
                        LatexFormula = "V_{VCC}=10\\,\\mathrm{V}"
                    },
                    new()
                    {
                        NetName = "N2", NetId = 2, VoltageV = 5, Display = "5 V",
                        LatexFormula = "V_{N2}=5\\,\\mathrm{V}"
                    }
                },
                PortEquivalents = new List<PortEquivalentInfo>
                {
                    new()
                    {
                        PortLabel = "R1 端口 (VCC-N2)", PositiveNet = "VCC", NegativeNet = "N2",
                        ResistanceOhm = 1000, HasOpenCircuitVoltage = true, OpenCircuitVoltageV = 10,
                        ResistanceDisplay = "1 kΩ", OpenCircuitVoltageDisplay = "10 V",
                        Note = "独立源置零后测得"
                    }
                },
                FrequencyResponse = new FrequencyResponseInfo
                {
                    InputPortLabel = "Vin",
                    OutputPortLabel = "Vout",
                    OperatingFrequencyHz = 1000,
                    GainRatio = 0.1,
                    GainDb = -20,
                    PhaseDeg = -45,
                    GainRatioDisplay = "0.1",
                    GainDbDisplay = "-20 dB",
                    PhaseDisplay = "-45°",
                    LowerCutoffHz = 100,
                    UpperCutoffHz = 100000,
                    BandwidthHz = 99900,
                    LowerCutoffDisplay = "100 Hz",
                    UpperCutoffDisplay = "100 kHz",
                    BandwidthDisplay = "99.9 kHz",
                    BoundaryNote = "频响基于小信号线性化，未考虑非线性失真。",
                    Points = new List<AcSweepPointInfo>
                    {
                        new()
                        {
                            FrequencyHz = 10, GainRatio = 0.999, GainDb = -0.01, PhaseDeg = -2.9,
                            FrequencyDisplay = "10 Hz", GainDbDisplay = "-0.01 dB", PhaseDisplay = "-2.9°"
                        },
                        new()
                        {
                            FrequencyHz = 1000, GainRatio = 0.1, GainDb = -20, PhaseDeg = -45,
                            FrequencyDisplay = "1 kHz", GainDbDisplay = "-20 dB", PhaseDisplay = "-45°"
                        },
                        new()
                        {
                            FrequencyHz = 100000, GainRatio = 0.01, GainDb = -40, PhaseDeg = -88,
                            FrequencyDisplay = "100 kHz", GainDbDisplay = "-40 dB", PhaseDisplay = "-88°"
                        }
                    }
                }
            }
        };

        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 1,
            BadgeText = "建模",
            Title = Step1Title,
            MethodName = "基尔霍夫电压定律",
            Content = "对输入回路列写 KVL：电压源 <V1> 与电阻 R1 串联，占空比 50%。",
            LatexFormula = Step1Formula,
            SubstitutionFormula = "10 - I_{R1} \\times 1000 - V_C = 0",
            ResultValue = "I_R1 = 10 mA",
            Status = "Completed"
        });
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 2,
            BadgeText = "求解",
            Title = Step2Title,
            MethodName = "欧姆定律",
            Content = @"特殊字符测试：a_b & c%d#e{f}g$h~i^j\k 以及 <tag>。",
            LatexFormula = Step2Formula,
            SubstitutionFormula = "I_{R1} = \\frac{10}{1000} = 10\\,\\mathrm{mA}",
            ResultValue = "I_R1 = 10 mA",
            Status = "Completed"
        });
        solution.Steps.Add(new PedagogicalSolutionStep
        {
            StepIndex = 3,
            BadgeText = "AC 分析",
            Title = Step3Title,
            MethodName = "小信号交流分析",
            Content = "计算分压比与截止频率，确认低通特性。",
            LatexFormula = Step3Formula,
            SubstitutionFormula = "A_u = \\frac{1}{2}",
            ResultValue = "Au = 0.5",
            Status = "Completed"
        });

        return new AnalysisReportContent
        {
            ProjectName = ProjectName,
            ProjectFilePath = @"D:\BJTU\2026年大创\samples\rc_lowpass.circuit.json",
            GeneratedAt = new DateTime(2026, 10, 17, 17, 22, 47, DateTimeKind.Local),
            Netlist = netlist,
            Solution = solution
        };
    }

    private static CircuitElement CreateElement(
        string id,
        ElementType type,
        float x,
        float y,
        double displayNumber,
        string unitSymbol,
        string name = "")
    {
        var element = new CircuitElement
        {
            Id = id,
            Name = name,
            DefinitionId = "test." + type,
            Category = "test",
            Type = type,
            X = x,
            Y = y,
            DisplayNumber = displayNumber,
            UnitSymbol = unitSymbol
        };
        element.InitializeDefaultPins();
        return element;
    }

    private static CircuitWire CreateWire(
        string id,
        CircuitElement startElement,
        string startPinId,
        CircuitElement endElement,
        string endPinId)
    {
        var startPin = startElement.Pins.First(pin => pin.Id == startPinId);
        var endPin = endElement.Pins.First(pin => pin.Id == endPinId);
        var startPosition = startElement.GetPinWorldPosition(startPin);
        var endPosition = endElement.GetPinWorldPosition(endPin);

        return new CircuitWire
        {
            Id = id,
            StartElementId = startElement.Id,
            StartPinName = startPin.Id,
            StartX = startPosition.X,
            StartY = startPosition.Y,
            EndElementId = endElement.Id,
            EndPinName = endPin.Id,
            EndX = endPosition.X,
            EndY = endPosition.Y
        };
    }

    // ======================================================================
    // Assertion / IO helpers
    // ======================================================================

    private static void Check(bool condition, string name, string? details = null)
    {
        _total++;
        if (condition)
        {
            _passed++;
            Console.WriteLine($"[PASS] {name}");
            return;
        }

        var message = string.IsNullOrEmpty(details) ? name : name + " -> " + details;
        Failures.Add(message);
        Console.WriteLine($"[FAIL] {message}");
    }

    private static string BuildTextSafely(Func<string> factory, string formatName)
    {
        try
        {
            return factory();
        }
        catch (Exception exception)
        {
            Check(false, formatName + " 生成不抛异常", exception.GetType().Name + ": " + exception.Message);
            return string.Empty;
        }
    }

    private static byte[] BuildBytesSafely(Func<byte[]> factory, string formatName)
    {
        try
        {
            return factory();
        }
        catch (Exception exception)
        {
            Check(false, formatName + " 生成不抛异常", exception.GetType().Name + ": " + exception.Message);
            return Array.Empty<byte>();
        }
    }

    private static bool TryParseXml(string? text, out XDocument? document)
    {
        document = null;
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            document = XDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ReadDocxDocumentText(byte[] docx)
    {
        if (docx.Length == 0) return string.Empty;
        try
        {
            using var stream = new MemoryStream(docx);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = archive.GetEntry("word/document.xml");
            if (entry == null) return string.Empty;
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, new UTF8Encoding(false), true);
            var xml = reader.ReadToEnd();
            var document = XDocument.Parse(xml);
            return document.Root?.Value ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Writes the four generated reports to REPORT_EXPORT_ARTIFACTS_DIR when that
    /// environment variable is set. Used only for manual external validation
    /// (python zipfile / Word / LibreOffice); the export service itself never
    /// touches the file system.
    /// </summary>
    private static void DumpArtifactsIfRequested(string markdown, string latex, string html, byte[] docx)
    {
        var directory = Environment.GetEnvironmentVariable("REPORT_EXPORT_ARTIFACTS_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        try
        {
            Directory.CreateDirectory(directory);
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(Path.Combine(directory, "report.md"), markdown, utf8);
            File.WriteAllText(Path.Combine(directory, "report.tex"), latex, utf8);
            File.WriteAllText(Path.Combine(directory, "report.html"), html, utf8);
            File.WriteAllBytes(Path.Combine(directory, "report.docx"), docx);
            Console.WriteLine();
            Console.WriteLine("[INFO] 调试工件已写入: " + directory);
        }
        catch (Exception exception)
        {
            Check(false, "写出可选调试工件", exception.Message);
        }
    }
}
