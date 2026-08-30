#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path

METHOD_PATTERN = re.compile(
    r"^[ \t]*(?:public|private|internal|protected)(?:\s+(?:static|override|virtual|sealed|new|readonly|async))*\s+[^\r\n{;=]*\b([A-Za-z_]\w*)\([^\r\n)]*\)",
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


def invocation_names(method: str) -> list[str]:
    names = re.findall(r"\b(?:WorldGen\.)?([A-Za-z_]\w*)\s*\(", method)
    ignored = {"if", "for", "while", "switch", "catch", "lock", "nameof", "typeof", "checked", "unchecked"}
    return sorted({name for name in names if name not in ignored})


def literals(method: str) -> list[str]:
    values = re.findall(r"(?<![A-Za-z_])[-+]?\d+(?:\.\d+)?(?:[fFdDmM])?(?![A-Za-z_])", method)
    return sorted(set(values), key=lambda value: (len(value), value))


def main() -> int:
    parser = argparse.ArgumentParser(description="Inspect pinned TerrariaServer 1.4.5.8 JunglePass.")
    parser.add_argument("--jungle-pass", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    source = Path(args.jungle_pass).read_text(encoding="utf-8")
    signatures = [match.group(0).strip() for match in METHOD_PATTERN.finditer(source)]
    if not signatures:
        raise SystemExit("Pinned JunglePass exposes no methods.")

    source_sha = hashlib.sha256(source.encode("utf-8")).hexdigest()
    lines = [
        "source=TerrariaServer 1.4.5.8",
        "decompiler=ilspycmd 11.0.0.9375",
        f"JunglePass_source_sha256={source_sha}",
        f"JunglePass_method_count={len(signatures)}",
    ]
    print(lines[2])
    print(lines[3])

    apply_count = 0
    for index, signature in enumerate(signatures):
        name = method_name(signature)
        body = extract_method(source, signature)
        digest = hashlib.sha256(body.encode("utf-8")).hexdigest()
        calls = invocation_names(body)
        constants = literals(body)
        lines.extend([
            f"JunglePass_method_{index:03d}_name={name}",
            f"JunglePass_method_{index:03d}_signature={compact(signature)}",
            f"JunglePass_method_{index:03d}_sha256={digest}",
            f"JunglePass_method_{index:03d}_calls={'|'.join(calls)}",
            f"JunglePass_method_{index:03d}_numeric_literals={'|'.join(constants)}",
        ])
        print(f"JunglePass_method_{index:03d}_name={name}")
        print(f"JunglePass_method_{index:03d}_signature={compact(signature)}")
        print(f"JunglePass_method_{index:03d}_sha256={digest}")
        print(f"JunglePass_method_{index:03d}_calls={'|'.join(calls)}")
        print(f"JunglePass_method_{index:03d}_numeric_literals={'|'.join(constants)}")
        print(f"BEGIN_JunglePass_{index:03d}_{name}")
        print(compact(body))
        print(f"END_JunglePass_{index:03d}_{name}")
        if name == "ApplyPass":
            apply_count += 1

    if apply_count != 1:
        raise SystemExit(f"Expected exactly one JunglePass.ApplyPass, found {apply_count}.")

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
