using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldItemAuthority
{
    private struct DollMotion
    {
        public WorldItemHandle Handle;
        public bool Wet, Honey, Lava;
    }
    private readonly DollMotion[] dollMotion = new DollMotion[RuntimeWorldItemStore.VanillaCapacity];

    internal void TickGuideDolls(WorldTileStore tiles, NpcAuthority npcs)
    {
        int count = worldItems.CopyActive(reservationScan);
        for (int i = 0; i < count; i++)
        {
            WorldItemSnapshot item = reservationScan[i];
            // Only source-verified267:14x26 ordinary gravity. Generic rarity burns are a separate catalog gate.
            if (item.ItemNetId != VanillaWallOfFleshItemIds.GuideVoodooDoll.Value || item.OwnerPlayerId != byte.MaxValue || item.Shimmered || item.ShimmerTime > 0) continue;
            ref DollMotion motion = ref dollMotion[item.Handle.Slot];
            if (motion.Handle != item.Handle) motion = new DollMotion { Handle = item.Handle };
            if (!VanillaOrdinaryWorldItemMotion1458.TryStep(tiles, 14, 26,
                item.PositionX, item.PositionY, item.VelocityX, item.VelocityY, motion.Wet, motion.Honey, motion.Lava,
                out float x, out float y, out float vx, out float vy, out bool wet, out bool honey, out bool lava)) continue;
            if (!worldItems.TryAdvanceMotion(item.Handle, x, y, vx, vy, out _)) continue;
            motion.Wet = wet; motion.Honey = honey; motion.Lava = lava;
            if (!lava || !TryTakeTrusted(item.Handle, out WorldItemSnapshot burned)) continue;
            npcs.ApplyBurnedGuideDoll(in burned);
        }
    }
}
