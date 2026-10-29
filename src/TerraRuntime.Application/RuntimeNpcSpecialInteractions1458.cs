using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Actual caught Mystic Frog search and teleport/despawn, staged on the shared source cursor.</summary>
internal sealed class RuntimeMysticFrogCatchService1458(
    RuntimeNpcStore npcs, WorldTileStore tiles, INpcRawPlayerSlotLookup1458 players,
    SystemVanillaNpcRandom? sourceRandom, RuntimeNpcBuffStatus1458 status,
    RuntimeNpcReplicationRegistry? replication = null)
{
    private bool active;

    public bool TryApply(NpcHandle handle, out bool teleported, Func<bool>? callerCurrent = null)
    {
        teleported = false;
        if (active) return false;
        active = true;
        try { return TryApplyCore(handle, out teleported, callerCurrent); }
        finally { active = false; }
    }

    private bool TryApplyCore(NpcHandle handle, out bool teleported, Func<bool>? callerCurrent)
    {
        teleported = false;
        if (sourceRandom is null || !npcs.TryGet(handle, out var frog) || frog.Type != 687 ||
            !VanillaNpcDefinitionCatalog.TryGet(frog.TypeIdentity, frog.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(frog.Simulation, out var body) ||
            frog.Simulation.TeleportTime is null || frog.Simulation.TeleportStyle is null ||
            !status.CaptureInteraction(in frog, out var beforeStatus)) return false;
        var live = sourceRandom.SourceRandom;
        var beforeRandom = live.Clone();
        float centerX = frog.PositionX + body.Width * .5f, centerY = frog.PositionY + body.Height * .5f;
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || centerX < 0f || centerY < 0f ||
            centerX >= int.MaxValue || centerY >= int.MaxValue) return false;
        int targetX = (int)(centerX / 16f), targetY = (int)(centerY / 16f);
        // Source indexes its complete candidate search directly. Unrepresented edge histories
        // refuse before draws instead of silently changing the candidate sequence.
        if (targetX - 16 < 0 || targetX + 16 >= tiles.Dimensions.WidthTiles || targetY - 19 < 0 ||
            targetY + 15 >= tiles.Dimensions.HeightTiles) return false;
        var versions = CaptureTerrain(targetX - 16, targetY - 19, targetX + 16, targetY + 15);
        if (versions is null) return false;
        var raw = new NpcRawPlayerSlotSnapshot1458[256];
        for (int slot = 0; slot < raw.Length; slot++)
        {
            if (!players.TryCapture((byte)slot, out raw[slot]) || !TryPlayerBounds(raw[slot], out _)) return false;
        }
        if (!live.HasSameState(beforeRandom) || !npcs.TryCreateAiSpawnPreview(in frog, live, out var preview) || preview is null)
            return false;
        bool InputsCurrent()
        {
            if (callerCurrent is not null && !callerCurrent()) return false;
            foreach (var captured in raw) if (!players.IsCurrent(in captured)) return false;
            return status.IsCurrentInteraction(in frog, in beforeStatus) && TerrainCurrent(versions) && live.HasSameState(beforeRandom);
        }
        if (TryFindTeleportSpot(targetX, targetY, raw, preview.Random, out int x, out int y))
        {
            float newX = x * 16f - body.Width / 2, newY = y * 16f - body.Height;
            if (!TerrariaEntityTeleportCodec1458.TryEncode(frog.Handle.Slot, npc: true, newX, newY, 13,
                    useCurrentPosition: false, out var teleportFrame)) return false;
            var simulation = frog.Simulation with { TeleportTime = 1f, TeleportStyle = 13 };
            var update = new NpcStateUpdate(frog.Type, frog.NetId, newX, newY, frog.VelocityX, frog.VelocityY,
                frog.Target, frog.Ai, simulation);
            RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? publication = null;
            if (replication is not null &&
                (!replication.TryPrepareHistoricalCheckpoint(in frog, in update, npcs.HasPendingBirth(frog.Handle),
                    [teleportFrame], out publication) || !publication!.TrySealRecipients())) return false;
            // Both actual Main.TeleportEffect calls execute style13 caller arguments even on a dedicated host.
            for (int side = 0; side < 2; side++)
            for (int particle = 0; particle < 21; particle++)
            {
                _ = preview.Random.NextDouble();
                _ = preview.Random.NextDouble();
                _ = preview.Random.Next(1, 3);
            }
            if (!preview.TryAdoptDirect(in update, InputsCurrent, out var committed)) return false;
            if (!status.TryAdoptInteraction(in frog, in beforeStatus, in committed, materializedTransform: false, out var afterStatus))
                throw new InvalidOperationException("Preflighted caught-frog status lost callback-free adoption.");
            if (publication is not null && !publication.TryAdoptBaseline(npcs, in committed))
                throw new InvalidOperationException("Preflighted caught-frog baseline lost callback-free adoption.");
            teleported = true;
            if (publication is not null)
                publication.Publish(() => npcs.TryPublishHistoricalCheckpoint(in frog, in committed),
                    () => status.IsCurrentInteraction(in committed, in afterStatus));
            else npcs.TryPublishHistoricalCheckpoint(in frog, in committed);
            return true;
        }
        float smokeX = centerX - 20f, smokeY = centerY - 20f;
        if (smokeX < int.MinValue || smokeX >= 2147483648f ||
            !TerrariaPoofOfSmokeCodec1458.TryEncode((int)smokeX, smokeY, out var smokeFrame) ||
            !npcs.TryPrepareCatchRemoval(in frog, out var removal)) return false;
        RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? retiredPublication = null;
        if (replication is not null &&
            (!replication.TryPrepareCatchPublication(removal!, [smokeFrame], out retiredPublication) ||
                !retiredPublication!.TrySealRecipients())) return false;
        VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(preview.Random));
        if (!preview.IsBeforeCurrent() || !InputsCurrent() || !removal!.TryAdoptUnpublished()) return false;
        live.CopyStateFrom(preview.Random);
        if (retiredPublication is not null)
        {
            if (!retiredPublication.TryAdoptRetiredBaseline(npcs, removal))
                throw new InvalidOperationException("Preflighted caught-frog retirement lost callback-free baseline adoption.");
            retiredPublication.Publish(removal.TryPublish);
        }
        else removal.TryPublish();
        return true;
    }

    private (WorldSectionId Section, long Version)[]? CaptureTerrain(int left, int top, int right, int bottom)
    {
        var first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, left, top);
        var last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, right, bottom);
        var result = new List<(WorldSectionId, long)>();
        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            var section = new WorldSectionId(x, y); long version = tiles.GetSectionVersion(section);
            if ((version & 1L) != 0) return null;
            result.Add((section, version));
        }
        return result.ToArray();
    }

    private bool TerrainCurrent((WorldSectionId Section, long Version)[] versions)
    {
        foreach (var captured in versions)
            if ((captured.Version & 1L) != 0 || tiles.GetSectionVersion(captured.Section) != captured.Version) return false;
        return true;
    }

    private readonly record struct PlayerBounds(int Left, int Top, int Right, int Bottom);
    private static bool TryPlayerBounds(in NpcRawPlayerSlotSnapshot1458 raw, out PlayerBounds bounds)
    {
        bounds = default;
        if (!raw.Facts.Active || raw.Facts.Dead) return true;
        if (raw.LivePlayer is not { } player) return false;
        double dx = player.VelocityX * 20f, dy = player.VelocityY * 20f;
        double left = raw.Facts.PositionX, top = raw.Facts.PositionY;
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx < int.MinValue || dx >= 2147483648d ||
            dy < int.MinValue || dy >= 2147483648d || left < int.MinValue || left >= 2147483648d ||
            top < int.MinValue || top >= 2147483648d) return false;
        int x = (int)left, y = (int)top, shiftX = (int)dx, shiftY = (int)dy;
        long right = (long)x + raw.Facts.Width, bottom = (long)y + raw.Facts.Height;
        long x2 = (long)x + shiftX, y2 = (long)y + shiftY;
        long right2 = right + shiftX, bottom2 = bottom + shiftY;
        if (Math.Min(x2, right2) < int.MinValue || Math.Max(x2, right2) > int.MaxValue ||
            Math.Min(y2, bottom2) < int.MinValue || Math.Max(y2, bottom2) > int.MaxValue ||
            right > int.MaxValue || bottom > int.MaxValue) return false;
        bounds = new(Math.Min(x, (int)x2), Math.Min(y, (int)y2), Math.Max((int)right, (int)right2), Math.Max((int)bottom, (int)bottom2));
        return true;
    }

    private bool TryFindTeleportSpot(int targetX, int targetY, NpcRawPlayerSlotSnapshot1458[] raw,
        VanillaUnifiedRandom1458 random, out int chosenX, out int chosenY)
    {
        chosenX = chosenY = 0;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            int x = random.Next(targetX - 15, targetX + 16);
            for (int y = random.Next(targetY - 15, targetY + 16); y < targetY + 15; y++)
            {
                if (y >= targetY - 1 && y <= targetY + 1 && x >= targetX - 1 && x <= targetX + 1) continue;
                WorldTile ground = tiles.Get(x, y);
                if (!ground.IsActive || ground.IsActuated) continue;
                if (tiles.Get(x, y - 1).LiquidKind == WorldLiquidKind.Lava ||
                    !VanillaTileCollisionCatalog.IsSolid(ground.TileType) || HasSolidTiles(x - 1, x + 1, y - 4, y - 1)) continue;
                bool blocked = false;
                foreach (var captured in raw)
                {
                    if (!captured.Facts.Active || captured.Facts.Dead) continue;
                    if (!TryPlayerBounds(in captured, out var bounds)) return false;
                    if (bounds.Left < x * 16 + 144 && bounds.Right > x * 16 - 128 &&
                        bounds.Top < y * 16 + 144 && bounds.Bottom > y * 16 - 128) { blocked = true; break; }
                }
                if (!blocked) { chosenX = x; chosenY = y; return true; }
                // A player-blocked solid candidate ends this vertical scan in the source.
                break;
            }
        }
        return false;
    }

    private bool HasSolidTiles(int left, int right, int top, int bottom)
    {
        if (top < 0 || bottom >= tiles.Dimensions.HeightTiles - 40) return true;
        for (int x = left; x <= right; x++)
        for (int y = top; y <= bottom; y++)
        {
            WorldTile tile = tiles.Get(x, y);
            if (tile.IsActive && !tile.IsActuated && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)) return true;
        }
        return false;
    }
}

