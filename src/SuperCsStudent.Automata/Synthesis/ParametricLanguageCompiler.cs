using System.Text;
using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Turing;

namespace SuperCsStudent.Automata.Synthesis;

/// <summary>
/// 100% deterministic formal-language synthesizer for user-authored parametric specifications:
///   - Independent bounded blocks -> Regular (NFA, Minimal DFA, Regular Grammar, Decidable TM)
///   - 2-block linear constraints (n > m, n >= 2m, n != m, n = 2m+1, n = m or n = 2m, m < n < 2m)
///     -> ContextFree (Exact CFG, PDA, Decidable TM)
///   - 3-block linear constraints:
///       * 2-of-3 constrained, 1 free (n > m, n = k, a^n b^m a^k where n > k) -> ContextFree
///       * 3-variable additive (n + m op k, n op m + k, m op n + k)           -> ContextFree
///       * Disjunction (i = j or j = k)                                       -> ContextFree
///       * Coupled conjunction across all 3 blocks (n > m > k, i = j = k)     -> ContextSensitive (TM)
///   - Unordered symbol-count languages (#a > #b, #x >= #y, #0 != #1, #a = 2*#b) -> ContextFree
/// </summary>
public static class ParametricLanguageCompiler
{
    private const int MaxCoeff = 6;
    private const int MaxOffset = 10;
    private const int MaxBound = 10;

    public static LanguageSpecCompilationResult? TryCompile(string rawSpec, IEnumerable<char>? explicitAlphabet = null)
    {
        var parsed = ParametricSpecParser.TryParse(rawSpec, explicitAlphabet);
        if (parsed is null) return null;

        return parsed switch
        {
            BlockExponentSpec blockSpec => CompileBlockSpec(blockSpec),
            SymbolCountSpec countSpec => CompileSymbolCountSpec(countSpec),
            _ => null
        };
    }

    // =========================================================================
    // EXACT GROUND-TRUTH ORACLE & AUTO TEST SUITE GENERATOR
    // =========================================================================

    public static bool EvaluateSpec(ParametricSpec spec, string word) => spec switch
    {
        BlockExponentSpec b => EvaluateBlockSpec(b, word),
        SymbolCountSpec c => EvaluateSymbolCountSpec(c, word),
        _ => false
    };

    private static bool EvaluateSymbolCountSpec(SymbolCountSpec spec, string word)
    {
        var allowed = new HashSet<char>(spec.Alphabet);
        int c1 = 0, c2 = 0;
        foreach (var ch in word)
        {
            if (!allowed.Contains(ch)) return false;
            if (ch == spec.Symbol1) c1++;
            if (ch == spec.Symbol2) c2++;
        }
        return LinearConstraint.EvaluateOp(spec.Coeff1 * c1 - spec.Coeff2 * c2, spec.Op, spec.Offset);
    }

    private static bool EvaluateBlockSpec(BlockExponentSpec spec, string word)
    {
        var normalizedBranches = NormalizeBranches(spec);
        if (!TryExtractBlockCounts(word, spec.Blocks, out var counts))
        {
            return false;
        }
        return normalizedBranches.Any(br => br.SatisfiedBy(counts));
    }

    private static bool TryExtractBlockCounts(string word, IReadOnlyList<BlockTerm> blocks, out int[] counts)
    {
        counts = new int[blocks.Count];
        var idx = 0;
        for (var b = 0; b < blocks.Count; b++)
        {
            var sym = blocks[b].Symbol;
            var start = idx;
            while (idx < word.Length && word[idx] == sym) idx++;
            counts[b] = idx - start;
        }
        return idx == word.Length;
    }

    // =========================================================================
    // BLOCK SPECIFICATION NORMALIZATION
    // =========================================================================

    private sealed record TwoVarRelation(int BlockA, int BlockB, int CoeffA, int CoeffB, CompareOp Op, int Offset, int StepA = 0, int StepB = 0)
    {
        // Represents: CoeffA * count[BlockA] - CoeffB * count[BlockB] (Op) Offset, with CoeffA > 0, CoeffB > 0
        // Optional StepA/StepB > 0 enforces count[BlockA] % StepA == 0 and count[BlockB] % StepB == 0
        public bool SatisfiedBy(int[] counts) =>
            LinearConstraint.EvaluateOp(CoeffA * counts[BlockA] - CoeffB * counts[BlockB], Op, Offset)
            && (StepA <= 0 || (counts[BlockA] % StepA == 0 && counts[BlockB] % StepB == 0));
    }

    private sealed record ThreeVarAdditiveRelation(int SumA, int SumB, int Target, CompareOp Op, int Offset)
    {
        // Represents: count[SumA] + count[SumB] (Op) count[Target] + Offset
        public bool SatisfiedBy(int[] counts) =>
            LinearConstraint.EvaluateOp(counts[SumA] + counts[SumB] - counts[Target], Op, Offset);
    }

    private sealed record NormalizedBranch(
        VariableBounds[] Bounds,
        IReadOnlyList<TwoVarRelation> TwoVarRelations,
        IReadOnlyList<ThreeVarAdditiveRelation> AdditiveRelations,
        IReadOnlyList<(int BlockIdx, int Mod, int Rem)> ModConstraints,
        bool Unsatisfiable)
    {
        public bool SatisfiedBy(int[] counts)
        {
            if (Unsatisfiable) return false;
            for (var i = 0; i < Bounds.Length; i++)
            {
                if (!Bounds[i].Contains(counts[i])) return false;
            }
            foreach (var (idx, mod, rem) in ModConstraints)
            {
                if (((counts[idx] % mod) + mod) % mod != rem) return false;
            }
            foreach (var rel in TwoVarRelations)
            {
                if (!rel.SatisfiedBy(counts)) return false;
            }
            foreach (var add in AdditiveRelations)
            {
                if (!add.SatisfiedBy(counts)) return false;
            }
            return true;
        }
    }

    private static List<NormalizedBranch> NormalizeBranches(BlockExponentSpec spec)
    {
        var r = spec.Blocks.Count;
        var results = new List<NormalizedBranch>();

        foreach (var rawBranch in spec.Branches)
        {
            results.Add(NormalizeSingleBranch(spec.Blocks, rawBranch));
        }
        return results;
    }

    private static NormalizedBranch NormalizeSingleBranch(IReadOnlyList<BlockTerm> blocks, ConstraintBranch rawBranch)
    {
        var r = blocks.Count;
        var bounds = Enumerable.Range(0, r).Select(_ => new VariableBounds(0, null)).ToArray();
        var modConstraints = new List<(int BlockIdx, int Mod, int Rem)>();

        // Collect all pattern variables (default >= 0) and map them onto block variables _b0, _b1, _b2
        var allVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var blk in blocks)
        {
            foreach (var v in blk.Exponent.Terms.Keys) allVars.Add(v);
        }
        foreach (var c in rawBranch.Constraints)
        {
            foreach (var v in c.Left.Terms.Keys) allVars.Add(v);
            foreach (var v in c.Right.Terms.Keys) allVars.Add(v);
        }

        // Check for unknown variables in predicate that never appeared in the block pattern
        var patternVars = new HashSet<string>(
            blocks.SelectMany(b => b.Exponent.Terms.Keys),
            StringComparer.Ordinal);
        var extraVars = allVars.Except(patternVars).ToList();
        if (extraVars.Count > 0)
        {
            throw new FormatException(
                $"Constraint references variable(s) '{string.Join(", ", extraVars)}' not present in the block pattern.");
        }

        // Build block equations: _b{i} = blocks[i].Exponent
        var equations = new List<LinearConstraint>();
        var substitutions = new Dictionary<string, AffineExpr>(StringComparer.Ordinal);
        var resolvedBlocks = new HashSet<int>();

        // Pass 1: find variables appearing alone with coeff 1 in a block: _b{i} = v + c  =>  v = _b{i} - c
        for (var i = 0; i < r; i++)
        {
            var exp = blocks[i].Exponent;
            if (exp.Terms.Count == 1)
            {
                var kv = exp.Terms.First();
                if (kv.Value == 1 && !substitutions.ContainsKey(kv.Key))
                {
                    substitutions[kv.Key] = AffineExpr.FromVariable($"_b{i}").Subtract(AffineExpr.FromConstant(exp.Constant));
                    resolvedBlocks.Add(i);
                    if (exp.Constant > 0)
                    {
                        bounds[i] = bounds[i].WithMin(exp.Constant);
                    }
                    else if (exp.Constant < 0)
                    {
                        throw new FormatException($"Block exponent '{exp.Format()}' can produce negative length.");
                    }
                }
            }
        }

        // Pass 2: if a variable appears with coeff c > 1 in a single-term block and wasn't resolved yet (e.g. a^(2n) b^(3n) or a^(2n) b^m)
        var scaledVarBlock = new Dictionary<string, (int BlockIdx, int Coeff, int Const)>(StringComparer.Ordinal);
        for (var i = 0; i < r; i++)
        {
            if (resolvedBlocks.Contains(i)) continue;
            var exp = blocks[i].Exponent;
            if (exp.Terms.Count == 1)
            {
                var kv = exp.Terms.First();
                if (!substitutions.ContainsKey(kv.Key) && !scaledVarBlock.ContainsKey(kv.Key) && kv.Value > 1)
                {
                    scaledVarBlock[kv.Key] = (i, kv.Value, exp.Constant);
                    resolvedBlocks.Add(i);
                    bounds[i] = bounds[i].WithMin(System.Math.Max(0, exp.Constant));
                    modConstraints.Add((i, kv.Value, ((exp.Constant % kv.Value) + kv.Value) % kv.Value));
                }
            }
        }

        // Helper to substitute all known variables into a linear constraint (scaling by LCM if scaledVarBlock used)
        LinearConstraint EliminateVariables(LinearConstraint c)
        {
            var cur = c;
            foreach (var (v, repl) in substitutions)
            {
                cur = cur.Substitute(v, repl);
            }
            foreach (var (v, info) in scaledVarBlock)
            {
                if (cur.Left.Terms.ContainsKey(v) || cur.Right.Terms.ContainsKey(v))
                {
                    // Multiply both sides by info.Coeff (>0) so info.Coeff * v = _b{idx} - Const
                    var scaledL = cur.Left.Scale(info.Coeff);
                    var scaledR = cur.Right.Scale(info.Coeff);
                    // Now coeff of v in scaledL/scaledR is divisible by info.Coeff; replace info.Coeff*v with (_b{idx} - Const)
                    AffineExpr ReplaceScaled(AffineExpr e)
                    {
                        if (!e.Terms.TryGetValue(v, out var cv)) return e;
                        var q = cv / info.Coeff;
                        var rest = new AffineExpr(e.Terms.Where(kv => kv.Key != v), e.Constant);
                        var repl = AffineExpr.FromVariable($"_b{info.BlockIdx}").Subtract(AffineExpr.FromConstant(info.Const));
                        return rest.Add(repl.Scale(q));
                    }
                    cur = new LinearConstraint(ReplaceScaled(scaledL), cur.Op, ReplaceScaled(scaledR));
                }
            }
            return cur;
        }

        // Add implicit equations for unresolved blocks (e.g., block 1 in a^n b^n, block 2 in a^n b^m c^(n+m), constant blocks)
        for (var i = 0; i < r; i++)
        {
            if (resolvedBlocks.Contains(i)) continue;
            var blkEq = new LinearConstraint(AffineExpr.FromVariable($"_b{i}"), CompareOp.Eq, blocks[i].Exponent);
            equations.Add(EliminateVariables(blkEq));
        }

        // Add implicit v >= 0 for every pattern variable
        foreach (var v in patternVars)
        {
            equations.Add(EliminateVariables(
                new LinearConstraint(AffineExpr.FromVariable(v), CompareOp.Geq, AffineExpr.FromConstant(0))));
        }

        // Add user constraints
        foreach (var uc in rawBranch.Constraints)
        {
            equations.Add(EliminateVariables(uc));
        }

        // Verify no unresolved pattern variables remain
        foreach (var eq in equations)
        {
            foreach (var k in eq.Left.Terms.Keys.Concat(eq.Right.Terms.Keys))
            {
                if (!k.StartsWith("_b", StringComparison.Ordinal))
                {
                    throw new FormatException(
                        $"Could not relate variable '{k}' directly to block lengths. Try writing each block with a simple exponent such as 'a^n b^m where ...'.");
                }
            }
        }

        // Classify equations into unary bounds, 2-var relations, and 3-var additive relations
        var twoVar = new List<TwoVarRelation>();
        var threeAdd = new List<ThreeVarAdditiveRelation>();
        var unsatisfiable = false;

