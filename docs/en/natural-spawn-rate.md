# Natural spawn-rate coverage

## Bounded Pirate selection

The Pirate invasion now uses the ordered source `SpawnAnNPC` offers after the unconditional `GetZombieSettings` draw. A Flying Dutchman offer requires remaining size strictly below integer half of the starting size; its chance precedes the active ship cap and terrain check. Captain chance likewise precedes its active cap. Ordinary choices retain their original order and exact shared RNG cursor.

The ship clearance check uses the source inclusive rectangle, actuated-tile and platform exclusions, and bottom-world margin. Final terrain stamps include the complete rectangle reachable from the bounded floor search. Selected ship `491` and Captain `216` are refused without adopting RNG or creating a substitute while their linked cannon and Ghost lifecycles remain unsupported. Ordinary types `212–215` and `252` reuse the existing detached birth and final owner checks. Independent fixed-floor creation records also cover Captain bodies and corrected defaults; that component proof grants no live Captain admission.

The independent fixed-floor fixture contains 19 original selection/birth/sync records, including half-size, occupied-cap and terrain boundaries. It proves that component and literal `packet 23` bytes; it does not establish the complete spawn-priority table, full `Main` loop or linked ship encounter parity.

## Live invasion context and bounded Goblin selection

Invasion spawning reads the shared current invasion owner rather than a loaded boolean. The pinned `ShouldSpawnInvasionEnemies` predicate runs before the player spawn-rate pass: positive type, zero delay and positive remaining size are required. Its horizontal distance is strictly less than $3000\,\mathrm{px}$; its depth gate uses `NPC.sHeight`, $1200\,\mathrm{px}$, and the source spawn-point exception. Near the world midpoint it scans the complete retained physical NPC array. An inactive slot with the source `townNPC` flag still participates; the first nearby resident's zero `Next(3)` ends the scan. Unknown selected flags or body centers are refused before an invented result can affect spawning.

The bounded Goblin `SpawnAnNPC` dispatch preserves the unconditional `GetZombieSettings` `Next(7)` before the Hardmode Summoner offer and the ordered ordinary choices: Sorcerer `29`, Peon `26`, Archer `111`, Thief `27`, then Warrior `28`. The selected Summoner `471` identity is retained for the later actor-admission gate; unsupported behavior never falls back to another Goblin. This selector requires the represented `Skyblock.lowTiles == false` context. Separate special-AI and broader spawn-priority contexts remain explicit boundaries.

Current invasion type `1` suppresses daytime AI_003 despawn encouragement for Peon, Thief, Warrior and Archer. Changing the owner back to type `0` restores the ordinary branch. Sorcerer `29` retains its separate AI family. Independent original evidence contains 28 eligibility calls, 40 actual Goblin dispatches and eight source daytime-predicate calls. Three compact Facts compare decisions and exact RNG cursors; copied controls detect missing `Next(7)`, an active-only census, continuing after a zero roll, inclusive distance bounds and a missing daytime exemption. These component proofs do not claim a complete `Main` update or autonomous admission of every invasion actor.

Eligible Goblin attempts now execute through a detached physical allocation preview. Eligibility, rate, the existing bounded floor search, dispatch and source `NewNPC` use one cloned shared stream. Final spawn-context sampling and player recapture precede direct invasion, progression, clock, tile-section, NPC-table and RNG checks. Birth and RNG are adopted before retaining or publishing packet 23; a refused attempt exposes neither. An equal tile write invalidates the plan. The selected Summoner, Martian selector and Skyblock's extra branch remain fenced. This integrates the existing bounded floor-search profile; broader `NPC.Spawner` priority, geometry and complete `Main.SwapRandom` phase parity remain open.

Eight independent original `SpawnAnNPC` → `SyncNewlySpawnedNPCs` recordings pin source body, target, cursor and raw packet 23. They exposed the Archer's incorrect base height: source identity 111 uses 38 before scale, rather than 40. Natural `NewNPC` explicitly overwrites caster AI arguments with zero; the new birth path preserves that source order. Three new compact Facts also cover all 696 positive source `townNPC` flags (39 true), canonical loaded residents, retained inactive slots, imports, transforms, live spawning and late event/tile/RNG/NPC refusal. Copied controls detect these physical, RNG and admission regressions. This evidence is separate from the earlier three selector/context Facts and does not claim all invasion actors.

The server applies the source Underground Desert modifier when the player-center scene is below the world surface, has the non-ocean Desert tile threshold, and the center tile has a non-housing Sandstone, Hardened Sand, or Desert Fossil wall. This multiplier runs before Jungle and evil-zone rate transforms.

