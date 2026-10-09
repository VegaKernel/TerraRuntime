using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class IncomingHumanVitals1458Tests
{
    [Fact]
    public void Original_player_phase_then_local_callers_debit_owned_life_instead_of_stale_report()
    {
        using var source = Read("IncomingHumanVitals1458");
        Assert.Equal(24, source.RootElement.GetArrayLength());
        int hits = 0, references = 0;
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string route = row.GetProperty("route").GetString()!;
            if (route == "dedicated-hostile-control")
            {
                Assert.Equal(255, I(row, "myPlayer"));
                Assert.Equal(row.GetProperty("beforeHit").GetRawText(), row.GetProperty("afterHit").GetRawText());
                Assert.Equal(row.GetProperty("beforeRandom").GetRawText(), row.GetProperty("afterRandom").GetRawText());
                Assert.Empty(row.GetProperty("frames").EnumerateArray());
                references++;
                continue;
            }
            Assert.Equal(0, I(row, "myPlayer")); // Configured LOCAL caller, not dedicated255 scheduling.
            using var f = new Fixture(row.GetProperty("profile").GetString()!);
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.Move(I(step, "tick") > 0);
                AssertRandom(step.GetProperty("beforeRng"), f.PlayerRandom);
                f.State.Tick();
                AssertRandom(step.GetProperty("afterRng"), f.PlayerRandom);
                Assert.Equal(I(step.GetProperty("after"), "life"), f.Member.NpcHealth!.Value.Life);
                Assert.Equal(I(step.GetProperty("after"), "regenCount"), f.Member.NpcHealth.Value.RegenCount);
            }
            Assert.Equal(50, f.Member.Life);
            Assert.Equal(I(row.GetProperty("beforeHit"), "life"), f.Member.NpcHealth!.Value.Life);
            int? count = f.Member.NpcHealth.Value.RegenCount;
            f.Run(route, I(row, "damage"), new SourceRandom(1458));
            var expected = row.GetProperty("afterHit");
            Assert.Equal(I(expected, "life"), f.Member.Life);
            Assert.Equal(I(expected, "life"), f.Member.NpcHealth!.Value.Life);
            Assert.Equal(expected.GetProperty("dead").GetBoolean(), f.Member.IsDead);
            Assert.Equal(F(expected, "velocity", "X"), f.Member.VelocityX);
            Assert.Equal(F(expected, "velocity", "Y"), f.Member.VelocityY);
            Assert.Equal(count, f.Member.NpcHealth.Value.RegenCount);
            Assert.Equal(0f, f.Member.NpcHealth.Value.RegenTime);
            Assert.Equal(400, f.Member.MaxLife);
            Assert.Equal(400, f.Member.BaseLifeMax);
            Assert.Equal(400, f.Member.DerivedLifeMax);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var item));
            Assert.Equal(I(row, "inventoryType"), item.ItemType.Value);
            Assert.Equal(I(row, "inventoryStack"), item.Stack);
            hits++;
        }
        Assert.Equal(18, hits);
        Assert.Equal(6, references);
        // Source KillMe persistence/chat/projectiles and Hurt cosmetic RNG are not claimed here.
    }

    [Fact]
    public void Literal_Hurt_budget_preserves_source_potion_and_regeneration_life_on_both_platforms()
    {
        using var potion = Read("IncomingHumanVitalsHurt75");
        using var hurt = Read("IncomingHumanVitalsHurt");
        int compared = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            var row = Assert.Single(potion.RootElement.GetProperty(platform).EnumerateArray());
            using (var f = new Fixture("potion28"))
            {
                f.Warm();
                Assert.Equal(I(row.GetProperty("beforeVitals"), "statLife"), f.Member.NpcHealth!.Value.Life);
                Assert.Equal(I(row, "reportedInitialLife"), f.Member.Life);
                f.CommitLiteral(I(row, "damage"));
                Assert.Equal(I(row.GetProperty("afterVitals"), "statLife"), f.Member.Life);
                Assert.Equal(25, f.Member.Life);
                Assert.Equal(400, f.Member.MaxLife);
                compared++;
            }
            foreach (var reference in hurt.RootElement.GetProperty(platform).EnumerateArray())
            {
                string profile = reference.GetProperty("profile").GetString()!;
                using var f = new Fixture(profile.StartsWith("potion", StringComparison.Ordinal) ? "potion28" :
                    profile.StartsWith("regen", StringComparison.Ordinal) ? "regen" : "baseline");
                f.Warm();
                var before = reference.GetProperty("beforeVitals");
                Assert.Equal(I(before, "statLife"), f.Member.NpcHealth!.Value.Life);
                // Genuine Windows regeneration has count2 versus Linux1. This configured capture
                // proves Hurt preserves that count, not that the host phase owns both arithmetic paths.
                f.Member.NpcHealth = f.Member.NpcHealth.Value with { RegenCount = I(before, "lifeRegenCount") };
                f.CommitLiteral(I(reference, "damage"));
                var after = reference.GetProperty("afterVitals");
                Assert.Equal(I(after, "statLife"), f.Member.Life);
                Assert.Equal(I(after, "lifeRegenCount"), f.Member.NpcHealth!.Value.RegenCount);
                Assert.Equal(0f, f.Member.NpcHealth.Value.RegenTime);
                Assert.Equal(400, f.Member.MaxLife);
                compared++;
            }
        }
        Assert.Equal(8, compared);
        // Original117 supplied literal damage. The commit-component comparison does not claim
        // authoritative acceptance of arbitrary client117 or its relay/cosmetic/death publications.
    }

    [Fact]
    public void Unknown_vitals_refuse_before_offers_but_nullable_count_and_spawn_owned_life_remain_distinct()
    {
        foreach (string missing in new[] { "health", "current", "base", "derived", "revision", "negative", "large" })
        {
            using var f = new Fixture("regen");
            f.Warm();
            if (missing == "health") f.Member.NpcHealth = null;
            if (missing == "current") f.Member.NpcLifeCurrent = null;
            if (missing == "base") f.Member.BaseLifeMax = null;
            if (missing == "derived") f.Member.DerivedLifeMax = null;
            if (missing == "revision") f.Member.Revision = ulong.MaxValue;
            if (missing == "negative") f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = -1 };
            if (missing == "large") f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = 40_000 };
            var before = f.Member.CaptureSnapshot();
            var health = f.Member.NpcHealth;
            var random = new CallbackRandom(null);
            f.Run("npc-contact", 10, random);
            Assert.True(random.Draws == 0, $"Unexpected offer for missing {missing}");
            Assert.Equal(before, f.Member.CaptureSnapshot());
            Assert.Equal(health, f.Member.NpcHealth);
        }
        using (var f = new Fixture("regen"))
        {
            f.Warm();
            f.Member.NpcHealth = f.Member.NpcHealth!.Value with { RegenCount = null, SourceProfileKnown = false };
            f.CommitLiteral(10);
            Assert.Equal(41, f.Member.Life);
            Assert.Null(f.Member.NpcHealth!.Value.RegenCount);
            Assert.Equal(0f, f.Member.NpcHealth.Value.RegenTime);
        }
        using (var f = new Fixture("baseline", reportedLife: 0, configureSourceLife: false))
        {
            Assert.Equal(0, f.Member.Life);
            Assert.Equal(200, f.Member.NpcHealth!.Value.Life);
            Assert.True(f.Players.TryCaptureIncomingCombat(f.Connection.Player, out var capture));
            Assert.True(capture.IsAlive);
            f.CommitLiteral(10);
            Assert.Equal(190, f.Member.Life);
            Assert.Equal(190, f.Member.NpcHealth!.Value.Life);
            Assert.Equal(400, f.Member.MaxLife);
        }
        using (var f = new Fixture("baseline", configureSourceLife: false, reportHealth: false))
        {
            Assert.False(f.Member.HasHealth);
            Assert.Equal(0, f.Member.Life);
            Assert.Equal(100, f.Member.NpcHealth!.Value.Life);
            f.Warm(); // Derived maximum is owned by the actual phase before incoming admission.
            f.CommitLiteral(10);
            Assert.True(f.Member.HasHealth);
            Assert.Equal(90, f.Member.Life);
            Assert.Equal(100, f.Member.MaxLife);
            Assert.Equal(100, f.Member.BaseLifeMax);
        }
        using (var f = new Fixture("baseline", reportedLife: 0, configureSourceLife: false))
        {
            f.Member.ItemPhase = null;
            f.Member.GodMode = true;
            f.Run("npc-contact", 10, new CallbackRandom(null));
            Assert.Equal(0, f.Member.Life);
            Assert.Equal(200, f.Member.NpcHealth!.Value.Life);
            Assert.Null(f.Member.ItemPhase);
        }
        using (var f = new Fixture("baseline", reportedLife: 0, configureSourceLife: false,
            policy: IncomingHumanCombatPolicy1458.EquipmentComponent))
        {
            f.Member.ItemPhase = null;
            f.Member.GodMode = true;
            var random = new CallbackRandom(null);
            f.Run("npc-contact", 10, random);
            Assert.Equal(0, random.Draws);
            Assert.Equal(0, f.Member.Life);
            Assert.Equal(200, f.Member.NpcHealth!.Value.Life);
            Assert.Null(f.Member.ItemPhase);
        }
        using (var f = new Fixture("baseline"))
        {
            f.Warm();
            Assert.True(f.Players.TryCaptureIncomingCombat(f.Connection.Player, out var before));
            f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 70, 500)));
            Assert.False(f.Players.IsCurrentIncomingCombat(in before));
            Assert.Equal(500, f.Member.MaxLife); // packet16 remains BASE maximum, never derived.
            Assert.Equal(70, f.Member.NpcHealth!.Value.Life);
            f.State.Apply(new PlayerRespawnRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Assert.False(f.Players.IsCurrentIncomingCombat(in before));
            Assert.Equal(500, f.Member.MaxLife);
        }
    }

    [Fact]
    public void Vitals_only_callback_or_membership_change_cannot_commit_a_stale_target()
    {
        foreach (string route in new[] { "npc-contact", "hostile-projectile", "pvp-projectile" })
        foreach (string mutation in new[] { "life", "count", "base", "current", "disconnect" })
        {
            using var f = new Fixture("regen");
            f.Warm();
            var random = new CallbackRandom(() =>
            {
                if (mutation == "life") f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = 60 };
                if (mutation == "count") f.Member.NpcHealth = f.Member.NpcHealth!.Value with { RegenCount = 77 };
                if (mutation == "base") f.Member.BaseLifeMax = 500;
                if (mutation == "current") f.Member.NpcLifeCurrent = false;
                if (mutation == "disconnect") f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
            });
            f.Run(route, 10, random);
            Assert.True(random.Draws > 0);
            if (mutation == "disconnect") Assert.False(f.Players.TryGet(f.Connection, out _));
            else
            {
                Assert.Equal(50, f.Member.Life);
                Assert.Equal(0f, f.Member.VelocityX);
                Assert.Equal(0f, f.Member.VelocityY);
                Assert.False(f.Players.IsGeneralPveImmune(f.Connection.Player, 1));
            }
        }
        using (var f = new Fixture("regen"))
        {
            f.Warm();
            int? count = f.Member.NpcHealth!.Value.RegenCount;
            int publications = 0;
            f.Observer.Callback = request =>
            {
                publications++;
                Assert.Equal(41, request.Life);
                Assert.Equal(400, request.MaxLife);
                Assert.Equal(41, f.Member.Life);
                Assert.Equal(41, f.Member.NpcHealth!.Value.Life);
                Assert.Equal(count, f.Member.NpcHealth.Value.RegenCount);
                Assert.Equal(0f, f.Member.NpcHealth.Value.RegenTime);
                f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, 70, 500)));
            };
            var random = new CallbackRandom(null);
            f.Run("npc-contact", 10, random);
            Assert.Equal(1, publications);
            Assert.Equal(1, random.Draws);
            Assert.Equal(70, f.Member.Life);
            Assert.Equal(70, f.Member.NpcHealth!.Value.Life);
            Assert.Equal(500, f.Member.MaxLife);
            Assert.Equal(500, f.Member.BaseLifeMax);
        }
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(IncomingHumanVitals1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement row, string name) => row.GetProperty(name).GetInt32();
    private static float F(JsonElement row, string name, string component) => row.GetProperty(name).GetProperty(component).GetSingle();
    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(I(expected, "cursor"), Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(random)));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(random)!);
    }
    private sealed class SourceRandom(int seed) : Random
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public override int Next(int minValue, int maxValue) => source.Next(minValue, maxValue);
    }
    private sealed class CallbackRandom(Action? callback) : Random
    {
        internal int Draws;
        public override int Next(int minValue, int maxValue)
        {
            if (Draws++ == 0) callback?.Invoke();
            return minValue < 0 ? 0 : maxValue - 1;
        }
    }
    private sealed class Observer : IRuntimePlayerEventSink
    {
        internal Action<PlayerHealthCommitRequest>? Callback;
        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) => Callback?.Invoke(request);
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly PlayerSlotPool slots = new(2);
        private readonly string profile;
        internal readonly Observer Observer = new();
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal readonly RuntimeNpcStore Npcs = new(2);
        internal readonly RuntimeProjectileStore Shots = new(2);
        internal readonly VanillaUnifiedRandom1458 PlayerRandom;
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal Fixture(string profile, short reportedLife = 50, bool configureSourceLife = true, bool reportHealth = true,
            IncomingHumanCombatPolicy1458 policy = IncomingHumanCombatPolicy1458.PhaseOwned)
        {
            this.profile = profile;
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(playerEvents: Observer, worldTiles: tiles, playerUpdateRandomSeed: new(1458), incomingHumanCombatPolicy: policy);
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!).Players;
            Players.SetPlayerUpdateWorldFacts(new(400, 300, 80, false, false));
            Players.SetRemotePlayerEnvironment(new(false, false), Shots);
            PlayerRandom = (VanillaUnifiedRandom1458)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Players)!;
            Connection = Join(94020, reportedLife, reportHealth);
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
                new(Connection.Player.Slot, 0, (short)(profile == "potion28" ? 3 : 0), 0, (short)(profile == "potion28" ? 28 : 0), 0)));
            if (configureSourceLife)
                Member.NpcHealth = Member.NpcHealth!.Value with { Life = 50, RegenCount = profile == "regen" ? 119 : 0, RegenTime = profile == "regen" ? 600f : 0f };
        }
        private ConnectionHandle Join(long id, short life = 400, bool reportHealth = true)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var c = new ConnectionHandle(GameCommandSourceId.FromConnection(id), session.Handle);
            if (reportHealth) State.Apply(new PlayerHealthRuntimeCommand(c, new(c.Player.Slot, life, 400)));
            State.Apply(new PlayerManaRuntimeCommand(c, new(c.Player.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(c, session, new(c.Player.Slot, 100, 103, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerEquipmentRuntimeCommand(c, new(c.Player.Slot, 59, 0, 0, 0, 0)));
            State.Apply(new PlayerPvpToggleRuntimeCommand(c, true));
            State.Apply(new PlayerMovementRuntimeCommand(c,
                new(c.Player.Slot, 64, 16, 0, 64, 0, c.Player.Slot.Value == 0 ? 1600 : 1700, 1606, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            return c;
        }
        internal void Move(bool use) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, 64, 0, 1600, 1606,
                false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        internal void Warm()
        {
            Move(false); State.Tick();
            if (profile == "potion28") { Move(true); State.Tick(); }
        }
        internal void CommitLiteral(int damage)
        {
            var state = new NpcStateUpdate(3, 3, 1600, 1606, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawn(0, in state, out var source));
            Assert.Equal(PlayerDamageCommitResult.Committed, Players.TryCommitAuthoritativeNpcContactDamage(1,
                source.Handle, Connection.Player, damage, 1, VanillaPlayerImmunityChannel1458.General, out _));
        }
        internal void Run(string route, int damage, Random random)
        {
            var npcState = new NpcStateUpdate(3, 3, 1600, 1606, 0, 0, 255, default,
                NpcSimulationState.Initial with { Friendly = false, DamageOverride = damage });
            Assert.True(Npcs.TrySpawn(0, in npcState, out var npc));
            if (route == "npc-contact") { new RuntimeNpcPlayerCombatPass(Npcs, Players, random).Tick(1); return; }
            bool pvp = route == "pvp-projectile";
            ConnectionHandle owner = pvp ? Join(94021) : default;
            ProjectileHandle handle;
            if (pvp)
            {
                var state = new ProjectileStateUpdate(new ProjectileTypeId(14), owner.Player.Slot.Value, 1600, 1606, 1, 0, default, 0, (short)damage, 0, (short)damage);
                Assert.True(Shots.TrySpawn(0, in state, out var shot)); handle = shot.Handle;
                Assert.True(Shots.TryMarkCombatTrusted(handle, owner.Player));
            }
            else
            {
                var intent = new NpcAiProjectileIntent(new ProjectileTypeId(100), 1600, 1606, 1, 0, damage, 0);
                Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(Shots, npc.Handle, in intent, out var shot)); handle = shot.Handle;
            }
            new RuntimeProjectilePlayerCombatPass(Shots, Npcs, Players, () => 1, random).Tick([]);
        }
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
}
