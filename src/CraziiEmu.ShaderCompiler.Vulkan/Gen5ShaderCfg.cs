// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Generic;
using System.Linq;
using CraziiEmu.ShaderCompiler;

namespace CraziiEmu.ShaderCompiler.Vulkan;

public enum Gen5CfgBranchCondition
{
    Always,
    SccZero,
    SccNonZero,
    VccZero,
    VccNonZero,
    ExecZero,
    ExecNonZero,
    ScalarInstruction,
    Unknown,
}

public enum Gen5CfgTerminatorKind
{
    Branch,
    ConditionalBranch,
    Return,
    Unsupported,
}

public sealed class Gen5CfgTerminator
{
    public Gen5CfgTerminatorKind Kind { get; set; } = Gen5CfgTerminatorKind.Return;
    public Gen5CfgBranchCondition Condition { get; set; } = Gen5CfgBranchCondition.Always;
    public int TrueBlock { get; set; } = -1;
    public int FalseBlock { get; set; } = -1;
    public int MergeBlock { get; set; } = -1;
    public int ContinueBlock { get; set; } = -1;
    public bool IsLoopHeader { get; set; }
}

public sealed class Gen5CfgBlock
{
    public int Id { get; set; }
    public uint StartPc { get; set; }
    public uint EndPc { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public List<int> Predecessors { get; } = [];
    public List<int> Successors { get; } = [];
    public HashSet<int> Dominators { get; set; } = [];
    public HashSet<int> PostDominators { get; set; } = [];
    public Gen5CfgTerminator Terminator { get; } = new();

    public override string ToString() =>
        $"Block {Id} [0x{StartPc:X}-0x{EndPc:X}] Succ={string.Join(",", Successors)} Pred={string.Join(",", Predecessors)} Term={Terminator.Kind}";
}

public readonly record struct Gen5CfgBackEdge(int From, int To, bool Natural);

public sealed class Gen5CfgNaturalLoop
{
    public int Header { get; set; } = -1;
    public int Latch { get; set; } = -1;
    public int Merge { get; set; } = -1;
    public int ContinueBlock { get; set; } = -1;
    public HashSet<int> BodyBlocks { get; } = [];
    public HashSet<int> ExitBlocks { get; } = [];
}

public sealed class Gen5CfgGraph
{
    public List<Gen5CfgBlock> Blocks { get; } = [];
    public List<Gen5CfgBackEdge> BackEdges { get; } = [];
    public List<Gen5CfgNaturalLoop> NaturalLoops { get; } = [];
    public int EntryBlock { get; set; }
    public bool IsStructured { get; set; }
    public string FailureReason { get; set; } = string.Empty;

    public Gen5CfgBlock? FindBlock(int id) =>
        id >= 0 && id < Blocks.Count ? Blocks[id] : null;

    public bool Dominates(int dominator, int block) =>
        FindBlock(block)?.Dominators.Contains(dominator) ?? false;

    public bool PostDominates(int postDominator, int block) =>
        FindBlock(block)?.PostDominators.Contains(postDominator) ?? false;

    public int FindNearestCommonPostDominator(int blockA, int blockB)
    {
        var bA = FindBlock(blockA);
        var bB = FindBlock(blockB);
        if (bA == null || bB == null)
        {
            return -1;
        }

        // Common post-dominators are intersection of PostDominators(A) and PostDominators(B)
        var common = new HashSet<int>(bA.PostDominators);
        common.IntersectWith(bB.PostDominators);
        if (common.Count == 0)
        {
            return -1;
        }

        // Among common post-dominators, the nearest is the one dominated by all other candidates
        int best = -1;
        foreach (var candidate in common)
        {
            if (best == -1)
            {
                best = candidate;
                continue;
            }

            // If candidate is post-dominated by best, then candidate comes BEFORE best
            if (PostDominates(best, candidate))
            {
                best = candidate;
            }
        }

        return best;
    }
}

public static class Gen5ShaderCfg
{
    public static bool TryBuildAndStructurize(
        IReadOnlyList<Gen5ShaderInstruction> instructions,
        out Gen5CfgGraph graph)
    {
        graph = BuildGraph(instructions);
        if (graph.Blocks.Count == 0)
        {
            graph.FailureReason = "Shader has no instructions";
            return false;
        }

        if (!Structurize(graph))
        {
            return false;
        }

        graph.IsStructured = true;
        return true;
    }

