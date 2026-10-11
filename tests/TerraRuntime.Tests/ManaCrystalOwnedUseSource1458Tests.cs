using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Tests;
using Xunit;

// Original configured local ItemCheck and later explicit sync on Linux/CLR4 Windows.
// Runtime State.Tick is owned component composition; the source has no accepted next Player.Update trace.
public sealed class ManaCrystalOwnedUseSource1458Tests
{
    [Fact]
    public void Original_sync_chronology_is_an_acknowledgement_of_one_owned_crystal_use()
    {
        using var phaseStream = typeof(ManaCrystalOwnedUseSource1458Tests).Assembly.GetManifestResourceStream("HumanChannelledMagicPhase1458")!;
        using var gzip = new GZipStream(phaseStream, CompressionMode.Decompress);
        using var phases = JsonDocument.Parse(gzip);
        using var originals = ReadOriginal();
        int cases = 0;
        foreach (string platform in new[] { "linux", "windows" })
            foreach (var original in originals.RootElement.GetProperty(platform).EnumerateArray())
                foreach (int order in new[] { 0, 1, 2 })
                {
                    using var fixture = new HumanChannelledMagicAuthority1458Tests.Harness.Fixture(phases.RootElement[0], false);
                    var before = original.GetProperty("before").GetProperty("vitals");
                    var expected = original.GetProperty("afterOuter").GetProperty("vitals");
                    int stack = original.GetProperty("before").GetProperty("item").GetProperty("stack").GetInt32();
                    fixture.Equipment(0, 109, (short)stack);
                    fixture.State.Apply(new PlayerManaRuntimeCommand(fixture.Connection,
                        new(fixture.Connection.Player.Slot, (short)before.GetProperty("statMana").GetInt32(),
                            (short)before.GetProperty("statManaMax").GetInt32())));
                    fixture.Member.ItemPhase = fixture.Member.ItemPhase!.Value with
                    {
                        ManaHeat = 1f,
                        Mana = fixture.Member.ItemPhase.Value.Mana with { Count = 0, Delay = 100f },
                        Selected = fixture.Member.ItemPhase.Value.Selected with
                        { ReleaseUseItem = true, Animation = 0, AnimationMax = 0, ItemTime = 0, ItemTimeMax = 0 }
                    };
                    ApplyFrames(fixture, original.GetProperty("outerFrames"));
                    if (order == 0) ApplyFrames(fixture, original.GetProperty("syncFrames"));
                    var ownedRandomBefore = fixture.Random.Clone();
                    fixture.State.Tick();
                    Assert.False(fixture.Random.HasSameState(ownedRandomBefore));
                    if (order != 0) ApplyFrames(fixture, original.GetProperty("syncFrames"));
                    if (order == 2) ApplyFrames(fixture, original.GetProperty("syncFrames"));
                    Assert.Equal(expected.GetProperty("statManaMax").GetInt32(), fixture.Member.ItemPhase!.Value.BaseManaMaximum);
                    Assert.Equal(expected.GetProperty("statManaMax2").GetInt32(), fixture.Member.ItemPhase.Value.Mana.Maximum);
                    // Existing Player.Update regeneration/clamp precedes the configured ItemCheck effect.
                    // The imported over-maximum original component row is not a whole runtime phase comparator.
                    if (before.GetProperty("statMana").GetInt32() <= before.GetProperty("statManaMax").GetInt32())
                        Assert.Equal(expected.GetProperty("statMana").GetInt32(), fixture.Member.Mana);
                    Assert.Equal(expected.GetProperty("itemTime").GetInt32(), fixture.Member.ItemPhase.Value.Selected.ItemTime);
                    Assert.Equal(expected.GetProperty("itemAnimation").GetInt32(), fixture.Member.ItemAnimation);
                    Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var after));
                    Assert.Equal(stack - (before.GetProperty("statManaMax").GetInt32() < 200 ? 1 : 0), after.Stack);
                    if (after.Stack == 0) Assert.True(after.ItemType.IsNone);
                    // The cosmetic source bit remains valid on Air and the next represented item.
                    fixture.State.Tick();
                    Assert.NotNull(fixture.Member.ItemPhase);
                    fixture.Equipment(0, 113, 1);
                    fixture.State.Apply(new PlayerMovementRuntimeCommand(fixture.Connection,
                        new(fixture.Connection.Player.Slot, 64, 16, 0, 128, 0,
                            fixture.Member.PositionX, fixture.Member.PositionY, false, 0, 0, false, 0,
                            false, 0, 0, 0, 0, false, 0, 0)));
                    fixture.State.Tick();
                    Assert.NotNull(fixture.Member.ItemPhase);
                    Assert.Equal(expected.GetProperty("statManaMax").GetInt32(), fixture.Member.ItemPhase!.Value.BaseManaMaximum);
                    if (before.GetProperty("statManaMax").GetInt32() < 200)
                    {
                        // A retired phase and arbitrary client image cannot reopen a consumed quantity.
                        fixture.Member.ItemPhase = null;
                        fixture.Equipment(0, 3, 1);
                        fixture.Equipment(0, 0, 0);
                        fixture.Equipment(0, 109, (short)stack);
                        Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var refusedRestore));
                        Assert.True(refusedRestore.IsEmpty);
                        fixture.Equipment(0, 109, (short)(stack - 1));
                        Assert.True(fixture.Players.TryGetInventoryItem(fixture.Connection, 0, out var allowedRemaining));
                        Assert.Equal(stack - 1, allowedRemaining.Stack);
                    }
                    cases++;
                }
        Assert.Equal(42, cases);
    }
    [Fact]
    public void Whole_census_adoption_precedes_observers_and_late_provider_edits_refuse()
    {
        OwnedInventoryQuantityAuthority1458Checks.Run();
        RejectMalformedConsumptionJournals();
        using var phaseStream = typeof(ManaCrystalOwnedUseSource1458Tests).Assembly.GetManifestResourceStream("HumanChannelledMagicPhase1458")!;
        using var gzip = new GZipStream(phaseStream, CompressionMode.Decompress);
        using var phases = JsonDocument.Parse(gzip);
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var sink = new CrystalSink();
            using var f = new HumanChannelledMagicAuthority1458Tests.Harness.Fixture(phases.RootElement[0], false, sink);
            f.Equipment(0, 109, 3);
            f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 50, 180)));
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with
            {
                ManaHeat = 1f,
                Mana = f.Member.ItemPhase.Value.Mana with { Count = 0, Delay = 100f },
                Selected = f.Member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 }
            };
            f.Move(true, 0, true);
            int manaCalls = 0, itemCalls = 0;
            sink.Mana = () => { Assert.Equal(200, f.Member.MaxMana); Assert.Equal(70, f.Member.Mana); manaCalls++; };
            sink.Item = () =>
            {
                itemCalls++;
                Assert.Equal(200, f.Member.MaxMana); Assert.Equal(70, f.Member.Mana);
                Assert.Equal(200, f.Member.ItemPhase!.Value.BaseManaMaximum);
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var item)); Assert.Equal(2, item.Stack);
                if (mode == 1) throw new InvalidOperationException("accepted-observer");
                if (mode == 2) { sink.Item = null; f.Equipment(0, 3, 1); }
            };
            if (mode == 1) Assert.Throws<InvalidOperationException>(() => f.State.Tick()); else f.State.Tick();
            Assert.Equal(1, itemCalls); Assert.Equal(1, manaCalls);
            if (mode == 2) { Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var item)); Assert.Equal(3, item.ItemType.Value); }
        }

        var mixed = phases.RootElement.EnumerateArray().First(row => row.GetProperty("spec").TryGetProperty("mixed", out var value) && value.GetBoolean());
        using (var f = new HumanChannelledMagicAuthority1458Tests.Harness.Fixture(mixed, false))
        {
            foreach (var connection in new[] { f.Connection, f.SecondConnection })
            {
                f.State.Apply(new PlayerAppearanceRuntimeCommand(connection, new(connection.Player.Slot, 0, 0, 0f, 0, "CrystalCensus", 0, 0, 0, default, default, default, default, default, default, default, 0, 0, 0)));
                f.State.Apply(new PlayerEquipmentRuntimeCommand(connection, new(connection.Player.Slot, 0, 3, 0, 109, 0)));
                f.State.Apply(new PlayerManaRuntimeCommand(connection, new(connection.Player.Slot, 50, 180)));
                Assert.True(f.Players.TryGet(connection, out var member));
                member.ItemPhase = member.ItemPhase!.Value with { ManaHeat = 1f, Mana = member.ItemPhase.Value.Mana with { Count = 0, Delay = 100f }, Selected = member.ItemPhase.Value.Selected with { ReleaseUseItem = true, Animation = 0, ItemTime = 0 } };
                f.State.Apply(new PlayerMovementRuntimeCommand(connection, new(connection.Player.Slot, 96, 16, 0, 64, 0, 1600, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            }
            var randomBefore = f.Random.Clone(); int calls = 0;
            f.Players.SetNpcHealthGrapplingFacts(player =>
            {
                calls++;
                if (calls == 4) f.Equipment(0, 3, 1); // Later actor's final provider edits the earlier owner.
                return false;
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(50, f.Member.Mana); Assert.Equal(50, f.SecondMember.Mana);
            Assert.Equal(180, f.Member.ItemPhase!.Value.BaseManaMaximum);
            Assert.Equal(180, f.SecondMember.ItemPhase!.Value.BaseManaMaximum);
            Assert.True(f.Random.HasSameState(randomBefore));
            Assert.True(f.Players.TryGetInventoryItem(f.SecondConnection, 0, out var second)); Assert.Equal(3, second.Stack);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var newer)); Assert.Equal(3, newer.ItemType.Value); Assert.Equal(1, newer.Stack);
        }
    }
    private static void RejectMalformedConsumptionJournals()
    {
        var connection = new TerraRuntime.Contracts.Runtime.ConnectionHandle(
            TerraRuntime.Contracts.Runtime.GameCommandSourceId.FromConnection(99231), new(new(0), new(1)));
        var store = new RuntimePlayerInventoryStore();
        Assert.True(store.TryAttach(connection));
        var before = new RuntimePlayerInventoryItem(new(109), 3, default, 1);
        Assert.True(store.TrySet(connection, before.ToCommitRequest(connection.Player.Slot, 0)));
        var valid = before with { Stack = 2 };
        foreach (var after in new[] { before, before with { Stack = 4 }, before with { Stack = 1 },
            default, valid with { ItemType = new(110) }, valid with { Prefix = new(1) }, valid with { ItemFlags = 0 } })
        {
            ulong serial = store.Serial;
            Assert.False(store.TryPrepareRemotePhase([new(connection, 0, before, after)], serial, out _));
            Assert.Equal(serial, store.Serial);
            Assert.True(store.TryGet(connection, 0, out var current));
            Assert.Equal(before, current);
            Assert.Empty(store.CaptureOwnedQuantities(connection));
        }
        Assert.False(store.TryPrepareRemotePhase([new(connection, 0, default, default)], store.Serial, out _));
        Assert.True(store.TryPrepareRemotePhase([new(connection, 0, before, valid)], store.Serial, out var prepared));
        Assert.True(prepared!.TryAdoptUnpublished());
        Assert.Single(store.CaptureOwnedQuantities(connection));
    }

    private static JsonDocument ReadOriginal()
    {
        using var stream = typeof(ManaCrystalOwnedUseSource1458Tests).Assembly.GetManifestResourceStream("ManaCrystalSync1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static void ApplyFrames(HumanChannelledMagicAuthority1458Tests.Harness.Fixture f, JsonElement frames)
    {
        foreach (var item in frames.EnumerateArray())
        {
            byte[] bytes = Convert.FromHexString(item.GetString()!);
            var frame = new TerrariaFrame((ushort)bytes.Length, bytes[2], new ReadOnlySequence<byte>(bytes),
                new ReadOnlySequence<byte>(bytes.AsMemory(3)));
            switch (frame.MessageId)
            {
                case 13:
                    Assert.Equal(TerrariaPlayerMovementDecodeResult.Decoded, TerrariaPlayerMovementDecoder.TryDecode(in frame, out var movement));
                    f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, movement.ControlFlags, movement.MovementFlags, movement.MiscFlags1,
                            movement.MiscFlags2, movement.SelectedItem, movement.PositionX + 1200, movement.PositionY + 1206,
                            movement.HasVelocity, movement.VelocityX, movement.VelocityY, movement.HasMount, movement.MountType,
                            movement.HasPotionOfReturnPositions, movement.PotionOfReturnOriginalPositionX,
                            movement.PotionOfReturnOriginalPositionY, movement.PotionOfReturnHomePositionX,
                            movement.PotionOfReturnHomePositionY, movement.HasCameraTarget, movement.CameraTargetX, movement.CameraTargetY)));
                    break;
                case 5:
                    Assert.Equal(TerrariaPlayerEquipmentDecodeResult.Decoded, TerrariaPlayerEquipmentCodec.TryDecode(in frame, out var equipment));
                    f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, equipment.SlotId,
                        equipment.Stack, equipment.Prefix, equipment.ItemNetId, equipment.ItemFlags)));
                    break;
                case 42:
                    Assert.Equal(TerrariaPlayerManaDecodeResult.Decoded, TerrariaPlayerVitalsCodec.TryDecodeMana(in frame, out var mana));
                    f.State.Apply(new PlayerManaRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, mana.Mana, mana.MaxMana)));
                    break;
                default:
                    Assert.True(frame.MessageId is 43 or 138); // Visual effect/sync marker has no mana-grant command.
                    break;
            }
        }
    }

}

