using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Application;

// Private AI history is attached to the exact projectile generation and connection.
// It contains no inventory or reward budget; payment remains in PlayerAuthority.
internal sealed record FishingBobber1458(ProjectileSnapshot Snapshot, RuntimePlayerMember Member,
    ConnectionHandle Connection, bool Returning = false, float WaitTimer = 0,
    float BiteClock = 0, FishingResult1458 Result = default, bool PaidCatch = false);

internal sealed partial class ProjectileAuthority
{
    private readonly FishingBobber1458?[] fishingBobbers = new FishingBobber1458?[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];
    private NpcAuthority? fishingNpcs;
    // Projectile._context.Random belongs to the static FishDropsDB context in
    // vanilla, separately from Main.rand. One owner per logical world is required.
    private readonly VanillaUnifiedRandom1458 fishingDropRandom = new(Environment.TickCount);

    internal void BindFishingNpcs(NpcAuthority owner) => fishingNpcs = owner;

    internal FishingBobber1458? CaptureFishingBobber(ConnectionHandle connection)
    {
        foreach (var bobber in fishingBobbers)
            if (bobber is not null && bobber.Connection == connection && IsFishingCurrent(bobber)) return bobber;
        return null;
    }

    internal bool IsFishingCurrent(FishingBobber1458 bobber) =>
        ReferenceEquals(fishingBobbers[bobber.Snapshot.Handle.Slot], bobber) &&
        projectiles.TryGet(bobber.Snapshot.Handle, out var current) && current == bobber.Snapshot;

    internal bool TryCommitFishingBirth(ConnectionHandle connection)
    {
        if (replication is null || CaptureFishingBobber(connection) is not null ||
            !players.TryGetFishingLaunch(connection, out var member, out var action, out var report) ||
            !players.TryCaptureProjectileUse(connection, out var actor) || actor is null ||
            action.Pose.Player.HasMount || action.Pose.GravityDirection != 1 ||
            report.ProjectileType != action.Pole.ProjectileType ||
            replication.WireIdentities.TryResolve(report.Key, out _)) return false;
        float length = MathF.Sqrt(report.VelocityX * report.VelocityX + report.VelocityY * report.VelocityY);
        if (!float.IsFinite(length) || MathF.Abs(length - action.Pole.ShootSpeed) > .002f) return false;
        var state = new ProjectileStateUpdate(new(action.Pole.ProjectileType), connection.Player.Slot.Value,
            action.Pose.Player.PositionX + 3, action.Pose.Player.PositionY + 14,
            report.VelocityX, report.VelocityY, default, 0, 0, 0, 0);
        if (MathF.Abs(report.PositionX - state.PositionX) > .002f ||
            MathF.Abs(report.PositionY - state.PositionY) > .002f ||
            !projectiles.TryPrepareVanillaSpawn(state, null, out var spawn) || spawn is null) return false;
        var preview = spawn.PreparedBirth;
        if (!replication.TryPrepareSpawnBirthJournal(new[] { preview }, new[] { report.Key }, connection.Source, out var journal) ||
            journal is null || !spawn.IsCurrent || !journal.IsCurrentOwned || !players.IsCurrentProjectileUse(actor)) return false;
        if (!spawn.TryCommitUnpublished(out var born) || !projectiles.TryMarkCombatTrusted(born.Handle, connection.Player))
            throw new InvalidOperationException("Prepared fishing birth lost projectile ownership.");
        players.AdoptFishingLaunch(member, action);
        fishingBobbers[born.Handle.Slot] = new(born, member, connection);
        if (!journal.TryAdoptBaselines(projectiles)) throw new InvalidOperationException("Fishing birth lost its wire identity.");
        AppliedSpawns++; PromotedClientProjectileSpawns++;
        journal.TryPublish(projectiles);
        using var scope = journal.EnterObserverPublication();
        spawn.TryPublish(born);
        return true;
    }

