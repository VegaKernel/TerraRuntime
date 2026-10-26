using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class FishingClientRewardAuthority1458Tests
{
    [Fact]
    public void Every_original_crate_client_outcome_is_paid_through_5_138_without_matching_server_random()
    {
        using var resource = typeof(FishingClientRewardAuthority1458Tests).Assembly.GetManifestResourceStream("OriginalFishingCrate1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var original = JsonDocument.Parse(gzip);
        int cases = 0;
        foreach (var row in original.RootElement.GetProperty("rows").EnumerateArray().Where(row => row.GetProperty("seed").GetInt32() == 1))
        {
            int crate = row.GetProperty("id").GetInt32(); bool remix = row.GetProperty("remix").GetBoolean();
            using var f = new FishingCrateAuthority1458Tests.Fixture(remix);
            f.Players.BindFishingCrateWorld(false);
            FundCrate(f, crate);
            var raw = OriginalGrants(row, crate, remix);
            var plan = Plan(f, 0, raw, remix);
            Assert.Empty(plan.Overflow);
            var random = ServerRandom(f); random.CopyStateFrom(new(987_654)); var beforeRandom = random.Clone();
            SendReports(f, f.Connection, plan);
            Assert.Equal(plan.After, f.ItemImage());
            Assert.True(f.ItemImage()[0].IsEmpty, $"crate={crate},remix={remix}");
            Assert.True(beforeRandom.HasSameState(random), $"clientcrate={crate} advanced unrelated server RNG");
            Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out short remaining)); Assert.Equal(0, remaining);
            foreach (short slot in Enumerable.Range(0, 58).Select(value => (short)value))
            {
                var item = f.ItemImage()[slot];
                if (item.IsEmpty || item.ItemType.Value is >= 71 and <= 74) continue;
                Assert.True(Store(f).TryGetFundedQuantity(f.Connection, slot, out short quantity)); Assert.Equal(item.Stack, quantity);
            }
            var accepted = f.ItemImage(); var quantities = Store(f).CaptureFundedQuantities(f.Connection);
            SendReports(f, f.Connection, plan); // Exact delayed duplicate images, not a second opening.
            Assert.Equal(accepted, f.ItemImage()); Assert.Equal(quantities, Store(f).CaptureFundedQuantities(f.Connection));
            Assert.True(beforeRandom.HasSameState(random)); Assert.Equal(0, f.Items.ActiveCount);
            cases++;
        }
        Assert.Equal(52, cases);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49)]
    public void Last_unit_and_reward_first_order_preserve_the_actual_client_prefix(short crateSlot)
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.Players.BindFishingCrateWorld(false); FundCrate(f, 2334);
        if (crateSlot != 0)
        {
            f.Set(0, 0, 0); f.Set(58, 2334, 1); Complete(f, f.Connection);
            f.Set(crateSlot, 2334, 1); f.Set(58, 0, 0); Complete(f, f.Connection);
        }
        // Actual official wooden-crate seed1: accessory953, prefix80.
        var plan = Plan(f, crateSlot, [new(953, 1, 80)], false);
        var random = ServerRandom(f); random.CopyStateFrom(new(31)); var beforeRandom = random.Clone();
        SendReports(f, f.Connection, plan);
        Assert.Equal(plan.After, f.ItemImage()); Assert.True(f.ItemImage()[crateSlot].IsEmpty);
        Assert.Contains(f.ItemImage(), item => item.ItemType.Value == 953 && item.Prefix.Value == 80);
        Assert.True(beforeRandom.HasSameState(random));
        if (crateSlot == 49)
        {
            // Original empty-inventory seed1 places this non-usable accessory
            // in49. The still-occupied last crate makes GetItem choose48 here.
            int rewardSlot = Array.FindIndex(plan.After, item => item.ItemType.Value == 953);
            Assert.Equal(48, rewardSlot); Assert.True(rewardSlot < crateSlot);
        }
    }

    [Theory]
    [InlineData("required")]
    [InlineData("exclusive")]
    [InlineData("stack")]
    [InlineData("prefix")]
    [InlineData("extra")]
    public void Malicious_full_client_cohorts_restore_paid_source_and_never_fund_rewards(string failure)
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.Players.BindFishingCrateWorld(false); FundCrate(f, 2334);
        Grant[] raw = failure switch
        {
            "required" => [new(2674, 4)],
            "exclusive" => [new(3200, 1), new(3201, 1)],
            "stack" => [new(2323, 4)],
            "prefix" => [new(2323, 3), new(2674, 4, 1)],
            _ => [new(2323, 3), new(2674, 4), new(9, 1)]
        };
        var before = f.ItemImage(); var quantities = Store(f).CaptureFundedQuantities(f.Connection);
        var random = ServerRandom(f); var beforeRandom = random.Clone();
        SendReports(f, f.Connection, Plan(f, 0, raw, false));
        Assert.Equal(before, f.ItemImage()); Assert.Equal(quantities, Store(f).CaptureFundedQuantities(f.Connection));
        Assert.True(Store(f).CanSpendFishingStock(f.Connection, 0, 2334));
        Assert.True(beforeRandom.HasSameState(random)); Assert.Equal(0, f.Items.ActiveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_inventory_21_before_5_pays_two_overflow_grants_once_and_preserves_last_crate_order(bool selectedCrate)
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.Players.BindFishingCrateWorld(false); FundCrate(f, 2334);
        FillMainWithOwnedDirt(f);
        if (!selectedCrate) Select(f, 1);
        var plan = Plan(f, 0, [new(2323, 3), new(2674, 4)], false);
        Assert.Equal(2, plan.Overflow.Length);
        Assert.True(plan.After[0].IsEmpty); // Last source is debited after both storage/overflow attempts.
        var random = ServerRandom(f); var beforeRandom = random.Clone();
        foreach (var offer in plan.Overflow)
        {
            var completion = new TaskCompletionSource<WorldItemSnapshot?>();
            f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, offer, completion));
            Assert.Null(await completion.Task); Assert.Equal(0, f.Items.ActiveCount);
            Assert.Equal(2334, f.ItemImage()[0].ItemType.Value);
        }
        SendReports(f, f.Connection, plan, sendOverflow: false);
        Assert.Equal(plan.After, f.ItemImage());
        Assert.Equal(2, f.Items.ActiveCount);
        var world = new WorldItemSnapshot[f.Items.Capacity]; int count = f.Items.CopyActive(world);
        Assert.Equal(2, count);
        for (int i = 0; i < count; i++)
        {
            var item = world[i];
            Assert.Contains(plan.Overflow, offer => offer.ItemNetId == item.ItemNetId && offer.Stack == item.Stack &&
                offer.Prefix == item.Prefix && offer.PositionX == item.PositionX && offer.PositionY == item.PositionY &&
                offer.VelocityX == item.VelocityX && offer.VelocityY == item.VelocityY);
            Assert.True(f.Items.TryGetFundedQuantity(item, out int quantity)); Assert.Equal(item.Stack, quantity);
        }
        Assert.True(beforeRandom.HasSameState(random));
        var accepted = f.ItemImage();
        SendReports(f, f.Connection, plan, sendOverflow: false);
        Assert.Equal(accepted, f.ItemImage()); Assert.Equal(2, f.Items.ActiveCount);
        Assert.True(beforeRandom.HasSameState(random));
    }

    [Theory]
    [InlineData("velocity")]
    [InlineData("position")]
    [InlineData("duplicate")]
    public void Forged_overflow_cannot_create_world_items_or_spend_the_crate(string failure)
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.Players.BindFishingCrateWorld(false); FundCrate(f, 2334);
        FillMainWithOwnedDirt(f);
        Select(f, 1);
        var before = f.ItemImage(); var random = ServerRandom(f); var beforeRandom = random.Clone();
        var plan = Plan(f, 0, [new(2323, 3), new(2674, 4)], false);
        var first = plan.Overflow[0];
        first = failure switch { "velocity" => first with { VelocityX = 4 }, "position" => first with { PositionX = first.PositionX + 1 }, _ => first };
        f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, first));
        f.State.Apply(new WorldItemAllocateRuntimeCommand(f.Connection, failure == "duplicate" ? first : plan.Overflow[1]));
        SendReports(f, f.Connection, plan, sendOverflow: false);
        Assert.Equal(before, f.ItemImage()); Assert.Equal(0, f.Items.ActiveCount);
        Assert.True(beforeRandom.HasSameState(random)); Assert.True(Store(f).CanSpendFishingStock(f.Connection, 0, 2334));
    }

    [Fact]
    public async Task Paid_client_reward_and_consumed_source_survive_managed_reconnect_without_replay_credit()
    {
        using var f = new FishingCrateAuthority1458Tests.Fixture();
        f.Players.BindFishingCrateWorld(false); FundCrate(f, 2334);
        var plan = Plan(f, 0, [new(2323, 3), new(2674, 4)], false);
        SendReports(f, f.Connection, plan);
        var accepted = f.ItemImage(); var quantities = Store(f).CaptureFundedQuantities(f.Connection);
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = await detached.Task; Assert.NotNull(transfer);
        var current = new ConnectionHandle(f.Connection.Source, new(f.Connection.Player.Slot, new(f.Connection.Player.Generation.Value + 1)));
        var attached = new TaskCompletionSource<bool>();
        f.State.Apply(new PlayerTransferAttachRuntimeCommand(current, transfer, 100, 103, true, false, attached));
        Assert.True(await attached.Task);
        SendReports(f, f.Connection, plan); // Retired occupation cannot write its old receipt.
        SendReports(f, current, plan); // Current occupation cannot reopen an absent source.
        var actual = new RuntimePlayerInventoryItem[59]; Assert.True(Store(f).TryCopyInventory(current, actual));
        Assert.Equal(accepted, actual); Assert.Equal(quantities, Store(f).CaptureFundedQuantities(current));
        Assert.False(Store(f).CanSpendFishingStock(current, 0, 2334)); Assert.Equal(0, f.Items.ActiveCount);
    }

    private readonly record struct Grant(int Type, int Stack, int Prefix = 0);
    private sealed record ClientPlan(RuntimePlayerInventoryItem[] Before, RuntimePlayerInventoryItem[] After, WorldItemDropStateUpdate[] Overflow);
    private static RuntimePlayerInventoryStore Store(FishingCrateAuthority1458Tests.Fixture f) => FishingCrateAuthority1458Tests.Store(f.Players);
    private static VanillaUnifiedRandom1458 ServerRandom(FishingCrateAuthority1458Tests.Fixture f) =>
        ((SystemWorldItemSpawnRandom)f.Runtime.WorldItems.SpawnRandom).SourceRandom;
    private static void FundCrate(FishingCrateAuthority1458Tests.Fixture f, int crate)
    {
        var source = f.Drop(checked((short)crate), 1); f.State.Apply(new WorldItemRemoveRuntimeCommand(f.Connection, source.Handle));
        Assert.Equal(crate, f.ItemImage()[0].ItemType.Value);
        Assert.True(Store(f).TryGetFundedQuantity(f.Connection, 0, out short paid)); Assert.Equal(1, paid);
    }
    private static void FillMainWithOwnedDirt(FishingCrateAuthority1458Tests.Fixture f)
    {
        // Raw Set1..49 would leave an observed journal whose retained image
        // predates the fill. Establish this scenario through the existing owner
        // before any source overflow/report sequence starts.
        Assert.True(f.Players.TryFishingRewards(f.Connection, -1, 0, false, rewards =>
        {
            for (int i = 0; i < 49; i++) if (!rewards.Grant(3, 9999)) return false;
            return true;
        }));
        Assert.Equal(49, f.ItemImage().Count(item => item.ItemType.Value == 3 && item.Stack == 9999));
        Assert.True(f.Players.TryGet(f.Connection, out var member)); Assert.Null(member.CursorMove?.Shop);
        Assert.True(Store(f).TryCaptureShopJournal(f.Connection, out var capture)); Assert.NotNull(capture);
        Assert.Equal(f.ItemImage(), capture.Before.AsSpan(0, 59).ToArray());
    }
    private static Grant[] OriginalGrants(JsonElement row, int crate, bool remix)
    {
        var raw = row.GetProperty("official").GetProperty("inventory").EnumerateArray()
            .Where(item => item.GetProperty("type").GetInt32() != 0)
            .Select(item => new Grant(item.GetProperty("type").GetInt32(), item.GetProperty("stack").GetInt32(), item.GetProperty("prefix").GetInt32())).ToArray();
        Assert.True(VanillaFishingCrate1458.TryValidateOutcome(crate, remix, false,
            raw.Select(item => new FishingReward1458(item.Type, item.Stack)).ToArray(), out var order));
        return order.Select(item => raw.Single(rawItem => rawItem.Type == item.Type)).ToArray();
    }
    private static ClientPlan Plan(FishingCrateAuthority1458Tests.Fixture f, short sourceSlot, IReadOnlyList<Grant> grants, bool remix)
    {
        var before = f.ItemImage(); var after = before.ToArray(); var overflow = new List<WorldItemDropStateUpdate>();
        var random = new VanillaUnifiedRandom1458(20000); var world = new VanillaItemPrefixWorld1458(remix, false, false);
        Assert.True(f.Players.TryGet(f.Connection, out var member)); var actor = member.CaptureSnapshot();
        foreach (var grant in grants)
        {
            var incoming = new WorldItemPickupSlot1458(grant.Type, grant.Stack, grant.Prefix, false);
            var slots = after.Select(item => new WorldItemPickupSlot1458(item.ItemType.Value, item.Stack, item.Prefix.Value, false)).ToArray();
            Assert.True(VanillaWorldItemOwner1458.TryPlanStorage(in incoming, slots, false, world, out var storage)); Assert.NotNull(storage);
            foreach (var mutation in storage.Mutations)
                after[mutation.Slot] = mutation.After.Type == 0 ? default : new(new(mutation.After.Type), checked((short)mutation.After.Stack), new(mutation.After.Prefix), 0);
            var residual = storage.Residual;
            if (residual.Type == 0 || residual.Stack <= 0) continue;
            Assert.True(VanillaWorldItemOwner1458.TryGetSourceCreationNoGravity(new(residual.Type), out bool noGravity));
            overflow.Add(new(actor.PositionX + 10 - VanillaWorldItemOwner1458.PhysicalWidth / 2f,
                actor.PositionY + 21 - VanillaWorldItemOwner1458.PhysicalHeight / 2f,
                random.Next(-30, 31) * .1f, (noGravity ? random.Next(-30, 31) : random.Next(-40, -15)) * .1f,
                checked((short)residual.Stack), checked((byte)residual.Prefix), WorldItemOwnershipMode.ReserveForLocalPlayer,
                checked((short)residual.Type), false, 0, 100));
        }
        after[sourceSlot] = after[sourceSlot].Stack == 1 ? default : after[sourceSlot] with { Stack = (short)(after[sourceSlot].Stack - 1) };
        return new(before, after, overflow.ToArray());
    }
    private static void SendReports(FishingCrateAuthority1458Tests.Fixture f, ConnectionHandle connection, ClientPlan plan, bool sendOverflow = true)
    {
        if (sendOverflow)
            foreach (var offer in plan.Overflow) f.State.Apply(new WorldItemAllocateRuntimeCommand(connection, offer));
        for (short slot = 0; slot < plan.After.Length; slot++)
        {
            if (plan.Before[slot] == plan.After[slot]) continue;
            var request = plan.After[slot].ToCommitRequest(connection.Player.Slot, slot);
            var bytes = TerrariaPlayerEquipmentCodec.Encode(new(connection.Player.Slot.Value, slot, request.Stack, request.Prefix, request.ItemNetId, request.ItemFlags));
            var frame = new TerrariaFrame(checked((ushort)bytes.Length), bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(frame, out var decoded));
            f.State.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, decoded.SlotId, decoded.Stack, decoded.Prefix, decoded.ItemNetId, decoded.ItemFlags)));
        }
        Complete(f, connection);
    }
    private static void Complete(FishingCrateAuthority1458Tests.Fixture f, ConnectionHandle connection)
    {
        // Packet138 has the source's empty payload, represented by its typed command.
        f.State.Apply(new PlayerInventorySyncCompleteRuntimeCommand(connection));
        int slices = 0;
        while (!f.Players.CanApplyShopSource(connection.Source)) { Assert.True(slices++ < 100_000); f.Players.TickShopJournalWork(); }
    }
    private static void Select(FishingCrateAuthority1458Tests.Fixture f, byte slot)
    {
        Assert.True(f.Players.TryGet(f.Connection, out var member)); var p = member.CaptureSnapshot();
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 64, 16, 0, 0, slot,
            p.PositionX, p.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
    }
}
