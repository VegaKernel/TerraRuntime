using System.Text.Json;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcDeathPreludeWire1458Tests
{
    [Theory]
    [MemberData(nameof(WireRows))]
    public void Module_bytes_match_independent_original_serializers(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        byte[] actual;
        string kind = row.GetProperty("kind").GetString()!;
        short banner = row.TryGetProperty("banner", out var b) ? b.GetInt16() : (short)0;
        switch (kind)
        {
            case "claimRequest": actual = TerrariaNpcDeathPreludeCodec1458.EncodeClaimRequest(banner, row.GetProperty("amount").GetUInt16()); break;
            case "claimResponse": actual = TerrariaNpcDeathPreludeCodec1458.EncodeClaimResponse(banner, row.GetProperty("amount").GetUInt16(), row.GetProperty("granted").GetBoolean()); break;
            case "killCount": actual = TerrariaNpcDeathPreludeCodec1458.EncodeBannerKill(banner, row.GetProperty("kills").GetInt32()); break;
            case "claimCount": actual = TerrariaNpcDeathPreludeCodec1458.EncodeBannerClaimCount(banner, row.GetProperty("amount").GetUInt16()); break;
            case "bestiaryKill": actual = TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(row.GetProperty("netId").GetInt16(), row.GetProperty("kills").GetInt32()); break;
            case "bestiarySight": actual = TerrariaNpcDeathPreludeCodec1458.EncodeBestiarySight(row.GetProperty("netId").GetInt16()); break;
            case "bestiaryChat": actual = TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryChat(row.GetProperty("netId").GetInt16()); break;
            case "full":
                var kills = new int[293]; var claims = new ushort[293];
                foreach (int id in new[] { 0, 1, 17, 291, 292 }) { kills[id] = int.MaxValue; claims[id] = int.MaxValue % 10000; }
                actual = TerrariaNpcDeathPreludeCodec1458.EncodeBannerFull(kills, claims); break;
            default: throw new InvalidDataException(kind);
        }
        Assert.Equal(Convert.FromHexString(row.GetProperty("hex").GetString()!), actual);
    }

    [Theory]
    [MemberData(nameof(BannerRows))]
    public void Announcement_identity_and_threshold_match_original_banner_inverse(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.True(VanillaBannerCatalog1458.TryGet(row.GetProperty("id").GetInt32(), out var facts));
        Assert.Equal(row.GetProperty("sourceItem").GetInt32(), facts.ItemId);
        Assert.Equal(row.GetProperty("threshold").GetInt32(), facts.KillsNeeded);
        Assert.Equal(row.GetProperty("nameKey").GetString(), facts.NameKey);
    }
    public static IEnumerable<object[]> WireRows() => NpcDeathPrelude1458Tests.Rows("wire");
    public static IEnumerable<object[]> BannerRows() => NpcDeathPrelude1458Tests.Rows("banners");

    [Theory]
    [MemberData(nameof(AchievementRows))]
    public void Targeted_achievement_matches_original_NetMessage_SendData97(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        Assert.Equal(Convert.FromHexString(row.GetProperty("hex").GetString()!),
            TerrariaNpcDeathPreludeCodec1458.EncodeNpcKillAchievement(row.GetProperty("netId").GetInt16()));
    }
    public static IEnumerable<object[]> AchievementRows() => NpcDeathPrelude1458Tests.Rows("achievement-wire");
}
