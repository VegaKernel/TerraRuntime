using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemotePlayerWorldPhysics1458Tests
{
    [Fact]
    public void Actual_remote_Player_Update_world_motion_matches_before_ItemCheck_without_physics_RNG()
    {
        using Stream stream = typeof(RemotePlayerWorldPhysics1458Tests).Assembly.GetManifestResourceStream("RemotePlayerWorldPhysics1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            document.RootElement.GetProperty("sourceSha256").GetString());
        Assert.Equal(12, document.RootElement.GetProperty("rows").GetArrayLength());
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            Assert.Equal(2, row.GetProperty("netMode").GetInt32());
            Assert.Equal(255, row.GetProperty("myPlayer").GetInt32());
            var w = row.GetProperty("world");
            var world = new RuntimePlayerUpdateWorld1458(w.GetProperty("maxTilesX").GetInt32(),
                w.GetProperty("maxTilesY").GetInt32(), w.GetProperty("worldSurface").GetDouble(),
                w.GetProperty("remixWorld").GetBoolean(), w.GetProperty("skyblockWorld").GetBoolean());
            var tiles = new WorldTileStore(new WorldDimensions(world.MaxTilesX, world.MaxTilesY));
            for (int x = 0; x < world.MaxTilesX; x++)
                tiles.Set(x, w.GetProperty("floor").GetInt32(), new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);
            var jump = new VanillaServerPlayerJumpState(0, false, 0);
            var contacts = default(VanillaLiquidContactState);
            string? mode = row.GetProperty("mode").GetString();
            for (int tick = 0; tick < 3; tick++)
            {
                JsonElement before = Event(row, tick, "UpdateManaRegen:before");
                JsonElement manaAfter = Event(row, tick, "UpdateManaRegen:after");
                JsonElement expected = Event(row, tick, "ItemCheck:before");
                Assert.Equal(20, before.GetProperty("width").GetInt32());
                Assert.Equal(42, before.GetProperty("height").GetInt32());
                Assert.Equal(jump.RemainingTicks, before.GetProperty("jump").GetInt32());
                Assert.Equal(jump.ReleaseReady, before.GetProperty("releaseJump").GetBoolean());
                Assert.Equal(manaAfter.GetProperty("state").GetRawText(), expected.GetProperty("state").GetRawText());
                var player = Player(Point(before, "position", "X"), Point(before, "position", "Y"),
                    Point(before, "velocity", "X"), Point(before, "velocity", "Y"));
                Assert.True(stepper.TryStep(in player,
                    mode == "right" ? ServerPlayerHorizontalIntent.Right : ServerPlayerHorizontalIntent.Stop,
                    mode == "jump" ? ServerPlayerJumpIntent.Held : ServerPlayerJumpIntent.Released,
                    in jump, in contacts, in world, out var actual, out var nextJump), $"{mode}/{tick}");
                Assert.Equal(before.GetProperty("gravity").GetSingle(),
                    VanillaServerPlayerPhysicsProfile.Resolve(in contacts, in world, player.PositionY).Gravity);
                Assert.Equal(Point(expected, "position", "X"), actual.PositionX);
                Assert.Equal(Point(expected, "position", "Y"), actual.PositionY);
                Assert.Equal(Point(expected, "velocity", "X"), actual.VelocityX);
                Assert.Equal(Point(expected, "velocity", "Y"), actual.VelocityY);
                Assert.Equal(expected.GetProperty("jump").GetInt32(), nextJump.RemainingTicks);
                Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), nextJump.ReleaseReady);
                jump = nextJump; contacts = actual.LiquidContacts;
            }
        }
    }

    [Fact]
    public void Unknown_world_and_selected_unowned_border_death_refuse_without_replacing_the_baseline_component()
    {
        var tiles = new WorldTileStore(new WorldDimensions(400, 300));
        var stepper = new VanillaServerPlayerDryPhysicsStepper(tiles);
        var player = Player(800f, 1500f);
        var jump = new VanillaServerPlayerJumpState(0, false, 0);
        var contacts = default(VanillaLiquidContactState);
        var world = new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false);
        foreach (var invalid in new[] { world with { WorldSurface = double.NaN }, world with { WorldSurface = 0 }, world with { MaxTilesX = 401 } })
            Assert.False(stepper.TryStep(in player, ServerPlayerHorizontalIntent.Stop, ServerPlayerJumpIntent.Released,
                in jump, in contacts, in invalid, out _, out _));
        var top = player with { PositionY = 500f };
        var remix = world with { RemixWorld = true };
        Assert.False(stepper.TryStep(in top, ServerPlayerHorizontalIntent.Stop, ServerPlayerJumpIntent.Released,
            in jump, in contacts, in remix, out _, out _));
        var bottom = player with { PositionY = 4200f };
        var skyblock = world with { SkyblockWorld = true };
        Assert.False(stepper.TryStep(in bottom, ServerPlayerHorizontalIntent.Stop, ServerPlayerJumpIntent.Released,
            in jump, in contacts, in skyblock, out _, out _));
        Assert.Equal(500f, top.PositionY); Assert.Equal(4200f, bottom.PositionY);
        var tinyTiles = new WorldTileStore(new WorldDimensions(100, 160));
        var baseline = new VanillaServerPlayerDryPhysicsStepper(tinyTiles);
        var configured = Player(800f, 438f);
        Assert.True(baseline.TryStep(in configured, out var oldComponent));
        Assert.Equal(438.4f, oldComponent.PositionY);
        Assert.Equal(.4f, oldComponent.VelocityY);
    }

    private static float Point(JsonElement value, string field, string axis) => value.GetProperty(field).GetProperty(axis).GetSingle();
    private static JsonElement Event(JsonElement row, int tick, string phase) => row.GetProperty("sourcePhaseEvents").EnumerateArray()
        .Single(value => value.GetProperty("tick").GetInt32() == tick && value.GetProperty("phase").GetString() == phase);
    private static PlayerStateSnapshot Player(float x, float y, float vx = 0, float vy = 0) => default(PlayerStateSnapshot) with
    {
        Player = new(new PlayerSlotId(0), new PlayerSessionGeneration(1)), Revision = new(1),
        PositionX = x, PositionY = y, VelocityX = vx, VelocityY = vy
    };
}
