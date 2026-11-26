using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;

namespace SuperCsStudent.Automata.Turing;

public sealed record TmMacroStep(
    int Step,
    string NodeId,
    string NodeLabel,
    string Tape,
    int HeadIndex,
    string ActionSummary);

public sealed record TmTraceStepState(
    string CurrentNodeId,
    string CurrentNodeLabel,
    string CurrentMachineState,
    string Tape,
    int HeadIndex,
    string ConfigString,
    string ActionSummary,
    bool? AcceptedDecision,
    DiagramScene SchemaScene);

public sealed record TmSchemaTraceResult(
    bool HaltedNormally,
    TmHaltReason HaltReason,
    string FinalState,
    string FinalTape,
    string TrimmedTape,
    string OutputContent,
    int FinalHeadIndex,
    bool? AcceptedDecision,
    IReadOnlyList<TmMacroStep> MacroTrace,
    IReadOnlyList<TmMicroStep> MicroTrace,
    TraceTimeline<TmTraceStepState> MacroTimeline,
    DiagramScene SchemaDiagram);

/// <summary>
/// Compiled Turing Machine schema containing the complete formal quadruple transition table,
/// the schema diagram scene, and deterministic macro+micro trace execution.
/// </summary>
public sealed class TmCompiledSchema
{
    public TmSchema Schema { get; }
    public TuringMachine Machine { get; }
    public DiagramScene SchemaScene { get; }

    internal IReadOnlyDictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>> PreCompletedByTransition { get; }
    internal IReadOnlyDictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>> PostCompletedByTransition { get; }
    internal IReadOnlyDictionary<string, string> StateToSchemaNodeId { get; }

    internal TmCompiledSchema(
        TmSchema schema,
        TuringMachine machine,
        DiagramScene schemaScene,
        IReadOnlyDictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>> preCompleted,
        IReadOnlyDictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>> postCompleted,
        IReadOnlyDictionary<string, string> stateToSchemaNodeId)
    {
        Schema = schema;
        Machine = machine;
        SchemaScene = schemaScene;
        PreCompletedByTransition = preCompleted;
        PostCompletedByTransition = postCompleted;
        StateToSchemaNodeId = stateToSchemaNodeId;
    }

    public TmSchemaTraceResult Trace(string? input, int maxSteps = 20000, HeadStartMode? headStartOverride = null)
    {
        var mode = headStartOverride ?? Schema.HeadStart;
        var word = AutomataSymbols.NormalizeWord(input, Machine.TapeAlphabet);
        var initialTape = $"{Machine.BlankSymbol}{word}{Machine.BlankSymbol}";
        var head = mode switch
        {
            HeadStartMode.LeftBlank => 0,
            HeadStartMode.FirstSymbol => 1,
            _ => word.Length + 1
        };

        return TraceOnRawTape(initialTape, head, maxSteps);
    }

