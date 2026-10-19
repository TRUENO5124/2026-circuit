// ============================================================================
// File: apps/desktop-winui/Math/MathLatexParser.cs
// Description: Recursive-descent parser for the LaTeX subset produced by the
//              CircuitAssistant teaching engine (Engine/Strategies/*).
//
//              Supported: \frac, \sqrt (incl. \sqrt[n]), _{}, ^{}, single-char
//              scripts, primes, \text / \mathrm / \mathbf / \mathit, Greek,
//              the usual relation/binary/symbol commands, \left / \right,
//              \big| style delimiters, \, \; \: \! \quad \qquad, \\ line
//              breaks, brace groups and the matrix/cases/array/aligned
//              environments (with column alignment and \hline rules).
//
//              The parser never throws: malformed input degrades to whatever
//              was parsed so a single bad formula can never crash the UI.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace CircuitAssistant.MathRendering;

/// <summary>Parses the engine's LaTeX subset into a <see cref="MathNode"/> tree.</summary>
public static class MathLatexParser
{
    /// <summary>Parses a LaTeX math string. Returns an empty <see cref="MathRow"/> for blank input.</summary>
    public static MathNode Parse(string? latex)
    {
        if (string.IsNullOrWhiteSpace(latex))
        {
            return new MathRow();
        }

        try
        {
            var parser = new Parser(latex);
            return parser.ParseTopLevel();
        }
        catch
        {
            // Absolute last resort: strip the LaTeX scaffolding so the user at
            // least sees recognizable plain text instead of raw markup.
            return new MathRow(new MathRun(SanitizeFallback(latex), italic: false));
        }
    }

    /// <summary>Parses and guarantees a row result.</summary>
    public static MathRow ParseRow(string? latex)
    {
        MathNode node = Parse(latex);
        return node as MathRow ?? new MathRow(node);
    }

    private static string SanitizeFallback(string latex)
    {
        var sb = new StringBuilder(latex.Length);
        foreach (char c in latex)
        {
            if (c is '_' or '^' or '\\' or '{' or '}' or '$' or '&')
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }

    // ------------------------------------------------------------------
    // Command tables
    // ------------------------------------------------------------------
    private static readonly Dictionary<string, string> LowercaseGreek = new(StringComparer.Ordinal)
    {
        ["alpha"] = "\u03B1",
        ["beta"] = "\u03B2",
        ["gamma"] = "\u03B3",
        ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5",
        ["varepsilon"] = "\u03F5",
        ["zeta"] = "\u03B6",
        ["eta"] = "\u03B7",
        ["theta"] = "\u03B8",
        ["vartheta"] = "\u03D1",
        ["iota"] = "\u03B9",
        ["kappa"] = "\u03BA",
        ["lambda"] = "\u03BB",
        ["mu"] = "\u03BC",
        ["nu"] = "\u03BD",
        ["xi"] = "\u03BE",
        ["omicron"] = "\u03BF",
        ["pi"] = "\u03C0",
        ["varpi"] = "\u03D6",
        ["rho"] = "\u03C1",
        ["varrho"] = "\u03F1",
        ["sigma"] = "\u03C3",
        ["varsigma"] = "\u03C2",
        ["tau"] = "\u03C4",
        ["upsilon"] = "\u03C5",
        ["phi"] = "\u03D5",
        ["varphi"] = "\u03C6",
        ["chi"] = "\u03C7",
        ["psi"] = "\u03C8",
        ["omega"] = "\u03C9",
    };

    private static readonly Dictionary<string, string> UppercaseGreek = new(StringComparer.Ordinal)
    {
        ["Gamma"] = "\u0393",
        ["Delta"] = "\u0394",
        ["Theta"] = "\u0398",
        ["Lambda"] = "\u039B",
        ["Xi"] = "\u039E",
        ["Pi"] = "\u03A0",
        ["Sigma"] = "\u03A3",
        ["Upsilon"] = "\u03A5",
        ["Phi"] = "\u03A6",
        ["Psi"] = "\u03A8",
        ["Omega"] = "\u03A9",
    };

    /// <summary>Relational operators: medium (0.28 em) space on both sides.</summary>
    private static readonly Dictionary<string, string> Relations = new(StringComparer.Ordinal)
    {
        ["approx"] = "\u2248",
        ["le"] = "\u2264",
        ["leq"] = "\u2264",
        ["ge"] = "\u2265",
        ["geq"] = "\u2265",
        ["ne"] = "\u2260",
        ["neq"] = "\u2260",
        ["equiv"] = "\u2261",
        ["sim"] = "\u223C",
        ["simeq"] = "\u2243",
        ["cong"] = "\u2245",
        ["propto"] = "\u221D",
        ["parallel"] = "\u2225",
        ["perp"] = "\u22A5",
        ["rightarrow"] = "\u2192",
        ["to"] = "\u2192",
        ["leftarrow"] = "\u2190",
        ["Rightarrow"] = "\u21D2",
        ["implies"] = "\u21D2",
        ["Leftarrow"] = "\u21D0",
        ["Leftrightarrow"] = "\u21D4",
        ["leftrightarrow"] = "\u2194",
        ["mapsto"] = "\u21A6",
        ["ll"] = "\u226A",
        ["gg"] = "\u226B",
        ["in"] = "\u2208",
        ["notin"] = "\u2209",
        ["subset"] = "\u2282",
        ["subseteq"] = "\u2286",
    };

    /// <summary>Binary operators: thin (0.22 em) space on both sides.</summary>
    private static readonly Dictionary<string, string> BinaryOperators = new(StringComparer.Ordinal)
    {
        ["pm"] = "\u00B1",
        ["mp"] = "\u2213",
        ["times"] = "\u00D7",
        ["cdot"] = "\u00B7",
        ["div"] = "\u00F7",
        ["ast"] = "\u2217",
        ["star"] = "\u22C6",
        ["cap"] = "\u2229",
        ["cup"] = "\u222A",
        ["oplus"] = "\u2295",
    };

    /// <summary>Symbols rendered upright without automatic spacing.</summary>
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["infty"] = "\u221E",
        ["infinity"] = "\u221E",
        ["partial"] = "\u2202",
        ["int"] = "\u222B",
        ["iint"] = "\u222C",
        ["oint"] = "\u222E",
        ["sum"] = "\u2211",
        ["prod"] = "\u220F",
        ["angle"] = "\u2220",
        ["circ"] = "\u00B0",
        ["degree"] = "\u00B0",
        ["cdots"] = "\u22EF",
        ["ldots"] = "\u2026",
        ["dots"] = "\u2026",
        ["vdots"] = "\u22EE",
        ["ddots"] = "\u22F1",
        ["prime"] = "\u2032",
        ["hbar"] = "\u210F",
        ["ell"] = "\u2113",
        ["Re"] = "\u211C",
        ["Im"] = "\u2111",
        ["emptyset"] = "\u2205",
        ["nabla"] = "\u2207",
        ["therefore"] = "\u2234",
        ["because"] = "\u2235",
        ["textasciicircum"] = "^",
        ["textasciitilde"] = "~",
        ["textunderscore"] = "_",
        ["textbackslash"] = "\\",
        ["textbar"] = "|",
        ["textless"] = "<",
        ["textgreater"] = ">",
        ["checkmark"] = "\u2713",
        ["square"] = "\u25A1",
    };

