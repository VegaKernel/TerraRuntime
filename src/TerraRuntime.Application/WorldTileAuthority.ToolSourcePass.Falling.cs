using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Tiles;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct FallingWorldBoundary1458(
    TerrariaTileManipulationState State, ToolWorldPublicationOffset1458 End);

internal sealed partial class WorldTileAuthority
{
    internal sealed partial class ToolWorldSourcePass1458
    {
        // AI010 changes overlapping decorations/slopes before HandleMovement.
        // The loop's upper Y bound is evaluated again after every16px lift.
        internal bool TryStageFallingAiTiles(in ProjectileSnapshot expected,
            in ProjectileStateUpdate body, in ProjectileLifecycleState life, ref float y,
            List<FallingWorldBoundary1458> boundaries)
        {
            float x = body.PositionX;
            if (!VanillaFallingBlock1458.TryGetTile(body.Type, out _) || server is null ||
                !IsCurrentOwned || work.Count != 0 || !float.IsFinite(x) || !float.IsFinite(y)) return Reject();
            int width = Overlay.Tiles.Dimensions.WidthTiles, height = Overlay.Tiles.Dimensions.HeightTiles;
            int count = 0;
            for (int tx = (int)(x / 16f); tx <= (int)((x + 10f) / 16f); tx++)
                for (int ty = (int)(y / 16f); ty <= (int)((y + 10f) / 16f); ty++)
                {
                    if (++count > 64) return Reject();
                    if ((uint)tx >= (uint)width || (uint)ty >= (uint)height) continue;
                    if (!Overlay.TryRead(tx, ty, out var tile)) return Reject();
                    if (!tile.IsActive) continue;
                    if (tile.Type is >= 185 and <= 187 or 165 or 12 or 665 or 639 or 105 or 178)
                    {
                        var phaseBody = body with { PositionY = y };
                        if (!server.TrySetSourceActorView(in expected, in phaseBody, in life, true)) return Reject();
                        if (!TryKillTile(tx, ty, false, false, false, out _)) return false;
                        boundaries.Add(new(new(0, checked((short)tx), checked((short)ty), 0, 0),
                            server.CapturePublicationOffset()));
                    }
                    else if (tile.SourceSlope is 1 or 2)
                    {
                        if (!VanillaToolTileCatalog1458.TryGet(tile.TileType, out var definition)) return Reject();
                        if (definition.IsPlatform) continue;
                        var phaseBody = body with { PositionY = y };
                        if (!server.TrySetSourceActorView(in expected, in phaseBody, in life, true)) return Reject();
                        if (!TrySlopeTile(tx, ty, 0, out _)) return false;
                        boundaries.Add(new(new(14, checked((short)tx), checked((short)ty), 0, 0),
                            server.CapturePublicationOffset()));
                        y -= 16f;
                    }
                }
            return IsCurrentOwned;
        }

        internal bool TryStageFallingLanding(in ProjectileSnapshot killed, bool? rainbowSand,
            List<FallingWorldBoundary1458> boundaries)
        {
            if (server is null || !IsCurrentOwned || work.Count != 0 ||
                !VanillaFallingBlock1458.TryGetTile(killed.Type, out var tileType)) return Reject();
            int tx = (int)(killed.PositionX + 5f) / 16;
            int ty = (int)(killed.PositionY + 5f) / 16;
            if (!Overlay.TryReadSourceSafely(tx, ty, out var target)) return Reject();
            if (target.IsActive && !target.IsActuated && target.IsHalfBrick &&
                killed.VelocityY > 0f && MathF.Abs(killed.VelocityY) > MathF.Abs(killed.VelocityX))
            {
                ty--;
                if (!Overlay.TryReadSourceSafely(tx, ty, out target)) return Reject();
            }
            int item = killed.Type.Value switch
            {
                31 => 169, 56 => 370, 67 => 408, 71 => 424, 179 => 1103,
                241 => 1246, 812 => 4090, 411 => 71, 412 => 72, 413 => 73, 414 => 74, _ => 0
            };
            if (target.IsActive) return TryStageFallingItem(item, killed.PositionX + 5f, killed.PositionY + 5f);
            bool itemOnly = false;
            if (ty < Overlay.Tiles.Dimensions.HeightTiles - 2)
            {
                if (!Overlay.TryRead(tx, ty + 1, out var support)) return Reject();
                if (support.IsActive)
                {
                    if (!TryBelowMakesFalling(tx, ty, out bool belowFalls)) return Reject();
                    itemOnly = VanillaFallingBlock1458.ConvertsLandingToItem(support.TileType) || belowFalls;
                }
            }
            if (itemOnly) return TryStageFallingItem(item, killed.PositionX + 5f, killed.PositionY + 3f);
            // Main.onlyShimmerOceanWorlds is an explicit source-world input.
            // Missing metadata must not silently certify ordinary sand paint.
            if (tileType.Value == 53 && rainbowSand is not false) return Reject();
            var request = new WorldTileMutationRequest(WorldTileMutationKind.PlaceTile, tx, ty, tileType);
            if (!server.TryStage(in request, out var placed)) return Reject();
            if (placed.Applied)
            {
                mutationOrdinal++;
                if (tileType.Value == 53 && facts.TenthAnniversaryWorld && !facts.RemixWorld &&
                    !facts.NotTheBeesWorld && !facts.DontStarveWorld)
                {
                    if (!Overlay.TryRead(tx, ty, out var before)) return Reject();
                    var after = before; after.TileColor = 7;
                    if (!Stage(WorldTileMutationKind.TransformTile, tx, ty, in before, in after)) return false;
                }
                if (!PushSquare(tx, ty) || !Drain()) return false;
            }
            if (!Overlay.TryRead(tx, ty, out var actual)) return Reject();
            if (actual.IsActive && actual.TileType == tileType)
            {
                if (!Overlay.TryRead(tx, ty + 1, out var support)) return Reject();
                if (support.IsHalfBrick || support.SourceSlope != 0)
                {
                    if (!TrySlopeTile(tx, ty + 1, 0, out _)) return false;
                    boundaries.Add(new(new(14, checked((short)tx), checked((short)(ty + 1)), 0, 0),
                        server.CapturePublicationOffset()));
                }
                boundaries.Add(new(new(1, checked((short)tx), checked((short)ty), checked((short)tileType.Value), 0),
                    server.CapturePublicationOffset()));
                return IsCurrentOwned;
            }
            return placed.Applied || TryStageFallingItem(item, killed.PositionX + 5f, killed.PositionY + 3f);
        }

