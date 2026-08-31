# Server-authoritative player movement

TerraRuntime treats packet 13 as an input report, not as permission for the client to replace server movement history.

## Boundary contract

`RuntimePlayerMovementIngress` first applies the protocol-neutral `VanillaPlayerMovementNormalizer`, then passes the normalized report through `RuntimePlayerMovementAuthority` before the command can enter the bounded authoritative queue.

The authority owns movement history by generation-safe `PlayerHandle`, not by reusable player slot alone. When a newer generation appears for a slot, history from the previous occupant is discarded. A command from an older generation is rejected before queueing.

The current hard rejection rules are deliberately conservative:

- non-finite packet fields remain rejected by the normalizer;
- velocity outside the bounded server plausibility envelope is rejected before the game-loop queue;
- stale player generations are rejected before the game-loop queue;
- failed queue admission does not advance trusted movement history.

The production boundary also detects position discontinuities from the last successfully queued position, elapsed monotonic time, and a deliberately generous jitter/travel allowance. These discontinuities are observable today but are **not rejected by default**. This avoids turning incomplete teleport/respawn coverage into a false-positive anti-cheat system.

## Exceptional movement permits

Strict position enforcement is already supported by the authority for tests and future production enablement. A server subsystem can grant one short-lived, single-use exception tied to the exact player generation for:

- teleport;
- respawn;
- mount transition;
- server correction.

A permit may optionally name an expected target position and radius. A stale generation cannot consume a permit issued to another occupation of the same wire slot.

Production position enforcement should be enabled only after every legitimate discontinuity producer grants an explicit permit. Until then, `PositionViolations` is telemetry rather than a kick/ban signal.

## Observability

`RuntimePlayerMovementAuthoritySnapshot` exposes accepted reports, queue rejections, stale-generation rejections, velocity rejections, observed position discontinuities, strict-mode position rejections, accepted exceptional moves, tracked players, and whether strict position enforcement is enabled.

These counters describe the boundary itself. They must not be interpreted as proof that a player is cheating without the surrounding server state.

## Regression gate

The `Player Movement Authority` workflow builds the .NET 11 test project and runs both the existing packet-13 ingress tests and the generation/history authority contract. The focused suite proves impossible-velocity rejection, generation reuse safety, one-shot teleport permits, observe-only production position handling, and transactional history when the bounded queue refuses a command.
