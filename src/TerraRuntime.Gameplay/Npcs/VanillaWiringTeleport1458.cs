using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Wiring.Teleport geometry and dedicated Main.TeleportEffect offers, Terraria1.4.5.8.</summary>
public static class VanillaWiringTeleport1458
{
    public static bool IsImmune(NpcTypeId type) => type.Value is 552 or 553 or 554 or 561 or 562 or 563 or
        555 or 556 or 557 or 558 or 559 or 560 or 576 or 577 or 568 or 569 or 566 or 567 or 570 or 571 or
        572 or 573 or 548 or 549 or 564 or 565 or 574 or 575 or 551 or 578;

    // Rectangle.Union comparison is inclusive: touching the platform volume's edge qualifies.
    public static bool Touches(int padX, int padBottom, float x, float y, int width, int height) =>
        float.IsFinite(x) && float.IsFinite(y) && x >= -2147483000f && x <= 2147483000f &&
        y >= -2147483000f && y <= 2147483000f && width is > 0 and <= 128 && height is > 0 and <= 128 &&
        (int)x <= padX + 48 && (int)x + width >= padX && (int)y <= padBottom && (int)y + height >= padBottom - 48;

    public static void ConsumeNpcEffects(IVanillaNpcRandom random, int width, int height, float teleportTime) =>
        ConsumeEffects(random, width, height, teleportTime > 0f ? .3f : 1f);

    public static void ConsumePlayerEffects(IVanillaNpcRandom random, int width, int height, float teleportTime) =>
        ConsumeEffects(random, width, height, Math.Clamp(1f - teleportTime * .99f, .01f, 1f));

    public static float AdvanceNpcVisual(IVanillaNpcRandom random, float time, int style)
    {
        if (!(time > 0f)) return time;
        if (style == 0) _ = random.NextInt32(0, 100);
        return Math.Max(0f, time - .005f);
    }

    private static void ConsumeEffects(IVanillaNpcRandom random, int width, int height, float multiplier)
    {
        int count = (int)((width * height / 5) * multiplier);
        for (int side = 0; side < 2; side++) for (int i = 0; i < count; i++) _ = random.NextInt32(20, 70);
    }
}
