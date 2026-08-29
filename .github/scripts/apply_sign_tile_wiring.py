from pathlib import Path

path = Path("src/TerraRuntime/TerrariaServerHost.cs")
text = path.read_text(encoding="utf-8")

old = "        var signStore = new RuntimeSignStore(world.Signs);\n"
new = "        var signStore = new RuntimeSignStore(world.Signs, world.Tiles);\n"

if new in text:
    print("sign tile normalization host wiring already applied")
    raise SystemExit(0)

count = text.count(old)
if count != 1:
    raise SystemExit(f"expected exactly one sign store construction, found {count}")

path.write_text(text.replace(old, new, 1), encoding="utf-8")
print("applied sign tile normalization host wiring")
