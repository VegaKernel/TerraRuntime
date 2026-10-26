using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

// Transient capability attached to one existing projectile generation. It carries no
// inventory image, random cursor, reward journal or persistent quantity of its own.
internal sealed class FishingClientClaim1458(ConnectionHandle connection, ProjectileHandle projectile,
    short baitSlot, RuntimePlayerInventoryItem baitBefore, FishingClientCatchContext1458 context, long tick)
{
    internal ConnectionHandle Connection { get; } = connection;
    internal ProjectileHandle Projectile { get; } = projectile;
    internal short BaitSlot { get; set; } = baitSlot;
    internal int BaitType => Context.Conditions.BaitItemType;
    internal RuntimePlayerInventoryItem BaitBefore { get; set; } = baitBefore;
    internal FishingClientCatchContext1458 InitialContext { get; } = context;
    internal FishingClientCatchContext1458 Context { get; set; } = context;
    internal FishingClientCatchContext1458? AlternativeContext { get; set; }
    internal FishingPowerContext1458 PowerContext { get; init; }
    internal int ContextCount => AlternativeContext is null ? 1 : 2;
    internal int SettledFramesAtBite { get; init; }
    internal bool BaitDebitObserved { get; set; }
    internal long CreatedTick { get; } = tick;
    internal float InitialBiteClock { get; set; }
    internal FishingBaitConsumption1458 Consumption { get; set; } = FishingBaitConsumption1458.Conserved | FishingBaitConsumption1458.Consumed;
    internal int ItemType { get; set; }
    internal int NpcType { get; set; }
    internal int MinimumStack { get; set; } = 1;
    internal int MaximumStack { get; set; } = 1;
    internal bool PullReported { get; set; }
    internal bool PaymentCommitted { get; set; }
    internal bool ConsumedBait { get; set; }
    internal bool ClientClosed { get; set; }
    internal bool NpcReported { get; set; }
    internal bool NpcSpawned { get; set; }
    internal bool RewardAdopted { get; set; }
    internal long ClosedTick { get; set; }
    internal RuntimeProjectileStore.SimulationPreparation? TerminalPublication { get; set; }
}

internal sealed class FishingClientActor1458(long tick, float launchSpeed)
{
    internal long BirthTick { get; } = tick;
    internal float MaximumMotionSpeed { get; } = Math.Max(15.9f, launchSpeed) + .002f;
    internal long LastReportTick { get; set; } = tick;
    internal long PullInputTick { get; set; } = long.MinValue;
    internal int SettledFrames { get; set; }
    internal int MaximumPower { get; set; }
    internal FishingClientClaim1458? Claim { get; set; }
    internal TerrariaProjectileUpdateState? PendingPull { get; set; }
    internal long PendingPullTick { get; set; }
    internal (int Type, int TileX, int TileY)? NpcCandidate { get; set; }
}

internal sealed partial class ProjectileAuthority
{
    internal bool TryCaptureFishingClientClaim(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = CaptureFishingBobber(connection)?.Client?.Claim;
        return claim is not null && IsFishingClientClaimCurrent(claim);
    }

    internal bool TryCaptureFishingClientPayment(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = CaptureFishingBobber(connection)?.Client?.Claim;
        return claim is not null && IsFishingClientClaimCurrent(claim) && !claim.PaymentCommitted;
    }

    internal bool TryCaptureFishingClientReward(ConnectionHandle connection, out FishingClientClaim1458? claim)
    {
        claim = CaptureFishingBobber(connection)?.Client?.Claim;
        return claim is { PullReported: true, PaymentCommitted: true, ClientClosed: true, ItemType: > 0,
            RewardAdopted: false } && IsFishingClientClaimCurrent(claim);
    }

    internal bool IsFishingClientClaimCurrent(FishingClientClaim1458 claim) =>
        claim.Projectile.Slot < fishingBobbers.Length && fishingBobbers[claim.Projectile.Slot] is { } bobber &&
        ReferenceEquals(bobber.Client?.Claim, claim) && bobber.Snapshot.Handle == claim.Projectile &&
        bobber.Connection == claim.Connection && IsFishingCurrent(bobber) && !claim.RewardAdopted &&
        players.TryGet(claim.Connection, out var member) && ReferenceEquals(member, bobber.Member) && !member.IsDead &&
        tickProvider() - (claim.ClientClosed ? claim.ClosedTick : claim.CreatedTick) <= 2400;

