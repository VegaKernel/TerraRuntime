using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

public enum VanillaWormSegmentRole : byte
{
    None = 0,
    Head = 1,
    Body = 2,
    Tail = 3
}

public interface IVanillaWormEnvironment
{
    bool IsDigging(float positionX, float positionY, int width, int height);
}

public readonly record struct VanillaWormMotionProfile(
    float MaximumSpeed,
    float TurnRate,
    float SegmentGap,
    float AirGravity,
    float RisingAirGravity,
    bool AlwaysDig = false)
{
    public bool IsValid =>
        float.IsFinite(MaximumSpeed) && MaximumSpeed > 0f &&
        float.IsFinite(TurnRate) && TurnRate > 0f &&
        float.IsFinite(SegmentGap) && SegmentGap > 0f &&
        float.IsFinite(AirGravity) && AirGravity > 0f &&
        float.IsFinite(RisingAirGravity) && RisingAirGravity > 0f;
}

public readonly record struct VanillaWormNpcEntry(
    VanillaNpcDefinition Definition,
    VanillaWormSegmentRole Role,
    NpcTypeId HeadType,
    NpcTypeId BodyType,
    NpcTypeId TailType,
    VanillaWormMotionProfile Motion)
{
    public bool IsValid =>
        Definition.Type.IsAssigned &&
        Role != VanillaWormSegmentRole.None &&
        HeadType.IsAssigned &&
        BodyType.IsAssigned &&
        TailType.IsAssigned &&
        Motion.IsValid;
}

/// <summary>
/// Initial source-backed AI_006 relationship catalog. Each segment explicitly names its family
/// head/body/tail identities; slot links remain synchronized AI state, never inferred from type.
/// </summary>
public static class VanillaWormNpcCatalog
{
    private static readonly NpcTypeId[] WyvernFollowers =
    [
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernLegs,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernLegs,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody,
        VanillaNpcIds.WyvernBody2,
        VanillaNpcIds.WyvernBody3,
        VanillaNpcIds.WyvernTail
    ];

    private static readonly NpcTypeId[] CultistDragonFollowers =
    [
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody1,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody2,
        VanillaNpcIds.CultistDragonBody3,
        VanillaNpcIds.CultistDragonBody4,
        VanillaNpcIds.CultistDragonTail
    ];

