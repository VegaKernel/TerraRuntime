using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Buffs;

/// <summary>A source-selected effect, before player immunity and AddBuff duration adjustment.</summary>
public readonly record struct NpcContactBuff1458(BuffTypeId Type, int DurationTicks);

/// <summary>TerrariaServer 1.4.5.8 Player.StatusFromNPC and its contact-only slime exception.</summary>
public static class VanillaNpcContactBuffs1458
{
    // A carried stink potion yields two effects, followed by Ice Slime's two own effects.
    public const int MaximumEffects = 4;

    public static bool IsSlimeContactOnly(int type, float ai1) => type == 1 && ai1 == 1125f;

    public static bool IsContactBuffType(BuffTypeId type) => type.Value is
        20 or 22 or 23 or 24 or 30 or 31 or 32 or 33 or 35 or 36 or 39 or 44 or 46 or 47 or 48 or
        67 or 69 or 70 or 80 or 103 or 120 or 148 or 149 or 156;

    /// <summary>
    /// Consumes the existing contact RNG in source order. Call only after a positive, nonlethal committed Hurt.
    /// Immunity is deliberately checked by the buff owner afterwards: an immune player still consumes these draws.
    /// </summary>
    public static int Evaluate(int type, int netId, float ai1, bool expertMode, bool frozen,
        bool goodWorld, Random random, Span<NpcContactBuff1458> effects, bool? windowsPrecision = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (effects.Length < MaximumEffects) throw new ArgumentException("Contact effect span is too small.", nameof(effects));
        int count = 0;
        int id = type;
        if (expertMode && (id == 266 && random.Next(3) == 0 || id == 267))
        {
            int selection = random.Next(9);
            if (selection is 2 or 4) selection = random.Next(9);
            int scaleRoll = random.Next(75, 150);
            float scale = scaleRoll * 0.01f;
            int buff = selection switch { 0 => 20, 1 => 22, 2 => 23, 3 => 30, 4 => 31, 5 => 32, 6 => 33, 7 => 35, _ => 36 };
            int duration = selection switch
            {
                0 or 5 => (int)(60f * scale * 3.5f),
                1 => (int)(60f * scale * 2f),
                2 => (int)(60f * scale * 0.5f),
                3 => (int)(60f * scale * 10f),
                4 or 7 => (int)(60f * scale),
                6 => (int)(60f * scale * 7.5f),
                _ => (int)((double)(60f * scale) * 6.5)
            };
            // The official Windows x86 JIT retains this local in extended precision. Its truncation differs
            // from the Linux float stores; original direct-method captures pin both source editions.
            if (windowsPrecision ?? OperatingSystem.IsWindows())
            {
                double preciseScale = scaleRoll * (double)0.01f;
                duration = (int)(60.0 * preciseScale * (selection switch
                {
                    0 or 5 => 3.5,
                    1 => 2.0,
                    2 => 0.5,
                    3 => 10.0,
                    4 or 7 => 1.0,
                    6 => 7.5,
                    _ => 6.5
                }));
            }
            Add(effects, ref count, buff, duration);
        }
        if (id is 1 or 59 or 147 or 184 or 537 && ai1 > 0f)
        {
            if (ai1 is 364f or 1104f or 365f or 1105f or 366f or 1106f)
            {
                int buff = random.Next(7) switch { 0 => 31, 1 => 35, 2 => 23, 3 => 32, 4 => 80, 5 => 33, _ => 36 };
                Add(effects, ref count, buff, random.Next(60, 301));
                return count;
            }
            if (ai1 == 3f) Add(effects, ref count, 156, random.Next(30, 60));
            else if (ai1 == 150f) Add(effects, ref count, 149, random.Next(30, 60));
            else if (ai1 == 147f) Add(effects, ref count, 30, random.Next(600, 1200));
            else if (ai1 == 8f && goodWorld) Add(effects, ref count, 24, random.Next(180, 300));
            else if (ai1 == 5395f)
            {
                int duration = random.Next(300, 600);
                Add(effects, ref count, 120, duration);
                Add(effects, ref count, 20, expertMode ? duration / 2 : duration);
            }
            else if (ai1 == 174f) Add(effects, ref count, 67, random.Next(60, 180));
            else if (ai1 is 1103f or 593f) Add(effects, ref count, 44, random.Next(180, 360));
            else if (ai1 == 3347f) Add(effects, ref count, 23, random.Next(60, 300));
        }
        if (id is 530 or 531) Add(effects, ref count, 70, random.Next(240, 241));
        if (id is 159 or 158) Add(effects, ref count, 30, random.Next(600, 1200));
        if (id == 525) Add(effects, ref count, 39, 240);
        if (id == 526) Add(effects, ref count, 69, 420);
        if (id == 527) Add(effects, ref count, 31, 840);
        if (expertMode && id is 49 or 93 or 51 or 152 or 634 && random.Next(10) == 0)
            Add(effects, ref count, 148, random.Next(1800, 5400));
        if (expertMode && id == 222) Add(effects, ref count, 20, random.Next(60, 240));
        if (expertMode && id is 210 or 211) Add(effects, ref count, 20, random.Next(60, 180));
        if (expertMode && id == 35) Add(effects, ref count, 30, random.Next(360, 600));
        if (expertMode && id == 36 && random.Next(2) == 0) Add(effects, ref count, 32, random.Next(30, 60));
        if (id is >= 269 and <= 272)
        {
            if (random.Next(3) == 0) Add(effects, ref count, 30, 1200);
            else if (random.Next(3) == 0) Add(effects, ref count, 32, 300);
        }
        if (id is >= 273 and <= 276 && random.Next(2) == 0) Add(effects, ref count, 36, 600);
        if (id is >= 277 and <= 280) Add(effects, ref count, 24, 600);
        if (id == 371) Add(effects, ref count, 103, 60 * random.Next(3, 8));
        if (id == 370 && expertMode)
        {
            int buff = random.Next(3) switch { 1 => 148, 2 => 30, _ => 0 };
            if (buff != 0) Add(effects, ref count, buff, 60 * random.Next(6, 21));
        }
        if ((id == 1 && netId == -6 || id is 81 or 79 or 183 or 630) && random.Next(4) == 0) Add(effects, ref count, 22, 900);
        if (id is 23 or 25 && random.Next(3) == 0) Add(effects, ref count, 24, 420);
        if (id is 34 or 83 or 84 or 179 or 289 && random.Next(3) == 0) Add(effects, ref count, 23, 240);
        if (id is 104 or 102 && random.Next(5) == 0) Add(effects, ref count, 30, 2700);
        if (id == 75 && random.Next(10) == 0) Add(effects, ref count, 35, 420);
        if (id is 163 or 238 or 236 or 237 && random.Next(10) == 0) Add(effects, ref count, 70, 240);
        if (id is 79 or 103 or 630 && random.Next(5) == 0) Add(effects, ref count, 35, 420);
        if (id is 75 or 78 or 82 && random.Next(8) == 0) Add(effects, ref count, 32, 900);
        if (id is 93 or 109 or 80 && random.Next(14) == 0) Add(effects, ref count, 31, 300);
        if (id is >= 305 and <= 314 && random.Next(10) == 0) Add(effects, ref count, 33, 3600);
        if (id == 77 && random.Next(6) == 0) Add(effects, ref count, 36, 7200);
        if (id == 112 && random.Next(20) == 0) Add(effects, ref count, 33, 18000);
        if (id == 182 && random.Next(25) == 0) Add(effects, ref count, 33, 7200);
        if (id == 141 && random.Next(2) == 0) Add(effects, ref count, 20, 600);
        if (id == 147 && !frozen && random.Next(12) == 0) Add(effects, ref count, 46, 600);
        if (id == 150)
        {
            if (random.Next(2) == 0) Add(effects, ref count, 46, 900);
            if (!frozen && random.Next(35) == 0) Add(effects, ref count, 47, 60);
            else if (!frozen && expertMode && random.Next(35) == 0) Add(effects, ref count, 47, 60);
        }
        if (id == 184)
        {
            Add(effects, ref count, 46, 1200);
            if (!frozen && random.Next(15) == 0) Add(effects, ref count, 47, 60);
            else if (!frozen && expertMode && random.Next(25) == 0) Add(effects, ref count, 47, 60);
        }
        return count;
    }

    /// <summary>Player.AddBuff_DetermineBuffTimeToAdd; incoming packet 55 must receive this adjusted duration.</summary>
    public static int ResolveDuration(BuffTypeId type, int rawDuration, bool expertMode, bool masterMode, bool resistCold)
    {
        if (rawDuration <= 0) return 0;
        int duration = rawDuration;
        if (expertMode && VanillaBuffDefinitionCatalog.TryGet(type, out var definition) && definition.TimeIsExtendedWithGameDifficulty)
            duration = (int)((masterMode ? 2.5f : 2f) * duration);
        if (resistCold)
        {
            if (type.Value == 46) duration /= 4;
            else if (type.Value == 47) duration /= 2;
        }
        return duration;
    }

    private static void Add(Span<NpcContactBuff1458> effects, ref int count, int type, int duration) =>
        effects[count++] = new(new BuffTypeId(type), duration);
}
