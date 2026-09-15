using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>AI_011's localized red server announcement, encoded through the protocol-326 text module.</summary>
public static class TerrariaSkeletronTauntCodec1458
{
    public static bool TryEncode(int variant, out byte[] encoded)
    {
        string key = variant switch
        {
            2 => "SkeletronText.Taunt2",
            3 => "SkeletronText.Taunt3",
            4 => "SkeletronText.Taunt4",
            5 => "SkeletronText.Taunt5",
            _ => string.Empty
        };
        if (key.Length == 0) { encoded = []; return false; }
        var module = new NetTextModule
        {
            PayloadKind = NetTextModulePayloadKind.ServerChatMessage,
            AuthorId = byte.MaxValue,
            ServerText = new NetworkText
            {
                TextMode = (byte)NetworkText.Mode.LocalizationKey,
                Text = key,
                SubstitutionList = []
            },
            MessageColor = new ColorStruct { R = 255, G = 0, B = 0 }
        };
        encoded = (new LoadNetModule { LoadedModule = module }).ToArray();
        return true;
    }
}
