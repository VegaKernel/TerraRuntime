using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Tests;

public sealed partial class GroundCombatPlayerHit1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string name in new[] { "GroundCombatPlayerHitLinux1458.v1", "GroundCombatPlayerHitModesLinux1458.v2" })
        {
            using var stream = typeof(GroundCombatPlayerHit1458Tests).Assembly.GetManifestResourceStream(
                "TerraRuntime.Tests.Fixtures." + name + ".json.gz")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
                yield return [row.Clone()];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_hostile_pass_matches_original_hit_status_body_and_retirement(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32(), seed = row.GetProperty("seed").GetInt32();
        string scene = row.GetProperty("scene").GetString()!;
        var before = row.GetProperty("before");
        int mode = row.TryGetProperty("mode", out var modeValue) ? modeValue.GetInt32() : 0;
        bool goodWorld = row.TryGetProperty("goodWorld", out var goodValue) && goodValue.GetBoolean();
        using var f = new Fixture(type, seed, scene, mode: mode, goodWorld: goodWorld,
            damage: before.GetProperty("projectile").GetProperty("damage").GetInt32());
        Assert.Equal(before.GetProperty("player").GetProperty("player").GetProperty("statLifeMax2").GetInt32(),
            f.InitialPlayer.DerivedLifeMax);
        Assert.Equal(F(before.GetProperty("projectile"), "position", 0), f.Shot.PositionX);
        Assert.Equal(F(before.GetProperty("projectile"), "position", 1), f.Shot.PositionY);
        f.Pass.Tick([]);
        var expected = row.GetProperty("afterCaller");
        var player = expected.GetProperty("player").GetProperty("player");
        if (scene != "inactive")
        {
            Assert.True(f.Players.TryCapture(f.Connection.Player, out var actual));
            Assert.Equal(player.GetProperty("statLife").GetInt32(), actual.Life);
            Assert.Equal(player.GetProperty("dead").GetBoolean(), actual.IsDead);
            Assert.Equal(Duration(player, 31), f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(31)));
        }
        else Assert.False(f.Players.TryCapture(f.Connection.Player, out _));
        Assert.Equal(expected.GetProperty("projectile").GetProperty("active").GetBoolean(),
            f.Store.TryGet(f.Shot.Handle, out _));
        // The independently observed original lethal KillMe/tombstone entropy is an existing,
        // explicitly unqualified player-death tail. HP/death/no-status and projectile debit remain checked.
        if (scene != "lethal") AssertRandom(expected.GetProperty("player").GetProperty("rng"), f.Random);
    }

    [Fact]
    public void Status_is_owned_before_health_observer_and_reentrant_replacement_gets_no_old_effect()
    {
        // Original seed0/default100 corpus selects the source >=2 branch after blood arguments.
        using var f = new Fixture(436, 0, "hit");
        int seen = 0;
        f.Events.OnHealth = () =>
        {
            seen++;
            Assert.Equal(300, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(31)));
            f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection));
        };
        f.Pass.Tick([]);
        Assert.Equal(1, seen);
        Assert.Empty(f.Events.Published);
        Assert.False(f.Store.TryGet(f.Shot.Handle, out _));
        var after = f.Random.Clone();
        f.Pass.Tick([]);
        Assert.True(after.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(0)] // absent shared entropy capability
    [InlineData(1)] // absent source Kill sink
    [InlineData(2)] // unowned derived maximum
    [InlineData(3)] // unowned vanity work
    [InlineData(4)] // actual Chicken voice in a functional accessory slot
    [InlineData(5)] // actual Chicken voice in a vanity accessory slot
    public void Missing_required_context_refuses_before_hp_entropy_and_penetration(int kind)
    {
        using var f = new Fixture(436, 1458, "hit", missingRandom: kind == 0, missingSink: kind == 1);
        if (kind == 2) f.Member.DerivedLifeMax = null;
        if (kind is 3 or 4 or 5) f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection,
            new(f.Connection.Player.Slot,
                kind == 4 ? (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3) : (short)(VanillaPlayerItemSlotCatalog.VanityArmorStart + 3),
                1, 0, (short)(kind == 3 ? 1613 : 5485), 0)));
        var before = f.Random.Clone();
        f.Pass.Tick([]);
        Assert.Equal(100, f.Member.Life);
        Assert.True(before.HasSameState(f.Random));
        Assert.True(f.Store.TryGet(f.Shot.Handle, out var retained));
        Assert.Equal(f.Shot, retained);
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(31)));
    }

    private static int Duration(JsonElement p, int type)
    {
        var types = p.GetProperty("buffType"); var times = p.GetProperty("buffTime");
        for (int i = 0; i < types.GetArrayLength(); i++) if (types[i].GetInt32() == type) return times[i].GetInt32();
        return 0;
    }

    private static float F(JsonElement value, string name, int index) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetProperty(name)[index].GetString(), 16));

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(actual)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(actual)!);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        internal readonly Events Events = new();
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Store = new(2);
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeProjectilePlayerCombatPass Pass;
        internal readonly ConnectionHandle Connection;
        internal readonly ProjectileSnapshot Shot;
        internal readonly PlayerStateSnapshot InitialPlayer;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }

        internal Fixture(int type, int seed, string scene, bool missingRandom = false, bool missingSink = false,
            int mode = 0, bool goodWorld = false, int damage = 30)
        {
            Random = new(seed);
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(23), session.Handle);
            Players = new(Events, null, expertMode: mode >= 1, masterMode: mode >= 2);
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new(session.Slot, VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0, 0, 0)));
            if (scene == "immune-buff") Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new(session.Slot, (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), 1, 0, 1613, 0)));
            if (scene == "immune")
            {
                Assert.Equal(PlayerDamageCommitResult.Committed, Players.TryCommitAuthoritativeNpcContactDamage(
                    0, new NpcHandle(1, new NpcGeneration(1)), Connection.Player, 1, 0,
                    VanillaPlayerImmunityChannel1458.General, out _));
                Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            }
            if (scene == "god")
            {
                var completion = new TaskCompletionSource<bool>();
                Players.TryApply(new SetPlayerGodModeRuntimeCommand(Connection.Player, true, completion));
                Assert.True(completion.Task.GetAwaiter().GetResult());
            }
            if (scene == "lethal") Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 1, 100)));
            if (scene == "dead")
                Assert.Equal(PlayerDamageCommitResult.Committed, Players.TryCommitAuthoritativeNpcContactDamage(
                    0, new NpcHandle(1, new NpcGeneration(1)), Connection.Player, 1000, 0,
                    VanillaPlayerImmunityChannel1458.General, out _));
            Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(new ProjectileTypeId((ushort)type), out var definition));
            float sx = scene == "miss" ? 2000 : scene == "body-edge" ? 1000 - definition.Width + 1 : scene == "fractional-miss" ? 1000.75f : 1000;
            float px = scene == "fractional-miss" ? 1000 + definition.Width + .5f : 1000;
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
                new(session.Slot, 0, 0, 0, 0, 0, px, 1000, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            var update = new ProjectileStateUpdate(new ProjectileTypeId((ushort)type), 255, sx, 1000, 0, 0,
                default, 0, (short)(scene == "zero" ? 0 : damage), 0, (short)damage);
            Assert.True(Store.TrySpawn(0, in update, out Shot));
            Assert.True(Store.TrySetServerNpcSource(Shot.Handle, new NpcHandle(3, new NpcGeneration(1))));
            var sink = new RuntimeProjectileTerminationEffectSink(new RuntimeProjectileExplosionQueue(2, Random),
                new RuntimeProjectileTileExplosionQueue(2), new RuntimeProjectileChildSpawnQueue(2), random: Random);
            Pass = new(Store, new RuntimeNpcStore(), Players, () => 1,
                sourceRandom: missingRandom ? null : Random, terminationEffects: missingSink ? null : sink,
                expertMode: mode >= 1, masterMode: mode >= 2, goodWorld: goodWorld);
            Assert.True(Players.TryCapture(Connection.Player, out InitialPlayer));
            if (scene == "inactive") Players.TryApply(new PlayerDisconnectRuntimeCommand(Connection));
        }

        public void Dispose() => session.Dispose();
    }

    private sealed class Events : IRuntimePlayerEventSink
    {
        internal Action? OnHealth;
        internal readonly List<BuffTypeId> Published = [];
        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) => OnHealth?.Invoke();
        public void PlayerNpcContactBuffApplied(PlayerHandle player, BuffTypeId type, int ticks) => Published.Add(type);
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
