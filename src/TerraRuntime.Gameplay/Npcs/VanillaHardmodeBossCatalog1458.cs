using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Version-pinned TerrariaServer 1.4.5.8 SetDefaults slice for Hardmode/endgame boss identities.
/// Definitions with BehaviorFamily.None are metadata-only and remain fail-closed in authoritative AI.
/// </summary>
public static class VanillaHardmodeBossCatalog1458
{
    private static readonly VanillaNpcDefinition[] Definitions =
    [
        Boss(VanillaNpcIds.Retinazer, VanillaNpcAiStyles.Retinazer, 100, 110, 45, 10, 20_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Retinazer),
        Boss(VanillaNpcIds.Spazmatism, VanillaNpcAiStyles.Spazmatism, 100, 110, 50, 10, 23_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Spazmatism),
        Boss(VanillaNpcIds.SkeletronPrime, VanillaNpcAiStyles.SkeletronPrime, 80, 102, 47, 24, 28_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.SkeletronPrime),
        Part(VanillaNpcIds.PrimeCannon, VanillaNpcAiStyles.PrimeCannon, 52, 52, 30, 23, 7_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PrimeCannon),
        Part(VanillaNpcIds.PrimeSaw, VanillaNpcAiStyles.PrimeSaw, 52, 52, 56, 38, 9_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PrimeSaw),
        Part(VanillaNpcIds.PrimeVice, VanillaNpcAiStyles.PrimeVice, 52, 52, 52, 34, 9_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PrimeVice),
        Part(VanillaNpcIds.PrimeLaser, VanillaNpcAiStyles.PrimeLaser, 52, 52, 29, 20, 6_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PrimeLaser),
        Boss(VanillaNpcIds.Destroyer, VanillaNpcAiStyles.Destroyer, 38, 38, 70, 0, 80_000,
            scale: 1.25f, noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Destroyer),
        Part(VanillaNpcIds.DestroyerBody, VanillaNpcAiStyles.Destroyer, 38, 38, 55, 30, 80_000,
            scale: 1.25f, noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Destroyer),
        Part(VanillaNpcIds.DestroyerTail, VanillaNpcAiStyles.Destroyer, 38, 38, 40, 35, 80_000,
            scale: 1.25f, noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Destroyer),

        Boss(VanillaNpcIds.Golem, VanillaNpcAiStyles.Golem, 140, 140, 72, 26, 15_000,
            behavior: VanillaNpcBehaviorFamily.Golem, physics: VanillaNpcPhysicsFamily.GenericGround),
        Part(VanillaNpcIds.GolemHead, VanillaNpcAiStyles.GolemHead, 70, 70, 64, 20, 25_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.GolemHead),
        Part(VanillaNpcIds.GolemFistLeft, VanillaNpcAiStyles.GolemFist, 40, 30, 59, 28, 10_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.GolemFist),
        Part(VanillaNpcIds.GolemFistRight, VanillaNpcAiStyles.GolemFist, 40, 30, 59, 28, 10_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.GolemFist),
        Part(VanillaNpcIds.GolemHeadFree, VanillaNpcAiStyles.GolemHeadFree, 70, 70, 80, 32, 16_000,
            noGravity: true, noTileCollide: true, dontTakeDamage: true, behavior: VanillaNpcBehaviorFamily.GolemHeadFree),

        Boss(VanillaNpcIds.Plantera, VanillaNpcAiStyles.Plantera, 86, 86, 50, 14, 30_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.Plantera),
        Part(VanillaNpcIds.PlanteraHook, VanillaNpcAiStyles.PlanteraHook, 40, 40, 60, 24, 4_000,
            noGravity: true, noTileCollide: true, dontTakeDamage: true, behavior: VanillaNpcBehaviorFamily.PlanteraHook),
        Part(VanillaNpcIds.PlanteraTentacle, VanillaNpcAiStyles.PlanteraTentacle, 24, 24, 60, 20, 1_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PlanteraTentacle),
        Part(VanillaNpcIds.PlanteraSpore, VanillaNpcAiStyles.PlanteraSpore, 18, 18, 70, 0, 1,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.PlanteraSpore),

        Boss(VanillaNpcIds.DukeFishron, VanillaNpcAiStyles.DukeFishron, 150, 100, 100, 50, 60_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.DukeFishron),
        Part(VanillaNpcIds.DetonatingBubble, VanillaNpcAiStyles.DetonatingBubble, 36, 36, 100, 0, 1,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.DetonatingBubble),
        Part(VanillaNpcIds.Sharkron, VanillaNpcAiStyles.Sharkron, 120, 24, 100, 100, 100,
            noGravity: true, behavior: VanillaNpcBehaviorFamily.DukeFishron),
        Part(VanillaNpcIds.Sharkron2, VanillaNpcAiStyles.Sharkron, 100, 24, 120, 100, 100,
            noGravity: true, behavior: VanillaNpcBehaviorFamily.DukeFishron),
        Boss(VanillaNpcIds.LunaticCultist, VanillaNpcAiStyles.LunaticCultist, 24, 50, 50, 42, 32_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.LunaticCultist),
        Part(VanillaNpcIds.LunaticCultistClone, VanillaNpcAiStyles.LunaticCultist, 24, 50, 0, 35, 10_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.LunaticCultistClone),
        Part(VanillaNpcIds.AncientVision, VanillaNpcAiStyles.AncientVision, 60, 60, 90, 30, 2_000,
            behavior: VanillaNpcBehaviorFamily.LunaticCultist, physics: VanillaNpcPhysicsFamily.NoClipFlight),
        Part(VanillaNpcIds.AncientLight, VanillaNpcAiStyles.AncientLight, 26, 26, 120, 0, 400,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.LunaticCultist),
        Part(VanillaNpcIds.AncientDoom, VanillaNpcAiStyles.AncientDoom, 42, 42, 30, 0, 500,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.LunaticCultist),
        Boss(VanillaNpcIds.MoonLordHead, VanillaNpcAiStyles.MoonLordHead, 38, 56, 0, 50, 45_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.MoonLordHead),
        Boss(VanillaNpcIds.MoonLordHand, VanillaNpcAiStyles.MoonLordHand, 46, 66, 0, 40, 25_000,
            noGravity: true, noTileCollide: true, behavior: VanillaNpcBehaviorFamily.MoonLordHand),
        Boss(VanillaNpcIds.MoonLordCore, VanillaNpcAiStyles.MoonLordCore, 46, 66, 0, 70, 50_000,
            noGravity: true, noTileCollide: true, dontTakeDamage: true, behavior: VanillaNpcBehaviorFamily.MoonLordCore),
        Part(VanillaNpcIds.MoonLordFreeEye, VanillaNpcAiStyles.MoonLordFreeEye, 60, 60, 60, 0, 100,
            noGravity: true, noTileCollide: true, dontTakeDamage: true, behavior: VanillaNpcBehaviorFamily.MoonLordFreeEye),
        Boss(VanillaNpcIds.EmpressOfLight, VanillaNpcAiStyles.EmpressOfLight, 100, 100, 80, 50, 70_000,
            noGravity: true, noTileCollide: true, dontTakeDamage: true, behavior: VanillaNpcBehaviorFamily.EmpressOfLight),
        Boss(VanillaNpcIds.QueenSlime, VanillaNpcAiStyles.QueenSlime, 114, 100, 60, 26, 18_000,
            behavior: VanillaNpcBehaviorFamily.QueenSlime, physics: VanillaNpcPhysicsFamily.GenericGround),
        Part(VanillaNpcIds.QueenSlimeMinionPurple, new NpcAiStyleId(14), 40, 30, 50, 30, 120,
            knockBackResist: 0.8f)
    ];

