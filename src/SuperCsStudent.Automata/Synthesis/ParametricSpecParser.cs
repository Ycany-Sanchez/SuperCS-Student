using System.Text;
using System.Text.RegularExpressions;

namespace SuperCsStudent.Automata.Synthesis;

/// <summary>
/// Deterministic parser for user-authored parametric formal language specifications:
///   - Ordered block-exponent set-builder / natural specs:
///       "a^n b^m where n > m", "{ a^n b^m | n > m >= 1 }", "x^i y^j | i >= 2j",
///       "0^n 1^m (n != m)", "a^(2n+1) b^n", "a^n b^m c^k where n + m = k",
///       "a^i b^j c^k where i = j or j = k", "a^n b^m c^k where n > m > k"
///   - Unordered symbol-count specs:
///       "#a > #b", "#x(w) >= #y(w)", "#0 != #1", "{ w in {x,y,z}* | #x(w) > #y(w) }"
/// Returns null if the input is not a parametric specification (allowing regex / regular constraints
/// to handle it); throws FormatException only when the user clearly wrote a parametric spec that
/// contains a syntax error or unsupported structure.
/// </summary>
public static class ParametricSpecParser
{
    private static readonly Regex CountPatternRegex = new(
        @"^(?:(?<sethead>w\s*(?:in|∈)\s*\{(?<setalpha>[^}]+)\}\s*\*?\s*(?:\||where|:))\s*)?(?<expr>.*#[a-z0-9].*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BlockSignalRegex = new(
        @"[a-z0-9]\s*\^\s*(?:\([^)]+\)|[0-9]*[a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingParenPredicateRegex = new(
        @"^(?<pattern>[^()]+)\(\s*(?<pred>[^()]*(?:<=|>=|!=|==|=|<|>)[^()]*)\s*\)$",
        RegexOptions.Compiled);

    public static ParametricSpec? TryParse(string rawSpec, IEnumerable<char>? explicitAlphabet = null)
    {
        if (string.IsNullOrWhiteSpace(rawSpec)) return null;

        var normalized = NormalizeUnicode(rawSpec.Trim());
        var inner = StripSetWrapper(normalized, out var wasSetWrapped);

        // 1. Try unordered symbol-count specification (#a > #b, { w in {x,y}* | #x(w) >= #y(w) })
        if (inner.Contains('#'))
        {
            var countSpec = TryParseSymbolCount(rawSpec, inner, explicitAlphabet);
            if (countSpec is not null) return countSpec;
        }

        // 2. Check if this looks like a block-exponent parametric specification (contains sym^var)
        if (!BlockSignalRegex.IsMatch(inner))
        {
            return null;
        }

        // Split into block pattern and optional predicate clause
        var (patternPart, predicatePart) = SplitPatternAndPredicate(inner, wasSetWrapped);
        if (string.IsNullOrWhiteSpace(patternPart) || !BlockSignalRegex.IsMatch(patternPart))
        {
            return null;
        }

        var blocks = ParseBlockTerms(patternPart);
        if (blocks.Count == 0 || blocks.All(b => b.Exponent.IsConstant))
        {
            return null;
        }

        if (blocks.Count > 3)
        {
            throw new FormatException(
                $"Parametric specifications currently support up to 3 symbol blocks (found {blocks.Count} in '{patternPart.Trim()}').");
        }

        var branches = string.IsNullOrWhiteSpace(predicatePart)
            ? new List<ConstraintBranch> { new(Array.Empty<LinearConstraint>()) }
            : ParsePredicateBranches(predicatePart);

        var explicitList = explicitAlphabet?
            .Where(c => c != AutomataSymbols.DefaultBlank)
            .Select(char.ToLowerInvariant)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        return new BlockExponentSpec(rawSpec, blocks, branches, explicitList is { Count: > 0 } ? explicitList : null);
    }

