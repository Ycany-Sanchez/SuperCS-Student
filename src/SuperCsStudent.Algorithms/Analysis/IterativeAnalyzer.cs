using SuperCsStudent.Algorithms.Ast;
using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Analysis;

/// <summary>
/// Exact symbolic runtime analyzer for iterative algorithms (and hybrid functions containing recursive calls).
/// Computes exact worst-case and best-case operation counts T(n), step-by-step algebraic derivations,
/// inner-loop box annotations (T_in(i), T_in2(j)), annotated pseudocode with inline op badges,
/// asymptotic witness proofs, and growth chart data.
/// </summary>
public static class IterativeAnalyzer
{
    public static RuntimeAnalysisResult Analyze(
        AlgorithmFunction fn,
        CostRulesProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(fn);
        profile ??= CostRulesProfile.StandardUnitCost;

        var worstCtx = new AnalysisContext(profile, isWorstCase: true);
        var bestCtx = new AnalysisContext(profile, isWorstCase: false);

        var worstEval = AnalyzeFunctionBody(fn, worstCtx);
        var bestEval = AnalyzeFunctionBody(fn, bestCtx);

        var primaryVar = DetectPrimaryVariable(fn, worstEval, bestEval);

        var worstCase = new CaseAnalysis(
            CaseName: "Worst Case",
            Expression: worstEval,
            Latex: worstEval.ToLatexString(),
            Plain: worstEval.ToPlainString(),
            DerivationSteps: worstCtx.Steps,
            AnnotatedLines: worstCtx.Lines,
            InnerBoxes: worstCtx.InnerBoxes);

        var bestCase = new CaseAnalysis(
            CaseName: "Best Case",
            Expression: bestEval,
            Latex: bestEval.ToLatexString(),
            Plain: bestEval.ToPlainString(),
            DerivationSteps: bestCtx.Steps,
            AnnotatedLines: bestCtx.Lines,
            InnerBoxes: bestCtx.InnerBoxes);

        var asymptotic = AsymptoticAnalyzer.Analyze(worstEval, bestEval, primaryVar);
        var chartSeries = AsymptoticAnalyzer.BuildGrowthChartSeries(worstEval, bestEval, primaryVar);

        RecurrenceAnalysisResult? recurrence = null;
        if (ContainsRecursiveCall(fn.Body))
        {
            recurrence = RecurrenceAnalyzer.AnalyzeFunction(fn, profile);
        }

        return new RuntimeAnalysisResult(
            FunctionName: fn.Name,
            PrimaryVariable: primaryVar,
            Profile: profile,
            WorstCase: worstCase,
            BestCase: bestCase,
            Asymptotic: asymptotic,
            GrowthChartSeries: chartSeries,
            Recurrence: recurrence);
    }

    private static SymbolicExpr AnalyzeFunctionBody(AlgorithmFunction fn, AnalysisContext ctx)
    {
        var paramList = string.Join(", ", fn.Parameters);
        ctx.AddLine(null, 0, $"function {fn.Name}({paramList}) {{", Array.Empty<InlineOpBadge>(), "0");

        var total = AnalyzeBlock(fn.Body, ctx, indent: 1, freqLabel: "1", enclosingBoxId: null);

        ctx.AddLine(null, 0, "}", Array.Empty<InlineOpBadge>(), "0");

        var pVar = DetectPrimaryVariable(fn, total, total);
        ctx.Steps.Add(new DerivationStep(
            ctx.IsWorstCase ? "Total Worst-Case T(n)" : "Total Best-Case T(n)",
            $@"T({pVar}) = {total.ToLatexString()}",
            $"T({pVar}) = {total.ToPlainString()}"));

        return total;
    }

    private static SymbolicExpr AnalyzeBlock(
        IReadOnlyList<AlgorithmStatement> statements,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? enclosingBoxId)
    {
        var sum = SymbolicExpr.Zero;
        foreach (var stmt in statements)
        {
            sum += AnalyzeStatement(stmt, ctx, indent, freqLabel, enclosingBoxId);
        }
        return sum;
    }

    private static SymbolicExpr AnalyzeStatement(
        AlgorithmStatement stmt,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? enclosingBoxId)
    {
        return stmt switch
        {
            DeclareStatement decl => AnalyzeDeclare(decl, ctx, indent, freqLabel, enclosingBoxId),
            AssignStatement assign => AnalyzeAssign(assign, ctx, indent, freqLabel, enclosingBoxId),
            UnaryStatement unary => AnalyzeUnary(unary, ctx, indent, freqLabel, enclosingBoxId),
            ReturnStatement ret => AnalyzeReturn(ret, ctx, indent, freqLabel, enclosingBoxId),
            BreakStatement brk => AnalyzeBreak(brk, ctx, indent, enclosingBoxId),
            CallStatement call => AnalyzeCall(call, ctx, indent, freqLabel, enclosingBoxId),
            RecursiveCallStatement rec => AnalyzeRecursiveCallStub(rec, ctx, indent, enclosingBoxId),
            IfStatement ifStmt => AnalyzeIf(ifStmt, ctx, indent, freqLabel, enclosingBoxId),
            SwitchStatement sw => AnalyzeSwitch(sw, ctx, indent, freqLabel, enclosingBoxId),
            ForStatement forStmt => AnalyzeFor(forStmt, ctx, indent, freqLabel, enclosingBoxId),
            WhileStatement whileStmt => AnalyzeWhile(whileStmt, ctx, indent, freqLabel, enclosingBoxId),
            _ => SymbolicExpr.Zero
        };
    }

