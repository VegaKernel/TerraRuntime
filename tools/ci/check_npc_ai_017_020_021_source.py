from pathlib import Path
import re

root = Path(__file__).resolve().parents[2]
npc = root / "decompiled/1458/Terraria/NPC.cs"
if not npc.exists():
    raise SystemExit("decompiled/1458/Terraria/NPC.cs not found")
source = npc.read_text(encoding="utf-8", errors="ignore")

def bounded(start_marker: str, end_marker: str) -> str:
    start = source.find(start_marker)
    if start < 0:
        raise SystemExit(f"missing source marker: {start_marker}")
    end = source.find(end_marker, start + len(start_marker))
    if end < 0:
        raise SystemExit(f"missing source end marker: {end_marker}")
    return source[start:end]

def require(name: str, block: str, *tokens: str) -> None:
    missing = [token for token in tokens if token not in block]
    if missing:
        raise SystemExit(f"{name} source contract missing: {missing}")

vulture_defaults = bounded("else if (type == 61)", "else if (type == 62)")
require("Vulture defaults", vulture_defaults,
        "width = 36;", "height = 36;", "aiStyle = 17;", "damage = 15;",
        "defense = 4;", "lifeMax = 40;", "knockBackResist = 0.8f;")

raven_defaults = bounded("else if (type == 301)", "if (type == 302)")
require("Raven defaults", raven_defaults,
        "width = 36;", "height = 26;", "aiStyle = 17;", "damage = 12;",
        "defense = 2;", "lifeMax = 35;", "knockBackResist = 0.85f;")

spike_defaults = bounded("else if (type == 70)", "else if (type == 71)")
require("Spike Ball defaults", spike_defaults,
        "width = 34;", "height = 34;", "aiStyle = 20;", "damage = 32;",
        "defense = 100;", "lifeMax = 100;", "noGravity = true;",
        "noTileCollide = true;", "dontTakeDamage = true;", "scale = 1.5f;")

wheel_defaults = bounded("else if (type == 72)", "else if (type == 73)")
require("Blazing Wheel defaults", wheel_defaults,
        "width = 34;", "height = 34;", "aiStyle = 21;", "damage = 24;",
        "defense = 100;", "lifeMax = 100;", "noGravity = true;",
        "dontTakeDamage = true;", "scale = 1.2f;")
if "noTileCollide = true;" in wheel_defaults:
    raise SystemExit("Blazing Wheel unexpectedly gained noTileCollide in pinned SetDefaults")

ai17 = bounded("if (aiStyle == 17)\n\t\t{", "if (aiStyle == 18)\n\t\t{")
require("AI_017", ai17,
        "noGravity = true;", "noGravity = false;", "velocity.Y -= 6f;",
        "velocity.X = oldVelocity.X * -0.5f;", "velocity.Y = oldVelocity.Y * -0.5f;",
        "velocity.X -= 0.1f;", "velocity.X += 0.1f;",
        "velocity.Y -= 0.5f;", "velocity.Y = -4f;")

ai20 = bounded("if (aiStyle == 20)\n\t\t{", "else if (aiStyle == 21)\n\t\t{")
require("AI_020", ai20,
        "TargetClosest();", "position.Y += height / 2 + 8;",
        "ai[3] = 1f + (float)Main.rand.Next(15) * 0.1f;",
        "velocity.Y = (float)(directionY * 6) * ai[3];", "ai[0] = -1f;")
speed = re.search(r"float\s+(\w+)\s*=\s*6f\s*\*\s*ai\[3\];", ai20)
accel = re.search(r"float\s+(\w+)\s*=\s*0\.2f\s*\*\s*ai\[3\];", ai20)
if speed is None or accel is None:
    raise SystemExit("AI_020 speed/acceleration structure drifted")
speed_name = re.escape(speed.group(1))
accel_name = re.escape(accel.group(1))
if not re.search(rf"velocity\.X\s*\+=\s*{accel_name}\s*\*\s*\(float\)direction;", ai20):
    raise SystemExit("AI_020 horizontal acceleration contract drifted")
if not re.search(rf"velocity\.Y\s*\+=\s*{accel_name}\s*\*\s*\(float\)directionY;", ai20):
    raise SystemExit("AI_020 vertical acceleration contract drifted")
if not re.search(rf"velocity\.X\s*=\s*{speed_name}\s*\*\s*\(float\)direction;", ai20):
    raise SystemExit("AI_020 horizontal phase speed contract drifted")

ai21 = bounded("else if (aiStyle == 21)\n\t\t{", "else if (aiStyle == 22)\n\t\t{")
require("AI_021", ai21,
        "TargetClosest();", "directionY = 1;", "ai[0] = 1f;",
        "rotation += (float)(direction * directionY) * 0.13f;",
        "rotation -= (float)(direction * directionY) * 0.13f;")
wheel_speed = re.search(r"int\s+(\w+)\s*=\s*6;", ai21)
if wheel_speed is None:
    raise SystemExit("AI_021 speed constant drifted")
wheel_speed_name = re.escape(wheel_speed.group(1))
if not re.search(rf"velocity\.X\s*=\s*{wheel_speed_name}\s*\*\s*direction;", ai21):
    raise SystemExit("AI_021 horizontal speed contract drifted")
if not re.search(rf"velocity\.Y\s*=\s*{wheel_speed_name}\s*\*\s*directionY;", ai21):
    raise SystemExit("AI_021 vertical speed contract drifted")

print("NPC AI 017/020/021 TerrariaServer 1.4.5.8 source contract OK")
