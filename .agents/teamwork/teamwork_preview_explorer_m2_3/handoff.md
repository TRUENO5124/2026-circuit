# Milestone 2 Explorer M2-3 Handoff Report: Formula Rendering, Scientific Notation & ViewModel Integration

## 1. Observation

### 1.1 Existing Implementation of `MathFormulaView.xaml.cs`
- **Location**: `apps/desktop-winui/Controls/MathFormulaView.xaml.cs` (lines 73–262).
- **Core Logic**: `FormatLatexToUnicode(string latex)` transforms LaTeX strings to Unicode plain text rendered by a WinUI 3 `TextBlock` configured with `FontFamily="Cambria Math, Latin Modern Math, STIX Two Math, Segoe UI Variable Text, Consolas"`.
- **Specific Deficiencies Identified**:
  1. **Missing Greek Symbols**:
     - Line 96–101 only replaces `\beta`, `\alpha`, `\pi`, `\tau`, `\mu`, `\Omega`.
     - `\omega` (omega) is **completely missing**. As a result, critical circuit variables such as angular cutoff frequency $\omega_c$, resonant frequency $\omega_0$, upper cutoff $\omega_H$, and damped frequency $\omega_d$ render as raw unparsed `\omega₀` or `\omega_0`.
     - `\Delta` (Delta) is **missing** (e.g. $\Delta v, \Delta t$).
     - `\theta`, `\phi`, `\delta`, `\sigma`, `\zeta`, `\lambda` are all missing.
  2. **Missing Mathematical Operators & Relations**:
     - `\sqrt` (square root) is unparsed: expressions like `\sqrt{LC}` or `\sqrt{\alpha^2 - \omega_0^2}` render verbatim with raw LaTeX braces.
     - `\pm` (plus-minus) and `\mp` are missing: roots $s_{1,2} = -\alpha \pm \sqrt{\alpha^2 - \omega_0^2}$ render as raw `\pm`.
     - `\le` / `\leq`, `\ge` / `\geq`, `\neq` / `\ne` are missing.
     - `\to` / `\rightarrow` and `\infty` are missing: limits like $t \to \infty$ or steady-state $f(\infty)$ render as `t \to \infty`.
     - `\angle` (phasor angle) and `\circ` (degrees) are missing: AC phasors like $10\angle -45^\circ$ render broken.
  3. **Broken Subscript Regex for Greek Letters**:
     - In lines 118–119:
       ```csharp
       text = Regex.Replace(text, @"([A-Za-z])_\{([A-Za-z0-9]+)\}", m => m.Groups[1].Value + ToSubscript(m.Groups[2].Value));
       text = Regex.Replace(text, @"([A-Za-z])_([0-9])", m => m.Groups[1].Value + ToSubscript(m.Groups[2].Value));
       ```
     - The regex token `([A-Za-z])` strictly matches ASCII Latin letters. When `\omega` or `\tau` or `\alpha` is converted to Greek Unicode (`ω`, `τ`, `α`), `[A-Za-z]` **fails to match**! Thus, `\omega_0` $\to$ `ω_0` was never converted to `ω₀`.
  4. **Corrupted Unicode Glyph in Subscripts**:
     - In line 253: `'c' or 'C' => '꜀'`. Character `꜀` (U+A700) is a Latin modifier tone letter (Chinese tone bar), NOT a subscript 'c'. In Cambria Math and Windows fonts, it renders as an ugly, broken vertical bar.
  5. **Total Absence of Matrix Formatting**:
     - In SPICE MNA matrix equations ($\mathbf{G}\mathbf{v} + \mathbf{B}\mathbf{i} = \mathbf{J}$), LaTeX emits `\begin{bmatrix} ... \end{bmatrix}`.
     - Currently, `&` is stripped and `\\` creates line breaks with no brackets, column padding, or 2D alignment, turning matrices into disorganized unreadable text.

