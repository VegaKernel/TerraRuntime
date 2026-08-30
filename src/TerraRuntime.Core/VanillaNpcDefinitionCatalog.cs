using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Runtime-owned implementation family selected for one verified vanilla NPC definition.
/// AiStyle remains source metadata; this family is an explicit opt-in to a TerraRuntime behavior
/// implementation so a future NPC that happens to share an aiStyle cannot silently inherit a
/// behavior path that has not been verified for that type.
/// </summary>
public enum VanillaNpcBehaviorFamily : byte
{
    None = 0,
    SlimeGround = 1,
    FlyingEye = 2,
    GroundFighter = 3,
    EyeOfCthulhu = 4,
    Flyer = 5,
    KingSlime = 6
}

/// <summary>
/// Runtime-owned physics family selected independently from AI behavior. A shared AI style/family does not
/// prove identical gravity, collision, platform or obstacle semantics, so every admitted definition opts in
/// explicitly to the world-motion path that has been verified for it.
/// </summary>
public enum VanillaNpcPhysicsFamily : byte
{
    None = 0,
    SlimeGround = 1,
    FlyingEye = 2,
    GroundFighter = 3,
    NoClipFlight = 4
}

/// <summary>One resolved vanilla NPC hitbox for the current runtime scale.</summary>
public readonly record struct VanillaNpcHitboxSize(int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>Terraria packet-23 position anchor expressed as fractions of the current live NPC hitbox.</summary>
public readonly record struct VanillaNpcSyncAnchor(float X, float Y)
{
    public static VanillaNpcSyncAnchor TopLeft => default;

    public bool IsValid =>
        float.IsFinite(X) &&
        float.IsFinite(Y) &&
        X is >= 0f and <= 1f &&
        Y is >= 0f and <= 1f;
}

/// <summary>
/// Source-backed vanilla NPC defaults required by authoritative lifecycle and AI bring-up.
/// BaseWidth/BaseHeight are the raw SetDefaults dimensions before vanilla applies NPC.scale. Width/Height expose
/// the initial post-scale hitbox for compatibility. Runtime geometry that observes mutable NPC scale must call
/// TryResolveHitbox with the live simulation scale instead of freezing the initial dimensions.
/// </summary>
public readonly record struct VanillaNpcDefinition(
    NpcTypeId Type,
    NpcAiStyleId AiStyle,
    VanillaNpcBehaviorFamily BehaviorFamily,
    VanillaNpcPhysicsFamily PhysicsFamily,
    NpcArchetypeRole Role,
    int BaseWidth,
    int BaseHeight,
    int Damage,
    int Defense,
    int LifeMax,
    float KnockBackResist,
    float Scale,
    bool NoGravityAtSpawn,
    bool NoTileCollideAtSpawn,
    VanillaNpcSyncAnchor SyncAnchor)
{
    public bool IsBoss => Role == NpcArchetypeRole.Boss;

    public int Width => TryResolveHitbox(Scale, out VanillaNpcHitboxSize hitbox) ? hitbox.Width : BaseWidth;

    public int Height => TryResolveHitbox(Scale, out VanillaNpcHitboxSize hitbox) ? hitbox.Height : BaseHeight;

    public bool TryResolveHitbox(float scale, out VanillaNpcHitboxSize hitbox)
    {
        if (BaseWidth <= 0 ||
            BaseHeight <= 0 ||
            !float.IsFinite(scale) ||
            scale <= 0f)
        {
            hitbox = default;
            return false;
        }

        double scaledWidth = Math.Floor(BaseWidth * (double)scale);
        double scaledHeight = Math.Floor(BaseHeight * (double)scale);
        if (scaledWidth < 1d ||
            scaledWidth > int.MaxValue ||
            scaledHeight < 1d ||
            scaledHeight > int.MaxValue)
        {
            hitbox = default;
            return false;
        }

        hitbox = new VanillaNpcHitboxSize((int)scaledWidth, (int)scaledHeight);
        return true;
    }
}

/// <summary>
/// Initial verified slice of the Terraria 1.4.5.8 NPC defaults catalog.
/// Reference TerrariaServer.exe SHA-256:
/// d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e.
/// </summary>
public static class VanillaNpcDefinitionCatalog
{
    public const ushort DefaultTarget = byte.MaxValue;
    public const int DefaultTimeLeft = 750;
    public const int DefaultSpriteDirection = -1;

    /// <summary>
    /// Raw-id compatibility boundary for protocol/bootstrap callers that have not yet crossed into
    /// typed gameplay identity. Authoritative gameplay should prefer the typed overload.
    /// </summary>
    public static bool TryGet(int type, out VanillaNpcDefinition definition)
    {
        if (!NpcTypeId.TryCreate(type, out NpcTypeId npcType))
        {
            definition = default;
            return false;
        }

        return TryGet(npcType, out definition);
    }

    public static bool TryGet(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (type == VanillaNpcIds.BlueSlime)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.BlueSlime,
                AiStyle: VanillaNpcAiStyles.Slime,
                BehaviorFamily: VanillaNpcBehaviorFamily.SlimeGround,
                PhysicsFamily: VanillaNpcPhysicsFamily.SlimeGround,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 24,
                BaseHeight: 18,
                Damage: 7,
                Defense: 2,
                LifeMax: 25,
                KnockBackResist: 1f,
                Scale: 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.DemonEye)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.DemonEye,
                AiStyle: VanillaNpcAiStyles.DemonEye,
                BehaviorFamily: VanillaNpcBehaviorFamily.FlyingEye,
                PhysicsFamily: VanillaNpcPhysicsFamily.FlyingEye,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 30,
                BaseHeight: 32,
                Damage: 18,
                Defense: 2,
                LifeMax: 60,
                KnockBackResist: 0.8f,
                Scale: 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.Zombie)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.Zombie,
                AiStyle: VanillaNpcAiStyles.Fighter,
                BehaviorFamily: VanillaNpcBehaviorFamily.GroundFighter,
                PhysicsFamily: VanillaNpcPhysicsFamily.GroundFighter,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 18,
                BaseHeight: 40,
                Damage: 14,
                Defense: 6,
                LifeMax: 45,
                KnockBackResist: 0.5f,
                Scale: 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.EyeOfCthulhu)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.EyeOfCthulhu,
                AiStyle: VanillaNpcAiStyles.EyeOfCthulhu,
                BehaviorFamily: VanillaNpcBehaviorFamily.EyeOfCthulhu,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 100,
                BaseHeight: 110,
                Damage: 15,
                Defense: 12,
                LifeMax: 2800,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.ServantOfCthulhu)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.ServantOfCthulhu,
                AiStyle: VanillaNpcAiStyles.Flyer,
                BehaviorFamily: VanillaNpcBehaviorFamily.Flyer,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 20,
                BaseHeight: 20,
                Damage: 12,
                Defense: 0,
                LifeMax: 8,
                KnockBackResist: 1f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.Skeleton)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.Skeleton,
                AiStyle: VanillaNpcAiStyles.Fighter,
                BehaviorFamily: VanillaNpcBehaviorFamily.GroundFighter,
                PhysicsFamily: VanillaNpcPhysicsFamily.GroundFighter,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 18,
                BaseHeight: 40,
                Damage: 20,
                Defense: 8,
                LifeMax: 60,
                KnockBackResist: 0.5f,
                Scale: 1f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.KingSlime)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.KingSlime,
                AiStyle: VanillaNpcAiStyles.KingSlime,
                BehaviorFamily: VanillaNpcBehaviorFamily.KingSlime,
                PhysicsFamily: VanillaNpcPhysicsFamily.SlimeGround,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 98,
                BaseHeight: 92,
                Damage: 40,
                Defense: 10,
                LifeMax: 2000,
                KnockBackResist: 0f,
                Scale: 1.25f,
                NoGravityAtSpawn: false,
                NoTileCollideAtSpawn: false,
                SyncAnchor: new VanillaNpcSyncAnchor(0.5f, 1f));
            return true;
        }

        definition = default;
        return false;
    }
}