    internal void AdoptFishingClientPayment(FishingClientClaim1458 claim, bool consumed)
    {
        FishingBaitConsumption1458 decision = consumed ? FishingBaitConsumption1458.Consumed : FishingBaitConsumption1458.Conserved;
        if (!IsFishingClientClaimCurrent(claim) || !claim.PullReported || claim.PaymentCommitted ||
            (claim.Consumption & decision) == 0) throw new InvalidOperationException("Fishing payment lost its source capability.");
        var before = fishingBobbers[claim.Projectile.Slot]!;
        claim.PaymentCommitted = true; claim.ConsumedBait = consumed;
        fishingBobbers[claim.Projectile.Slot] = before with { PaidCatch = claim.ItemType > 0 };
        players.AdoptFishingClientBaitEffects(before.Member, before, consumed);
        if (claim.ClientClosed) RefreshFishingClientStackBounds(claim);
    }

    internal void AdoptFishingClientConsumedBait(FishingClientClaim1458 claim)
    {
        if (!IsFishingClientClaimCurrent(claim) || !claim.PaymentCommitted || claim.ConsumedBait ||
            (claim.Consumption & FishingBaitConsumption1458.Consumed) == 0)
            throw new InvalidOperationException("Fishing late bait debit lost its current capability.");
        claim.ConsumedBait = true;
        var before = fishingBobbers[claim.Projectile.Slot]!;
        players.AdoptFishingClientBaitEffects(before.Member, before, true);
        if (claim.ClientClosed) RefreshFishingClientStackBounds(claim);
    }

    internal void AdoptFishingClientReward(FishingClientClaim1458 claim)
    {
        if (!IsFishingClientClaimCurrent(claim) || !claim.ClientClosed || !claim.PaymentCommitted || claim.ItemType <= 0)
            throw new InvalidOperationException("Fishing reward lost its one-shot source capability.");
        var before = fishingBobbers[claim.Projectile.Slot]!;
        AdoptFishingClientTerminal(before, claim);
    }

    internal bool TryObserveFishingClientUpdate(ConnectionHandle connection, in ProjectileSnapshot current,
        in TerrariaProjectileUpdateState packet)
    {
        if (current.Handle.Slot >= fishingBobbers.Length || fishingBobbers[current.Handle.Slot] is not { Client: { } client } before)
            return false;
        if (before.Connection != connection || before.Snapshot != current || !IsFishingCurrent(before) ||
            !players.TryGet(connection, out var member) || !ReferenceEquals(member, before.Member) || member.IsDead ||
            packet.Ai2 != 0 || packet.Ai0 < 0 || packet.Ai0 > 3 || packet.Ai0 != MathF.Truncate(packet.Ai0) ||
            packet.BannerIdToRespondTo != 0 || packet.Damage != 0 || packet.OriginalDamage != 0 || packet.KnockBack != 0 ||
            !float.IsFinite(packet.Ai1) || packet.Ai1 != MathF.Truncate(packet.Ai1) || !float.IsFinite(packet.PositionX) || !float.IsFinite(packet.PositionY) ||
            !float.IsFinite(packet.VelocityX) || !float.IsFinite(packet.VelocityY) ||
            MathF.Abs(packet.VelocityX) > client.MaximumMotionSpeed || MathF.Abs(packet.VelocityY) > client.MaximumMotionSpeed || worldTiles is null ||
            packet.PositionX < 0 || packet.PositionY < 0 || packet.PositionX + 14 >= worldTiles.Dimensions.WidthTiles * 16 ||
            packet.PositionY + 14 >= worldTiles.Dimensions.HeightTiles * 16)
            return RefuseFishingClientUpdate();
        if (client.Claim is { ClientClosed: true }) return RefuseFishingClientUpdate();
        long elapsed = Math.Clamp(tickProvider() - client.LastReportTick, 0, 600);
        // Before the first local bite the admitted source trajectory is deterministic.
        // Eight pixels cover phase/float convergence near the waterline; random bite
        // impulses afterward are bounded by source per-frame speed instead.
        float travel = client.Claim is null ? 8 : (elapsed + 3) * client.MaximumMotionSpeed;
        if (MathF.Abs(packet.PositionX - current.PositionX) > travel || MathF.Abs(packet.PositionY - current.PositionY) > travel)
            return RefuseFishingClientUpdate();
        if (packet.Ai0 == 0)
        {
            if (before.Returning || packet.Ai1 > 0 || client.Claim is not null && packet.Ai1 < current.Ai.Ai1)
                return RefuseFishingClientUpdate();
            if (packet.Ai1 < 0 && client.Claim is null && !TryPrepareFishingClientBite(before, in packet))
                return RefuseFishingClientUpdate();
            else if (packet.Ai1 == 0 && client.Claim is { PullReported: false })
            { client.Claim = null; client.NpcCandidate = null; client.SettledFrames = 0; }
            return ApplyFishingClientReport(before, in packet);
        }
        if (before.Returning)
        {
            int expected = client.Claim?.ItemType ?? 0;
            if (packet.Ai1 > 0 && packet.Ai1 != expected) return RefuseFishingClientUpdate();
            return ApplyFishingClientReport(before, in packet);
        }
        // Source13 and source27 can be delivered on opposite sides of the owned
        // ItemCheck. Retain one bounded candidate; raw ai never pays or grants.
        if (tickProvider() - client.PullInputTick is < 0 or > 2)
        {
            if (client.PendingPull is { } prior && prior != packet) return RefuseFishingClientUpdate();
            client.PendingPull = packet; client.PendingPullTick = tickProvider(); return true;
        }
        return TryAdmitFishingClientPull(before, in packet);
    }

