using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Graphs;

public sealed record DistanceRow(
    string Vertex,
    double Distance,
    string? Parent,
    bool InCloud)
{
    public string DistanceDisplay => GraphModel.FormatWeight(Distance);
}

public sealed record GraphStepState(
    DiagramScene Scene,
    GraphRepresentations Representations,
    IReadOnlyList<string> VisitOrder,
    string AuxStructureTitle,
    IReadOnlyList<string> AuxItems,
    IReadOnlyList<DistanceRow>? DistanceTable = null,
    double? TotalWeight = null,
    IReadOnlyList<IReadOnlyList<string>>? BfsLevels = null,
    IReadOnlyDictionary<string, string>? EdgeClassifications = null);

public sealed class GraphTracer
{
    private static readonly IReadOnlyList<string> BfsPseudocode =
    [
        "BFS(G, start):",
        "  mark start visited; initialize level L_0 = {start} / queue = [start]",
        "  while frontier not empty:",
        "    take vertex u; inspect neighbors v in configured order",
        "    if v unvisited: mark visited, classify (u,v) as Discovery, enqueue v",
        "    else: classify (u,v) as Cross edge"
    ];

    private static readonly IReadOnlyList<string> DfsPseudocode =
    [
        "DFS(G, start):",
        "  visit vertex u; record discovery time tin[u]",
        "  for each neighbor v of u in configured order:",
        "    if v unvisited: classify (u,v) as Discovery; recurse/push v",
        "    else if v active: classify (u,v) as Back edge",
        "    else: classify (u,v) as Forward (tin[u] < tin[v]) or Cross edge"
    ];

    private static readonly IReadOnlyList<string> DijkstraPseudocode =
    [
        "Dijkstra(G, start):",
        "  set D[start] = 0, D[v] = ∞ for all v != start; Cloud C = ∅",
        "  while unvisited reachable vertex exists:",
        "    u = vertex outside C with minimum D[u]; add u to C",
        "    for each neighbor v of u outside C:",
        "      if D[u] + w(u,v) < D[v]: D[v] = D[u] + w(u,v), Parent[v] = u"
    ];

    private static readonly IReadOnlyList<string> PrimPseudocode =
    [
        "PrimJarnik(G, start):",
        "  set Key[start] = 0, Key[v] = ∞ for all v != start; Cloud C = ∅",
        "  while unvisited reachable vertex exists:",
        "    u = vertex outside C with minimum Key[u]; add u & edge(Parent[u], u) to MST",
        "    for each neighbor v of u outside C:",
        "      if w(u,v) < Key[v]: Key[v] = w(u,v), Parent[v] = u"
    ];

    private static readonly IReadOnlyList<string> KruskalPseudocode =
    [
        "Kruskal(G):",
        "  place each vertex in its own disjoint cluster (Union-Find)",
        "  sort all edges by non-decreasing weight",
        "  for each edge (u, v) in sorted order:",
        "    if Find(u) != Find(v): ACCEPT edge, Union(u, v)",
        "    else: REJECT edge (would form cycle); stop early when |T| = n - 1"
    ];

    public GraphRuleset Ruleset { get; set; }

    public GraphTracer(GraphRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? GraphRuleset.CourseStandard;
    }

