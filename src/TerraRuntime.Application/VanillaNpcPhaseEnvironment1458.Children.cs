using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class VanillaNpcPhaseEnvironment1458
{
    private sealed partial class Capture
    {
        private readonly List<(byte Slot, NpcSnapshot? Active)> childInputs = [];

        public bool TryFinishScutlixMount(RuntimeNpcStore.AiSpawnPreview preview, in NpcSnapshot birth,
            out NpcSnapshot finished)
        {
            finished = default;
            if (!IsCurrent || !VanillaNpcDefinitionCatalog.TryGet(birth.TypeIdentity, birth.NetIdentity, out var definition) ||
                !definition.TryResolveHitbox(birth.Simulation, out var body)) return false;
            terrain.FenceBody(birth.PositionX, birth.PositionY, 0f, 0f, body.Width, body.Height);
            bool wet = VanillaWorldCollision.TryGetWetContact(tiles, birth.PositionX, birth.PositionY,
                body.Width, body.Height, out _);
            return IsCurrent && preview.TryFinishScutlixMount(in birth, wet, out finished);
        }

        public bool TryCaptureNpcSlot(byte slot, out VanillaNpcRetainedSlot captured)
        {
            captured = default;
            if (slot >= npcs.Capacity || !IsCurrent) return false;
            NpcSnapshot? active = npcs.TryGetActive(slot, out var current) ? current : null;
            foreach (var saved in childInputs)
            {
                if (saved.Slot != slot) continue;
                if (saved.Active != active) return false;
                captured = Captured(slot, active);
                return true;
            }
            childInputs.Add((slot, active));
            captured = Captured(slot, active);
            return IsCurrent;
        }

        private bool ChildInputsCurrent()
        {
            foreach (var saved in childInputs)
            {
                bool active = npcs.TryGetActive(saved.Slot, out var current);
                if (saved.Active is not { } expected)
                { if (active) return false; continue; }
                if (!active) return false;
                if (current == expected) continue;
                // An invalid parent can be the source slot itself. Its source read still
                // sees the same fields after the executor's identical-state placeholder;
                // the preview separately requires that exact single table mutation.
                if (expected != source || source.Revision.Value == ulong.MaxValue ||
                    current != (source with { Revision = new(source.Revision.Value + 1) })) return false;
            }
            return true;
        }

        private static VanillaNpcRetainedSlot Captured(byte slot, NpcSnapshot? active) => active is { } current
            ? new(slot, true, new(current.Type, current.NetId, current.PositionX, current.PositionY,
                current.VelocityX, current.VelocityY, current.Target, current.Ai, current.Simulation))
            // AI072/075 branch on active before reading any retained identity/body fields.
            // A false active read does not grant authority over an unrepresented inactive NPC.
            : new(slot, false, default);
    }
}
