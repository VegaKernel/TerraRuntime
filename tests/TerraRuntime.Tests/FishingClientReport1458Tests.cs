using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class FishingClientReport1458Tests
{
    [Fact]
    public void Actual_official_database_and_payment_results_remain_possible_without_predicting_client_random()
    {
        using var stream = typeof(FishingClientReport1458Tests).Assembly.GetManifestResourceStream("FishingRulesLinux1458")!;
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(zip);
        int catches = 0;
        foreach (var row in source.RootElement.GetProperty("drops").EnumerateArray())
        {
            int item = row.GetProperty("item").GetInt32();
            if (item <= 0) continue;
            var attempt = SourceDatabaseScene(row.GetProperty("scene").GetInt32());
            Assert.True(FishingRules1458.IsPossibleItemDrop(attempt, item));
            Assert.False(FishingRules1458.IsPossibleItemDrop(attempt, 1));
            catches++;
        }
        Assert.True(catches > 100);
        int payments = 0;
        foreach (var row in source.RootElement.GetProperty("payment").EnumerateArray())
        {
            int caught = row.GetProperty("caught").GetInt32();
            var possible = FishingRules1458.GetPossibleClientBaitConsumption(row.GetProperty("bait").GetInt32(),
                Math.Max(0, caught), Math.Max(0, -caught));
            var observed = row.GetProperty("remaining").GetInt32() == 0
                ? FishingBaitConsumption1458.Consumed : FishingBaitConsumption1458.Conserved;
            Assert.NotEqual(FishingBaitConsumption1458.None, possible & observed);
            payments++;
        }
        Assert.Equal(240, payments);
    }

    [Fact]
    public void Known_liquid_quest_biome_depth_and_progression_guards_reject_impossible_results()
    {
        var context = new FishingClientCatchContext1458(1000, 350, 5600,
            new(2294, 50, 2674, 15, 65, 1), new(4200, 1200, 300, 600), FishingBiomes1458.None,
            new(1200, false, false, 0), 0, 2450);
        Assert.True(FishingRules1458.IsPossibleClientCatch(context, 2290));
        Assert.True(FishingRules1458.IsPossibleClientCatch(context, 2450));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context with { AvailableQuestFish = 0 }, 2450));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 1));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 2314));
        Assert.True(FishingRules1458.IsPossibleClientCatch(context with { Pond = new(1200, false, true, 0) }, 2314));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context with { Pond = new(1200, true, false, 0) }, 2312));
        Assert.True(FishingRules1458.IsPossibleClientCatch(context with { Pond = new(1200, true, false, 0),
            Conditions = context.Conditions with { PoleItemType = 2422 } }, 2312));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 2302));
        Assert.True(FishingRules1458.IsPossibleClientCatch(context with { Biomes = FishingBiomes1458.Jungle }, 2302));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 3984));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 0, 618));
        var blood = context with { World = context.World with { BloodMoon = true, DayTime = false } };
        Assert.True(FishingRules1458.IsPossibleClientCatch(blood, 0, 586));
        Assert.False(FishingRules1458.IsPossibleClientCatch(blood, 0, 618));
        Assert.True(FishingRules1458.IsPossibleClientCatch(blood with { World = blood.World with { HardMode = true } }, 0, 618));
        Assert.False(FishingRules1458.IsPossibleClientCatch(blood with { World = blood.World with { RedSlimeUnlocked = true } }, 0, 682));
        var worm = context with { Conditions = context.Conditions with { BaitItemType = 2673 }, X = 100 };
        Assert.True(FishingRules1458.IsPossibleClientCatch(worm, 0, 370));
        Assert.False(FishingRules1458.IsPossibleClientCatch(worm with { Pond = new(1000, false, false, 0) }, 0, 370));
        Assert.False(FishingRules1458.IsPossibleClientCatch(worm with { World = worm.World with { DukeFishronActive = true } }, 0, 370));
        Assert.Equal(11, FishingRules1458.GetMinimumClientFishingWaitFrames(65));
        Assert.Equal((1, 3), FishingRules1458.GetClientCatchStackBounds(3196, 0));
        Assert.Equal((7, 20), FishingRules1458.GetClientCatchStackBounds(3197, 0));
    }

    [Fact]
    public void High_power_keeps_source_rarity_clamps_but_junk_and_guaranteed_liquid_rows_still_restrict_results()
    {
        var context = new FishingClientCatchContext1458(1000, 350, 5600,
            new(2294, 50, 2674, 15, 10_000, 1), new(4200, 1200, 300, 600), FishingBiomes1458.None,
            new(75, false, false, 0), 0, 0);
        // Official RollDropLevels clamps every denominator above1 even at high
        // power: allfalse is possible. The basewater fallback remains reachable.
        Assert.True(FishingRules1458.IsPossibleClientCatch(context, 2290));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context, 2337));
        Assert.True(FishingRules1458.IsPossibleClientCatch(context with
            { Conditions = context.Conditions with { FinalFishingLevel = 10 } }, 2337));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context with { Pond = new(1200, false, true, 0) }, 2290));
        Assert.False(FishingRules1458.IsPossibleClientCatch(context with { Pond = new(1200, true, false, 0) }, 2290));
        var honey = SourceDatabaseScene(15) with { Pond = new(1200, false, true, 0),
            Levels = new(false, false, true, false, false, false) };
        Assert.True(FishingRules1458.IsPossibleItemDrop(honey, 2314));
        Assert.False(FishingRules1458.IsPossibleItemDrop(honey, 2290));
        Assert.Equal(11, FishingRules1458.GetMinimumClientFishingWaitFrames(0));
    }

    private static FishingAttempt1458 SourceDatabaseScene(int scene)
    {
        // Same factual source-method setup as the portable independent observation,
        // without sampling any random cursor to choose the submitted result.
        int mask = scene switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, 4 => 8, 5 => 16, 6 => 32,
            7 => 64, 8 => 128, 9 => 256, 10 => 3, 11 => 12, 12 => 320, _ => scene * 73 % 512 };
        int quest = scene switch { 3 => 2452, 4 => 2467, 8 => 2475, 9 => 4393, 24 => 2461, _ => -1 };
        var biomes = (FishingBiomes1458)mask;
        return new(100, 800, 50, scene % 5, 300, new(1200, scene is 13 or 14, scene is 15 or 16, 0), default,
            new(4200, 1200, 300, 600, (scene & 1) != 0, scene >= 24, false, scene % 4 == 0, true, true),
            biomes, biomes, scene % 2 == 0, scene == 29, scene == 17, true, quest,
            new(true, true, scene % 3 == 0, scene % 4 == 0, scene % 5 == 0, scene is >= 18 and < 23));
    }
}

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Vanilla_client_cast_reports_one_legal_catch_and_two_paid_inventory_cohorts_without_random_matching()
    {
        using var f = new FishingFixture(2, importedBait: true);
        f.Control(false); for (int i = 0; i < 20; i++) f.Tick(); f.Control(true);
        Assert.True(FishingRules1458.TryGetPole(2294, out var pole));
        var birth = new TerrariaProjectileUpdateState(new(f.Connection.Player.Slot.Value, 450, 1), pole.ProjectileType,
            f.Member.PositionX + 3, f.Member.PositionY + 14, pole.ShootSpeed * .8f, pole.ShootSpeed * -.6f,
            0, 0, 0, 0, 0, 0, 0);
        Report(birth); f.Tick(); f.Control(false);
        var born = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(born); Assert.NotNull(born.Client);
        var dropRandom = DropRandom(f.Authority).Clone();
        Report(Packet() with { Ai1 = -180 });
        Assert.Null(f.Authority.CaptureFishingBobber(f.Connection)!.Client!.Claim);
        for (int i = 0; i < 400; i++)
        {
            f.Tick(); var current = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(current);
            if (current.Client!.SettledFrames >= 20) break;
        }
        Assert.True(f.Authority.CaptureFishingBobber(f.Connection)!.Client!.SettledFrames >= 20);
        Assert.False(f.Authority.CaptureFishingBobber(f.Connection)!.Result.HasBite);
        Assert.True(dropRandom.HasSameState(DropRandom(f.Authority)));
        Report(Packet() with { Ai1 = -180 });
        Assert.True(f.Players.TryCaptureFishingClientPayment(f.Connection, out var claim)); Assert.NotNull(claim);
        Assert.False(claim.PullReported);
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        Report(Packet() with { Ai0 = 1, Ai1 = 1 });
        Assert.False(claim.PullReported); Assert.Equal(1, f.Count(1)); // The fixture owns one stone already.
        Report(Packet() with { Ai0 = 1, Ai1 = 2290 });
        Assert.True(claim.PullReported); Assert.False(claim.PaymentCommitted);
        Assert.Equal(0, f.Count(2290));
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            (f.Item(f.BaitSlot) with { Stack = 1 }).ToCommitRequest(f.Connection.Player.Slot, f.BaitSlot)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
        for (int i = 0; i < 400; i++)
        {
            f.Tick(); var bobber = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(bobber);
            if (bobber.Snapshot.PositionX < f.Member.PositionX + 20 && bobber.Snapshot.PositionX + 14 > f.Member.PositionX &&
                bobber.Snapshot.PositionY < f.Member.PositionY + 42 && bobber.Snapshot.PositionY + 14 > f.Member.PositionY) break;
        }
        var ending = f.Authority.CaptureFishingBobber(f.Connection)!;
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, ending.Snapshot.PositionX, ending.Snapshot.PositionY)));
        Assert.True(claim.ClientClosed); Assert.Equal(0, f.Count(2290));
        var image = Enumerable.Range(0, 59).Select(i => f.Item((short)i)).ToArray();
        var slots = image.Select(item => new WorldItemPickupSlot1458(item.ItemType.Value, item.Stack, item.Prefix.Value,
            (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(new(2290, 1, 0, false), slots, false, new(false, false, false), out var storage));
        Assert.NotNull(storage);
        foreach (var mutation in storage.Mutations)
        {
            var after = mutation.After;
            var item = after.Type == 0 ? default : new RuntimePlayerInventoryItem(new(after.Type), checked((short)after.Stack),
                new(after.Prefix), after.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, item.ToCommitRequest(f.Connection.Player.Slot, checked((short)mutation.Slot))));
        }
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(1, f.Count(2290)); Assert.True(claim.RewardAdopted); Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, ending.Snapshot.PositionX, ending.Snapshot.PositionY)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(1, f.Count(2290)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);

        TerrariaProjectileUpdateState Packet()
        {
            var current = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
            return birth with { PositionX = current.PositionX, PositionY = current.PositionY,
                VelocityX = current.VelocityX, VelocityY = current.VelocityY };
        }
        void Report(TerrariaProjectileUpdateState packet)
        {
            Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(packet, out var bytes));
            var buffer = new ReadOnlySequence<byte>(bytes);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out var frame));
            Assert.Equal(TerrariaProjectileDecodeResult.Decoded, TerrariaProjectileDecoder.TryDecodeUpdate(frame, out var decoded));
            f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, decoded) { ValidateVanillaFishingClient = true });
        }
    }
}

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Vanilla_Truffle_request_before_pull_waits_for_real_last_bait_payment_and_spawns_once()
    {
        using var f = new FishingFixture(1, 2673, importedBait: true);
        for (int x = 112; x <= 166; x++)
            for (int y = 100; y <= 139; y++)
                f.Tiles.Set(x, y, new() { LiquidAmount = y == 100 ? (byte)128 : (byte)255 });
        for (int y = 100; y <= 140; y++)
            foreach (int x in new[] { 111, 167 }) f.Tiles.Set(x, y, new() { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 111; x <= 167; x++) f.Tiles.Set(x, 140, new() { Type = 1, Flags = WorldTileFlags.Active });
        f.Control(false); for (int i = 0; i < 20; i++) f.Tick(); f.Control(true);
        var birth = new TerrariaProjectileUpdateState(new(f.Connection.Player.Slot.Value, 451, 1), 364,
            f.Member.PositionX + 3, f.Member.PositionY + 14, 13.6f, -10.2f, 0, 0, 0, 0, 0, 0, 0);
        Report(birth); f.Tick(); f.Control(false);
        for (int i = 0; i < 400; i++)
        {
            f.Tick(); var bobber = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(bobber);
            if (bobber.Client!.SettledFrames >= 20) break;
        }
        Assert.True(f.Authority.CaptureFishingBobber(f.Connection)!.Client!.SettledFrames >= 20);
        Report(Packet() with { Ai1 = -200 });
        Assert.True(f.Players.TryCaptureFishingClientClaim(f.Connection, out var claim)); Assert.NotNull(claim);
        long before = f.Runtime.Npcs.AppliedSpawns;
        // Actual source packet61 is sent inside PullBobber before returning27.
        byte[] requestBytes = [7, 0, 61, f.Connection.Player.Slot.Value, 0, 0x72, 0x01];
        var requestBuffer = new ReadOnlySequence<byte>(requestBytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref requestBuffer, out var npcFrame));
        Assert.True(TerrariaFishingNpcRequestCodec1458.TryDecode(npcFrame, out var request));
        f.Apply(new FishingNpcClientRequestRuntimeCommand(f.Connection, request));
        Assert.Equal(before, f.Runtime.Npcs.AppliedSpawns);
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        Report(Packet() with { Ai0 = 2, Ai1 = -200 });
        Assert.True(claim.PullReported); Assert.Equal(370, claim.NpcType);
        Assert.Equal(FishingBaitConsumption1458.Consumed, claim.Consumption);
        Assert.False(claim.PaymentCommitted); Assert.Equal(before, f.Runtime.Npcs.AppliedSpawns);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            default(RuntimePlayerInventoryItem).ToCommitRequest(f.Connection.Player.Slot, f.BaitSlot)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait);
        Assert.True(f.Item(f.BaitSlot).IsEmpty); Assert.Equal(0, f.Funded(f.BaitSlot));
        Assert.Equal(before + 1, f.Runtime.Npcs.AppliedSpawns);
        var actors = new NpcSnapshot[200]; int count = f.Runtime.Npcs.CopyActive(actors);
        Assert.Equal(1, actors.Take(count).Count(actor => actor.Type == 370));
        f.Apply(new FishingNpcClientRequestRuntimeCommand(f.Connection, request));
        Report(Packet() with { Ai0 = 2, Ai1 = -200 });
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(before + 1, f.Runtime.Npcs.AppliedSpawns); Assert.True(f.Item(f.BaitSlot).IsEmpty);

        TerrariaProjectileUpdateState Packet()
        {
            var current = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
            return birth with { PositionX = current.PositionX, PositionY = current.PositionY,
                VelocityX = current.VelocityX, VelocityY = current.VelocityY };
        }
        void Report(TerrariaProjectileUpdateState packet) =>
            f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet) { ValidateVanillaFishingClient = true });
    }
}

