using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;

namespace SuperCsStudent.Automata.Pda;

/// <summary>
/// Five-column PDA computation trace row: (State, RemainingInput, Stack, TransitionUsed, Remarks).
/// </summary>
public sealed record PdaTraceRow(
    string State,
    string RemainingInput,
    string Stack,
    string TransitionUsed,
    string Remarks);

public sealed record PdaStepState(
    string CurrentState,
    string ConsumedInput,
    string RemainingInput,
    string Stack,
    IReadOnlyList<string> StackFrames,
    PdaTraceRow CurrentRow,
    IReadOnlyList<PdaTraceRow> RowsSoFar,
    bool? IsAccepted,
    DiagramScene Scene);

public sealed record PdaTraceResult(
    bool Accepted,
    TraceTimeline<PdaStepState> Timeline,
    IReadOnlyList<PdaTraceRow> Rows,
    int ExploredConfigurations);

/// <summary>
/// Bounded breadth-first nondeterministic PDA simulator emitting exact 5-column trace rows,
/// live stack frame snapshots, and DiagramScenes.
/// </summary>
public static class PdaTracer
{
    private static readonly IReadOnlyList<string> PdaPseudocode = new[]
    {
        "enqueue initial config (s, w, e)",
        "while queue not empty:",
        "    (q, u, γ) ← dequeue()",
        "    if q ∈ F and u = e and γ = e: return ACCEPT",
        "    for each ((q, x, α), (p, β)) matching prefix of u and top of γ:",
        "        enqueue (p, u[|x|..], β + γ[|α|..])",
        "return REJECT"
    };

    private readonly record struct Config(string State, int InputIndex, string Stack);

