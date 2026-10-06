using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class LavaNpcHitEffect1458Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dedicated_HitEffect_preserves_actual_caller_random_choices(bool lethal)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LavaNpcHitEffect1458");
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int compared = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var original = row.GetProperty("before");
            Assert.False(row.GetProperty("expert").GetBoolean());
            Assert.False(row.GetProperty("remix").GetBoolean());
            Assert.False(row.GetProperty("lowTiles").GetBoolean());
            int life = original.GetProperty("life").GetInt32();
            if ((life <= 0) != lethal) continue;
            var npcs = new RuntimeNpcStore();
            var items = new RuntimeWorldItemStore();
            var source = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
            var simulation = NpcSimulationState.Initial with
            {
                Life = life,
                LifeMax = original.GetProperty("lifeMax").GetInt32(),
                HitboxOverride = new(original.GetProperty("width").GetInt32(), original.GetProperty("height").GetInt32())
            };
            Assert.True(npcs.TrySpawn(0, new(59, 59, original.GetProperty("x").GetSingle(),
                original.GetProperty("y").GetSingle(), 0, 0, 255, default, simulation), out var before));
            var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new EmptyPlayers(),
                new PlayerAuthority(null, null), () => 0, null, new(items), null, null, new(), false, false,
                lootRandom: source);
            string methodName = lethal ? "ExecuteNpcDeathHitEffects" : "ExecuteNpcNonlethalHitEffects";
            MethodInfo method = typeof(RuntimeNpcNetworkCombatPipeline).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.NotNull(method);
            method.Invoke(pipeline, lethal ? [before] : [before, row.GetProperty("damage").GetInt32()]);
            Assert.Equal(row.GetProperty("after").GetProperty("next").GetInt32(), source.Next());
            Assert.True(npcs.TryGet(before.Handle, out var after));
            Assert.Equal(before, after);
            Assert.Equal(0, items.ActiveCount);
            Assert.Empty(row.GetProperty("frames").EnumerateArray());
            compared++;
        }
        Assert.Equal(lethal ? 3 : 6, compared);
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
    }
}
