using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CircuitAssistant.Controls;

public sealed partial class MathFormulaView : UserControl
{
    public static readonly DependencyProperty FormulaProperty =
        DependencyProperty.Register(
            nameof(Formula),
            typeof(string),
            typeof(MathFormulaView),
            new PropertyMetadata(string.Empty, OnFormulaChanged));

    public string Formula
    {
        get => (string)GetValue(FormulaProperty);
        set => SetValue(FormulaProperty, value);
    }

    public MathFormulaView()
    {
        InitializeComponent();
    }

    private static void OnFormulaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MathFormulaView view)
        {
            view.UpdateContent();
        }
    }

    private void UpdateContent()
    {
        if (string.IsNullOrWhiteSpace(Formula))
        {
            FormulaText.Text = string.Empty;
            return;
        }

        FormulaText.Text = FormatLatexToUnicode(Formula);
    }

    private void CopyLatex_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Formula)) return;
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(Formula);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);

            CopyIcon.Glyph = "\uE73E"; // Checkmark icon
            CopyText.Text = "已复制";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, args) =>
            {
                CopyIcon.Glyph = "\uE8C8";
                CopyText.Text = "LaTeX";
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CopyLatex Exception] {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a standard LaTeX mathematical formula string into rich Unicode plain text
    /// formatted for Cambria Math rendering (including 2D matrices, Greek, square roots, and subscripts).
    /// </summary>
    public static string FormatLatexToUnicode(string latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return string.Empty;
        string text = latex.Trim();

        // 1. Environments: Format 2D matrices (\begin{bmatrix} ... \end{bmatrix})
        text = FormatMatrixEnvironments(text);

        // 2. Remove standard LaTeX environments and alignment markers
        text = text.Replace(@"\begin{aligned}", "")
                   .Replace(@"\end{aligned}", "")
                   .Replace(@"\begin{array}", "")
                   .Replace(@"\end{array}", "")
                   .Replace(@"\left(", "(")
                   .Replace(@"\right)", ")")
                   .Replace(@"\left[", "[")
                   .Replace(@"\right]", "]")
                   .Replace(@"\left\{", "{")
                   .Replace(@"\right\}", "}")
                   .Replace(@"\left|", "|")
                   .Replace(@"\right|", "|")
                   .Replace(@"\{", "{")
                   .Replace(@"\}", "}")
                   .Replace(@"\quad", "   ")
                   .Replace(@"\qquad", "      ")
                   .Replace(@"\;", " ")
                   .Replace(@"\,", " ")
                   .Replace(@"\!", "")
                   .Replace("~", " ")
                   .Replace("&=", "= ")
                   .Replace("&", " ");

        // 3. Unpack formatting commands (\text, \mathrm, \mathbf, \mathit, \operatorname, \bm)
        text = UnpackEnclosingCommands(text, [@"\\text", @"\\mathrm", @"\\mathbf", @"\\mathit", @"\\operatorname", @"\\bm", @"\\boldsymbol"]);

        // 4. Greek Symbols (Case-sensitive, uppercase first)
        text = ReplaceGreekSymbols(text);

        // 5. Mathematical Operators & Relations
        text = ReplaceMathOperators(text);

        // 6. Balanced-brace Square Roots (\sqrt{...} -> √( ... ))
        text = ReplaceSquareRoots(text);

        // 7. Balanced-brace Fractions (\frac{num}{den} -> (num) / (den))
        text = ReplaceFractions(text);

        // 8. Powers and Superscripts
        text = ReplacePowers(text);

        // 9. Subscripts (Unicode Aware for Latin, Greek & Digits)
        text = ReplaceSubscripts(text);

        // 10. Line breaks: replace LaTeX '\\' with real environment newlines
        string[] lines = text.Split(new[] { @"\\", "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ReplaceGreekSymbols(string text)
    {
        // Uppercase Greek
        text = text.Replace(@"\Delta", "Δ")
                   .Replace(@"\Omega", "Ω")
                   .Replace(@"\Theta", "Θ")
                   .Replace(@"\Phi", "Φ")
                   .Replace(@"\Sigma", "Σ")
                   .Replace(@"\Pi", "Π");

        // Lowercase Greek
        text = text.Replace(@"\omega", "ω")
                   .Replace(@"\alpha", "α")
                   .Replace(@"\beta", "β")
                   .Replace(@"\tau", "τ")
                   .Replace(@"\mu", "μ")
                   .Replace(@"\pi", "π")
                   .Replace(@"\delta", "δ")
                   .Replace(@"\theta", "θ")
                   .Replace(@"\phi", "φ")
                   .Replace(@"\lambda", "λ")
                   .Replace(@"\sigma", "σ")
                   .Replace(@"\zeta", "ζ")
                   .Replace(@"\gamma", "γ")
                   .Replace(@"\epsilon", "ε")
                   .Replace(@"\eta", "η");

        return text;
    }

    private static string ReplaceMathOperators(string text)
    {
        return text.Replace(@"\pm", "±")
                   .Replace(@"\mp", "∓")
                   .Replace(@"\times", "×")
                   .Replace(@"\cdot", "·")
                   .Replace(@"\approx", "≈")
                   .Replace(@"\parallel", "∥")
                   .Replace(@"\le", "≤")
                   .Replace(@"\leq", "≤")
                   .Replace(@"\ge", "≥")
                   .Replace(@"\geq", "≥")
                   .Replace(@"\neq", "≠")
                   .Replace(@"\ne", "≠")
                   .Replace(@"\to", "→")
                   .Replace(@"\rightarrow", "→")
                   .Replace(@"\leftarrow", "←")
                   .Replace(@"\infty", "∞")
                   .Replace(@"\angle", "∠")
                   .Replace(@"\circ", "°")
                   .Replace(@"\partial", "∂")
                   .Replace(@"\int", "∫")
                   .Replace(@"\sum", "∑")
                   .Replace(@"\max", "max")
                   .Replace(@"\min", "min")
                   .Replace(@"\ln", "ln")
                   .Replace(@"\log", "log")
                   .Replace(@"\exp", "exp")
                   .Replace(@"\sin", "sin")
                   .Replace(@"\cos", "cos")
                   .Replace(@"\tan", "tan")
                   .Replace(@"\arctan", "arctan");
    }

    public static string ReplaceSquareRoots(string input)
    {
        string text = input;
        int maxIterations = 20;
        while (maxIterations-- > 0)
        {
            int idx = text.IndexOf(@"\sqrt");
            if (idx == -1) break;

            int braceStart = text.IndexOf('{', idx);
            if (braceStart == -1 || braceStart > idx + 10) break;

            int braceEnd = FindMatchingBrace(text, braceStart);
            if (braceEnd == -1) break;

            string inner = text.Substring(braceStart + 1, braceEnd - braceStart - 1).Trim();
            inner = FormatLatexToUnicode(inner);

            string replacement = $"√({inner})";
            text = text.Substring(0, idx) + replacement + text.Substring(braceEnd + 1);
        }
        return text;
    }

    public static string ReplaceFractions(string input)
    {
        string text = input;
        int maxIterations = 25;
        while (maxIterations-- > 0)
        {
            int fracIdx = text.IndexOf(@"\frac");
            if (fracIdx == -1) break;

            int numStart = text.IndexOf('{', fracIdx);
            if (numStart == -1) break;

            int numEnd = FindMatchingBrace(text, numStart);
            if (numEnd == -1) break;

            int denStart = text.IndexOf('{', numEnd + 1);
            if (denStart == -1 || denStart > numEnd + 4) break;

            int denEnd = FindMatchingBrace(text, denStart);
            if (denEnd == -1) break;

            string num = text.Substring(numStart + 1, numEnd - numStart - 1).Trim();
            string den = text.Substring(denStart + 1, denEnd - denStart - 1).Trim();

            num = ReplaceFractions(num);
            den = ReplaceFractions(den);

            bool needsParenDen = den.Contains('+') || den.Contains('-') || den.Contains('×') || den.Contains('·') || den.Contains('∥') || den.Contains('/');
            bool needsParenNum = num.Contains('+') || num.Contains('-');

            string numPart = needsParenNum ? $"({num})" : num;
            string denPart = needsParenDen ? $"({den})" : den;

            string replacement = $"{numPart} / {denPart}";
            text = text.Substring(0, fracIdx) + replacement + text.Substring(denEnd + 1);
        }
        return text;
    }

    private static string ReplacePowers(string text)
    {
        text = Regex.Replace(text, @"10\^\{([+-]?\d+)\}", m => "10" + ToSuperscript(m.Groups[1].Value));
        text = Regex.Replace(text, @"\^\{([+-]?\d+)\}", m => ToSuperscript(m.Groups[1].Value));
        text = Regex.Replace(text, @"\^([+-]?\d)", m => ToSuperscript(m.Groups[1].Value));
        text = Regex.Replace(text, @"\^2\b", "²");
        text = Regex.Replace(text, @"\^3\b", "³");
        text = text.Replace("0^+", "0⁺").Replace("0^-", "0⁻");
        return text;
    }

    private static string ReplaceSubscripts(string text)
    {
        text = Regex.Replace(text, @"([\p{L}\p{N}\)]|\b)_\{([^{}]+)\}", m =>
        {
            string baseChar = m.Groups[1].Value;
            string sub = m.Groups[2].Value;
            return FormatSubscriptToken(baseChar, sub);
        });

        text = Regex.Replace(text, @"([\p{L}\p{N}\)]|\b)_([0-9\p{L}])", m =>
        {
            string baseChar = m.Groups[1].Value;
            string sub = m.Groups[2].Value;
            return FormatSubscriptToken(baseChar, sub);
        });

        return text;
    }

    private static string FormatSubscriptToken(string baseChar, string sub)
    {
        bool allConvertible = sub.All(CanConvertToSubscript);
        if (allConvertible)
        {
            return baseChar + ToSubscript(sub);
        }
        return $"{baseChar}_{sub}";
    }

    private static bool CanConvertToSubscript(char c)
    {
        return char.IsDigit(c) || c is '+' or '-' or 'a' or 'e' or 'h' or 'i' or 'j' or 'k' or 'l' or 'm' or 'n' or 'o' or 'p' or 'r' or 's' or 't' or 'u' or 'v' or 'x';
    }

    public static string FormatMatrixEnvironments(string text)
    {
        string pattern = @"\\begin\{(?:bmatrix|pmatrix|matrix)\}([\s\S]*?)\\end\{(?:bmatrix|pmatrix|matrix)\}";
        return Regex.Replace(text, pattern, m =>
        {
            string matrixBody = m.Groups[1].Value.Trim();
            string[] rawRows = matrixBody.Split(new[] { @"\\" }, StringSplitOptions.RemoveEmptyEntries);
            if (rawRows.Length == 0) return "[ ]";

            var grid = new List<List<string>>();
            foreach (var r in rawRows)
            {
                var cells = r.Split('&').Select(c => FormatLatexToUnicode(c.Trim())).ToList();
                grid.Add(cells);
            }

            int rowCount = grid.Count;
            int colCount = grid.Max(r => r.Count);

            int[] colWidths = new int[colCount];
            for (int c = 0; c < colCount; c++)
            {
                colWidths[c] = grid.Max(r => c < r.Count ? r[c].Length : 0);
            }

            var sb = new StringBuilder();
            for (int r = 0; r < rowCount; r++)
            {
                string leftBracket = rowCount == 1 ? "[" : (r == 0 ? "⎡" : (r == rowCount - 1 ? "⎣" : "⎢"));
                string rightBracket = rowCount == 1 ? "]" : (r == 0 ? "⎤" : (r == rowCount - 1 ? "⎦" : "⎥"));

                sb.Append(leftBracket).Append(' ');
                for (int c = 0; c < colCount; c++)
                {
                    string cellVal = c < grid[r].Count ? grid[r][c] : "";
                    sb.Append(cellVal.PadLeft(colWidths[c]));
                    if (c < colCount - 1) sb.Append("  ");
                }
                sb.Append(' ').Append(rightBracket);
                if (r < rowCount - 1) sb.AppendLine();
            }
            return sb.ToString();
        });
    }

    private static string UnpackEnclosingCommands(string input, string[] commands)
    {
        string text = input;
        foreach (var cmd in commands)
        {
            string prefix = cmd.StartsWith(@"\\") ? cmd.Substring(1) : cmd;
            int maxLoops = 25;
            while (maxLoops-- > 0)
            {
                int cmdIdx = text.IndexOf(prefix + "{", StringComparison.Ordinal);
                if (cmdIdx == -1) break;

                int openIdx = cmdIdx + prefix.Length;
                int closeIdx = FindMatchingBrace(text, openIdx);
                if (closeIdx == -1) break;

                string inner = text.Substring(openIdx + 1, closeIdx - openIdx - 1);
                text = text.Substring(0, cmdIdx) + inner + text.Substring(closeIdx + 1);
            }
        }
        return text;
    }

    private static int FindMatchingBrace(string s, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    public static string ToSuperscript(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '-' => '⁻',
                '+' => '⁺',
                '0' => '⁰',
                '1' => '¹',
                '2' => '²',
                '3' => '³',
                '4' => '⁴',
                '5' => '⁵',
                '6' => '⁶',
                '7' => '⁷',
                '8' => '⁸',
                '9' => '⁹',
                _ => c
            });
        }
        return sb.ToString();
    }

    public static string ToSubscript(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '0' => '₀',
                '1' => '₁',
                '2' => '₂',
                '3' => '₃',
                '4' => '₄',
                '5' => '₅',
                '6' => '₆',
                '7' => '₇',
                '8' => '₈',
                '9' => '₉',
                '+' => '₊',
                '-' => '₋',
                'a' or 'A' => 'ₐ',
                'e' or 'E' => 'ₑ',
                'h' or 'H' => 'ₕ',
                'i' or 'I' => 'ᵢ',
                'j' or 'J' => 'ⱼ',
                'k' or 'K' => 'ₖ',
                'l' or 'L' => 'ₗ',
                'm' or 'M' => 'ₘ',
                'n' or 'N' => 'ₙ',
                'o' or 'O' => 'ₒ',
                'p' or 'P' => 'ₚ',
                'r' or 'R' => 'ᵣ',
                's' or 'S' => 'ₛ',
                't' or 'T' => 'ₜ',
                'u' or 'U' => 'ᵤ',
                'v' or 'V' => 'ᵥ',
                'x' or 'X' => 'ₓ',
                _ => c
            });
        }
        return sb.ToString();
    }
}