    public TmSchemaTraceResult TraceOnRawTape(string rawTape, int initialHeadIndex, int maxSteps = 20000)
    {
        if (string.IsNullOrEmpty(rawTape))
        {
            rawTape = $"{Machine.BlankSymbol}{Machine.BlankSymbol}";
        }

        var tape = new List<char>(rawTape);
        while (tape.Count <= initialHeadIndex)
        {
            tape.Add(Machine.BlankSymbol);
        }

        var state = Machine.StartState;
        var head = initialHeadIndex;
        var microSteps = new List<TmMicroStep>();
        var macroSteps = new List<TmMacroStep>();
        var timelineSteps = new List<TraceStep<TmTraceStepState>>();
        var opLog = new List<string>();

        var nodeLookup = Schema.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var startNode = nodeLookup[Schema.StartNodeId];

        var initTapeStr = new string(tape.ToArray());
        var initConfig = TuringMachine.FormatConfig(state, tape, head);
        microSteps.Add(new TmMicroStep(0, state, initTapeStr, head, initConfig, "Initial configuration"));

        macroSteps.Add(new TmMacroStep(0, startNode.Id, startNode.Label, initTapeStr, head, "Initial tape configuration"));
        var initScene = TmSchemaCompiler.BuildDiagramScene(Schema, startNode.Id, null, null, $"Start: {initTapeStr} (head {head})");
        timelineSteps.Add(new TraceStep<TmTraceStepState>(
            StepIndex: 0,
            Title: $"Start at {startNode.Label}",
            Explanation: $"Initial tape {initTapeStr} with head at index {head}.",
            ActivePseudocodeLine: 0,
            State: new TmTraceStepState(startNode.Id, startNode.Label, state, initTapeStr, head, initConfig, "Initial tape configuration", null, initScene),
            HighlightedNodeIds: new[] { startNode.Id },
            HighlightedEdgeIds: Array.Empty<string>(),
            OperationCode: "INIT",
            BadgeLabel: $"head={head}"));
        opLog.Add($"INIT {initConfig}");

        var reason = TmHaltReason.Running;
        var stepCount = 0;

        while (state != Machine.HaltState && stepCount < maxSteps)
        {
            var scanned = tape[head];
            var transKey = (state, scanned);

            // Emit macro steps for any schema nodes that completed right before this transition
            if (PreCompletedByTransition.TryGetValue(transKey, out var preList))
            {
                var snapTape = new string(tape.ToArray());
                var snapConfig = TuringMachine.FormatConfig(state, tape, head);
                foreach (var meta in preList)
                {
                    var summary = meta.NextNodeId is not null
                        ? $"Completed [{meta.NodeLabel}] → scanned '{scanned}', routing to {meta.NextNodeId}"
                        : $"Completed [{meta.NodeLabel}] → scanned '{scanned}', halting";
                    macroSteps.Add(new TmMacroStep(macroSteps.Count, meta.NodeId, meta.NodeLabel, snapTape, head, summary));

                    var edgeId = meta.NextNodeId is not null ? $"{meta.NodeId}->{meta.NextNodeId}" : null;
                    var sc = TmSchemaCompiler.BuildDiagramScene(Schema, meta.NextNodeId ?? meta.NodeId, edgeId, null, summary);
                    timelineSteps.Add(new TraceStep<TmTraceStepState>(
                        StepIndex: timelineSteps.Count,
                        Title: meta.NodeLabel,
                        Explanation: summary,
                        ActivePseudocodeLine: 1,
                        State: new TmTraceStepState(meta.NodeId, meta.NodeLabel, state, snapTape, head, snapConfig, summary, null, sc),
                        HighlightedNodeIds: new[] { meta.NodeId },
                        HighlightedEdgeIds: edgeId is not null ? new[] { edgeId } : Array.Empty<string>(),
                        OperationCode: "MACRO",
                        BadgeLabel: $"head={head}"));
                    opLog.Add($"MACRO [{meta.NodeLabel}] {snapConfig}");
                }
            }

            if (!Machine.Transitions.TryGetValue(transKey, out var tr))
            {
                reason = TmHaltReason.NoTransition;
                break;
            }

            var prevState = state;
            var transText = $"δ({prevState}, {scanned}) = ({tr.NextState}, {tr.Action})";

            switch (tr.Action.Kind)
            {
                case TmActionKind.Write:
                    tape[head] = tr.Action.WriteSymbol;
                    break;
                case TmActionKind.MoveLeft:
                    head--;
                    if (head < 0)
                    {
                        state = tr.NextState;
                        stepCount++;
                        reason = TmHaltReason.SteppedOffLeft;
                        microSteps.Add(new TmMicroStep(
                            stepCount,
                            state,
                            new string(tape.ToArray()),
                            -1,
                            $"({state}, <OFF-LEFT>{new string(tape.ToArray())})",
                            $"{transText} [Stepped left off cell 0]"));
                        goto Done;
                    }
                    break;
                case TmActionKind.MoveRight:
                    head++;
                    if (head >= tape.Count)
                    {
                        tape.Add(Machine.BlankSymbol);
                    }
                    break;
            }

            state = tr.NextState;
            stepCount++;

            var afterTape = new string(tape.ToArray());
            var afterConfig = TuringMachine.FormatConfig(state, tape, head);
            microSteps.Add(new TmMicroStep(stepCount, state, afterTape, head, afterConfig, transText));

            // Emit macro steps for any terminal schema node completed directly into HaltState
            if (PostCompletedByTransition.TryGetValue(transKey, out var postList))
            {
                foreach (var meta in postList)
                {
                    var summary = $"Completed [{meta.NodeLabel}] → Halted in {Machine.HaltState}";
                    macroSteps.Add(new TmMacroStep(macroSteps.Count, meta.NodeId, meta.NodeLabel, afterTape, head, summary));

                    var contentNow = TuringMachine.ExtractOutputContent(afterTape, Machine.BlankSymbol);
                    bool? dec = contentNow == "y" ? true : contentNow == "n" ? false : null;
                    var sc = TmSchemaCompiler.BuildDiagramScene(Schema, meta.NodeId, null, dec, summary);
                    timelineSteps.Add(new TraceStep<TmTraceStepState>(
                        StepIndex: timelineSteps.Count,
                        Title: $"{meta.NodeLabel} (Halt)",
                        Explanation: summary,
                        ActivePseudocodeLine: 2,
                        State: new TmTraceStepState(meta.NodeId, meta.NodeLabel, state, afterTape, head, afterConfig, summary, dec, sc),
                        HighlightedNodeIds: new[] { meta.NodeId },
                        HighlightedEdgeIds: Array.Empty<string>(),
                        OperationCode: "HALT",
                        BadgeLabel: dec.HasValue ? (dec.Value ? "Accept (y)" : "Reject (n)") : "Halt"));
                    opLog.Add($"HALT [{meta.NodeLabel}] {afterConfig}");
                }
            }
        }

        if (state == Machine.HaltState)
        {
            reason = TmHaltReason.EnteredHaltState;
        }
        else if (reason == TmHaltReason.Running)
        {
            reason = TmHaltReason.StepLimitExceeded;
        }

    Done:
        var finalTape = new string(tape.ToArray());
        var trimmed = TuringMachine.TrimTape(finalTape, System.Math.Max(0, head), Machine.BlankSymbol);
        var content = TuringMachine.ExtractOutputContent(finalTape, Machine.BlankSymbol);
        bool? decision = reason == TmHaltReason.EnteredHaltState
            ? content == "y" ? true : content == "n" ? false : null
            : null;

        // Ensure timeline ends with final verdict state if halted via conditional halt or abnormal stop
        if (timelineSteps.Count > 0 && timelineSteps[^1].OperationCode != "HALT")
        {
            var lastNodeId = macroSteps[^1].NodeId;
            var lastLabel = macroSteps[^1].NodeLabel;
            var finalConfig = head >= 0 ? TuringMachine.FormatConfig(state, tape, head) : $"({state}, <OFF-LEFT>)";
            var verdictText = reason switch
            {
                TmHaltReason.EnteredHaltState => $"Halted in {Machine.HaltState} with tape {trimmed}",
                TmHaltReason.SteppedOffLeft => "Halted abnormally: stepped left off cell 0",
                TmHaltReason.StepLimitExceeded => $"Stopped: exceeded step limit ({maxSteps})",
                _ => $"Halted ({reason}) with tape {trimmed}"
            };
            var finalScene = TmSchemaCompiler.BuildDiagramScene(Schema, lastNodeId, null, decision, verdictText);
            timelineSteps.Add(new TraceStep<TmTraceStepState>(
                StepIndex: timelineSteps.Count,
                Title: reason == TmHaltReason.EnteredHaltState ? "Halted" : $"Stopped ({reason})",
                Explanation: verdictText,
                ActivePseudocodeLine: 2,
                State: new TmTraceStepState(lastNodeId, lastLabel, state, finalTape, head, finalConfig, verdictText, decision, finalScene),
                HighlightedNodeIds: new[] { lastNodeId },
                HighlightedEdgeIds: Array.Empty<string>(),
                OperationCode: "HALT",
                BadgeLabel: decision.HasValue ? (decision.Value ? "Accept (y)" : "Reject (n)") : reason.ToString()));
        }

        var timelineSummary = decision.HasValue
            ? $"{(decision.Value ? "Accepted" : "Rejected")} ({trimmed}) in {microSteps.Count - 1} micro-step(s) / {macroSteps.Count - 1} macro-step(s)."
            : $"Halted ({reason}): {trimmed} in {microSteps.Count - 1} micro-step(s).";

        var pseudo = new[]
        {
            "initialize semi-infinite tape #w# and head position",
            "execute active schema node operations & route on scanned symbol",
            "halt in state h (or on left-end boundary)"
        };
        var timeline = new TraceTimeline<TmTraceStepState>(timelineSteps, pseudo, opLog, timelineSummary, reason == TmHaltReason.EnteredHaltState);

        return new TmSchemaTraceResult(
            HaltedNormally: reason == TmHaltReason.EnteredHaltState,
            HaltReason: reason,
            FinalState: state,
            FinalTape: finalTape,
            TrimmedTape: trimmed,
            OutputContent: content,
            FinalHeadIndex: head,
            AcceptedDecision: decision,
            MacroTrace: macroSteps,
            MicroTrace: microSteps,
            MacroTimeline: timeline,
            SchemaDiagram: SchemaScene);
    }
}

