using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class NpcHealPacketTests
{
    [Fact]
    public void Numeric_combat_text_matches_original_NPC_HealEffect_packets()
    {
        using Stream resource = typeof(NpcHealPacketTests).Assembly.GetManifestResourceStream("NpcHealPacket1458")!;
        using var bytes = new MemoryStream(); resource.CopyTo(bytes);
        Assert.Equal("00ca7b0b22ae766f6c58a6d160957dc00392a4177a7f78958837f216a9cad7cc",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        foreach (var row in json.RootElement.EnumerateArray())
        {
            // NPC.HealEffect receives a Rectangle; its Center uses integer half dimensions.
            int x = row.GetProperty("x").GetInt32() + row.GetProperty("width").GetInt32() / 2;
            int y = row.GetProperty("y").GetInt32() + row.GetProperty("height").GetInt32() / 2;
            byte[] actual = TerrariaCombatTextCodec.EncodeNumber(x, y, row.GetProperty("amount").GetInt32(),
                new TerrariaRgbColor(100, 255, 100));
            Assert.Equal(row.GetProperty("packet").GetString(), Convert.ToHexString(actual));
        }
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(0f, float.PositiveInfinity)]
    public void Numeric_combat_text_rejects_nonfinite_world_coordinates(float x, float y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaCombatTextCodec.EncodeNumber(x, y, 1, default));
}