        internal bool TryStageFallingCuts(in ProjectileStateUpdate body, bool collision,
            bool npcProjectile, List<FallingWorldBoundary1458> boundaries)
        {
            if (server is null || !IsCurrentOwned || !VanillaFallingBlock1458.TryGetTile(body.Type, out _))
                return Reject();
            // Collision always draws before its ownership/damage test, including
            // the four non-damaging coin bodies. Damage itself has no such draw.
            if (collision)
            {
                if (body.Damage > 0 && !CutPass(in body, 1, boundaries)) return false;
                int cracked = random.Next(3);
                return body.Damage <= 0 || cracked != 0 || CutPass(in body, 2, boundaries);
            }
            return npcProjectile || body.Damage <= 0 || CutPass(in body, 0, boundaries);
        }

        private bool CutPass(in ProjectileStateUpdate body, byte mode, List<FallingWorldBoundary1458> boundaries)
        {
            bool collision = mode != 0;
            int left = collision ? (int)(body.PositionX + body.VelocityX - 1f) / 16 : (int)(body.PositionX / 16f);
            int top = collision ? (int)(body.PositionY + body.VelocityY - 1f) / 16 : (int)(body.PositionY / 16f);
            int right = (int)((body.PositionX + 10f + (collision ? body.VelocityX + 1f : 0f)) / 16f) + 1;
            int bottom = (int)((body.PositionY + 10f + (collision ? body.VelocityY + 1f : 0f)) / 16f) + 1;
            left = Math.Clamp(left, 0, Overlay.Tiles.Dimensions.WidthTiles);
            right = Math.Clamp(right, 0, Overlay.Tiles.Dimensions.WidthTiles);
            top = Math.Clamp(top, 0, Overlay.Tiles.Dimensions.HeightTiles);
            bottom = Math.Clamp(bottom, 0, Overlay.Tiles.Dimensions.HeightTiles);
            if ((long)(right - left) * (bottom - top) > 64) return Reject();
            for (int x = left; x < right; x++)
                for (int y = top; y < bottom; y++)
                {
                    if (!Overlay.TryRead(x, y, out var tile)) return Reject();
                    if (mode == 0 && !tile.IsActive) continue;
                    bool candidate = mode switch
                    {
                        1 => tile.Type == 484,
                        2 => tile.Type is 481 or 482 or 483,
                        _ => VanillaProjectileTileCutFacts.IsCuttable(tile.TileType)
                    };
                    if (!candidate || VanillaProjectileTileCutFacts.IsBlockedByWall(tile.WallType) ||
                        VanillaProjectileTileCutFacts.RequiresMinimumFrame(tile.TileType) &&
                        tile.FrameX < VanillaProjectileTileCutFacts.FrameGatedMinimumFrameX) continue;
                    if (y + 1 >= Overlay.Tiles.Dimensions.HeightTiles ||
                        !Overlay.TryRead(x, y + 1, out var support)) return Reject();
                    if (VanillaProjectileTileCutFacts.IsBlockedBySupport(support.TileType)) continue;
                    if (!TryKillTile(x, y, false, false, false, out _)) return false;
                    boundaries.Add(new(new(0, checked((short)x), checked((short)y), 0, 0),
                        server!.CapturePublicationOffset()));
                }
            return IsCurrentOwned;
        }

        private bool TryBelowMakesFalling(int x, int y, out bool falling)
        {
            falling = false;
            if (y >= Overlay.Tiles.Dimensions.HeightTiles - 1) return true;
            if (!Overlay.TryRead(x, y + 1, out var below)) return Reject();
            if (!below.IsActive || below.IsActuated) { falling = true; return true; }
            if (!Overlay.TryRead(x, y + 2, out var second)) return Reject();
            falling = (!second.IsActive || second.IsActuated) && !VanillaTileCollisionCatalog.IsSolid(below.TileType) ||
                below.Type == 165;
            return true;
        }

        private bool TryStageFallingItem(int item, float centerX, float centerY)
        {
            if (item <= 0 || server is null) return Reject();
            var drop = new WorldItemDropStateUpdate(centerX - 8f, centerY - 8f,
                random.Next(-30, 31) * .1f, random.Next(-40, -15) * .1f,
                1, 0, WorldItemOwnershipMode.None, checked((short)item), false, 0f, 0);
            return server.TryStageDrop(in drop) && IsCurrentOwned;
        }
    }
}
