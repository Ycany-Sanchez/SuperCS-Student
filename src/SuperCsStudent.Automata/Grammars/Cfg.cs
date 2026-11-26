using System.Text;

namespace SuperCsStudent.Automata.Grammars;

public sealed class CfgRule : IEquatable<CfgRule>
{
    public string Left { get; }
    public IReadOnlyList<string> Right { get; }
    public bool IsEpsilon => Right.Count == 0;

    public CfgRule(string left, IReadOnlyList<string> right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            throw new ArgumentException("Rule left-hand side cannot be empty.", nameof(left));
        }
        Left = left.Trim();
        Right = right.ToArray();
    }

    public string FormatRight()
    {
        if (Right.Count == 0)
        {
            return AutomataSymbols.Empty;
        }
        var needsSpaces = Right.Any(s => s.Length > 1);
        return string.Join(needsSpaces ? " " : string.Empty, Right);
    }

    public override string ToString() => $"{Left} -> {FormatRight()}";

    public bool Equals(CfgRule? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return StringComparer.Ordinal.Equals(Left, other.Left)
            && Right.SequenceEqual(other.Right, StringComparer.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as CfgRule);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(Left, StringComparer.Ordinal);
        foreach (var sym in Right)
        {
            hc.Add(sym, StringComparer.Ordinal);
        }
        return hc.ToHashCode();
    }
}

/// <summary>
/// Context-Free Grammar quadruple (Variables, Terminals, Rules, StartSymbol)
/// where Variables = NonTerminals ∪ Terminals and NonTerminals = Variables - Terminals.
/// </summary>
public sealed class Cfg
{
    public IReadOnlySet<string> Variables { get; }
    public IReadOnlySet<string> Terminals { get; }
    public IReadOnlySet<string> NonTerminals { get; }
    public IReadOnlyList<CfgRule> Rules { get; }
    public string StartSymbol { get; }

    public Cfg(
        IEnumerable<string> variables,
        IEnumerable<string> terminals,
        IEnumerable<CfgRule> rules,
        string startSymbol)
    {
        var termSet = new HashSet<string>(terminals.Select(t => t.Trim()), StringComparer.Ordinal);
        var varSet = new HashSet<string>(variables.Select(v => v.Trim()), StringComparer.Ordinal);
        foreach (var t in termSet)
        {
            varSet.Add(t);
        }

        var nonTerms = new HashSet<string>(varSet, StringComparer.Ordinal);
        nonTerms.ExceptWith(termSet);

        if (!nonTerms.Contains(startSymbol))
        {
            throw new ArgumentException($"Start symbol '{startSymbol}' must be in NonTerminals (Variables - Terminals).", nameof(startSymbol));
        }

        var ruleList = new List<CfgRule>();
        var seen = new HashSet<CfgRule>();
        foreach (var r in rules)
        {
            if (!nonTerms.Contains(r.Left))
            {
                throw new ArgumentException($"Rule LHS '{r.Left}' must be a non-terminal.", nameof(rules));
            }
            foreach (var sym in r.Right)
            {
                if (!varSet.Contains(sym))
                {
                    throw new ArgumentException($"Rule symbol '{sym}' in '{r}' is not in Variables.", nameof(rules));
                }
            }
            if (seen.Add(r))
            {
                ruleList.Add(r);
            }
        }

        Variables = varSet;
        Terminals = termSet;
        NonTerminals = nonTerms;
        Rules = ruleList;
        StartSymbol = startSymbol;
    }

    /// <summary>
    /// Parses a context-free grammar from readable production rules such as:
    ///   S -> aSb | e
    ///   E -> E+T | T
    ///   T -> T*F | F
    ///   F -> (E) | x | y
    /// </summary>
    public static Cfg ParseFromText(string text, string? explicitStartSymbol = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("Grammar text cannot be empty.");
        }

        var rawLines = text
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        if (rawLines.Count == 0)
        {
            throw new FormatException("No grammar rules found.");
        }

        var parsedLines = new List<(string Lhs, string Rhs)>();
        var nonTerminals = new HashSet<string>(StringComparer.Ordinal);
        string? firstLhs = null;

