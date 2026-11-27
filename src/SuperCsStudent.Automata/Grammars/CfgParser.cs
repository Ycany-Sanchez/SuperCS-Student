using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;

namespace SuperCsStudent.Automata.Grammars;

public sealed record ParseTreeNode(
    string Symbol,
    bool IsTerminal,
    IReadOnlyList<ParseTreeNode> Children);

public sealed record CfgQueueSymbol(
    string Symbol,
    bool IsTerminal,
    bool IsNewlyProduced = false,
    bool IsExpandHead = false);

public sealed record CfgDerivationQueueStep(
    int StepIndex,
    string SententialForm,
    IReadOnlyList<string> ResolvedOutput,
    int NewlyResolvedCount,
    IReadOnlyList<CfgQueueSymbol> QueueSymbols,
    string? ExpandedNonTerminal,
    IReadOnlyList<string> ProducedSymbols,
    string? AppliedRule,
    bool IsComplete);

public sealed record CfgDerivationToken(
    string Symbol,
    bool IsTerminal,
    bool IsNewlyInserted = false,
    bool IsNextToExpand = false,
    bool IsMatchedPrefix = false,
    bool IsMatchedSuffix = false,
    bool IsMismatch = false);

public sealed record CfgRuleAttempt(
    string RuleText,
    string ResultingForm,
    string Reason);

public sealed record CfgDerivationStep(
    int StepIndex,
    string SententialForm,
    IReadOnlyList<CfgDerivationToken> Tokens,
    string? ExpandedNonTerminal,
    string? AppliedRule,
    string Explanation,
    int MatchedPrefixLength,
    int MatchedSuffixLength,
    int? FailureInputIndex = null,
    bool IsAccepted = false,
    bool IsRejected = false,
    IReadOnlyList<CfgRuleAttempt>? RejectedRuleAttempts = null);

