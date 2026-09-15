using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class MoonLordClotSpawnTests
{
    private static readonly JsonElement[] Rows = ReadCases();
    public static TheoryData<int> Cases => new(Enumerable.Range(0, 40));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Head_creation_matches_original_given_anchor_and_buff_query_facts(int index)
    {
        var row = Rows[index];
        var store = new RuntimeNpcStore(8);
        var core = new NpcStateUpdate(398, 398, 1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in core, out _));
        var head = new NpcStateUpdate(396, 396, 900, 900, 2, -3, 0,
            new NpcAiState(0, row.GetProperty("tick").GetInt32(), 0, 0),
            NpcSimulationState.Initial with { LocalAi = new NpcAiState(0, 0, 0, 15) });
        Assert.True(store.TrySpawn(1, in head, out _));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetProjectileAnchors(new ReferenceAnchorFacts(row.GetProperty("mode").GetInt32()));
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        new RuntimeNpcAiStateExecutor(store).Tick(new HeadOnly(vanilla));
        var expected = row.GetProperty("blobs");
        Assert.Equal(expected.GetArrayLength() + 2, store.ActiveCount);
        foreach (var blob in expected.EnumerateArray())
        {
            Assert.True(store.TryGetActive(blob.GetProperty("slot").GetByte(), out var actual));
            Assert.Equal(VanillaNpcIds.MoonLordLeechBlob, actual.TypeIdentity);
            Assert.Equal(blob.GetProperty("x").GetSingle(), actual.PositionX);
            Assert.Equal(blob.GetProperty("y").GetSingle(), actual.PositionY);
            Assert.Equal(blob.GetProperty("head").GetSingle(), actual.Ai.Ai0);
            Assert.Equal(blob.GetProperty("key").GetUInt32(), BitConverter.SingleToUInt32Bits(actual.Ai.Ai1));
            Assert.Equal(blob.GetProperty("hidden").GetBoolean(), actual.Simulation.Hidden);
            Assert.Equal((ushort)255, actual.Target);
        }
    }

    [Fact]
    public void Thousand_live_anchors_fill_remaining_capacity_without_rejecting_head_and_new_clot_steps_in_same_pass()
    {
        var players = new PlayerAuthority(null, null);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(70),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var buff = new PlayerBuffTypesCommitRequest(connection.Player.Slot, new BuffTypeId[] { VanillaBuffIds.MoonLeech });
        Assert.True(players.TryApply(new PlayerBuffTypesRuntimeCommand(connection, buff)));
        var projectiles = new RuntimeProjectileStore(1000);
        var identities = new RuntimeProjectileWireIdentityRegistry(1000);
        var projectile = new ProjectileStateUpdate(VanillaProjectileIds.MoonLeech, 255, 1200, 1000, 0, 0,
            new ProjectileAiState(-6, 0, 0), 0, 0, 0, 0);
        for (ushort i = 0; i < 1000; i++)
        {
            Assert.True(projectiles.TrySpawn(i, in projectile, out var shot));
            var key = new TerrariaProjectileKeyState(255, i, 1);
            Assert.True(identities.TryBind(in key, shot.Handle));
        }
        var store = new RuntimeNpcStore(4);
        var core = new NpcStateUpdate(398, 398, 1000, 1000, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in core, out _));
        var head = new NpcStateUpdate(396, 396, 900, 900, 0, 0, 0, new NpcAiState(0, 329, 0, 0), NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(1, in head, out _));
        var occupied = new NpcStateUpdate(1, 1, 100, 100, 0, 0, 0, default, NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(2, in occupied, out _));
        var vanilla = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        vanilla.SetCandidates([new VanillaNpcTargetCandidate(0, 1510, 821, 0, true, false, false, false)]);
        vanilla.SetProjectileAnchors(new RuntimeNpcProjectileAnchors(projectiles, identities, players));
        var result = new RuntimeNpcAiStateExecutor(store).Tick(new HeadAndClot(vanilla));
        Assert.Equal(0, result.Rejected);
        Assert.Equal(2, result.Applied);
        Assert.Equal(4, store.ActiveCount);
        Assert.True(store.TryGetActive(3, out var clot));
        Assert.Equal(VanillaNpcIds.MoonLordLeechBlob, clot.TypeIdentity);
        Assert.Equal(1f, clot.Ai.Ai2);
        Assert.Equal(0x000400ffu, BitConverter.SingleToUInt32Bits(clot.Ai.Ai1));
    }

    private sealed class HeadAndClot(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return (npc.TypeIdentity == VanillaNpcIds.MoonLordHead || npc.TypeIdentity == VanillaNpcIds.MoonLordLeechBlob) &&
                inner.TryStepState(in npc, out next);
        }
    }

    private sealed class HeadOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return npc.TypeIdentity == VanillaNpcIds.MoonLordHead && inner.TryStepState(in npc, out next);
        }
    }

    // These are the probe's read-only projectile/player inputs, not an alternate head simulation.
    // Application-level selection of real projectile slots and packet-50 state is tested separately.
    private sealed class ReferenceAnchorFacts(int mode) : IVanillaNpcProjectileAnchorLookup
    {
        public bool TryGetProjectile(float keyBits, out ProjectileSnapshot projectile) { projectile = default; return false; }
        public bool TryGetHealingAnchor(ushort physicalSlot, out float keyBits)
        {
            keyBits = default;
            if (mode is 0 or 3 or 4 || physicalSlot >= (mode == 5 ? 3 : 1)) return false;
            var buffs = new PlayerBuffState();
            if (mode != 6) buffs.ReplaceNetworkSnapshot([VanillaBuffIds.MoonLeech]);
            if (!buffs.Contains(VanillaBuffIds.MoonLeech, immune: mode == 7)) return false;
            uint bits = 255u | ((uint)physicalSlot << 8) | ((mode == 5 ? 8160u : 1u) << 18);
            keyBits = BitConverter.UInt32BitsToSingle(bits);
            return true;
        }
    }

    private static JsonElement[] ReadCases()
    {
        using Stream resource = typeof(MoonLordClotSpawnTests).Assembly.GetManifestResourceStream("MoonLordClotSpawn1458")!;
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        Assert.Equal("0efd2e88193845b4a8d931eba93c2688a66eb326935b6874c5d4a73a6e7532e1",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
