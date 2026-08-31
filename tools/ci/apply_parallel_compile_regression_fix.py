from pathlib import Path

path = Path("tests/TerraRuntime.Tests/VanillaKingSlimeDifficultyLootTests.cs")
text = path.read_text(encoding="utf-8")
old = "DamageSource.FromPlayerProjectile(player),"
new = "DamageSource.FromPlayerProjectile(player, new ProjectileHandle(11, new ProjectileGeneration(1))),"
if text.count(old) != 1:
    raise SystemExit(f"King Slime projectile provenance compile fix: expected 1 occurrence, found {text.count(old)}")
path.write_text(text.replace(old, new), encoding="utf-8")