    private static SymbolicExpr AnalyzeDeclare(
        DeclareStatement decl,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var cost = ctx.Profile.DeclarationCost > 0
            ? ctx.Profile.DeclarationCost * Math.Max(1, decl.Names.Count)
            : 0;
        if (cost > 0)
        {
            badges.Add(new InlineOpBadge("declare", freqLabel, "Variable declaration"));
        }

        var code = $"int {string.Join(", ", decl.Names)};";
        var expr = SymbolicExpr.Constant(cost);
        ctx.AddLine(decl.Id, indent, code, badges, expr.ToLatexString(), boxId);
        return expr;
    }

    private static SymbolicExpr AnalyzeAssign(
        AssignStatement assign,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var opCost = assign.Op == "=" ? 1 : ctx.Profile.CompoundAssignCost;
        if (opCost > 0)
        {
            badges.Add(new InlineOpBadge(assign.Op, freqLabel,
                assign.Op == "=" ? "Assignment" : $"Compound assignment (cost {opCost})"));
        }

        var targetIndexCost = 0;
        if (assign.Target.Contains('[') && assign.Target.Contains(']'))
        {
            targetIndexCost = ctx.Profile.ArrayIndexingCost;
            if (targetIndexCost > 0)
            {
                badges.Add(new InlineOpBadge(assign.Target, freqLabel, "Array indexing"));
            }
        }

        var rhsCost = CountExprOps(assign.Value, ctx.Profile, badges, freqLabel);
        var total = SymbolicExpr.Constant(opCost + targetIndexCost + rhsCost);
        var code = $"{assign.Target} {assign.Op} {FormatExpr(assign.Value)};";
        ctx.AddLine(assign.Id, indent, code, badges, total.ToLatexString(), boxId);
        return total;
    }

    private static SymbolicExpr AnalyzeUnary(
        UnaryStatement unary,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var cost = ctx.Profile.UnaryIncrementCost;
        if (cost > 0)
        {
            badges.Add(new InlineOpBadge(unary.Op, freqLabel, $"Unary {unary.Op} (cost {cost})"));
        }
        var expr = SymbolicExpr.Constant(cost);
        ctx.AddLine(unary.Id, indent, $"{unary.Variable}{unary.Op};", badges, expr.ToLatexString(), boxId);
        return expr;
    }

    private static SymbolicExpr AnalyzeReturn(
        ReturnStatement ret,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var kwCost = ctx.Profile.ReturnKeywordCost;
        if (kwCost > 0)
        {
            badges.Add(new InlineOpBadge("return", freqLabel, "Return keyword"));
        }
        var exprCost = CountExprOps(ret.Value, ctx.Profile, badges, freqLabel);
        var total = SymbolicExpr.Constant(kwCost + exprCost);
        var code = ret.Value is null ? "return;" : $"return {FormatExpr(ret.Value)};";
        ctx.AddLine(ret.Id, indent, code, badges, total.ToLatexString(), boxId);
        return total;
    }

    private static SymbolicExpr AnalyzeBreak(
        BreakStatement brk,
        AnalysisContext ctx,
        int indent,
        string? boxId)
    {
        ctx.AddLine(brk.Id, indent, "break;", Array.Empty<InlineOpBadge>(), "0", boxId);
        return SymbolicExpr.Zero;
    }

    private static SymbolicExpr AnalyzeCall(
        CallStatement call,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var cost = ctx.Profile.FunctionCallCost;
        if (cost > 0)
        {
            badges.Add(new InlineOpBadge($"{call.FunctionName}()", freqLabel, "Function call"));
        }
        var total = SymbolicExpr.Constant(cost);
        ctx.AddLine(call.Id, indent, $"{call.FunctionName}({call.Args});", badges, total.ToLatexString(), boxId);
        return total;
    }

    private static SymbolicExpr AnalyzeRecursiveCallStub(
        RecursiveCallStatement rec,
        AnalysisContext ctx,
        int indent,
        string? boxId)
    {
        var code = rec.Branches == 1
            ? $"recurse({rec.Subproblem});"
            : $"recurse {rec.Branches}x ({rec.Subproblem});";
        var badge = new InlineOpBadge($"T({rec.Subproblem})", $"{rec.Branches}x", "Recursive subproblem call");
        ctx.AddLine(rec.Id, indent, code, new[] { badge }, $@"{rec.Branches} T({rec.Subproblem})", boxId);
        return SymbolicExpr.Zero;
    }

