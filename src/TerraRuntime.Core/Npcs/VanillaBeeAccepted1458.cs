using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

/// <summary>Retained AI005 owner for the two source Hive children. Other flyers keep their own strategies.</summary>
internal sealed class VanillaBeeAccepted1458
{
    private RuntimeNpcStore? store;
    private IVanillaFlyingEyeRetainedEnvironment1458? environment;
    private SystemVanillaNpcRandom? random;
    private Plan? retained;
    private NpcSnapshot completedBefore;
    private NpcSnapshot completedAfter;
    private bool completedInactive;

    private sealed record Plan(NpcSnapshot Before, NpcStateUpdate Update,
        RuntimeNpcStore.AiSpawnPreview Preview, VanillaNpcBehaviorContext Context,
        VanillaNpcTargetCandidate[] Candidates, NpcRawPlayerSlotSnapshot1458[] Raw,
        IVanillaFlyingEyeWorldFence1458 World, bool DayTime, double Surface);

    internal bool IsConfigured => store is not null && environment is not null && random is not null;

    internal void Configure(RuntimeNpcStore owner, IVanillaFlyingEyeEnvironment world, IVanillaNpcRandom sourceRandom)
    {
        store = owner ?? throw new ArgumentNullException(nameof(owner));
        environment = world as IVanillaFlyingEyeRetainedEnvironment1458;
        random = sourceRandom as SystemVanillaNpcRandom;
    }

    internal static bool IsBee(NpcTypeId type) => type == VanillaNpcIds.Bee || type == VanillaNpcIds.SmallBee;
    internal bool HasPlan(in NpcSnapshot before) => retained?.Before == before;
    internal void Cancel() => retained = null;

    internal bool CanAdmitChild(in NpcSnapshot child, VanillaNpcBehaviorContext context)
    {
        if (!IsConfigured || !context.HasRawPlayerSlots || !IsBee(child.TypeIdentity) ||
            child.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !VanillaNpcDefinitionCatalog.TryGet(child.TypeIdentity, child.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(child.Simulation, out _) ||
            !context.TryCaptureRawPlayer(byte.MaxValue, out _))
            return false;
        for (int slot = 0; slot < store!.Capacity; slot++)
        {
            if (!store.TryGetActive((byte)slot, out var peer))
                continue;
            if (slot >= 200 || !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var peerDefinition) ||
                !peerDefinition.TryResolveHitbox(peer.Simulation, out _) || peer.Simulation.Chaseable is null ||
                peer.Simulation.Friendly is null || peer.Simulation.Immortal is null)
                return false;
        }
        foreach (var candidate in context.Candidates)
            if (candidate.Active && (!context.TryCaptureRawPlayer(candidate.Slot, out var player) ||
                !Matches(candidate, player.Facts) || player.Facts.NoAggro || player.Facts.Aggro < 0))
                return false;
        return true;
    }