    private static string NormalizeUnicode(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            switch (ch)
            {
                case '≥': sb.Append(">="); break;
                case '≤': sb.Append("<="); break;
                case '≠': sb.Append("!="); break;
                case '∧': sb.Append(" and "); break;
                case '∨': sb.Append(" or "); break;
                case '∈': sb.Append(" in "); break;
                case '−':
                case '–': sb.Append('-'); break;
                case '·':
                case '×': sb.Append('*'); break;
                case '⁰': AppendSup(sb, '0'); break;
                case '¹': AppendSup(sb, '1'); break;
                case '²': AppendSup(sb, '2'); break;
                case '³': AppendSup(sb, '3'); break;
                case '⁴': AppendSup(sb, '4'); break;
                case '⁵': AppendSup(sb, '5'); break;
                case 'ⁿ': AppendSup(sb, 'n'); break;
                case 'ᵐ': AppendSup(sb, 'm'); break;
                case 'ᵏ': AppendSup(sb, 'k'); break;
                case 'ⁱ': AppendSup(sb, 'i'); break;
                case 'ʲ': AppendSup(sb, 'j'); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    private static void AppendSup(StringBuilder sb, char c)
    {
        // Insert '^' only if previous char is not already part of a superscript run
        if (sb.Length > 0 && sb[^1] != '^' && !IsSupContinuation(sb))
        {
            sb.Append('^');
        }
        sb.Append(c);
    }

    private static bool IsSupContinuation(StringBuilder sb)
    {
        for (var i = sb.Length - 1; i >= 0; i--)
        {
            if (sb[i] == '^') return true;
            if (!char.IsLetterOrDigit(sb[i])) return false;
        }
        return false;
    }

    private static string StripSetWrapper(string s, out bool wasWrapped)
    {
        wasWrapped = false;
        var work = s.Trim();
        var mLead = Regex.Match(work, @"^[A-Za-z][A-Za-z0-9_]*\s*=\s*(.+)$");
        if (mLead.Success && mLead.Groups[1].Value.TrimStart().StartsWith('{'))
        {
            work = mLead.Groups[1].Value.Trim();
        }
        if (work.StartsWith('{') && work.EndsWith('}') && HasMatchingOuterBraces(work))
        {
            wasWrapped = true;
            work = work[1..^1].Trim();
        }
        return work;
    }

    private static bool HasMatchingOuterBraces(string s)
    {
        var depth = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}')
            {
                depth--;
                if (depth == 0 && i < s.Length - 1) return false;
            }
        }
        return depth == 0;
    }

    private static (string Pattern, string Predicate) SplitPatternAndPredicate(string inner, bool wasSetWrapped)
    {
        // 1. Keyword separator: "where", "such that", "s.t."
        var mWhere = Regex.Match(inner, @"\b(?:where|such\s+that|s\.t\.)\b", RegexOptions.IgnoreCase);
        if (mWhere.Success)
        {
            return (inner[..mWhere.Index].Trim(), inner[(mWhere.Index + mWhere.Length)..].Trim());
        }

        // 2. Top-level '|' or ':' separator (outside parentheses/braces)
        var sepIdx = FindTopLevelSeparator(inner, '|');
        if (sepIdx < 0) sepIdx = FindTopLevelSeparator(inner, ':');
        if (sepIdx >= 0)
        {
            var left = inner[..sepIdx].Trim();
            var right = inner[(sepIdx + 1)..].Trim();
            // Ensure '|' isn't regex alternation unless wrapped in { ... } or right has comparison ops
            if (wasSetWrapped || ContainsComparison(right))
            {
                return (left, right);
            }
        }

        // 3. Trailing parenthesized predicate: "a^n b^m (n > m)"
        var mParen = TrailingParenPredicateRegex.Match(inner);
        if (mParen.Success)
        {
            return (mParen.Groups["pattern"].Value.Trim(), mParen.Groups["pred"].Value.Trim());
        }

        return (inner.Trim(), string.Empty);
    }