    private static SymbolicExpr AnalyzeIf(
        IfStatement ifStmt,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        var badges = new List<InlineOpBadge>();
        var condOps = CountExprOps(ifStmt.Condition, ctx.Profile, badges, freqLabel);
        var condExpr = SymbolicExpr.Constant(condOps);

        ctx.AddLine(ifStmt.Id, indent, $"if ({FormatExpr(ifStmt.Condition)}) {{", badges, condExpr.ToLatexString(), boxId);

        var thenCost = AnalyzeBlock(ifStmt.ThenBody, ctx, indent + 1, freqLabel, boxId);

        SymbolicExpr branchCost;
        if (ifStmt.ElseBody.Count > 0)
        {
            ctx.AddLine(null, indent, "} else {", Array.Empty<InlineOpBadge>(), "0", boxId);
            var elseCost = AnalyzeBlock(ifStmt.ElseBody, ctx, indent + 1, freqLabel, boxId);
            ctx.AddLine(null, indent, "}", Array.Empty<InlineOpBadge>(), "0", boxId);

            branchCost = ctx.IsWorstCase
                ? SymbolicExpr.Max(thenCost, elseCost)
                : SymbolicExpr.Min(thenCost, elseCost);
        }
        else
        {
            ctx.AddLine(null, indent, "}", Array.Empty<InlineOpBadge>(), "0", boxId);
            branchCost = ctx.IsWorstCase ? thenCost : SymbolicExpr.Zero;
        }

        return condExpr + branchCost;
    }

    private static SymbolicExpr AnalyzeSwitch(
        SwitchStatement sw,
        AnalysisContext ctx,
        int indent,
        string freqLabel,
        string? boxId)
    {
        ctx.AddLine(sw.Id, indent, $"switch ({sw.Variable}) {{", Array.Empty<InlineOpBadge>(), "0", boxId);

        var cmpCost = ctx.Profile.SwitchCaseComparisonCost;
        var outcomes = new List<SymbolicExpr>();
        var caseIndex = 0;
        var hasDefault = false;

        foreach (var c in sw.Cases)
        {
            if (c.IsDefault)
            {
                hasDefault = true;
                ctx.AddLine(null, indent + 1, "default:", Array.Empty<InlineOpBadge>(), "0", boxId);
                var bodyCost = AnalyzeBlock(c.Body, ctx, indent + 2, freqLabel, boxId);
                outcomes.Add(SymbolicExpr.Constant(caseIndex * cmpCost) + bodyCost);
            }
            else
            {
                caseIndex++;
                var badge = new InlineOpBadge($"case {c.Label}", $"{caseIndex} cmp", "Switch comparison");
                ctx.AddLine(null, indent + 1, $"case {c.Label}:", new[] { badge }, cmpCost.ToString(), boxId);
                var bodyCost = AnalyzeBlock(c.Body, ctx, indent + 2, freqLabel, boxId);
                outcomes.Add(SymbolicExpr.Constant(caseIndex * cmpCost) + bodyCost);
            }
        }

        if (!hasDefault)
        {
            outcomes.Add(SymbolicExpr.Constant(caseIndex * cmpCost));
        }

        ctx.AddLine(null, indent, "}", Array.Empty<InlineOpBadge>(), "0", boxId);

        if (outcomes.Count == 0) return SymbolicExpr.Zero;

        var chosen = outcomes[0];
        for (var i = 1; i < outcomes.Count; i++)
        {
            chosen = ctx.IsWorstCase
                ? SymbolicExpr.Max(chosen, outcomes[i])
                : SymbolicExpr.Min(chosen, outcomes[i]);
        }
        return chosen;
    }