    internal void CommitFishingPull(ConnectionHandle connection, FishingItemAction1458 action)
    {
        if (action.Pull is not { } before || before.Connection != connection || !IsFishingCurrent(before) ||
            !players.TryGet(connection, out var member) || !ReferenceEquals(member, before.Member) ||
            member.ItemPhase?.ToolUse?.FishingAction != action) return;
        var snapshot = before.Snapshot;
        bool caught = action.Caught && !action.BrokeLine;
        bool npc = caught && before.Result.NpcType != 0;
        var ai = new ProjectileAiState(npc || action.BrokeLine ? 2 : 1,
            caught && !npc ? before.Result.ItemType : 0, 0);
        if (!projectiles.TryGetLifecycle(snapshot.Handle, out var lifecycle) ||
            !projectiles.TryPrepareSimulation(snapshot, out var plan) || plan is null) return;
        var update = SnapshotToUpdate(snapshot) with { Ai = ai,
            VelocityY = lifecycle.Liquid.Wet ? Math.Min(-10, snapshot.VelocityY) : snapshot.VelocityY };
        if (!plan.TryStageMotion(update, lifecycle) || !plan.TryQueuePublication() || !plan.IsCurrent ||
            !plan.TryAdoptUnpublished(out var accepted, out _)) return;
        // The paid source phase has already consumed bait, and this one-shot
        // receipt is retired before any projectile/NPC/network observer runs.
        players.AdoptFishingLaunch(member, action);
        fishingBobbers[snapshot.Handle.Slot] = before with { Snapshot = accepted, Returning = true,
            PaidCatch = caught && !npc, BiteClock = 0 };
        players.AdoptConsumedFishingBait(member, action);
        try
        {
            if (npc) fishingNpcs?.TrySpawnFishingNpc(connection, before.Result.NpcType, snapshot.PositionX, snapshot.PositionY);
        }
        finally { PublishFishing(plan, true); }
    }

