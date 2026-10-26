namespace TerraRuntime.Gameplay.Items;

// Both generation and legality execute these semantic branches. No seed search.
internal static class FishingCrateGrammar1458
{
    internal sealed class Roll(Func<int, int, int> next, Func<int, int, bool> grant)
    {
        internal int Next(int min, int max) => next(min, max);
        internal bool Give(int type, int stack) => grant(type, stack);
    }
    internal sealed class Match(IReadOnlyList<FishingReward1458> rewards, bool ordered)
    {
        private readonly Dictionary<Node, uint> masks = [];
        internal int Steps;
        internal bool Exhausted;
        internal bool Step()
        {
            if (Steps >= VanillaFishingCrate1458.MaximumValidationSteps) { Exhausted = true; return false; }
            Steps++;
            return true;
        }
        internal bool Find(int type, int min, int max, uint used, out int index)
        {
            index = -1;
            for (int i = 0; i < rewards.Count; i++)
                if ((used & (1u << i)) == 0 && rewards[i].Type == type && rewards[i].Stack >= min && rewards[i].Stack <= max)
                {
                    if (ordered && i != System.Numerics.BitOperations.PopCount(used)) return false;
                    index = i; return true;
                }
            return false;
        }
        internal uint TypeMask(int type)
        {
            for (int i = 0; i < rewards.Count; i++) if (rewards[i].Type == type) return 1u << i;
            return 0;
        }
        internal uint Possible(Node node)
        {
            if (masks.TryGetValue(node, out uint mask)) return mask;
            if (!Step()) return 0;
            mask = node.Candidates(this); masks.Add(node, mask); return mask;
        }
    }
    internal sealed record Trace(Trace? Previous, int Index);
    internal readonly record struct State(uint Used, bool Pending, Trace? Trace);
    internal abstract class Node
    {
        internal abstract bool Generate(Roll roll, ref bool pending);
        internal abstract uint Candidates(Match match);
        protected abstract List<State> AcceptCore(Match match, State state);
        internal List<State> Accept(Match match, State state)
        {
            if (!match.Step()) return [];
            uint local = match.Possible(this);
            // Source grant identities never occur in two sequential groups of
            // one opening. A group must consume every supplied identity it can
            // produce; otherwise a missing mandatory/exclusive branch cannot be
            // repaired later. This also prunes optional-group subset explosions.
            return AcceptCore(match, state).Where(result => (result.Used & local) == local).ToList();
        }
        protected static List<State> Distinct(IEnumerable<State> states)
        {
            var seen = new HashSet<(uint, bool)>();
            return states.Where(state => seen.Add((state.Used, state.Pending))).ToList();
        }
    }
    private sealed class Empty : Node
    {
        internal override bool Generate(Roll roll, ref bool pending) => true;
        internal override uint Candidates(Match match) => 0;
        protected override List<State> AcceptCore(Match match, State state) => [state];
    }
    internal static readonly Node None = new Empty();
    private sealed class Sequence(Node[] children) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending)
        {
            foreach (var child in children) if (!child.Generate(roll, ref pending)) return false;
            return true;
        }
        internal override uint Candidates(Match match) => children.Aggregate(0u, (mask, child) => mask | match.Possible(child));
        protected override List<State> AcceptCore(Match match, State state)
        {
            List<State> states = [state];
            foreach (var child in children)
            {
                states = Distinct(states.SelectMany(value => child.Accept(match, value)));
                if (states.Count == 0 || match.Exhausted) return [];
            }
            return states;
        }
    }
    internal static Node S(params Node[] children) => new Sequence(children);
    private sealed class Emit(int type, int min, int max, bool randomCount) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending)
        {
            int count = randomCount ? roll.Next(min, max + 1) : min;
            if (!roll.Give(type, count)) return false;
            pending = false; return true;
        }
        internal override uint Candidates(Match match) => match.TypeMask(type);
        protected override List<State> AcceptCore(Match match, State state)
        {
            if (!match.Find(type, min, max, state.Used, out int index)) return [];
            return [new(state.Used | (1u << index), false, new(state.Trace, index))];
        }
    }
    internal static Node E(int type, int count = 1) => new Emit(type, count, count, false);
    internal static Node R(int type, int min, int max) => new Emit(type, min, max - 1, true);
    private sealed class Chance(int denominator, Node yes, Node no) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending) => (roll.Next(0, denominator) == 0 ? yes : no).Generate(roll, ref pending);
        internal override uint Candidates(Match match) => match.Possible(yes) | (denominator == 1 ? 0 : match.Possible(no));
        protected override List<State> AcceptCore(Match match, State state)
        {
            return denominator == 1 ? yes.Accept(match, state) : Distinct(yes.Accept(match, state).Concat(no.Accept(match, state)));
        }
    }
    internal static Node C(int denominator, Node yes, Node? no = null) => new Chance(denominator, yes, no ?? None);
    private sealed class Choice(int min, int max, Node[] children) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending)
        {
            int index = roll.Next(min, max) - min;
            return (uint)index < children.Length && children[index].Generate(roll, ref pending);
        }
        internal override uint Candidates(Match match) => children.Aggregate(0u, (mask, child) => mask | match.Possible(child));
        protected override List<State> AcceptCore(Match match, State state) => Distinct(children.SelectMany(child => child.Accept(match, state)));
    }
    internal static Node P(int[] items, Func<int, Node> child) => new Choice(0, items.Length, items.Select(child).ToArray());
    internal static Node P(params int[] items) => P(items, type => E(type));
    internal static Node T(int min, int max, Func<int, Node> child) => new Choice(min, max, Enumerable.Range(min, max - min).Select(child).ToArray());
    private sealed class Discard(int min, int max, Node child) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending)
        {
            _ = roll.Next(min, max); return child.Generate(roll, ref pending);
        }
        internal override uint Candidates(Match match) => match.Possible(child);
        protected override List<State> AcceptCore(Match match, State state) => child.Accept(match, state);
    }
    internal static Node D(int min, int max, Node child) => new Discard(min, max, child);
    private sealed class Pending(Node child) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending) => !pending || child.Generate(roll, ref pending);
        internal override uint Candidates(Match match) => match.Possible(child);
        protected override List<State> AcceptCore(Match match, State state) => state.Pending ? child.Accept(match, state) : [state];
    }
    internal static Node Q(Node child) => new Pending(child);
    private sealed class Retry(Node child) : Node
    {
        internal override bool Generate(Roll roll, ref bool pending)
        {
            pending = true;
            for (int attempt = 0; attempt < 256; attempt++)
            {
                if (!child.Generate(roll, ref pending)) return false;
                if (!pending) return true;
            }
            return false;
        }
        internal override uint Candidates(Match match) => match.Possible(child);
        protected override List<State> AcceptCore(Match match, State state)
        {
            // Retried iterations have emitted nothing and retain no local values.
            // Matching one successful body therefore recognizes the same outcomes.
            return child.Accept(match, state with { Pending = true }).Where(result => !result.Pending).ToList();
        }
    }
    internal static Node A(Node child) => new Retry(child);
}
