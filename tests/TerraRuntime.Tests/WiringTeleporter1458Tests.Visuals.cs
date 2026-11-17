using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed partial class WiringTeleporter1458Tests
{
    public static IEnumerable<object[]> VisualRows()
    {
        using var input = typeof(WiringTeleporter1458Tests).Assembly.GetManifestResourceStream("WiringTeleportVisualLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(VisualRows))]
    public void Style_zero_visual_ticks_match_original_time_and_complete_rng_state(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        float time = row.GetProperty("time").GetSingle();
        var authority = new PlayerAuthority(null, null);
        var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(883), session.Handle);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 20, 20, 0, 0, 0, 0, 0))));
        // Independent source midstate import exercises the real retained player clock, not a duplicated formula.
        var history = (WiringPlayerTeleport1458[])typeof(PlayerAuthority).GetField("wiringTeleports", Hidden)!.GetValue(authority)!;
        history[0] = new(connection.Player, time, 0, 0, 4);
        foreach (var point in row.GetProperty("points").EnumerateArray())
        {
            if (row.GetProperty("player").GetBoolean())
            {
                authority.TickWiringTeleportPresentation(random);
                time = authority.CaptureWiringTeleport(connection.Player).Time;
            }
            else time = VanillaWiringTeleport1458.AdvanceNpcVisual(new SystemVanillaNpcRandom(random), time, 0);
            Assert.Equal(point.GetProperty("time").GetSingle(), time);
            AssertRandom(point.GetProperty("random"), random);
        }
    }
    public static IEnumerable<object[]> ConstructorRows()
    {
        using var input = typeof(WiringTeleporter1458Tests).Assembly.GetManifestResourceStream("WiringTeleportVisualLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("constructors").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(ConstructorRows))]
    public void Fresh_history_matches_represented_original_constructors_and_preserves_unknown_state(string observation)
    {
        using var json = JsonDocument.Parse(observation); var row = json.RootElement;
        Assert.Empty(row.GetProperty("exceptions").EnumerateArray());
        var input = new NpcStateUpdate(row.GetProperty("type").GetInt32(), row.GetProperty("netId").GetInt16(), 0, 0, 0, 0, 255, default, NpcSimulationState.Initial);
        var fresh = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in input);
        Assert.Equal(0f, row.GetProperty("time").GetSingle()); Assert.Equal(0, row.GetProperty("style").GetInt32());
        if (VanillaNpcDefinitionCatalog.TryGet(new(input.Type), new(input.NetId), out _))
        {
            Assert.Equal(row.GetProperty("time").GetSingle(), fresh.Simulation.TeleportTime);
            Assert.Equal(row.GetProperty("style").GetInt32(), fresh.Simulation.TeleportStyle);
        }
        else { Assert.Null(fresh.Simulation.TeleportTime); Assert.Null(fresh.Simulation.TeleportStyle); }
        var retained = input with { Simulation = input.Simulation with { TeleportTime = .3f, TeleportStyle = 14 } };
        retained = RuntimeNpcStateOwnershipPolicy.MaterializeSpawnDefaults(in retained);
        Assert.Equal(.3f, retained.Simulation.TeleportTime); Assert.Equal(14, retained.Simulation.TeleportStyle);
    }

}
