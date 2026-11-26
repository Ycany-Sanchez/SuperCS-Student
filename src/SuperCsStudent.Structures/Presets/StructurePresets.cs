using SuperCsStudent.Structures.Config;
using SuperCsStudent.Structures.Graphs;
using SuperCsStudent.Structures.Trees;

namespace SuperCsStudent.Structures.Presets;

public enum StructureCategory
{
    BinaryTree,
    Bst,
    Avl,
    Splay,
    TwoFour,
    RedBlack,
    Graph
}

public sealed record BinaryTreePlacement(string? ParentLabel, string Label, bool IsLeft);

public sealed record StructurePreset(
    string Key,
    string Title,
    string Subtitle,
    StructureCategory Category,
    string Description,
    string? TreeScript = null,
    TreeRuleset? RecommendedTreeRuleset = null,
    IReadOnlyList<BinaryTreePlacement>? BinaryTreePlacements = null,
    string? GraphEdgeList = null,
    bool GraphIsDirected = false,
    bool GraphIsWeighted = false,
    string? DefaultStartVertex = null,
    string? DefaultGraphAlgorithm = null,
    GraphRuleset? RecommendedGraphRuleset = null);

public static class StructurePresets
{
    private static readonly IReadOnlyList<StructurePreset> AllPresets =
    [
        // Matches AppStateService key: "splay-delete"
        new(
            Key: "splay-delete",
            Title: "Splay Tree Deletion Variants",
            Subtitle: "Compare BST-delete vs Splay Max-Left / Min-Right",
            Category: StructureCategory.Splay,
            Description: "Builds a splay tree containing 8 with predecessor 7 (under parent 6) and successor 10, then deletes 8 to contrast all three splay deletion strategies.",
            TreeScript: "insert 12, 10, 4, 7, 6, 8; delete 8",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard with
            {
                DeletionReplacement = DeletionReplacement.InOrderPredecessor,
                SplayDeleteStrategy = SplayDeleteStrategy.BstDeleteThenSplayParent
            }),

        // Matches AppStateService key: "tree24-split"
        new(
            Key: "tree24-split",
            Title: "(2,4) Tree Overflow Splits",
            Subtitle: "Toggle 3rd-key (k3) vs 2nd-key (k2) split promotion",
            Category: StructureCategory.TwoFour,
            Description: "Inserts 4, 6, 12, 15, 3, 5, 10, 8 to highlight 5-node overflow splits under both right-middle (k3) and left-middle (k2) promotion rules.",
            TreeScript: "insert 4, 6, 12, 15, 3, 5, 10, 8",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard with
            {
                TwoFourSplitPromotion = TwoFourSplitPromotion.ThirdKeyRightMiddle
            }),

        // Matches AppStateService key: "rb-demo"
        new(
            Key: "rb-demo",
            Title: "Red-Black Tree + (2,4) Isomorphism",
            Subtitle: "Insertion & deletion violations with isomorphic (2,4) view",
            Category: StructureCategory.RedBlack,
            Description: "Inserts a 10-key sequence triggering double-red Case 1 restructuring and Case 2 recoloring, with live isomorphic (2,4) tree projection enabled.",
            TreeScript: "insert 4, 7, 12, 15, 3, 5, 14, 18, 16, 17",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard with
            {
                ShowTwoFourIsomorphism = true
            }),

        // Matches AppStateService key: "graph-airport"
        new(
            Key: "graph-airport",
            Title: "Weighted Airport Network (9 Nodes)",
            Subtitle: "Dijkstra from BWI, Prim-Jarník from PVD, and Kruskal MST",
            Category: StructureCategory.Graph,
            Description: "Classic 9-airport weighted undirected mileage network demonstrating Dijkstra shortest-path relaxation and minimum spanning trees (total weight 4456).",
            GraphEdgeList: string.Join('\n',
            [
                "PVD JFK 144",
                "BWI JFK 184",
                "BOS JFK 187",
                "SFO LAX 337",
                "BWI ORD 621",
                "JFK ORD 740",
                "ORD DFW 802",
                "ORD PVD 849",
                "BOS ORD 867",
                "BWI MIA 946",
                "JFK MIA 1090",
                "DFW MIA 1121",
                "DFW LAX 1235",
                "BOS MIA 1258",
                "JFK DFW 1391",
                "DFW SFO 1464",
                "ORD SFO 1846",
                "LAX MIA 2342",
                "BOS SFO 2704"
            ]),
            GraphIsDirected: false,
            GraphIsWeighted: true,
            DefaultStartVertex: "BWI",
            DefaultGraphAlgorithm: "Dijkstra",
            RecommendedGraphRuleset: GraphRuleset.CourseStandard),

        // 31-operation BST sequence
        new(
            Key: "bst-exercise31",
            Title: "BST 31-Operation Trace",
            Subtitle: "Mixed insertions and 0/1/2-child deletions",
            Category: StructureCategory.Bst,
            Description: "Executes 31 interleaved insertions and deletions to compare In-Order Successor vs In-Order Predecessor replacement behavior.",
            TreeScript: "insert 52, 27, 35, 17, 67, 97, remove 35, insert 19, 55, remove 67, insert 30, 22, 34, 47, 18, remove 27, insert 53, 83, 10, 85, 88, 4, remove 97, insert 6, 70, remove 52, remove 47, insert 8, remove 22, insert 96, 2",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard),

        // AVL insert + equal-height tie-break delete
        new(
            Key: "avl-insert-delete",
            Title: "AVL Trinode Restructure & Tie-Break",
            Subtitle: "ZIGZAGRIGHT insertion followed by equal-height ZIGLEFT deletion",
            Category: StructureCategory.Avl,
            Description: "Builds [44, 17, 78, 32, 50, 88, 48, 62], inserts 54 triggering ZIGZAGRIGHT, then deletes 32 triggering equal-height tie-breaking ZIGLEFT.",
            TreeScript: "insert 44, 17, 78, 32, 50, 88, 48, 62, 54; delete 32",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard),

        // Splay multi-rotation showcase
        new(
            Key: "splay-rotate14",
            Title: "Splay Zig, Zig-Zig & Zig-Zag Trace",
            Subtitle: "Bottom-up splaying across deep search paths",
            Category: StructureCategory.Splay,
            Description: "Constructs an unbalanced path and accesses 14 to step through ZIGZAG, ZIGZIG, and ZIG rotations bringing 14 to the root.",
            TreeScript: "insert 10, 20, 15, 12, 18, 14; search 10; search 14",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard),

        // (2,4) Tree transfer, cascading split, and cascading fusion
        new(
            Key: "tree24-fusion",
            Title: "(2,4) Tree Transfer & Cascading Fusion",
            Subtitle: "Cascading split on 17, transfer on 4, root collapse on 12/13/14",
            Category: StructureCategory.TwoFour,
            Description: "Demonstrates cascading overflow splitting on inserting 17, sibling transfer on deleting 4, and cascading fusion with root collapse on deleting 12, 13, 14.",
            TreeScript: "insert 4, 6, 12, 15, 3, 5, 10, 8; delete 3; insert 11, 13, 14, 17; delete 4, 12, 13, 14",
            RecommendedTreeRuleset: TreeRuleset.CourseStandard with
            {
                DeletionReplacement = DeletionReplacement.InOrderPredecessor,
                TwoFourSplitPromotion = TwoFourSplitPromotion.ThirdKeyRightMiddle,
                TwoFourUnderflowSibling = TwoFourUnderflowSibling.RightSiblingFirst
            }),

        // General Binary Tree traversals
        new(
            Key: "binary-traversals",
            Title: "Binary Tree Traversal Suite",
            Subtitle: "Preorder, Inorder, Postorder, Level-Order & Euler Tour",
            Category: StructureCategory.BinaryTree,
            Description: "10-node general binary tree (G, B, S, A, F, K, D, J, M, P) demonstrating all five traversal orders and height conventions.",
            BinaryTreePlacements:
            [
                new(null, "G", true),
                new("G", "B", true),
                new("G", "S", false),
                new("B", "A", true),
                new("B", "F", false),
                new("S", "K", true),
                new("F", "D", true),
                new("K", "J", true),
                new("K", "M", false),
                new("M", "P", false)
            ],
            RecommendedTreeRuleset: TreeRuleset.CourseStandard),

        // Directed 7-node airport graph
        new(
            Key: "graph-directed-airports",
            Title: "Directed Flight Routes (7 Nodes)",
            Subtitle: "BFS levels from DVO with unreachable hub SUG and DFS edge classes",
            Category: StructureCategory.Graph,
            Description: "7-vertex directed flight network where BFS from DVO partitions into L0={DVO}, L1={CDO,CEB,MNL}, L2={ILO,OZC}, leaving SUG unreachable.",
            GraphEdgeList: string.Join('\n',
            [
                "DVO -> CDO",
                "DVO -> CEB",
                "DVO -> MNL",
                "CDO -> CEB",
                "CDO -> OZC",
                "CEB -> ILO",
                "CEB -> MNL",
                "MNL -> DVO",
                "MNL -> ILO",
                "ILO -> OZC",
                "OZC -> CDO",
                "SUG -> CDO",
                "SUG -> CEB"
            ]),
            GraphIsDirected: true,
            GraphIsWeighted: false,
            DefaultStartVertex: "DVO",
            DefaultGraphAlgorithm: "BFS",
            RecommendedGraphRuleset: GraphRuleset.CourseStandard with { BfsMode = BfsMode.LevelByLevel }),

        // Weighted 9-node graph with tied weights
        new(
            Key: "graph-weighted-ties",
            Title: "Weighted Graph with Edge Ties (A..I)",
            Subtitle: "MST weight 18 and Dijkstra shortest paths from A",
            Category: StructureCategory.Graph,
            Description: "9-vertex undirected weighted graph containing multiple equal-weight edges to examine deterministic tie-breaking in Dijkstra, Prim-Jarník, and Kruskal.",
            GraphEdgeList: string.Join('\n',
            [
                "C D 1",
                "E F 1",
                "A B 2",
                "B I 2",
                "I E 2",
                "A G 3",
                "G H 3",
                "G I 3",
                "E H 3",
                "H F 3",
                "A I 4",
                "B C 4",
                "C E 4",
                "D F 4"
            ]),
            GraphIsDirected: false,
            GraphIsWeighted: true,
            DefaultStartVertex: "A",
            DefaultGraphAlgorithm: "Dijkstra",
            RecommendedGraphRuleset: GraphRuleset.CourseStandard)
    ];

    public static IReadOnlyList<StructurePreset> All => AllPresets;

    public static StructurePreset? GetByKey(string key) =>
        AllPresets.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<StructurePreset> GetByCategory(StructureCategory category) =>
        AllPresets.Where(p => p.Category == category).ToList();

    public static BinaryTreeTracer BuildBinaryTreeFromPreset(StructurePreset preset, TreeRuleset? ruleset = null)
    {
        var tracer = new BinaryTreeTracer(ruleset ?? preset.RecommendedTreeRuleset ?? TreeRuleset.CourseStandard);
        if (preset.BinaryTreePlacements is null) return tracer;

        foreach (var step in preset.BinaryTreePlacements)
        {
            if (step.ParentLabel is null)
            {
                tracer.AddRoot(step.Label);
            }
            else if (step.IsLeft)
            {
                tracer.AddLeft(step.ParentLabel, step.Label);
            }
            else
            {
                tracer.AddRight(step.ParentLabel, step.Label);
            }
        }

        return tracer;
    }

    public static GraphModel BuildGraphFromPreset(StructurePreset preset) =>
        GraphModel.ParseEdgeList(preset.GraphEdgeList ?? "", preset.GraphIsDirected, preset.GraphIsWeighted);
}