    public GraphStepState BuildSnapshot(GraphModel graph, string summary = "Graph snapshot", string? startVertex = null)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var scene = BuildScene(graph, startVertex, summary, null, null, null, null, null);
        return new GraphStepState(scene, reps, Array.Empty<string>(), "Graph", Array.Empty<string>());
    }

    public TraceTimeline<GraphStepState> TraceBfs(GraphModel graph, string startVertex)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var steps = new List<TraceStep<GraphStepState>>();
        var ops = new List<string>();

        if (!graph.ContainsVertex(startVertex))
        {
            var scene = BuildScene(graph, null, $"Start vertex '{startVertex}' not found", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Start Vertex Not Found",
                $"Vertex '{startVertex}' does not exist in the graph.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Queue", Array.Empty<string>()),
                Array.Empty<string>(), Array.Empty<string>(), "ERROR", "Error"));
            return new TraceTimeline<GraphStepState>(steps, BfsPseudocode, ops, $"Vertex '{startVertex}' not found.", isSuccess: false);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visitOrder = new List<string>();
        var nodeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeDashed = new HashSet<string>(StringComparer.Ordinal);
        var edgeClassifications = new Dictionary<string, string>(StringComparer.Ordinal);
        var levels = new List<List<string>>();
        var auxTitle = Ruleset.BfsMode == BfsMode.LevelByLevel ? "BFS Levels (L_i)" : "BFS Queue";

        List<string> BuildLevelAux() =>
            levels.Select((lvl, i) => $"L_{i} = {{{string.Join(", ", lvl)}}}").ToList();

        void RunComponentBfs(string seed)
        {
            if (Ruleset.BfsMode == BfsMode.LevelByLevel)
            {
                var currentLevel = new List<string> { seed };
                visited.Add(seed);
                visitOrder.Add(seed);
                nodeStates[seed] = SemanticStates.Active;
                var levelIndex = levels.Count;
                levels.Add(new List<string> { seed });
                ops.Add($"BFS_LEVEL L_{levelIndex}: {seed}");

                steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                    $"Initialize L_{levelIndex} = {{{seed}}}",
                    $"Starting level-by-level BFS from {seed}.",
                    1, new[] { seed }, Array.Empty<string>(), "BFS_START", $"L_{levelIndex}",
                    visitOrder, auxTitle, BuildLevelAux(), nodeStates, edgeStates, edgeDashed,
                    null, null, null, null, levels, edgeClassifications));

                nodeStates[seed] = SemanticStates.Visited;

                while (currentLevel.Count > 0)
                {
                    var nextLevel = new List<string>();
                    foreach (var u in currentLevel)
                    {
                        nodeStates[u] = SemanticStates.Active;
                        foreach (var (v, edge) in graph.GetOutgoingEdges(u, Ruleset.NeighborOrder))
                        {
                            if (!visited.Contains(v))
                            {
                                visited.Add(v);
                                visitOrder.Add(v);
                                nextLevel.Add(v);
                                nodeStates[v] = SemanticStates.Frontier;
                                edgeStates[edge.Id] = SemanticStates.Accepted;
                                edgeClassifications[edge.Id] = "Discovery";
                                ops.Add($"DISCOVERY {u}->{v}");

                                var previewLevels = levels.Select(l => (IReadOnlyList<string>)l.ToList()).ToList();
                                previewLevels.Add(nextLevel.ToList());
                                var auxPreview = previewLevels.Select((lvl, i) => $"L_{i} = {{{string.Join(", ", lvl)}}}").ToList();

                                steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                                    $"Discover {v} from {u}",
                                    $"Edge ({u}, {v}) is a Discovery edge; added {v} to L_{levels.Count}.",
                                    4, new[] { u, v }, new[] { edge.Id }, "DISCOVERY", "Discovery",
                                    visitOrder, auxTitle, auxPreview, nodeStates, edgeStates, edgeDashed,
                                    null, null, null, null, previewLevels, edgeClassifications));
                            }
                            else if (!edgeClassifications.ContainsKey(edge.Id))
                            {
                                edgeClassifications[edge.Id] = "Cross";
                                if (Ruleset.ShowEdgeClassification)
                                {
                                    edgeStates[edge.Id] = SemanticStates.Rejected;
                                    edgeDashed.Add(edge.Id);
                                }
                                ops.Add($"CROSS {u}->{v}");

                                steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                                    $"Cross Edge ({u}, {v})",
                                    $"Vertex {v} was already visited; classified ({u}, {v}) as a Cross edge.",
                                    5, new[] { u, v }, new[] { edge.Id }, "CROSS", "Cross",
                                    visitOrder, auxTitle, BuildLevelAux(), nodeStates, edgeStates, edgeDashed,
                                    null, null, null, null, levels, edgeClassifications));
                            }
                        }
                        nodeStates[u] = SemanticStates.Visited;
                    }

                    if (nextLevel.Count > 0)
                    {
                        levels.Add(nextLevel);
                        foreach (var v in nextLevel) nodeStates[v] = SemanticStates.Visited;
                        ops.Add($"BFS_LEVEL L_{levels.Count - 1}: {string.Join(", ", nextLevel)}");
                    }

                    currentLevel = nextLevel;
                }
            }
            else
            {
                var queue = new Queue<string>();
                var depthOf = new Dictionary<string, int>(StringComparer.Ordinal) { [seed] = 0 };
                var baseLevelOffset = levels.Count;

                void EnsureLevel(int idx)
                {
                    while (levels.Count <= idx) levels.Add(new List<string>());
                }

                visited.Add(seed);
                queue.Enqueue(seed);
                EnsureLevel(baseLevelOffset);
                levels[baseLevelOffset].Add(seed);
                nodeStates[seed] = SemanticStates.Frontier;
                ops.Add($"ENQUEUE {seed}");

                steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                    $"Enqueue Start {seed}",
                    $"Marked {seed} discovered and enqueued it into BFS queue.",
                    1, new[] { seed }, Array.Empty<string>(), "ENQUEUE", "Start",
                    visitOrder, auxTitle, queue.ToList(), nodeStates, edgeStates, edgeDashed,
                    null, null, null, null, levels, edgeClassifications));

                while (queue.Count > 0)
                {
                    var u = queue.Dequeue();
                    visitOrder.Add(u);
                    nodeStates[u] = SemanticStates.Active;
                    ops.Add($"VISIT {u}");

                    steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                        $"Dequeue & Visit {u}",
                        $"Dequeued {u} (level L_{depthOf[u]}). Inspecting outgoing neighbors.",
                        3, new[] { u }, Array.Empty<string>(), "VISIT", $"L_{depthOf[u]}",
                        visitOrder, auxTitle, queue.ToList(), nodeStates, edgeStates, edgeDashed,
                        null, null, null, null, levels, edgeClassifications));

                    foreach (var (v, edge) in graph.GetOutgoingEdges(u, Ruleset.NeighborOrder))
                    {
                        if (!visited.Contains(v))
                        {
                            visited.Add(v);
                            depthOf[v] = depthOf[u] + 1;
                            EnsureLevel(baseLevelOffset + depthOf[v]);
                            levels[baseLevelOffset + depthOf[v]].Add(v);
                            queue.Enqueue(v);

                            nodeStates[v] = SemanticStates.Frontier;
                            edgeStates[edge.Id] = SemanticStates.Accepted;
                            edgeClassifications[edge.Id] = "Discovery";
                            ops.Add($"DISCOVERY {u}->{v}");

                            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                                $"Discover {v} via ({u}, {v})",
                                $"Edge ({u}, {v}) is a Discovery edge; enqueued {v} at level L_{depthOf[v]}.",
                                4, new[] { u, v }, new[] { edge.Id }, "DISCOVERY", "Discovery",
                                visitOrder, auxTitle, queue.ToList(), nodeStates, edgeStates, edgeDashed,
                                null, null, null, null, levels, edgeClassifications));
                        }
                        else if (!edgeClassifications.ContainsKey(edge.Id))
                        {
                            edgeClassifications[edge.Id] = "Cross";
                            if (Ruleset.ShowEdgeClassification)
                            {
                                edgeStates[edge.Id] = SemanticStates.Rejected;
                                edgeDashed.Add(edge.Id);
                            }
                            ops.Add($"CROSS {u}->{v}");

                            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                                $"Cross Edge ({u}, {v})",
                                $"Vertex {v} was already discovered; classified ({u}, {v}) as a Cross edge.",
                                5, new[] { u, v }, new[] { edge.Id }, "CROSS", "Cross",
                                visitOrder, auxTitle, queue.ToList(), nodeStates, edgeStates, edgeDashed,
                                null, null, null, null, levels, edgeClassifications));
                        }
                    }

                    nodeStates[u] = SemanticStates.Visited;
                }
            }
        }

        RunComponentBfs(startVertex);

        if (Ruleset.TraverseDisconnected)
        {
            foreach (var v in graph.GetOrderedVertexIds(Ruleset.NeighborOrder))
            {
                if (!visited.Contains(v))
                {
                    RunComponentBfs(v);
                }
            }
        }

        var summary = $"BFS from {startVertex} visited {visitOrder.Count}/{graph.Vertices.Count} vertex(es): [{string.Join(", ", visitOrder)}].";
        return new TraceTimeline<GraphStepState>(steps, BfsPseudocode, ops, summary, isSuccess: true);
    }

    public TraceTimeline<GraphStepState> TraceDfs(GraphModel graph, string startVertex)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var steps = new List<TraceStep<GraphStepState>>();
        var ops = new List<string>();

        if (!graph.ContainsVertex(startVertex))
        {
            var scene = BuildScene(graph, null, $"Start vertex '{startVertex}' not found", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Start Vertex Not Found",
                $"Vertex '{startVertex}' does not exist in the graph.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Stack", Array.Empty<string>()),
                Array.Empty<string>(), Array.Empty<string>(), "ERROR", "Error"));
            return new TraceTimeline<GraphStepState>(steps, DfsPseudocode, ops, $"Vertex '{startVertex}' not found.", isSuccess: false);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var activeStack = new HashSet<string>(StringComparer.Ordinal);
        var callStackDisplay = new List<string>();
        var tin = new Dictionary<string, int>(StringComparer.Ordinal);
        var tout = new Dictionary<string, int>(StringComparer.Ordinal);
        var timer = 0;

        var visitOrder = new List<string>();
        var nodeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeDashed = new HashSet<string>(StringComparer.Ordinal);
        var edgeClassifications = new Dictionary<string, string>(StringComparer.Ordinal);
        var auxTitle = Ruleset.DfsMode == DfsMode.Recursive ? "Recursion Call Stack" : "Explicit DFS Stack";

        void ClassifyNonTreeEdge(string u, string v, GraphEdge edge)
        {
            if (edgeClassifications.ContainsKey(edge.Id)) return;

            string classification;
            string edgeSemantic;
            int line;

            if (!graph.IsDirected)
            {
                classification = "Back";
                edgeSemantic = SemanticStates.Violation;
                line = 4;
            }
            else if (activeStack.Contains(v))
            {
                classification = "Back";
                edgeSemantic = SemanticStates.Violation;
                line = 4;
            }
            else if (tin.TryGetValue(u, out var tu) && tin.TryGetValue(v, out var tv) && tu < tv)
            {
                classification = "Forward";
                edgeSemantic = SemanticStates.Frontier;
                line = 5;
            }
            else
            {
                classification = "Cross";
                edgeSemantic = SemanticStates.Rejected;
                line = 5;
            }

            edgeClassifications[edge.Id] = classification;
            if (Ruleset.ShowEdgeClassification)
            {
                edgeStates[edge.Id] = edgeSemantic;
                edgeDashed.Add(edge.Id);
            }
            ops.Add($"{classification.ToUpperInvariant()} {u}->{v}");

            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                $"{classification} Edge ({u}, {v})",
                $"Classified ({u}, {v}) as a {classification} edge.",
                line, new[] { u, v }, new[] { edge.Id }, classification.ToUpperInvariant(), classification,
                visitOrder, auxTitle, callStackDisplay.ToList(), nodeStates, edgeStates, edgeDashed,
                null, null, null, null, null, edgeClassifications));
        }

        void DfsRecursive(string u)
        {
            visited.Add(u);
            activeStack.Add(u);
            callStackDisplay.Add(u);
            tin[u] = ++timer;
            visitOrder.Add(u);
            nodeStates[u] = SemanticStates.Active;
            ops.Add($"VISIT {u}");

            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                $"Visit {u} (tin={tin[u]})",
                $"Entered {u} via recursive DFS.",
                1, new[] { u }, Array.Empty<string>(), "VISIT", $"t={tin[u]}",
                visitOrder, auxTitle, callStackDisplay.ToList(), nodeStates, edgeStates, edgeDashed,
                null, null, null, null, null, edgeClassifications));

            foreach (var (v, edge) in graph.GetOutgoingEdges(u, Ruleset.NeighborOrder))
            {
                if (!visited.Contains(v))
                {
                    edgeClassifications[edge.Id] = "Discovery";
                    edgeStates[edge.Id] = SemanticStates.Accepted;
                    ops.Add($"DISCOVERY {u}->{v}");

                    steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                        $"Discovery Edge ({u}, {v})",
                        $"Following Discovery edge ({u}, {v}) to unvisited vertex {v}.",
                        3, new[] { u, v }, new[] { edge.Id }, "DISCOVERY", "Discovery",
                        visitOrder, auxTitle, callStackDisplay.ToList(), nodeStates, edgeStates, edgeDashed,
                        null, null, null, null, null, edgeClassifications));

                    nodeStates[u] = SemanticStates.Frontier;
                    DfsRecursive(v);
                    nodeStates[u] = SemanticStates.Active;
                }
                else
                {
                    ClassifyNonTreeEdge(u, v, edge);
                }
            }

            activeStack.Remove(u);
            callStackDisplay.RemoveAt(callStackDisplay.Count - 1);
            tout[u] = ++timer;
            nodeStates[u] = SemanticStates.Visited;
            ops.Add($"FINISH {u}");

            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                $"Finish {u} (tout={tout[u]})",
                $"Completed exploration of {u}; backtracking.",
                1, new[] { u }, Array.Empty<string>(), "FINISH", $"f={tout[u]}",
                visitOrder, auxTitle, callStackDisplay.ToList(), nodeStates, edgeStates, edgeDashed,
                null, null, null, null, null, edgeClassifications));
        }

        void DfsIterative(string seed)
        {
            var stack = new Stack<(string Vertex, GraphEdge? IncomingEdge, string? Parent)>();
            stack.Push((seed, null, null));

            while (stack.Count > 0)
            {
                var (u, incoming, parent) = stack.Pop();
                if (visited.Contains(u))
                {
                    if (incoming is not null && parent is not null)
                    {
                        ClassifyNonTreeEdge(parent, u, incoming);
                    }
                    continue;
                }

                visited.Add(u);
                tin[u] = ++timer;
                visitOrder.Add(u);
                nodeStates[u] = SemanticStates.Active;

                if (incoming is not null && parent is not null)
                {
                    edgeClassifications[incoming.Id] = "Discovery";
                    edgeStates[incoming.Id] = SemanticStates.Accepted;
                    ops.Add($"DISCOVERY {parent}->{u}");
                }
                ops.Add($"VISIT {u}");

                var neighbors = graph.GetOutgoingEdges(u, Ruleset.NeighborOrder);
                for (var i = neighbors.Count - 1; i >= 0; i--)
                {
                    var (v, edge) = neighbors[i];
                    if (!visited.Contains(v))
                    {
                        stack.Push((v, edge, u));
                    }
                    else if (!edgeClassifications.ContainsKey(edge.Id))
                    {
                        // Classify immediately as Back/Cross/Forward relative to discovered nodes
                        ClassifyNonTreeEdge(u, v, edge);
                    }
                }

                callStackDisplay.Clear();
                callStackDisplay.AddRange(stack.Select(s => s.Vertex));

                steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                    $"Pop & Visit {u}",
                    $"Popped {u} from stack and pushed unvisited neighbors.",
                    1, new[] { u }, incoming is not null ? new[] { incoming.Id } : Array.Empty<string>(),
                    "VISIT", $"#{visitOrder.Count}",
                    visitOrder, auxTitle, callStackDisplay.ToList(), nodeStates, edgeStates, edgeDashed,
                    null, null, null, null, null, edgeClassifications));

                nodeStates[u] = SemanticStates.Visited;
            }
        }

        void RunFrom(string seed)
        {
            if (Ruleset.DfsMode == DfsMode.Recursive) DfsRecursive(seed);
            else DfsIterative(seed);
        }

        RunFrom(startVertex);

        if (Ruleset.TraverseDisconnected)
        {
            foreach (var v in graph.GetOrderedVertexIds(Ruleset.NeighborOrder))
            {
                if (!visited.Contains(v))
                {
                    RunFrom(v);
                }
            }
        }

        var summary = $"DFS ({Ruleset.DfsMode}) from {startVertex} visited {visitOrder.Count}/{graph.Vertices.Count} vertex(es): [{string.Join(", ", visitOrder)}].";
        return new TraceTimeline<GraphStepState>(steps, DfsPseudocode, ops, summary, isSuccess: true);
    }

    public TraceTimeline<GraphStepState> TraceDijkstra(GraphModel graph, string startVertex)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var steps = new List<TraceStep<GraphStepState>>();
        var ops = new List<string>();

        if (!graph.ContainsVertex(startVertex))
        {
            var scene = BuildScene(graph, null, $"Start vertex '{startVertex}' not found", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Start Vertex Not Found",
                $"Vertex '{startVertex}' does not exist in the graph.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Cloud", Array.Empty<string>()),
                Array.Empty<string>(), Array.Empty<string>(), "ERROR", "Error"));
            return new TraceTimeline<GraphStepState>(steps, DijkstraPseudocode, ops, $"Vertex '{startVertex}' not found.", isSuccess: false);
        }

        if (graph.Edges.Any(e => e.Weight < 0))
        {
            var scene = BuildScene(graph, startVertex, "Negative edge weight detected", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Negative Weight Unsupported",
                "Dijkstra's algorithm requires all edge weights to be non-negative.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Cloud", Array.Empty<string>()),
                Array.Empty<string>(), Array.Empty<string>(), "ERROR", "Negative Weight"));
            return new TraceTimeline<GraphStepState>(steps, DijkstraPseudocode, ops, "Dijkstra requires non-negative edge weights.", isSuccess: false);
        }

        var orderedVertices = graph.GetOrderedVertexIds(Ruleset.NeighborOrder);
        var dist = orderedVertices.ToDictionary(v => v, _ => double.PositiveInfinity, StringComparer.Ordinal);
        var parent = orderedVertices.ToDictionary(v => v, _ => (string?)null, StringComparer.Ordinal);
        var parentEdgeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var inCloud = new HashSet<string>(StringComparer.Ordinal);
        var visitOrder = new List<string>();

        var nodeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeDashed = new HashSet<string>(StringComparer.Ordinal);
        var subLabels = orderedVertices.ToDictionary(v => v, _ => "D=∞", StringComparer.Ordinal);

        dist[startVertex] = 0;
        subLabels[startVertex] = "D=0";
        nodeStates[startVertex] = SemanticStates.Frontier;

        IReadOnlyList<DistanceRow> BuildTable() =>
            orderedVertices.Select(v => new DistanceRow(v, dist[v], parent[v], inCloud.Contains(v))).ToList();

        List<string> BuildPriorityQueueDisplay() =>
            orderedVertices
                .Where(v => !inCloud.Contains(v))
                .OrderBy(v => dist[v])
                .ThenBy(v => orderedVertices.ToList().IndexOf(v))
                .Select(v => $"{v}: {GraphModel.FormatWeight(dist[v])}")
                .ToList();

        ops.Add($"INIT {startVertex}=0");
        steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
            $"Initialize Dijkstra from {startVertex}",
            $"Set D[{startVertex}] = 0 and D[v] = ∞ for all other vertices.",
            1, new[] { startVertex }, Array.Empty<string>(), "INIT", "D=0",
            visitOrder, "Priority Queue (D[v])", BuildPriorityQueueDisplay(),
            nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), null, null, null));

        while (true)
        {
            string? u = null;
            var bestDist = double.PositiveInfinity;
            foreach (var candidate in orderedVertices)
            {
                if (inCloud.Contains(candidate)) continue;
                if (dist[candidate] < bestDist)
                {
                    bestDist = dist[candidate];
                    u = candidate;
                }
            }

            if (u is null || double.IsPositiveInfinity(bestDist))
            {
                break;
            }

            inCloud.Add(u);
            visitOrder.Add(u);
            nodeStates[u] = SemanticStates.Accepted;

            var acceptedEdgeIds = new List<string>();
            if (parentEdgeId.TryGetValue(u, out var treeEdgeId))
            {
                edgeStates[treeEdgeId] = SemanticStates.Accepted;
                edgeDashed.Remove(treeEdgeId);
                acceptedEdgeIds.Add(treeEdgeId);
            }

            ops.Add($"SETTLE {u} ({GraphModel.FormatWeight(dist[u])})");
            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                $"Pull {u} into Cloud (D={GraphModel.FormatWeight(dist[u])})",
                $"Vertex {u} has minimum tentative distance {GraphModel.FormatWeight(dist[u])}; added to cloud C.",
                3, new[] { u }, acceptedEdgeIds, "SETTLE", $"D={GraphModel.FormatWeight(dist[u])}",
                visitOrder, "Priority Queue (D[v])", BuildPriorityQueueDisplay(),
                nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), null, null, null));

            foreach (var (v, edge) in graph.GetOutgoingEdges(u, Ruleset.NeighborOrder))
            {
                if (inCloud.Contains(v)) continue;

                var candidateDist = dist[u] + edge.Weight;
                if (candidateDist < dist[v])
                {
                    if (parentEdgeId.TryGetValue(v, out var oldEdgeId) && oldEdgeId != edge.Id)
                    {
                        edgeStates[oldEdgeId] = SemanticStates.Rejected;
                        edgeDashed.Add(oldEdgeId);
                    }

                    dist[v] = candidateDist;
                    parent[v] = u;
                    parentEdgeId[v] = edge.Id;
                    subLabels[v] = $"D={GraphModel.FormatWeight(candidateDist)}";
                    nodeStates[v] = SemanticStates.Frontier;
                    edgeStates[edge.Id] = SemanticStates.Frontier;
                    edgeDashed.Remove(edge.Id);

                    ops.Add($"RELAX {u}->{v} = {GraphModel.FormatWeight(candidateDist)}");
                    steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                        $"Relax ({u}, {v}) -> D[{v}] = {GraphModel.FormatWeight(candidateDist)}",
                        $"Improved D[{v}] to {GraphModel.FormatWeight(dist[u])} + {GraphModel.FormatWeight(edge.Weight)} = {GraphModel.FormatWeight(candidateDist)} via {u}.",
                        5, new[] { u, v }, new[] { edge.Id }, "RELAX", $"D={GraphModel.FormatWeight(candidateDist)}",
                        visitOrder, "Priority Queue (D[v])", BuildPriorityQueueDisplay(),
                        nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), null, null, null));
                }
                else
                {
                    if (!edgeStates.TryGetValue(edge.Id, out var existingState) ||
                        (existingState != SemanticStates.Accepted && existingState != SemanticStates.Frontier))
                    {
                        edgeStates[edge.Id] = SemanticStates.Rejected;
                        edgeDashed.Add(edge.Id);
                    }

                    ops.Add($"NO_RELAX {u}->{v}");
                    steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                        $"Keep D[{v}] = {GraphModel.FormatWeight(dist[v])} (Reject {u}->{v})",
                        $"Path via {u} costs {GraphModel.FormatWeight(candidateDist)} >= current D[{v}] = {GraphModel.FormatWeight(dist[v])}.",
                        5, new[] { u, v }, new[] { edge.Id }, "REJECT", "No Improvement",
                        visitOrder, "Priority Queue (D[v])", BuildPriorityQueueDisplay(),
                        nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), null, null, null));
                }
            }
        }

        var summary = $"Dijkstra from {startVertex} settled {inCloud.Count}/{orderedVertices.Count} vertex(es).";
        return new TraceTimeline<GraphStepState>(steps, DijkstraPseudocode, ops, summary, isSuccess: true);
    }

    public TraceTimeline<GraphStepState> TracePrimJarnik(GraphModel graph, string startVertex)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var steps = new List<TraceStep<GraphStepState>>();
        var ops = new List<string>();

        if (!graph.ContainsVertex(startVertex))
        {
            var scene = BuildScene(graph, null, $"Start vertex '{startVertex}' not found", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Start Vertex Not Found",
                $"Vertex '{startVertex}' does not exist in the graph.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Frontier", Array.Empty<string>()),
                Array.Empty<string>(), Array.Empty<string>(), "ERROR", "Error"));
            return new TraceTimeline<GraphStepState>(steps, PrimPseudocode, ops, $"Vertex '{startVertex}' not found.", isSuccess: false);
        }

        var orderedVertices = graph.GetOrderedVertexIds(Ruleset.NeighborOrder);
        var key = orderedVertices.ToDictionary(v => v, _ => double.PositiveInfinity, StringComparer.Ordinal);
        var parent = orderedVertices.ToDictionary(v => v, _ => (string?)null, StringComparer.Ordinal);
        var parentEdgeId = new Dictionary<string, string>(StringComparer.Ordinal);
        var inCloud = new HashSet<string>(StringComparer.Ordinal);
        var visitOrder = new List<string>();
        double totalWeight = 0;

        var nodeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeDashed = new HashSet<string>(StringComparer.Ordinal);
        var subLabels = orderedVertices.ToDictionary(v => v, _ => "k=∞", StringComparer.Ordinal);

        key[startVertex] = 0;
        subLabels[startVertex] = "k=0";
        nodeStates[startVertex] = SemanticStates.Frontier;

        IReadOnlyList<DistanceRow> BuildTable() =>
            orderedVertices.Select(v => new DistanceRow(v, key[v], parent[v], inCloud.Contains(v))).ToList();

        List<string> BuildFrontierDisplay() =>
            orderedVertices
                .Where(v => !inCloud.Contains(v) && !double.IsPositiveInfinity(key[v]))
                .OrderBy(v => key[v])
                .ThenBy(v => v, StringComparer.Ordinal)
                .Select(v => $"{parent[v] ?? "·"}-{v} ({GraphModel.FormatWeight(key[v])})")
                .ToList();

        ops.Add($"PRIM_START {startVertex}");
        steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
            $"Initialize Prim-Jarník at {startVertex}",
            $"Starting MST cloud growth at {startVertex}. Total weight = 0.",
            1, new[] { startVertex }, Array.Empty<string>(), "INIT", "Start",
            visitOrder, "Frontier Edges", BuildFrontierDisplay(),
            nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), totalWeight, null, null));

        while (true)
        {
            string? u = null;
            var bestKey = double.PositiveInfinity;
            foreach (var candidate in orderedVertices)
            {
                if (inCloud.Contains(candidate)) continue;
                if (key[candidate] < bestKey)
                {
                    bestKey = key[candidate];
                    u = candidate;
                }
            }

            if (u is null || double.IsPositiveInfinity(bestKey))
            {
                break;
            }

            inCloud.Add(u);
            visitOrder.Add(u);
            nodeStates[u] = SemanticStates.Accepted;

            var acceptedEdgeIds = new List<string>();
            if (parentEdgeId.TryGetValue(u, out var mstEdgeId))
            {
                edgeStates[mstEdgeId] = SemanticStates.Accepted;
                edgeDashed.Remove(mstEdgeId);
                acceptedEdgeIds.Add(mstEdgeId);
                totalWeight += bestKey;
                ops.Add($"ADD_EDGE {parent[u]}-{u} ({GraphModel.FormatWeight(bestKey)})");
            }
            else
            {
                ops.Add($"ADD_ROOT {u}");
            }

            steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                parent[u] is not null
                    ? $"Add {u} via ({parent[u]}, {u}) w={GraphModel.FormatWeight(bestKey)}"
                    : $"Add Start Vertex {u} to Cloud",
                parent[u] is not null
                    ? $"Added minimum frontier edge ({parent[u]}, {u}) of weight {GraphModel.FormatWeight(bestKey)}. MST total = {GraphModel.FormatWeight(totalWeight)}."
                    : $"Added {u} to MST cloud.",
                3, new[] { u }, acceptedEdgeIds, "ACCEPT", $"W={GraphModel.FormatWeight(totalWeight)}",
                visitOrder, "Frontier Edges", BuildFrontierDisplay(),
                nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), totalWeight, null, null));

            foreach (var (v, edge) in graph.GetOutgoingEdges(u, Ruleset.NeighborOrder))
            {
                if (inCloud.Contains(v)) continue;

                if (edge.Weight < key[v])
                {
                    if (parentEdgeId.TryGetValue(v, out var prevId) && prevId != edge.Id)
                    {
                        edgeStates[prevId] = SemanticStates.Rejected;
                        edgeDashed.Add(prevId);
                    }

                    key[v] = edge.Weight;
                    parent[v] = u;
                    parentEdgeId[v] = edge.Id;
                    subLabels[v] = $"k={GraphModel.FormatWeight(edge.Weight)}";
                    nodeStates[v] = SemanticStates.Frontier;
                    edgeStates[edge.Id] = SemanticStates.Frontier;
                    edgeDashed.Remove(edge.Id);

                    ops.Add($"FRONTIER {u}-{v} ({GraphModel.FormatWeight(edge.Weight)})");
                    steps.Add(CreateGraphStep(steps.Count, graph, reps, startVertex,
                        $"Update Frontier Key[{v}] = {GraphModel.FormatWeight(edge.Weight)}",
                        $"Edge ({u}, {v}) improves cheapest connection to {v} to {GraphModel.FormatWeight(edge.Weight)}.",
                        5, new[] { u, v }, new[] { edge.Id }, "UPDATE_KEY", $"k={GraphModel.FormatWeight(edge.Weight)}",
                        visitOrder, "Frontier Edges", BuildFrontierDisplay(),
                        nodeStates, edgeStates, edgeDashed, subLabels, null, BuildTable(), totalWeight, null, null));
                }
            }
        }

        var summary = $"Prim-Jarník MST from {startVertex}: {inCloud.Count} vertices, total weight = {GraphModel.FormatWeight(totalWeight)}.";
        return new TraceTimeline<GraphStepState>(steps, PrimPseudocode, ops, summary, isSuccess: true);
    }

    public TraceTimeline<GraphStepState> TraceKruskal(GraphModel graph)
    {
        var reps = GraphRepresentationBuilder.Build(graph, Ruleset.NeighborOrder);
        var steps = new List<TraceStep<GraphStepState>>();
        var ops = new List<string>();

        var orderedVertices = graph.GetOrderedVertexIds(Ruleset.NeighborOrder);
        if (orderedVertices.Count == 0)
        {
            var scene = BuildScene(graph, null, "Empty graph", null, null, null, null, null);
            steps.Add(new TraceStep<GraphStepState>(0, "Empty Graph", "Graph has no vertices.", 0,
                new GraphStepState(scene, reps, Array.Empty<string>(), "Clusters", Array.Empty<string>(), TotalWeight: 0),
                Array.Empty<string>(), Array.Empty<string>(), "EMPTY", "Empty"));
            return new TraceTimeline<GraphStepState>(steps, KruskalPseudocode, ops, "Empty graph; MST weight = 0.", isSuccess: true);
        }

        // Union-Find structure
        var ufParent = orderedVertices.ToDictionary(v => v, v => v, StringComparer.Ordinal);
        string Find(string x)
        {
            while (ufParent[x] != x)
            {
                ufParent[x] = ufParent[ufParent[x]];
                x = ufParent[x];
            }
            return x;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb) return;
            if (string.CompareOrdinal(ra, rb) <= 0) ufParent[rb] = ra;
            else ufParent[ra] = rb;
        }

        List<string> BuildClustersDisplay() =>
            orderedVertices
                .GroupBy(Find)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{{{string.Join(", ", g.OrderBy(x => x, StringComparer.Ordinal))}}}")
                .ToList();

        static (string A, string B) CanonicalEndpoints(GraphEdge e) =>
            string.CompareOrdinal(e.From, e.To) <= 0 ? (e.From, e.To) : (e.To, e.From);

        var sortedEdges = graph.Edges
            .OrderBy(e => e.Weight)
            .ThenBy(e => CanonicalEndpoints(e).A, StringComparer.Ordinal)
            .ThenBy(e => CanonicalEndpoints(e).B, StringComparer.Ordinal)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        var nodeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var edgeDashed = new HashSet<string>(StringComparer.Ordinal);
        var acceptedEdgeLabels = new List<string>();
        double totalWeight = 0;
        var acceptedCount = 0;
        var examinedCount = 0;
        var targetEdgeCount = orderedVertices.Count - 1;

        ops.Add($"KRUSKAL_INIT {orderedVertices.Count} vertices, {sortedEdges.Count} edges");
        steps.Add(CreateGraphStep(steps.Count, graph, reps, null,
            $"Initialize {orderedVertices.Count} Singleton Clusters",
            $"Sorted {sortedEdges.Count} edge(s) by weight. Need {targetEdgeCount} edge(s) for a spanning tree.",
            1, Array.Empty<string>(), Array.Empty<string>(), "INIT", $"0/{targetEdgeCount}",
            acceptedEdgeLabels, "Clusters (Union-Find)", BuildClustersDisplay(),
            nodeStates, edgeStates, edgeDashed, null, null, null, totalWeight, null, null));

        foreach (var edge in sortedEdges)
        {
            examinedCount++;
            var ru = Find(edge.From);
            var rv = Find(edge.To);

            if (ru != rv)
            {
                Union(ru, rv);
                acceptedCount++;
                totalWeight += edge.Weight;
                nodeStates[edge.From] = SemanticStates.Accepted;
                nodeStates[edge.To] = SemanticStates.Accepted;
                edgeStates[edge.Id] = SemanticStates.Accepted;
                var edgeLabel = $"{edge.From}-{edge.To} ({GraphModel.FormatWeight(edge.Weight)})";
                acceptedEdgeLabels.Add(edgeLabel);
                ops.Add($"ACCEPT #{examinedCount} {edge.From}-{edge.To} ({GraphModel.FormatWeight(edge.Weight)})");

                steps.Add(CreateGraphStep(steps.Count, graph, reps, null,
                    $"Accept Edge #{examinedCount}: {edgeLabel}",
                    $"Endpoints {edge.From} and {edge.To} belong to different clusters; merged clusters. Accepted {acceptedCount}/{targetEdgeCount}, weight = {GraphModel.FormatWeight(totalWeight)}.",
                    4, new[] { edge.From, edge.To }, new[] { edge.Id }, "ACCEPT", $"W={GraphModel.FormatWeight(totalWeight)}",
                    acceptedEdgeLabels, "Clusters (Union-Find)", BuildClustersDisplay(),
                    nodeStates, edgeStates, edgeDashed, null, null, null, totalWeight, null, null));

                if (acceptedCount == targetEdgeCount)
                {
                    var skipped = sortedEdges.Count - examinedCount;
                    ops.Add($"EARLY_STOP examined={examinedCount} skipped={skipped} weight={GraphModel.FormatWeight(totalWeight)}");
                    steps.Add(CreateGraphStep(steps.Count, graph, reps, null,
                        $"Early Stop at {acceptedCount} Edges (After Edge #{examinedCount})",
                        $"Reached n - 1 = {targetEdgeCount} MST edges after examining {examinedCount} edge(s); skipped remaining {skipped} edge(s). Total MST weight = {GraphModel.FormatWeight(totalWeight)}.",
                        5, Array.Empty<string>(), Array.Empty<string>(), "EARLY_STOP", $"W={GraphModel.FormatWeight(totalWeight)}",
                        acceptedEdgeLabels, "Clusters (Union-Find)", BuildClustersDisplay(),
                        nodeStates, edgeStates, edgeDashed, null, null, null, totalWeight, null, null));
                    break;
                }
            }
            else
            {
                edgeStates[edge.Id] = SemanticStates.Rejected;
                edgeDashed.Add(edge.Id);
                ops.Add($"REJECT #{examinedCount} {edge.From}-{edge.To} ({GraphModel.FormatWeight(edge.Weight)})");

                steps.Add(CreateGraphStep(steps.Count, graph, reps, null,
                    $"Reject Edge #{examinedCount}: {edge.From}-{edge.To} (Cycle)",
                    $"Both {edge.From} and {edge.To} are already in cluster '{ru}'; adding ({edge.From}, {edge.To}) would form a cycle.",
                    5, new[] { edge.From, edge.To }, new[] { edge.Id }, "REJECT", "Cycle",
                    acceptedEdgeLabels, "Clusters (Union-Find)", BuildClustersDisplay(),
                    nodeStates, edgeStates, edgeDashed, null, null, null, totalWeight, null, null));
            }
        }

        var summary = $"Kruskal MST: {acceptedCount} edges (examined {examinedCount}/{sortedEdges.Count}), total weight = {GraphModel.FormatWeight(totalWeight)}.";
        return new TraceTimeline<GraphStepState>(steps, KruskalPseudocode, ops, summary, isSuccess: true);
    }

    private TraceStep<GraphStepState> CreateGraphStep(
        int stepIndex,
        GraphModel graph,
        GraphRepresentations reps,
        string? startVertex,
        string title,
        string explanation,
        int? activeLine,
        IReadOnlyList<string> highlightedNodes,
        IReadOnlyList<string> highlightedEdges,
        string? opCode,
        string? badge,
        IReadOnlyList<string> visitOrder,
        string auxTitle,
        IReadOnlyList<string> auxItems,
        IReadOnlyDictionary<string, string> nodeStates,
        IReadOnlyDictionary<string, string> edgeStates,
        IReadOnlySet<string> edgeDashed,
        IReadOnlyDictionary<string, string>? subLabels,
        IReadOnlyDictionary<string, string>? nodeBadges,
        IReadOnlyList<DistanceRow>? distanceTable,
        double? totalWeight,
        IReadOnlyList<IReadOnlyList<string>>? bfsLevels,
        IReadOnlyDictionary<string, string>? edgeClassifications)
    {
        var scene = BuildScene(graph, startVertex, title, nodeStates, edgeStates, edgeDashed, subLabels, nodeBadges);
        var state = new GraphStepState(
            Scene: scene,
            Representations: reps,
            VisitOrder: visitOrder.ToList(),
            AuxStructureTitle: auxTitle,
            AuxItems: auxItems.ToList(),
            DistanceTable: distanceTable,
            TotalWeight: totalWeight,
            BfsLevels: bfsLevels?.Select(l => (IReadOnlyList<string>)l.ToList()).ToList(),
            EdgeClassifications: edgeClassifications is null
                ? null
                : new Dictionary<string, string>(edgeClassifications, StringComparer.Ordinal));

        return new TraceStep<GraphStepState>(
            stepIndex,
            title,
            explanation,
            activeLine,
            state,
            highlightedNodes,
            highlightedEdges,
            opCode,
            badge);
    }

    private DiagramScene BuildScene(
        GraphModel graph,
        string? startVertex,
        string caption,
        IReadOnlyDictionary<string, string>? nodeStates,
        IReadOnlyDictionary<string, string>? edgeStates,
        IReadOnlySet<string>? edgeDashed,
        IReadOnlyDictionary<string, string>? subLabels,
        IReadOnlyDictionary<string, string>? nodeBadges)
    {
        var orderedVertices = graph.GetOrderedVertexIds(Ruleset.NeighborOrder);
        var labelMap = graph.Vertices.ToDictionary(v => v.Id, v => v.Label, StringComparer.Ordinal);

        var nodeInputs = orderedVertices.Select(vId =>
        {
            var semantic = nodeStates is not null && nodeStates.TryGetValue(vId, out var ns)
                ? ns
                : SemanticStates.Default;
            var sub = subLabels is not null && subLabels.TryGetValue(vId, out var sl) ? sl : null;
            var badge = nodeBadges is not null && nodeBadges.TryGetValue(vId, out var nb) ? nb : null;

            return new GraphNodeInput(
                Id: vId,
                Label: labelMap.TryGetValue(vId, out var lbl) ? lbl : vId,
                Shape: NodeShapes.Circle,
                SemanticState: semantic,
                SubLabel: sub,
                Badge: badge,
                IsStart: startVertex is not null && string.Equals(vId, startVertex, StringComparison.Ordinal));
        }).ToList();

        var edgeInputs = graph.Edges.Select(e =>
        {
            var semantic = edgeStates is not null && edgeStates.TryGetValue(e.Id, out var es)
                ? es
                : SemanticStates.Default;
            var dashed = edgeDashed is not null && edgeDashed.Contains(e.Id);
            var label = graph.IsWeighted
                ? (string.IsNullOrEmpty(e.Label) ? GraphModel.FormatWeight(e.Weight) : e.Label)
                : e.Label;

            return new GraphEdgeInput(
                Id: e.Id,
                SourceId: e.From,
                TargetId: e.To,
                Label: label,
                IsDirected: graph.IsDirected,
                SemanticState: semantic,
                IsDashed: dashed);
        }).ToList();

        var preferCircular = !graph.IsDirected || orderedVertices.Count >= 7;
        return DiagramLayout.LayoutStateGraph(
            nodeInputs,
            edgeInputs,
            startNodeId: startVertex,
            preferCircular: preferCircular,
            caption: caption,
            overlayTag: "Graph");
    }
}
