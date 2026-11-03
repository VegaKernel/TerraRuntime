using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.Gameplay.Npcs;
/// <summary>NPCID.Sets.SavesAndLoads; these are the four source persistent nontown identities.</summary>
public static class VanillaNpcPersistenceFacts1458 {
 public static bool SavesOutsideTown(NpcTypeId type)=>type.Value is 422 or 493 or 507 or 517;
}
