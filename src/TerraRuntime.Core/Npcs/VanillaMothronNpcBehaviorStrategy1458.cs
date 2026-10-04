using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Bounded, retained whole-AI plan. Speculation uses a genuine clone, never live random draws.</summary>
internal sealed class VanillaMothronNpcBehaviorStrategy1458 : IVanillaNpcBehaviorStrategy
{
    private IVanillaMothronEnvironment1458? environment;
    private IVanillaNpcRandom? random;
    private Plan? pending;
    private NpcSnapshot completedBefore;
    private NpcSnapshot completedAfter;
    private bool completedForce;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Update,
        NpcSnapshot[] Peers, NpcStateUpdate[] PeerUpdates, bool[] ChangePeer,
        VanillaUnifiedRandom1458 Owner, VanillaUnifiedRandom1458 BeforeRandom, VanillaUnifiedRandom1458 AfterRandom,
        NpcAiSpawnIntent? Egg, bool Force);

    internal void Configure(IVanillaMothronEnvironment1458 value, IVanillaNpcRandom source)
    {
        environment = value ?? throw new ArgumentNullException(nameof(value));
        random = source ?? throw new ArgumentNullException(nameof(source));
    }

    public bool TryStep(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, INpcAiStateStepper inner, out NpcStateUpdate next)
    {
        _ = inner;
        pending = null;
        next = default;
        if (environment is null || random is not SystemVanillaNpcRandom trusted || context.GoodWorld ||
            context.RemixWorld || npc.Handle.Slot >= 200 || npc.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !definition.TryResolveHitbox(npc.Simulation, out var body) || npc.Simulation.LifeMax <= 0 ||
            !TryTarget(in npc, in definition, context, out var target))
            return false;

        VanillaUnifiedRandom1458 beforeRandom = trusted.SourceRandom.Clone();
        VanillaUnifiedRandom1458 afterRandom = beforeRandom.Clone();
        var previewRandom = new SystemVanillaNpcRandom(afterRandom);
        float difficulty = npc.Simulation.SpawnDifficulty ?? (context.MasterMode ? 3f : context.ExpertMode ? 2f : 1f);
        float knockbackMultiplier = 1f + (Math.Clamp(difficulty, 1f, 3f) - 1f) * (.8f - 1f) / 2f;
        var state = new VanillaMothronAiState1458
        {
            Type = npc.Type,
            X = npc.PositionX,
            Y = npc.PositionY,
            Vx = npc.VelocityX,
            Vy = npc.VelocityY,
            OldVx = npc.Simulation.OldVelocityX,
            OldVy = npc.Simulation.OldVelocityY,
            Rotation = npc.Simulation.Rotation ?? 0f,
            A0 = npc.Ai.Ai0,
            A1 = npc.Ai.Ai1,
            A2 = npc.Ai.Ai2,
            A3 = npc.Ai.Ai3,
            Width = body.Width,
            Height = body.Height,
            Life = npc.Simulation.Life,
            LifeMax = npc.Simulation.LifeMax,
            Damage = npc.Simulation.DamageOverride ?? definition.Damage,
            BaseDamage = npc.Simulation.BaseDamage ?? definition.Damage,
            Defense = npc.Simulation.DefenseOverride ?? definition.Defense,
            Direction = npc.Simulation.DirectionX,
            DirectionY = npc.Simulation.DirectionY,
            OldDirection = npc.Simulation.DirectionX,
            OldDirectionY = npc.Simulation.DirectionY,
            Sprite = npc.Simulation.SpriteDirection,
            Target = target.Slot,
            OldTarget = npc.Target,
            FacingX = (int)(target.CenterX - target.Width * .5f) + (int)target.Width / 2,
            FacingY = (int)(target.CenterY - target.Height * .5f) + (int)target.Height / 2,
            Knockback = npc.Simulation.KnockBackResist ?? definition.KnockBackResist,
            KnockbackMultiplier = knockbackMultiplier,
            NoGravity = npc.Simulation.NoGravity,
            NoTile = npc.Simulation.NoTileCollide,
            Invulnerable = npc.Simulation.DontTakeDamage,
            CollideX = npc.Simulation.CollideX,
            CollideY = npc.Simulation.CollideY,
            TimeLeft = npc.Simulation.TimeLeft,
            Confused = npc.Simulation.Confused
        };
        VanillaMothronNpcCatalog1458.TryGetDefinition(VanillaNpcIds.BabyMothron, out var childDefinition);
        if (!VanillaNpcSpawnDefaults.TryResolve(in childDefinition,
            new VanillaNpcSpawnContext(context.MasterMode ? 3f : context.ExpertMode ? 2f : 1f, 1, false), out var childDefaults))
            return false;
        state.HatchLifeMax = childDefaults.LifeMax;
        state.HatchDamage = childDefaults.Damage;

        Span<NpcSnapshot> scratch = stackalloc NpcSnapshot[200];
        var peers = new List<NpcSnapshot>();
        foreach (NpcTypeId type in new[] { VanillaNpcIds.MothronEgg, VanillaNpcIds.BabyMothron })
        {
            int count = context.CopyNpcPeers(type, scratch);
            for (int index = 0; index < count; index++)
                if (scratch[index].Handle.Slot < 200 && scratch[index].Handle != npc.Handle)
                    peers.Add(scratch[index]);
        }
        peers.Sort(static (left, right) => left.Handle.Slot.CompareTo(right.Handle.Slot));
        var peerUpdates = new NpcStateUpdate[peers.Count];
        var changePeer = new bool[peers.Count];
        if (npc.TypeIdentity == VanillaNpcIds.BabyMothron && context.EclipseActive && npc.Ai.Ai0 is 0f or 1f)
        {
            for (int index = 0; index < peers.Count; index++)
            {
                NpcSnapshot peer = peers[index];
                if (peer.TypeIdentity != VanillaNpcIds.BabyMothron ||
                    !childDefinition.TryResolveHitbox(peer.Simulation, out var peerBody))
                    continue;
                float dx = peer.PositionX + peerBody.Width * .5f - (state.X + state.Width * .5f);
                float dy = peer.PositionY + peerBody.Height * .5f - (state.Y + state.Height * .5f);
                float distance = VanillaMothronYoungAi1458.Length(dx, dy);
                if (distance >= state.Width + state.Height)
                    continue;
                (dx, dy) = VanillaMothronYoungAi1458.Normalize(dx, dy, -.1f);
                state.Vx += dx;
                state.Vy += dy;
                peerUpdates[index] = State(in peer) with { VelocityX = peer.VelocityX - dx, VelocityY = peer.VelocityY - dy };
                if (!Finite(in peerUpdates[index]))
                    return false;
                changePeer[index] = true;
            }
        }

        if (npc.TypeIdentity == VanillaNpcIds.Mothron)
        {
            if (!VanillaMothronParentAi1458.TryStep(state, context.ExpertMode, context.EclipseActive,
                npc.Simulation.JustHit, target.CenterX, target.CenterY, npc.Handle.Slot, environment,
                previewRandom, peers.Count))
                return false;
        }
        else if (npc.TypeIdentity == VanillaNpcIds.MothronEgg)
            VanillaMothronYoungAi1458.StepEgg(state, context.ExpertMode, npc.Simulation.JustHit,
                target.CenterX, target.CenterY, previewRandom);
        else
        {
            var repelled = (state.Vx, state.Vy);
            state.Vx = npc.VelocityX;
            state.Vy = npc.VelocityY;
            VanillaMothronYoungAi1458.StepBaby(state, context.ExpertMode, context.EclipseActive,
                target.CenterX, target.CenterY, environment.SolidCollision(state.X, state.Y, state.Width, state.Height), repelled);
        }

        bool hatch = state.Type != npc.Type;
        var simulation = npc.Simulation with
        {
            DirectionX = state.Direction,
            DirectionY = state.DirectionY,
            SpriteDirection = state.Sprite,
            Life = state.Life,
            LifeMax = state.LifeMax,
            TimeLeft = state.TimeLeft,
            NoGravity = state.NoGravity,
            NoTileCollide = state.NoTile,
            CollideX = state.CollideX,
            CollideY = state.CollideY,
            DontTakeDamage = state.Invulnerable,
            Rotation = state.Rotation,
            KnockBackResist = state.Knockback,
            DamageOverride = state.Damage,
            DefenseOverride = state.Defense
        };
        if (hatch)
        {
            simulation = simulation with
            {
                HitboxOverride = new(state.Width, state.Height),
                Scale = childDefaults.Scale,
                LocalAi = default,
                SpawnDifficulty = childDefaults.Difficulty,
                BaseDamage = childDefaults.Damage,
                BaseDefense = childDefaults.Defense,
                BaseLifeMax = childDefaults.LifeMax
            };
            var transformed = npc with { Type = state.Type, NetId = checked((short)state.Type), PositionY = state.Y, Simulation = simulation };
            if (!TryTarget(in transformed, in childDefinition, context, out var hatchTarget))
                return false;
            state.Target = hatchTarget.Slot;
            int playerX = (int)(hatchTarget.CenterX - hatchTarget.Width * .5f) + (int)hatchTarget.Width / 2;
            int playerY = (int)(hatchTarget.CenterY - hatchTarget.Height * .5f) + (int)hatchTarget.Height / 2;
            int direction = playerX < state.X + state.Width / 2 ? -1 : 1;
            if (simulation.Confused)
                direction *= -1;
            simulation = simulation with { DirectionX = direction, DirectionY = playerY < state.Y + state.Height / 2 ? -1 : 1, SpriteDirection = direction };
        }
        var update = new NpcStateUpdate(state.Type, hatch ? checked((short)state.Type) : npc.NetId,
            state.X, state.Y, state.Vx, state.Vy, state.Target,
            new(state.A0, state.A1, state.A2, state.A3), simulation);
        if (!Finite(in update) || !trusted.SourceRandom.HasSameState(beforeRandom))
            return false;
        NpcAiSpawnIntent? egg = state.Egg is { } spawn
            ? new(VanillaNpcIds.MothronEgg, (int)spawn.X + 17, (int)spawn.Y + 34, 0f, 0f, state.Target) { StartSlot = npc.Handle.Slot }
            : null;
        pending = new(npc, update, peers.ToArray(), peerUpdates, changePeer,
            trusted.SourceRandom, beforeRandom, afterRandom, egg, state.Force);
        next = State(in npc);
        return true;
    }

    internal bool TryGetAcceptedPlan(in NpcSnapshot before, in NpcSnapshot accepted,
        INpcAiCommittedNpcMutationSink mutations, out NpcStateUpdate update)
    {
        update = default;
        if (pending is not { } plan || plan.Before != before || accepted.Handle != before.Handle ||
            accepted.TypeIdentity != before.TypeIdentity || !Validate(plan, in accepted, mutations))
            return false;
        update = plan.Update;
        return true;
    }

    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted,
        in NpcStateUpdate final, INpcAiCommittedNpcMutationSink mutations)
    {
        if (pending is not { } plan || plan.Before != before || !Validate(plan, in accepted, mutations) ||
            !mutations.TryUpdateState(in accepted, in final, out var completed))
            return default;
        if (before.Type != final.Type)
        {
            // SetDefaults during Transform changes baselines. A same-type, unpublished write restores the
            // source-verified child defaults without carrying old-type baselines through generic ownership.
            if (!mutations.TryUpdateState(in completed, in final, out var restored))
                return default;
            completed = restored;
        }
        if (!Validate(plan, in completed, mutations))
            return default;
        plan.Owner.CopyStateFrom(plan.AfterRandom);
        for (int index = 0; index < plan.Peers.Length; index++)
            if (plan.ChangePeer[index] && !mutations.TryUpdateState(in plan.Peers[index], in plan.PeerUpdates[index], out _))
                return default;
        if (plan.Egg is { } egg)
            mutations.TrySpawn(in completed, in egg, out _);
        completedBefore = before;
        completedAfter = completed;
        completedForce = plan.Force;
        pending = null;
        return completed;
    }

    internal bool RequiresForcedUpdate(in NpcSnapshot before, in NpcSnapshot after) =>
        completedBefore == before && completedAfter == after && completedForce;

    private static bool Validate(Plan plan, in NpcSnapshot source, INpcAiCommittedNpcMutationSink mutations)
    {
        if (!plan.Owner.HasSameState(plan.BeforeRandom) ||
            !mutations.TryGetActive(source.Handle.Slot, out var current) || current != source)
            return false;
        int count = 0;
        for (int slot = 0; slot < 200; slot++)
        {
            if (!mutations.TryGetActive((byte)slot, out var peer) || peer.Handle == source.Handle ||
                (peer.TypeIdentity != VanillaNpcIds.MothronEgg && peer.TypeIdentity != VanillaNpcIds.BabyMothron))
                continue;
            if (count >= plan.Peers.Length || peer != plan.Peers[count++])
                return false;
        }
        return count == plan.Peers.Length && plan.Owner.HasSameState(plan.BeforeRandom);
    }

    private static bool TryTarget(in NpcSnapshot npc, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out VanillaNpcTargetCandidate target)
    {
        target = default;
        ushort slot = npc.Target;
        if ((npc.TypeIdentity == VanillaNpcIds.Mothron && npc.Ai.Ai0 is 0f or 4f) ||
            (npc.TypeIdentity == VanillaNpcIds.BabyMothron && npc.Ai.Ai0 == 0f))
        {
            if (!context.TrySelectClosestTarget(in npc, in definition, out var closest))
                return false;
            slot = closest.Target;
        }
        return slot < 255 && context.TryFindCandidate((byte)slot, out target) && target.Active &&
            !target.Dead && !target.Ghost && !target.NoAggro && !(target.Aggro < 0 && target.ItemAnimation == 0);
    }

    private static bool Finite(in NpcStateUpdate update) =>
        float.IsFinite(update.PositionX) && float.IsFinite(update.PositionY) &&
        float.IsFinite(update.VelocityX) && float.IsFinite(update.VelocityY) &&
        update.Ai.IsFinite && update.Simulation.IsValid;

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId,
        npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
}
