# Town NPC projectile and melee combat ownership

TerraRuntime now owns a bounded, source-backed TerrariaServer 1.4.5.8 AI_007 combat slice for the persistent Merchant, Nurse, Arms Dealer and Guide. These four residents are admitted because every projectile identity used by this slice already has an authoritative runtime lifecycle and behavior path.

The controller preserves the gameplay-critical source ordering: hostile NPC danger scan, nearest left/right target choice, visibility gate, `localAI[1]` cooldown, attack-state transition, attack timer/local timer advancement, source tick projectile cadence, recovery timer, and post-source-commit projectile allocation. Merchant and Nurse use AI state 10; Arms Dealer and Guide use state 12. The Arms Dealer's Hardmode burst occurs at local attack ticks 1, 10, 20 and 30. The Guide changes from Wooden Arrow to Fire Arrow in Hardmode.

Town damage and average attack chance are projected from the persisted 1.4.5.8 progression milestones plus both Combat Book flags. Classic/Expert/Master damage uses the pinned town-NPC difficulty curve. Post-load progression mutations are ORed with the persisted baseline, so a boss defeated during the current runtime immediately contributes to later town attacks.

This does **not** claim complete AI_007 combat. Other town attackers remain fail-closed until their projectile or special side effects are authoritative. Presentation-only sound/dust, Tipsy modifiers, Skyblock `lowTiles`, stinky-player targeting, Dryad special combat, Nurse healing state 13 and projectile `npcProj`/`noDropItem` flags that the current projectile state model does not consume are outside this slice. The N4 town-AI roadmap therefore remains open.


## AI_007 melee slice

The same runtime owner now admits the source `AttackType == 3` branch for Dye Trader (207), Tax Collector (441), and Stylist (353). It preserves the pinned danger ranges, attack times/chances, state-15 entry, three-phase `GetSwingStats`/`TweakSwingStats` rectangle geometry, source-shaped per-target server immunity, recovery cadence, progression/Combat Book/difficulty damage scaling, and the Tax Collector `GivenName == "Andrew"` double damage/knockback easter egg. Hits cross a generation-safe NPC-contact damage sink; lethal hits continue through the existing imported-loot/progression/despawn/death-replication pipeline rather than leaving `Life == 0` occupants in the NPC table.

TerrariaServer 1.4.5.8 still contains an `IsTownPet[type]` case inside state 15, but every current town-pet identity in the pinned `NPCID.Sets` has `AttackType = -1` and `AttackTime = -1`. TerraRuntime keeps that fact explicit and does not manufacture a natural pet melee entry.