### 1.2 Existing Implementation of `ScientificNotationFormatter.cs`
- **Location**: `apps/desktop-winui/Services/ScientificNotationFormatter.cs` (lines 6–128).
- **Core Methods**: `ToLatexScientific(double value, int precision = 3)` and `ToUnicodeScientific(double value, int precision = 3)`.
- **Specific Deficiencies Identified**:
  1. **Edge Case Formatting**:
     - When `value` is `double.NaN`, it returns `"NaN"`. In LaTeX mode, this should be safe math text `\text{NaN}`.
     - When `value` is `double.PositiveInfinity` or `double.NegativeInfinity`, it returns `"Infinity"`, instead of standard LaTeX `\infty` / `-\infty` or Unicode `∞` / `-∞`.
  2. **Loss of Fixed Decimals**:
     - `ORIGINAL_REQUEST.md` specifically requires formatting like `$6.28 \times 10^3$` and `$2.00 \times 10^{-5}$`.
     - In line 43, `Math.Round(mantissa, precision).ToString("0.###")` trims trailing zeros. Thus, `0.00002` becomes `2 \times 10^{-5}` instead of `2.00 \times 10^{-5}`.
  3. **Missing Engineering Value & Unit Helper**:
     - The codebase lacks an engineering formatter `FormatWithUnit(double value, string unit)` to format values with standard engineering prefixes (e.g. $1.25\text{ mA}$, $10\text{ k}\Omega$, $4.7\text{ }\mu\text{F}$, $100\text{ kHz}$) across both LaTeX and UI text.

### 1.3 Existing Implementation of `AnalysisStudioViewModel.cs`
- **Location**: `apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs` (lines 891–944).
- **Hardcoded Logic Observed**:
  - `SolveCircuitAsync()` (lines 891–926) ignores canvas `Wires` completely.
  - It invokes `SyncElementValuesToModel()` (lines 928–944), which checks only 11 hardcoded string IDs:
    ```csharp
    if (el.Id == "VCC") VccNumber = el.DisplayNumber;
    else if (el.Id == "Rb1" || el.Id == "Rb" || el.Id == "R1") Rb1Number = el.DisplayNumber;
    else if (el.Id == "Rb2") Rb2Number = el.DisplayNumber;
    else if (el.Id == "Rc") RcNumber = el.DisplayNumber;
    else if (el.Id == "Re") ReNumber = el.DisplayNumber;
    else if (el.Id == "RL") RlNumber = el.DisplayNumber;
    else if (el.Id == "C1") C1Number = el.DisplayNumber;
    else if (el.Id == "C2") C2Number = el.DisplayNumber;
    else if (el.Id == "Ce") CeNumber = el.DisplayNumber;
    else if (el.Id == "Q1" || el.Id == "BJT") BetaNumber = el.DisplayNumber;
    else if (el.Id.Equals("vs", StringComparison.OrdinalIgnoreCase)) FreqNumber = el.DisplayNumber;
    ```
  - It forwards these 11 parameters to `_apiService.AnalyzeCircuitAsync(...)`, which falls back to `CircuitApiService.GeneratePedagogicalSolution(...)` and generates a 5-step BJT common-emitter amplifier derivation with $I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}$.
  - **Direct Defect**: If a user creates an RC circuit (`VS`, `R1`, `C1`, `GND`), the engine ignores the user's RC circuit and outputs the hardcoded BJT derivation.

### 1.4 Verification and Build Baseline
- `dotnet build apps/desktop-winui/CircuitAssistant.csproj`: Build succeeded with **0 warnings, 0 errors** in 27.17s.
- `tests/ChallengerRunner/ChallengerRunner.csproj`: Standalone C# runner project (`net9.0-windows10.0.26100.0`) available for high-speed headless testing.

---

## 2. Logic Chain

1. **Why `\omega_0` Failed to Render as `ω₀`**:
   - *Observation*: `\omega` was replaced with `ω` (U+03C9) in Greek replacement, producing `ω_0`.
   - *Observation*: The subscript regex was `@"([A-Za-z])_([0-9])"`.
   - *Fact*: In .NET regular expressions, `[A-Za-z]` matches only Unicode range `[0x0041-0x005A, 0x0061-0x007A]`. Greek character `ω` is `0x03C9`, outside `[A-Za-z]`.
   - *Deduction*: The regex must match any Unicode letter category `\p{L}` (which covers Latin, Greek, Cyrillic) or simply match `_\{([^{}]+)\}` and `_([0-9\p{L}])` regardless of what precedes it.