    internal bool TryStep(in NpcSnapshot before, in VanillaNpcDefinition definition,
        VanillaNpcBehaviorContext context, out NpcStateUpdate next)
    {
        retained = null;
        completedInactive = false;
        next = default;
        if (!IsConfigured || !context.HasRawPlayerSlots || !IsBee(before.TypeIdentity) ||
            before.Simulation.LifeMax <= 0 || before.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer ||
            !definition.TryResolveHitbox(before.Simulation, out var body) ||
            !store!.TryCreateAiSpawnPreview(in before, random!.SourceRandom, out var preview))
            return false;

        var candidates = context.Candidates.ToArray();
        bool dayTime = context.DayTime;
        double surface = context.WorldSurfacePixels;
        var raw = new List<NpcRawPlayerSlotSnapshot1458>(candidates.Length + 2);
        // This genuine constructor slot also carries the global raw-owner/membership stamp.
        if (!context.TryCaptureRawPlayer(byte.MaxValue, out var sentinel))
            return false;
        raw.Add(sentinel);
        var targets = new List<VanillaBeeTargetBody1458>(store.Capacity + candidates.Length);
        for (int slot = 0; slot < store.Capacity; slot++)
        {
            if (!store.TryGetActive((byte)slot, out var peer) || peer.Handle == before.Handle)
                continue;
            if (slot >= 200 || !VanillaNpcDefinitionCatalog.TryGet(peer.TypeIdentity, peer.NetIdentity, out var peerDefinition) ||
                !peerDefinition.TryResolveHitbox(peer.Simulation, out var peerBody) ||
                peer.Simulation.Chaseable is null || peer.Simulation.Friendly is null || peer.Simulation.Immortal is null)
                return false;
            bool excluded = IsBee(peer.TypeIdentity) || peer.TypeIdentity == VanillaNpcIds.QueenBee ||
                peer.TypeIdentity == VanillaNpcIds.BlueSlime && peer.Ai.Ai1 is 1124f or 1125f;
            targets.Add(new((ushort)(300 + slot), peer.PositionX, peer.PositionY, peerBody.Width,
                peerBody.Height, !excluded && VanillaNpcChaseability1458.CanBeChasedBy(in peer)));
        }
        foreach (var candidate in candidates)
        {
            if (!candidate.Active)
                continue;
            if (!context.TryCaptureRawPlayer(candidate.Slot, out var player) || !Matches(candidate, player.Facts) ||
                player.Facts.NoAggro || player.Facts.Aggro < 0)
                return false;
            raw.Add(player);
            targets.Add(new(candidate.Slot, player.Facts.PositionX, player.Facts.PositionY,
                player.Facts.Width, player.Facts.Height, !player.Facts.Dead && !player.Facts.Ghost, player.Facts.Aggro));
        }
        if (!VanillaBeeTarget1458.TrySelect(before.PositionX, before.PositionY, body.Width, body.Height,
            before.Target, before.Simulation.DirectionX, before.Simulation.DirectionY,
            targets.ToArray(), out var selection))
            return false;

        // GetTargetData returns a typed None/zero rectangle when the retained target is invalid.
        // A failed search preserves the old index and facing; it does not select constructor Player0.
        VanillaBeeTargetBody1458 targetData = default;
        foreach (var target in targets)
        {
            if (target.Target != selection.Target)
                continue;
            bool valid = target.Target >= 300 || target.Eligible;
            if (valid)
                targetData = target;
            break;
        }
        var queryTarget = new VanillaNpcTargetCandidate(0, targetData.X + targetData.Width * .5f,
            targetData.Y + targetData.Height * .5f, 0, false, false, false, false)
        {
            HitboxWidth = Math.Max(1, targetData.Width),
            HitboxHeight = Math.Max(1, targetData.Height)
        };
        if (!environment!.TryCapture(in before, in queryTarget, in queryTarget, out var world) ||
            !VanillaFlyerNpcCatalog.TryGetMotionProfile(before.TypeIdentity, out var profile))
            return false;
        var input = new VanillaFlyerAiMotionInput(before.PositionY,
            before.PositionX + body.Width * .5f, before.PositionY + body.Height * .5f,
            before.VelocityX, before.VelocityY, targetData.X + targetData.Width / 2,
            targetData.Y + targetData.Height / 2, targetData.Y,
            before.Simulation.OldVelocityX, before.Simulation.OldVelocityY, selection.DirectionX,
            before.Ai, before.Simulation.Scale, before.Simulation.CollideX, before.Simulation.CollideY,
            before.Simulation.Wet, dayTime, context.ExpertMode, surface, before.Simulation.TimeLeft);
        if (!VanillaFlyerAiMotion.TryStep(before.TypeIdentity, in input, in profile, out var motion))
            return false;
        var update = State(in before) with
        {
            Target = selection.Target,
            VelocityX = motion.VelocityX,
            VelocityY = motion.VelocityY,
            Ai = motion.Ai,
            Simulation = before.Simulation with
            {
                NoGravity = true,
                DirectionX = selection.DirectionX,
                DirectionY = selection.DirectionY,
                TimeLeft = motion.TimeLeft
            }
        };
        var plan = new Plan(before, update, preview!, context, candidates, raw.ToArray(), world, dayTime, surface);
        if (!InputsCurrent(plan) || !preview!.IsBeforeCurrent() || !RuntimeNpcStore.IsValid(in update))
            return false;
        retained = plan;
        next = State(in before);
        return true;
    }

