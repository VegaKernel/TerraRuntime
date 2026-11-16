using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Tests;

public sealed partial class WiringDartPlayerHit1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using var stream = typeof(WiringDartPlayerHit1458Tests).Assembly.GetManifestResourceStream("WiringTrapPlayerHit1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            if (row.GetProperty("immunity").GetInt32() != 70)
                foreach (bool server in new[] { false, true }) yield return [row.GetRawText(), server];
    }

    public static IEnumerable<object[]> RegenerationCases()
    {
        using var stream = typeof(WiringDartPlayerHit1458Tests).Assembly.GetManifestResourceStream("WiringTrapPlayerVenomDot1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(RegenerationCases))]
    public void Venom_and_existing_flag_counter_profiles_match_original_UpdateLifeRegen(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int mask = row.GetProperty("mask").GetInt32(), maximum = row.GetProperty("maximum").GetInt32();
        var before = new PlayerNpcHealthState1458(maximum / 2, row.GetProperty("counter").GetInt32(), row.GetProperty("time").GetSingle(), true);
        var after = VanillaRemotePlayerHealth1458.Step(before, maximum, false, false, false, true, row.GetProperty("expert").GetBoolean(),
            row.GetProperty("moving").GetBoolean(), row.GetProperty("regenerationSlots").GetInt32(), (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
            false, false, false, false, false, venom: (mask & 8) != 0);
        Assert.NotNull(after); Assert.Equal(row.GetProperty("life").GetInt32(), after.Value.Life);
        Assert.Equal(row.GetProperty("afterCounter").GetInt32(), after.Value.RegenCount);
        Assert.Equal(row.GetProperty("afterTime").GetSingle(), after.Value.RegenTime);
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void Existing_human_and_server_player_owners_match_original_EVP_projection(string observation, bool server)
    {
        using var json = JsonDocument.Parse(observation); RunSourceRow(json.RootElement, server);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void Missing_capability_or_final_clock_mutation_refuses_hp_status_and_prepared_entropy(bool server, int sabotage)
    {
        var first = SourceCases().First(); using var json = JsonDocument.Parse((string)first[0]); RunSourceRow(json.RootElement, server, sabotage);
    }

    [Fact]
    public void Venom_duration_one_keeps_its_final_effect_without_granting_it_from_packet50()
    {
        var buffs = new PlayerBuffState(); buffs.ReplaceNetworkSnapshot([new BuffTypeId(70)]);
        Assert.False(buffs.CaptureDebuffFlags().Venom);
        Assert.True(buffs.TryApplyNpcContactBuff(new(70), 1));
        Assert.True(buffs.TickNpcContactBuffs()); Assert.Equal(0, buffs.GetDuration(new(70)));
        Assert.True(buffs.CaptureDebuffFlags().Venom);
        buffs.TickNpcContactBuffs(); Assert.False(buffs.CaptureDebuffFlags().Venom);
    }

    [Fact]
    public void Server_venom_expiry_preserves_residue_and_the_last_positive_update_deals_damage()
    {
        var slots = new PlayerSlotPool(2); var registry = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(registry, slots.Capacity); var owner = new ServerPlayerAuthority(states, registry);
        var id = new ServerPlayerId("test:venom-clock"); var created = owner.Create(id, 1040, 1040); Assert.True(created.IsCreated);
        Assert.True(owner.SetVitals(id, new(500, 500, 20, 20))); var player = created.Player;
        var tiles = new TerraRuntime.World.WorldTileStore(new(200, 200));
        Assert.True(owner.TryApplyNpcContactBuff(player, new(70), 1, publish: false));
        owner.TickBuffs(); owner.TickLava(1, tiles, false, false, false); owner.TickHealthContext(tiles);
        Assert.True(owner.TryGet(player, out var after)); Assert.Equal(500, after.Life); Assert.True(after.Debuffs!.Value.Venom);
        Assert.Equal(0, owner.GetNpcContactBuffDuration(player, new(70)));
        owner.TickBuffs(); owner.TickLava(2, tiles, false, false, false); owner.TickHealthContext(tiles);
        Assert.True(owner.TryGet(player, out after)); Assert.Equal(500, after.Life); Assert.False(after.Debuffs!.Value.Venom);
        Assert.True(owner.TryApplyNpcContactBuff(player, new(70), 3, publish: false));
        for (int tick = 3; tick <= 5; tick++) { owner.TickBuffs(); owner.TickLava(tick, tiles, false, false, false); }
        Assert.True(owner.TryGet(player, out after)); Assert.Equal(499, after.Life);
        Assert.Equal(0, owner.GetNpcContactBuffDuration(player, new(70)));
    }

    private static void RunSourceRow(JsonElement row, bool server, int sabotage = -1, short maximum = 1000)
    {
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        int mode = row.GetProperty("mode").GetInt32(); bool good = row.GetProperty("good").GetBoolean();
        var slots = new PlayerSlotPool(2); var registry = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(registry, slots.Capacity); var servers = new ServerPlayerAuthority(states, registry);
        var humans = new PlayerAuthority(null, null, expertMode: mode > 0, masterMode: mode == 2);
        humans.SetCombatEquipmentWorldModes(mode > 0 || good, mode == 2 || mode > 0 && good);
        servers.SetCombatEquipmentWorldModes(mode > 0 || good, mode == 2 || mode > 0 && good);
        PlayerJoinSession? session = null; ConnectionHandle connection = default; PlayerHandle player;
        var id = new ServerPlayerId("test:wiring-dart");
        if (server)
        {
            var created = servers.Create(id, 1040, 1040); Assert.True(created.IsCreated); player = created.Player;
            Assert.True(servers.SetVitals(id, new(maximum, maximum, 20, 20)));
            Assert.True(servers.TryGet(player, out var current)); Assert.True(states.TrySetDerivedLifeMax(in current, maximum));
        }
        else
        {
            Assert.True(slots.TryAcquireConnection(out var lease)); session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest(); connection = new(GameCommandSourceId.FromConnection(92125), session.Handle);
            Assert.True(humans.TryApply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 65, 65, 0, 0, 0, 0, 0))));
            Assert.True(humans.TryApply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, maximum, maximum))));
            Assert.True(humans.TryGet(connection, out var member)); member.DerivedLifeMax = maximum; player = connection.Player;
            Assert.True(humans.TryApply(new PlayerMovementRuntimeCommand(connection,
                new(session.Slot, 0, 0, 0, 0, 0, 1040, 1040, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0))));
        }
        try
        {
            void Equip(int index, int type, int prefix = 0)
            {
                short slot = (short)(VanillaPlayerItemSlotCatalog.ArmorStart + index);
                if (server) Assert.True(servers.SetItem(id, new(slot, new(type), (short)(type == 0 ? 0 : 1), new((byte)prefix), 0)));
                else Assert.True(humans.TryApply(new PlayerEquipmentRuntimeCommand(connection,
                    new(player.Slot, slot, (short)(type == 0 ? 0 : 1), (byte)prefix, (short)type, 0))));
            }
            Equip(0, 0);
            if (row.GetProperty("sweater").GetBoolean()) Equip(1, 5007);
            if (row.GetProperty("defense").GetInt32() == 50)
            {
                Equip(0, VanillaItemIds.SolarFlareHelmet.Value); Equip(2, VanillaItemIds.SolarFlareLeggings.Value);
                Equip(3, 888, 65); Equip(4, 886, 63);
            }
            if (row.GetProperty("immunity").GetInt32() == 20) Equip(5, 887);
            VanillaPlayerCombatSnapshot capturedCombat;
            if (server) Assert.True(servers.TryCaptureCombatSnapshot(player, out capturedCombat));
            else Assert.True(humans.TryCaptureCombatSnapshot(player, out capturedCombat));
            Assert.Equal(row.GetProperty("defense").GetInt32(), capturedCombat.Defense);
            Assert.Equal(row.GetProperty("sweater").GetBoolean(), capturedCombat.DeadMansSweater);
            var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()); var store = new RuntimeProjectileStore(2);
            int style = row.GetProperty("style").GetInt32();
            var shot = new ProjectileStateUpdate(new(style == 0 ? 98 : style == 1 ? 184 : 980), 255, 1053, 1043, 12, 0,
                default, 0, (short)(style == 0 ? 20 : style == 1 ? 40 : 30), 2, 0);
            if (row.TryGetProperty("projectile", out var image))
            {
                var pos = image.GetProperty("position"); var vel = image.GetProperty("velocity");
                shot = new(new(image.GetProperty("type").GetInt32()), 255, pos.GetProperty("X").GetSingle(), pos.GetProperty("Y").GetSingle(),
                    vel.GetProperty("X").GetSingle(), vel.GetProperty("Y").GetSingle(), default, 0, image.GetProperty("damage").GetInt16(), 2, 0);
            }
            if (sabotage == 1) Assert.True(store.TrySpawn(0, in shot, out _));
            else using (var birth = store.CreateWorldSpawnPreparation())
            { Assert.True(birth.TryStage(in shot, false, out _)); Assert.True(birth.TryAdoptUnpublished(out _)); }
            if (shot.Type.Value == 188)
            {
                Assert.True(store.TryGetActive(0, out var current)); Assert.True(store.TryGetLifecycle(current.Handle, out var life));
                life = life with { LocalAi = new(5, 0, 0) };
                Assert.True(store.TryPrepareSimulation(in current, out var actor)); Assert.NotNull(actor);
                Assert.True(actor.TryStageMotion(in shot, in life)); Assert.True(actor.TryAdoptUnpublished(out _, out _));
            }
            var retainedRandom = random.Clone(); int clockCalls = 0;
            long Clock()
            {
                clockCalls++;
                if (clockCalls == 2)
                {
                    if (sabotage == 2) random.Next();
                    if (sabotage == 3) return 1;
                    if (sabotage == 4)
                    {
                        if (server) Assert.True(servers.SetVitals(id, new(500, 1000, 20, 20)));
                        else Assert.True(humans.TryApply(new PlayerHealthRuntimeCommand(connection, new(player.Slot, 500, 1000))));
                    }
                    if (sabotage == 5)
                    {
                        if (server) Assert.True(servers.TryApplyNpcContactBuff(player, new(20), 1, publish: false));
                        else
                        {
                            // PvP relay publishes a message but does not mutate owned buffs.
                            // Exercise the actual independent buff owner instead.
                            var profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                                .GetField("transferProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(humans)!;
                            var before = profiles.CaptureBuffState(connection); Assert.NotNull(before);
                            var next = before!.Clone(); Assert.True(next.TryApplyNpcContactBuff(new(20), 1));
                            Assert.True(profiles.TryAdoptBuffState(connection, before, next));
                        }
                    }
                }
                return 0;
            }
            var pass = new RuntimeProjectilePlayerCombatPass(store, new RuntimeNpcStore(), humans, Clock,
                serverPlayers: servers, expertMode: mode > 0, masterMode: mode == 2, sourceRandom: sabotage == 0 ? null : random, goodWorld: good);
            pass.Tick([]);
            PlayerStateSnapshot actual;
            if (server) Assert.True(servers.TryGet(player, out actual)); else Assert.True(humans.TryCapture(player, out actual));
            if (sabotage >= 0)
            {
                Assert.Equal(sabotage == 4 ? 500 : 1000, actual.Life);
                Assert.Equal(0, pass.HostileCommittedHits);
                int currentBuff = server ? servers.GetNpcContactBuffDuration(player, new(20)) : humans.GetBuffDuration(player, new(20));
                Assert.Equal(sabotage == 5 ? 1 : 0, currentBuff);
                if (sabotage == 2) retainedRandom.Next();
                Assert.True(retainedRandom.HasSameState(random));
                return;
            }
            Assert.Equal(row.GetProperty("life").GetInt32(), actual.Life);
            Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX); Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            int buffType = style == 2 ? 323 : style is 3 or 4 ? 0 : style == 5 ? 70 : 20; int duration = 0; var types = row.GetProperty("buffType"); var times = row.GetProperty("buffTime");
            for (int i = 0; i < types.GetArrayLength(); i++) if (types[i].GetInt32() == buffType) duration = times[i].GetInt32();
            Assert.Equal(duration, server ? servers.GetNpcContactBuffDuration(player, new(buffType)) : humans.GetBuffDuration(player, new(buffType)));
            var rng = row.GetProperty("rng"); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.Equal(rng.GetProperty("inext").GetUInt32(), (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
            Assert.Equal(rng.GetProperty("SeedArray").EnumerateArray().Select(v => v.GetInt32()),
                (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
            var accepted = random.Clone(); pass.Tick([]); Assert.True(accepted.HasSameState(random)); Assert.Equal(1, pass.HostileCommittedHits);
        }
        finally { session?.Dispose(); if (server) servers.Despawn(id); }
    }
}
