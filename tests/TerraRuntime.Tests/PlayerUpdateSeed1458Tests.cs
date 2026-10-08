using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Flat;

namespace TerraRuntime.Tests;

public sealed class PlayerUpdateSeed1458Tests
{
    [Fact]
    public void Persisted_text_translation_and_injected_player_stream_match_original_scoped_calls()
    {
        using Stream source = typeof(PlayerUpdateSeed1458Tests).Assembly.GetManifestResourceStream("PlayerUpdateSeed1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        Assert.Equal(14, document.RootElement.GetProperty("rows").GetArrayLength());
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            int seed = WorldGenerationRequest.ResolveVanillaSeed1458(row.GetProperty("text").GetString()!);
            Assert.Equal(row.GetProperty("translated").GetInt32(), seed);
            var npcRandom = new VanillaUnifiedRandom1458(seed);
            var state = new ServerRuntimeState(naturalSpawnRandom: new SystemVanillaNpcRandom(npcRandom),
                playerUpdateRandomSeed: new(seed));
            VanillaUnifiedRandom1458 playerRandom = Assert.IsType<VanillaUnifiedRandom1458>(PlayerRandom(state));
            Assert.NotSame(npcRandom, playerRandom);
            Assert.Equal(row.GetProperty("first").GetInt32(), playerRandom.Next());
            Assert.Equal(row.GetProperty("npcFirst").GetInt32(), npcRandom.Next());
            Assert.Equal(row.GetProperty("second").GetInt32(), playerRandom.Next());
            Assert.True(row.GetProperty("restored").GetBoolean());
            Assert.True(row.GetProperty("samePlayerStream").GetBoolean());
        }
    }

    [Fact]
    public void Missing_seed_remains_unowned_and_explicit_zero_is_a_distinct_bound_stream()
    {
        Assert.Null(PlayerRandom(new ServerRuntimeState()));
        var explicitZero = new ServerRuntimeState(playerUpdateRandomSeed: new(0));
        var random = Assert.IsType<VanillaUnifiedRandom1458>(PlayerRandom(explicitZero));
        Assert.Equal(1559595546, random.Next());
        var players = Composition(explicitZero).Players;
        players.SetPlayerUpdateRandom(random);
        Assert.Throws<InvalidOperationException>(() => players.SetPlayerUpdateRandom(new(0)));
        Assert.Equal(1755192844, random.Next());
        Assert.Throws<ArgumentNullException>(() => WorldGenerationRequest.ResolveVanillaSeed1458(null!));
    }

    [Fact]
    public void Real_world_runtime_uses_exact_loaded_header_seed_text_instead_of_provider_seed()
    {
        foreach (string text in new[] { "desert", "18446744073709551615", "" })
        {
            var source = new SandboxWorldSource.Generated(FlatProvider.GeneratorId, "Player RNG", 1458,
                32, 24, WorldGenerationOptions.Default, text);
            var result = new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits())
                .Materialize(source, CancellationToken.None);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(text, result.World!.Header.SeedText);
            using var world = new WorldRuntime(new(WorldRuntimeId.CreateNew(), WorldSessionId.CreateNew()),
                source, result.World, result.Bootstrap!, new InterestManagementControl(), new WorldRuntimeOptions { MaxPlayers = 2 });
            var actual = Assert.IsType<VanillaUnifiedRandom1458>(PlayerRandom(world.State));
            var expected = new VanillaUnifiedRandom1458(WorldGenerationRequest.ResolveVanillaSeed1458(text));
            Assert.True(actual.HasSameState(expected));
            Assert.False(actual.HasSameState(new VanillaUnifiedRandom1458(1458)));
        }
    }

    private static ServerRuntimeComposition Composition(ServerRuntimeState state) =>
        (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;

    private static VanillaUnifiedRandom1458? PlayerRandom(ServerRuntimeState state) =>
        (VanillaUnifiedRandom1458?)typeof(PlayerAuthority).GetField("playerUpdateRandom", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Composition(state).Players);
}
