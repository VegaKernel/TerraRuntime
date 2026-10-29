using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private VanillaGroundCritterTerrain1458? plannedMaggotTerrain;
    private RuntimeNpcReplicationRegistry.RetainedBirthJournal[] plannedMaggotBirthJournals = [];

    private static bool UsesPreparedLinkedDeath1458(NpcTypeId type) =>
        type == VanillaNpcIds.PirateCaptain || type == VanillaNpcIds.PirateGhost || type.Value == 632;

    private bool CaptureMaggotBirthWet(VanillaGroundCritterTerrain1458 terrain, float x, float y, int width, int height)
    {
        terrain.FenceBody(x, y, 0f, 0f, width, height);
        return VanillaWorldCollision.TryGetWetContact(worldTiles!, x, y, width, height, out _);
    }

    private bool TryRetainMaggotOwners(in NpcSnapshot projected)
    {
        plannedMaggotBirthJournals = [];
        plannedCaptainBirthJournal = null;
        plannedCaptainDeathPublication = null;
        if (plannedMaggotTerrain?.IsCurrent != true || plannedDeathSpawnSerial is not { } serial ||
            previewDeathNpcs is null || !npcs.TryPrepareMaggotDeathBirth(in projected,
                previewDeathNpcs, serial, out plannedCaptainBirth) ||
            !npcs.TryPrepareProjectedTerminalRemoval(in projected, out plannedCaptainRemoval)) return false;
        if (npcReplication is null) return true;
        if (!npcReplication.TryPrepareRetainedDeath(plannedCaptainRemoval!, out plannedCaptainDeathPublication)) return false;
        var journals = new List<RuntimeNpcReplicationRegistry.RetainedBirthJournal>(3);
        foreach (var child in plannedCaptainBirth!.Children)
        {
            if (!npcReplication.TryPrepareRetainedBirth(in child, out var journal, deferLive: true)) return false;
            journals.Add(journal!);
        }
        plannedMaggotBirthJournals = journals.ToArray();
        return true;
    }

    private bool SealLinkedBirthRecipients1458()
    {
        if (plannedCaptainBirthJournal?.TrySealRecipients() == false) return false;
        foreach (var journal in plannedMaggotBirthJournals)
            if (!journal.TrySealRecipients()) return false;
        return true;
    }
}
