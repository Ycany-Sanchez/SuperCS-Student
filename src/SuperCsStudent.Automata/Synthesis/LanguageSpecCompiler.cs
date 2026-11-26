using System.Text.RegularExpressions;
using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Turing;

namespace SuperCsStudent.Automata.Synthesis;

public enum LanguageSpecMode
{
    Auto,
    Regex,
    Constraint,
    Parametric
}

public enum LanguageClass
{
    Regular,
    ContextFree,
    ContextSensitive,
    Transducer
}

public sealed record LanguageSpecCompilationResult(
    string Specification,
    LanguageSpecMode ResolvedMode,
    LanguageClass LanguageClass,
    string Title,
    string Description,
    Dfa? MinimalDfa,
    Nfa? Nfa,
    Cfg? Grammar,
    PushdownAutomaton? Pda,
    TmSchema TmSchema,
    TmCompiledSchema CompiledTm);

/// <summary>
/// 100% deterministic formal-language specification compiler supporting:
///   1. Regular expressions -> NFA -> Minimal DFA -> Regular Grammar -> Decidable TM Schema
///   2. Boolean regular constraints (starts with, ends with, contains, does not contain,
///      even/odd counts, length divisible by k, at least/exactly/at most k, with AND/OR/NOT/())
///   3. Parametric non-regular languages and Turing transducers
///      (a^n b^n, a^2n b^n, a^n b^n c^n, a^2n b^m c^n, a^n b^n c^m, palindromes, even palindromes,
///       balanced (), #a > #b, #a = #b, invert a and b, shift left, shift right, AND, XOR,
///       m >= n, m <= n, snake_case to camelCase).
/// </summary>
public static class LanguageSpecCompiler
{
    private static readonly Regex ConstraintSignalRegex = new(
        @"\b(starts?\s+with|begins?\s+with|ends?\s+with|contains?|does\s+not|doesn't|even|odd|length|divisible|multiple|at\s+least|at\s+most|exactly)\b|\b(AND|OR|NOT)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static LanguageSpecCompilationResult Compile(
        string specification,
        LanguageSpecMode mode = LanguageSpecMode.Auto,
        IEnumerable<char>? alphabet = null)
    {
        if (string.IsNullOrWhiteSpace(specification))
        {
            throw new ArgumentException("Language specification cannot be empty.", nameof(specification));
        }

        var trimmed = specification.Trim();

        if (mode == LanguageSpecMode.Parametric)
        {
            return TryCompileParametric(trimmed)
                ?? throw new FormatException($"Unrecognized parametric specification: '{trimmed}'.");
        }

        if (mode == LanguageSpecMode.Constraint)
        {
            return CompileConstraint(trimmed, alphabet);
        }

        if (mode == LanguageSpecMode.Regex)
        {
            return CompileRegex(trimmed, alphabet);
        }

        // Auto-detection: Parametric -> Constraint -> Regex
        var parametric = TryCompileParametric(trimmed);
        if (parametric is not null)
        {
            return parametric;
        }

        if (ConstraintSignalRegex.IsMatch(trimmed))
        {
            return CompileConstraint(trimmed, alphabet);
        }

        return CompileRegex(trimmed, alphabet);
    }

    public static LanguageSpecCompilationResult CompileRegex(string pattern, IEnumerable<char>? alphabet = null)
    {
        var compiled = RegexCompiler.Compile(pattern, alphabet);
        var minDfa = compiled.MinimalDfa;
        var rg = RegularGrammarConverter.DfaToRegularGrammar(minDfa);
        var schema = DfaToDecidableTmSchema(minDfa, $"Decider for /{pattern}/");
        var compiledTm = TmSchemaCompiler.Compile(schema);

        return new LanguageSpecCompilationResult(
            Specification: pattern,
            ResolvedMode: LanguageSpecMode.Regex,
            LanguageClass: LanguageClass.Regular,
            Title: $"Regular Expression: {pattern}",
            Description: $"Compiled regex '{pattern}' via Thompson NFA ({compiled.Nfa.States.Count} states) → Minimal DFA ({minDfa.States.Count} states) → Decidable TM Schema.",
            MinimalDfa: minDfa,
            Nfa: compiled.Nfa,
            Grammar: rg,
            Pda: null,
            TmSchema: schema,
            CompiledTm: compiledTm);
    }

    public static LanguageSpecCompilationResult CompileConstraint(string constraintSpec, IEnumerable<char>? alphabet = null)
    {
        var dfa = CompileConstraintToDfa(constraintSpec, alphabet);
        var rg = RegularGrammarConverter.DfaToRegularGrammar(dfa);
        var schema = DfaToDecidableTmSchema(dfa, $"Decider: {constraintSpec}");
        var compiledTm = TmSchemaCompiler.Compile(schema);

        return new LanguageSpecCompilationResult(
            Specification: constraintSpec,
            ResolvedMode: LanguageSpecMode.Constraint,
            LanguageClass: LanguageClass.Regular,
            Title: $"Regular Constraint: {constraintSpec}",
            Description: $"Synthesized exact minimal DFA ({dfa.States.Count} states) via Boolean DFA closure and converted to a decidable TM Schema.",
            MinimalDfa: dfa,
            Nfa: dfa.ToNfa(),
            Grammar: rg,
            Pda: null,
            TmSchema: schema,
            CompiledTm: compiledTm);
    }

    /// <summary>
    /// Compiles a Boolean regular constraint expression into an exact minimized DFA.
    /// </summary>
    public static Dfa CompileConstraintToDfa(string expression, IEnumerable<char>? explicitAlphabet = null)
    {
        var tokens = TokenizeConstraint(expression);
        if (tokens.Count == 0)
        {
            throw new FormatException("Constraint expression is empty.");
        }

        var alpha = InferConstraintAlphabet(tokens, explicitAlphabet);
        var parser = new ConstraintParser(tokens, alpha);
        var dfa = parser.ParseOr();
        if (!parser.IsAtEnd)
        {
            throw new FormatException($"Unexpected token '{parser.Current.Text}' in constraint expression.");
        }
        return dfa.Minimize();
    }

