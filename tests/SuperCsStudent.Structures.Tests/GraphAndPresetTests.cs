using SuperCsStudent.Structures.Config;
using SuperCsStudent.Structures.Graphs;
using SuperCsStudent.Structures.Presets;

namespace SuperCsStudent.Structures.Tests;

public sealed class GraphAndPresetTests
{
    [Fact]
    public void GraphRepresentations_StaySynchronizedAcrossEdits()
    {
        var graph = GraphModel.ParseEdgeList("A -> B 5\nB -> C 3\nA -> C 10", isDirected: true, isWeighted: true);
        graph.AddVertex("D");
        graph.AddEdge("C", "D", 2);

        var reps = GraphRepresentationBuilder.Build(graph, NeighborOrder.AlphabeticalAsc);

        Assert.Equal(4, reps.EdgeList.Count);
        Assert.Equal(new[] { "A", "B", "C", "D" }, reps.AdjacencyMatrix.Headers);
        Assert.Equal("5", reps.AdjacencyMatrix.Cells[0][1]);
        Assert.Equal("10", reps.AdjacencyMatrix.Cells[0][2]);
        Assert.Equal("∞", reps.AdjacencyMatrix.Cells[1][0]); // Directed: no B -> A

        var rowA = reps.AdjacencyList.Single(r => r.Vertex == "A");
        Assert.Equal(new[] { "B (5)", "C (10)" }, rowA.Outgoing);
        Assert.Empty(rowA.Incoming);

        var mapA = reps.AdjacencyMap.Single(r => r.Vertex == "A");
        Assert.Equal("5", mapA.Entries["B"]);
        Assert.Equal("10", mapA.Entries["C"]);

        // Remove vertex B and re-verify sync across all 4 representations
        Assert.True(graph.RemoveVertex("B"));
        var repsAfter = GraphRepresentationBuilder.Build(graph, NeighborOrder.AlphabeticalAsc);
        Assert.Equal(2, repsAfter.EdgeList.Count);
        Assert.Equal(new[] { "A", "C", "D" }, repsAfter.AdjacencyMatrix.Headers);
        Assert.DoesNotContain(repsAfter.AdjacencyList, r => r.Vertex == "B");
    }

    [Fact]
    public void DirectedAirportGraph_BfsLevelsAndDfsEdgeClassification_MatchExpected()
    {
        var preset = StructurePresets.GetByKey("graph-directed-airports")!;
        var graph = StructurePresets.BuildGraphFromPreset(preset);

        Assert.Equal(7, graph.Vertices.Count);

        // BFS Level-by-Level from DVO
        var tracer = new GraphTracer(GraphRuleset.CourseStandard with
        {
            BfsMode = BfsMode.LevelByLevel,
            TraverseDisconnected = false
        });
        var bfs = tracer.TraceBfs(graph, "DVO");

        Assert.True(bfs.IsSuccess);
        var finalState = bfs.Steps[^1].State;
        Assert.NotNull(finalState.BfsLevels);
        Assert.Equal(3, finalState.BfsLevels!.Count);
        Assert.Equal(new[] { "DVO" }, finalState.BfsLevels[0]);
        Assert.Equal(new[] { "CDO", "CEB", "MNL" }, finalState.BfsLevels[1].OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "ILO", "OZC" }, finalState.BfsLevels[2].OrderBy(x => x).ToArray());
        Assert.DoesNotContain("SUG", finalState.VisitOrder);

        // Verify QueueBased mode gives identical level grouping
        tracer.Ruleset = tracer.Ruleset with { BfsMode = BfsMode.QueueBased };
        var bfsQueue = tracer.TraceBfs(graph, "DVO");
        Assert.DoesNotContain("SUG", bfsQueue.Steps[^1].State.VisitOrder);
        Assert.Equal(6, bfsQueue.Steps[^1].State.VisitOrder.Count);

        // Verify TraverseDisconnected visits SUG too
        tracer.Ruleset = tracer.Ruleset with { TraverseDisconnected = true };
        var bfsAll = tracer.TraceBfs(graph, "DVO");
        Assert.Contains("SUG", bfsAll.Steps[^1].State.VisitOrder);
        Assert.Equal(7, bfsAll.Steps[^1].State.VisitOrder.Count);

        // DFS Recursive edge classification produces Discovery, Back, Forward, and Cross edges
        var dfsTracer = new GraphTracer(GraphRuleset.CourseStandard with { DfsMode = DfsMode.Recursive });
        var dfs = dfsTracer.TraceDfs(graph, "DVO");
        var classes = dfs.Steps[^1].State.EdgeClassifications!;
        Assert.Contains("Discovery", classes.Values);
        Assert.Contains("Back", classes.Values);
        Assert.Contains("Forward", classes.Values);
        Assert.Contains("Cross", classes.Values);