    private bool TryPrepareFishingClientBite(FishingBobber1458 before, in TerrariaProjectileUpdateState packet)
    {
        var client = before.Client!;
        // Master Bait2676 is the source normal-bait maximum50, used ONLY as a
        // provisional timing bound when27 precedes a legal inventory5 swap.
        // Pull still binds one of at most two actually owned source contexts.
        if (fishingNpcs is null || !fishingNpcs.TryCaptureFishingWorld(before.Member, out var world, out var weather, out var biomes) ||
            !players.TryCaptureFishingPlayer(before.Member, weather, out var owner) || owner is null ||
            client.SettledFrames < FishingRules1458.GetMinimumClientFishingWaitFrames(
                FishingRules1458.GetConditionsForPoleAndBait(owner.Conditions.PoleItemType, 2676,
                    owner.PowerContext with { CloudAlpha = 1 }).FinalFishingLevel) ||
            packet.Ai1 < -240 - Math.Ceiling(FishingRules1458.GetConditionsForPoleAndBait(
                owner.Conditions.PoleItemType, 2676, owner.PowerContext with { CloudAlpha = 1 }).FinalFishingLevel * 1.4) - 32)
            return false;
        int x = (int)((packet.PositionX + 7) / 16), y = (int)((packet.PositionY + 7) / 16);
        var pond = FishingRules1458.MeasurePond(x, y, world.Width, world.Height, FishingCell);
        if (pond.WaterTiles < 75 || before.Member.PhysicsPhase?.Contacts.Wet == true &&
            packet.PositionY + 7 < before.Member.PositionY + 21) return false;
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        if (!players.TryCaptureFishingClientBait(before.Connection, image, owner.Conditions.BaitItemType, out short slot, out var bait)) return false;
        var context = new FishingClientCatchContext1458(x, y, packet.PositionY, owner.Conditions, world, biomes, pond,
            owner.Luck, fishingNpcs.HasActiveFishingAngler ? players.CaptureFishingQuestFish(before.Connection) : 0);
        client.Claim = new(before.Connection, before.Snapshot.Handle, slot, bait, context, tickProvider())
            { InitialBiteClock = packet.Ai1, PowerContext = owner.PowerContext, SettledFramesAtBite = client.SettledFrames };
        return true;
    }

