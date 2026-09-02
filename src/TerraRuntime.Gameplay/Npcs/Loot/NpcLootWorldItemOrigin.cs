namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-backed NPC loot spawn origin. Terraria's ordinary DropItemFromNPC path converts NPC top-left position
/// to an integer center before Item.NewItem receives the drop.
/// </summary>
public readonly record struct NpcLootWorldItemOrigin(float CenterX, float CenterY)
{
    public bool IsValid => float.IsFinite(CenterX) && float.IsFinite(CenterY);
}
