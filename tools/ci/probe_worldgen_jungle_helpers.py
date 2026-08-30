#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path

METHOD_PATTERN = re.compile(
    r"^[ \t]*(?:public|private|internal|protected)(?:\s+(?:static|override|virtual|sealed|new|readonly|async|unsafe))*\s+[^\r\n{;=]*\b([A-Za-z_]\w*)\([^\r\n)]*\)",
    re.MULTILINE,
)


def compact(value: str) -> str:
    return re.sub(r"\s+", " ", value).strip()


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def extract_method(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise SystemExit(f"Could not locate method signature: {signature}")
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
            continue
        if ch == "\\" and (in_string or in_char):
            escaped = True
            continue
        if ch == '"' and not in_char:
            in_string = not in_string
            continue
        if ch == "'" and not in_string:
            in_char = not in_char
            continue
        if in_string or in_char:
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise SystemExit(f"Method body did not terminate: {signature}")


def inspect(source: str, names: set[str], output: Path) -> int:
    signatures = [match.group(0).strip() for match in METHOD_PATTERN.finditer(source)]
    selected = [signature for signature in signatures if method_name(signature) in names]
    if not selected:
        raise SystemExit(f"Pinned WorldGen exposes none of the requested helpers: {sorted(names)}")

    found = {method_name(signature) for signature in selected}
    missing = sorted(names - found)
    if missing:
        raise SystemExit(f"Pinned WorldGen is missing requested Jungle helpers: {missing}")

    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
    ]
    for index, signature in enumerate(selected):
        name = method_name(signature)
        body = extract_method(source, signature)
        digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
        lines.extend([
            f"WorldGen_JungleHelper_{index:03d}_name={name}",
            f"WorldGen_JungleHelper_{index:03d}_signature={compact(signature)}",
            f"WorldGen_JungleHelper_{index:03d}_sha256={digest}",
        ])
        print(f"WorldGen_JungleHelper_{index:03d}_name={name}")
        print(f"WorldGen_JungleHelper_{index:03d}_signature={compact(signature)}")
        print(f"WorldGen_JungleHelper_{index:03d}_sha256={digest}")
        print(f"BEGIN_WorldGen_JungleHelper_{index:03d}_{name}")
        print(compact(body))
        print(f"END_WorldGen_JungleHelper_{index:03d}_{name}")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect WorldGen helpers used by pinned JunglePass.")
    parser.add_argument("--world-gen", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    source = Path(args.world_gen).read_text(encoding="utf-8")
    return inspect(source, {"TileRunner", "MudWallRunner"}, Path(args.output))


if __name__ == "__main__":
    raise SystemExit(main())