    private static int FindTopLevelSeparator(string s, char sep)
    {
        var paren = 0;
        var brace = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '(') paren++;
            else if (ch == ')') paren = System.Math.Max(0, paren - 1);
            else if (ch == '{') brace++;
            else if (ch == '}') brace = System.Math.Max(0, brace - 1);
            else if (ch == sep && paren == 0 && brace == 0)
            {
                // Avoid splitting '||'
                if (sep == '|' && ((i + 1 < s.Length && s[i + 1] == '|') || (i > 0 && s[i - 1] == '|')))
                    continue;
                return i;
            }
        }
        return -1;
    }

    private static bool ContainsComparison(string text) =>
        text.Contains('=') || text.Contains('<') || text.Contains('>');

    private static List<BlockTerm> ParseBlockTerms(string pattern)
    {
        var blocks = new List<BlockTerm>();
        var i = 0;
        var s = pattern.Trim();

        while (i < s.Length)
        {
            while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == '*' || s[i] == '.')) i++;
            if (i >= s.Length) break;

            var symChar = char.ToLowerInvariant(s[i]);
            if (!char.IsLetterOrDigit(symChar))
            {
                throw new FormatException($"Unexpected character '{s[i]}' in block pattern '{pattern}'.");
            }
            i++;

            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;

            AffineExpr exp;
            if (i < s.Length && s[i] == '^')
            {
                i++;
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i >= s.Length)
                {
                    throw new FormatException($"Missing exponent after '{symChar}^' in '{pattern}'.");
                }

                if (s[i] == '(')
                {
                    var close = FindMatchingParen(s, i);
                    if (close < 0)
                    {
                        throw new FormatException($"Unclosed '(' in exponent of '{symChar}' in '{pattern}'.");
                    }
                    var expText = s[(i + 1)..close];
                    exp = ParseAffineExpr(expText);
                    i = close + 1;
                }
                else
                {
                    // Read unparenthesized exponent like "n", "2n", "3*m", "2"
                    var start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '*')) i++;
                    if (i < s.Length && char.IsLetter(s[i]))
                    {
                        i++;
                    }
                    if (i == start)
                    {
                        throw new FormatException($"Invalid exponent after '{symChar}^' in '{pattern}'.");
                    }
                    var expText = s[start..i];
                    exp = ParseAffineExpr(expText);
                }
            }
            else
            {
                // Bare literal symbol inside a block pattern, e.g. "a b^n" -> a^1 b^n
                exp = AffineExpr.FromConstant(1);
            }

            // Merge adjacent identical symbols (e.g. a^n a^m -> a^(n+m), a a^n -> a^(n+1))
            if (blocks.Count > 0 && blocks[^1].Symbol == symChar)
            {
                var prev = blocks[^1];
                blocks[^1] = new BlockTerm(symChar, prev.Exponent.Add(exp));
            }
            else
            {
                blocks.Add(new BlockTerm(symChar, exp));
            }
        }

        return blocks;
    }

    private static int FindMatchingParen(string s, int openIdx)
    {
        var depth = 0;
        for (var i = openIdx; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static List<ConstraintBranch> ParsePredicateBranches(string predicate)
    {
        var orParts = SplitTopLevelLogical(predicate, isOr: true);
        var branches = new List<ConstraintBranch>();

        foreach (var orPart in orParts)
        {
            var trimmed = StripOuterParens(orPart.Trim());
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var andParts = SplitTopLevelLogical(trimmed, isOr: false);
            var constraints = new List<LinearConstraint>();

            foreach (var atomRaw in andParts)
            {
                var atom = StripOuterParens(atomRaw.Trim());
                if (string.IsNullOrWhiteSpace(atom)) continue;

                // Ignore harmless domain declarations like "n in N", "n, m in Z+"
                if (TryParseDomainMembership(atom, constraints))
                {
                    continue;
                }

                // Handle shared-bound shorthand: "n, m >= 1" or "n, m, k > 0"
                if (TryParseSharedBounds(atom, constraints))
                {
                    continue;
                }

                // Handle single or chained comparisons: "n > m", "n > m >= 1", "0 <= k < m < n"
                var chain = ParseChainedComparison(atom);
                constraints.AddRange(chain);
            }

            branches.Add(new ConstraintBranch(constraints));
        }

        if (branches.Count == 0)
        {
            throw new FormatException($"Could not parse constraint predicate '{predicate}'.");
        }

        return branches;
    }

    private static bool TryParseDomainMembership(string atom, List<LinearConstraint> constraints)
    {
        var m = Regex.Match(atom, @"^(?<vars>[a-z](?:\s*,\s*[a-z])*)\s+in\s+(?<dom>[A-Za-z0-9_+>=]+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        var vars = m.Groups["vars"].Value.Split(',').Select(v => v.Trim().ToLowerInvariant()).ToList();
        var dom = m.Groups["dom"].Value.Trim();
        var minVal = dom is "Z+" or "N+" or "N1" or "N>0" ? 1 : 0;
        foreach (var v in vars)
        {
            constraints.Add(new LinearConstraint(AffineExpr.FromVariable(v), CompareOp.Geq, AffineExpr.FromConstant(minVal)));
        }
        return true;
    }

    private static bool TryParseSharedBounds(string atom, List<LinearConstraint> constraints)
    {
        var m = Regex.Match(atom, @"^(?<vars>[a-z](?:\s*,\s*[a-z])+)\s*(?<op><=|>=|!=|==|=|<|>)\s*(?<rhs>.+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;

        var op = ParseCompareOp(m.Groups["op"].Value);
        var rhs = ParseAffineExpr(m.Groups["rhs"].Value);
        var vars = m.Groups["vars"].Value.Split(',').Select(v => v.Trim().ToLowerInvariant());
        foreach (var v in vars)
        {
            constraints.Add(new LinearConstraint(AffineExpr.FromVariable(v), op, rhs));
        }
        return true;
    }

    private static List<LinearConstraint> ParseChainedComparison(string atom)
    {
        var parts = new List<string>();
        var ops = new List<CompareOp>();

        var cur = new StringBuilder();
        var paren = 0;
        for (var i = 0; i < atom.Length; i++)
        {
            var ch = atom[i];
            if (ch == '(') { paren++; cur.Append(ch); continue; }
            if (ch == ')') { paren = System.Math.Max(0, paren - 1); cur.Append(ch); continue; }

            if (paren == 0)
            {
                if (i + 1 < atom.Length)
                {
                    var two = atom.Substring(i, 2);
                    if (two is "<=" or ">=" or "!=" or "==")
                    {
                        parts.Add(cur.ToString().Trim());
                        cur.Clear();
                        ops.Add(ParseCompareOp(two));
                        i++;
                        continue;
                    }
                }
                if (ch is '=' or '<' or '>')
                {
                    parts.Add(cur.ToString().Trim());
                    cur.Clear();
                    ops.Add(ParseCompareOp(ch.ToString()));
                    continue;
                }
            }
            cur.Append(ch);
        }
        parts.Add(cur.ToString().Trim());

        if (ops.Count == 0 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new FormatException($"Expected comparison constraint (e.g. 'n > m'), got '{atom}'.");
        }

        var exprs = parts.Select(ParseAffineExpr).ToList();
        var result = new List<LinearConstraint>(ops.Count);
        for (var k = 0; k < ops.Count; k++)
        {
            result.Add(new LinearConstraint(exprs[k], ops[k], exprs[k + 1]));
        }
        return result;
    }

    private static List<string> SplitTopLevelLogical(string text, bool isOr)
    {
        var parts = new List<string>();
        var start = 0;
        var paren = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '(') { paren++; continue; }
            if (ch == ')') { paren = System.Math.Max(0, paren - 1); continue; }
            if (paren != 0) continue;

            if (isOr)
            {
                if (i + 1 < text.Length && text[i] == '|' && text[i + 1] == '|')
                {
                    parts.Add(text[start..i]);
                    start = i + 2;
                    i++;
                    continue;
                }
                if (MatchesKeywordAt(text, i, "or"))
                {
                    parts.Add(text[start..i]);
                    start = i + 2;
                    i++;
                    continue;
                }
            }
            else
            {
                if (ch == ';')
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                    continue;
                }
                if (ch == ',')
                {
                    // Do NOT split shared variable lists like "n, m >= 1" before the comparison op
                    var leftChunk = text[start..i].Trim();
                    if (!Regex.IsMatch(leftChunk, @"^[a-z](?:\s*,\s*[a-z])*$", RegexOptions.IgnoreCase))
                    {
                        parts.Add(text[start..i]);
                        start = i + 1;
                        continue;
                    }
                }
                if (i + 1 < text.Length && text[i] == '&' && text[i + 1] == '&')
                {
                    parts.Add(text[start..i]);
                    start = i + 2;
                    i++;
                    continue;
                }
                if (MatchesKeywordAt(text, i, "and"))
                {
                    parts.Add(text[start..i]);
                    start = i + 3;
                    i += 2;
                    continue;
                }
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static bool MatchesKeywordAt(string text, int index, string keyword)
    {
        if (index + keyword.Length > text.Length) return false;
        if (!text.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            return false;
        var prevOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
        var nextIdx = index + keyword.Length;
        var nextOk = nextIdx >= text.Length || !char.IsLetterOrDigit(text[nextIdx]);
        return prevOk && nextOk;
    }

    private static string StripOuterParens(string s)
    {
        while (s.StartsWith('(') && s.EndsWith(')') && FindMatchingParen(s, 0) == s.Length - 1)
        {
            s = s[1..^1].Trim();
        }
        return s;
    }

    private static CompareOp ParseCompareOp(string op) => op switch
    {
        "=" or "==" => CompareOp.Eq,
        "!=" => CompareOp.Neq,
        ">" => CompareOp.Gt,
        ">=" => CompareOp.Geq,
        "<" => CompareOp.Lt,
        "<=" => CompareOp.Leq,
        _ => throw new FormatException($"Unsupported comparison operator '{op}'.")
    };

    /// <summary>
    /// Parses a linear affine expression such as "n", "2n", "2*n + 1", "n + m - 1", "3(n + 1)".
    /// </summary>
    public static AffineExpr ParseAffineExpr(string text)
    {
        var p = new AffineParser(text);
        var expr = p.ParseAdditive();
        p.SkipWhitespace();
        if (!p.IsAtEnd)
        {
            throw new FormatException($"Unexpected token '{p.Remaining}' in expression '{text}'.");
        }
        return expr;
    }

    private static SymbolCountSpec? TryParseSymbolCount(
        string rawSpec,
        string inner,
        IEnumerable<char>? explicitAlphabet)
    {
        var m = CountPatternRegex.Match(inner);
        if (!m.Success) return null;

        var setAlphaChars = new SortedSet<char>();
        if (m.Groups["setalpha"].Success)
        {
            foreach (var tok in m.Groups["setalpha"].Value.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var ch in tok.Trim())
                {
                    if (char.IsLetterOrDigit(ch)) setAlphaChars.Add(char.ToLowerInvariant(ch));
                }
            }
        }
        if (explicitAlphabet is not null)
        {
            foreach (var ch in explicitAlphabet)
            {
                if (ch != AutomataSymbols.DefaultBlank) setAlphaChars.Add(char.ToLowerInvariant(ch));
            }
        }

        // Let regular Boolean constraints (e.g. "#a mod 3 = 1", "#a divisible by 3", "NOT (#a = 2)") fall through
        var rawExpr = m.Groups["expr"].Value.Trim();
        if (Regex.IsMatch(rawExpr, @"\b(mod|divisible|multiple|AND|OR|NOT|starts?|ends?|contains?|does\s+not|doesn't)\b", RegexOptions.IgnoreCase))
        {
            return null;
        }

        // Normalize "#a(w)" or "count(a)" -> synthetic variable "__c_a" so our affine parser handles arbitrary linear count forms!
        var exprText = Regex.Replace(
            rawExpr,
            @"#([a-z0-9])(?:\s*\(\s*[a-z]\s*\))?",
            match => $"_c{char.ToLowerInvariant(match.Groups[1].Value[0])}_",
            RegexOptions.IgnoreCase);

        if (!ContainsComparison(exprText)) return null;

        var constraints = ParseChainedComparison(exprText);
        if (constraints.Count != 1)
        {
            throw new FormatException($"Symbol-count specifications support a single linear comparison (e.g. '#a > #b' or '#x >= 2*#y').");
        }

        var (terms, op, rhs) = constraints[0].Normalize();
        if (terms.Count != 2)
        {
            return null;
        }

        var entries = terms.ToList();
        if (!TryDecodeCountVar(entries[0].Key, out var sym1) || !TryDecodeCountVar(entries[1].Key, out var sym2))
        {
            return null;
        }

        var c1 = entries[0].Value;
        var c2 = entries[1].Value;

        // Normalize so c1 > 0 and c2 < 0 (i.e. p * #s1 op q * #s2 + d)
        if (c1 < 0 && c2 > 0)
        {
            (sym1, sym2) = (sym2, sym1);
            (c1, c2) = (c2, c1);
        }
        else if (c1 < 0 && c2 < 0)
        {
            c1 = -c1;
            c2 = -c2;
            rhs = -rhs;
            op = LinearConstraint.Flip(op);
        }

        if (c1 <= 0 || c2 >= 0)
        {
            throw new FormatException($"Symbol-count comparison must compare two symbol counts with opposite signs (e.g. '#a > #b' or '#a = 2*#b').");
        }

        setAlphaChars.Add(sym1);
        setAlphaChars.Add(sym2);

        return new SymbolCountSpec(
            RawSpecification: rawSpec,
            Symbol1: sym1,
            Symbol2: sym2,
            Coeff1: c1,
            Coeff2: -c2,
            Op: op,
            Offset: rhs,
            Alphabet: setAlphaChars.ToList());
    }

    private static bool TryDecodeCountVar(string varName, out char sym)
    {
        if (varName.Length == 4 && varName.StartsWith("_c", StringComparison.Ordinal) && varName.EndsWith('_'))
        {
            sym = varName[2];
            return true;
        }
        sym = '\0';
        return false;
    }

    private sealed class AffineParser
    {
        private readonly string _text;
        private int _pos;

        public AffineParser(string text) => _text = text;
        public bool IsAtEnd => _pos >= _text.Length;
        public string Remaining => _pos < _text.Length ? _text[_pos..] : string.Empty;

        public void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        public AffineExpr ParseAdditive()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '+')
                {
                    _pos++;
                    left = left.Add(ParseTerm());
                }
                else if (_pos < _text.Length && _text[_pos] == '-')
                {
                    _pos++;
                    left = left.Subtract(ParseTerm());
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        private AffineExpr ParseTerm()
        {
            SkipWhitespace();
            var sign = 1;
            while (_pos < _text.Length && (_text[_pos] == '+' || _text[_pos] == '-'))
            {
                if (_text[_pos] == '-') sign = -sign;
                _pos++;
                SkipWhitespace();
            }

            var factor = ParseFactor();

            // Implicit or explicit scalar multiplication: "2n", "2*n", "2(n+1)", "n*2"
            while (true)
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '*')
                {
                    _pos++;
                    var rhs = ParseFactor();
                    factor = MultiplyAffine(factor, rhs);
                }
                else if (factor.IsConstant && _pos < _text.Length && (char.IsLetter(_text[_pos]) || _text[_pos] == '_' || _text[_pos] == '('))
                {
                    var rhs = ParseFactor();
                    factor = MultiplyAffine(factor, rhs);
                }
                else
                {
                    break;
                }
            }

            return sign < 0 ? factor.Scale(-1) : factor;
        }

        private AffineExpr ParseFactor()
        {
            SkipWhitespace();
            if (_pos >= _text.Length)
            {
                throw new FormatException($"Unexpected end of expression in '{_text}'.");
            }

            if (_text[_pos] == '(')
            {
                _pos++;
                var inner = ParseAdditive();
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != ')')
                {
                    throw new FormatException($"Missing closing ')' in '{_text}'.");
                }
                _pos++;
                return inner;
            }

            if (char.IsDigit(_text[_pos]))
            {
                var start = _pos;
                while (_pos < _text.Length && char.IsDigit(_text[_pos])) _pos++;
                var val = int.Parse(_text[start.._pos]);
                return AffineExpr.FromConstant(val);
            }

            // Encoded count variable "_cX_"
            if (_text[_pos] == '_' && _pos + 4 <= _text.Length && _text[_pos + 1] == 'c' && _text[_pos + 3] == '_')
            {
                var token = _text.Substring(_pos, 4).ToLowerInvariant();
                _pos += 4;
                return AffineExpr.FromVariable(token);
            }

            if (char.IsLetter(_text[_pos]))
            {
                // Single-letter mathematical variable (n, m, k, i, j, ...)
                var varName = char.ToLowerInvariant(_text[_pos]).ToString();
                _pos++;
                return AffineExpr.FromVariable(varName);
            }

            throw new FormatException($"Unexpected character '{_text[_pos]}' in expression '{_text}'.");
        }

        private static AffineExpr MultiplyAffine(AffineExpr a, AffineExpr b)
        {
            if (a.IsConstant) return b.Scale(a.Constant);
            if (b.IsConstant) return a.Scale(b.Constant);
            throw new FormatException("Non-linear variable multiplication (e.g. n*m or n^2) is not supported in linear constraints.");
        }
    }
}