        // DFS IterativeStack visits all 6 reachable vertices from DVO
        dfsTracer.Ruleset = dfsTracer.Ruleset with { DfsMode = DfsMode.IterativeStack };
        var dfsIter = dfsTracer.TraceDfs(graph, "DVO");
        Assert.Equal(6, dfsIter.Steps[^1].State.VisitOrder.Count);
    }

    [Fact]
    public void WeightedAirportGraph_DijkstraPrimAndKruskal_MatchExactWeightsAndEarlyStop()
    {
        var preset = StructurePresets.GetByKey("graph-airport")!;
        var graph = StructurePresets.BuildGraphFromPreset(preset);
        var tracer = new GraphTracer(GraphRuleset.CourseStandard);

        Assert.Equal(9, graph.Vertices.Count);

        // Dijkstra from BWI
        var dijkstra = tracer.TraceDijkstra(graph, "BWI");
        Assert.True(dijkstra.IsSuccess);
        var distMap = dijkstra.Steps[^1].State.DistanceTable!
            .ToDictionary(r => r.Vertex, r => r.Distance);

        var expectedDijkstra = new Dictionary<string, double>
        {
            ["BWI"] = 0,
            ["JFK"] = 184,
            ["PVD"] = 328,
            ["BOS"] = 371,
            ["ORD"] = 621,
            ["MIA"] = 946,
            ["DFW"] = 1423,
            ["SFO"] = 2467,
            ["LAX"] = 2658
        };

        foreach (var (v, expectedDist) in expectedDijkstra)
        {
            Assert.Equal(expectedDist, distMap[v]);
        }

        // Prim-Jarnik from PVD -> total weight 4456
        var prim = tracer.TracePrimJarnik(graph, "PVD");
        Assert.True(prim.IsSuccess);
        Assert.Equal(4456, prim.Steps[^1].State.TotalWeight);
        Assert.Equal(9, prim.Steps[^1].State.VisitOrder.Count);

        // Kruskal -> early stop after 13th edge, total weight 4456
        var kruskal = tracer.TraceKruskal(graph);
        Assert.True(kruskal.IsSuccess);
        Assert.Equal(4456, kruskal.Steps[^1].State.TotalWeight);
        Assert.Contains(kruskal.OperationLog, op => op.StartsWith("EARLY_STOP examined=13 ", StringComparison.Ordinal));
    }

    [Fact]
    public void WeightedGraphWithTies_MatchesExactDijkstraAndMstWeight18()
    {
        var preset = StructurePresets.GetByKey("graph-weighted-ties")!;
        var graph = StructurePresets.BuildGraphFromPreset(preset);
        var tracer = new GraphTracer(GraphRuleset.CourseStandard);

        Assert.Equal(9, graph.Vertices.Count);

        var dijkstra = tracer.TraceDijkstra(graph, "A");
        Assert.True(dijkstra.IsSuccess);
        var distMap = dijkstra.Steps[^1].State.DistanceTable!
            .ToDictionary(r => r.Vertex, r => r.Distance);

        var expected = new Dictionary<string, double>
        {
            ["A"] = 0,
            ["B"] = 2,
            ["G"] = 3,
            ["I"] = 4,
            ["C"] = 6,
            ["E"] = 6,
            ["H"] = 6,
            ["D"] = 7,
            ["F"] = 7
        };

        foreach (var (v, expectedDist) in expected)
        {
            Assert.Equal(expectedDist, distMap[v]);
        }

        var prim = tracer.TracePrimJarnik(graph, "A");
        var kruskal = tracer.TraceKruskal(graph);
        Assert.Equal(18, prim.Steps[^1].State.TotalWeight);
        Assert.Equal(18, kruskal.Steps[^1].State.TotalWeight);
    }

    [Fact]
    public void Dijkstra_NegativeEdgeWeight_FailsGracefully()
    {
        var graph = GraphModel.ParseEdgeList("A -> B -3\nB -> C 2", isDirected: true, isWeighted: true);
        var tracer = new GraphTracer();
        var result = tracer.TraceDijkstra(graph, "A");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void StructurePresets_ContainAllRequiredKeys()
    {
        var requiredKeys = new[]
        {
            "splay-delete",
            "tree24-split",
            "rb-demo",
            "graph-airport",
            "bst-exercise31",
            "avl-insert-delete",
            "splay-rotate14",
            "tree24-fusion",
            "binary-traversals",
            "graph-directed-airports",
            "graph-weighted-ties"
        };

        foreach (var key in requiredKeys)
        {
            var preset = StructurePresets.GetByKey(key);
            Assert.NotNull(preset);
            Assert.False(string.IsNullOrWhiteSpace(preset!.Title));
            Assert.False(string.IsNullOrWhiteSpace(preset.Description));
        }
    }

    [Fact]
    public void GraphTracer_NeighborOrderAndUndirectedIterativeDfs_HonorConfiguration()
    {
        var graph = GraphModel.ParseEdgeList("A B\nA D\nA C\nB C", isDirected: false, isWeighted: false);

        var asc = new GraphTracer(GraphRuleset.CourseStandard with { NeighborOrder = NeighborOrder.AlphabeticalAsc });
        var desc = new GraphTracer(GraphRuleset.CourseStandard with { NeighborOrder = NeighborOrder.AlphabeticalDesc });
        var ins = new GraphTracer(GraphRuleset.CourseStandard with { NeighborOrder = NeighborOrder.InsertionOrder });

        Assert.Equal(new[] { "A", "B", "C", "D" }, asc.TraceBfs(graph, "A").Steps[^1].State.VisitOrder);
        Assert.Equal(new[] { "A", "D", "C", "B" }, desc.TraceBfs(graph, "A").Steps[^1].State.VisitOrder);
        Assert.Equal(new[] { "A", "B", "D", "C" }, ins.TraceBfs(graph, "A").Steps[^1].State.VisitOrder);

        var iterDfs = new GraphTracer(GraphRuleset.CourseStandard with { DfsMode = DfsMode.IterativeStack });
        var dfs = iterDfs.TraceDfs(graph, "A");
        Assert.Equal(4, dfs.Steps[^1].State.VisitOrder.Count);
        Assert.Contains("Back", dfs.Steps[^1].State.EdgeClassifications!.Values);
    }
}
