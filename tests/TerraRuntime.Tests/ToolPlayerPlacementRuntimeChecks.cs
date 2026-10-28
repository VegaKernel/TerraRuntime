using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void PlacementRuntime()
    {
        PlacementUnfundedBeforeFirstTick();
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var wall = PlacementSource("PlacementWallFill" + platform + "1458");
            foreach (var row in wall.RootElement.EnumerateArray()) PlacementTailOracle(row);
            using var edge = PlacementSource("PlacementEdge" + platform + "1458");
            foreach (var row in edge.RootElement.EnumerateArray())
                if (row.GetProperty("mode").GetString() == "wallFill") PlacementTailOracle(row);
        }
        string ownPlatform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        using var ownWall = PlacementSource("PlacementWallFill" + ownPlatform + "1458");
        foreach (var row in ownWall.RootElement.EnumerateArray())
        { PlacementPaidRow(row); if (row.GetProperty("stack").GetInt32() == 6) PlacementPaidRow(row, 5); }
        PlacementDeniedCosts(ownWall.RootElement[2]);
        using var ownEdge = PlacementSource("PlacementEdge" + ownPlatform + "1458");
        using var gray = PlacementSource("PlacementGrayBrick" + ownPlatform + "1458");
        PlacementPaidRow(gray.RootElement[0]);
        PlacementPaidRow(ownEdge.RootElement[6]);
        PlacementUnknownLocalRefusal(ownEdge.RootElement[6]);
        PlacementUnknownLocalRefusal(ownEdge.RootElement[6], unknownWorldContext: true);
        PlacementNormalSwitch(ownEdge.RootElement[6]);
        PlacementFastSwitch(ownEdge.RootElement[7]);
        PlacementFastSwitch(ownEdge.RootElement[7], externalRandomDraw: true);
        foreach (var row in ownEdge.RootElement.EnumerateArray())
            if (row.GetProperty("mode").GetString() == "wallFill") PlacementPaidRow(row);
    }

    private static JsonDocument PlacementSource(string name)
    {
        using var data = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing original placement numeric resource: " + name);
        using var gzip = new GZipStream(data, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void PlacementTailOracle(JsonElement row)
    {
        int stock = row.GetProperty("stack").GetInt32();
        var step = row.GetProperty("steps")[0];
        var after = step.GetProperty("after");
        var clock = after.GetProperty("clock");
        var placements = PlacementActionBytes(step).Where(x => x[2] == 17 && x[3] == 3).ToArray();
        Assert.InRange(placements.Length, 1, 5);
        Assert.True(VanillaLocalPlacementCatalog1458.TryGet(new(row.GetProperty("item").GetInt32()), out var definition));
        var owner = new ToolClockState1458(new(0, 0, clock.GetProperty("itemAnimation").GetInt32(),
            clock.GetProperty("itemAnimationMax").GetInt32(), false, 0, 0), 0, false, false);
        Assert.True(VanillaLocalPlacement1458.TryPlanTail(definition, owner, stock, placements.Length - 1, 1f,
            OperatingSystem.IsWindows(), out var tail));
        Assert.Equal(clock.GetProperty("itemTime").GetInt32(), tail.State.Selected.ItemTime);
        Assert.Equal(clock.GetProperty("itemTimeMax").GetInt32(), tail.State.Selected.ItemTimeMax);
        Assert.Equal(clock.GetProperty("toolTime").GetInt32(), tail.State.ToolTime);
        var canonical = step.GetProperty("afterSync").GetProperty("inventory")[0];
        Assert.Equal(canonical[1].GetInt32(), tail.RemainingStack);
        Assert.Equal(stock - tail.RemainingStack, tail.ConsumedQuantity);
        if (stock is 2 or 3)
        {
            Assert.Equal(5, placements.Length);
            Assert.True(after.GetProperty("inventory")[0][1].GetInt32() < 0);
            Assert.Equal(0, canonical[0].GetInt32()); Assert.Equal(0, canonical[1].GetInt32());
        }
    }

    private static byte[][] PlacementActionBytes(JsonElement step) => step.GetProperty("actionFrames")
        .EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();

    private static Fixture PlacementScene(short item, short stock, bool knownLocalContext = true,
        IWorldItemStateCommitSink? worldItemSink = null)
    {
        var f = new Fixture(item, worldItemSink, initialStack: stock, playerSeed: 7, worldSeed: 19, knownLocalContext: knownLocalContext);
        for (int x = 0; x < 400; x++)
        {
            f.Tiles.Set(x, 103, default);
            f.Tiles.Set(x, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        for (int x = 102; x <= 104; x++) for (int y = 101; y <= 102; y++) f.Tiles.Set(x, y, default);
        f.Tiles.Set(51, 50, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 1, 0, 1, 0)));
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 96, 16, 0, 64, 0,
            736, 806, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        return f;
    }

    private static void PlacementPaidRow(JsonElement row, short? availableFunding = null)
    {
        short item = row.GetProperty("item").GetInt16();
        short stock = row.GetProperty("stack").GetInt16();
        short funded = availableFunding ?? stock;
        using var f = PlacementScene(item, stock);
        int wall = item == 26 ? 1 : item == 93 ? 4 : 0;
        if (wall != 0)
            foreach (var (x, y) in new[] { (48,50),(49,49),(49,51),(52,50),(51,49),(51,51),(50,48),(50,52) })
            { var tile = f.Tiles.Get(x,y); tile.Wall = (ushort)wall; f.Tiles.Set(x,y,tile); }
        // Configured owned preimage: official Terraria has no private q field.
        // The actual producer below must spend exactly these existing units.
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = funded;
        var before = f.CaptureInventory();
        var step = row.GetProperty("steps")[0];
        Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("preResetRandom"))));
        var peers = LucyPeers(f);
        using var transport = new PlacementTransport(f);
        bool observed = false;
        int expectedStack = step.GetProperty("afterSync").GetProperty("inventory")[0][1].GetInt32();
        short expectedFunding = checked((short)(funded - (stock - expectedStack)));
        f.Events.OnPlacementEquipment = report =>
        {
            if (report.SlotId != 0) return;
            observed = true;
            // Exact source MessageBuffer17 endpoint at joint placement publication.
            // State.Tick subsequently runs the separate natural-NPC spawn caller
            // on the same server Main stream; that is outside this source capsule.
            Assert.True(f.PlacementWorldRandom.HasSameState(ReadLucyRandom(step.GetProperty("serverRngAfter"))),
                $"placement publication server item={item} stock={stock}: cursor actual={LucyRandomCursor(f.PlacementWorldRandom)} expected={step.GetProperty("serverRngAfter").GetProperty("cursor").GetUInt32()}");
            var complete = f.CaptureInventory();
            Assert.Equal((short)expectedStack, complete.Items[0].Stack);
            Assert.Equal(expectedFunding, complete.Quantity[0]);
            Assert.Equal(1UL, f.Tools.ActionSerial);
            if (wall == 0) Assert.True(f.Tiles.Get(50,50).IsActive);
            else Assert.Equal(wall, f.Tiles.Get(50,50).Wall);
        };
        foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
        Assert.Equal(0, Composition(f.State).WorldTileAuthority.CommittedToolActions);
        Assert.Equal(before.Items, f.CaptureInventory().Items);
        foreach (var encoded in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes = Convert.FromHexString(encoded.GetString()!);
            if (bytes[2] is 5 or 138) transport.Post(bytes);
        }
        Assert.False(observed);
        f.Tick();
        Assert.True(observed);
        var expected = step.GetProperty("afterSync").GetProperty("inventory")[0];
        var after = f.CaptureInventory();
        Assert.Equal((short)expected[1].GetInt32(), after.Items[0].Stack);
        Assert.Equal(expected[0].GetInt32(), after.Items[0].ItemType.Value);
        Assert.Equal(expectedFunding, after.Quantity[0]);
        for (int slot = 1; slot < before.Items.Length; slot++)
        {
            Assert.Equal(before.Items[slot], after.Items[slot]);
            Assert.Equal(before.Quantity[slot], after.Quantity[slot]);
        }
        Assert.Equal(before.Copper, after.Copper); Assert.Equal(before.Caps, after.Caps);
        Assert.Equal(1UL, f.Tools.ActionSerial);
        Assert.Equal(step.GetProperty("after").GetProperty("clock").GetProperty("itemTime").GetInt32(), f.Tools.Clocks.Selected.ItemTime);
        Assert.Equal(step.GetProperty("after").GetProperty("clock").GetProperty("itemTimeMax").GetInt32(), f.Tools.Clocks.Selected.ItemTimeMax);
        Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))),
            $"source item={item} stock={stock}: local cursor actual={LucyRandomCursor(CurrentLucyRandom(f))} expected={step.GetProperty("afterRandom").GetProperty("cursor").GetUInt32()}");

        int cells = PlacementActionBytes(step).Count(bytes => bytes[2] == 17);
        Assert.Equal(cells, Composition(f.State).WorldTileAuthority.AppliedClientManipulations);
        if (wall == 0)
        {
            var sourceTile = step.GetProperty("afterWorld").EnumerateArray()
                .Single(cell => cell.GetProperty("x").GetInt32() == 50 && cell.GetProperty("y").GetInt32() == 50);
            var acceptedTile=f.Tiles.Get(50,50);
            Assert.True(acceptedTile.IsActive);
            Assert.Equal(sourceTile.GetProperty("type").GetInt32(), acceptedTile.Type);
            // Normal solid cosmetic frames are intentionally not file state in
            // the existing SourcePass. Keep literal source frame values in the
            // resource, and compare through that explicit normalized projection.
            Assert.Equal((short)0, acceptedTile.FrameX);
            Assert.Equal((short)0, acceptedTile.FrameY);
            Assert.Equal(sourceTile.GetProperty("wall").GetInt32(),acceptedTile.Wall);
            Assert.Equal(sourceTile.GetProperty("liquid").GetInt32(),acceptedTile.LiquidAmount);
            Assert.Equal(sourceTile.GetProperty("half").GetBoolean(),acceptedTile.IsHalfBrick);
            Assert.Equal(sourceTile.GetProperty("slope").GetInt32(),acceptedTile.SourceSlope);
        }
        else foreach (var packet in PlacementActionBytes(step))
        {
            if (packet[2] != 17 || packet[3] != 3) continue;
            int x = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(4));
            int y = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(6));
            Assert.Equal(wall, f.Tiles.Get(x,y).Wall);
        }
        Assert.Empty(DrainLucyFrames(peers.Sender));
        Assert.Equal(Enumerable.Repeat((byte)17,cells), DrainLucyFrames(peers.Peer));
    }

    private static void PlacementDeniedCosts(JsonElement row)
    {
        Assert.Equal(26, row.GetProperty("item").GetInt32());
        Assert.Equal(3, row.GetProperty("stack").GetInt32());
        foreach (short funded in new short[] { 0, 2, 3 })
        {
            using var f = PlacementScene(26, 3);
            foreach (var (x,y) in new[] { (48,50),(49,49),(49,51),(52,50),(51,49),(51,51),(50,48),(50,52) })
            { var t=f.Tiles.Get(x,y);t.Wall=1;f.Tiles.Set(x,y,t); }
            PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = funded;
            var before = f.CaptureInventory();
            using var transport = new PlacementTransport(f);
            var step = row.GetProperty("steps")[0];
            foreach (var bytes in PlacementActionBytes(step)) if (bytes[2] == 17) transport.Post(bytes);
            if (funded == 3)
            {
                // Reentry-like world mutation after retained beforeimage, before
                // payment/frame adoption, must invalidate the whole cohort.
                var unrelated = f.Tiles.Get(47,49); unrelated.LiquidAmount = 23; f.Tiles.Set(47,49,unrelated);
            }
            foreach (var encoded in step.GetProperty("syncFrames").EnumerateArray())
            {
                var bytes=Convert.FromHexString(encoded.GetString()!);
                if (bytes[2] is 5 or 138) transport.Post(bytes);
            }
            f.Tick();
            Assert.Equal(0UL, f.Tools.ActionSerial);
            Assert.Equal(0, f.Tiles.Get(50,50).Wall);
            Assert.Equal(0, Composition(f.State).WorldTileAuthority.CommittedToolActions);
            f.AssertInventory(before);
        }
    }

    private static void PlacementUnknownLocalRefusal(JsonElement row, bool unknownWorldContext = false)
    {
        using var f = PlacementScene(2, 3, knownLocalContext: !unknownWorldContext);
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 3;
        var before = f.CaptureInventory();
        var phase = f.Member.ItemPhase!.Value;
        var localBefore = phase.ToolUse!.Clocks;
        // Explicit unknown imported/local-operation preimage. The unchanged
        // permanent-consumable Fact separately produces this state naturally.
        if (!unknownWorldContext)
            f.Member.ItemPhase = phase with { ToolUse = phase.ToolUse with { LocalClocksKnown = false } };
        using var transport = new PlacementTransport(f);
        var step = row.GetProperty("steps")[0];
        foreach (var bytes in PlacementActionBytes(step)) if(bytes[2]==17) transport.Post(bytes);
        Assert.NotNull(f.Tools.Input);
        foreach(var encoded in step.GetProperty("syncFrames").EnumerateArray())
        {
            var bytes=Convert.FromHexString(encoded.GetString()!);
            if(bytes[2]is 5 or 138)transport.Post(bytes);
        }
        f.Tick();
        Assert.True(f.Member.ItemPhase.HasValue);
        Assert.Equal(14,f.Member.ItemPhase!.Value.Selected.Animation);
        Assert.False(f.Tools.LocalClocksKnown);
        Assert.Equal(localBefore,f.Tools.Clocks);
        Assert.Null(f.Tools.SourceStagePose); Assert.Null(f.Tools.PlacementReady);
        Assert.Null(f.Tools.PlacementAlternative); Assert.Null(f.Tools.Input);
        Assert.True(f.Tools.RequiresFreshUse); Assert.False(f.Tools.ShootEligible);
        Assert.Equal(0UL,f.Tools.ActionSerial); Assert.Equal(0,f.Events.Births);
        Assert.False(f.Tiles.Get(50,50).IsActive); f.AssertInventory(before);
        // A later trusted restoration of local knowledge cannot turn the old
        // retained reports into a source action without a fresh observation.
        var accepted=f.Member.ItemPhase!.Value;
        f.Member.ItemPhase=accepted with { ToolUse=accepted.ToolUse! with { LocalClocksKnown=true } };
        f.Tick();
        Assert.True(f.Member.ItemPhase.HasValue); Assert.Null(f.Tools.Input);
        Assert.Equal(0UL,f.Tools.ActionSerial); Assert.False(f.Tiles.Get(50,50).IsActive);
        f.AssertInventory(before);
    }

    private static void PlacementNormalSwitch(JsonElement row)
    {
        var component = row;
        using var caller = PlacementSource("PlacementNormalSwitchFullCaller1458");
        row = caller.RootElement.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "Windows" : "Linux")
            .GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("mode").GetString() == "paidNormalSwitch");
        for (int i = 0; i < row.GetProperty("steps").GetArrayLength(); i++)
            foreach (string name in new[] { "inputFrames", "actionFrames", "syncFrames" })
                Assert.Equal(component.GetProperty("steps")[i].GetProperty(name).GetRawText(), row.GetProperty("steps")[i].GetProperty(name).GetRawText());
        using var f = PlacementScene(2, 3);
        // The original companion is Main.myPlayer from its first tick, before
        // TrySyncingInput emits its first packet13. Qualify that trusted role.
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot,96,16,0,64,0,736,806,false,0,0,false,0,false,0,0,0,0,false,0,0))
            { ItemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection });
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 3;
        using var transport = new PlacementTransport(f);
        using var crit = PlacementSource("PlacementCrit" + (OperatingSystem.IsWindows() ? "Windows" : "Linux") + "1458");
        var inventoryBefore = f.CaptureInventory();
        for (int tick = 0; tick <= 28; tick++)
        {
            var step = row.GetProperty("steps")[tick];
            PlacementHitOracle(crit.RootElement[0].GetProperty("steps")[tick].GetProperty("before").GetProperty("hitTile"), f.Tools.Hits);
            PlacementHitOracle(crit.RootElement[0].GetProperty("steps")[tick].GetProperty("before").GetProperty("hitReplace"), f.Tools.ReplacementHits);
            foreach (var encoded in step.GetProperty("inputFrames").EnumerateArray())
                transport.Post(Convert.FromHexString(encoded.GetString()!));
            foreach (var bytes in PlacementActionBytes(step))
                if (bytes[2] is 17 or 125) transport.Post(bytes);
            foreach (var encoded in step.GetProperty("syncFrames").EnumerateArray())
            {
                var bytes = Convert.FromHexString(encoded.GetString()!);
                if (bytes[2] is 5 or 138) transport.Post(bytes);
            }
            f.Tick();
            Assert.True(f.Tools.LocalClocksKnown, $"normal switch tick={tick}: source local clocks retired");
            Assert.Equal((short)2, f.CaptureInventory().Items[0].Stack);
            Assert.Equal((short)2, f.CaptureInventory().Quantity[0]);
            if (tick <= 9 || tick is 15 or 28)
            {
                var clock = step.GetProperty("after").GetProperty("clock");
                Assert.Equal(clock.GetProperty("itemAnimation").GetInt32(), f.Tools.Clocks.Selected.Animation);
                Assert.Equal(clock.GetProperty("itemTime").GetInt32(), f.Tools.Clocks.Selected.ItemTime);
                Assert.Equal(clock.GetProperty("itemTimeMax").GetInt32(), f.Tools.Clocks.Selected.ItemTimeMax);
                Assert.Equal(clock.GetProperty("toolTime").GetInt32(), f.Tools.Clocks.ToolTime);
            }
            if (tick <= 9 || tick is 15 or 28)
                Assert.Equal(step.GetProperty("after").GetProperty("clock").GetProperty("revolverCritChanceBonus").GetInt32(), f.Member.ItemPhase!.Value.Selected.RevolverCritBonus);
            if (tick is 15 or 28)
                PlacementHitOracle(crit.RootElement[0].GetProperty("steps")[tick].GetProperty("after").GetProperty("hitTile"), f.Tools.Hits);
            Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))),
                $"normal switch tick={tick} RNG actual={LucyRandomCursor(CurrentLucyRandom(f))} expected={step.GetProperty("afterRandom").GetProperty("cursor").GetUInt32()}");
        }
        Assert.Equal((byte)1, f.Tools.SourceSlot);
        Assert.Equal(3UL, f.Tools.ActionSerial);
        Assert.False(f.Tiles.Get(50,50).IsActive);
        var final = f.CaptureInventory();
        for (int i = 1; i < final.Items.Length; i++)
        { Assert.Equal(inventoryBefore.Items[i], final.Items[i]); Assert.Equal(inventoryBefore.Quantity[i], final.Quantity[i]); }
        Assert.Equal(inventoryBefore.Copper, final.Copper); Assert.Equal(inventoryBefore.Caps, final.Caps);
    }

    private static void PlacementFastSwitch(JsonElement row, bool externalRandomDraw = false)
    {
        var component = row;
        using var caller = PlacementSource("PlacementNormalSwitchFullCaller1458");
        row = caller.RootElement.GetProperty("platforms").GetProperty(OperatingSystem.IsWindows() ? "Windows" : "Linux")
            .GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("mode").GetString() == "paidFastSwapSwitch");
        for (int i = 0; i < row.GetProperty("steps").GetArrayLength(); i++)
            foreach (string name in new[] { "inputFrames", "actionFrames", "syncFrames" })
                Assert.Equal(component.GetProperty("steps")[i].GetProperty(name).GetRawText(), row.GetProperty("steps")[i].GetProperty(name).GetRawText());
        using var f = PlacementScene(2, 3);
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot,96,16,0,64,0,736,806,false,0,0,false,0,false,0,0,0,0,false,0,0))
            { ItemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection });
        f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 1, 1, 0, 2774, 0)));
        f.Tiles.Set(49, 50, new WorldTile { Type = 226, Flags = WorldTileFlags.Active });
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 3;
        var inventoryBefore = f.CaptureInventory();
        using var transport = new PlacementTransport(f);
        using var crit = PlacementSource("PlacementCrit" + (OperatingSystem.IsWindows() ? "Windows" : "Linux") + "1458");
        for (int tick = 0; tick <= 13; tick++)
        {
            var step = row.GetProperty("steps")[tick];
            if (!externalRandomDraw)
            {
                var sourceBefore=crit.RootElement[1].GetProperty("steps")[tick].GetProperty("before");
                PlacementHitOracle(sourceBefore.GetProperty("hitReplace"), f.Tools.PlacementAlternative?.ReplacementHits ?? f.Tools.ReplacementHits);
                PlacementHitOracle(sourceBefore.GetProperty("hitTile"), f.Tools.Hits);
            }
            VanillaUnifiedRandom1458? refusedEndpoint = null;
            if (tick == 13 && externalRandomDraw)
            {
                CurrentLucyRandom(f).Next(100); // external entropy, not this retained source-history participant
                refusedEndpoint = CurrentLucyRandom(f).Clone();
                // Actual local CheckCrackedBrickBreak spends both speed offers
                // before ItemCheck even on this dry ordinary support. An invalid
                // alternative must not resync across these real caller samples.
                refusedEndpoint.Next(2, 12);
                refusedEndpoint.Next(2, 12);
                refusedEndpoint.Next(3); // one ordinary shared ItemCheck sample, no hidden resync
            }
            foreach (var encoded in step.GetProperty("inputFrames").EnumerateArray())
                transport.Post(Convert.FromHexString(encoded.GetString()!));
            foreach (var bytes in PlacementActionBytes(step))
                if (bytes[2] is 17 or 125 or 27) transport.Post(bytes);
            foreach (var encoded in step.GetProperty("syncFrames").EnumerateArray())
            {
                var bytes = Convert.FromHexString(encoded.GetString()!);
                if (bytes[2] is 5 or 138) transport.Post(bytes);
            }
            f.Tick();
            Assert.True(f.Tools.LocalClocksKnown, $"fast switch tick={tick}: source local clocks retired");
            Assert.Equal((short)2, f.CaptureInventory().Items[0].Stack);
            Assert.Equal((short)2, f.CaptureInventory().Quantity[0]);
            if (tick <= 9)
            {
                var clock = step.GetProperty("after").GetProperty("clock");
                Assert.Equal(clock.GetProperty("itemAnimation").GetInt32(), f.Tools.Clocks.Selected.Animation);
                Assert.Equal(clock.GetProperty("itemTime").GetInt32(), f.Tools.Clocks.Selected.ItemTime);
            }
            if (tick == 10 && !externalRandomDraw)
            {
                Assert.NotNull(f.Tools.PlacementAlternative);
                PlacementHitOracle(crit.RootElement[1].GetProperty("steps")[tick].GetProperty("after").GetProperty("hitReplace"), f.Tools.PlacementAlternative!.ReplacementHits);
            }
            if (tick == 13 && externalRandomDraw)
            {
                Assert.Equal((byte)0, f.Tools.SourceSlot);
                Assert.Equal(0, f.Events.Births);
                Assert.Equal(1UL, f.Tools.ActionSerial);
                Assert.True(CurrentLucyRandom(f).HasSameState(refusedEndpoint!));
            }
            if (tick == 13 && !externalRandomDraw)
            {
                PlacementHitOracle(crit.RootElement[1].GetProperty("steps")[tick].GetProperty("after").GetProperty("hitReplace"), f.Tools.ReplacementHits);
                PlacementHitOracle(crit.RootElement[1].GetProperty("steps")[tick].GetProperty("after").GetProperty("hitTile"), f.Tools.Hits);
                Assert.Equal(step.GetProperty("after").GetProperty("clock").GetProperty("revolverCritChanceBonus").GetInt32(), f.Member.ItemPhase!.Value.Selected.RevolverCritBonus);
                Assert.Equal((byte)1, f.Tools.SourceSlot);
                Assert.Equal(2774, f.Tools.SourceItem.Value);
                Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))),
                    $"fast chosen RNG cursor actual={LucyRandomCursor(CurrentLucyRandom(f))} expected={step.GetProperty("afterRandom").GetProperty("cursor").GetUInt32()}");
                Assert.Equal(2UL, f.Tools.ActionSerial); // paid placement and first pick attempt; birth has no world-action serial
                Assert.Equal(1, f.Events.Births);
                Assert.True(f.Tools.HeldProjectile is { } held && f.Projectiles.IsCombatTrusted(held));
            }
        }
        Assert.True(f.Tiles.Get(49, 50).IsActive); Assert.Equal((ushort)226, f.Tiles.Get(49,50).Type);
        var final = f.CaptureInventory();
        for (int i = 1; i < final.Items.Length; i++)
        { Assert.Equal(inventoryBefore.Items[i], final.Items[i]); Assert.Equal(inventoryBefore.Quantity[i], final.Quantity[i]); }
        Assert.Equal(inventoryBefore.Copper, final.Copper); Assert.Equal(inventoryBefore.Caps, final.Caps);
    }

    private static void PlacementHitOracle(JsonElement source, ToolHitState1458 actual)
    {
        Assert.Equal(source.GetProperty("buffer").GetInt32(), actual.BufferLocation);
        var entries=source.GetProperty("entries");var order=source.GetProperty("order");
        Assert.Equal(ToolHitState1458.SlotCount, entries.GetArrayLength());
        Assert.Equal(ToolHitState1458.SlotCount, order.GetArrayLength());
        for(int i=0;i<ToolHitState1458.SlotCount;i++)
        {
            var e=entries[i];
            Assert.Equal(new ToolHitEntry1458(e[0].GetInt32(),e[1].GetInt32(),e[2].GetInt32(),e[3].GetInt32(),e[4].GetInt32()),actual.GetEntry(i));
            Assert.Equal(order[i].GetInt32(),actual.GetOrder(i));
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "fundedQuantities")]
    private static extern ref short[] PlacementFunding(RuntimePlayerInventoryStore store);

    private sealed class PlacementTransport : IDisposable
    {
        private readonly Fixture fixture;
        private readonly PlacementQueue queue;
        private readonly PlayerBootstrapFrameSink bootstrap;
        private readonly ProjectileLifecycleFrameSink sink;
        internal PlacementTransport(Fixture fixture)
        {
            this.fixture = fixture; queue = new(fixture.Connection.Source);
            var source = fixture.Connection.Source;
            bootstrap = new(new PlayerSlotPool(1), new TerrariaConnectionOutboundQueue(new(32,8192,2048)),
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] {3,0,(byte)TerrariaMessageId.WorldData},
                    Array.Empty<ReadOnlyMemory<byte>>(), new byte[] {3,0,(byte)TerrariaMessageId.PlayerSpawnSelf}),
                source, new LucySpawn(), null, new RuntimePlayerEquipmentIngress(queue), new RuntimePlayerMovementIngress(queue));
            sink = new(source, bootstrap, bootstrap, new RuntimeProjectileNetworkIngress(queue));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.Hello,
                new byte[] {11,(byte)'T',(byte)'e',(byte)'r',(byte)'r',(byte)'a',(byte)'r',(byte)'i',(byte)'a',(byte)'3',(byte)'2',(byte)'6'})));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.RequestWorldData, [])));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.SpawnTileData, new byte[9])));
            Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(LucySimpleFrame(TerrariaMessageId.PlayerSpawn,
                new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength])));
            Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        }
        internal void Post(byte[] source)
        {
            var bytes = source.ToArray(); var frame = LucyFrame(bytes);
            Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(frame));
            Array.Clear(bytes); // The queued value must not borrow the released receive buffer.
            while (queue.Commands.TryDequeue(out var command)) fixture.Apply(command);
        }
        public void Dispose() => bootstrap.Dispose();
    }
    private sealed class PlacementQueue(GameCommandSourceId source) : IGameCommandIngress<RuntimeCommand>
    {
        internal readonly Queue<RuntimeCommand> Commands = new();
        public bool TryPost(GameCommandSourceId submitted, RuntimeCommand command)
        {
            if (submitted != source || Commands.Count == 64) return false;
            Commands.Enqueue(command); return true;
        }
    }
}
