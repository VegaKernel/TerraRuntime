namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Source-backed Terraria 1.4.5.8 NPC.playerInteraction slot range. Slot 255 is the vanilla sentinel and therefore
/// never participates in boss per-player loot or interaction tracking.
/// </summary>
public static class VanillaNpcPlayerInteractionFacts
{
    public const int InteractablePlayerSlots = byte.MaxValue;
}