    private static SymbolicExpr AnalyzeFor(
        ForStatement forStmt,
        AnalysisContext ctx,
        int indent,
        string outerFreqLabel,
        string? enclosingBoxId)
    {
        var startExpr = SymbolicParser.Parse(forStmt.StartExpr);
        var boundExpr = SymbolicParser.Parse(forStmt.BoundExpr);

        var (lowerExpr, upperExpr) = ResolveInclusiveBounds(startExpr, forStmt.Comparator, boundExpr);
        var isMultiplicative = forStmt.StepOp is "*=" or "/=";
        var isUnaryStep = forStmt.StepOp is "++" or "--";

        var stepCost = isUnaryStep
            ? ctx.Profile.UnaryIncrementCost
            : ctx.Profile.CompoundAssignCost;

        var boundArithOps = ctx.Profile.CountLoopBoundArithmetic
            ? SymbolicParser.CountArithmeticOps(forStmt.BoundExpr)
            : 0;
        var condOpCost = 1 + boundArithOps;
        const int initCost = 1;

        var stepNumeric = ParsePositiveStep(forStmt.StepValue, isMultiplicative ? 2 : 1);

        // Compute symbolic iteration count I and condition checks I + 1
        SymbolicExpr iterations;
        if (isMultiplicative)
        {
            var logExpr = SymbolicExpr.LogOf(upperExpr, stepNumeric);
            iterations = ctx.Profile.LogLoopConvention == LogLoopConvention.ExactLogBase
                ? logExpr
                : logExpr + SymbolicExpr.One;
        }
        else if (stepNumeric > 1)
        {
            iterations = ctx.Profile.StepLoopConvention == StepLoopConvention.AlgebraicRatio
                ? (upperExpr - lowerExpr + SymbolicExpr.One) / BigRational.FromInt(stepNumeric)
                : (upperExpr - lowerExpr) / BigRational.FromInt(stepNumeric) + SymbolicExpr.One;
        }
        else
        {
            iterations = upperExpr - lowerExpr + SymbolicExpr.One;
        }

        var condChecks = iterations + SymbolicExpr.One;

        // Track nesting depth & inner-loop box metadata
        var isInnerLoop = ctx.LoopDepth > 0;
        var outerIterator = ctx.LoopIterators.Count > 0 ? ctx.LoopIterators.Peek() : null;
        string? myBoxId = enclosingBoxId;
        var myDepth = ctx.LoopDepth;
        if (isInnerLoop)
        {
            myBoxId = ctx.NextBoxId();
        }

        // Format header text
        var stepText = isUnaryStep
            ? $"{forStmt.Iterator}{forStmt.StepOp}"
            : $"{forStmt.Iterator} {forStmt.StepOp} {forStmt.StepValue}";
        var headerCode = $"for ({forStmt.Iterator} = {forStmt.StartExpr}; {forStmt.Iterator} {forStmt.Comparator} {forStmt.BoundExpr}; {stepText}) {{";

        var hasEarlyBreak = TryFindDirectBreakPath(forStmt.Body, out var breakIndex, out var breakStmt);

        var headerLineIndex = ctx.Lines.Count;
        var headerBadges = BuildForHeaderBadges(
            forStmt,
            condOpCost,
            stepCost,
            condChecks,
            iterations,
            hasEarlyBreak && !ctx.IsWorstCase);

        // Reserve header line (updated with exact cost + BoxHeaderLabel after body evaluation)
        ctx.AddLine(forStmt.Id, indent, headerCode, headerBadges, string.Empty, myBoxId);

        ctx.LoopDepth++;
        ctx.LoopIterators.Push(forStmt.Iterator);

        SymbolicExpr loopTotal;
        if (hasEarlyBreak)
        {
            loopTotal = AnalyzeForWithBreak(
                forStmt,
                ctx,
                indent,
                myBoxId,
                lowerExpr,
                upperExpr,
                iterations,
                condChecks,
                initCost,
                condOpCost,
                stepCost,
                isMultiplicative,
                stepNumeric,
                breakIndex,
                breakStmt);
        }
        else
        {
            var bodyFreqLabel = iterations.ToPlainString();
            var bodyPerIter = AnalyzeBlock(forStmt.Body, ctx, indent + 1, bodyFreqLabel, myBoxId);

            SummationResult bodySumResult;
            if (isMultiplicative)
            {
                bodySumResult = SummationEngine.SumMultiplicative(
                    bodyPerIter,
                    forStmt.Iterator,
                    upperExpr,
                    stepNumeric,
                    ctx.Profile.LogLoopConvention,
                    forStmt.Id);
            }
            else
            {
                bodySumResult = SummationEngine.Sum(
                    bodyPerIter,
                    forStmt.Iterator,
                    lowerExpr,
                    upperExpr,
                    stepNumeric,
                    ctx.Profile.StepLoopConvention,
                    forStmt.Id);
            }

            ctx.Steps.AddRange(bodySumResult.Steps);

            var headerCost = SymbolicExpr.Constant(initCost)
                             + SymbolicExpr.Constant(condOpCost) * condChecks
                             + SymbolicExpr.Constant(stepCost) * iterations;

            loopTotal = headerCost + bodySumResult.Result;

            var loopLabel = isInnerLoop
                ? BuildInnerBoxName(myDepth, outerIterator, loopTotal)
                : $"Loop ({forStmt.Iterator})";

            ctx.Steps.Add(new DerivationStep(
                loopLabel,
                $@"{FormatBoxNameLatex(myDepth, outerIterator, loopTotal, isInnerLoop)} = {initCost} + {FormatScaledLatex(condOpCost, condChecks)} + {FormatScaledLatex(stepCost, iterations)} + \left({bodySumResult.Result.ToLatexString()}\right) = {loopTotal.ToLatexString()}",
                $"{loopLabel} = {loopTotal.ToPlainString()}",
                forStmt.Id));
        }

        ctx.LoopIterators.Pop();
        ctx.LoopDepth--;

        ctx.AddLine(null, indent, "}", Array.Empty<InlineOpBadge>(), "0", myBoxId);

        // Update header line with exact total cost and BoxHeaderLabel for nested inner loops
        string? boxHeaderLabel = null;
        if (isInnerLoop && myBoxId is not null)
        {
            var boxNamePlain = BuildInnerBoxName(myDepth, outerIterator, loopTotal);
            var boxNameLatex = FormatBoxNameLatex(myDepth, outerIterator, loopTotal, isInnerLoop: true);
            boxHeaderLabel = $"{boxNamePlain} = {loopTotal.ToPlainString()}";

            ctx.InnerBoxes.Add(new InnerLoopBox(
                BoxGroupId: myBoxId,
                Label: boxNamePlain,
                Latex: $"{boxNameLatex} = {loopTotal.ToLatexString()}",
                Plain: boxHeaderLabel,
                Expression: loopTotal,
                NestingDepth: myDepth,
                BlockId: forStmt.Id));
        }

        var existingHeader = ctx.Lines[headerLineIndex];
        ctx.Lines[headerLineIndex] = existingHeader with
        {
            LineCostLatex = loopTotal.ToLatexString(),
            BoxHeaderLabel = boxHeaderLabel
        };

        return loopTotal;
    }

