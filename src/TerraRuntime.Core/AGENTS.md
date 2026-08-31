# TerraRuntime.Core layout and naming rules

These rules supplement the repository-root `AGENTS.md` for work under `src/TerraRuntime.Core/`.

## Ownership layout

`TerraRuntime.Core` is the authoritative runtime/core project, not a catch-all directory. Keep loop, scheduling, ownership and cross-subsystem composition infrastructure at the project root. Put subsystem-owned implementation in subject folders as the codebase grows, following the roadmap boundaries such as `Items/`, `Npcs/`, `Projectiles/` and `Worlds/`.

A physical folder move does not require a public namespace break. Preserve an established public namespace when moving existing public types unless a namespace migration is an explicit, reviewed compatibility change. New internal implementation should converge folder and namespace ownership rather than adding more flat root types.

Do not move `WorldItem` entity storage into `Items/` merely because both names contain "Item". Inventory/item definitions and world-item entity ownership are separate runtime concerns.

## Naming

Use `Vanilla` when the name communicates a real boundary that would otherwise be ambiguous: vanilla content/ID identity, a source- or version-pinned Terraria contract, or a deliberate vanilla-versus-extension implementation choice.

Do not add `Vanilla` merely to restate that TerraRuntime implements Terraria behavior. Inside an already-scoped Terraria subsystem, prefer the domain concept itself for local implementation helpers when no competing non-vanilla implementation exists.

Keep source-backed catalogs explicit about their vanilla/version contract. `VanillaItemDefinitionCatalog`, vanilla ID catalogs and equivalent verified data surfaces are intentionally descriptive and should not be shortened just to reduce characters.

Do not add proxy-only `*Facts`, `*Helper`, `*Provider`, `*Manager` or compatibility facades when an existing catalog/service already owns the same fact or operation. A compatibility facade requires an actual compatibility commitment and a removal/migration reason, not hypothetical future callers.

## Layering

Vega, plugin and module concepts belong above TerraRuntime. Do not make the runtime core depend on Vega-specific extension policy or names. TerraRuntime exposes stable runtime/host contracts; Vega and modules compose or extend them from above.