internal sealed record CompletedSchemaNodeMeta(
    string NodeId,
    string NodeLabel,
    string? NextNodeId);

/// <summary>
/// Deterministically compiles any TmSchema into a formal write-OR-move quadruple TuringMachine,
/// a DiagramScene of the schema graph, and macro+micro execution tracing metadata.
/// </summary>
public static class TmSchemaCompiler
{
    private readonly record struct RawEdge(
        bool IsEpsilon,
        TmAction Action,
        int Target,
        CompletedSchemaNodeMeta? CompletedNode);

    public static TmCompiledSchema Compile(TmSchema schema)
    {
        var alpha = schema.TapeAlphabet.OrderBy(c => c == schema.BlankSymbol ? 0 : 1).ThenBy(c => c).ToList();
        var blank = schema.BlankSymbol;

        var rawTrans = new List<Dictionary<char, RawEdge>>();
        var rawOwner = new List<string>();

        int NewRawState(string ownerNodeId)
        {
            var id = rawTrans.Count;
            rawTrans.Add(new Dictionary<char, RawEdge>());
            rawOwner.Add(ownerNodeId);
            return id;
        }

        void AddPrim(int from, char sym, TmAction action, int to)
        {
            rawTrans[from][sym] = new RawEdge(false, action, to, null);
        }

        void AddPrimAll(int from, TmAction action, int to)
        {
            foreach (var sym in alpha)
            {
                rawTrans[from][sym] = new RawEdge(false, action, to, null);
            }
        }

        void AddEps(int from, char sym, int to, CompletedSchemaNodeMeta? completed = null)
        {
            rawTrans[from][sym] = new RawEdge(true, default, to, completed);
        }

        void AddEpsAll(int from, int to, CompletedSchemaNodeMeta? completed = null)
        {
            foreach (var sym in alpha)
            {
                rawTrans[from][sym] = new RawEdge(true, default, to, completed);
            }
        }

        void EmitSearch(string owner, int inS, int outS, char target, bool moveLeft)
        {
            var loopS = NewRawState(owner);
            var move = moveLeft ? TmAction.Left : TmAction.Right;
            AddPrimAll(inS, move, loopS);
            foreach (var sym in alpha)
            {
                if (sym == target)
                {
                    AddEps(loopS, sym, outS);
                }
                else
                {
                    AddPrim(loopS, sym, move, loopS);
                }
            }
        }

        void EmitOperation(string owner, string op, int inS, int outS)
        {
            if (op == "L")
            {
                AddPrimAll(inS, TmAction.Left, outS);
                return;
            }
            if (op == "R")
            {
                AddPrimAll(inS, TmAction.Right, outS);
                return;
            }
            if (op == "Y")
            {
                AddPrimAll(inS, TmAction.Write('y'), outS);
                return;
            }
            if (op == "N")
            {
                AddPrimAll(inS, TmAction.Write('n'), outS);
                return;
            }
            if (op.Length == 3 && op[0] == '\'' && op[2] == '\'')
            {
                AddPrimAll(inS, TmAction.Write(op[1]), outS);
                return;
            }
            if (op.StartsWith("W:", StringComparison.OrdinalIgnoreCase) && op.Length == 3)
            {
                AddPrimAll(inS, TmAction.Write(op[2]), outS);
                return;
            }
            if (op.StartsWith("L_", StringComparison.Ordinal) && op.Length == 3)
            {
                EmitSearch(owner, inS, outS, op[2], moveLeft: true);
                return;
            }
            if (op.StartsWith("R_", StringComparison.Ordinal) && op.Length == 3)
            {
                EmitSearch(owner, inS, outS, op[2], moveLeft: false);
                return;
            }
            if (op == "S_L")
            {
                // Shift Left: L_# -> R -> loop { non-blank σ: L, Write(σ), R, R; blank #: L, Write(#) -> outS }
                var atLeftBlank = NewRawState(owner);
                EmitSearch(owner, inS, atLeftBlank, blank, moveLeft: true);

                var readSym = NewRawState(owner);
                AddPrimAll(atLeftBlank, TmAction.Right, readSym);

                var writeFinalBlank = NewRawState(owner);
                AddPrim(readSym, blank, TmAction.Left, writeFinalBlank);
                AddPrimAll(writeFinalBlank, TmAction.Write(blank), outS);

                var stepR1 = NewRawState(owner);
                var stepR2 = NewRawState(owner);
                AddPrimAll(stepR1, TmAction.Right, stepR2);
                AddPrimAll(stepR2, TmAction.Right, readSym);

                foreach (var sym in alpha)
                {
                    if (sym == blank) continue;
                    var writeSym = NewRawState(owner);
                    AddPrim(readSym, sym, TmAction.Left, writeSym);
                    AddPrimAll(writeSym, TmAction.Write(sym), stepR1);
                }
                return;
            }
            if (op == "S_R")
            {
                // Shift Right: L -> loop { non-blank σ: R, Write(σ), L, L; blank #: R, Write(#), R_# -> outS }
                var readSym = NewRawState(owner);
                AddPrimAll(inS, TmAction.Left, readSym);

                var writeVacated = NewRawState(owner);
                var afterVacated = NewRawState(owner);
                AddPrim(readSym, blank, TmAction.Right, writeVacated);
                AddPrimAll(writeVacated, TmAction.Write(blank), afterVacated);
                EmitSearch(owner, afterVacated, outS, blank, moveLeft: false);

                var stepL1 = NewRawState(owner);
                var stepL2 = NewRawState(owner);
                AddPrimAll(stepL1, TmAction.Left, stepL2);
                AddPrimAll(stepL2, TmAction.Left, readSym);

                foreach (var sym in alpha)
                {
                    if (sym == blank) continue;
                    var writeSym = NewRawState(owner);
                    AddPrim(readSym, sym, TmAction.Right, writeSym);
                    AddPrimAll(writeSym, TmAction.Write(sym), stepL1);
                }
                return;
            }
            if (op == "E")
            {
                // Erase non-blanks left to boundary '#' then step R onto cell 1
                var scanLeft = NewRawState(owner);
                foreach (var sym in alpha)
                {
                    if (sym == blank)
                    {
                        AddPrim(inS, sym, TmAction.Left, scanLeft);
                        AddPrim(scanLeft, sym, TmAction.Right, outS);
                    }
                    else
                    {
                        AddPrim(inS, sym, TmAction.Write(blank), inS);
                        AddPrim(scanLeft, sym, TmAction.Write(blank), inS);
                    }
                }
                return;
            }
            if (op == "C")
            {
                // Copy: # w [#] -> # w # w [#] using blank placeholder
                var atLeftBlank = NewRawState(owner);
                EmitSearch(owner, inS, atLeftBlank, blank, moveLeft: true);

                var readSym = NewRawState(owner);
                AddPrimAll(atLeftBlank, TmAction.Right, readSym);

                // When hitting separating '#', move right past copy to trailing '#'
                var finishStart = NewRawState(owner);
                AddEps(readSym, blank, finishStart);
                EmitSearch(owner, finishStart, outS, blank, moveLeft: false);

                foreach (var sym in alpha)
                {
                    if (sym == blank) continue;
                    var erased = NewRawState(owner);
                    var afterR1 = NewRawState(owner);
                    var afterR2 = NewRawState(owner);
                    var afterWriteCopy = NewRawState(owner);
                    var afterL1 = NewRawState(owner);
                    var afterL2 = NewRawState(owner);

                    AddPrim(readSym, sym, TmAction.Write(blank), erased);
                    EmitSearch(owner, erased, afterR1, blank, moveLeft: false);
                    EmitSearch(owner, afterR1, afterR2, blank, moveLeft: false);
                    AddPrimAll(afterR2, TmAction.Write(sym), afterWriteCopy);
                    EmitSearch(owner, afterWriteCopy, afterL1, blank, moveLeft: true);
                    EmitSearch(owner, afterL1, afterL2, blank, moveLeft: true);
                    AddPrimAll(afterL2, TmAction.Write(sym), atLeftBlank);
                }
                return;
            }
            if (op.Length == 1)
            {
                AddPrimAll(inS, TmAction.Write(op[0]), outS);
                return;
            }

            throw new ArgumentException($"Unknown Turing machine schema operation '{op}' in node '{owner}'.");
        }

        // Step 1: Compile every schema node into raw primitive/epsilon states
        var nodeEntry = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeExit = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeById = schema.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var node in schema.Nodes)
        {
            var entry = NewRawState(node.Id);
            var current = entry;
            for (var i = 0; i < node.Operations.Count; i++)
            {
                var next = NewRawState(node.Id);
                EmitOperation(node.Id, node.Operations[i], current, next);
                current = next;
            }
            nodeEntry[node.Id] = entry;
            nodeExit[node.Id] = current;
        }