    public static Gen5CfgGraph BuildGraph(IReadOnlyList<Gen5ShaderInstruction> instructions)
    {
        var graph = new Gen5CfgGraph();
        if (instructions.Count == 0)
        {
            return graph;
        }

        // 1. Identify basic block leaders
        var leaders = new SortedSet<uint> { instructions[0].Pc };
        for (var i = 0; i < instructions.Count; i++)
        {
            var inst = instructions[i];
            if (IsBranch(inst.Opcode) && TryGetBranchTargetPc(inst, out var targetPc))
            {
                leaders.Add(targetPc);
            }

            if ((IsBranch(inst.Opcode) || inst.Opcode == "SEndpgm") && i + 1 < instructions.Count)
            {
                leaders.Add(instructions[i + 1].Pc);
            }
        }

        var validStarts = leaders
            .Where(pc => instructions.Any(inst => inst.Pc == pc))
            .ToArray();

        // 2. Build initial basic blocks
        for (var i = 0; i < validStarts.Length; i++)
        {
            var startIndex = FindInstructionIndex(instructions, validStarts[i]);
            var endIndex = i + 1 < validStarts.Length
                ? FindInstructionIndex(instructions, validStarts[i + 1])
                : instructions.Count;

            if (startIndex >= 0 && endIndex > startIndex)
            {
                var block = new Gen5CfgBlock
                {
                    Id = graph.Blocks.Count,
                    StartPc = validStarts[i],
                    EndPc = instructions[endIndex - 1].Pc + (uint)(instructions[endIndex - 1].Words.Count * sizeof(uint)),
                    StartIndex = startIndex,
                    EndIndex = endIndex,
                };
                graph.Blocks.Add(block);
            }
        }

        // Helper to find block by start PC
        int FindBlockIndexByPc(uint pc)
        {
            for (var b = 0; b < graph.Blocks.Count; b++)
            {
                if (graph.Blocks[b].StartPc == pc)
                {
                    return b;
                }
            }
            return -1;
        }

        // 3. Connect successors and terminators
        for (var i = 0; i < graph.Blocks.Count; i++)
        {
            var block = graph.Blocks[i];
            var terminator = instructions[block.EndIndex - 1];
            var hasFallthrough = i + 1 < graph.Blocks.Count;
            var fallthroughId = hasFallthrough ? i + 1 : -1;

            if (terminator.Opcode == "SEndpgm")
            {
                block.Terminator.Kind = Gen5CfgTerminatorKind.Return;
                continue;
            }

            if (terminator.Opcode == "SBranch")
            {
                if (TryGetBranchTargetPc(terminator, out var targetPc))
                {
                    if (IsExitBranchTarget(instructions, targetPc))
                    {
                        block.Terminator.Kind = Gen5CfgTerminatorKind.Return;
                    }
                    else
                    {
                        var targetBlock = FindBlockIndexByPc(targetPc);
                        if (targetBlock >= 0)
                        {
                            block.Terminator.Kind = Gen5CfgTerminatorKind.Branch;
                            block.Terminator.TrueBlock = targetBlock;
                            block.Successors.Add(targetBlock);
                        }
                        else
                        {
                            block.Terminator.Kind = Gen5CfgTerminatorKind.Unsupported;
                        }
                    }
                }
                else
                {
                    block.Terminator.Kind = Gen5CfgTerminatorKind.Unsupported;
                }
                continue;
            }

            if (terminator.Opcode.StartsWith("SCbranch", StringComparison.Ordinal))
            {
                var cond = MapBranchCondition(terminator.Opcode);
                var hasTarget = TryGetBranchTargetPc(terminator, out var targetPc);
                var targetBlock = hasTarget ? FindBlockIndexByPc(targetPc) : -1;
                var targetExits = hasTarget && IsExitBranchTarget(instructions, targetPc);

                if (cond != Gen5CfgBranchCondition.Unknown && (targetBlock >= 0 || targetExits))
                {
                    block.Terminator.Kind = Gen5CfgTerminatorKind.ConditionalBranch;
                    block.Terminator.Condition = cond;
                    block.Terminator.TrueBlock = targetExits ? -1 : targetBlock;
                    block.Terminator.FalseBlock = fallthroughId;

                    if (targetBlock >= 0)
                    {
                        block.Successors.Add(targetBlock);
                    }
                    if (fallthroughId >= 0)
                    {
                        block.Successors.Add(fallthroughId);
                    }
                }
                else
                {
                    block.Terminator.Kind = Gen5CfgTerminatorKind.Unsupported;
                }
                continue;
            }

            // Normal fallthrough
            if (fallthroughId >= 0)
            {
                block.Terminator.Kind = Gen5CfgTerminatorKind.Branch;
                block.Terminator.TrueBlock = fallthroughId;
                block.Successors.Add(fallthroughId);
            }
            else
            {
                block.Terminator.Kind = Gen5CfgTerminatorKind.Return;
            }
        }

        // 4. Prune unreachable blocks
        PruneUnreachableBlocks(graph);

        // 5. Compute Dominators & PostDominators
        ComputeDominators(graph);
        ComputePostDominators(graph);
        ComputeBackEdges(graph);
        ComputeNaturalLoops(graph);

        return graph;
    }

