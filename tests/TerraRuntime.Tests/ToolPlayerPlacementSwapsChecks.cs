using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Protocol;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void OrdinaryPlacementAndSwaps()
    {
        string platform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        using var source = PlacementSource("PlacementSwaps" + platform + "1458");
        int ordinary = 0, replacements = 0, walls = 0, slow = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            if (mode == "ordinary")
            {
                ordinary++;
                int tile = row.GetProperty("steps")[0].GetProperty("afterWorld").EnumerateArray()
                    .Single(t => t.GetProperty("x").GetInt32() == 50 && t.GetProperty("y").GetInt32() == 50)
                    .GetProperty("type").GetInt32();
                Assert.True(TerraRuntime.Gameplay.Tiles.VanillaLocalPlacementTiles1458.IsOrdinarySolid(new(tile)));
                // Falling has a separate server afterimage/publication assertion.
                if (tile is not (53 or 112 or 116 or 123 or 224 or 234 or 495)) PlacementPaidRow(row);
                else PlacementSwapRow(row);
                continue;
            }
            if (!row.GetProperty("steps").EnumerateArray().Any(s =>
                PlacementActionBytes(s).Any(p => p[2] == 17 && p[3] is 21 or 22))) continue;
            PlacementSwapRow(row);
            if (mode == "swapWall") walls++;
            else if (mode == "swapSlow") slow++;
            else replacements++;
        }
        Assert.Equal(251, ordinary); Assert.Equal(14, replacements);
        Assert.Equal(4, walls); Assert.Equal(2, slow);
        var direct = source.RootElement.EnumerateArray().First(r =>
            r.GetProperty("mode").GetString() == "swapFast" && r.GetProperty("item").GetInt32() == 2 &&
            r.GetProperty("stack").GetInt32() == 3);
        var wall = source.RootElement.EnumerateArray().First(r =>
            r.GetProperty("mode").GetString() == "swapWall" && r.GetProperty("item").GetInt32() == 26 &&
            r.GetProperty("stack").GetInt32() == 3);
        PlacementSwapRow(direct, 1);
        foreach (var row in new[] { direct, wall })
        {
            PlacementSwapDenied(row, false);
            PlacementSwapDenied(row, true);
        }
        PlacementSwapDropReentry(direct);
        var delayed = source.RootElement.EnumerateArray().First(r =>
            r.GetProperty("mode").GetString() == "ordinary" && r.GetProperty("item").GetInt32() == 2);
        PlacementReleasedBeforeTail(delayed);
        foreach (int fault in new[] { 1, 2, 3, 4, 5 }) PlacementReleasedBeforeTail(delayed, fault);
        PlacementReleasedBeforeTail(delayed, 6);
        using var rich = PlacementSource("PlacementRichSwaps" + platform + "1458");
        Assert.Equal(4, rich.RootElement.GetArrayLength());
        foreach (var row in rich.RootElement.EnumerateArray()) PlacementSwapRow(row);
        using var wallsSource = PlacementSource("PlacementAllWalls" + platform + "1458");
        Assert.Equal(292, wallsSource.RootElement.GetArrayLength());
        foreach (var row in wallsSource.RootElement.EnumerateArray()) PlacementSwapRow(row);
        using var existing = PlacementSource("PlacementExistingTargets" + platform + "1458");
        Assert.Equal(13, existing.RootElement.GetArrayLength());
        foreach (var row in existing.RootElement.EnumerateArray()) PlacementSwapRow(row);
    }

    private static void PlacementSwapRow(JsonElement row, short? availableFunding = null)
    {
        short item = row.GetProperty("item").GetInt16();
        short stock = row.GetProperty("stack").GetInt16();
        string mode = row.GetProperty("mode").GetString()!;
        using var f = PlacementScene(item, stock);
        if (mode == "swapFast" || mode.StartsWith("richTile", StringComparison.Ordinal) ||
            mode.StartsWith("existing", StringComparison.Ordinal))
            f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, 1, 1, 0, 2774, 0)));
        if (mode is "swapFast" or "swapSlow")
            f.Tiles.Set(50, 50, new WorldTile { Type = (ushort)(item == 3 ? 0 : 1), Flags = WorldTileFlags.Active });
        else if (mode == "swapWall")
        {
            var center = f.Tiles.Get(50, 50); center.Wall = (ushort)(item == 26 ? 4 : 1);
            f.Tiles.Set(50, 50, center);
        }
        if (mode.StartsWith("rich", StringComparison.Ordinal))
        {
            var richTile = new WorldTile { Type = 1, Wall = 4, TileColor = 9, WallColor = 10,
                LiquidAmount = 87, LiquidKind = mode.EndsWith("Lava", StringComparison.Ordinal) ? WorldLiquidKind.Lava :
                    mode.EndsWith("Honey", StringComparison.Ordinal) ? WorldLiquidKind.Honey : WorldLiquidKind.Water,
                Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation |
                    WorldTileFlagMasks.Visibility | WorldTileFlagMasks.Fullbright };
            richTile.TrySetSourceShape(true, 1);
            f.Tiles.Set(50, 50, richTile);
        }
        if (mode.StartsWith("existing", StringComparison.Ordinal))
        {
            ushort old = ushort.Parse(mode.AsSpan(8));
            f.Tiles.Set(50, 50, new WorldTile { Type = old, Flags = WorldTileFlags.Active });
            if (old == 404)
            {
                f.Tiles.Set(49, 50, new WorldTile { Type = old, Flags = WorldTileFlags.Active });
                f.Tiles.Set(50, 49, new WorldTile { Type = old, Flags = WorldTileFlags.Active });
            }
        }
        using var transport = new PlacementTransport(f);
        // Complete the configured character-inventory import before beginning
        // the measured use. A partial replacement has no inventory5/138 tail
        // that could otherwise close this unrelated bootstrap cursor journal.
        transport.Post([3, 0, 138]);
        short funded = availableFunding ?? stock;
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = funded;
        var before = f.CaptureInventory();
        int completion = -1;
        JsonElement publicationStep = default;
        bool hasPublicationStep = false;
        int inventoryPublications = 0;
        f.Events.OnPlacementEquipment = report =>
        {
            if (report.SlotId != 0) return;
            Assert.True(hasPublicationStep);
            inventoryPublications++;
            Assert.True(f.PlacementWorldRandom.HasSameState(ReadLucyRandom(publicationStep.GetProperty("serverRngAfter"))),
                $"swap publication server {mode}: actual={LucyRandomCursor(f.PlacementWorldRandom)} expected={publicationStep.GetProperty("serverRngAfter").GetProperty("cursor")}");
            Assert.Equal(1UL, f.Tools.ActionSerial);
            Assert.Equal((short)(stock - 1), f.CaptureInventory().Items[0].Stack);
            Assert.Equal((short)(funded - 1), f.CaptureInventory().Quantity[0]);
        };
        var steps = row.GetProperty("steps");
        for (int tick = 0; tick < steps.GetArrayLength(); tick++)
        {
            var step = steps[tick];
            var packets = PlacementActionBytes(step).Where(p => p[2] == 17).ToArray();
            if (packets.Length > 0)
            {
                publicationStep = step; hasPublicationStep = true;
                // The original server17 component starts at seed19. Configure
                // that separate role stream BEFORE observing the cohort; prior
                // runtime NPC scheduling is outside this component oracle.
                f.PlacementWorldRandom.CopyStateFrom(new VanillaUnifiedRandom1458(19));
                foreach (var bytes in packets) transport.Post(bytes);
                foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
                {
                    var bytes = Convert.FromHexString(text.GetString()!);
                    if (bytes[2] is 5 or 138) transport.Post(bytes);
                }
                completion = tick;
            }
            f.Tick();
            Assert.True(f.Tools.LocalClocksKnown);
            if (completion < 0)
            {
                f.AssertInventory(before);
                Assert.Equal(0UL, f.Tools.ActionSerial);
                // A matching retained history, not the no-extra-action
                // canonical branch, must preserve the original partial image.
                var alternatives = f.Tools.PlacementAlternative;
                Assert.NotNull(alternatives);
                RuntimePlayerPlacementTarget1458? matching = null;
                foreach (var candidate in alternatives!.Targets)
                    if (candidate.X == 50 && candidate.Y == 50) matching = candidate;
                Assert.NotNull(matching);
                PlacementHitOracle(step.GetProperty("after").GetProperty("hitReplace"), matching!.ReplacementHits);
                Assert.True(matching.CreatePreview().HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))));
                continue;
            }
            Assert.Equal(1UL, f.Tools.ActionSerial);
            var clock = step.GetProperty("after").GetProperty("clock");
            Assert.Equal(clock.GetProperty("itemTime").GetInt32(), f.Tools.Clocks.Selected.ItemTime);
            Assert.Equal(clock.GetProperty("itemTimeMax").GetInt32(), f.Tools.Clocks.Selected.ItemTimeMax);
            Assert.Equal(clock.GetProperty("itemAnimation").GetInt32(), f.Tools.Clocks.Selected.Animation);
            Assert.Equal(clock.GetProperty("itemAnimationMax").GetInt32(), f.Tools.Clocks.Selected.AnimationMax);
            PlacementHitOracle(step.GetProperty("after").GetProperty("hitTile"), f.Tools.Hits);
            PlacementHitOracle(step.GetProperty("after").GetProperty("hitReplace"), f.Tools.ReplacementHits);
            Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))));
            var after = f.CaptureInventory();
            Assert.Equal((short)(stock - 1), after.Items[0].Stack);
            Assert.Equal((short)(funded - 1), after.Quantity[0]);
            for (int slot = 1; slot < before.Items.Length; slot++)
            { Assert.Equal(before.Items[slot], after.Items[slot]); Assert.Equal(before.Quantity[slot], after.Quantity[slot]); }
            Assert.Equal(before.Copper, after.Copper); Assert.Equal(before.Caps, after.Caps);
            var sourceTile = step.GetProperty("serverAfter").GetProperty("cells").EnumerateArray()
                .Single(t => t.GetProperty("x").GetInt32() == 50 && t.GetProperty("y").GetInt32() == 50);
            var actual = f.Tiles.Get(50, 50);
            Assert.Equal((sourceTile.GetProperty("header").GetInt32() & 32) != 0, actual.IsActive);
            Assert.Equal(sourceTile.GetProperty("type").GetInt32(), actual.Type);
            Assert.Equal(sourceTile.GetProperty("wall").GetInt32(), actual.Wall);
            Assert.Equal(sourceTile.GetProperty("liquid").GetInt32(), actual.LiquidAmount);
            Assert.Equal(sourceTile.GetProperty("slope").GetInt32(), actual.SourceSlope);
            Assert.Equal(sourceTile.GetProperty("half").GetBoolean(), actual.IsHalfBrick);
            foreach (var cell in step.GetProperty("serverAfter").GetProperty("cells").EnumerateArray())
            {
                var projected = f.Tiles.Get(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
                Assert.Equal((cell.GetProperty("header").GetInt32() & 32) != 0, projected.IsActive);
                Assert.Equal(cell.GetProperty("type").GetInt32(), projected.Type);
                Assert.Equal(cell.GetProperty("wall").GetInt32(), projected.Wall);
                Assert.Equal(cell.GetProperty("liquid").GetInt32(), projected.LiquidAmount);
                Assert.Equal((byte)(cell.GetProperty("header").GetInt32() & 31), projected.TileColor);
                Assert.Equal((byte)(cell.GetProperty("b1").GetInt32() & 31), projected.WallColor);
                Assert.Equal(cell.GetProperty("half").GetBoolean(), projected.IsHalfBrick);
                Assert.Equal(cell.GetProperty("slope").GetInt32(), projected.SourceSlope);
            }
            if (mode.StartsWith("rich", StringComparison.Ordinal))
            {
                bool wallReplacement = mode.StartsWith("richWall", StringComparison.Ordinal);
                Assert.Equal(WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation,
                    actual.Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation));
                Assert.Equal(wallReplacement ? (byte)9 : (byte)0, actual.TileColor);
                Assert.Equal(wallReplacement ? (byte)0 : (byte)10, actual.WallColor);
                Assert.Equal(wallReplacement, (actual.Flags & WorldTileFlags.InvisibleBlock) != 0);
                Assert.Equal(wallReplacement, (actual.Flags & WorldTileFlags.FullbrightBlock) != 0);
                Assert.Equal(!wallReplacement, (actual.Flags & WorldTileFlags.InvisibleWall) != 0);
                Assert.Equal(!wallReplacement, (actual.Flags & WorldTileFlags.FullbrightWall) != 0);
                Assert.Equal(mode.EndsWith("Lava", StringComparison.Ordinal) ? WorldLiquidKind.Lava :
                    mode.EndsWith("Honey", StringComparison.Ordinal) ? WorldLiquidKind.Honey : WorldLiquidKind.Water,
                    actual.LiquidKind);
            }
            break;
        }
        Assert.True(completion >= 0);
        Assert.Equal(1, inventoryPublications);
        if (mode == "swapSlow") Assert.Equal(26, completion);
    }

    private static void PlacementSwapDenied(JsonElement row, bool staleWorld)
    {
        short item = row.GetProperty("item").GetInt16();
        using var f = PlacementScene(item, 3);
        bool wall = row.GetProperty("mode").GetString() == "swapWall";
        if (!wall) f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot, 1, 1, 0, 2774, 0)));
        var beforeTile = f.Tiles.Get(50, 50);
        if (wall) beforeTile.Wall = 4;
        else { beforeTile.Type = 1; beforeTile.Flags |= WorldTileFlags.Active; }
        f.Tiles.Set(50, 50, beforeTile);
        using var transport = new PlacementTransport(f);
        transport.Post([3,0,138]);
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = staleWorld ? (short)3 : (short)0;
        var before = f.CaptureInventory();
        var step = row.GetProperty("steps")[0];
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        if (staleWorld)
        {
            var changed = beforeTile; changed.TileColor = 9;
            f.Tiles.Set(50, 50, changed);
            beforeTile = changed;
        }
        foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(text.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        f.Tick();
        Assert.Equal(0UL, f.Tools.ActionSerial);
        Assert.Equal(beforeTile, f.Tiles.Get(50, 50));
        f.AssertInventory(before);
        f.Move(0, false);
        Assert.Null(f.Tools.Input);
        f.AssertInventory(before);
    }

    private static void PlacementSwapDropReentry(JsonElement row)
    {
        var sink = new PlacementSwapDropSink();
        using var f = PlacementScene(2, 3, worldItemSink: sink);
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot, 1, 1, 0, 2774, 0)));
        f.Tiles.Set(50, 50, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        using var transport = new PlacementTransport(f);
        transport.Post([3,0,138]);
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 1;
        var before = f.CaptureInventory();
        var step = row.GetProperty("steps")[0];
        sink.OnDrop = snapshot =>
        {
            Assert.Equal((short)3, snapshot.ItemNetId); Assert.Equal((short)1, snapshot.Stack);
            var current = f.CaptureInventory();
            Assert.Equal((short)2, current.Items[0].Stack);
            Assert.Equal((short)0, current.Quantity[0]);
            Assert.Equal(before.Copper, current.Copper); Assert.Equal(before.Caps, current.Caps);
            Assert.Equal(1UL, f.Tools.ActionSerial);
            Assert.True(f.Tiles.Get(50,50).IsActive); Assert.Equal((ushort)0, f.Tiles.Get(50,50).Type);
            foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
            foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
            {
                var bytes = Convert.FromHexString(text.GetString()!);
                if (bytes[2] is 5 or 138) transport.Post(bytes);
            }
        };
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(text.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        f.Tick();
        Assert.Equal(1, sink.Drops);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal((short)2, f.CaptureInventory().Items[0].Stack);
        Assert.Equal((short)0, f.CaptureInventory().Quantity[0]);
    }

    private static void PlacementReleasedBeforeTail(JsonElement row, int fault = 0)
    {
        using var f = PlacementScene(2, 3);
        using var transport = new PlacementTransport(f);
        transport.Post([3,0,138]);
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 3;
        f.Tick(); // an actual qualified held source stage, no injected local clock.
        var step = row.GetProperty("steps")[0];
        f.PlacementWorldRandom.CopyStateFrom(ReadLucyRandom(step.GetProperty("serverRngBefore")));
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        Assert.NotNull(f.Tools.Input); Assert.Null(f.Tools.Input!.PlacementInventoryReport);
        Assert.True(f.Tools.Input.Actor!.HasCurrentSourceStage);
        if (fault == 1)
            f.Apply(new PlayerMovementRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot,64,16,0,64,0,737,806,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        else f.Move(0, false);
        Assert.NotNull(f.Tools.Input); // release alone cannot discard the pending economic tail.
        Assert.Equal(fault != 1, f.Tools.Input!.HasKnownPlacementStage(f.Tools.Input.Actor!));
        Assert.False(f.Tools.Input.Actor!.HasCurrentSourceStage); // the general source-stage contract stays strict.
        if (fault == 2) CurrentLucyRandom(f).Next(3);
        if (fault == 3) PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 2;
        if (fault == 4) f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot, 3, 1, 0, 2, 0)));
        if (fault == 5)
        {
            var changed = f.Tiles.Get(50,50); changed.TileColor = 9; f.Tiles.Set(50,50,changed);
        }
        if (fault == 6) f.Move(0, false); // same exact release-only endpoint chain remains bounded and valid.
        var expectedInventory = f.CaptureInventory();
        foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(text.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        if (fault is >= 1 and <= 5)
        {
            Assert.Equal(0UL, f.Tools.ActionSerial);
            Assert.False(f.Tiles.Get(50,50).IsActive);
            f.AssertInventory(expectedInventory);
            return;
        }
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.True(f.Tiles.Get(50,50).IsActive);
        Assert.Equal((short)2, f.CaptureInventory().Items[0].Stack);
        Assert.Equal((short)2, f.CaptureInventory().Quantity[0]);
        Assert.Equal(0, f.Member.ControlFlags & 32);
        Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))));
    }

    internal static void UnfundedReleasedPlacementTailRecovers()
    {
        string platform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        using var source = PlacementSource("PlacementSwaps" + platform + "1458");
        var row = source.RootElement.EnumerateArray().First(r =>
            r.GetProperty("mode").GetString() == "ordinary" && r.GetProperty("item").GetInt32() == 2);
        var step = row.GetProperty("steps")[0];
        using var f = PlacementScene(2, 3);
        using var transport = new PlacementTransport(f);
        transport.Post([3,0,138]);
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 0;
        f.Tick();
        var before = f.CaptureInventory();
        var local = CurrentLucyRandom(f).Clone();
        var world = f.PlacementWorldRandom.Clone();
        var clocks = f.Tools.Clocks;
        var hit = f.Tools.Hits;
        var replacement = f.Tools.ReplacementHits;
        var tiles = new WorldTile[9];
        for (int x = 49; x <= 51; x++) for (int y = 49; y <= 51; y++)
            tiles[(x - 49) * 3 + y - 49] = f.Tiles.Get(x,y);
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        Assert.True(f.Tools.Input!.Actor!.HasCurrentSourceStage);
        PlacementSocketControls(f, transport, false);
        Assert.NotNull(f.Tools.Input);
        Assert.True(f.Tools.Input!.HasKnownPlacementStage(f.Tools.Input.Actor!));
        Assert.False(f.Tools.Input.Actor!.HasCurrentSourceStage);
        foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(text.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        Assert.NotNull(f.Tools.Input!.PlacementInventoryReport);
        Assert.Equal(0UL, f.Tools.ActionSerial);
        f.AssertInventory(before);
        PlacementSocketControls(f, transport, false);
        Assert.Null(f.Tools.Input); // completed q0 tail must release the same bounded cohort.
        Assert.Equal(0UL, f.Tools.ActionSerial);
        f.AssertInventory(before);
        Assert.Equal(clocks, f.Tools.Clocks);
        Assert.Same(hit, f.Tools.Hits);
        Assert.Same(replacement, f.Tools.ReplacementHits);
        Assert.True(CurrentLucyRandom(f).HasSameState(local));
        Assert.True(f.PlacementWorldRandom.HasSameState(world));
        for (int x = 49; x <= 51; x++) for (int y = 49; y <= 51; y++)
            Assert.Equal(tiles[(x - 49) * 3 + y - 49], f.Tiles.Get(x,y));
        // A new configured funded preimage then uses the next real source-ready
        // stage. No timer, source pose or RNG is reset to recover this use.
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 1;
        for (int i = 0; i < 64 &&
            (f.Tools.Clocks.Selected.Animation > 0 || f.Tools.Clocks.Selected.ItemTime > 0); i++) f.Tick();
        Assert.True(f.Tools.LocalClocksKnown);
        Assert.Equal(0, f.Tools.Clocks.Selected.Animation);
        Assert.Equal(0, f.Tools.Clocks.Selected.ItemTime);
        PlacementSocketControls(f, transport, true);
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        foreach (var text in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(text.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        f.Tick();
        Assert.Null(f.Tools.Input);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.True(f.Tiles.Get(50,50).IsActive);
        var paid = f.CaptureInventory();
        Assert.Equal((short)2, paid.Items[0].Stack);
        Assert.Equal((short)0, paid.Quantity[0]);
        for (int slot = 1; slot < before.Items.Length; slot++)
        { Assert.Equal(before.Items[slot], paid.Items[slot]); Assert.Equal(before.Quantity[slot], paid.Quantity[slot]); }
        Assert.Equal(before.Copper, paid.Copper); Assert.Equal(before.Caps, paid.Caps);
    }

    private static void PlacementSocketControls(Fixture f, PlacementTransport transport, bool use)
    {
        var m = f.Member;
        var movement = new TerrariaPlayerMovementState(f.Connection.Player.Slot.Value,
            (byte)(64 | (use ? 32 : 0)), 16, 0, 64, 0, m.PositionX, m.PositionY,
            false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0);
        transport.Post(TerraRuntime.Protocol.Multiplicity.TerrariaPlayerMovementEncoder.Encode(movement));
    }

    private sealed class PlacementSwapDropSink : IWorldItemStateCommitSink
    {
        internal int Drops;
        internal Action<WorldItemSnapshot>? OnDrop;
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        {
            if (kind != WorldItemStateCommitKind.Drop) return;
            Drops++;
            var callback = OnDrop; OnDrop = null;
            callback?.Invoke(snapshot);
        }
    }
}

public sealed class OwnedOrdinaryPlacementSwaps1458Tests
{
    [Fact]
    public void Released_unfunded_placement_tail_retires_once_and_next_funded_use_recovers()
        => ToolPlayerBoundaryChecks.UnfundedReleasedPlacementTailRecovers();

    [Fact]
    public void Paid_ordinary_placement_and_replacement_keep_source_clocks_caches_and_role_rng()
        => ToolPlayerBoundaryChecks.OrdinaryPlacementAndSwaps();
}
