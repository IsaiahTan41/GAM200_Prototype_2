using System;
using System.Collections.Generic;

namespace KungFuCrashout.AI
{
    /// <summary>
    /// A deliberately small behaviour tree (GDD → AI system: "Behaviour tree system meant to react to player movements").
    /// Nodes are plain C# objects built in code, ticked every frame by BehaviourTreeAI.
    ///
    ///   Selector  — tries children in order; the first that doesn't FAIL wins (priority list: "react, else punish, else…")
    ///   Sequence  — runs children in order; stops at the first that doesn't SUCCEED ("if this, then do that")
    ///   Condition — a yes / no question about the fight (Success / Failure)
    ///   Act       — does something; returns Success, Failure or Running
    ///   Chance    — passes only a set percentage of the time (re-rolled at most every Reroll seconds), so the
    ///               difficulty tiers can share one tree and differ only in their numbers
    ///
    /// Every node has a Name, and the last leaf that ran is kept as the tree's Trace, so the debug label above the
    /// AI can show WHY it did something ("Punish ▸ Heavy on downed").
    /// Place in: Assets/_Project/Scripts/AI/BehaviourTree.cs
    /// </summary>
    public enum NodeStatus { Success, Failure, Running }

    public abstract class BtNode
    {
        public string Name { get; }
        protected BtNode(string name) => Name = name;
        public abstract NodeStatus Tick(BtContext ctx);
    }

    /// <summary>Shared per-tick data: the path of node names that led to the current action.</summary>
    public class BtContext
    {
        readonly List<string> path = new List<string>();
        public string Trace { get; private set; } = "-";

        public void Push(string name) => path.Add(name);
        public void Pop() { if (path.Count > 0) path.RemoveAt(path.Count - 1); }

        /// <summary>Remember the path down to the leaf that just acted (shown on the debug label).</summary>
        public void Commit() => Trace = path.Count == 0 ? "-" : string.Join(" > ", path);
    }

    public sealed class Selector : BtNode
    {
        readonly BtNode[] children;
        public Selector(string name, params BtNode[] children) : base(name) => this.children = children;

        public override NodeStatus Tick(BtContext ctx)
        {
            ctx.Push(Name);
            try
            {
                foreach (var c in children)
                {
                    var s = c.Tick(ctx);
                    if (s != NodeStatus.Failure) return s;
                }
                return NodeStatus.Failure;
            }
            finally { ctx.Pop(); }
        }
    }

    public sealed class Sequence : BtNode
    {
        readonly BtNode[] children;
        public Sequence(string name, params BtNode[] children) : base(name) => this.children = children;

        public override NodeStatus Tick(BtContext ctx)
        {
            ctx.Push(Name);
            try
            {
                foreach (var c in children)
                {
                    var s = c.Tick(ctx);
                    if (s != NodeStatus.Success) return s;
                }
                return NodeStatus.Success;
            }
            finally { ctx.Pop(); }
        }
    }

    public sealed class Condition : BtNode
    {
        readonly Func<bool> test;
        public Condition(string name, Func<bool> test) : base(name) => this.test = test;
        public override NodeStatus Tick(BtContext ctx) => test() ? NodeStatus.Success : NodeStatus.Failure;
    }

    public sealed class Act : BtNode
    {
        readonly Func<NodeStatus> action;
        public Act(string name, Func<NodeStatus> action) : base(name) => this.action = action;

        public override NodeStatus Tick(BtContext ctx)
        {
            var s = action();
            if (s != NodeStatus.Failure)
            {
                ctx.Push(Name);
                ctx.Commit();
                ctx.Pop();
            }
            return s;
        }
    }

    /// <summary>
    /// Passes with a probability. The roll is kept for Reroll seconds so a "30% chance to block" isn't re-rolled
    /// every frame (which would make it ~100% within a second).
    /// </summary>
    public sealed class Chance : BtNode
    {
        readonly Func<float> probability;
        readonly float reroll;
        readonly Func<float> clock;
        float nextRoll = float.MinValue;
        bool passed;

        public Chance(string name, Func<float> probability, float reroll, Func<float> clock) : base(name)
        {
            this.probability = probability;
            this.reroll = reroll;
            this.clock = clock;
        }

        public override NodeStatus Tick(BtContext ctx)
        {
            float now = clock();
            if (now >= nextRoll)
            {
                passed = UnityEngine.Random.value < probability();
                nextRoll = now + reroll;
            }
            return passed ? NodeStatus.Success : NodeStatus.Failure;
        }

        /// <summary>Force a fresh roll next tick (e.g. when a new threat appears).</summary>
        public void Reset() => nextRoll = float.MinValue;
    }
}
