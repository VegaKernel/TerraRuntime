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

public static class LifeProgressionBoundaryChecks
{
    // Original local ItemCheck consumption outputs; configured real State.Apply/Tick membership.
    // The source20 ItemCheck RNG/clock cohort is not a whole State.Update oracle.
    public static void Run()
    {
        foreach (string name in new[] { "LinuxFirst", "LinuxRepeat", "WindowsFirst", "WindowsRepeat" })
        {
            using var raw = typeof(LifeProgressionBoundaryChecks).Assembly.GetManifestResourceStream("LifeProgression" + name + "1458");
            Assert.NotNull(raw);
            using var gzip = new GZipStream(raw, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray().Where(r => r.GetProperty("local").GetBoolean() &&
                r.GetProperty("itemId").GetInt32() is 29 or 1291 &&
                r.GetProperty("mode").GetString() is "success-false" or "ordinary" or "last-stack" or "full-vitals" or "at-cap" or "near-cap" or "off-grid"))
            {
                foreach (short budget in new short[] { 0, 1, checked((short)row.GetProperty("before").GetProperty("inventory")[0].GetProperty("stack").GetInt32()) }.Distinct())
                    SourceBatch(row, budget);
                if (row.GetProperty("mode").GetString() != "ordinary") continue;
                var fresh = document.RootElement.EnumerateArray().Single(r => r.GetProperty("itemId").GetInt32() == row.GetProperty("itemId").GetInt32() && r.GetProperty("local").GetBoolean() && r.GetProperty("mode").GetString() == "success-false");
                AnimationBoundary(row, 3, fresh);
                foreach (bool selectionChange in new[] { false, true }) RetiredPendingBoundary(row, 1, selectionChange, fresh);
                foreach (short budget in new short[] { 0, 1 }) ConfiguredVitalsFirst(row, budget);
                foreach (bool throughState in new[] { false, true }) ProviderBoundary(row, 1, throughState);
                if (row.GetProperty("itemId").GetInt32() == 29)
                {
                    InterleavedHit(row, 1);
                    foreach (int callbackMode in new[] { 0, 1, 2 }) CallbackBoundary(row, 1, callbackMode);
                }
            }
        }
    }