/// <summary>Source Damage_TryUsingPowders type10 rectangle scan on the existing staged NPC table.</summary>
internal sealed class RuntimePurificationPowderNpcInteraction1458(
    RuntimeNpcStore npcs, RuntimeProjectileStore projectiles, RuntimeTownNpcStateStore townNpcs,
    RuntimeWorldProgressionMutations progression, bool infectedSeed, SystemVanillaNpcRandom? sourceRandom,
    INpcRawPlayerSlotLookup1458? rawPlayers, IRuntimePlayerSlotSnapshotLookup? playerSnapshots,
    RuntimeNpcBuffStatus1458 status, RuntimeNpcReplicationRegistry? replication)
{
    private readonly ProjectileSnapshot[] projectileBuffer = new ProjectileSnapshot[RuntimeProjectileStore.MaximumProtocolAddressableCapacity];
    private readonly NpcSnapshot[] npcBuffer = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];
    private Func<RuntimeWorldProgressionMutationSnapshot, byte[]?>? prepareWorldInfo;
    private Action? worldStateAdopted;
    private bool active;

    internal void BindWorldInfoSource(Func<RuntimeWorldProgressionMutationSnapshot, byte[]?> prepare, Action adopted)
    {
        ArgumentNullException.ThrowIfNull(prepare); ArgumentNullException.ThrowIfNull(adopted);
        if (prepareWorldInfo is not null) throw new InvalidOperationException("Powder world-info source is already bound.");
        prepareWorldInfo = prepare; worldStateAdopted = adopted;
    }

    public int Tick()
    {
        if (active) return 0;
        int total = 0;
        int count = projectiles.CopyActive(projectileBuffer);
        for (int index = 0; index < count; index++)
        {
            var powder = projectileBuffer[index];
            if (powder.Type.Value != 10 || !float.IsFinite(powder.PositionX) || !float.IsFinite(powder.PositionY) ||
                powder.PositionX < int.MinValue || powder.PositionX >= 2147483648f ||
                powder.PositionY < int.MinValue || powder.PositionY >= 2147483648f) continue;
            int size = infectedSeed ? 106 : 64;
            if (TryApply(10, (int)powder.PositionX, (int)powder.PositionY, size, size,
                    () => projectiles.TryGet(powder.Handle, out var current) && current == powder, out int transformed))
                total += transformed;
        }
        return total;
    }

    // The actual source primitive reads the supplied damage rectangle, not an invented projectile lifetime.
    // Tick supplies its real projectile occupation guard; isolated source-component callers can supply the same
    // explicit rectangle/type boundary without creating or retiring a synthetic world projectile.
    internal bool TryApply(int projectileType, int left, int top, int width, int height,
        Func<bool>? callerCurrent, out int transformed)
    {
        transformed = 0;
        if (active || projectileType != 10 || width is <= 0 or > 4096 || height is <= 0 or > 4096 ||
            (long)left + width > int.MaxValue || (long)top + height > int.MaxValue) return false;
        active = true;
        try { return TryApplyCore(left, top, width, height, callerCurrent, out transformed); }
        finally { active = false; }
    }

    private bool TryApplyCore(int left, int top, int width, int height, Func<bool>? callerCurrent, out int transformed)
    {
        transformed = 0;
        int count = npcs.CopyActive(npcBuffer);
        var selected = new List<NpcSnapshot>();
        for (int index = 0; index < count; index++)
        {
            var source = npcBuffer[index];
            if (source.Type is not (534 or 687)) continue;
            if (!VanillaNpcDefinitionCatalog.TryGet(source.TypeIdentity, source.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(source.Simulation, out var body) ||
                !float.IsFinite(source.PositionX) || !float.IsFinite(source.PositionY) ||
                source.PositionX < int.MinValue || source.PositionX >= 2147483648f ||
                source.PositionY < int.MinValue || source.PositionY >= 2147483648f) return false;
            int x = (int)source.PositionX, y = (int)source.PositionY;
            if ((long)left < (long)x + body.Width && (long)left + width > x &&
                (long)top < (long)y + body.Height && (long)top + height > y) selected.Add(source);
        }
        if (selected.Count == 0) return true;
        if (sourceRandom is null || rawPlayers is null || playerSnapshots is null ||
            townNpcs.Count + selected.Count > RuntimeTownNpcStateStore.MaximumTownNpcs) return false;
        var live = sourceRandom.SourceRandom;
        var beforeRandom = live.Clone(); var working = beforeRandom.Clone();
        var beforeTown = townNpcs.CaptureNpcPersistence(); var beforeRooms = townNpcs.CaptureTownRooms();
        var beforeProgression = progression.CaptureSnapshot(); bool beforeUnlocked = progression.IsSlimeYellowSpawnUnlocked;
        var raw = new NpcRawPlayerSlotSnapshot1458[256];
        for (int slot = 0; slot < raw.Length; slot++)
            if (!rawPlayers.TryCapture((byte)slot, out raw[slot])) return false;
        var behavior = new VanillaNpcBehaviorContext();
        behavior.SetCandidates(raw.AsSpan(0, 255).ToArray().Select(value => value.Facts.Candidate).ToArray());
        behavior.SetRawPlayerSlots(rawPlayers); behavior.SetPlayerSnapshotLookup(playerSnapshots);
        if (!live.HasSameState(beforeRandom) || !npcs.TryPrepareWorldSpawns(live, working, out var batch) || batch is null)
            return false;
        var plans = new List<TransformPlan>(); bool unlock = beforeUnlocked;
        foreach (var source in selected)
        {
            if (!status.CaptureInteraction(in source, out var beforeStatus)) return false;
            var target = source.Type == 534 ? VanillaNpcIds.TaxCollector : VanillaNpcIds.TownSlimeYellow;
            if (!batch.TryStageTransform(in source, target, default, behavior, out var checkpoint) ||
                !townNpcs.CanAdoptTransformedResident(in checkpoint)) return false;
            // WorldGen.genRand aliases Main.rand in the original1458 runtime. Reuse the existing
            // town-identity adapter on this same working cursor, never a separate name stream.
            bool shimmered = townNpcs.IsShimmeredNpcType(target);
            var nameRandom = new SystemVanillaTownNpcRandom1458(new FishingRandom1458(working));
            var identity = new VanillaTownNpcIdentityResolver1458(mainRandom: nameRandom, worldGenRandom: nameRandom)
                .Resolve(target, shimmered);
            var home = new TerrariaNpcHomeState(checkpoint.Handle.Slot,
                checked((short)checkpoint.Simulation.HomeTileX!.Value), checked((short)checkpoint.Simulation.HomeTileY!.Value),
                (byte)TerrariaNpcHomeStatus.Homeless);
            var identityState = new TerrariaTownNpcIdentityState(checkpoint.Handle.Slot, identity.GivenName, identity.VariationIndex);
            if (TerrariaNpcHomeCodec.TryEncode(in home, out var homeFrame) != TerrariaNpcHomeEncodeResult.Encoded ||
                TerrariaTownNpcIdentityCodec.TryEncode(in identityState, out var identityFrame) != TerrariaTownNpcIdentityEncodeResult.Encoded ||
                !RuntimeNpcBuffStatus1458.TryPrepareGemBunnyBuffFrame(checkpoint.Handle.Slot, beforeStatus.Slots, out var buffFrame))
                return false;
            var prefix = new List<byte[]>(); var tails = new List<byte[]> { buffFrame };
            if (source.Type == 687)
            {
                prefix.Add(homeFrame);
                if (!VanillaNpcDefinitionCatalog.TryGet(checkpoint.TypeIdentity, checkpoint.NetIdentity, out var afterDefinition) ||
                    !afterDefinition.TryResolveHitbox(checkpoint.Simulation, out var afterBody)) return false;
                float smokeX = checkpoint.PositionX + afterBody.Width * .5f - 20f;
                float smokeY = checkpoint.PositionY + afterBody.Height * .5f - 20f;
                if (!float.IsFinite(smokeX) || smokeX < int.MinValue || smokeX >= 2147483648f ||
                    !TerrariaPoofOfSmokeCodec1458.TryEncode((int)smokeX, smokeY, out var smokeFrame)) return false;
                VanillaPoofOfSmoke1458.ConsumeDedicatedSourceOffers(new SystemVanillaNpcRandom(working));
                tails.Add(smokeFrame);
                if (!unlock)
                {
                    if (prepareWorldInfo is null || worldStateAdopted is null ||
                        prepareWorldInfo(beforeProgression with { UnlockSlimeYellowSpawn = true }) is not { } worldFrame ||
                        worldFrame.Length < 3 || worldFrame[2] != (byte)TerraRuntime.Protocol.TerrariaMessageId.WorldData) return false;
                    tails.Add(worldFrame); unlock = true;
                }
            }
            var final = Update(checkpoint);
            RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? publication = null;
            if (replication is not null &&
                (!replication.TryPrepareHistoricalCheckpoint(in checkpoint, in final, npcs.HasPendingBirth(source.Handle),
                    prefix.ToArray(), tails.ToArray(), out publication) || !publication!.TrySealRecipients())) return false;
            plans.Add(new(source, checkpoint, beforeStatus, identity, homeFrame, identityFrame, publication));
        }
        if (!batch.ValidateContext() || callerCurrent is not null && !callerCurrent()) return false;
        foreach (var captured in raw) if (!rawPlayers.IsCurrent(in captured)) return false;
        foreach (var plan in plans) if (!status.IsCurrentInteraction(in plan.Before, in plan.BeforeStatus) ||
            !townNpcs.CanAdoptTransformedResident(in plan.Accepted)) return false;
        if (!SameTown(beforeTown, townNpcs.CaptureNpcPersistence()) ||
            !beforeRooms.AsSpan().SequenceEqual(townNpcs.CaptureTownRooms()) ||
            progression.CaptureSnapshot() != beforeProgression || progression.IsSlimeYellowSpawnUnlocked != beforeUnlocked ||
            !batch.IsCurrentOwned || !live.HasSameState(beforeRandom) || !batch.TryAdoptUnpublished(out _)) return false;
        live.CopyStateFrom(working);
        foreach (var plan in plans)
        {
            if (!npcs.TryGet(plan.Accepted.Handle, out var committed) || committed != plan.Accepted ||
                !status.TryAdoptInteraction(in plan.Before, in plan.BeforeStatus, in committed,
                    materializedTransform: true, out plan.AfterStatus) ||
                !townNpcs.TryAdoptTransformedResident(in committed, in plan.Identity))
                throw new InvalidOperationException("Preflighted powder NPC/status/town adoption failed without callbacks.");
        }
        foreach (var plan in plans) replication?.ObserveMaterializedTransform(in plan.Accepted);
        foreach (var plan in plans)
        {
            if (plan.Publication is not null &&
                (!plan.Publication.TryAdoptBaseline(npcs, in plan.Accepted) ||
                 !replication!.TryAdoptTransformedTownBaselines(in plan.Accepted, plan.HomeFrame, plan.IdentityFrame) ||
                 plan.Before.Type == 687 && !replication.TryAdoptSourceHomeUpdate(in plan.Accepted, plan.HomeFrame)))
                throw new InvalidOperationException("Preflighted powder baseline adoption failed without callbacks.");
        }
        if (unlock && !beforeUnlocked && !progression.MarkSlimeYellowSpawnUnlocked())
            throw new InvalidOperationException("Preflighted powder unlock failed without callbacks.");
        transformed = plans.Count;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
        if (unlock && !beforeUnlocked)
        {
            try { worldStateAdopted!(); }
            catch (Exception exception) { failure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
        }
        foreach (var plan in plans)
        {
            try
            {
                if (plan.Publication is not null)
                    plan.Publication.Publish(() => npcs.TryPublishHistoricalCheckpoint(in plan.Accepted, in plan.Accepted),
                        () => status.IsCurrentInteraction(in plan.Accepted, in plan.AfterStatus));
                else npcs.TryPublishHistoricalCheckpoint(in plan.Accepted, in plan.Accepted);
            }
            catch (Exception exception) { failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception); }
        }
        failure?.Throw();
        return true;
    }

    private static NpcStateUpdate Update(in NpcSnapshot snapshot) => new(snapshot.Type, snapshot.NetId,
        snapshot.PositionX, snapshot.PositionY, snapshot.VelocityX, snapshot.VelocityY,
        snapshot.Target, snapshot.Ai, snapshot.Simulation);

    private static bool SameTown(WorldNpcPersistence left, WorldNpcPersistence right) =>
        left.ShimmeredTownNpcIndices.AsSpan().SequenceEqual(right.ShimmeredTownNpcIndices) &&
        left.TownNpcs.AsSpan().SequenceEqual(right.TownNpcs) && left.PersistentNpcs.AsSpan().SequenceEqual(right.PersistentNpcs);

    private sealed class TransformPlan(NpcSnapshot before, NpcSnapshot accepted,
        RuntimeNpcBuffAdditionCheckpoint1458 beforeStatus, VanillaTownNpcSpawnIdentity1458 identity,
        byte[] homeFrame, byte[] identityFrame, RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? publication)
    {
        internal readonly NpcSnapshot Before = before;
        internal readonly NpcSnapshot Accepted = accepted;
        internal readonly RuntimeNpcBuffAdditionCheckpoint1458 BeforeStatus = beforeStatus;
        internal RuntimeNpcBuffAdditionCheckpoint1458 AfterStatus;
        internal readonly VanillaTownNpcSpawnIdentity1458 Identity = identity;
        internal readonly byte[] HomeFrame = homeFrame;
        internal readonly byte[] IdentityFrame = identityFrame;
        internal readonly RuntimeNpcReplicationRegistry.RetainedHistoricalPublication? Publication = publication;
    }
}
