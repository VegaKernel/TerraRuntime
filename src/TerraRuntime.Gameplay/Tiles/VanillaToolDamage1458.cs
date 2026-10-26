using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Gameplay.Tiles;

public readonly record struct ToolTargetTile1458(TileTypeId Type, short FrameX, short FrameY,
    bool Active, ushort Wall);

public readonly record struct ToolDamageWorld1458(int Width, int Height, double WorldSurface,
    int UnderworldLayer, bool GetGoodWorld, bool InfectedSeed, bool NotTheBeesWorld,
    bool NoTrapsWorld, bool TenthAnniversaryWorld);

public readonly record struct ToolPickDamage1458(int Damage, int HitX, int HitY,
    bool UpdateHitPosition, bool BlockMouse);

public readonly record struct ToolShovelHit1458(int OffsetX, int OffsetY, int PickPower);

/// <summary>Local tool damage arithmetic. Position-dependent kill permissions remain a separate input.</summary>
public static class VanillaToolDamage1458
{
    public static bool IsTilePoundable(TileTypeId type) =>
        VanillaToolTileCatalog1458.TryGet(type, out var facts) && !facts.IsHammerTarget &&
        (facts.IsSolid || facts.IsLivingFire || type.Value is 314 or 424 or 442 or 351 or 470);

    public static bool IsAHammerTarget(TileTypeId type) =>
        VanillaToolTileCatalog1458.TryGet(type, out var facts) &&
        (facts.IsHammerTarget || IsTilePoundable(type));

    public const int TargetWindowSide = 5;
    public const int TargetWindowCount = TargetWindowSide * TargetWindowSide;

    public static bool CanPickTarget(TileTypeId type) =>
        type.Value < VanillaToolTileCatalog1458.TileCount && type.Value != 504;

    // The row-major window is centered on x/y. Anchor reads are part of this
    // bounded input, so computing the result cannot invoke a mutable world query.
    public static bool TryGetPickDamage(int power, int x, int y,
        ReadOnlySpan<ToolTargetTile1458> window, in ToolDamageWorld1458 world,
        out ToolPickDamage1458 result)
    {
        result = default;
        if (power is < 0 or > 1000 || window.Length != TargetWindowCount ||
            world.Width < 5 || world.Height < 5 || x < 2 || y < 2 ||
            x >= world.Width - 2 || y >= world.Height - 2 ||
            !double.IsFinite(world.WorldSurface)) return false;
        foreach (var tile in window)
            if (!VanillaToolTileCatalog1458.TryGet(tile.Type, out _)) return false;
        var target = window[12];
        VanillaToolTileCatalog1458.TryGet(target.Type, out var roles);
        int type = target.Type.Value;
        int damage = roles.NoFail ? 100 : 0;
        if (roles.IsDungeon || type is 58 or 25 or 117 or 203) damage += power / 2;
        else if (type == 85) damage += world.GetGoodWorld ? power / 4 : power;
        else if (type is 48 or 232 || roles.IsCloud) damage += power * 2;
        else if (type is 226 or 111 or 223) damage += power / 4;
        else if (type is 107 or 221) damage += power / 2;
        else if (type is 108 or 222) damage += power / 3;
        else if (type == 211) damage += power / 5;
        else damage += power;
        if (type == 211 && power < 200) damage = 0;
        if ((!world.InfectedSeed && type is 25 or 203 && power < 65) ||
            type == 117 && power < 65 || type == 37 && power < 50 ||
            type is 22 or 204 && y > world.WorldSurface && power < 55 ||
            type == 56 && power < 55 || type == 77 && power < 65 && y >= world.UnderworldLayer ||
            type == 58 && power < 65 || type is 226 or 237 && power < 210)
            damage = 0;
        else if (type == 137 && power < 210 &&
            (!world.NotTheBeesWorld || !world.NoTrapsWorld || world.TenthAnniversaryWorld))
        {
            int frame = target.FrameY / 18;
            if ((uint)(frame - 1) <= 3) damage = 0;
        }
        else if (roles.IsDungeon && power < 100 && y > world.WorldSurface)
        {
            if (x < world.Width * 0.35 || x > world.Width * 0.65) damage = 0;
        }
        else if (type is 107 or 221 && power < 100 ||
            type is 108 or 222 && power < 110 || type is 111 or 223 && power < 150)
            damage = 0;
        if (type is 147 or 0 or 40 or 53 or 57 or 59 or 123 or 224 or 397) damage += power;
        if (type == 404) damage += 5;
        if (type is 165 or 199 || roles.IsRope) damage = 100;

        int relativeX = 0, relativeY = 0, hitX = x, hitY = y;
        bool moved = false, blocked = false;
        if (type is 128 or 269)
        {
            if (target.FrameX is 18 or 54)
            {
                relativeX--;
                target = window[12 + relativeX];
                hitX = x + relativeX;
                moved = true;
            }
            if (target.FrameX >= 100) { damage = 0; blocked = true; }
        }
        if (target.Type.Value == 334)
        {
            if (target.FrameY == 0)
            {
                relativeY++;
                target = window[12 + relativeY * 5 + relativeX];
                hitX = x + relativeX;
                hitY = y + relativeY;
                moved = true;
            }
            if (target.FrameY == 36)
            {
                relativeY--;
                target = window[12 + relativeY * 5 + relativeX];
                hitX = x + relativeX;
                hitY = y + relativeY;
                moved = true;
            }
            if (target.FrameX < 5000)
            {
                relativeX -= target.FrameX / 18 % 3;
                // An invalid mixed-object window can exceed the source's valid
                // anchor footprint. Refuse before any state/RNG is adopted.
                if (relativeX is < -2 or > 2 || relativeY is < -2 or > 2) return false;
                target = window[12 + relativeY * 5 + relativeX];
            }
            if (target.FrameX >= 5000) { damage = 0; blocked = true; }
        }
        result = new(damage, hitX, hitY, moved, blocked);
        return true;
    }