    private static readonly VanillaWormNpcEntry[] Entries =
    [
        FamilyEntry(VanillaNpcIds.DevourerHead, 22, 22, 31, 2, 100, VanillaWormSegmentRole.Head,
            VanillaNpcIds.DevourerHead, VanillaNpcIds.DevourerBody, VanillaNpcIds.DevourerTail, 9f, 0.1f, 22f),
        FamilyEntry(VanillaNpcIds.DevourerBody, 22, 22, 16, 6, 100, VanillaWormSegmentRole.Body,
            VanillaNpcIds.DevourerHead, VanillaNpcIds.DevourerBody, VanillaNpcIds.DevourerTail, 9f, 0.1f, 22f),
        FamilyEntry(VanillaNpcIds.DevourerTail, 22, 22, 13, 10, 100, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.DevourerHead, VanillaNpcIds.DevourerBody, VanillaNpcIds.DevourerTail, 9f, 0.1f, 22f),

        FamilyEntry(VanillaNpcIds.GiantWormHead, 14, 14, 8, 0, 30, VanillaWormSegmentRole.Head,
            VanillaNpcIds.GiantWormHead, VanillaNpcIds.GiantWormBody, VanillaNpcIds.GiantWormTail, 6f, 0.05f, 14f),
        FamilyEntry(VanillaNpcIds.GiantWormBody, 14, 14, 4, 4, 30, VanillaWormSegmentRole.Body,
            VanillaNpcIds.GiantWormHead, VanillaNpcIds.GiantWormBody, VanillaNpcIds.GiantWormTail, 6f, 0.05f, 14f),
        FamilyEntry(VanillaNpcIds.GiantWormTail, 14, 14, 4, 6, 30, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.GiantWormHead, VanillaNpcIds.GiantWormBody, VanillaNpcIds.GiantWormTail, 6f, 0.05f, 14f),

        FamilyEntry(VanillaNpcIds.EaterOfWorldsHead, 38, 38, 22, 2, 150, VanillaWormSegmentRole.Head,
            VanillaNpcIds.EaterOfWorldsHead, VanillaNpcIds.EaterOfWorldsBody, VanillaNpcIds.EaterOfWorldsTail, 10f, 0.07f, 38f),
        FamilyEntry(VanillaNpcIds.EaterOfWorldsBody, 38, 38, 13, 4, 150, VanillaWormSegmentRole.Body,
            VanillaNpcIds.EaterOfWorldsHead, VanillaNpcIds.EaterOfWorldsBody, VanillaNpcIds.EaterOfWorldsTail, 10f, 0.07f, 38f),
        FamilyEntry(VanillaNpcIds.EaterOfWorldsTail, 38, 38, 11, 8, 150, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.EaterOfWorldsHead, VanillaNpcIds.EaterOfWorldsBody, VanillaNpcIds.EaterOfWorldsTail, 10f, 0.07f, 38f),

        FamilyEntry(VanillaNpcIds.BoneSerpentHead, 22, 22, 36, 12, 300, VanillaWormSegmentRole.Head,
            VanillaNpcIds.BoneSerpentHead, VanillaNpcIds.BoneSerpentBody, VanillaNpcIds.BoneSerpentTail, 9f, 0.1f, 22f, 0.08f),
        FamilyEntry(VanillaNpcIds.BoneSerpentBody, 22, 22, 20, 18, 300, VanillaWormSegmentRole.Body,
            VanillaNpcIds.BoneSerpentHead, VanillaNpcIds.BoneSerpentBody, VanillaNpcIds.BoneSerpentTail, 9f, 0.1f, 22f, 0.08f),
        FamilyEntry(VanillaNpcIds.BoneSerpentTail, 22, 22, 16, 18, 300, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.BoneSerpentHead, VanillaNpcIds.BoneSerpentBody, VanillaNpcIds.BoneSerpentTail, 9f, 0.1f, 22f, 0.08f),

        FamilyEntry(VanillaNpcIds.WyvernHead, 32, 32, 80, 10, 4000, VanillaWormSegmentRole.Head,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.WyvernLegs, 32, 32, 40, 20, 4000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.WyvernBody, 32, 32, 40, 20, 4000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.WyvernBody2, 32, 32, 40, 20, 4000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.WyvernBody3, 32, 32, 40, 20, 4000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.WyvernTail, 32, 32, 40, 20, 4000, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.WyvernHead, VanillaNpcIds.WyvernBody, VanillaNpcIds.WyvernTail, 11f, 0.25f, 42f, alwaysDig: true),

        FamilyEntry(VanillaNpcIds.DiggerHead, 22, 22, 45, 10, 200, VanillaWormSegmentRole.Head,
            VanillaNpcIds.DiggerHead, VanillaNpcIds.DiggerBody, VanillaNpcIds.DiggerTail, 5.5f, 0.045f, 22f, scale: 0.9f),
        FamilyEntry(VanillaNpcIds.DiggerBody, 22, 22, 28, 20, 200, VanillaWormSegmentRole.Body,
            VanillaNpcIds.DiggerHead, VanillaNpcIds.DiggerBody, VanillaNpcIds.DiggerTail, 5.5f, 0.045f, 22f, scale: 0.9f),
        FamilyEntry(VanillaNpcIds.DiggerTail, 22, 22, 26, 30, 200, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.DiggerHead, VanillaNpcIds.DiggerBody, VanillaNpcIds.DiggerTail, 5.5f, 0.045f, 22f, scale: 0.9f),

        FamilyEntry(VanillaNpcIds.SeekerHead, 22, 22, 70, 36, 500, VanillaWormSegmentRole.Head,
            VanillaNpcIds.SeekerHead, VanillaNpcIds.SeekerBody, VanillaNpcIds.SeekerTail, 8f, 0.07f, 22f),
        FamilyEntry(VanillaNpcIds.SeekerBody, 22, 22, 55, 40, 500, VanillaWormSegmentRole.Body,
            VanillaNpcIds.SeekerHead, VanillaNpcIds.SeekerBody, VanillaNpcIds.SeekerTail, 8f, 0.07f, 22f),
        FamilyEntry(VanillaNpcIds.SeekerTail, 22, 22, 40, 44, 500, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.SeekerHead, VanillaNpcIds.SeekerBody, VanillaNpcIds.SeekerTail, 8f, 0.07f, 22f),

        FamilyEntry(VanillaNpcIds.LeechHead, 14, 14, 26, 2, 60, VanillaWormSegmentRole.Head,
            VanillaNpcIds.LeechHead, VanillaNpcIds.LeechBody, VanillaNpcIds.LeechTail, 8f, 0.07f, 14f),
        FamilyEntry(VanillaNpcIds.LeechBody, 14, 14, 22, 6, 60, VanillaWormSegmentRole.Body,
            VanillaNpcIds.LeechHead, VanillaNpcIds.LeechBody, VanillaNpcIds.LeechTail, 8f, 0.07f, 14f),
        FamilyEntry(VanillaNpcIds.LeechTail, 14, 14, 18, 10, 60, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.LeechHead, VanillaNpcIds.LeechBody, VanillaNpcIds.LeechTail, 8f, 0.07f, 14f),

        FamilyEntry(VanillaNpcIds.TruffleWormDigger, 10, 10, 0, 0, 5, VanillaWormSegmentRole.Head,
            VanillaNpcIds.TruffleWormDigger, VanillaNpcIds.TruffleWormDigger, VanillaNpcIds.TruffleWormDigger, 6f, 0.15f, 10f),

        FamilyEntry(VanillaNpcIds.StardustWormHead, 32, 32, 80, 10, 1200, VanillaWormSegmentRole.Head,
            VanillaNpcIds.StardustWormHead, VanillaNpcIds.StardustWormHead, VanillaNpcIds.StardustWormHead, 9f, 0.3f, 32f, alwaysDig: true),

        FamilyEntry(VanillaNpcIds.SolarCrawltipedeHead, 20, 20, 120, 1000, 10000, VanillaWormSegmentRole.Head,
            VanillaNpcIds.SolarCrawltipedeHead, VanillaNpcIds.SolarCrawltipedeBody, VanillaNpcIds.SolarCrawltipedeTail, 10f, 0.3f, 26f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.SolarCrawltipedeBody, 20, 20, 80, 1000, 10000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.SolarCrawltipedeHead, VanillaNpcIds.SolarCrawltipedeBody, VanillaNpcIds.SolarCrawltipedeTail, 10f, 0.3f, 26f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.SolarCrawltipedeTail, 20, 20, 50, 0, 10000, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.SolarCrawltipedeHead, VanillaNpcIds.SolarCrawltipedeBody, VanillaNpcIds.SolarCrawltipedeTail, 10f, 0.3f, 26f, alwaysDig: true),

        FamilyEntry(VanillaNpcIds.CultistDragonHead, 32, 32, 100, 15, 10000, VanillaWormSegmentRole.Head,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.CultistDragonBody1, 32, 32, 50, 30, 10000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.CultistDragonBody2, 32, 32, 50, 30, 10000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.CultistDragonBody3, 32, 32, 50, 30, 10000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.CultistDragonBody4, 32, 32, 50, 30, 10000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.CultistDragonTail, 32, 32, 50, 30, 10000, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.CultistDragonHead, VanillaNpcIds.CultistDragonBody2, VanillaNpcIds.CultistDragonTail, 20f, 0.55f, 36f, alwaysDig: true),

        FamilyEntry(VanillaNpcIds.DuneSplicerHead, 34, 34, 58, 18, 500, VanillaWormSegmentRole.Head,
            VanillaNpcIds.DuneSplicerHead, VanillaNpcIds.DuneSplicerBody, VanillaNpcIds.DuneSplicerTail, 10f, 0.25f, 34f),
        FamilyEntry(VanillaNpcIds.DuneSplicerBody, 34, 34, 54, 28, 500, VanillaWormSegmentRole.Body,
            VanillaNpcIds.DuneSplicerHead, VanillaNpcIds.DuneSplicerBody, VanillaNpcIds.DuneSplicerTail, 10f, 0.25f, 34f),
        FamilyEntry(VanillaNpcIds.DuneSplicerTail, 34, 34, 50, 34, 500, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.DuneSplicerHead, VanillaNpcIds.DuneSplicerBody, VanillaNpcIds.DuneSplicerTail, 10f, 0.25f, 34f),

        FamilyEntry(VanillaNpcIds.TombCrawlerHead, 22, 22, 18, 0, 60, VanillaWormSegmentRole.Head,
            VanillaNpcIds.TombCrawlerHead, VanillaNpcIds.TombCrawlerBody, VanillaNpcIds.TombCrawlerTail, 7f, 0.1f, 16f),
        FamilyEntry(VanillaNpcIds.TombCrawlerBody, 22, 22, 7, 12, 60, VanillaWormSegmentRole.Body,
            VanillaNpcIds.TombCrawlerHead, VanillaNpcIds.TombCrawlerBody, VanillaNpcIds.TombCrawlerTail, 7f, 0.1f, 16f),
        FamilyEntry(VanillaNpcIds.TombCrawlerTail, 22, 22, 7, 14, 60, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.TombCrawlerHead, VanillaNpcIds.TombCrawlerBody, VanillaNpcIds.TombCrawlerTail, 7f, 0.1f, 16f),

        FamilyEntry(VanillaNpcIds.BloodEelHead, 28, 28, 90, 0, 6000, VanillaWormSegmentRole.Head,
            VanillaNpcIds.BloodEelHead, VanillaNpcIds.BloodEelBody, VanillaNpcIds.BloodEelTail, 15f, 0.45f, 24f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.BloodEelBody, 28, 28, 60, 30, 6000, VanillaWormSegmentRole.Body,
            VanillaNpcIds.BloodEelHead, VanillaNpcIds.BloodEelBody, VanillaNpcIds.BloodEelTail, 15f, 0.45f, 24f, alwaysDig: true),
        FamilyEntry(VanillaNpcIds.BloodEelTail, 28, 28, 50, 40, 6000, VanillaWormSegmentRole.Tail,
            VanillaNpcIds.BloodEelHead, VanillaNpcIds.BloodEelBody, VanillaNpcIds.BloodEelTail, 15f, 0.45f, 24f, alwaysDig: true)
    ];

