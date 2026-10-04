using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownSocialNurseWire1458Tests
{
    public static IEnumerable<object[]> SocialCases() => Rows("TownSocialWire1458");
    public static IEnumerable<object[]> NurseCases() => Rows("TownNurseWire1458");
    private static IEnumerable<object[]> Rows(string name)
    {
        using var resource = typeof(RuntimeTownSocialNurseWire1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(SocialCases))]
    public void RPS_emotes_match_actual_original_socket_bytes_and_playing_peers(JsonElement row)
    {
        var registry = new RuntimeNpcReplicationRegistry();
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { family = "frame",
            frame = row.GetProperty("frame").GetInt32(), ownTwo = row.GetProperty("wins").GetInt32(),
            ownThree = row.GetProperty("losses").GetInt32(), seed = row.GetProperty("seed").GetInt32() }));
        var f = new RuntimeTownNpcSocialNurse1458Tests.Fixture(input.RootElement, false, registry);
        var first = Endpoint(registry, null, 10001, 0);
        var second = Endpoint(registry, null, 10002, 1);
        var waiting = Endpoint(registry, null, 10003, 2, playing: false);
        Drain(first); Drain(second);
        Assert.Equal(0, f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat).RejectedCommits);
        byte[][] frames = Drain(first), peers = Drain(second);
        Assert.Equal(frames.Length, peers.Length);
        for (int i = 0; i < frames.Length; i++) Assert.Equal(frames[i], peers[i]);
        byte[][] expected = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
        byte[][] emotes = frames.Where(x => x[2] == 91).ToArray();
        Assert.Equal(expected.Length, emotes.Length);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], emotes[i]);
        Assert.Equal(new byte[] { 91, 91 }, frames.Take(2).Select(x => x[2]).ToArray());
        Assert.Empty(Drain(waiting));
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        var joined = Endpoint(registry, null, 10004, 3);
        Assert.DoesNotContain(Drain(joined), x => x[2] == 91);
    }

    [Theory, MemberData(nameof(NurseCases))]
    public void Healing_destroy_then_body_center_81_matches_original_and_refreshes_join_baseline(JsonElement row)
    {
        float x = row.GetProperty("x").GetSingle();
        int life = row.GetProperty("life").GetInt32();
        var npcRegistry = new RuntimeNpcReplicationRegistry();
        var projectileRegistry = new RuntimeProjectileReplicationRegistry();
        var npcs = new RuntimeNpcStore(commitSink: npcRegistry);
        var resident = new NpcStateUpdate(17, 17, x, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = life, LifeMax = 250 });
        Assert.True(npcs.TrySpawn(0, in resident, out var target));
        var projectiles = new RuntimeProjectileStore(4, commitSink: projectileRegistry);
        var state = new ProjectileStateUpdate(VanillaProjectileIds.NurseSyringeHeal, 255,
            Math.Max(1, x + 1), 456, 8, 0, default, 0, 0, 0, 0);
        Assert.True(projectiles.TrySpawn(0, in state, out _));
        var a = Endpoint(npcRegistry, projectileRegistry, 10011, 0);
        var b = Endpoint(npcRegistry, projectileRegistry, 10012, 1);
        var waiting = Endpoint(npcRegistry, projectileRegistry, 10013, 2, playing: false);
        Drain(a); Drain(b);
        var healing = new RuntimeProjectileNpcHealing1458(npcs, npcRegistry);
        var sink = new RuntimeProjectileSimulationCommitSink(new(4), new(4), npcHealing: healing);
        var summary = new RuntimeProjectileStateExecutor(projectiles, sink, npcs: npcs)
            .Tick(new VanillaProjectileWorldStateStepper(new(new(100, 80)), npcs: npcs));
        Assert.Equal(1, summary.Applied);
        byte[][] frames = Drain(a), peers = Drain(b);
        byte[][] expected = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).ToArray();
        Assert.Equal(expected.Length, frames.Length); Assert.Equal(frames.Length, peers.Length);
        for (int i = 0; i < frames.Length; i++) { Assert.Equal(expected[i], frames[i]); Assert.Equal(frames[i], peers[i]); }
        Assert.Empty(Drain(waiting));
        Assert.True(npcs.TryGet(target.Handle, out var healed));
        Assert.Equal(row.GetProperty("healed").GetInt32(), healed.Simulation.Life);
        // Healing updates retained spawn state, without inventing a simultaneous source netUpdate.
        byte[][] joined = Drain(Endpoint(npcRegistry, projectileRegistry, 10014, 3));
        Assert.DoesNotContain(joined, frame => frame[2] is 29 or 81);
        Assert.Single(joined, frame => frame[2] == 23);
    }

    [Fact]
    public void Stale_heal_never_emits_combat_text_or_changes_a_replacement()
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var table = new RuntimeNpcStore(commitSink: registry);
        var state = new NpcStateUpdate(17, 17, 639, 440, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 200, LifeMax = 250 });
        Assert.True(table.TrySpawn(0, in state, out var old));
        var queue = Endpoint(registry, null, 10021, 0); Drain(queue);
        Assert.True(table.TryDespawn(old.Handle)); Assert.True(table.TrySpawn(0, in state, out var replacement)); Drain(queue);
        Assert.False(new RuntimeProjectileNpcHealing1458(table, registry).TryApply(new(old.Handle, old.Revision, 20)));
        Assert.Empty(Drain(queue)); Assert.True(table.TryGet(replacement.Handle, out var retained)); Assert.Equal(200, retained.Simulation.Life);
    }

    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry npc,
        RuntimeProjectileReplicationRegistry? projectile, long id, byte slot, bool playing = true)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(npc.TryRegister(source, queue)); if (projectile is not null) Assert.True(projectile.TryRegister(source, queue));
        if (playing)
        {
            var handle = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
            var request = new PlayerSpawnCommitRequest(handle.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            npc.PlayerSpawned(handle, in request); projectile?.PlayerSpawned(handle, in request);
        }
        return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        var frames = new List<byte[]>();
        while (owned.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
        return frames.ToArray();
    }
}
