#!/usr/bin/env python3
import argparse
import hashlib
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


def find_apply_pass(source: str) -> tuple[str, str]:
    pattern = re.compile(
        r"^[ \t]*protected\s+override\s+void\s+ApplyPass\([^\r\n)]*\)",
        re.MULTILINE,
    )
    matches = list(pattern.finditer(source))
    if len(matches) != 1:
        raise SystemExit(
            "Pinned TerrainPass must expose exactly one protected override ApplyPass; "
            f"found {len(matches)}."
        )
    signature = matches[0].group(0).strip()
    return signature, extract_method(source, signature)


def find_constructor(source: str) -> str:
    pattern = re.compile(
        r"^[ \t]*public\s+TerrainPass\([^\r\n)]*\)\s*:\s*base\([^\r\n)]*\)",
        re.MULTILINE,
    )
    matches = list(pattern.finditer(source))
    if len(matches) != 1:
        raise SystemExit(
            "Pinned TerrainPass must expose exactly one public constructor with a base call; "
            f"found {len(matches)}."
        )
    return compact(matches[0].group(0))


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 TerrainPass.")
    parser.add_argument("--terrain-pass", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    source = Path(args.terrain_pass).read_text(encoding="utf-8")
    constructor = find_constructor(source)
    signature, method = find_apply_pass(source)
    source_sha = hashlib.sha256(source.encode("utf-8")).hexdigest()
    method_sha = hashlib.sha256(method.encode("utf-8")).hexdigest()

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "type=Terraria.GameContent.Biomes.TerrainPass",
        f"TerrainPass_source_sha256={source_sha}",
        f"TerrainPass_constructor={constructor}",
        f"TerrainPass_ApplyPass_signature={compact(signature)}",
        f"TerrainPass_ApplyPass_sha256={method_sha}",
    ]
    for line in lines:
        print(line)
    print("BEGIN_TerrainPass_ApplyPass")
    print(compact(method))
    print("END_TerrainPass_ApplyPass")

    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
