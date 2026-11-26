using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;

namespace SuperCsStudent.Automata.Grammars;

public sealed record ParseTreeNode(
    string Symbol,
    bool IsTerminal,
    IReadOnlyList<ParseTreeNode> Children);

public sealed record CfgParseResult(
    bool Accepted,
    string Input,
    IReadOnlyList<string> DerivationSteps,
    ParseTreeNode? Root,
    DiagramScene ParseTreeScene,
    string Summary);

/// <summary>
/// General Earley parser for arbitrary context-free grammars supporting left recursion,
/// epsilon productions, cyclic rules, and ambiguity. Extracts a minimum-size acyclic parse tree,
/// leftmost derivation steps, and a tidy-tree DiagramScene.
/// </summary>
public static class CfgParser
{
    public static CfgParseResult Parse(Cfg grammar, string? input)
    {
        var normalized = AutomataSymbols.NormalizeWord(input, grammar.Terminals);
        var displayInput = AutomataSymbols.FormatWord(normalized);

        if (!TryTokenizeInput(grammar, normalized, out var tokens))
        {
            return new CfgParseResult(
                Accepted: false,
                Input: displayInput,
                DerivationSteps: Array.Empty<string>(),
                Root: null,
                ParseTreeScene: DiagramLayout.LayoutTidyTree(null, caption: $"Rejected: '{displayInput}' contains unknown symbols", overlayTag: "CFG"),
                Summary: $"Rejected '{displayInput}': contains symbol outside grammar terminals.");
        }

        var n = tokens.Count;
        var nullable = ComputeNullable(grammar);

        // Rule index 0..m-1 are grammar rules; index -1 is augmented rule $START -> StartSymbol
        var rules = grammar.Rules;
        var rulesByLhs = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var nt in grammar.NonTerminals)
        {
            rulesByLhs[nt] = new List<int>();
        }
        for (var r = 0; r < rules.Count; r++)
        {
            rulesByLhs[rules[r].Left].Add(r);
        }

        var chart = new List<EarleyItem>[n + 1];
        var chartSets = new HashSet<EarleyItem>[n + 1];
        for (var i = 0; i <= n; i++)
        {
            chart[i] = new List<EarleyItem>();
            chartSets[i] = new HashSet<EarleyItem>();
        }

        void AddItem(int pos, EarleyItem item)
        {
            if (chartSets[pos].Add(item))
            {
                chart[pos].Add(item);
            }
        }

        var startItem = new EarleyItem(-1, 0, 0);
        AddItem(0, startItem);

        for (var i = 0; i <= n; i++)
        {
            for (var idx = 0; idx < chart[i].Count; idx++)
            {
                var item = chart[i][idx];
                var rhsCount = item.RuleIndex == -1 ? 1 : rules[item.RuleIndex].Right.Count;

                if (item.Dot < rhsCount)
                {
                    var nextSym = item.RuleIndex == -1 ? grammar.StartSymbol : rules[item.RuleIndex].Right[item.Dot];
                    if (grammar.NonTerminals.Contains(nextSym))
                    {
                        // Predictor
                        foreach (var rIdx in rulesByLhs[nextSym])
                        {
                            AddItem(i, new EarleyItem(rIdx, 0, i));
                        }

                        // Aycock-Horspool nullable advancement
                        if (nullable.Contains(nextSym))
                        {
                            AddItem(i, new EarleyItem(item.RuleIndex, item.Dot + 1, item.Origin));
                        }
                    }
                    else
                    {
                        // Scanner
                        if (i < n && StringComparer.Ordinal.Equals(tokens[i], nextSym))
                        {
                            AddItem(i + 1, new EarleyItem(item.RuleIndex, item.Dot + 1, item.Origin));
                        }
                    }
                }
                else
                {
                    // Completer
                    if (item.RuleIndex == -1) continue;
                    var completedNt = rules[item.RuleIndex].Left;
                    var originChart = chart[item.Origin];
                    // Snapshot count because when item.Origin == i, originChart may grow
                    var originCount = originChart.Count;
                    for (var k = 0; k < originCount; k++)
                    {
                        var waiting = originChart[k];
                        var waitingRhsCount = waiting.RuleIndex == -1 ? 1 : rules[waiting.RuleIndex].Right.Count;
                        if (waiting.Dot < waitingRhsCount)
                        {
                            var waitingSym = waiting.RuleIndex == -1
                                ? grammar.StartSymbol
                                : rules[waiting.RuleIndex].Right[waiting.Dot];
                            if (StringComparer.Ordinal.Equals(waitingSym, completedNt))
                            {
                                AddItem(i, new EarleyItem(waiting.RuleIndex, waiting.Dot + 1, waiting.Origin));
                            }
                        }
                    }
                }
            }
        }

        var goalItem = new EarleyItem(-1, 1, 0);
        if (!chartSets[n].Contains(goalItem))
        {
            return new CfgParseResult(
                Accepted: false,
                Input: displayInput,
                DerivationSteps: Array.Empty<string>(),
                Root: null,
                ParseTreeScene: DiagramLayout.LayoutTidyTree(null, caption: $"Rejected: '{displayInput}' ∉ L(G)", overlayTag: "CFG"),
                Summary: $"Rejected '{displayInput}': no valid derivation from {grammar.StartSymbol}.");
        }

