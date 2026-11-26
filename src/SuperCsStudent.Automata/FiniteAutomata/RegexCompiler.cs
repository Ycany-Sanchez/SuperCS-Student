namespace SuperCsStudent.Automata.FiniteAutomata;

public abstract record RegexNode;
public sealed record EmptySetRegexNode : RegexNode;
public sealed record EpsilonRegexNode : RegexNode;
public sealed record LiteralRegexNode(char Symbol) : RegexNode;
public sealed record ConcatRegexNode(RegexNode Left, RegexNode Right) : RegexNode;
public sealed record UnionRegexNode(RegexNode Left, RegexNode Right) : RegexNode;
public sealed record StarRegexNode(RegexNode Inner) : RegexNode;
public sealed record PlusRegexNode(RegexNode Inner) : RegexNode;
public sealed record OptionalRegexNode(RegexNode Inner) : RegexNode;

public sealed record RegexCompilationResult(
    string Pattern,
    RegexNode Ast,
    Nfa Nfa,
    Dfa Dfa,
    Dfa MinimalDfa);

/// <summary>
/// Recursive-descent regular expression parser and compiler:
/// supports union (|, +, ∪), concatenation (implicit or .), *, +, ?, grouping (), empty string (e, ε, λ), and empty set (∅).
/// Compiles Regex -> NFA (Thompson construction) -> DFA (subset construction) -> Minimal DFA (partition refinement).
/// </summary>
public static class RegexCompiler
{
    public static RegexNode Parse(string pattern)
    {
        if (pattern is null)
        {
            throw new ArgumentNullException(nameof(pattern));
        }

        var cleaned = new string(pattern.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (cleaned.Length == 0)
        {
            return new EpsilonRegexNode();
        }

        var parser = new Parser(cleaned);
        var ast = parser.ParseExpression();
        if (!parser.IsAtEnd)
        {
            throw new FormatException($"Unexpected character '{parser.Current}' at position {parser.Position} in regex '{pattern}'.");
        }
        return ast;
    }

    public static Nfa CompileToNfa(string pattern, IEnumerable<char>? extraAlphabet = null)
    {
        var ast = Parse(pattern);
        return BuildNfaFromAst(ast, extraAlphabet);
    }

    public static Dfa CompileToDfa(string pattern, IEnumerable<char>? extraAlphabet = null)
    {
        return CompileToNfa(pattern, extraAlphabet).ToDfa();
    }

    public static Dfa CompileToMinimalDfa(string pattern, IEnumerable<char>? extraAlphabet = null)
    {
        return CompileToNfa(pattern, extraAlphabet).ToMinimalDfa();
    }

    public static RegexCompilationResult Compile(string pattern, IEnumerable<char>? extraAlphabet = null)
    {
        var ast = Parse(pattern);
        var nfa = BuildNfaFromAst(ast, extraAlphabet);
        var dfa = nfa.ToDfa();
        var minDfa = dfa.Minimize();
        return new RegexCompilationResult(pattern, ast, nfa, dfa, minDfa);
    }

    public static Nfa BuildNfaFromAst(RegexNode ast, IEnumerable<char>? extraAlphabet = null)
    {
        var states = new List<string>();
        var transitions = new List<NfaTransition>();
        var alphabet = new SortedSet<char>();
        if (extraAlphabet is not null)
        {
            foreach (var c in extraAlphabet)
            {
                alphabet.Add(c);
            }
        }

        var nextId = 0;
        string NewState()
        {
            var s = $"n{nextId++}";
            states.Add(s);
            return s;
        }

        (string Start, string Accept) Build(RegexNode node)
        {
            switch (node)
            {
                case EmptySetRegexNode:
                {
                    var s = NewState();
                    var f = NewState();
                    return (s, f);
                }
                case EpsilonRegexNode:
                {
                    var s = NewState();
                    var f = NewState();
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, f));
                    return (s, f);
                }
                case LiteralRegexNode lit:
                {
                    alphabet.Add(lit.Symbol);
                    var s = NewState();
                    var f = NewState();
                    transitions.Add(new NfaTransition(s, lit.Symbol.ToString(), f));
                    return (s, f);
                }
                case ConcatRegexNode concat:
                {
                    var left = Build(concat.Left);
                    var right = Build(concat.Right);
                    transitions.Add(new NfaTransition(left.Accept, AutomataSymbols.Empty, right.Start));
                    return (left.Start, right.Accept);
                }
                case UnionRegexNode union:
                {
                    var s = NewState();
                    var f = NewState();
                    var left = Build(union.Left);
                    var right = Build(union.Right);
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, left.Start));
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, right.Start));
                    transitions.Add(new NfaTransition(left.Accept, AutomataSymbols.Empty, f));
                    transitions.Add(new NfaTransition(right.Accept, AutomataSymbols.Empty, f));
                    return (s, f);
                }
                case StarRegexNode star:
                {
                    var s = NewState();
                    var f = NewState();
                    var inner = Build(star.Inner);
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, inner.Start));
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, f));
                    transitions.Add(new NfaTransition(inner.Accept, AutomataSymbols.Empty, inner.Start));
                    transitions.Add(new NfaTransition(inner.Accept, AutomataSymbols.Empty, f));
                    return (s, f);
                }
                case PlusRegexNode plus:
                {
                    var s = NewState();
                    var f = NewState();
                    var inner = Build(plus.Inner);
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, inner.Start));
                    transitions.Add(new NfaTransition(inner.Accept, AutomataSymbols.Empty, inner.Start));
                    transitions.Add(new NfaTransition(inner.Accept, AutomataSymbols.Empty, f));
                    return (s, f);
                }
                case OptionalRegexNode opt:
                {
                    var s = NewState();
                    var f = NewState();
                    var inner = Build(opt.Inner);
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, inner.Start));
                    transitions.Add(new NfaTransition(s, AutomataSymbols.Empty, f));
                    transitions.Add(new NfaTransition(inner.Accept, AutomataSymbols.Empty, f));
                    return (s, f);
                }
                default:
                    throw new InvalidOperationException($"Unsupported regex AST node: {node.GetType().Name}");
            }
        }

        var (start, accept) = Build(ast);
        return new Nfa(states, alphabet, transitions, start, new[] { accept });
    }

    private sealed class Parser
    {
        private readonly string _input;
        private readonly bool _hasExplicitUnionOp;
        public int Position { get; private set; }

        public Parser(string input)
        {
            _input = input;
            _hasExplicitUnionOp = ContainsUnescaped(input, '|') || ContainsUnescaped(input, '∪');
        }

        public bool IsAtEnd => Position >= _input.Length;
        public char Current => _input[Position];

        private static bool ContainsUnescaped(string s, char target)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (s[i] == target)
                {
                    return true;
                }
            }
            return false;
        }

        public RegexNode ParseExpression()
        {
            var left = ParseConcat();
            while (!IsAtEnd && IsUnionOperatorAtCurrent())
            {
                Position++;
                var right = ParseConcat();
                left = new UnionRegexNode(left, right);
            }
            return left;
        }

        private bool IsUnionOperatorAtCurrent()
        {
            if (IsAtEnd) return false;
            if (Current is '|' or '∪') return true;
            // Disambiguate '+' between algebraic union (e.g., (a+b)*) and Kleene plus (e.g., a+ or (a|b)+c)
            if (Current == '+' && !_hasExplicitUnionOp)
            {
                return Position + 1 < _input.Length && CanStartAtom(_input[Position + 1]);
            }
            return false;
        }

        private static bool CanStartAtom(char c)
        {
            return c is not (')' or '|' or '∪' or '*' or '?' or '.' or '+');
        }

        private RegexNode ParseConcat()
        {
            RegexNode? left = null;
            while (!IsAtEnd && Current != ')' && !IsUnionOperatorAtCurrent())
            {
                if (Current == '.')
                {
                    if (left is null)
                    {
                        throw new FormatException($"Unexpected '.' at position {Position}.");
                    }
                    Position++;
                    var explicitRight = ParseQuantified();
                    left = new ConcatRegexNode(left, explicitRight);
                    continue;
                }

                var term = ParseQuantified();
                left = left is null ? term : new ConcatRegexNode(left, term);
            }

            return left ?? new EpsilonRegexNode();
        }

        private RegexNode ParseQuantified()
        {
            var atom = ParseAtom();
            while (!IsAtEnd)
            {
                if (Current == '*')
                {
                    Position++;
                    atom = new StarRegexNode(atom);
                }
                else if (Current == '?')
                {
                    Position++;
                    atom = new OptionalRegexNode(atom);
                }
                else if (Current == '+' && !IsUnionOperatorAtCurrent())
                {
                    Position++;
                    atom = new PlusRegexNode(atom);
                }
                else
                {
                    break;
                }
            }
            return atom;
        }

        private RegexNode ParseAtom()
        {
            if (IsAtEnd)
            {
                throw new FormatException("Unexpected end of regular expression.");
            }

            if (Current == '(')
            {
                Position++;
                var inner = ParseExpression();
                if (IsAtEnd || Current != ')')
                {
                    throw new FormatException($"Expected ')' at position {Position}.");
                }
                Position++;
                return inner;
            }

            if (Current == '\\')
            {
                Position++;
                if (IsAtEnd)
                {
                    throw new FormatException("Trailing escape character '\\' in regular expression.");
                }
                var escaped = Current;
                Position++;
                return new LiteralRegexNode(escaped);
            }

            if (Current is 'ε' or 'λ' or 'e')
            {
                Position++;
                return new EpsilonRegexNode();
            }

            if (Current is '∅' or 'Ø')
            {
                Position++;
                return new EmptySetRegexNode();
            }

            if (Current is ')' or '|' or '∪' or '*' or '+' or '?' or '.')
            {
                throw new FormatException($"Unexpected operator '{Current}' at position {Position}.");
            }

            var ch = Current;
            Position++;
            return new LiteralRegexNode(ch);
        }
    }
}
