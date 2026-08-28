using System.Globalization;

namespace TerraRuntime.Contracts.Gameplay;

/// <summary>
/// Protocol-neutral Terraria item content identity. Zero is the canonical empty/air item.
/// Version-specific vanilla range validation belongs to <see cref="VanillaItemIds"/>.
/// </summary>
public readonly record struct ItemTypeId
{
    public ItemTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public bool IsNone => Value == 0;

    public static bool TryCreate(int value, out ItemTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new ItemTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Positive gameplay NPC content identity. This is deliberately distinct from NPC slot/generation
/// identity and from the signed protocol net id used for vanilla variants.
/// </summary>
public readonly record struct NpcTypeId
{
    public NpcTypeId(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public bool IsAssigned => Value > 0;

    public static bool TryCreate(int value, out NpcTypeId id)
    {
        if (value <= 0)
        {
            id = default;
            return false;
        }

        id = new NpcTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral projectile content identity.</summary>
public readonly record struct ProjectileTypeId
{
    public ProjectileTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out ProjectileTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new ProjectileTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral tile content identity. Tile type zero is valid vanilla content.</summary>
public readonly record struct TileTypeId
{
    public TileTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out TileTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new TileTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral wall content identity.</summary>
public readonly record struct WallTypeId
{
    public WallTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out WallTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new WallTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral buff content identity.</summary>
public readonly record struct BuffTypeId
{
    public BuffTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out BuffTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new BuffTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral item prefix identity.</summary>
public readonly record struct PrefixId
{
    public PrefixId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out PrefixId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new PrefixId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Protocol-neutral tile-entity content identity.</summary>
public readonly record struct TileEntityTypeId
{
    public TileEntityTypeId(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        Value = value;
    }

    public int Value { get; }

    public static bool TryCreate(int value, out TileEntityTypeId id)
    {
        if (value < 0)
        {
            id = default;
            return false;
        }

        id = new TileEntityTypeId(value);
        return true;
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Semantic NPC AI-style identity. The numeric value is vanilla/version data, not an entity type.</summary>
public readonly record struct NpcAiStyleId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Semantic projectile AI-style identity, distinct from projectile content identity.</summary>
public readonly record struct ProjectileAiStyleId(int Value)
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
