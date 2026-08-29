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


def named_signatures(source: str, names: set[str]) -> list[str]:
    name_pattern = "|".join(re.escape(name) for name in sorted(names, key=len, reverse=True))
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)(?: static)? [^\r\n{{;]*\b(?:{name_pattern})\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def emit(label: str, source: str, names: set[str]) -> set[str]:
    found = named_signatures(source, names)
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
        description="Inspect pinned TerrariaServer 1.4.5.8 fresh-world initialization."
    )
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--main", required=True)
    args = parser.parse_args()

    world_gen = Path(args.world_gen).read_text(encoding="utf-8")
    main_source = Path(args.main).read_text(encoding="utf-8")

    world_gen_names = {
        "GenerateWorld",
        "clearWorld",
        "ClearWorld",
        "SetWorldSize",
        "setWorldSize",
        "RandomizeTreeStyle",
        "RandomizeCaveBackgrounds",
    }
    main_names = {
        "SetWorldSize",
        "setWorldSize",
        "Initialize",
        "ResetGameCounter",
    }

    emitted_world_gen = emit("WorldGen", world_gen, world_gen_names)
    emitted_main = emit("Main", main_source, main_names)

    if "GenerateWorld" not in emitted_world_gen:
        raise SystemExit("Pinned Terraria.WorldGen did not expose GenerateWorld.")
    if not ({"clearWorld", "ClearWorld"} & emitted_world_gen):
        raise SystemExit("Pinned Terraria.WorldGen did not expose clearWorld/ClearWorld.")

    print(f"worldgen_methods_emitted={sorted(emitted_world_gen)}")
    print(f"main_methods_emitted={sorted(emitted_main)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
