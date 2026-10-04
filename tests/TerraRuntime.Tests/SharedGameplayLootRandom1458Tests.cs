using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class SharedGameplayLootRandom1458Tests
{
    public static IEnumerable<object[]> OriginalEyeCallbacks()
    {
        using var resource = typeof(SharedGameplayLootRandom1458Tests).Assembly.GetManifestResourceStream("SharedClassicGameplayRandom1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    // Independent original callbacks, executable SHA4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    // Flask Kill uses netMode2; direct NewNPC/loot calls omit broadcasts using netMode0.
    // Captured Eye callbacks pin imported/recovery/money/healing ordering; they do not prove a continuous encounter.
    [Theory]
    [MemberData(nameof(OriginalEyeCallbacks))]
    public void Composition_shares_loot_and_dedicated_flask_random(JsonElement row)
    {
        var random = new SystemWorldItemSpawnRandom(row.GetProperty("seed").GetInt32());
        var npcs = new RuntimeNpcStore();
        var items = new RuntimeWorldItemStore();
        var projectiles = new RuntimeProjectileStore();
        var runtime = new ServerRuntimeState(npcs: npcs, worldItems: items, projectiles: projectiles,
            worldItemSpawnRandom: random, worldClock: new RuntimeWorldClock(0, false, 0, 0, 1),
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with
            { TenthAnniversaryWorld = row.GetProperty("mode").GetInt32() == 4 });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(734), session.Handle);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0)));
        var spawn = new NpcStateUpdate(3, 3, 2000, 2000, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawnVanilla(in spawn, out var first));
        Assert.Equal(row.GetProperty("createdNetId").GetInt32(), first.NetId);
        var body = NpcSimulationState.Initial with
        { HitboxOverride = new(row.GetProperty("npcWidth").GetInt32(), row.GetProperty("npcHeight").GetInt32()) };
        Assert.True(npcs.TrySpawnVanilla(new(4, 4, 1000.75f, 1000.25f, 0, 0, 0, default, body), out var eye));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(runtime));
        Assert.True(graph.Npcs.TryStrikeBotPlayerMelee(session.Handle, eye.Handle, 100_000, 0, false, 0, 1));
        Assert.False(npcs.TryGet(eye.Handle, out _));
        var actual = new WorldItemSnapshot[400];
        int count = items.CopyActive(actual);
        var expected = row.GetProperty("drops");
        Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var drop = expected[i];
            Assert.Equal((drop.GetProperty("id").GetInt32(), drop.GetProperty("stack").GetInt32(), drop.GetProperty("prefix").GetInt32()),
                ((int)actual[i].ItemNetId, actual[i].Stack, (int)actual[i].Prefix));
            Assert.Equal((drop.GetProperty("x").GetSingle(), drop.GetProperty("y").GetSingle(),
                drop.GetProperty("vx").GetSingle(), drop.GetProperty("vy").GetSingle()),
                (actual[i].PositionX, actual[i].PositionY, actual[i].VelocityX, actual[i].VelocityY));
        }
        Assert.True(projectiles.TrySpawnVanilla(new(new ProjectileTypeId(501), 255, 3000, 3100,
            0, 0, default, 0, 37, 2.5f, 15), out var shot));
        var queue = Assert.IsType<RuntimeProjectileExplosionQueue>(typeof(ProjectileAuthority).GetField("explosions", flags)!.GetValue(graph.Projectiles));
        var termination = new ProjectileTerminationCommit(shot, shot,
            ProjectileSimulationTerminationReason.LifetimeExpired, false, default, first.Handle);
        queue.ProjectileTerminated(in termination);
        Assert.Equal(1, queue.Events.Length);
        spawn = spawn with { PositionX = 2200, PositionY = 2200 };
        Assert.True(npcs.TrySpawnVanilla(in spawn, out var last));
        Assert.Equal(row.GetProperty("afterNetId").GetInt32(), last.NetId);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }
}
