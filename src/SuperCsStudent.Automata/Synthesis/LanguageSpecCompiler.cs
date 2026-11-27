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

public sealed record ConstraintNfaResult(
    Nfa Nfa,
    bool RequiredDeterminization,
    string? DeterminizationReason);

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
    TmCompiledSchema CompiledTm,
    IReadOnlyList<LanguageTestCase>? SuggestedTests = null,
    bool NfaRequiredDeterminization = false,
    string? NfaDeterminizationReason = null);

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
        @"\b(starts?\s+with|begins?\s+with|ends?\s+with|contains?|has\s+substring|does\s+not|doesn't|even|odd|length|divisible|multiple|mod|at\s+least|at\s+most|exactly|more\s+than|fewer\s+than|less\s+than|no\s+consecutive|no\s+adjacent|consecutive|adjacent|every\s+\S+\s+(?:is\s+)?followed\s+by|starts?\s+and\s+ends?|from\s+(?:right|end|last)|to\s+last|equals?|exact|non-?empty|is\s+empty|empty|count\s+of|no\s+\S|(?:kleene\s+)?star\s*\()\b|\b\d+(?:st|nd|rd|th)?\s+(?:symbol|char|character|letter|from)\b|\b(AND|OR|NOT)\b",
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
            return TryCompileParametric(trimmed, alphabet)
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
        var parametric = TryCompileParametric(trimmed, alphabet);
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
            CompiledTm: compiledTm,
            SuggestedTests: GenerateDfaSuggestedTests(minDfa));
    }

    public static LanguageSpecCompilationResult CompileConstraint(string constraintSpec, IEnumerable<char>? alphabet = null)
    {
        var dfa = CompileConstraintToDfa(constraintSpec, alphabet);
        var nfaResult = CompileConstraintToNfa(constraintSpec, alphabet);
        var rg = RegularGrammarConverter.DfaToRegularGrammar(dfa);
        var schema = DfaToDecidableTmSchema(dfa, $"Decider: {constraintSpec}");
        var compiledTm = TmSchemaCompiler.Compile(schema);

        var desc = nfaResult.RequiredDeterminization
            ? $"Negated regular constraint determinized into complete minimal DFA ({dfa.States.Count} states) for complementation and converted to a decidable TM Schema."
            : $"Synthesized compact NFA ({nfaResult.Nfa.States.Count} states) and exact minimal DFA ({dfa.States.Count} states) → Decidable TM Schema.";

        return new LanguageSpecCompilationResult(
            Specification: constraintSpec,
            ResolvedMode: LanguageSpecMode.Constraint,
            LanguageClass: LanguageClass.Regular,
            Title: $"Regular Constraint: {constraintSpec}",
            Description: desc,
            MinimalDfa: dfa,
            Nfa: nfaResult.Nfa,
            Grammar: rg,
            Pda: null,
            TmSchema: schema,
            CompiledTm: compiledTm,
            SuggestedTests: GenerateDfaSuggestedTests(dfa),
            NfaRequiredDeterminization: nfaResult.RequiredDeterminization,
            NfaDeterminizationReason: nfaResult.DeterminizationReason);
    }

    /// <summary>
    /// Compiles a Boolean regular constraint into a true compact NFA (supporting string-labeled edges
    /// like q0 --aba--> q1 with no dead sink states for positive constraints).
    /// When negation (does not start/contain/end with or NOT) appears, explicitly determinizes to a
    /// complete DFA before complementing accept states (F' = Q \ F) and flags RequiredDeterminization = true.
    /// </summary>
    public static ConstraintNfaResult CompileConstraintToNfa(string expression, IEnumerable<char>? explicitAlphabet = null)
    {
        var tokens = TokenizeConstraint(expression);
        if (tokens.Count == 0)
        {
            throw new FormatException("Constraint expression is empty.");
        }

        var alpha = InferConstraintAlphabet(tokens, explicitAlphabet);
        var parser = new ConstraintNfaParser(tokens, alpha);
        var res = parser.ParseOr();
        if (!parser.IsAtEnd)
        {
            throw new FormatException($"Unexpected token '{parser.Current.Text}' in constraint expression.");
        }
        return res;
    }

    internal static IReadOnlyList<LanguageTestCase> GenerateDfaSuggestedTests(Dfa dfa)
    {
        var positives = new List<LanguageTestCase>();
        var negatives = new List<LanguageTestCase>();
        var alpha = dfa.OrderedAlphabet;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryWord(string w)
        {
            var disp = w.Length == 0 ? "e" : w;
            if (!seen.Add(disp)) return;
            var acc = dfa.Accepts(w);
            var tc = new LanguageTestCase(disp, acc, acc ? "accepted by DFA" : "rejected by DFA");
            if (acc) { if (positives.Count < 4) positives.Add(tc); }
            else { if (negatives.Count < 4) negatives.Add(tc); }
        }

        TryWord("");
        var queue = new Queue<string>();
        queue.Enqueue("");
        var maxLen = alpha.Count <= 2 ? 7 : 5;
        while (queue.Count > 0 && (positives.Count < 4 || negatives.Count < 4) && seen.Count < 400)
        {
            var cur = queue.Dequeue();
            if (cur.Length >= maxLen) continue;
            foreach (var c in alpha)
            {
                var next = cur + c;
                TryWord(next);
                if (positives.Count >= 4 && negatives.Count >= 4) break;
                queue.Enqueue(next);
            }
        }

        // If shortest accepted/rejected witness is longer than maxLen, use DFA shortest-path BFS
        if (positives.Count == 0)
        {
            var witness = FaOperations.IsEmpty(dfa).Counterexample;
            if (witness is not null) TryWord(witness == AutomataSymbols.Empty ? "" : witness);
        }
        if (negatives.Count == 0)
        {
            var witness = FaOperations.IsUniversal(dfa).Counterexample;
            if (witness is not null) TryWord(witness == AutomataSymbols.Empty ? "" : witness);
        }

        return positives.Concat(negatives).ToList();
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

    private static LanguageSpecCompilationResult? TryCompileParametric(string rawSpec, IEnumerable<char>? explicitAlphabet = null)
    {
        var norm = Regex.Replace(rawSpec.Trim().ToLowerInvariant(), @"\s+", " ");
        var compact = norm.Replace(" ", string.Empty);
        var customAlpha = explicitAlphabet?.Where(c => c != AutomataSymbols.DefaultBlank).Distinct().OrderBy(c => c).ToList();

        // 0. Generalized user-authored parametric language compiler
        //    Handles arbitrary block-exponent specs (e.g. a^n b^m where n > m, a^n b^m c^k where n + m = k,
        //    a^n b^m c^k where n > m > k, { x^i y^j | i >= 2j, j >= 1 }) and symbol-count specs (#x >= #y)
        var generalized = ParametricLanguageCompiler.TryCompile(rawSpec, explicitAlphabet);
        if (generalized is not null)
        {
            return generalized;
        }

        // 1. Generalized s1^n s2^n s3^n (e.g. a^n b^n c^n, x^n y^n z^n, 0^n 1^n 2^n)
        var mTriples = Regex.Match(compact, @"^\{?([a-z0-9])\^n([a-z0-9])\^n([a-z0-9])\^n\}?$");
        if (compact is "anbncn" || (mTriples.Success && mTriples.Groups[1].Value != mTriples.Groups[2].Value && mTriples.Groups[2].Value != mTriples.Groups[3].Value && mTriples.Groups[1].Value != mTriples.Groups[3].Value))
        {
            var s1 = mTriples.Success ? mTriples.Groups[1].Value[0] : 'a';
            var s2 = mTriples.Success ? mTriples.Groups[2].Value[0] : 'b';
            var s3 = mTriples.Success ? mTriples.Groups[3].Value[0] : 'c';
            var schema = BuildAnBnCnSchema(s1, s2, s3);
            return BuildParametricResult(rawSpec, LanguageClass.ContextSensitive,
                $"L = {{ {s1}ⁿ{s2}ⁿ{s3}ⁿ | n ≥ 0 }}",
                $"Context-sensitive language decided by marking matching triples ({s1}, {s2}, {s3}).",
                null, null, schema);
        }

        // 2. Generalized s1^2n s2^m s3^n (e.g. a^2n b^m c^n, x^2n y^m z^n)
        var m2nm = Regex.Match(compact, @"^\{?([a-z0-9])\^(?:2n|\(2n\))([a-z0-9])\^m([a-z0-9])\^n\}?$");
        if (compact is "a2nbmcn" || (m2nm.Success && m2nm.Groups[1].Value != m2nm.Groups[2].Value && m2nm.Groups[2].Value != m2nm.Groups[3].Value && m2nm.Groups[1].Value != m2nm.Groups[3].Value))
        {
            var s1 = m2nm.Success ? m2nm.Groups[1].Value[0] : 'a';
            var s2 = m2nm.Success ? m2nm.Groups[2].Value[0] : 'b';
            var s3 = m2nm.Success ? m2nm.Groups[3].Value[0] : 'c';
            var cfg = Cfg.ParseFromText($"S -> {s1}{s1}S{s3} | B\nB -> {s2}B | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildA2nBmCnSchema(s1, s2, s3);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ {s1}²ⁿ{s2}ᵐ{s3}ⁿ | n, m ≥ 0 }}",
                $"Context-free language matching two leading {s1}'s per trailing {s3} around any number of {s2}'s.",
                cfg, pda, schema);
        }

        // 3. Generalized s1^n s2^n s3^m (e.g. a^n b^n c^m, x^n y^n z^m)
        var mnnm = Regex.Match(compact, @"^\{?([a-z0-9])\^n([a-z0-9])\^n([a-z0-9])\^m\}?$");
        if (compact is "anbncm" || (mnnm.Success && mnnm.Groups[1].Value != mnnm.Groups[2].Value && mnnm.Groups[2].Value != mnnm.Groups[3].Value && mnnm.Groups[1].Value != mnnm.Groups[3].Value))
        {
            var s1 = mnnm.Success ? mnnm.Groups[1].Value[0] : 'a';
            var s2 = mnnm.Success ? mnnm.Groups[2].Value[0] : 'b';
            var s3 = mnnm.Success ? mnnm.Groups[3].Value[0] : 'c';
            var cfg = Cfg.ParseFromText($"S -> AC\nA -> {s1}A{s2} | e\nC -> {s3}C | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildAnBnCmSchema(s1, s2, s3);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ {s1}ⁿ{s2}ⁿ{s3}ᵐ | n, m ≥ 0 }}",
                $"Context-free language matching equal {s1}'s and {s2}'s followed by any number of {s3}'s.",
                cfg, pda, schema);
        }

        // 4. Generalized s1^2n s2^n (e.g. a^2n b^n, x^2n y^n, 0^2n 1^n)
        var m2n = Regex.Match(compact, @"^\{?([a-z0-9])\^(?:2n|\(2n\))([a-z0-9])\^n\}?$");
        if (compact is "a2nbn" || (m2n.Success && m2n.Groups[1].Value != m2n.Groups[2].Value))
        {
            var s1 = m2n.Success ? m2n.Groups[1].Value[0] : 'a';
            var s2 = m2n.Success ? m2n.Groups[2].Value[0] : 'b';
            var cfg = Cfg.ParseFromText($"S -> {s1}{s1}S{s2} | e");
            var pda = PushdownAutomaton.ParseTransitions(
                $"start: s\naccept: f\n((s, {s1}{s1}, e), (s, {s1}))\n((s, e, e), (f, e))\n((f, {s2}, {s1}), (f, e))");
            var schema = BuildA2nBnSchema(s1, s2);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ {s1}²ⁿ{s2}ⁿ | n ≥ 0 }}",
                $"Context-free language with twice as many leading {s1}'s as trailing {s2}'s.",
                cfg, pda, schema);
        }

        // 5. Generalized s1^n s2^n (e.g. a^n b^n, x^n y^n, 0^n 1^n)
        var mnn = Regex.Match(compact, @"^\{?([a-z0-9])\^n([a-z0-9])\^n\}?$");
        if (compact is "anbn" || (mnn.Success && mnn.Groups[1].Value != mnn.Groups[2].Value))
        {
            var s1 = mnn.Success ? mnn.Groups[1].Value[0] : 'a';
            var s2 = mnn.Success ? mnn.Groups[2].Value[0] : 'b';
            var cfg = Cfg.ParseFromText($"S -> {s1}S{s2} | e");
            var pda = PushdownAutomaton.ParseTransitions(
                $"start: s\naccept: f\n((s, {s1}, e), (s, {s1}))\n((s, e, e), (f, e))\n((f, {s2}, {s1}), (f, e))");
            var schema = BuildAnBnSchema(s1, s2);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ {s1}ⁿ{s2}ⁿ | n ≥ 0 }}",
                $"Classic context-free language decided by stripping matching outer ({s1}, {s2}) pairs via R_# S_L.",
                cfg, pda, schema);
        }

        // 6. even palindromes (supports custom Σ e.g. x,y,z or 0,1)
        if (norm is "even palindromes" or "even palindrome" || compact is "ww^r" or "evenpalindromes")
        {
            var syms = customAlpha is { Count: >= 2 } ? customAlpha : new List<char> { 'a', 'b' };
            var cfgRules = string.Join(" | ", syms.Select(c => $"{c}S{c}")) + " | e";
            var cfg = Cfg.ParseFromText($"S -> {cfgRules}");
            var pdaLines = new List<string> { "start: s", "accept: f" };
            foreach (var c in syms) pdaLines.Add($"((s, {c}, e), (s, {c}))");
            pdaLines.Add("((s, e, e), (f, e))");
            foreach (var c in syms) pdaLines.Add($"((f, {c}, {c}), (f, e))");
            var pda = PushdownAutomaton.ParseTransitions(string.Join('\n', pdaLines));
            var schema = BuildPalindromeSchema(evenOnly: true, symbols: syms);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"Even Palindromes {{ w wᴿ | w ∈ {{{string.Join(", ", syms)}}}* }}",
                "Even-length palindromes decided by peeling matching outer symbols with R_# S_L.",
                cfg, pda, schema);
        }

        // 7. palindromes (w = w^R) over any Σ
        if (norm is "palindromes" or "palindrome" || compact is "w=w^r" or "palindromes(w=w^r)")
        {
            var syms = customAlpha is { Count: >= 2 } ? customAlpha : new List<char> { 'a', 'b' };
            var cfgRules = string.Join(" | ", syms.Select(c => $"{c}S{c}")) + " | " + string.Join(" | ", syms) + " | e";
            var cfg = Cfg.ParseFromText($"S -> {cfgRules}");
            var pdaLines = new List<string> { "start: s", "accept: f" };
            foreach (var c in syms) pdaLines.Add($"((s, {c}, e), (s, {c}))");
            pdaLines.Add("((s, e, e), (f, e))");
            foreach (var c in syms) pdaLines.Add($"((s, {c}, e), (f, e))");
            foreach (var c in syms) pdaLines.Add($"((f, {c}, {c}), (f, e))");
            var pda = PushdownAutomaton.ParseTransitions(string.Join('\n', pdaLines));
            var schema = BuildPalindromeSchema(evenOnly: false, symbols: syms);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"Palindromes {{ w ∈ {{{string.Join(", ", syms)}}}* | w = wᴿ }}",
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

        // 9. Generalized #s1 > #s2 (e.g. #a > #b, #x > #y, #1 > #0)
        var mMore = Regex.Match(compact, @"^#([a-z0-9])>#([a-z0-9])$");
        if (compact is "count(a)>count(b)" || norm is "more a than b" || (mMore.Success && mMore.Groups[1].Value != mMore.Groups[2].Value))
        {
            var s1 = mMore.Success ? mMore.Groups[1].Value[0] : 'a';
            var s2 = mMore.Success ? mMore.Groups[2].Value[0] : 'b';
            var cfg = Cfg.ParseFromText($"S -> E {s1} S | E {s1} E\nE -> {s1} E {s2} E | {s2} E {s1} E | e");
            var pda = CfgToPdaConverter.Convert(cfg);
            var schema = BuildMoreAThanBSchema(s1, s2);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ w ∈ {{{s1}, {s2}}}* | #{s1}(w) > #{s2}(w) }}",
                $"Strings containing strictly more {s1}'s than {s2}'s, decided by pairwise cancellation.",
                cfg, pda, schema);
        }

        // 10. Generalized #s1 = #s2 (e.g. #a = #b, #x = #y, #0 = #1)
        var mEq = Regex.Match(compact, @"^#([a-z0-9])==?#([a-z0-9])$");
        if (compact is "count(a)=count(b)" || norm is "equal a and b" or "equal #a and #b" || (mEq.Success && mEq.Groups[1].Value != mEq.Groups[2].Value))
        {
            var s1 = mEq.Success ? mEq.Groups[1].Value[0] : 'a';
            var s2 = mEq.Success ? mEq.Groups[2].Value[0] : 'b';
            var cfg = Cfg.ParseFromText($"S -> {s1}S{s2}S | {s2}S{s1}S | e");
            var pda = PushdownAutomaton.ParseTransitions(
                $"start: s\naccept: s\n((s, {s1}, {s2}), (s, e))\n((s, {s1}, e), (s, {s1}))\n((s, {s2}, {s1}), (s, e))\n((s, {s2}, e), (s, {s2}))");
            var schema = BuildEqualABSchema(s1, s2);
            return BuildParametricResult(rawSpec, LanguageClass.ContextFree,
                $"L = {{ w ∈ {{{s1}, {s2}}}* | #{s1}(w) = #{s2}(w) }}",
                $"Strings with equal counts of {s1} and {s2}, decided by pairwise cancellation.",
                cfg, pda, schema);
        }

        // 11. Generalized invert s1 and s2 / complement
        var mInv = Regex.Match(norm, @"^(?:invert|complement|flip)\s+([a-z0-9])\s*(?:and|/|↔|<->)\s*([a-z0-9])$");
        if (norm is "invert" or "complement" || (mInv.Success && mInv.Groups[1].Value != mInv.Groups[2].Value))
        {
            var s1 = mInv.Success ? mInv.Groups[1].Value[0] : (customAlpha is { Count: >= 2 } ? customAlpha[0] : 'a');
            var s2 = mInv.Success ? mInv.Groups[2].Value[0] : (customAlpha is { Count: >= 2 } ? customAlpha[1] : 'b');
            var schema = BuildInvertABSchema(s1, s2);
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                $"Invert {s1} ↔ {s2} Transducer",
                $"Replaces every '{s1}' with '{s2}' and every '{s2}' with '{s1}' across the tape.",
                null, null, schema);
        }

        // 12. shift left (S_L) over custom or default Σ
        if (norm is "shift left" or "s_l" or "shift left (s_l)" || compact is "shiftleft" or "s_l")
        {
            var schema = BuildShiftLeftSchema(customAlpha);
            return BuildParametricResult(rawSpec, LanguageClass.Transducer,
                "Shift Left Macro (S_L)",
                "Shifts the non-blank block immediately left of the head one cell to the left, ending in L #.",
                null, null, schema);
        }

        // 13. shift right (S_R) over custom or default Σ
        if (norm is "shift right" or "s_r" or "shift right (s_r)" || compact is "shiftright" or "s_r")
        {
            var schema = BuildShiftRightSchema(customAlpha);
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

    private static char PickMarker(char preferred, params char[] used)
    {
        if (!used.Contains(preferred)) return preferred;
        foreach (var cand in new[] { 'X', 'Q', 'Z', 'M', 'P', 'W', 'K', 'J' })
        {
            if (!used.Contains(cand)) return cand;
        }
        return '$';
    }

    public static TmSchema BuildAnBnSchema(char s1 = 'a', char s2 = 'b') =>
        new TmSchemaBuilder($"Decider for {s1}^n {s2}^n")
            .Alphabet(s1, s2, 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("peel_a", "# R_# S_L")
            .Node("check_b", "L")
            .Node("erase_b", "#")
            .Node("acc_empty", "Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Node("rej_zero", "R N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "peel_a", s1.ToString())
            .Edge("start", "rej_mid", s2.ToString())
            .Edge("peel_a", "check_b", "*")
            .Edge("check_b", "erase_b", s2.ToString())
            .Edge("check_b", "rej_end", s1.ToString())
            .Edge("check_b", "rej_zero", "#")
            .Edge("erase_b", "start", "*")
            .Build();

    public static TmSchema BuildA2nBnSchema(char s1 = 'a', char s2 = 'b') =>
        new TmSchemaBuilder($"Decider for {s1}^2n {s2}^n")
            .Alphabet(s1, s2, 'y', 'n')
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
            .Edge("start", "peel_a1", s1.ToString())
            .Edge("start", "rej_mid", s2.ToString())
            .Edge("peel_a1", "find_a2", "*")
            .Edge("find_a2", "peel_a2", s1.ToString())
            .Edge("find_a2", "rej_mid", s2.ToString())
            .Edge("find_a2", "rej_empty", "#")
            .Edge("peel_a2", "check_b", "*")
            .Edge("check_b", "erase_b", s2.ToString())
            .Edge("check_b", "rej_end", s1.ToString())
            .Edge("check_b", "rej_zero", "#")
            .Edge("erase_b", "start", "*")
            .Build();

    public static TmSchema BuildPalindromeSchema(bool evenOnly = false, IEnumerable<char>? symbols = null)
    {
        var syms = symbols?.Where(c => c != AutomataSymbols.DefaultBlank).Distinct().OrderBy(c => c).ToList();
        if (syms is null || syms.Count < 2) syms = new List<char> { 'a', 'b' };

        var alpha = new SortedSet<char>(syms) { 'y', 'n' };
        var b = new TmSchemaBuilder(evenOnly ? "Even Palindrome Decider" : "Palindrome Decider")
            .Alphabet(alpha)
            .StartNode("start", "L_# R")
            .Node("erase_last", "#")
            .Node("acc_empty", "Y R")
            .Node("mid_single", evenOnly ? "R N R" : "R Y R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("erase_last", "start", "*");

        foreach (var c in syms)
        {
            var peelId = $"peel_{c}";
            var matchId = $"match_{c}";
            var others = string.Join(", ", syms.Where(o => o != c));
            b.Node(peelId, "# R_# S_L")
             .Node(matchId, "L")
             .Edge("start", peelId, c.ToString())
             .Edge(peelId, matchId, "*")
             .Edge(matchId, "erase_last", c.ToString())
             .Edge(matchId, "mid_single", "#")
             .Edge(matchId, "rej_end", others);
        }

        return b.Build();
    }

    public static TmSchema BuildAnBnCnSchema(char s1 = 'a', char s2 = 'b', char s3 = 'c')
    {
        var m1 = PickMarker('X', s1, s2, s3, 'y', 'n');
        var m2 = PickMarker('Q', s1, s2, s3, 'y', 'n', m1);
        var m3 = PickMarker('Z', s1, s2, s3, 'y', 'n', m1, m2);
        return new TmSchemaBuilder($"Decider for {s1}^n {s2}^n {s3}^n")
            .Alphabet(s1, s2, s3, m1, m2, m3, 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("mark_a", m1.ToString())
            .Node("find_b", "R")
            .Node("mark_b", m2.ToString())
            .Node("find_c", "R")
            .Node("mark_c", m3.ToString())
            .Node("back", "L_# R")
            .Node("skip_x", "R")
            .Node("verify_q", "R")
            .Node("verify_z", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "mark_a", s1.ToString())
            .Edge("start", "rej_mid", "other")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", $"{s1}, {m2}")
            .Edge("find_b", "mark_b", s2.ToString())
            .Edge("find_b", "rej_end", "#")
            .Edge("find_b", "rej_mid", "other")
            .Edge("mark_b", "find_c", "*")
            .Edge("find_c", "find_c", $"{s2}, {m3}")
            .Edge("find_c", "mark_c", s3.ToString())
            .Edge("find_c", "rej_end", "#")
            .Edge("find_c", "rej_mid", "other")
            .Edge("mark_c", "back", "*")
            .Edge("back", "skip_x", m1.ToString())
            .Edge("back", "rej_mid", "other")
            .Edge("skip_x", "skip_x", m1.ToString())
            .Edge("skip_x", "mark_a", s1.ToString())
            .Edge("skip_x", "verify_q", m2.ToString())
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "rej_mid", "other")
            .Edge("verify_q", "verify_q", m2.ToString())
            .Edge("verify_q", "verify_z", m3.ToString())
            .Edge("verify_q", "rej_end", "#")
            .Edge("verify_q", "rej_mid", "other")
            .Edge("verify_z", "verify_z", m3.ToString())
            .Edge("verify_z", "acc_end", "#")
            .Edge("verify_z", "rej_mid", "other")
            .Build();
    }

    public static TmSchema BuildA2nBmCnSchema(char s1 = 'a', char s2 = 'b', char s3 = 'c') =>
        new TmSchemaBuilder($"Decider for {s1}^2n {s2}^m {s3}^n")
            .Alphabet(s1, s2, s3, 'y', 'n')
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
            .Edge("start", "peel_a1", s1.ToString())
            .Edge("start", "scan_b", s2.ToString())
            .Edge("start", "rej_mid", s3.ToString())
            .Edge("peel_a1", "find_a2", "*")
            .Edge("find_a2", "peel_a2", s1.ToString())
            .Edge("find_a2", "rej_empty", "#")
            .Edge("find_a2", "rej_mid", $"{s2}, {s3}")
            .Edge("peel_a2", "check_c", "*")
            .Edge("check_c", "erase_c", s3.ToString())
            .Edge("check_c", "rej_zero", "#")
            .Edge("check_c", "rej_end", $"{s1}, {s2}")
            .Edge("erase_c", "start", "*")
            .Edge("scan_b", "scan_b", s2.ToString())
            .Edge("scan_b", "acc_end", "#")
            .Edge("scan_b", "rej_mid", $"{s1}, {s3}")
            .Build();

    public static TmSchema BuildAnBnCmSchema(char s1 = 'a', char s2 = 'b', char s3 = 'c')
    {
        var m1 = PickMarker('X', s1, s2, s3, 'y', 'n');
        var m2 = PickMarker('Q', s1, s2, s3, 'y', 'n', m1);
        return new TmSchemaBuilder($"Decider for {s1}^n {s2}^n {s3}^m")
            .Alphabet(s1, s2, s3, m1, m2, 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("mark_a", m1.ToString())
            .Node("find_b", "R")
            .Node("mark_b", m2.ToString())
            .Node("back", "L_# R")
            .Node("skip_x", "R")
            .Node("verify_q", "R")
            .Node("scan_c", "R")
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_mid", "R_# E N R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "mark_a", s1.ToString())
            .Edge("start", "scan_c", s3.ToString())
            .Edge("start", "rej_mid", "other")
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", $"{s1}, {m2}")
            .Edge("find_b", "mark_b", s2.ToString())
            .Edge("find_b", "rej_end", "#")
            .Edge("find_b", "rej_mid", "other")
            .Edge("mark_b", "back", "*")
            .Edge("back", "skip_x", m1.ToString())
            .Edge("back", "rej_mid", "other")
            .Edge("skip_x", "skip_x", m1.ToString())
            .Edge("skip_x", "mark_a", s1.ToString())
            .Edge("skip_x", "verify_q", m2.ToString())
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "rej_mid", "other")
            .Edge("verify_q", "verify_q", m2.ToString())
            .Edge("verify_q", "scan_c", s3.ToString())
            .Edge("verify_q", "acc_end", "#")
            .Edge("verify_q", "rej_mid", "other")
            .Edge("scan_c", "scan_c", s3.ToString())
            .Edge("scan_c", "acc_end", "#")
            .Edge("scan_c", "rej_mid", "other")
            .Build();
    }

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

    public static TmSchema BuildEqualABSchema(char s1 = 'a', char s2 = 'b')
    {
        var mk = PickMarker('X', s1, s2, 'y', 'n');
        return new TmSchemaBuilder($"Decider for #{s1} = #{s2}")
            .Alphabet(s1, s2, mk, 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("skip_x", "R")
            .Node("mark_a", mk.ToString())
            .Node("find_b", "R")
            .Node("mark_b", mk.ToString())
            .Node("find_a", "R")
            .Node("pair_done", mk.ToString())
            .Node("acc_empty", "Y R")
            .Node("acc_end", "E Y R")
            .Node("rej_end", "E N R")
            .Edge("start", "acc_empty", "#")
            .Edge("start", "skip_x", mk.ToString())
            .Edge("start", "mark_a", s1.ToString())
            .Edge("start", "mark_b", s2.ToString())
            .Edge("skip_x", "skip_x", mk.ToString())
            .Edge("skip_x", "acc_end", "#")
            .Edge("skip_x", "mark_a", s1.ToString())
            .Edge("skip_x", "mark_b", s2.ToString())
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", $"{s1}, {mk}")
            .Edge("find_b", "pair_done", s2.ToString())
            .Edge("find_b", "rej_end", "#")
            .Edge("mark_b", "find_a", "*")
            .Edge("find_a", "find_a", $"{s2}, {mk}")
            .Edge("find_a", "pair_done", s1.ToString())
            .Edge("find_a", "rej_end", "#")
            .Edge("pair_done", "start", "*")
            .Build();
    }

    public static TmSchema BuildMoreAThanBSchema(char s1 = 'a', char s2 = 'b')
    {
        var mk = PickMarker('X', s1, s2, 'y', 'n');
        return new TmSchemaBuilder($"Decider for #{s1} > #{s2}")
            .Alphabet(s1, s2, mk, 'y', 'n')
            .StartNode("start", "L_# R")
            .Node("skip_x", "R")
            .Node("mark_a", mk.ToString())
            .Node("find_b", "R")
            .Node("mark_b", mk.ToString())
            .Node("find_a", "R")
            .Node("pair_done", mk.ToString())
            .Node("acc_end", "E Y R")
            .Node("rej_empty", "N R")
            .Node("rej_end", "E N R")
            .Edge("start", "rej_empty", "#")
            .Edge("start", "skip_x", mk.ToString())
            .Edge("start", "mark_a", s1.ToString())
            .Edge("start", "mark_b", s2.ToString())
            .Edge("skip_x", "skip_x", mk.ToString())
            .Edge("skip_x", "rej_end", "#")
            .Edge("skip_x", "mark_a", s1.ToString())
            .Edge("skip_x", "mark_b", s2.ToString())
            .Edge("mark_a", "find_b", "*")
            .Edge("find_b", "find_b", $"{s1}, {mk}")
            .Edge("find_b", "pair_done", s2.ToString())
            .Edge("find_b", "acc_end", "#")
            .Edge("mark_b", "find_a", "*")
            .Edge("find_a", "find_a", $"{s2}, {mk}")
            .Edge("find_a", "pair_done", s1.ToString())
            .Edge("find_a", "rej_end", "#")
            .Edge("pair_done", "start", "*")
            .Build();
    }

    public static TmSchema BuildInvertABSchema(char s1 = 'a', char s2 = 'b') =>
        new TmSchemaBuilder($"Invert {s1} and {s2}")
            .Alphabet(s1, s2)
            .StartNode("rewind", "L_#")
            .Node("step", "R")
            .Node("write_b", s2.ToString())
            .Node("write_a", s1.ToString())
            .Edge("rewind", "step", "*")
            .Edge("step", "write_b", s1.ToString())
            .Edge("step", "write_a", s2.ToString())
            .Edge("write_b", "step", "*")
            .Edge("write_a", "step", "*")
            .Build();

    public static TmSchema BuildShiftLeftSchema(IEnumerable<char>? symbols = null)
    {
        var syms = symbols?.Where(c => c != AutomataSymbols.DefaultBlank).Distinct().ToList();
        if (syms is null || syms.Count == 0) syms = new List<char> { 'a', 'b' };
        return new TmSchemaBuilder("Shift Left (S_L)")
            .Alphabet(syms)
            .StartNode("shift_l", "S_L")
            .Build();
    }

    public static TmSchema BuildShiftRightSchema(IEnumerable<char>? symbols = null)
    {
        var syms = symbols?.Where(c => c != AutomataSymbols.DefaultBlank).Distinct().ToList();
        if (syms is null || syms.Count == 0) syms = new List<char> { 'a', 'b' };
        return new TmSchemaBuilder("Shift Right (S_R)")
            .Alphabet(syms)
            .StartNode("shift_r", "S_R")
            .Build();
    }

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

    private enum ConstraintTokenKind { Atom, And, Or, Not, PrefixStar, Star, Plus, LParen, RParen }
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
            if ((c == '*' || c == '+') &&
                atomChars.All(char.IsWhiteSpace) &&
                tokens.Count > 0 &&
                tokens[^1].Kind is ConstraintTokenKind.RParen or ConstraintTokenKind.Star or ConstraintTokenKind.Plus)
            {
                atomChars.Clear();
                tokens.Add(new ConstraintToken(c == '*' ? ConstraintTokenKind.Star : ConstraintTokenKind.Plus, c.ToString()));
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
            if (MatchesWord("KLEENE"))
            {
                var rest = input[(i + 6)..].TrimStart();
                if (rest.StartsWith("star", StringComparison.OrdinalIgnoreCase))
                {
                    FlushAtom();
                    i += 6;
                    continue;
                }
            }
            if (MatchesWord("STAR"))
            {
                var rest = input[(i + 4)..].TrimStart();
                if (rest.StartsWith('('))
                {
                    FlushAtom();
                    tokens.Add(new ConstraintToken(ConstraintTokenKind.PrefixStar, "star"));
                    i += 4;
                    continue;
                }
            }
            if (MatchesWord("AND"))
            {
                var currentAtom = new string(atomChars.ToArray()).Trim();
                var afterAnd = input[(i + 3)..].TrimStart();
                var isStartAndEnd =
                    (currentAtom.EndsWith("starts", StringComparison.OrdinalIgnoreCase) ||
                     currentAtom.EndsWith("start", StringComparison.OrdinalIgnoreCase)) &&
                    (afterAnd.StartsWith("ends", StringComparison.OrdinalIgnoreCase) ||
                     afterAnd.StartsWith("end", StringComparison.OrdinalIgnoreCase));
                if (!isStartAndEnd)
                {
                    FlushAtom();
                    tokens.Add(new ConstraintToken(ConstraintTokenKind.And, "AND"));
                    i += 3;
                    continue;
                }
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

        if (discovered.All(c => c is 'a' or 'b'))
        {
            discovered.Add('a');
            discovered.Add('b');
            return discovered;
        }

        // For custom symbols like {x, y, z}, keep exact discovered symbols without forcing a, b
        return discovered;
    }

    private static void CollectAtomSymbols(string atom, SortedSet<char> symbols)
    {
        var s = Regex.Replace(atom.Trim(), @"\s+", " ");
        if (TryStripDoesNotPrefix(s, out var innerPos))
        {
            CollectAtomSymbols(innerPos, symbols);
            return;
        }

        var m = Regex.Match(s, @"^(?:starts? with|begins? with|ends? with|contains?|has substring|equals?|exact)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var arg = Unquote(m.Groups[1].Value.Trim());
            if (!AutomataSymbols.IsEmptyToken(arg))
            {
                foreach (var c in arg) symbols.Add(c);
            }
            return;
        }

        var followedMatch = Regex.Match(s, @"^every\s+#?([^\s])\s+(?:is\s+)?followed\s+by\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (followedMatch.Success)
        {
            symbols.Add(followedMatch.Groups[1].Value[0]);
            symbols.Add(followedMatch.Groups[2].Value[0]);
            return;
        }

        var consecMatch = Regex.Match(s, @"^(?:consecutive|adjacent)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (consecMatch.Success)
        {
            symbols.Add(consecMatch.Groups[1].Value[0]);
            return;
        }

        var posMatch = Regex.Match(s, @"^\d+(?:st|nd|rd|th)?(?:\s+(?:symbol|char|character|letter))?(?:\s+(?:from\s+(?:left|start|right|end|last)|to\s+last))?\s+(?:is|=)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (posMatch.Success)
        {
            symbols.Add(posMatch.Groups[1].Value[0]);
            return;
        }

        var symModMatch = Regex.Match(s, @"^(?:count\s+of\s+)?#?([^\s])\s+(?:divisible\s+by|multiple\s+of|mod)\s+\d+(?:\s*=\s*\d+)?$", RegexOptions.IgnoreCase);
        if (symModMatch.Success && !symModMatch.Groups[1].Value.Equals("length", StringComparison.OrdinalIgnoreCase))
        {
            symbols.Add(symModMatch.Groups[1].Value[0]);
            return;
        }

        var countMatch = Regex.Match(s, @"^(?:even|odd)(?:\s+number\s+of)?\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (countMatch.Success)
        {
            symbols.Add(countMatch.Groups[1].Value[0]);
            return;
        }

        var numMatch = Regex.Match(s, @"^(?:at least|at most|exactly|more than|fewer than|less than|>=|<=|=|>|<)\s+\d+\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            symbols.Add(numMatch.Groups[1].Value[0]);
        }
    }

    private static bool TryStripDoesNotPrefix(string atom, out string innerPositive)
    {
        // "does not start/begin/end with X", "does not contain X", "does not equal X"
        var m = Regex.Match(atom, @"^(?:does not|doesn't)\s+(start|begin|end)\s+with\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var verb = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "begin" => "begins with",
                "end" => "ends with",
                _ => "starts with"
            };
            innerPositive = $"{verb} {m.Groups[2].Value.Trim()}";
            return true;
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+contain\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = $"contains {m.Groups[1].Value.Trim()}";
            return true;
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+equal\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = $"equals {m.Groups[1].Value.Trim()}";
            return true;
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+start\s+and\s+end\s+with\s+(?:the\s+)?(same|different)\s+symbols?$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = $"starts and ends with {m.Groups[1].Value.ToLowerInvariant()} symbol";
            return true;
        }

        m = Regex.Match(atom, @"^(?:does not|doesn't)\s+have\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = m.Groups[1].Value.Trim();
            return true;
        }

        m = Regex.Match(atom, @"^no\s+(?:consecutive|adjacent)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = $"consecutive {m.Groups[1].Value[0]}";
            return true;
        }

        m = Regex.Match(atom, @"^(?:contains?\s+)?(?:no|zero)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            innerPositive = $"contains {m.Groups[1].Value[0]}";
            return true;
        }

        innerPositive = string.Empty;
        return false;
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
            if (!IsAtEnd && Current.Kind == ConstraintTokenKind.PrefixStar)
            {
                _pos++;
                var inner = ParseNot();
                return FaOperations.KleeneStar(inner);
            }
            return ParsePostfix();
        }

        private Dfa ParsePostfix()
        {
            var inner = ParsePrimary();
            while (!IsAtEnd && Current.Kind is ConstraintTokenKind.Star or ConstraintTokenKind.Plus)
            {
                var kind = Current.Kind;
                _pos++;
                inner = kind == ConstraintTokenKind.Star
                    ? FaOperations.KleeneStar(inner)
                    : FaOperations.Concatenation(inner, FaOperations.KleeneStar(inner));
            }
            return inner;
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

        // Unified "does not ..." / "doesn't ..." / "no ..." negation wrapper
        if (TryStripDoesNotPrefix(atom, out var innerPositive))
        {
            var pos = BuildAtomicDfa(innerPositive, alpha);
            return FaOperations.Complement(pos, alpha);
        }

        // Positive prefix/suffix/substring/exact
        var m = Regex.Match(atom, @"^(?:starts? with|begins? with)\s+(.+)$", RegexOptions.IgnoreCase);
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

        m = Regex.Match(atom, @"^(?:equals?|exact)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var x = Unquote(m.Groups[1].Value.Trim());
            return RegexCompiler.CompileToMinimalDfa(EscapeWord(x), alpha);
        }

        // Consecutive / adjacent symbol pair
        m = Regex.Match(atom, @"^(?:consecutive|adjacent)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var sym = m.Groups[1].Value[0];
            var pair = $"{EscapeRegexChar(sym)}{EscapeRegexChar(sym)}";
            return RegexCompiler.CompileToMinimalDfa($"{sigmaStar}{pair}{sigmaStar}", alpha);
        }

        // Every X is followed by Y
        m = Regex.Match(atom, @"^every\s+#?([^\s])\s+(?:is\s+)?followed\s+by\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return BuildEveryFollowedByDfa(alpha, m.Groups[1].Value[0], m.Groups[2].Value[0]);
        }

        // Starts and ends with same / different symbol
        m = Regex.Match(atom, @"^starts?\s+and\s+ends?\s+with\s+(?:the\s+)?(same|different)\s+symbols?$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var same = m.Groups[1].Value.Equals("same", StringComparison.OrdinalIgnoreCase);
            return BuildSameOrDifferentEndsNfa(alpha, same).ToDfa(renameStates: true).Minimize();
        }

        // k-th symbol from right / from last is c
        m = Regex.Match(atom, @"^(\d+)(?:st|nd|rd|th)?(?:\s+(?:symbol|char|character|letter))?\s+(?:from\s+(?:right|end|last)|to\s+last)\s+(?:is|=)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var rightK))
        {
            if (rightK < 1 || rightK > 8)
            {
                throw new FormatException($"'k-th from right' supports k between 1 and 8 (found {rightK}; a complete DFA requires 2^k states).");
            }
            return BuildKthFromRightNfa(rightK, m.Groups[2].Value[0], alpha).ToDfa(renameStates: true).Minimize();
        }

        // k-th symbol (from left) is c
        m = Regex.Match(atom, @"^(\d+)(?:st|nd|rd|th)?\s+(?:symbol|char|character|letter)(?:\s+from\s+(?:left|start))?\s+(?:is|=)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var leftK))
        {
            if (leftK < 1 || leftK > 12)
            {
                throw new FormatException($"'k-th symbol' supports k between 1 and 12 (found {leftK}).");
            }
            return BuildKthFromLeftDfa(leftK, m.Groups[2].Value[0], alpha);
        }

        // Empty / non-empty
        if (atom.Equals("non-empty", StringComparison.OrdinalIgnoreCase) || atom.Equals("nonempty", StringComparison.OrdinalIgnoreCase))
        {
            return BuildLengthCompareDfa(alpha, ">=", 1);
        }
        if (atom.Equals("empty", StringComparison.OrdinalIgnoreCase) || atom.Equals("is empty", StringComparison.OrdinalIgnoreCase))
        {
            return BuildLengthCompareDfa(alpha, "=", 0);
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

        // Symbol count divisible by k or mod k = r
        m = Regex.Match(atom, @"^(?:count\s+of\s+)?#?([^\s])\s+(?:divisible\s+by|multiple\s+of)\s+(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var symDivK))
        {
            if (symDivK < 1 || symDivK > 12)
            {
                throw new FormatException($"Modulus k must be between 1 and 12 (found {symDivK}).");
            }
            return BuildSymbolModDfa(alpha, m.Groups[1].Value[0], symDivK, 0);
        }

        m = Regex.Match(atom, @"^(?:count\s+of\s+)?#?([^\s])\s+mod\s+(\d+)\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var symModK) && int.TryParse(m.Groups[3].Value, out var symRem))
        {
            if (symModK < 1 || symModK > 12)
            {
                throw new FormatException($"Modulus k must be between 1 and 12 (found {symModK}).");
            }
            return BuildSymbolModDfa(alpha, m.Groups[1].Value[0], symModK, symRem % symModK);
        }

        // Length divisible by k or length mod k = r
        m = Regex.Match(atom, @"^length\s+(?:divisible\s+by|multiple\s+of)\s+(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var modK))
        {
            if (modK < 1 || modK > 12)
            {
                throw new FormatException($"Length modulus k must be between 1 and 12 (found {modK}).");
            }
            return BuildLengthModDfa(alpha, modK, 0);
        }

        m = Regex.Match(atom, @"^length\s+mod\s+(\d+)\s*=\s*(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var lenModK) && int.TryParse(m.Groups[2].Value, out var lenRem))
        {
            if (lenModK < 1 || lenModK > 12)
            {
                throw new FormatException($"Length modulus k must be between 1 and 12 (found {lenModK}).");
            }
            return BuildLengthModDfa(alpha, lenModK, lenRem % lenModK);
        }

        // Exact / at least / at most / strict length
        m = Regex.Match(atom, @"^length\s*(=|>=|<=|>|<)\s*(\d+)$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var lenK))
        {
            if (lenK > 15)
            {
                throw new FormatException($"Length bound k must be 15 or less (found {lenK}).");
            }
            return BuildLengthCompareDfa(alpha, m.Groups[1].Value, lenK);
        }

        // At least / at most / exactly / more than / fewer than k occurrences of symbol c
        m = Regex.Match(atom, @"^(at least|at most|exactly|more than|fewer than|less than|>=|<=|=|>|<)\s+(\d+)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var countK))
        {
            if (countK > 15)
            {
                throw new FormatException($"Symbol count bound k must be 15 or less (found {countK}).");
            }
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
                ">" => i > k,
                "<" => i < k,
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
                "more than" or ">" => i > k,
                "fewer than" or "less than" or "<" => i < k,
                _ => false
            };
            if (ok) accept.Add($"q{i}");
        }

        return new Dfa(states, alpha, trans, "q0", accept).Minimize();
    }

    private static Dfa BuildEveryFollowedByDfa(SortedSet<char> alpha, char first, char follower)
    {
        var states = new[] { "q0", "q1", "q2" };
        var trans = new Dictionary<(string State, char Symbol), string>();
        foreach (var c in alpha)
        {
            trans[("q0", c)] = c == first ? "q1" : "q0";
            trans[("q1", c)] = c == follower ? (first == follower ? "q1" : "q0") : "q2";
            trans[("q2", c)] = "q2";
        }
        return new Dfa(states, alpha, trans, "q0", new[] { "q0" }).Minimize();
    }

    private static Dfa BuildKthFromLeftDfa(int k, char targetSym, SortedSet<char> alpha)
    {
        // q0 .. q_{k-1} count prefix length; q_k = accept sink; q_{k+1} = dead sink
        var states = Enumerable.Range(0, k + 2).Select(i => $"q{i}").ToList();
        var acceptState = $"q{k}";
        var deadState = $"q{k + 1}";
        var trans = new Dictionary<(string State, char Symbol), string>();

        for (var i = 0; i < k - 1; i++)
        {
            foreach (var c in alpha) trans[($"q{i}", c)] = $"q{i + 1}";
        }
        foreach (var c in alpha)
        {
            trans[($"q{k - 1}", c)] = c == targetSym ? acceptState : deadState;
            trans[(acceptState, c)] = acceptState;
            trans[(deadState, c)] = deadState;
        }

        return new Dfa(states, alpha, trans, "q0", new[] { acceptState }).Minimize();
    }

    // --- Compact NFA Constraint Synthesizer (True String-Edge NFAs + Negation-to-DFA Rule) ---

    private const string NegationDetReason =
        "Negation requires a complete DFA before complementing accept states (F' = Q \\ F).";

    private sealed class ConstraintNfaParser
    {
        private readonly List<ConstraintToken> _tokens;
        private readonly SortedSet<char> _alpha;
        private int _pos;

        public ConstraintNfaParser(List<ConstraintToken> tokens, SortedSet<char> alpha)
        {
            _tokens = tokens;
            _alpha = alpha;
        }

        public bool IsAtEnd => _pos >= _tokens.Count;
        public ConstraintToken Current => _tokens[_pos];

        public ConstraintNfaResult ParseOr()
        {
            var left = ParseAnd();
            while (!IsAtEnd && Current.Kind == ConstraintTokenKind.Or)
            {
                _pos++;
                var right = ParseAnd();
                var union = UnionNfa(left.Nfa, right.Nfa, _alpha);
                var reqDet = left.RequiredDeterminization || right.RequiredDeterminization;
                left = new ConstraintNfaResult(union, reqDet, reqDet ? NegationDetReason : null);
            }
            return left;
        }

        private ConstraintNfaResult ParseAnd()
        {
            var left = ParseNot();
            while (!IsAtEnd && Current.Kind == ConstraintTokenKind.And)
            {
                _pos++;
                var right = ParseNot();
                var reqDet = left.RequiredDeterminization || right.RequiredDeterminization;
                var inter = IntersectNfa(left.Nfa, right.Nfa, _alpha, pruneDeadStates: !reqDet);
                left = new ConstraintNfaResult(inter, reqDet, reqDet ? NegationDetReason : null);
            }
            return left;
        }

        private ConstraintNfaResult ParseNot()
        {
            if (!IsAtEnd && Current.Kind == ConstraintTokenKind.Not)
            {
                _pos++;
                var inner = ParseNot();
                var compDfa = FaOperations.Complement(inner.Nfa.ToDfa().Complete(), _alpha).Minimize();
                return new ConstraintNfaResult(compDfa.ToNfa(), RequiredDeterminization: true, DeterminizationReason: NegationDetReason);
            }
            if (!IsAtEnd && Current.Kind == ConstraintTokenKind.PrefixStar)
            {
                _pos++;
                var inner = ParseNot();
                return new ConstraintNfaResult(StarNfa(inner.Nfa, _alpha), inner.RequiredDeterminization, inner.DeterminizationReason);
            }
            return ParsePostfix();
        }

        private ConstraintNfaResult ParsePostfix()
        {
            var inner = ParsePrimary();
            while (!IsAtEnd && Current.Kind is ConstraintTokenKind.Star or ConstraintTokenKind.Plus)
            {
                var kind = Current.Kind;
                _pos++;
                var next = kind == ConstraintTokenKind.Star
                    ? StarNfa(inner.Nfa, _alpha)
                    : PlusNfa(inner.Nfa, _alpha);
                inner = new ConstraintNfaResult(next, inner.RequiredDeterminization, inner.DeterminizationReason);
            }
            return inner;
        }

        private ConstraintNfaResult ParsePrimary()
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
                return BuildAtomicNfa(atom, _alpha);
            }

            throw new FormatException($"Unexpected token '{Current.Text}' in constraint expression.");
        }
    }

    private static ConstraintNfaResult BuildAtomicNfa(string rawAtom, SortedSet<char> alpha)
    {
        var atom = Regex.Replace(rawAtom.Trim(), @"\s+", " ");

        // Negated constraints MUST determinize to a complete DFA before complementing!
        if (TryStripDoesNotPrefix(atom, out _))
        {
            var compDfa = BuildAtomicDfa(atom, alpha).Complete().Minimize();
            return new ConstraintNfaResult(compDfa.ToNfa(), RequiredDeterminization: true, DeterminizationReason: NegationDetReason);
        }

        // Positive: starts with w  =>  q0 --w--> q1 (accept, self-loop Σ)
        var m = Regex.Match(atom, @"^(?:starts? with|begins? with)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var w = Unquote(m.Groups[1].Value.Trim());
            return new ConstraintNfaResult(BuildStartsWithNfa(w, alpha), false, null);
        }

        // Positive: ends with w  =>  q0 (self-loop Σ) --w--> q1 (accept)
        m = Regex.Match(atom, @"^ends? with\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var w = Unquote(m.Groups[1].Value.Trim());
            return new ConstraintNfaResult(BuildEndsWithNfa(w, alpha), false, null);
        }

        // Positive: contains w  =>  q0 (self-loop Σ) --w--> q1 (accept, self-loop Σ)
        m = Regex.Match(atom, @"^(?:contains?|has substring)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var w = Unquote(m.Groups[1].Value.Trim());
            return new ConstraintNfaResult(BuildContainsNfa(w, alpha), false, null);
        }

        // Positive: equals w  =>  q0 --w--> q1 (accept)
        m = Regex.Match(atom, @"^(?:equals?|exact)\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var w = Unquote(m.Groups[1].Value.Trim());
            return new ConstraintNfaResult(BuildEqualsNfa(w, alpha), false, null);
        }

        // Positive: consecutive c  =>  contains cc
        m = Regex.Match(atom, @"^(?:consecutive|adjacent)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var sym = m.Groups[1].Value[0];
            return new ConstraintNfaResult(BuildContainsNfa($"{sym}{sym}", alpha), false, null);
        }

        // Positive: every X followed by Y  =>  compact 1-state string-edge NFA when X != Y
        m = Regex.Match(atom, @"^every\s+#?([^\s])\s+(?:is\s+)?followed\s+by\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var first = m.Groups[1].Value[0];
            var follower = m.Groups[2].Value[0];
            if (first != follower)
            {
                return new ConstraintNfaResult(BuildEveryFollowedByNfa(alpha, first, follower), false, null);
            }
        }

        // Positive: starts and ends with same / different symbol  =>  compact branching NFA
        m = Regex.Match(atom, @"^starts?\s+and\s+ends?\s+with\s+(?:the\s+)?(same|different)\s+symbols?$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var same = m.Groups[1].Value.Equals("same", StringComparison.OrdinalIgnoreCase);
            return new ConstraintNfaResult(BuildSameOrDifferentEndsNfa(alpha, same), false, null);
        }

        // Positive: k-th symbol from right is c  =>  classic (k+1)-state compact NFA
        m = Regex.Match(atom, @"^(\d+)(?:st|nd|rd|th)?(?:\s+(?:symbol|char|character|letter))?\s+(?:from\s+(?:right|end|last)|to\s+last)\s+(?:is|=)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var rightK))
        {
            if (rightK < 1 || rightK > 8)
            {
                throw new FormatException($"'k-th from right' supports k between 1 and 8 (found {rightK}; a complete DFA requires 2^k states).");
            }
            return new ConstraintNfaResult(BuildKthFromRightNfa(rightK, m.Groups[2].Value[0], alpha), false, null);
        }

        // Positive: k-th symbol from left is c  =>  compact (k+1)-state NFA (zero dead trap)
        m = Regex.Match(atom, @"^(\d+)(?:st|nd|rd|th)?\s+(?:symbol|char|character|letter)(?:\s+from\s+(?:left|start))?\s+(?:is|=)\s+#?([^\s])$", RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var leftK))
        {
            if (leftK < 1 || leftK > 12)
            {
                throw new FormatException($"'k-th symbol' supports k between 1 and 12 (found {leftK}).");
            }
            return new ConstraintNfaResult(BuildKthFromLeftNfa(leftK, m.Groups[2].Value[0], alpha), false, null);
        }

        // Other positive constraints (even/odd, divisible, mod, at least/at most/exactly): minimal NFA with dead sink pruned
        var baseDfa = BuildAtomicDfa(atom, alpha);
        return new ConstraintNfaResult(PruneDeadStatesAsNfa(baseDfa), false, null);
    }

    private static Nfa BuildEqualsNfa(string w, SortedSet<char> alpha)
    {
        if (AutomataSymbols.IsEmptyToken(w))
        {
            return new Nfa(new[] { "q0" }, alpha, Array.Empty<NfaTransition>(), "q0", new[] { "q0" });
        }
        return new Nfa(new[] { "q0", "q1" }, alpha, new[] { new NfaTransition("q0", w, "q1") }, "q0", new[] { "q1" });
    }

    private static Nfa BuildEveryFollowedByNfa(SortedSet<char> alpha, char first, char follower)
    {
        var transitions = new List<NfaTransition>();
        foreach (var c in alpha)
        {
            if (c != first)
            {
                transitions.Add(new NfaTransition("q0", c.ToString(), "q0"));
            }
        }
        transitions.Add(new NfaTransition("q0", $"{first}{follower}", "q0"));
        return new Nfa(new[] { "q0" }, alpha, transitions, "q0", new[] { "q0" });
    }

    private static Nfa BuildKthFromLeftNfa(int k, char targetSym, SortedSet<char> alpha)
    {
        var states = Enumerable.Range(0, k + 1).Select(i => $"q{i}").ToList();
        var transitions = new List<NfaTransition>();
        for (var i = 0; i < k - 1; i++)
        {
            foreach (var c in alpha) transitions.Add(new NfaTransition($"q{i}", c.ToString(), $"q{i + 1}"));
        }
        transitions.Add(new NfaTransition($"q{k - 1}", targetSym.ToString(), $"q{k}"));
        foreach (var c in alpha) transitions.Add(new NfaTransition($"q{k}", c.ToString(), $"q{k}"));
        return new Nfa(states, alpha, transitions, "q0", new[] { $"q{k}" });
    }

    private static Nfa BuildKthFromRightNfa(int k, char targetSym, SortedSet<char> alpha)
    {
        var states = Enumerable.Range(0, k + 1).Select(i => $"q{i}").ToList();
        var transitions = new List<NfaTransition>();
        foreach (var c in alpha) transitions.Add(new NfaTransition("q0", c.ToString(), "q0"));
        transitions.Add(new NfaTransition("q0", targetSym.ToString(), "q1"));
        for (var i = 1; i < k; i++)
        {
            foreach (var c in alpha) transitions.Add(new NfaTransition($"q{i}", c.ToString(), $"q{i + 1}"));
        }
        return new Nfa(states, alpha, transitions, "q0", new[] { $"q{k}" });
    }

    private static Nfa BuildSameOrDifferentEndsNfa(SortedSet<char> alpha, bool same)
    {
        var states = new List<string> { "q0" };
        var transitions = new List<NfaTransition>();
        var accept = new List<string>();
        var nextId = 1;

        if (same)
        {
            foreach (var c in alpha)
            {
                var mid = $"q{nextId++}";
                var acc = $"q{nextId++}";
                states.Add(mid);
                states.Add(acc);
                accept.Add(acc);

                transitions.Add(new NfaTransition("q0", c.ToString(), acc));
                transitions.Add(new NfaTransition("q0", c.ToString(), mid));
                foreach (var x in alpha) transitions.Add(new NfaTransition(mid, x.ToString(), mid));
                transitions.Add(new NfaTransition(mid, c.ToString(), acc));
            }
        }
        else
        {
            var acc = $"q{nextId++}";
            states.Add(acc);
            accept.Add(acc);
            foreach (var c in alpha)
            {
                var mid = $"q{nextId++}";
                states.Add(mid);
                transitions.Add(new NfaTransition("q0", c.ToString(), mid));
                foreach (var x in alpha) transitions.Add(new NfaTransition(mid, x.ToString(), mid));
                foreach (var d in alpha)
                {
                    if (d != c) transitions.Add(new NfaTransition(mid, d.ToString(), acc));
                }
            }
        }

        return new Nfa(states, alpha, transitions, "q0", accept);
    }

    private static Nfa BuildStartsWithNfa(string w, SortedSet<char> alpha)
    {
        if (AutomataSymbols.IsEmptyToken(w))
        {
            var loops = alpha.Select(c => new NfaTransition("q0", c.ToString(), "q0"));
            return new Nfa(new[] { "q0" }, alpha, loops, "q0", new[] { "q0" });
        }

        var transitions = new List<NfaTransition> { new("q0", w, "q1") };
        foreach (var c in alpha)
        {
            transitions.Add(new NfaTransition("q1", c.ToString(), "q1"));
        }
        return new Nfa(new[] { "q0", "q1" }, alpha, transitions, "q0", new[] { "q1" });
    }

    private static Nfa BuildEndsWithNfa(string w, SortedSet<char> alpha)
    {
        if (AutomataSymbols.IsEmptyToken(w))
        {
            var loops = alpha.Select(c => new NfaTransition("q0", c.ToString(), "q0"));
            return new Nfa(new[] { "q0" }, alpha, loops, "q0", new[] { "q0" });
        }

        var transitions = new List<NfaTransition>();
        foreach (var c in alpha)
        {
            transitions.Add(new NfaTransition("q0", c.ToString(), "q0"));
        }
        transitions.Add(new NfaTransition("q0", w, "q1"));
        return new Nfa(new[] { "q0", "q1" }, alpha, transitions, "q0", new[] { "q1" });
    }

    private static Nfa BuildContainsNfa(string w, SortedSet<char> alpha)
    {
        if (AutomataSymbols.IsEmptyToken(w))
        {
            var loops = alpha.Select(c => new NfaTransition("q0", c.ToString(), "q0"));
            return new Nfa(new[] { "q0" }, alpha, loops, "q0", new[] { "q0" });
        }

        var transitions = new List<NfaTransition>();
        foreach (var c in alpha)
        {
            transitions.Add(new NfaTransition("q0", c.ToString(), "q0"));
            transitions.Add(new NfaTransition("q1", c.ToString(), "q1"));
        }
        transitions.Add(new NfaTransition("q0", w, "q1"));
        return new Nfa(new[] { "q0", "q1" }, alpha, transitions, "q0", new[] { "q1" });
    }

    private static Nfa StarNfa(Nfa inner, SortedSet<char> alpha)
    {
        var states = new List<string> { "q0" };
        var transitions = new List<NfaTransition>();
        var accept = new HashSet<string>(StringComparer.Ordinal) { "q0" };
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var nextId = 1;

        foreach (var s in inner.OrderedStates)
        {
            var renamed = $"q{nextId++}";
            map[s] = renamed;
            states.Add(renamed);
            if (inner.AcceptStates.Contains(s)) accept.Add(renamed);
        }

        foreach (var t in inner.Transitions)
        {
            transitions.Add(new NfaTransition(map[t.FromState], t.Symbol, map[t.ToState]));
        }

        var innerStart = map[inner.StartState];
        transitions.Add(new NfaTransition("q0", AutomataSymbols.Empty, innerStart));
        foreach (var acc in inner.AcceptStates)
        {
            transitions.Add(new NfaTransition(map[acc], AutomataSymbols.Empty, innerStart));
        }

        return new Nfa(states, alpha, transitions, "q0", accept);
    }

    private static Nfa PlusNfa(Nfa inner, SortedSet<char> alpha)
    {
        var transitions = inner.Transitions.ToList();
        foreach (var acc in inner.AcceptStates)
        {
            transitions.Add(new NfaTransition(acc, AutomataSymbols.Empty, inner.StartState));
        }
        return new Nfa(inner.OrderedStates, alpha, transitions, inner.StartState, inner.AcceptStates);
    }

    private static Nfa UnionNfa(Nfa first, Nfa second, SortedSet<char> alpha)
    {
        var states = new List<string> { "q0" };
        var transitions = new List<NfaTransition>();
        var accept = new HashSet<string>(StringComparer.Ordinal);
        var nextId = 1;

        string AddBranch(Nfa branch)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in branch.OrderedStates)
            {
                var renamed = $"q{nextId++}";
                map[s] = renamed;
                states.Add(renamed);
                if (branch.AcceptStates.Contains(s)) accept.Add(renamed);
            }
            foreach (var t in branch.Transitions)
            {
                transitions.Add(new NfaTransition(map[t.FromState], t.Symbol, map[t.ToState]));
            }
            return map[branch.StartState];
        }

        var s1 = AddBranch(first);
        var s2 = AddBranch(second);
        transitions.Add(new NfaTransition("q0", AutomataSymbols.Empty, s1));
        transitions.Add(new NfaTransition("q0", AutomataSymbols.Empty, s2));

        return new Nfa(states, alpha, transitions, "q0", accept);
    }

    private static Nfa IntersectNfa(Nfa first, Nfa second, SortedSet<char> alpha, bool pruneDeadStates)
    {
        var d1 = first.ToDfa().Complete();
        var d2 = second.ToDfa().Complete();
        var prod = FaOperations.Intersection(d1, d2).Minimize();
        return pruneDeadStates ? PruneDeadStatesAsNfa(prod) : prod.ToNfa();
    }

    private static Nfa PruneDeadStatesAsNfa(Dfa dfa)
    {
        // Reverse BFS from accept states to find co-accessible (live) states
        var revAdj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var q in dfa.States) revAdj[q] = new List<string>();
        foreach (var ((from, _), to) in dfa.Transitions)
        {
            revAdj[to].Add(from);
        }

        var live = new HashSet<string>(dfa.AcceptStates, StringComparer.Ordinal);
        var queue = new Queue<string>(dfa.AcceptStates);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var prev in revAdj[cur])
            {
                if (live.Add(prev)) queue.Enqueue(prev);
            }
        }
        live.Add(dfa.StartState);

        var transitions = new List<NfaTransition>();
        foreach (var ((from, sym), to) in dfa.Transitions)
        {
            if (live.Contains(from) && live.Contains(to))
            {
                transitions.Add(new NfaTransition(from, sym.ToString(), to));
            }
        }

        var pruned = new Nfa(live, dfa.OrderedAlphabet, transitions, dfa.StartState, dfa.AcceptStates);
        return pruned.CompactLiteralChains();
    }
}
