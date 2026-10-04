using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Npcs;
internal sealed partial class VanillaFlyingEyeNpcBehaviorStrategy
{
    private IVanillaNpcRandom? retainedRandom;
    private RetainedPlan? retainedPlan;
    private NpcSnapshot completedBefore;
    private NpcSnapshot completedAfter;
    private bool completedForce;
    private sealed record RetainedPlan(NpcSnapshot Before, NpcStateUpdate Update, VanillaNpcBehaviorContext Context, VanillaNpcTargetCandidate[] Candidates, PlayerStateSnapshot[] Players, bool DayTime, double Surface, IVanillaFlyingEyeWorldFence1458 World, VanillaUnifiedRandom1458 Owner, VanillaUnifiedRandom1458 BeforeRandom, VanillaUnifiedRandom1458 AfterRandom, bool Force);
    internal void SetRandom(IVanillaNpcRandom random) => retainedRandom = random;
    internal bool HasRetainedPlan(in NpcSnapshot source) => retainedPlan?.Before == source;
    private bool TryRetain(in NpcSnapshot npc, in VanillaNpcDefinition definition, VanillaNpcBehaviorContext context, IVanillaFlyingEyeRetainedEnvironment1458 environment, out NpcStateUpdate next)
    {
        retainedPlan = null;
        next = default;
        if (retainedRandom is not SystemVanillaNpcRandom trusted || definition.AiStyle != VanillaNpcAiStyles.DemonEye || npc.Simulation.LifeMax <= 0 || npc.Simulation.Scale is <= 0f or >= 2f || npc.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer || !definition.TryResolveHitbox(npc.Simulation, out var body) || npc.Target >= 255 || !context.TryFindCandidate((byte)npc.Target, out var current) || !Admitted(current) || !context.TrySelectClosestTarget(in npc, in definition, out var selected) || !context.TryFindCandidate((byte)selected.Target, out var closest) || !Admitted(closest))
            return false;
        var candidates = context.Candidates.ToArray();
        var players = new List<PlayerStateSnapshot>(candidates.Length);
        foreach (var candidate in candidates)
        {
            if (!candidate.Active)
                continue;
            if (!context.TryGetOwnedPlayer(candidate.Slot, out var player) || !MatchesBody(candidate, in player))
                return false;
            players.Add(player);
        }

        var beforeRandom = trusted.SourceRandom.Clone();
        if (!environment.TryCapture(in npc, in current, in closest, out var world) || !world.IsCurrent)
            return false;
        var afterRandom = beforeRandom.Clone();
        var state = new VanillaFlyingEyeAi1458
        {
            Type = npc.Type,
            X = npc.PositionX,
            Y = npc.PositionY,
            Width = body.Width,
            Height = body.Height,
            Vx = npc.VelocityX,
            Vy = npc.VelocityY,
            OldVx = npc.Simulation.OldVelocityX,
            OldVy = npc.Simulation.OldVelocityY,
            Scale = npc.Simulation.Scale,
            Clock = npc.Ai.Ai0,
            Phase = npc.Ai.Ai1,
            Direction = npc.Simulation.DirectionX,
            DirectionY = npc.Simulation.DirectionY,
            OldDirection = npc.Simulation.DirectionX,
            OldDirectionY = npc.Simulation.DirectionY,
            Target = npc.Target,
            OldTarget = npc.Target,
            Life = npc.Simulation.Life,
            LifeMax = npc.Simulation.LifeMax,
            TimeLeft = npc.Simulation.TimeLeft,
            Alpha = npc.Simulation.Alpha,
            Rotation = npc.Simulation.Rotation ?? 0f,
            NoTileCollide = npc.Simulation.NoTileCollide,
            CollideX = npc.Simulation.CollideX,
            CollideY = npc.Simulation.CollideY,
            Wet = npc.Simulation.Wet,
            Confused = npc.Simulation.Confused,
            DayTime = context.DayTime,
            WorldSurfacePixels = context.WorldSurfacePixels,
            TargetInGraveyard = environment.IsGraveyardAt(current.CenterX, current.CenterY),
            Current = Target(current),
            Closest = Target(closest)
        };
        state.Step(environment, new SystemVanillaNpcRandom(afterRandom));
        var simulation = npc.Simulation with
        {
            DirectionX = state.Direction,
            DirectionY = state.DirectionY,
            NoGravity = true,
            NoTileCollide = state.NoTileCollide,
            Wet = state.Wet,
            TimeLeft = state.TimeLeft,
            Alpha = state.Alpha,
            Rotation = state.Rotation
        };
        var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, state.Vx, state.Vy, checked((ushort)state.Target), new(state.Clock, state.Phase, npc.Ai.Ai2, npc.Ai.Ai3), simulation);
        var plan = new RetainedPlan(npc, update, context, candidates, players.ToArray(), context.DayTime, context.WorldSurfacePixels, world, trusted.SourceRandom, beforeRandom, afterRandom, state.Force);
        if (!Finite(in update) || !InputsCurrent(plan))
            return false;
        retainedPlan = plan;
        next = State(in npc);
        return true;
    }

    internal bool TryGetRetainedPlan(in NpcSnapshot before, in NpcSnapshot accepted, INpcAiCommittedNpcMutationSink mutations, out NpcStateUpdate update)
    {
        update = default;
        if (retainedPlan is not { } plan || plan.Before != before || accepted.Handle != before.Handle || accepted.Revision.Value != before.Revision.Value + 1 || State(in accepted) != State(in before) || !Current(plan, in accepted, mutations))
            return false;
        update = plan.Update;
        return true;
    }

    internal NpcSnapshot CompleteRetained(in NpcSnapshot before, in NpcSnapshot accepted, in NpcStateUpdate final, INpcAiCommittedNpcMutationSink mutations)
    {
        if (retainedPlan is not { } plan || plan.Before != before || !Finite(in final) || !Current(plan, in accepted, mutations) || !TryFinishOuter(plan, in final, out var finished) || !mutations.TryUpdateState(in accepted, in finished, out var completed) || !Current(plan, in completed, mutations))
        {
            retainedPlan = null;
            return default;
        }

        plan.Owner.CopyStateFrom(plan.AfterRandom);
        completedBefore = before;
        completedAfter = completed;
        completedForce = plan.Force;
        retainedPlan = null;
        return completed;
    }

    private static bool TryFinishOuter(RetainedPlan plan, in NpcStateUpdate state, out NpcStateUpdate finished)
    {
        finished = default;
        if (!VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(state.Type), new NpcNetId(state.NetId), out var definition) || !definition.TryResolveHitbox(state.Simulation, out var body))
            return false;
        // NPC.FindFrame follows collision: Pigron keeps the pre-steering AI rotation.
        var simulation = state.Simulation;
        if (VanillaFlyingEyeNpcCatalog.IsPigron(new NpcTypeId(state.Type)))
            simulation = simulation with
            {
                SpriteDirection = simulation.DirectionX
            };
        else if (state.VelocityX != 0f)
            simulation = simulation with
            {
                SpriteDirection = state.VelocityX > 0f ? 1 : -1,
                Rotation = (float)Math.Atan2(state.VelocityY, state.VelocityX) + (state.VelocityX < 0f ? 3.14f : 0f)
            };
        // All admitted AI_002 types use the ordinary CheckActive rectangles. Dead active
        // players still keep an NPC active here; target eligibility is a separate source phase.
        float centerX = state.PositionX + body.Width / 2;
        float centerY = state.PositionY + body.Height / 2;
        int rangeX = (int)(centerX - 4032f);
        int rangeY = (int)(centerY - 2520f);
        int resetX = (int)((double)centerX - 960d - body.Width);
        int resetY = (int)((double)centerY - 600d - body.Height);
        bool active = false;
        int timeLeft = simulation.TimeLeft;
        foreach (var player in plan.Candidates)
        {
            if (!player.Active)
                continue;
            int x = (int)(player.CenterX - player.Width * 0.5f);
            int y = (int)(player.CenterY - player.Height * 0.5f);
            int width = (int)player.Width;
            int height = (int)player.Height;
            active |= Intersects(rangeX, rangeY, 8064, 5040, x, y, width, height);
            if (Intersects(resetX, resetY, 1920 + body.Width * 2, 1200 + body.Height * 2, x, y, width, height))
                timeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft;
        }

        timeLeft--;
        // TimeLeft=0 is the runtime's owned expiry boundary; it removes the generation
        // after this completed update, without authorizing loot or combat death.
        bool expired = !active || timeLeft <= 0;
        simulation = simulation with
        {
            TimeLeft = expired ? 0 : timeLeft,
            Life = expired ? 0 : simulation.Life
        };
        finished = state with
        {
            Simulation = simulation
        };
        return true;
    }

    private static bool Intersects(int x, int y, int width, int height, int otherX, int otherY, int otherWidth, int otherHeight) => otherX < x + width && x < otherX + otherWidth && otherY < y + height && y < otherY + otherHeight;
    internal bool RequiresRetainedForcedUpdate(in NpcSnapshot before, in NpcSnapshot after) => completedBefore == before && completedAfter == after && completedForce;
    internal void CancelRetained() => retainedPlan = null;
    private static bool Current(RetainedPlan plan, in NpcSnapshot source, INpcAiCommittedNpcMutationSink mutations) => InputsCurrent(plan) && mutations.TryGetActive(source.Handle.Slot, out var current) && current == source && InputsCurrent(plan);
    private static bool InputsCurrent(RetainedPlan plan)
    {
        if (!plan.Owner.HasSameState(plan.BeforeRandom) || plan.Context.DayTime != plan.DayTime || plan.Context.WorldSurfacePixels != plan.Surface || !plan.Context.Candidates.SequenceEqual(plan.Candidates) || !plan.World.IsCurrent)
            return false;
        foreach (var expected in plan.Players)
            if (!plan.Context.TryGetOwnedPlayer(expected.Player.Slot.Value, out var current) || current != expected)
                return false;
        return plan.Owner.HasSameState(plan.BeforeRandom) && plan.World.IsCurrent;
    }

    private static VanillaFlyingEyeTarget1458 Target(VanillaNpcTargetCandidate candidate) => new(candidate.Slot, candidate.CenterX - candidate.Width * 0.5f, candidate.CenterY - candidate.Height * 0.5f, (int)candidate.Width, (int)candidate.Height);
    private static bool MatchesBody(VanillaNpcTargetCandidate candidate, in PlayerStateSnapshot player)
    {
        var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) : (20f, 42f);
        return candidate.Width == size.Item1 && candidate.Height == size.Item2 && candidate.CenterX == player.PositionX + size.Item1 * 0.5f && candidate.CenterY == player.PositionY + size.Item2 * 0.5f && candidate.Dead == player.IsDead;
    }

    private static bool Admitted(VanillaNpcTargetCandidate target) => target.Active && !target.Dead && !target.Ghost && !target.NoAggro && target.Aggro >= 0 && float.IsFinite(target.CenterX) && float.IsFinite(target.CenterY) && float.IsFinite(target.Width) && float.IsFinite(target.Height) && target.Width is> 0 and <= 1024 && target.Height is> 0 and <= 1024 && target.Width == (int)target.Width && target.Height == (int)target.Height;
    private static bool Finite(in NpcStateUpdate state) => float.IsFinite(state.PositionX) && float.IsFinite(state.PositionY) && float.IsFinite(state.VelocityX) && float.IsFinite(state.VelocityY) && state.Ai.IsFinite && state.Simulation.IsValid;
    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
}
