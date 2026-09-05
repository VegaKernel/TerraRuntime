# TerraRuntime.Core layout and naming rules

These rules supplement repository-root `AGENTS.md` and `src/AGENTS.md` for work under `src/TerraRuntime.Core/`.

## Ownership

`TerraRuntime.Core` is the authoritative execution-mechanics project, not a catch-all Terraria implementation project.

Keep here:

- authoritative single-writer ownership primitives;
- scheduling/game-loop mechanics;
- typed command ingress/application boundaries;
- bounded worker/lifecycle mechanics;
- mutable runtime stores/executors whose semantics are specifically about authoritative runtime ownership;
- genuinely cross-subsystem runtime mechanics that cannot live lower without creating the wrong dependency direction.

Move protocol-neutral gameplay semantics, item/NPC/buff definitions and source-backed gameplay catalogs to `TerraRuntime.Gameplay` when they do not require Core ownership mechanics.

Do not move `WorldItem` runtime entity storage into `Gameplay.Items` merely because both names contain “Item”. Inventory/content semantics and live world-item entity ownership are separate concerns.

Do not put `.wld` parsing/persistence here; that belongs to `TerraRuntime.World`. Do not put Vega/plugin/module policy here.

This repository has no backwards-compatibility commitment yet. When a type moves to a better owner, migrate its namespace and callers directly. Do not add Core compatibility facades for moved gameplay types.

## Naming

Follow `src/AGENTS.md` and use the shortest unambiguous domain name after namespace ownership is correct.

Use `Vanilla` only when it communicates a real source/version/vanilla boundary. Source-backed catalogs such as `VanillaDefinitionCatalog` intentionally keep that marker.

Avoid proxy-only `*Facts`, `*Helper`, `*Provider`, `*Manager`, `*Service`, `*Factory` types. In particular, do not create a `WorldRuntimeManager` simply because multiple worlds exist; use the concrete owner such as a runtime registry/host only if it owns real lifecycle/state.

A compatibility facade requires an actual external compatibility commitment and a removal/migration reason. Hypothetical future callers do not qualify.

## Layering

`TerraRuntime.Gameplay` may be a dependency of Core for protocol-neutral gameplay semantics. Core must not be required by Gameplay merely to access a catalog, ID rule or pure evaluator.

Vega, plugin and module concepts stay above TerraRuntime. Core exposes runtime mechanics/contracts; external policy composes them from above.