    /// <summary>Function names: upright with a thin space after.</summary>
    private static readonly HashSet<string> FunctionNames = new(StringComparer.Ordinal)
    {
        "max", "min", "ln", "log", "lg", "exp", "sin", "cos", "tan", "cot", "sec", "csc",
        "arcsin", "arccos", "arctan", "sinh", "cosh", "tanh", "coth", "lim", "det", "dim",
        "mod", "gcd", "arg", "deg", "sgn",
    };

    /// <summary>Commands whose single argument is displayed upright as text, spaces preserved.</summary>
    private static readonly HashSet<string> TextCommands = new(StringComparer.Ordinal)
    {
        "text", "textrm", "textsf", "texttt", "textnormal", "mbox", "mathrm", "operatorname",
    };

    /// <summary>Commands whose argument is parsed as math and then restyled bold.</summary>
    private static readonly HashSet<string> BoldCommands = new(StringComparer.Ordinal) { "mathbf", "textbf", "bm", "boldsymbol" };

    private static readonly HashSet<string> ItalicCommands = new(StringComparer.Ordinal) { "mathit", "textit" };

    private static readonly HashSet<string> AccentCommands = new(StringComparer.Ordinal) { "hat", "widehat", "dot", "ddot" };

    private static readonly HashSet<string> PlainWrapperCommands = new(StringComparer.Ordinal)
    {
        "vec", "overrightarrow", "overleftarrow", "bar", "overline", "underline", "tilde", "widetilde",
        "mathcal", "mathbb", "mathfrak", "mathsf", "boxed", "phantom", "hphantom", "vphantom",
    };

    private static readonly HashSet<string> OpenDelimiters = new(StringComparer.Ordinal) { "(", "[", "{", "\u27E8", "\u230A", "\u2308" };

    private static readonly HashSet<string> CloseDelimiters = new(StringComparer.Ordinal) { ")", "]", "}", "\u27E9", "\u230B", "\u2309" };

    /// <summary>Characters that signal a binary/unary operator context.</summary>
    private static readonly HashSet<char> OperatorLikeChars = new()
    {
        '+', '\u2212', '-', '=', '<', '>', '\u2264', '\u2265', '\u2248', '\u2260', '\u2261', '\u223C', '\u2243',
        '\u221D', '\u00D7', '\u00B7', '\u00F7', '\u00B1', '\u2213', '\u2225', '\u22A5', '\u2192', '\u2190',
        '\u21D2', '\u21D0', '\u21D4', '\u2194', '\u21A6', '\u226A', '\u226B', '\u2208',
    };

