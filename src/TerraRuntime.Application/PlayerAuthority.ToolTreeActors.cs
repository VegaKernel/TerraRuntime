using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryCaptureToolTreeActors(out ToolTreeActorsPreparation? preparation)
    {
        preparation = null;
        if (playerUpdateWorld is not { IsValid: true } world) return false;
        ulong members = membership.Serial, items = inventory.Serial;
        bool equipmentExpert = combatEquipmentExpertMode, equipmentMaster = combatEquipmentMasterMode;
        var localVanity = combatEquipmentLocalVanityArmor;
        var snapshots = new PlayerStateSnapshot?[byte.MaxValue];
        var humans = new RuntimePlayerMember?[byte.MaxValue];
        var selected = new RuntimePlayerInventoryItem[byte.MaxValue];
        var actors = new List<ToolTreeActor1458>();
        for (int slot = 0; slot < byte.MaxValue; slot++)
        {
            if (!TryReadToolTreeActor((byte)slot, out var snapshot, out var human, out var item)) return false;
            if (snapshot is not { } player) continue;
            if (!float.IsFinite(player.PositionX) || !float.IsFinite(player.PositionY) ||
                !float.IsFinite(player.Luck)) return false;
            int axe = 0;
            if (!item.IsEmpty && VanillaToolCatalog1458.TryGet(item.ItemType, out _))
            {
                if (!VanillaToolCatalog1458.TryResolve(item.ItemType, item.Prefix, world.RemixWorld,
                    world.WindowsItemPrefixArithmetic, out var tool)) return false;
                axe = tool.Axe;
            }
            var size = player.HasMount ? VanillaPlayerMountHitbox1458.Resolve(player.MountType) :
                (VanillaPlayerHitboxFacts.BaseWidth, VanillaPlayerHitboxFacts.BaseHeight);
            snapshots[slot] = player;
            humans[slot] = human;
            selected[slot] = item;
            bool? flowerBoots = null, seedTool = null;
            if (human is not null)
            {
                if (transferProfiles.TryCapture(human.Connection, out var appearance, out var equipment, out _))
                {
                    bool? extra = appearance is { } known
                        ? (known.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag) != 0 : null;
                    var context = new VanillaPlayerCombatEquipmentContext(extra, equipmentExpert, equipmentMaster)
                    { LocalVanityArmor = localVanity };
                    if (VanillaPlayerCombatEquipmentCatalog.TryCaptureFlowerBoots(equipment, in context, out bool flowers))
                        flowerBoots = flowers;
                }
                bool knownInventory = true, hasSeedTool = false;
                // Player.HasItem searches0..57, excluding the cursor slot58.
                for (short inventorySlot = 0; inventorySlot < 58; inventorySlot++)
                {
                    if (!inventory.TryGet(human.Connection, inventorySlot, out var physical) || !physical.IsCanonical)
                    { knownInventory = false; break; }
                    if (!physical.IsEmpty && physical.ItemType.Value is 281 or 986)
                    { hasSeedTool = true; break; }
                }
                if (knownInventory) seedTool = hasSeedTool;
            }
            actors.Add(new((byte)slot, true, player.IsDead, player.PositionX, player.PositionY,
                (int)size.Item1, (int)size.Item2, axe, player.Luck)
            {
                FlowerBoots = flowerBoots,
                // Player.ZoneGraveyard is source zone4 bit6 (GetData36).
                ZoneGraveyard = player.Zones is { } zones ? (zones.Zone4 & 64) != 0 : null,
                HasSeedTool = seedTool
            });
        }
        preparation = new(this, world, members, items, snapshots, humans, selected, actors.ToArray(),
            equipmentExpert, equipmentMaster, localVanity);
        return preparation.IsCurrent;
    }

    private bool TryReadToolTreeActor(byte slot, out PlayerStateSnapshot? snapshot,
        out RuntimePlayerMember? human, out RuntimePlayerInventoryItem selected)
    {
        snapshot = null;
        human = null;
        selected = default;
        bool hasHuman = membership.TryGet(slot, out var member);
        bool hasServer = serverPlayers is not null &&
            serverPlayers.TryGet(new PlayerSlotId(slot), out var candidate) &&
            serverPlayers.TryGet(candidate.Player, out _);
        if (hasHuman && hasServer) return false;
        if (hasHuman)
        {
            if (!inventory.TryGet(member.Connection, member.SelectedItem, out selected) ||
                !selected.IsCanonical) return false;
            human = member;
            snapshot = member.CaptureSnapshot();
        }
        else if (hasServer)
        {
            if (!serverPlayers!.TryCaptureToolTreeActor(slot, out var player, out selected)) return false;
            snapshot = player;
        }
        return true;
    }

    internal sealed class ToolTreeActorsPreparation(PlayerAuthority owner,
        RuntimePlayerUpdateWorld1458 world, ulong members, ulong items,
        PlayerStateSnapshot?[] snapshots, RuntimePlayerMember?[] humans,
        RuntimePlayerInventoryItem[] selected, ToolTreeActor1458[] actors,
        bool equipmentExpert, bool equipmentMaster, VanillaPlayerLocalVanityArmor1458? localVanity)
    {
        private bool hurtProjected;
        internal ReadOnlySpan<ToolTreeActor1458> Actors => actors;
        internal bool TryHasWorldBombProximity(float centerX, float centerY,
            bool windowsArithmetic, out bool found)
        {
            found = false;
            if (!float.IsFinite(centerX) || !float.IsFinite(centerY) || !IsCurrent) return false;
            foreach (var actor in actors)
            {
                if (!actor.Active || actor.Dead) continue;
                if (snapshots[actor.Slot] is not { } snapshot) return false;
                if ((snapshot.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0) continue;
                float dx = centerX - (actor.X + actor.Width * .5f);
                float dy = centerY - (actor.Y + actor.Height * .5f);
                // AI_016 (1458) compares Vector2.Length with40, not squared
                // distance. Preserve the source float result at the boundary.
                float distance = (float)Math.Sqrt(windowsArithmetic
                    ? dx * (double)dx + dy * (double)dy
                    : dx * dx + dy * dy);
                if (distance < 40f) { found = true; break; }
            }
            return IsCurrent;
        }
        internal bool TryIsPlacementTileEmpty(int tileX, int tileY, out bool empty)
        {
            empty = false;
            if (!IsCurrent) return false;
            int x = tileX * 16, y = tileY * 16;
            foreach (var actor in actors)
            {
                if (!actor.Active || actor.Dead) continue;
                if (snapshots[actor.Slot] is not { } snapshot) return false;
                if ((snapshot.MovementFlags & VanillaPlayerHealthContext1458.GhostMovementFlag) != 0) continue;
                if (x < (int)actor.X + actor.Width && x + 16 > (int)actor.X &&
                    y < (int)actor.Y + actor.Height && y + 16 > (int)actor.Y) return true;
            }
            empty = true;
            return IsCurrent;
        }

        internal bool IsCurrent
        {
            get
            {
                if (owner.membership.Serial != members || owner.inventory.Serial != items ||
                    owner.playerUpdateWorld != world || owner.combatEquipmentExpertMode != equipmentExpert ||
                    owner.combatEquipmentMasterMode != equipmentMaster || owner.combatEquipmentLocalVanityArmor != localVanity)
                    return false;
                // Negative slots matter too: a closer joining actor changes
                // Player.FindClosest even if all old actors remain unchanged.
                for (int slot = 0; slot < byte.MaxValue; slot++)
                    if (!owner.TryReadToolTreeActor((byte)slot, out var current, out var human, out var item) ||
                        current != snapshots[slot] || !ReferenceEquals(human, humans[slot]) ||
                        item != selected[slot]) return false;
                return true;
            }
        }

        internal bool TryProjectSelfHurt(ToolSelfHurtPreparation hurt)
        {
            if (hurtProjected || hurt.SourcePlayer.Slot.Value == byte.MaxValue ||
                !IsCurrent || !hurt.IsCurrent || snapshots[hurt.SourcePlayer.Slot.Value] is not { } old ||
                old.Player != hurt.SourcePlayer) return false;
            for (int index = 0; index < actors.Length; index++)
                if (actors[index].Slot == old.Player.Slot.Value)
                {
                    // Detached source afterimage only. The original player and
                    // complete census remain guarded until joint adoption.
                    actors[index] = actors[index] with { Dead = hurt.ProjectedDead };
                    hurtProjected = true;
                    return true;
                }
            return false;
        }
    }
}
