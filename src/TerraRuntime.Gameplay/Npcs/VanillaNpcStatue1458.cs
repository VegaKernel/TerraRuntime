using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Bounded NPC-producing Wiring.HitWireSingle(105) and NPC.MechSpawn, Terraria 1.4.5.8.</summary>
public static class VanillaNpcStatue1458
{
    public const int Cooldown = 30;

    public static bool TryResolve(int style, bool party, out int type, out int xOffset, out int yOffset)
    {
        type = style switch
        {
            4 => 1, 5 => 73, 7 => 49, 8 => 55, 9 => party ? 540 : 46, 10 => 21,
            13 => 24, 18 => 67, 27 => 85, 30 => 6, 35 => 2, 42 => 58,
            16 => 42, 23 => 63, 28 => 74, 50 => 65,
            51 => 299, 52 => 356, 53 => 357, 54 => 355, 55 => 367, 56 => 359,
            57 => 377, 58 => 300, 59 => 364, 60 => 148, 61 => 361, 62 => 487,
            63 => 164, 64 => 86, 65 => 490, 66 => 82, 67 => 449, 68 => 167,
            69 => 480, 70 => 48, 71 => 170, 72 => 481, 73 => 482, 74 => 430,
            75 => 489, 76 => 611, 77 => 602, 78 => 595, 79 => 616, 80 => 671,
            81 => 673, 82 => 674, _ => 0
        };
        xOffset = style == 7 ? -4 : style == 27 ? -9 : 0;
        yOffset = style == 7 ? -6 : style is 4 or 8 or 9 or 16 or 18 or 23 or 28 or 42 or 50 ? -12 : 0;
        return type != 0;
    }

    // Generic statue selection precedes CheckMech, including refused cooldowns.
    // Bird selection belongs to the later legacy branch, after MechSpawn(74).
    public static int SelectBeforeCooldown(int style, bool party, IVanillaNpcRandom random)
    {
        ReadOnlySpan<int> choices = style switch
        {
            51 => [299, 538], 54 => [355, 358], 55 => [367, 366],
            56 => [359, 359, 359, 359, 360], 59 => [364, 362],
            62 => [487, 486, 485], 71 => [170, 180, 171],
            78 => [595, 596, 599, 597, 600, 598], 79 => [616, 617],
            80 => [671, 672], 82 => [674, 675], _ => []
        };
        return choices.IsEmpty ? TryResolve(style, party, out int type, out _, out _) ? type : 0 :
            choices[random.NextInt32(0, choices.Length)];
    }

    public static int SelectBird(IVanillaNpcRandom random) => random.NextInt32(0, 3) switch { 0 => 74, 1 => 297, _ => 298 };

    public static bool NeedsClearance(int style, out int left, out int right, out int bottom)
    {
        left = style == 16 ? -1 : -2;
        right = style == 16 ? 1 : 3;
        bottom = style == 16 ? 1 : 2;
        return style is 16 or 50 or 64 or 71;
    }

    private static bool SharesPopulation(NpcTypeId requested, NpcTypeId candidate) => requested == candidate ||
        IsBunny(requested) && IsBunny(candidate) ||
        requested.Value is 74 or 297 or 298 && candidate.Value is 74 or 297 or 298 ||
        requested.Value is 362 or 364 && candidate.Value is 362 or 363 or 364 or 365 ||
        requested.Value == 602 && candidate.Value is 602 or 603 ||
        requested.Value is 616 or 617 && candidate.Value is 616 or 617 ||
        requested.Value is >= 595 and <= 601 && candidate.Value is >= 595 and <= 601 ||
        requested == VanillaNpcIds.Goldfish && candidate == VanillaNpcIds.GoldfishWalker;

    private static bool IsBunny(NpcTypeId type) => type == VanillaNpcIds.Bunny || type == VanillaNpcIds.PartyBunny ||
        type == VanillaNpcIds.BunnySlimed || type == VanillaNpcIds.BunnyXmas;

    public static bool CanSpawn(ReadOnlySpan<NpcSnapshot> npcs, float x, float y, int type)
    {
        if (npcs.Length > 200 || !float.IsFinite(x) || !float.IsFinite(y) || type is not (1 or 2 or 6 or 21 or 24 or 42 or 46 or 48 or 49 or 55 or 58 or 63 or 65 or 67 or 73 or 74 or 82 or 85 or 86 or 148 or 164 or 165 or 167 or 170 or 171 or 180 or 297 or 298 or 299 or 300 or 355 or 356 or 357 or 358 or 359 or 360 or 361 or 362 or 364 or 366 or 367 or 377 or 430 or 449 or 480 or 481 or 482 or 485 or 486 or 487 or 489 or 490 or 538 or 540 or 595 or 596 or 597 or 598 or 599 or 600 or 602 or 611 or 616 or 617 or 671 or 672 or 673 or 674 or 675)) return false;
        var requested = new NpcTypeId(type);
        int total = 0, near = 0, middle = 0;
        foreach (var npc in npcs)
        {
            if (!npc.IsActive || !SharesPopulation(requested, npc.TypeIdentity)) continue;
            total++;
            float dx = npc.PositionX - x, dy = npc.PositionY - y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance < 200f) near++;
            if (distance < 600f) middle++;
        }
        return near < 3 && middle < 6 && total < 10;
    }
}
