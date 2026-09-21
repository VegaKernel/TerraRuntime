# Natural spawn-rate coverage

The server applies the source Underground Desert modifier when the player-center scene is below the world surface, has the non-ocean Desert tile threshold, and the center tile has a non-housing Sandstone, Hardened Sand, or Desert Fossil wall. This multiplier runs before Jungle and evil-zone rate transforms.

Jungle uses the source four rate/cap bands for zero, one, two, and at least three active town NPCs. Residents are counted by their live centers in the `3840×2400`-pixel `SceneMetrics.TownNPCRectSize`, so their persisted home coordinates do not alter the count.

Meteor uses the source active Meteorite tile `37` threshold of 75 before its rate/cap transform.

Lihzahrd Temple uses the source center-wall predicate (`Wall == 87`) after Meteor; Remix worlds add its second rate/cap transform.

On the Remix surface, Corruption and Crimson apply both source modifiers around the occupancy bands.

An active Wall of Flesh applies its source Underworld cap and rate transform before NPC occupancy bands.

Known persisted invasions reset the rate and scale the cap from the authoritative active-player count.

Water and Peace Candles are scanned from their active source tiles and apply after NPC occupancy bands.

Before Skeletron is defeated, Dungeon applies the source final spawn-rate override.

Skyblock low-tile state halves the final source spawn rate.

Server-owned Blue, Green and Pink Fairies within the source 1920-pixel range apply the post-candle fairy rate and cap modifier.

Nearby NPC population uses the source `NPC.CheckActive` body-intersection rectangle of 4032 by 2520 pixels, not a radial distance approximation.

Population also uses the source `NPC.SetDefaults` `npcSlots` weights for each currently admitted ordinary type before cap and rate-band evaluation: Fire Imp 3, Bone Serpent head 6, Cave/Hell/Lava Bat .5, Demon/Voodoo Demon 2, and the default 1 for other admitted base types.

Admitted negative net variants `-11…-23`, `-38…-43`, and `-56…-65` additionally multiply their source slots by their `SetDefaultsFromNetId` scale; slime-like variants `-1…-10` retain unscaled slots.

While Slime Rain is active, each eligible player receives the separate source `NPC.SlimeRainSpawns` pass before the ordinary natural-spawn attempt. It preserves the 15-slot surface gate, the 45-to-495 interval, a 1920-by-1200 screen sample, solid and housing rejection, and the Pinky/Purple/Green selection order. The ordinary attempt still follows even when the event pass spawns a slime.

An active Moon Lord Core within the source strict 4500-pixel center distance suppresses both event and ordinary natural-spawn attempts before they consume RNG.

Pumpkin Moon and Snow Moon use their source night and Remix spawn-rate transforms, then apply the final surface-or-Remix rate 20 override before later invasion handling.
