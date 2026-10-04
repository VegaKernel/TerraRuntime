# Town NPC projectile and melee combat ownership

2026-10-04 integrated checkpoint: the Mothron/Mother, death healing/prelude/banner/bestiary and ordinary town contact/regen/Stinky slices pass664,317/664,317 tests in404.906s, with zero failures/errors/skips. Clean Release rebuild, Windows NativeAOT and all five smokes plus documentation/domain/graph/diff gates pass with unchanged production/test source. Source incoming contact28 precedes final23. Linux NativeAOT, full N1-N5 parity, item overflow and existing nonclient/custom-strike publication remain open.

TerraRuntime now owns a bounded, source-backed TerrariaServer 1.4.5.8 AI_007 combat slice for the persistent Merchant, Nurse, Arms Dealer and Guide. These four residents are admitted because every projectile identity used by this slice already has an authoritative runtime lifecycle and behavior path.

The controller preserves the gameplay-critical source ordering: hostile NPC danger scan, nearest left/right target choice, visibility gate, `localAI[1]` cooldown, attack-state transition, attack timer/local timer advancement, source tick projectile cadence, recovery timer, and post-source-commit projectile allocation. Merchant and Nurse use AI state 10; Arms Dealer and Guide use state 12. The Arms Dealer's Hardmode burst occurs at local attack ticks 1, 10, 20 and 30. The Guide changes from Wooden Arrow to Fire Arrow in Hardmode.

Town damage and average attack chance are projected from the persisted 1.4.5.8 progression milestones plus both Combat Book flags. Classic/Expert/Master damage uses the pinned town-NPC difficulty curve. Post-load progression mutations are ORed with the persisted baseline, so a boss defeated during the current runtime immediately contributes to later town attacks.

This does **not** claim complete AI_007 combat. Other town attackers remain fail-closed until their projectile or special side effects are authoritative. Presentation-only sound/dust, Tipsy modifiers, Skyblock `lowTiles`, Dryad special combat and projectile `npcProj`/`noDropItem` flags that the current projectile state model does not consume are outside this slice. The N4 town-AI roadmap therefore remains open.


## AI_007 melee slice

The same runtime owner now admits the source `AttackType == 3` branch for Dye Trader (207), Tax Collector (441), and Stylist (353). It preserves the pinned danger ranges, attack times/chances, state-15 entry, three-phase `GetSwingStats`/`TweakSwingStats` rectangle geometry, source-shaped per-target server immunity, recovery cadence, progression/Combat Book/difficulty damage scaling, and the Tax Collector `GivenName == "Andrew"` double damage/knockback easter egg. Hits cross a generation-safe NPC-contact damage sink; lethal hits continue through the existing imported-loot/progression/despawn/death-replication pipeline rather than leaving `Life == 0` occupants in the NPC table.

TerrariaServer 1.4.5.8 still contains an `IsTownPet[type]` case inside state 15, but every current town-pet identity in the pinned `NPCID.Sets` has `AttackType = -1` and `AttackTime = -1`. TerraRuntime keeps that fact explicit and does not manufacture a natural pet melee entry.

## Unified ordinary common phase

The schedule/combat owner now plans one source danger scan, body/poses and real offers, attack initialization or active clocks, then physics and one state commit. Recovery follows the source two ordered draws and late cooldown decrement. Zero-spread projectiles still take the genuine two-component spread offer; normalized aim preserves source floating-point operand order and default lobbed fallback. Physical allocation converts the source center to the verified projectile top-left, including the 8x8 Nurse syringe. The original 448-case active-attack matrix and1600-case danger matrix are documented in [Town housing and schedules](town-npc-housing-shops.md). Seven supported profiles remain bounded; potentially lethal or reflected hostile contact, contextual social emotes, wet Nurse and other special attackers do not enter an invented fallback.

## Ordinary incoming contact and pre-AI status

The dry resident phase now follows AI007 vitals/body/attack, immunity aging, gravity, CheckLifeRegen, nonlethal GetHurtByOtherNPCs, collision and one final publication. Contact uses source integer body edges, supported weapon extensions, physical-slot priority, genuine DamageVar and knockback, AI reset and generation-owned immune255. Regeneration preserves full-health counters and heals only above180, including Guide/Cyborg rates. The source RNG preview is adopted after unpublished revision-checked state mutation and before callbacks. Potentially lethal/reflected or nearby frame-unowned contact is rejected before world-door effects; regeneration is included in that admission decision.

Shared Stinky visual offers now run before non-town inner AI rather than fencing it. The offer observes the source Good World Golem suppression and retains genuine RNG order; town offers share the complete resident preview. The [housing/schedule evidence](town-npc-housing-shops.md) records5996 new original cases,8223 focused regressions and six isolated negative controls. Contextual social emotes, wet Nurse, lethal town death, reflected damage, unrepresented buffs/frames and global mixed-slot ordering stay explicit boundaries.

Accepted incoming contacts emit source packet28 before final23;96 independent original socket captures and live peer queues pin bytes/order. The isolated no28 control fails65/97 assertions without runner errors. Shared nonplayer/custom strike wire publication is a separate N5 boundary.

## Nurse healing within the common phase

The dry ordinary dispatcher also owns Nurse state `13` and zero-damage syringe `584`; the hostile attack admission flag remains the source value computed before idle/Nurse offers. A same-tick combat initiation may therefore replace the healing state exactly as the source does. Active healing clocks, frame presentation and projectile allocation share the resident commit boundary rather than a second scheduler.

The authoritative projectile path checks target generation/revision, heals up to `20`, publishes removal `29` before combat text `81` and refreshes the retained join baseline without an immediate forced NPC update. Its dry collision path owns homing, bounce penetration and the genuine zero-damage CutTiles RNG offer before removal publication. Unknown/wet/overfull contexts remain bounded as detailed with the `1466` original cases and isolated controls in [Town housing and schedules](town-npc-housing-shops.md#ordinary-paired-socials-and-nurse-healing). This does not admit every town attacker or close N4.