    public static bool Structurize(Gen5CfgGraph graph)
    {
        if (graph.Blocks.Count == 0)
        {
            return false;
        }

        // If any block has unsupported terminator, we cannot structurize
        foreach (var block in graph.Blocks)
        {
            if (block.Terminator.Kind == Gen5CfgTerminatorKind.Unsupported)
            {
                graph.FailureReason = $"Block {block.Id} has unsupported terminator";
                return false;
            }
        }

        // 1. Process natural loops: assign headers, continue blocks, and merge blocks
        foreach (var loop in graph.NaturalLoops)
        {
            var header = graph.FindBlock(loop.Header);
            if (header == null)
            {
                graph.FailureReason = $"Loop header {loop.Header} not found";
                return false;
            }

            header.Terminator.IsLoopHeader = true;
            header.Terminator.ContinueBlock = loop.ContinueBlock;

            // Find loop merge block: first block outside loop dominated by header or reached by loop exits
            int loopMerge = -1;
            foreach (var exit in loop.ExitBlocks)
            {
                var exitBlock = graph.FindBlock(exit);
                if (exitBlock == null) continue;

                foreach (var succ in exitBlock.Successors)
                {
                    if (!loop.BodyBlocks.Contains(succ))
                    {
                        if (loopMerge == -1)
                        {
                            loopMerge = succ;
                        }
                        else if (loopMerge != succ)
                        {
                            // Multiple exit targets: find common post-dominator
                            loopMerge = graph.FindNearestCommonPostDominator(loopMerge, succ);
                        }
                    }
                }
            }

            if (loopMerge == -1)
            {
                // Terminal loop: select external successor of latch if any
                var latchBlock = graph.FindBlock(loop.Latch);
                if (latchBlock != null)
                {
                    foreach (var s in latchBlock.Successors)
                    {
                        if (!loop.BodyBlocks.Contains(s))
                        {
                            loopMerge = s;
                            break;
                        }
                    }
                }
            }

            loop.Merge = loopMerge;
            header.Terminator.MergeBlock = loopMerge;
        }

        // 2. Process conditional branches (selections): assign selection merge blocks
        foreach (var block in graph.Blocks)
        {
            if (block.Terminator.Kind != Gen5CfgTerminatorKind.ConditionalBranch)
            {
                continue;
            }

            var trueBlock = block.Terminator.TrueBlock;
            var falseBlock = block.Terminator.FalseBlock;

            // If one branch exits or returns
            if (trueBlock == -1 && falseBlock != -1)
            {
                block.Terminator.MergeBlock = falseBlock;
                continue;
            }
            if (falseBlock == -1 && trueBlock != -1)
            {
                block.Terminator.MergeBlock = trueBlock;
                continue;
            }
            if (trueBlock == -1 && falseBlock == -1)
            {
                continue;
            }

            // Selection merge is nearest common post-dominator of true and false branches
            var merge = graph.FindNearestCommonPostDominator(trueBlock, falseBlock);
            if (merge == -1)
            {
                if (graph.PostDominates(falseBlock, trueBlock))
                {
                    merge = falseBlock;
                }
                else if (graph.PostDominates(trueBlock, falseBlock))
                {
                    merge = trueBlock;
                }
            }

            // Check if this selection is inside a loop; merge cannot escape the loop unless it's the loop's own merge
            var containingLoop = FindInnermostLoop(graph, block.Id);
            if (containingLoop != null && merge >= 0 && !containingLoop.BodyBlocks.Contains(merge))
            {
                if (merge != containingLoop.Merge)
                {
                    merge = containingLoop.Merge;
                }
            }

            block.Terminator.MergeBlock = merge;
        }

        return true;
    }

    private static Gen5CfgNaturalLoop? FindInnermostLoop(Gen5CfgGraph graph, int blockId)
    {
        Gen5CfgNaturalLoop? best = null;
        foreach (var loop in graph.NaturalLoops)
        {
            if (loop.BodyBlocks.Contains(blockId))
            {
                if (best == null || loop.BodyBlocks.Count < best.BodyBlocks.Count)
                {
                    best = loop;
                }
            }
        }
        return best;
    }

