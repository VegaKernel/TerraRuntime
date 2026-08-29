#!/usr/bin/env python3
import argparse
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


def extract_constant(source: str, name: str) -> str:
    match = re.search(
        rf'public\s+const\s+string\s+{re.escape(name)}\s*=\s*"([0-9a-fA-F]+)"\s*;',
        source,
    )
    if match is None:
        raise SystemExit(f"Runtime RNG is missing fingerprint constant {name}.")
    return match.group(1).lower()


def find_method_hash(evidence: dict[str, str], signature_fragment: str) -> str:
    for key, signature in evidence.items():
        if not key.endswith("_signature") or signature_fragment not in signature:
            continue
        hash_key = key.removesuffix("_signature") + "_sha256"
        if hash_key not in evidence:
            raise SystemExit(f"Pinned RNG evidence has signature without fingerprint: {key}")
        return evidence[hash_key]
    raise SystemExit(f"Pinned RNG evidence is missing method signature containing {signature_fragment!r}.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify TerraRuntime's vanilla RNG fingerprints against pinned Terraria evidence.")
    parser.add_argument("--runtime-rng", required=True)
    parser.add_argument("--source-evidence", required=True)
    args = parser.parse_args()

    source = Path(args.runtime_rng).read_text(encoding="utf-8")
    evidence = read_evidence(Path(args.source_evidence))
    expected = {
        "SourceSha256": evidence["UnifiedRandom_source_sha256"],
        "SetSeedSha256": find_method_hash(evidence, "SetSeed(int Seed)"),
        "InternalSampleSha256": find_method_hash(evidence, "InternalSample()"),
        "SampleSha256": find_method_hash(evidence, "Sample()"),
        "LargeRangeSha256": find_method_hash(evidence, "GetSampleForLargeRange()"),
    }

    for name, pinned in expected.items():
        runtime = extract_constant(source, name)
        if runtime != pinned.lower():
            raise SystemExit(f"{name} mismatch: runtime={runtime}, pinned={pinned}")
        print(f"{name}={runtime}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