    private static SymbolicExpr AnalyzeForWithBreak(
        ForStatement forStmt,
        AnalysisContext ctx,
        int indent,
        string? myBoxId,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr,
        SymbolicExpr iterations,
        SymbolicExpr condChecks,
        int initCost,
        int condOpCost,
        int stepCost,
        bool isMultiplicative,
        int stepNumeric,
        int breakIndex,
        AlgorithmStatement breakStmt)
    {
        // Emit annotated lines for body statements while computing pre-break, break-if, and post-break costs
        var preStmts = forStmt.Body.Take(breakIndex).ToList();
        var postStmts = forStmt.Body.Skip(breakIndex + 1).ToList();

        var freqLabel = ctx.IsWorstCase ? iterations.ToPlainString() : "1";
        var preCost = AnalyzeBlock(preStmts, ctx, indent + 1, freqLabel, myBoxId);

        // Case A: Direct unconditional break inside loop body
        if (breakStmt is BreakStatement brk)
        {
            ctx.AddLine(brk.Id, indent + 1, "break;", Array.Empty<InlineOpBadge>(), "0", myBoxId);
            // Statements after unconditional break are unreachable, but list them with cost 0
            foreach (var post in postStmts)
            {
                AnalyzeStatement(post, ctx, indent + 1, "0", myBoxId);
            }

            var preAtFirst = preCost.Substitute(forStmt.Iterator, lowerExpr);
            var oneIter = SymbolicExpr.Constant(initCost) + SymbolicExpr.Constant(condOpCost) + preAtFirst;
            ctx.Steps.Add(new DerivationStep(
                "Immediate Break Loop",
                $@"T_{{\text{{loop}}}} = {initCost} + {condOpCost} + {preAtFirst.ToLatexString()} = {oneIter.ToLatexString()}",
                $"Loop exits immediately on 1st iteration = {oneIter.ToPlainString()}",
                forStmt.Id));
            return oneIter;
        }

        // Case B: Conditional early exit: if (cond) { ... break; } [else { ... }]
        var ifStmt = (IfStatement)breakStmt;
        var ifBadges = new List<InlineOpBadge>();
        var ifCondOps = CountExprOps(ifStmt.Condition, ctx.Profile, ifBadges, freqLabel);
        var ifCondExpr = SymbolicExpr.Constant(ifCondOps);

        ctx.AddLine(ifStmt.Id, indent + 1, $"if ({FormatExpr(ifStmt.Condition)}) {{", ifBadges, ifCondExpr.ToLatexString(), myBoxId);
        var thenBreakFreq = ctx.IsWorstCase ? "0 or 1" : "1";
        var thenBreakCost = AnalyzeBlock(ifStmt.ThenBody, ctx, indent + 2, thenBreakFreq, myBoxId);

        SymbolicExpr elseCost = SymbolicExpr.Zero;
        if (ifStmt.ElseBody.Count > 0)
        {
            ctx.AddLine(null, indent + 1, "} else {", Array.Empty<InlineOpBadge>(), "0", myBoxId);
            elseCost = AnalyzeBlock(ifStmt.ElseBody, ctx, indent + 2, freqLabel, myBoxId);
        }
        ctx.AddLine(null, indent + 1, "}", Array.Empty<InlineOpBadge>(), "0", myBoxId);

        var postCost = AnalyzeBlock(postStmts, ctx, indent + 1, ctx.IsWorstCase ? iterations.ToPlainString() : "0", myBoxId);

        if (!ctx.IsWorstCase)
        {
            // Best Case: breaks on the very first iteration (i = L), skipping post-statements, step, and remaining checks
            var firstIterBody = (preCost + ifCondExpr + thenBreakCost).Substitute(forStmt.Iterator, lowerExpr);
            var breakFirstTotal = SymbolicExpr.Constant(initCost) + SymbolicExpr.Constant(condOpCost) + firstIterBody;

            // Compare against running all iterations without ever breaking (in case I could be 0 or smaller)
            var noBreakPerIter = preCost + ifCondExpr + elseCost + postCost;
            var noBreakSum = isMultiplicative
                ? SummationEngine.SumMultiplicative(noBreakPerIter, forStmt.Iterator, upperExpr, stepNumeric, ctx.Profile.LogLoopConvention).Result
                : SummationEngine.Sum(noBreakPerIter, forStmt.Iterator, lowerExpr, upperExpr, stepNumeric, ctx.Profile.StepLoopConvention).Result;
            var noBreakTotal = SymbolicExpr.Constant(initCost)
                               + SymbolicExpr.Constant(condOpCost) * condChecks
                               + SymbolicExpr.Constant(stepCost) * iterations
                               + noBreakSum;

            var bestTotal = SymbolicExpr.Min(breakFirstTotal, noBreakTotal);

            ctx.Steps.Add(new DerivationStep(
                "Best Case (1st-Iteration Break)",
                $@"T_{{\text{{best,loop}}}} = {initCost} + {condOpCost} + {firstIterBody.ToLatexString()} = {bestTotal.ToLatexString()}",
                $"Best case breaks on 1st iteration: {bestTotal.ToPlainString()}",
                forStmt.Id));
            return bestTotal;
        }
        else
        {
            // Worst Case: compare (1) never breaking across all I iterations vs (2) breaking on the last iteration
            var noBreakPerIter = preCost + ifCondExpr + elseCost + postCost;
            var noBreakSumRes = isMultiplicative
                ? SummationEngine.SumMultiplicative(noBreakPerIter, forStmt.Iterator, upperExpr, stepNumeric, ctx.Profile.LogLoopConvention, forStmt.Id)
                : SummationEngine.Sum(noBreakPerIter, forStmt.Iterator, lowerExpr, upperExpr, stepNumeric, ctx.Profile.StepLoopConvention, forStmt.Id);
            ctx.Steps.AddRange(noBreakSumRes.Steps);

            var pathNoBreak = SymbolicExpr.Constant(initCost)
                              + SymbolicExpr.Constant(condOpCost) * condChecks
                              + SymbolicExpr.Constant(stepCost) * iterations
                              + noBreakSumRes.Result;

            // Path 2: I - 1 non-breaking iterations + final iteration enters thenBreakCost (skips final step & final false cond)
            SymbolicExpr pathLastBreak;
            if (!isMultiplicative && stepNumeric == 1)
            {
                var prevIterationsSum = SummationEngine.Sum(
                    noBreakPerIter,
                    forStmt.Iterator,
                    lowerExpr,
                    upperExpr - SymbolicExpr.One,
                    1,
                    ctx.Profile.StepLoopConvention).Result;
                var lastIterBody = (preCost + ifCondExpr + thenBreakCost).Substitute(forStmt.Iterator, upperExpr);
                pathLastBreak = SymbolicExpr.Constant(initCost)
                                + SymbolicExpr.Constant(condOpCost) * iterations
                                + SymbolicExpr.Constant(stepCost) * (iterations - SymbolicExpr.One)
                                + prevIterationsSum
                                + lastIterBody;
            }
            else
            {
                var lastIterDelta = (preCost + ifCondExpr + thenBreakCost - noBreakPerIter).Substitute(forStmt.Iterator, upperExpr);
                pathLastBreak = pathNoBreak
                                - SymbolicExpr.Constant(condOpCost)
                                - SymbolicExpr.Constant(stepCost)
                                + lastIterDelta;
            }

            var worstTotal = SymbolicExpr.Max(pathNoBreak, pathLastBreak);

            ctx.Steps.Add(new DerivationStep(
                "Worst Case (No Early Break)",
                $@"T_{{\text{{worst,loop}}}} = {initCost} + {FormatScaledLatex(condOpCost, condChecks)} + {FormatScaledLatex(stepCost, iterations)} + {noBreakSumRes.Result.ToLatexString()} = {worstTotal.ToLatexString()}",
                $"Worst case runs full loop: {worstTotal.ToPlainString()}",
                forStmt.Id));
            return worstTotal;
        }
    }

