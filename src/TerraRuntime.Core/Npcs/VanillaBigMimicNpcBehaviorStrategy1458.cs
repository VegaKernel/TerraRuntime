using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

internal interface IVanillaBigMimicEffectPlan1458 : IDisposable
{
    bool IsCurrent { get; }
    bool TryClaim();
    bool TryCommit();
}

internal interface IVanillaBigMimicEffects1458
{
    bool TryPlanReflection(in NpcSnapshot source, IVanillaNpcRandom random, out IVanillaBigMimicEffectPlan1458 plan);
    bool TryPlanCannon(in NpcSnapshot source, ReadOnlySpan<VanillaBigMimicCannonItem1458> items,
        IVanillaNpcRandom random, out IVanillaBigMimicEffectPlan1458 plan);
}

/// <summary>Retains source AI, random checkpoints and external effect admission until the NPC revision is accepted.</summary>
internal sealed class VanillaBigMimicNpcBehaviorStrategy1458 : IVanillaNpcBehaviorStrategy
{
    private IVanillaBigMimicEnvironment1458? environment;
    private IVanillaBigMimicEffects1458? effects;
    private IVanillaNpcRandom? random;
    private bool tenthAnniversary;
    private Plan? pending;
    private NpcSnapshot completedBefore;
    private NpcSnapshot completedAfter;
    private bool completedForce;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Update,
        VanillaNpcBehaviorContext Context, VanillaNpcTargetCandidate[] Candidates,
        VanillaUnifiedRandom1458 Owner, VanillaUnifiedRandom1458 BeforeRandom,
        VanillaUnifiedRandom1458 AfterRandom, IVanillaBigMimicEffectPlan1458? Effects, bool FallThrough, bool Force);

    internal void Configure(IVanillaBigMimicEnvironment1458 value, IVanillaNpcRandom source)
    {
        environment = value;
        random = source;
    }

    internal void ConfigureEffects(IVanillaBigMimicEffects1458 value, bool tenth)
    {
        effects = value;
        tenthAnniversary = tenth;
    }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        pending?.Effects?.Dispose();
        pending = null;
        next = default;
        if (environment is null || random is not SystemVanillaNpcRandom trusted || context.RemixWorld ||
            npc.Handle.Slot >= 200 || npc.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) || npc.Simulation.LifeMax <= 0 ||
            npc.Target >= 255 || !context.TryFindCandidate((byte)npc.Target, out var current) || !Admitted(current))
            return false;

        VanillaNpcTargetCandidate closest = current;
        if (context.TrySelectClosestTarget(in npc, in definition, out var selected) &&
            context.TryFindCandidate((byte)selected.Target, out var candidate))
            closest = candidate;
        if (!Admitted(closest))
            return false;
        var beforeRandom = trusted.SourceRandom.Clone();
        var afterRandom = beforeRandom.Clone();
        var preview = new SystemVanillaNpcRandom(afterRandom);
        var state = new VanillaBigMimicAi1458
        {
            Type = npc.Type,
            X = npc.PositionX,
            Y = npc.PositionY,
            Width = body.Width,
            Height = body.Height,
            Vx = npc.VelocityX,
            Vy = npc.VelocityY,
            Phase = npc.Ai.Ai0,
            Clock = npc.Ai.Ai1,
            Repeat = npc.Ai.Ai2,
            Hops = npc.Ai.Ai3,
            Target = npc.Target,
            OldTarget = npc.Target,
            TargetX = current.CenterX,
            TargetY = current.CenterY,
            TargetWidth = (int)current.Width,
            TargetHeight = (int)current.Height,
            TargetDead = current.Dead,
            Closest = new(closest.Slot, closest.CenterX, closest.CenterY, (int)closest.Width, (int)closest.Height, closest.Dead),
            Dir = npc.Simulation.DirectionX,
            DirY = npc.Simulation.DirectionY,
            OldDir = npc.Simulation.DirectionX,
            OldDirY = npc.Simulation.DirectionY,
            Sprite = npc.Simulation.SpriteDirection,
            Life = npc.Simulation.Life,
            LifeMax = npc.Simulation.LifeMax,
            Damage = npc.Simulation.DamageOverride ?? definition.Damage,
            Defense = npc.Simulation.DefenseOverride ?? definition.Defense,
            Alpha = npc.Simulation.Alpha,
            Difficulty = npc.Simulation.SpawnDifficulty ?? (context.MasterMode ? 3f : context.ExpertMode ? 2f : 1f),
            Expert = context.ExpertMode,
            Tenth = tenthAnniversary,
            CollideX = npc.Simulation.CollideX,
            CollideY = npc.Simulation.CollideY,
            JustHit = npc.Simulation.JustHit,
            Confused = npc.Simulation.Confused
        };
        Span<VanillaBigMimicCannonItem1458> cannon = stackalloc VanillaBigMimicCannonItem1458[10];
        int count = state.Step(environment, preview, cannon);
        var simulation = npc.Simulation with
        {
            DirectionX = state.Dir,
            DirectionY = state.DirY,
            SpriteDirection = state.Sprite,
            Life = state.Life,
            Alpha = state.Alpha,
            NoGravity = state.NoGravity,
            NoTileCollide = state.NoTile,
            DontTakeDamage = state.Invulnerable,
            ReflectsProjectiles = state.Reflects,
            KnockBackResist = state.Knockback,
            DamageOverride = state.Damage,
            DefenseOverride = state.Defense
        };
        var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
            state.Vx, state.Vy, checked((ushort)state.Target), new(state.Phase, state.Clock, state.Repeat, state.Hops), simulation);
        if (count < 0 || !Finite(in update))
            return false;
        IVanillaBigMimicEffectPlan1458? effectPlan = null;
        if (state.Reflects)
        {
            if (effects is null || !effects.TryPlanReflection(in npc, preview, out effectPlan))
                return false;
        }
        else if (count != 0)
        {
            if (effects is null || !effects.TryPlanCannon(in npc, cannon[..count], preview, out effectPlan))
                return false;
        }
        if (!trusted.SourceRandom.HasSameState(beforeRandom) || effectPlan is { IsCurrent: false })
        {
            effectPlan?.Dispose();
            return false;
        }
        pending = new(npc, update, context, context.Candidates.ToArray(), trusted.SourceRandom,
            beforeRandom, afterRandom, effectPlan,
            state.TargetY - state.TargetHeight * .5f > npc.PositionY + body.Height, state.Sync);
        next = State(in npc);
        return true;
    }

    internal bool TryGetAcceptedPlan(in NpcSnapshot before, in NpcSnapshot accepted,
        INpcAiCommittedNpcMutationSink mutations, out NpcStateUpdate update, out bool fallThrough)
    {
        update = default;
        fallThrough = false;
        if (pending is not { } plan || plan.Before != before || accepted.Handle != before.Handle ||
            accepted.TypeIdentity != before.TypeIdentity || accepted.Revision.Value != before.Revision.Value + 1 ||
            State(in accepted) != State(in before) ||
            !Validate(plan, in accepted, mutations) || (plan.Effects is not null && !plan.Effects.TryClaim()))
            return false;
        update = plan.Update;
        fallThrough = plan.FallThrough;
        return true;
    }

    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted,
        in NpcStateUpdate final, INpcAiCommittedNpcMutationSink mutations)
    {
        if (pending is not { } plan || plan.Before != before || !Finite(in final) ||
            !Validate(plan, in accepted, mutations) || !mutations.TryUpdateState(in accepted, in final, out var completed))
        {
            pending?.Effects?.Dispose();
            pending = null;
            return default;
        }
        // All source effects were computed from the pre-physics body. The trusted stores have no
        // callbacks during reflection; claimed item allocation cannot fail after this final fence.
        if (!plan.Owner.HasSameState(plan.BeforeRandom))
        {
            plan.Effects?.Dispose();
            pending = null;
            return default;
        }
        plan.Owner.CopyStateFrom(plan.AfterRandom);
        if (plan.Effects is not null && !plan.Effects.TryCommit())
        {
            plan.Effects.Dispose();
            pending = null;
            return default;
        }
        completedBefore = before;
        completedAfter = completed;
        completedForce = plan.Force;
        plan.Effects?.Dispose();
        pending = null;
        return completed;
    }

    internal bool RequiresForcedUpdate(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedForce;

    internal void Cancel()
    {
        pending?.Effects?.Dispose();
        pending = null;
    }

    private static bool Validate(Plan plan, in NpcSnapshot source, INpcAiCommittedNpcMutationSink mutations) =>
        plan.Owner.HasSameState(plan.BeforeRandom) && plan.Context.Candidates.SequenceEqual(plan.Candidates) &&
        mutations.TryGetActive(source.Handle.Slot, out var current) && current == source &&
        (plan.Effects?.IsCurrent ?? true) && plan.Owner.HasSameState(plan.BeforeRandom);

    private static bool Admitted(VanillaNpcTargetCandidate target) =>
        target.Active && !target.Dead && !target.Ghost && !target.NoAggro && target.Aggro >= 0 &&
        float.IsFinite(target.CenterX) && float.IsFinite(target.CenterY);

    private static bool Finite(in NpcStateUpdate state) =>
        float.IsFinite(state.PositionX) && float.IsFinite(state.PositionY) && float.IsFinite(state.VelocityX) &&
        float.IsFinite(state.VelocityY) && state.Ai.IsFinite && state.Simulation.IsValid;

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
}
