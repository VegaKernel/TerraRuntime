#!/usr/bin/env python3
import argparse
import re
from pathlib import Path


def compact(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate exact signature: {signature}")
    brace = source.find("{", start + len(signature))
    if brace < 0:
        raise SystemExit(f"Method declaration has no body: {signature}")

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
                    return source[start:index + 1]

    raise SystemExit(f"Method body did not terminate: {signature}")


def signatures(source: str) -> list[str]:
    pattern = re.compile(
        r"^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{;]*(?:Save|save)[A-Za-z0-9_]*\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def method_name(signature: str) -> str:
    prefix = signature.split("(", 1)[0]
    return prefix.rsplit(" ", 1)[-1]


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 world save methods."
    )
    parser.add_argument("--world-file", required=True)
    args = parser.parse_args()

    source = Path(args.world_file).read_text(encoding="utf-8")
    found = signatures(source)
    print(f"save_method_count={len(found)}")
    for signature in found:
        print(f"save_signature={compact(signature)}")

    preferred = {
        "SaveWorld_Version2",
        "SaveWorldHeader",
        "SaveWorldTiles",
        "SaveChests",
        "SaveSigns",
        "SaveNPCs",
        "SaveTileEntities",
        "SaveWeightedPressurePlates",
        "SaveTownManager",
        "SaveBestiary",
        "SaveCreativePowers",
    }
    emitted = 0
    for signature in found:
        name = method_name(signature)
        if name not in preferred:
            continue
        body = compact(extract_method(source, signature))
        print(f"BEGIN_{name}")
        print(body)
        print(f"END_{name}")
        emitted += 1

    print(f"preferred_methods_emitted={emitted}")
    if not any(method_name(signature) in {"SaveWorld_Version2", "SaveWorldHeader"} for signature in found):
        raise SystemExit("Pinned Terraria.IO.WorldFile did not expose the expected version-2/header save entry point.")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
