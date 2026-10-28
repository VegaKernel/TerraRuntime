using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.ToolPlayer;

internal static partial class ToolPlayerBoundaryChecks
{
    internal static void CurrentFrameResetPaths()
    {
        string platform = OperatingSystem.IsWindows() ? "Windows" : "Linux";
        using var stream = typeof(ToolPlayerBoundaryChecks).Assembly.GetManifestResourceStream("ToolFrameReset"+platform+"1458");
        Assert.NotNull(stream);
        using var unzip = new GZipStream(stream!, CompressionMode.Decompress);
        using var source = JsonDocument.Parse(unzip);
        Assert.Equal(12, source.RootElement.GetArrayLength());
        using var caller = PlacementSource("ToolFrameResetFullCaller1458");
        var rows = caller.RootElement.GetProperty("platforms").GetProperty(platform).GetProperty("rows");
        Assert.Equal(12, rows.GetArrayLength());
        foreach (var component in source.RootElement.EnumerateArray())
        {
            var row = rows.EnumerateArray().Single(r => r.GetProperty("mode").GetString() == component.GetProperty("mode").GetString());
            Assert.Equal(component.GetProperty("steps").GetArrayLength(), row.GetProperty("steps").GetArrayLength());
            for (int i = 0; i < row.GetProperty("steps").GetArrayLength(); i++)
                foreach (string name in new[] { "inputFrames", "actionFrames", "syncFrames" })
                    Assert.Equal(component.GetProperty("steps")[i].GetProperty(name).GetRawText(), row.GetProperty("steps")[i].GetProperty(name).GetRawText());
            // Preserve the earlier component proof; whole owned ticks additionally
            // execute the source local heat/tile caller prefix before ItemCheck.
            CurrentFrameResetRow(row);
        }
    }

    private static void CurrentFrameResetRow(JsonElement row)
    {
        string mode = row.GetProperty("mode").GetString()!;
        ushort type = ushort.Parse(mode.AsSpan(4));
        short item = row.GetProperty("item").GetInt16();
        using var f = PlacementScene(item, 1);
        var tile = new WorldTile { Type = type, Flags = WorldTileFlags.Active };
        if (mode.StartsWith("half", StringComparison.Ordinal)) tile.TrySetSourceShape(true, 0);
        f.Tiles.Set(50, 50, tile);
        using var transport = new PlacementTransport(f);
        transport.Post([3, 0, 138]); // finish configured character stock import, before measured use.
        f.Apply(new PlayerMovementRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot,64,16,0,64,0,736,806,false,0,0,false,0,false,0,0,0,0,false,0,0))
            { ItemUseCaller = RuntimeItemUseCaller1458.LocalOwnerProjection });
        PlacementFunding(Inventory(f.Players))[f.Connection.Player.Slot.Value * 59] = 1;
        var inventory = f.CaptureInventory();
        int actionCount = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var actions = PlacementActionBytes(step).Where(p => p[2] is 17 or 125).ToArray();
            if (actions.Length > 0)
                f.PlacementWorldRandom.CopyStateFrom(ReadLucyRandom(step.GetProperty("serverRngBefore")));
            foreach (var frame in step.GetProperty("inputFrames").EnumerateArray())
                transport.Post(Convert.FromHexString(frame.GetString()!));
            foreach (var frame in actions) transport.Post(frame);
            f.Tick();
            f.AssertInventory(inventory);
            var expected = step.GetProperty("after");
            var clock = expected.GetProperty("clock");
            Assert.Equal(clock.GetProperty("itemTime").GetInt32(), f.Tools.Clocks.Selected.ItemTime);
            Assert.Equal(clock.GetProperty("itemTimeMax").GetInt32(), f.Tools.Clocks.Selected.ItemTimeMax);
            Assert.Equal(clock.GetProperty("itemAnimation").GetInt32(), f.Tools.Clocks.Selected.Animation);
            Assert.Equal(clock.GetProperty("itemAnimationMax").GetInt32(), f.Tools.Clocks.Selected.AnimationMax);
            Assert.Equal(clock.GetProperty("toolTime").GetInt32(), f.Tools.Clocks.ToolTime);
            PlacementHitOracle(expected.GetProperty("hitTile"), f.Tools.Hits);
            PlacementHitOracle(expected.GetProperty("hitReplace"), f.Tools.ReplacementHits);
            Assert.True(CurrentLucyRandom(f).HasSameState(ReadLucyRandom(step.GetProperty("afterRandom"))),
                $"local role {mode} tick={step.GetProperty("tick")}: actual={LucyRandomCursor(CurrentLucyRandom(f))} expected={step.GetProperty("afterRandom").GetProperty("cursor")}");
            if (actions.Length == 0) continue;
            actionCount++;
            Assert.Equal((ulong)actionCount, f.Tools.ActionSerial);
            var serverExpected = ReadLucyRandom(step.GetProperty("serverRngAfter"));
            // The tool action is followed by one separate natural-NPC scheduler
            // sample in this configured State fixture. It is outside the
            // original server17 component and is accounted explicitly here.
            serverExpected.Next();
            Assert.True(f.PlacementWorldRandom.HasSameState(serverExpected),
                $"server role {mode} tick={step.GetProperty("tick")}: actual={LucyRandomCursor(f.PlacementWorldRandom)} expected={LucyRandomCursor(serverExpected)}");
            var sourceTile = step.GetProperty("serverAfter").GetProperty("cells").EnumerateArray()
                .Single(c => c.GetProperty("x").GetInt32() == 50 && c.GetProperty("y").GetInt32() == 50);
            var after = f.Tiles.Get(50, 50);
            Assert.True(after.IsActive); Assert.Equal(type, after.Type);
            Assert.Equal(sourceTile.GetProperty("half").GetBoolean(), after.IsHalfBrick);
            Assert.Equal(sourceTile.GetProperty("slope").GetInt32(), after.SourceSlope);
            Assert.Equal(sourceTile.GetProperty("wall").GetInt32(), after.Wall);
            Assert.Equal(sourceTile.GetProperty("liquid").GetInt32(), after.LiquidAmount);
        }
        Assert.Equal(mode.StartsWith("pick", StringComparison.Ordinal) ? 2 : 1, actionCount);
    }
}

public sealed class OwnedToolFrameReset1458Tests
{
    [Fact]
    public void Original_pound_slope_and_failed_pick_keep_current_tool_reset_rng()
        => ToolPlayerBoundaryChecks.CurrentFrameResetPaths();
}
