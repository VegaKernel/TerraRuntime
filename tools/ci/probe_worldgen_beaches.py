#!/usr/bin/env python3
"""Verify the clean-room Beaches routes and TuneOceanDepth table against TerrariaServer 1.4.5.8."""

import argparse
import hashlib
import re
from pathlib import Path


STANDARD = [
    (3, .2), (6, .15), (9, .1), (15, .07), (50, .05), (75, .04), (100, .03), (125, .02),
    (150, .01), (175, .005), (200, .001), (230, .01), (235, .05), (240, .1), (245, .05), (255, .01),
]
FLORIDA = [
    (3, .001), (6, .002), (9, .004), (15, .007), (50, .01), (75, .014), (100, .019), (125, .027),
    (150, .038), (175, .052), (200, .08), (230, .12), (235, .16), (240, .27), (245, .43), (255, .6),
]


def require(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, re.DOTALL) is None:
        raise SystemExit(f"Beaches contract is missing {label}: /{pattern}/")


def extract_method(source: str, name: str) -> str:
    match = re.search(rf"(?m)^\s*(?:public|private|internal|protected)[^\n;]*\b{re.escape(name)}\s*\([^\n)]*\)\s*", source)
    if not match:
        raise SystemExit(f"Could not locate pinned method {name}.")
    brace = source.find("{", match.end())
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[match.start():index + 1]
    raise SystemExit(f"Pinned method {name} did not terminate.")


def runtime_bands(catalog: str, field: str) -> list[tuple[int, float]]:
    match = re.search(rf"{field}\s*=\s*\[(?P<body>.*?)\];", catalog, re.DOTALL)
    if not match:
        raise SystemExit(f"Runtime ocean catalog is missing {field}.")
    return [(int(limit), float(scale)) for limit, scale in re.findall(r"new\((\d+),\s*([0-9.]+)d\)", match.group("body"))]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--runtime-catalog", required=True)
    parser.add_argument("--runtime-pass", required=True)
    parser.add_argument("--canonical-provider", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    catalog = Path(args.runtime_catalog).read_text(encoding="utf-8")
    runtime_pass = Path(args.runtime_pass).read_text(encoding="utf-8")
    canonical = Path(args.canonical_provider).read_text(encoding="utf-8")

    start = world_gen.find("AddGenerationPass(GenPassNameID.BeachesAndOceanCleanup")
    if start < 0:
        raise SystemExit("Pinned BeachesAndOceanCleanup pass is missing.")
    end = world_gen.find("AddGenerationPass(", start + 20)
    beaches = world_gen[start:end]
    tune = extract_method(world_gen, "TuneOceanDepth")

    source_routes = [
        (r"genRand\.Next\(4\).*?genRand\.Next\(2\)", "one-sided Florida profile selection"),
        (r"genRand\.Next\(GenVars\.oceanWaterStartRandomMin, GenVars\.oceanWaterStartRandomMax\)", "random water start"),
        (r"GenVars\.oceanWaterForcedJungleLength", "forced jungle-side length"),
        (r"GenVars\.leftBeachEnd - num", "left Reset beach clamp"),
        (r"GenVars\.rightBeachStart \+ num", "right Reset beach clamp"),
        (r"num6 \* 0\.75 - 3\.0", "left water-to-floor boundary"),
        (r"num11 \* 0\.75 - 3\.0", "right water-to-floor boundary"),
        (r"liquid = byte\.MaxValue", "full water columns"),
        (r"liquid = 127", "half-filled shoreline row"),
        (r"type = 53", "sand floor"),
    ]
    for pattern, label in source_routes:
        require(beaches, pattern, label)

    if runtime_bands(catalog, "StandardDepthBands") != STANDARD:
        raise SystemExit("Runtime standard ocean depth bands differ from pinned TuneOceanDepth.")
    if runtime_bands(catalog, "FloridaDepthBands") != FLORIDA:
        raise SystemExit("Runtime Florida ocean depth bands differ from pinned TuneOceanDepth.")

    for marker in [
        "random.Next(4)", "random.Next(2)", "GetDepthIncrementScale", "WaterToFloorRatio",
        "HalfLiquidAmount", "BeachBoundaryPadding", "ForcedJungleOceanLength",
    ]:
        if marker not in runtime_pass:
            raise SystemExit(f"Runtime Beaches implementation is missing route: {marker}")
    if "AlignOcean" in canonical or "VanillaCanonicalOceanFinalCleanupPass1458" in canonical:
        raise SystemExit("Canonical provider still contains the late destructive ocean repair.")

    # The exact increment literals are asserted through the runtime table above; these source markers pin both
    # branches and their terminal no-increment behavior without depending on decompiler local-variable names.
    require(tune, r"if \(!floridaStyle\).*?count < 255", "standard depth branch")
    require(tune, r"else if \(count < 3\).*?count < 255", "Florida depth branch")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=ordinary Beaches bounds, shared-RNG routes, water body and TuneOceanDepth profiles",
        f"WorldGen_Beaches_sha256={hashlib.sha256(beaches.encode('utf-8')).hexdigest()}",
        f"WorldGen_TuneOceanDepth_sha256={hashlib.sha256(tune.encode('utf-8')).hexdigest()}",
        "standard_depth_bands=16",
        "florida_depth_bands=16",
        "late_final_cleanup_repair=absent",
        "status=verified",
    ]
    print("\n".join(lines))
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