        // Step 2: Connect schema edges from each nodeExit[from] -> nodeEntry[to] with deterministic precedence
        var edgesByFrom = schema.Edges
            .GroupBy(e => e.FromNodeId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var haltRawState = NewRawState("__halt__");

        foreach (var node in schema.Nodes)
        {
            var exit = nodeExit[node.Id];
            if (!edgesByFrom.TryGetValue(node.Id, out var outgoing) || outgoing.Count == 0)
            {
                AddEpsAll(exit, haltRawState, new CompletedSchemaNodeMeta(node.Id, node.Label, null));
                continue;
            }

            foreach (var sym in alpha)
            {
                var matchedEdge = ResolveMatchingEdge(outgoing, sym);
                if (matchedEdge is not null)
                {
                    var meta = new CompletedSchemaNodeMeta(node.Id, node.Label, matchedEdge.ToNodeId);
                    AddEps(exit, sym, nodeEntry[matchedEdge.ToNodeId], meta);
                }
                else
                {
                    var meta = new CompletedSchemaNodeMeta(node.Id, node.Label, null);
                    AddEps(exit, sym, haltRawState, meta);
                }
            }
        }

        // Step 3: Eliminate dispatch epsilons and build clean reachable formal quadruple TM
        (bool IsHalt, TmAction Action, int NextRaw, List<CompletedSchemaNodeMeta> Completed) Resolve(int rawState, char sym)
        {
            var cur = rawState;
            var completed = new List<CompletedSchemaNodeMeta>();
            var visited = new HashSet<int>();

            while (true)
            {
                if (cur == haltRawState)
                {
                    return (true, default, haltRawState, completed);
                }
                if (!visited.Add(cur))
                {
                    throw new InvalidOperationException($"Detected a zero-action cycle in TM schema '{schema.Name}' on symbol '{sym}'.");
                }
                if (!rawTrans[cur].TryGetValue(sym, out var edge))
                {
                    return (true, default, haltRawState, completed);
                }
                if (!edge.IsEpsilon)
                {
                    return (false, edge.Action, edge.Target, completed);
                }
                if (edge.CompletedNode is not null)
                {
                    completed.Add(edge.CompletedNode);
                }
                cur = edge.Target;
            }
        }

        // Determine which raw states halt unconditionally across every tape symbol (so incoming primitives jump straight to "h")
        var unconditionallyHalting = new Dictionary<int, List<CompletedSchemaNodeMeta>?>();
        bool IsUnconditionallyHalting(int rawState, out List<CompletedSchemaNodeMeta> haltMeta)
        {
            if (unconditionallyHalting.TryGetValue(rawState, out var cached))
            {
                haltMeta = cached ?? new List<CompletedSchemaNodeMeta>();
                return cached is not null;
            }

            List<CompletedSchemaNodeMeta>? firstMeta = null;
            foreach (var sym in alpha)
            {
                var res = Resolve(rawState, sym);
                if (!res.IsHalt)
                {
                    unconditionallyHalting[rawState] = null;
                    haltMeta = new List<CompletedSchemaNodeMeta>();
                    return false;
                }
                firstMeta ??= res.Completed;
            }

            firstMeta ??= new List<CompletedSchemaNodeMeta>();
            unconditionallyHalting[rawState] = firstMeta;
            haltMeta = firstMeta;
            return true;
        }

        var startRaw = nodeEntry[schema.StartNodeId];
        var rawToName = new Dictionary<int, string>();
        var queue = new Queue<int>();

        string RegisterRaw(int raw)
        {
            if (!rawToName.TryGetValue(raw, out var name))
            {
                name = $"q{rawToName.Count}";
                rawToName[raw] = name;
                queue.Enqueue(raw);
            }
            return name;
        }

        var startStateName = RegisterRaw(startRaw);
        var transitions = new Dictionary<(string State, char Symbol), TmTransitionResult>();
        var preCompleted = new Dictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>>();
        var postCompleted = new Dictionary<(string State, char Symbol), IReadOnlyList<CompletedSchemaNodeMeta>>();
        var stateToOwner = new Dictionary<string, string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var raw = queue.Dequeue();
            var fromName = rawToName[raw];
            stateToOwner[fromName] = rawOwner[raw];

            foreach (var sym in alpha)
            {
                var res = Resolve(raw, sym);
                var key = (fromName, sym);

                if (res.Completed.Count > 0)
                {
                    preCompleted[key] = res.Completed;
                }

                if (res.IsHalt)
                {
                    // Explicit halt transition preserving tape symbol and head position
                    transitions[key] = new TmTransitionResult(AutomataSymbols.DefaultHaltState, TmAction.Write(sym));
                }
                else if (IsUnconditionallyHalting(res.NextRaw, out var postMeta))
                {
                    transitions[key] = new TmTransitionResult(AutomataSymbols.DefaultHaltState, res.Action);
                    if (postMeta.Count > 0)
                    {
                        postCompleted[key] = postMeta;
                    }
                }
                else
                {
                    var toName = RegisterRaw(res.NextRaw);
                    transitions[key] = new TmTransitionResult(toName, res.Action);
                }
            }
        }

