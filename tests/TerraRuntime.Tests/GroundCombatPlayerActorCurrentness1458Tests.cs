using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Tests;

public sealed class GroundCombatPlayerActorCurrentness1458Tests
{
    private static JsonElement Row(int type, string scene, int seed = 1458) =>
        GroundCombatPlayerSubupdates1458Tests.Cases().Select(x => (JsonElement)x[0]).Single(row =>
            row.GetProperty("type").GetInt32() == type && row.GetProperty("scene").GetString() == scene &&
            row.GetProperty("seed").GetInt32() == seed);

    [Theory]
    [InlineData("none")]
    [InlineData("player")]
    [InlineData("terrain")]
    [InlineData("random")]
    [InlineData("buff")]
    [InlineData("clock")]
    public void Last_clock_callback_cannot_escape_joint_owner_guards_or_reenter_final_position_damage(string mutation)
    {
        using var f = new GroundCombatPlayerSubupdates1458Tests.Fixture(Row(438, "cross"));
        var expectedRandom = f.Random.Clone();
        int reads = 0;
        typeof(RuntimeProjectilePlayerCombatPass).GetField("tickProvider", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.Graph.ProjectilePlayerCombat, (Func<long>)(() => {
                if (++reads == 2)
                {
                    if (mutation == "player") f.World.Apply(new PlayerMovementRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, 0, 0, 0, 0, 0, 1250, 1000, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
                    if (mutation == "terrain") f.Tiles.Set(41, 62, new TerraRuntime.World.WorldTile { Type = 1, Flags = TerraRuntime.World.WorldTileFlags.Active });
                    if (mutation == "random") f.Random.Next();
                    if (mutation == "buff") f.World.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                        new(f.Connection.Player.Slot, new BuffTypeId[] { new(31) })));
                    expectedRandom = f.Random.Clone(); // retain the actual external write, never restore entropy
                    if (mutation == "clock") return 1;
                }
                return 0;
            }));
        Assert.True(f.Graph.Projectiles.TryTickState());
        Assert.Equal(2, reads);
        f.Graph.ProjectilePlayerCombat.Tick([]);
        Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var player));
        Assert.True(f.Shots.TryGet(f.Shot.Handle, out var shot));
        if (mutation == "none")
        {
            Assert.Equal(42, player.Life);
            Assert.Equal(688f, shot.PositionX);
            Assert.False(expectedRandom.HasSameState(f.Random));
        }
        else
        {
            Assert.Equal(100, player.Life);
            Assert.Equal(f.Shot, shot);
            Assert.True(expectedRandom.HasSameState(f.Random));
            Assert.Equal(1, f.Graph.Projectiles.LastTick.Rejected);
        }
    }

    [Fact]
    public void Godmode_observer_newborn_waits_for_next_actor_census_without_an_extra_final_position_roll()
    {
        var row = Row(438, "god-overlap");
        var events = new GroundCombatPlayerSubupdates1458Tests.EventSink();
        using var f = new GroundCombatPlayerSubupdates1458Tests.Fixture(row, events);
        ProjectileSnapshot newborn = default;
        int observations = 0;
        events.Avoided = () => {
            events.Avoided = null;
            observations++;
            Assert.True(f.Shots.TryGet(f.Shot.Handle, out var accepted));
            Assert.Equal(644f, accepted.PositionX);
            Assert.Equal(4, f.Graph.ProjectilePlayerCombat.HostileGodModeAvoidances);
            Assert.True(f.Shots.TryDespawn(accepted.Handle, out _));
            var state = new ProjectileStateUpdate(accepted.Type, 255, accepted.PositionX, accepted.PositionY,
                accepted.VelocityX, accepted.VelocityY, accepted.Ai, 0, 30, 0, 0);
            Assert.True(f.Shots.TrySpawn(accepted.Handle.Slot, in state, out newborn));
            Assert.True(f.Shots.TrySetServerNpcSource(newborn.Handle, new NpcHandle(3, new NpcGeneration(1))));
            Assert.False(f.Graph.Projectiles.TryTickState());
            f.Graph.ProjectilePlayerCombat.Tick([]);
        };
        Assert.True(f.Graph.Projectiles.TryTickState());
        f.Graph.ProjectilePlayerCombat.Tick([]);
        Assert.Equal(1, observations);
        Assert.NotEqual(f.Shot.Handle.Generation, newborn.Handle.Generation);
        Assert.True(f.Shots.TryGet(newborn.Handle, out var retained));
        Assert.Equal(newborn, retained);
        var sourceRng = row.GetProperty("afterUpdate").GetProperty("player").GetProperty("rng");
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.Equal(sourceRng.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!.GetValue(f.Random)!);
        Assert.Equal(sourceRng.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!.GetValue(f.Random)!);
        var checkpoint = f.Random.Clone();
        Assert.True(f.Graph.Projectiles.TryTickState());
        Assert.True(f.Shots.TryGet(newborn.Handle, out retained));
        Assert.Equal(648f, retained.PositionX);
        Assert.False(checkpoint.HasSameState(f.Random));
    }
}