    /// <summary>
    /// Converts any DFA into a clean decidable Turing machine schema (> L_# R ... R_# E Y R / E N R)
    /// that halts with #y# on accepted strings and #n# on rejected strings.
    /// </summary>
    public static TmSchema DfaToDecidableTmSchema(Dfa dfa, string? name = null)
    {
        var min = dfa.Minimize().Complete();
        var blank = AutomataSymbols.DefaultBlank;
        var alpha = new SortedSet<char>(min.OrderedAlphabet) { 'y', 'n' };

        bool IsUniversalSink(string q) =>
            min.AcceptStates.Contains(q) &&
            min.OrderedAlphabet.All(a => min.Transitions[(q, a)] == q);

        bool IsDeadSink(string q) =>
            !min.AcceptStates.Contains(q) &&
            min.OrderedAlphabet.All(a => min.Transitions[(q, a)] == q);

        var builder = new TmSchemaBuilder(name ?? "DFA Decider")
            .Alphabet(alpha)
            .Blank(blank);

        // Start node moves from right delimiter '#' to cell 1
        builder.StartNode("start", "L_# R");

        var needAcceptMid = false;
        var needRejectMid = false;
        var needAcceptEnd = false;
        var needRejectEnd = false;

        var stepNodesNeeded = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        void RouteTransition(string fromNodeId, string targetDfaState, string symLabel)
        {
            if (IsUniversalSink(targetDfaState))
            {
                needAcceptMid = true;
                builder.Edge(fromNodeId, "accept_mid", symLabel);
            }
            else if (IsDeadSink(targetDfaState))
            {
                needRejectMid = true;
                builder.Edge(fromNodeId, "reject_mid", symLabel);
            }
            else
            {
                var targetNodeId = $"s_{targetDfaState}";
                if (stepNodesNeeded.Add(targetDfaState))
                {
                    queue.Enqueue(targetDfaState);
                }
                builder.Edge(fromNodeId, targetNodeId, symLabel);
            }
        }

        // Emit edges from start node (simulating startState on cell 1)
        foreach (var group in min.OrderedAlphabet.GroupBy(a => min.Transitions[(min.StartState, a)]))
        {
            var label = string.Join(", ", group.OrderBy(c => c));
            RouteTransition("start", group.Key, label);
        }

        if (min.AcceptStates.Contains(min.StartState))
        {
            needAcceptEnd = true;
            builder.Edge("start", "accept_end", blank.ToString());
        }
        else
        {
            needRejectEnd = true;
            builder.Edge("start", "reject_end", blank.ToString());
        }

        // Emit intermediate R nodes for reachable non-sink DFA states
        while (queue.Count > 0)
        {
            var q = queue.Dequeue();
            var nodeId = $"s_{q}";
            builder.Node(nodeId, "R");

            foreach (var group in min.OrderedAlphabet.GroupBy(a => min.Transitions[(q, a)]))
            {
                var label = string.Join(", ", group.OrderBy(c => c));
                RouteTransition(nodeId, group.Key, label);
            }

            if (min.AcceptStates.Contains(q))
            {
                needAcceptEnd = true;
                builder.Edge(nodeId, "accept_end", blank.ToString());
            }
            else
            {
                needRejectEnd = true;
                builder.Edge(nodeId, "reject_end", blank.ToString());
            }
        }

        if (needAcceptMid) builder.Node("accept_mid", "R_# E Y R");
        if (needRejectMid) builder.Node("reject_mid", "R_# E N R");
        if (needAcceptEnd) builder.Node("accept_end", "E Y R");
        if (needRejectEnd) builder.Node("reject_end", "E N R");

        return builder.Build();
    }

    // --- Parametric Non-Regular Languages & Transducers ---

    private static LanguageSpecCompilationResult? TryCompileParametric(string rawSpec)
    {
        var norm = Regex.Replace(rawSpec.Trim().ToLowerInvariant(), @"\s+", " ");
        var compact = norm.Replace(" ", string.Empty);

        // 1. a^n b^n c^n
        if (compact is "a^nb^nc^n" or "anbncn" or "{a^nb^nc^n}")
        {
            var schema = BuildAnBnCnSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextSensitive,
                "L = { aⁿbⁿcⁿ | n ≥ 0 }",
                "Context-sensitive language decided by marking matching triples (a, b, c) -> (X, Q, Z).",
                null, null, schema);
        }

        // 2. a^2n b^m c^n
        if (compact is "a^2nb^mc^n" or "a^(2n)b^mc^n" or "a2nbmcn" or "{a^2nb^mc^n}")
        {
            var cfg = Cfg.ParseFromText("S -> aaSc | B\nB -> bB | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildA2nBmCnSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { a²ⁿbᵐcⁿ | n, m ≥ 0 }",
                "Context-free language matching two leading a's per trailing c around an arbitrary block of b's.",
                cfg, pda, schema);
        }

        // 3. a^n b^n c^m
        if (compact is "a^nb^nc^m" or "anbncm" or "{a^nb^nc^m}")
        {
            var cfg = Cfg.ParseFromText("S -> AC\nA -> aAb | e\nC -> cC | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildAnBnCmSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { aⁿbⁿcᵐ | n, m ≥ 0 }",
                "Context-free language matching equal a's and b's followed by any number of c's.",
                cfg, pda, schema);
        }