    private static void SourceBatch(JsonElement row, short budget)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var bankBefore = new RuntimePlayerInventoryItem[160]; var bankCopperBefore = new long[160];
        Assert.True(store.TryCaptureShopBanks(c, bankBefore, bankCopperBefore));
        Assert.True(store.TryCaptureShopBankOwnership(c, out var bankQuantityBefore, out var bankCapsBefore));
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        Replay(f, row.GetProperty("syncFrames"));
        f.Tick();
        int sourceBefore = before.GetProperty("statLifeMax").GetInt32();
        int sourceAfter = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32();
        bool consumed = sourceAfter != sourceBefore;
        int expected = budget > 0 && consumed ? sourceAfter : sourceBefore;
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (budget > 0 && consumed)
        {
            for (short index = 0; index < 59; index++)
            {
                Assert.True(f.Players.TryGetInventoryItem(c, index, out var actual));
                var image = row.GetProperty("afterSync").GetProperty("inventory")[index];
                Assert.Equal(image.GetProperty("type").GetInt32(), actual.ItemType.Value);
                Assert.Equal(image.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(image.GetProperty("prefix").GetInt32(), actual.Prefix.Value);
                Assert.Equal(image.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0);
            }
        }
        // Fixed paid/denied explanation must survive raw stack restoration without maximum replay.
        Replay(f, row.GetProperty("syncFrames"));
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        var expectedQuantity = new short[59]; expectedQuantity[0] = (short)(budget > 0 && consumed ? budget - 1 : budget);
        Assert.Equal(expectedQuantity, store.CaptureFundedQuantities(c));
        var bankAfter = new RuntimePlayerInventoryItem[160]; var bankCopperAfter = new long[160];
        Assert.True(store.TryCaptureShopBanks(c, bankAfter, bankCopperAfter));
        Assert.True(store.TryCaptureShopBankOwnership(c, out var bankQuantityAfter, out var bankCapsAfter));
        Assert.Equal(bankBefore, bankAfter); Assert.Equal(bankCopperBefore, bankCopperAfter);
        Assert.Equal(bankQuantityBefore, bankQuantityAfter); Assert.Equal(bankCapsBefore, bankCapsAfter);
        if (consumed)
        {
            // Edit changes the completed explanation's item; permanent family ownership
            // must remain independent of the latest receipt and transfer lifecycle.
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 2, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 50, checked((short)(expected + 20)))));
            Assert.Equal(expected, member.BaseLifeMax); Assert.Equal(expected, member.MaxLife);
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.Apply(new PlayerTransferDetachRuntimeCommand(c, detached));
            Assert.True(detached.Task.IsCompletedSuccessfully); Assert.NotNull(detached.Task.Result);
            var transfer = detached.Task.Result!;
            var attached = new TaskCompletionSource<bool>();
            f.Apply(new PlayerTransferAttachRuntimeCommand(c, transfer, 100, 103, true, false, attached));
            Assert.True(attached.Task.IsCompletedSuccessfully); Assert.True(attached.Task.Result);
            var returned = f.Member(2);
            Assert.Null(returned.ItemPhase?.SelectedPotionUse);
            f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 50, checked((short)(expected + 20)))));
            Assert.Equal(expected, returned.BaseLifeMax); Assert.Equal(expected, returned.MaxLife);
            Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
            Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
            Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        }
    }

    private static void InterleavedHit(JsonElement row, short budget)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        f.Tick(); // Configured owned health initialization before the new source batch.
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        var frames = row.GetProperty("syncFrames").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Replay(f, frames.Take(1));
        Assert.True(member.ItemPhase?.SelectedPotionUse is { Completed: false });
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Players.TryCommitAuthoritativeNpcContactDamage(
            100, new(0, new(1)), c.Player, 5, 0, VanillaPlayerImmunityChannel1458.General, out _));
        int hitLife = member.NpcHealth!.Value.Life;
        Replay(f, frames.Skip(1));
        Assert.Equal(hitLife, member.NpcHealth.Value.Life);
        f.Tick();
        Assert.Equal(hitLife + 20, member.NpcHealth!.Value.Life);
        int sourceBefore = before.GetProperty("statLifeMax").GetInt32();
        int sourceAfter = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32();
        bool consumed = sourceAfter != sourceBefore;
        int expected = budget > 0 && consumed ? sourceAfter : sourceBefore;
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (budget > 0 && consumed)
        {
            for (short index = 0; index < 59; index++)
            {
                Assert.True(f.Players.TryGetInventoryItem(c, index, out var actual));
                var image = row.GetProperty("afterSync").GetProperty("inventory")[index];
                Assert.Equal(image.GetProperty("type").GetInt32(), actual.ItemType.Value);
                Assert.Equal(image.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(image.GetProperty("prefix").GetInt32(), actual.Prefix.Value);
                Assert.Equal(image.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0);
            }
        }
        // Fixed paid/denied explanation must survive raw stack restoration without maximum replay.
        Replay(f, row.GetProperty("syncFrames"));
        Assert.Equal(hitLife + 20, member.NpcHealth!.Value.Life);
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
    }

    private static void CallbackBoundary(JsonElement row, short budget, int callbackMode)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        Replay(f, row.GetProperty("syncFrames"));
        int callbackCount = 0; int oldHealthCount = f.Events.HealthConnections.Count(x => x == c);
        f.Events.OnEquipment = (connection, report) =>
        {
            if (connection != c || callbackCount++ != 0) return;
            Assert.Equal(120, member.BaseLifeMax); Assert.Equal(120, member.MaxLife);
            Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
            Assert.True(f.Players.TryGetInventoryItem(c, 0, out var adopted)); Assert.Equal((short)2, adopted.Stack);
            if (callbackMode == 0) throw new CallbackFailure();
            if (callbackMode == 1) Replay(f, row.GetProperty("syncFrames"));
            if (callbackMode == 2) f.Apply(new PlayerDisconnectRuntimeCommand(c));
        };
        try { f.Tick(); Assert.True(callbackMode != 0); }
        catch (CallbackFailure) { Assert.Equal(0, callbackMode); }
        f.Events.OnEquipment = null;
        Assert.Equal(1, callbackCount);
        if (callbackMode == 2)
        {
            Assert.False(f.Players.TryGet(c.Player.Slot.Value, out _));
            Assert.Equal(oldHealthCount, f.Events.HealthConnections.Count(x => x == c));
            f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 70, 120)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 2, 0, 29, 0)));
            Assert.False(f.Players.TryGet(c.Player.Slot.Value, out _));
            return;
        }
        int sourceBefore = before.GetProperty("statLifeMax").GetInt32();
        int sourceAfter = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32();
        bool consumed = sourceAfter != sourceBefore;
        int expected = budget > 0 && consumed ? sourceAfter : sourceBefore;
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (budget > 0 && consumed)
        {
            for (short index = 0; index < 59; index++)
            {
                Assert.True(f.Players.TryGetInventoryItem(c, index, out var actual));
                var image = row.GetProperty("afterSync").GetProperty("inventory")[index];
                Assert.Equal(image.GetProperty("type").GetInt32(), actual.ItemType.Value);
                Assert.Equal(image.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(image.GetProperty("prefix").GetInt32(), actual.Prefix.Value);
                Assert.Equal(image.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0);
            }
        }
        // Fixed paid/denied explanation must survive raw stack restoration without maximum replay.
        Replay(f, row.GetProperty("syncFrames"));
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
    }

    private static void AnimationBoundary(JsonElement row, short budget, JsonElement freshSource)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        Replay(f, row.GetProperty("syncFrames"));
        f.Tick();
        int sourceBefore = before.GetProperty("statLifeMax").GetInt32();
        int sourceAfter = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32();
        bool consumed = sourceAfter != sourceBefore;
        int expected = budget > 0 && consumed ? sourceAfter : sourceBefore;
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (budget > 0 && consumed)
        {
            for (short index = 0; index < 59; index++)
            {
                Assert.True(f.Players.TryGetInventoryItem(c, index, out var actual));
                var image = row.GetProperty("afterSync").GetProperty("inventory")[index];
                Assert.Equal(image.GetProperty("type").GetInt32(), actual.ItemType.Value);
                Assert.Equal(image.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(image.GetProperty("prefix").GetInt32(), actual.Prefix.Value);
                Assert.Equal(image.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0);
            }
        }
        // Fixed paid/denied explanation must survive raw stack restoration without maximum replay.
        Replay(f, row.GetProperty("syncFrames"));
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        f.Move(2, false);
        for (int tick = 0; tick < 40; tick++) f.Tick();
        Assert.Equal(0, member.ItemPhase!.Value.Selected.Animation);
        Assert.Equal(0, member.ItemPhase.Value.Selected.ItemTime);
        // A presentation animation is not a fresh captured local use intent.
        f.Apply(new PlayerItemAnimationRuntimeCommand(c, 0, 29));
        f.Tick();
        Assert.Equal(expected, member.BaseLifeMax); Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)2, store.CaptureFundedQuantities(c)[0]);
        Assert.True(f.Players.TryGetInventoryItem(c, 0, out var after)); Assert.Equal((short)2, after.Stack);
        for (int tick = 0; tick < 40; tick++) f.Tick();
        Assert.Equal(0, member.ItemPhase!.Value.Selected.Animation);
        Assert.Equal(0, member.ItemPhase.Value.Selected.ItemTime);
        // Real captured initial13, followed by configured next source-valid use
        // images at the now-owned120 maximum; not a captured whole Main sequence.
        Replay(f, freshSource.GetProperty("checkFrames"));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 1, 0, checked((short)row.GetProperty("itemId").GetInt32()), 0)));
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)(member.NpcHealth!.Value.Life + sourceAfter - sourceBefore)), checked((short)(expected + sourceAfter - sourceBefore)))));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        Assert.Equal(expected + sourceAfter - sourceBefore, member.BaseLifeMax); Assert.Equal(expected + sourceAfter - sourceBefore, member.MaxLife);
        Assert.Equal((short)1, store.CaptureFundedQuantities(c)[0]);
        Assert.True(f.Players.TryGetInventoryItem(c, 0, out var secondAfter)); Assert.Equal((short)1, secondAfter.Stack);
    }

    private static void ProviderBoundary(JsonElement row, short budget, bool throughState)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        Replay(f, row.GetProperty("syncFrames"));
        var phaseBefore = member.ItemPhase; var randomBefore = f.Random.Clone(); bool invoked = false;
        f.Players.SetNpcHealthWorldFacts(() => { invoked = true; member.OwnsLifeMaximum = false; return new(false, false); });
        if (throughState) f.Tick(); else Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.True(invoked);
        Assert.Equal(before.GetProperty("statLifeMax").GetInt32(), member.BaseLifeMax);
        Assert.Equal(before.GetProperty("statLifeMax").GetInt32(), member.MaxLife);
        Assert.Equal((short)1, store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (!throughState)
        {
            Assert.Equal(phaseBefore, member.ItemPhase);
            Assert.Equal(RandomCursor(randomBefore), RandomCursor(f.Random));
            Assert.Equal(RandomState(randomBefore), RandomState(f.Random));
            Assert.Equal(randomBefore.Clone().Next(), f.Random.Clone().Next());
        }
        return;
    }

    private static void RetiredPendingBoundary(JsonElement row, short budget, bool selectionChange, JsonElement fresh)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Assert.True(member.ItemPhase?.SelectedPotionUse is { Completed: false });
        // Capture is pending; no paid local effect has yet adopted.
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 2, 0, checked((short)row.GetProperty("itemId").GetInt32()), 0)));
        Assert.True(member.ItemPhase?.SelectedPotionUse is { Completed: false });
        if (selectionChange)
        {
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 1, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(member.ItemPhase?.SelectedPotionUse is { Completed: true, DeniedLifeUse: true });
            // Returning with unsuccessful held-use input is not a fresh local
            // certificate. Selection alone has already retired the old intent.
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 0, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        else f.Move(2, false);
        f.Apply(new PlayerItemAnimationRuntimeCommand(c, 0, 29));
        f.Tick();
        Assert.Equal(before.GetProperty("statLifeMax").GetInt32(), member.BaseLifeMax);
        Assert.Equal(before.GetProperty("statLifeMax").GetInt32(), member.MaxLife);
        Assert.Equal((short)1, store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        f.Move(2, false);
        for (int tick = 0; tick < 40; tick++) f.Tick();
        Assert.Equal(0, member.ItemPhase!.Value.Selected.Animation);
        Assert.Equal(0, member.ItemPhase.Value.Selected.ItemTime);
        Assert.True(f.Players.TryGetInventoryItem(c, 0, out var cancelledPhysical)); Assert.Equal((short)3, cancelledPhysical.Stack);
        Replay(f, fresh.GetProperty("checkFrames"));
        int increase = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32() - before.GetProperty("statLifeMax").GetInt32();
        int nextMaximum = before.GetProperty("statLifeMax").GetInt32() + increase;
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 2, 0, checked((short)row.GetProperty("itemId").GetInt32()), 0)));
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)(member.NpcHealth!.Value.Life + increase)), checked((short)nextMaximum))));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        f.Tick();
        Assert.Equal(nextMaximum, member.BaseLifeMax); Assert.Equal(nextMaximum, member.MaxLife);
        Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
        Assert.True(f.Players.TryGetInventoryItem(c, 0, out var finalItem)); Assert.Equal((short)2, finalItem.Stack);
    }

    private static void ConfiguredVitalsFirst(JsonElement row, short budget)
    {
        using var f = new Fixture("inactive-first", runtimeTick: true);
        var c = f.Connections[2]; var member = f.Member(2); var before = row.GetProperty("before");
        f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, checked((short)before.GetProperty("statLife").GetInt32()), checked((short)before.GetProperty("statLifeMax").GetInt32()))));
        short slot = 0;
        foreach (var item in before.GetProperty("inventory").EnumerateArray())
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, slot++, checked((short)item.GetProperty("stack").GetInt32()), checked((byte)item.GetProperty("prefix").GetInt32()), checked((short)item.GetProperty("type").GetInt32()), item.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        while (!f.Players.CanApplyShopSource(c.Source)) f.Players.TickShopJournalWork();
        var store = Inventory(f.Players); var q = new short[59]; q[0] = budget; store.AdoptFundedQuantities(c, q);
        var copper = store.CaptureFundedCoinValues(c); var caps = store.CaptureOwnedQuantities(c);
        var bankBefore = new RuntimePlayerInventoryItem[160]; var bankCopperBefore = new long[160];
        Assert.True(store.TryCaptureShopBanks(c, bankBefore, bankCopperBefore));
        Assert.True(store.TryCaptureShopBankOwnership(c, out var bankQuantityBefore, out var bankCapsBefore));
        var buffs = f.Buffs(2).Clone();
        Assert.NotNull(member.ItemPhase);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
        // Ordinary source mirror already contains held control/success, so its prior ingress is configured.
        // success-false supplies the genuine captured initial13 before source35.
        if (row.GetProperty("mode").GetString() != "success-false")
            f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, 32, (byte)(member.MovementFlags | 16), 0, 64, 0, member.PositionX, member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Replay(f, row.GetProperty("checkFrames"));
        // Explicit configured packet16-before5 transport composition, not
        // the captured original ordered synchronization batch.
        var ordered = row.GetProperty("syncFrames").EnumerateArray().Select(x => x.GetString()!).OrderBy(x => Convert.FromHexString(x)[2] == 16 ? 0 : Convert.FromHexString(x)[2] == 5 ? 1 : 2).ToArray();
        Replay(f, ordered);
        f.Tick();
        int sourceBefore = before.GetProperty("statLifeMax").GetInt32();
        int sourceAfter = row.GetProperty("afterSync").GetProperty("statLifeMax").GetInt32();
        bool consumed = sourceAfter != sourceBefore;
        int expected = budget > 0 && consumed ? sourceAfter : sourceBefore;
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal(expected, member.MaxLife);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
        Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        if (budget > 0 && consumed)
        {
            for (short index = 0; index < 59; index++)
            {
                Assert.True(f.Players.TryGetInventoryItem(c, index, out var actual));
                var image = row.GetProperty("afterSync").GetProperty("inventory")[index];
                Assert.Equal(image.GetProperty("type").GetInt32(), actual.ItemType.Value);
                Assert.Equal(image.GetProperty("stack").GetInt32(), actual.Stack);
                Assert.Equal(image.GetProperty("prefix").GetInt32(), actual.Prefix.Value);
                Assert.Equal(image.GetProperty("favorite").GetBoolean(), (actual.ItemFlags & 1) != 0);
            }
        }
        // Fixed paid/denied explanation must survive raw stack restoration without maximum replay.
        Replay(f, row.GetProperty("syncFrames"));
        Assert.Equal(expected, member.BaseLifeMax);
        Assert.Equal((short)(budget > 0 && consumed ? budget - 1 : budget), store.CaptureFundedQuantities(c)[0]);
        var expectedQuantity = new short[59]; expectedQuantity[0] = (short)(budget > 0 && consumed ? budget - 1 : budget);
        Assert.Equal(expectedQuantity, store.CaptureFundedQuantities(c));
        var bankAfter = new RuntimePlayerInventoryItem[160]; var bankCopperAfter = new long[160];
        Assert.True(store.TryCaptureShopBanks(c, bankAfter, bankCopperAfter));
        Assert.True(store.TryCaptureShopBankOwnership(c, out var bankQuantityAfter, out var bankCapsAfter));
        Assert.Equal(bankBefore, bankAfter); Assert.Equal(bankCopperBefore, bankCopperAfter);
        Assert.Equal(bankQuantityBefore, bankQuantityAfter); Assert.Equal(bankCapsBefore, bankCapsAfter);
        if (consumed)
        {
            // Edit changes the completed explanation's item; permanent family ownership
            // must remain independent of the latest receipt and transfer lifecycle.
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 2, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 50, checked((short)(expected + 20)))));
            Assert.Equal(expected, member.BaseLifeMax); Assert.Equal(expected, member.MaxLife);
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.Apply(new PlayerTransferDetachRuntimeCommand(c, detached));
            Assert.True(detached.Task.IsCompletedSuccessfully); Assert.NotNull(detached.Task.Result);
            var transfer = detached.Task.Result!;
            var attached = new TaskCompletionSource<bool>();
            f.Apply(new PlayerTransferAttachRuntimeCommand(c, transfer, 100, 103, true, false, attached));
            Assert.True(attached.Task.IsCompletedSuccessfully); Assert.True(attached.Task.Result);
            var returned = f.Member(2);
            Assert.Null(returned.ItemPhase?.SelectedPotionUse);
            f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, 50, checked((short)(expected + 20)))));
            Assert.Equal(expected, returned.BaseLifeMax); Assert.Equal(expected, returned.MaxLife);
            Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
            Assert.Equal(copper, store.CaptureFundedCoinValues(c)); Assert.Equal(caps, store.CaptureOwnedQuantities(c));
            Assert.True(buffs.HasSameSlots(f.Buffs(2)));
        }
    }

    private static void Replay(Fixture f, JsonElement frames)
        => Replay(f, frames.EnumerateArray().Select(hex => hex.GetString()!));

    private static void Replay(Fixture f, IEnumerable<string> frames)
    {
        var c = f.Connections[2]; var member = f.Member(2);
        foreach (var hex in frames)
        {
            byte[] bytes = Convert.FromHexString(hex);
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes), new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (bytes[2])
            {
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var item));
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, item.SlotId, item.Stack, item.Prefix, item.ItemNetId, item.ItemFlags))); break;
                case 16:
                    Assert.Equal(TerrariaPlayerHealthDecodeResult.Decoded, TerrariaPlayerVitalsCodec.TryDecodeHealth(in frame, out var health));
                    f.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, health.Life, health.MaxLife))); break;
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in frame, out var move));
                    f.Apply(new PlayerMovementRuntimeCommand(c, new(c.Player.Slot, move.ControlFlags, move.MovementFlags, move.MiscFlags1, move.MiscFlags2, move.SelectedItem, member.PositionX, member.PositionY, move.HasVelocity, move.VelocityX, move.VelocityY, move.HasMount, move.MountType, false, 0, 0, 0, 0, false, 0, 0))); break;
                case 35:
                    Assert.Equal(6, bytes.Length);
                    f.Apply(new PlayerVitalsPresentationRuntimeCommand(c, 35, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(4)))); break;
                case 138: f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c)); break;
                default: throw new InvalidOperationException("Unexpected life source packet " + bytes[2]);
            }
        }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
    private static extern ref int[] RandomState(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
    private static extern ref uint RandomCursor(VanillaUnifiedRandom1458 random);
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
                short id = slot == 2 ? (short)0 : slot == 0 && mode == "potion-first" ? (short)28 : slot == 0 && mode == "weapon98-first" ? (short)98 : (short)0;
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
    private sealed class CallbackFailure : Exception;
    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal int Count;
        internal readonly List<byte> Seen = [];
        internal readonly List<ConnectionHandle> HealthConnections = [];
        internal readonly List<ConnectionHandle> BuffConnections = [];
        internal Action<ConnectionHandle, PlayerEquipmentCommitRequest>? OnEquipment = null;
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) => Count++;
        public void PlayerEquipmentUpdated(ConnectionHandle c, in PlayerEquipmentCommitRequest r) { Count++; Seen.Add(5); OnEquipment?.Invoke(c, r); }
        public void PlayerHealthUpdated(ConnectionHandle c, in PlayerHealthCommitRequest r) { Count++; Seen.Add(16); HealthConnections.Add(c); }
        public void PlayerManaUpdated(ConnectionHandle c, in PlayerManaCommitRequest r) { Count++; Seen.Add(42); }
        public void PlayerItemAnimationUpdated(ConnectionHandle c, float rotation, short animation) => Count++;
        public void PlayerBuffTypesUpdated(ConnectionHandle c, in PlayerBuffTypesCommitRequest r) { Count++; Seen.Add(50); BuffConnections.Add(c); }
        public void PlayerSpawned(ConnectionHandle c, in PlayerSpawnCommitRequest r) => Count++;
        public void PlayerMoved(ConnectionHandle c, in PlayerMovementCommitRequest r) => Count++;
        public void PlayerDisconnected(ConnectionHandle c) => Count++;
    }
}
