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
        raise SystemExit(f"Could not locate signature: {signature}")
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


def method_signatures(source: str) -> list[str]:
    pattern = re.compile(
        r"^[ \t]*(?:public|private|internal|protected)(?:\s+(?:static|override|virtual|sealed|new|readonly|async))*\s+[^\r\n{;=]*\b([A-Za-z_]\w*)\([^\r\n)]*\)",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def method_name(signature: str) -> str:
    return signature.split("(", 1)[0].rsplit(" ", 1)[-1]


def constructor_signatures(source: str, type_name: str) -> list[str]:
    pattern = re.compile(
        rf"^[ \t]*(?:public|private|internal|protected)\s+{re.escape(type_name)}\([^\r\n)]*\)(?:\s*:\s*(?:base|this)\([^\r\n)]*\))?",
        re.MULTILINE,
    )
    return [match.group(0).strip() for match in pattern.finditer(source)]


def emit_body(prefix: str, source: str, signature: str, lines: list[str]) -> None:
    body = extract_method(source, signature)
    digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
    lines.append(f"{prefix}_signature={compact(signature)}")
    lines.append(f"{prefix}_sha256={digest}")
    print(f"{prefix}_signature={compact(signature)}")
    print(f"{prefix}_sha256={digest}")
    print(f"BEGIN_{prefix}")
    print(compact(body))
    print(f"END_{prefix}")


def emit_crc32(source: str, lines: list[str]) -> None:
    source_sha = hashlib.sha256(source.encode("utf-8")).hexdigest()
    lines.append(f"Crc32_source_sha256={source_sha}")
    print(f"Crc32_source_sha256={source_sha}")

    declarations: list[str] = []
    for raw in source.splitlines():
        value = compact(raw)
        lower = value.lower()
        if value and ("table" in lower or "polynomial" in lower or "crc" in lower) and value.endswith(";"):
            declarations.append(value)
    declarations = list(dict.fromkeys(declarations))
    lines.append(f"Crc32_declaration_count={len(declarations)}")
    print(f"Crc32_declaration_count={len(declarations)}")
    for index, value in enumerate(declarations):
        lines.append(f"Crc32_declaration_{index:02d}={value}")
        print(f"Crc32_declaration_{index:02d}={value}")

    calculate_methods = [signature for signature in method_signatures(source) if method_name(signature) == "Calculate"]
    if not calculate_methods:
        raise SystemExit("Pinned Crc32 exposes no Calculate methods.")
    lines.append(f"Crc32_Calculate_count={len(calculate_methods)}")
    print(f"Crc32_Calculate_count={len(calculate_methods)}")
    for index, signature in enumerate(calculate_methods):
        emit_body(f"Crc32_Calculate_{index:02d}", source, signature, lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Discover pinned TerrariaServer 1.4.5.8 WorldFileData seed semantics.")
    parser.add_argument("--world-file-data", required=True)
    parser.add_argument("--crc32", required=True)
    parser.add_argument("--output")
    args = parser.parse_args()

    source = Path(args.world_file_data).read_text(encoding="utf-8")
    crc32_source = Path(args.crc32).read_text(encoding="utf-8")
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "type=Terraria.IO.WorldFileData",
        f"WorldFileData_source_sha256={hashlib.sha256(source.encode('utf-8')).hexdigest()}",
    ]
    for line in lines:
        print(line)

    seed_declarations: list[str] = []
    for raw in source.splitlines():
        value = compact(raw)
        if not value or "Seed" not in value:
            continue
        if any(token in value for token in (" Seed", "SeedText", "seedText", "_seed", "seed =", "seed;")):
            seed_declarations.append(value)

    seed_declarations = list(dict.fromkeys(seed_declarations))
    lines.append(f"WorldFileData_seed_declaration_count={len(seed_declarations)}")
    print(f"WorldFileData_seed_declaration_count={len(seed_declarations)}")
    for index, value in enumerate(seed_declarations):
        lines.append(f"WorldFileData_seed_declaration_{index:02d}={value}")
        print(f"WorldFileData_seed_declaration_{index:02d}={value}")

    constructors = constructor_signatures(source, "WorldFileData")
    lines.append(f"WorldFileData_constructor_count={len(constructors)}")
    print(f"WorldFileData_constructor_count={len(constructors)}")
    for index, signature in enumerate(constructors):
        emit_body(f"WorldFileData_constructor_{index:02d}", source, signature, lines)

    seed_methods: list[str] = []
    for signature in method_signatures(source):
        try:
            body = extract_method(source, signature)
        except SystemExit:
            continue
        if "Seed" in signature or "SeedText" in body or re.search(r"\bseed\b", body, re.IGNORECASE):
            seed_methods.append(signature)

    lines.append(f"WorldFileData_seed_method_count={len(seed_methods)}")
    print(f"WorldFileData_seed_method_count={len(seed_methods)}")
    for index, signature in enumerate(seed_methods):
        emit_body(f"WorldFileData_seed_method_{index:02d}", source, signature, lines)

    if not seed_declarations and not seed_methods:
        raise SystemExit("Pinned WorldFileData exposes no discoverable seed surface.")

    emit_crc32(crc32_source, lines)

    if args.output:
        output = Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
