using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Contracts.Gameplay;

public enum ActorInteractionKind : byte
{
    NpcConversation = 1,
    NpcShopOpen = 2
}

/// <summary>
/// Generation-safe semantic interaction request. Wire slot identities must be resolved before this boundary.
/// </summary>
public readonly record struct ActorInteractionRequest(
    PlayerHandle Player,
    NpcHandle Target,
    ActorInteractionKind Kind);

public enum ActorInteractionValidationResult : byte
{
    Accepted = 0,
    InvalidRequest = 1,
    InvalidPlayer = 2,
    PlayerUnavailable = 3,
    InvalidTarget = 4,
    TargetUnavailable = 5,
    UnsupportedTargetType = 6,
    OutOfRange = 7
}

/// <summary>
/// Revisions captured by the authoritative interaction validation point.
/// </summary>
public readonly record struct ActorInteractionAcceptance(
    ActorInteractionRequest Request,
    PlayerStateRevision PlayerRevision,
    NpcRevision TargetRevision);
