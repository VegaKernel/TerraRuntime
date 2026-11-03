using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    // The caller supplies the world's existing wire traversal/clock owner. Fresh native generation
    // has no TE births; configured source sensors may still pulse this canonical circuit.
    internal WorldWiringCircuit1458? SourceWiringCircuit { get; set; }

    private void ClearColdLeashedItem(int x, int y, ushort type)
    {
        var owner=store.RuntimeTileEntities??throw new NotSupportedException("Leashed anchors require canonical tile-entity metadata.");
        if(!owner.TryPrepare(1,out var change)||change is null||!change.TryGetAt(x,y,out var entity))throw new InvalidOperationException("Could not inspect cold leashed anchor.");
        if(entity?.Kind is not (WorldTileEntityKind.KiteAnchor or WorldTileEntityKind.CritterAnchor)||entity.Payload is not WorldLeashedAnchorPayload leash||leash.ItemType<=0)return;
        // Source prefix accepts either subclass at this anchor. In genuine fresh generation neither
        // constructor/ReadExtraData has created a physical leash; loading/InsertItem is another owner.
        if(!change.TryStageLeashedAnchorItemClear(x,y,entity.Kind,leash.ItemType)||!change.TrySeal())throw new InvalidOperationException("Could not stage leashed held-item reset.");
        WorldSectionId[] sections=[TerrariaSectionGeometry.FromTile(store.Dimensions,x,y)];store.BeginMetadataMutation(sections);
        try{if(!change.TryAdoptUnpublished())throw new InvalidOperationException("Leashed held-item reset changed during adoption.");}finally{store.EndMetadataMutation(sections);}
    }

    private void RequireColdSensorCircuit(int x,int y)
    {
        var owner=store.RuntimeTileEntities??throw new NotSupportedException("Logic sensors require canonical tile-entity metadata.");
        if(!owner.TryPrepare(1,out var inspection)||inspection is null||!inspection.TryGetAt(x,y,out var row))throw new InvalidOperationException("Could not inspect sensor circuit.");
        if(row?.Kind==WorldTileEntityKind.LogicSensor&&row.Payload is WorldLogicSensorPayload{IsOn:true,LogicCheck:>=3 and <=7})
        {
            if(SourceWiringCircuit is null||!SourceWiringCircuit.OwnsTiles(store))
                throw new NotSupportedException("An on sensor requires its canonical wire circuit.");
            RequireQualifiedColdSensorNetwork(x,y);
        }
    }

    private void RequireQualifiedColdSensorNetwork(int x,int y)
    {
        // Read-only admission, not another wire executor. Other device and live teleport/NPC
        // branches need their own qualification before this cold sensor can reach them.
        foreach(var flag in new[]{WorldTileFlags.WireRed,WorldTileFlags.WireBlue,WorldTileFlags.WireGreen,WorldTileFlags.WireYellow})
        {
            var visited=new HashSet<(int X,int Y)>();var pending=new Queue<(int X,int Y)>();pending.Enqueue((x,y));
            while(pending.TryDequeue(out var cell))
            {
                if(!Contains(cell.X,cell.Y)||!visited.Add(cell)||(At(cell.X,cell.Y).Flags&flag)==0)continue;
                var tile=At(cell.X,cell.Y);
                if(tile.HasActuator||tile.IsActive&&tile.Type!=4&&cell!=(x,y))
                    throw new NotSupportedException("Cold sensor wire component requires an independently qualified device owner.");
                pending.Enqueue((cell.X,cell.Y+1));pending.Enqueue((cell.X,cell.Y-1));pending.Enqueue((cell.X+1,cell.Y));pending.Enqueue((cell.X-1,cell.Y));
            }
        }
    }

    private void KillColdLogicSensor(int x, int y)
    {
        var owner = store.RuntimeTileEntities ?? throw new NotSupportedException("Logic sensors require canonical tile-entity metadata.");
        if (!owner.TryPrepare(1, out var inspection) || inspection is null || !inspection.TryGetAt(x, y, out var row))
            throw new InvalidOperationException("Could not inspect generation sensor.");
        if (row?.Kind != WorldTileEntityKind.LogicSensor || row.Payload is not WorldLogicSensorPayload sensor) return;
        // UpdateWorld returns while generating/loading: its TE update-loop and deferred removal
        // branch are unreachable in this cold context. This is not an imported live-TE update path.
        if (sensor.IsOn && sensor.LogicCheck is >= 3 and <= 7)
        {
            if (SourceWiringCircuit is null) throw new NotSupportedException("An on sensor requires its canonical wire circuit.");
            SourceWiringCircuit.HitSwitch(x, y);
        }
        RemoveOwnedTileEntity(x, y, WorldTileEntityKind.LogicSensor);
    }

    private void FramePylon(int x, int y)
    {
        if (destroyingObject) return;
        var tile = At(x, y);int left=x-tile.FrameX/18%3,top=y-tile.FrameY/18%4;
        if (!Contains(left, top) || !Contains(left+2, top+4)) return;
        bool broken=false;
        for(int column=left;column<left+3;column++)
        {
            broken |= !SolidTileAllowBottomSlope(column,top+4);
            for(int row=top;row<top+4;row++)broken |= !At(column,row).IsActive || At(column,row).Type!=597;
        }
        if(!broken)return;
        var owner=store.RuntimeTileEntities??throw new NotSupportedException("Pylons require canonical tile-entity metadata.");
        if(!owner.TryPrepare(1,out var inspection)||inspection is null||!inspection.TryGetAt(left,top,out var entity))throw new InvalidOperationException("Could not inspect generation pylon.");
        if(entity?.Kind==WorldTileEntityKind.TeleportationPylon)
        {
            RemoveOwnedTileEntity(left,top,WorldTileEntityKind.TeleportationPylon);
            owner.RequestImmediatePylonUpdate(SourceClient);
        }
        destroyingObject=true;
        for(int column=left;column<left+3;column++)for(int row=top;row<top+4;row++)
            if(At(column,row).IsActive&&At(column,row).Type==597)KillTile(column,row);
        destroyingObject=false;
    }
}