    internal void RefreshFishingClientBaitAlternative(ConnectionHandle connection)
    {
        if (!TryCaptureFishingClientClaim(connection, out var claim) || claim is null || claim.PullReported) return;
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        if (!players.TryCaptureFishingClientBait(connection, image, 0, out _, out var bait) ||
            bait.ItemType.Value == claim.InitialContext.Conditions.BaitItemType) return;
        var conditions = FishingRules1458.GetConditionsForPoleAndBait(claim.InitialContext.Conditions.PoleItemType,
            bait.ItemType.Value, claim.PowerContext);
        claim.AlternativeContext = claim.InitialContext with { Conditions = conditions };
    }

    private bool IsFishingClientContextTimingPossible(FishingClientClaim1458 claim, in FishingClientCatchContext1458 context)
    {
        int maximumPower = FishingRules1458.GetConditionsForPoleAndBait(context.Conditions.PoleItemType,
            context.Conditions.BaitItemType, claim.PowerContext with { CloudAlpha = 1 }).FinalFishingLevel;
        if (claim.SettledFramesAtBite < FishingRules1458.GetMinimumClientFishingWaitFrames(maximumPower)) return false;
        if (context.Conditions.BaitItemType == 2673) return claim.InitialBiteClock >= -280;
        int maximumLevel = maximumPower + (context.Pond.ChumCount > 0 ? 11 : 0) +
            (context.Pond.ChumCount > 1 ? 6 : 0) + (context.Pond.ChumCount > 2 ? 3 : 0);
        if (context.Luck > 0) maximumLevel = (int)Math.Ceiling(maximumLevel * 1.4);
        return claim.InitialBiteClock >= -240 - maximumLevel;
    }

    private bool TryBindFishingClientContext(FishingClientClaim1458 claim, in FishingClientCatchContext1458 context,
        int item, int npc)
    {
        if (!IsFishingClientContextTimingPossible(claim, context) ||
            (item > 0 || npc > 0) && !FishingRules1458.IsPossibleClientCatch(context, item, npc)) return false;
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        if (!players.TryCaptureFishingClientBait(claim.Connection, image, context.Conditions.BaitItemType,
            out short slot, out var bait)) return false;
        claim.Context = context; claim.BaitSlot = slot; claim.BaitBefore = bait;
        claim.Consumption = FishingRules1458.GetPossibleClientBaitConsumption(claim.BaitType, item, npc);
        return true;
    }

    internal bool TryMatchFishingClientBaitDebit(FishingClientClaim1458 claim, short slot,
        in RuntimePlayerInventoryItem reported)
    {
        if (!IsFishingClientClaimCurrent(claim) || claim.ConsumedBait || claim.RewardAdopted) return false;
        if (!claim.PullReported) RefreshFishingClientBaitAlternative(claim.Connection);
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        for (int option = 0; option < claim.ContextCount; option++)
        {
            var context = option == 0 ? claim.InitialContext : claim.AlternativeContext!.Value;
            if (!players.TryCaptureFishingClientBait(claim.Connection, image, context.Conditions.BaitItemType,
                out short expectedSlot, out var bait) || expectedSlot != slot ||
                reported != (bait.Stack == 1 ? default : bait with { Stack = (short)(bait.Stack - 1) }) ||
                !TryBindFishingClientContext(claim, context, claim.ItemType, claim.NpcType)) continue;
            if ((claim.Consumption & FishingBaitConsumption1458.Consumed) == 0) return false;
            claim.BaitDebitObserved = true;
            return true;
        }
        return false;
    }