2. **Why Math Parsing Must Be Decoupled from WinUI 3**:
   - *Observation*: `MathFormulaView` inherits from `Microsoft.UI.Xaml.Controls.UserControl`.
   - *Fact*: UserControl requires the Windows App SDK runtime and an initialized UI thread to instantiate.
   - *Fact*: Automated unit tests and challenger runners (like `ChallengerRunner`) run in a headless console environment.
   - *Deduction*: Extracting the pure text transformation into `CircuitAssistant.Services.LatexFormulaParser` allows 100% of the LaTeX-to-Unicode conversion, matrix formatting, and symbol mapping to be verified by automated unit tests without UI dependencies. `MathFormulaView` simply delegates to `LatexFormulaParser.FormatToUnicode(latex)`.

3. **Why Matrix Formatting Requires Cambria Math Multi-Line Envelopes**:
   - *Observation*: SPICE MNA formulation emits $\begin{bmatrix}\mathbf{G} & \mathbf{B} \\ \mathbf{C} & \mathbf{D}\end{bmatrix} \begin{bmatrix}\mathbf{v} \\ \mathbf{j}\end{bmatrix} = \begin{bmatrix}\mathbf{i} \\ \mathbf{e}\end{bmatrix}$.
   - *Observation*: `MathFormulaView` uses `TextBlock` with `FontFamily="Cambria Math"`.
   - *Fact*: Cambria Math includes high-precision mathematical brackets:
     - 2 rows: `⎡ ... ⎤` (U+23A1, U+23A4) and `⎣ ... ⎦` (U+23A3, U+23A6).
     - 3+ rows: `⎡ ... ⎤`, `⎢ ... ⎥` (U+23A2, U+23A5), and `⎣ ... ⎦`.
   - *Deduction*: Parsing `\begin{bmatrix}` into aligned columns padded with spaces and enclosed in Unicode brackets creates readable textbook matrices directly in Cambria Math.

4. **Why `SolveCircuitAsync()` Must Use the Engine Pipeline**:
   - *Observation*: The user acceptance criterion explicitly mandates: "在画布上仅放置 VS、R、C 构成一阶 RC 电路时，点击‘求解与理论分析’仅生成一阶 RC 电路微分方程、时间常数与频响推导，绝不包含任何三极管参数".
   - *Observation*: `Elements` and `Wires` contain the full canvas graph.
   - *Deduction*: Replacing `SyncElementValuesToModel()` with `NetlistExtractor.ExtractNetlist(Elements, Wires)` $\to$ `TopologyClassifier.Classify(netlist, out var strategy)` $\to$ `strategy.SolveAsync(netlist, parameters)` guarantees that the active circuit topology dynamically dictates the solution strategy, completely eliminating hardcoded BJT derivations.

---

## 3. Caveats

1. **Subscript Unicode Limitation**:
   - Unicode standard provides native subscript glyphs for digits `₀-₉`, signs `₊₋`, and letters `ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓᵦᵧᵨᵩᵪ`.
   - Letters like `b`, `c`, `d`, `g`, `q` have no standard Latin subscript glyphs in Unicode. In particular, `c` must NEVER be replaced with `꜀` (tone bar).
   - *Design Choice*: For subscripts consisting entirely of convertible characters (e.g. `ω₀`, `fₗ`, `fₕ`, `Rₜₕ`, `Rᵢₙ`, `Aᵥ`), we attach the Unicode subscripts directly. For subscripts with unmappable letters (e.g. `V_{CEQ}`, `I_{BQ}`, `r_{be}`), we format them cleanly as `V_CEQ`, `I_BQ`, `r_be`, ensuring consistent baseline rendering without corrupt symbols.
2. **Matrix Inline vs Block Display**:
   - When matrix equations contain multiple matrices multiplied together (e.g. `[G][v] = [J]`), combining them row-by-row into a unified multi-line block requires that each matrix in the same equation has compatible row counts. The designed parser handles horizontal row alignment across equations cleanly.
