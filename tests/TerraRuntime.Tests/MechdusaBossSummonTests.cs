using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MechdusaBossSummonTests
{
    [Fact]
    public void Zenith_packet_61_minus_16_creates_the_source_ordered_mechanical_batch()
    {
        var npcs = new RuntimeNpcStore();
        var state = new ServerRuntimeState(
            npcs: npcs,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { ZenithWorld = true });
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle player = new(GameCommandSourceId.FromConnection(1458), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(
            player, session, new PlayerSpawnCommitRequest(session.Slot, 200, 120, 0, 0, 0, 0, 0)));

        state.Apply(new ClientBossSummonRuntimeCommand(player, -16));

        NpcSnapshot[] active = new NpcSnapshot[10];
        Assert.Equal(6, npcs.CopyActive(active));
        NpcSnapshot[] batch = active.Take(6).ToArray();
        NpcSnapshot prime = Single(batch, VanillaNpcIds.SkeletronPrime);
        NpcSnapshot retinazer = Single(batch, VanillaNpcIds.Retinazer);
        NpcSnapshot spazmatism = Single(batch, VanillaNpcIds.Spazmatism);
        NpcSnapshot destroyer = Single(batch, VanillaNpcIds.Destroyer);
        NpcSnapshot[] probes = batch.Where(static npc => npc.TypeIdentity == VanillaNpcIds.Probe).ToArray();
        Assert.Equal(2, probes.Length);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(prime.TypeIdentity, prime.NetIdentity, out VanillaNpcDefinition definition));
        Assert.True(definition.TryResolveHitbox(prime.Simulation, out VanillaNpcHitboxSize hitbox));
        float x = (int)(prime.PositionX + hitbox.Width * 0.5f);
        float y = (int)(prime.PositionY + hitbox.Height * 0.5f);
        foreach (NpcSnapshot part in new[] { retinazer, spazmatism, destroyer }.Concat(probes))
        {
            Assert.Equal(x, part.PositionX);
            Assert.Equal(y, part.PositionY);
            Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, part.Target);
        }

        Assert.All(probes, probe => Assert.Equal(destroyer.Handle.Slot, probe.Ai.Ai2));
        Assert.Equal([-1f, 1f], probes.Select(static probe => probe.Ai.Ai3).Order());

        // NPC.SpawnMechQueen first rejects any active Prime, Destroyer or Twin.
        state.Apply(new ClientBossSummonRuntimeCommand(player, -16));
        Assert.Equal(6, npcs.CopyActive(active));
    }

    [Fact]
    public void Packet_61_minus_16_is_ignored_outside_a_zenith_world()
    {
        var npcs = new RuntimeNpcStore();
        var state = new ServerRuntimeState(
            npcs: npcs,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle player = new(GameCommandSourceId.FromConnection(1459), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(
            player, session, new PlayerSpawnCommitRequest(session.Slot, 200, 120, 0, 0, 0, 0, 0)));

        state.Apply(new ClientBossSummonRuntimeCommand(player, -16));

        Assert.Equal(0, npcs.ActiveCount);
    }

    private static NpcSnapshot Single(NpcSnapshot[] snapshots, NpcTypeId type) =>
        Assert.Single(snapshots, npc => npc.TypeIdentity == type);

    private static PlayerJoinSession CreatePlayingSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }
}