internal sealed class CrystalSink : IRuntimePlayerEventSink
{
    internal Action? Item;
    internal Action? Mana;
    public void PlayerEquipmentUpdated(TerraRuntime.Contracts.Runtime.ConnectionHandle c, in TerraRuntime.Contracts.Runtime.PlayerEquipmentCommitRequest r) => Item?.Invoke();
    public void PlayerManaUpdated(TerraRuntime.Contracts.Runtime.ConnectionHandle c, in TerraRuntime.Contracts.Runtime.PlayerManaCommitRequest r) => Mana?.Invoke();
    public void PlayerAppearanceUpdated(TerraRuntime.Contracts.Runtime.ConnectionHandle c, in TerraRuntime.Contracts.Runtime.PlayerAppearanceCommitRequest r) { }
    public void PlayerSpawned(TerraRuntime.Contracts.Runtime.ConnectionHandle c, in TerraRuntime.Contracts.Runtime.PlayerSpawnCommitRequest r) { }
    public void PlayerMoved(TerraRuntime.Contracts.Runtime.ConnectionHandle c, in TerraRuntime.Contracts.Runtime.PlayerMovementCommitRequest r) { }
    public void PlayerDisconnected(TerraRuntime.Contracts.Runtime.ConnectionHandle c) { }
}