    private bool TryAdmitFishingClientPull(FishingBobber1458 before, in TerrariaProjectileUpdateState packet)
    {
        var client = before.Client!;
        if (client.Claim is not { } claim)
        {
            if (packet.Ai1 > 0 || client.NpcCandidate is not null) return RefuseFishingClientUpdate();
            return ApplyFishingClientReport(before with { Returning = true }, in packet);
        }
        RefreshFishingClientBaitAlternative(before.Connection);
        int item = packet.Ai1 > 0 && packet.Ai1 == MathF.Truncate(packet.Ai1) ? (int)packet.Ai1 : 0;
        int npc = client.NpcCandidate?.Type ?? 0;
        bool bound = TryBindFishingClientContext(claim, claim.Context, item, npc);
        if (!bound && !claim.BaitDebitObserved)
            bound = TryBindFishingClientContext(claim, claim.InitialContext, item, npc) ||
                claim.AlternativeContext is { } alternative && TryBindFishingClientContext(claim, alternative, item, npc);
        if (item > 0 && npc > 0 || !bound ||
            item == 0 && npc == 0 && packet.Ai1 > 0 ||
            item > 0 && packet.Ai0 != 1 || npc == 370 && packet.Ai0 is not (2 or 3) ||
            npc is > 0 and not 370 && packet.Ai0 != 1 ||
            tickProvider() - claim.CreatedTick > -claim.InitialBiteClock + 2)
            return RefuseFishingClientUpdate();
        if (item == 0 && npc == 0)
        {
            client.Claim = null;
            return ApplyFishingClientReport(before with { Returning = true }, in packet);
        }
        Span<FishingInventorySlot1458> image = stackalloc FishingInventorySlot1458[59];
        if (!players.TryCaptureFishingClientBait(before.Connection, image, claim.BaitType, out short slot, out var bait))
            return RefuseFishingClientUpdate();
        if (client.NpcCandidate is { Type: not 370 } candidate &&
            (Math.Abs(candidate.TileX - (int)packet.PositionX / 16) > 2 ||
             Math.Abs(candidate.TileY - ((int)packet.PositionY + (npc == 618 ? 64 : 0)) / 16) > 2))
            return RefuseFishingClientUpdate();
        var bounds = FishingRules1458.GetClientCatchStackBounds(item, claim.Context.Conditions.FinalFishingLevel);
        return ApplyFishingClientReport(before with { Returning = true,
            Result = new(item, npc, packet.Ai1, claim.BaitType, claim.Context.Conditions.FinalFishingLevel) }, in packet, () =>
        {
            claim.BaitSlot = slot; claim.BaitBefore = bait; claim.ItemType = item; claim.NpcType = npc;
            claim.Consumption = FishingRules1458.GetPossibleClientBaitConsumption(claim.BaitType, item, npc);
            claim.PullReported = true; claim.NpcReported = npc > 0;
            claim.MinimumStack = bounds.Minimum; claim.MaximumStack = bounds.Maximum;
        });
    }

    internal bool TryObserveFishingClientDestroy(ConnectionHandle connection, in ProjectileSnapshot current,
        in TerrariaProjectileDestroyState packet)
    {
        if (current.Handle.Slot >= fishingBobbers.Length || fishingBobbers[current.Handle.Slot] is not { Client: { } client } before)
            return false;
        if (before.Connection != connection || before.Snapshot != current || !IsFishingCurrent(before)) return true;
        if (client.Claim is not { PullReported: true } claim)
        { _ = EndFishingBobber(current, before, false, 0); return true; }
        if (claim.ClientClosed) return true;
        bool atPlayer = packet.PositionX < before.Member.PositionX + 20 && packet.PositionX + 14 > before.Member.PositionX &&
            packet.PositionY < before.Member.PositionY + 42 && packet.PositionY + 14 > before.Member.PositionY;
        bool switched = !players.IsFishingClientPoleHeld(before.Member, current.Type.Value);
        if (!float.IsFinite(packet.PositionX) || !float.IsFinite(packet.PositionY) || (!atPlayer && !switched) ||
            MathF.Abs(packet.PositionX - current.PositionX) > 96 || MathF.Abs(packet.PositionY - current.PositionY) > 96)
        { RejectedClientDestroys++; return true; }
        claim.ClientClosed = true; claim.ClosedTick = tickProvider();
        // A late bait cohort can still change the last-unit conditions. Refresh
        // once more after actual payment adoption before the reward is admitted.
        RefreshFishingClientStackBounds(claim);
        return true;
    }

