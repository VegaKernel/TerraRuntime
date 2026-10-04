using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;
namespace TerraRuntime.Protocol.Multiplicity;

public static class TerrariaBannerAnnouncementCodec1458
{
    public static byte[] Encode(int kills, string npcNameKey, int playerSlot, string? playerName)
    {
        var count = new NetworkText { TextMode = (byte)NetworkText.Mode.Literal, Text = kills.ToString(System.Globalization.CultureInfo.InvariantCulture), SubstitutionList = [] };
        var npc = new NetworkText { TextMode = (byte)NetworkText.Mode.LocalizationKey, Text = npcNameKey, SubstitutionList = [] };
        bool attributed = playerSlot is >= 0 and < 255;
        var text = new NetworkText { TextMode = (byte)NetworkText.Mode.LocalizationKey,
            Text = attributed ? "Game.EnemiesDefeatedByAnnouncement" : "Game.EnemiesDefeatedAnnouncement",
            SubstitutionList = attributed ? [new NetworkText { TextMode = (byte)NetworkText.Mode.Literal, Text = playerName ?? "", SubstitutionList = [] }, count, npc] : [count, npc] };
        var module = new NetTextModule { PayloadKind = NetTextModulePayloadKind.ServerChatMessage, AuthorId = byte.MaxValue,
            ServerText = text, MessageColor = new ColorStruct { R = 250, G = 250, B = 0 } };
        return (new LoadNetModule { LoadedModule = module }).ToArray();
    }
}
