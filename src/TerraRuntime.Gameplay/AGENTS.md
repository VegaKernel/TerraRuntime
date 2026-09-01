# TerraRuntime.Gameplay layout and naming rules

These rules supplement the repository-root `AGENTS.md` for work under `src/TerraRuntime.Gameplay/`.

## Ownership

`TerraRuntime.Gameplay` owns protocol-neutral Terraria gameplay definitions and behavior that do not belong to runtime scheduling, networking, persistence, host composition, or Vega policy.

Keep subject namespaces aligned with ownership, for example `TerraRuntime.Gameplay.Items`, `TerraRuntime.Gameplay.Npcs`, `TerraRuntime.Gameplay.Players`, and `TerraRuntime.Gameplay.Buffs`.

Source-backed catalogs belong here when they describe verified gameplay/content semantics rather than runtime execution mechanics. Do not create compatibility facades in `TerraRuntime.Core` for moved types; this repository has no backwards-compatibility commitment yet.

## Dependencies

Keep this project as low in the dependency graph as the behavior allows. Prefer `TerraRuntime.Contracts` and BCL dependencies. Add a dependency on another TerraRuntime project only when the gameplay implementation genuinely needs that project's semantics; never introduce a Core <-> Gameplay cycle to make a move compile.

World persistence, protocol framing, sockets, terminal UI, Vega/plugin policy, and process transport do not belong here.

## Naming

Use namespaces to carry repeated context. Keep `Vanilla` and version suffixes on source-pinned public catalogs/behaviors where they communicate a real vanilla/version boundary. Internal helpers should use the shortest unambiguous domain name and should not repeat `Runtime`, `Gameplay`, `World`, and subsystem names merely because they are available.

Delete transparent `*Facts`, `*Helper`, `*Provider`, `*Manager`, and compatibility wrappers instead of moving them.
