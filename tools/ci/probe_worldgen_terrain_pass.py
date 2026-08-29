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


def find_unique_method(source: str, name: str, required_prefix: str | None = None) -> tuple[str, str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)(?:\s+static)?\s+[^\r\n{{;]*\b{re.escape(name)}\([^\r\n)]*\)",
        re.MULTILINE,
    )
    signatures = [match.group(0).strip() for match in pattern.finditer(source)]
    if required_prefix is not None:
        signatures = [signature for signature in signatures if signature.startswith(required_prefix)]
    if len(signatures) != 1:
        raise SystemExit(
            f"Pinned TerrainPass must expose exactly one {name} method matching the probe; "
            f"found {len(signatures)}."
        )
    signature = signatures[0]
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


def extract_enum(source: str, enum_name: str) -> str:
    pattern = re.compile(rf"\benum\s+{re.escape(enum_name)}\b[^{{]*{{")
    match = pattern.search(source)
    if match is None:
        raise SystemExit(f"Pinned TerrainPass source does not contain enum {enum_name}.")
    brace = source.find("{", match.start())
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[match.start():index + 1]
    raise SystemExit(f"Pinned TerrainPass enum {enum_name} did not terminate.")


def emit_method(lines: list[str], label: str, signature: str, method: str) -> None:
    method_sha = hashlib.sha256(method.encode("utf-8")).hexdigest()
    lines.append(f"TerrainPass_{label}_signature={compact(signature)}")
    lines.append(f"TerrainPass_{label}_sha256={method_sha}")
    print(f"TerrainPass_{label}_signature={compact(signature)}")
    print(f"TerrainPass_{label}_sha256={method_sha}")
    print(f"BEGIN_TerrainPass_{label}")
    print(compact(method))
    print(f"END_TerrainPass_{label}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 TerrainPass.")
    parser.add_argument("--terrain-pass", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    source = Path(args.terrain_pass).read_text(encoding="utf-8")
    constructor = find_constructor(source)
    source_sha = hashlib.sha256(source.encode("utf-8")).hexdigest()
    enum_source = extract_enum(source, "TerrainFeatureType")
    enum_sha = hashlib.sha256(enum_source.encode("utf-8")).hexdigest()

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "type=Terraria.GameContent.Biomes.TerrainPass",
        f"TerrainPass_source_sha256={source_sha}",
        f"TerrainPass_constructor={constructor}",
        f"TerrainPass_TerrainFeatureType={compact(enum_source)}",
        f"TerrainPass_TerrainFeatureType_sha256={enum_sha}",
    ]
    for line in lines:
        print(line)

    helper_names = [
        "ApplyPass",
        "GenerateWorldSurfaceOffset",
        "FillColumn",
        "RetargetSurfaceHistory",
    ]
    for name in helper_names:
        prefix = "protected override" if name == "ApplyPass" else None
        signature, method = find_unique_method(source, name, prefix)
        emit_method(lines, name, signature, method)

    # SurfaceHistory is nested in the pinned TerrainPass source. Its tiny ring-buffer behavior affects the beach
    # retarget path, so fingerprint and emit the constructor/Record helpers rather than treating it as an incidental
    # implementation detail.
    for name in ["Record"]:
        signature, method = find_unique_method(source, name)
        emit_method(lines, f"SurfaceHistory_{name}", signature, method)

    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
