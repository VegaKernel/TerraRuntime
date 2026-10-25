# Moon-event death authority in 1.4.5.8

The authoritative NPC combat pipeline rejects client packet `28` hits against every Goblin, Frost Legion, Pirate and Martian invasion participant and every Pumpkin/Frost Moon point-bearing NPC. The legacy compatibility fallback cannot turn a reported hit into event progress or loot. Existing server-owned projectile, melee and environment damage remain the owners of real kills.

For Scarecrows `305..314` and Zombie Elves `338..340`, lethal server damage uses the existing detached death preparation. It validates the NPC generation/revision, world clock, shared RNG and item allocations before HP adoption. The retained wave counters, loot, death prelude and RNG are adopted before packet or item observers. Repeating the command for an old generation cannot grant another kill. A callback can replace the NPC or advance newer event progress without the accepted operation restoring older state.

The same clock adoption now preserves Moon-event wave number, wave kills, total invasion points and progress revision for existing prepared projectile/debuff and linked Captain/Ghost deaths, alongside the existing Slime Rain counter.

The newly admitted families require a captured predicate that excludes the unowned generic Dungeon Spirit death producer. A possible or unknown post-Plantera Dungeon context refuses lethal adoption before HP, loot or RNG changes. Vanilla rolls this producer before testing the NPC's wall, so an absent wall is insufficient evidence to skip the roll.

## Source evidence and limits

TerrariaServer `1.4.5.8` calls `NPC.CheckProgressFrostMoon` and `CheckProgressPumpkinMoon` from `NPC.checkDead`, after `NPCLoot` and NPC deactivation. The point tables and difficulty scalars remain owned by the existing world clock. These early families contribute one base point; Expert uses `2`, Master uses `2.5`; crossing a wave threshold discards surplus points. Their dedicated `HitEffect` branches contain only fixed-argument Dust/Gore calls, without additional retained death producers.

`MoonEventPreparedDeath1458Tests` exercises real prepared projectile kills, server melee and environment kills, threshold crossing in all three difficulties, forged repeated client hits, stale owners and callback replacement. The tests also require event counters to be committed before a retained money-drop observer.

This change does not establish complete event loot tables, special-event boss death producers, all participant AI, wave announcements, official-client replay or whole-server vanilla parity. Other Moon families retain their existing death lane until their complete producers and client-local rewards have an unpublished owner. The atomic adoption contract commits related state before observers; it does not claim that the runtime reproduces every internal vanilla call boundary.
