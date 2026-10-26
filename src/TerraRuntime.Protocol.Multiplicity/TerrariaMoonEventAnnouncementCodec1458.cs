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

    public static byte[] EncodeFirstWave(bool snowMoon) => EncodeWave(snowMoon, 1);

    public static byte[] EncodeWave(bool snowMoon, int wave)
    {
        // NPC.CheckProgress* names the destination wave. Only wave20 is displayed as FinalWave;
        // wave15 is an ordinary numbered wave, despite the temporary-season achievement threshold.
        ReadOnlySpan<short> participants = snowMoon ? FrostWaveParticipants(wave) : PumpkinWaveParticipants(wave);
        NetworkText Key(string key) => new()
            { TextMode = (byte)NetworkText.Mode.LocalizationKey, Text = key, SubstitutionList = [] };
        var substitutions = new NetworkText[participants.Length + 1];
        substitutions[0] = wave switch
        {
            1 => Key("Game.FirstWave"),
            20 => Key("Game.FinalWave"),
            _ => new NetworkText
            {
                TextMode = (byte)NetworkText.Mode.LocalizationKey, Text = "Game.Wave",
                SubstitutionList = [new NetworkText
                {
                    TextMode = (byte)NetworkText.Mode.Literal,
                    Text = wave.ToString(CultureInfo.CurrentCulture), SubstitutionList = []
                }]
            }
        };
        for (int index = 0; index < participants.Length; index++)
            substitutions[index + 1] = Key(ParticipantName(participants[index]));
        return Encode(new NetworkText
        {
            TextMode = (byte)NetworkText.Mode.LocalizationKey,
            Text = "Game.InvasionWave_Type" + participants.Length.ToString(CultureInfo.CurrentCulture),
            SubstitutionList = substitutions
        }, false);
    }

    private static ReadOnlySpan<short> PumpkinWaveParticipants(int wave) => wave switch
    {
        1 => [305], 2 => [326, 305], 3 => [329, 326], 4 => [330, 326, 305],
        5 => [315, 329], 6 => [325, 326, 305], 7 => [325, 330, 329], 8 => [315, 330, 329],
        9 => [325, 330, 329, 326, 305], 10 => [327, 329, 326], 11 => [325, 330, 326],
        12 => [327, 330], 13 => [325, 315, 330, 329, 326], 14 => [327, 325, 315, 330, 329, 326, 305],
        15 => [327, 325, 330, 326, 305], 16 => [327, 315, 330, 329, 326],
        17 => [327, 325, 315, 330, 329], 18 => [327, 325, 315, 330],
        19 or 20 => [327, 325, 315],
        _ => throw new ArgumentOutOfRangeException(nameof(wave))
    };

    private static ReadOnlySpan<short> FrostWaveParticipants(int wave) => wave switch
    {
        1 => [338, 342], 2 => [338, 350], 3 => [338, 350, 342, 348],
        4 => [344, 338, 350, 342], 5 => [344, 338, 350, 348], 6 => [344, 350, 348, 347],
        7 => [346, 342, 350, 338], 8 => [346, 347, 350, 348, 351],
        9 => [346, 344, 348, 347, 342], 10 => [346, 344, 351, 338, 347],
        11 => [345, 352, 338, 342], 12 => [345, 344, 342, 343, 338],
        13 => [345, 346, 342, 352, 343, 347], 14 => [345, 346, 344, 343, 351],
        15 => [345, 346, 344, 343, 347], 16 => [345, 346, 344, 343, 352],
        17 => [345, 346, 344, 343, 351, 347], 18 => [345, 346, 344, 343, 348, 351],
        19 => [345, 346, 344, 343], 20 => [345, 346, 344],
        _ => throw new ArgumentOutOfRangeException(nameof(wave))
    };

    private static string ParticipantName(short type) => type switch
    {
        305 => "NPCName.Scarecrow1", 315 => "NPCName.HeadlessHorseman", 325 => "NPCName.MourningWood",
        326 => "NPCName.Splinterling", 327 => "NPCName.Pumpking", 329 => "NPCName.Hellhound",
        330 => "NPCName.Poltergeist", 338 => "NPCName.ZombieElf", 342 => "NPCName.GingerbreadMan",
        343 => "NPCName.Yeti", 344 => "NPCName.Everscream", 345 => "NPCName.IceQueen",
        346 => "NPCName.SantaNK1", 347 => "NPCName.ElfCopter", 348 => "NPCName.Nutcracker",
        350 => "NPCName.ElfArcher", 351 => "NPCName.Krampus", 352 => "NPCName.Flocko",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

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