        foreach (var line in rawLines)
        {
            string? sep = null;
            if (line.Contains("->", StringComparison.Ordinal)) sep = "->";
            else if (line.Contains("→", StringComparison.Ordinal)) sep = "→";
            else if (line.Contains("::=", StringComparison.Ordinal)) sep = "::=";

            if (sep is null)
            {
                throw new FormatException($"Invalid production rule (missing '->'): '{line}'.");
            }

            var idx = line.IndexOf(sep, StringComparison.Ordinal);
            var lhs = line[..idx].Trim();
            var rhs = line[(idx + sep.Length)..].Trim();
            if (lhs.Length == 0)
            {
                throw new FormatException($"Missing left-hand side in rule: '{line}'.");
            }

            firstLhs ??= lhs;
            nonTerminals.Add(lhs);
            parsedLines.Add((lhs, rhs));
        }

        var startSymbol = explicitStartSymbol ?? firstLhs!;
        nonTerminals.Add(startSymbol);

        var sortedNonTerminals = nonTerminals
            .OrderByDescending(nt => nt.Length)
            .ThenBy(nt => nt, StringComparer.Ordinal)
            .ToList();

        var terminals = new HashSet<string>(StringComparer.Ordinal);
        var rules = new List<CfgRule>();

        foreach (var (lhs, rhsAll) in parsedLines)
        {
            var alternatives = rhsAll.Split('|');
            foreach (var rawAlt in alternatives)
            {
                var alt = rawAlt.Trim();
                if (alt.Length == 0 || (AutomataSymbols.IsEmptyToken(alt) && !nonTerminals.Contains(alt)))
                {
                    rules.Add(new CfgRule(lhs, Array.Empty<string>()));
                    continue;
                }

                var tokens = TokenizeRhs(alt, sortedNonTerminals, nonTerminals);
                foreach (var tok in tokens)
                {
                    if (!nonTerminals.Contains(tok))
                    {
                        terminals.Add(tok);
                    }
                }
                rules.Add(new CfgRule(lhs, tokens));
            }
        }

        var variables = new HashSet<string>(nonTerminals, StringComparer.Ordinal);
        foreach (var t in terminals)
        {
            variables.Add(t);
        }

        return new Cfg(variables, terminals, rules, startSymbol);
    }

    private static List<string> TokenizeRhs(string alt, List<string> sortedNonTerminals, HashSet<string> nonTerminals)
    {
        var result = new List<string>();
        var segments = alt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (nonTerminals.Contains(seg))
            {
                result.Add(seg);
                continue;
            }

            if (segments.Length == 1 && AutomataSymbols.IsEmptyToken(seg))
            {
                continue;
            }

            var pos = 0;
            while (pos < seg.Length)
            {
                string? matchedNt = null;
                foreach (var nt in sortedNonTerminals)
                {
                    if (pos + nt.Length <= seg.Length &&
                        string.CompareOrdinal(seg, pos, nt, 0, nt.Length) == 0)
                    {
                        matchedNt = nt;
                        break;
                    }
                }

                if (matchedNt is not null)
                {
                    result.Add(matchedNt);
                    pos += matchedNt.Length;
                }
                else
                {
                    result.Add(seg[pos].ToString());
                    pos++;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Returns true iff every rule is right-linear: A -> e, A -> w, A -> B, or A -> w B
    /// where w ∈ Σ+ and B ∈ (V - Σ).
    /// </summary>
    public bool IsRegularGrammar()
    {
        foreach (var rule in Rules)
        {
            for (var i = 0; i < rule.Right.Count - 1; i++)
            {
                if (NonTerminals.Contains(rule.Right[i]))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public string ToGrammarText()
    {
        var sb = new StringBuilder();
        var grouped = Rules.GroupBy(r => r.Left);
        foreach (var g in grouped)
        {
            sb.Append(g.Key).Append(" -> ");
            sb.AppendLine(string.Join(" | ", g.Select(r => r.FormatRight())));
        }
        return sb.ToString().TrimEnd();
    }
}
