# Current player debuffs — Terraria 1.4.5.8

The ordinary owned player phase now continues with Darkness `22`, Bleeding `30`, Slow `32`, Weak `33`, Silenced `35` and Broken Armor `36`. This extends the [sixteen Ankh-line accessories](ankh-immunity-equipment-1458.md) through the existing equipment, buff, health, item-use and movement owners. It adds no parallel timers, inventory balance or RNG stream.

| Effect | Represented source behavior |
|---|---|
| Darkness | Current blind effect; dedicated gameplay has no client lighting or drawing owner. |
| Bleeding | Reset natural regeneration time after its ordinary/Aegis increments; positive regeneration-potion bonuses remain. |
| Slow | Scale movement and the separate accelerated run-speed cap; simultaneous left/right uses the current speed cap. |
| Weak | Indexed melee damage/speed, defense and movement reductions before equipment. Preserve negative intermediate defense until the later clamp. |
| Silenced | Refuse fresh represented mana-positive magic use/payment; preserve already-paid birth/midpoint and running clocks, health and motion. |
| Broken Armor | Integer-halving of nonnegative defense after equipment/permanent boosters, retaining this frame's flag after an immunity cure. |

The existing buff slots retain their provenance. An immutable ordered current-frame projection is captured before owned contact countdown or late equipment removal. This preserves duration-one effects and `Food → Weak` versus `Weak → Food` Single rounding after slot compaction. The following update clears the projection. A captured array cannot mutate the owner; transfer, explicit replacement and death retire prior-frame effects. Packet buff reports do not establish paid food/contact timer custody.

Weak can produce an attack-speed reciprocal above one. The shared melee clock accepts a bounded lengthened animation and keeps source multiply-then-truncate arithmetic. Equipment calculations admit negative defense only through an explicit before-equipment capability; ordinary final combat snapshots still require nonnegative defense. Source `Player.Update` clamps after gear/permanent boosters and applies Broken Armor later.

Fresh magic and already-paid magic use the existing payment owner. Silence does not become a universal item-use prohibition: represented swords, bows and non-magic consumables continue. Source Confused `31` still needs retained previous-frame input semantics, and active Stoned `156` still needs additional source hurt/fall/control effects. Both remain fenced. The six new continuations qualify ordinary in-world actors; their separate out-of-range retained-field `UpdateBuffs` path refuses. Ankh Shield `1613` fireWalk, Hand Warmer `1921`, Shimmer Cloak `5355`, unrepresented equipment, environments and full player parity remain outside this block.

Independent evidence executes the unmodified initialized original `Player.Update(0)`: three seeds, both local/remote roles, three weapons, bare/Ankh/ingredient equipment, poison/food combinations in both indexed orders and duration-one/ordinary timers. The immutable V8 corpus has 3,240 contexts and 12,960 updates per repeat; both repeats are byte-identical. The accepted runtime projection compares 324 remote contexts and 24 local final-contact contexts through four sequential updates, plus 42 separate simultaneous-control contexts: 1,434 complete projected observations with named health, combat/effects, buff slots, item/mana clocks, pose/velocity and all physical RNG slots/cursor. Local timer tests join after a real empty server tick so the existing contact clock advances from its initial epoch.

Initial source/runtime mana heat is observed constructor `0f`; the already-owned early heat VFX accounts for its RNG. No cursor offset or post-execution normalization is used. Earlier V7 explicitly authored `1f` and therefore mismatched the fresh runtime preimage; its first assertion failures are rejected setup evidence. Raw V6/V7 records remain immutable in cache. Managed execution of the Linux original on Windows CoreCLR does not establish Linux Native, GUI, whole Main or transport parity.

The local component's focused, neighbor and same-test-DLL omission evidence is recorded in [the component manifest](../verification/scalar-debuff-player-20261029.json). The Roslyn readback remains an aid to source inspection: `UpdateLifeRegen` and the cooldown methods resolve at their spans, while this pilot's mixed source/metadata `UpdateBuffs` has an unresolved mount call. No complete dependency closure or automatic translation is claimed. Aggregate fast/full/Native and publication belong to the later integration checkpoint.
