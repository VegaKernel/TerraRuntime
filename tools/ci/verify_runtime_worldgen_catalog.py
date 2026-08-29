#!/usr/bin/env python3
import argparse
import hashlib
import re
from pathlib import Path


def read_evidence(path: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        if "=" not in raw:
            continue
        key, value = raw.split("=", 1)
        result[key] = value
    return result


def extract_string_constant(source: str, name: str) -> str:
    match = re.search(
        rf'public\s+const\s+string\s+{re.escape(name)}\s*=\s*"([0-9a-fA-F]+)"\s*;',
        source,
    )
    if not match:
        raise SystemExit(f"Runtime catalog is missing string constant {name}.")
    return match.group(1).lower()


def extract_array(source: str, name: str) -> list[str]:
    match = re.search(
        rf'private\s+static\s+readonly\s+string\[\]\s+{re.escape(name)}\s*=\s*\[(.*?)\];',
        source,
        re.DOTALL,
    )
    if not match:
        raise SystemExit(f"Runtime catalog is missing string array {name}.")
    return re.findall(r'"((?:\\.|[^"\\])*)"', match.group(1))


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify runtime vanilla worldgen catalog against pinned source evidence.")
    parser.add_argument("--runtime-catalog", required=True)
    parser.add_argument("--source-evidence", required=True)
    args = parser.parse_args()

    source = Path(args.runtime_catalog).read_text(encoding="utf-8")
    evidence = read_evidence(Path(args.source_evidence))

    expected_constants = {
        "AddPassesSha256": evidence["WorldGen_AddPasses_sha256"],
        "RegistrationExpressionSequenceSha256": evidence["WorldGen_AddPasses_registration_sequence_sha256"],
        "ResolvedPassNameSequenceSha256": evidence["WorldGen_resolved_registration_sequence_sha256"],
        "DisablePassesForSpecialSeedsSha256": evidence["WorldGen_DisablePassesForSpecialSeeds_sha256"],
    }
    for name, expected in expected_constants.items():
        actual = extract_string_constant(source, name)
        if actual != expected.lower():
            raise SystemExit(f"{name} mismatch: runtime={actual}, pinned={expected}")

    pass_names = extract_array(source, "PassNames")
    source_count = int(evidence["WorldGen_resolved_registration_count"])
    source_names = [evidence[f"WorldGen_resolved_registration_{index:03d}"] for index in range(source_count)]
    if pass_names != source_names:
        for index, (actual, expected) in enumerate(zip(pass_names, source_names)):
            if actual != expected:
                raise SystemExit(f"Pass name mismatch at {index}: runtime={actual!r}, pinned={expected!r}")
        raise SystemExit(f"Pass name count mismatch: runtime={len(pass_names)}, pinned={len(source_names)}")

    digest = hashlib.sha256("\n".join(pass_names).encode("utf-8")).hexdigest()
    if digest != evidence["WorldGen_resolved_registration_sequence_sha256"]:
        raise SystemExit(f"Resolved pass-name digest mismatch: runtime={digest}")

    print(f"runtime_catalog_pass_count={len(pass_names)}")
    print(f"runtime_catalog_sequence_sha256={digest}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
