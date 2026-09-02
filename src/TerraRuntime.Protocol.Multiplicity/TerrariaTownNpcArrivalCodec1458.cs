using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// TerrariaServer 1.4.5.8 packet-82 projection for WorldGen.SpawnTownNPC arrival announcements.
/// The outer and type/title text stay as localization keys so every client renders them in its own language.
/// </summary>
public static class TerrariaTownNpcArrivalCodec1458
{
    public const string ArrivalLocalizationKey = "Announcement.HasArrived";
    public const string NpcTitleLocalizationKey = "Game.NPCTitle";
    public const byte ServerAuthorId = byte.MaxValue;
    public static TerrariaRgbColor NpcTravelColor => new(50, 125, 255);

    public static bool TryEncode(int npcType, string givenName, out byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(givenName);
        if (!TryGetNpcTypeLocalizationKey(npcType, out string typeKey) || givenName.IndexOf('\0') >= 0)
        {
            encoded = Array.Empty<byte>();
            return false;
        }

        NetworkText typeName = Key(typeKey);
        NetworkText fullName = givenName.Length == 0
            ? typeName
            : Key(NpcTitleLocalizationKey, Literal(givenName), typeName);
        NetworkText arrival = Key(ArrivalLocalizationKey, fullName);
        TerrariaRgbColor color = NpcTravelColor;
        var module = new NetTextModule
        {
            PayloadKind = NetTextModulePayloadKind.ServerChatMessage,
            AuthorId = ServerAuthorId,
            ServerText = arrival,
            MessageColor = new ColorStruct { R = color.R, G = color.G, B = color.B }
        };
        encoded = (new LoadNetModule { LoadedModule = module }).ToArray();
        return true;
    }

    public static bool TryGetNpcTypeLocalizationKey(int npcType, out string key)
    {
        key = npcType switch
        {
            17 => "NPCName.Merchant",
            18 => "NPCName.Nurse",
            19 => "NPCName.ArmsDealer",
            20 => "NPCName.Dryad",
            22 => "NPCName.Guide",
            37 => "NPCName.OldMan",
            38 => "NPCName.Demolitionist",
            54 => "NPCName.Clothier",
            107 => "NPCName.GoblinTinkerer",
            108 => "NPCName.Wizard",
            124 => "NPCName.Mechanic",
            142 => "NPCName.SantaClaus",
            160 => "NPCName.Truffle",
            178 => "NPCName.Steampunker",
            207 => "NPCName.DyeTrader",
            208 => "NPCName.PartyGirl",
            209 => "NPCName.Cyborg",
            227 => "NPCName.Painter",
            228 => "NPCName.WitchDoctor",
            229 => "NPCName.Pirate",
            353 => "NPCName.Stylist",
            368 => "NPCName.TravellingMerchant",
            369 => "NPCName.Angler",
            441 => "NPCName.TaxCollector",
            453 => "NPCName.SkeletonMerchant",
            550 => "NPCName.DD2Bartender",
            588 => "NPCName.Golfer",
            633 => "NPCName.BestiaryGirl",
            637 => "NPCName.TownCat",
            638 => "NPCName.TownDog",
            656 => "NPCName.TownBunny",
            663 => "NPCName.Princess",
            670 => "NPCName.TownSlimeBlue",
            678 => "NPCName.TownSlimeGreen",
            679 => "NPCName.TownSlimeOld",
            680 => "NPCName.TownSlimePurple",
            681 => "NPCName.TownSlimeRainbow",
            682 => "NPCName.TownSlimeRed",
            683 => "NPCName.TownSlimeYellow",
            684 => "NPCName.TownSlimeCopper",
            _ => string.Empty
        };
        return key.Length != 0;
    }

    private static NetworkText Literal(string text) => new()
    {
        TextMode = (byte)NetworkText.Mode.Literal,
        Text = text,
        SubstitutionList = Array.Empty<NetworkText>()
    };

    private static NetworkText Key(string key, params NetworkText[] substitutions) => new()
    {
        TextMode = (byte)NetworkText.Mode.LocalizationKey,
        Text = key,
        SubstitutionList = substitutions
    };
}
