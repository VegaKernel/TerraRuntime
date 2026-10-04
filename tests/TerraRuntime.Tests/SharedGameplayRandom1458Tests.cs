using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class SharedGameplayRandom1458Tests
{
    // Actual original GoodWorld NewNPC -> WorldGen.KillTile -> Flask.Kill -> NewNPC callbacks.
    // Binary4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Kill uses netMode2 (server Gore early return); other direct callbacks use netMode0 to omit broadcasts.
    // This pins admitted callback ordering, not full ticks, weather or player combat.
    [Theory]
    [InlineData(0, 614, 614, 1.9f, -2.1000001f, 675580731)]
    [InlineData(1, 46, 614, -2.4f, -2.9f, 1507096884)]
    [InlineData(2, 614, 614, -0.6f, -3.6000001f, 191129390)]
    [InlineData(1458, 614, 46, 0.7f, -1.8000001f, 1845354897)]
    public void Default_composition_shares_spawn_tile_and_flask_stream(int seed, int firstType, int lastType,
        float vx, float vy, int next)
    {
        var random = new SystemWorldItemSpawnRandom(seed);
        var npcs = new RuntimeNpcStore();
        var projectiles = new RuntimeProjectileStore();
        var runtime = new ServerRuntimeState(npcs: npcs, projectiles: projectiles,
            worldItemSpawnRandom: random,
            worldClock: new RuntimeWorldClock(0, false, 0, 0, 1, getGoodWorld: true),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { GoodWorld = true });
        var spawn = new NpcStateUpdate(46, 46, 2000, 2000, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in spawn, out var first));
        Assert.Equal(firstType, first.Type);
        var item = VanillaSimpleTileBreakResolver1458.MaterializeItemState(new(2), 1, 10, 10, random);
        Assert.Equal((160f, 160f, vx, vy), (item.PositionX, item.PositionY, item.VelocityX, item.VelocityY));
        Assert.True(projectiles.TrySpawnVanilla(new(new ProjectileTypeId(501), 255, 3000, 3100,
            0, 0, default, 0, 37, 2.5f, 15), out var shot));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(runtime));
        var queue = Assert.IsType<RuntimeProjectileExplosionQueue>(typeof(ProjectileAuthority).GetField("explosions", flags)!.GetValue(graph.Projectiles));
        var termination = new ProjectileTerminationCommit(shot, shot,
            ProjectileSimulationTerminationReason.LifetimeExpired, false, default, first.Handle);
        queue.ProjectileTerminated(in termination);
        Assert.Equal(1, queue.Events.Length);
        spawn = spawn with { PositionX = 2200, PositionY = 2200 };
        Assert.True(npcs.TrySpawnVanilla(in spawn, out var last));
        Assert.Equal(lastType, last.Type);
        Assert.Equal(next, random.SourceRandom.Next());
    }
}
