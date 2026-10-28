using System.Drawing;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void ReleasedPickThenHeldHammer()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var originals = PlacementSource("ReleasedTool" + platform + "1458");
            Assert.Equal(14, originals.RootElement.GetArrayLength());
            AssertReleasedSourceHistoryEquivalence(originals);
            foreach (var row in originals.RootElement.EnumerateArray())
            {
                var steps = row.GetProperty("steps");
                bool local = row.GetProperty("local").GetBoolean();
                string mode = row.GetProperty("mode").GetString()!;
                Assert.Equal(local && mode is "ready" or "remaining" or "remaining-no-buffer",
                    steps[steps.GetArrayLength() - 1].GetProperty("tile").GetProperty("half").GetBoolean());
                if (mode == "remaining" && local)
                {
                    var released = steps[2];
                    Assert.True(released.GetProperty("after").GetProperty("clock").GetProperty("poundRelease").GetBoolean());
                    Assert.True(released.GetProperty("entry").GetProperty("clock").GetProperty("itemAnimation").GetInt32() > 0);
                    Assert.True(released.GetProperty("entry").GetProperty("clock").GetProperty("itemTime").GetInt32() > 0);
                    Assert.Equal(24, steps[steps.GetArrayLength() - 1].GetProperty("tick").GetInt32());
                }
                if (!local)
                    foreach (var step in steps.EnumerateArray())
                        Assert.False(step.GetProperty("after").GetProperty("clock").GetProperty("poundRelease").GetBoolean());
            }
        }
        string own = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        using var component = PlacementSource("ReleasedTool" + own + "1458");
        using var caller = PlacementSource("ReleasedToolFullCaller1458");
        using var source = JsonDocument.Parse(caller.RootElement.GetProperty("platforms").GetProperty(own).GetProperty("rows").GetRawText());
        Assert.Equal(7, source.RootElement.GetArrayLength());
        AssertReleasedSourceHistoryEquivalence(source);
        foreach (var row in source.RootElement.EnumerateArray())
        {
            var old = component.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() == row.GetProperty("local").GetBoolean() &&
                r.GetProperty("mode").GetString() == row.GetProperty("mode").GetString());
            for (int i = 0; i < row.GetProperty("steps").GetArrayLength(); i++)
                foreach (string name in new[] { "checkFrames", "syncFrames" })
                    Assert.Equal(old.GetProperty("steps")[i].GetProperty(name).GetRawText(), row.GetProperty("steps")[i].GetProperty(name).GetRawText());
        }
        foreach (string mode in new[] { "ready", "remaining", "remaining-no-buffer", "no-building", "old-range-edge", "no-rerelease" })
            ReleasedPickSourceRow(ReleasedPickRow(source, mode));
        ReleasedPickSourceRow(ReleasedPickRow(source, "ready"), staleEvent: true);
        ReleasedPickSourceRow(ReleasedPickRow(source, "ready"), replaceOldSlot: true);
        foreach (int fault in new[] { 1, 2, 3 })
            ReleasedPickSourceRow(ReleasedPickRow(source, "remaining"), alternativeFault: fault);
    }

    private static void AssertReleasedSourceHistoryEquivalence(JsonDocument source)
    {
        var buffered = ReleasedPickRow(source, "remaining");
        var unbuffered = ReleasedPickRow(source, "remaining-no-buffer");
        var first = buffered.GetProperty("steps"); var second = unbuffered.GetProperty("steps");
        Assert.Equal(25, first.GetArrayLength()); Assert.Equal(25, second.GetArrayLength());
        Assert.True(first[2].GetProperty("entrySelection").GetProperty("buffered").GetBoolean());
        Assert.False(second[2].GetProperty("entrySelection").GetProperty("buffered").GetBoolean());
        for (int tick = 0; tick < first.GetArrayLength(); tick++)
        {
            foreach (string name in new[] { "checkFrames", "syncFrames", "random", "hit", "replacement", "tile" })
                Assert.Equal(first[tick].GetProperty(name).GetRawText(), second[tick].GetProperty(name).GetRawText());
            foreach (var field in first[tick].GetProperty("after").EnumerateObject())
            {
                if (field.Name != "clock")
                { Assert.Equal(field.Value.GetRawText(), second[tick].GetProperty("after").GetProperty(field.Name).GetRawText()); continue; }
                foreach (var clock in field.Value.EnumerateObject())
                {
                    var other = second[tick].GetProperty("after").GetProperty("clock").GetProperty(clock.Name);
                    if (tick == 23 && clock.Name == "itemAnimationMax")
                    { Assert.Equal(23, clock.Value.GetInt32()); Assert.Equal(0, other.GetInt32()); }
                    else Assert.Equal(clock.Value.GetRawText(), other.GetRawText());
                }
            }
        }
        Assert.Equal(first[24].GetProperty("after").GetRawText(), second[24].GetProperty("after").GetRawText());
        Assert.Equal(buffered.GetProperty("publications").GetRawText(), unbuffered.GetProperty("publications").GetRawText());
    }

    private static JsonElement ReleasedPickRow(JsonDocument source, string mode) =>
        source.RootElement.EnumerateArray().Single(r => r.GetProperty("local").GetBoolean() && r.GetProperty("mode").GetString() == mode);

    private static void ReleasedPickSourceRow(JsonElement row, bool staleEvent = false, bool replaceOldSlot = false, int alternativeFault = 0)
    {
        string mode = row.GetProperty("mode").GetString()!;
        bool positive = (mode is "ready" or "remaining" or "remaining-no-buffer") && !staleEvent && !replaceOldSlot;
        int targetX = mode == "old-range-edge" ? 52 : 50;
        using var f = new Fixture(3509);
        f.Equip(1, 7);
        f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));
        for (int x = 102; x <= 104; x++) for (int y = 101; y <= 102; y++) f.Tiles.Set(x, y, default);
        for (int x = 0; x < 400; x++) f.Tiles.Set(x, 54, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tiles.Set(targetX, 52, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 64, 16, 0, 64, 0,
            736, 822, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        Assert.True(f.Players.TryCaptureToolBuildingBeforeMovement(746, 843, out var building));
        var permission = building.Owner!;
        if (mode == "no-building") Assert.True(permission.TrySetSourceEvent(true, new Rectangle(700, 800, 100, 100)));
        using var transport = new PlacementTransport(f);
        transport.Post([3, 0, 138]);
        var inventory = f.CaptureInventory();
        int actions = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            int tick = step.GetProperty("tick").GetInt32();
            if (tick == 1)
            {
                if (mode == "no-building") Assert.True(permission.TrySetSourceEvent(false, null));
                if (staleEvent) Assert.True(permission.TrySetSourceEvent(true, new Rectangle(0, 0, 16, 16)));
                if (replaceOldSlot)
                {
                    f.Equip(0, 1);
                    transport.Post([3, 0, 138]);
                    inventory = f.CaptureInventory();
                }
            }
            var input = step.GetProperty("beforeCheck").GetProperty("clock");
            bool held = input.GetProperty("controlUseItem").GetBoolean();
            ReleasedPickControls(f, transport, input.GetProperty("selectedItem").GetByte(), held);
            foreach (var encoded in step.GetProperty("checkFrames").EnumerateArray())
            {
                var bytes = Convert.FromHexString(encoded.GetString()!);
                if (bytes[2] is not (17 or 125)) continue;
                if (bytes[2] == 17) actions++;
                transport.Post(bytes);
            }
            f.Tick();
            f.AssertInventory(inventory);
            if (!positive) continue;
            Assert.Equal((ulong)actions, f.Tools.ActionSerial);
            var clock = step.GetProperty("after").GetProperty("clock");
            var compatible = f.Tools.Clocks;
            if (clock.GetProperty("itemAnimationMax").GetInt32() != compatible.Selected.AnimationMax)
            {
                Assert.Equal("remaining", mode); Assert.Equal(23, tick);
                Assert.Equal(0, compatible.Selected.AnimationMax);
                var alternative = f.Tools.ReleaseWitness!.BufferedClockAlternative;
                Assert.NotNull(alternative);
                Assert.True(f.Players.TryCaptureToolActor(f.Connection, out var actor));
                Assert.True(alternative!.TryCaptureSourceClocks(actor!, targetX, 52, out compatible));
                Assert.True((alternative.PossibleSelections & (1UL << 1)) != 0);
                Assert.Equal(clock.GetProperty("poundRelease").GetBoolean(), compatible.PoundRelease);
                Assert.Equal(clock.GetProperty("revolverCritChanceBonus").GetInt32(), alternative.RemoteCrit);
                Assert.True(alternative.CreateRandomPreview().HasSameState(ReadLucyRandom(step.GetProperty("random"))));
                PlacementHitOracle(step.GetProperty("hit"), alternative.Hits);
                PlacementHitOracle(step.GetProperty("replacement"), alternative.ReplacementHits);
                if (alternativeFault != 0)
                {
                    if (alternativeFault == 1) CurrentLucyRandom(f).Next(3);
                    else if (alternativeFault == 2) f.Equip(3, 2);
                    else Assert.True(permission.TrySetSourceEvent(true, new Rectangle(0, 0, 16, 16)));
                    var changedInventory = f.CaptureInventory();
                    var changedRandom = CurrentLucyRandom(f).Clone();
                    var changedWorld = f.PlacementWorldRandom.Clone();
                    var changedClocks = f.Tools.Clocks;
                    bool captured = f.Players.TryCaptureToolActor(f.Connection, out var changedActor);
                    if (alternativeFault == 1) Assert.True(captured); // independently sensitive endpoint check.
                    Assert.False(captured && alternative.TryCaptureSourceClocks(changedActor!, targetX, 52, out _));
                    Assert.Equal(changedClocks, f.Tools.Clocks);
                    f.AssertInventory(changedInventory);
                    Assert.True(CurrentLucyRandom(f).HasSameState(changedRandom));
                    Assert.True(f.PlacementWorldRandom.HasSameState(changedWorld));
                    return;
                }
            }
            Assert.Equal(clock.GetProperty("itemTime").GetInt32(), compatible.Selected.ItemTime);
            Assert.Equal(clock.GetProperty("itemTimeMax").GetInt32(), compatible.Selected.ItemTimeMax);
            Assert.Equal(clock.GetProperty("itemAnimation").GetInt32(), compatible.Selected.Animation);
            Assert.Equal(clock.GetProperty("itemAnimationMax").GetInt32(), compatible.Selected.AnimationMax);
            Assert.Equal(clock.GetProperty("toolTime").GetInt32(), compatible.ToolTime);
            Assert.Equal(clock.GetProperty("revolverCritChanceBonus").GetInt32(), compatible.Selected.RevolverCritBonus);
            PlacementHitOracle(step.GetProperty("hit"), f.Tools.Hits);
            PlacementHitOracle(step.GetProperty("replacement"), f.Tools.ReplacementHits);
            Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("random"))),
                $"released tool {mode} tick={tick}: live={LucyRandomCursor(CurrentLucyRandom(f))} source={step.GetProperty("random").GetProperty("cursor")}");
            Assert.Equal(step.GetProperty("tile").GetProperty("half").GetBoolean(), f.Tiles.Get(targetX, 52).IsHalfBrick);
            if (!held) Assert.NotNull(f.Tools.ReleaseWitness);
        }
        if (positive)
        {
            Assert.True(f.Tiles.Get(targetX, 52).IsHalfBrick);
            Assert.Null(f.Tools.ReleaseWitness);
            var final = row.GetProperty("steps")[row.GetProperty("steps").GetArrayLength() - 1];
            var repeated = final.GetProperty("checkFrames").EnumerateArray()
                .Select(x => Convert.FromHexString(x.GetString()!)).Single(x => x[2] == 17);
            AssertReleasedPickDuplicate(f, transport, repeated, targetX, inventory);
            for (int i = 0; i < 64 && f.Tools.Clocks.Selected.ItemTime > 0; i++) f.Tick();
            Assert.Equal(0, f.Tools.Clocks.Selected.ItemTime);
            Assert.True(f.Tools.Clocks.Selected.Animation > 0);
            AssertReleasedPickDuplicate(f, transport, repeated, targetX, inventory);
            return;
        }
        // No unobserved target, timer, or selected-slot report creates permission.
        // Wait on real owned frames only, then test a currently usable hammer.
        for (int i = 0; i < 100 && (f.Tools.SourceItem != new ItemTypeId(7) ||
            f.Tools.RequiresFreshUse || f.Tools.Clocks.Selected.Animation == 0); i++) f.Tick();
        Assert.Equal(new ItemTypeId(7), f.Tools.SourceItem);
        Assert.False(f.Tools.RequiresFreshUse);
        Assert.True(f.Tools.Clocks.Selected.Animation > 0);
        ulong serial = f.Tools.ActionSerial;
        var random = CurrentLucyRandom(f).Clone();
        var worldRandom = f.PlacementWorldRandom.Clone();
        var tile = f.Tiles.Get(targetX, 52);
        transport.Post([11, 0, 17, 7, (byte)targetX, 0, 52, 0, 1, 0, 0]);
        Assert.Equal(serial, f.Tools.ActionSerial);
        Assert.Equal(tile, f.Tiles.Get(targetX, 52));
        Assert.False(tile.IsHalfBrick);
        Assert.True(CurrentLucyRandom(f).HasSameState(random));
        Assert.True(f.PlacementWorldRandom.HasSameState(worldRandom));
        f.AssertInventory(inventory);
    }

    private static void AssertReleasedPickDuplicate(Fixture f, PlacementTransport transport, byte[] repeated,
        int targetX, Image inventory)
    {
        ulong serial = f.Tools.ActionSerial;
        var random = CurrentLucyRandom(f).Clone();
        var worldRandom = f.PlacementWorldRandom.Clone();
        var before = f.Tiles.Get(targetX, 52);
        transport.Post(repeated);
        Assert.Equal(serial, f.Tools.ActionSerial);
        Assert.Equal(before, f.Tiles.Get(targetX, 52));
        f.AssertInventory(inventory);
        Assert.True(CurrentLucyRandom(f).HasSameState(random));
        Assert.True(f.PlacementWorldRandom.HasSameState(worldRandom));
    }

    private static void ReleasedPickControls(Fixture f, PlacementTransport transport, byte selected, bool held)
    {
        var player = f.Member;
        var controls = new TerrariaPlayerMovementState(f.Connection.Player.Slot.Value,
            (byte)(64 | (held ? 32 : 0)), 16, 0, 64, selected, player.PositionX, player.PositionY,
            false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0);
        transport.Post(TerrariaPlayerMovementEncoder.Encode(controls));
    }
}

public sealed class OwnedReleasedToolPoundContinuity1458Tests
{
    [Fact]
    public void Source_released_pick_qualifies_later_held_hammer_without_raw_clock_reset()
        => ToolPlayerBoundaryChecks.ReleasedPickThenHeldHammer();
}
