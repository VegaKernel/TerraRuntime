using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Bots;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Application.Bots;

internal enum RuntimeBotAttackKind : byte
{
    Melee,
    Bow,
    Gun
}

internal readonly record struct BotGuardTarget(
    NpcHandle Npc,
    PlayerHandle Player,
    float CenterX,
    float CenterY,
    float VelocityX,
    float VelocityY,
    float Width,
    float Height);

internal readonly record struct PlayerBotPersonality(
    float EscortOffsetX,
    float EscortOffsetY,
    long WanderPhaseTicks);

internal readonly record struct PlayerBotVisualIdentity(
    byte SkinVariant,
    byte VoiceVariant,
    float VoicePitchOffset,
    byte Hair,
    PlayerRgbColor HairColor,
    PlayerRgbColor SkinColor,
    PlayerRgbColor EyeColor,
    PlayerRgbColor ShirtColor,
    PlayerRgbColor UnderShirtColor,
    PlayerRgbColor PantsColor,
    PlayerRgbColor ShoeColor,
    ItemTypeId HeadArmor,
    ItemTypeId BodyArmor,
    ItemTypeId LegArmor);

internal sealed class BotState(
    int id,
    ServerPlayerId serverPlayerId,
    string name,
    RuntimeBotConfiguration configuration,
    RuntimePlayerBotLoadout1458 loadout,
    PlayerBotVisualIdentity visualIdentity,
    PlayerBotPersonality personality,
    long createdAtTick)
{
    public int Id { get; } = id;
    public ulong GoalGeneration { get; set; } = 1;
    public ulong ObservationRevision { get; set; }
    public long CurrentTick { get; set; }
    public WorldItemHandle LastPickedItem { get; set; }
    public RuntimeBotController Controller { get; set; } = null!;
    public ServerPlayerId ServerPlayerId { get; } = serverPlayerId;
    public PlayerHandle Player { get; set; }
    public bool OwnsCurrentActor(ServerPlayerAuthority authority) =>
        authority.TryGetPlayer(ServerPlayerId, out var current) && current == Player;
    public string Name { get; } = name;
    public RuntimeBotConfiguration Configuration { get; set; } = configuration;
    public RuntimePlayerBotLoadout1458 Loadout { get; } = loadout;
    public PlayerBotVisualIdentity VisualIdentity { get; } = visualIdentity;
    public PlayerBotPersonality Personality { get; } = personality;
    public bool TargetAvailable { get; set; }
    public bool PvpEnabled { get; set; }
    public bool IsStuck { get; set; }
    public bool IsDead { get; set; }
    public long TeleportCount { get; set; }
    public long LastProgressTick { get; set; } = createdAtTick;
    public long TeleportCooldownUntil { get; set; }
    public long MirrorStartedAtTick { get; set; } = -1;
    public bool MirrorTeleported { get; set; }
    public long FlightDecisionUntilTick { get; set; }
    public long TraversalUntilTick { get; set; }
    public long NextTraversalSearchTick { get; set; }
    public float TraversalX { get; set; }
    public float TraversalY { get; set; }
    public long NextAttackTick { get; set; }
    public long UseItemUntilTick { get; set; }
    public long PotionDelayUntilTick { get; set; }
    public NpcHandle LockedGuardNpc { get; set; }
    public PlayerHandle LockedGuardPlayer { get; set; }
    public long GuardTargetLockUntilTick { get; set; }
    public long GuardRepositionUntilTick { get; set; }
    public float GuardRepositionX { get; set; }
    public float GuardRepositionY { get; set; }
    public float LastDistance { get; set; } = float.PositiveInfinity;
    public Dictionary<BuffTypeId, long> ActiveBuffs { get; } = [];
}
