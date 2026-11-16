using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>Item-producing Wiring.HitWireSingle(105) and Item.MechSpawn, Terraria 1.4.5.8.</summary>
public static class VanillaItemStatue1458
{
    public const int Cooldown = 600;
    public const int ManaStyle = 2, BombStyle = 17, HeartStyle = 37;
    private const int Heart = 58, Star = 184, Bomb = 166;

    public static bool TryResolve(int style, out ItemTypeId item, out int verticalOffset)
    {
        item = new(style switch { HeartStyle => Heart, ManaStyle => Star, BombStyle => Bomb, _ => 0 });
        verticalOffset = style == BombStyle ? 20 : 16;
        return !item.IsNone;
    }

    public static bool IsSupported(ItemTypeId item) => item.Value is Heart or Star or Bomb;
    public static bool NeedsCalendar(ItemTypeId item) => item.Value is Heart or Star;

    public static bool CanSpawn(ReadOnlySpan<WorldItemAllocationState1458> items, float x, float y, ItemTypeId requested)
    {
        if (items.Length > VanillaWorldItemAllocation1458.Capacity || !float.IsFinite(x) || !float.IsFinite(y)) return false;
        ReadOnlySpan<int> types = requested.Value switch
        {
            Heart => [Heart, 1734, 1867],
            Star => [Star, 1735, 1868],
            Bomb => [Bomb],
            _ => []
        };
        if (types.IsEmpty) return false;
        // Each seasonal identity has its OWN source limit. Stack quantities do
        // not enter MechSpawn; distances use item top-left, not its center.
        foreach (int type in types)
        {
            int total = 0, near = 0, middle = 0;
            foreach (var item in items)
            {
                if (!item.Active || item.Type != type) continue;
                total++;
                float dx = item.X - x, dy = item.Y - y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance < 300f) near++;
                if (distance < 800f) middle++;
            }
            if (near >= 3 || middle >= 6 || total >= 10) return false;
        }
        return true;
    }
}
