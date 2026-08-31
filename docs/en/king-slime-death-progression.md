# King Slime death progression

TerraRuntime keeps King Slime death finalization inside the authoritative NPC state pipeline instead of inferring boss death from a client packet or from a later slot scan.

## Committed source order

TerrariaServer 1.4.5.8 handles the King Slime death case in this observable order:

1. if Slime Rain is active, `StopSlimeRain` consumes `Next(3024, 6048)` and stores the negative cooldown `-roll * 100`;
2. on the first kill, `unlockedSlimeBlueSpawn` becomes true;
3. Nerdy Slime (`NPC 670`) is created at `(int)KingSlime.Center.X - 10, (int)KingSlime.Center.Y`;
4. after `NewNPC`, `NextFloatDirection()` supplies the launch X velocity (`* 3`), while Y becomes `-10`;
5. `downedSlimeKing` is marked last.

`RuntimeNpcAiStateExecutor` exposes a narrow `INpcAiStatePostCommitEffect` mutation boundary. The King Slime effects run only after the exact dead NPC generation successfully commits `TimeLeft = 0`, so a stale/reused NPC slot consumes none of the death RNG and cannot leak a Nerdy Slime spawn. The committed mutation sink also keeps the Nerdy velocity update generation-safe.

## World scoping and persistence

`RuntimeWorldProgressionRegistry` uses the exact `WorldTileStore` as a weak key. The progression journal tracks both the King Slime milestone and the newly produced blue-town-slime unlock while separately remembering whether that unlock was already present in the loaded world. A persisted unlock therefore suppresses repeat Nerdy spawning without being misreported as a new save mutation.

`WorldFileProgressionHeaderPatcher` persists both `downedSlimeKing` and `UnlockedSlimeBlueSpawn`. It walks the real Terraria 1.4.5.8 header layout, including variable-length Angler names, BannerSystem arrays, party NPC entries and TreeTops data, before locating the blue-slime flag. It changes only owned booleans and fails closed for unsupported milestone bits.

## Remaining boundary

This closes the source-backed King Slime **terminal transition, Slime Rain stop, first-kill Nerdy unlock/spawn and persistence** slice. `FullVanillaAiParity` remains false. The remaining Expert/Master integration work is live combat/death ingress for the already implemented per-player loot finalizer and authoritative ticking of instanced item-slot leases; presentation-only death effects and broader boss announcements are separate concerns.