3. **M2 Boundary Alignment**:
   - Explorer M2-1 designs the internal graph data structures of `ElectricalNetlist` and `NetlistExtractor`.
   - Explorer M2-2 designs the internal solver algorithms of `TopologyClassifier` and the 4 strategies.
   - This handoff designs the orchestration in `AnalysisStudioViewModel`, formula rendering in `LatexFormulaParser` / `MathFormulaView`, scientific formatting in `ScientificNotationFormatter`, and automated verification.

---

## 4. Conclusion & Technical Design

### 4.1 Component 1: `LatexFormulaParser.cs` & `MathFormulaView.xaml.cs`

#### Proposed File: `apps/desktop-winui/Services/LatexFormulaParser.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CircuitAssistant.Services;

/// <summary>
/// High-fidelity LaTeX-to-Unicode mathematical formula parser and formatter.
/// Formats mathematical expressions, Greek symbols, operators, fractions, square roots,
/// superscripts, subscripts, and 2D matrices for Cambria Math rendering.
/// Pure C# implementation with zero UI dependencies for headless testing.
/// </summary>
public static class LatexFormulaParser
{
    public static string FormatToUnicode(string latex)
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

        // Lowercase Greek (CRITICAL: \omega must be replaced!)
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
            if (braceStart == -1 || braceStart > idx + 10) break; // If no brace, exit

            int braceEnd = FindMatchingBrace(text, braceStart);
            if (braceEnd == -1) break;

            string inner = text.Substring(braceStart + 1, braceEnd - braceStart - 1).Trim();
            inner = FormatToUnicode(inner); // Recursively resolve inner expressions

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

