using System.Buffers.Binary;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Tiles;
using Xunit;

namespace TerraRuntime.Tests.Tools1458;

internal static partial class AllLocalToolSourceChecks
{
    private static void CheckFinalPowerAndShovel(string platform, string repeat)
    {
        foreach (bool active in new[] { false, true })
        {
        using var document = Read(active ? "ActivePower" : "FinalPower", platform, repeat);
        Assert.Equal(6032, document.RootElement.GetArrayLength());
        foreach (var row in document.RootElement.EnumerateArray())
        {
            int type = I(row, "type"), prior = I(row, "prior");
            context = $"final-pick/{type}/{prior}/{B(row, "good")}/{B(row, "respect")}";
            var hits = ToolHitState1458.Constructor;
            Assert.True(hits.TryHitObject(50, 50, 1, out hits, out int slot));
            if (prior > 0) Assert.True(hits.TryAddDamage(slot, prior, true, out hits, out _, out _));
            var window = new ToolTargetTile1458[25];
            window[12] = new(new(type), 0, 0, active, 0);
            Assert.True(VanillaToolDamage1458.TryGetPickDamage(55, 50, 50, window,
                new(100, 100, 80d, 0, B(row, "good"), false, false, false, false), out var preliminary));
            Assert.True(VanillaToolDamage1458.TryFinalizePickDamage(preliminary, new(type),
                B(row, "canKill"), B(row, "good"), B(row, "respect"), hits, slot, 55, -1,
                out int damage));
            Assert.Equal(I(row, "slot"), slot);
            Assert.Equal(I(row, "damage"), damage);
            var random = Random(row.GetProperty("beforeRandom"));
            AssertRandom(row.GetProperty("afterRandom"), random);
        }
        }
        // Independent literal client frames qualify the source X-major 3x3
        // shovel order, including inactive Air calls and a skipped stone cell.
        using var behavior = Read("ItemCheck", platform, repeat);
        var source = behavior.RootElement.EnumerateArray().Single(row => I(row, "item") == 4711 &&
            B(row, "local") && row.GetProperty("mode").GetString() == "ordinary");
        var shovelWindow = new ToolTargetTile1458[9];
        shovelWindow[4] = new(new(1), 0, 0, true, 0);
        Span<ToolShovelHit1458> calls = stackalloc ToolShovelHit1458[18];
        Assert.True(VanillaToolDamage1458.TryGetShovelHits(shovelWindow, calls, out int count));
        Assert.Equal(8, count);
        var frames = source.GetProperty("steps")[0].GetProperty("frames").EnumerateArray()
            .Select(frame => Convert.FromHexString(frame.GetString()!)).Where(frame => frame[2] == 17).ToArray();
        Assert.Equal(count, frames.Length);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(26 + calls[i].OffsetX, BinaryPrimitives.ReadInt16LittleEndian(frames[i].AsSpan(4)));
            Assert.Equal(26 + calls[i].OffsetY, BinaryPrimitives.ReadInt16LittleEndian(frames[i].AsSpan(6)));
            Assert.Equal(30, calls[i].PickPower);
        }
    }
}