    private static SymbolicExpr AnalyzeWhile(
        WhileStatement whileStmt,
        AnalysisContext ctx,
        int indent,
        string outerFreqLabel,
        string? enclosingBoxId)
    {
        var iterations = SymbolicParser.Parse(whileStmt.IterationsExpr);
        var condChecks = iterations + SymbolicExpr.One;

        var condBadges = new List<InlineOpBadge>();
        var condOps = Math.Max(1, CountExprOps(whileStmt.Condition, ctx.Profile, condBadges, condChecks.ToPlainString()));

        var headerLineIndex = ctx.Lines.Count;
        ctx.AddLine(whileStmt.Id, indent, $"while ({FormatExpr(whileStmt.Condition)}) {{", condBadges, string.Empty, enclosingBoxId);

        ctx.LoopDepth++;
        var hasBreak = TryFindDirectBreakPath(whileStmt.Body, out var breakIndex, out var breakStmt);
        SymbolicExpr loopTotal;

        if (hasBreak && breakStmt is IfStatement ifStmt)
        {
            var pre = AnalyzeBlock(whileStmt.Body.Take(breakIndex).ToList(), ctx, indent + 1,
                ctx.IsWorstCase ? iterations.ToPlainString() : "1", enclosingBoxId);
            var ifBadges = new List<InlineOpBadge>();
            var ifCondCost = SymbolicExpr.Constant(CountExprOps(ifStmt.Condition, ctx.Profile, ifBadges,
                ctx.IsWorstCase ? iterations.ToPlainString() : "1"));
            ctx.AddLine(ifStmt.Id, indent + 1, $"if ({FormatExpr(ifStmt.Condition)}) {{", ifBadges, ifCondCost.ToLatexString(), enclosingBoxId);
            var thenCost = AnalyzeBlock(ifStmt.ThenBody, ctx, indent + 2, "1", enclosingBoxId);
            var elseCost = SymbolicExpr.Zero;
            if (ifStmt.ElseBody.Count > 0)
            {
                ctx.AddLine(null, indent + 1, "} else {", Array.Empty<InlineOpBadge>(), "0", enclosingBoxId);
                elseCost = AnalyzeBlock(ifStmt.ElseBody, ctx, indent + 2, iterations.ToPlainString(), enclosingBoxId);
            }
            ctx.AddLine(null, indent + 1, "}", Array.Empty<InlineOpBadge>(), "0", enclosingBoxId);
            var post = AnalyzeBlock(whileStmt.Body.Skip(breakIndex + 1).ToList(), ctx, indent + 1,
                ctx.IsWorstCase ? iterations.ToPlainString() : "0", enclosingBoxId);

            if (!ctx.IsWorstCase)
            {
                loopTotal = SymbolicExpr.Constant(condOps) + pre + ifCondCost + thenCost;
            }
            else
            {
                var noBreak = SymbolicExpr.Constant(condOps) * condChecks + (pre + ifCondCost + elseCost + post) * iterations;
                var lastBreak = SymbolicExpr.Constant(condOps) * iterations + (pre + ifCondCost + elseCost + post) * (iterations - SymbolicExpr.One) + pre + ifCondCost + thenCost;
                loopTotal = SymbolicExpr.Max(noBreak, lastBreak);
            }
        }
        else
        {
            var bodyPerIter = AnalyzeBlock(whileStmt.Body, ctx, indent + 1, iterations.ToPlainString(), enclosingBoxId);
            loopTotal = SymbolicExpr.Constant(condOps) * condChecks + bodyPerIter * iterations;
        }

        ctx.LoopDepth--;
        ctx.AddLine(null, indent, "}", Array.Empty<InlineOpBadge>(), "0", enclosingBoxId);

        ctx.Lines[headerLineIndex] = ctx.Lines[headerLineIndex] with { LineCostLatex = loopTotal.ToLatexString() };
        ctx.Steps.Add(new DerivationStep(
            "While Loop",
            $@"T_{{\text{{while}}}} = {FormatScaledLatex(condOps, condChecks)} + \text{{body}} = {loopTotal.ToLatexString()}",
            $"While loop = {loopTotal.ToPlainString()}",
            whileStmt.Id));

        return loopTotal;
    }