        foreach (var eq in equations)
        {
            var (terms, op, rhs) = eq.Normalize();
            if (terms.Count == 0)
            {
                if (!LinearConstraint.EvaluateOp(0, op, rhs))
                {
                    unsatisfiable = true;
                }
                continue;
            }

            if (terms.Count == 1)
            {
                var kv = terms.First();
                var idx = int.Parse(kv.Key[2..]);
                var c = kv.Value;
                if (c < 0)
                {
                    c = -c;
                    rhs = -rhs;
                    op = LinearConstraint.Flip(op);
                }

                // c * b_idx (op) rhs
                switch (op)
                {
                    case CompareOp.Eq:
                        if (rhs % c != 0) { unsatisfiable = true; break; }
                        var val = rhs / c;
                        bounds[idx] = bounds[idx].WithMin(val).WithMax(val);
                        break;
                    case CompareOp.Geq:
                        bounds[idx] = bounds[idx].WithMin(CeilDiv(rhs, c));
                        break;
                    case CompareOp.Gt:
                        bounds[idx] = bounds[idx].WithMin(CeilDiv(rhs + 1, c));
                        break;
                    case CompareOp.Leq:
                        bounds[idx] = bounds[idx].WithMax(FloorDiv(rhs, c));
                        break;
                    case CompareOp.Lt:
                        bounds[idx] = bounds[idx].WithMax(FloorDiv(rhs - 1, c));
                        break;
                    case CompareOp.Neq:
                        // Unary != handled only if constant; split or reject cleanly
                        if (rhs % c == 0)
                        {
                            var forb = rhs / c;
                            if (forb == bounds[idx].Min) bounds[idx] = bounds[idx].WithMin(forb + 1);
                            else throw new FormatException($"Unary exclusion '!= {forb}' is better written as '< {forb} or > {forb}'.");
                        }
                        break;
                }
                continue;
            }

            if (terms.Count == 2)
            {
                var list = terms.OrderBy(kv => int.Parse(kv.Key[2..])).ToList();
                var idxA = int.Parse(list[0].Key[2..]);
                var idxB = int.Parse(list[1].Key[2..]);
                var cA = list[0].Value;
                var cB = list[1].Value;

                // Normalize so cA > 0
                if (cA < 0)
                {
                    cA = -cA;
                    cB = -cB;
                    rhs = -rhs;
                    op = LinearConstraint.Flip(op);
                }

                if (cB > 0)
                {
                    // Same-sign sum constraint: cA * b_A + cB * b_B (op) rhs
                    // When bounded above (<=, <, =) this is a finite regular relation; otherwise flag clearly
                    if (op is CompareOp.Eq or CompareOp.Leq or CompareOp.Lt && rhs >= 0)
                    {
                        var effRhs = op == CompareOp.Lt ? rhs - 1 : rhs;
                        if (effRhs < 0) { unsatisfiable = true; continue; }
                        bounds[idxA] = bounds[idxA].WithMax(effRhs / cA);
                        bounds[idxB] = bounds[idxB].WithMax(effRhs / cB);
                        if (op == CompareOp.Eq && cA == 1 && cB == 1)
                        {
                            // Keep exact filter as negative coeffB? Better: handled by finite bounded enumeration below
                            twoVar.Add(new TwoVarRelation(idxA, idxB, cA, -cB, op, rhs));
                        }
                        else
                        {
                            twoVar.Add(new TwoVarRelation(idxA, idxB, cA, -cB, op, rhs));
                        }
                        continue;
                    }
                    throw new FormatException(
                        $"Sum constraint '{eq.Format()}' between two blocks is not supported directly; use a comparison such as 'n > m', 'n = 2m', or 'n + m = k'.");
                }

                var q = -cB; // q > 0
                ValidateBounds(cA, q, rhs);

                var stepA = 0;
                var stepB = 0;
                // Preserve forced step when a single variable had coeff > 1 in both blocks (e.g. a^(2n) b^(2n))
                var modA = modConstraints.FirstOrDefault(m => m.BlockIdx == idxA);
                var modB = modConstraints.FirstOrDefault(m => m.BlockIdx == idxB);
                if (modA.Mod > 1 && op == CompareOp.Eq)
                {
                    var g = Gcd(cA, q);
                    var redA = q / g;
                    var redB = cA / g;
                    var mult = modA.Mod / Gcd(modA.Mod, redA);
                    stepA = redA * mult;
                    stepB = redB * mult;
                    // Remove redundant mod constraints now encoded in stepA/stepB
                    modConstraints.RemoveAll(m => m.BlockIdx == idxA || m.BlockIdx == idxB);
                }

                // Simplify gcd when no forced step
                if (stepA == 0)
                {
                    var g = Gcd(cA, q);
                    if (g > 1)
                    {
                        if (op == CompareOp.Eq && rhs % g != 0)
                        {
                            unsatisfiable = true;
                            continue;
                        }
                        if (op == CompareOp.Neq && rhs % g != 0)
                        {
                            continue; // tautology!
                        }
                        if (rhs % g == 0)
                        {
                            cA /= g;
                            q /= g;
                            rhs /= g;
                        }
                    }
                }

                twoVar.Add(new TwoVarRelation(idxA, idxB, cA, q, op, rhs, stepA, stepB));
                continue;
            }

            if (terms.Count == 3)
            {
                // Expect unit additive constraint: (+1, +1, -1) or (-1, -1, +1) -> b_A + b_B (op) b_C + offset
                var entries = terms.Select(kv => (Idx: int.Parse(kv.Key[2..]), Coeff: kv.Value)).ToList();
                var pos = entries.Where(e => e.Coeff == 1).ToList();
                var neg = entries.Where(e => e.Coeff == -1).ToList();

                if (pos.Count == 2 && neg.Count == 1)
                {
                    ValidateOffset(rhs);
                    threeAdd.Add(new ThreeVarAdditiveRelation(pos[0].Idx, pos[1].Idx, neg[0].Idx, op, rhs));
                    continue;
                }
                if (neg.Count == 2 && pos.Count == 1)
                {
                    ValidateOffset(-rhs);
                    threeAdd.Add(new ThreeVarAdditiveRelation(neg[0].Idx, neg[1].Idx, pos[0].Idx, LinearConstraint.Flip(op), -rhs));
                    continue;
                }

                throw new FormatException(
                    $"Three-variable constraint '{eq.Format()}' must use unit additive form such as 'n + m = k', 'n + m > k', 'n = m + k', or 'm = n + k'.");
            }
        }

        for (var i = 0; i < r; i++)
        {
            if (bounds[i].Min > MaxBound || (bounds[i].Max is { } bMax && bMax > MaxBound))
            {
                throw new FormatException($"Variable bound exceeds practical display limit ({MaxBound}).");
            }
            if (bounds[i].IsUnsatisfiable)
            {
                unsatisfiable = true;
            }
        }