    public static PdaTraceResult Trace(
        PushdownAutomaton pda,
        string? input,
        int maxExploredConfigs = 8000,
        int maxStackDepth = 48)
    {
        var word = AutomataSymbols.NormalizeWord(input, pda.InputAlphabet);
        var effectiveMaxStack = System.Math.Max(maxStackDepth, word.Length * 3 + 12);

        var transitionsByState = pda.Transitions
            .GroupBy(t => t.FromState)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var start = new Config(pda.StartState, 0, string.Empty);
        var visited = new HashSet<Config> { start };
        var parent = new Dictionary<Config, (Config Prev, PdaTransition Transition)>();
        var queue = new Queue<Config>();
        queue.Enqueue(start);

        Config? acceptingConfig = null;
        var bestRejected = start;
        var explored = 0;

        while (queue.Count > 0 && explored < maxExploredConfigs)
        {
            var cur = queue.Dequeue();
            explored++;

            if (cur.InputIndex > bestRejected.InputIndex ||
                (cur.InputIndex == bestRejected.InputIndex && cur.Stack.Length < bestRejected.Stack.Length))
            {
                bestRejected = cur;
            }

            if (pda.AcceptStates.Contains(cur.State) &&
                cur.InputIndex == word.Length &&
                cur.Stack.Length == 0)
            {
                acceptingConfig = cur;
                break;
            }

            if (!transitionsByState.TryGetValue(cur.State, out var outgoing))
            {
                continue;
            }

            foreach (var tr in outgoing)
            {
                var rawRead = tr.RawRead;
                if (cur.InputIndex + rawRead.Length > word.Length)
                {
                    continue;
                }
                if (rawRead.Length > 0 &&
                    string.CompareOrdinal(word, cur.InputIndex, rawRead, 0, rawRead.Length) != 0)
                {
                    continue;
                }

                var rawPop = tr.RawPop;
                if (rawPop.Length > 0 && !cur.Stack.StartsWith(rawPop, StringComparison.Ordinal))
                {
                    continue;
                }

                var remainderStack = rawPop.Length == 0 ? cur.Stack : cur.Stack[rawPop.Length..];
                var nextStack = tr.RawPush + remainderStack;
                if (nextStack.Length > effectiveMaxStack)
                {
                    continue;
                }

                var next = new Config(tr.ToState, cur.InputIndex + rawRead.Length, nextStack);
                if (visited.Add(next))
                {
                    parent[next] = (cur, tr);
                    queue.Enqueue(next);
                }
            }
        }

        var accepted = acceptingConfig.HasValue;
        var target = acceptingConfig ?? bestRejected;

        // Reconstruct path from start to target
        var chain = new List<(Config Config, PdaTransition? Transition)>();
        var cursor = target;
        while (true)
        {
            if (parent.TryGetValue(cursor, out var p))
            {
                chain.Add((cursor, p.Transition));
                cursor = p.Prev;
            }
            else
            {
                chain.Add((cursor, null));
                break;
            }
        }
        chain.Reverse();

        var rows = new List<PdaTraceRow>();
        var steps = new List<TraceStep<PdaStepState>>();
        var opLog = new List<string>();

        for (var i = 0; i < chain.Count; i++)
        {
            var (cfg, tr) = chain[i];
            var consumed = word[..cfg.InputIndex];
            var rem = AutomataSymbols.FormatWord(word[cfg.InputIndex..], pda.InputAlphabet);
            var stackDisplay = AutomataSymbols.FormatWord(cfg.Stack);
            var frames = ToStackFrames(cfg.Stack);
            var isLast = i == chain.Count - 1;

            string transText;
            string remarks;
            string title;
            int pseudoLine;
            string opCode;

            if (tr is null)
            {
                transText = "—";
                remarks = $"Start configuration ({cfg.State}, {rem}, {stackDisplay})";
                title = "Initial Configuration";
                pseudoLine = 0;
                opCode = "INIT";
            }
            else
            {
                transText = tr.ToTupleString();
                remarks = DescribeTransition(tr);
                title = $"Apply {tr.ToEdgeLabel()}: {tr.FromState} → {tr.ToState}";
                pseudoLine = 4;
                opCode = "STEP";
            }

            bool? stepVerdict = null;
            if (isLast && accepted)
            {
                remarks = tr is null
                    ? "Start configuration already satisfies (p ∈ F, e, e) — Accepted"
                    : $"{remarks} — Accepted";
                stepVerdict = true;
                pseudoLine = 3;
                opCode = "ACCEPT";
            }

            var row = new PdaTraceRow(cfg.State, rem, stackDisplay, transText, remarks);
            rows.Add(row);

            var scene = BuildDiagramScene(
                pda,
                cfg.State,
                tr,
                stepVerdict,
                $"({cfg.State}, {rem}, {stackDisplay})");

            var highlightedEdges = tr is null
                ? Array.Empty<string>()
                : new[] { EdgeId(tr.FromState, tr.ToState) };

            steps.Add(new TraceStep<PdaStepState>(
                StepIndex: steps.Count,
                Title: isLast && accepted ? $"Accepted in {cfg.State}" : title,
                Explanation: remarks,
                ActivePseudocodeLine: pseudoLine,
                State: new PdaStepState(
                    cfg.State,
                    consumed,
                    rem,
                    stackDisplay,
                    frames,
                    row,
                    rows.ToArray(),
                    stepVerdict,
                    scene),
                HighlightedNodeIds: new[] { cfg.State },
                HighlightedEdgeIds: highlightedEdges,
                OperationCode: opCode,
                BadgeLabel: $"({cfg.State}, {rem}, {stackDisplay})"));
            opLog.Add($"({cfg.State}, {rem}, {stackDisplay}) | {transText}");
        }

        if (!accepted)
        {
            var lastCfg = chain[^1].Config;
            var consumed = word[..lastCfg.InputIndex];
            var rem = AutomataSymbols.FormatWord(word[lastCfg.InputIndex..], pda.InputAlphabet);
            var stackDisplay = AutomataSymbols.FormatWord(lastCfg.Stack);
            var frames = ToStackFrames(lastCfg.Stack);

            var rejectRemark = $"Rejected — no accepting computation reaches (p ∈ F, e, e) (explored {explored} configs)";
            var rejectRow = new PdaTraceRow(lastCfg.State, rem, stackDisplay, "—", rejectRemark);
            rows.Add(rejectRow);

            var rejectScene = BuildDiagramScene(
                pda,
                lastCfg.State,
                null,
                false,
                $"Rejected at ({lastCfg.State}, {rem}, {stackDisplay})");

            steps.Add(new TraceStep<PdaStepState>(
                StepIndex: steps.Count,
                Title: "Rejected",
                Explanation: rejectRemark,
                ActivePseudocodeLine: 6,
                State: new PdaStepState(
                    lastCfg.State,
                    consumed,
                    rem,
                    stackDisplay,
                    frames,
                    rejectRow,
                    rows.ToArray(),
                    false,
                    rejectScene),
                HighlightedNodeIds: new[] { lastCfg.State },
                HighlightedEdgeIds: Array.Empty<string>(),
                OperationCode: "REJECT",
                BadgeLabel: "Rejected"));
            opLog.Add($"REJECT ({lastCfg.State}, {rem}, {stackDisplay})");
        }

        var summary = accepted
            ? $"Accepted '{AutomataSymbols.FormatWord(word, pda.InputAlphabet)}' in {rows.Count - 1} step(s) (explored {explored} configs)."
            : $"Rejected '{AutomataSymbols.FormatWord(word, pda.InputAlphabet)}' after exploring {explored} config(s).";

        var timeline = new TraceTimeline<PdaStepState>(steps, PdaPseudocode, opLog, summary, accepted);
        return new PdaTraceResult(accepted, timeline, rows, explored);
    }