public sealed partial class FishingAuthority1458Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verified_cursor_bait_swap_handles_negative27_before_or_after_its_slot5_cohort(bool biteBeforeSlotReports)
    {
        // A real cursor click swaps these two owned stacks in one source frame.
        // The source sends slot54 before58; neither private quantity is funded.
        var cursor = new RuntimePlayerInventoryItem(new(2676), 1, default, 0);
        using var f = new FishingFixture(2, importedBait: true,
            initialItems: new Dictionary<short, RuntimePlayerInventoryItem> { [58] = cursor });
        var birth = CastClientForBaitSwap(f);
        for (int i = 0; i < 400; i++)
        { f.Tick(); if (f.Authority.CaptureFishingBobber(f.Connection)!.Client!.SettledFrames >= 20) break; }
        Assert.True(f.Authority.CaptureFishingBobber(f.Connection)!.Client!.SettledFrames >= 20);
        if (biteBeforeSlotReports) Report(SnapshotPacket() with { Ai1 = -260 });
        var oldBait = f.Item(54);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, cursor.ToCommitRequest(f.Connection.Player.Slot, 54)));
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, oldBait.ToCommitRequest(f.Connection.Player.Slot, 58)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(cursor, f.Item(54)); Assert.Equal(oldBait, f.Item(58)); Assert.Equal(0, f.Funded(54));
        if (!biteBeforeSlotReports) Report(SnapshotPacket() with { Ai1 = -260 });
        Assert.True(f.Players.TryCaptureFishingClientClaim(f.Connection, out var claim)); Assert.NotNull(claim);
        Assert.Equal(biteBeforeSlotReports ? 2 : 1, claim.ContextCount);
        // An absent source stack cannot become the second context via raw5.
        var forged = new RuntimePlayerInventoryItem(new(2675), 1, default, 0);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, forged.ToCommitRequest(f.Connection.Player.Slot, 56)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(f.Item(56).IsEmpty); Assert.InRange(claim.ContextCount, 1, 2);
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        Report(SnapshotPacket() with { Ai0 = 1, Ai1 = 2290 });
        Assert.True(claim.PullReported); Assert.Equal(2676, claim.BaitType); Assert.Equal(54, claim.BaitSlot);
        FinishClientBassAfterBaitSwap(f, birth, claim);
        Assert.Equal(oldBait, f.Item(58));

        TerrariaProjectileUpdateState SnapshotPacket()
        {
            var current = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
            return birth with { PositionX = current.PositionX, PositionY = current.PositionY,
                VelocityX = current.VelocityX, VelocityY = current.VelocityY };
        }
        void Report(TerrariaProjectileUpdateState packet) =>
            f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet) { ValidateVanillaFishingClient = true });
    }

    private static TerrariaProjectileUpdateState CastClientForBaitSwap(FishingFixture f)
    {
        f.Control(false); for (int i = 0; i < 20; i++) f.Tick(); f.Control(true);
        var packet = new TerrariaProjectileUpdateState(new(f.Connection.Player.Slot.Value, 452, 1), 364,
            f.Member.PositionX + 3, f.Member.PositionY + 14, 13.6f, -10.2f, 0, 0, 0, 0, 0, 0, 0);
        f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet) { ValidateVanillaFishingClient = true });
        f.Tick(); f.Control(false);
        Assert.NotNull(f.Authority.CaptureFishingBobber(f.Connection)!.Client);
        return packet;
    }

    private static void FinishClientBassAfterBaitSwap(FishingFixture f, TerrariaProjectileUpdateState birth,
        FishingClientClaim1458 claim)
    {
        Assert.Equal(0, f.Count(2290));
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            default(RuntimePlayerInventoryItem).ToCommitRequest(f.Connection.Player.Slot, claim.BaitSlot)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait); Assert.True(f.Item(claim.BaitSlot).IsEmpty);
        for (int i = 0; i < 400; i++)
        {
            f.Tick(); var bobber = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(bobber);
            if (bobber.Snapshot.PositionX < f.Member.PositionX + 20 && bobber.Snapshot.PositionX + 14 > f.Member.PositionX &&
                bobber.Snapshot.PositionY < f.Member.PositionY + 42 && bobber.Snapshot.PositionY + 14 > f.Member.PositionY) break;
        }
        var ending = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, ending.PositionX, ending.PositionY)));
        Assert.True(claim.ClientClosed);
        var slots = Enumerable.Range(0, 59).Select(i => f.Item((short)i)).Select(item => new WorldItemPickupSlot1458(
            item.ItemType.Value, item.Stack, item.Prefix.Value, (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(new(2290, 1, 0, false), slots, false, new(false, false, false), out var storage));
        Assert.NotNull(storage);
        foreach (var mutation in storage.Mutations)
        {
            var item = new RuntimePlayerInventoryItem(new(mutation.After.Type), checked((short)mutation.After.Stack),
                new(mutation.After.Prefix), mutation.After.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, item.ToCommitRequest(f.Connection.Player.Slot, checked((short)mutation.Slot))));
        }
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(1, f.Count(2290)); Assert.True(claim.RewardAdopted); Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, ending.PositionX, ending.PositionY)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection)); Assert.Equal(1, f.Count(2290));
    }
}

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Golden_pole_accepts_source_air_followup_above16_then_cancels_without_bait_or_catch()
    {
        using var f = new FishingFixture(2, importedBait: true);
        f.Control(false); for (int i = 0; i < 20; i++) f.Tick(); f.Control(true);
        Assert.True(FishingRules1458.TryGetPole(2294, out var pole));
        var birth = new TerrariaProjectileUpdateState(new(f.Connection.Player.Slot.Value, 453, 1), pole.ProjectileType,
            // A slight upward aim avoids source AI61's extra horizontal*.95
            // when initial vertical velocity is exactly zero. Norm is still17.
            f.Member.PositionX + 3, f.Member.PositionY + 14, MathF.Sqrt(pole.ShootSpeed * pole.ShootSpeed - 1), -1,
            0, 0, 0, 0, 0, 0, 0);
        Report(birth); f.Tick(); f.Control(false);
        var airborne = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(airborne);
        Assert.InRange(airborne.Snapshot.VelocityX, 16.01f, pole.ShootSpeed);
        long rejected = f.Authority.RejectedClientUpdates;
        long applied = f.Authority.AppliedUpdates;
        var followup = SnapshotPacket();
        Report(followup);
        Assert.Equal(rejected, f.Authority.RejectedClientUpdates);
        Assert.Equal(applied + 1, f.Authority.AppliedUpdates);
        Assert.Equal(airborne.Snapshot.VelocityX, f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot.VelocityX);
        for (int i = 0; i < 12; i++) f.Tick();
        var sourceStock = f.InventoryImage();
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        Report(SnapshotPacket() with { Ai0 = 1, Ai1 = 0 });
        var returning = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(returning);
        Assert.True(returning.Returning); Assert.Null(returning.Client!.Claim);
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
            new(birth.Key, returning.Snapshot.PositionX, returning.Snapshot.PositionY)));
        Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        Report(followup with { Ai0 = 1, Ai1 = 2290 });
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
            new(birth.Key, returning.Snapshot.PositionX, returning.Snapshot.PositionY)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(sourceStock, f.InventoryImage()); Assert.Equal(0, f.Count(2290));
        Assert.Equal(2, f.Item(f.BaitSlot).Stack); Assert.Equal(0, f.Funded(f.BaitSlot));

        TerrariaProjectileUpdateState SnapshotPacket()
        {
            var current = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
            return birth with { PositionX = current.PositionX, PositionY = current.PositionY,
                VelocityX = current.VelocityX, VelocityY = current.VelocityY };
        }
        void Report(TerrariaProjectileUpdateState packet) =>
            f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, packet) { ValidateVanillaFishingClient = true });
    }
}