    internal bool TryGetPlan(in NpcSnapshot before, in NpcSnapshot accepted, out NpcStateUpdate update)
    {
        update = default;
        if (retained is not { } plan || plan.Before != before ||
            !InputsCurrent(plan) || !plan.Preview.IsCurrent(in accepted))
            return false;
        update = plan.Update;
        return true;
    }

    internal NpcSnapshot Complete(in NpcSnapshot before, in NpcSnapshot accepted, in NpcStateUpdate final)
    {
        if (retained is not { } plan || plan.Before != before || !InputsCurrent(plan) ||
            !VanillaNpcDefinitionCatalog.TryGet(before.TypeIdentity, before.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(final.Simulation, out var body))
        {
            retained = null;
            return default;
        }
        Span<VanillaNpcRawPlayer1458> players = stackalloc VanillaNpcRawPlayer1458[plan.Raw.Length];
        int count = 0;
        foreach (var snapshot in plan.Raw)
            if (snapshot.Facts.Active)
                players[count++] = snapshot.Facts;
        if (!VanillaOrdinarySlimeCheckActive1458.TryStep(final.PositionX, final.PositionY, body.Width,
            body.Height, final.Simulation.TimeLeft, players[..count], out int lifetime, out bool inactive))
        {
            retained = null;
            return default;
        }
        var simulation = final.Simulation with
        {
            TimeLeft = lifetime,
            Rotation = final.VelocityX * .2f,
            SpriteDirection = final.VelocityX > 0f ? 1 : final.VelocityX < 0f ? -1 : final.Simulation.SpriteDirection
        };
        var completedState = final with { Simulation = simulation };
        if (!InputsCurrent(plan) || !plan.Preview.TryAdopt(in accepted, in completedState, out var completed))
        {
            retained = null;
            return default;
        }
        retained = null;
        completedBefore = before;
        completedAfter = completed;
        completedInactive = inactive;
        return completed;
    }

    internal bool Deactivates(in NpcSnapshot before, in NpcSnapshot after) =>
        completedInactive && completedBefore == before && completedAfter == after;

    private static bool InputsCurrent(Plan plan)
    {
        if (plan.Context.DayTime != plan.DayTime || plan.Context.WorldSurfacePixels != plan.Surface ||
            !plan.Context.Candidates.SequenceEqual(plan.Candidates) || !plan.World.IsCurrent)
            return false;
        foreach (var snapshot in plan.Raw)
            if (!plan.Context.IsRawPlayerCurrent(in snapshot))
                return false;
        return plan.World.IsCurrent;
    }

    private static bool Matches(VanillaNpcTargetCandidate candidate, VanillaNpcRawPlayer1458 raw) =>
        candidate.Active == raw.Active && candidate.Dead == raw.Dead && candidate.Ghost == raw.Ghost &&
        candidate.NoAggro == raw.NoAggro && candidate.Aggro == raw.Aggro &&
        candidate.Width == raw.Width && candidate.Height == raw.Height &&
        candidate.CenterX == raw.PositionX + raw.Width * .5f &&
        candidate.CenterY == raw.PositionY + raw.Height * .5f;

    private static NpcStateUpdate State(in NpcSnapshot npc) =>
        new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY, npc.VelocityX, npc.VelocityY,
            npc.Target, npc.Ai, npc.Simulation);
}
