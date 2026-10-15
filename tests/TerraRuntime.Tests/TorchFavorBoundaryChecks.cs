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
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public static class TorchFavorBoundaryChecks
{
    public static void RunTorch()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        foreach (string repeat in new[] { "first", "repeat" })
        {
            using var stream = typeof(TorchFavorBoundaryChecks).Assembly.GetManifestResourceStream("TorchFavorSource" + platform + (repeat == "first" ? "First" : "Repeat") + "1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var input = JsonDocument.Parse(gzip);
            foreach (var row in input.RootElement.EnumerateArray())
            {
                string mode = row.GetProperty("mode").GetString()!;
                if (!row.GetProperty("local").GetBoolean() || mode is not ("ordinary" or "last-stack" or "full-vitals" or "success-false")) continue;
                int[]? deniedRandom = null;
                uint deniedCursor = 0;
                foreach (short budget in new short[] { 0, 1 })
                {
                    using var f = new Fixture("inactive-first", true);
                    var c = f.Connections[2]; var member = f.Member(2); var store = Inventory(f.Players);
                    short initial = (short)row.GetProperty("before").GetProperty("inventory")[0].GetProperty("stack").GetInt32();
                    var appearance = new PlayerAppearanceCommitRequest(c.Player.Slot, 0, 0, 0, 0, "configured", 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0);
                    f.Apply(new PlayerAppearanceRuntimeCommand(c, appearance));
                    member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
                    f.Move(2, true);
                    f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, initial, 0, 5043, 0)));
                    f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
                    for (int i = 0; i < 10000 && !f.Players.CanApplyShopSource(c.Source); i++) f.Players.TickShopJournalWork();
                    var quantities = new short[59]; quantities[0] = budget; store.AdoptFundedQuantities(c, quantities);
                    var beforeCopper = store.CaptureFundedCoinValues(c); var beforeCaps = store.CaptureOwnedQuantities(c);
                    var beforeBanks = new RuntimePlayerInventoryItem[160]; var beforeBankCopper = new long[160];
                    Assert.True(store.TryCaptureShopBanks(c, beforeBanks, beforeBankCopper));
                    Assert.True(store.TryCaptureShopBankOwnership(c, out var beforeBankQ, out var beforeBankCaps));
                    int equipmentCalls = 0;
                    f.Events.OnEquipment = (_, _) =>
                    {
                        equipmentCalls++;
                        Assert.True(Profiles(f.Players).TryCapture(c, out var joint, out _, out _));
                        Assert.Equal((byte)5, (byte)(joint!.Value.TorchAndCartFlags & 5));
                        Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
                        Assert.Equal(1, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
                    };
                    var reportsRandom = (int[])RandomState(f.Random).Clone(); uint reportsCursor = RandomCursor(f.Random);
                    Replay(f, row.GetProperty("checkFrames"));
                    Replay(f, row.GetProperty("syncFrames"));
                    Assert.Equal(reportsCursor, RandomCursor(f.Random)); Assert.Equal(reportsRandom, RandomState(f.Random));
                    Assert.Equal(budget, store.CaptureFundedQuantities(c)[0]);
                    Assert.True(member.OwnsTorchFavor);
                    Assert.True(Profiles(f.Players).TryCapture(c, out var heldAppearance, out _, out _));
                    Assert.Equal((byte)0, (byte)(heldAppearance!.Value.TorchAndCartFlags & 4));
                    // Configure the UpdatePlayers stream at the captured ItemCheck
                    // boundary; this is not an outer Main.Update RNG claim.
                    RandomCursor(f.Random) = row.GetProperty("beforeRandom").GetProperty("cursor").GetUInt32();
                    var sourceRandomBefore = row.GetProperty("beforeRandom").GetProperty("state").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                    sourceRandomBefore.CopyTo(RandomState(f.Random), 0);
                    f.Tick();
                    Assert.True(member.ItemPhase.HasValue, "Torch paid or denied use must retain the admitted phase.");
                    // The original corpus is direct ItemCheck, while State.Tick
                    // also runs other admitted owners. Compare the full owned
                    // stream between paid/denied siblings; the independent Game
                    // kernel proof compares the source ItemCheck full56 exactly.
                    if (budget == 0) { deniedRandom = (int[])RandomState(f.Random).Clone(); deniedCursor = RandomCursor(f.Random); }
                    else { Assert.Equal(deniedCursor, RandomCursor(f.Random)); Assert.Equal(deniedRandom!, RandomState(f.Random)); }
                    Assert.True(Profiles(f.Players).TryCapture(c, out var actual, out _, out _));
                    Assert.Equal(budget > 0 ? (byte)5 : (byte)0, (byte)(actual!.Value.TorchAndCartFlags & 5));
                    Assert.Equal(budget > 0 ? 1 : 0, equipmentCalls);
                    Assert.Equal(budget > 0 ? 1 : 0, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
                    Assert.Equal(new short[59], store.CaptureFundedQuantities(c));
                    for (short slot = 0; slot < 59; slot++)
                    {
                        Assert.True(store.TryGet(c, slot, out var actualItem));
                        short remaining = (short)(initial - (budget > 0 ? 1 : 0));
                        var expected = slot == 0 && remaining != 0 ? new RuntimePlayerInventoryItem(new(5043), remaining, default, 0) : default;
                        Assert.Equal(expected, actualItem);
                    }
                    Assert.Equal(beforeCopper, store.CaptureFundedCoinValues(c)); Assert.Equal(beforeCaps, store.CaptureOwnedQuantities(c));
                    var afterBanks = new RuntimePlayerInventoryItem[160]; var afterBankCopper = new long[160];
                    Assert.True(store.TryCaptureShopBanks(c, afterBanks, afterBankCopper));
                    Assert.True(store.TryCaptureShopBankOwnership(c, out var afterBankQ, out var afterBankCaps));
                    Assert.Equal(beforeBanks, afterBanks); Assert.Equal(beforeBankCopper, afterBankCopper);
                    Assert.Equal(beforeBankQ, afterBankQ); Assert.Equal(beforeBankCaps, afterBankCaps);
                    if (budget > 0)
                    {
                        var expected = row.GetProperty("phases")[0].GetProperty("state");
                        Assert.Equal(expected.GetProperty("itemAnimation").GetInt32(), member.ItemPhase!.Value.Selected.Animation);
                        Assert.Equal(expected.GetProperty("itemTime").GetInt32(), member.ItemPhase.Value.Selected.ItemTime);
                    }
                    f.Events.OnEquipment = null;
                    // Exact stale source4/5/51 cannot register a second paid scripted kill.
                    Replay(f, row.GetProperty("checkFrames")); Replay(f, row.GetProperty("syncFrames"));
                    Assert.Equal(budget > 0 ? 1 : 0, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
                    Console.WriteLine("TORCH " + platform + " " + repeat + " " + mode + " q=" + budget);
                }
            }
        }
    }

    private static void ArmTorch(Fixture f, byte slot, short budget)
    {
        var c = f.Connections[slot]; var member = f.Member(slot);
        f.Apply(new PlayerAppearanceRuntimeCommand(c, new(c.Player.Slot, 0, 0, 0, 0, "configured", 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0)));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 5043, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        for (int step = 0; step < 10000 && !f.Players.CanApplyShopSource(c.Source); step++) f.Players.TickShopJournalWork();
        var q = new short[59]; q[0] = budget; Inventory(f.Players).AdoptFundedQuantities(c, q);
        member.ItemPhase = member.ItemPhase!.Value with { Selected = member.ItemPhase.Value.Selected with
            { Animation = 0, ItemTime = 0, ReleaseUseItem = true } };
        member.ItemAnimation = 0;
        f.Move(slot, true);
        Assert.True(Profiles(f.Players).TryCapture(c, out var before, out _, out _));
        f.Apply(new PlayerAppearanceRuntimeCommand(c, before!.Value with { TorchAndCartFlags = 5 }));
        f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 2, 0, 5043, 0)));
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
        Assert.True(member.OwnsTorchFavor);
    }

    public static void RunBoundaries()
    {
        for (int mode = 0; mode < 7; mode++)
        {
            using var f = new Fixture("inactive-first", true);
            ArmTorch(f, 2, 1);
            var c = f.Connections[2]; var member = f.Member(2); var store = Inventory(f.Players);
            int callbacks = 0; bool provider = false;
            var beforeRandom = (int[])RandomState(f.Random).Clone(); uint beforeCursor = RandomCursor(f.Random);
            if (mode <= 2) f.Events.OnEquipment = (connection, report) =>
            {
                if (connection != c || report.SlotId != 0 || report.Stack != 2) return;
                callbacks++;
                Assert.True(member.PaidTorchFavor);
                Assert.Equal((short)0, store.CaptureFundedQuantities(c)[0]);
                Assert.Equal(1, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
                if (mode == 0) throw new CallbackFailure();
                if (mode == 1) f.Apply(new PlayerEquipmentRuntimeCommand(c, report));
                if (mode == 2) f.Apply(new PlayerDisconnectRuntimeCommand(c));
            };
            if (mode == 3) f.Move(2, false);
            if (mode == 4) f.Players.SetNpcHealthGrapplingFacts(player =>
            {
                if (!provider) { provider = true; member.OwnsTorchFavor = false; }
                return false;
            });
            if (mode == 5) f.Players.SetNpcHealthGrapplingFacts(player =>
            {
                if (!provider) { provider = true; f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 1, 1, 0, 9, 0))); }
                return false;
            });
            if (mode == 6) f.Players.SetNpcHealthGrapplingFacts(player =>
            {
                if (!provider)
                {
                    provider = true;
                    NpcSnapshot[] victims = [new(default, default, 664, 664, 0, 0, 0, 0, 0, default, default)];
                    Assert.True(f.Prelude.TryPrepareScriptedKills(victims, out var preview));
                    Assert.True(f.Prelude.TryAdoptUnpublished(preview!, f.Prelude.Revision, out _));
                }
                return false;
            });
            if (mode == 2) f.Events.AppearanceConnections.Clear();
            bool threw = false;
            try { f.Tick(); } catch (CallbackFailure) { threw = true; }
            f.Events.OnEquipment = null;
            Assert.Equal(mode == 0, threw);
            if (mode <= 2) Assert.Equal(1, callbacks);
            if (mode == 2)
            {
                Assert.False(f.Players.TryGet(c.Player.Slot.Value, out _));
                Assert.DoesNotContain(c, f.Events.AppearanceConnections);
                continue;
            }
            bool accepted = mode <= 1;
            Assert.True(Profiles(f.Players).TryCapture(c, out var appearance, out _, out _));
            Assert.Equal(accepted ? (byte)5 : (byte)0, (byte)(appearance!.Value.TorchAndCartFlags & 5));
            Assert.Equal(accepted ? (short)0 : (short)1, store.CaptureFundedQuantities(c)[0]);
            Assert.Equal(accepted || mode == 6 ? 1 : 0, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
            if (mode >= 4)
            {
                Assert.True(provider); Assert.Equal(beforeCursor, RandomCursor(f.Random)); Assert.Equal(beforeRandom, RandomState(f.Random));
            }
        }
        foreach (short budget in new short[] { 0, 1 })
        {
            using var f = new Fixture("inactive-first", true);
            ArmTorch(f, 2, budget); f.Tick();
            var c = f.Connections[2]; var member = f.Member(2); var store = Inventory(f.Players);
            Assert.True(member.OwnsTorchFavor); Assert.Equal(budget > 0, member.PaidTorchFavor);
            f.Move(2, false);
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 0, 3, 0, 2, 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            for (int step = 0; step < 10000 && !f.Players.CanApplyShopSource(c.Source); step++) f.Players.TickShopJournalWork();
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.Apply(new PlayerTransferDetachRuntimeCommand(c, detached));
            Assert.True(detached.Task.IsCompletedSuccessfully); Assert.NotNull(detached.Task.Result);
            Assert.True(detached.Task.Result!.OwnsTorchFavor); Assert.Equal(budget > 0, detached.Task.Result.PaidTorchFavor);
            var attached = new TaskCompletionSource<bool>();
            f.Apply(new PlayerTransferAttachRuntimeCommand(c, detached.Task.Result, 100, 103, true, false, attached));
            Assert.True(attached.Task.IsCompletedSuccessfully); Assert.True(attached.Task.Result);
            var returned = f.Member(2); Assert.True(returned.OwnsTorchFavor); Assert.Equal(budget > 0, returned.PaidTorchFavor);
            Assert.True(Profiles(f.Players).TryCapture(c, out var current, out _, out _));
            f.Apply(new PlayerAppearanceRuntimeCommand(c, current!.Value with { TorchAndCartFlags = budget > 0 ? (byte)0 : (byte)5 }));
            Assert.True(Profiles(f.Players).TryCapture(c, out var protectedAppearance, out _, out _));
            Assert.Equal(budget > 0 ? (byte)5 : (byte)0, (byte)(protectedAppearance!.Value.TorchAndCartFlags & 5));
        }
        using (var f = new Fixture("reverse-join", true))
        {
            ArmTorch(f, 0, 1); ArmTorch(f, 2, 1);
            var peer0 = f.AddPreludePeer(0); var peer2 = f.AddPreludePeer(2);
            int callbacks = 0;
            f.Events.OnEquipment = (_, _) =>
            {
                callbacks++;
                Assert.Equal(2, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
                foreach (byte slot in new byte[] { 0, 2 })
                {
                    Assert.True(f.Member(slot).PaidTorchFavor);
                    Assert.Equal((short)0, Inventory(f.Players).CaptureFundedQuantities(f.Connections[slot])[0]);
                }
            };
            f.Tick();
            Assert.Equal(2, callbacks);
            byte[][] expectedFrames = [TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(664, 1),
                TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(664, 2)];
            Assert.Equal(expectedFrames, DrainPrelude(peer0)); Assert.Equal(expectedFrames, DrainPrelude(peer2));
            Assert.Equal(2, f.Prelude.CaptureBestiary().Kills.Where(k => k.PersistentId == "TorchGod").Sum(k => k.KillCount));
        }
    }

    private readonly record struct CraftImage(RuntimePlayerInventoryItem[] Physical, short[] Quantity,
        long[] Copper, OwnedInventoryQuantity1458[] Caps, short[] BankCaps);

    private static CraftImage CaptureCraftImage(Fixture f, ConnectionHandle connection)
    {
        var store = Inventory(f.Players);
        Assert.True(store.TryCaptureShopJournal(connection, out var capture));
        Assert.NotNull(capture);
        var banks = new RuntimePlayerInventoryItem[160]; var copper = new long[160];
        Assert.True(store.TryCaptureShopBanks(connection, banks, copper));
        Assert.True(store.TryCaptureShopBankOwnership(connection, out var quantities, out var caps));
        return new(capture.Before.ToArray(), store.CaptureFundedQuantities(connection).Concat(quantities).ToArray(),
            store.CaptureFundedCoinValues(connection).Concat(copper).ToArray(),
            store.CaptureOwnedQuantities(connection), caps);
    }

    private static void EqualCraftImage(CraftImage expected, CraftImage actual)
    {
        Assert.Equal(expected.Physical, actual.Physical);
        Assert.Equal(expected.Quantity, actual.Quantity);
        Assert.Equal(expected.Copper, actual.Copper);
        Assert.Equal(expected.Caps, actual.Caps);
        Assert.Equal(expected.BankCaps, actual.BankCaps);
    }

    public static void RunCrafting()
    {
        // Configured caller overlays use the independently captured catalog and
        // source environment rules. These are not new original combined UI runs.
        foreach (int recipeIndex in new[] { 190, 3565 })
        foreach (string mode in new[] { "paid", "using-disabled", "raw-import", "late-custody" })
        {
            using var f = new Fixture("inactive-first", true);
            var c = f.Connections[2]; var member = f.Member(2); var store = Inventory(f.Players);
            if (mode != "raw-import")
            {
                ArmTorch(f, 2, 1); f.Tick();
                Assert.True(member.PaidTorchFavor);
                f.Move(2, false);
                for (int tick = 0; tick < 55; tick++) f.Tick();
                Assert.Equal(0, member.ItemAnimation);
                Assert.Equal(0, member.ItemPhase!.Value.Selected.ItemTime);
                Assert.True(Profiles(f.Players).TryCapture(c, out var paid, out _, out _));
                if (mode == "using-disabled")
                {
                    f.Apply(new PlayerAppearanceRuntimeCommand(c, paid!.Value with { TorchAndCartFlags = 4 }));
                    Assert.True(Profiles(f.Players).TryCapture(c, out var disabled, out _, out _));
                    Assert.Equal((byte)4, (byte)(disabled!.Value.TorchAndCartFlags & 5));
                }
            }
            else
            {
                f.Apply(new PlayerAppearanceRuntimeCommand(c, new(c.Player.Slot, 0, 0, 0, 0, "configured", 0, 0, 0,
                    default, default, default, default, default, default, default, 0, 5, 0)));
                Assert.False(member.PaidTorchFavor);
                Assert.True(Profiles(f.Players).TryCapture(c, out var imported, out _, out _));
                Assert.Equal((byte)5, (byte)(imported!.Value.TorchAndCartFlags & 5));
            }
            Assert.True(VanillaRecipeCatalog1458.TryGet(recipeIndex, out var recipe));
            Assert.True(recipe.Flags.HasFlag(VanillaRecipeFlags1458.NeedTorchGodsFavor));
            Assert.Equal(recipeIndex == 190 ? 5573 : 5638, recipe.ResultType.Value);
            Assert.Equal(recipeIndex == 190 ? 13 : 18, recipe.RequiredTile);
            int stationX = (int)(member.PositionX / 16) + 4;
            int stationY = (int)(member.PositionY / 16);
            f.Tiles.Set(stationX, stationY, new WorldTile { Type = (ushort)recipe.RequiredTile, Flags = WorldTileFlags.Active });
            int count = VanillaRecipeCatalog1458.GetIngredientCount(recipeIndex);
            var funded = new short[59];
            for (int part = 0; part < count; part++)
            {
                Assert.True(VanillaRecipeCatalog1458.TryGetIngredient(recipeIndex, part, out var ingredient));
                Assert.False(ingredient.IsGroup);
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, (short)(part + 1),
                    checked((short)ingredient.Amount), 0, checked((short)ingredient.OriginalType.Value), 0)));
                funded[part + 1] = checked((short)ingredient.Amount);
            }
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            for (int tick = 0; tick < 10000 && !f.Players.CanApplyShopSource(c.Source); tick++) f.Tick();
            Assert.True(f.Players.CanApplyShopSource(c.Source));
            store.AdoptFundedQuantities(c, funded);
            var original = CaptureCraftImage(f, c);
            long rejected = f.Players.RejectedEquipmentUpdates;
            for (int part = 0; part < count; part++)
                f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, (short)(part + 1), 0, 0, 0, 0)));
            f.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 58,
                checked((short)recipe.ResultStack), recipe.ResultPrefix, checked((short)recipe.ResultType.Value), 0)));
            f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(c));
            Assert.False(f.Players.CanApplyShopSource(c.Source));
            var beforeWork = CaptureCraftImage(f, c);
            if (mode == "late-custody")
            {
                for (int step = 0; step < 10000 && member.CursorMove?.Shop?.CraftResult?.Environment is null &&
                    !f.Players.CanApplyShopSource(c.Source); step++) f.Players.TickShopJournalWork(1);
                Assert.NotNull(member.CursorMove?.Shop?.CraftResult?.Environment);
                var environment = member.CursorMove!.Shop!.CraftResult!.Environment!;
                Assert.True(environment.Environment.TorchGod);
                Assert.True(environment.IsCurrent);
                member.PaidTorchFavor = false;
                Assert.False(environment.IsCurrent);
                beforeWork = CaptureCraftImage(f, c);
            }
            for (int tick = 0; tick < 10000 && !f.Players.CanApplyShopSource(c.Source); tick++)
            {
                f.Tick();
                Assert.True(f.Players.LastShopWorkSteps <= 64);
            }
            Assert.True(f.Players.CanApplyShopSource(c.Source));
            bool accepted = mode is "paid" or "using-disabled";
            Assert.Equal(rejected + (accepted ? 0 : 1), f.Players.RejectedEquipmentUpdates);
            var actual = CaptureCraftImage(f, c);
            if (accepted)
            {
                var physical = original.Physical.ToArray(); var quantities = original.Quantity.ToArray();
                for (int part = 0; part < count; part++) { physical[part + 1] = default; quantities[part + 1] = 0; }
                physical[58] = new(recipe.ResultType, checked((short)recipe.ResultStack), new(recipe.ResultPrefix), 0);
                quantities[58] = checked((short)recipe.ResultStack);
                EqualCraftImage(new(physical, quantities, original.Copper, original.Caps, original.BankCaps), actual);
            }
            else
            {
                // Retained rejection preserves the image that actually reached
                // the work boundary; this is not a manufactured vanilla refund.
                EqualCraftImage(beforeWork, actual);
                Assert.Equal((short)0, actual.Quantity[58]);
            }
            Console.WriteLine("TORCH-CRAFT recipe=" + recipeIndex + " mode=" + mode + " rejectedDelta=" + (f.Players.RejectedEquipmentUpdates - rejected));
        }
    }

    private static byte[][] DrainPrelude(TerrariaConnectionOutboundQueue outbound)
    {
        var result = new List<byte[]>();
        while (Outbound(outbound).TryRead(out var frame)) result.Add(frame.Bytes.ToArray());
        return result.ToArray();
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_queue")]
    private static extern ref BoundedOutboundQueue Outbound(TerrariaConnectionOutboundQueue owner);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_session")]
    private static extern ref PlayerJoinSession? BootstrapSession(PlayerBootstrapFrameSink owner);

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
                case 4:
                    Assert.Equal(TerrariaPlayerAppearanceDecodeResult.Decoded, TerrariaPlayerAppearanceCodec.TryDecode(in frame, out var a));
                    f.Apply(new PlayerAppearanceRuntimeCommand(c, new(c.Player.Slot, a.SkinVariant, a.VoiceVariant, a.VoicePitchOffset, a.Hair, a.Name, a.HairDye, a.HideVisibleAccessory, a.HideMisc, new(a.HairColor.R, a.HairColor.G, a.HairColor.B), new(a.SkinColor.R, a.SkinColor.G, a.SkinColor.B), new(a.EyeColor.R, a.EyeColor.G, a.EyeColor.B), new(a.ShirtColor.R, a.ShirtColor.G, a.ShirtColor.B), new(a.UnderShirtColor.R, a.UnderShirtColor.G, a.UnderShirtColor.B), new(a.PantsColor.R, a.PantsColor.G, a.PantsColor.B), new(a.ShoeColor.R, a.ShoeColor.G, a.ShoeColor.B), a.DifficultyFlags, a.TorchAndCartFlags, a.ConsumableUnlockFlags))); break;
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
                case 51:
                    Assert.Equal(5, bytes.Length); Assert.Equal((byte)5, bytes[4]);
                    // Production Bootstrap currently forwards this event without
                    // an economic command. Exercise that real path on the already
                    // playing fixture; only the paid phase may register the kill.
                    f.ForwardSourceEvent(in frame);
                    break;
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
        internal RuntimeNpcDeathPrelude1458 Prelude => Composition(state!).Npcs.DeathPrelude;
        internal void ForwardSourceEvent(in TerrariaFrame frame)
        {
            var member = Member(2); var inventory = Inventory(Players);
            var quantities = inventory.CaptureFundedQuantities(Connections[2]);
            var bestiary = Prelude.CaptureBestiary();
            var randomBefore = (int[])RandomState(Random).Clone(); uint cursorBefore = RandomCursor(Random);
            var outbound = new TerrariaConnectionOutboundQueue(new(8, 4096, 1024));
            using var bootstrap = new PlayerBootstrapFrameSink(pool, outbound,
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }));
            Assert.Equal(PlayerJoinState.Playing, sessions[2].State);
            BootstrapSession(bootstrap) = sessions[2];
            try { Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(in frame)); }
            finally { BootstrapSession(bootstrap) = null; }
            Assert.Equal(quantities, inventory.CaptureFundedQuantities(Connections[2]));
            Assert.Equal(bestiary.Kills, Prelude.CaptureBestiary().Kills);
            Assert.Equal(cursorBefore, RandomCursor(Random)); Assert.Equal(randomBefore, RandomState(Random));
        }
        internal readonly PlayerAuthority Players;
        internal readonly WorldTileStore Tiles = new(new WorldDimensions(400, 300));
        internal readonly VanillaUnifiedRandom1458 Random;
        private readonly ServerRuntimeState? state;
        internal readonly EventSink Events = new();
        internal readonly RuntimeNpcReplicationRegistry Replication = new();
        internal TerrariaConnectionOutboundQueue AddPreludePeer(byte slot)
        {
            var outbound = new TerrariaConnectionOutboundQueue(new(4096, 4_194_304, 65_536));
            var connection = Connections[slot];
            Assert.True(Replication.TryRegister(connection.Source, outbound));
            Replication.PlayerSpawned(connection, new(connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            DrainPrelude(outbound);
            return outbound;
        }
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
                state = new(playerEvents: Events, worldTiles: Tiles, projectiles: projectiles, npcReplication: Replication,
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
            Players.SetNpcHealthGrapplingFacts(player => false);
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
        internal readonly List<ConnectionHandle> AppearanceConnections = [];
        public void PlayerAppearanceUpdated(ConnectionHandle c, in PlayerAppearanceCommitRequest r) { Count++; AppearanceConnections.Add(c); }
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