        var root = ExtractMinimalParseTree(grammar, tokens, chart);
        var derivation = BuildLeftmostDerivation(root);
        var scene = BuildParseTreeScene(root, $"Parse tree for '{displayInput}' ({derivation.Count - 1} steps)");

        return new CfgParseResult(
            Accepted: true,
            Input: displayInput,
            DerivationSteps: derivation,
            Root: root,
            ParseTreeScene: scene,
            Summary: $"Accepted '{displayInput}' in {derivation.Count - 1} derivation step(s).");
    }

    private static bool TryTokenizeInput(Cfg grammar, string normalized, out List<string> tokens)
    {
        tokens = new List<string>();
        if (normalized.Length == 0)
        {
            return true;
        }

        var sortedTerms = grammar.Terminals
            .OrderByDescending(t => t.Length)
            .ThenBy(t => t, StringComparer.Ordinal)
            .ToList();

        var pos = 0;
        while (pos < normalized.Length)
        {
            string? matched = null;
            foreach (var term in sortedTerms)
            {
                if (term.Length > 0 &&
                    pos + term.Length <= normalized.Length &&
                    string.CompareOrdinal(normalized, pos, term, 0, term.Length) == 0)
                {
                    matched = term;
                    break;
                }
            }

            if (matched is null)
            {
                return false;
            }

            tokens.Add(matched);
            pos += matched.Length;
        }

        return true;
    }

    private static HashSet<string> ComputeNullable(Cfg grammar)
    {
        var nullable = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var rule in grammar.Rules)
            {
                if (nullable.Contains(rule.Left)) continue;
                if (rule.Right.All(sym => nullable.Contains(sym)))
                {
                    nullable.Add(rule.Left);
                    changed = true;
                }
            }
        } while (changed);
        return nullable;
    }

    /// <summary>
    /// Extracts a strictly acyclic minimum-cost parse tree from the completed Earley chart
    /// via Bellman-Ford cost relaxation over completed spans (eliminates any cycle risk on
    /// nullable or unit-production loops such as S -> SS | e or A -> A).
    /// </summary>
    private static ParseTreeNode ExtractMinimalParseTree(
        Cfg grammar,
        List<string> tokens,
        List<EarleyItem>[] chart)
    {
        var n = tokens.Count;
        var rules = grammar.Rules;

        // Collect all completed spans (RuleIndex, Origin, End)
        var completed = new List<(int RuleIndex, int Origin, int End)>();
        for (var end = 0; end <= n; end++)
        {
            foreach (var item in chart[end])
            {
                if (item.RuleIndex >= 0 && item.Dot == rules[item.RuleIndex].Right.Count)
                {
                    completed.Add((item.RuleIndex, item.Origin, end));
                }
            }
        }

        var bestCost = new Dictionary<(string Nt, int Start, int End), int>();
        var bestChoice = new Dictionary<(string Nt, int Start, int End), (int RuleIndex, int[] Splits)>();

        // Base cases: epsilon productions
        foreach (var (rIdx, origin, end) in completed)
        {
            var rule = rules[rIdx];
            if (rule.Right.Count == 0 && origin == end)
            {
                var key = (rule.Left, origin, end);
                if (!bestCost.ContainsKey(key))
                {
                    bestCost[key] = 1;
                    bestChoice[key] = (rIdx, new[] { origin });
                }
            }
        }

        // Iterative relaxation until fixed point
        bool improved;
        do
        {
            improved = false;
            foreach (var (rIdx, origin, end) in completed)
            {
                var rule = rules[rIdx];
                var k = rule.Right.Count;
                if (k == 0) continue;

                // Small DP over RHS symbols d = 0..k and tape positions origin..end
                var spanLen = end - origin + 1;
                var dpCost = new int[k + 1, spanLen];
                var dpPrev = new int[k + 1, spanLen];
                for (var d = 0; d <= k; d++)
                {
                    for (var p = 0; p < spanLen; p++)
                    {
                        dpCost[d, p] = int.MaxValue / 4;
                        dpPrev[d, p] = -1;
                    }
                }
                dpCost[0, 0] = 0;

                for (var d = 0; d < k; d++)
                {
                    var sym = rule.Right[d];
                    var isNt = grammar.NonTerminals.Contains(sym);

                    for (var pOff = 0; pOff < spanLen; pOff++)
                    {
                        var curCost = dpCost[d, pOff];
                        if (curCost >= int.MaxValue / 4) continue;
                        var pos = origin + pOff;

                        if (!isNt)
                        {
                            if (pos < end && StringComparer.Ordinal.Equals(tokens[pos], sym))
                            {
                                var nextOff = pOff + 1;
                                var cand = curCost + 1;
                                if (cand < dpCost[d + 1, nextOff])
                                {
                                    dpCost[d + 1, nextOff] = cand;
                                    dpPrev[d + 1, nextOff] = pOff;
                                }
                            }
                        }
                        else
                        {
                            for (var nextOff = pOff; nextOff < spanLen; nextOff++)
                            {
                                var nextPos = origin + nextOff;
                                if (bestCost.TryGetValue((sym, pos, nextPos), out var childCost))
                                {
                                    var cand = curCost + childCost;
                                    if (cand < dpCost[d + 1, nextOff])
                                    {
                                        dpCost[d + 1, nextOff] = cand;
                                        dpPrev[d + 1, nextOff] = pOff;
                                    }
                                }
                            }
                        }
                    }
                }

                var totalRhsCost = dpCost[k, spanLen - 1];
                if (totalRhsCost < int.MaxValue / 4)
                {
                    var candidateCost = 1 + totalRhsCost;
                    var key = (rule.Left, origin, end);
                    if (!bestCost.TryGetValue(key, out var existing) || candidateCost < existing)
                    {
                        var splits = new int[k + 1];
                        var curOff = spanLen - 1;
                        splits[k] = end;
                        for (var d = k; d >= 1; d--)
                        {
                            curOff = dpPrev[d, curOff];
                            splits[d - 1] = origin + curOff;
                        }

                        bestCost[key] = candidateCost;
                        bestChoice[key] = (rIdx, splits);
                        improved = true;
                    }
                }
            }
        } while (improved);

        ParseTreeNode BuildNode(string nt, int start, int end)
        {
            var (rIdx, splits) = bestChoice[(nt, start, end)];
            var rule = rules[rIdx];
            if (rule.Right.Count == 0)
            {
                return new ParseTreeNode(
                    nt,
                    IsTerminal: false,
                    Children: new[] { new ParseTreeNode(AutomataSymbols.Empty, IsTerminal: true, Array.Empty<ParseTreeNode>()) });
            }

            var children = new List<ParseTreeNode>(rule.Right.Count);
            for (var d = 0; d < rule.Right.Count; d++)
            {
                var sym = rule.Right[d];
                if (grammar.NonTerminals.Contains(sym))
                {
                    children.Add(BuildNode(sym, splits[d], splits[d + 1]));
                }
                else
                {
                    children.Add(new ParseTreeNode(sym, IsTerminal: true, Array.Empty<ParseTreeNode>()));
                }
            }

            return new ParseTreeNode(nt, IsTerminal: false, children);
        }

        return BuildNode(grammar.StartSymbol, 0, n);
    }

    private static IReadOnlyList<string> BuildLeftmostDerivation(ParseTreeNode root)
    {
        var steps = new List<string>();
        var frontier = new List<ParseTreeNode> { root };

        string FormatFrontier(List<ParseTreeNode> nodes)
        {
            if (nodes.Count == 0)
            {
                return AutomataSymbols.Empty;
            }
            var needsSpace = nodes.Any(x => x.Symbol.Length > 1);
            return string.Join(needsSpace ? " " : string.Empty, nodes.Select(x => x.Symbol));
        }

        steps.Add(FormatFrontier(frontier));

        while (true)
        {
            var expandIdx = frontier.FindIndex(x => !x.IsTerminal);
            if (expandIdx < 0)
            {
                break;
            }

            var target = frontier[expandIdx];
            frontier.RemoveAt(expandIdx);

            // Replace non-terminal with its children, dropping synthetic epsilon leaves
            var isEpsilonExpansion = target.Children.Count == 1
                && target.Children[0].IsTerminal
                && target.Children[0].Symbol == AutomataSymbols.Empty;

            if (!isEpsilonExpansion)
            {
                frontier.InsertRange(expandIdx, target.Children);
            }

            steps.Add(FormatFrontier(frontier));
        }

        return steps;
    }

    private static DiagramScene BuildParseTreeScene(ParseTreeNode root, string caption)
    {
        var idCounter = 0;

        LayoutTreeNode Convert(ParseTreeNode node, bool isRoot)
        {
            var id = $"pt_{idCounter++}";
            var isEpsilonLeaf = node.IsTerminal && node.Symbol == AutomataSymbols.Empty && node.Children.Count == 0;
            var shape = isEpsilonLeaf
                ? NodeShapes.Sentinel
                : node.IsTerminal
                    ? NodeShapes.Pill
                    : NodeShapes.Circle;
            var sem = isRoot
                ? SemanticStates.Active
                : isEpsilonLeaf
                    ? SemanticStates.Visited
                    : node.IsTerminal
                        ? SemanticStates.Accepted
                        : SemanticStates.Default;

            var layout = new LayoutTreeNode
            {
                Id = id,
                Label = node.Symbol,
                Shape = shape,
                SemanticState = sem,
                Width = System.Math.Max(42, node.Symbol.Length * 11 + 22),
                Height = 42
            };

            foreach (var child in node.Children)
            {
                layout.Children.Add(Convert(child, isRoot: false));
            }

            return layout;
        }

        var layoutRoot = Convert(root, isRoot: true);
        return DiagramLayout.LayoutTidyTree(layoutRoot, horizontalGap: 20, levelGap: 76, caption: caption, overlayTag: "Parse Tree");
    }

    private readonly record struct EarleyItem(int RuleIndex, int Dot, int Origin);
}
