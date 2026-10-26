using System.Globalization;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Models;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Source Moon score and temporary-season messages; callers publish only committed dawn effects.</summary>
public static class TerrariaMoonEventAnnouncementCodec1458
{
    public static byte[] EncodeInvocation(short action, bool remixWorld = false) => Encode(action switch
    {
        -4 => "LegacyMisc.31",
        -5 => "LegacyMisc.34",
        -6 => remixWorld ? "LegacyMisc.106" : "LegacyMisc.20",
        -10 => "LegacyMisc.8",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    }, true, null);

    public static byte[] EncodeFirstWave(bool snowMoon)
    {
        // Lang.GetInvasionWaveText(1,305) / (1,338,342), called by Main.start*Moon.
        NetworkText Key(string key) => new()
            { TextMode = (byte)NetworkText.Mode.LocalizationKey, Text = key, SubstitutionList = [] };
        return Encode(new NetworkText
        {
            TextMode = (byte)NetworkText.Mode.LocalizationKey,
            Text = snowMoon ? "Game.InvasionWave_Type2" : "Game.InvasionWave_Type1",
            SubstitutionList = snowMoon
                ? [Key("Game.FirstWave"), Key("NPCName.ZombieElf"), Key("NPCName.GingerbreadMan")]
                : [Key("Game.FirstWave"), Key("NPCName.Scarecrow1")]
        }, false);
    }

    public static byte[] EncodeScore(bool snowMoon, float totalPoints)
    {
        if (!float.IsFinite(totalPoints) || totalPoints < 0f)
            throw new ArgumentOutOfRangeException(nameof(totalPoints));
        // NetworkText.FromKey converts the original float substitution through object.ToString().
        return Encode(snowMoon ? "Misc.FrostMoonScore" : "Misc.PumpkinMoonScore", false,
            totalPoints.ToString(CultureInfo.CurrentCulture));
    }

    public static byte[] EncodeSeasonChange(bool christmas, bool active) => Encode(
        christmas
            ? active ? "Misc.StartedVictoryXmas" : "Misc.EndedVictoryXmas"
            : active ? "Misc.StartedVictoryHalloween" : "Misc.EndedVictoryHalloween", true, null);

    private static byte[] Encode(string key, bool worldColor, string? score)
    {
        var text = new NetworkText
        {
            TextMode = (byte)NetworkText.Mode.LocalizationKey,
            Text = key,
            SubstitutionList = score is null ? [] :
                [new NetworkText { TextMode = (byte)NetworkText.Mode.Literal, Text = score, SubstitutionList = [] }]
        };
        return Encode(text, worldColor);
    }

    private static byte[] Encode(NetworkText text, bool worldColor)
    {
        var module = new NetTextModule
        {
            PayloadKind = NetTextModulePayloadKind.ServerChatMessage,
            AuthorId = byte.MaxValue,
            ServerText = text,
            MessageColor = worldColor
                ? new ColorStruct { R = 50, G = 255, B = 130 }
                : new ColorStruct { R = 175, G = 75, B = 255 }
        };
        return (new LoadNetModule { LoadedModule = module }).ToArray();
    }
}
