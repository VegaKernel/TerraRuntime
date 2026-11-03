"""Invented compiler spellings: role binding must preserve the checked game contract."""
from pathlib import Path
import sys
import unittest
sys.path.insert(0, str(Path(__file__).parent))
import check_moon_lord_death_source as check


def attachment(by_ref=False):
    receiver = "((Vector2)(ref motion))" if by_ref else "motion"
    return (
        "Vector2 motion = Main.npc[(int)ai[3]].Center + new Vector2(350f * num, -100f) - base.Center; "
        f"if ({receiver}.Length() > 20f) {{ {receiver}.Normalize(); motion *= 6f; "
        "Vector2 previous = velocity; if (motion != Vector2.Zero) { SimpleFlyMovement(motion, 0.3f); } "
        "velocity = Vector2.Lerp(previous, velocity, 0.5f); }")


def leech(by_ref=False):
    offset = ("Vector2 mouth = default(Vector2); ((Vector2)(ref mouth))..ctor(0f, 216f); "
              if by_ref else "Vector2 mouth = new Vector2(0f, 216f); ")
    return ("float duration = 90f; " + offset +
        "int head = (int)Math.Abs(ai[0]) - 1; int core = (int)Main.npc[head].ai[3]; "
        "if (left == -1 && Main.npc[actor].type == 397 && Main.npc[actor].ai[2] == 0f) { left = actor; } "
        "if (right == -1 && Main.npc[actor].type == 397 && Main.npc[actor].ai[2] == 1f) { right = actor; } "
        "int healingHead = head; int budget = 1000; "
        "int coreDeficit = Main.npc[core].lifeMax - Main.npc[core].life; "
        "int leftDeficit = Main.npc[left].lifeMax - Main.npc[left].life; "
        "int rightDeficit = Main.npc[right].lifeMax - Main.npc[right].life; "
        "int headDeficit = Main.npc[healingHead].lifeMax - Main.npc[healingHead].life; "
        "base.Center = Vector2.Lerp(proj.Center, Main.npc[head].Center + mouth, ai[2] / duration);")


class ReferenceBindingTests(unittest.TestCase):
    def test_attachment_accepts_both_compiler_receivers(self):
        for by_ref in [False, True]:
            check.require_retired_hand_attachment(attachment(by_ref))

    def test_attachment_rejects_changed_operands_and_wrong_aliases(self):
        for before, after in [("ai[3]", "ai[2]"), ("350f", "351f"), ("-100f", "-99f"),
                ("> 20f", "> 21f"), ("*= 6f", "*= 7f"), ("motion, 0.3f", "previous, 0.3f"),
                ("0.3f", "0.4f"), ("Lerp(previous, velocity", "Lerp(motion, velocity"), ("0.5f", "0.6f")]:
            with self.subTest(before=before), self.assertRaises(SystemExit):
                check.require_retired_hand_attachment(attachment(True).replace(before, after))

    def test_leech_compiler_spellings_bind_same_roles(self):
        plain = check.bind_leech_reference_locals(leech())
        self.assertEqual(plain, check.bind_leech_reference_locals(leech(True)))
        self.assertIn("Main.npc[num1213].Center + vector150, ai[2] / num1212", plain)
        self.assertIn("num1223 = Main.npc[num1217].lifeMax - Main.npc[num1217].life", plain)

    def test_leech_rejects_changed_source_roles(self):
        for before, after in [("= 90f", "= 91f"), ("0f, 216f", "0f, 215f"),
                ("Math.Abs(ai[0])", "Math.Abs(ai[1])"), ("npc[head].ai[3]", "npc[head].ai[2]"),
                ("left = actor", "right = actor"), ("npc[actor].ai[2] == 1f", "npc[other].ai[2] == 1f"),
                ("= 1000", "= 999"), ("npc[left].lifeMax - Main.npc[left].life", "npc[left].lifeMax - Main.npc[right].life")]:
            with self.subTest(before=before), self.assertRaises(SystemExit):
                check.bind_leech_reference_locals(leech(True).replace(before, after))

    def test_leech_rejects_ambiguous_role_declarations(self):
        with self.assertRaises(SystemExit):
            check.bind_leech_reference_locals(leech() + " float other = 90f;")


if __name__ == "__main__":
    unittest.main()
