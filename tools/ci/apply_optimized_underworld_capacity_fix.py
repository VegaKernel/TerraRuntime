from pathlib import Path

path = Path("src/TerraRuntime.World/Generation/Optimized/OptimizedLandmarkWorldGenerationProvider.cs")
text = path.read_text(encoding="utf-8")
old = """        for (int ordinal = 0; ordinal < state.UnderworldHouseTarget; ordinal++)
        {
            double fraction = (ordinal + 1d) / (state.UnderworldHouseTarget + 1d);
            int centerX = left + (int)Math.Round((right - left) * fraction);
            if (TryBuildUnderworldHouse(context, chests, centerX, floorY, ordinal))
            {
                centers.Add(centerX);
                state.UnderworldHousesPlaced++;
            }
        }
        if (state.UnderworldHousesPlaced < state.UnderworldHouseTarget)
            throw new InvalidOperationException($\"Optimized landmark layer placed only {state.UnderworldHousesPlaced}/{state.UnderworldHouseTarget} required underworld houses.\");
        for (int i = 1; i < centers.Count; i++)
"""
new = """        int retryStep = Math.Clamp((right - left) / Math.Max(1, state.UnderworldHouseTarget * 12), 36, 140);
        int[] retryOffsets = [0, -retryStep, retryStep, -2 * retryStep, 2 * retryStep];
        for (int ordinal = 0; ordinal < state.UnderworldHouseTarget; ordinal++)
        {
            double fraction = (ordinal + 1d) / (state.UnderworldHouseTarget + 1d);
            int preferredCenter = left + (int)Math.Round((right - left) * fraction);
            foreach (int offset in retryOffsets)
            {
                int centerX = Math.Clamp(preferredCenter + offset, left + 9, right - 9);
                if (centers.Any(existing => Math.Abs(existing - centerX) < 34))
                    continue;
                if (!TryBuildUnderworldHouse(context, chests, centerX, floorY, ordinal))
                    continue;
                // Canonical Small places the guaranteed Hellforge near the middle settlement slot. Search a bounded
                // neighborhood instead of treating one protected X coordinate as proof that the whole Underworld
                // cannot satisfy its explicit settlement budget.
                centers.Add(centerX);
                state.UnderworldHousesPlaced++;
                break;
            }
        }
        if (state.UnderworldHousesPlaced < state.UnderworldHouseTarget)
            throw new InvalidOperationException($\"Optimized landmark layer placed only {state.UnderworldHousesPlaced}/{state.UnderworldHouseTarget} required underworld houses.\");
        centers.Sort();
        for (int i = 1; i < centers.Count; i++)
"""
if text.count(old) != 1:
    raise SystemExit(f"underworld settlement bounded search: expected 1 occurrence, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")
