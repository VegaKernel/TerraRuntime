#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1458}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CACHE="$ROOT/.cache/terraria-$VERSION"
TOOLS="$ROOT/.tools"
OUT="$ROOT/decompiled/$VERSION"
ZIP="$CACHE/terraria-server-$VERSION.zip"
URL="https://terraria.org/api/download/pc-dedicated-server/terraria-server-$VERSION.zip"

command -v dotnet >/dev/null 2>&1 || {
  echo "dotnet SDK is required" >&2
  exit 1
}
command -v curl >/dev/null 2>&1 || {
  echo "curl is required" >&2
  exit 1
}
command -v unzip >/dev/null 2>&1 || {
  echo "unzip is required" >&2
  exit 1
}

mkdir -p "$CACHE" "$TOOLS" "$ROOT/decompiled"

if [[ ! -f "$ZIP" ]]; then
  echo "Downloading Terraria dedicated server $VERSION..."
  curl -fL "$URL" -o "$ZIP"
fi

rm -rf "$CACHE/extracted"
mkdir -p "$CACHE/extracted"
unzip -q -o "$ZIP" -d "$CACHE/extracted"

ASSEMBLY="$(find "$CACHE/extracted" -type f -path '*/Windows/TerrariaServer.exe' -print -quit)"
if [[ -z "$ASSEMBLY" ]]; then
  ASSEMBLY="$(find "$CACHE/extracted" -type f -name 'TerrariaServer.exe' -print -quit)"
fi
if [[ -z "$ASSEMBLY" ]]; then
  echo "TerrariaServer.exe was not found in the downloaded archive" >&2
  exit 1
fi

if [[ ! -x "$TOOLS/ilspycmd" ]]; then
  dotnet tool install ilspycmd --tool-path "$TOOLS"
fi

rm -rf "$OUT"
mkdir -p "$OUT"

"$TOOLS/ilspycmd" -p -o "$OUT" "$ASSEMBLY"

SHA256="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
cat > "$OUT/REFERENCE_SOURCE.txt" <<EOF
Terraria dedicated server version: $VERSION
Download URL: $URL
Decompiled assembly: $ASSEMBLY
Assembly SHA-256: $SHA256
Decompiler: ilspycmd

This directory is intentionally ignored by git and is for local reference only.
EOF

echo "Reference tree created at: $OUT"