    private static bool TryFindDirectBreakPath(
        IReadOnlyList<AlgorithmStatement> body,
        out int index,
        out AlgorithmStatement breakHolder)
    {
        for (var i = 0; i < body.Count; i++)
        {
            var stmt = body[i];
            if (stmt is BreakStatement)
            {
                index = i;
                breakHolder = stmt;
                return true;
            }
            if (stmt is IfStatement ifStmt && ContainsDirectBreak(ifStmt.ThenBody))
            {
                index = i;
                breakHolder = ifStmt;
                return true;
            }
        }
        index = -1;
        breakHolder = null!;
        return false;
    }

    private static bool ContainsDirectBreak(IReadOnlyList<AlgorithmStatement> stmts)
    {
        foreach (var s in stmts)
        {
            if (s is BreakStatement) return true;
            if (s is IfStatement innerIf && (ContainsDirectBreak(innerIf.ThenBody) || ContainsDirectBreak(innerIf.ElseBody)))
                return true;
        }
        return false;
    }

    private static bool ContainsRecursiveCall(IReadOnlyList<AlgorithmStatement> stmts)
    {
        foreach (var s in stmts)
        {
            switch (s)
            {
                case RecursiveCallStatement:
                    return true;
                case ForStatement f when ContainsRecursiveCall(f.Body):
                    return true;
                case WhileStatement w when ContainsRecursiveCall(w.Body):
                    return true;
                case IfStatement i when ContainsRecursiveCall(i.ThenBody) || ContainsRecursiveCall(i.ElseBody):
                    return true;
                case SwitchStatement sw when sw.Cases.Any(c => ContainsRecursiveCall(c.Body)):
                    return true;
            }
        }
        return false;
    }

    private static (SymbolicExpr Lower, SymbolicExpr Upper) ResolveInclusiveBounds(
        SymbolicExpr start,
        string comparator,
        SymbolicExpr bound)
    {
        return comparator switch
        {
            "<=" => (start, bound),
            "<" => (start, bound - SymbolicExpr.One),
            ">=" => (bound, start),
            ">" => (bound + SymbolicExpr.One, start),
            _ => (start, bound)
        };
    }

    private static int ParsePositiveStep(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var parsed = SymbolicParser.Parse(text);
        if (parsed.IsConstant && parsed.ConstantValue.IsInteger && parsed.ConstantValue.Numerator >= 1)
        {
            return (int)parsed.ConstantValue.Numerator;
        }
        return fallback;
    }

    private static IReadOnlyList<InlineOpBadge> BuildForHeaderBadges(
        ForStatement f,
        int condOpCost,
        int stepCost,
        SymbolicExpr condChecks,
        SymbolicExpr iterations,
        bool bestCaseBreak)
    {
        var badges = new List<InlineOpBadge>
        {
            new("=", "1", "Loop initialization"),
            new(f.Comparator, bestCaseBreak ? "1" : condChecks.ToPlainString(),
                condOpCost > 1 ? $"Condition + bound arithmetic (cost {condOpCost}/check)" : "Loop condition"),
            new(f.StepOp, bestCaseBreak ? "0" : iterations.ToPlainString(),
                $"Loop step (cost {stepCost}/iter)")
        };
        return badges;
    }

