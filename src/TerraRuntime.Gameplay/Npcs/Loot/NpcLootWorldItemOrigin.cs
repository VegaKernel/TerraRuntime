namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-backed NPC loot spawn origin. Terraria's ordinary DropItemFromNPC path converts NPC top-left position
/// to an integer center before Item.NewItem receives the drop.
/// </summary>
public readonly record struct NpcLootWorldItemOrigin(float CenterX, float CenterY)
{
    public bool IsValid => float.IsFinite(CenterX) && float.IsFinite(CenterY);
}

/// <summary>Item.NewItem's explicit velocity bypasses both default launch random draws.</summary>
public readonly record struct NpcLootWorldItemVelocity1458(float X, float Y)
{
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y);
}
