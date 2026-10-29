using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCombatPlayerSubupdates1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string resource in new[] { "GroundCombatPlayerSubupdatesLinux1458.v2", "GroundCombatPlayerBoundariesLinux1458.v1" })
        {
            using var input = typeof(GroundCombatPlayerSubupdates1458Tests).Assembly.GetManifestResourceStream(
                "TerraRuntime.Tests.Fixtures." + resource + ".json.gz")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.Clone()];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void Whole_projectile_authority_contacts_each_source_subupdate_before_final_position(JsonElement row)
    {
        using var f = new Fixture(row);
        Assert.True(f.Graph.Projectiles.TryTickState());
        f.Graph.ProjectilePlayerCombat.Tick(f.Graph.Projectiles.PendingExplosions);
        Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var player));
        var expected = row.GetProperty("afterUpdate");
        Assert.Equal(expected.GetProperty("player").GetProperty("player").GetProperty("statLife").GetInt32(), player.Life);
        Assert.Equal(expected.GetProperty("player").GetProperty("player").GetProperty("dead").GetBoolean(), player.IsDead);
        var state = expected.GetProperty("projectile");
        bool active = state.GetProperty("active").GetBoolean();
        Assert.Equal(active, f.Shots.TryGet(f.Shot.Handle, out var shot));
        if (!active) shot = Assert.Single(f.Terminations.Events).FinalProjectile;
        else Assert.Empty(f.Terminations.Events);
        Assert.Equal(F(state, "position", 0), shot.PositionX);
        Assert.Equal(F(state, "position", 1), shot.PositionY);
        Assert.Equal(F(state, "velocity", 0), shot.VelocityX);
        Assert.Equal(F(state, "velocity", 1), shot.VelocityY);
        Assert.Equal(state.GetProperty("ai")[0].GetSingle(), shot.Ai.Ai0);
        Assert.Equal(state.GetProperty("ai")[1].GetSingle(), shot.Ai.Ai1);
        Assert.Equal(state.GetProperty("ai")[2].GetSingle(), shot.Ai.Ai2);
        if (active)
        {
            Assert.True(f.Shots.TryGetLifecycle(shot.Handle, out var life));
            Assert.Equal(state.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
        }
        else if (row.GetProperty("type").GetInt32() is 438 or 592 &&
            row.GetProperty("scene").GetString() is { } scene && scene.StartsWith("ttl"))
        {
            var death = Assert.Single(f.Terminations.Events);
            string originField = int.Parse(scene.Substring(3, 1)) < 4 ? "oldPosition" : "position";
            Assert.Equal(new ProjectileKillOrigin(F(state, originField, 0), F(state, originField, 1)), death.KillOrigin);
        }
        if (!row.GetProperty("scene").GetString()!.StartsWith("lethal"))
        {
            Assert.Equal(F(expected.GetProperty("player").GetProperty("player"), "velocity", 0), player.VelocityX);
            Assert.Equal(F(expected.GetProperty("player").GetProperty("player"), "velocity", 1), player.VelocityY);
        }
        Assert.Equal(Duration(expected.GetProperty("player").GetProperty("player"), 31),
            f.Graph.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(31)));
        if (row.GetProperty("scene").GetString()!.StartsWith("lethal")) return; // existing death entropy is explicitly unqualified

        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var rng = expected.GetProperty("player").GetProperty("rng");
        Assert.Equal(rng.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(f.Random)!);
        Assert.Equal(rng.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(f.Random)!);
    }

    private static int Duration(JsonElement player, int type)
    {
        var types = player.GetProperty("buffType"); var times = player.GetProperty("buffTime");
        for (int i = 0; i < types.GetArrayLength(); i++) if (types[i].GetInt32() == type) return times[i].GetInt32();
        return 0;
    }

    private static float F(JsonElement value, string name, int index) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetProperty(name)[index].GetString(), 16));

    internal sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        internal readonly ServerRuntimeState World;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly RuntimeProjectileStore Shots = new(2);
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ConnectionHandle Connection;
        internal readonly ProjectileSnapshot Shot;
        internal readonly WorldTileStore Tiles;
        internal readonly TerminationCapture Terminations = new();

        internal Fixture(JsonElement row, EventSink? events = null)
        {
            Random = new(row.GetProperty("seed").GetInt32());
            Tiles = new WorldTileStore(new WorldDimensions(400, 400));
            for (int x = 0; x < 400; x++) Tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            if (row.GetProperty("scene").GetString() == "collision")
                for (int y = 60; y < 66; y++) Tiles.Set(41, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            World = new(playerEvents: events, worldTiles: Tiles, projectiles: Shots,
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                incomingHumanCombatPolicy: IncomingHumanCombatPolicy1458.EquipmentComponent);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            Graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", flags)!.GetValue(World)!;
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(23), session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            World.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            World.Apply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 59, 0, 0, 0, 0)));
            var before = row.GetProperty("before");
            var player = before.GetProperty("player").GetProperty("player");
            World.Apply(new PlayerHealthRuntimeCommand(Connection,
                new(session.Slot, checked((short)player.GetProperty("statLife").GetInt32()), 100)));
            if (before.GetProperty("godMode").GetBoolean())
            {
                var completion = new TaskCompletionSource<bool>();
                World.Apply(new SetPlayerGodModeRuntimeCommand(Connection.Player, true, completion));
                Assert.True(completion.Task.GetAwaiter().GetResult());
            }
            World.Apply(new PlayerMovementRuntimeCommand(Connection, new(session.Slot, 0, 0, 0, 0, 0,
                F(player, "position", 0), F(player, "position", 1), false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            var shot = before.GetProperty("projectile");
            var ai = shot.GetProperty("ai");
            var state = new ProjectileStateUpdate(new ProjectileTypeId(row.GetProperty("type").GetInt32()), 255,
                F(shot, "position", 0), F(shot, "position", 1), F(shot, "velocity", 0), F(shot, "velocity", 1),
                new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle()), 0,
                checked((short)shot.GetProperty("damage").GetInt32()), 0, 0);
            Assert.True(Shots.TrySpawnVanilla(in state, shot.GetProperty("timeLeft").GetInt32(), out Shot));
            Assert.True(Shots.TrySetServerNpcSource(Shot.Handle, new NpcHandle(3, new NpcGeneration(1))));
            // Passive observation on the existing termination fan-out. No substitute motion,
            // damage, source-offer or RNG implementation is installed.
            var effects = Graph.Projectiles.TerminationEffects;
            effects.GetType().GetField("falling", flags)!.SetValue(effects, Terminations);
        }

        public void Dispose() => session.Dispose();
    }

    internal sealed class TerminationCapture : IProjectileTerminationCommitSink
    {
        internal readonly List<ProjectileTerminationCommit> Events = [];
        public void ProjectileTerminated(in ProjectileTerminationCommit termination) => Events.Add(termination);
    }

    internal sealed class EventSink : IRuntimePlayerEventSink
    {
        internal Action? Health = null;
        internal Action? Avoided = null;
        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) => Health?.Invoke();
        public void PlayerDamageAvoided(PlayerHandle player, float x, float y, string text) => Avoided?.Invoke();
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
