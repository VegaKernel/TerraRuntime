using Xunit;
using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Network;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public static class BuffPotionBoundaryProbe
{
    public static void Run()
    {
        CursorPairNeighbor();
        int failures = 0;
        foreach (var (name, proof) in new (string, Action)[] { (nameof(SourceBatches), SourceBatches), (nameof(NextBoundary), NextBoundary), (nameof(UnfundedFullSourceBatch), UnfundedFullSourceBatch), (nameof(UnfundedPhaseFirstSourceBatch), UnfundedPhaseFirstSourceBatch), (nameof(SubsetRemoval), SubsetRemoval), (nameof(SparseRemoval), SparseRemoval), (nameof(ReceiptLifecycle), ReceiptLifecycle), (nameof(PublicationAndCurrentness), PublicationAndCurrentness) })
        {
            try { proof(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + " " + error); }
        }
        if (failures != 0) throw new InvalidOperationException($"{failures} boundary failures");
    }

    // A configured source-supported context0 split is inventory movement, not paid use.
    private static void CursorPairNeighbor()
    {
        using var fixture = new Fixture("empty-first", runtimeTick: true);
        var connection = fixture.Connections[2];
        var inventory = Inventory(fixture.Players);
        fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, 292, 0)));
        short[] funding = new short[59];
        funding[0] = 1;
        inventory.AdoptFundedQuantities(connection, funding);
        fixture.Move(2, false);
        fixture.Tick();
        fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 2, 0, 292, 0)));
        fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 58, 1, 0, 292, 0)));
        fixture.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        fixture.Tick();
        Assert.True(inventory.TryGet(connection, 0, out var selected) && selected.Stack == 2);
        Assert.True(inventory.TryGet(connection, 58, out var cursor) && cursor.Stack == 1 && cursor.ItemType.Value == 292);
        Assert.Equal(1, inventory.CaptureFundedQuantities(connection).Sum(value => (int)value));
        Assert.Equal(0, fixture.Buffs(2).GetDuration(new(5)));
        Assert.False(fixture.Member(2).ItemPhase?.SelectedPotionUse is { Completed: true });
    }

    private static void SourceBatches()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream($"SelectedBuffPotion{platform}First1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            int cases = 0;
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() || row.GetProperty("mode").GetString() is not
                    ("ordinary" or "last-stack" or "last-early-sync" or "existing-shorter" or "existing-longer" or "control-false" or "release-false")) continue;
                using var fixture = new Fixture("inactive-first", runtimeTick: true);
                var connection = fixture.Connections[2];
                var inventory = Inventory(fixture.Players);
                var before = row.GetProperty("before");
                int type = row.GetProperty("itemId").GetInt32();
                short stack = (short)before.GetProperty("inventory")[0].GetProperty("stack").GetInt32();
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, stack, 0, (short)type, 0)));
                short[] funding = new short[59]; funding[0] = 1;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false);
                fixture.Tick();
                var member = fixture.Member(2);
                member.ItemPhase = member.ItemPhase!.Value with
                {
                    Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = before.GetProperty("releaseUseItem").GetBoolean() },
                    Mana = member.ItemPhase.Value.Mana with { Mana = 10, Delay = 1000, Count = 0 }
                };
                var previous = fixture.Buffs(2);
                var originalTypes = before.GetProperty("buffType").EnumerateArray().Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray();
                var originalTimes = before.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                var imported = PlayerBuffState.FromSlots(originalTypes, originalTimes);
                Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, previous, imported));
                fixture.Move(2, before.GetProperty("controlUseItem").GetBoolean());
                fixture.Random.SetSeed(row.GetProperty("seed").GetInt32());
                bool early = row.GetProperty("mode").GetString() == "last-early-sync";
                bool noSync = row.GetProperty("mode").GetString() is "control-false" or "release-false";
                Replay(fixture, row, preUse: early, expectDelimiter: !noSync);
                fixture.Tick();
                var expected = row.GetProperty("phases")[0];
                fixture.Buffs(2).CaptureSlots(out var types, out var times);
                Assert.True(types.Select(value => (int)value.Value).SequenceEqual(expected.GetProperty("state").GetProperty("buffType").EnumerateArray().Select(value => value.GetInt32())), $"source44types {type}/{platform}/{row.GetProperty("mode").GetString()}");
                Assert.True(times.SequenceEqual(expected.GetProperty("state").GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32())), $"source44duration {type}/{platform}/{row.GetProperty("mode").GetString()}");
                bool paid = row.GetProperty("mode").GetString() is not ("control-false" or "release-false");
                Assert.Equal((short)(paid ? 0 : 1), inventory.CaptureFundedQuantities(connection)[0]);
                for (short slot = 0; slot < 59; slot++)
                {
                    Assert.True(inventory.TryGet(connection, slot, out var item));
                    var wanted = row.GetProperty("afterSync").GetProperty("inventory")[slot];
                    Assert.Equal(wanted.GetProperty("type").GetInt32(), (int)item.ItemType.Value);
                    Assert.Equal(wanted.GetProperty("stack").GetInt32(), (int)item.Stack);
                    Assert.Equal(wanted.GetProperty("prefix").GetInt32(), (int)item.Prefix.Value);
                }
                var clock = member.ItemPhase!.Value.Selected;
                var wantedClock = expected.GetProperty("state");
                Assert.Equal(wantedClock.GetProperty("itemTime").GetInt32(), clock.ItemTime);
                Assert.Equal(wantedClock.GetProperty("itemTimeMax").GetInt32(), clock.ItemTimeMax);
                Assert.Equal(wantedClock.GetProperty("itemAnimation").GetInt32(), clock.Animation);
                Assert.Equal(wantedClock.GetProperty("itemAnimationMax").GetInt32(), clock.AnimationMax);
                Assert.True(RandomArray(fixture.Random).SequenceEqual(expected.GetProperty("random").GetProperty("state").EnumerateArray().Select(value => value.GetInt32())), $"source56 {type}/{platform}");
                Assert.Equal(expected.GetProperty("random").GetProperty("cursor").GetUInt32(), RandomCursor(fixture.Random));
                Assert.Equal(expected.GetProperty("next").GetInt32(), fixture.Random.Clone().Next());
                cases++;
            }
            Console.WriteLine($"source-batch {platform}: {cases} cases");
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomArray(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint RandomCursor(VanillaUnifiedRandom1458 random);

    private static void NextBoundary()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream($"SelectedBuffPotionNextBoundary{platform}First1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            int cases = 0;
            foreach (JsonElement row in document.RootElement.EnumerateArray())
            {
                if (!row.GetProperty("local").GetBoolean() || row.GetProperty("mode").GetString() == "immune") continue;
                using var fixture = new Fixture("inactive-first", runtimeTick: true);
                var connection = fixture.Connections[2];
                var inventory = Inventory(fixture.Players);
                int type = row.GetProperty("itemId").GetInt32();
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, (short)type, 0)));
                short[] funding = new short[59]; funding[0] = 1;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false); fixture.Tick();
                var member = fixture.Member(2);
                member.ItemPhase = member.ItemPhase!.Value with { Mana = member.ItemPhase.Value.Mana with { Mana = 10, Delay = 1000, Count = 0 } };
                var before = row.GetProperty("before");
                var previous = fixture.Buffs(2);
                var imported = PlayerBuffState.FromSlots(
                    before.GetProperty("buffType").EnumerateArray().Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray(),
                    before.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32()).ToArray());
                Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, previous, imported));
                fixture.Random.SetSeed(row.GetProperty("seed").GetInt32());
                fixture.Move(2, true); fixture.Tick();
                fixture.Buffs(2).CaptureSlots(out var paidTypes, out var paidTimes);
                var paid = row.GetProperty("afterUse");
                Assert.True(paidTypes.Select(value => (int)value.Value).SequenceEqual(paid.GetProperty("buffType").EnumerateArray().Select(value => value.GetInt32())));
                Assert.True(paidTimes.SequenceEqual(paid.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32())));
                Assert.Equal((short)0, inventory.CaptureFundedQuantities(connection)[0]);
                Assert.True(RandomArray(fixture.Random).SequenceEqual(row.GetProperty("afterUseRandom").GetProperty("state").EnumerateArray().Select(value => value.GetInt32())));
                Assert.Equal(row.GetProperty("afterUseRandom").GetProperty("cursor").GetUInt32(), RandomCursor(fixture.Random));
                Assert.Equal(row.GetProperty("afterUseNext").GetInt32(), fixture.Random.Clone().Next());
                if (row.GetProperty("mode").GetString() == "ordinary")
                {
                    var current = member.ItemPhase!.Value.DerivedCombat!.Value;
                    Assert.Equal(0, current.Defense);
                    Assert.Equal(BitConverter.SingleToInt32Bits(1f), BitConverter.SingleToInt32Bits(current.ArrowDamage));
                    Assert.Equal(BitConverter.SingleToInt32Bits(1f), BitConverter.SingleToInt32Bits(current.MeleeDamage));
                    Assert.Equal(BitConverter.SingleToInt32Bits(0f), BitConverter.SingleToInt32Bits(current.Endurance));
                    Assert.False(current.Archery);
                }
                fixture.Tick();
                var expected = row.GetProperty("phases")[0].GetProperty("state");
                var combat = member.ItemPhase!.Value.DerivedCombat!.Value;
                Assert.Equal(expected.GetProperty("statDefense").GetInt32(), combat.Defense);
                Assert.Equal(expected.GetProperty("meleeCrit").GetInt32(), combat.MeleeCrit);
                Assert.Equal(expected.GetProperty("rangedCrit").GetInt32(), combat.RangedCrit);
                Assert.Equal(expected.GetProperty("magicCrit").GetInt32(), combat.MagicCrit);
                Assert.Equal(expected.GetProperty("archery").GetBoolean(), combat.Archery);
                var bits = expected.GetProperty("combatFloatBits");
                foreach (var pair in new (string Name, float Value)[] { ("endurance", combat.Endurance), ("meleeDamage", combat.MeleeDamage),
                    ("rangedDamage", combat.RangedDamage), ("magicDamage", combat.MagicDamage), ("minionDamage", combat.MinionDamage), ("arrowDamage", combat.ArrowDamage) })
                    Assert.Equal(bits.GetProperty(pair.Name).GetInt32(), BitConverter.SingleToInt32Bits(pair.Value));
                fixture.Buffs(2).CaptureSlots(out var remoteTypes, out var remoteTimes);
                Assert.True(paidTypes.SequenceEqual(remoteTypes) && paidTimes.SequenceEqual(remoteTimes), "remote presence policy retains durations; local original UpdateBuffs separately decrements");
                Assert.Equal(expected.GetProperty("ammoPotion").GetBoolean(), remoteTypes.Any(value => value.Value == 112));
                cases++;
            }
            Console.WriteLine($"next-boundary {platform}: {cases} cases; actual Single bits, remote duration policy");
        }
    }

    private static void UnfundedFullSourceBatch() => UnfundedFullSourceBatch(false);

    private static void UnfundedPhaseFirstSourceBatch() => UnfundedFullSourceBatch(true);

    private static void UnfundedFullSourceBatch(bool phaseFirst)
    {
        using var fixture = new Fixture("empty-first", runtimeTick: true);
        var connection = fixture.Connections[2];
        var inventory = Inventory(fixture.Players);
        fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, 292, 0)));
        inventory.AdoptFundedQuantities(connection, new short[59]);
        fixture.Move(2, false);
        fixture.Tick();
        using var stream = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream("SelectedBuffPotionLinuxFirst1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        var row = original.RootElement.EnumerateArray().Single(value => value.GetProperty("local").GetBoolean() &&
            value.GetProperty("itemId").GetInt32() == 292 && value.GetProperty("mode").GetString() == "ordinary");
        if (phaseFirst) { fixture.Move(2, true); fixture.Tick(); }
        Replay(fixture, row);
        fixture.Tick();
        Assert.Equal((short)0, inventory.CaptureFundedQuantities(connection)[0]);
        fixture.Buffs(2).CaptureSlots(out var deniedTypes, out var deniedTimes);
        fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot,
            new[] { new BuffTypeId(5), new BuffTypeId(16) })));
        fixture.Buffs(2).CaptureSlots(out var afterIncrementTypes, out var afterIncrementTimes);
        Assert.Equal(deniedTypes, afterIncrementTypes);
        Assert.Equal(deniedTimes, afterIncrementTimes);
        Console.WriteLine($"q0 phaseFirst={phaseFirst} after full source batch: buff5={fixture.Buffs(2).GetDuration(new(5))};funded={inventory.CaptureFundedQuantities(connection)[0]}");
        Assert.Equal(0, fixture.Buffs(2).GetDuration(new(5)));
        Assert.True(fixture.Member(0).ItemPhase is not null, "second owned actor continues after q0 use denial");
    }

    private static void SubsetRemoval()
    {
        using var fixture = new Fixture("empty-first", runtimeTick: true);
        var connection = fixture.Connections[2];
        var inventory = Inventory(fixture.Players);
        foreach (short type in new short[] { 2349, 292 })
        {
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, type, 0)));
            short[] funding = new short[59];
            funding[0] = 3;
            inventory.AdoptFundedQuantities(connection, funding);
            fixture.Move(2, false);
            fixture.Tick();
            fixture.Move(2, true);
            fixture.Tick();
            Assert.True(fixture.Member(2).ItemPhase!.Value.SelectedPotionUse is { Completed: true });
            fixture.Move(2, false);
            for (int tick = 0; tick < 20; tick++) fixture.Tick();
        }
        fixture.Buffs(2).CaptureSlots(out var beforeTypes, out var beforeTimes);
        Assert.True(beforeTypes.Any(type => type.Value == 117) && beforeTypes.Any(type => type.Value == 5));
        int remaining = fixture.Buffs(2).GetDuration(new(5));
        Assert.True(remaining > 60);
        fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, new[] { new BuffTypeId(5) })));
        Assert.Equal(0, fixture.Buffs(2).GetDuration(new(117)));
        Console.WriteLine($"subset removal: remaining5 before={remaining};after={fixture.Buffs(2).GetDuration(new(5))}");
        Assert.Equal(remaining, fixture.Buffs(2).GetDuration(new(5)));
        fixture.Buffs(2).CaptureSlots(out var removedTypes, out var removedTimes);
        using Stream source = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream("SelectedBuffPotionRemoveLinuxFirst1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        var afterRemove = original.RootElement[0].GetProperty("afterRemove");
        Assert.True(removedTypes.Select(value => (int)value.Value).SequenceEqual(afterRemove.GetProperty("buffType").EnumerateArray().Select(value => value.GetInt32())));
        Assert.True(removedTimes.SequenceEqual(afterRemove.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32())));
        fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, beforeTypes.Where(value => value.Value != 0).ToArray())));
        fixture.Buffs(2).CaptureSlots(out var staleTypes, out var staleTimes);
        Assert.True(removedTypes.SequenceEqual(staleTypes) && removedTimes.SequenceEqual(staleTimes), "old exact50 after source subset removal never resurrects Wrath or truncates remaining Ironskin");
    }

    private static void SparseRemoval()
    {
        using Stream source = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream("SelectedBuffPotionSparseRemoveLinuxFirst1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        foreach (JsonElement row in original.RootElement.EnumerateArray())
        {
            using var fixture = new Fixture("empty-first", runtimeTick: true);
            var connection = fixture.Connections[2];
            var inventory = Inventory(fixture.Players);
            var expectedBefore = row.GetProperty("before");
            bool tail = expectedBefore.GetProperty("buffType")[43].GetInt32() != 0;
            if (tail)
            {
                var old = fixture.Buffs(2);
                var types = new BuffTypeId[44]; var times = new int[44];
                types[43] = new(115); times[43] = 777;
                Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, old, PlayerBuffState.FromSlots(types, times)));
            }
            foreach (short type in new short[] { 2349, 292, 303 })
            {
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, type, 0)));
                short[] funding = new short[59]; funding[0] = 3;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false); fixture.Tick();
                fixture.Move(2, true); fixture.Tick();
                Assert.True(fixture.Member(2).ItemPhase!.Value.SelectedPotionUse is { Completed: true });
                fixture.Move(2, false);
                for (int tick = 0; tick < 20; tick++) fixture.Tick();
            }
            var before = fixture.Buffs(2);
            var slots = PlayerBuffState.FromSlots(
                expectedBefore.GetProperty("buffType").EnumerateArray().Select(value => new BuffTypeId((ushort)value.GetInt32())).ToArray(),
                expectedBefore.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32()).ToArray());
            Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, before, slots));
            byte[] bytes = Convert.FromHexString(row.GetProperty("frames").EnumerateArray().Single(value => Convert.FromHexString(value.GetString()!)[2] == 50).GetString()!);
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded, TerrariaPlayerBuffCodec1458.TryDecode(in frame, out _, out var reported));
            fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, reported)));
            fixture.Buffs(2).CaptureSlots(out var finalTypes, out var finalTimes);
            var after = row.GetProperty("after");
            Assert.True(finalTypes.Select(value => (int)value.Value).SequenceEqual(after.GetProperty("buffType").EnumerateArray().Select(value => value.GetInt32())), "actual source sparse DelBuff preserves survivor index and slot43");
            Assert.True(finalTimes.SequenceEqual(after.GetProperty("buffTime").EnumerateArray().Select(value => value.GetInt32())), "actual source sparse DelBuff preserves every survivor duration");
        }
    }

    private static void ReceiptLifecycle()
    {
        foreach (short type in new short[] { 292, 303, 2344, 2346, 2347, 2349 })
            foreach (bool completed in new[] { false, true })
            {
                using var fixture = new Fixture("empty-first", runtimeTick: true);
                var connection = fixture.Connections[2];
                var inventory = Inventory(fixture.Players);
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, type, 0)));
                short[] funding = new short[59]; funding[0] = 1;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false); fixture.Tick();
                if (completed) { fixture.Move(2, true); fixture.Tick(); }
                else fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 2, 0, type, 0)));
                Assert.True(fixture.Member(2).ItemPhase!.Value.SelectedPotionUse is not null);
                var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
                fixture.Apply(new PlayerTransferDetachRuntimeCommand(connection, detach));
                Assert.True(detach.Task.IsCompletedSuccessfully);
                var transfer = detach.Task.Result!;
                Assert.NotNull(transfer);
                var replacement = new ConnectionHandle(GameCommandSourceId.FromConnection(99122), new(connection.Player.Slot, new(connection.Player.Generation.Value + 1)));
                var attach = new TaskCompletionSource<bool>();
                fixture.Apply(new PlayerTransferAttachRuntimeCommand(replacement, transfer, 100, 103, true, false, attach));
                Assert.True(attach.Task.IsCompletedSuccessfully && attach.Task.Result);
                fixture.Connections[2] = replacement;
                Assert.Null(fixture.Member(2).ItemPhase!.Value.SelectedPotionUse);
                Assert.True(transfer.FundedInventoryQuantities!.AsSpan().SequenceEqual(inventory.CaptureFundedQuantities(replacement)));
                Assert.True(transfer.OwnedInventoryQuantities!.AsSpan().SequenceEqual(inventory.CaptureOwnedQuantities(replacement)));
                var physical = new RuntimePlayerInventoryItem[59];
                Assert.True(inventory.TryCopyInventory(replacement, physical));
                Assert.True(transfer.Inventory.AsSpan().SequenceEqual(physical));
                var beforeOld = fixture.Member(2).CaptureSnapshot();
                fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, Array.Empty<BuffTypeId>())));
                Assert.Equal(beforeOld, fixture.Member(2).CaptureSnapshot());
            }
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(BuffPotionBoundaryProbe).Assembly.GetManifestResourceStream($"SelectedBuffPotion{platform}First1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var original = JsonDocument.Parse(gzip);
            foreach (var row in original.RootElement.EnumerateArray().Where(value => value.GetProperty("local").GetBoolean() && value.GetProperty("mode").GetString() == "ordinary"))
            {
                using var fixture = new Fixture("empty-first", runtimeTick: true);
                var connection = fixture.Connections[2];
                var inventory = Inventory(fixture.Players);
                short type = (short)row.GetProperty("itemId").GetInt32();
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, type, 0)));
                short[] funding = new short[59]; funding[0] = 1;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false); fixture.Tick();
                Replay(fixture, row); fixture.Tick();
                fixture.Buffs(2).CaptureSlots(out var paidTypes, out var paidTimes);
                var random = fixture.Random.Clone();
                Replay(fixture, row);
                fixture.Buffs(2).CaptureSlots(out var duplicateTypes, out var duplicateTimes);
                Assert.True(paidTypes.SequenceEqual(duplicateTypes) && paidTimes.SequenceEqual(duplicateTimes));
                Assert.True(fixture.Random.HasSameState(random));
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, type, 0)));
                Assert.Equal((short)0, inventory.CaptureFundedQuantities(connection)[0]);
                Replay(fixture, row);
                fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, Array.Empty<BuffTypeId>())));
                fixture.Buffs(2).CaptureSlots(out var removedTypes, out var removedTimes);
                Replay(fixture, row);
                fixture.Buffs(2).CaptureSlots(out var staleTypes, out var staleTimes);
                Assert.True(removedTypes.SequenceEqual(staleTypes) && removedTimes.SequenceEqual(staleTimes), "latest exact50 never renews a removed owned effect after physical restore");
                Assert.Equal((short)0, inventory.CaptureFundedQuantities(connection)[0]);
                var second = fixture.Member(0).ItemPhase!.Value.Selected;
                fixture.Tick();
                Assert.True(fixture.Member(0).ItemPhase is not null && fixture.Member(2).ItemPhase is not null, "late buff ACK never freezes the player census");
                Assert.True(fixture.Member(0).ItemPhase!.Value.Selected.RevolverCritBonus <= second.RevolverCritBonus);
            }
        }
    }

    private static void PublicationAndCurrentness()
    {
        foreach (bool reentry in new[] { false, true })
        {
            using var fixture = new Fixture("empty-first", runtimeTick: true);
            var connection = fixture.Connections[2];
            var inventory = Inventory(fixture.Players);
            fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, 292, 0)));
            short[] funding = new short[59]; funding[0] = 1;
            inventory.AdoptFundedQuantities(connection, funding);
            fixture.Move(2, false); fixture.Tick();
            var first = new InvalidOperationException("first owned buff item observer");
            int calls = 0;
            fixture.Events.OnEquipment = (actor, item) =>
            {
                if (actor != connection || item.SlotId != 0) return;
                calls++;
                Assert.Equal((short)0, inventory.CaptureFundedQuantities(connection)[0]);
                Assert.True(inventory.TryGet(connection, 0, out var actual) && actual.Stack == 2);
                Assert.Equal(28800, fixture.Buffs(2).GetDuration(new(5)));
                Assert.True(fixture.Member(2).ItemPhase!.Value.SelectedPotionUse is { Completed: true });
                if (reentry)
                    fixture.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(connection.Player.Slot, new[] { new BuffTypeId(5) })));
                else throw first;
            };
            fixture.Move(2, true);
            int start = fixture.Events.Seen.Count;
            Exception? observed = null;
            try { fixture.Tick(); } catch (Exception error) { observed = error; }
            Assert.Equal(1, calls);
            if (reentry) Assert.Null(observed);
            else Assert.True(ReferenceEquals(first, observed));
            Assert.True(fixture.Events.Seen.Skip(start).Contains((byte)50), "remaining current buff publication drains after first observer failure");
            Assert.Equal(28800, fixture.Buffs(2).GetDuration(new(5)));
        }
        foreach (bool wholeCaller in new[] { false, true })
            foreach (bool mutateBuff in new[] { false, true })
            {
                using var fixture = new Fixture("empty-first", runtimeTick: true);
                var connection = fixture.Connections[2];
                var inventory = Inventory(fixture.Players);
                fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, 292, 0)));
                short[] funding = new short[59]; funding[0] = 1;
                inventory.AdoptFundedQuantities(connection, funding);
                fixture.Move(2, false); fixture.Tick();
                fixture.Move(2, true);
                var random = fixture.Random.Clone();
                var first = fixture.Member(0).ItemPhase;
                bool invoked = false;
                fixture.Players.SetNpcHealthGrapplingFacts(_ =>
                {
                    if (!invoked)
                    {
                        invoked = true;
                        if (mutateBuff)
                        {
                            var old = fixture.Buffs(2);
                            var types = new BuffTypeId[44]; var times = new int[44];
                            types[0] = new(5); times[0] = 200;
                            Assert.True(Profiles(fixture.Players).TryAdoptBuffState(connection, old, PlayerBuffState.FromSlots(types, times)));
                        }
                        else fixture.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 4, 0, 292, 0)));
                    }
                    return false;
                });
                if (wholeCaller) fixture.Tick();
                else Assert.False(fixture.Players.TryTickRemotePlayerPhase());
                Assert.True(invoked, "actual external owner provider executes during census");
                if (!wholeCaller)
                {
                    Assert.True(fixture.Random.HasSameState(random));
                    Assert.Equal(first, fixture.Member(0).ItemPhase);
                }
                Assert.Equal((short)1, inventory.CaptureFundedQuantities(connection)[0]);
                Assert.Equal(mutateBuff ? 200 : 0, fixture.Buffs(2).GetDuration(new(5)));
                Assert.True(inventory.TryGet(connection, 0, out var physical));
                Assert.Equal((short)(mutateBuff ? 3 : 4), physical.Stack);
                Assert.False(fixture.Member(2).ItemPhase?.SelectedPotionUse is { Completed: true });
            }
    }

    private static void Replay(Fixture f, JsonElement row, bool preUse = false, bool skipInventory = false, bool expectDelimiter = true)
    {
        var c = f.Connections[2]; var member = f.Member(2);
        int delimiters = 0;
        foreach (var hex in row.GetProperty(preUse ? "checkFrames" : "syncFrames").EnumerateArray())
        {
            byte[] bytes = Convert.FromHexString(hex.GetString()!);
            if (skipInventory && bytes[2] == 5) continue;
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (bytes[2])
            {
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var item));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, item.SlotId, item.Stack, item.Prefix, item.ItemNetId, item.ItemFlags))); break;
                case 16:
                    Assert.Equal(TerrariaPlayerHealthDecodeResult.Decoded, TerrariaPlayerVitalsCodec.TryDecodeHealth(in frame, out var health));
                    f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, health.Life, health.MaxLife))); break;
                case 42:
                    Assert.Equal(TerrariaPlayerManaDecodeResult.Decoded, TerrariaPlayerVitalsCodec.TryDecodeMana(in frame, out var mana));
                    f.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, mana.Mana, mana.MaxMana))); break;
                case 50:
                    Assert.Equal(TerrariaPlayerBuffDecodeResult.Decoded, TerrariaPlayerBuffCodec1458.TryDecode(in frame, out _, out var types));
                    f.Apply(new PlayerBuffTypesRuntimeCommand(c, new(c.Player.Slot, types))); break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in frame, out var move));
                    f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, move.ControlFlags, move.MovementFlags, move.MiscFlags1, move.MiscFlags2, move.SelectedItem,
                        member.PositionX, member.PositionY, move.HasVelocity, move.VelocityX, move.VelocityY, move.HasMount, move.MountType,
                        move.HasPotionOfReturnPositions, move.PotionOfReturnOriginalPositionX, move.PotionOfReturnOriginalPositionY,
                        move.PotionOfReturnHomePositionX, move.PotionOfReturnHomePositionY, move.HasCameraTarget, move.CameraTargetX, move.CameraTargetY))); break;
                case 138: f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); delimiters++; break;
                case 35 or 43:
                    Assert.True(preUse && bytes.Length == 6, "original cosmetic effect, no vitals grant command");
                    break; // The separately accepted presentation relay has its own source/runtime proof; this buff-family replay contains no35/43.
                default: throw new InvalidOperationException("Unaccounted source packet " + bytes[2]);
            }
        }
        Assert.Equal(!expectDelimiter ? 0 : preUse && row.GetProperty("mode").GetString() != "last-early-sync" ? 0 : 1, delimiters);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
    private static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "membership")]
    private static extern ref RuntimePlayerMembership Membership(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "transferProfiles")]
    private static extern ref RuntimePlayerTransferProfileStore Profiles(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? UpdateRandom(PlayerAuthority owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
    private static extern ref ServerRuntimeComposition Composition(ServerRuntimeState owner);
    private sealed class Fixture : IDisposable
    {
        internal readonly PlayerAuthority Players;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly EventSink Events = new();
        internal readonly Dictionary<byte, ConnectionHandle> Connections = [];
        internal readonly Dictionary<byte, short> ItemIds = [];
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly HashSet<byte> moved = [];
        private readonly PlayerSlotPool pool = new(3);
        internal Fixture(string mode, bool runtimeTick = false)
        {
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var projectiles = new RuntimeProjectileStore();
            if (runtimeTick)
            {
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles,
                    townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 80 },
                    playerUpdateRandomSeed: new(0));
                Players = Composition(state).Players;
                Random = UpdateRandom(Players)!;
            }
            else { Players = new(Events, Tiles); Random = new(0); }
            // These prefix-none references have identical clocks on both source platforms.
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false)
            { WindowsItemPrefixArithmetic = OperatingSystem.IsWindows() });
            Players.SetPlayerUpdateRandom(Random);
            Players.SetRemotePlayerEnvironment(new(false, false), projectiles);
            Players.SetNpcHealthWorldFacts(() => new(false, false));
            Players.SetNpcHealthGrapplingFacts(_ => false);
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(out var lease));
                sessions.Add(new(lease!));
            }
            var membership = Membership(Players);
            // Slot255 is not a connection lease. A trusted imported source placeholder remains untouched.
            membership.Commit(new RuntimePlayerMember
            {
                Slot = new(255),
                Revision = 1,
                Connection = new(GameCommandSourceId.FromConnection(99255),
                new(new(255), new(1))),
                ItemPhase = null,
                PhysicsPhase = null
            });
            foreach (byte slot in mode == "reverse-join" ? new byte[] { 2, 0 } : new byte[] { 0, 2 })
            {
                if (slot == 0 && mode == "inactive-first") continue;
                var session = sessions[slot]; session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99000 + slot), session.Handle);
                Connections.Add(slot, connection);
                short id = slot == 2 ? (short)28 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
                if (slot == 2 && mode == "potion-first") id = 0;
                ItemIds.Add(slot, id);
                Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
                Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, id == 0 ? (short)0 : (short)3, 0, id, 0)));
                Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
                Move(slot, false);
                // Explicit source-configured imported lifecycle branches, after genuine membership creation.
                // Packet movement admission is not weakened to inject outside coordinates.
                if (slot == 0 && mode == "outside-first") { Member(slot).PositionX = 0; Member(slot).PositionY = 0; }
                if (slot == 0 && mode == "dead-first") Member(slot).IsDead = true;
                if (slot == 0 && mode == "ghost-first") Member(slot).MovementFlags |= VanillaPlayerHealthContext1458.GhostMovementFlag;
            }
        }
        internal RuntimePlayerMember Member(byte slot) { Assert.True(Players.TryGet(slot, out var member)); return member; }
        internal void Apply(RuntimeCommand command)
        {
            if (state is null) Assert.True(Players.TryApply(command));
            else state.Apply(command);
        }
        internal void Tick() { Assert.NotNull(state); state.Tick(); }
        internal bool Step()
        {
            if (state is null) return Players.TryTickRemotePlayerPhase();
            state.Tick();
            return Member(2).ItemPhase is not null;
        }
        internal void ReconnectFirst()
        {
            Apply(new PlayerDisconnectRuntimeCommand(Connections[0]));
            sessions[0].Dispose();
            Assert.True(pool.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            Assert.Equal(new PlayerSlotId(0), session.Slot);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(99100), session.Handle);
            Connections[0] = connection;
            Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 50, 400)));
            Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
            Apply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0)));
            Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
        }
        internal void Move(byte slot, bool control)
        {
            var member = Member(slot);
            bool first = moved.Add(slot);
            if (!first && member.PositionX == 0 && member.PositionY == 0)
            {
                // Trusted source-configured outside import: ordinary movement would reject this geometry.
                member.ControlFlags = control ? (byte)32 : (byte)0;
                return;
            }
            Apply(new PlayerMovementRuntimeCommand(Connections[slot], new(new(slot), control ? (byte)32 : (byte)0,
                (byte)(member.MovementFlags | 16), 0, 64, 0, first ? 1600 + slot * 40 : member.PositionX,
                first ? 1606 : member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal PlayerBuffState Buffs(byte slot) => Profiles(Players).CaptureBuffState(Connections[slot])!;
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Count++; Seen.Add(5); OnEquipment?.Invoke(c, r); }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) { Count++; Seen.Add(16); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Count++; Seen.Add(42); }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Count++; Seen.Add(50); }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
