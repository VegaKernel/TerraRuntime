# Natural spawn-rate coverage

The server applies the source Underground Desert modifier when the player-center scene is below the world surface, has the non-ocean Desert tile threshold, and the center tile has a non-housing Sandstone, Hardened Sand, or Desert Fossil wall. This multiplier runs before Jungle and evil-zone rate transforms.

Jungle uses the source four rate/cap bands for zero, one, two, and at least three active town NPCs. Residents are counted by their live centers in the `3840×2400`-pixel `SceneMetrics.TownNPCRectSize`, so their persisted home coordinates do not alter the count.

Meteor uses the source active Meteorite tile `37` threshold of 75 before its rate/cap transform.

Lihzahrd Temple uses the source center-wall predicate (`Wall == 87`) after Meteor; Remix worlds add its second rate/cap transform.

On the Remix surface, Corruption and Crimson apply both source modifiers around the occupancy bands.

An active Wall of Flesh applies its source Underworld cap and rate transform before NPC occupancy bands.

Known persisted invasions reset the rate and scale the cap from the authoritative active-player count.
