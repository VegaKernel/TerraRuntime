# Vanilla worldgen visual parity audit - 2026-08-31

These observed defects remain vanilla-specific parity debt and are not papered over with optimized geometry helpers.

- [ ] terrain silhouette: add final post-pass reference-world fixtures for canonical Small/Medium/Large;
- [ ] surface shaping: identify every non-zero tile `Shape` writer near ordinary surface and compare half-block/slope output to TerrariaServer 1.4.5.8;
- [ ] trees: replace the explicitly source-shaped trunk/branch scaffold with complete 1.4.5.8 framing, crowns and branches;
- [ ] dungeon: replace/verify the current source-shaped vertical shaft + periodic-room approximation against Terraria dungeon graph geometry;
- [ ] oceans: prove continuous floors and beach transitions after late `AlignOcean` correction on all canonical sizes.

Observed symptoms: segmented trees without crowns, jagged/half-block-heavy surface patches, unusual dungeon geometry and ocean regions that can look under-generated despite containing water.
