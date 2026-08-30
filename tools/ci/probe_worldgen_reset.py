#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path


def extract_method(source: str, name: str) -> str:
    match = re.search(rf"(?m)^\s*(?:public|private|internal|protected)[^\n;]*\b{re.escape(name)}\s*\([^\n)]*\)\s*", source)
    if not match:
        raise SystemExit(f"Could not locate method {name} in pinned source.")
    brace = source.find("{", match.end())
    if brace < 0:
        raise SystemExit(f"Method {name} has no body.")
    depth = 0
    in_string = False
    in_char = False
    escaped = False
    for index in range(brace, len(source)):
        ch = source[index]
        if escaped:
            escaped = False
        elif ch == "\\" and (in_string or in_char):
            escaped = True
        elif ch == '"' and not in_char:
            in_string = not in_string
        elif ch == "'" and not in_string:
            in_char = not in_char
        elif not in_string and not in_char:
            if ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return source[match.start():index + 1]
    raise SystemExit(f"Method {name} body did not terminate.")


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def require_regex(source: str, pattern: str, label: str) -> None:
    if re.search(pattern, source, re.MULTILINE | re.DOTALL) is None:
        raise SystemExit(f"Pinned TerrariaServer 1.4.5.8 Reset contract missing {label}: /{pattern}/")


def require_runtime(runtime: str, pattern: str, label: str) -> None:
    if re.search(pattern, runtime, re.MULTILINE | re.DOTALL) is None:
        raise SystemExit(f"Runtime Reset bootstrap missing {label}: /{pattern}/")


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify TerraRuntime ordinary WorldGen.Reset bootstrap against pinned TerrariaServer 1.4.5.8 source.")
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--runtime-bootstrap", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    runtime = Path(args.runtime_bootstrap).read_text(encoding="utf-8")
    reset = extract_method(world_gen, "Reset")
    normalized = compact(reset)

    source_contract = [
        (r"beachBordersWidth\s*=\s*275\s*;", "beachBordersWidth=275"),
        (r"beachSandRandomCenter\s*=\s*GenVars\.beachBordersWidth\s*\+\s*5\s*\+\s*40\s*;", "beachSandRandomCenter=beachBordersWidth+5+40"),
        (r"beachSandRandomWidthRange\s*=\s*20\s*;", "beachSandRandomWidthRange=20"),
        (r"beachSandDungeonExtraWidth\s*=\s*40\s*;", "beachSandDungeonExtraWidth=40"),
        (r"beachSandJungleExtraWidth\s*=\s*20\s*;", "beachSandJungleExtraWidth=20"),
        (r"oceanWaterStartRandomMin\s*=\s*220\s*;", "oceanWaterStartRandomMin=220"),
        (r"oceanWaterStartRandomMax\s*=\s*GenVars\.oceanWaterStartRandomMin\s*\+\s*40\s*;", "oceanWaterStartRandomMax=min+40"),
        (r"oceanWaterForcedJungleLength\s*=\s*275\s*;", "oceanWaterForcedJungleLength=275"),
        (r"RandomizeTreeStyle\s*\(", "RandomizeTreeStyle call"),
        (r"RandomizeCaveBackgrounds\s*\(", "RandomizeCaveBackgrounds call"),
        (r"leftBeachEnd\s*=\s*[^;]*\.Next\s*\([^;]*beachSandRandomCenter[^;]*beachSandRandomWidthRange", "randomized left beach boundary"),
        (r"rightBeachStart\s*=\s*[^;]*-\s*[^;]*\.Next\s*\([^;]*beachSandRandomCenter[^;]*beachSandRandomWidthRange", "randomized right beach boundary"),
    ]
    for pattern, label in source_contract:
        require_regex(reset, pattern, label)

    tree_pos = normalized.find("RandomizeTreeStyle(")
    cave_pos = normalized.find("RandomizeCaveBackgrounds(")
    if tree_pos < 0 or cave_pos < 0 or tree_pos >= cave_pos:
        raise SystemExit("Pinned Reset no longer calls RandomizeTreeStyle before RandomizeCaveBackgrounds.")

    runtime_contract = [
        (r"BeachBordersWidth\s*=\s*275\s*;", "BeachBordersWidth=275"),
        (r"BeachSandRandomCenter\s*=\s*BeachBordersWidth\s*\+\s*5\s*\+\s*40\s*;", "BeachSandRandomCenter"),
        (r"BeachSandRandomWidthRange\s*=\s*20\s*;", "BeachSandRandomWidthRange=20"),
        (r"BeachSandDungeonExtraWidth\s*=\s*40\s*;", "BeachSandDungeonExtraWidth=40"),
        (r"BeachSandJungleExtraWidth\s*=\s*20\s*;", "BeachSandJungleExtraWidth=20"),
        (r"int\s+jungleHut\s*=\s*random\.Next\(5\)\s*;", "initial jungle-hut RNG"),
        (r"bool\s+crimsonLeft\s*=\s*random\.Next\(2\)\s*!=\s*0\s*;", "crimson-side RNG"),
        (r"int\s+worldId\s*=\s*random\.Next\(int\.MaxValue\)\s*;", "world-id RNG"),
        (r"RandomizeTreeStyle\(random,\s*width\)", "tree-style RNG helper"),
        (r"RandomizeCaveBackgrounds\(random,\s*width\)", "cave-background RNG helper"),
        (r"leftBeachEnd\s*=\s*random\.Next\(\s*BeachSandRandomCenter\s*-\s*BeachSandRandomWidthRange,\s*BeachSandRandomCenter\s*\+\s*BeachSandRandomWidthRange\s*\)", "runtime left beach RNG"),
        (r"rightBeachStart\s*=\s*width\s*-\s*random\.Next\(\s*BeachSandRandomCenter\s*-\s*BeachSandRandomWidthRange,\s*BeachSandRandomCenter\s*\+\s*BeachSandRandomWidthRange\s*\)", "runtime right beach RNG"),
    ]
    for pattern, label in runtime_contract:
        require_runtime(runtime, pattern, label)

    reset_sha = hashlib.sha256(reset.encode("utf-8")).hexdigest()
    runtime_sha = hashlib.sha256(runtime.encode("utf-8")).hexdigest()
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "scope=ordinary canonical-size WorldGen.Reset bootstrap",
        f"WorldGen_Reset_sha256={reset_sha}",
        f"Runtime_bootstrap_sha256={runtime_sha}",
        "BeachBordersWidth=275",
        "BeachSandRandomCenter=320",
        "BeachSandRandomWidthRange=20",
        "BeachSandDungeonExtraWidth=40",
        "BeachSandJungleExtraWidth=20",
        "OceanWaterStartRandomMin=220",
        "OceanWaterStartRandomMax=260",
        "OceanWaterForcedJungleLength=275",
        "ordering=RandomizeTreeStyle<RandomizeCaveBackgrounds",
        "status=verified",
    ]
    for line in lines:
        print(line)
    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