    internal bool TryGetFishingClientStackBounds(FishingClientClaim1458 claim,
        RuntimePlayerInventoryItem[] prepared, out int minimum, out int maximum)
    {
        minimum = maximum = 0;
        if (prepared.Length != 59 || !IsFishingClientClaimCurrent(claim)) return false;
        var before = fishingBobbers[claim.Projectile.Slot]!;
        if (fishingNpcs is null || !fishingNpcs.TryCaptureFishingWorld(before.Member, out _, out var weather, out _) ||
            !players.TryCaptureFishingPlayer(before.Member, weather, out var owner, ending: true, inventoryImage: prepared) || owner is null)
            return false;
        var bounds = FishingRules1458.GetClientCatchStackBounds(claim.ItemType, owner.Conditions.FinalFishingLevel);
        minimum = bounds.Minimum; maximum = bounds.Maximum;
        // cloudBGAlpha is rendered client history, unlike dedicated cloudAlpha.
        // Both source0 and sourcepositive branches are possible without asserting
        // an independently simulated visual/cloud random state.
        if (weather.CloudAlpha <= 0 && players.TryCaptureFishingPlayer(before.Member, weather with { CloudAlpha = 1 },
            out var cloudy, ending: true, inventoryImage: prepared) && cloudy is not null)
        {
            var other = FishingRules1458.GetClientCatchStackBounds(claim.ItemType, cloudy.Conditions.FinalFishingLevel);
            minimum = Math.Min(minimum, other.Minimum); maximum = Math.Max(maximum, other.Maximum);
        }
        return minimum > 0 && maximum >= minimum;
    }

    private void RefreshFishingClientStackBounds(FishingClientClaim1458 claim)
    {
        var before = fishingBobbers[claim.Projectile.Slot]!;
        if (fishingNpcs is not null && fishingNpcs.TryCaptureFishingWorld(before.Member, out _, out var weather, out _) &&
            players.TryCaptureFishingPlayer(before.Member, weather, out var owner, ending: true) && owner is not null)
        {
            var bounds = FishingRules1458.GetClientCatchStackBounds(claim.ItemType, owner.Conditions.FinalFishingLevel);
            claim.MinimumStack = bounds.Minimum; claim.MaximumStack = bounds.Maximum;
            if (weather.CloudAlpha <= 0 && players.TryCaptureFishingPlayer(before.Member, weather with { CloudAlpha = 1 },
                out var cloudy, ending: true) && cloudy is not null)
            {
                var other = FishingRules1458.GetClientCatchStackBounds(claim.ItemType, cloudy.Conditions.FinalFishingLevel);
                claim.MinimumStack = Math.Min(claim.MinimumStack, other.Minimum);
                claim.MaximumStack = Math.Max(claim.MaximumStack, other.Maximum);
            }
        }
        else if (claim.ItemType is 3196 or 3197) claim.MinimumStack = claim.MaximumStack = 0;
    }

    internal bool TryObserveFishingClientNpc(ConnectionHandle connection, int npcType, int tileX, int tileY)
    {
        if (npcType is not (370 or 586 or 587 or 618 or 620 or 621 or 682)) return false;
        var before = CaptureFishingBobber(connection);
        if (before?.Client is not { } client || client.Claim is not { } claim ||
            !IsFishingClientClaimCurrent(claim)) return true;
        RefreshFishingClientBaitAlternative(connection);
        if (!FishingRules1458.IsPossibleClientCatch(claim.Context, 0, npcType) &&
            !(claim.AlternativeContext is { } alternative && FishingRules1458.IsPossibleClientCatch(alternative, 0, npcType))) return true;
        if (npcType != 370)
        {
            // Source sends130 before the returning27. Validate a bounded candidate
            // now, then bind its exact tile location to that later pull report.
            long elapsed = Math.Clamp(tickProvider() - client.LastReportTick, 0, 600);
            float reach = (elapsed + 3) * client.MaximumMotionSpeed;
            float px = tileX * 16f, py = tileY * 16f - (npcType == 618 ? 64 : 0);
            if (worldTiles is null || tileX < 0 || tileY < 0 || tileX >= worldTiles.Dimensions.WidthTiles ||
                tileY >= worldTiles.Dimensions.HeightTiles || MathF.Abs(px - before.Snapshot.PositionX) > reach ||
                MathF.Abs(py - before.Snapshot.PositionY) > reach) return true;
        }
        if (client.NpcCandidate is { } prior && prior != (npcType, tileX, tileY)) return true;
        client.NpcCandidate = (npcType, tileX, tileY);
        if (claim.PullReported && claim.ItemType == 0 && (claim.NpcType == 0 || claim.NpcType == npcType))
        { claim.NpcType = npcType; claim.NpcReported = true; }
        return true;
    }

