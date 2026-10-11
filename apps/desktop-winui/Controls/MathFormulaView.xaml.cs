using System;
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

    public static string FormatLatexToUnicode(string latex)
    {
        string text = latex.Trim();

        // Remove aligned environment tags
        text = text.Replace(@"\begin{aligned}", "")
                   .Replace(@"\end{aligned}", "")
                   .Replace(@"\left(", "(")
                   .Replace(@"\right)", ")")
                   .Replace(@"\left[", "[")
                   .Replace(@"\right]", "]")
                   .Replace(@"\quad", "  ")
                   .Replace(@"\;", " ")
                   .Replace(@"\,", " ")
                   .Replace("&=", "= ")
                   .Replace("&", "");

        // Convert Greek symbols & operators
        text = text.Replace(@"\beta", "β")
                   .Replace(@"\alpha", "α")
                   .Replace(@"\pi", "π")
                   .Replace(@"\tau", "τ")
                   .Replace(@"\mu", "μ")
                   .Replace(@"\Omega", "Ω")
                   .Replace(@"\times", "×")
                   .Replace(@"\cdot", "·")
                   .Replace(@"\approx", "≈")
                   .Replace(@"\parallel", "∥")
                   .Replace(@"\max", "max");

        // Convert \text{...} -> ...
        text = Regex.Replace(text, @"\\text\{([^}]+)\}", "$1");

        // Convert scientific superscripts e.g. 10^{3} -> 10³, 10^{-3} -> 10⁻³
        text = text.Replace("10^{3}", "10³")
                   .Replace("10^{6}", "10⁶")
                   .Replace("10^{9}", "10⁹")
                   .Replace("10^{-3}", "10⁻³")
                   .Replace("10^{-6}", "10⁻⁶")
                   .Replace("10^{-9}", "10⁻⁹")
                   .Replace("10^{-12}", "10⁻¹²");

        // Convert generic \frac{A}{B}
        // Match simple fractions
        int maxLoops = 10;
        while (text.Contains(@"\frac") && maxLoops-- > 0)
        {
            var match = Regex.Match(text, @"\\frac\{([^{}]+)\}\{([^{}]+)\}");
            if (match.Success)
            {
                string num = match.Groups[1].Value.Trim();
                string den = match.Groups[2].Value.Trim();
                string replacement = den.Contains('+') || den.Contains('-') || den.Contains('×')
                    ? $"{num} / ({den})"
                    : $"{num} / {den}";
                text = text.Substring(0, match.Index) + replacement + text.Substring(match.Index + match.Length);
            }
            else
            {
                break;
            }
        }

        // Clean up subscripts like R_{b1} -> Rb1, V_{CC} -> VCC, I_{BQ} -> IBQ
        text = Regex.Replace(text, @"([A-Za-z])_\{([A-Za-z0-9]+)\}", "$1$2");
        text = Regex.Replace(text, @"([A-Za-z])_([A-Za-z0-9])", "$1$2");

        // Format line breaks: replace LaTeX '\\' with real newlines
        string[] lines = text.Split(new[] { @"\\", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Trim();
        }

        return string.Join(Environment.NewLine, lines);
    }
}
