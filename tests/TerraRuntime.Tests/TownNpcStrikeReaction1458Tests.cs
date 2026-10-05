using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class TownNpcStrikeReaction1458Tests
{
    [Fact]
    public void Admission_that_changes_source_rng_cannot_commit_town_HP_or_reaction()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(0, new(22, 22, 800, 800, 0, 0, 255, default,
            NpcSimulationState.Initial), out var npc));
        var random = new VanillaUnifiedRandom1458(5); var before = random.Clone();
        var after = before.Clone();
        var reaction = new TerraRuntime.Core.Npcs.NpcTownStrikeReaction1458(npc.Handle, npc.Revision,
            npc.Ai with { Ai0 = 1, Ai1 = 300 + after.Next(300), Ai2 = 0 }, 1, random, before);
        bool Admit(in NpcSnapshot pending) { random.Next(); return true; }
        var executor = new TerraRuntime.Core.Npcs.RuntimeNpcDamageExecutor(npcs, lethalAdmission: Admit);
        var request = new NpcDamageRequest(npc.Handle, DamageSource.Server, 9999);
        Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, townReaction: reaction));
        Assert.True(npcs.TryGet(npc.Handle, out var retained)); Assert.Equal(npc, retained);
        var expected = before.Clone(); expected.Next(); Assert.True(random.HasSameState(expected));
    }

    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(TownNpcStrikeReaction1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.town-strike-reaction-checkpoint-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            yield return [row.GetRawText(), false];
            if (row.GetProperty("lane").GetInt32() == 1) yield return [row.GetRawText(), true];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void Common_environment_player_projectile_and_client_strikes_retain_original_reaction(string json, bool projectile)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32(), state = row.GetProperty("state").GetInt32();
        int direction = row.GetProperty("direction").GetInt32();
        bool peerActive = row.GetProperty("peerActive").GetBoolean();
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        Assert.True(npcs.TrySpawn(0, new(type, (short)type, 800, 800, 0, 0, 255, new(state, 12, 1, 7),
            NpcSimulationState.Initial with { DirectionX = 1, LocalAi = new(0, 0, 0, 9) }), out var npc));
        NpcSnapshot peer = default;
        if (peerActive) Assert.True(npcs.TrySpawn(1, new(17, 17, 900, 900, 0, 0, 255, default,
            NpcSimulationState.Initial), out peer));
        var lookup = new Lookup(); var players = new PlayerAuthority(null, null);
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1701), session.Handle);
        Assert.True(players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 62, 62, 0, 100, 100, 0, 0))));
        Assert.True(players.TryApply(new PlayerEquipmentRuntimeCommand(connection,
            new PlayerEquipmentCommitRequest(connection.Player.Slot, 0, 1, 0,
                checked((short)VanillaItemIds.WoodenBow.Value), 0))));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, lookup, players, () => 0,
            null, new(items), null, null, new(), false, false, lootRandom: random);
        var before = random.Clone();
        int lane = row.GetProperty("lane").GetInt32(); bool accepted;
        if (lane == 0)
            accepted = pipeline.TryStrikeEnvironment(npc.Handle, 20, 2f, direction) == RuntimeTownNpcMeleeDamageResult1458.Committed;
        else if (lane == 2)
        {
            var wire = new TerrariaNpcDamageState(npc.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation), 20, 2f, (byte)(direction + 1), 0);
            accepted = pipeline.TryApply(connection, in wire) == RuntimeNpcNetworkDamageResult.Committed;
        }
        else if (projectile)
        {
            var shot = new ProjectileSnapshot(new(0, new(1)), new(1), VanillaProjectileIds.WoodenArrowFriendly,
                0, 800, 800, 1, 0, default, 0, 20, 2f, 20);
            accepted = pipeline.TryStrikeProjectile(in shot, npc.Handle, direction) == RuntimeProjectileNpcDamageResult.Committed;
        }
        else
            accepted = pipeline.TryStrikeServerPlayerMelee(lookup.Player, npc.Handle, 20, 0, false, 2f, direction) ==
                RuntimeProjectileNpcDamageResult.Committed;
        if (npc.Simulation.DontTakeDamage || npc.Simulation.LifeMax <= 0 ||
            (state == 3 && peerActive && type != 453))
        {
            // The source-only Old Man / Traveling Merchant rows retain their original outcomes;
            // this runtime catalog does not admit their combat defaults yet.
            // Vanilla mutates the active conversation peer as well. This current one-actor transaction
            // fences that physical writer instead of drawing its RNG and silently omitting the peer.
            Assert.False(accepted); Assert.True(npcs.TryGet(npc.Handle, out var unchanged)); Assert.Equal(npc, unchanged);
            if (peerActive) { Assert.True(npcs.TryGet(peer.Handle, out unchanged)); Assert.Equal(peer, unchanged); }
            Assert.True(random.HasSameState(before)); return;
        }
        Assert.True(accepted, $"type={type} state={state} lane={lane} projectile={projectile} peer={peerActive}");
        Assert.True(npcs.TryGet(npc.Handle, out var actual));
        Assert.Equal(2ul, actual.Revision.Value); // Reaction and damage share one committed update.
        Assert.Equal(row.GetProperty("life").GetInt32(), actual.Simulation.Life);
        var ai = row.GetProperty("ai");
        Assert.Equal(new(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), actual.Ai);
        Assert.Equal(row.GetProperty("localAi3").GetSingle(), actual.Simulation.LocalAi.Ai3);
        Assert.Equal(row.GetProperty("actualDirection").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(row.GetProperty("vx").GetSingle(), actual.VelocityX);
        Assert.Equal(row.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    private sealed class Lookup : IRuntimePlayerSlotSnapshotLookup
    {
        public readonly PlayerHandle Player = new(new(0), new(1));
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot player)
        {
            player = default(PlayerStateSnapshot) with { Player = Player, Revision = new(1), PositionX = 1000, PositionY = 1000 };
            return slot.Value == 0;
        }
    }
}
