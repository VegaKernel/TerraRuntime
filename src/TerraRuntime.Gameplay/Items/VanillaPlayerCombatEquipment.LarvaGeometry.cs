using TerraRuntime.Contracts.Runtime;
namespace TerraRuntime.Gameplay.Items;
public static partial class VanillaPlayerCombatEquipmentCatalog
{
    // Player.ApplyEquipFunctional(3..9), not all packet5 relayable storage.
    // Reuse the source first-favorite/usability selector; unrelated inactive items
    // cannot supply stool or scope even when their sharing metadata is unknown.
    public static bool TryCaptureLarvaSpawnGeometry(ReadOnlySpan<PlayerEquipmentCommitRequest> equipment,
        in VanillaPlayerCombatEquipmentContext context, out bool stool, out bool scope)
    {
        stool=scope=false;
        if(context.MasterMode && !context.ExpertMode)return false;
        for(int index=3;index<VanillaPlayerItemSlotCatalog.FunctionalArmorCount;index++)
        {
            var candidate=FindEquipment(equipment,VanillaPlayerItemSlotCatalog.ArmorStart+index);
            if(candidate.Stack<=0 && !TryFindFirstFavorite(equipment,index,out candidate))continue;
            if(candidate.ItemNetId is not (PortableStool or HandOfCreation or RifleScope or SniperScope or ReconScope))continue;
            if(!context.TryIsSlotUsable(index,out bool usable))return false;
            if(!usable)continue;
            if(!TrySelectEffectiveEquipment(equipment,in context,index,out var selected))return false;
            if(selected.Stack<=0)continue;
            if(!selected.TryGetCanonicalItemType(out var type))return false;
            stool |= type.Value is PortableStool or HandOfCreation;
            scope |= type.Value is RifleScope or SniperScope or ReconScope;
        }
        return true;
    }
    // Terraria1.4.5.8 ItemID; these are source accessory identity tests.
    private const int PortableStool=4341,HandOfCreation=5126,RifleScope=1300,SniperScope=1858,ReconScope=4005;
}