        // 4. a^2n b^n
        if (compact is "a^2nb^n" or "a^(2n)b^n" or "a2nbn" or "{a^2nb^n}")
        {
            var cfg = Cfg.ParseFromText("S -> aaSb | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: f\n((s, aa, e), (s, a))\n((s, e, e), (f, e))\n((f, b, a), (f, e))");
            var schema = BuildA2nBnSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { a²ⁿbⁿ | n ≥ 0 }",
                "Context-free language with twice as many leading a's as trailing b's.",
                cfg, pda, schema);
        }

        // 5. a^n b^n
        if (compact is "a^nb^n" or "anbn" or "{a^nb^n}")
        {
            var cfg = Cfg.ParseFromText("S -> aSb | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: f\n((s, a, e), (s, a))\n((s, e, e), (f, e))\n((f, b, a), (f, e))");
            var schema = BuildAnBnSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { aⁿbⁿ | n ≥ 0 }",
                "Classic context-free language decided by stripping matching outer (a, b) pairs via R_# S_L.",
                cfg, pda, schema);
        }

        // 6. even palindromes
        if (norm is "even palindromes" or "even palindrome" || compact is "ww^r" or "evenpalindromes")
        {
            var cfg = Cfg.ParseFromText("S -> aSa | bSb | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: f\n((s, a, e), (s, a))\n((s, b, e), (s, b))\n((s, e, e), (f, e))\n((f, a, a), (f, e))\n((f, b, b), (f, e))");
            var schema = BuildPalindromeSchema(evenOnly: true);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "Even Palindromes { w wᴿ | w ∈ {a, b}* }",
                "Even-length palindromes decided by peeling matching outer symbols with R_# S_L.",
                cfg, pda, schema);
        }

        // 7. palindromes (w = w^R)
        if (norm is "palindromes" or "palindrome" || compact is "w=w^r" or "palindromes(w=w^r)")
        {
            var cfg = Cfg.ParseFromText("S -> aSa | bSb | a | b | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: f\n((s, a, e), (s, a))\n((s, b, e), (s, b))\n((s, e, e), (f, e))\n((s, a, e), (f, e))\n((s, b, e), (f, e))\n((f, a, a), (f, e))\n((f, b, b), (f, e))");
            var schema = BuildPalindromeSchema(evenOnly: false);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "Palindromes { w ∈ {a, b}* | w = wᴿ }",
                "Palindromes of any length decided by peeling outer matching characters via # R_# S_L.",
                cfg, pda, schema);
        }

        // 8. balanced ()
        if (norm is "balanced ()" or "balanced parens" or "balanced parentheses" or "dyck")
        {
            var cfg = Cfg.ParseFromText("S -> (S) | SS | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: s\n((s, (, e), (s, ()) \n((s, ), (), (s, e))");
            var schema = BuildBalancedParensSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "Balanced Parentheses",
                "Dyck language of properly nested parentheses over {(, )}.",
                cfg, pda, schema);
        }

        // 9. #a > #b
        if (compact is "#a>#b" or "count(a)>count(b)" || norm is "more a than b")
        {
            var cfg = Cfg.ParseFromText("S -> E a S | E a E\nE -> a E b E | b E a E | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildMoreAThanBSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { w ∈ {a, b}* | #a(w) > #b(w) }",
                "Strings containing strictly more a's than b's, decided by pairwise cancellation.",
                cfg, pda, schema);
        }

        // 10. #a = #b
        if (compact is "#a=#b" or "#a==#b" or "count(a)=count(b)" || norm is "equal a and b" or "equal #a and #b")
        {
            var cfg = Cfg.ParseFromText("S -> aSbS | bSaS | e");
            var pda = PushdownAutomaton.ParseTransitions(
                "start: s\naccept: s\n((s, a, b), (s, e))\n((s, a, e), (s, a))\n((s, b, a), (s, e))\n((s, b, e), (s, b))");
            var schema = BuildEqualABSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { w ∈ {a, b}* | #a(w) = #b(w) }",
                "Strings with equal counts of a and b, decided by pairwise cancellation.",
                cfg, pda, schema);
        }

        // 11. invert a and b / complement
        if (norm is "invert a and b" or "invert a/b" or "invert" or "complement" or "complement a and b" or "flip a and b")
        {
            var schema = BuildInvertABSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Invert a ↔ b Transducer",
                "Replaces every 'a' with 'b' and every 'b' with 'a' across the tape (#aab# → #bba#).",
                null, null, schema);
        }

        // 12. shift left (S_L)
        if (norm is "shift left" or "s_l" or "shift left (s_l)" || compact is "shiftleft" or "s_l")
        {
            var schema = BuildShiftLeftSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Shift Left Macro (S_L)",
                "Shifts the non-blank block immediately left of the head one cell to the left, ending in L #.",
                null, null, schema);
        }

        // 13. shift right (S_R)
        if (norm is "shift right" or "s_r" or "shift right (s_r)" || compact is "shiftright" or "s_r")
        {
            var schema = BuildShiftRightSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Shift Right Macro (S_R)",
                "Shifts the non-blank block immediately left of the head one cell to the right.",
                null, null, schema);
        }

        // 14. AND transducer
        if (norm is "and" or "binary and" or "bitwise and")
        {
            var schema = BuildBinaryAndSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Binary AND Transducer",
                "Computes logical AND across binary input over {0, 1}+ (outputs #1# iff all bits are 1, else #0#).",
                null, null, schema);
        }

        // 15. XOR transducer
        if (norm is "xor" or "binary xor" or "bitwise xor")
        {
            var schema = BuildBinaryXorSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Binary XOR Transducer",
                "Computes logical XOR / odd parity across binary input over {0, 1}* (outputs #1# or #0#).",
                null, null, schema);
        }

        // 16. m >= n
        if (compact is "m>=n" or "a^mb^n(m>=n)" or "a^mb^nm>=n" || norm is "m >= n")
        {
            var cfg = Cfg.ParseFromText("S -> aSb | aS | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildMGeqNSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { aᵐbⁿ | m ≥ n ≥ 0 }",
                "Context-free comparison language accepting aᵐbⁿ whenever m ≥ n.",
                cfg, pda, schema);
        }

        // 17. m <= n
        if (compact is "m<=n" or "a^mb^n(m<=n)" or "a^mb^nm<=n" || norm is "m <= n")
        {
            var cfg = Cfg.ParseFromText("S -> aSb | Sb | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildMLeqNSchema();
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                "L = { aᵐbⁿ | 0 ≤ m ≤ n }",
                "Context-free comparison language accepting aᵐbⁿ whenever m ≤ n.",
                cfg, pda, schema);
        }

        // 18. snake_case to camelCase
        if (norm is "snake_case to camelcase" or "snakecase to camelcase" or "snake to camel")
        {
            var schema = BuildSnakeToCamelSchema();
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "snake_case → camelCase Transducer",
                "Converts underscore-separated lowercase identifiers to camelCase in place via capitalize + R_# S_L.",
                null, null, schema);
        }

        return null;
    }

    private static LanguageSpecCompilationResult BuildParametricResult(
        string spec,
        LanguageClass langClass,
        string title,
        string description,
        Cfg? cfg,
        PushdownAutomaton? pda,
        TmSchema schema)
    {
        var compiled = TmSchemaCompiler.Compile(schema);
        return new LanguageSpecCompilationResult(
            Specification: spec,
            ResolvedMode: LanguageSpecMode.Parametric,
            LanguageClass: langClass,
            Title: title,
            Description: description,
            MinimalDfa: null,
            Nfa: null,
            Grammar: cfg,
            Pda: pda,
            TmSchema: schema,
            CompiledTm: compiled);
    }

    // --- Reusable Parametric TM Schema Builders ---

    public static TmSchema BuildAnBnSchema() =>
        new TmSchemaBuilder("Decider for a^n b^n")
            .Alphabet('a', 'b', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a", "# R_# S_L")
            .Node("check_b", "L")
            .Node("erase_b", "#")
            .Node("acc_empty", "Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Node("rej_zero", "R N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a", "a")
            .Edge("start", "rej_mid", "b")
            .Edge("peel_a", "check_b", "*")
            .Edge("check_b", "erase_b", "b")
            .Edge("check_b", "rej_end", "a")
            .Edge("check_b", "rej_zero", "#")
            .Edge("erase_b", "start", "*")
            .Build();

    public static TmSchema BuildA2nBnSchema() =>
        new TmSchemaBuilder("Decider for a^2n b^n")
            .Alphabet('a', 'b', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a1", "# R_# S_L")
            .Node("find_a2", "L_# R")
            .Node("peel_a2", "# R_# S_L")
            .Node("check_b", "L")
            .Node("erase_b", "#")
            .Node("acc_empty", "Y R")
            .Node("rej_empty", "N R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Node("rej_zero", "R N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a1", "a")
            .Edge("start", "rej_mid", "b")
            .Edge("peel_a1", "find_a2", "*")
            .Edge("find_a2", "peel_a2", "a")
            .Edge("find_a2", "rej_mid", "b")
            .Edge("find_a2", "rej_empty", "#")
            .Edge("peel_a2", "check_b", "*")
            .Edge("check_b", "erase_b", "b")
            .Edge("check_b", "rej_end", "a")
            .Edge("check_b", "rej_zero", "#")
            .Edge("erase_b", "start", "*")
            .Build();

    public static TmSchema BuildPalindromeSchema(bool evenOnly = false) =>
        new TmSchemaBuilder(evenOnly ? "Even Palindrome Decider" : "Palindrome Decider")
            .Alphabet('a', 'b', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a", "# R_# S_L")
            .Node("match_a", "L")
            .Node("peel_b", "# R_# S_L")
            .Node("match_b", "L")
            .Node("erase_last", "#")
            .Node("acc_empty", "Y R")
            .Node("mid_single", evenOnly ? "R N R" : "R Y R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a", "a")
            .Edge("start", "peel_b", "b")
            .Edge("peel_a", "match_a", "*")
            .Edge("match_a", "erase_last", "a")
            .Edge("match_a", "mid_single", "#")
            .Edge("match_a", "rej_end", "b")
            .Edge("peel_b", "match_b", "*")
            .Edge("match_b", "erase_last", "b")
            .Edge("match_b", "mid_single", "#")
            .Edge("match_b", "rej_end", "a")
            .Edge("erase_last", "start", "*")
            .Build();

    public static TmSchema BuildAnBnCnSchema() =>
        new TmSchemaBuilder("Decider for a^n b^n c^n")
            .Alphabet('a', 'b', 'c', 'X', 'Q', 'Z', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("mark_a", "X")
            .Node("find_b", "R")
            .Node("mark_b", "Q")
            .Node("find_c", "R")
            .Node("mark_c", "Z")
            .Node("back", "L_# R")
            .Node("skip_x", "R")
            .Node("verify_q", "R")
            .Node("verify_z", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "mark_a", "a")
            .Edge("start", "rej_mid", "other")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", "a, Q")
            .Edge("find_b", "mark_b", "b")
            .Edge("find_b", "rej_end", "#")
            .Edge("find_b", "rej_mid", "other")
            .Edge("mark_b", "find_c", "*")
            .Edge("find_c", "find_c", "b, Z")
            .Edge("find_c", "mark_c", "c")
            .Edge("find_c", "rej_end", "#")
            .Edge("find_c", "rej_mid", "other")
            .Edge("mark_c", "back", "*")
            .Edge("back", "skip_x", "X")
            .Edge("back", "rej_mid", "other")
            .Edge("skip_x", "skip_x", "X")
            .Edge("skip_x", "mark_a", "a")
            .Edge("skip_x", "verify_q", "Q")
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "rej_mid", "other")
            .Edge("verify_q", "verify_q", "Q")
            .Edge("verify_q", "verify_z", "Z")
            .Edge("verify_q", "rej_end", "#")
            .Edge("verify_q", "rej_mid", "other")
            .Edge("verify_z", "verify_z", "Z")
            .Edge("verify_z", "acc_end", "#")
            .Edge("verify_z", "rej_mid", "other")
            .Build();

    public static TmSchema BuildA2nBmCnSchema() =>
        new TmSchemaBuilder("Decider for a^2n b^m c^n")
            .Alphabet('a', 'b', 'c', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a1", "# R_# S_L")
            .Node("find_a2", "L_# R")
            .Node("peel_a2", "# R_# S_L")
            .Node("check_c", "L")
            .Node("erase_c", "#")
            .Node("scan_b", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_empty", "N R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Node("rej_zero", "R N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a1", "a")
            .Edge("start", "scan_b", "b")
            .Edge("start", "rej_mid", "c")
            .Edge("peel_a1", "find_a2", "*")
            .Edge("find_a2", "peel_a2", "a")
            .Edge("find_a2", "rej_empty", "#")
            .Edge("find_a2", "rej_mid", "b, c")
            .Edge("peel_a2", "check_c", "*")
            .Edge("check_c", "erase_c", "c")
            .Edge("check_c", "rej_zero", "#")
            .Edge("check_c", "rej_end", "a, b")
            .Edge("erase_c", "start", "*")
            .Edge("scan_b", "scan_b", "b")
            .Edge("scan_b", "acc_end", "#")
            .Edge("scan_b", "rej_mid", "a, c")
            .Build();

    public static TmSchema BuildAnBnCmSchema() =>
        new TmSchemaBuilder("Decider for a^n b^n c^m")
            .Alphabet('a', 'b', 'c', 'X', 'Q', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("mark_a", "X")
            .Node("find_b", "R")
            .Node("mark_b", "Q")
            .Node("back", "L_# R")
            .Node("skip_x", "R")
            .Node("verify_q", "R")
            .Node("scan_c", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "mark_a", "a")
            .Edge("start", "scan_c", "c")
            .Edge("start", "rej_mid", "other")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", "a, Q")
            .Edge("find_b", "mark_b", "b")
            .Edge("find_b", "rej_end", "#")
            .Edge("find_b", "rej_mid", "other")
            .Edge("mark_b", "back", "*")
            .Edge("back", "skip_x", "X")
            .Edge("back", "rej_mid", "other")
            .Edge("skip_x", "skip_x", "X")
            .Edge("skip_x", "mark_a", "a")
            .Edge("skip_x", "verify_q", "Q")
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "rej_mid", "other")
            .Edge("verify_q", "verify_q", "Q")
            .Edge("verify_q", "scan_c", "c")
            .Edge("verify_q", "acc_end", "#")
            .Edge("verify_q", "rej_mid", "other")
            .Edge("scan_c", "scan_c", "c")
            .Edge("scan_c", "acc_end", "#")
            .Edge("scan_c", "rej_mid", "other")
            .Build();

    public static TmSchema BuildBalancedParensSchema() =>
        new TmSchemaBuilder("Balanced Parentheses Decider")
            .Alphabet('(', ')', 'X', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("scan_r", "R")
            .Node("mark_r", "X")
            .Node("find_l", "L")
            .Node("mark_l", "X")
            .Node("verify_start", "L_# R")
            .Node("verify_loop", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "scan_r", "(")
            .Edge("start", "rej_mid", ")")
            .Edge("start", "scan_r", "X")
            .Edge("scan_r", "scan_r", "(, X")
            .Edge("scan_r", "mark_r", ")")
            .Edge("scan_r", "verify_start", "#")
            .Edge("mark_r", "find_l", "*")
            .Edge("find_l", "find_l", "X")
            .Edge("find_l", "mark_l", "(")
            .Edge("find_l", "rej_mid", "#, )")
            .Edge("mark_l", "scan_r", "*")
            .Edge("verify_start", "verify_loop", "X")
            .Edge("verify_start", "acc_end", "#")
            .Edge("verify_start", "rej_mid", "(, )")
            .Edge("verify_loop", "verify_loop", "X")
            .Edge("verify_loop", "acc_end", "#")
            .Edge("verify_loop", "rej_mid", "(, )")
            .Build();

    public static TmSchema BuildEqualABSchema() =>
        new TmSchemaBuilder("Decider for #a = #b")
            .Alphabet('a', 'b', 'X', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("skip_x", "R")
            .Node("mark_a", "X")
            .Node("find_b", "R")
            .Node("mark_b", "X")
            .Node("find_a", "R")
            .Node("pair_done", "X")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "skip_x", "X")
            .Edge("start", "mark_a", "a")
            .Edge("start", "mark_b", "b")
            .Edge("skip_x", "skip_x", "X")
            .Edge("skip_x", "acc_end", "#")
            .Edge("skip_x", "mark_a", "a")
            .Edge("skip_x", "mark_b", "b")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", "a, X")
            .Edge("find_b", "pair_done", "b")
            .Edge("find_b", "rej_end", "#")
            .Edge("mark_b", "find_a", "*")
            .Edge("find_a", "find_a", "b, X")
            .Edge("find_a", "pair_done", "a")
            .Edge("find_a", "rej_end", "#")
            .Edge("pair_done", "start", "*")
            .Build();

    public static TmSchema BuildMoreAThanBSchema() =>
        new TmSchemaBuilder("Decider for #a > #b")
            .Alphabet('a', 'b', 'X', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("skip_x", "R")
            .Node("mark_a", "X")
            .Node("find_b", "R")
            .Node("mark_b", "X")
            .Node("find_a", "R")
            .Node("pair_done", "X")
            .Node("acc_end", "E Y R")
            .Node("rej_empty", "N R")
            .Node("rej_end", "E N R")
            .Edge("start", "rej_empty", "#")
            .Edge("start", "skip_x", "X")
            .Edge("start", "mark_a", "a")
            .Edge("start", "mark_b", "b")
            .Edge("skip_x", "skip_x", "X")
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "mark_a", "a")
            .Edge("skip_x", "mark_b", "b")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", "a, X")
            .Edge("find_b", "pair_done", "b")
            .Edge("find_b", "acc_end", "#")
            .Edge("mark_b", "find_a", "*")
            .Edge("find_a", "find_a", "b, X")
            .Edge("find_a", "pair_done", "a")
            .Edge("find_a", "rej_end", "#")
            .Edge("pair_done", "start", "*")
            .Build();

    public static TmSchema BuildInvertABSchema() =>
        new TmSchemaBuilder("Invert a and b")
            .Alphabet('a', 'b')
            .StartNode("rewind", "L_#")
            .Node("step", "R")
            .Node("write_b", "b")
            .Node("write_a", "a")
            .Edge("rewind", "step", "*")
            .Edge("step", "write_b", "a")
            .Edge("step", "write_a", "b")
            .Edge("write_b", "step", "*")
            .Edge("write_a", "step", "*")
            .Build();

    public static TmSchema BuildShiftLeftSchema() =>
        new TmSchemaBuilder("Shift Left (S_L)")
            .Alphabet('a', 'b')
            .StartNode("shift_l", "S_L")
            .Build();

    public static TmSchema BuildShiftRightSchema() =>
        new TmSchemaBuilder("Shift Right (S_R)")
            .Alphabet('a', 'b')
            .StartNode("shift_r", "S_R")
            .Build();

    public static TmSchema BuildBinaryAndSchema() =>
        new TmSchemaBuilder("Binary AND Transducer")
            .Alphabet('0', '1')
            .StartNode("start", "L_# R")
            .Node("scan", "R")
            .Node("out_zero_mid", "R_# E 0 R")
            .Node("out_zero_empty", "0 R")
            .Node("out_one", "E 1 R")
            .Edge("start", "scan", "1")
            .Edge("start", "out_zero_mid", "0")
            .Edge("start", "out_zero_empty", "#")
            .Edge("scan", "scan", "1")
            .Edge("scan", "out_zero_mid", "0")
            .Edge("scan", "out_one", "#")
            .Build();

    public static TmSchema BuildBinaryXorSchema() =>
        new TmSchemaBuilder("Binary XOR Transducer")
            .Alphabet('0', '1')
            .StartNode("start", "L_# R")
            .Node("even", "R")
            .Node("odd", "R")
            .Node("out_zero", "E 0 R")
            .Node("out_one", "E 1 R")
            .Edge("start", "even", "0")
            .Edge("start", "odd", "1")
            .Edge("start", "out_zero", "#")
            .Edge("even", "even", "0")
            .Edge("even", "odd", "1")
            .Edge("even", "out_zero", "#")
            .Edge("odd", "odd", "0")
            .Edge("odd", "even", "1")
            .Edge("odd", "out_one", "#")
            .Build();

    public static TmSchema BuildMGeqNSchema() =>
        new TmSchemaBuilder("Decider for a^m b^n (m >= n)")
            .Alphabet('a', 'b', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a", "# R_# S_L")
            .Node("check_back", "L")
            .Node("erase_b", "#")
            .Node("verify_a_start", "L_# R")
            .Node("verify_a", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_zero", "R Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a", "a")
            .Edge("start", "rej_mid", "b")
            .Edge("peel_a", "check_back", "*")
            .Edge("check_back", "erase_b", "b")
            .Edge("check_back", "verify_a_start", "a")
            .Edge("check_back", "acc_zero", "#")
            .Edge("erase_b", "start", "*")
            .Edge("verify_a_start", "verify_a", "a")
            .Edge("verify_a_start", "acc_end", "#")
            .Edge("verify_a_start", "rej_mid", "b")
            .Edge("verify_a", "verify_a", "a")
            .Edge("verify_a", "acc_end", "#")
            .Edge("verify_a", "rej_mid", "b")
            .Build();

    public static TmSchema BuildMLeqNSchema() =>
        new TmSchemaBuilder("Decider for a^m b^n (m <= n)")
            .Alphabet('a', 'b', 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a", "# R_# S_L")
            .Node("check_back", "L")
            .Node("erase_b", "#")
            .Node("verify_b", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Node("rej_zero", "R N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a", "a")
            .Edge("start", "verify_b", "b")
            .Edge("peel_a", "check_back", "*")
            .Edge("check_back", "erase_b", "b")
            .Edge("check_back", "rej_end", "a")
            .Edge("check_back", "rej_zero", "#")
            .Edge("erase_b", "start", "*")
            .Edge("verify_b", "verify_b", "b")
            .Edge("verify_b", "acc_end", "#")
            .Edge("verify_b", "rej_mid", "a")
            .Build();

    public static TmSchema BuildSnakeToCamelSchema()
    {
        var alpha = new List<char> { '_' };
        for (var c = 'a'; c <= 'z'; c++) alpha.Add(c);
        for (var c = 'A'; c <= 'Z'; c++) alpha.Add(c);

        var builder = new TmSchemaBuilder("snake_case to camelCase")
            .Alphabet(alpha)
            .StartNode("start", "L_# R")
            .Node("scan", "R")
            .Node("erase_us", "# R")
            .Node("shift", "R_# S_L");

        // On start / scan: '_' triggers erase_us; '#' halts; all other letters keep scanning right
        builder.Edge("start", "erase_us", "_");
        builder.Edge("start", "scan", "~#");
        builder.Edge("scan", "erase_us", "_");
        builder.Edge("scan", "scan", "~#");

        // Wait: '~#' matches '_' only if exact '_' didn't match first, and exact matching has priority 1!
        // On erase_us (now scanning char right after erased '_'):
        //   - lowercase 'a'..'z' -> capitalize via quoted write 'A'..'Z' then shift left into erased '_'
        //   - uppercase 'A'..'Z' -> directly shift
        //   - '_' -> another underscore, shift first and re-scan
        //   - '#' -> trailing underscore already erased; halt!
        for (var c = 'a'; c <= 'z'; c++)
        {
            var upper = char.ToUpperInvariant(c);
            var capNode = $"cap_{c}";
            builder.Node(capNode, $"'{upper}'");
            builder.Edge("erase_us", capNode, c.ToString());
            builder.Edge(capNode, "shift", "*");
        }

        // Uppercase letter or extra underscore after '_': shift left without altering case
        var upperList = string.Join(", ", Enumerable.Range('A', 26).Select(i => ((char)i).ToString()));
        builder.Edge("erase_us", "shift", $"{upperList}, _");
        builder.Edge("shift", "start", "*");

        return builder.Build();
    }

    // --- Constraint Expression Parser & Atomic DFA Builders ---

    private enum ConstraintTokenKind { Atom, And, Or, Not, LParen, RParen }
    private sealed record ConstraintToken(ConstraintTokenKind Kind, string Text);

    private static List<ConstraintToken> TokenizeConstraint(string input)
    {
        var tokens = new List<ConstraintToken>();
        var i = 0;
        var atomChars = new List<char>();

        void FlushAtom()
        {
            if (atomChars.Count == 0) return;
            var text = new string(atomChars.ToArray()).Trim();
            atomChars.Clear();
            if (text.Length > 0)
            {
                tokens.Add(new ConstraintToken(ConstraintTokenKind.Atom, text));
            }
        }

        bool MatchesWord(string word)
        {
            if (i + word.Length > input.Length) return false;
            if (!input.AsSpan(i, word.Length).Equals(word.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var beforeOk = i == 0 || char.IsWhiteSpace(input[i - 1]) || input[i - 1] is '(' or ')';
            var afterIdx = i + word.Length;
            var afterOk = afterIdx == input.Length || char.IsWhiteSpace(input[afterIdx]) || input[afterIdx] is '(' or ')';
            return beforeOk && afterOk;
        }

        while (i < input.Length)
        {
            var c = input[i];
            if (c == '(')
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.LParen, "("));
                i++;
                continue;
            }
            if (c == ')')
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.RParen, ")"));
                i++;
                continue;
            }
            if (c == '&' && i + 1 < input.Length && input[i + 1] == '&')
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.And, "AND"));
                i += 2;
                continue;
            }
            if (c == '|' && i + 1 < input.Length && input[i + 1] == '|')
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.Or, "OR"));
                i += 2;
                continue;
            }
            if (c is '!' or '~')
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.Not, "NOT"));
                i++;
                continue;
            }
            if (MatchesWord("AND"))
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.And, "AND"));
                i += 3;
                continue;
            }
            if (MatchesWord("OR"))
            {
                FlushAtom();
                tokens.Add(new ConstraintToken(ConstraintTokenKind.Or, "OR"));
                i += 2;
                continue;
            }
            // Only treat NOT as prefix Boolean operator when NOT part of "does not ..."
            if (MatchesWord("NOT"))
            {
                var currentAtom = new string(atomChars.ToArray()).Trim();
                if (!currentAtom.EndsWith("does", StringComparison.OrdinalIgnoreCase))
                {
                    FlushAtom();
                    tokens.Add(new ConstraintToken(ConstraintTokenKind.Not, "NOT"));
                    i += 3;
                    continue;
                }
            }

            atomChars.Add(c);
            i++;
        }

        FlushAtom();
        return tokens;
    }

    private static SortedSet<char> InferConstraintAlphabet(List<ConstraintToken> tokens, IEnumerable<char>? explicitAlphabet)
    {
        var discovered = new SortedSet<char>();
        foreach (var tok in tokens.Where(t => t.Kind == ConstraintTokenKind.Atom))
        {
            CollectAtomSymbols(tok.Text, discovered);
        }

        if (explicitAlphabet is not null)
        {
            var merged = new SortedSet<char>(explicitAlphabet);
            foreach (var c in discovered)
            {
                merged.Add(c);
            }
            if (merged.Count > 0) return merged;
        }

        if (discovered.Count == 0)
        {
            return new SortedSet<char> { 'a', 'b' };
        }

        if (discovered.All(c => c is '0' or '1'))
        {
            discovered.Add('0');
            discovered.Add('1');
            return discovered;
        }

        discovered.Add('a');
        discovered.Add('b');
        return discovered;
    }

    private static void CollectAtomSymbols(string atom, SortedSet<char> symbols)
    {
        var s = Regex.Replace(atom.Trim(), @"\s+", " ");
        var m = Regex.Match(s, @"^(?:(?:does not|doesn't)\s+)?(?:starts? with|begins? with|ends? with|contains?|has substring)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var arg = Unquote(m.Groups[1].Value.Trim());
            if (!AutomataSymbols.IsEmptyToken(arg))
            {
                foreach (var c in arg) symbols.Add(c);
            }
            return;
        }

        var countMatch = Regex.Match(s, @"^(?:even|odd)(?:\s+number\s+of)?\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (countMatch.Success)
        {
            symbols.Add(countMatch.Groups[1].Value[0]);
            return;
        }

        var numMatch = Regex.Match(s, @"^(?:at least|at most|exactly|>=|<=|=)\s+\d+\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            symbols.Add(numMatch.Groups[1].Value[0]);
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }
        return value;
    }

    private sealed class ConstraintParser
    {
        private readonly List<ConstraintToken> _tokens;
        private readonly SortedSet<char> _alpha;
        private int _pos;

        public ConstraintParser(List<ConstraintToken> tokens, SortedSet<char> alpha)
        {
            _tokens = tokens;
            _alpha = alpha;
        }

        public bool IsAtEnd => _pos >= _tokens.Count;
        public ConstraintToken Current => _tokens[_pos];

        public Dfa ParseOr()
        {
            var left = ParseAnd();
            while (!IsAtEnd && Current.Kind == ConstraintTokenKind.Or)
            {
                _pos++;
                var right = ParseAnd();
                left = FaOperations.Union(left, right);
            }
            return left;
        }

        private Dfa ParseAnd()
        {
            var left = ParseNot();
            while (!IsAtEnd && Current.Kind == ConstraintTokenKind.And)
            {
                _pos++;
                var right = ParseNot();
                left = FaOperations.Intersection(left, right);
            }
            return left;
        }

        private Dfa ParseNot()
        {
            if (!IsAtEnd && Current.Kind == ConstraintTokenKind.Not)
            {
                _pos++;
                var inner = ParseNot();
                return FaOperations.Complement(inner, _alpha);
            }
            return ParsePrimary();
        }

        private Dfa ParsePrimary()
        {
            if (IsAtEnd)
            {
                throw new FormatException("Unexpected end of constraint expression.");
            }

            if (Current.Kind == ConstraintTokenKind.LParen)
            {
                _pos++;
                var inner = ParseOr();
                if (IsAtEnd || Current.Kind != ConstraintTokenKind.RParen)
                {
                    throw new FormatException("Missing closing ')' in constraint expression.");
                }
                _pos++;
                return inner;
            }

            if (Current.Kind == ConstraintTokenKind.Atom)
            {
                var atom = Current.Text;
                _pos++;
                return BuildAtomicDfa(atom, _alpha);
            }

            throw new FormatException($"Unexpected token '{Current.Text}' in constraint expression.");
        }
    }

    private static Dfa BuildAtomicDfa(string rawAtom, SortedSet<char> alpha)
    {
        var atom = Regex.Replace(rawAtom.Trim(), @"\s+", " ");
        var sigmaStar = "(" + string.Join("|", alpha.Select(EscapeRegexChar)) + ")*";

        string EscapeWord(string w)
        {
            if (AutomataSymbols.IsEmptyToken(w)) return AutomataSymbols.Empty;
            return string.Concat(w.Select(EscapeRegexChar));
        }

        // Negated prefix/suffix/substring
        var m = Regex.Match(atom, @"^(?:does not|doesn't)\s+(?:start|begin)\s+with\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            var pos = RegexCompiler.CompileToMinimalDfa($"{EscapeWord(x)}{sigmaStar}", alpha);
            return FaOperations.Complement(pos, alpha);
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+end\s+with\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            var pos = RegexCompiler.CompileToMinimalDfa($"{sigmaStar}{EscapeWord(x)}", alpha);
            return FaOperations.Complement(pos, alpha);
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+contain\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            var pos = RegexCompiler.CompileToMinimalDfa($"{sigmaStar}{EscapeWord(x)}{sigmaStar}", alpha);
            return FaOperations.Complement(pos, alpha);
        }

        // Positive prefix/suffix/substring
        m = Regex.Match(atom, @"^(?:starts? with|begins? with)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            return RegexCompiler.CompileToMinimalDfa($"{EscapeWord(x)}{sigmaStar}", alpha);
        }

        m = Regex.Match(atom, @"^ends? with\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            return RegexCompiler.CompileToMinimalDfa($"{sigmaStar}{EscapeWord(x)}", alpha);
        }

        m = Regex.Match(atom, @"^(?:contains?|has substring)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            return RegexCompiler.CompileToMinimalDfa($"{sigmaStar}{EscapeWord(x)}{sigmaStar}", alpha);
        }

        // Even / odd length
        if (atom.Equals("even length", StringComparison.OrdinalIgnoreCase))
        {
            return BuildLengthModDfa(alpha, 2, 0);
        }
        if (atom.Equals("odd length", StringComparison.OrdinalIgnoreCase))
        {
            return BuildLengthModDfa(alpha, 2, 1);
        }

        // Even / odd count of symbol
        m = Regex.Match(atom, @"^(even|odd)(?:\s+number\s+of)?\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var isOdd = m.Groups[1].Value.Equals("odd", StringComparison.OrdinalIgnoreCase);
            var sym = m.Groups[2].Value[0];
            return BuildSymbolModDfa(alpha, sym, 2, isOdd ? 1 : 0);
        }

        // Length divisible by k
        m = Regex.Match(atom, @"^length\s+(?:divisible\s+by|multiple\s+of)\s+(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var modK) && modK >= 1)
        {
            return BuildLengthModDfa(alpha, modK, 0);
        }

        // Exact / at least / at most length
        m = Regex.Match(atom, @"^length\s*(=|>=|<=)\s*(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var lenK))
        {
            return BuildLengthCompareDfa(alpha, m.Groups[1].Value, lenK);
        }

        // At least / at most / exactly k occurrences of symbol c
        m = Regex.Match(atom, @"^(at least|at most|exactly|>=|<=|=)\s+(\d+)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var countK))
        {
            var op = m.Groups[1].Value.ToLowerInvariant();
            var sym = m.Groups[3].Value[0];
            return BuildSymbolCountDfa(alpha, sym, op, countK);
        }

        throw new FormatException($"Unrecognized atomic constraint: '{rawAtom}'.");
    }

    private static string EscapeRegexChar(char c) =>
        c is '(' or ')' or '|' or '∪' or '*' or '+' or '?' or '.' or '\\' or 'e' or 'ε' or 'λ'
            ? $"\\{c}"
            : c.ToString();

    private static Dfa BuildLengthModDfa(SortedSet<char> alpha, int k, int targetRem)
    {
        var states = Enumerable.Range(0, k).Select(i => $"q{i}").ToList();
        var trans = new Dictionary<(string State, char Symbol), string>();
        for (var i = 0; i < k; i++)
        {
            var next = $"q{(i + 1) % k}";
            foreach (var c in alpha)
            {
                trans[($"q{i}", c)] = next;
            }
        }
        return new Dfa(states, alpha, trans, "q0", new[] { $"q{targetRem % k}" }).Minimize();
    }

    private static Dfa BuildSymbolModDfa(SortedSet<char> alpha, char targetSym, int k, int targetRem)
    {
        var states = Enumerable.Range(0, k).Select(i => $"q{i}").ToList();
        var trans = new Dictionary<(string State, char Symbol), string>();
        for (var i = 0; i < k; i++)
        {
            foreach (var c in alpha)
            {
                var nextIdx = c == targetSym ? (i + 1) % k : i;
                trans[($"q{i}", c)] = $"q{nextIdx}";
            }
        }
        return new Dfa(states, alpha, trans, "q0", new[] { $"q{targetRem % k}" }).Minimize();
    }

    private static Dfa BuildLengthCompareDfa(SortedSet<char> alpha, string op, int k)
    {
        var maxState = k + 1;
        var states = Enumerable.Range(0, maxState + 1).Select(i => $"q{i}").ToList();
        var trans = new Dictionary<(string State, char Symbol), string>();
        for (var i = 0; i <= maxState; i++)
        {
            var next = $"q{System.Math.Min(i + 1, maxState)}";
            foreach (var c in alpha)
            {
                trans[($"q{i}", c)] = next;
            }
        }

        var accept = new List<string>();
        for (var i = 0; i <= maxState; i++)
        {
            var ok = op switch
            {
                "=" => i == k,
                ">=" => i >= k,
                "<=" => i <= k,
                _ => false
            };
            if (ok) accept.Add($"q{i}");
        }

        return new Dfa(states, alpha, trans, "q0", accept).Minimize();
    }

    private static Dfa BuildSymbolCountDfa(SortedSet<char> alpha, char targetSym, string op, int k)
    {
        var cap = k + 1;
        var states = Enumerable.Range(0, cap + 1).Select(i => $"q{i}").ToList();
        var trans = new Dictionary<(string State, char Symbol), string>();
        for (var i = 0; i <= cap; i++)
        {
            foreach (var c in alpha)
            {
                var nextIdx = c == targetSym ? System.Math.Min(i + 1, cap) : i;
                trans[($"q{i}", c)] = $"q{nextIdx}";
            }
        }

        var accept = new List<string>();
        for (var i = 0; i <= cap; i++)
        {
            var ok = op switch
            {
                "at least" or ">=" => i >= k,
                "at most" or "<=" => i <= k,
                "exactly" or "=" => i == k,
                _ => false
            };
            if (ok) accept.Add($"q{i}");
        }

        return new Dfa(states, alpha, trans, "q0", accept).Minimize();
    }
}