    public static int Count => Entries.Length;
    public static ReadOnlySpan<VanillaWormNpcEntry> All => Entries;

    public static bool TryGet(NpcTypeId type, out VanillaWormNpcEntry entry)
    {
        foreach (VanillaWormNpcEntry candidate in Entries)
        {
            if (candidate.Definition.Type == type)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    public static bool TryGetDefinition(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (TryGet(type, out VanillaWormNpcEntry entry))
        {
            definition = entry.Definition;
            return true;
        }

        definition = default;
        return false;
    }

    public static bool TryGetInitialSegmentCountRange(
        NpcTypeId headType,
        out int minimumInclusive,
        out int maximumExclusive)
    {
        if (headType == VanillaNpcIds.DevourerHead)
        {
            minimumInclusive = 8;
            maximumExclusive = 13;
            return true;
        }

        if (headType == VanillaNpcIds.GiantWormHead)
        {
            minimumInclusive = 4;
            maximumExclusive = 7;
            return true;
        }

        if (headType == VanillaNpcIds.BoneSerpentHead)
        {
            minimumInclusive = 14;
            maximumExclusive = 23;
            return true;
        }

        if (headType == VanillaNpcIds.DiggerHead)
        {
            minimumInclusive = 6;
            maximumExclusive = 12;
            return true;
        }

        if (headType == VanillaNpcIds.SeekerHead)
        {
            minimumInclusive = 20;
            maximumExclusive = 26;
            return true;
        }

        if (headType == VanillaNpcIds.LeechHead)
        {
            minimumInclusive = 3;
            maximumExclusive = 6;
            return true;
        }

        if (headType == VanillaNpcIds.DuneSplicerHead)
        {
            minimumInclusive = 11;
            maximumExclusive = 20;
            return true;
        }

        if (headType == VanillaNpcIds.TombCrawlerHead)
        {
            minimumInclusive = 5;
            maximumExclusive = 9;
            return true;
        }

        if (headType == VanillaNpcIds.BloodEelHead)
        {
            minimumInclusive = 15;
            maximumExclusive = 16;
            return true;
        }

        if (headType == VanillaNpcIds.SolarCrawltipedeHead)
        {
            minimumInclusive = 29;
            maximumExclusive = 30;
            return true;
        }

        minimumInclusive = 0;
        maximumExclusive = 0;
        return false;
    }

    public static bool HasChainProfile(NpcTypeId headType) =>
        headType == VanillaNpcIds.EaterOfWorldsHead ||
        TryGetInitialSegmentCountRange(headType, out _, out _) ||
        TryGetFixedFollowerSequence(headType, out _);

    public static int GetEaterOfWorldsBodyCount(bool expertMode) =>
        expertMode ? 70 : 65;

    public static bool TryGetFixedFollowerType(
        NpcTypeId headType,
        int remainingFollowersAfterChild,
        out NpcTypeId followerType)
    {
        if (!TryGetFixedFollowerSequence(headType, out NpcTypeId[] sequence) ||
            remainingFollowersAfterChild < 0 ||
            remainingFollowersAfterChild >= sequence.Length)
        {
            followerType = default;
            return false;
        }

        followerType = sequence[sequence.Length - remainingFollowersAfterChild - 1];
        return true;
    }

    public static bool TryGetFixedFollowerCount(NpcTypeId headType, out int count)
    {
        if (TryGetFixedFollowerSequence(headType, out NpcTypeId[] sequence))
        {
            count = sequence.Length;
            return true;
        }

        count = 0;
        return false;
    }

    private static bool TryGetFixedFollowerSequence(
        NpcTypeId headType,
        out NpcTypeId[] sequence)
    {
        if (headType == VanillaNpcIds.WyvernHead)
        {
            sequence = WyvernFollowers;
            return true;
        }

        if (headType == VanillaNpcIds.CultistDragonHead)
        {
            sequence = CultistDragonFollowers;
            return true;
        }

        sequence = [];
        return false;
    }

    private static VanillaWormNpcEntry FamilyEntry(
        NpcTypeId type,
        int width,
        int height,
        int damage,
        int defense,
        int lifeMax,
        VanillaWormSegmentRole role,
        NpcTypeId head,
        NpcTypeId body,
        NpcTypeId tail,
        float speed,
        float turn,
        float gap,
        float risingGravity = 0.11f,
        float scale = 1f,
        bool alwaysDig = false) =>
        new(
            new VanillaNpcDefinition(
                type,
                VanillaNpcAiStyles.Worm,
                VanillaNpcBehaviorFamily.Worm,
                VanillaNpcPhysicsFamily.NoClipFlight,
                NpcArchetypeRole.Ordinary,
                width,
                height,
                damage,
                defense,
                lifeMax,
                KnockBackResist: 0f,
                Scale: scale,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                VanillaNpcSyncAnchor.TopLeft),
            role,
            head,
            body,
            tail,
            new VanillaWormMotionProfile(speed, turn, gap, 0.11f, risingGravity, alwaysDig));
}