        var machine = new TuringMachine(
            states: rawToName.Values,
            tapeAlphabet: alpha,
            transitions: transitions,
            startState: startStateName,
            blankSymbol: blank,
            haltState: AutomataSymbols.DefaultHaltState,
            defaultHeadStart: schema.HeadStart);

        var diagramScene = BuildDiagramScene(schema, null, null, null, $"TM Schema: {schema.Name}");
        return new TmCompiledSchema(schema, machine, diagramScene, preCompleted, postCompleted, stateToOwner);
    }

    private static TmSchemaEdge? ResolveMatchingEdge(List<TmSchemaEdge> edges, char sym)
    {
        // Priority 1: Exact symbol match
        foreach (var edge in edges)
        {
            if (MatchesExact(edge.Condition, sym))
            {
                return edge;
            }
        }

        // Priority 2: Negated symbol match (~#, !a, not #)
        foreach (var edge in edges)
        {
            if (MatchesNegated(edge.Condition, sym))
            {
                return edge;
            }
        }

        // Priority 3: Fallback "other" / "else"
        foreach (var edge in edges)
        {
            var cond = edge.Condition.Trim();
            if (cond.Equals("other", StringComparison.OrdinalIgnoreCase) ||
                cond.Equals("else", StringComparison.OrdinalIgnoreCase))
            {
                return edge;
            }
        }

        // Priority 4: Wildcard "*" or unlabeled ""
        foreach (var edge in edges)
        {
            var cond = edge.Condition.Trim();
            if (cond.Length == 0 || cond == "*")
            {
                return edge;
            }
        }

        return null;
    }

    private static bool MatchesExact(string condition, char sym)
    {
        var trimmed = condition.Trim();
        if (trimmed.Length == 0 || trimmed == "*" ||
            trimmed.Equals("other", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("else", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith('~') || trimmed.StartsWith('!') ||
            trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim();
            if (p.Length == 1 && p[0] == sym)
            {
                return true;
            }
            if (p.Length == 3 && p[0] == '\'' && p[2] == '\'' && p[1] == sym)
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesNegated(string condition, char sym)
    {
        var trimmed = condition.Trim();
        if ((trimmed.StartsWith('~') || trimmed.StartsWith('!')) && trimmed.Length == 2)
        {
            return sym != trimmed[1];
        }
        if (trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 5)
        {
            return sym != trimmed[4];
        }
        return false;
    }

    public static DiagramScene BuildDiagramScene(
        TmSchema schema,
        string? activeNodeId = null,
        string? activeEdgeId = null,
        bool? verdict = null,
        string? caption = null)
    {
        var nodes = schema.Nodes.Select(n =>
        {
            var isStart = n.Id == schema.StartNodeId;
            var sem = SemanticStates.Default;
            if (n.Id == activeNodeId)
            {
                sem = verdict switch
                {
                    true => SemanticStates.Accepted,
                    false => SemanticStates.Rejected,
                    null => SemanticStates.Active
                };
            }

            var width = System.Math.Max(64, n.Label.Length * 9 + 24);
            return new GraphNodeInput(
                Id: n.Id,
                Label: n.Label,
                Shape: NodeShapes.SchemaNode,
                SemanticState: sem,
                SubLabel: n.Id,
                Badge: isStart ? ">" : null,
                IsStart: isStart,
                IsAccepting: false,
                Width: width,
                Height: 46);
        }).ToList();

        var edges = schema.Edges
            .GroupBy(e => (e.FromNodeId, e.ToNodeId))
            .Select(g =>
            {
                var id = $"{g.Key.FromNodeId}->{g.Key.ToNodeId}";
                var label = string.Join(", ", g.Select(e => string.IsNullOrWhiteSpace(e.Condition) ? "*" : e.Condition.Trim()));
                return new GraphEdgeInput(
                    Id: id,
                    SourceId: g.Key.FromNodeId,
                    TargetId: g.Key.ToNodeId,
                    Label: label == "*" ? string.Empty : label,
                    IsDirected: true,
                    SemanticState: id == activeEdgeId ? SemanticStates.Active : SemanticStates.Default);
            })
            .ToList();

        return DiagramLayout.LayoutStateGraph(nodes, edges, schema.StartNodeId, caption: caption, overlayTag: "TM Schema");
    }
}
