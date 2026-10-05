using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class NpcUndeadHitEffects1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(NpcUndeadHitEffects1458Tests).Assembly.GetManifestResourceStream("NpcUndeadHitEffects1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_strike_routes_preserve_dedicated_hit_effect_life_and_shared_rng(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32(), damage = row.GetProperty("damage").GetInt32();
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var npcs = new RuntimeNpcStore(); var items = new RuntimeWorldItemStore();
        var players = new PlayerAuthority(null, null); var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquireConnection(out var lease)); using var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(88171), session.Handle);
        players.TryApply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
        players.TryApply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 40, 30, 0, 0, 0, 0, 0)));
        Assert.True(npcs.TrySpawnVanilla(new((short)type, (short)type, 600, 400, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DefenseOverride = 0, MoneyValue = 0,
                DontTakeDamage = row.GetProperty("blocked").GetBoolean() }), out var npc));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new RuntimePlayerSnapshotLookup(players, null),
            players, () => 0, null, new(items), null, null, new(), false, false,
            lootRandom: random, npcSpecificLowTiles: () => false);
        string route = row.GetProperty("route").GetString()!;
        bool equipped = row.GetProperty("equipped").GetBoolean();
        if (equipped)
        {
            Assert.Equal(39, row.GetProperty("effectiveHeld").GetInt32());
            Assert.Equal(1, row.GetProperty("effectiveStack").GetInt32());
            Assert.Equal(20, row.GetProperty("animation").GetInt32());
            players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new(session.Slot, 0, 1, 0, 39, 0)));
            players.TryApply(new PlayerItemAnimationRuntimeCommand(connection, 0, 20));
        }
        if (route == "client")
            pipeline.TryApply(connection, new TerrariaNpcDamageState(npc.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(npc.Handle.Generation),
                (short)Math.Min(damage, short.MaxValue), 0, 1, 0));
        else if (route == "player")
            pipeline.TryStrikeServerPlayerMelee(connection.Player, npc.Handle, damage, 0, false, 0, 1);
        else pipeline.TryStrikeEnvironment(npc.Handle, damage);
        if (row.GetProperty("blocked").GetBoolean() || (route == "client" && !equipped))
        {
            // Direct source StrikeNPC bypasses upstream dontTakeDamage admission. This runtime's
            // admitted combat boundary is narrower; an unrepresented selected item is also rejected.
            // Such callers must not execute its HitEffect. Equipped bow callbacks use the existing
            // ranged compatibility lane; this does not admit complete item/projectile damage authority.
            Assert.True(npcs.TryGet(npc.Handle, out var unchanged)); Assert.Equal(npc, unchanged);
            Assert.Equal(row.GetProperty("originalNext").GetInt32(), random.Next());
            return;
        }
        if (row.GetProperty("active").GetBoolean())
        {
            Assert.True(npcs.TryGet(npc.Handle, out var after));
            Assert.Equal(row.GetProperty("life").GetInt32(), after.Simulation.Life);
        }
        else Assert.False(npcs.TryGet(npc.Handle, out _));
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }
}
