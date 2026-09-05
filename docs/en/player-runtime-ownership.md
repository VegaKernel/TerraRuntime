# Player runtime ownership

[Русский](../ru/player-runtime-ownership.md) · [Architecture](architecture.md)

## Ownership rule

A connected player's mutable gameplay state is owned by exactly one `WorldRuntime` authoritative loop at a time. Socket routing, operator commands and sandbox control code do not receive player stores or mutable transfer payloads.

Normal packet handling enters the owning runtime through typed authoritative commands. Cross-runtime movement uses the same rule rather than temporarily making connection code a second state owner.

## Layer ownership

Player data now follows the same dependency direction as the rest of the runtime:

- `TerraRuntime.Contracts.Runtime` owns detached player commit DTOs such as `PlayerAppearanceCommitRequest`, `PlayerMovementCommitRequest`, `PlayerSpawnCommitRequest`, equipment and vitals requests;
- `TerraRuntime.Gameplay.Players` owns source-backed vanilla normalization and validation that does not retain mutable runtime state;
- `TerraRuntime.Core` owns the authoritative ingress contracts and shared execution mechanics;
- `TerraRuntime.Core.Players` owns server-player slot identities and mutable server-player state stores, keeping player-specific mechanics out of the flat Core namespace;
- application composition owns connection admission, anti-cheat/history policy and the concrete authoritative command routing; its world-owned `ServerPlayerAuthority` is the sole application-level owner that combines server-player lifecycle, semantic control intents, physics progression and replication events.

Packet-5 signed net-id compatibility conversion is owned by the application ingress boundary in `PlayerEquipmentPacket5Normalizer`. Core receives canonical positive item identities and validates server-owned inventory state directly; Gameplay remains free of wire compatibility arithmetic.

## Cross-runtime transfer

A Level 1 transfer has three distinct ownership phases:

```mermaid
sequenceDiagram
    participant Route as Connection route
    participant Source as Source WorldRuntime
    participant Tx as Detached transfer transaction
    participant Destination as Destination WorldRuntime

    Route->>Source: typed detach barrier
    Source-->>Tx: detached ownership token
    Note over Source,Tx: source no longer owns live player state
    Route->>Destination: reserve/register socket binding + bootstrap
    Route->>Tx: attach to destination
    Tx->>Destination: typed attach barrier
    Destination-->>Tx: accepted
    Note over Tx,Destination: destination is now the sole owner
```

`RuntimePlayerTransferTransaction` keeps the detached `RuntimePlayerTransferState` private. `RuntimeConnectionRoute` can read only the small routing projection it needs, such as the player name, and can request one of three terminal actions:

- attach the player to a destination `WorldRuntime`;
- restore the exact detached state to the source runtime after a failed move;
- discard the detached state during an intentional disconnect.

The transaction is single-use. After attach, restore or discard, another terminal action is rejected. This makes accidental double ownership and reuse of a detached payload explicit failures instead of implicit shared-state behavior.

## Failure semantics

Destination slot reservation happens before the source detach barrier where possible. Once the source has detached the player, any later routing/bootstrap/attach failure must restore the source authoritative state before normal play resumes.

The route never reaches into another runtime's player dictionary, inventory store or transfer-profile store. All mutable-state transitions cross `RuntimePlayerTransferIngress`, so the destination game loop remains the only code allowed to install transferred state.

World-space position is not portable player state across different worlds. A successful cross-runtime transfer always installs the destination world's spawn coordinates; the previous coordinates are retained only for rollback into the same source runtime when a transfer fails. This is independent of `forceRespawn`: an ordinary drag/move into a sandbox also arrives at that world's spawn.

Same-runtime respawn uses the same detach/attach transaction. That keeps respawn and sandbox movement on one ownership model instead of maintaining a second mutation path. Once an inbound client packet 12 is accepted as a respawn, it is relayed to other playing connections but is not echoed back to the originating connection: that client already performed its spawn transition, and replaying the same spawn can retrigger the local transition and form a feedback loop.

## Runtime GodMode ownership

GodMode is an authoritative, generation-scoped runtime flag. It is set only through the typed trusted-host/TUI administration boundary, travels with the detached Level-1 transfer state for the same live connection, and is discarded on disconnect. It is not persisted. Authoritative PvP, NPC contact and admitted hostile NPC-projectile damage return before HP/immunity mutation when the flag is enabled.

Terraria 1.4.5.8 still computes lava, drowning and fall damage locally through `Player.Hurt`. Until those environmental sources are fully simulated by TerraRuntime, a GodMode player cannot lower authoritative HP through incoming packet 16: a decrease is rejected and the current authoritative health is immediately replicated back to that connection. This is a compatibility guard, not permission for arbitrary client health authority.
