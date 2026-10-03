using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Localization identities for the admitted NPC.SpawnOnPlayer and boss death routes.</summary>
public static class VanillaBossAnnouncementCatalog1458
{
    public const string MechdusaAwoken = "LegacyMisc.107";

    public static bool TryGetSpawn(NpcTypeId type, out string key, out string? name)
    {
        name = null;
        key = string.Empty;
        // SpawnBoss announces the Twins only when creating Retinazer, never Spazmatism.
        if (type == VanillaNpcIds.Spazmatism) return false;
        if (type == VanillaNpcIds.Retinazer) { key = "LegacyMisc.48"; return true; }
        name = type == VanillaNpcIds.MoonLordCore ? "Enemies.MoonLord" : GetName(type);
        if (name is null) return false;
        key = "Announcement.HasAwoken";
        return true;
    }

    public static bool TryGetDefeat(NpcTypeId type, bool lastEaterSegment, out string key, out string? name)
    {
        key = "Announcement.HasBeenDefeated_Single";
        name = null;
        if (type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism)
        {
            key = "Announcement.HasBeenDefeated_Plural";
            name = "Enemies.TheTwins";
            return true;
        }
        if (type == VanillaNpcIds.MoonLordCore) { name = "Enemies.MoonLord"; return true; }
        if (type == VanillaNpcIds.EaterOfWorldsHead || type == VanillaNpcIds.EaterOfWorldsBody ||
            type == VanillaNpcIds.EaterOfWorldsTail)
        {
            if (!lastEaterSegment) return false;
        }
        else if (type == VanillaNpcIds.PrimeCannon || type == VanillaNpcIds.PrimeSaw ||
            type == VanillaNpcIds.PrimeVice || type == VanillaNpcIds.PrimeLaser) return false;
        name = GetName(type);
        return name is not null;
    }

    private static string? GetName(NpcTypeId type)
    {
        // Lang.InitializeLegacyLocalization maps NPCID field names to NPCName keys in 1.4.5.8.
        if (type == VanillaNpcIds.EyeOfCthulhu) return "NPCName.EyeofCthulhu";
        if (type == VanillaNpcIds.EaterOfWorldsHead) return "NPCName.EaterofWorldsHead";
        if (type == VanillaNpcIds.EaterOfWorldsBody) return "NPCName.EaterofWorldsBody";
        if (type == VanillaNpcIds.EaterOfWorldsTail) return "NPCName.EaterofWorldsTail";
        if (type == VanillaNpcIds.SkeletronHead) return "NPCName.SkeletronHead";
        if (type == VanillaNpcIds.KingSlime) return "NPCName.KingSlime";
        if (type == VanillaNpcIds.WallOfFlesh) return "NPCName.WallofFlesh";
        if (type == VanillaNpcIds.SkeletronPrime) return "NPCName.SkeletronPrime";
        if (type == VanillaNpcIds.PrimeCannon) return "NPCName.PrimeCannon";
        if (type == VanillaNpcIds.PrimeSaw) return "NPCName.PrimeSaw";
        if (type == VanillaNpcIds.PrimeVice) return "NPCName.PrimeVice";
        if (type == VanillaNpcIds.PrimeLaser) return "NPCName.PrimeLaser";
        if (type == VanillaNpcIds.Destroyer) return "NPCName.TheDestroyer";
        if (type == VanillaNpcIds.QueenBee) return "NPCName.QueenBee";
        if (type == VanillaNpcIds.Golem) return "NPCName.Golem";
        if (type == VanillaNpcIds.Plantera) return "NPCName.Plantera";
        if (type == VanillaNpcIds.BrainOfCthulhu) return "NPCName.BrainofCthulhu";
        if (type == VanillaNpcIds.DukeFishron) return "NPCName.DukeFishron";
        if (type == VanillaNpcIds.LunaticCultist) return "NPCName.CultistBoss";
        if (type == VanillaNpcIds.EmpressOfLight) return "NPCName.HallowBoss";
        if (type == VanillaNpcIds.QueenSlime) return "NPCName.QueenSlimeBoss";
        if (type == VanillaNpcIds.Deerclops) return "NPCName.Deerclops";
        return null;
    }
}