Jungle uses the source four rate/cap bands for zero, one, two, and at least three active town NPCs. Residents are counted by their live centers in the `3840×2400`-pixel `SceneMetrics.TownNPCRectSize`, so their persisted home coordinates do not alter the count.

Meteor uses the source active Meteorite tile `37` threshold of 75 before its rate/cap transform.

Lihzahrd Temple uses the source center-wall predicate (`Wall == 87`) after Meteor; Remix worlds add its second rate/cap transform.

On the Remix surface, Corruption and Crimson apply both source modifiers around the occupancy bands.

An active Wall of Flesh applies its source Underworld cap and rate transform before NPC occupancy bands.

Only players selected by the current source invasion predicate receive the invasion rate and active-player cap override. Players outside that region retain ordinary spawning.

Water and Peace Candles are scanned from their active source tiles and apply after NPC occupancy bands.

Before Skeletron is defeated, Dungeon applies the source final spawn-rate override.

Skyblock low-tile state halves the final source spawn rate.

Server-owned Blue, Green and Pink Fairies within the source 1920-pixel range apply the post-candle fairy rate and cap modifier.

Nearby NPC population uses the source `NPC.CheckActive` body-intersection rectangle of 4032 by 2520 pixels, not a radial distance approximation.

Population also uses the source `NPC.SetDefaults` `npcSlots` weights for each currently admitted ordinary type before cap and rate-band evaluation: Fire Imp 3, Bone Serpent head 6, Cave/Hell/Lava Bat .5, Demon/Voodoo Demon 2, and the default 1 for other admitted base types.

Admitted negative net variants `-11…-23`, `-38…-43`, and `-56…-65` additionally multiply their source slots by their `SetDefaultsFromNetId` scale; slime-like variants `-1…-10` retain unscaled slots.

While Slime Rain is active, each eligible player receives the separate source `NPC.SlimeRainSpawns` pass before the ordinary natural-spawn attempt. It preserves the 15-slot surface gate, the 45-to-495 interval, a 1920-by-1200 screen sample, solid and housing rejection, and the Pinky/Purple/Green selection order. The ordinary attempt still follows even when the event pass spawns a slime.

Deaths of those canonical Blue-Slime-type event variants now advance the transient source Slime Rain counter after loot. At 150 kills, or 75 once King Slime has been defeated, the server invokes its `SpawnOnPlayer` King Slime boundary and resets the counter to negative half of that threshold. An existing King Slime blocks counter progress.

An active Moon Lord Core within the source strict 4500-pixel center distance suppresses both event and ordinary natural-spawn attempts before they consume RNG.

Pumpkin Moon and Snow Moon use their source night and Remix spawn-rate transforms, then apply the final surface-or-Remix rate 20 override before later invasion handling.

The AI_003 Moon-event ground fighters now have source `SetDefaults` geometry, combat values, scale and knockback resistance plus the admitted server ground-fighter movement path. This covers Pumpkin types 305–314 and 326, and Snow types 342, 343 and 348–351; wave selection and special-AI event NPCs remain open.

Moon events now take the source `NPC.Spawner.SpawnAnNPC` selection table for waves one through five after the source Dungeon and Meteor priorities. The server preserves source random-draw order and active-type caps, uses admitted ordinary fighters when selected, simulates Snow type 341 with its source AI_025 wait/jump cycle and type 344 Everscream with AI_057 hover probes, life-scaled pursuit, attack state clocks and committed Pine Needle/Ornament projectiles; Pumpkin type 315 has its AI_026 six-pixel charge and committed 480-tick fireball timer, type 329 its AI_026 unicorn pursuit, close lunge, event-gated despawn and obstacle jumps, and type 330 its AI_022 flight, fade and event-gated despawn. Other special-AI selections remain fail-closed rather than becoming ordinary hostiles; later waves remain open.

Moon-event deaths now advance server-owned transient wave state after loot in every authoritative lethal-damage path. The counter starts at wave 1, uses the source 21-entry requirement table (wave 20 remains endless), discards overflow when a wave completes, and keeps the source Pumpkin/Snow point values with Classic, Expert and Master scalars of 1, 2 and 2.5. Each qualifying death also broadcasts the source packet-78 progress payload (`Int32` current points, `Int32` current-wave maximum, Frost/Pumpkin icon, and wave number) to every playing client. Event NPC definitions and wave selection remain open.

Packet 61 actions `-4` and `-5` now start Pumpkin and Snow Moon through the world clock only at night while no Moon event is active. The clock resets wave state at dawn and requests WorldInfo replication on both start and finish.