public sealed partial class FishingAuthority1458Tests
{
    [Fact]
    public void Adopted_client_catch_drains_terminal_wire_after_inventory_observer_throws_once()
    {
        var observer = new FishingPublicationPlayerObserver();
        using var f = new FishingFixture(2, importedBait: true, observer: observer);
        var peer = ObserveFishingPublicationPeer(f);
        var birth = CastClientForBaitSwap(f);
        var claim = PreparePublicationClientPull(f, birth, 2290);
        ReportPublicationBaitDebit(f, claim);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait);
        ClosePublicationClientActor(f, birth, claim);
        Assert.Equal(0, CountFishingPublicationFrames(peer, 29));
        StagePublicationCatchReward(f);
        var failure = new FishingPublicationException("catch item observer"); observer.Armed = failure;
        Assert.Same(failure, Assert.Throws<FishingPublicationException>(() =>
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection))));
        Assert.Equal(1, f.Count(2290)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
        Assert.True(claim.RewardAdopted); Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        Assert.Equal(1, CountFishingPublicationFrames(peer, 29));
        Assert.False(f.Replication.WireIdentities.TryResolve(birth.Key, out _));
        long relayedBefore = f.Authority.RelayedUnknownDestroys;
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, f.Member.PositionX, f.Member.PositionY)));
        Assert.Equal(1, f.Count(2290));
        // Vanilla relays an unresolved duplicate29; it is not a second terminal
        // adoption or a second item grant.
        int duplicateWire = CountFishingPublicationFrames(peer, 29);
        Assert.Equal(1, f.Authority.RelayedUnknownDestroys - relayedBefore);
        Assert.Equal(0, duplicateWire - checked((int)(f.Authority.RelayedUnknownDestroys - relayedBefore)));
    }

    [Theory]
    [InlineData("retention", false)]
    [InlineData("retention", true)]
    [InlineData("birth", false)]
    [InlineData("birth", true)]
    public void Paid_client_NPC_publications_drain_independently_and_preserve_first_observer_exception(
        string throwChannel, bool earlierItemFailure)
    {
        var observer = new FishingPublicationPlayerObserver();
        var sink = new FishingPublicationNpcObserver();
        using var f = new FishingFixture(1, 2673, importedBait: true, observer: observer, npcSink: sink);
        for (int x = 112; x <= 166; x++) for (int y = 100; y <= 139; y++)
            f.Tiles.Set(x, y, new() { LiquidAmount = y == 100 ? (byte)128 : (byte)255 });
        for (int y = 100; y <= 140; y++) foreach (int x in new[] { 111, 167 })
            f.Tiles.Set(x, y, new() { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 111; x <= 167; x++) f.Tiles.Set(x, 140, new() { Type = 1, Flags = WorldTileFlags.Active });
        var peer = ObserveFishingPublicationPeer(f);
        var birth = CastClientForBaitSwap(f);
        WaitPublicationClientWater(f);
        ReportPublicationClient(f, PublicationPacket(f, birth) with { Ai1 = -200 });
        Assert.True(f.Players.TryCaptureFishingClientClaim(f.Connection, out var claim)); Assert.NotNull(claim);
        f.Apply(new FishingNpcClientRequestRuntimeCommand(f.Connection, new(370, -1, -1)));
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        ReportPublicationClient(f, PublicationPacket(f, birth) with { Ai0 = 2, Ai1 = -200 });
        Assert.True(claim.PullReported); Assert.False(claim.PaymentCommitted);
        ClosePublicationClientActor(f, birth, claim);
        Assert.False(claim.PaymentCommitted); Assert.Equal(0, CountFishingPublicationFrames(peer, 29));
        long spawns = f.Runtime.Npcs.AppliedSpawns;
        ReportPublicationBaitDebit(f, claim);
        var primary = new FishingPublicationException("earlier item observer");
        var secondary = new FishingPublicationException("NPC " + throwChannel + " observer");
        observer.Armed = earlierItemFailure ? primary : null;
        sink.Armed = secondary; sink.ThrowChannel = throwChannel;
        Assert.Same(earlierItemFailure ? primary : secondary, Assert.Throws<FishingPublicationException>(() =>
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection))));
        Assert.True(f.Item(f.BaitSlot).IsEmpty); Assert.Equal(0, f.Funded(f.BaitSlot));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait); Assert.True(claim.NpcSpawned);
        Assert.Equal(spawns + 1, f.Runtime.Npcs.AppliedSpawns);
        Assert.Equal(1, sink.RetentionAttempts); Assert.Equal(1, sink.BirthAttempts);
        Assert.True(claim.RewardAdopted); Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        Assert.Equal(1, CountFishingPublicationFrames(peer, 29));
        f.Apply(new FishingNpcClientRequestRuntimeCommand(f.Connection, new(370, -1, -1)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(spawns + 1, f.Runtime.Npcs.AppliedSpawns);
        Assert.Equal(1, sink.RetentionAttempts); Assert.Equal(1, sink.BirthAttempts);
        Assert.Equal(0, CountFishingPublicationFrames(peer, 29));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paid_client_pull_with_unsupported_held_timeout_never_uses_host_reward_fallback(bool closed)
    {
        using var f = new FishingFixture(2, importedBait: true);
        var birth = CastClientForBaitSwap(f);
        var claim = PreparePublicationClientPull(f, birth, 2290);
        ReportPublicationBaitDebit(f, claim);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait); Assert.Equal(0, f.Count(2290));
        if (closed) ClosePublicationClientActor(f, birth, claim);
        f.Control(false, selected: 1);
        for (int i = 0; i < 260; i++) f.Tick();
        Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        Assert.Equal(0, f.Count(2290)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, f.Member.PositionX, f.Member.PositionY)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(0, f.Count(2290)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
    }

    [Fact]
    public void Supported_paid_client_claim_expires_without29_and_late_inventory_cannot_revive_reward()
    {
        using var f = new FishingFixture(2, importedBait: true);
        var peer = ObserveFishingPublicationPeer(f);
        var birth = CastClientForBaitSwap(f);
        // Wooden Crate is a real source water catch and its existing physical
        // ceiling also distinguishes refused late reports from an earned grant.
        var claim = PreparePublicationClientPull(f, birth, 2334);
        ReportPublicationBaitDebit(f, claim);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.True(claim.PaymentCommitted); Assert.True(claim.ConsumedBait); Assert.False(claim.ClientClosed);
        for (int i = 0; i <= ProjectileAuthority.FishingClientClaimMaximumTicks; i++) f.Tick();
        if (f.Authority.CaptureFishingBobber(f.Connection) is not null)
            Assert.False(f.Players.IsFishingClientClaimCurrent(claim));
        long relayedBefore = f.Authority.RelayedUnknownDestroys;
        int terminalBeforeLate = CountFishingPublicationFrames(peer, 29);
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
            new(birth.Key, f.Member.PositionX + 3, f.Member.PositionY + 14)));
        StagePublicationCatchReward(f, 2334);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(0, f.Count(2334)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
        Assert.False(claim.RewardAdopted); Assert.False(claim.ClientClosed);
        Assert.Null(f.Authority.CaptureFishingBobber(f.Connection));
        int terminalAfterLate = CountFishingPublicationFrames(peer, 29);
        // Source forwards an unresolved late29 even after the authoritative
        // expiry29. Count that relay separately from the one terminal adoption.
        Assert.Equal(1, terminalBeforeLate + terminalAfterLate -
            checked((int)(f.Authority.RelayedUnknownDestroys - relayedBefore)));
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection,
            new(birth.Key, f.Member.PositionX + 3, f.Member.PositionY + 14)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        Assert.Equal(0, f.Count(2334)); Assert.Equal(1, f.Item(f.BaitSlot).Stack);
    }

    private static FishingClientClaim1458 PreparePublicationClientPull(FishingFixture f,
        TerrariaProjectileUpdateState birth, int item)
    {
        WaitPublicationClientWater(f);
        ReportPublicationClient(f, PublicationPacket(f, birth) with { Ai1 = -180 });
        Assert.True(f.Players.TryCaptureFishingClientClaim(f.Connection, out var claim)); Assert.NotNull(claim);
        f.Control(false); f.Control(true); f.Tick(); f.Control(false);
        ReportPublicationClient(f, PublicationPacket(f, birth) with { Ai0 = 1, Ai1 = item });
        Assert.True(claim.PullReported); Assert.False(claim.PaymentCommitted);
        return claim;
    }

    private static void WaitPublicationClientWater(FishingFixture f)
    {
        for (int i = 0; i < 400; i++)
        { f.Tick(); if (f.Authority.CaptureFishingBobber(f.Connection)!.Client!.SettledFrames >= 20) return; }
        throw new InvalidOperationException("Real bobber did not settle in source water.");
    }

    private static TerrariaProjectileUpdateState PublicationPacket(FishingFixture f, TerrariaProjectileUpdateState birth)
    {
        var current = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
        return birth with { PositionX = current.PositionX, PositionY = current.PositionY,
            VelocityX = current.VelocityX, VelocityY = current.VelocityY };
    }

    private static void ReportPublicationClient(FishingFixture f, TerrariaProjectileUpdateState packet)
    {
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(packet, out var bytes));
        var buffer = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out var frame));
        Assert.Equal(TerrariaProjectileDecodeResult.Decoded, TerrariaProjectileDecoder.TryDecodeUpdate(frame, out var decoded));
        f.Apply(new ClientProjectileUpdateRuntimeCommand(f.Connection, decoded) { ValidateVanillaFishingClient = true });
    }

    private static void ReportPublicationBaitDebit(FishingFixture f, FishingClientClaim1458 claim)
    {
        var source = f.Item(claim.BaitSlot);
        var after = source.Stack == 1 ? default : source with { Stack = (short)(source.Stack - 1) };
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, after.ToCommitRequest(f.Connection.Player.Slot, claim.BaitSlot)));
    }

    private static void ClosePublicationClientActor(FishingFixture f, TerrariaProjectileUpdateState birth, FishingClientClaim1458 claim)
    {
        bool returned = false;
        for (int i = 0; i < 400; i++)
        {
            f.Tick(); var bobber = f.Authority.CaptureFishingBobber(f.Connection); Assert.NotNull(bobber);
            if (bobber.Snapshot.PositionX < f.Member.PositionX + 20 && bobber.Snapshot.PositionX + 14 > f.Member.PositionX &&
                bobber.Snapshot.PositionY < f.Member.PositionY + 42 && bobber.Snapshot.PositionY + 14 > f.Member.PositionY)
            { returned = true; break; }
        }
        Assert.True(returned);
        var ending = f.Authority.CaptureFishingBobber(f.Connection)!.Snapshot;
        f.Apply(new ClientProjectileDestroyRuntimeCommand(f.Connection, new(birth.Key, ending.PositionX, ending.PositionY)));
        Assert.True(claim.ClientClosed);
    }

    private static void StagePublicationCatchReward(FishingFixture f, int itemType = 2290)
    {
        var slots = Enumerable.Range(0, 59).Select(i => f.Item((short)i)).Select(item => new WorldItemPickupSlot1458(
            item.ItemType.Value, item.Stack, item.Prefix.Value, (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag) != 0)).ToArray();
        Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(new(itemType, 1, 0, false), slots, false, new(false, false, false), out var storage));
        Assert.NotNull(storage);
        foreach (var mutation in storage.Mutations)
        {
            var after = mutation.After;
            var item = new RuntimePlayerInventoryItem(new(after.Type), checked((short)after.Stack), new(after.Prefix),
                after.Favorite ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, item.ToCommitRequest(f.Connection.Player.Slot, checked((short)mutation.Slot))));
        }
    }

    private static TerrariaConnectionOutboundQueue ObserveFishingPublicationPeer(FishingFixture f)
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(maxFrames: 256, maxQueuedBytes: 65536, maxFrameBytes: 8192));
        var source = GameCommandSourceId.FromConnection(99401);
        Assert.True(f.Replication.TryRegister(source, queue));
        var peer = new ConnectionHandle(source, new(new(1), new(1)));
        f.Replication.PlayerSpawned(peer, new(new(1), 100, 103, 0, 0, 0, 0, 0));
        return queue;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InnerQueue")]
    private static extern BoundedOutboundQueue FishingPublicationFrames(TerrariaConnectionOutboundQueue queue);

    private static int CountFishingPublicationFrames(TerrariaConnectionOutboundQueue queue, byte messageId)
    {
        int count = 0;
        while (FishingPublicationFrames(queue).TryRead(out var frame))
        {
            var bytes = new ReadOnlySequence<byte>(frame.Bytes);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref bytes, out var decoded));
            if (decoded.MessageId == messageId) count++;
        }
        return count;
    }

    private sealed class FishingPublicationException(string message) : Exception(message);

    private sealed class FishingPublicationPlayerObserver : IRuntimePlayerEventSink
    {
        internal FishingPublicationException? Armed;
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            if (Armed is not { } failure) return;
            Armed = null; throw failure;
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }

    private sealed class FishingPublicationNpcObserver : INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal FishingPublicationException? Armed;
        internal string ThrowChannel = "";
        internal int RetentionAttempts, BirthAttempts;
        public void NpcBirthRetained(in NpcSnapshot snapshot)
        {
            if (snapshot.Type != 370) return;
            RetentionAttempts++;
            ThrowIfArmed("retention");
        }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        {
            if (kind != NpcStateCommitKind.Spawn || snapshot.Type != 370) return;
            BirthAttempts++;
            ThrowIfArmed("birth");
        }
        private void ThrowIfArmed(string channel)
        {
            if (Armed is not { } failure || channel != ThrowChannel) return;
            Armed = null; throw failure;
        }
    }
}