    private ProjectileActorSimulationResult TrySimulateFishingBobber(ProjectileSnapshot initial)
    {
        if (initial.Handle.Slot >= fishingBobbers.Length || fishingBobbers[initial.Handle.Slot] is not { } before ||
            before.Snapshot != initial || worldTiles is null || stepper is not VanillaProjectileWorldStateStepper concrete ||
            !projectiles.TryGetLifecycle(initial.Handle, out var lifecycle) || fishingNpcs is null)
            return ProjectileActorSimulationResult.Rejected;
        bool alive = players.TryGet(before.Connection, out var member) && ReferenceEquals(member, before.Member) && !member.IsDead;
        bool supported = alive &&
            (member!.MovementFlags & 1) == 0 &&
            players.TryPrepareHeldToolLifecycle(member!, before.Connection.Player, 0, out var held) && held is not null &&
            !held.Inactive && !held.CrowdControlled && !held.NoItems &&
            FishingRules1458.TryGetPole(held.Selected.ItemType.Value, out var pole) &&
            (pole.ProjectileType == initial.Type.Value || initial.Type.Value is >= 986 and <= 993);
        if (!supported)
        {
            // Source AI61 Kill still runs GiveItem for an already paid positive
            // ai1 when selection changes. A pending bite has no such entitlement.
            if (alive && before.PaidCatch && fishingNpcs.TryCaptureFishingWorld(member!, out _, out var endingWeather, out _) &&
                players.TryCaptureFishingPlayer(member!, endingWeather, out var endingOwner, ending: true) && endingOwner is not null)
                return EndFishingBobber(initial, before, true, endingOwner.Conditions.FinalFishingLevel);
            return EndFishingBobber(initial, before, false, 0);
        }
        if (!fishingNpcs.TryCaptureFishingWorld(member!, out var world, out var weather, out var biomes) ||
            !players.TryCaptureFishingPlayer(member!, weather, out var owner) || owner is null)
            return ProjectileActorSimulationResult.Rejected;
        var randomBefore = projectileRandom.Clone(); var random = randomBefore.Clone();
        var dropBefore = fishingDropRandom.Clone(); var drop = dropBefore.Clone();
        var draws = new FishingRandom1458(random);
        var dropDraws = new FishingRandom1458(drop);
        float x = initial.PositionX, y = initial.PositionY, vx = initial.VelocityX, vy = initial.VelocityY;
        float dx = member!.PositionX + 10 - (x + 7), dy = member.PositionY + 21 - (y + 7);
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        // AI061 changes ai0 after entering the cast branch. Crossing 900 pixels
        // therefore starts homing on the following update, with this tick's
        // ordinary cast collision and velocity still retained.
        bool returning = before.Returning;
        bool net = false;
        var next = before;
        var aiLiquid = lifecycle.Liquid;
        if (returning)
        {
            if (distance > 3000 || x < member.PositionX + 20 && x + 14 > member.PositionX &&
                y < member.PositionY + 42 && y + 14 > member.PositionY)
                return EndFishingBobber(initial, before, before.PaidCatch, owner.Conditions.FinalFishingLevel);
            if (distance == 0) return EndFishingBobber(initial, before, before.PaidCatch, owner.Conditions.FinalFishingLevel);
            vx = (vx * 9 + dx * 15.9f / distance) / 10;
            vy = (vy * 9 + dy * 15.9f / distance) / 10;
        }
        else
        {
            if (distance > 900) next = next with { Returning = true };
            bool settled = false;
            if (lifecycle.Liquid.Wet)
            {
                if (lifecycle.Liquid.ShimmerWet) return EndFishingBobber(initial, before, false, 0);
                vx *= .9f;
                if (vy > 0) vy *= .5f;
                float line = FishingRules1458.GetWaterLine((int)((x + 7) / 16), (int)((y + 7) / 16), y + 14, FishingCell);
                if (y + 7 > line)
                {
                    vy = Math.Max(-8, vy - .1f);
                    if (y + 7 + vy < line) vy = line - (y + 7);
                }
                else vy = line - (y + 7);
                settled = vy is >= -.01f and <= .01f;
            }
            else
            {
                if (vy == 0) vx *= .95f;
                vx *= .98f; vy = Math.Min(15.9f, vy + .2f);
            }
            if (before.BiteClock < 0)
            {
                if (vy == 0 || lifecycle.Liquid.HoneyWet && Math.Abs(vy) <= .01f)
                {
                    vy = random.Next(100, 500) * .015f; vx = random.Next(-100, 101) * .015f;
                    // The impulse clears these source flags before the current
                    // Update water-contact pass reconstructs them from terrain.
                    aiLiquid = default;
                }
                float bite = before.BiteClock + random.Next(1, 5);
                next = next with { BiteClock = Math.Min(0, bite) };
                if (bite >= 0) { next = next with { WaitTimer = 0, Result = default }; net = true; }
            }
            else if (settled)
            {
                float wait = FishingRules1458.AdvanceWait(before.WaitTimer, owner.Conditions.FinalFishingLevel, draws);
                if (wait > 660)
                {
                    wait = 0;
                    int px = (int)((x + 7) / 16), py = (int)((y + 7) / 16);
                    var pond = FishingRules1458.MeasurePond(px, py, world.Width, world.Height, FishingCell);
                    FishingResult1458 result = default;
                    bool canAttempt = member.PhysicsPhase?.Contacts.Wet != true ||
                        y + 7 >= member.PositionY + 21;
                    if (canAttempt && owner.Conditions.BaitItemType == 2673)
                        result = FishingRules1458.RollSpecialBait(px, pond, world, draws);
                    else if (canAttempt && FishingRules1458.TryPrepareAttempt(px, py, y, owner.Conditions, world, biomes,
                        pond, owner.Luck, owner.CratePotion, false, players.CaptureFishingQuestFish(before.Connection), draws, out var attempt))
                    {
                        FishingRules1458.ConsumePreviewRandom(attempt, draws, dropDraws, FishingCell, FishingSolidRectangle);
                        result = FishingRules1458.RollOutcome(attempt, draws, dropDraws, owner.SonarPotion);
                        wait += result.WaitBonus;
                    }
                    if (result.HasBite) { next = next with { Result = result, BiteClock = result.BiteClock }; net = true; }
                }
                next = next with { WaitTimer = wait };
            }
        }
        if (!concrete.TryCaptureToolTerrain(initial.Type, x, y, vx, vy, !returning,
                aiLiquid, lifecycle.SourceWetCount, out var terrain)) return ProjectileActorSimulationResult.Rejected;
        // AI61's OnTileCollide rebounds changed axes, retaining ordinary wet movement.
        float incomingX = vx, incomingY = vy;
        vx = terrain.ClampedX; vy = terrain.ClampedY;
        if (terrain.HasImpact)
        {
            if (vx != incomingX) vx = incomingX * -.3f;
            if (vy != incomingY && incomingY > 1) vy = incomingY * -.3f;
        }
        if (returning && terrain.Liquid.Wet)
            terrain = terrain with { WetVelocityX = initial.VelocityX, WetVelocityY = initial.VelocityY };
        (x, y) = VanillaProjectileWorldMotionResolver.CompleteToolPosition(terrain, vx, vy);
        if (x <= 0 || y <= 0 || x + 14 >= world.Width * 16 || y + 14 >= world.Height * 16)
            return EndFishingBobber(initial, before, false, 0);
        var state = SnapshotToUpdate(initial) with { PositionX = x, PositionY = y, VelocityX = vx, VelocityY = vy,
            Ai = new(next.Returning ? returning ? initial.Ai.Ai0 == 2 ? 3 : Math.Max(1, initial.Ai.Ai0) : 1 : 0,
                returning ? initial.Ai.Ai1 : next.BiteClock, 0) };
        var life = lifecycle with { TimeLeft = 59, Liquid = terrain.Liquid, SourceWetCount = terrain.WetCount };
        if (!projectiles.TryPrepareSimulation(initial, out var plan) || plan is null ||
            !plan.TryStageMotion(state, life) || !plan.TryQueuePublication() || !plan.IsCurrent ||
            !IsFishingCurrent(before) || !players.IsCurrentProjectileUse(owner.Actor) ||
            !projectileRandom.HasSameState(randomBefore) || !fishingDropRandom.HasSameState(dropBefore))
            return ProjectileActorSimulationResult.Rejected;
        if (!plan.TryAdoptUnpublished(out var accepted, out _)) return ProjectileActorSimulationResult.Rejected;
        projectileRandom.CopyStateFrom(random); fishingDropRandom.CopyStateFrom(drop);
        fishingBobbers[initial.Handle.Slot] = next with { Snapshot = accepted };
        PublishFishing(plan, net);
        return ProjectileActorSimulationResult.Applied;
    }

