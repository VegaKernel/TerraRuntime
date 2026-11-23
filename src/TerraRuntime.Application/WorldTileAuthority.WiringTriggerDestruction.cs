using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        private bool FramePressurePlate(int x, int y)
        {
            if (!Overlay.TryReadSourceSafely(x, y + 1, out var support) ||
                !VanillaToolTileCatalog1458.TryGet(support.TileType, out var facts)) return false;
            int platformFrame = support.FrameX / 18;
            bool properPlatformTop = facts.IsPlatform &&
                platformFrame is >= 0 and <= 7 or >= 12 and <= 16 or >= 25 and <= 26;
            // WorldGen.Check1x1 calls SolidTileAllowBottomSlope, then excludes boulders.
            if (support.IsActive && !support.IsActuated && !support.IsHalfBrick &&
                (facts.IsSolid || facts.IsSolidTop) && (support.SourceSlope is not (1 or 2) || properPlatformTop) &&
                !facts.IsBoulder) return true;
            return Push(new(StepKind.Kill, x, y));
        }

        private bool TryRemoveLogicSensor(int x, int y)
        {
            if (clientRole) return true;
            if (server is null || !server.TryGetFurniture(out var metadata) || metadata is null ||
                !metadata.TryGetAt(x, y, out var sensor)) return false;
            if (sensor is null) return true;
            if (sensor.Kind != WorldTileEntityKind.LogicSensor || sensor.Payload is not WorldLogicSensorPayload logic ||
                logic.LogicCheck is < 1 or > 7) return false;
            // TELogicSensor.Kill pulses before removing the entity. Our admitted
            // circuit has no devices that consult actor or sensor state during traversal.
            if (logic.IsOn && logic.LogicCheck >= 3 && !server.TryStageDestructionRelease(x, y)) return false;
            if (!metadata.TryStageRemoval(x, y, WorldTileEntityKind.LogicSensor)) return false;
            server.RecordLogicSensorRemoval();
            return true;
        }

        private bool TryStageWiringTriggerDrop(int x, int y, in WorldTile before)
        {
            if (server is null) return false;
            ushort item = GetWiringTriggerDropItem(before.Type, before.FrameY);
            // The source routes a pressed plate's release through HitSwitch before
            // allocating the item, including when the observed frame has no item.
            if (before.Type == 428 && !server.TryStageWeightedPlateRemoval(x, y)) return false;
            return item == 0 || server.TryStageSourceItem(new ItemTypeId(item), 1, x, y);
        }

        private static ushort GetWiringTriggerDropItem(ushort type, short frameY)
        {
            // The dedicated KillTile_GetItemDrops maps the vertical frame to each item.
            int style = frameY / 18;
            return type switch
            {
                135 => style switch { 0 => 529, 1 => 541, 2 => 542, 3 => 543, 4 => 852, 5 => 853, 6 => 1151, _ => 0 },
                423 => style switch { 0 => 3613, 1 => 3614, 2 => 3615, 3 => 3726, 4 => 3727, 5 => 3728, 6 => 3729, _ => 0 },
                428 => style switch { 0 => 3630, 1 => 3632, 2 => 3631, 3 => 3626, _ => 0 },
                _ => 0
            };
        }
    }

    internal sealed partial class ToolWorldMutationPlan
    {
        private List<(int X, int Y)>? unpressedPressurePlates;
        private List<WorldWeightedPressurePlates1458.Destruction>? pressedPressurePlates;
        private bool removesLogicSensor;
        private bool hasDestructionRelease;

        internal void RecordLogicSensorRemoval() => removesLogicSensor = true;

        private void AdoptTriggerRemovalState()
        {
            // TELogicSensor.Kill clears the existing teleportation block even
            // when this sensor's current state does not trip its wire.
            if (pressedPressurePlates is not null)
                foreach (var removal in pressedPressurePlates) removal.Adopt();
            if (hasDestructionRelease) owner.wiring!.AdoptDetachedRelease();
            else if (removesLogicSensor) owner.wiring?.EndLogicSensorUpdate();
        }

        private bool AreWiringTriggerReadsCurrent
        {
            get
            {
                if (hasDestructionRelease && owner.wiring?.CanStageDetachedRelease != true) return false;
                if (pressedPressurePlates is not null)
                    foreach (var removal in pressedPressurePlates)
                        if (!removal.IsCurrent) return false;
                if (unpressedPressurePlates is null || Overlay.Tiles.RuntimePressurePlates is not { } pressure) return true;
                foreach (var (x, y) in unpressedPressurePlates)
                    if (pressure.IsPressed(x, y)) return false;
                return true;
            }
        }

        internal bool TryStageWeightedPlateRemoval(int x, int y)
        {
            if (Overlay.Tiles.RuntimePressurePlates?.PrepareDestruction(x, y) is { } removal)
            {
                if (!TryStageDestructionRelease(x, y)) return false;
                (pressedPressurePlates ??= []).Add(removal);
            }
            else (unpressedPressurePlates ??= []).Add((x, y));
            return true;
        }

        internal bool TryStageDestructionRelease(int x, int y)
        {
            if (owner.wiring is not { } wiring ||
                !wiring.TryStageDetachedRelease(x, y, Overlay.TryRead, StageReleaseTorch) ||
                !TryStageSourceHitSwitch(x, y)) return Fail();
            hasDestructionRelease = true;
            return true;
        }

        internal void RejectStagedDestructionRelease()
        {
            if (hasDestructionRelease) _ = Fail();
        }

        private bool StageReleaseTorch(int x, int y, in WorldTile before, in WorldTile after) =>
            TryStageSourceWiringCell(x, y, in before, in after) && TryStageSourceTileSquare(x, y);

        private bool HasSameWiringTriggerReads(ToolWorldMutationPlan other)
        {
            if (removesLogicSensor != other.removesLogicSensor || hasDestructionRelease != other.hasDestructionRelease ||
                (unpressedPressurePlates is null ? other.unpressedPressurePlates is not null :
                    other.unpressedPressurePlates is null || !unpressedPressurePlates.SequenceEqual(other.unpressedPressurePlates)))
                return false;
            if (pressedPressurePlates is null) return other.pressedPressurePlates is null;
            if (other.pressedPressurePlates is not { } expected || expected.Count != pressedPressurePlates.Count) return false;
            for (int i = 0; i < expected.Count; i++)
                if (!pressedPressurePlates[i].Matches(expected[i])) return false;
            return true;
        }
    }
}
