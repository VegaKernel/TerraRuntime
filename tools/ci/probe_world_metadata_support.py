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


def signatures(source: str, names: set[str]) -> list[str]:
    name_pattern = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{{;]*\b(?:{name_pattern})\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def emit(label: str, path: str, names: set[str]) -> set[str]:
    source = Path(path).read_text(encoding="utf-8")
    found = signatures(source, names)
    emitted: set[str] = set()
    for signature in found:
        name = method_name(signature)
        print(f"{label}_signature={compact(signature)}")
        print(f"BEGIN_{label}_{name}")
        print(compact(extract_method(source, signature)))
        print(f"END_{label}_{name}")
        emitted.add(name)
    return emitted


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Inspect pinned TerrariaServer 1.4.5.8 world-header support serializers."
    )
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--banner-system", required=True)
    parser.add_argument("--dd2-event", required=True)
    parser.add_argument("--extra-spawns", required=True)
    parser.add_argument("--tree-tops", required=True)
    args = parser.parse_args()

    manifest = emit("WorldManifest", args.manifest, {"Serialize", "Deserialize"})
    banners = emit("BannerSystem", args.banner_system, {"Save", "Load", "Clear"})
    dd2 = emit("DD2Event", args.dd2_event, {"Save", "Load", "ResetProgressEntirely"})
    extra_spawns = emit("ExtraSpawnPointManager", args.extra_spawns, {"Write", "Read", "ResetExtraSpawns"})
    tree_tops = emit(
        "TreeTopsInfo",
        args.tree_tops,
        {"Save", "Load", "CopyExistingWorldInfoForWorldGeneration"},
    )

    required = {
        "WorldManifest": (manifest, {"Serialize"}),
        "BannerSystem": (banners, {"Save", "Clear"}),
        "DD2Event": (dd2, {"Save", "ResetProgressEntirely"}),
        "ExtraSpawnPointManager": (extra_spawns, {"Write", "ResetExtraSpawns"}),
        "TreeTopsInfo": (tree_tops, {"Save"}),
    }
    for label, (emitted, expected) in required.items():
        missing = sorted(expected - emitted)
        if missing:
            raise SystemExit(f"Pinned {label} is missing required methods: {missing}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
