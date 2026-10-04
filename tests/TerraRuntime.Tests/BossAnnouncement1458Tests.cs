using System.Reflection;
using System.Buffers.Binary;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class BossAnnouncement1458Tests
{
    // Captured independently from official 1.4.5.8 NetTextModule.SerializeServerMessage,
    // after NetworkInitializer's Liquid=0/Text=1 registration and NetPacket.ShrinkToFit.
    [Theory]
    [InlineData("Announcement.HasAwoken", "NPCName.EyeofCthulhu", "3900520100FF0216416E6E6F756E63656D656E742E48617341776F6B656E0102144E50434E616D652E4579656F66437468756C687500AF4BFF")]
    [InlineData("Announcement.HasBeenDefeated_Plural", "Enemies.TheTwins", "4200520100FF0223416E6E6F756E63656D656E742E4861734265656E44656665617465645F506C7572616C010210456E656D6965732E5468655477696E7300AF4BFF")]
    [InlineData("Announcement.HasBeenDefeated_Single", "Enemies.MoonLord", "4200520100FF0223416E6E6F756E63656D656E742E4861734265656E44656665617465645F53696E676C65010210456E656D6965732E4D6F6F6E4C6F726400AF4BFF")]
    [InlineData("LegacyMisc.48", null, "1900520100FF020D4C65676163794D6973632E343800AF4BFF")]
    [InlineData("LegacyMisc.107", null, "1A00520100FF020E4C65676163794D6973632E31303700AF4BFF")]
    public void Codec_matches_official_server_golden_bytes(string key, string? name, string hex) =>
        Assert.Equal(Convert.FromHexString(hex), TerrariaBossAnnouncementCodec1458.Encode(key, name));

    [Theory]
    [InlineData(4, "Announcement.HasAwoken", "NPCName.EyeofCthulhu")]
    [InlineData(50, "Announcement.HasAwoken", "NPCName.KingSlime")]
    [InlineData(125, "LegacyMisc.48", null)]
    [InlineData(127, "Announcement.HasAwoken", "NPCName.SkeletronPrime")]
    [InlineData(128, "Announcement.HasAwoken", "NPCName.PrimeCannon")]
    [InlineData(657, "Announcement.HasAwoken", "NPCName.QueenSlimeBoss")]
    [InlineData(-16, "LegacyMisc.107", null)]
    public void Successful_owned_summon_announces_once_to_playing_peers(int action, string key, string? name)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = Register(replication, playing: true);
        var joining = Register(replication, playing: false, id: 2);
        var npcs = new RuntimeNpcStore();
        var state = new ServerRuntimeState(npcs: npcs, npcReplication: replication,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { ZenithWorld = true });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 200, 120, 0, 0, 0, 0, 0)));
        Drain(queue);
        state.Apply(new ClientBossSummonRuntimeCommand(connection, checked((short)action)));
        AssertAnnouncement(Assert.Single(Drain(queue)), key, name);
        Assert.Empty(Drain(joining));
        state.Apply(new ClientBossSummonRuntimeCommand(connection, checked((short)action)));
        Assert.Empty(Drain(queue));
    }

    [Fact]
    public void Spazmatism_spawn_is_silent_and_unowned_summon_is_rejected()
    {
        Assert.False(VanillaBossAnnouncementCatalog1458.TryGetSpawn(VanillaNpcIds.Spazmatism, out _, out _));
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = Register(replication, true);
        var state = new ServerRuntimeState(npcReplication: replication);
        state.Apply(new ClientBossSummonRuntimeCommand(new(GameCommandSourceId.FromConnection(99), new(new(0), new(1))), 4));
        Assert.Empty(Drain(queue));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-16)]
    public void Failed_allocation_does_not_announce(int action)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = Register(replication, true);
        var npcs = new RuntimeNpcStore(1);
        Assert.True(npcs.TrySpawnVanilla(new(1, 1, 100, 200, 0, 0, 0, default, NpcSimulationState.Initial), out _));
        var state = new ServerRuntimeState(npcs: npcs, npcReplication: replication,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { ZenithWorld = true });
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 200, 120, 0, 0, 0, 0, 0)));
        Drain(queue);
        state.Apply(new ClientBossSummonRuntimeCommand(connection, checked((short)action)));
        Assert.Empty(Drain(queue));
        Assert.Equal(1, npcs.ActiveCount);
    }

    [Theory]
    [InlineData(4, "NPCName.EyeofCthulhu")]
    [InlineData(35, "NPCName.SkeletronHead")]
    [InlineData(50, "NPCName.KingSlime")]
    [InlineData(127, "NPCName.SkeletronPrime")]
    [InlineData(134, "NPCName.TheDestroyer")]
    [InlineData(657, "NPCName.QueenSlimeBoss")]
    [InlineData(668, "NPCName.Deerclops")]
    public void Owned_death_announces_once_for_player_and_environment_routes(int type, string name)
    {
        foreach (bool playerHit in new[] { false, true })
        {
            var f = new Fixture();
            var boss = f.Spawn(type);
            if (playerHit) Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(boss));
            else Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(boss.Handle, 100_000));
            AssertAnnouncement(Assert.Single(Drain(f.Queue)), "Announcement.HasBeenDefeated_Single", name);
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Hit(boss));
            Assert.Empty(Drain(f.Queue));
        }
    }

    [Theory]
    [InlineData(125, 126)]
    [InlineData(126, 125)]
    public void Only_last_twin_announces_plural_defeat(int firstType, int secondType)
    {
        var f = new Fixture();
        var first = f.Spawn(firstType);
        var second = f.Spawn(secondType);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(first));
        Assert.Empty(Drain(f.Queue));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(second));
        AssertAnnouncement(Assert.Single(Drain(f.Queue)), "Announcement.HasBeenDefeated_Plural", "Enemies.TheTwins");
    }

    [Theory]
    [InlineData(13, "NPCName.EaterofWorldsHead")]
    [InlineData(14, "NPCName.EaterofWorldsBody")]
    [InlineData(15, "NPCName.EaterofWorldsTail")]
    public void Only_last_eater_segment_announces_its_own_source_name(int type, string name)
    {
        var f = new Fixture();
        var first = f.Spawn(13);
        var last = f.Spawn(type);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(first));
        Assert.Empty(Drain(f.Queue));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(last));
        AssertAnnouncement(Assert.Single(Drain(f.Queue)), "Announcement.HasBeenDefeated_Single", name);
    }

    [Theory]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(128)]
    [InlineData(396)]
    [InlineData(397)]
    [InlineData(1)]
    public void Ordinary_parts_do_not_admit_boss_defeat(int type)
    {
        Assert.False(VanillaBossAnnouncementCatalog1458.TryGetDefeat(new(type), false, out _, out _));
    }

    [Fact]
    public void Moon_lord_announces_only_owned_terminal_death_and_never_departure()
    {
        foreach (float phase in new[] { 2f, 3f })
        {
            var f = new Fixture();
            var core = f.Spawn(398);
            var update = new NpcStateUpdate(core.Type, core.NetId, 100, 200, 0, 0, 0,
                new(phase, 599f, 0, 0), core.Simulation with { Life = 0 });
            Assert.True(f.Npcs.TryUpdate(core.Handle, in update, out core));
            f.Pipeline.NpcAiStateCommitted(in core);
            Assert.Empty(Drain(f.Queue));
            if (phase == 3f) continue;
            update = update with { Ai = new(phase, 600f, 0, 0) };
            Assert.True(f.Npcs.TryUpdate(core.Handle, in update, out core));
            f.Pipeline.NpcAiStateCommitted(in core);
            AssertAnnouncement(Assert.Single(Drain(f.Queue)), "Announcement.HasBeenDefeated_Single", "Enemies.MoonLord");
            f.Pipeline.NpcAiStateCommitted(in core);
            Assert.Empty(Drain(f.Queue));
        }
    }

    private static void AssertAnnouncement(NetTextModule module, string key, string? name)
    {
        Assert.Equal(byte.MaxValue, module.AuthorId);
        Assert.Equal((byte)175, module.MessageColor.R);
        Assert.Equal((byte)75, module.MessageColor.G);
        Assert.Equal((byte)255, module.MessageColor.B);
        Assert.Equal((byte)NetworkText.Mode.LocalizationKey, module.ServerText!.TextMode);
        Assert.Equal(key, module.ServerText.Text);
        if (name is null) Assert.Empty(module.ServerText.SubstitutionList);
        else
        {
            var substitution = Assert.Single(module.ServerText.SubstitutionList);
            Assert.Equal((byte)NetworkText.Mode.LocalizationKey, substitution.TextMode);
            Assert.Equal(name, substitution.Text);
            Assert.Empty(substitution.SubstitutionList);
        }
    }

    private static TerrariaConnectionOutboundQueue Register(RuntimeNpcReplicationRegistry replication, bool playing, int id = 1)
    {
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 131_072, 4096));
        var source = GameCommandSourceId.FromConnection(id);
        Assert.True(replication.TryRegister(source, queue));
        if (playing) replication.PlayerSpawned(new(source, new(new(0), new(1))), new(new(0), 100, 200, 0, 0, 0, 0, 0));
        return queue;
    }

    private static List<NetTextModule> Drain(TerrariaConnectionOutboundQueue outbound)
    {
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        List<NetTextModule> result = [];
        while (queue.TryRead(out var frame))
        {
            // Source module 1 is chat. Death credit now also emits genuine modules 4 and 11;
            // those have separate original-byte coverage and are not boss announcements.
            if (frame.Bytes.Span[2] != 82 || BinaryPrimitives.ReadUInt16LittleEndian(frame.Bytes.Span[3..]) != 1) continue;
            Assert.True(TerrariaPacket.TryDeserializePayload(82, frame.Bytes[3..], out var packet));
            result.Add(Assert.IsType<NetTextModule>(Assert.IsType<LoadNetModule>(packet).LoadedModule));
        }
        return result;
    }

    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public RuntimeNpcStore Npcs { get; } = new();
        public RuntimeNpcNetworkCombatPipeline Pipeline { get; }
        public TerrariaConnectionOutboundQueue Queue { get; }
        private readonly PlayerHandle player = new(new(0), new(1));
        public Fixture()
        {
            var replication = new RuntimeNpcReplicationRegistry();
            Queue = Register(replication, true);
            var items = new RuntimeWorldItemStore();
            Pipeline = new(Npcs, items, this, new PlayerAuthority(null, null), static () => 0,
                replication, new(items), null, null, new(), false, false);
        }
        public NpcSnapshot Spawn(int type)
        {
            Assert.True(Npcs.TrySpawnVanilla(new(type, checked((short)type), 100, 200, 0, 0, 0, default,
                NpcSimulationState.Initial), out var npc));
            return npc;
        }
        public RuntimeProjectileNpcDamageResult Hit(in NpcSnapshot target) =>
            Pipeline.TryStrikeServerPlayerMelee(player, target.Handle, 100_000, 0, false, 0, 1);
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with { Player = player, Revision = new(1),
                PositionX = 100, PositionY = 200, HasHealth = true, Life = 500, MaxLife = 500 };
            return slot == player.Slot;
        }
    }
}
