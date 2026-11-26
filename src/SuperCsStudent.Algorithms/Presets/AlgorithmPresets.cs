using SuperCsStudent.Algorithms.Analysis;
using SuperCsStudent.Algorithms.Ast;

namespace SuperCsStudent.Algorithms.Presets;

/// <summary>
/// Ready-to-load algorithm example containing both an immutable AST and synchronized Blockly workspace JSON.
/// </summary>
public sealed record AlgorithmPreset(
    string Key,
    string Title,
    string Category,
    string Description,
    bool IsRecursive,
    AlgorithmFunction Ast,
    string BlocklyStateJson,
    RecurrenceSpec? RecurrenceSpec = null);

/// <summary>
/// Curated catalog of iterative and recursive algorithm presets covering loops, steps,
/// logarithms, modified bounds, multi-level nested summations, early exits, and recurrences.
/// </summary>
public static class AlgorithmPresets
{
    private static readonly IReadOnlyList<AlgorithmPreset> _all = BuildAll();
    private static readonly IReadOnlyDictionary<string, AlgorithmPreset> _byKey =
        _all.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AlgorithmPreset> All => _all;

    public static AlgorithmPreset Get(string key)
    {
        if (_byKey.TryGetValue(key, out var preset)) return preset;
        throw new KeyNotFoundException($"Unknown algorithm preset key '{key}'.");
    }

    public static bool TryGet(string key, out AlgorithmPreset? preset) =>
        _byKey.TryGetValue(key, out preset);

    private static IReadOnlyList<AlgorithmPreset> BuildAll()
    {
        return new List<AlgorithmPreset>
        {
            BuildIterative(
                key: "nested-3",
                title: "3-Level Triangular Nested Loops",
                category: "Nested Loops",
                description: "Three dependent nested loops (i=1..n, j=1..i, k=1..j) producing exact cubic fraction (2/3)n^3 + 4n^2 + (22/3)n + 3.",
                ast: new AlgorithmFunction(
                    "tripleNestedSum",
                    new[] { "n" },
                    new AlgorithmStatement[]
                    {
                        new DeclareStatement(new[] { "i", "j", "k", "sum" }, "n3_decl"),
                        new AssignStatement("sum", "=", new LiteralExpr("0"), "n3_init"),
                        new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new ForStatement("j", "1", "<=", "i", "++", "1", new AlgorithmStatement[]
                            {
                                new ForStatement("k", "1", "<=", "j", "++", "1", new AlgorithmStatement[]
                                {
                                    new AssignStatement("sum", "=",
                                        new BinaryExpr("+", new VariableExpr("sum"), new VariableExpr("k")),
                                        "n3_body")
                                }, "n3_for_k")
                            }, "n3_for_j")
                        }, "n3_for_i"),
                        new ReturnStatement(new VariableExpr("sum"), "n3_ret")
                    })),

            BuildIterative(
                key: "log-base2",
                title: "Doubling Loop (Base-2 Log)",
                category: "Logarithmic Loops",
                description: "Multiplicative loop multiplying iterator by 2 each iteration: T(n) = 4 log_2 n + 3.",
                ast: BuildSumArrayLoop("logBase2Sum", "1", "<=", "n", "*=", "2", "lb2")),