    public static DiagramScene BuildDiagramScene(
        PushdownAutomaton pda,
        string? activeState = null,
        PdaTransition? activeTransition = null,
        bool? verdict = null,
        string? caption = null)
    {
        var nodes = pda.OrderedStates.Select(q =>
        {
            var isAccept = pda.AcceptStates.Contains(q);
            var isStart = q == pda.StartState;
            var sem = SemanticStates.Default;
            if (q == activeState)
            {
                sem = verdict switch
                {
                    true => SemanticStates.Accepted,
                    false => SemanticStates.Rejected,
                    null => SemanticStates.Active
                };
            }

            return new GraphNodeInput(
                Id: q,
                Label: q,
                Shape: isAccept ? NodeShapes.DoubleCircle : NodeShapes.Circle,
                SemanticState: sem,
                Badge: isStart ? ">" : null,
                IsStart: isStart,
                IsAccepting: isAccept);
        }).ToList();

        var groupedEdges = pda.Transitions
            .GroupBy(t => (t.FromState, t.ToState))
            .OrderBy(g => g.Key.FromState, StringComparer.Ordinal)
            .ThenBy(g => g.Key.ToState, StringComparer.Ordinal)
            .Select(g =>
            {
                var isActive = activeTransition is not null && g.Any(t => t == activeTransition);
                var label = string.Join(" ; ", g.Select(t => t.ToEdgeLabel()));
                return new GraphEdgeInput(
                    Id: EdgeId(g.Key.FromState, g.Key.ToState),
                    SourceId: g.Key.FromState,
                    TargetId: g.Key.ToState,
                    Label: label,
                    IsDirected: true,
                    SemanticState: isActive ? SemanticStates.Active : SemanticStates.Default);
            })
            .ToList();

        return DiagramLayout.LayoutStateGraph(nodes, groupedEdges, pda.StartState, caption: caption, overlayTag: "PDA");
    }

    private static IReadOnlyList<string> ToStackFrames(string stack)
    {
        if (stack.Length == 0)
        {
            return Array.Empty<string>();
        }
        return stack.Select(c => c.ToString()).ToArray();
    }

    private static string DescribeTransition(PdaTransition tr)
    {
        var parts = new List<string>();
        if (tr.RawRead.Length > 0) parts.Add($"Read '{tr.RawRead}'");
        if (tr.RawPop.Length > 0) parts.Add($"Pop '{tr.RawPop}'");
        if (tr.RawPush.Length > 0) parts.Add($"Push '{tr.RawPush}'");
        if (parts.Count == 0)
        {
            return $"Epsilon transition {tr.FromState} → {tr.ToState}";
        }
        return string.Join(", ", parts);
    }

    private static string EdgeId(string from, string to) => $"{from}->{to}";
}