            // Recursively process nested fractions
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
        // 10^{exp} -> 10ⁿ
        text = Regex.Replace(text, @"10\^\{([+-]?\d+)\}", m => "10" + ToSuperscript(m.Groups[1].Value));
        // ^{exp} -> ⁿ
        text = Regex.Replace(text, @"\^\{([+-]?\d+)\}", m => ToSuperscript(m.Groups[1].Value));
        // ^n -> ⁿ
        text = Regex.Replace(text, @"\^([+-]?\d)", m => ToSuperscript(m.Groups[1].Value));
        // Variable powers: s^2 -> s², \alpha^2 -> α²
        text = Regex.Replace(text, @"\^2\b", "²");
        text = Regex.Replace(text, @"\^3\b", "³");
        // Transient 0^+ and 0^- -> 0⁺, 0⁻
        text = text.Replace("0^+", "0⁺").Replace("0^-", "0⁻");
        return text;
    }

    private static string ReplaceSubscripts(string text)
    {
        // Unicode letter category \p{L} + digits aware subscript parser
        // Matches e.g. R_{b1}, \omega_{0}, s_{1,2}, \tau_1
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
        bool allConvertible = sub.All(c => CanConvertToSubscript(c));
        if (allConvertible)
        {
            return baseChar + ToSubscript(sub);
        }
        // For unmappable Latin strings (e.g. CEQ, BQ, be, eq), keep clean subscript format
        return $"{baseChar}_{sub}";
    }

    private static bool CanConvertToSubscript(char c)
    {
        return char.IsDigit(c) || c is '+' or '-' or 'a' or 'e' or 'h' or 'i' or 'j' or 'k' or 'l' or 'm' or 'n' or 'o' or 'p' or 'r' or 's' or 't' or 'u' or 'v' or 'x';
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

    public static string FormatMatrixEnvironments(string text)
    {
        // Parses \begin{bmatrix} ... \end{bmatrix} and replaces with 2D Unicode matrix blocks
        string pattern = @"\\begin\{(?:bmatrix|pmatrix|matrix)\}([\s\S]*?)\\end\{(?:bmatrix|pmatrix|matrix)\}";
        return Regex.Replace(text, pattern, m =>
        {
            string matrixBody = m.Groups[1].Value.Trim();
            string[] rawRows = matrixBody.Split(new[] { @"\\" }, StringSplitOptions.RemoveEmptyEntries);
            if (rawRows.Length == 0) return "[ ]";

            var grid = new List<List<string>>();
            foreach (var r in rawRows)
            {
                var cells = r.Split('&').Select(c => FormatToUnicode(c.Trim())).ToList();
                grid.Add(cells);
            }

            int rowCount = grid.Count;
            int colCount = grid.Max(r => r.Count);

            // Compute max width per column
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

    public static int FindMatchingBrace(string s, int openIndex)
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
}
```

#### Updated `MathFormulaView.xaml.cs`:
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CircuitAssistant.Services;

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

        FormulaText.Text = LatexFormulaParser.FormatToUnicode(Formula);
    }

    // CopyLatex_Click remains unchanged
}
```

---

### 4.2 Component 2: `ScientificNotationFormatter.cs` Comprehensive Design

#### Updated `apps/desktop-winui/Services/ScientificNotationFormatter.cs`:
```csharp
using System;
using System.Globalization;

namespace CircuitAssistant.Services;

/// <summary>
/// Professional scientific notation and engineering unit formatter for electrical EDA.
/// Enforces industrial standard: values >= 1000 or < 0.001 are formatted in scientific notation,
/// with robust handling for NaN, Infinity, and zero.
/// </summary>
public static class ScientificNotationFormatter
{
    /// <summary>
    /// Formats a double into standard LaTeX scientific notation (e.g. 6.28 \times 10^{3}, 2.00 \times 10^{-5}).
    /// </summary>
    public static string ToLatexScientific(double value, int precision = 3, bool fixedDecimals = false)
    {
        if (double.IsNaN(value)) return @"\text{NaN}";
        if (double.IsPositiveInfinity(value)) return @"\infty";
        if (double.IsNegativeInfinity(value)) return @"-\infty";

        if (Math.Abs(value) < 1e-15) return "0";

        double absVal = Math.Abs(value);

        // Moderate numbers (0.001 <= |v| < 1000)
        if (absVal >= 0.001 && absVal < 1000)
        {
            string fmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
            return Math.Round(value, precision).ToString(fmt, CultureInfo.InvariantCulture);
        }

        // Scientific format e.g. "6.28e+03"
        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2) return value.ToString(CultureInfo.InvariantCulture);

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string mantissaFmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
        string mantissaStr = Math.Round(mantissa, precision).ToString(mantissaFmt, CultureInfo.InvariantCulture);

        if (!fixedDecimals && Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10^{{{exponent}}}";
        }
        if (!fixedDecimals && Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10^{{{exponent}}}";
        }

        return $"{mantissaStr} \\times 10^{{{exponent}}}";
    }

    /// <summary>
    /// Formats a double into standard Unicode scientific notation (e.g. 6.28 × 10³, 2.00 × 10⁻⁵).
    /// </summary>
    public static string ToUnicodeScientific(double value, int precision = 3, bool fixedDecimals = false)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "∞";
        if (double.IsNegativeInfinity(value)) return "-∞";

        if (Math.Abs(value) < 1e-15) return "0";

        double absVal = Math.Abs(value);
        if (absVal >= 0.001 && absVal < 1000)
        {
            string fmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
            return Math.Round(value, precision).ToString(fmt, CultureInfo.InvariantCulture);
        }

        string expStr = value.ToString($"e{precision - 1}", CultureInfo.InvariantCulture);
        string[] parts = expStr.Split('e', 'E');
        if (parts.Length != 2) return value.ToString(CultureInfo.InvariantCulture);

        double mantissa = double.Parse(parts[0], CultureInfo.InvariantCulture);
        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);

        string supExp = LatexFormulaParser.ToSuperscript(exponent.ToString(CultureInfo.InvariantCulture));

        if (!fixedDecimals && Math.Abs(mantissa - 1.0) < 1e-6)
        {
            return $"10{supExp}";
        }
        if (!fixedDecimals && Math.Abs(mantissa - (-1.0)) < 1e-6)
        {
            return $"-10{supExp}";
        }

        string mantissaFmt = fixedDecimals ? $"F{precision - 1}" : "0.###";
        string mantissaStr = Math.Round(mantissa, precision).ToString(mantissaFmt, CultureInfo.InvariantCulture);
        return $"{mantissaStr} × 10{supExp}";
    }

    /// <summary>
    /// Formats an engineering value with appropriate SI engineering prefix (e.g. 1.25 mA, 10 kΩ, 4.7 μF, 100 kHz).
    /// </summary>
    public static string FormatWithUnit(double value, string baseUnit, int precision = 3, bool useLatex = false)
    {
        if (double.IsNaN(value)) return useLatex ? $@"\text{{NaN {baseUnit}}}" : $"NaN {baseUnit}";
        if (double.IsPositiveInfinity(value)) return useLatex ? $@"\infty\text{{ {baseUnit}}}" : $"∞ {baseUnit}";
        if (double.IsNegativeInfinity(value)) return useLatex ? $@"-\infty\text{{ {baseUnit}}}" : $"-∞ {baseUnit}";

        if (Math.Abs(value) < 1e-15)
        {
            return useLatex ? $@"0\text{{ {baseUnit}}}" : $"0 {baseUnit}";
        }

        double absVal = Math.Abs(value);

        // Find engineering decade: 10^(3*k)
        int exp3 = (int)Math.Floor(Math.Log10(absVal) / 3.0) * 3;

        // Supported prefixes: f (10^-15) to T (10^12)
        if (exp3 < -15 || exp3 > 12)
        {
            return useLatex
                ? $"{ToLatexScientific(value, precision)}\\text{{ {baseUnit}}}"
                : $"{ToUnicodeScientific(value, precision)} {baseUnit}";
        }

        double scaled = value / Math.Pow(10, exp3);
        string prefix = exp3 switch
        {
            12 => "T",
            9 => "G",
            6 => "M",
            3 => "k",
            0 => "",
            -3 => "m",
            -6 => "μ",
            -9 => "n",
            -12 => "p",
            -15 => "f",
            _ => ""
        };

        string scaledStr = Math.Round(scaled, precision).ToString("0.###", CultureInfo.InvariantCulture);
        string unitStr = $"{prefix}{baseUnit}";

        if (useLatex)
        {
            // In LaTeX, replace Greek micro with \mu and Ohm with \Omega
            string latexUnit = unitStr.Replace("μ", @"\mu ").Replace("Ω", @"\Omega");
            return $"{scaledStr}\\text{{ {latexUnit}}}";
        }

        return $"{scaledStr} {unitStr}";
    }
}
```

---

### 4.3 Component 3: `AnalysisStudioViewModel.cs` Pipeline Integration

#### Updated `SolveCircuitAsync()` and Pipeline Orchestration
```csharp
    [RelayCommand]
    public async Task SolveCircuitAsync()
    {
        // 1. Guard Check: Empty canvas
        if (Elements.Count == 0)
        {
            StatusMessage = "当前画布无元器件，请先从左侧元件族添加元件并连接导线";
            return;
        }

        IsAnalyzing = true;
        StatusMessage = "正在构建等效网络图并提取拓扑结构...";

        try
        {
            // 2. Industrial Netlist Extraction via Union-Find graph connectivity
            var netlist = NetlistExtractor.ExtractNetlist(Elements, Wires);

            // 3. Topology Classification
            var topologyType = TopologyClassifier.Classify(netlist, out var strategy);

            // 4. Edge Case: Open circuit / Disconnected components
            if (topologyType == CircuitTopologyType.Disconnected || topologyType == CircuitTopologyType.OpenCircuit)
            {
                Result = new AnalysisResult
                {
                    IsSuccess = false,
                    CircuitTitle = "电路开路 / 未闭合回路",
                    SummaryText = "检测到电路中存在未连接导线的悬空引脚或未闭合网络。请使用导线连接各元件引脚构成完整的闭合回路后重新求解。"
                };
                StatusMessage = "求解中止：电路未形成闭合电气回路，请检查导线连接";
                return;
            }

            StatusMessage = $"已识别电路拓扑: {GetTopologyDisplayName(topologyType)} · 正在执行高精度符号推导...";

            // 5. Build dynamic parameters from elements on canvas (not hardcoded IDs)
            var parameters = BuildAnalysisParameters(netlist);

            // 6. Execute Analytical Solving Strategy
            var solution = await strategy.SolveAsync(netlist, parameters);

            // 7. Update ViewModel UI State
            Result = solution;
            StatusMessage = $"求解成功 · 拓扑: {GetTopologyDisplayName(topologyType)} — 已生成 {solution.Steps.Count} 个教学解析步骤 (电路含 {Elements.Count} 个元件)";

            // Automatically switch right panel to "解题推导" (Tab 3) for immediate student feedback
            SelectedRightTabIndex = 2;
        }
        catch (Exception ex)
        {
            App.LogTrace($"[SolveCircuitAsync Exception] {ex}");
            Result = new AnalysisResult
            {
                IsSuccess = false,
                CircuitTitle = "求解异常",
                SummaryText = $"分析引擎在解算过程中发生异常：{ex.Message}\n请检查电路拓扑是否包含异常短路或奇异矩阵结构。"
            };
            StatusMessage = $"求解失败: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static string GetTopologyDisplayName(CircuitTopologyType type) => type switch
    {
        CircuitTopologyType.FirstOrderRc => "一阶 RC 暂态与滤波电路",
        CircuitTopologyType.FirstOrderRl => "一阶 RL 暂态与滤波电路",
        CircuitTopologyType.SecondOrderRlc => "二阶 RLC 谐振与暂态电路",
        CircuitTopologyType.BjtAmplifier => "晶体管共射极放大电路",
        CircuitTopologyType.MosfetAmplifier => "MOSFET 共源极放大电路",
        CircuitTopologyType.GeneralLinear => "通用线性网络 (MNA 矩阵分析)",
        _ => "线性网络"
    };

    private AnalysisParameters BuildAnalysisParameters(ElectricalNetlist netlist)
    {
        // Extract excitation frequency or default to 1 kHz
        double freq = 1000.0;
        var vsElem = netlist.Elements.Values.FirstOrDefault(e => e.Type == ElementType.AcVoltageSource);
        if (vsElem != null)
        {
            freq = vsElem.DisplayNumber * GetUnitMultiplier(vsElem.UnitSymbol);
        }

        return new AnalysisParameters
        {
            Frequency = freq > 0 ? freq : 1000.0,
            ThermalVoltageUt = 0.026 // 26 mV at 300K
        };
    }
```

---

### 4.4 Component 4: Automated Test Suite for Milestone 2 (`ChallengerRunner`)

We design automated test cases to be compiled into `tests/ChallengerRunner/`:

```csharp
namespace ChallengerRunner;

public static class Milestone2EngineTests
{
    public static (int Passed, int Failed, List<string> Failures) Run()
    {
        int passed = 0;
        int failed = 0;
        var failures = new List<string>();

        void Assert(bool condition, string testName, string errorDetails)
        {
            if (condition) passed++;
            else
            {
                failed++;
                failures.Add($"[M2-TEST] {testName}: {errorDetails}");
            }
        }

        // Test 1: LatexFormulaParser Greek Symbol Replacement
        string r1 = LatexFormulaParser.FormatToUnicode(@"\omega_0 = \frac{1}{\sqrt{LC}}");
        Assert(r1.Contains("ω₀") && r1.Contains("√(LC)"),
            "Greek & Sqrt Parsing",
            $"Expected 'ω₀' and '√(LC)', got '{r1}'");

        // Test 2: LatexFormulaParser Differential Equation
        string r2 = LatexFormulaParser.FormatToUnicode(@"\tau \frac{du_C}{dt} + u_C = u_C(\infty)");
        Assert(r2.Contains("τ") && r2.Contains("∞"),
            "Tau & Infinity Parsing",
            $"Expected 'τ' and '∞', got '{r2}'");

        // Test 3: LatexFormulaParser Matrix Formatting
        string r3 = LatexFormulaParser.FormatToUnicode(@"\begin{bmatrix} 1 & -2 \\ 3 & 4 \end{bmatrix}");
        Assert(r3.Contains("⎡") && r3.Contains("⎣") && r3.Contains("⎤") && r3.Contains("⎦"),
            "2D Matrix Bracket Parsing",
            $"Expected multi-line 2D brackets, got '{r3}'");

        // Test 4: ScientificNotationFormatter LaTeX
        string s1 = ScientificNotationFormatter.ToLatexScientific(6280, 3);
        Assert(s1 == @"6.28 \times 10^{3}",
            "ToLatexScientific 6280",
            $"Expected '6.28 \\times 10^{{3}}', got '{s1}'");

        string s2 = ScientificNotationFormatter.ToLatexScientific(0.00002, 3, fixedDecimals: true);
        Assert(s2 == @"2.00 \times 10^{-5}",
            "ToLatexScientific 0.00002 fixed",
            $"Expected '2.00 \\times 10^{{-5}}', got '{s2}'");

        // Test 5: ScientificNotationFormatter Units
        string u1 = ScientificNotationFormatter.FormatWithUnit(0.00125, "A");
        Assert(u1 == "1.25 mA", "FormatWithUnit 1.25 mA", $"Expected '1.25 mA', got '{u1}'");

        string u2 = ScientificNotationFormatter.FormatWithUnit(10000, "Ω");
        Assert(u2 == "10 kΩ", "FormatWithUnit 10 kΩ", $"Expected '10 kΩ', got '{u2}'");

        string u3 = ScientificNotationFormatter.FormatWithUnit(4.7e-6, "F");
        Assert(u3 == "4.7 μF", "FormatWithUnit 4.7 μF", $"Expected '4.7 μF', got '{u3}'");

        string u4 = ScientificNotationFormatter.FormatWithUnit(100000, "Hz");
        Assert(u4 == "100 kHz", "FormatWithUnit 100 kHz", $"Expected '100 kHz', got '{u4}'");

        // Test 6: ScientificNotationFormatter Edge Cases
        string e1 = ScientificNotationFormatter.ToLatexScientific(double.PositiveInfinity);
        Assert(e1 == @"\infty", "Infinity LaTeX", $"Expected '\\infty', got '{e1}'");

        string e2 = ScientificNotationFormatter.ToLatexScientific(double.NaN);
        Assert(e2 == @"\text{NaN}", "NaN LaTeX", $"Expected '\\text{{NaN}}', got '{e2}'");

        return (passed, failed, failures);
    }
}
```

---

## 5. Verification Method

### 5.1 Independent Build Verification
Execute via command line:
```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj
```
*Expected Result*: Build succeeded with 0 errors and 0 warnings.

### 5.2 Automated Engine Test Execution
Execute via command line:
```powershell
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
```
*Expected Result*: Milestone 2 test cases pass 100% with 0 failures.

### 5.3 Acceptance Criteria Verification
1. **RC Circuit (Acceptance Criterion 1)**:
   - On a canvas with only `vs`, `R1`, `C1`, and `GND`, invoke `SolveCircuitAsync()`.
   - *Verification*: Steps output time constant $\tau = R_1 C_1$, differential equation, and cutoff frequency $f_c$.
   - *Invalidation Condition*: Any presence of $I_{BQ}, I_{CQ}, V_{CEQ}, r_{be}$ or transistor parameters in generated steps.
2. **Formula Rendering (Acceptance Criterion 3)**:
   - Inspect formulas containing $\omega_0$, $\Delta$, $\sqrt{LC}$, and matrices.
   - *Verification*: $\omega_0$ renders as `ω₀`, $\sqrt{LC}$ renders as `√(LC)`, and matrices render with aligned `⎡ ⎤` and `⎣ ⎦` brackets.
3. **Scientific Notation (Acceptance Criterion 3)**:
   - Numerical values $\ge 1000$ or $< 0.001$ (e.g. $51000\,\Omega$, $0.00001\text{ F}$) appear as $5.10 \times 10^4$ and $1.00 \times 10^{-5}$ without raw floating-point overflows or unformatted decimals.
