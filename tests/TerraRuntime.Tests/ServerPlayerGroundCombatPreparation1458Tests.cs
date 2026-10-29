using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerGroundCombatPreparation1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using var stream = typeof(ServerPlayerGroundCombatPreparation1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.GroundCombatPlayerHitLinux1458.v1.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("scene").GetString() is "hit" or "god" or "lethal" or "immune-buff")
                yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void Server_owned_detached_hp_and_status_match_original_EVP_component(JsonElement row)
    {
        string scene = row.GetProperty("scene").GetString()!;
        using var f = new Fixture(row.GetProperty("type").GetInt32(), scene);
        var before = f.Current();
        var token = f.Prepare();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
        Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out bool contact));
        Assert.True(contact);
        Assert.Equal(before, f.Current());
        Assert.Empty(f.Events.Log);
        Assert.True(token.TryAdoptUnpublished());
        Assert.Empty(f.Events.Log);
        var expected = row.GetProperty("afterEvp").GetProperty("player");
        var player = expected.GetProperty("player");
        Assert.Equal(player.GetProperty("statLife").GetInt32(), f.Current().Life);
        Assert.Equal(player.GetProperty("dead").GetBoolean(), f.Current().IsDead);
        Assert.Equal(Duration(player, 31), f.Owner.GetNpcContactBuffDuration(f.Player, new(31)));
        // HP/death/no-status remain functional; complete KillMe/tombstone entropy
        // is the existing unqualified death tail, not fabricated by this token.
        if (scene != "lethal") AssertRandom(expected.GetProperty("rng"), random);
        token.Publish();
        int publications = f.Events.Log.Count;
        token.Publish();
        Assert.Equal(publications, f.Events.Log.Count);
        Assert.Equal(scene == "god" ? 0 : 1, token.CommittedHits);
        Assert.Equal(scene == "god" ? 1 : 0, token.GodModeAvoidances);
    }

    [Fact]
    public void Buff_only_mutation_and_new_server_census_refuse_the_entire_staged_hp()
    {
        foreach (bool changeBuff in new[] { false, true })
        {
            using var f = new Fixture(436);
            var token = f.Prepare();
            var original = f.Current();
            var random = new VanillaUnifiedRandom1458(0);
            Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
            Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out _));
            if (changeBuff)
            {
                Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, new(31), 1, publish: false));
                Assert.Equal(original, f.Current()); // independent of player revision
            }
            else Assert.True(f.Owner.Create(new("test:second-server"), 1500f, 1500f).IsCreated);
            Assert.False(token.IsCurrent);
            Assert.False(token.TryAdoptUnpublished());
            token.Publish();
            Assert.Equal(original, f.Current());
            Assert.Empty(f.Events.Log);
        }
    }

    [Fact]
    public void God_mode_retains_each_subupdate_variation_without_hp_status_or_notifications()
    {
        using var f = new Fixture(438, "god");
        var before = f.Current();
        var token = f.Prepare();
        var random = new VanillaUnifiedRandom1458(1458);
        var expected = random.Clone();
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
        for (int i = 0; i < 4; i++)
        {
            Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out bool contact));
            Assert.True(contact);
            expected.Next(-15, 16);
        }
        Assert.True(expected.HasSameState(random));
        Assert.True(token.TryAdoptUnpublished());
        token.Publish();
        Assert.Equal(before, f.Current());
        Assert.Equal(4, token.GodModeAvoidances);
        Assert.Empty(f.Events.Log);
    }

    [Fact]
    public void Surviving_hurt_shadows_general_immunity_before_the_next_subupdate()
    {
        using var f = new Fixture(438);
        var token = f.Prepare();
        var random = new VanillaUnifiedRandom1458(1458);
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
        Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out bool first));
        Assert.True(first);
        var checkpoint = random.Clone();
        Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out bool second));
        Assert.False(second);
        Assert.True(checkpoint.HasSameState(random));
        Assert.Equal(1, token.CommittedHits);
        Assert.True(token.TryAdoptUnpublished());
        Assert.True(f.Owner.IsGeneralPveImmune(f.Player, 1));
    }

    [Fact]
    public void Adopted_status_precedes_observers_and_reused_generation_gets_no_old_journal()
    {
        using var f = new Fixture(436);
        var token = f.Prepare();
        var random = new VanillaUnifiedRandom1458(0);
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
        Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out _));
        Assert.True(token.TryAdoptUnpublished());
        f.Events.OnMoved = () =>
        {
            Assert.Equal(300, f.Owner.GetNpcContactBuffDuration(f.Player, new(31)));
            Assert.True(f.Owner.Despawn(f.Id));
            Assert.True(f.Owner.Create(f.Id, 1000f, 1000f).IsCreated);
        };
        token.Publish();
        Assert.Equal(new[] { "moved" }, f.Events.Log);
    }

    [Fact]
    public void Hit_direction_uses_float_origins_after_integer_body_collision()
    {
        using var f = new Fixture(436, playerX: 1000.25f, projectileX: 1005.5f);
        var token = f.Prepare();
        var random = new VanillaUnifiedRandom1458(1458);
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(f.Shot.Type, out var definition));
        Assert.True(token.TryStageTarget(f.Player.Slot.Value, in f.Shot, in definition, random, out bool contact));
        Assert.True(contact);
        Assert.True(token.TryAdoptUnpublished());
        Assert.Equal(-4.5f, f.Current().VelocityX);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Events Events = new();
        internal readonly ServerPlayerId Id = new("test:prepared-ground-combat");
        internal readonly ServerPlayerAuthority Owner;
        internal readonly PlayerHandle Player;
        internal readonly ProjectileSnapshot Shot;
        private readonly VanillaProjectileWorldStateStepper stepper = new(new WorldTileStore(new(200, 200)));
        internal Fixture(int type, string scene = "hit", float playerX = 1000f, float projectileX = 1000f)
        {
            var slots = new PlayerSlotPool(2);
            var registry = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(registry, slots.Capacity);
            Owner = new(states, registry, events: Events);
            var created = Owner.Create(Id, playerX, 1000f);
            Assert.True(created.IsCreated);
            Player = created.Player;
            Assert.True(Owner.SetVitals(Id, new((short)(scene == "lethal" ? 1 : 100), 100, 20, 20)));
            if (scene == "god") Assert.True(Owner.SetGodMode(Id, true));
            if (scene == "immune-buff") Assert.True(Owner.SetItem(Id,
                new((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), new(1613), 1, default, 0)));
            Shot = new(new(0, new(1)), new(1), new(type), 255, projectileX, 1000f, 0f, 0f, default, 0, 30, 0f, 0);
            Events.Log.Clear();
        }
        internal PlayerStateSnapshot Current() { Assert.True(Owner.TryGet(Player, out var p)); return p; }
        internal ServerPlayerAuthority.GroundCombatHitPreparation Prepare()
        {
            Assert.True(stepper.TryCaptureGroundCombatTerrain(in Shot, out var terrain));
            Assert.True(Owner.TryPrepareGroundCombatHits(in Shot, new(3, new(1)), 1, false, false, false, terrain, out var prepared));
            return prepared!;
        }
        public void Dispose() => Owner.Despawn(Id);
    }

    private sealed class Events : IRuntimeServerPlayerEventSink
    {
        internal readonly List<string> Log = [];
        internal Action? OnMoved;
        public void ServerPlayerMoved(in PlayerStateSnapshot player) { Log.Add("moved"); OnMoved?.Invoke(); }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState vitals) => Log.Add("vitals");
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs) => Log.Add("buffs");
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId projectileType, int damage, int hitDirection) => Log.Add("death");
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState appearance) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool hostile) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool enabled) { }
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) { }
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short animationTicks) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short x, short y) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
    private static int Duration(JsonElement p, int type)
    {
        var types = p.GetProperty("buffType"); var times = p.GetProperty("buffTime");
        for (int i = 0; i < types.GetArrayLength(); i++) if (types[i].GetInt32() == type) return times[i].GetInt32();
        return 0;
    }
    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(actual)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()), (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(actual)!);
    }
}
