using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private bool IsCurrentBurnableDoll(in WorldItemSnapshot doll) =>
        doll.ItemNetId == VanillaWallOfFleshItemIds.GuideVoodooDoll.Value && doll.Stack > 0 &&
        doll.OwnerPlayerId == byte.MaxValue && !doll.Shimmered && doll.ShimmerTime <= 0 &&
        worldItems.TryGetActive(doll.Handle.Slot, out var current) && current == doll;

    private bool TryBuildGuideDollWallSpawn(in WorldItemSnapshot doll, out NpcAiSpawnIntent? spawn)
    {
        spawn = null;
        if (worldTiles is null || !float.IsFinite(doll.PositionX) || !float.IsFinite(doll.PositionY) ||
            doll.PositionX < 0 || doll.PositionX >= worldTiles.Dimensions.WidthTiles * 16f ||
            doll.PositionY < 0 || doll.PositionY >= worldTiles.Dimensions.HeightTiles * 16f) return false;
        for (int slot = 0; slot < npcs.Capacity; slot++)
            if (npcs.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity == VanillaNpcIds.WallOfFlesh) return true;
        var active = new PlayerStateSnapshot[VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots];
        int count = 0;
        for (int slot = 0; slot < active.Length; slot++)
            if (players.TryGetPlayer(new((byte)slot), out var player)) active[count++] = player;
        if (VanillaWallOfFleshSpawn1458.TryFind(worldTiles, doll.PositionX, doll.PositionY,
                active.AsSpan(0, count), out int x, out int y))
            spawn = new(VanillaNpcIds.WallOfFlesh, x, y, 0, 0, byte.MaxValue);
        return true;
    }

}
