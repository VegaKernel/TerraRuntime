using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Runtime;

public sealed partial class Workspace
{
    /// <summary>Actual source allocation precedes the shared identity resolver and persistence projection.</summary>
    internal bool TrySpawnGeneratedTownNpc(
        NpcTypeId type,
        int bottomX,
        int bottomY,
        bool homeless,
        int homeX,
        int homeY,
        IWorldGenerationVanillaRandom random,
        Func<VanillaTownNpcSpawnIdentity1458> resolveIdentity,
        int? sourceDirectionX = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(resolveIdentity);
        if (type != VanillaNpcIds.Guide && type != VanillaNpcIds.OldMan)
            throw new NotSupportedException("This source town identity profile is not qualified yet.");
        var effects = EntityEffects ?? throw new InvalidOperationException("Missing composed source town owner.");
        if ((uint)homeX >= (uint)WidthTiles || (uint)homeY >= (uint)HeightTiles)
            return false;

        // NewNPC consumes its GoodWorld draw before allocation. Its maxNPCs sentinel
        // does not call GiveTownUniqueDataToNPCsThatNeedIt and creates no active record.
        if (!effects.SpawnNpc(type, bottomX, bottomY, 0, 255, default, random, out var handle))
            return true;
        if (!effects.TryCaptureNpc(handle.Slot, out var npc) || npc.Handle != handle || npc.Simulation.TownNpc != true)
            throw new InvalidOperationException("Lost canonical source town birth.");

        VanillaTownNpcSpawnIdentity1458 identity = resolveIdentity();
        effects.MoveTownNpc(handle, npc.PositionX, npc.PositionY, homeless, homeX, homeY, sourceDirectionX);
        var projection = new WorldTownNpc(type.Value, identity.GivenName, npc.PositionX, npc.PositionY,
            homeless, homeX, homeY, identity.VariationIndex, false);
        generatedTownNpcs.Add(new(projection, handle));
        return true;
    }
}
