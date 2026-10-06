using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;
internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private readonly RuntimeNpcDeathPrelude1458 deathPrelude;
    internal RuntimeNpcDeathPrelude1458 DeathPrelude => deathPrelude;

    private const byte PlayerGhostMovementFlag1458 = 1 << 6; // MessageBuffer case13 bitsByte25[6].

    private bool TryFindBannerPlayer(in NpcSnapshot npc, out PlayerStateSnapshot selected, out string name)
    {
        selected = default; name = "";
        if (TryGetPreparedProjectileInteraction(in npc, out var projectedPlayer) &&
            players.TryGetPlayer(projectedPlayer.Slot, out selected) && !selected.IsDead)
            return TryResolveBannerPlayerName(selected.Player, out name);
        if (interactions.TryGetLastInteraction(npc.Handle, out PlayerSlotId last) &&
            players.TryGetPlayer(last, out selected) && !selected.IsDead)
            return TryResolveBannerPlayerName(selected.Player, out name);
        if (!VanillaNpcDefinitionCatalog.TryGet(npc.TypeIdentity, npc.NetIdentity, out var definition) ||
            !definition.TryResolveHitbox(npc.Simulation, out var body)) return false;

        // NPC.FindClosestPlayer uses Vector2.DistanceSquared with float half dimensions (NPC.cs78257).
        float centerX = npc.PositionX + body.Width * .5f;
        float centerY = npc.PositionY + body.Height * .5f;
        for (int pass = 0; pass < 2; pass++)
        {
            float minimum = float.MaxValue;
            bool found = false;
            for (int index = 0; index < VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots; index++)
            {
                if (!players.TryGetPlayer(new PlayerSlotId((byte)index), out var player) ||
                    (pass == 0 && (player.IsDead || (player.MovementFlags & PlayerGhostMovementFlag1458) != 0))) continue;
                var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                    (VanillaPlayerWidth, VanillaPlayerHeight);
                float dx = centerX - (player.PositionX + size.Item1 * .5f);
                float dy = centerY - (player.PositionY + size.Item2 * .5f);
                float distance = dx * dx + dy * dy;
                if (distance >= minimum) continue;
                minimum = distance; selected = player; found = true;
            }
            if (found) return TryResolveBannerPlayerName(selected.Player, out name);
        }
        return false;
    }

    private bool TryResolveBannerPlayerName(PlayerHandle player, out string name)
    {
        if (players is IRuntimePlayerPreludeNameLookup1458 lookup && lookup.TryGetPreludePlayerName(player, out name)) return true;
        name = "";
        // Source permits an empty player name. A known active slot still owns the announcement attribution.
        return true;
    }
}