    private const float RelationSpaceEm = 0.28f;
    private const float BinarySpaceEm = 0.22f;
    private const float FunctionSpaceEm = 0.10f;
    private const float CommaSpaceEm = 0.16f;

    // ==================================================================
    // Parser implementation
    // ==================================================================
    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string source)
        {
            _s = NormalizeSource(source);
            _i = 0;
        }

        private static string NormalizeSource(string source)
        {
            string text = source.Trim();
            // Strip a surrounding $...$ / \[...\] wrapper if present.
            if (text.StartsWith("$$", StringComparison.Ordinal) && text.EndsWith("$$", StringComparison.Ordinal) && text.Length > 4)
            {
                text = text.Substring(2, text.Length - 4);
            }
            else if (text.StartsWith("$", StringComparison.Ordinal) && text.EndsWith("$", StringComparison.Ordinal) && text.Length > 2)
            {
                text = text.Substring(1, text.Length - 2);
            }

            if (text.StartsWith("\\[", StringComparison.Ordinal) && text.EndsWith("\\]", StringComparison.Ordinal) && text.Length > 4)
            {
                text = text.Substring(2, text.Length - 4);
            }

            return text;
        }

        public MathRow ParseTopLevel()
        {
            MathRow row = ParseRow(insideGroup: false);
            row.TrimTrailingSpaces();
            return row;
        }

        // --------------------------------------------------------------
        // Row / group parsing
        // --------------------------------------------------------------
        private MathRow ParseRow(bool insideGroup)
        {
            var row = new MathRow();
            while (_i < _s.Length)
            {
                char c = _s[_i];

                if (c is ' ' or '\t' or '\r' or '\n')
                {
                    _i++;
                    continue;
                }

                if (c == '}')
                {
                    if (insideGroup)
                    {
                        break;
                    }

                    // Stray closing brace at top level: skip it rather than dropping the rest.
                    _i++;
                    continue;
                }

                if (c == '&' || c == '$')
                {
                    _i++;
                    continue;
                }

                if (c == '\\')
                {
                    if (_i + 1 < _s.Length && _s[_i + 1] == '\\')
                    {
                        _i += 2;
                        SkipOptionalBracketArgument();
                        row.TrimTrailingSpaces();
                        row.Add(new MathLineBreak());
                        continue;
                    }

                    ParseCommand(row);
                    continue;
                }

                if (c is '^' or '_')
                {
                    ParseScript(row);
                    continue;
                }

                if (c == '\'')
                {
                    ParsePrimes(row);
                    continue;
                }

                if (c == '{')
                {
                    _i++;
                    MathRow inner = ParseRow(insideGroup: true);
                    if (_i < _s.Length && _s[_i] == '}')
                    {
                        _i++;
                    }

                    inner.TrimTrailingSpaces();
                    row.AddRange(inner.Children);
                    continue;
                }

                if (c == '~')
                {
                    _i++;
                    row.Add(new MathSpace(0.25f));
                    continue;
                }

                ParseSimpleAtom(row);
            }

            row.TrimTrailingSpaces();
            return row;
        }

        /// <summary>Parses a single brace/bracket argument into a row.</summary>
        private MathRow ParseRequiredGroup()
        {
            SkipSpaces();
            if (_i >= _s.Length)
            {
                return new MathRow();
            }

            if (_s[_i] == '{')
            {
                _i++;
                MathRow inner = ParseRow(insideGroup: true);
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                }

                inner.TrimTrailingSpaces();
                return inner;
            }

            // LaTeX allows a single token without braces: \frac12, x^2 ...
            var single = new MathRow();
            ParseSimpleAtom(single);
            single.TrimTrailingSpaces();
            return single;
        }

        private void SkipSpaces()
        {
            while (_i < _s.Length && (_s[_i] is ' ' or '\t' or '\r' or '\n'))
            {
                _i++;
            }
        }

        private void SkipOptionalBracketArgument()
        {
            int save = _i;
            SkipSpaces();
            if (_i < _s.Length && _s[_i] == '[')
            {
                int close = _s.IndexOf(']', _i + 1);
                if (close > 0)
                {
                    _i = close + 1;
                    return;
                }
            }

            _i = save;
        }

        private string ReadRawGroup()
        {
            SkipSpaces();
            if (_i >= _s.Length)
            {
                return string.Empty;
            }

            if (_s[_i] != '{')
            {
                char single = _s[_i];
                _i++;
                return single.ToString();
            }

            _i++; // consume '{'
            int depth = 0;
            var sb = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '\\' && _i + 1 < _s.Length)
                {
                    char next = _s[_i + 1];
                    if (next is '{' or '}' or '%' or '$' or '_' or '&' or '#' or '\\')
                    {
                        sb.Append(next);
                        _i += 2;
                        continue;
                    }

                    if (next is ' ' or ',' or ';' or ':')
                    {
                        if (next == ' ')
                        {
                            sb.Append(' ');
                        }

                        _i += 2;
                        continue;
                    }

                    sb.Append(c);
                    _i++;
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    sb.Append(c);
                    _i++;
                    continue;
                }

                if (c == '}')
                {
                    if (depth == 0)
                    {
                        _i++;
                        break;
                    }

                    depth--;
                    sb.Append(c);
                    _i++;
                    continue;
                }

                sb.Append(c);
                _i++;
            }

            return sb.ToString();
        }

        // --------------------------------------------------------------
        // Scripts / primes
        // --------------------------------------------------------------
        private void ParseScript(MathRow row)
        {
            char kind = _s[_i];
            _i++;
            row.TrimTrailingSpaces();
            MathRow argument = ParseScriptArgument();

            MathNode? baseNode = row.Count > 0 ? row.RemoveLast() : null;
            if (baseNode is MathScript existing)
            {
                if (kind == '_')
                {
                    existing.Subscript = Combine(existing.Subscript, argument);
                }
                else
                {
                    existing.Superscript = Combine(existing.Superscript, argument);
                }

                row.Add(existing);
                return;
            }

            var script = new MathScript(
                baseNode ?? new MathRow(),
                kind == '_' ? argument : null,
                kind == '^' ? argument : null);
            row.Add(script);
        }

        private static MathRow Combine(MathNode? existing, MathRow added)
        {
            if (existing is null)
            {
                return added;
            }

            var merged = new MathRow();
            if (existing is MathRow existingRow)
            {
                merged.AddRange(existingRow.Children);
            }
            else
            {
                merged.Add(existing);
            }

            merged.AddRange(added.Children);
            return merged;
        }

        private MathRow ParseScriptArgument()
        {
            SkipSpaces();
            if (_i >= _s.Length)
            {
                return new MathRow();
            }

            char c = _s[_i];
            if (c == '{')
            {
                return ParseRequiredGroup();
            }

            var row = new MathRow();
            if (c == '\\')
            {
                ParseCommand(row);
            }
            else if (c is '^' or '_')
            {
                _i++;
            }
            else
            {
                ParseSimpleAtom(row);
            }

            row.TrimTrailingSpaces();
            return row;
        }

        private void ParsePrimes(MathRow row)
        {
            int count = 0;
            while (_i < _s.Length && _s[_i] == '\'')
            {
                count++;
                _i++;
            }

            string glyph = count switch
            {
                1 => "\u2032",
                2 => "\u2033",
                3 => "\u2034",
                _ => "\u2032",
            };

            row.TrimTrailingSpaces();
            MathNode? baseNode = row.Count > 0 ? row.RemoveLast() : null;
            var prime = new MathRow(new MathRun(glyph, italic: false));
            if (baseNode is MathScript existing)
            {
                existing.Superscript = Combine(existing.Superscript, prime);
                row.Add(existing);
                return;
            }

            row.Add(new MathScript(baseNode ?? new MathRow(), null, prime));
        }

        // --------------------------------------------------------------
        // Simple atoms (runs, digits, punctuation, delimiters)
        // --------------------------------------------------------------
        private void ParseSimpleAtom(MathRow row)
        {
            char c = _s[_i];

            if (char.IsAsciiDigit(c) || (c == '.' && _i + 1 < _s.Length && char.IsAsciiDigit(_s[_i + 1])))
            {
                int start = _i;
                while (_i < _s.Length && (char.IsAsciiDigit(_s[_i]) || _s[_i] == '.'))
                {
                    _i++;
                }

                row.Add(new MathRun(_s.Substring(start, _i - start), italic: false));
                return;
            }

            if (IsAsciiLetter(c))
            {
                int start = _i;
                while (_i < _s.Length && IsAsciiLetter(_s[_i]))
                {
                    _i++;
                }

                row.Add(new MathRun(_s.Substring(start, _i - start), italic: true));
                return;
            }

            switch (c)
            {
                case '+':
                case '-':
                case '\u2212':
                case '=':
                case '<':
                case '>':
                    _i++;
                    AppendOperator(row, c == '-' ? "\u2212" : c.ToString(), c is '+' or '-' or '\u2212' ? BinarySpaceEm : RelationSpaceEm);
                    return;

                case '(':
                case '[':
                case '{':
                    _i++;
                    row.Add(new MathRun(c.ToString(), italic: false));
                    return;

                case ')':
                case ']':
                case '}':
                    _i++;
                    AppendClosingDelimiter(row, c.ToString());
                    return;

                case '|':
                    _i++;
                    row.Add(new MathRun("|", italic: false));
                    return;

                case '/':
                    _i++;
                    row.Add(new MathRun("/", italic: false));
                    return;

                case ',':
                    _i++;
                    AppendPunctuation(row, ",");
                    return;

                case ';':
                    _i++;
                    AppendPunctuation(row, ";");
                    return;

                case ':':
                    _i++;
                    AppendPunctuation(row, ":");
                    return;
            }

            // Any other character (literal Greek, CJK, unknown punctuation): upright run.
            _i++;
            row.Add(new MathRun(c.ToString(), italic: false));
        }

        private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

        private void AppendPunctuation(MathRow row, string glyph)
        {
            row.TrimTrailingSpaces();
            row.Add(new MathRun(glyph, italic: false, canBreakAfter: true));
            row.Add(new MathSpace(CommaSpaceEm));
        }

        private void AppendClosingDelimiter(MathRow row, string glyph)
        {
            row.TrimTrailingSpaces();
            row.Add(new MathRun(glyph, italic: false));
        }

        private void AppendOperator(MathRow row, string glyph, float spaceEm)
        {
            bool unary = IsUnaryContext(row);
            if (!unary)
            {
                row.Add(new MathSpace(spaceEm));
            }

            row.Add(new MathRun(glyph, italic: false, canBreakAfter: true));
            row.Add(new MathSpace(spaceEm));
        }

        private static bool IsUnaryContext(MathRow row)
        {
            int index = row.Count - 1;
            while (index >= 0 && row[index] is MathSpace)
            {
                index--;
            }

            if (index < 0)
            {
                return true;
            }

            MathNode last = row[index];
            if (last is MathLineBreak)
            {
                return true;
            }

            if (last is MathRun run && run.Text.Length > 0)
            {
                if (run.Text.Length == 1)
                {
                    char ch = run.Text[0];
                    if (OperatorLikeChars.Contains(ch) || ch is '(' or '[' or '{' or '/' or ',' or ';')
                    {
                        return true;
                    }
                }

                if (run.Text is "(" or "[" or "{" or "|")
                {
                    return true;
                }

                if (run.Text[^1] is ',' or ';')
                {
                    return true;
                }
            }

            return false;
        }

        // --------------------------------------------------------------
        // Commands
        // --------------------------------------------------------------
        private void ParseCommand(MathRow row)
        {
            string name = ReadCommandName();
            if (name.Length == 0)
            {
                return;
            }

            // Greek letters
            if (LowercaseGreek.TryGetValue(name, out string? lower))
            {
                row.Add(new MathRun(lower, italic: true));
                return;
            }

            if (UppercaseGreek.TryGetValue(name, out string? upper))
            {
                row.Add(new MathRun(upper, italic: false));
                return;
            }

            // Relations / binary operators / symbols
            if (Relations.TryGetValue(name, out string? relation))
            {
                AppendOperator(row, relation, RelationSpaceEm);
                return;
            }

            if (BinaryOperators.TryGetValue(name, out string? binary))
            {
                AppendOperator(row, binary, BinarySpaceEm);
                return;
            }

            if (Symbols.TryGetValue(name, out string? symbol))
            {
                row.Add(new MathRun(symbol, italic: false));
                return;
            }

            if (FunctionNames.Contains(name))
            {
                row.Add(new MathRun(name, italic: false));
                row.Add(new MathSpace(FunctionSpaceEm));
                return;
            }

            switch (name)
            {
                case "frac":
                case "dfrac":
                case "tfrac":
                case "cfrac":
                {
                    MathRow numerator = ParseRequiredGroup();
                    MathRow denominator = ParseRequiredGroup();
                    row.Add(new MathFraction(numerator, denominator));
                    return;
                }

                case "sqrt":
                {
                    MathRow? index = null;
                    SkipSpaces();
                    if (_i < _s.Length && _s[_i] == '[')
                    {
                        _i++;
                        index = ParseRow(insideGroup: false);
                        if (_i < _s.Length && _s[_i] == ']')
                        {
                            _i++;
                        }
                    }

                    MathRow radicand = ParseRequiredGroup();
                    row.Add(new MathSqrt(radicand, index));
                    return;
                }

                case "left":
                case "right":
                case "big":
                case "Big":
                case "bigg":
                case "Bigg":
                case "bigl":
                case "Bigl":
                case "biggl":
                case "Biggl":
                case "bigr":
                case "Bigr":
                case "biggr":
                case "Biggr":
                case "bigm":
                case "Bigm":
                    ParseDelimiter(row, name.StartsWith("right", StringComparison.Ordinal) || name.StartsWith("bigr", StringComparison.Ordinal) || name.StartsWith("Bigr", StringComparison.Ordinal) || name.StartsWith("biggr", StringComparison.Ordinal) || name.StartsWith("Biggr", StringComparison.Ordinal));
                    return;

                case "begin":
                    ParseEnvironment(row);
                    return;

                case "end":
                {
                    // Stray \end{...}: consume the environment name.
                    SkipSpaces();
                    if (_i < _s.Length && _s[_i] == '{')
                    {
                        _ = ReadRawGroup();
                    }

                    return;
                }

                case "hline":
                case "hdashline":
                case "toprule":
                case "midrule":
                case "bottomrule":
                case "displaystyle":
                case "textstyle":
                case "scriptstyle":
                case "scriptscriptstyle":
                case "limits":
                case "nolimits":
                case "middle":
                case "nonumber":
                case "notag":
                    return;

                case "quad":
                    row.Add(new MathSpace(1.0f, canBreakAfter: true));
                    return;

                case "qquad":
                    row.Add(new MathSpace(2.0f, canBreakAfter: true));
                    return;

                case "enspace":
                    row.Add(new MathSpace(0.5f));
                    return;

                case "thinspace":
                    row.Add(new MathSpace(0.1667f));
                    return;

                case "medspace":
                    row.Add(new MathSpace(0.2222f));
                    return;

                case "thickspace":
                    row.Add(new MathSpace(0.2778f));
                    return;

                case "cr":
                case "newline":
                case "linebreak":
                    row.TrimTrailingSpaces();
                    row.Add(new MathLineBreak());
                    return;

                case " ":
                case "space":
                    row.Add(new MathSpace(0.25f));
                    return;

                case ",":
                    row.Add(new MathSpace(0.1667f));
                    return;

                case ";":
                    row.Add(new MathSpace(0.2778f));
                    return;

                case ":":
                    row.Add(new MathSpace(0.2222f));
                    return;

                case "!":
                    return;

                case "~":
                    row.Add(new MathSpace(0.25f));
                    return;

                case "\\":
                    row.TrimTrailingSpaces();
                    row.Add(new MathLineBreak());
                    return;

                case "%":
                case "$":
                case "#":
                case "&":
                case "_":
                    row.Add(new MathRun(name, italic: false));
                    return;

                case "{":
                    row.Add(new MathRun("{", italic: false));
                    return;

                case "}":
                    row.Add(new MathRun("}", italic: false));
                    return;

                case "|":
                    row.Add(new MathRun("\u2225", italic: false));
                    return;

                case "prime":
                    ParsePrimes(row);
                    return;
            }

            if (TextCommands.Contains(name))
            {
                string raw = ReadRawGroup();
                string text = ProcessText(raw);
                if (text.Length > 0)
                {
                    row.Add(new MathRun(text, italic: false));
                }

                return;
            }

            if (BoldCommands.Contains(name))
            {
                MathRow inner = ParseRequiredGroup();
                ApplyStyle(inner, bold: true, upright: name is "mathbf" or "textbf");
                row.AddRange(inner.Children);
                return;
            }

            if (ItalicCommands.Contains(name))
            {
                MathRow inner = ParseRequiredGroup();
                ApplyStyle(inner, bold: null, upright: false);
                row.AddRange(inner.Children);
                return;
            }

            if (AccentCommands.Contains(name))
            {
                MathRow inner = ParseRequiredGroup();
                MathAccentKind accentKind = name switch
                {
                    "dot" => MathAccentKind.Dot,
                    "ddot" => MathAccentKind.DoubleDot,
                    _ => MathAccentKind.Hat,
                };
                row.Add(new MathAccent(accentKind, inner.Count == 1 ? inner[0] : inner));
                return;
            }

            if (PlainWrapperCommands.Contains(name))
            {
                MathRow inner = ParseRequiredGroup();
                row.AddRange(inner.Children);
                return;
            }

            // Unknown command: drop the backslash; inline a following group if present.
            SkipSpaces();
            if (_i < _s.Length && _s[_i] == '{')
            {
                MathRow inner = ParseRequiredGroup();
                row.AddRange(inner.Children);
            }
        }

        private string ReadCommandName()
        {
            // _i points at '\'
            _i++;
            if (_i >= _s.Length)
            {
                return string.Empty;
            }

            if (char.IsAsciiLetter(_s[_i]))
            {
                int start = _i;
                while (_i < _s.Length && char.IsAsciiLetter(_s[_i]))
                {
                    _i++;
                }

                string name = _s.Substring(start, _i - start);
                // \operatorname* -- consume the star so it is not rendered.
                if (_i < _s.Length && _s[_i] == '*')
                {
                    _i++;
                }

                return name;
            }

            char symbol = _s[_i];
            _i++;
            return symbol.ToString();
        }

        private static void ApplyStyle(MathNode node, bool? bold, bool? upright)
        {
            switch (node)
            {
                case MathRow row:
                    for (int i = 0; i < row.Count; i++)
                    {
                        MathNode child = row[i];
                        if (child is MathRun run)
                        {
                            bool italic = upright switch
                            {
                                true => false,
                                false => true,
                                null => run.Italic,
                            };
                            row.Replace(i, bold.HasValue ? run.WithStyle(italic, bold.Value) : run.WithStyle(italic));
                        }
                        else
                        {
                            ApplyStyle(child, bold, upright);
                        }
                    }

                    return;

                case MathFraction fraction:
                    ApplyStyle(fraction.Numerator, bold, upright);
                    ApplyStyle(fraction.Denominator, bold, upright);
                    return;

                case MathSqrt sqrt:
                    ApplyStyle(sqrt.Radicand, bold, upright);
                    if (sqrt.Index is not null)
                    {
                        ApplyStyle(sqrt.Index, bold, upright);
                    }

                    return;

                case MathScript script:
                    ApplyStyle(script.Base, bold, upright);
                    if (script.Subscript is not null)
                    {
                        ApplyStyle(script.Subscript, bold, upright);
                    }

                    if (script.Superscript is not null)
                    {
                        ApplyStyle(script.Superscript, bold, upright);
                    }

                    return;

                case MathAccent accent:
                    ApplyStyle(accent.Base, bold, upright);
                    return;

                case MathMatrix matrix:
                    foreach (IReadOnlyList<MathRow> matrixRow in matrix.Rows)
                    {
                        foreach (MathRow cell in matrixRow)
                        {
                            ApplyStyle(cell, bold, upright);
                        }
                    }

                    return;
            }
        }

        private static string ProcessText(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\\' && i + 1 < raw.Length)
                {
                    if (char.IsAsciiLetter(raw[i + 1]))
                    {
                        int start = i + 1;
                        int j = start;
                        while (j < raw.Length && char.IsAsciiLetter(raw[j]))
                        {
                            j++;
                        }

                        string command = raw.Substring(start, j - start);
                        if (LowercaseGreek.TryGetValue(command, out string? lower))
                        {
                            sb.Append(lower);
                        }
                        else if (UppercaseGreek.TryGetValue(command, out string? upper))
                        {
                            sb.Append(upper);
                        }
                        else if (Symbols.TryGetValue(command, out string? symbol))
                        {
                            sb.Append(symbol);
                        }
                        else
                        {
                            sb.Append(command);
                        }

                        i = j - 1;
                        continue;
                    }

                    char escaped = raw[i + 1];
                    sb.Append(escaped);
                    i++;
                    continue;
                }

                if (c is '{' or '}')
                {
                    continue;
                }

                sb.Append(c == '~' ? ' ' : c);
            }

            return sb.ToString();
        }

        // --------------------------------------------------------------
        // Delimiters
        // --------------------------------------------------------------
        private void ParseDelimiter(MathRow row, bool isClosingHint)
        {
            SkipSpaces();
            if (_i >= _s.Length)
            {
                return;
            }

            char c = _s[_i];
            if (c == '.')
            {
                _i++;
                return; // invisible delimiter
            }

            string glyph;
            if (c == '\\')
            {
                int save = _i;
                string name = ReadCommandName();
                glyph = name switch
                {
                    "{" or "lbrace" => "{",
                    "}" or "rbrace" => "}",
                    "|" or "Vert" => "\u2225",
                    "langle" => "\u27E8",
                    "rangle" => "\u27E9",
                    "lvert" => "|",
                    "rvert" => "|",
                    "lfloor" => "\u230A",
                    "rfloor" => "\u230B",
                    "lceil" => "\u2308",
                    "rceil" => "\u2309",
                    _ => string.Empty,
                };

                if (glyph.Length == 0)
                {
                    // Not a delimiter command: back off and treat it as a bar.
                    _i = save;
                    glyph = "|";
                }
            }
            else
            {
                _i++;
                glyph = c switch
                {
                    '<' => "\u27E8",
                    '>' => "\u27E9",
                    _ => c.ToString(),
                };
            }

            bool closing = isClosingHint || CloseDelimiters.Contains(glyph) || (!OpenDelimiters.Contains(glyph) && glyph == "|");
            if (closing)
            {
                AppendClosingDelimiter(row, glyph);
            }
            else
            {
                row.Add(new MathRun(glyph, italic: false));
            }
        }

        // --------------------------------------------------------------
        // Environments (cases / matrices / aligned)
        // --------------------------------------------------------------
        private void ParseEnvironment(MathRow row)
        {
            string env = ReadEnvironmentName();
            if (env.Length == 0)
            {
                return;
            }

            if (env is "array" or "tabular" or "tabularx")
            {
                // Column specification {cc} -- consume it.
                SkipSpaces();
                if (_i < _s.Length && _s[_i] == '{')
                {
                    _ = ReadRawGroup();
                }
            }

            string content = ReadUntilEndEnvironment(env);
            MathMatrix matrix = BuildMatrix(env, content);
            row.Add(matrix);
        }

        private string ReadEnvironmentName()
        {
            SkipSpaces();
            if (_i >= _s.Length || _s[_i] != '{')
            {
                return string.Empty;
            }

            _i++;
            int start = _i;
            while (_i < _s.Length && _s[_i] != '}')
            {
                _i++;
            }

            string name = _s.Substring(start, Math.Max(0, _i - start)).Trim();
            if (_i < _s.Length)
            {
                _i++;
            }

            return name;
        }

        private string ReadUntilEndEnvironment(string env)
        {
            string beginToken = "\\begin{" + env + "}";
            string endToken = "\\end{" + env + "}";
            int depth = 1;
            int scan = _i;
            while (scan < _s.Length && depth > 0)
            {
                int nextBegin = _s.IndexOf(beginToken, scan, StringComparison.Ordinal);
                int nextEnd = _s.IndexOf(endToken, scan, StringComparison.Ordinal);
                if (nextEnd < 0)
                {
                    string rest = _s.Substring(_i);
                    _i = _s.Length;
                    return rest;
                }

                if (nextBegin >= 0 && nextBegin < nextEnd)
                {
                    depth++;
                    scan = nextBegin + beginToken.Length;
                    continue;
                }

                depth--;
                if (depth == 0)
                {
                    string content = _s.Substring(_i, nextEnd - _i);
                    _i = nextEnd + endToken.Length;
                    return content;
                }

                scan = nextEnd + endToken.Length;
            }

            return string.Empty;
        }

        private static MathMatrix BuildMatrix(string env, string content)
        {
            var rows = new List<IReadOnlyList<MathRow>>();
            var rulesAboveRows = new List<int>();
            var currentRow = new List<MathRow>();
            var cell = new StringBuilder();
            int braceDepth = 0;
            bool ruleAboveCurrentRow = false;

            void FlushRow()
            {
                currentRow.Add(ParseCell(cell));
                cell.Clear();
                if (ruleAboveCurrentRow)
                {
                    rulesAboveRows.Add(rows.Count);
                    ruleAboveCurrentRow = false;
                }

                rows.Add(currentRow);
                currentRow = new List<MathRow>();
            }

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (c == '\\' && i + 1 < content.Length)
                {
                    char next = content[i + 1];
                    if (next == '\\' && braceDepth == 0)
                    {
                        FlushRow();
                        i++;
                        // Optional row spacing argument: \\[4pt]
                        if (i + 1 < content.Length && content[i + 1] == '[')
                        {
                            int close = content.IndexOf(']', i + 2);
                            if (close > 0)
                            {
                                i = close;
                            }
                        }

                        continue;
                    }

                    if (braceDepth == 0 && char.IsAsciiLetter(next))
                    {
                        int j = i + 1;
                        while (j < content.Length && char.IsAsciiLetter(content[j]))
                        {
                            j++;
                        }

                        string command = content.Substring(i + 1, j - i - 1);
                        if (command is "hline" or "hdashline")
                        {
                            bool cellEmpty = cell.ToString().Trim().Length == 0 && currentRow.Count == 0;
                            if (cellEmpty)
                            {
                                ruleAboveCurrentRow = true;
                            }
                            else
                            {
                                FlushRow();
                                rulesAboveRows.Add(rows.Count);
                            }

                            i = j - 1;
                            continue;
                        }

                        if (command == "cline")
                        {
                            int close = content.IndexOf('}', j);
                            i = close > 0 ? close : j - 1;
                            continue;
                        }

                        cell.Append(content, i, j - i);
                        i = j - 1;
                        continue;
                    }

                    cell.Append(c);
                    cell.Append(next);
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    braceDepth++;
                }
                else if (c == '}')
                {
                    braceDepth = Math.Max(0, braceDepth - 1);
                }
                else if (c == '&' && braceDepth == 0)
                {
                    currentRow.Add(ParseCell(cell));
                    cell.Clear();
                    continue;
                }

                cell.Append(c);
            }

            if (cell.ToString().Trim().Length > 0 || currentRow.Count > 0)
            {
                FlushRow();
            }

            if (ruleAboveCurrentRow)
            {
                rulesAboveRows.Add(rows.Count); // bottom border
            }

            MathMatrixKind kind = env switch
            {
                "bmatrix" or "Bmatrix" or "array" or "tabular" => MathMatrixKind.Bracket,
                "pmatrix" => MathMatrixKind.Parenthesis,
                "cases" or "dcases" => MathMatrixKind.Cases,
                "vmatrix" or "Vmatrix" => MathMatrixKind.Bar,
                _ => MathMatrixKind.Plain,
            };

            // matrix / aligned / gathered / split render without brackets.
            if (env is "matrix" or "aligned" or "gathered" or "split" or "align" or "alignedat")
            {
                kind = MathMatrixKind.Plain;
            }

            return new MathMatrix(kind, rows, rulesAboveRows);
        }

        private static MathRow ParseCell(StringBuilder cell)
        {
            return MathLatexParser.ParseRow(cell.ToString());
        }
    }
}
