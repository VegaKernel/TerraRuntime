using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
namespace TerraRuntime.Application;
internal sealed partial class PlayerAuthority
{
    internal bool TryCapturePlainLarvaGeometry(byte slot,out ItemTypeId selectedType,out bool scopeCandidate,
        out VanillaPlayerCombatEquipmentContext context)
    {
        selectedType=default;scopeCandidate=false;context=default;
        if(!TryReadToolTreeActor(slot,out var snapshot,out var human,out var selected))return false;
        if(snapshot is null)return true;
        if(snapshot.Value.HasMount)return false;
        if(human is not null)
        {
            if(!transferProfiles.TryCapture(human.Connection,out var appearance,out var equipment,out _))return false;
            bool? extra=appearance is { } known ?
                (known.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag)!=0 : null;
            context=new(extra,combatEquipmentExpertMode,combatEquipmentMasterMode){LocalVanityArmor=combatEquipmentLocalVanityArmor};
            if(!VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(equipment,in context,out bool stool,out scopeCandidate) || stool)return false;
        }
        else if(serverPlayers is null || !serverPlayers.TryHasPlainLarvaGeometry(snapshot.Value.Player,out scopeCandidate,out context))return false;
        selectedType=selected.IsEmpty?default:selected.ItemType;return true;
    }
}
internal sealed partial class ServerPlayerAuthority
{
    internal bool TryHasPlainLarvaGeometry(PlayerHandle player,out bool scopeCandidate,
        out VanillaPlayerCombatEquipmentContext context)
    {
        scopeCandidate=false;context=default;
        if(!states.TryGet(player,out var snapshot) || snapshot.HasMount)return false;
        bool? extra=states.TryGetAppearance(player,out var appearance) ?
            (appearance.DifficultyFlags & VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag)!=0 : null;
        context=new(extra,combatEquipmentExpertMode,combatEquipmentMasterMode){LocalVanityArmor=combatEquipmentLocalVanityArmor};
        Span<PlayerEquipmentCommitRequest> equipment=stackalloc PlayerEquipmentCommitRequest[
            VanillaPlayerItemSlotCatalog.ArmorCount+VanillaPlayerItemSlotCatalog.FunctionalArmorCount*VanillaPlayerItemSlotCatalog.LoadoutCount];
        int count=0;
        for(short slot=VanillaPlayerItemSlotCatalog.ArmorStart;slot<VanillaPlayerItemSlotCatalog.VanityArmorEndExclusive;slot++)
        {
            if(!states.TryGetItem(player,slot,out var item))return false;
            if(item.IsEmpty)continue;
            equipment[count++]=new(player.Slot,slot,item.Stack,checked((byte)item.Prefix.Value),checked((short)item.ItemType.Value),item.ItemFlags);
        }
        for(int loadout=0;loadout<VanillaPlayerItemSlotCatalog.LoadoutCount;loadout++)
        for(int index=0;index<VanillaPlayerItemSlotCatalog.FunctionalArmorCount;index++)
        {
            short slot=checked((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart+loadout*VanillaPlayerItemSlotCatalog.LoadoutStride+index));
            if(!states.TryGetItem(player,slot,out var item))return false;
            if(item.IsEmpty || (item.ItemFlags & PlayerEquipmentCommitRequest.FavoriteItemFlag)==0)continue;
            equipment[count++]=new(player.Slot,slot,item.Stack,checked((byte)item.Prefix.Value),checked((short)item.ItemType.Value),item.ItemFlags);
        }
        return VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(equipment[..count],in context,out bool stool,out scopeCandidate) && !stool;
    }
}
internal static class LarvaBossItems1458
{
    internal const int SniperRifle=1254,Binoculars=1299;
}