    public static ReadOnlySpan<VanillaNpcDefinition> All => Definitions;

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        foreach (VanillaNpcDefinition candidate in Definitions)
        {
            if (candidate.Type != type)
                continue;
            definition = candidate;
            return true;
        }
        definition = default;
        return false;
    }

    public static bool IsHardmodeBossRoot(NpcTypeId type) =>
        type == VanillaNpcIds.Retinazer || type == VanillaNpcIds.Spazmatism ||
        type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.Destroyer ||
        type == VanillaNpcIds.QueenSlime || type == VanillaNpcIds.Plantera ||
        type == VanillaNpcIds.Golem || type == VanillaNpcIds.DukeFishron ||
        type == VanillaNpcIds.LunaticCultist || type == VanillaNpcIds.EmpressOfLight ||
        type == VanillaNpcIds.MoonLordCore;

    private static VanillaNpcDefinition Boss(
        NpcTypeId type, NpcAiStyleId aiStyle, int width, int height, int damage, int defense, int lifeMax,
        float knockBackResist = 0f, float scale = 1f, bool noGravity = false, bool noTileCollide = false,
        bool dontTakeDamage = false, VanillaNpcBehaviorFamily behavior = VanillaNpcBehaviorFamily.None,
        VanillaNpcPhysicsFamily physics = VanillaNpcPhysicsFamily.NoClipFlight) =>
        Create(type, aiStyle, NpcArchetypeRole.Boss, width, height, damage, defense, lifeMax,
            knockBackResist, scale, noGravity, noTileCollide, dontTakeDamage, behavior, physics);

    private static VanillaNpcDefinition Part(
        NpcTypeId type, NpcAiStyleId aiStyle, int width, int height, int damage, int defense, int lifeMax,
        float knockBackResist = 0f, float scale = 1f, bool noGravity = false, bool noTileCollide = false,
        bool dontTakeDamage = false, VanillaNpcBehaviorFamily behavior = VanillaNpcBehaviorFamily.None,
        VanillaNpcPhysicsFamily physics = VanillaNpcPhysicsFamily.NoClipFlight) =>
        Create(type, aiStyle, NpcArchetypeRole.Ordinary, width, height, damage, defense, lifeMax,
            knockBackResist, scale, noGravity, noTileCollide, dontTakeDamage, behavior, physics);

    private static VanillaNpcDefinition Create(
        NpcTypeId type, NpcAiStyleId aiStyle, NpcArchetypeRole role, int width, int height, int damage, int defense,
        int lifeMax, float knockBackResist, float scale, bool noGravity, bool noTileCollide, bool dontTakeDamage,
        VanillaNpcBehaviorFamily behavior, VanillaNpcPhysicsFamily physics) =>
        new(type, aiStyle, behavior, physics, role, width, height, damage, defense, lifeMax,
            knockBackResist, scale, noGravity, noTileCollide, VanillaNpcSyncAnchor.TopLeft)
        {
            DontTakeDamageAtSpawn = dontTakeDamage
        };
}