    private ProjectileActorSimulationResult EndFishingBobber(ProjectileSnapshot initial,
        FishingBobber1458 before, bool grant, int currentFishingPower)
    {
        if (!projectiles.TryGetLifecycle(initial.Handle, out var lifecycle) ||
            !projectiles.TryPrepareSimulation(initial, out var ending) || ending is null ||
            !ending.TryStageMotion(SnapshotToUpdate(initial), lifecycle with { TimeLeft = 0 }) ||
            !ending.TryQueueFishingDespawn()) return ProjectileActorSimulationResult.Rejected;
        bool Current() => IsFishingCurrent(before) && ending.IsCurrent;
        void Adopt()
        {
            if (!ending.TryAdoptUnpublished(out _, out bool expired) || !expired)
                throw new InvalidOperationException("Fishing termination lost its generation.");
            fishingBobbers[initial.Handle.Slot] = null;
        }
        try
        {
            if (grant && before.Result.ItemType > 0)
            {
                if (!players.TryFishingRewards(before.Connection, -1, 0, false,
                    reward => reward.Grant(before.Result.ItemType,
                        FishingRules1458.GetCatchStack(before.Result.ItemType, currentFishingPower, new FishingRandom1458(reward.Random)),
                        0, canGoIntoVoidVault: false), Current, Adopt))
                    return ProjectileActorSimulationResult.Rejected;
            }
            else { if (!Current()) return ProjectileActorSimulationResult.Rejected; Adopt(); }
            return ProjectileActorSimulationResult.Applied;
        }
        finally
        {
            // A reward/NPC observer may throw after payment and removal already
            // committed. The terminal wire journal must still drain exactly once.
            if (!ending.IsCurrent) PublishFishing(ending, false);
        }
    }

    private void PublishFishing(RuntimeProjectileStore.SimulationPreparation plan, bool netUpdate)
    {
        if (replication is not null) replication.TryPublishFishingActor(plan, netUpdate);
        else while (plan.TryPublishNext()) { }
    }

    private FishingWaterCell1458 FishingCell(int x, int y)
    {
        if (worldTiles is null || x < 0 || y < 0 || x >= worldTiles.Dimensions.WidthTiles || y >= worldTiles.Dimensions.HeightTiles)
            return new(0, 0, true);
        var tile = worldTiles.Get(x, y);
        bool solid = tile.IsActive && (tile.Flags & WorldTileFlags.Inactive) == 0 &&
            !tile.IsHalfBrick && tile.SourceSlope == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        return new(tile.LiquidAmount, (byte)tile.LiquidKind, solid);
    }

    private bool FishingSolidRectangle(int x, int y, int width, int height)
    {
        for (int column = x / 16; column <= (x + width) / 16; column++)
            for (int row = y / 16; row <= (y + height) / 16; row++)
                if (FishingCell(column, row).Solid) return true;
        return false;
    }
}
