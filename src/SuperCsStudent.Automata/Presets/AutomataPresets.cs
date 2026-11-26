using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Synthesis;
using SuperCsStudent.Automata.Turing;

namespace SuperCsStudent.Automata.Presets;

public enum AutomataPresetKind
{
    FiniteAutomaton,
    ContextFreeGrammar,
    PushdownAutomaton,
    TuringMachine
}

public sealed record AutomataPreset(
    string Key,
    string Title,
    string Description,
    AutomataPresetKind Kind,
    string DefaultInput,
    IReadOnlyList<string> SampleInputs,
    string? RegexPattern = null,
    string? ConstraintSpec = null,
    string? GrammarText = null,
    string? PdaTransitionsText = null,
    string? TmSchemaText = null,
    Func<Dfa>? BuildDfa = null,
    Func<Cfg>? BuildCfg = null,
    Func<PushdownAutomaton>? BuildPda = null,
    Func<TmSchema>? BuildTmSchema = null);

/// <summary>
/// Curated catalog of Finite Automaton, Context-Free Grammar, Pushdown Automaton,
/// and Turing Machine presets ready for interactive studio inspection and testing.
/// </summary>
public static class AutomataPresets
{
    private const string PdaAnBnText =
        "start: s\n" +
        "accept: f\n" +
        "((s, a, e), (s, a))\n" +
        "((s, e, e), (f, e))\n" +
        "((f, b, a), (f, e))";

    private const string PdaPalindromeText =
        "start: s\n" +
        "accept: f\n" +
        "((s, a, e), (s, a))\n" +
        "((s, b, e), (s, b))\n" +
        "((s, e, e), (f, e))\n" +
        "((s, a, e), (f, e))\n" +
        "((s, b, e), (f, e))\n" +
        "((f, a, a), (f, e))\n" +
        "((f, b, b), (f, e))";

    private const string PdaParensText =
        "start: s\n" +
        "accept: s\n" +
        "((s, (, e), (s, ()) \n" +
        "((s, ), (), (s, e))";

    private const string CfgArithText =
        "E -> E+T | T\n" +
        "T -> T*F | F\n" +
        "F -> (E) | x | y";

    public static IReadOnlyList<AutomataPreset> All { get; }
    public static IReadOnlyDictionary<string, AutomataPreset> ByKey { get; }