public sealed record CfgParseResult(
    bool Accepted,
    string Input,
    IReadOnlyList<string> DerivationSteps,
    ParseTreeNode? Root,
    DiagramScene ParseTreeScene,
    string Summary,
    IReadOnlyList<CfgDerivationQueueStep>? QueueSteps = null,
    IReadOnlyList<CfgDerivationStep>? TraceSteps = null);

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

        if (!TryTokenizeInput(grammar, normalized, out var tokens, out var badPos, out var badSym))
        {
            var unknownTrace = BuildUnknownSymbolTrace(grammar, displayInput, badPos, badSym);
            return new CfgParseResult(
                Accepted: false,
                Input: displayInput,
                DerivationSteps: Array.Empty<string>(),
                Root: null,
                ParseTreeScene: DiagramLayout.LayoutTidyTree(null, caption: $"Rejected: '{displayInput}' contains unknown symbols", overlayTag: "CFG"),
                Summary: $"Rejected '{displayInput}': symbol '{badSym}' at position {badPos + 1} is outside grammar terminals.",
                TraceSteps: unknownTrace);
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
            var rejectedTrace = BuildRejectedTraceSteps(grammar, tokens, displayInput);
            var failureReason = rejectedTrace.Count > 0 ? rejectedTrace[^1].Explanation : $"No valid derivation from {grammar.StartSymbol}.";
            return new CfgParseResult(
                Accepted: false,
                Input: displayInput,
                DerivationSteps: Array.Empty<string>(),
                Root: null,
                ParseTreeScene: DiagramLayout.LayoutTidyTree(null, caption: $"Rejected: '{displayInput}' ∉ L(G)", overlayTag: "CFG"),
                Summary: $"Rejected '{displayInput}': {failureReason}",
                TraceSteps: rejectedTrace);
        }

        var root = ExtractMinimalParseTree(grammar, tokens, chart);
        var (derivation, queueSteps, traceSteps) = BuildLeftmostDerivationWithQueue(root, tokens, displayInput);
        var scene = BuildParseTreeScene(root, $"Parse tree for '{displayInput}' ({derivation.Count - 1} steps)");

        return new CfgParseResult(
            Accepted: true,
            Input: displayInput,
            DerivationSteps: derivation,
            Root: root,
            ParseTreeScene: scene,
            Summary: $"Accepted '{displayInput}' in {derivation.Count - 1} derivation step(s).",
            QueueSteps: queueSteps,
            TraceSteps: traceSteps);
    }

    private static bool TryTokenizeInput(
        Cfg grammar,
        string normalized,
        out List<string> tokens,
        out int badPos,
        out string badSym)
    {
        tokens = new List<string>();
        badPos = -1;
        badSym = string.Empty;
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
                badPos = pos;
                badSym = normalized[pos].ToString();
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

    private static (IReadOnlyList<string> Steps, IReadOnlyList<CfgDerivationQueueStep> QueueSteps, IReadOnlyList<CfgDerivationStep> TraceSteps)
        BuildLeftmostDerivationWithQueue(ParseTreeNode root, IReadOnlyList<string> targetTokens, string displayInput)
    {
        var steps = new List<string>();
        var queueSteps = new List<CfgDerivationQueueStep>();
        var traceSteps = new List<CfgDerivationStep>();
        var frontier = new List<ParseTreeNode> { root };

        static string FormatNodes(IReadOnlyList<ParseTreeNode> nodes)
        {
            if (nodes.Count == 0)
            {
                return AutomataSymbols.Empty;
            }
            var needsSpace = nodes.Any(x => x.Symbol.Length > 1);
            return string.Join(needsSpace ? " " : string.Empty, nodes.Select(x => x.Symbol));
        }

        static string FormatSymbols(IReadOnlyList<string> syms)
        {
            if (syms.Count == 0) return AutomataSymbols.Empty;
            var needsSpace = syms.Any(x => x.Length > 1);
            return string.Join(needsSpace ? " " : string.Empty, syms);
        }

        static (int Prefix, int Suffix, IReadOnlyList<CfgDerivationToken> Tokens) BuildAcceptedTokens(
            IReadOnlyList<ParseTreeNode> curFrontier,
            int insertedStart,
            int insertedLength,
            int nextExpandIdx)
        {
            if (curFrontier.Count == 0)
            {
                return (0, 0, new[]
                {
                    new CfgDerivationToken(
                        AutomataSymbols.Empty,
                        IsTerminal: true,
                        IsNewlyInserted: true,
                        IsMatchedPrefix: true)
                });
            }

            var prefix = nextExpandIdx < 0 ? curFrontier.Count : nextExpandIdx;
            var suffix = 0;
            if (nextExpandIdx >= 0)
            {
                var lastNt = -1;
                for (var k = curFrontier.Count - 1; k >= 0; k--)
                {
                    if (!curFrontier[k].IsTerminal)
                    {
                        lastNt = k;
                        break;
                    }
                }
                if (lastNt >= 0)
                {
                    suffix = curFrontier.Count - 1 - lastNt;
                }
            }

            var tokList = new List<CfgDerivationToken>(curFrontier.Count);
            var suffixStart = curFrontier.Count - suffix;
            for (var j = 0; j < curFrontier.Count; j++)
            {
                var isNew = insertedStart >= 0 && j >= insertedStart && j < insertedStart + insertedLength;
                tokList.Add(new CfgDerivationToken(
                    curFrontier[j].Symbol,
                    curFrontier[j].IsTerminal,
                    IsNewlyInserted: isNew,
                    IsNextToExpand: j == nextExpandIdx,
                    IsMatchedPrefix: j < prefix,
                    IsMatchedSuffix: suffix > 0 && j >= suffixStart));
            }
            return (prefix, suffix, tokList);
        }

        var initialForm = FormatNodes(frontier);
        steps.Add(initialForm);

        var initExpandIdx = frontier.FindIndex(x => !x.IsTerminal);
        var initResolvedEnd = initExpandIdx < 0 ? frontier.Count : initExpandIdx;
        var initResolved = frontier.Take(initResolvedEnd).Select(x => x.Symbol).ToArray();
        var initQueue = new List<CfgQueueSymbol>();
        for (var j = initResolvedEnd; j < frontier.Count; j++)
        {
            initQueue.Add(new CfgQueueSymbol(
                frontier[j].Symbol,
                frontier[j].IsTerminal,
                IsNewlyProduced: false,
                IsExpandHead: j == initExpandIdx));
        }

        queueSteps.Add(new CfgDerivationQueueStep(
            StepIndex: 0,
            SententialForm: initialForm,
            ResolvedOutput: initResolved,
            NewlyResolvedCount: 0,
            QueueSymbols: initQueue,
            ExpandedNonTerminal: null,
            ProducedSymbols: Array.Empty<string>(),
            AppliedRule: null,
            IsComplete: initExpandIdx < 0));

        var (initPrefix, initSuffix, initTraceTokens) = BuildAcceptedTokens(frontier, -1, 0, initExpandIdx);
        traceSteps.Add(new CfgDerivationStep(
            StepIndex: 0,
            SententialForm: initialForm,
            Tokens: initTraceTokens,
            ExpandedNonTerminal: null,
            AppliedRule: null,
            Explanation: $"Start at {root.Symbol}. Goal: derive w = \"{displayInput}\".",
            MatchedPrefixLength: initPrefix,
            MatchedSuffixLength: initSuffix,
            IsAccepted: initExpandIdx < 0));

        while (true)
        {
            var expandIdx = frontier.FindIndex(x => !x.IsTerminal);
            if (expandIdx < 0)
            {
                break;
            }

            var prevResolvedCount = expandIdx;
            var target = frontier[expandIdx];
            frontier.RemoveAt(expandIdx);

            // Replace non-terminal with its children, dropping synthetic epsilon leaves
            var isEpsilonExpansion = target.Children.Count == 1
                && target.Children[0].IsTerminal
                && target.Children[0].Symbol == AutomataSymbols.Empty;

            var produced = isEpsilonExpansion
                ? Array.Empty<string>()
                : target.Children.Select(c => c.Symbol).ToArray();

            if (!isEpsilonExpansion)
            {
                frontier.InsertRange(expandIdx, target.Children);
            }

            var form = FormatNodes(frontier);
            steps.Add(form);

            var nextExpandIdx = frontier.FindIndex(x => !x.IsTerminal);
            var resolvedEnd = nextExpandIdx < 0 ? frontier.Count : nextExpandIdx;
            var resolvedOutput = frontier.Take(resolvedEnd).Select(x => x.Symbol).ToArray();
            var newlyResolvedCount = System.Math.Max(0, resolvedEnd - prevResolvedCount);

            var producedEndExclusive = expandIdx + produced.Length;
            var queueSyms = new List<CfgQueueSymbol>();
            for (var j = resolvedEnd; j < frontier.Count; j++)
            {
                queueSyms.Add(new CfgQueueSymbol(
                    frontier[j].Symbol,
                    frontier[j].IsTerminal,
                    IsNewlyProduced: j >= expandIdx && j < producedEndExclusive,
                    IsExpandHead: j == nextExpandIdx));
            }

            var ruleStr = $"{target.Symbol} → {FormatSymbols(produced)}";
            var isDone = nextExpandIdx < 0;
            queueSteps.Add(new CfgDerivationQueueStep(
                StepIndex: queueSteps.Count,
                SententialForm: form,
                ResolvedOutput: resolvedOutput,
                NewlyResolvedCount: newlyResolvedCount,
                QueueSymbols: queueSyms,
                ExpandedNonTerminal: target.Symbol,
                ProducedSymbols: produced,
                AppliedRule: ruleStr,
                IsComplete: isDone));

            var (prefLen, suffLen, stepTokens) = BuildAcceptedTokens(frontier, expandIdx, produced.Length, nextExpandIdx);
            string explanation;
            if (isDone)
            {
                explanation = $"Replace {target.Symbol} using {ruleStr}. No non-terminals remain and \"{form}\" matches w = \"{displayInput}\" — Accepted!";
            }
            else
            {
                var matchNote = DescribeMatchProgress(targetTokens, prefLen, suffLen);
                explanation = string.IsNullOrEmpty(matchNote)
                    ? $"Replace leftmost {target.Symbol} using {ruleStr}."
                    : $"Replace leftmost {target.Symbol} using {ruleStr}. {matchNote}";
            }

            traceSteps.Add(new CfgDerivationStep(
                StepIndex: traceSteps.Count,
                SententialForm: form,
                Tokens: stepTokens,
                ExpandedNonTerminal: target.Symbol,
                AppliedRule: ruleStr,
                Explanation: explanation,
                MatchedPrefixLength: prefLen,
                MatchedSuffixLength: suffLen,
                IsAccepted: isDone));
        }

        return (steps, queueSteps, traceSteps);
    }

    private static string DescribeMatchProgress(IReadOnlyList<string> targetTokens, int prefixLen, int suffixLen)
    {
        if (prefixLen == 0 && suffixLen == 0) return string.Empty;
        var n = targetTokens.Count;
        var parts = new List<string>();
        if (prefixLen > 0)
        {
            parts.Add($"prefix \"{string.Concat(targetTokens.Take(prefixLen))}\"");
        }
        if (suffixLen > 0 && prefixLen + suffixLen <= n)
        {
            parts.Add($"suffix \"{string.Concat(targetTokens.Skip(n - suffixLen))}\"");
        }
        var remLen = n - prefixLen - suffixLen;
        if (remLen > 0)
        {
            var rem = string.Concat(targetTokens.Skip(prefixLen).Take(remLen));
            return $"Matches {string.Join(" & ", parts)}; remaining inner slice: \"{rem}\".";
        }
        return $"Matches {string.Join(" & ", parts)}.";
    }

    private static IReadOnlyList<CfgDerivationStep> BuildUnknownSymbolTrace(
        Cfg grammar,
        string displayInput,
        int badPos,
        string badSym)
    {
        var sigmaList = string.Join(", ", grammar.Terminals.OrderBy(t => t, StringComparer.Ordinal));
        var startSym = grammar.StartSymbol;
        var reason = $"Symbol '{badSym}' at position {badPos + 1} is not in grammar terminals Σ = {{ {sigmaList} }}, so no production rule can ever generate it.";

        return
        [
            new CfgDerivationStep(
                StepIndex: 0,
                SententialForm: startSym,
                Tokens: [new CfgDerivationToken(startSym, IsTerminal: false, IsNextToExpand: true)],
                ExpandedNonTerminal: null,
                AppliedRule: null,
                Explanation: $"Start at {startSym}. Goal: derive w = \"{displayInput}\".",
                MatchedPrefixLength: 0,
                MatchedSuffixLength: 0),
            new CfgDerivationStep(
                StepIndex: 1,
                SententialForm: startSym,
                Tokens: [new CfgDerivationToken(startSym, IsTerminal: false, IsMismatch: true)],
                ExpandedNonTerminal: startSym,
                AppliedRule: "✕ Unknown terminal",
                Explanation: reason,
                MatchedPrefixLength: 0,
                MatchedSuffixLength: 0,
                FailureInputIndex: badPos,
                IsRejected: true,
                RejectedRuleAttempts:
                [
                    new CfgRuleAttempt($"Σ = {{ {sigmaList} }}", displayInput, $"'{badSym}' at position {badPos + 1} is outside Σ.")
                ])
        ];
    }

    private static IReadOnlyList<CfgDerivationStep> BuildRejectedTraceSteps(
        Cfg grammar,
        IReadOnlyList<string> targetTokens,
        string displayInput)
    {
        const int Inf = 1_000_000;
        var n = targetTokens.Count;
        var minLen = ComputeMinimumDerivableLengths(grammar, Inf);
        var rulesByLhs = new Dictionary<string, List<CfgRule>>(StringComparer.Ordinal);
        foreach (var nt in grammar.NonTerminals)
        {
            rulesByLhs[nt] = new List<CfgRule>();
        }
        foreach (var rule in grammar.Rules)
        {
            rulesByLhs[rule.Left].Add(rule);
        }

        static string FormatForm(IReadOnlyList<string> syms)
        {
            if (syms.Count == 0) return AutomataSymbols.Empty;
            var needsSpace = syms.Any(s => s.Length > 1);
            return string.Join(needsSpace ? " " : string.Empty, syms);
        }

        static IReadOnlyList<CfgDerivationToken> BuildTokensForForm(
            Cfg g,
            IReadOnlyList<string> syms,
            int insertedStart,
            int insertedLen,
            int prefixLen,
            int suffixLen,
            int nextExpandIdx,
            bool markExpandAsMismatch)
        {
            if (syms.Count == 0)
            {
                return [new CfgDerivationToken(AutomataSymbols.Empty, IsTerminal: true, IsMismatch: markExpandAsMismatch)];
            }
            var list = new List<CfgDerivationToken>(syms.Count);
            var suffixStart = syms.Count - suffixLen;
            for (var i = 0; i < syms.Count; i++)
            {
                var isTerm = !g.NonTerminals.Contains(syms[i]);
                var isNew = insertedStart >= 0 && i >= insertedStart && i < insertedStart + insertedLen;
                list.Add(new CfgDerivationToken(
                    syms[i],
                    IsTerminal: isTerm,
                    IsNewlyInserted: isNew,
                    IsNextToExpand: !markExpandAsMismatch && i == nextExpandIdx,
                    IsMatchedPrefix: i < prefixLen,
                    IsMatchedSuffix: suffixLen > 0 && i >= suffixStart,
                    IsMismatch: markExpandAsMismatch && i == nextExpandIdx));
            }
            return list;
        }

        // Guard unproductive start symbol (e.g. S -> aS with no terminating rule)
        if (!minLen.TryGetValue(grammar.StartSymbol, out var startMin) || startMin >= Inf)
        {
            var startSym = grammar.StartSymbol;
            var attempts = rulesByLhs[startSym]
                .Select(r => new CfgRuleAttempt(
                    $"{r.Left} → {r.FormatRight()}",
                    FormatForm(r.Right),
                    $"Never reaches a finite terminal string from {startSym} (missing base case)."))
                .ToList();
            if (attempts.Count == 0)
            {
                attempts.Add(new CfgRuleAttempt($"{startSym} → ∅", startSym, $"No production rules defined for {startSym}."));
            }

            return
            [
                new CfgDerivationStep(
                    StepIndex: 0,
                    SententialForm: startSym,
                    Tokens: [new CfgDerivationToken(startSym, IsTerminal: false, IsNextToExpand: true)],
                    ExpandedNonTerminal: null,
                    AppliedRule: null,
                    Explanation: $"Start at {startSym}. Goal: derive w = \"{displayInput}\".",
                    MatchedPrefixLength: 0,
                    MatchedSuffixLength: 0),
                new CfgDerivationStep(
                    StepIndex: 1,
                    SententialForm: startSym,
                    Tokens: [new CfgDerivationToken(startSym, IsTerminal: false, IsMismatch: true)],
                    ExpandedNonTerminal: startSym,
                    AppliedRule: "✕ No base rule",
                    Explanation: $"Non-terminal {startSym} cannot derive any finite terminal string (no terminating base rule).",
                    MatchedPrefixLength: 0,
                    MatchedSuffixLength: 0,
                    FailureInputIndex: n > 0 ? 0 : null,
                    IsRejected: true,
                    RejectedRuleAttempts: attempts)
            ];
        }

        // Bounded leftmost BFS to locate the closest viable partial derivation before dead end
        const int MaxVisited = 280;
        var maxDepth = System.Math.Clamp(n + 4, 6, 14);
        var maxSententialLen = System.Math.Max(n + 6, 10);

        var visitedKeys = new HashSet<string>(StringComparer.Ordinal);
        var explored = new List<RejectedBfsNode>();
        var queue = new Queue<RejectedBfsNode>();

        var rootForm = new[] { grammar.StartSymbol };
        var rootNode = new RejectedBfsNode(
            symbols: rootForm,
            parent: null,
            expandedNonTerminal: null,
            appliedRule: null,
            insertedStart: -1,
            insertedLength: 0,
            depth: 0,
            prefixLen: 0,
            suffixLen: 0);

        visitedKeys.Add(string.Join("\u001F", rootForm));
        queue.Enqueue(rootNode);
        explored.Add(rootNode);

        while (queue.Count > 0 && explored.Count < MaxVisited)
        {
            var cur = queue.Dequeue();
            var expandIdx = FindLeftmostNonTerminal(grammar, cur.Symbols);
            if (expandIdx < 0)
            {
                continue;
            }

            var nt = cur.Symbols[expandIdx];
            var rules = rulesByLhs[nt];

            foreach (var rule in rules)
            {
                var nextSyms = new List<string>(cur.Symbols.Count - 1 + rule.Right.Count);
                for (var i = 0; i < expandIdx; i++) nextSyms.Add(cur.Symbols[i]);
                for (var i = 0; i < rule.Right.Count; i++) nextSyms.Add(rule.Right[i]);
                for (var i = expandIdx + 1; i < cur.Symbols.Count; i++) nextSyms.Add(cur.Symbols[i]);

                var ruleText = $"{rule.Left} → {rule.FormatRight()}";
                var eval = EvaluateSententialCandidate(grammar, nextSyms, targetTokens, displayInput, minLen, Inf);
                if (!eval.IsViable)
                {
                    cur.FailedAttempts.Add((new CfgRuleAttempt(ruleText, FormatForm(nextSyms), eval.FailureReason!), eval.FailureInputIndex));
                    continue;
                }

                cur.ViableRuleAttempts.Add(new CfgRuleAttempt(ruleText, FormatForm(nextSyms), string.Empty));
                if (cur.Depth >= maxDepth || nextSyms.Count > maxSententialLen)
                {
                    continue;
                }

                var key = string.Join("\u001F", nextSyms);
                if (visitedKeys.Add(key) && explored.Count < MaxVisited)
                {
                    var child = new RejectedBfsNode(
                        symbols: nextSyms,
                        parent: cur,
                        expandedNonTerminal: nt,
                        appliedRule: ruleText,
                        insertedStart: expandIdx,
                        insertedLength: rule.Right.Count,
                        depth: cur.Depth + 1,
                        prefixLen: eval.PrefixLen,
                        suffixLen: eval.SuffixLen);
                    explored.Add(child);
                    queue.Enqueue(child);
                }
            }
        }

        // Select the most informative frontier node:
        // 1. Highest matched prefix + suffix count
        // 2. Prefer a direct dead-end (all rules fail right here) if within +3 steps of min depth at that match count
        // 3. Shortest depth
        var maxMatched = explored.Max(x => x.Matched);
        var atMax = explored.Where(x => x.Matched == maxMatched).ToList();
        var minDepthAtMax = atMax.Min(x => x.Depth);
        var directDeadEnds = atMax
            .Where(x => x.ViableRuleAttempts.Count == 0 && x.Depth <= minDepthAtMax + 3)
            .OrderBy(x => x.Depth)
            .ToList();

        var best = directDeadEnds.Count > 0
            ? directDeadEnds[0]
            : atMax.OrderBy(x => x.Depth).First();

        // Reconstruct path root -> ... -> best
        var chain = new List<RejectedBfsNode>();
        for (var node = best; node is not null; node = node.Parent)
        {
            chain.Add(node);
        }
        chain.Reverse();

        var steps = new List<CfgDerivationStep>(chain.Count + 1);
        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            var form = FormatForm(node.Symbols);
            var nextNtIdx = FindLeftmostNonTerminal(grammar, node.Symbols);
            var tokList = BuildTokensForForm(
                grammar,
                node.Symbols,
                node.InsertedStart,
                node.InsertedLength,
                node.PrefixLen,
                node.SuffixLen,
                nextNtIdx,
                markExpandAsMismatch: false);

            string exp;
            if (i == 0)
            {
                exp = $"Start at {grammar.StartSymbol}. Goal: derive w = \"{displayInput}\".";
            }
            else
            {
                var progress = DescribeMatchProgress(targetTokens, node.PrefixLen, node.SuffixLen);
                exp = string.IsNullOrEmpty(progress)
                    ? $"Try leftmost {node.ExpandedNonTerminal} → via {node.AppliedRule}."
                    : $"Apply {node.AppliedRule}. {progress}";
            }

            steps.Add(new CfgDerivationStep(
                StepIndex: i,
                SententialForm: form,
                Tokens: tokList,
                ExpandedNonTerminal: node.ExpandedNonTerminal,
                AppliedRule: node.AppliedRule,
                Explanation: exp,
                MatchedPrefixLength: node.PrefixLen,
                MatchedSuffixLength: node.SuffixLen));
        }

        // Append the final Rejection step explaining why EVERY rule for the leftmost non-terminal fails
        var stuckNtIdx = FindLeftmostNonTerminal(grammar, best.Symbols);
        var stuckNt = stuckNtIdx >= 0 ? best.Symbols[stuckNtIdx] : grammar.StartSymbol;
        var bestForm = FormatForm(best.Symbols);
        var remLen = System.Math.Max(0, n - best.PrefixLen - best.SuffixLen);
        var remSlice = remLen > 0
            ? string.Concat(targetTokens.Skip(best.PrefixLen).Take(remLen))
            : string.Empty;

        var finalAttempts = new List<CfgRuleAttempt>();
        int? failIdx = null;
        var innerEndExclusive = n - best.SuffixLen;
        foreach (var (attempt, idx) in best.FailedAttempts)
        {
            finalAttempts.Add(attempt);
            if (!failIdx.HasValue && idx.HasValue &&
                (remLen == 0 || (idx.Value >= best.PrefixLen && idx.Value < innerEndExclusive)))
            {
                failIdx = idx.Value;
            }
        }
        foreach (var viable in best.ViableRuleAttempts)
        {
            var deadReason = remLen > 0
                ? $"Expands {stuckNt}, but no further continuation can derive remaining \"{remSlice}\"."
                : $"Expands {stuckNt}, but no further continuation matches w = \"{displayInput}\".";
            finalAttempts.Add(viable with { Reason = deadReason });
        }

        if (!failIdx.HasValue && n > 0)
        {
            failIdx = remLen > 0
                ? best.PrefixLen
                : System.Math.Clamp(best.PrefixLen, 0, n - 1);
        }

        var stuckExplanation = remLen > 0
            ? $"Stuck at {stuckNt} in \"{bestForm}\": no rule for {stuckNt} can derive remaining \"{remSlice}\" (checked {finalAttempts.Count} rule{(finalAttempts.Count == 1 ? "" : "s")})."
            : $"Stuck at {stuckNt} in \"{bestForm}\": all symbols of \"{displayInput}\" are accounted for, but {stuckNt} cannot finish matching w.";

        var stuckTokens = BuildTokensForForm(
            grammar,
            best.Symbols,
            insertedStart: -1,
            insertedLen: 0,
            prefixLen: best.PrefixLen,
            suffixLen: best.SuffixLen,
            nextExpandIdx: stuckNtIdx,
            markExpandAsMismatch: true);

        steps.Add(new CfgDerivationStep(
            StepIndex: steps.Count,
            SententialForm: bestForm,
            Tokens: stuckTokens,
            ExpandedNonTerminal: stuckNt,
            AppliedRule: "✕ Rejected",
            Explanation: stuckExplanation,
            MatchedPrefixLength: best.PrefixLen,
            MatchedSuffixLength: best.SuffixLen,
            FailureInputIndex: failIdx,
            IsRejected: true,
            RejectedRuleAttempts: finalAttempts));

        return steps;
    }

    private static int FindLeftmostNonTerminal(Cfg grammar, IReadOnlyList<string> symbols)
    {
        for (var i = 0; i < symbols.Count; i++)
        {
            if (grammar.NonTerminals.Contains(symbols[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static Dictionary<string, int> ComputeMinimumDerivableLengths(Cfg grammar, int inf)
    {
        var minLen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in grammar.Terminals)
        {
            minLen[t] = 1;
        }
        foreach (var nt in grammar.NonTerminals)
        {
            minLen[nt] = inf;
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var rule in grammar.Rules)
            {
                var sum = 0;
                foreach (var sym in rule.Right)
                {
                    var sLen = minLen.TryGetValue(sym, out var v) ? v : inf;
                    if (sLen >= inf)
                    {
                        sum = inf;
                        break;
                    }
                    sum += sLen;
                }
                if (sum < minLen[rule.Left])
                {
                    minLen[rule.Left] = sum;
                    changed = true;
                }
            }
        } while (changed);

        return minLen;
    }

    private static (bool IsViable, int PrefixLen, int SuffixLen, string? FailureReason, int? FailureInputIndex)
        EvaluateSententialCandidate(
            Cfg grammar,
            IReadOnlyList<string> syms,
            IReadOnlyList<string> targetTokens,
            string displayInput,
            Dictionary<string, int> minLen,
            int inf)
    {
        var n = targetTokens.Count;
        var firstNt = FindLeftmostNonTerminal(grammar, syms);

        // Case A: All terminals — since Earley rejected w, this terminal string cannot match w
        if (firstNt < 0)
        {
            if (syms.Count == 0)
            {
                return (false, 0, 0, $"Produces empty string {AutomataSymbols.Empty}, but target is \"{displayInput}\".", n > 0 ? 0 : null);
            }

            var form = string.Concat(syms);
            var minCount = System.Math.Min(syms.Count, n);
            for (var i = 0; i < minCount; i++)
            {
                if (!StringComparer.Ordinal.Equals(syms[i], targetTokens[i]))
                {
                    return (false, i, 0, $"Produces \"{form}\", differing at pos {i + 1} (got '{syms[i]}', expected '{targetTokens[i]}').", i);
                }
            }

            if (syms.Count < n)
            {
                return (false, syms.Count, 0, $"Produces \"{form}\" (length {syms.Count}), missing '{targetTokens[syms.Count]}' at pos {syms.Count + 1} of \"{displayInput}\".", syms.Count);
            }

            return (false, n, 0, $"Produces \"{form}\" (length {syms.Count}), exceeding target length {n}.", n > 0 ? n - 1 : 0);
        }

        // Case B: Leading terminal prefix must match targetTokens[0..firstNt]
        for (var j = 0; j < firstNt; j++)
        {
            if (j >= n)
            {
                return (false, n, 0, $"Requires '{syms[j]}' at pos {j + 1}, past end of input (length {n}).", n > 0 ? n - 1 : 0);
            }
            if (!StringComparer.Ordinal.Equals(syms[j], targetTokens[j]))
            {
                return (false, j, 0, $"Requires '{syms[j]}' at pos {j + 1}, but input has '{targetTokens[j]}'.", j);
            }
        }

        // Case C: Trailing terminal suffix must match end of targetTokens
        var lastNt = firstNt;
        for (var k = syms.Count - 1; k >= firstNt; k--)
        {
            if (grammar.NonTerminals.Contains(syms[k]))
            {
                lastNt = k;
                break;
            }
        }

        var suffixLen = syms.Count - 1 - lastNt;
        for (var off = 0; off < suffixLen; off++)
        {
            var sym = syms[syms.Count - 1 - off];
            var targetPos = n - 1 - off;
            if (targetPos < firstNt)
            {
                return (false, firstNt, off, $"Needs trailing '{sym}', exceeding target length {n}.", System.Math.Clamp(firstNt, 0, System.Math.Max(0, n - 1)));
            }
            if (!StringComparer.Ordinal.Equals(sym, targetTokens[targetPos]))
            {
                return (false, firstNt, off, $"Requires trailing '{sym}' at pos {targetPos + 1}, but input has '{targetTokens[targetPos]}'.", targetPos);
            }
        }

        // Case D: Minimum derivable terminal length cannot exceed n
        var minTotal = 0;
        for (var i = 0; i < syms.Count; i++)
        {
            var sLen = minLen.TryGetValue(syms[i], out var v) ? v : inf;
            if (sLen >= inf)
            {
                return (false, firstNt, suffixLen, $"Introduces non-terminal {syms[i]} which cannot derive any finite terminal string.", n > 0 ? System.Math.Clamp(firstNt, 0, n - 1) : null);
            }
            minTotal += sLen;
        }
        if (minTotal > n)
        {
            return (false, firstNt, suffixLen, $"Needs at least {minTotal} symbol(s), exceeding target length {n}.", n > 0 ? System.Math.Clamp(firstNt, 0, n - 1) : null);
        }

        // Case E: All interior terminals between firstNt and lastNt must appear in order inside targetTokens[firstNt .. n - suffixLen)
        var scanPos = firstNt;
        var scanEnd = n - suffixLen;
        for (var i = firstNt + 1; i < lastNt; i++)
        {
            if (grammar.NonTerminals.Contains(syms[i])) continue;
            var foundAt = -1;
            for (var t = scanPos; t < scanEnd; t++)
            {
                if (StringComparer.Ordinal.Equals(targetTokens[t], syms[i]))
                {
                    foundAt = t;
                    break;
                }
            }
            if (foundAt < 0)
            {
                var rem = scanEnd > firstNt ? string.Concat(targetTokens.Skip(firstNt).Take(scanEnd - firstNt)) : AutomataSymbols.Empty;
                return (false, firstNt, suffixLen, $"Introduces '{syms[i]}' which does not appear in remaining input \"{rem}\".", n > 0 ? System.Math.Clamp(firstNt, 0, n - 1) : null);
            }
            scanPos = foundAt + 1;
        }

        return (true, firstNt, suffixLen, null, null);
    }

    private sealed class RejectedBfsNode
    {
        public IReadOnlyList<string> Symbols { get; }
        public RejectedBfsNode? Parent { get; }
        public string? ExpandedNonTerminal { get; }
        public string? AppliedRule { get; }
        public int InsertedStart { get; }
        public int InsertedLength { get; }
        public int Depth { get; }
        public int PrefixLen { get; }
        public int SuffixLen { get; }
        public int Matched => PrefixLen + SuffixLen;
        public List<(CfgRuleAttempt Attempt, int? FailureInputIndex)> FailedAttempts { get; } = new();
        public List<CfgRuleAttempt> ViableRuleAttempts { get; } = new();

        public RejectedBfsNode(
            IReadOnlyList<string> symbols,
            RejectedBfsNode? parent,
            string? expandedNonTerminal,
            string? appliedRule,
            int insertedStart,
            int insertedLength,
            int depth,
            int prefixLen,
            int suffixLen)
        {
            Symbols = symbols;
            Parent = parent;
            ExpandedNonTerminal = expandedNonTerminal;
            AppliedRule = appliedRule;
            InsertedStart = insertedStart;
            InsertedLength = insertedLength;
            Depth = depth;
            PrefixLen = prefixLen;
            SuffixLen = suffixLen;
        }
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