        // Deduplicate identical two-var relations
        twoVar = twoVar.Distinct().ToList();
        return new NormalizedBranch(bounds, twoVar, threeAdd, modConstraints, unsatisfiable);
    }

    // =========================================================================
    // MAIN BLOCK COMPILER DISPATCH
    // =========================================================================

    private static LanguageSpecCompilationResult CompileBlockSpec(BlockExponentSpec spec)
    {
        var branches = NormalizeBranches(spec).Where(b => !b.Unsatisfiable).ToList();
        if (branches.Count == 0)
        {
            throw new FormatException($"Specification '{spec.RawSpecification}' is unsatisfiable (defines the empty language).");
        }

        var blockSyms = spec.Blocks.Select(b => b.Symbol).ToArray();
        var alphabet = BuildBlockAlphabet(blockSyms, spec.ExplicitAlphabet);
        var tests = GenerateBlockSuggestedTests(spec, branches, alphabet);

        // Case 0: Every branch has NO cross-variable constraints (or every constrained pair is finite) -> REGULAR!
        if (branches.All(b => IsRegularBranch(b)))
        {
            return CompileRegularBlockLanguage(spec, branches, alphabet, tests);
        }

        // Case 1: 2-block language
        if (spec.Blocks.Count == 2)
        {
            return CompileTwoBlockLanguage(spec, branches, alphabet, tests);
        }

        // Case 2: 3-block language
        if (spec.Blocks.Count == 3)
        {
            return CompileThreeBlockLanguage(spec, branches, alphabet, tests);
        }

        throw new FormatException($"Unsupported parametric specification '{spec.RawSpecification}'.");
    }

    private static bool IsRegularBranch(NormalizedBranch b)
    {
        if (b.AdditiveRelations.Count > 0) return false;
        if (b.TwoVarRelations.Count == 0) return true;
        // If every variable appearing in any 2-var relation has a finite upper bound on at least one side of each relation
        // (or all variables are finite), it's regular!
        if (b.Bounds.All(bd => bd.Max.HasValue)) return true;
        if (b.Bounds.Length == 2 && b.TwoVarRelations.Count == 1 && (b.Bounds[0].Max.HasValue || b.Bounds[1].Max.HasValue))
        {
            return true;
        }
        return false;
    }

    // =========================================================================
    // FAMILY 1: INDEPENDENT / FINITE-BOUNDED BLOCKS (REGULAR)
    // =========================================================================

    private static LanguageSpecCompilationResult CompileRegularBlockLanguage(
        BlockExponentSpec spec,
        IReadOnlyList<NormalizedBranch> branches,
        IReadOnlyList<char> alphabet,
        IReadOnlyList<LanguageTestCase> tests)
    {
        var branchRegexes = new List<string>();
        var syms = spec.Blocks.Select(b => b.Symbol).ToArray();

        foreach (var br in branches)
        {
            if (br.TwoVarRelations.Count == 0)
            {
                var parts = new List<string>();
                for (var i = 0; i < syms.Length; i++)
                {
                    var mod = br.ModConstraints.FirstOrDefault(m => m.BlockIdx == i);
                    parts.Add(BuildBoundedSymbolRegex(syms[i], br.Bounds[i], mod.Mod, mod.Rem));
                }
                branchRegexes.Add(string.Concat(parts));
            }
            else if (syms.Length == 2)
            {
                // Finite enumeration along the bounded side
                var subAlts = new List<string>();
                if (br.Bounds[0].Max is { } xMax)
                {
                    for (var x = br.Bounds[0].Min; x <= xMax; x++)
                    {
                        var yRange = SolveSingleVarRangeForFixed(br, fixedIdx: 0, fixedVal: x, freeIdx: 1);
                        if (yRange is not null)
                        {
                            var rx = x == 0 ? "" : new string(syms[0], x);
                            var ry = BuildBoundedSymbolRegex(syms[1], yRange, 0, 0);
                            var term = rx + ry;
                            subAlts.Add(string.IsNullOrEmpty(term) ? "e" : term);
                        }
                    }
                }
                else if (br.Bounds[1].Max is { } yMax)
                {
                    for (var y = br.Bounds[1].Min; y <= yMax; y++)
                    {
                        var xRange = SolveSingleVarRangeForFixed(br, fixedIdx: 1, fixedVal: y, freeIdx: 0);
                        if (xRange is not null)
                        {
                            var rx = BuildBoundedSymbolRegex(syms[0], xRange, 0, 0);
                            var ry = y == 0 ? "" : new string(syms[1], y);
                            var term = rx + ry;
                            subAlts.Add(string.IsNullOrEmpty(term) ? "e" : term);
                        }
                    }
                }
                if (subAlts.Count > 0)
                {
                    branchRegexes.Add($"({string.Join("|", subAlts)})");
                }
            }
        }

        if (branchRegexes.Count == 0)
        {
            throw new FormatException($"Specification '{spec.RawSpecification}' defines the empty language.");
        }

        var fullRegex = branchRegexes.Count == 1 ? branchRegexes[0] : string.Join("|", branchRegexes);
        var compiled = RegexCompiler.Compile(fullRegex, alphabet);
        var minDfa = compiled.MinimalDfa;
        var rg = RegularGrammarConverter.DfaToRegularGrammar(minDfa);
        var schema = LanguageSpecCompiler.DfaToDecidableTmSchema(minDfa, $"Decider: {spec.RawSpecification}");
        var compiledTm = TmSchemaCompiler.Compile(schema);

        return new LanguageSpecCompilationResult(
            Specification: spec.RawSpecification,
            ResolvedMode: LanguageSpecMode.Parametric,
            LanguageClass: LanguageClass.Regular,
            Title: FormatSpecTitle(spec),
            Description: $"Independent/finite exponent bounds define a Regular language → Minimal DFA ({minDfa.States.Count} states), Right-Linear Grammar, and Decidable TM.",
            MinimalDfa: minDfa,
            Nfa: compiled.Nfa,
            Grammar: rg,
            Pda: null,
            TmSchema: schema,
            CompiledTm: compiledTm,
            SuggestedTests: tests);
    }

    private static VariableBounds? SolveSingleVarRangeForFixed(NormalizedBranch br, int fixedIdx, int fixedVal, int freeIdx)
    {
        var min = br.Bounds[freeIdx].Min;
        var max = br.Bounds[freeIdx].Max;

        foreach (var rel in br.TwoVarRelations)
        {
            // CoeffA * count[A] - CoeffB * count[B] (Op) Offset
            int freeCoeff;
            CompareOp op = rel.Op;
            int rhs;
            if (rel.BlockA == freeIdx)
            {
                freeCoeff = rel.CoeffA;
                rhs = rel.Offset + rel.CoeffB * fixedVal;
            }
            else
            {
                freeCoeff = -rel.CoeffB;
                rhs = rel.Offset - rel.CoeffA * fixedVal;
            }

            if (freeCoeff < 0)
            {
                freeCoeff = -freeCoeff;
                rhs = -rhs;
                op = LinearConstraint.Flip(op);
            }

            switch (op)
            {
                case CompareOp.Eq:
                    if (rhs % freeCoeff != 0) return null;
                    var v = rhs / freeCoeff;
                    min = System.Math.Max(min, v);
                    max = max.HasValue ? System.Math.Min(max.Value, v) : v;
                    break;
                case CompareOp.Geq:
                    min = System.Math.Max(min, CeilDiv(rhs, freeCoeff));
                    break;
                case CompareOp.Gt:
                    min = System.Math.Max(min, CeilDiv(rhs + 1, freeCoeff));
                    break;
                case CompareOp.Leq:
                    var u1 = FloorDiv(rhs, freeCoeff);
                    max = max.HasValue ? System.Math.Min(max.Value, u1) : u1;
                    break;
                case CompareOp.Lt:
                    var u2 = FloorDiv(rhs - 1, freeCoeff);
                    max = max.HasValue ? System.Math.Min(max.Value, u2) : u2;
                    break;
                case CompareOp.Neq:
                    throw new FormatException("Combined finite bound with '!=' is better written using '<' or '>'.");
            }
        }

        if (max.HasValue && min > max.Value) return null;
        return new VariableBounds(min, max);
    }

    private static string BuildBoundedSymbolRegex(char sym, VariableBounds bd, int mod, int rem)
    {
        if (mod > 1)
        {
            var start = bd.Min;
            while (start % mod != rem) start++;
            var prefix = start == 0 ? "" : new string(sym, start);
            var step = new string(sym, mod);
            if (!bd.Max.HasValue)
            {
                return $"{prefix}({step})*";
            }
            var alts = new List<string>();
            for (var k = start; k <= bd.Max.Value; k += mod)
            {
                alts.Add(k == 0 ? "e" : new string(sym, k));
            }
            return alts.Count == 1 ? alts[0] : $"({string.Join("|", alts)})";
        }

        if (!bd.Max.HasValue)
        {
            if (bd.Min == 0) return $"{sym}*";
            if (bd.Min == 1) return $"{sym}+";
            return $"{new string(sym, bd.Min)}{sym}*";
        }

        var list = new List<string>();
        for (var k = bd.Min; k <= bd.Max.Value; k++)
        {
            list.Add(k == 0 ? "e" : new string(sym, k));
        }
        return list.Count == 1 ? list[0] : $"({string.Join("|", list)})";
    }

    // =========================================================================
    // FAMILY 2: 2-BLOCK LINEAR LANGUAGES (CONTEXT-FREE)
    // =========================================================================

    private static LanguageSpecCompilationResult CompileTwoBlockLanguage(
        BlockExponentSpec spec,
        IReadOnlyList<NormalizedBranch> branches,
        IReadOnlyList<char> alphabet,
        IReadOnlyList<LanguageTestCase> tests)
    {
        var s1 = spec.Blocks[0].Symbol;
        var s2 = spec.Blocks[1].Symbol;

        // Single branch with single 2-var relation: exact minimal CFG + PDA + clean outer-peeling TM!
        if (branches.Count == 1 && branches[0].TwoVarRelations.Count == 1 && branches[0].ModConstraints.Count == 0)
        {
            var br = branches[0];
            var rel = br.TwoVarRelations[0];
            var cfg = BuildTwoBlockLinearCfg(s1, s2, rel, br.Bounds[0].Min, br.Bounds[1].Min);
            var pda = CfgToPdaConverter.Convert(cfg);
            var tm = BuildTwoBlockPeelingTmSchema(
                $"Decider: {spec.RawSpecification}",
                s1, s2, rel, br.Bounds[0].Min, br.Bounds[1].Min, alphabet);
            return BuildResult(spec.RawSpecification, LanguageClass.ContextFree,
                FormatSpecTitle(spec),
                $"Context-free 2-block linear language synthesized into exact CFG ({cfg.Rules.Count} rules), PDA, and decidable TM.",
                cfg, pda, tm, tests);
        }

        // Single branch with 2 relations (sandwich cone e.g. m < n < 2m, m <= n <= 2m)
        if (branches.Count == 1 && branches[0].TwoVarRelations.Count == 2 && branches[0].ModConstraints.Count == 0)
        {
            var br = branches[0];
            var cfg = TryBuildTwoBlockSandwichConeCfg(s1, s2, br);
            var pda = cfg is not null ? CfgToPdaConverter.Convert(cfg) : null;
            var tm = BuildMultiPassMarkingTmSchema(
                $"Decider: {spec.RawSpecification}",
                new[] { s1, s2 },
                branches,
                alphabet);
            var desc = cfg is not null
                ? $"Context-free 2-block cone language synthesized into exact CFG ({cfg.Rules.Count} rules), PDA, and non-destructive mark-and-restore TM."
                : "Context-free 2-block conjunction decided by a non-destructive 2-pass mark-and-restore Turing Machine.";
            return BuildResult(spec.RawSpecification, LanguageClass.ContextFree,
                FormatSpecTitle(spec), desc, cfg, pda, tm, tests);
        }

        // Disjunction (OR) of 2-block branches: union of CFGs + mark-and-restore TM!
        if (branches.Count >= 2 && branches.All(b => b.TwoVarRelations.Count <= 1 && b.ModConstraints.Count == 0))
        {
            var branchCfgs = new List<Cfg>();
            foreach (var br in branches)
            {
                if (br.TwoVarRelations.Count == 0)
                {
                    branchCfgs.Add(BuildIndependentTwoBlockCfg(s1, s2, br.Bounds[0], br.Bounds[1]));
                }
                else
                {
                    branchCfgs.Add(BuildTwoBlockLinearCfg(s1, s2, br.TwoVarRelations[0], br.Bounds[0].Min, br.Bounds[1].Min));
                }
            }
            var unionCfg = UnionCfgsSingleLetter(branchCfgs);
            var pda = CfgToPdaConverter.Convert(unionCfg);
            var tm = BuildMultiPassMarkingTmSchema(
                $"Decider: {spec.RawSpecification}",
                new[] { s1, s2 },
                branches,
                alphabet);
            return BuildResult(spec.RawSpecification, LanguageClass.ContextFree,
                FormatSpecTitle(spec),
                $"Context-free union of {branches.Count} linear constraints synthesized into CFG ({unionCfg.Rules.Count} rules), PDA, and mark-and-restore TM.",
                unionCfg, pda, tm, tests);
        }

        // Fallback: general multi-branch marking TM for any valid 2-block spec
        var fallbackTm = BuildMultiPassMarkingTmSchema(
            $"Decider: {spec.RawSpecification}",
            new[] { s1, s2 },
            branches,
            alphabet);
        return BuildResult(spec.RawSpecification, LanguageClass.ContextFree,
            FormatSpecTitle(spec),
            "Context-free parametric language decided by a non-destructive mark-and-restore Turing Machine.",
            null, null, fallbackTm, tests);
    }

    private static Cfg BuildIndependentTwoBlockCfg(char s1, char s2, VariableBounds b0, VariableBounds b1)
    {
        var lines = new List<string> { "S -> A B" };
        lines.Add(BuildSingleBlockCfgRule("A", s1, b0));
        lines.Add(BuildSingleBlockCfgRule("B", s2, b1));
        return Cfg.ParseFromText(string.Join('\n', lines));
    }

    private static string BuildSingleBlockCfgRule(string nt, char sym, VariableBounds bd)
    {
        if (!bd.Max.HasValue)
        {
            var baseStr = bd.Min == 0 ? "e" : new string(sym, bd.Min);
            return $"{nt} -> {sym}{nt} | {baseStr}";
        }
        var alts = new List<string>();
        for (var k = bd.Min; k <= bd.Max.Value; k++)
        {
            alts.Add(k == 0 ? "e" : new string(sym, k));
        }
        return $"{nt} -> {string.Join(" | ", alts)}";
    }

    /// <summary>
    /// Exact, mathematically complete CFG synthesis for ANY 2-block linear relation
    ///   p * x - q * y (op) d,   x >= xMin, y >= yMin
    /// using clean single-letter non-terminals (100% compatible with CfgToPdaConverter &amp; PdaTracer).
    /// </summary>
    private static Cfg BuildTwoBlockLinearCfg(
        char s1, char s2, TwoVarRelation rel, int xMin, int yMin, string? innerMidNt = null)
    {
        var p = rel.CoeffA;
        var q = rel.CoeffB;
        var d = rel.Offset;
        var op = rel.Op;

        var g = Gcd(p, q);
        var stepX = rel.StepA > 0 ? rel.StepA : q / g;
        var stepY = rel.StepB > 0 ? rel.StepB : p / g;

        if (op == CompareOp.Neq)
        {
            var gtRel = rel with { Op = CompareOp.Geq, Offset = d + 1 };
            var ltRel = rel with { Op = CompareOp.Leq, Offset = d - 1 };
            var leftCfg = BuildTwoBlockLinearCfg(s1, s2, gtRel, xMin, yMin, innerMidNt);
            var rightCfg = BuildTwoBlockLinearCfg(s1, s2, ltRel, xMin, yMin, innerMidNt);
            return UnionCfgsSingleLetter(new[] { leftCfg, rightCfg });
        }

        string WrapMid(int xCount, string? mid, int yCount)
        {
            var sb = new StringBuilder();
            if (xCount > 0) sb.Append(new string(s1, xCount));
            if (!string.IsNullOrEmpty(mid)) sb.Append(mid);
            if (yCount > 0) sb.Append(new string(s2, yCount));
            return sb.Length == 0 ? "e" : sb.ToString();
        }

        if (op == CompareOp.Eq)
        {
            // Find minimal (x0, y0) with x0 >= xMin, y0 >= yMin, p*x0 - q*y0 = d
            int x0 = -1, y0 = -1;
            for (var r = 0; r < stepY; r++)
            {
                var yCand = yMin + r;
                var num = q * yCand + d;
                if (num % p == 0)
                {
                    var xCand = num / p;
                    while (xCand < xMin)
                    {
                        xCand += stepX;
                        yCand += stepY;
                    }
                    // Step back as far as possible while keeping x >= xMin and y >= yMin
                    while (xCand - stepX >= xMin && yCand - stepY >= yMin)
                    {
                        xCand -= stepX;
                        yCand -= stepY;
                    }
                    x0 = xCand;
                    y0 = yCand;
                    break;
                }
            }

            if (x0 < 0)
            {
                throw new FormatException("Equality constraint has no non-negative integer solutions.");
            }

            var mid = innerMidNt ?? string.Empty;
            if (x0 == 0 && y0 == 0)
            {
                var baseAlt = string.IsNullOrEmpty(mid) ? "e" : mid;
                return Cfg.ParseFromText($"S -> {new string(s1, stepX)}S{new string(s2, stepY)} | {baseAlt}");
            }

            var baseProd = WrapMid(x0, mid, y0);
            return Cfg.ParseFromText($"S -> {new string(s1, stepX)}S{new string(s2, stepY)} | {baseProd}");
        }

        if (op is CompareOp.Gt or CompareOp.Geq)
        {
            var effD = op == CompareOp.Gt ? d + 1 : d;
            var alts = new List<string>();
            var needA = false;
            var mid = innerMidNt ?? string.Empty;

            // Strip 1: y_r in [yMin, yMin + stepY - 1], any x >= x_r -> s1^{x_r} A [mid] s2^{y_r}
            for (var r = 0; r < stepY; r++)
            {
                var yr = yMin + r;
                var xr = System.Math.Max(xMin, CeilDiv(q * yr + effD, p));
                needA = true;
                var midWithA = string.IsNullOrEmpty(mid) ? "A" : $"A{mid}";
                alts.Add(WrapMid(xr, midWithA, yr));
            }

            // Strip 2 (only if xMin is high enough that floor-on-x hits before floor-on-y):
            // finite set of (xb, yb) with xb in [xMin, xMin + stepX - 1], yb >= yMin + stepY, p*xb - q*yb >= effD
            for (var rx = 0; rx < stepX; rx++)
            {
                var xb = xMin + rx;
                var yHi = FloorDiv(p * xb - effD, q);
                for (var yb = yMin + stepY; yb <= yHi; yb++)
                {
                    alts.Add(WrapMid(xb, mid, yb));
                }
            }

            var rules = new List<string>
            {
                $"S -> {new string(s1, stepX)}S{new string(s2, stepY)} | {string.Join(" | ", alts.Distinct())}"
            };
            if (needA)
            {
                rules.Add($"A -> {s1}A | e");
            }
            return Cfg.ParseFromText(string.Join('\n', rules));
        }

        // Symmetric case: CompareOp.Lt or CompareOp.Leq (p * x - q * y <= effD)
        {
            var effD = op == CompareOp.Lt ? d - 1 : d;
            var alts = new List<string>();
            var needB = false;
            var mid = innerMidNt ?? string.Empty;

            // Strip 1: x_r in [xMin, xMin + stepX - 1], any y >= y_r -> s1^{x_r} [mid] B s2^{y_r}
            for (var r = 0; r < stepX; r++)
            {
                var xr = xMin + r;
                var yr = System.Math.Max(yMin, CeilDiv(p * xr - effD, q));
                needB = true;
                var midWithB = string.IsNullOrEmpty(mid) ? "B" : $"{mid}B";
                alts.Add(WrapMid(xr, midWithB, yr));
            }

            // Strip 2 (floor-on-y hits before floor-on-x)
            for (var ry = 0; ry < stepY; ry++)
            {
                var yb = yMin + ry;
                var xHi = FloorDiv(q * yb + effD, p);
                for (var xb = xMin + stepX; xb <= xHi; xb++)
                {
                    alts.Add(WrapMid(xb, mid, yb));
                }
            }

            var rules = new List<string>
            {
                $"S -> {new string(s1, stepX)}S{new string(s2, stepY)} | {string.Join(" | ", alts.Distinct())}"
            };
            if (needB)
            {
                rules.Add($"B -> {s2}B | e");
            }
            return Cfg.ParseFromText(string.Join('\n', rules));
        }
    }

    /// <summary>
    /// Exact CFG for integer-slope 2-block sandwich cones such as m &lt; n &lt; 2m or m &lt;= n &lt;= 2m
    /// (with lower bounds xMin = 0, yMin = 0 and offset 0). Returns null if the conjunction does not
    /// match a clean integer-slope cone (caller still synthesizes exact TM).
    /// </summary>
    private static Cfg? TryBuildTwoBlockSandwichConeCfg(char s1, char s2, NormalizedBranch br)
    {
        if (br.Bounds[0].Min != 0 || br.Bounds[1].Min != 0 || br.Bounds[0].Max.HasValue || br.Bounds[1].Max.HasValue)
            return null;

        var r1 = br.TwoVarRelations[0];
        var r2 = br.TwoVarRelations[1];
        if (r1.Offset != 0 || r2.Offset != 0) return null;

        // Check if one is lower bound on x/y (x >= k1*y or x > k1*y) and other is upper bound (x <= k2*y or x < k2*y)
        TwoVarRelation? lower = null;
        TwoVarRelation? upper = null;
        foreach (var rel in new[] { r1, r2 })
        {
            if (rel.CoeffA == 1 && rel.Op is CompareOp.Gt or CompareOp.Geq) lower = rel;
            else if (rel.CoeffA == 1 && rel.Op is CompareOp.Lt or CompareOp.Leq) upper = rel;
        }
        if (lower is null || upper is null) return null;

        var k1 = lower.CoeffB;
        var k2 = upper.CoeffB;
        if (k1 >= k2) return null;

        var strictLow = lower.Op == CompareOp.Gt;
        var strictHigh = upper.Op == CompareOp.Lt;

        var stepAlts = new List<string>();
        for (var k = k1; k <= k2; k++)
        {
            stepAlts.Add($"{new string(s1, k)}S{s2}");
        }

        if (!strictLow && !strictHigh)
        {
            // k1*y <= x <= k2*y: S -> s1^{k1} S s2 | ... | s1^{k2} S s2 | e
            stepAlts.Add("e");
            return Cfg.ParseFromText($"S -> {string.Join(" | ", stepAlts)}");
        }

        if (strictLow && strictHigh)
        {
            // k1*y < x < k2*y
            var bases = new List<string>();
            if (k2 == k1 + 1)
            {
                // Need at least one (k1,1) and at least one (k2,1) -> base is (k1 + k2, 2)
                bases.Add($"{new string(s1, k1 + k2)}{new string(s2, 2)}");
            }
            else
            {
                // Any interior integer slope k in (k1, k2) is a valid single-step base (k, 1)
                for (var k = k1 + 1; k <= k2 - 1; k++)
                {
                    bases.Add($"{new string(s1, k)}{s2}");
                }
            }
            stepAlts.AddRange(bases);
            return Cfg.ParseFromText($"S -> {string.Join(" | ", stepAlts)}");
        }

        if (strictLow && !strictHigh)
        {
            // k1*y < x <= k2*y: need at least one step > k1
            for (var k = k1 + 1; k <= k2; k++)
            {
                stepAlts.Add($"{new string(s1, k)}{s2}");
            }
            return Cfg.ParseFromText($"S -> {string.Join(" | ", stepAlts)}");
        }

        // !strictLow && strictHigh: k1*y <= x < k2*y: need at least one step < k2
        for (var k = k1; k <= k2 - 1; k++)
        {
            stepAlts.Add($"{new string(s1, k)}{s2}");
        }
        return Cfg.ParseFromText($"S -> {string.Join(" | ", stepAlts)}");
    }

    // =========================================================================
    // FAMILY 2 TM: CLEAN OUTER-PEELING DECIDER FOR ANY 2-BLOCK LINEAR RELATION
    // =========================================================================

    private static TmSchema BuildTwoBlockPeelingTmSchema(
        string name,
        char s1,
        char s2,
        TwoVarRelation rel,
        int xMin,
        int yMin,
        IReadOnlyList<char> alphabet)
    {
        var p = rel.CoeffA;
        var q = rel.CoeffB;
        var d = rel.Offset;
        var op = rel.Op;
        var g = Gcd(p, q);
        var stepX = rel.StepA > 0 ? rel.StepA : q / g;
        var stepY = rel.StepB > 0 ? rel.StepB : p / g;

        var alpha = new SortedSet<char>(alphabet) { s1, s2, 'y', 'n' };
        var b = new TmSchemaBuilder(name)
            .Alphabet(alpha)
            .Node("acc_empty", "Y R")
            .Node("rej_empty", "N R")
            .Node("acc_zero", "R Y R")
            .Node("rej_zero", "R N R")
            .Node("acc_end", "E Y R")
            .Node("rej_end", "E N R")
            .Node("acc_mid", "R_# E Y R")
            .Node("rej_mid", "R_# E N R");

        // --- Phase 1: Validate s1* s2* order AND lower bounds x >= xMin, y >= yMin ---
        // Start scanning at first cell
        var phase2Entry = "px_0";
        if (xMin == 0 && yMin == 0)
        {
            b.StartNode("v_start", "L_# R")
             .Node("v_a", "R")
             .Node("v_b", "R");

            b.Edge("v_start", "v_a", s1.ToString())
             .Edge("v_start", "v_b", s2.ToString())
             .Edge("v_start", phase2Entry, "#")
             .Edge("v_start", "rej_mid", "other");

            b.Edge("v_a", "v_a", s1.ToString())
             .Edge("v_a", "v_b", s2.ToString())
             .Edge("v_a", phase2Entry, "#")
             .Edge("v_a", "rej_mid", "other");

            b.Edge("v_b", "v_b", s2.ToString())
             .Edge("v_b", phase2Entry, "#")
             .Edge("v_b", "rej_mid", "other");
        }
        else
        {
            b.StartNode("v_init", "L_# R");
            // Require xMin copies of s1
            var cur = "v_init";
            for (var k = 0; k < xMin; k++)
            {
                var next = k + 1 < xMin ? $"v_xmin_{k + 1}" : "v_a_more";
                b.Node(next, "R");
                b.Edge(cur, next, s1.ToString());
                b.Edge(cur, "rej_end", "#");
                b.Edge(cur, "rej_mid", "other");
                cur = next;
            }
            if (xMin == 0)
            {
                b.Node("v_a_more", "R");
                b.Edge("v_init", "v_a_more", s1.ToString());
                // If yMin > 0, seeing s2 immediately satisfies 1st required y
                var afterFirstB = yMin > 1 ? "v_ymin_1" : "v_b_more";
                if (yMin == 0)
                {
                    b.Node("v_b_more", "R");
                    b.Edge("v_init", "v_b_more", s2.ToString());
                    b.Edge("v_init", phase2Entry, "#");
                }
                else
                {
                    b.Node(afterFirstB, "R");
                    b.Edge("v_init", afterFirstB, s2.ToString());
                    b.Edge("v_init", "rej_end", "#");
                }
                b.Edge("v_init", "rej_mid", "other");
                cur = "v_a_more";
            }

            // Skip remaining s1's
            b.Edge("v_a_more", "v_a_more", s1.ToString());
            if (yMin == 0)
            {
                if (!HasNode(b, "v_b_more")) b.Node("v_b_more", "R");
                b.Edge("v_a_more", "v_b_more", s2.ToString());
                b.Edge("v_a_more", phase2Entry, "#");
                b.Edge("v_a_more", "rej_mid", "other");
            }
            else
            {
                var firstBTarget = yMin > 1 ? "v_ymin_1" : "v_b_more";
                if (!HasNode(b, firstBTarget)) b.Node(firstBTarget, "R");
                b.Edge("v_a_more", firstBTarget, s2.ToString());
                b.Edge("v_a_more", "rej_end", "#");
                b.Edge("v_a_more", "rej_mid", "other");

                // Require remaining yMin - 1 copies of s2
                var bCur = firstBTarget;
                for (var k = 1; k < yMin; k++)
                {
                    var next = k + 1 < yMin ? $"v_ymin_{k + 1}" : "v_b_more";
                    if (!HasNode(b, next)) b.Node(next, "R");
                    b.Edge(bCur, next, s2.ToString());
                    b.Edge(bCur, "rej_end", "#");
                    b.Edge(bCur, "rej_mid", "other");
                    bCur = next;
                }
            }

            b.Edge("v_b_more", "v_b_more", s2.ToString());
            b.Edge("v_b_more", phase2Entry, "#");
            b.Edge("v_b_more", "rej_mid", "other");
        }

        // --- Phase 2: Lockstep peel stepX of s1 from left and stepY of s2 from right ---
        bool EvalRemainder(int remX, int remY) =>
            LinearConstraint.EvaluateOp(p * remX - q * remY, op, d)
            && (rel.StepA <= 0 || (((remX - xMin) % stepX + stepX) % stepX == 0 && ((remY - yMin) % stepY + stepY) % stepY == 0));

        for (var cx = 0; cx < stepX; cx++)
        {
            var pxNode = $"px_{cx}";
            var eraseNode = $"ex_{cx}";
            b.Node(pxNode, "L_# R")
             .Node(eraseNode, "# R_# S_L");

            // Saw s1 -> erase and shift left
            b.Edge(pxNode, eraseNode, s1.ToString());
            var nextAfterErase = cx + 1 < stepX ? $"px_{cx + 1}" : "py_0";
            b.Edge(eraseNode, nextAfterErase, "*");

            // Saw '#' -> exact remaining counts (cx, 0)
            var emptyOk = EvalRemainder(cx, 0);
            b.Edge(pxNode, emptyOk ? "acc_empty" : "rej_empty", "#");

            // Saw s2 -> zero s1 left, remaining tape is pure s2^{remY} (remY >= 1), walking RIGHT
            // Evaluate p*cx - q*remY (op) d for remY = 1, 2, ...
            var cxCaptured = cx;
            var yTailTarget = BuildRightCountTail(
                b, $"ty_{cx}", s2,
                remY => EvalRemainder(cxCaptured, remY),
                maxRelevant: ComputeRelevantThreshold(p * cx, q, d) + stepY + 2);
            b.Edge(pxNode, yTailTarget, s2.ToString());
        }

        for (var cy = 0; cy < stepY; cy++)
        {
            var pyNode = $"py_{cy}";
            var eraseNode = $"ey_{cy}";
            b.Node(pyNode, "L")
             .Node(eraseNode, "#");

            // Saw s2 -> erase from right end
            b.Edge(pyNode, eraseNode, s2.ToString());
            var nextAfterErase = cy + 1 < stepY ? $"py_{cy + 1}" : "px_0";
            b.Edge(eraseNode, nextAfterErase, "*");

            // Saw '#' -> moved left onto cell 0 of empty tape; exact counts (stepX, cy)
            var zeroOk = EvalRemainder(stepX, cy);
            b.Edge(pyNode, zeroOk ? "acc_zero" : "rej_zero", "#");

            // Saw s1 -> zero s2 left, remaining tape is pure s1^{remX} (remX >= 1), head at rightmost s1, walking LEFT
            // Total remaining x is stepX + remX, y is cy
            var cyCaptured = cy;
            var xTailTarget = BuildLeftCountTail(
                b, $"tx_{cy}", s1,
                remX => EvalRemainder(stepX + remX, cyCaptured),
                maxRelevant: ComputeRelevantThreshold(q * cy + d, p, 0) + stepX + 2);
            b.Edge(pyNode, xTailTarget, s1.ToString());
        }

        return b.Build();
    }

    private static int ComputeRelevantThreshold(int num, int den, int offset) =>
        System.Math.Clamp((System.Math.Abs(num) + System.Math.Abs(offset)) / System.Math.Max(1, den) + 2, 2, 14);

    /// <summary>
    /// Builds a minimal right-scanning counter over a pure run of `sym` starting at count = 1
    /// (the caller has already matched the 1st `sym`). Returns the target node ID to route to on that 1st `sym`.
    /// </summary>
    private static string BuildRightCountTail(
        TmSchemaBuilder b, string prefix, char sym, Func<int, bool> eval, int maxRelevant)
    {
        // Find smallest k >= 1 from which eval(n) is constant for all n >= k
        var tailVal = eval(maxRelevant + 5);
        var stableFrom = maxRelevant + 1;
        while (stableFrom > 1 && eval(stableFrom - 1) == tailVal)
        {
            stableFrom--;
        }

        // If constant for all rem >= 1, decide immediately!
        if (stableFrom == 1)
        {
            return tailVal ? "acc_mid" : "rej_mid";
        }

        // Otherwise step R to count rem = 1 .. stableFrom - 1
        for (var k = 1; k < stableFrom; k++)
        {
            var nodeId = $"{prefix}_{k}";
            b.Node(nodeId, "R");
            var endAccept = eval(k);
            b.Edge(nodeId, endAccept ? "acc_end" : "rej_end", "#");

            if (k + 1 < stableFrom)
            {
                b.Edge(nodeId, $"{prefix}_{k + 1}", sym.ToString());
            }
            else
            {
                // Saw one more sym => count >= stableFrom => guaranteed tailVal!
                b.Edge(nodeId, tailVal ? "acc_mid" : "rej_mid", sym.ToString());
            }
        }
        return $"{prefix}_1";
    }

    /// <summary>
    /// Builds a minimal left-scanning counter over a pure run of `sym` starting at count = 1
    /// (head is on the rightmost `sym`, moving LEFT). When hitting left `#`, `R_# E Y/N R` (= acc_mid/rej_mid) erases everything cleanly!
    /// </summary>
    private static string BuildLeftCountTail(
        TmSchemaBuilder b, string prefix, char sym, Func<int, bool> eval, int maxRelevant)
    {
        var tailVal = eval(maxRelevant + 5);
        var stableFrom = maxRelevant + 1;
        while (stableFrom > 1 && eval(stableFrom - 1) == tailVal)
        {
            stableFrom--;
        }

        if (stableFrom == 1)
        {
            return tailVal ? "acc_mid" : "rej_mid";
        }

        for (var k = 1; k < stableFrom; k++)
        {
            var nodeId = $"{prefix}_{k}";
            b.Node(nodeId, "L");
            var endAccept = eval(k);
            // At left '#', R_# sweeps right across the remaining s1's to right '#', then E erases them!
            b.Edge(nodeId, endAccept ? "acc_mid" : "rej_mid", "#");

            if (k + 1 < stableFrom)
            {
                b.Edge(nodeId, $"{prefix}_{k + 1}", sym.ToString());
            }
            else
            {
                b.Edge(nodeId, tailVal ? "acc_mid" : "rej_mid", sym.ToString());
            }
        }
        return $"{prefix}_1";
    }

    // =========================================================================
    // FAMILY 3: 3-BLOCK LINEAR LANGUAGES (CFL vs CONTEXT-SENSITIVE)
    // =========================================================================

    private static LanguageSpecCompilationResult CompileThreeBlockLanguage(
        BlockExponentSpec spec,
        IReadOnlyList<NormalizedBranch> branches,
        IReadOnlyList<char> alphabet,
        IReadOnlyList<LanguageTestCase> tests)
    {
        var s1 = spec.Blocks[0].Symbol;
        var s2 = spec.Blocks[1].Symbol;
        var s3 = spec.Blocks[2].Symbol;

        // Check whether every branch is Context-Free (at most 1 two-var relation OR 1 unit additive relation, no coupled 3-block AND)
        var allBranchesCfl = branches.All(IsThreeBlockCflBranch);

        if (allBranchesCfl)
        {
            var cfgs = branches.Select(br => BuildThreeBlockCflCfg(s1, s2, s3, br)).ToList();
            var cfg = cfgs.Count == 1 ? cfgs[0] : UnionCfgsSingleLetter(cfgs);
            var pda = CfgToPdaConverter.Convert(cfg);
            var tm = BuildMultiPassMarkingTmSchema(
                $"Decider: {spec.RawSpecification}",
                new[] { s1, s2, s3 },
                branches,
                alphabet);

            return BuildResult(spec.RawSpecification, LanguageClass.ContextFree,
                FormatSpecTitle(spec),
                $"Context-free 3-block language synthesized into exact CFG ({cfg.Rules.Count} rules), PDA, and decidable TM.",
                cfg, pda, tm, tests);
        }

        // Coupled conjunction across all 3 blocks (e.g. a^n b^m c^k where n > m > k, i = j and j = k) -> ContextSensitive!
        var cslTm = BuildMultiPassMarkingTmSchema(
            $"Decider: {spec.RawSpecification}",
            new[] { s1, s2, s3 },
            branches,
            alphabet);

        return BuildResult(spec.RawSpecification, LanguageClass.ContextSensitive,
            FormatSpecTitle(spec),
            "Context-sensitive language (coupled constraints across all 3 blocks — non-CFL) decided by a multi-pass mark-and-restore Turing Machine.",
            null, null, cslTm, tests);
    }

    private static bool IsThreeBlockCflBranch(NormalizedBranch br)
    {
        if (br.ModConstraints.Count > 0) return false;
        // 1. No relational constraints (already handled as regular, but safe)
        if (br.TwoVarRelations.Count == 0 && br.AdditiveRelations.Count == 0) return true;
        // 2. Single 2-variable constraint (3rd block free)
        if (br.TwoVarRelations.Count == 1 && br.AdditiveRelations.Count == 0) return true;
        // 3. Single 3-variable unit additive constraint with zero offset (or supported offset)
        if (br.TwoVarRelations.Count == 0 && br.AdditiveRelations.Count == 1 && br.AdditiveRelations[0].Offset == 0
            && br.Bounds.All(bd => !bd.Max.HasValue))
        {
            return true;
        }
        return false;
    }

    private static Cfg BuildThreeBlockCflCfg(char s1, char s2, char s3, NormalizedBranch br)
    {
        var syms = new[] { s1, s2, s3 };

        // Case A: Single 2-variable relation, 3rd block free
        if (br.TwoVarRelations.Count == 1 && br.AdditiveRelations.Count == 0)
        {
            var rel = br.TwoVarRelations[0];
            if (rel.BlockA == 0 && rel.BlockB == 1)
            {
                // Constrained (0, 1), free block 2 on right
                var sub = BuildTwoBlockLinearCfg(s1, s2, rel, br.Bounds[0].Min, br.Bounds[1].Min);
                return WrapWithSideBlock(sub, s3, br.Bounds[2], onLeft: false);
            }
            if (rel.BlockA == 1 && rel.BlockB == 2)
            {
                // Free block 0 on left, constrained (1, 2)
                var sub = BuildTwoBlockLinearCfg(s2, s3, rel, br.Bounds[1].Min, br.Bounds[2].Min);
                return WrapWithSideBlock(sub, s1, br.Bounds[0], onLeft: true);
            }
            if (rel.BlockA == 0 && rel.BlockB == 2)
            {
                // Constrained outer blocks (0, 2), free middle block 1!
                var sub = BuildTwoBlockLinearCfg(s1, s3, rel, br.Bounds[0].Min, br.Bounds[2].Min, innerMidNt: "M");
                var midRule = BuildSingleBlockCfgRule("M", s2, br.Bounds[1]);
                return Cfg.ParseFromText(sub.ToGrammarText() + "\n" + midRule);
            }
        }

        // Case B: Single 3-variable additive constraint with Offset == 0
        if (br.AdditiveRelations.Count == 1)
        {
            var add = br.AdditiveRelations[0];
            return BuildThreeBlockAdditiveCfg(s1, s2, s3, add, br.Bounds[0].Min, br.Bounds[1].Min, br.Bounds[2].Min);
        }

        // Independent 3-block fallback
        var lines = new[]
        {
            "S -> A B C",
            BuildSingleBlockCfgRule("A", s1, br.Bounds[0]),
            BuildSingleBlockCfgRule("B", s2, br.Bounds[1]),
            BuildSingleBlockCfgRule("C", s3, br.Bounds[2])
        };
        return Cfg.ParseFromText(string.Join('\n', lines));
    }

    private static Cfg WrapWithSideBlock(Cfg inner, char sym, VariableBounds bd, bool onLeft)
    {
        var used = new HashSet<string>(inner.NonTerminals, StringComparer.Ordinal);
        var newStart = PickSingleNt("S", used);
        used.Add(newStart);
        var sideNt = PickSingleNt(onLeft ? "F" : "C", used);
        used.Add(sideNt);

        // If inner already uses 'S', rename inner's start to a fresh single letter
        var innerMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nt in inner.NonTerminals)
        {
            innerMap[nt] = nt == "S" ? PickSingleNt("U", used) : nt;
            used.Add(innerMap[nt]);
        }

        var lines = new List<string>
        {
            onLeft ? $"S -> {sideNt}{innerMap[inner.StartSymbol]}" : $"S -> {innerMap[inner.StartSymbol]}{sideNt}",
            BuildSingleBlockCfgRule(sideNt, sym, bd)
        };
        lines.AddRange(FormatRenamedRules(inner, innerMap));
        return Cfg.ParseFromText(string.Join('\n', lines));
    }

    /// <summary>
    /// Exact CFG synthesis for 3-block additive languages:
    ///   (0, 1) vs 2: i + j op k
    ///   (1, 2) vs 0: j + k op i  (equivalently i op j + k)
    ///   (0, 2) vs 1: i + k op j  (equivalently j op i + k)
    /// Supports =, !=, >, >=, <, <= and lower bounds iMin, jMin, kMin >= 0.
    /// </summary>
    private static Cfg BuildThreeBlockAdditiveCfg(
        char s1, char s2, char s3,
        ThreeVarAdditiveRelation add,
        int iMin, int jMin, int kMin)
    {
        if (add.Op == CompareOp.Neq)
        {
            var gt = BuildThreeBlockAdditiveCfg(s1, s2, s3, add with { Op = CompareOp.Gt }, iMin, jMin, kMin);
            var lt = BuildThreeBlockAdditiveCfg(s1, s2, s3, add with { Op = CompareOp.Lt }, iMin, jMin, kMin);
            return UnionCfgsSingleLetter(new[] { gt, lt });
        }

        // Route by which block is the Target (sum of the other two blocks vs Target)
        if (add.Target == 2)
        {
            // i + j (op) k
            return BuildSumIJOpKCfg(s1, s2, s3, add.Op, iMin, jMin, kMin);
        }
        if (add.Target == 0)
        {
            // j + k (op) i  <=>  i (flip op) j + k
            // Exact string reversal of (k + j op i) with symbols reversed (s3, s2, s1)!
            var revCfg = BuildSumIJOpKCfg(s3, s2, s1, add.Op, kMin, jMin, iMin);
            return ReverseCfg(revCfg);
        }
        // add.Target == 1: i + k (op) j  <=>  j (flip op) i + k
        return BuildMiddleVsOuterSumCfg(s1, s2, s3, LinearConstraint.Flip(add.Op), iMin, jMin, kMin);
    }

    private static Cfg BuildSumIJOpKCfg(char s1, char s2, char s3, CompareOp op, int iMin, int jMin, int kMin)
    {
        // First strip mandatory iMin, jMin; remaining counts (i', j', k') satisfy:
        //   i' + j' (op) k' + (kMin - iMin - jMin) with i', j', k' >= 0?
        // Easier & 100% exact: strip min(iMin, kMin) of (s1, s3) and min(jMin, rem_kMin) of (s2, s3),
        // or enforce kMin directly when iMin == jMin == kMin == 0 (or via prefix/suffix wrapping).
        var basePairK = System.Math.Max(0, kMin - iMin - jMin);
        var p1 = new string(s1, iMin);
        var p2 = new string(s2, jMin);
        var p3 = new string(s3, iMin + jMin);

        if (op == CompareOp.Eq)
        {
            // i + j = k, with i >= iMin, j >= jMin, k >= kMin
            // Express as s1^{iMin} (extra pairs totaling >= basePairK of (s1,s3) before (s2,s3)) s3^{iMin}
            if (basePairK == 0)
            {
                // S -> s1^{iMin} U s3^{iMin}; U -> s1 U s3 | M; M -> s2^{jMin} V s3^{jMin}; V -> s2 V s3 | e
                if (iMin == 0 && jMin == 0)
                {
                    return Cfg.ParseFromText($"S -> {s1}S{s3} | M\nM -> {s2}M{s3} | e");
                }
                var sRhs = iMin == 0 ? "U" : $"{p1}U{new string(s3, iMin)}";
                var mRhs = jMin == 0 ? "V" : $"{p2}V{new string(s3, jMin)}";
                return Cfg.ParseFromText($"S -> {sRhs}\nU -> {s1}U{s3} | {mRhs}\nV -> {s2}V{s3} | e");
            }

            // Need at least basePairK additional (a,c) or (b,c) pairs: split across t = 0..basePairK of (a,c) and (basePairK-t) of (b,c)
            var alts = new List<string>();
            for (var t = 0; t <= basePairK; t++)
            {
                var rA = iMin + t;
                var rB = jMin + (basePairK - t);
                alts.Add($"{new string(s1, rA)}U{new string(s3, rA)}");
                // Better: keep per-t distinct middle count using exact recursive chain:
                // Actually (i' >= t, j' >= basePairK - t) with i' + j' = k' is simply:
                // s1^{rA} U_{t} s3^{rA} where U_t -> s1 U_t s3 | s2^{rB} V s3^{rB}
            }
            var rules = new List<string>();
            var topAlts = new List<string>();
            var used = new HashSet<string> { "S", "V" };
            for (var t = 0; t <= basePairK; t++)
            {
                var nt = PickSingleNt("U", used);
                used.Add(nt);
                var rA = iMin + t;
                var rB = jMin + (basePairK - t);
                topAlts.Add($"{(rA == 0 ? "" : new string(s1, rA))}{nt}{(rA == 0 ? "" : new string(s3, rA))}");
                var basePart = rB == 0 ? "V" : $"{new string(s2, rB)}V{new string(s3, rB)}";
                rules.Add($"{nt} -> {s1}{nt}{s3} | {basePart}");
            }
            rules.Insert(0, $"S -> {string.Join(" | ", topAlts)}");
            rules.Add($"V -> {s2}V{s3} | e");
            return Cfg.ParseFromText(string.Join('\n', rules));
        }

        if (op is CompareOp.Lt or CompareOp.Leq)
        {
            // i + j < k (extra >= 1 of s3) or i + j <= k (extra >= 0 of s3)
            var extraC = op == CompareOp.Lt ? 1 : 0;
            var needC = System.Math.Max(extraC, kMin - iMin - jMin);
            // Every valid string is: (i + j = k0 with i >= iMin, j >= jMin) followed by s3^{extra} where k0 + extra >= kMin and extra >= extraC
            // Split into Case 1: k0 >= kMin - extraC (so extra >= extraC); or Case 2: extra >= needC with any i >= iMin, j >= jMin!
            // Actually even simpler: let C -> s3 C | e.
            // If kMin <= iMin + jMin + extraC: ALWAYS k = i + j + extra >= iMin + jMin + extraC >= kMin!
            // So S -> E s3^{extraC} C!
            var eqZeroK = BuildSumIJOpKCfg(s1, s2, s3, CompareOp.Eq, iMin, jMin, 0);
            if (kMin <= iMin + jMin + extraC)
            {
                var cPart = extraC == 0 ? "C" : $"{new string(s3, extraC)}C";
                var map = RenameNonTerminalsSingle(eqZeroK, new HashSet<string> { "S", "C" });
                var lines = new List<string>
                {
                    $"S -> {map[eqZeroK.StartSymbol]}{cPart}",
                    $"C -> {s3}C | e"
                };
                lines.AddRange(FormatRenamedRules(eqZeroK, map));
                return Cfg.ParseFromText(string.Join('\n', lines));
            }

            // General kMin > iMin + jMin + extraC: union over dExtra in [extraC, needC - 1] (needing k0 >= kMin - dExtra) + dExtra >= needC (needing k0 >= 0)
            var parts = new List<Cfg>();
            for (var dEx = extraC; dEx < needC; dEx++)
            {
                var eqK = BuildSumIJOpKCfg(s1, s2, s3, CompareOp.Eq, iMin, jMin, kMin - dEx);
                parts.Add(WrapWithSideBlock(eqK, s3, new VariableBounds(dEx, dEx), onLeft: false));
            }
            parts.Add(WrapWithSideBlock(eqZeroK, s3, new VariableBounds(needC, null), onLeft: false));
            return UnionCfgsSingleLetter(parts);
        }

        // CompareOp.Gt or CompareOp.Geq: i + j > k  (or i + j >= k)
        // Exact decomposition:
        //   Branch 1: j > k (strictly more b's than c's) AND i >= iMin, j >= jMin, k >= kMin
        //   Branch 2: j <= k AND i >= (k - j) + extraA, with i >= iMin, j >= jMin, k >= kMin
        var extraA = op == CompareOp.Gt ? 1 : 0;

        // Branch 1: A(i >= iMin) followed by L_{bc}(j > k, j >= jMin, k >= kMin)
        var bcGt = BuildTwoBlockLinearCfg(s2, s3, new TwoVarRelation(0, 1, 1, 1, CompareOp.Gt, 0), jMin, kMin);
        var branch1 = WrapWithSideBlock(bcGt, s1, new VariableBounds(iMin, null), onLeft: true);

        // Branch 2: j <= k, write k = j + p (p >= 0). Need i >= max(iMin, p + extraA), j >= jMin, p + j >= kMin!
        // Notice: when iMin == 0 && jMin == 0 && kMin == 0:
        //   P -> a P c | a A M   (for >)  or  P -> a P c | A M  (for >=),  A -> a A | e, M -> b M c | e!
        var branch2List = new List<Cfg>();
        var minP = System.Math.Max(0, kMin - jMin);
        if (minP == 0)
        {
            // For any p >= 0, j >= jMin already satisfies p + j >= kMin.
            // Need i >= max(iMin, p + extraA).
            // For p in [0, iMin - extraA - 1]: i >= iMin => s1^{iMin} A M s3^p!
            // For p >= max(0, iMin - extraA): i = p + extraA + r => s1^{p0 + extraA} U s3^{p0} where U -> s1 U s3 | A M!
            var pSplit = System.Math.Max(0, iMin - extraA);
            var mDef = jMin == 0 ? $"M -> {s2}M{s3} | e" : $"M -> {s2}M{s3} | {new string(s2, jMin)}{new string(s3, jMin)}";
            var alts = new List<string>();
            for (var pv = 0; pv < pSplit; pv++)
            {
                alts.Add($"{new string(s1, iMin)}AM{(pv == 0 ? "" : new string(s3, pv))}");
            }
            var leadA = pSplit + extraA;
            var leadAPref = leadA == 0 ? "" : new string(s1, leadA);
            var trailCSuf = pSplit == 0 ? "" : new string(s3, pSplit);
            alts.Add($"{leadAPref}U{trailCSuf}");

            branch2List.Add(Cfg.ParseFromText(
                $"S -> {string.Join(" | ", alts)}\nU -> {s1}U{s3} | AM\nA -> {s1}A | e\n{mDef}"));
        }
        else
        {
            // Split j in [jMin, kMin - 1] (requiring p >= kMin - j) + j >= kMin (requiring p >= 0)
            for (var jFixed = jMin; jFixed < kMin; jFixed++)
            {
                var pReq = kMin - jFixed;
                // Fixed j = jFixed, p >= pReq, i >= max(iMin, p + extraA)
                var subRel = new TwoVarRelation(0, 1, 1, 1, CompareOp.Geq, extraA);
                var sub = BuildTwoBlockLinearCfg(s1, s3, subRel, iMin, pReq, innerMidNt: "M");
                var mFixed = $"{new string(s2, jFixed)}{new string(s3, jFixed)}";
                branch2List.Add(Cfg.ParseFromText(sub.ToGrammarText() + $"\nM -> {(string.IsNullOrEmpty(mFixed) ? "e" : mFixed)}"));
            }
            // j >= kMin: now p >= 0 freely!
            var jGeK = BuildSumIJOpKCfg(s1, s2, s3, op, iMin, kMin, kMin);
            branch2List.Add(jGeK);
        }

        branch2List.Insert(0, branch1);
        return UnionCfgsSingleLetter(branch2List);
    }

    private static Cfg BuildMiddleVsOuterSumCfg(char s1, char s2, char s3, CompareOp op, int iMin, int jMin, int kMin)
    {
        // j (op) i + k, with i >= iMin, j >= jMin, k >= kMin
        if (op == CompareOp.Eq)
        {
            // j = i + k -> split j = i + k into L(s1^i s2^i) R(s2^k s3^k) with i + k >= jMin
            var deficit = System.Math.Max(0, jMin - iMin - kMin);
            var alts = new List<Cfg>();
            for (var t = 0; t <= deficit; t++)
            {
                var rI = iMin + t;
                var rK = kMin + (deficit - t);
                var lBase = rI == 0 ? "e" : $"{new string(s1, rI)}{new string(s2, rI)}";
                var rBase = rK == 0 ? "e" : $"{new string(s2, rK)}{new string(s3, rK)}";
                alts.Add(Cfg.ParseFromText($"S -> LR\nL -> {s1}L{s2} | {lBase}\nR -> {s2}R{s3} | {rBase}"));
            }
            return alts.Count == 1 ? alts[0] : UnionCfgsSingleLetter(alts);
        }

        if (op is CompareOp.Gt or CompareOp.Geq)
        {
            // j >= i + k + extraB
            var extraB = op == CompareOp.Gt ? 1 : 0;
            var needB = System.Math.Max(extraB, jMin - iMin - kMin);
            var parts = new List<Cfg>();
            for (var dEx = extraB; dEx < needB; dEx++)
            {
                var deficit = (jMin - dEx) - iMin - kMin;
                for (var t = 0; t <= deficit; t++)
                {
                    var rI = iMin + t;
                    var rK = kMin + (deficit - t);
                    var lBase = rI == 0 ? "e" : $"{new string(s1, rI)}{new string(s2, rI)}";
                    var rBase = rK == 0 ? "e" : $"{new string(s2, rK)}{new string(s3, rK)}";
                    var midB = dEx == 0 ? "" : new string(s2, dEx);
                    parts.Add(Cfg.ParseFromText($"S -> L{midB}R\nL -> {s1}L{s2} | {lBase}\nR -> {s2}R{s3} | {rBase}"));
                }
            }
            var lB = iMin == 0 ? "e" : $"{new string(s1, iMin)}{new string(s2, iMin)}";
            var rB = kMin == 0 ? "e" : $"{new string(s2, kMin)}{new string(s3, kMin)}";
            var bPref = needB == 0 ? "" : new string(s2, needB);
            parts.Add(Cfg.ParseFromText($"S -> L{bPref}BR\nL -> {s1}L{s2} | {lB}\nR -> {s2}R{s3} | {rB}\nB -> {s2}B | e"));
            return parts.Count == 1 ? parts[0] : UnionCfgsSingleLetter(parts);
        }

        // j < i + k  or  j <= i + k:
        // Exact partition:
        //   Branch 1: j < i (strictly fewer b's than a's), any k >= kMin
        //   Branch 2: j >= i, write j = i + r (r >= 0), and r < k (for <) or r <= k (for <=), with i + r >= jMin!
        var br1Sub = BuildTwoBlockLinearCfg(s1, s2, new TwoVarRelation(0, 1, 1, 1, CompareOp.Gt, 0), iMin, jMin);
        var br1 = WrapWithSideBlock(br1Sub, s3, new VariableBounds(kMin, null), onLeft: false);

        var rkRel = new TwoVarRelation(0, 1, 1, 1, op, 0); // r < k or r <= k
        var parts2 = new List<Cfg> { br1 };

        var deficit2 = System.Math.Max(0, jMin - iMin);
        // For i in [iMin, jMin - 1]: require r >= jMin - i
        for (var iFixed = iMin; iFixed < jMin; iFixed++)
        {
            var rMin = jMin - iFixed;
            var rightCfg = BuildTwoBlockLinearCfg(s2, s3, rkRel, rMin, kMin);
            var map = RenameNonTerminalsSingle(rightCfg, new HashSet<string> { "S" });
            var leftFixed = $"{new string(s1, iFixed)}{new string(s2, iFixed)}";
            var lines = new List<string> { $"S -> {leftFixed}{map[rightCfg.StartSymbol]}" };
            lines.AddRange(FormatRenamedRules(rightCfg, map));
            parts2.Add(Cfg.ParseFromText(string.Join('\n', lines)));
        }

        // For i >= max(iMin, jMin): r >= 0 freely!
        var iStart = System.Math.Max(iMin, jMin);
        var rightFree = BuildTwoBlockLinearCfg(s2, s3, rkRel, 0, kMin);
        var mapFree = RenameNonTerminalsSingle(rightFree, new HashSet<string> { "S", "L" });
        var lBaseFree = iStart == 0 ? "e" : $"{new string(s1, iStart)}{new string(s2, iStart)}";
        var linesFree = new List<string>
        {
            $"S -> L{mapFree[rightFree.StartSymbol]}",
            $"L -> {s1}L{s2} | {lBaseFree}"
        };
        linesFree.AddRange(FormatRenamedRules(rightFree, mapFree));
        parts2.Add(Cfg.ParseFromText(string.Join('\n', linesFree)));

        return UnionCfgsSingleLetter(parts2);
    }

    private static Cfg ReverseCfg(Cfg cfg)
    {
        var rules = cfg.Rules
            .Select(r => new CfgRule(r.Left, r.Right.Reverse().ToList()))
            .ToList();
        return new Cfg(cfg.Variables, cfg.Terminals, rules, cfg.StartSymbol);
    }

    // =========================================================================
    // FAMILY 4: UNORDERED SYMBOL-COUNT LANGUAGES (#s1 op #s2)
    // =========================================================================

    private static LanguageSpecCompilationResult CompileSymbolCountSpec(SymbolCountSpec spec)
    {
        ValidateBounds(spec.Coeff1, spec.Coeff2, spec.Offset);
        var tests = GenerateSymbolCountSuggestedTests(spec);

        Cfg? cfg = null;
        if (spec.Coeff1 == 1 && spec.Coeff2 == 1 && spec.Offset == 0)
        {
            cfg = BuildUnitSymbolCountCfg(spec.Symbol1, spec.Symbol2, spec.Op, spec.Alphabet);
        }

        var pda = cfg is not null
            ? CfgToPdaConverter.Convert(cfg)
            : BuildCounterPda(spec);

        var tm = BuildSymbolCountMarkingTmSchema(spec);

        var title = FormatSymbolCountTitle(spec);
        var desc = cfg is not null
            ? $"Context-free symbol-count language over {{{string.Join(", ", spec.Alphabet)}}} synthesized into CFG ({cfg.Rules.Count} rules), PDA, and decidable TM."
            : $"Context-free weighted symbol-count language over {{{string.Join(", ", spec.Alphabet)}}} synthesized into counter PDA and decidable TM.";

        return BuildResult(spec.RawSpecification, LanguageClass.ContextFree, title, desc, cfg, pda, tm, tests);
    }

    private static Cfg BuildUnitSymbolCountCfg(char s1, char s2, CompareOp op, IReadOnlyList<char> alphabet)
    {
        var neutrals = alphabet.Where(c => c != s1 && c != s2).ToList();
        var eAlts = new List<string> { $"{s1}E{s2}E", $"{s2}E{s1}E" };
        foreach (var z in neutrals) eAlts.Add($"{z}E");
        eAlts.Add("e");
        var eRule = $"E -> {string.Join(" | ", eAlts)}";

        return op switch
        {
            CompareOp.Eq => Cfg.ParseFromText($"S -> E\n{eRule}"),
            CompareOp.Gt => Cfg.ParseFromText($"S -> E{s1}S | E{s1}E\n{eRule}"),
            CompareOp.Geq => Cfg.ParseFromText($"S -> E{s1}S | E\n{eRule}"),
            CompareOp.Lt => Cfg.ParseFromText($"S -> E{s2}S | E{s2}E\n{eRule}"),
            CompareOp.Leq => Cfg.ParseFromText($"S -> E{s2}S | E\n{eRule}"),
            CompareOp.Neq => Cfg.ParseFromText($"S -> L | R\nL -> E{s1}L | E{s1}E\nR -> E{s2}R | E{s2}E\n{eRule}"),
            _ => throw new FormatException($"Unsupported operator {op}.")
        };
    }

    private static PushdownAutomaton BuildCounterPda(SymbolCountSpec spec)
    {
        // Direct counter PDA tracking delta = p*#s1 - q*#s2 using stack symbols A (+1) and B (-1)
        var p = spec.Coeff1;
        var q = spec.Coeff2;
        var d = spec.Offset;
        var lines = new List<string> { "start: s", "accept: f" };

        // Reading s1 adds +p: consume k of B (0 <= k <= p) and push (p - k) of A
        for (var k = 0; k <= p; k++)
        {
            var pop = k == 0 ? "e" : new string('B', k);
            var push = (p - k) == 0 ? "e" : new string('A', p - k);
            lines.Add($"((s, {spec.Symbol1}, {pop}), (s, {push}))");
        }
        // Reading s2 subtracts q: consume k of A (0 <= k <= q) and push (q - k) of B
        for (var k = 0; k <= q; k++)
        {
            var pop = k == 0 ? "e" : new string('A', k);
            var push = (q - k) == 0 ? "e" : new string('B', q - k);
            lines.Add($"((s, {spec.Symbol2}, {pop}), (s, {push}))");
        }
        foreach (var z in spec.Alphabet.Where(c => c != spec.Symbol1 && c != spec.Symbol2))
        {
            lines.Add($"((s, {z}, e), (s, e))");
        }

        // Nondeterministic transition to drain state 'f' enforcing delta (op) d
        void AddDrain(string state, char sym, int minPop)
        {
            var reqPop = minPop == 0 ? "e" : new string(sym, minPop);
            lines.Add($"((s, e, {reqPop}), ({state}, e))");
            lines.Add($"(({state}, e, {sym}), ({state}, e))");
        }

        switch (spec.Op)
        {
            case CompareOp.Eq:
                var popEq = d == 0 ? "e" : (d > 0 ? new string('A', d) : new string('B', -d));
                lines.Add($"((s, e, {popEq}), (f, e))");
                break;
            case CompareOp.Gt:
                if (d + 1 >= 0) AddDrain("f", 'A', d + 1);
                else
                {
                    AddDrain("f", 'A', 0);
                    for (var bCnt = 1; bCnt <= -(d + 1); bCnt++)
                        lines.Add($"((s, e, {new string('B', bCnt)}), (f, e))");
                }
                break;
            case CompareOp.Geq:
                if (d >= 0) AddDrain("f", 'A', d);
                else
                {
                    AddDrain("f", 'A', 0);
                    for (var bCnt = 1; bCnt <= -d; bCnt++)
                        lines.Add($"((s, e, {new string('B', bCnt)}), (f, e))");
                }
                break;
            case CompareOp.Lt:
                if (1 - d >= 0) AddDrain("f", 'B', 1 - d);
                else
                {
                    AddDrain("f", 'B', 0);
                    for (var aCnt = 1; aCnt <= d - 1; aCnt++)
                        lines.Add($"((s, e, {new string('A', aCnt)}), (f, e))");
                }
                break;
            case CompareOp.Leq:
                if (-d >= 0) AddDrain("f", 'B', -d);
                else
                {
                    AddDrain("f", 'B', 0);
                    for (var aCnt = 1; aCnt <= d; aCnt++)
                        lines.Add($"((s, e, {new string('A', aCnt)}), (f, e))");
                }
                break;
            case CompareOp.Neq:
                // Either >= d+1 or <= d-1
                if (d + 1 >= 0) AddDrain("f", 'A', d + 1);
                else { AddDrain("f", 'A', 0); for (var bCnt = 1; bCnt <= -(d + 1); bCnt++) lines.Add($"((s, e, {new string('B', bCnt)}), (f, e))"); }
                if (1 - d >= 0) AddDrain("g", 'B', 1 - d);
                else { AddDrain("g", 'B', 0); for (var aCnt = 1; aCnt <= d - 1; aCnt++) lines.Add($"((s, e, {new string('A', aCnt)}), (g, e))"); }
                lines[1] = "accept: f, g";
                break;
        }

        return PushdownAutomaton.ParseTransitions(string.Join('\n', lines));
    }

    private static TmSchema BuildSymbolCountMarkingTmSchema(SymbolCountSpec spec)
    {
        var used = new HashSet<char>(spec.Alphabet) { 'y', 'n', AutomataSymbols.DefaultBlank };
        var m1 = PickFreshMarker('X', used);
        used.Add(m1);
        var m2 = PickFreshMarker('Q', used);
        used.Add(m2);

        var b = new TmSchemaBuilder($"Decider: {spec.RawSpecification}")
            .Alphabet(used)
            .Node("acc_end", "E Y R")
            .Node("rej_end", "E N R")
            .Node("acc_mid", "R_# E Y R")
            .Node("rej_mid", "R_# E N R");

        // Validate all characters belong to spec.Alphabet
        var allowedList = string.Join(", ", spec.Alphabet);
        b.StartNode("v_start", "L_# R")
         .Node("v_scan", "R")
         .Edge("v_start", "v_scan", allowedList)
         .Edge("v_start", "chk_start", "#")
         .Edge("v_start", "rej_mid", "other")
         .Edge("v_scan", "v_scan", allowedList)
         .Edge("v_scan", "chk_start", "#")
         .Edge("v_scan", "rej_mid", "other");

        AddTwoSymbolMarkCheckSubgraph(
            b, "chk", spec.Symbol1, m1, spec.Symbol2, m2,
            spec.Coeff1, spec.Coeff2, spec.Op, spec.Offset,
            onAccept: "acc_end", onReject: "rej_end");

        return b.Build();
    }

    // =========================================================================
    // REUSABLE MULTI-PASS NON-DESTRUCTIVE MARK-AND-RESTORE TM SYNTHESIZER
    // =========================================================================

    private static TmSchema BuildMultiPassMarkingTmSchema(
        string name,
        char[] blockSyms,
        IReadOnlyList<NormalizedBranch> branches,
        IReadOnlyList<char> alphabet)
    {
        var r = blockSyms.Length;
        var used = new HashSet<char>(alphabet) { 'y', 'n', AutomataSymbols.DefaultBlank };
        foreach (var s in blockSyms) used.Add(s);

        // Ensure every block has a distinct working tape symbol (if s1 == s3 e.g. a^n b^m a^k, tag block 2!)
        var workSyms = new char[r];
        var needsBlock2Tag = r == 3 && blockSyms[2] == blockSyms[0];
        workSyms[0] = blockSyms[0];
        workSyms[1] = blockSyms[1];
        if (r == 3)
        {
            if (needsBlock2Tag)
            {
                workSyms[2] = PickFreshMarker('T', used);
                used.Add(workSyms[2]);
            }
            else
            {
                workSyms[2] = blockSyms[2];
            }
        }

        // Distinct mark symbol per block
        var markSyms = new char[r];
        var preferred = new[] { 'X', 'Q', 'Z' };
        for (var i = 0; i < r; i++)
        {
            markSyms[i] = PickFreshMarker(preferred[i], used);
            used.Add(markSyms[i]);
        }

        var b = new TmSchemaBuilder(name)
            .Alphabet(used)
            .Node("acc_end", "E Y R")
            .Node("rej_end", "E N R")
            .Node("acc_mid", "R_# E Y R")
            .Node("rej_mid", "R_# E N R");

        // Phase 1: Validate block order s_0* s_1* [s_2*] (and tag block 2 -> workSyms[2] if needed)
        b.StartNode("ord_init", "L_# R");
        for (var k = 0; k < r; k++)
        {
            var scanId = $"ord_{k}";
            if (k == 2 && needsBlock2Tag)
            {
                b.Node("ord_2_tag", $"'{workSyms[2]}' R");
            }
            else
            {
                b.Node(scanId, "R");
            }
        }

        string TargetForBlock(int k) => (k == 2 && needsBlock2Tag) ? "ord_2_tag" : $"ord_{k}";

        // Transitions from ord_init (before consuming any symbol)
        b.Edge("ord_init", TargetForBlock(0), blockSyms[0].ToString());
        b.Edge("ord_init", TargetForBlock(1), blockSyms[1].ToString());
        if (r == 3 && !needsBlock2Tag)
        {
            b.Edge("ord_init", TargetForBlock(2), blockSyms[2].ToString());
        }
        b.Edge("ord_init", "br_0_start", "#");
        b.Edge("ord_init", "rej_mid", "other");

        // Transitions from block 0
        b.Edge(TargetForBlock(0), TargetForBlock(0), blockSyms[0].ToString());
        b.Edge(TargetForBlock(0), TargetForBlock(1), blockSyms[1].ToString());
        if (r == 3 && !needsBlock2Tag)
        {
            b.Edge(TargetForBlock(0), TargetForBlock(2), blockSyms[2].ToString());
        }
        b.Edge(TargetForBlock(0), "br_0_start", "#");
        b.Edge(TargetForBlock(0), "rej_mid", "other");

        // Transitions from block 1
        b.Edge(TargetForBlock(1), TargetForBlock(1), blockSyms[1].ToString());
        if (r == 3)
        {
            b.Edge(TargetForBlock(1), TargetForBlock(2), blockSyms[2].ToString());
        }
        b.Edge(TargetForBlock(1), "br_0_start", "#");
        b.Edge(TargetForBlock(1), "rej_mid", "other");

        // Transitions from block 2 (if 3 blocks)
        if (r == 3)
        {
            b.Edge(TargetForBlock(2), TargetForBlock(2), blockSyms[2].ToString());
            b.Edge(TargetForBlock(2), "br_0_start", "#");
            b.Edge(TargetForBlock(2), "rej_mid", "other");
        }

        // Phase 2: Evaluate branches sequentially (OR across branches; AND within each branch)
        for (var bi = 0; bi < branches.Count; bi++)
        {
            var br = branches[bi];
            var branchEntry = $"br_{bi}_start";
            var nextBranchOnFail = bi + 1 < branches.Count ? $"br_{bi}_restore_fail" : "rej_end";
            if (bi + 1 < branches.Count)
            {
                AddRestoreMarksSubgraph(b, $"br_{bi}_restore_fail", workSyms, markSyms, $"br_{bi + 1}_start");
            }

            // Collect ordered checks inside this branch: (bounds checks) + (2-var checks) + (additive checks)
            var checkSteps = new List<(string Kind, int Index)>();
            for (var k = 0; k < r; k++)
            {
                if (br.Bounds[k].Min > 0 || br.Bounds[k].Max.HasValue)
                    checkSteps.Add(("bound", k));
            }
            for (var k = 0; k < br.TwoVarRelations.Count; k++)
                checkSteps.Add(("two", k));
            for (var k = 0; k < br.AdditiveRelations.Count; k++)
                checkSteps.Add(("add", k));

            if (checkSteps.Count == 0)
            {
                // Empty check list = tautology after order check -> accept!
                b.Node(branchEntry, "L_# R_#")
                 .Edge(branchEntry, "acc_end", "*");
                continue;
            }

            // Route branchEntry to first check
            b.Node(branchEntry, "L_# R_#")
             .Edge(branchEntry, $"br_{bi}_c0_start", "*");

            for (var si = 0; si < checkSteps.Count; si++)
            {
                var stepPrefix = $"br_{bi}_c{si}";
                var isLast = si + 1 == checkSteps.Count;
                var onPass = isLast ? "acc_end" : $"{stepPrefix}_restore_ok";
                if (!isLast)
                {
                    AddRestoreMarksSubgraph(b, onPass, workSyms, markSyms, $"br_{bi}_c{si + 1}_start");
                }

                var (kind, idx) = checkSteps[si];
                if (kind == "bound")
                {
                    AddSingleSymbolBoundCheckSubgraph(
                        b, stepPrefix, workSyms[idx], br.Bounds[idx],
                        onAccept: onPass, onReject: nextBranchOnFail);
                }
                else if (kind == "two")
                {
                    var rel = br.TwoVarRelations[idx];
                    AddTwoSymbolMarkCheckSubgraph(
                        b, stepPrefix,
                        workSyms[rel.BlockA], markSyms[rel.BlockA],
                        workSyms[rel.BlockB], markSyms[rel.BlockB],
                        rel.CoeffA, rel.CoeffB, rel.Op, rel.Offset,
                        onAccept: onPass, onReject: nextBranchOnFail,
                        forcedStepA: rel.StepA, forcedStepB: rel.StepB);
                }
                else if (kind == "add")
                {
                    var add = br.AdditiveRelations[idx];
                    AddAdditiveMarkCheckSubgraph(
                        b, stepPrefix,
                        workSyms[add.SumA], markSyms[add.SumA],
                        workSyms[add.SumB], markSyms[add.SumB],
                        workSyms[add.Target], markSyms[add.Target],
                        add.Op, add.Offset,
                        onAccept: onPass, onReject: nextBranchOnFail);
                }
            }
        }

        return b.Build();
    }

    private static void AddRestoreMarksSubgraph(
        TmSchemaBuilder b, string entryId, char[] workSyms, char[] markSyms, string nextNode)
    {
        var scanId = $"{entryId}_scan";
        b.Node(entryId, "L_# R")
         .Node(scanId, "R");

        void WireFrom(string src)
        {
            b.Edge(src, nextNode, "#");
            for (var i = 0; i < workSyms.Length; i++)
            {
                var fixId = $"{entryId}_unmark_{i}";
                if (!HasNode(b, fixId))
                {
                    b.Node(fixId, $"'{workSyms[i]}' R");
                    WireFrom(fixId);
                }
                b.Edge(src, fixId, markSyms[i].ToString());
            }
            b.Edge(src, scanId, "other");
        }

        WireFrom(entryId);
        WireFrom(scanId);
    }

    private static void AddSingleSymbolBoundCheckSubgraph(
        TmSchemaBuilder b, string prefix, char sym, VariableBounds bd, string onAccept, string onReject)
    {
        var limit = bd.Max.HasValue ? bd.Max.Value + 1 : bd.Min;
        b.Node($"{prefix}_start", "L_# R");

        for (var k = 0; k <= limit; k++)
        {
            var cur = k == 0 ? $"{prefix}_start" : $"{prefix}_cnt_{k}";
            if (k > 0) b.Node(cur, "R");

            var endOk = bd.Contains(k);
            b.Edge(cur, endOk ? onAccept : onReject, "#");

            if (k < limit)
            {
                b.Edge(cur, $"{prefix}_cnt_{k + 1}", sym.ToString());
                b.Edge(cur, cur == $"{prefix}_start" ? $"{prefix}_skip_0" : cur, "other");
                if (k == 0)
                {
                    var sk0 = $"{prefix}_skip_0";
                    b.Node(sk0, "R");
                    b.Edge(sk0, endOk ? onAccept : onReject, "#");
                    b.Edge(sk0, $"{prefix}_cnt_1", sym.ToString());
                    b.Edge(sk0, sk0, "other");
                }
            }
            else
            {
                // Count >= limit
                if (bd.Max.HasValue)
                {
                    // Exceeded max -> fail cleanly at right '#'
                    var failEnd = $"{prefix}_fail_end";
                    b.Node(failEnd, "R_#");
                    b.Edge(cur, failEnd, sym.ToString());
                    b.Edge(cur, cur, "other");
                    b.Edge(failEnd, onReject, "*");
                }
                else
                {
                    // Reached min with no max -> pass cleanly at right '#'
                    var okEnd = $"{prefix}_ok_end";
                    b.Node(okEnd, "R_#");
                    b.Edge(cur, okEnd, sym.ToString());
                    b.Edge(cur, cur, "other");
                    b.Edge(okEnd, onAccept, "*");
                }
            }
        }
    }

    /// <summary>
    /// Non-destructive two-symbol linear relation checker:
    /// Checks p * count(symA) - q * count(symB) (op) d by marking stepA of symA -> markA and stepB of symB -> markB
    /// in lockstep, leaving all other tape symbols untouched. Always routes to onAccept / onReject with head at right '#'.
    /// </summary>
    private static void AddTwoSymbolMarkCheckSubgraph(
        TmSchemaBuilder b,
        string prefix,
        char symA, char markA,
        char symB, char markB,
        int p, int q, CompareOp op, int d,
        string onAccept, string onReject,
        int forcedStepA = 0, int forcedStepB = 0)
    {
        var g = Gcd(p, q);
        var stepA = forcedStepA > 0 ? forcedStepA : q / g;
        var stepB = forcedStepB > 0 ? forcedStepB : p / g;

        var startId = $"{prefix}_start";
        b.Node(startId, "L_# R")
         .Edge(startId, $"{prefix}_ma_0_go", "*");

        // Phase A: mark stepA occurrences of symA -> markA
        for (var ca = 0; ca < stepA; ca++)
        {
            var goId = $"{prefix}_ma_{ca}_go";
            var scanId = $"{prefix}_ma_{ca}_scan";
            var writeId = $"{prefix}_ma_{ca}_w";

            // goId starts at cell 1 (or right after previous mark)
            b.Node(goId, ca == 0 ? "L_# R" : "R")
             .Node(scanId, "R")
             .Node(writeId, $"'{markA}'");

            var nextAfterMark = ca + 1 < stepA ? $"{prefix}_ma_{ca + 1}_go" : $"{prefix}_mb_0_go";
            b.Edge(writeId, nextAfterMark, "*");

            // When '#' is hit at ca < stepA: zero unmarked symA remain!
            // Remaining count of unmarked symB determines p*ca - q*remB (op) d
            var countBEntry = AddMarkedRemainingCountTail(
                b, $"{prefix}_remB_{ca}", symB,
                remB => LinearConstraint.EvaluateOp(p * ca - q * remB, op, d),
                maxRelevant: ComputeRelevantThreshold(p * ca, q, d) + 2,
                onAccept, onReject);

            b.Edge(goId, writeId, symA.ToString());
            b.Edge(goId, countBEntry, "#");
            b.Edge(goId, scanId, "other");

            b.Edge(scanId, writeId, symA.ToString());
            b.Edge(scanId, countBEntry, "#");
            b.Edge(scanId, scanId, "other");
        }

        // Phase B: mark stepB occurrences of symB -> markB
        for (var cb = 0; cb < stepB; cb++)
        {
            var goId = $"{prefix}_mb_{cb}_go";
            var scanId = $"{prefix}_mb_{cb}_scan";
            var writeId = $"{prefix}_mb_{cb}_w";

            b.Node(goId, cb == 0 ? "L_# R" : "R")
             .Node(scanId, "R")
             .Node(writeId, $"'{markB}'");

            var nextAfterMark = cb + 1 < stepB ? $"{prefix}_mb_{cb + 1}_go" : $"{prefix}_ma_0_go";
            b.Edge(writeId, nextAfterMark, "*");

            // When '#' is hit at cb < stepB: zero unmarked symB remain!
            // Remaining unmarked symA count determines p*(stepA + remA) - q*cb (op) d
            var countAEntry = AddMarkedRemainingCountTail(
                b, $"{prefix}_remA_{cb}", symA,
                remA => LinearConstraint.EvaluateOp(p * (stepA + remA) - q * cb, op, d),
                maxRelevant: ComputeRelevantThreshold(q * cb + d, p, 0) + 2,
                onAccept, onReject);

            b.Edge(goId, writeId, symB.ToString());
            b.Edge(goId, countAEntry, "#");
            b.Edge(goId, scanId, "other");

            b.Edge(scanId, writeId, symB.ToString());
            b.Edge(scanId, countAEntry, "#");
            b.Edge(scanId, scanId, "other");
        }
    }

    /// <summary>
    /// Non-destructive 3-symbol additive checker:
    /// Checks count(symA) + count(symB) (op) count(symC) + offset by marking one unmarked {symA or symB}
    /// paired with one unmarked symC in lockstep.
    /// </summary>
    private static void AddAdditiveMarkCheckSubgraph(
        TmSchemaBuilder b,
        string prefix,
        char symA, char markA,
        char symB, char markB,
        char symC, char markC,
        CompareOp op, int offset,
        string onAccept, string onReject)
    {
        var startId = $"{prefix}_start";
        var findLhs = $"{prefix}_lhs_go";
        var scanLhs = $"{prefix}_lhs_scan";
        var writeA = $"{prefix}_wA";
        var writeB = $"{prefix}_wB";
        var findRhs = $"{prefix}_rhs_go";
        var scanRhs = $"{prefix}_rhs_scan";
        var writeC = $"{prefix}_wC";

        b.Node(startId, "L_# R")
         .Edge(startId, findLhs, "*");

        b.Node(findLhs, "L_# R")
         .Node(scanLhs, "R")
         .Node(writeA, $"'{markA}'")
         .Node(writeB, $"'{markB}'")
         .Node(findRhs, "L_# R")
         .Node(scanRhs, "R")
         .Node(writeC, $"'{markC}'");

        // When no unmarked symA/symB left: remaining unmarked symC determines 0 - remC (op) offset
        var remCEntry = AddMarkedRemainingCountTail(
            b, $"{prefix}_remC", symC,
            remC => LinearConstraint.EvaluateOp(-remC, op, offset),
            maxRelevant: System.Math.Abs(offset) + 3,
            onAccept, onReject);

        b.Edge(findLhs, writeA, symA.ToString())
         .Edge(findLhs, writeB, symB.ToString())
         .Edge(findLhs, remCEntry, "#")
         .Edge(findLhs, scanLhs, "other");

        b.Edge(scanLhs, writeA, symA.ToString())
         .Edge(scanLhs, writeB, symB.ToString())
         .Edge(scanLhs, remCEntry, "#")
         .Edge(scanLhs, scanLhs, "other");

        b.Edge(writeA, findRhs, "*")
         .Edge(writeB, findRhs, "*");

        // When no unmarked symC left after marking 1 LHS symbol: remaining unmarked (symA or symB) gives (1 + remAB) - 0 (op) offset
        var remABEntry = AddMarkedRemainingDualCountTail(
            b, $"{prefix}_remAB", symA, symB,
            remAB => LinearConstraint.EvaluateOp(1 + remAB, op, offset),
            maxRelevant: System.Math.Abs(offset) + 3,
            onAccept, onReject);

        b.Edge(findRhs, writeC, symC.ToString())
         .Edge(findRhs, remABEntry, "#")
         .Edge(findRhs, scanRhs, "other");

        b.Edge(scanRhs, writeC, symC.ToString())
         .Edge(scanRhs, remABEntry, "#")
         .Edge(scanRhs, scanRhs, "other");

        b.Edge(writeC, findLhs, "*");
    }

    private static string AddMarkedRemainingCountTail(
        TmSchemaBuilder b, string prefix, char targetSym,
        Func<int, bool> eval, int maxRelevant, string onAccept, string onReject)
    {
        var tailVal = eval(maxRelevant + 5);
        var stableFrom = maxRelevant + 1;
        while (stableFrom > 0 && eval(stableFrom - 1) == tailVal)
        {
            stableFrom--;
        }

        if (stableFrom == 0)
        {
            // Head is already at right '#' when entering this tail!
            return tailVal ? onAccept : onReject;
        }

        var entryId = $"{prefix}_0";
        b.Node(entryId, "L_# R");
        for (var k = 0; k < stableFrom; k++)
        {
            var cur = k == 0 ? entryId : $"{prefix}_{k}";
            var skip = $"{prefix}_sk_{k}";
            if (k > 0) b.Node(cur, "R");
            b.Node(skip, "R");

            var endRes = eval(k) ? onAccept : onReject;
            var nextOnMatch = k + 1 < stableFrom ? $"{prefix}_{k + 1}" : $"{prefix}_done";
            if (k + 1 == stableFrom && !HasNode(b, $"{prefix}_done"))
            {
                b.Node($"{prefix}_done", "R_#")
                 .Edge($"{prefix}_done", tailVal ? onAccept : onReject, "*");
            }

            b.Edge(cur, endRes, "#");
            b.Edge(cur, nextOnMatch, targetSym.ToString());
            b.Edge(cur, skip, "other");

            b.Edge(skip, endRes, "#");
            b.Edge(skip, nextOnMatch, targetSym.ToString());
            b.Edge(skip, skip, "other");
        }
        return entryId;
    }

    private static string AddMarkedRemainingDualCountTail(
        TmSchemaBuilder b, string prefix, char sym1, char sym2,
        Func<int, bool> eval, int maxRelevant, string onAccept, string onReject)
    {
        var tailVal = eval(maxRelevant + 5);
        var stableFrom = maxRelevant + 1;
        while (stableFrom > 0 && eval(stableFrom - 1) == tailVal)
        {
            stableFrom--;
        }

        if (stableFrom == 0)
        {
            return tailVal ? onAccept : onReject;
        }

        var matchCond = $"{sym1}, {sym2}";
        var entryId = $"{prefix}_0";
        b.Node(entryId, "L_# R");
        for (var k = 0; k < stableFrom; k++)
        {
            var cur = k == 0 ? entryId : $"{prefix}_{k}";
            var skip = $"{prefix}_sk_{k}";
            if (k > 0) b.Node(cur, "R");
            b.Node(skip, "R");

            var endRes = eval(k) ? onAccept : onReject;
            var nextOnMatch = k + 1 < stableFrom ? $"{prefix}_{k + 1}" : $"{prefix}_done";
            if (k + 1 == stableFrom && !HasNode(b, $"{prefix}_done"))
            {
                b.Node($"{prefix}_done", "R_#")
                 .Edge($"{prefix}_done", tailVal ? onAccept : onReject, "*");
            }

            b.Edge(cur, endRes, "#");
            b.Edge(cur, nextOnMatch, matchCond);
            b.Edge(cur, skip, "other");

            b.Edge(skip, endRes, "#");
            b.Edge(skip, nextOnMatch, matchCond);
            b.Edge(skip, skip, "other");
        }
        return entryId;
    }

    // =========================================================================
    // AUTO-GENERATED WITNESS & BOUNDARY TEST CASES
    // =========================================================================

    private static IReadOnlyList<LanguageTestCase> GenerateBlockSuggestedTests(
        BlockExponentSpec spec, IReadOnlyList<NormalizedBranch> branches, IReadOnlyList<char> alphabet)
    {
        var positives = new List<LanguageTestCase>();
        var negatives = new List<LanguageTestCase>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var syms = spec.Blocks.Select(b => b.Symbol).ToArray();

        void TryAdd(string word, bool expected, string reason)
        {
            var disp = word.Length == 0 ? "e" : word;
            if (!seen.Add(disp)) return;
            var tc = new LanguageTestCase(disp, expected, reason);
            if (expected) { if (positives.Count < 4) positives.Add(tc); }
            else { if (negatives.Count < 4) negatives.Add(tc); }
        }

        // Always test empty string first
        var emptyAcc = branches.Any(b => b.SatisfiedBy(new int[syms.Length]));
        TryAdd("", emptyAcc, emptyAcc ? "empty string satisfies spec" : "empty string rejected");

        if (syms.Length == 1)
        {
            for (var n = 1; n <= 8; n++)
            {
                var acc = branches.Any(b => b.SatisfiedBy(new[] { n }));
                TryAdd(new string(syms[0], n), acc, $"n={n}");
            }
        }
        else if (syms.Length == 2)
        {
            // Sweep small (x, y) ordered by total length x + y so shortest witnesses come first
            for (var len = 1; len <= 9 && (positives.Count < 4 || negatives.Count < 4); len++)
            {
                for (var x = 0; x <= len; x++)
                {
                    var y = len - x;
                    var acc = branches.Any(b => b.SatisfiedBy(new[] { x, y }));
                    var w = new string(syms[0], x) + new string(syms[1], y);
                    TryAdd(w, acc, $"{syms[0]}={x}, {syms[1]}={y}");
                }
            }
            // Always add an out-of-order structural negative test (e.g. "ba" or "aba")
            TryAdd($"{syms[1]}{syms[0]}", false, "out of block order");
            TryAdd($"{syms[0]}{syms[1]}{syms[0]}", false, "out of block order");
        }
        else if (syms.Length == 3)
        {
            var sameOuter = syms[0] == syms[2];
            for (var len = 1; len <= 9 && (positives.Count < 4 || negatives.Count < 4); len++)
            {
                for (var i = 0; i <= len; i++)
                {
                    for (var j = 0; j <= len - i; j++)
                    {
                        if (sameOuter && j == 0) continue; // Prefer unambiguous separated blocks when s1 == s3
                        var k = len - i - j;
                        var acc = branches.Any(b => b.SatisfiedBy(new[] { i, j, k }));
                        var w = new string(syms[0], i) + new string(syms[1], j) + new string(syms[2], k);
                        TryAdd(w, acc, $"({i},{j},{k})");
                    }
                }
            }
            if (!sameOuter)
            {
                TryAdd($"{syms[0]}{syms[2]}{syms[1]}", false, "out of block order");
            }
            else
            {
                TryAdd($"{syms[1]}{syms[0]}{syms[1]}", false, "out of block order");
            }
        }

        // Interleave positives then negatives for crisp presentation
        return positives.Concat(negatives).ToList();
    }

    private static IReadOnlyList<LanguageTestCase> GenerateSymbolCountSuggestedTests(SymbolCountSpec spec)
    {
        var positives = new List<LanguageTestCase>();
        var negatives = new List<LanguageTestCase>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var syms = spec.Alphabet;

        void TryAdd(string word)
        {
            var disp = word.Length == 0 ? "e" : word;
            if (!seen.Add(disp)) return;
            var acc = EvaluateSymbolCountSpec(spec, word);
            var c1 = word.Count(c => c == spec.Symbol1);
            var c2 = word.Count(c => c == spec.Symbol2);
            var tc = new LanguageTestCase(disp, acc, $"#{spec.Symbol1}={c1}, #{spec.Symbol2}={c2}");
            if (acc) { if (positives.Count < 4) positives.Add(tc); }
            else { if (negatives.Count < 4) negatives.Add(tc); }
        }

        TryAdd("");
        var queue = new Queue<string>();
        queue.Enqueue("");
        while (queue.Count > 0 && (positives.Count < 4 || negatives.Count < 4))
        {
            var cur = queue.Dequeue();
            if (cur.Length >= 5) continue;
            foreach (var c in syms)
            {
                var next = cur + c;
                TryAdd(next);
                queue.Enqueue(next);
            }
        }

        return positives.Concat(negatives).ToList();
    }

    // =========================================================================
    // SINGLE-LETTER NON-TERMINAL CFG UNION & HELPERS
    // =========================================================================

    private static Cfg UnionCfgsSingleLetter(IReadOnlyList<Cfg> grammars)
    {
        if (grammars.Count == 1) return grammars[0];

        var used = new HashSet<string>(StringComparer.Ordinal) { "S" };
        var startAlts = new List<string>();
        var allLines = new List<string>();

        foreach (var g in grammars)
        {
            var map = RenameNonTerminalsSingle(g, used);
            startAlts.Add(map[g.StartSymbol]);
            allLines.AddRange(FormatRenamedRules(g, map));
        }

        allLines.Insert(0, $"S -> {string.Join(" | ", startAlts)}");
        return Cfg.ParseFromText(string.Join('\n', allLines));
    }

    private static Dictionary<string, string> RenameNonTerminalsSingle(Cfg g, HashSet<string> used)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nt in g.NonTerminals.OrderBy(x => x == g.StartSymbol ? 0 : 1).ThenBy(x => x, StringComparer.Ordinal))
        {
            var fresh = PickSingleNt(nt, used);
            used.Add(fresh);
            map[nt] = fresh;
        }
        return map;
    }

    private static IEnumerable<string> FormatRenamedRules(Cfg g, IReadOnlyDictionary<string, string> map)
    {
        var grouped = g.Rules
            .GroupBy(r => map[r.Left], StringComparer.Ordinal);
        foreach (var grp in grouped)
        {
            var alts = grp.Select(r =>
            {
                if (r.Right.Count == 0) return "e";
                return string.Concat(r.Right.Select(sym => map.TryGetValue(sym, out var m) ? m : sym));
            });
            yield return $"{grp.Key} -> {string.Join(" | ", alts)}";
        }
    }

    private static string PickSingleNt(string preferred, HashSet<string> used)
    {
        if (preferred.Length == 1 && char.IsUpper(preferred[0]) && !used.Contains(preferred))
        {
            return preferred;
        }
        foreach (var c in "LRABCDEFGHJKMNPQTUVWXYZ")
        {
            var s = c.ToString();
            if (!used.Contains(s)) return s;
        }
        var idx = 1;
        while (used.Contains($"N{idx}")) idx++;
        return $"N{idx}";
    }

    private static bool HasNode(TmSchemaBuilder builder, string nodeId)
    {
        // Peek safely via reflection-free helper? Let's keep track cleanly or expose HasNode on TmSchemaBuilder!
        return builder.ContainsNode(nodeId);
    }

    private static char PickFreshMarker(char preferred, HashSet<char> used)
    {
        if (!used.Contains(preferred)) return preferred;
        foreach (var c in "XQZMPWKJTUVDGHFBR")
        {
            if (!used.Contains(c)) return c;
        }
        return '$';
    }

    private static IReadOnlyList<char> BuildBlockAlphabet(char[] blockSyms, IReadOnlyList<char>? explicitAlpha)
    {
        var set = new SortedSet<char>(blockSyms);
        if (explicitAlpha is not null)
        {
            foreach (var c in explicitAlpha) set.Add(c);
        }
        return set.ToList();
    }

    private static string FormatSpecTitle(BlockExponentSpec spec)
    {
        var pattern = string.Join("", spec.Blocks.Select(b =>
        {
            var e = b.Exponent.Format();
            return e.Length == 1 ? $"{b.Symbol}^{e}" : $"{b.Symbol}^({e})";
        }));
        var hasPred = spec.Branches.Any(br => br.Constraints.Count > 0);
        if (!hasPred)
        {
            return $"L = {{ {pattern} }}";
        }
        var predText = string.Join(" ∨ ", spec.Branches.Select(br =>
            string.Join(", ", br.Constraints.Select(c => c.Format()))));
        return $"L = {{ {pattern} | {predText} }}";
    }

    private static string FormatSymbolCountTitle(SymbolCountSpec spec)
    {
        var lhs = spec.Coeff1 == 1 ? $"#{spec.Symbol1}(w)" : $"{spec.Coeff1}·#{spec.Symbol1}(w)";
        var rhs = spec.Coeff2 == 1 ? $"#{spec.Symbol2}(w)" : $"{spec.Coeff2}·#{spec.Symbol2}(w)";
        if (spec.Offset != 0)
        {
            rhs += spec.Offset > 0 ? $" + {spec.Offset}" : $" - {System.Math.Abs(spec.Offset)}";
        }
        return $"L = {{ w ∈ {{{string.Join(", ", spec.Alphabet)}}}* | {lhs} {LinearConstraint.FormatOp(spec.Op)} {rhs} }}";
    }

    private static LanguageSpecCompilationResult BuildResult(
        string spec, LanguageClass langClass, string title, string description,
        Cfg? cfg, PushdownAutomaton? pda, TmSchema schema,
        IReadOnlyList<LanguageTestCase> tests)
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
            CompiledTm: compiled,
            SuggestedTests: tests);
    }

    private static void ValidateBounds(int c1, int c2, int offset)
    {
        if (System.Math.Abs(c1) > MaxCoeff || System.Math.Abs(c2) > MaxCoeff)
        {
            throw new FormatException($"Linear coefficients must be at most {MaxCoeff} to keep synthesized machines compact.");
        }
        ValidateOffset(offset);
    }

    private static void ValidateOffset(int offset)
    {
        if (System.Math.Abs(offset) > MaxOffset)
        {
            throw new FormatException($"Constant offset ({offset}) exceeds practical display limit (±{MaxOffset}).");
        }
    }

    private static int Gcd(int a, int b)
    {
        a = System.Math.Abs(a);
        b = System.Math.Abs(b);
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }

    private static int CeilDiv(int num, int den) =>
        num >= 0 ? (num + den - 1) / den : num / den;

    private static int FloorDiv(int num, int den) =>
        num >= 0 ? num / den : (num - den + 1) / den;
}
