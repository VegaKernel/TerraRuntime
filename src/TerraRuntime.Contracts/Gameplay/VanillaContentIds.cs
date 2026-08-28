namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC content ids used by TerraRuntime gameplay.
/// Extend this catalog only from the repository source-of-truth hierarchy; do not guess ids.
/// </summary>
public static class VanillaNpcIds
{
    public static readonly NpcTypeId BlueSlime = new(1);
    public static readonly NpcTypeId DemonEye = new(2);
    public static readonly NpcTypeId Zombie = new(3);
}

/// <summary>
/// Initial source-verified TerrariaServer 1.4.5.8 NPC AI-style ids corresponding to the supported
/// lifecycle/AI slice. These are behavior families, not NPC content ids.
/// </summary>
public static class VanillaNpcAiStyles
{
    public static readonly NpcAiStyleId Slime = new(1);
    public static readonly NpcAiStyleId DemonEye = new(2);
    public static readonly NpcAiStyleId Fighter = new(3);
}

/// <summary>
/// TerrariaServer 1.4.5.8 item content bounds. The count is pinned to ItemID.Count as independently
/// exercised by packet-5 Item.netDefaults normalization tests.
/// </summary>
public static class VanillaItemIds
{
    public const int Count = 6196;

    public static ItemTypeId None => default;

    public static bool TryCreate(int rawType, out ItemTypeId type)
    {
        if ((uint)rawType >= (uint)Count)
        {
            type = default;
            return false;
        }

        type = new ItemTypeId(rawType);
        return true;
    }
}