    static AutomataPresets()
    {
        var tmPalindrome = LanguageSpecCompiler.BuildPalindromeSchema(evenOnly: false);
        var tmAnBn = LanguageSpecCompiler.BuildAnBnSchema();
        var tmAnBnCn = LanguageSpecCompiler.BuildAnBnCnSchema();
        var tmA2nBn = LanguageSpecCompiler.BuildA2nBnSchema();
        var tmAbStar = LanguageSpecCompiler.DfaToDecidableTmSchema(
            RegexCompiler.CompileToMinimalDfa("a*b*a*", new[] { 'a', 'b' }),
            "Decider for a*b*a*");
        var tmInvert = LanguageSpecCompiler.BuildInvertABSchema();
        var tmShiftLeft = LanguageSpecCompiler.BuildShiftLeftSchema();
        var tmStartsA = LanguageSpecCompiler.DfaToDecidableTmSchema(
            LanguageSpecCompiler.CompileConstraintToDfa("starts with a", new[] { 'a', 'b' }),
            "Decider for starts with a");

        var list = new List<AutomataPreset>
        {
            new(
                Key: "tm-palindrome",
                Title: "TM: Palindromes (w = wᴿ)",
                Description: "Decides palindromes over {a, b} by peeling matching outer symbols with # R_# S_L.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "abba",
                SampleInputs: new[] { "e", "a", "aba", "abba", "abaa", "ab" },
                TmSchemaText: tmPalindrome.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildPalindromeSchema(evenOnly: false)),

            new(
                Key: "pda-anbn",
                Title: "PDA: Equal aⁿbⁿ",
                Description: "Generalized 6-tuple PDA accepting { aⁿbⁿ | n ≥ 0 } by empty stack and accepting state.",
                Kind: AutomataPresetKind.PushdownAutomaton,
                DefaultInput: "aabb",
                SampleInputs: new[] { "aabb", "e", "ab", "abb", "aab" },
                PdaTransitionsText: PdaAnBnText,
                BuildPda: () => PushdownAutomaton.ParseTransitions(PdaAnBnText)),

            new(
                Key: "dfa-contains-aba",
                Title: "DFA: Contains Substring 'aba'",
                Description: "Minimal 4-state DFA accepting strings over {a, b} containing 'aba'.",
                Kind: AutomataPresetKind.FiniteAutomaton,
                DefaultInput: "bbabaa",
                SampleInputs: new[] { "aba", "bbabaa", "aabb", "bbb", "e" },
                RegexPattern: "(a|b)*aba(a|b)*",
                ConstraintSpec: "contains aba",
                BuildDfa: () => RegexCompiler.CompileToMinimalDfa("(a|b)*aba(a|b)*", new[] { 'a', 'b' })),

            new(
                Key: "cfg-arith",
                Title: "CFG: Arithmetic Expressions",
                Description: "Left-recursive context-free grammar for +, *, parentheses, and variables x, y.",
                Kind: AutomataPresetKind.ContextFreeGrammar,
                DefaultInput: "(x+y)+x",
                SampleInputs: new[] { "(x+y)+x", "x+y*x", "(x*y)+(y*x)", "x+", "((x)" },
                GrammarText: CfgArithText,
                BuildCfg: () => Cfg.ParseFromText(CfgArithText)),

            new(
                Key: "tm-anbn",
                Title: "TM: Equal aⁿbⁿ",
                Description: "Decides { aⁿbⁿ | n ≥ 0 } by iteratively removing outer 'a' and 'b' pairs via S_L.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "aabb",
                SampleInputs: new[] { "aabb", "e", "aab", "abb", "ba", "bab" },
                TmSchemaText: tmAnBn.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildAnBnSchema()),

            new(
                Key: "tm-anbncn",
                Title: "TM: Equal Triples aⁿbⁿcⁿ",
                Description: "Decides the context-sensitive language { aⁿbⁿcⁿ | n ≥ 0 } using tape marks X, Q, Z.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "aabbcc",
                SampleInputs: new[] { "aabbcc", "abc", "e", "aabbc", "abbcc", "abcc" },
                TmSchemaText: tmAnBnCn.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildAnBnCnSchema()),

            new(
                Key: "tm-a2nbn",
                Title: "TM: Double Prefix a²ⁿbⁿ",
                Description: "Decides { a²ⁿbⁿ | n ≥ 0 } by matching two leading a's for each trailing b.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "aaaabb",
                SampleInputs: new[] { "e", "aab", "aaaabb", "ab", "aa", "aabb" },
                TmSchemaText: tmA2nBn.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildA2nBnSchema()),

            new(
                Key: "tm-abstar",
                Title: "TM: Regular Pattern a*b*a*",
                Description: "Decides L(a*b*a*) over {a, b} via a compiled DFA-to-TM schema.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "abba",
                SampleInputs: new[] { "abba", "aba", "e", "bbb", "babab", "baba" },
                RegexPattern: "a*b*a*",
                TmSchemaText: tmAbStar.ToDslText(),
                BuildDfa: () => RegexCompiler.CompileToMinimalDfa("a*b*a*", new[] { 'a', 'b' }),
                BuildTmSchema: () => LanguageSpecCompiler.DfaToDecidableTmSchema(
                    RegexCompiler.CompileToMinimalDfa("a*b*a*", new[] { 'a', 'b' }),
                    "Decider for a*b*a*")),

            new(
                Key: "tm-invert",
                Title: "TM: Invert a ↔ b",
                Description: "Transducer swapping every 'a' and 'b' on the tape (#aab# → #bba#).",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "aab",
                SampleInputs: new[] { "aab", "e", "baba", "aaa" },
                TmSchemaText: tmInvert.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildInvertABSchema()),

            new(
                Key: "tm-shift-left",
                Title: "TM: Shift Left (S_L)",
                Description: "Fundamental S_L building-block macro shifting a non-blank word left by one cell.",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "ba",
                SampleInputs: new[] { "ba", "aabb", "a" },
                TmSchemaText: tmShiftLeft.ToDslText(),
                BuildTmSchema: () => LanguageSpecCompiler.BuildShiftLeftSchema()),

            new(
                Key: "tm-starts-a",
                Title: "TM: Starts with 'a'",
                Description: "Decidable TM schema accepting strings over {a, b} that start with 'a' (#abb# → #y#).",
                Kind: AutomataPresetKind.TuringMachine,
                DefaultInput: "abb",
                SampleInputs: new[] { "abb", "a", "bba", "e" },
                ConstraintSpec: "starts with a",
                TmSchemaText: tmStartsA.ToDslText(),
                BuildDfa: () => LanguageSpecCompiler.CompileConstraintToDfa("starts with a", new[] { 'a', 'b' }),
                BuildTmSchema: () => LanguageSpecCompiler.DfaToDecidableTmSchema(
                    LanguageSpecCompiler.CompileConstraintToDfa("starts with a", new[] { 'a', 'b' }),
                    "Decider for starts with a")),

            new(
                Key: "pda-palindrome",
                Title: "PDA: Palindromes (Odd & Even)",
                Description: "Nondeterministic PDA accepting all palindromes over {a, b}.",
                Kind: AutomataPresetKind.PushdownAutomaton,
                DefaultInput: "abba",
                SampleInputs: new[] { "e", "a", "aba", "abba", "ab", "abaa" },
                PdaTransitionsText: PdaPalindromeText,
                BuildPda: () => PushdownAutomaton.ParseTransitions(PdaPalindromeText)),

            new(
                Key: "pda-parens",
                Title: "PDA: Balanced Parentheses",
                Description: "Single-state PDA recognizing well-formed parenthesis strings over {(, )}.",
                Kind: AutomataPresetKind.PushdownAutomaton,
                DefaultInput: "(()())",
                SampleInputs: new[] { "e", "()", "(())", "(()())", ")(", "(()" },
                PdaTransitionsText: PdaParensText,
                BuildPda: () => PushdownAutomaton.ParseTransitions(PdaParensText)),

            new(
                Key: "dfa-even-b",
                Title: "DFA: Even Number of b's",
                Description: "Minimal 2-state DFA accepting strings over {a, b} with an even count of 'b'.",
                Kind: AutomataPresetKind.FiniteAutomaton,
                DefaultInput: "abab",
                SampleInputs: new[] { "e", "aaa", "abab", "bb", "b", "abbba" },
                ConstraintSpec: "even b",
                BuildDfa: () => LanguageSpecCompiler.CompileConstraintToDfa("even b", new[] { 'a', 'b' })),

            new(
                Key: "dfa-bb-bab-aa",
                Title: "DFA: Starts bb, Contains bab, Ends aa",
                Description: "Exact minimal DFA synthesized from Boolean conjunction: starts with bb AND contains bab AND ends with aa.",
                Kind: AutomataPresetKind.FiniteAutomaton,
                DefaultInput: "bbabaa",
                SampleInputs: new[] { "bbabaa", "bbbabaa", "bbaa", "bbaba", "abaa" },
                ConstraintSpec: "starts with bb AND contains bab AND ends with aa",
                BuildDfa: () => LanguageSpecCompiler.CompileConstraintToDfa(
                    "starts with bb AND contains bab AND ends with aa",
                    new[] { 'a', 'b' }))
        };

        All = list;
        ByKey = list.ToDictionary(p => p.Key, StringComparer.Ordinal);
    }

    public static AutomataPreset Get(string key)
    {
        if (ByKey.TryGetValue(key, out var preset))
        {
            return preset;
        }
        throw new KeyNotFoundException($"Unknown automata preset key '{key}'.");
    }
}
