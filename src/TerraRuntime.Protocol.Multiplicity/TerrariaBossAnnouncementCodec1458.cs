using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>NPC boss/event chat using the source localization-key tree and BossOrEvent color.</summary>
public static class TerrariaBossAnnouncementCodec1458
{
    public static byte[] Encode(string key, string? nameKey = null)
    {
        var text = new NetworkText
        {
            TextMode = (byte)NetworkText.Mode.LocalizationKey,
            Text = key,
            SubstitutionList = nameKey is null ? [] :
                [new NetworkText { TextMode = (byte)NetworkText.Mode.LocalizationKey, Text = nameKey, SubstitutionList = [] }]
        };
        var module = new NetTextModule
        {
            PayloadKind = NetTextModulePayloadKind.ServerChatMessage,
            AuthorId = byte.MaxValue,
            ServerText = text,
            MessageColor = new ColorStruct { R = 175, G = 75, B = 255 }
        };
        return (new LoadNetModule { LoadedModule = module }).ToArray();
    }
}
