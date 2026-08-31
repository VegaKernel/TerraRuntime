from pathlib import Path

path = Path("src/TerraRuntime.World/Generation/Optimized/OptimizedLandmarkWorldGenerationProvider.cs")
text = path.read_text(encoding="utf-8")
old = """            foreach (int offset in offsets)
            {
                int centerX = Math.Clamp(span.Center + offset, span.Left + 4, span.Right - 4);
                int surfaceY = FindFirstActiveY(context.Workspace, centerX, Math.Max(8, layers.Surface - 28), Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer));
                if (surfaceY < 0 || !TryBuildPyramid(context, chests, span, centerX, surfaceY, state.PyramidsPlaced))
                    continue;
                state.PyramidsPlaced++;
                break;
            }
"""
new = """            var attemptedCenters = new HashSet<int>();
            foreach (int offset in offsets)
            {
                if (state.PyramidsPlaced >= state.PyramidTarget)
                    break;
                int centerX = Math.Clamp(span.Center + offset, span.Left + 4, span.Right - 4);
                if (!attemptedCenters.Add(centerX))
                    continue;
                int surfaceY = FindFirstActiveY(context.Workspace, centerX, Math.Max(8, layers.Surface - 28), Math.Min(context.Workspace.HeightTiles - 8, layers.RockLayer));
                if (surfaceY < 0 || !TryBuildPyramid(context, chests, span, centerX, surfaceY, state.PyramidsPlaced))
                    continue;
                // A canonical desert is one broad material span. Do not cap it to one pyramid: the explicit world-size
                // budget may require several well-separated structures inside that same span.
                state.PyramidsPlaced++;
            }
"""
if text.count(old) != 1:
    raise SystemExit(f"pyramid multi-placement capacity: expected 1 occurrence, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")
