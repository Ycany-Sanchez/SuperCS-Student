namespace SuperCsStudent.Automata.Grammars;

/// <summary>
/// Context-free language closure constructions: Union, Concatenation, and Kleene Star.
/// </summary>
public static class CfgClosure
{
    public static Cfg Union(Cfg first, Cfg second)
    {
        var terminals = new HashSet<string>(first.Terminals, StringComparer.Ordinal);
        terminals.UnionWith(second.Terminals);

        var (g1Rules, g1Start, g1Nts) = RenameNonTerminals(first, "A_", terminals);
        var (g2Rules, g2Start, g2Nts) = RenameNonTerminals(second, "B_", terminals);

        var allNts = new HashSet<string>(g1Nts, StringComparer.Ordinal);
        allNts.UnionWith(g2Nts);
        var newStart = FreshNonTerminal("S", allNts, terminals);
        allNts.Add(newStart);

        var rules = new List<CfgRule>
        {
            new(newStart, new[] { g1Start }),
            new(newStart, new[] { g2Start })
        };
        rules.AddRange(g1Rules);
        rules.AddRange(g2Rules);

        var variables = new HashSet<string>(allNts, StringComparer.Ordinal);
        variables.UnionWith(terminals);

        return new Cfg(variables, terminals, rules, newStart);
    }

    public static Cfg Concatenation(Cfg first, Cfg second)
    {
        var terminals = new HashSet<string>(first.Terminals, StringComparer.Ordinal);
        terminals.UnionWith(second.Terminals);

        var (g1Rules, g1Start, g1Nts) = RenameNonTerminals(first, "A_", terminals);
        var (g2Rules, g2Start, g2Nts) = RenameNonTerminals(second, "B_", terminals);

        var allNts = new HashSet<string>(g1Nts, StringComparer.Ordinal);
        allNts.UnionWith(g2Nts);
        var newStart = FreshNonTerminal("S", allNts, terminals);
        allNts.Add(newStart);

        var rules = new List<CfgRule>
        {
            new(newStart, new[] { g1Start, g2Start })
        };
        rules.AddRange(g1Rules);
        rules.AddRange(g2Rules);

        var variables = new HashSet<string>(allNts, StringComparer.Ordinal);
        variables.UnionWith(terminals);

        return new Cfg(variables, terminals, rules, newStart);
    }

    public static Cfg KleeneStar(Cfg grammar)
    {
        var terminals = new HashSet<string>(grammar.Terminals, StringComparer.Ordinal);
        var (gRules, gStart, gNts) = RenameNonTerminals(grammar, "K_", terminals);

        var allNts = new HashSet<string>(gNts, StringComparer.Ordinal);
        var newStart = FreshNonTerminal("S", allNts, terminals);
        allNts.Add(newStart);

        var rules = new List<CfgRule>
        {
            new(newStart, new[] { gStart, newStart }),
            new(newStart, Array.Empty<string>())
        };
        rules.AddRange(gRules);

        var variables = new HashSet<string>(allNts, StringComparer.Ordinal);
        variables.UnionWith(terminals);

        return new Cfg(variables, terminals, rules, newStart);
    }

    private static (List<CfgRule> Rules, string Start, HashSet<string> NonTerminals) RenameNonTerminals(
        Cfg grammar,
        string prefix,
        HashSet<string> forbiddenTerminals)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var nt in grammar.NonTerminals.OrderBy(x => x, StringComparer.Ordinal))
        {
            var cand = $"{prefix}{nt}";
            var idx = 0;
            while (forbiddenTerminals.Contains(cand) || used.Contains(cand))
            {
                cand = $"{prefix}{nt}_{idx++}";
            }
            map[nt] = cand;
            used.Add(cand);
        }

        var newRules = grammar.Rules.Select(r =>
            new CfgRule(
                map[r.Left],
                r.Right.Select(sym => map.TryGetValue(sym, out var mapped) ? mapped : sym).ToList()))
            .ToList();

        return (newRules, map[grammar.StartSymbol], used);
    }

    private static string FreshNonTerminal(string baseName, HashSet<string> usedNts, HashSet<string> terminals)
    {
        var cand = baseName;
        var idx = 0;
        while (usedNts.Contains(cand) || terminals.Contains(cand))
        {
            cand = $"{baseName}_{idx++}";
        }
        return cand;
    }
}
