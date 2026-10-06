# Owned player item use 1.4.5.8

[Русский](../ru/player-item-use-1458.md) · [Combat calculation](combat-damage.md)

The common equipment calculator admits all eight ordinary metal armor sets and their Ancient Iron/Gold helmet alternatives: 26 pieces and ten complete-set combinations. Complete defense is 6/7/9/11/13/15/16/20 for Copper/Tin/Iron/Lead/Silver/Tungsten/Gold/Platinum. Mixed pieces retain individual defense. PvE, PvP and server players use the same existing combat snapshot. Wrong slots, armor prefixes, unknown equipment and unowned complete endgame sets retain their gates; vanity and inactive loadouts do not grant effects.

Strict projectile item use captures the exact connection/player, inventory serial, equipment and buffs before calculation. Conservation is prepared on a clone of the existing shared `UnifiedRandom`: Celebration Mk2 (`3930`, `Next(2)`) precedes Magic Quiver; Minishark follows it. Endless ammo still performs applicable draws. Only the first accepted Celebration volley child owns conservation and consumption. Earlier unsupported compatible ammo is never skipped.

```mermaid
flowchart LR
    A[Capture owned player and inventory] --> B[Detached calculation and RNG]
    B --> C[Validate owners, cadence and source allocation]
    C --> D[Adopt projectile, item use, trust, RNG and cadence]
    D --> E[Publish inventory or mana]
    E --> F[Publish current projectile generation once]
```

Preparation does not advance a generation or invoke sinks. Rejected stale owners, allocation, cadence or RNG preserve the item-use state. The successful adoption tail has no external callbacks; inventory/mana, combat trust, projectile, RNG and cadence are complete before publication. Subsequent deliberate callback mutations are not overwritten. Projectile publication is single-use and refuses a generation retired or replaced by a callback. Public ordinary spawning retains the existing source full-pool replacement/overflow behavior. The transaction also protects already admitted standalone thrown and channeled magic sources.

Source births own empty buff slots. Missing imported buff ownership and active Ammo Box (`93`) or Ammo Reservation (`112`) remain outside strict ranged combat trust. Conservation armor, cycling, animation-dependent families, new ammo transforms, potion/bag use, complete item-use parity and complete player-update RNG phase ordering remain separate work. No new item-use packet is introduced.

Independent original armor defaults/grants/set effects and `Player.PickAmmo` captures accompany compact regression tests and failing controls. Command tests cover stale inventory/equipment/player/RNG, connection reuse, capacity, cadence, first-callback state, callback mutation retention, endless ammo, Celebration children and adjacent mana/standalone use.

Local checkpoint: full 1562839/1562840, focused 98/98, fast tier, zero failures/errors and one existing liquid skip; Windows NativeAOT/five smokes and all local gates pass. Linux NativeAOT remains locally unverified.
