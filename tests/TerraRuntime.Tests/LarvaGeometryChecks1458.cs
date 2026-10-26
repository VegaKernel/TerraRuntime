using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using Xunit;
namespace TerraRuntime.Tests.Larva1458;
public sealed partial class LarvaQueenBeeTests1458
{
 private static void RunGeometryRows(JsonElement row)
 {
  var ordinary=new VanillaPlayerCombatEquipmentContext(false,false,false);
  static PlayerEquipmentCommitRequest Item(short slot,short type,byte flags=0)=>new(new(0),slot,1,0,type,flags);
  foreach(short slot in new short[]{1,58,72,79,83,700,739,903,933,963})
  foreach(short type in new short[]{4341,5126,1300,1858,4005})
  {
   Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(slot,type)},ordinary,out bool stool,out bool scope));
   Assert.False(stool);Assert.False(scope);
  }
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(62,4341)},ordinary,out bool activeStool,out _));Assert.True(activeStool);
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(62,1858)},ordinary,out _,out bool activeScope));Assert.True(activeScope);
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(903,1858,PlayerEquipmentCommitRequest.FavoriteItemFlag)},ordinary,out _,out bool inheritedScope));Assert.True(inheritedScope);
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(62,9),Item(903,1858,PlayerEquipmentCommitRequest.FavoriteItemFlag)},ordinary,out _,out bool blockedScope));Assert.False(blockedScope);
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(67,4341)},ordinary,out bool unusableStool,out _));Assert.False(unusableStool);
  Assert.False(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(67,4341)},new(null,true,false),out _,out _));
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(67,4341)},new(true,true,false),out bool extraStool,out _));Assert.True(extraStool);
  Assert.True(VanillaPlayerCombatEquipmentCatalog.TryCaptureLarvaSpawnGeometry(new[]{Item(68,4341)},new(true,true,false),out bool nonMasterStool,out _));Assert.False(nonMasterStool);
  // Physical packet5 source storage survives capture and does not masquerade as equipment.
  RunSource(row,"Linux",storedSlot:700,storedItem:4341);
  RunSource(row,"Linux",storedSlot:739,storedItem:5126);
  RunSource(row,"Linux",storedSlot:1,storedItem:4341);
  RunSource(row,"Linux",storedSlot:700,storedItem:1858,held:9);
  RunSource(row,"Linux",storedSlot:903,storedItem:4341);
  RunSource(row,"Linux",fault:"equipment-mode");
 }
}