    internal void PublishFishingClientEffects(FishingClientClaim1458 claim)
    {
        TryPublishFishingClientNpc(claim);
        if (claim.TerminalPublication is { } terminal)
        {
            claim.TerminalPublication = null;
            PublishFishing(terminal, false);
        }
        else if (claim is { ClientClosed: true, PaymentCommitted: true, NpcType: > 0 } && IsFishingClientClaimCurrent(claim))
        {
            var before = fishingBobbers[claim.Projectile.Slot]!;
            AdoptFishingClientTerminal(before, claim);
            if (claim.TerminalPublication is { } despawn)
            { claim.TerminalPublication = null; PublishFishing(despawn, false); }
        }
    }

    private void AdoptFishingClientTerminal(FishingBobber1458 before, FishingClientClaim1458 claim)
    {
        if (!projectiles.TryGetLifecycle(before.Snapshot.Handle, out var life) ||
            !projectiles.TryPrepareSimulation(before.Snapshot, out var terminal) || terminal is null ||
            !terminal.TryStageMotion(SnapshotToUpdate(before.Snapshot), life with { TimeLeft = 0 }) ||
            !terminal.TryQueueFishingDespawn() || !terminal.IsCurrent || !IsFishingCurrent(before) ||
            !terminal.TryAdoptUnpublished(out _, out bool expired) || !expired)
            throw new InvalidOperationException("Fishing client completion lost its projectile generation.");
        claim.RewardAdopted = true;
        claim.TerminalPublication = terminal;
        fishingBobbers[before.Snapshot.Handle.Slot] = null;
    }

    private void TryPublishFishingClientNpc(FishingClientClaim1458 claim)
    {
        if (!claim.PaymentCommitted || !claim.PullReported || !claim.NpcReported || claim.NpcSpawned ||
            claim.NpcType <= 0 || !IsFishingClientClaimCurrent(claim)) return;
        var before = fishingBobbers[claim.Projectile.Slot]!;
        claim.NpcSpawned = true;
        if (claim.NpcType == 370 && fishingNpcs is not null &&
            fishingNpcs.TryCaptureFishingWorld(before.Member, out var currentWorld, out _, out _) && currentWorld.DukeFishronActive) return;
        var candidate = before.Client!.NpcCandidate;
        float x = claim.NpcType == 370 || candidate is null ? before.Snapshot.PositionX : candidate.Value.TileX * 16f;
        float y = claim.NpcType == 370 || candidate is null ? before.Snapshot.PositionY :
            candidate.Value.TileY * 16f - (claim.NpcType == 618 ? 64 : 0);
        fishingNpcs?.TrySpawnFishingNpc(claim.Connection, claim.NpcType, x, y);
    }

    private bool ApplyFishingClientReport(FishingBobber1458 before, in TerrariaProjectileUpdateState packet, Action? adopt = null)
    {
        var initial = before.Snapshot;
        if (!projectiles.TryGetLifecycle(initial.Handle, out var life) || !projectiles.TryPrepareSimulation(initial, out var plan) || plan is null ||
            !plan.TryStageMotion(SnapshotToUpdate(initial) with { PositionX = packet.PositionX, PositionY = packet.PositionY,
                VelocityX = packet.VelocityX, VelocityY = packet.VelocityY, Ai = new(packet.Ai0, packet.Ai1, 0) }, life) ||
            !plan.TryQueuePublication() || !plan.IsCurrent || !plan.TryAdoptUnpublished(out var accepted, out _)) return RefuseFishingClientUpdate();
        before.Client!.LastReportTick = tickProvider();
        fishingBobbers[initial.Handle.Slot] = before with { Snapshot = accepted, BiteClock = packet.Ai0 == 0 ? packet.Ai1 : 0 };
        adopt?.Invoke();
        PublishFishing(plan, true); AppliedUpdates++; return true;
    }

    private bool RefuseFishingClientUpdate() { RejectedTrustedClientUpdates++; RejectedClientUpdates++; return true; }

    private void AdmitPendingFishingClientPull(FishingBobber1458 before)
    {
        var client = before.Client!; client.PullInputTick = tickProvider();
        if (client.PendingPull is not { } report) return;
        client.PendingPull = null;
        if (tickProvider() - client.PendingPullTick <= 2) _ = TryAdmitFishingClientPull(before, in report);
    }
}
