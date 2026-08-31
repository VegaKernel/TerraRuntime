from pathlib import Path

path = Path("src/TerraRuntime/PlayerBootstrapFrameSink.cs")
text = path.read_text(encoding="utf-8-sig")
old = "using TerraRuntime.Protocol.Multiplicity;\n\nnamespace TerraRuntime;"
new = "using TerraRuntime.Protocol.Multiplicity;\nusing TerraRuntime.World;\n\nnamespace TerraRuntime;"
count = text.count(old)
if count != 1:
    raise SystemExit(f"world namespace import: expected 1 occurrence, found {count}")
path.write_text(text.replace(old, new), encoding="utf-8-sig")