    private static void PruneUnreachableBlocks(Gen5CfgGraph graph)
    {
        if (graph.Blocks.Count == 0) return;

        var reachable = new bool[graph.Blocks.Count];
        var pending = new Stack<int>();
        pending.Push(0);

        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id < 0 || id >= graph.Blocks.Count || reachable[id])
            {
                continue;
            }

            reachable[id] = true;
            var block = graph.Blocks[id];
            foreach (var succ in block.Successors)
            {
                if (!reachable[succ])
                {
                    pending.Push(succ);
                }
            }
        }

        if (reachable.All(r => r))
        {
            RebuildPredecessors(graph);
            return;
        }

        var idMap = new int[graph.Blocks.Count];
        Array.Fill(idMap, -1);
        var pruned = new List<Gen5CfgBlock>();

        for (var oldId = 0; oldId < graph.Blocks.Count; oldId++)
        {
            if (!reachable[oldId]) continue;
            idMap[oldId] = pruned.Count;
            pruned.Add(graph.Blocks[oldId]);
        }

        graph.Blocks.Clear();
        graph.Blocks.AddRange(pruned);

        for (var newId = 0; newId < graph.Blocks.Count; newId++)
        {
            var block = graph.Blocks[newId];
            block.Id = newId;

            for (var s = 0; s < block.Successors.Count; s++)
            {
                block.Successors[s] = idMap[block.Successors[s]];
            }
            block.Successors.RemoveAll(s => s < 0);

            var term = block.Terminator;
            if (term.TrueBlock >= 0) term.TrueBlock = idMap[term.TrueBlock];
            if (term.FalseBlock >= 0) term.FalseBlock = idMap[term.FalseBlock];
            if (term.MergeBlock >= 0) term.MergeBlock = idMap[term.MergeBlock];
            if (term.ContinueBlock >= 0) term.ContinueBlock = idMap[term.ContinueBlock];
        }