    private static string BuildInnerBoxName(int depth, string? outerVar, SymbolicExpr expr)
    {
        var prefix = depth <= 1 ? "T_in" : $"T_in{depth}";
        if (outerVar is not null && expr.ContainsVariable(outerVar))
        {
            return $"{prefix}({outerVar})";
        }
        return prefix;
    }

    private static string FormatBoxNameLatex(int depth, string? outerVar, SymbolicExpr expr, bool isInnerLoop)
    {
        if (!isInnerLoop) return @"T_{\text{loop}}";
        var sub = depth <= 1 ? "in" : $"in{depth}";
        if (outerVar is not null && expr.ContainsVariable(outerVar))
        {
            return $@"T_{{{sub}}}({outerVar})";
        }
        return $@"T_{{{sub}}}";
    }

    private static string FormatScaledLatex(int scalar, SymbolicExpr expr)
    {
        if (scalar == 0) return "0";
        if (scalar == 1) return expr.Terms.Count > 1 ? $@"({expr.ToLatexString()})" : expr.ToLatexString();
        return $@"{scalar}({expr.ToLatexString()})";
    }

    internal static int CountExprOps(
        AlgorithmExpr? expr,
        CostRulesProfile profile,
        List<InlineOpBadge>? badges,
        string freqLabel)
    {
        if (expr is null) return 0;

        switch (expr)
        {
            case LiteralExpr:
            case VariableExpr:
                return 0;

            case ArrayAccessExpr arr:
            {
                var cost = profile.ArrayIndexingCost;
                if (cost > 0)
                {
                    badges?.Add(new InlineOpBadge($"{arr.ArrayName}[{arr.IndexExpr}]", freqLabel, "Array indexing"));
                }
                var idxOps = SymbolicParser.CountArithmeticOps(arr.IndexExpr);
                if (idxOps > 0)
                {
                    badges?.Add(new InlineOpBadge(arr.IndexExpr, freqLabel, $"Index arithmetic ({idxOps} ops)"));
                }
                return cost + idxOps;
            }

            case BinaryExpr bin:
            {
                var left = CountExprOps(bin.Left, profile, badges, freqLabel);
                var right = CountExprOps(bin.Right, profile, badges, freqLabel);
                var opCost = bin.Op is "&&" or "||" ? profile.LogicalOperatorCost : 1;
                if (opCost > 0)
                {
                    badges?.Add(new InlineOpBadge(bin.Op, freqLabel, "Binary operator"));
                }
                return left + right + opCost;
            }

            default:
                return 0;
        }
    }

    internal static string FormatExpr(AlgorithmExpr? expr)
    {
        return expr switch
        {
            null => "0",
            LiteralExpr lit => lit.Value,
            VariableExpr v => v.Name,
            ArrayAccessExpr arr => $"{arr.ArrayName}[{arr.IndexExpr}]",
            BinaryExpr bin => $"{FormatExpr(bin.Left)} {bin.Op} {FormatExpr(bin.Right)}",
            _ => string.Empty
        };
    }

    private static string DetectPrimaryVariable(
        AlgorithmFunction fn,
        SymbolicExpr worst,
        SymbolicExpr best)
    {
        var worstVars = worst.GetVariables();
        if (worstVars.Contains("n")) return "n";
        if (worstVars.Count > 0) return worstVars[0];

        var bestVars = best.GetVariables();
        if (bestVars.Contains("n")) return "n";
        if (bestVars.Count > 0) return bestVars[0];

        if (fn.Parameters.Any(p => p.Equals("n", StringComparison.Ordinal))) return "n";
        return "n";
    }

    private sealed class AnalysisContext
    {
        private int _boxCounter;

        public CostRulesProfile Profile { get; }
        public bool IsWorstCase { get; }
        public int LoopDepth { get; set; }
        public Stack<string> LoopIterators { get; } = new();
        public List<DerivationStep> Steps { get; } = new();
        public List<AnnotatedCodeLine> Lines { get; } = new();
        public List<InnerLoopBox> InnerBoxes { get; } = new();

        public AnalysisContext(CostRulesProfile profile, bool isWorstCase)
        {
            Profile = profile;
            IsWorstCase = isWorstCase;
        }

        public string NextBoxId() => $"box_{++_boxCounter}";

        public void AddLine(
            string? blockId,
            int indent,
            string codeText,
            IReadOnlyList<InlineOpBadge> badges,
            string lineCostLatex,
            string? boxGroupId = null,
            string? boxHeaderLabel = null)
        {
            Lines.Add(new AnnotatedCodeLine(
                LineNumber: Lines.Count + 1,
                BlockId: blockId,
                IndentLevel: indent,
                CodeText: codeText,
                InlineBadges: badges,
                LineCostLatex: lineCostLatex,
                BoxGroupId: boxGroupId,
                BoxHeaderLabel: boxHeaderLabel));
        }
    }
}
