# Vanilla world generation: Micro Biomes (Terraria 1.4.5.8)

`terraruntime:vanilla` now advances one more source-backed boundary and owns the registered `Micro Biomes` pass for ordinary canonical worlds. Terraria exposes this as one generation pass, so TerraRuntime keeps the same graph identity instead of inventing public sub-passes for each internal biome.

## Pinned source contract

The implementation is grounded in the pinned official TerrariaServer 1.4.5.8 binary. The source-contract workflow decompiles `WorldGen.AddPasses` with ilspycmd 11.0.0.9375 and extracts the embedded `Terraria.GameContent.WorldBuilding.Configuration.json` resource.

Pinned fingerprints:

- `WorldGen.AddPasses`: `72e757af1fb0a7b565d397bbb0f9fd1d32a2960838ba636c593e122645ab9672`
- `Micro Biomes` registration: `2edda4081fc087d403859a905ae4ee5d92b3b574349790edad4f93a9eed2649e`
- world-generation configuration: `22a72bf1eadc9a6b6e48ef056bbdef700a290fb2967774b47928ae3332512da3`

For an ordinary world the official delegate performs these internal stages in order:

1. Dead Man's Chest trapification;
2. Thin Ice;
3. Enchanted Sword Shrines;
4. Campsites;
5. Mining Explosives;
6. Living Mahogany trees;
7. the source-reserved seventh progress tenth;
8. long minecart tracks;
9. standard minecart tracks;
10. lava traps.

`CorruptionPitBiome` still exists in the 1.4.5.8 assembly, but the pinned ordinary `Micro Biomes` delegate does not call it. TerraRuntime therefore does not add a speculative corruption-pit stage merely because older configuration files contained one.

## Pinned configuration

The embedded 1.4.5.8 configuration defines:

| Key | Range/value | Scaling |
| --- | --- | --- |
| `DeadManChests` | 10..20 | world width |
| `ThinIcePatchCount` | 3..5 | world width |
| `SwordShrineAttempts` | 1..2 | world width |
| `SwordShrinePlacementChance` | 0.5 | scalar |
| `CampsiteCount` | 6..11 | world area |
| `ExplosiveTrapCount` | 14..29 | world area |
| `LivingTreeCount` | 6..11 | world width |
| `LongTrackCount` | 1..2 | world width |
| `LongTrackLength` | 400..1000 | world width |
| `StandardTrackCount` | 4..7 | world area |
| `StandardTrackLength` | 150..300 | world width |

The clean-room implementation applies the same width/area scaling intent to the three canonical Terraria dimensions and consumes the same shared vanilla RNG surface.

## Placement ownership

The pass now owns source-shaped clean-room placers for all ordinary stages above. It deliberately protects generated chests and any frame-important objects before destructive carving. Dead Man's Chest generation trapifies already registered generated chests instead of creating orphan container tiles. Minecart tracks are laid only across a route that can be reserved without crossing protected objects.

Known tile identities used by this slice include Thin Ice `162`, Explosives `141`, Campfire `215`, Living Mahogany `383`, Living Mahogany Leaves `384`, Minecart Track `314` and the Enchanted Sword background-object family on tile `187`.

## Parity boundary

This is not a claim of byte-identical micro-biome geometry. The exact outer pass identity, source order, configuration keys/ranges, retry-budget intent and shared-RNG ownership are pinned to 1.4.5.8. The individual procedural geometry is a clean-room source-shaped implementation and remains replaceable biome by biome as deeper source ports are added.

Special seeds and non-canonical dimensions still use the prior compatibility plan. The next ordinary source-backed boundary is `Settle Liquids Again`.

## Verification

The vanilla generated-world acceptance gate builds the runtime, executes the focused 106-pass contracts, emits a canonical `.wld`, reloads it through TerraRuntime and finally boots the pinned official TerrariaServer 1.4.5.8 with that generated world.