        RebuildPredecessors(graph);
    }

    private static void RebuildPredecessors(Gen5CfgGraph graph)
    {
        foreach (var block in graph.Blocks)
        {
            block.Predecessors.Clear();
        }

        foreach (var block in graph.Blocks)
        {
            foreach (var succ in block.Successors)
            {
                if (succ >= 0 && succ < graph.Blocks.Count)
                {
                    if (!graph.Blocks[succ].Predecessors.Contains(block.Id))
                    {
                        graph.Blocks[succ].Predecessors.Add(block.Id);
                    }
                }
            }
        }

        foreach (var block in graph.Blocks)
        {
            block.Predecessors.Sort();
        }
    }

    private static void ComputeDominators(Gen5CfgGraph graph)
    {
        var count = graph.Blocks.Count;
        var all = new HashSet<int>(Enumerable.Range(0, count));

        for (var i = 0; i < count; i++)
        {
            graph.Blocks[i].Dominators = i == 0
                ? [0]
                : [.. all];
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 1; i < count; i++)
            {
                var block = graph.Blocks[i];
                if (block.Predecessors.Count == 0)
                {
                    var self = new HashSet<int> { i };
                    if (!block.Dominators.SetEquals(self))
                    {
                        block.Dominators = self;
                        changed = true;
                    }
                    continue;
                }

                HashSet<int>? newDom = null;
                foreach (var pred in block.Predecessors)
                {
                    if (newDom == null)
                    {
                        newDom = new HashSet<int>(graph.Blocks[pred].Dominators);
                    }
                    else
                    {
                        newDom.IntersectWith(graph.Blocks[pred].Dominators);
                    }
                }

                newDom ??= [];
                newDom.Add(i);

                if (!block.Dominators.SetEquals(newDom))
                {
                    block.Dominators = newDom;
                    changed = true;
                }
            }
        }
    }

    private static void ComputePostDominators(Gen5CfgGraph graph)
    {
        var count = graph.Blocks.Count;
        var all = new HashSet<int>(Enumerable.Range(0, count));

        for (var i = 0; i < count; i++)
        {
            var block = graph.Blocks[i];
            block.PostDominators = block.Successors.Count == 0
                ? [i]
                : [.. all];
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < count; i++)
            {
                var block = graph.Blocks[i];
                if (block.Successors.Count == 0)
                {
                    continue;
                }

                HashSet<int>? newPostDom = null;
                foreach (var succ in block.Successors)
                {
                    if (newPostDom == null)
                    {
                        newPostDom = new HashSet<int>(graph.Blocks[succ].PostDominators);
                    }
                    else
                    {
                        newPostDom.IntersectWith(graph.Blocks[succ].PostDominators);
                    }
                }

                newPostDom ??= [];
                newPostDom.Add(i);

                if (!block.PostDominators.SetEquals(newPostDom))
                {
                    block.PostDominators = newPostDom;
                    changed = true;
                }
            }
        }
    }

    private static void ComputeBackEdges(Gen5CfgGraph graph)
    {
        graph.BackEdges.Clear();
        foreach (var block in graph.Blocks)
        {
            foreach (var succ in block.Successors)
            {
                if (graph.Dominates(succ, block.Id))
                {
                    graph.BackEdges.Add(new Gen5CfgBackEdge(block.Id, succ, true));
                }
            }
        }
    }

    private static void ComputeNaturalLoops(Gen5CfgGraph graph)
    {
        graph.NaturalLoops.Clear();
        foreach (var edge in graph.BackEdges)
        {
            var header = edge.To;
            var latch = edge.From;

            var loop = new Gen5CfgNaturalLoop
            {
                Header = header,
                Latch = latch,
                ContinueBlock = latch,
            };

            loop.BodyBlocks.Add(header);
            loop.BodyBlocks.Add(latch);

            var stack = new Stack<int>();
            if (latch != header)
            {
                stack.Push(latch);
            }

            while (stack.Count > 0)
            {
                var bId = stack.Pop();
                var b = graph.FindBlock(bId);
                if (b == null) continue;

                foreach (var pred in b.Predecessors)
                {
                    if (loop.BodyBlocks.Add(pred))
                    {
                        if (pred != header)
                        {
                            stack.Push(pred);
                        }
                    }
                }
            }

            // Identify exit blocks (blocks in loop that branch to blocks outside loop)
            foreach (var bId in loop.BodyBlocks)
            {
                var b = graph.FindBlock(bId);
                if (b == null) continue;

                foreach (var succ in b.Successors)
                {
                    if (!loop.BodyBlocks.Contains(succ))
                    {
                        loop.ExitBlocks.Add(bId);
                        break;
                    }
                }
            }

            graph.NaturalLoops.Add(loop);
        }
    }

    private static bool IsBranch(string opcode) =>
        opcode == "SBranch" || opcode.StartsWith("SCbranch", StringComparison.Ordinal);

    private static bool TryGetBranchTargetPc(Gen5ShaderInstruction instruction, out uint targetPc)
    {
        targetPc = 0;
        if (instruction.Encoding != Gen5ShaderEncoding.Sopp || instruction.Words.Count == 0)
        {
            return false;
        }

        var offset = unchecked((short)(instruction.Words[0] & 0xFFFF));
        var nextPc = (long)instruction.Pc + (instruction.Words.Count * sizeof(uint));
        var target = nextPc + (offset * sizeof(uint));
        if (target < 0 || target > uint.MaxValue)
        {
            return false;
        }

        targetPc = (uint)target;
        return true;
    }

    private static bool IsExitBranchTarget(IReadOnlyList<Gen5ShaderInstruction> instructions, uint targetPc)
    {
        if (instructions.Count == 0) return false;
        var last = instructions[^1];
        var lastEndPc = last.Pc + (uint)(last.Words.Count * sizeof(uint));
        return targetPc >= lastEndPc;
    }

    private static int FindInstructionIndex(IReadOnlyList<Gen5ShaderInstruction> instructions, uint pc)
    {
        for (var i = 0; i < instructions.Count; i++)
        {
            if (instructions[i].Pc == pc)
            {
                return i;
            }
        }
        return -1;
    }

    private static Gen5CfgBranchCondition MapBranchCondition(string opcode) => opcode switch
    {
        "SBranch" => Gen5CfgBranchCondition.Always,
        "SCbranchScc0" => Gen5CfgBranchCondition.SccZero,
        "SCbranchScc1" => Gen5CfgBranchCondition.SccNonZero,
        "SCbranchVccz" => Gen5CfgBranchCondition.VccZero,
        "SCbranchVccnZ" or "SCbranchVccnz" => Gen5CfgBranchCondition.VccNonZero,
        "SCbranchExecz" => Gen5CfgBranchCondition.ExecZero,
        "SCbranchExecnz" => Gen5CfgBranchCondition.ExecNonZero,
        _ => Gen5CfgBranchCondition.Unknown,
    };
}
