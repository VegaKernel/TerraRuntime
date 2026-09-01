# TerraRuntime.Gameplay layout and naming rules

These rules supplement repository-root `AGENTS.md` and `src/AGENTS.md` for work under `src/TerraRuntime.Gameplay/`.

## Ownership

`TerraRuntime.Gameplay` owns protocol-neutral Terraria gameplay definitions and behavior that do not belong to runtime scheduling, networking, persistence, host composition, process transport or Vega policy.

Keep subject namespaces aligned with ownership, for example:

- `TerraRuntime.Gameplay.Items`;
- `TerraRuntime.Gameplay.Npcs`;
- `TerraRuntime.Gameplay.Players`;
- `TerraRuntime.Gameplay.Buffs`.

Source-backed catalogs belong here when they describe verified gameplay/content semantics rather than runtime execution mechanics.

Do not create compatibility facades in `TerraRuntime.Core` for moved types. This project is pre-1.0; migrate namespaces/callers directly when ownership improves.

## Dependencies

Keep Gameplay as low in the dependency graph as the behavior allows. Prefer `TerraRuntime.Contracts` and BCL dependencies.

Add a dependency on another TerraRuntime project only when the gameplay implementation genuinely needs that project's semantics. Never introduce a Core <-> Gameplay cycle to make a move compile.

World persistence, protocol framing, sockets, terminal UI, Vega/plugin policy and process transport do not belong here.

## Naming

Use namespaces to carry repeated context. Internal helpers should use the shortest unambiguous domain name and should not repeat `Runtime`, `Gameplay`, `World` and subsystem names merely because they are available.

Keep `Vanilla` and version suffixes on source-pinned public catalogs/behaviors where they communicate a real vanilla/version boundary.

Delete transparent `*Facts`, `*Helper`, `*Provider`, `*Manager`, `*Service`, `*Factory` and compatibility wrappers instead of moving them.

Do not introduce `IThing`/`Thing` pairs unless there are already multiple real implementations or a concrete boundary that needs substitution.

Large source-backed catalogs may remain cohesive even when long. Decompose mixed-responsibility gameplay classes by reason-to-change, not by line count alone.
