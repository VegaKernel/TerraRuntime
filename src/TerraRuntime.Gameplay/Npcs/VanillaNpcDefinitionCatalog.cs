using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

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
    KingSlime = 6,
    Worm = 7,
    BrainOfCthulhu = 8,
    BrainCreeper = 9,
    Vulture = 10,
    SpikeBall = 11,
    BlazingWheel = 12,
    SkeletronHead = 13,
    SkeletronHand = 14,
    QueenBee = 15,
    Deerclops = 16
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
    NoClipFlight = 4,
    Vulture = 5,
    SpikeBall = 6,
    BlazingWheel = 7
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
    /// <summary>SetDefaults-owned invulnerability that must exist before the first AI tick.</summary>
    public bool DontTakeDamageAtSpawn { get; init; }

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
    public const int NewNpcTimeLeft = 937;
    public const int DefaultSpriteDirection = -1;

    public static bool TryGet(NpcTypeId type, out VanillaNpcDefinition definition)
    {
        if (VanillaTownNpcFacts1458.TryGetDefinition(type, out definition))
            return true;

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

        if (VanillaSlimeNpcCatalog.TryGetDefinition(type, out definition))
            return true;

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

        if (VanillaFlyingEyeNpcCatalog.TryGetDefinition(type, out definition))
            return true;

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

        if (VanillaFlyerNpcCatalog.TryGetDefinition(type, out definition))
            return true;

        if (VanillaWormNpcCatalog.TryGetDefinition(type, out definition))
            return true;

        if (VanillaNpcAi17_20_21Catalog1458.TryGetDefinition(type, out definition))
            return true;

        if (type == VanillaNpcIds.QueenBee)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.QueenBee,
                AiStyle: VanillaNpcAiStyles.QueenBee,
                BehaviorFamily: VanillaNpcBehaviorFamily.QueenBee,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 66,
                BaseHeight: 66,
                Damage: 30,
                Defense: 8,
                LifeMax: 3400,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.Deerclops)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.Deerclops,
                AiStyle: VanillaNpcAiStyles.Deerclops,
                BehaviorFamily: VanillaNpcBehaviorFamily.Deerclops,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 60,
                BaseHeight: 154,
                Damage: 20,
                Defense: 10,
                LifeMax: 7000,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.BrainOfCthulhu)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.BrainOfCthulhu,
                AiStyle: VanillaNpcAiStyles.BrainOfCthulhu,
                BehaviorFamily: VanillaNpcBehaviorFamily.BrainOfCthulhu,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 160,
                BaseHeight: 110,
                Damage: 30,
                Defense: 14,
                LifeMax: 1250,
                KnockBackResist: 0.45f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft)
            {
                DontTakeDamageAtSpawn = true
            };
            return true;
        }

        if (type == VanillaNpcIds.BrainCreeper)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.BrainCreeper,
                AiStyle: VanillaNpcAiStyles.BrainCreeper,
                BehaviorFamily: VanillaNpcBehaviorFamily.BrainCreeper,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 30,
                BaseHeight: 30,
                Damage: 20,
                Defense: 10,
                LifeMax: 100,
                KnockBackResist: 0.8f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.SkeletronHead)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.SkeletronHead,
                AiStyle: VanillaNpcAiStyles.SkeletronHead,
                BehaviorFamily: VanillaNpcBehaviorFamily.SkeletronHead,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Boss,
                BaseWidth: 80,
                BaseHeight: 102,
                Damage: 32,
                Defense: 10,
                LifeMax: 4400,
                KnockBackResist: 0f,
                Scale: 1f,
                NoGravityAtSpawn: true,
                NoTileCollideAtSpawn: true,
                SyncAnchor: VanillaNpcSyncAnchor.TopLeft);
            return true;
        }

        if (type == VanillaNpcIds.SkeletronHand)
        {
            definition = new VanillaNpcDefinition(
                Type: VanillaNpcIds.SkeletronHand,
                AiStyle: VanillaNpcAiStyles.SkeletronHand,
                BehaviorFamily: VanillaNpcBehaviorFamily.SkeletronHand,
                PhysicsFamily: VanillaNpcPhysicsFamily.NoClipFlight,
                Role: NpcArchetypeRole.Ordinary,
                BaseWidth: 52,
                BaseHeight: 52,
                Damage: 20,
                Defense: 14,
                LifeMax: 600,
                KnockBackResist: 0f,
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

    /// <summary>
    /// Resolves the effective defaults for a positive gameplay type paired with its signed wire
    /// identity. Unknown or mismatched negative identities fail closed.
    /// </summary>
    public static bool TryGet(
        NpcTypeId type,
        NpcNetId netId,
        out VanillaNpcDefinition definition)
    {
        if (!TryGet(type, out VanillaNpcDefinition baseDefinition))
        {
            definition = default;
            return false;
        }

        if (netId.Value >= 0)
        {
            if (netId.Value != type.Value)
            {
                definition = default;
                return false;
            }

            definition = baseDefinition;
            return true;
        }

        if (!VanillaNpcNetVariantCatalog.TryGet(netId, out VanillaNpcNetVariantDefinition variant) ||
            variant.Type != type)
        {
            definition = default;
            return false;
        }

        definition = variant.ApplyTo(in baseDefinition);
        return true;
    }
}
