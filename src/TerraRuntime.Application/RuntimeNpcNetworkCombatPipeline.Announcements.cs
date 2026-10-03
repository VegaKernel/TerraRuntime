using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private void AnnounceBossDefeat(in NpcSnapshot dead, bool eaterBoss)
    {
        if (npcReplication is null) return;
        // DoDeathEvents_BeforeLoot suppresses the first Twin's boss celebration.
        if (dead.TypeIdentity == VanillaNpcIds.Retinazer || dead.TypeIdentity == VanillaNpcIds.Spazmatism)
        {
            NpcTypeId other = dead.TypeIdentity == VanillaNpcIds.Retinazer ? VanillaNpcIds.Spazmatism : VanillaNpcIds.Retinazer;
            int count = npcs.CopyActive(npcFamilyBuffer);
            for (int i = 0; i < count; i++)
                if (npcFamilyBuffer[i].TypeIdentity == other) return;
        }
        if (VanillaBossAnnouncementCatalog1458.TryGetDefeat(dead.TypeIdentity, eaterBoss, out string key, out string? name))
            npcReplication.BossAnnouncement(key, name);
    }
}
