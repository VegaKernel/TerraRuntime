using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeNpcNetworkCombatPipeline
{
    private WorldItemSnapshot? guideDollRemoval;
    private NpcAiSpawnIntent? guideDollWallSpawn;
    private ulong? guideDollNpcSerial;

    internal bool TryBurnDollWithoutGuide(in WorldItemSnapshot doll)
    {
        if (!IsCurrentBurnableDoll(in doll) || !npcs.TryCaptureDeathMutationSerial(out ulong serial)) return false;
        for (int slot = 0; slot < npcs.Capacity; slot++)
            if (npcs.TryGetActive((byte)slot, out var npc) && npc.TypeIdentity == VanillaNpcIds.Guide) return false;
        using var allocation = worldItems.CreateAllocationPreview();
        return allocation.TryRemoveSource(in doll) && npcs.TryCaptureDeathMutationSerial(out ulong current) &&
            current == serial && allocation.TryClaim() && allocation.TryCommitNext(out _, out _);
    }

    // This lane owns one ordinary lethal Guide strike. Multi-Guide and later town-victim selection
    // require a shared ordered batch of death plans, and are rejected by the producer before this seam.
    internal bool TryStrikeGuideDoll(in WorldItemSnapshot doll, in NpcSnapshot guide, ulong npcSerial,
        out NpcAiSpawnIntent? wallSpawn)
    {
        wallSpawn = null;
        if (guideDollRemoval.HasValue || pendingDeathPlan is not null || !IsCurrentBurnableDoll(in doll) ||
            guide.TypeIdentity != VanillaNpcIds.Guide || !npcs.TryGet(guide.Handle, out var retained) || retained != guide ||
            !npcs.TryCaptureDeathMutationSerial(out ulong serial) || serial != npcSerial ||
            !TryBuildGuideDollWallSpawn(in doll, out var spawn)) return false;
        // Use the existing source strike arithmetic and retained defaults without changing a live slot.
        // Nonlethal/immune/modded Guide branches are outside this whole-death transaction.
        var detached = npcs.CreateDeathPreview(random);
        var request = new NpcDamageRequest(guide.Handle, DamageSource.Environment, 9999,
            KnockBack: 10f, HitDirection: guide.Simulation.DirectionX);
        if (!new RuntimeNpcDamageExecutor(detached, expertMode).TryApply(in request, out var preview) || !preview.Lethal ||
            !npcs.TryCaptureDeathMutationSerial(out serial) || serial != npcSerial || !IsCurrentBurnableDoll(in doll))
            return false;
        guideDollRemoval = doll;
        guideDollWallSpawn = spawn;
        guideDollNpcSerial = npcSerial;
        try
        {
            if (TryStrikeEnvironment(guide.Handle, 9999, 10f, guide.Simulation.DirectionX) !=
                RuntimeTownNpcMeleeDamageResult1458.Killed) return false;
            wallSpawn = spawn;
            return true;
        }
        finally
        {
            CancelPendingDeathPlan();
            guideDollRemoval = null;
            guideDollWallSpawn = null;
            guideDollNpcSerial = null;
        }
    }

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

    private bool TryValidateGuideDollContinuation(in NpcSnapshot dead, VanillaUnifiedRandom1458 afterDeath)
    {
        if (guideDollRemoval is not { } doll) return true;
        if (!TryBuildGuideDollWallSpawn(in doll, out var currentSpawn) || currentSpawn != guideDollWallSpawn) return false;
        if (guideDollWallSpawn is not { } spawn) return true;
        // NewNPC's defaults must use the same admitted stream as this whole death. Preview its
        // post-Guide slot reuse and context with a separate clone, without adding birth draws to loot.
        if (!npcs.IsSpawnRandomSource(deathPreviewLiveRandom!)) return false;
        var detached = previewDeathNpcs!.CreateDeathPreview(new SystemVanillaNpcRandom(afterDeath.Clone()));
        return detached.TryDespawn(dead.Handle) && detached.TrySpawnIntent(in spawn, out _);
    }
}