    public static bool TryGetAxeDamage(int power, TileTypeId type, bool getGoodWorld,
        bool canKillTile, out int damage)
    {
        damage = 0;
        if (power is < 0 or > 1000 || !VanillaToolTileCatalog1458.TryGet(type, out var roles))
            return false;
        damage = (roles.NoFail ? 100 : 0) + (int)((type.Value == 80 ? power * 3 : power) * 1.2f);
        if (getGoodWorld) damage = (int)(damage * 1.3);
        if (!canKillTile) damage = 0;
        return true;
    }

    public static bool TryFinalizePickDamage(in ToolPickDamage1458 preliminary,
        TileTypeId originalType, bool canKillTile, bool getGoodWorld, bool respectTransformingTiles,
        ToolHitState1458 hits, int hitSlot, int pickPower, int dealDamageAsIfBaseNumberIs,
        out int damage)
    {
        damage = 0;
        if (preliminary.Damage is < 0 or > 10000 || pickPower is < 1 or > 1000 ||
            dealDamageAsIfBaseNumberIs is < -1 or > 1000 || hits is null ||
            !VanillaToolTileCatalog1458.TryGet(originalType, out var roles)) return false;
        damage = canKillTile ? preliminary.Damage : 0;
        if (getGoodWorld) damage *= 2;
        if (respectTransformingTiles &&
            (originalType.Value is 2 or 477 or 492 or 23 or 60 or 70 or 109 or 199 or 662 or 661 or 633 ||
                roles.IsMoss || roles.IsMossBrick))
        {
            if (!hits.TryAddDamage(hitSlot, damage, false, out _, out int projected, out _)) return false;
            if (projected >= 100) damage = 0;
        }
        if (damage > 0 && dealDamageAsIfBaseNumberIs != -1)
        {
            float ratio = damage / (float)pickPower;
            damage = Math.Min(damage, (int)(dealDamageAsIfBaseNumberIs * ratio));
        }
        return true;
    }

    // This is the nominal ordered source call list, not a promise that the
    // world remains unchanged between two hits on the same cell. The caller
    // evaluates each hit against its prepared working world in this order.
    public static bool TryGetShovelHits(ReadOnlySpan<ToolTargetTile1458> window,
        Span<ToolShovelHit1458> destination, out int count)
    {
        count = 0;
        if (window.Length != 9 || destination.Length < 18) return false;
        foreach (var tile in window)
            if (!VanillaToolTileCatalog1458.TryGet(tile.Type, out _)) return false;
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            {
                int index = (y + 1) * 3 + x + 1;
                var tile = window[index];
                VanillaToolTileCatalog1458.TryGet(tile.Type, out var roles);
                if (!roles.CanBeDugByShovel) continue;
                if (tile.Active && (roles.ConvertsAsGrass || roles.ConvertsAsJungleGrass || tile.Type.Value is 70 or 633))
                    destination[count++] = new(x, y, 100);
                destination[count++] = new(x, y, 30);
            }
        return true;
    }

    public static bool TryGetHammerDamage(int power, TileTypeId type, bool hardMode,
        bool canKillTile, out int damage, out bool altarSelfHurt)
    {
        damage = 0;
        altarSelfHurt = false;
        if (power is < 0 or > 1000 || !VanillaToolTileCatalog1458.TryGet(type, out var roles))
            return false;
        damage = (roles.NoFail ? 100 : 0) + power;
        if (!canKillTile) damage = 0;
        altarSelfHurt = type.Value == 26 && (power < 80 || !hardMode);
        if (altarSelfHurt) damage = 0;
        return true;
    }

    public static bool TryGetWallDamage(int hammerPower, out int damage)
    {
        damage = 0;
        if (hammerPower is < 0 or > 1000) return false;
        damage = (int)(hammerPower * 1.5f);
        return true;
    }
}