            BuildIterative(
                key: "find-small",
                title: "Minimum Element Scan",
                category: "Conditional Loops",
                description: "Single-branch conditional inside a scan loop: Worst 6n - 2, Best 4n.",
                ast: new AlgorithmFunction(
                    "findSmall",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new DeclareStatement(new[] { "i", "min" }, "fs_decl"),
                        new AssignStatement("min", "=", new ArrayAccessExpr("array", "0"), "fs_init"),
                        new ForStatement("i", "1", "<", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new IfStatement(
                                new BinaryExpr("<", new ArrayAccessExpr("array", "i"), new VariableExpr("min")),
                                new AlgorithmStatement[]
                                {
                                    new AssignStatement("min", "=", new ArrayAccessExpr("array", "i"), "fs_update")
                                },
                                Array.Empty<AlgorithmStatement>(),
                                "fs_if")
                        }, "fs_for"),
                        new ReturnStatement(new VariableExpr("min"), "fs_ret")
                    })),

            BuildRecursive(
                key: "recur-nlogn",
                title: "Divide-and-Conquer Linear Merge",
                category: "Recurrences",
                description: "Splits input in half across two recursive calls with linear combine work: T(n) = 2T(n/2) + n.",
                ast: new AlgorithmFunction(
                    "divideConquerMerge",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rnl_base") },
                            new AlgorithmStatement[]
                            {
                                new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                                {
                                    new AssignStatement("sum", "+=", new ArrayAccessExpr("array", "i"), "rnl_work")
                                }, "rnl_for"),
                                new RecursiveCallStatement(2, "n/2", "rnl_rec")
                            },
                            "rnl_if")
                    }),
                spec: new RecurrenceSpec(1, 1, 2, DivideByB: 2, SubtractC: null, NonRecursiveWorkLatex: "n", WorkPolyDegreeK: 1)),

            BuildIterative(
                key: "find-sum",
                title: "Linear Array Summation",
                category: "Linear Loops",
                description: "Accumulates array elements from 0 to n - 1: T(n) = 5n + 3.",
                ast: new AlgorithmFunction(
                    "findSum",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new DeclareStatement(new[] { "i", "sum" }, "sum_decl"),
                        new AssignStatement("sum", "=", new LiteralExpr("0"), "sum_init"),
                        new ForStatement("i", "0", "<", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new AssignStatement("sum", "=",
                                new BinaryExpr("+", new VariableExpr("sum"), new ArrayAccessExpr("array", "i")),
                                "sum_body")
                        }, "sum_for"),
                        new ReturnStatement(new VariableExpr("sum"), "sum_ret")
                    })),

            BuildIterative(
                key: "find-x-break",
                title: "Linear Search with Early Exit",
                category: "Conditional Loops",
                description: "Breaks immediately on first match: Worst 4n + 3 = O(n), Best 7 = Ω(1) (Not tight).",
                ast: new AlgorithmFunction(
                    "findTarget",
                    new[] { "array[]", "n", "x" },
                    new AlgorithmStatement[]
                    {
                        new DeclareStatement(new[] { "i", "found", "pos" }, "fx_decl"),
                        new AssignStatement("found", "=", new LiteralExpr("0"), "fx_init"),
                        new ForStatement("i", "0", "<", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new IfStatement(
                                new BinaryExpr("==", new ArrayAccessExpr("array", "i"), new VariableExpr("x")),
                                new AlgorithmStatement[]
                                {
                                    new AssignStatement("found", "=", new LiteralExpr("1"), "fx_found"),
                                    new AssignStatement("pos", "=", new VariableExpr("i"), "fx_pos"),
                                    new BreakStatement("fx_break")
                                },
                                Array.Empty<AlgorithmStatement>(),
                                "fx_if")
                        }, "fx_for"),
                        new ReturnStatement(new VariableExpr("found"), "fx_ret")
                    })),

            BuildIterative(
                key: "two-loops",
                title: "Sequential Sum and Product Loops",
                category: "Linear Loops",
                description: "Two independent sequential loops with compound assignments and binary return: T(n) = 8n + 7.",
                ast: new AlgorithmFunction(
                    "sumAndProduct",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new DeclareStatement(new[] { "i", "sum", "prod" }, "tl_decl"),
                        new AssignStatement("sum", "=", new LiteralExpr("0"), "tl_init1"),
                        new AssignStatement("prod", "=", new LiteralExpr("1"), "tl_init2"),
                        new ForStatement("i", "0", "<", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new AssignStatement("sum", "+=", new ArrayAccessExpr("array", "i"), "tl_body1")
                        }, "tl_for1"),
                        new ForStatement("i", "0", "<", "n", "++", "1", new AlgorithmStatement[]
                        {
                            new AssignStatement("prod", "*=", new ArrayAccessExpr("array", "i"), "tl_body2")
                        }, "tl_for2"),
                        new ReturnStatement(
                            new BinaryExpr("+", new VariableExpr("sum"), new VariableExpr("prod")),
                            "tl_ret")
                    })),

            BuildIterative(
                key: "step-2",
                title: "Stride-2 Step Loop",
                category: "Step Loops",
                description: "Additive loop advancing by 2 each iteration: T(n) = 2n + 3.",
                ast: BuildSumArrayLoop("stepTwoSum", "1", "<=", "n", "+=", "2", "st2")),

            BuildIterative(
                key: "step-3",
                title: "Stride-3 Step Loop",
                category: "Step Loops",
                description: "Additive loop advancing by 3 each iteration: T(n) = (4/3)n + 3.",
                ast: BuildSumArrayLoop("stepThreeSum", "1", "<=", "n", "+=", "3", "st3")),

            BuildIterative(
                key: "log-base10",
                title: "Base-10 Multiplicative Loop",
                category: "Logarithmic Loops",
                description: "Multiplicative loop multiplying iterator by 10 each step: T(n) = 4 log_10 n + 3.",
                ast: BuildSumArrayLoop("logBase10Sum", "1", "<=", "n", "*=", "10", "lb10")),

            BuildIterative(
                key: "bound-plus10",
                title: "Shifted Upper Bound (n + 10)",
                category: "Modified Bounds",
                description: "Linear loop running up to n + 10: T(n) = 4n + 43.",
                ast: BuildSumArrayLoop("boundPlusTen", "1", "<=", "n+10", "++", "1", "bp10")),

            BuildIterative(
                key: "bound-times5",
                title: "Scaled Upper Bound (n * 5)",
                category: "Modified Bounds",
                description: "Linear loop running up to 5n: T(n) = 20n + 3.",
                ast: BuildSumArrayLoop("boundTimesFive", "1", "<=", "n*5", "++", "1", "bt5")),

            BuildIterative(
                key: "bound-nsq",
                title: "Quadratic Upper Bound (n * n)",
                category: "Modified Bounds",
                description: "Single loop iterating up to n^2: T(n) = 4n^2 + 3.",
                ast: BuildSumArrayLoop("boundNSquared", "1", "<=", "n*n", "++", "1", "bnsq")),

            BuildIterative(
                key: "nested-square",
                title: "Square Nested Loops (n x n)",
                category: "Nested Loops",
                description: "Independent nested loops (i=1..n, j=1..n): T(n) = 4n^2 + 4n + 3.",
                ast: BuildDoubleNestedSum("nestedSquare", "1", "n", "++", "1", "1", "n", "++", "1", "nsq")),

            BuildIterative(
                key: "nested-dependent",
                title: "Triangular Nested Loops (j = 1..i)",
                category: "Nested Loops",
                description: "Dependent inner bound j <= i evaluated via exact summation: T(n) = 2n^2 + 6n + 3.",
                ast: BuildDoubleNestedSum("nestedTriangular", "1", "n", "++", "1", "1", "i", "++", "1", "ndep")),

            BuildIterative(
                key: "nested-step4",
                title: "Outer Stride-4 Nested Loops",
                category: "Nested Loops",
                description: "Outer loop steps by 4 with full inner n-loop: T(n) = n^2 + n + 3.",
                ast: BuildDoubleNestedSum("nestedStepFour", "1", "n", "+=", "4", "1", "n", "++", "1", "nst4")),

            BuildIterative(
                key: "nested-log",
                title: "Linear-Logarithmic Nested Loops",
                category: "Nested Loops",
                description: "Outer linear loop with inner doubling loop: T(n) = 4n log_2 n + 4n + 3.",
                ast: BuildDoubleNestedSum("nestedLogarithmic", "1", "n", "++", "1", "1", "n", "*=", "2", "nlog")),

            BuildIterative(
                key: "nested-start3",
                title: "Offset Start (i = 3..n) Nested Loops",
                category: "Nested Loops",
                description: "Outer loop begins at i = 3 with inner loop j = 1..n: T(n) = 4n^2 - 4n - 5.",
                ast: BuildDoubleNestedSum("nestedStartThree", "3", "n", "++", "1", "1", "n", "++", "1", "ns3")),

            BuildIterative(
                key: "nested-start4",
                title: "Offset Start (i = 4..n) Triangular Loops",
                category: "Nested Loops",
                description: "Dependent triangular sum starting at i = 4 via lower-bound splitting: T(n) = 2n^2 + 6n - 33.",
                ast: BuildDoubleNestedSum("nestedStartFour", "4", "n", "++", "1", "1", "i", "++", "1", "ns4")),

            BuildRecursive(
                key: "recur-factorial",
                title: "Linear Decrease Recurrence",
                category: "Recurrences",
                description: "Decreases problem size by 1 with constant per-call work: T(n) = T(n - 1) + 1 = Θ(n).",
                ast: new AlgorithmFunction(
                    "factorialLinear",
                    new[] { "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rf_base") },
                            new AlgorithmStatement[]
                            {
                                new RecursiveCallStatement(1, "n-1", "rf_rec"),
                                new ReturnStatement(new VariableExpr("n"), "rf_ret")
                            },
                            "rf_if")
                    }),
                spec: new RecurrenceSpec(0, 1, 1, DivideByB: null, SubtractC: 1, NonRecursiveWorkLatex: "1", WorkPolyDegreeK: 0)),

            BuildRecursive(
                key: "recur-n2",
                title: "Quadratic Subtraction Recurrence",
                category: "Recurrences",
                description: "Decreases by 1 with linear work per frame: T(n) = T(n - 1) + n = Θ(n^2).",
                ast: new AlgorithmFunction(
                    "quadraticDecrease",
                    new[] { "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rn2_base") },
                            new AlgorithmStatement[]
                            {
                                new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                                {
                                    new AssignStatement("sum", "+=", new VariableExpr("i"), "rn2_work")
                                }, "rn2_for"),
                                new RecursiveCallStatement(1, "n-1", "rn2_rec")
                            },
                            "rn2_if")
                    }),
                spec: new RecurrenceSpec(1, 1, 1, DivideByB: null, SubtractC: 1, NonRecursiveWorkLatex: "n", WorkPolyDegreeK: 1)),

            BuildRecursive(
                key: "recur-n3",
                title: "Cubic Subtraction Recurrence",
                category: "Recurrences",
                description: "Decreases by 1 with quadratic work per frame: T(n) = T(n - 1) + n^2 = Θ(n^3).",
                ast: new AlgorithmFunction(
                    "cubicDecrease",
                    new[] { "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rn3_base") },
                            new AlgorithmStatement[]
                            {
                                new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                                {
                                    new ForStatement("j", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                                    {
                                        new AssignStatement("sum", "+=", new VariableExpr("j"), "rn3_work")
                                    }, "rn3_for_j")
                                }, "rn3_for_i"),
                                new RecursiveCallStatement(1, "n-1", "rn3_rec")
                            },
                            "rn3_if")
                    }),
                spec: new RecurrenceSpec(1, 1, 1, DivideByB: null, SubtractC: 1, NonRecursiveWorkLatex: "n^2", WorkPolyDegreeK: 2)),

            BuildRecursive(
                key: "recur-log",
                title: "Binary Halving Recurrence",
                category: "Recurrences",
                description: "Halves problem size with constant work per step: T(n) = T(n / 2) + 1 = Θ(log n).",
                ast: new AlgorithmFunction(
                    "binaryHalving",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rlog_base") },
                            new AlgorithmStatement[]
                            {
                                new RecursiveCallStatement(1, "n/2", "rlog_rec")
                            },
                            "rlog_if")
                    }),
                spec: new RecurrenceSpec(1, 1, 1, DivideByB: 2, SubtractC: null, NonRecursiveWorkLatex: "1", WorkPolyDegreeK: 0)),

            BuildRecursive(
                key: "recur-linear-div2",
                title: "Halving with Linear Work",
                category: "Recurrences",
                description: "Single recursive call on half input plus linear scan: T(n) = T(n / 2) + n = Θ(n).",
                ast: new AlgorithmFunction(
                    "halvingLinearWork",
                    new[] { "array[]", "n" },
                    new AlgorithmStatement[]
                    {
                        new IfStatement(
                            new BinaryExpr("<=", new VariableExpr("n"), new LiteralExpr("1")),
                            new AlgorithmStatement[] { new ReturnStatement(new LiteralExpr("1"), "rld_base") },
                            new AlgorithmStatement[]
                            {
                                new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                                {
                                    new AssignStatement("sum", "+=", new ArrayAccessExpr("array", "i"), "rld_work")
                                }, "rld_for"),
                                new RecursiveCallStatement(1, "n/2", "rld_rec")
                            },
                            "rld_if")
                    }),
                spec: new RecurrenceSpec(1, 1, 1, DivideByB: 2, SubtractC: null, NonRecursiveWorkLatex: "n", WorkPolyDegreeK: 1))
        };
    }

    private static AlgorithmPreset BuildIterative(
        string key,
        string title,
        string category,
        string description,
        AlgorithmFunction ast)
    {
        return new AlgorithmPreset(
            Key: key,
            Title: title,
            Category: category,
            Description: description,
            IsRecursive: false,
            Ast: ast,
            BlocklyStateJson: AlgorithmAstSerializer.ToBlocklyWorkspaceJson(ast),
            RecurrenceSpec: null);
    }

    private static AlgorithmPreset BuildRecursive(
        string key,
        string title,
        string category,
        string description,
        AlgorithmFunction ast,
        RecurrenceSpec spec)
    {
        return new AlgorithmPreset(
            Key: key,
            Title: title,
            Category: category,
            Description: description,
            IsRecursive: true,
            Ast: ast,
            BlocklyStateJson: AlgorithmAstSerializer.ToBlocklyWorkspaceJson(ast),
            RecurrenceSpec: spec);
    }

    private static AlgorithmFunction BuildSumArrayLoop(
        string name,
        string start,
        string cmp,
        string bound,
        string stepOp,
        string stepVal,
        string idPrefix)
    {
        return new AlgorithmFunction(
            name,
            new[] { "array[]", "n" },
            new AlgorithmStatement[]
            {
                new DeclareStatement(new[] { "i", "sum" }, $"{idPrefix}_decl"),
                new AssignStatement("sum", "=", new LiteralExpr("0"), $"{idPrefix}_init"),
                new ForStatement("i", start, cmp, bound, stepOp, stepVal, new AlgorithmStatement[]
                {
                    new AssignStatement("sum", "+=", new ArrayAccessExpr("array", "i"), $"{idPrefix}_body")
                }, $"{idPrefix}_for"),
                new ReturnStatement(new VariableExpr("sum"), $"{idPrefix}_ret")
            });
    }

    private static AlgorithmFunction BuildDoubleNestedSum(
        string name,
        string outerStart,
        string outerBound,
        string outerStepOp,
        string outerStepVal,
        string innerStart,
        string innerBound,
        string innerStepOp,
        string innerStepVal,
        string idPrefix)
    {
        return new AlgorithmFunction(
            name,
            new[] { "n" },
            new AlgorithmStatement[]
            {
                new DeclareStatement(new[] { "i", "j", "sum" }, $"{idPrefix}_decl"),
                new AssignStatement("sum", "=", new LiteralExpr("0"), $"{idPrefix}_init"),
                new ForStatement("i", outerStart, "<=", outerBound, outerStepOp, outerStepVal, new AlgorithmStatement[]
                {
                    new ForStatement("j", innerStart, "<=", innerBound, innerStepOp, innerStepVal, new AlgorithmStatement[]
                    {
                        new AssignStatement("sum", "=",
                            new BinaryExpr("+", new VariableExpr("sum"), new VariableExpr("j")),
                            $"{idPrefix}_body")
                    }, $"{idPrefix}_for_j")
                }, $"{idPrefix}_for_i"),
                new ReturnStatement(new VariableExpr("sum"), $"{idPrefix}_ret")
            });
    }
}
