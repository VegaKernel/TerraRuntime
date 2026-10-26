using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("ToolsProbe <official-linux-edition-TerrariaServer.exe> <output.json>");
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Pinned capture host is Windows CoreCLR.");
string originalPath = Path.GetFullPath(args[0]), outputPath = Path.GetFullPath(args[1]);
string directory = Path.GetDirectoryName(originalPath)!, outputDirectory = Path.GetDirectoryName(outputPath)!;
const string sourceHash = "4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034";
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath))) != sourceHash)
    throw new InvalidDataException("Unexpected original server binary.");
if (string.Equals(originalPath, outputPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Separate output required.");
Directory.CreateDirectory(outputDirectory);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string neighbor = Path.Combine(directory, name.Name + ".dll");
    if (File.Exists(neighbor)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(neighbor);
    Assembly? game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TerrariaServer");
    string? resource = game?.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
    if (resource is null) return null;
    using Stream stream = game!.GetManifestResourceStream(resource)!;
    return AssemblyLoadContext.Default.LoadFromStream(stream);
};
Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(originalPath);
const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
Type T(string name) => game.GetType("Terraria." + name)!;
Type main = T("Main"), playerType = T("Player"), tileType = T("Tile"), itemType = T("Item"), randomType = T("Utilities.UnifiedRandom");
void Set(Type type, object? instance, string member, object? value)
{ if (type.GetField(member, flags) is { } field) field.SetValue(instance, value); else type.GetProperty(member, flags)!.SetValue(instance, value); }
object Read(Type type, object? instance, string member) => type.GetField(member, flags)?.GetValue(instance) ?? type.GetProperty(member, flags)!.GetValue(instance)!;
object R(object instance, string member) => Read(instance.GetType(), instance, member);
object? Call(Type type, object? instance, string member, params object?[] values) => type.GetMethods(flags)
    .Single(m => m.Name == member && m.GetParameters().Length == values.Length && m.GetParameters().Select(p => p.ParameterType)
        .Zip(values, (type, value) => value is null ? !type.IsValueType : type.IsInstanceOfType(value)).All(v => v)).Invoke(instance, values);
Set(T("Program"), null, "SavePath", Path.Combine(outputDirectory, "source-save"));
Set(main, null, "dedServ", true); Set(main, null, "netMode", 2); Set(main, null, "myPlayer", 255);
Set(main, null, "gameMenu", false); Set(main, null, "gamePaused", false);
Call(T("Lang"), null, "InitializeLegacyLocalization");
Set(main, null, "ActiveWorldFileData", Activator.CreateInstance(T("IO.WorldFileData"))!);
Set(main, null, "GameMode", 0); Set(main, null, "rand", Activator.CreateInstance(randomType, [1458])!);
Call(main, null, "Initialize_TileAndNPCData1"); Call(main, null, "Initialize_TileAndNPCData2");
Call(T("ObjectData.TileObjectData"), null, "Initialize");
Type vectorType = playerType.GetField("position", flags)!.FieldType;
object V(float x, float y) => Activator.CreateInstance(vectorType, [x, y])!;
Array Objects(Type type, int count)
{ Array result = Array.CreateInstance(type, count); for (int i = 0; i < count; i++) result.SetValue(Activator.CreateInstance(type), i); return result; }
Array players = Objects(playerType, 256), npcs = Objects(T("NPC"), 201), projectiles = Objects(T("Projectile"), 1001);
Set(main, null, "player", players); Set(main, null, "npc", npcs); Set(main, null, "projectile", projectiles);
Array worldItems = Objects(T("WorldItem"), 401); Set(main, null, "item", worldItems);
Array dust = Objects(T("Dust"), 6001); Set(main, null, "dust", dust);
Set(main, null, "screenWidth", 1280); Set(main, null, "screenHeight", 720); Set(main, null, "screenPosition", V(0, 0));
Type viewType = T("Graphics.SpriteViewMatrix"); object view = Activator.CreateInstance(viewType, [null])!;
Type viewportType = viewType.GetMethod("SetViewportOverride")!.GetParameters()[0].ParameterType;
Call(viewType, view, "SetViewportOverride", Activator.CreateInstance(viewportType, [0, 0, 1280, 720])!); Set(main, null, "GameViewMatrix", view);
Set(main, null, "instance", System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(main));
Set(main, null, "worldSurface", 15d); Set(main, null, "rockLayer", 40d);
Array tiles = Array.CreateInstance(tileType, 80, 100); Set(main, null, "tile", tiles); Set(main, null, "maxTilesX", 80); Set(main, null, "maxTilesY", 100);
void ClearTiles() { for (int x = 0; x < 80; x++) for (int y = 0; y < 100; y++) tiles.SetValue(Activator.CreateInstance(tileType), x, y); }
object Item(int type, int stack)
{ object item = Activator.CreateInstance(itemType)!; Call(itemType, item, "SetDefaults", type, null); Set(itemType, item, "stack", stack); return item; }
object Rng() => new { cursor = R(Read(main, null, "rand"), "inext"), state = ((int[])R(Read(main, null, "rand"), "SeedArray")).ToArray() };
int Peek()
{
    object source = Read(main, null, "rand"), clone = Activator.CreateInstance(randomType, [0])!;
    Set(randomType, clone, "inext", R(source, "inext")); Set(randomType, clone, "SeedArray", ((int[])R(source, "SeedArray")).ToArray());
    return (int)Call(randomType, clone, "Next")!;
}
object Cell(int x, int y)
{
    object tile = tiles.GetValue(x, y)!;
    return new { type = R(tile, "type"), active = Call(tileType, tile, "active"), actuator = Call(tileType, tile, "actuator"),
        inactive = Call(tileType, tile, "inActive"), frameX = R(tile, "frameX"), frameY = R(tile, "frameY"),
        red = Call(tileType, tile, "wire"), blue = Call(tileType, tile, "wire2"), green = Call(tileType, tile, "wire3"), yellow = Call(tileType, tile, "wire4") };
}
object[] Drops() => Enumerable.Range(0, 400).Select(i => worldItems.GetValue(i)!).Where(w => (bool)R(w, "active")).Select(w => (object)new
{ type = R(w, "type"), stack = R(w, "stack"), x = R(R(w, "position"), "X"), y = R(R(w, "position"), "Y"),
    vx = R(R(w, "velocity"), "X"), vy = R(R(w, "velocity"), "Y"), width = R(w, "width"), height = R(w, "height"),
    owner = R(w, "playerIndexTheItemIsReservedFor"), keep = R(w, "timeToKeepReservation"),
    enemyDelay=R(w,"enemyGrabDelayTime"),grabPlayer=R(w,"grabDelayPlayer"),grabTime=R(w,"grabDelayTime") }).ToArray();

// Only this developer probe emits an implementation of the original socket interface to record original bytes.
Type messageBuffer = T("MessageBuffer"), remoteClient = T("RemoteClient");
Array buffers = Objects(messageBuffer, 257), clients = Objects(remoteClient, 257);
Set(T("NetMessage"), null, "buffer", buffers); Set(T("Netplay"), null, "Clients", clients);
Type socketInterface = T("Net.Sockets.ISocket");
TypeBuilder socketBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("WiringToolCaptureSocket"), AssemblyBuilderAccess.Run)
    .DefineDynamicModule("capture").DefineType("CaptureSocket", TypeAttributes.Public | TypeAttributes.Sealed, null, [socketInterface]);
foreach (MethodInfo method in socketInterface.GetMethods())
{
    MethodBuilder body = socketBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
    ILGenerator il = body.GetILGenerator();
    if (method.Name == "AsyncSend") { for (int arg = 1; arg <= 5; arg++) il.Emit(OpCodes.Ldarg, arg); il.Emit(OpCodes.Call, typeof(ToolWireRecorder).GetMethod("Capture")!); }
    else if (method.ReturnType == typeof(bool)) il.Emit(method.Name == "IsConnected" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
    else if (method.ReturnType != typeof(void)) il.Emit(OpCodes.Ldnull);
    il.Emit(OpCodes.Ret); socketBuilder.DefineMethodOverride(body, method);
}
object socket = Activator.CreateInstance(socketBuilder.CreateType()!)!;
Set(remoteClient, clients.GetValue(1), "Socket", socket); Set(remoteClient, clients.GetValue(1), "State", 10); Set(remoteClient, clients.GetValue(1), "Id", 1);
Set(messageBuffer, buffers.GetValue(1), "broadcast", true);
Set(remoteClient, clients.GetValue(256), "Socket", socket); Set(remoteClient, clients.GetValue(256), "State", 10); Set(remoteClient, clients.GetValue(256), "Id", 256);
object connection = Read(T("Netplay"), null, "Connection"); Set(connection.GetType(), connection, "Socket", socket); Set(connection.GetType(), connection, "IsActive", true);

var rows = new List<object>();
var errors = new List<object>();
var profiles = new List<ToolScene>();
foreach (int tool in new[] { 509, 850, 851, 3612, 510, 849 }) profiles.Add(new(tool, "funded", Actuator: tool == 510, Inactive: tool == 510));
profiles.AddRange([
    new(509,"unfunded",WireStack:0,ExtraWireStack:0), new(509,"existing-red",Wires:1),
    new(509,"slot57",WireSlot:57,ExtraWireStack:0),new(509,"cursor58-excluded",WireSlot:58,ExtraWireStack:0),
    new(509,"reachX-inclusive",TargetX:46),new(509,"reachX-outside",TargetX:47),
    new(509,"reachY-inclusive",TargetY:45),new(509,"reachY-outside",TargetY:46),
    new(849,"last-selected",SelectedStack:1),new(849,"already-actuator",Actuator:true),
    new(849,"reachX-inclusive",TargetX:26),new(849,"reachX-outside",TargetX:27),
    new(510,"all-five-priority",Wires:15,Actuator:true,Inactive:true),
    new(510,"all-four-yellow-first",Wires:15),new(510,"three-green-first",Wires:7),
    new(510,"two-blue-first",Wires:3),new(510,"red-last",Wires:1),new(510,"empty-noop"),
    new(510,"visible-full-dust-pool",Actuator:true,Inactive:true,DustFull:true),
    new(509,"temple87-beforeGolem",Wall:87),new(509,"temple87-afterGolem",Wall:87,Golem:true),
    new(509,"wall350-always",Wall:350,Golem:true),new(509,"dual-temple-window92",Wall:92,Dual:true),
    new(509,"ordinary-window92",Wall:92),new(509,"dual-window92-above",Wall:92,Dual:true,TargetY:10),
    new(509,"busy-time2",Time:2),new(509,"no-control",Control:false),new(509,"noBuilding-is-not-wiring-guard",NoBuilding:true)
]);
foreach (ToolScene profile in profiles)
{
    int tool = profile.Tool;
    try
    {
        ClearTiles(); Set(main, null, "netMode", 1); Set(main, null, "myPlayer", 0); Set(main, null, "rand", Activator.CreateInstance(randomType, [1458])!);
        Set(main,null,"dualDungeonsSeed",profile.Dual);Set(T("NPC"),null,"downedGolemBoss",profile.Golem);
        for(int i=0;i<6001;i++)Set(T("Dust"),dust.GetValue(i),"active",profile.DustFull && i<6000);
        object player = Activator.CreateInstance(playerType)!; players.SetValue(player, 0);
        Set(playerType, player, "whoAmI", 0); Set(playerType, player, "active", true); Set(playerType, player, "position", V(320, 320));
        Set(playerType, player, "width", 20); Set(playerType, player, "height", 42); Set(playerType, player, "statLife", 400); Set(playerType, player, "statLifeMax2", 400);
        object selection = R(player, "selectedItemState"); Call(selection.GetType(), selection, "Select", 0); Call(selection.GetType(), selection, "Update"); Set(playerType, player, "selectedItemState", selection);
        Set(playerType, player, "controlUseItem", profile.Control); Set(playerType, player, "releaseUseItem", true);
        Set(playerType,player,"itemTime",profile.Time);Set(playerType,player,"itemTimeMax",profile.Time);Set(playerType,player,"noBuilding",profile.NoBuilding);
        Set(playerType, player, "IsAllowedToHoldItems", true); Set(playerType, null, "tileTargetX", profile.TargetX); Set(playerType, null, "tileTargetY", profile.TargetY);
        Array inventory = (Array)R(player, "inventory"); for (int slot = 0; slot < 59; slot++) inventory.SetValue(Item(0, 0), slot);
        inventory.SetValue(Item(tool, tool == 849 ? profile.SelectedStack : 1), 0);
        inventory.SetValue(Item(530, profile.WireStack), profile.WireSlot); if(profile.ExtraWireStack>0)inventory.SetValue(Item(530, profile.ExtraWireStack), 3);
        object target = tiles.GetValue(profile.TargetX, profile.TargetY)!; Set(tileType,target,"wall",profile.Wall);
        if(profile.Actuator)Call(tileType,target,"actuator",true);if(profile.Inactive)Call(tileType,target,"inActive",true);
        for(int i=0;i<4;i++)if((profile.Wires&(1<<i))!=0)Call(tileType,target,i==0?"wire":$"wire{i+1}",true);
        object Snapshot() => new { tile = Cell(profile.TargetX,profile.TargetY), inventory = Enumerable.Range(0, 59).Where(i => (int)R(inventory.GetValue(i)!, "type") != 0)
            .Select(i => new { slot = i, type = R(inventory.GetValue(i)!, "type"), stack = R(inventory.GetValue(i)!, "stack") }).ToArray(),
            time = R(player, "itemTime"), timeMax = R(player, "itemTimeMax"), animation = R(player, "itemAnimation"), animationMax = R(player, "itemAnimationMax"),
            release=R(player,"releaseUseItem"),revolver=R(player,"revolverCritChanceBonus"),pound=R(player,"poundRelease"),channel=R(player,"channel"),reuse=R(player,"reuseDelay"),
            lastSuccess=R(player,"lastItemUseAttemptSuccess"),noItems=R(player,"noItems"),cced=R(player,"CCed"),allowed=R(player,"IsAllowedToHoldItems"),justDropped=R(player,"JustDroppedAnItem"),
            toolTime = R(player, "toolTime"), random = Rng(), next = Peek(), drops = Drops() };
        ToolWireRecorder.Reset(); ToolWireRecorder.AtSend = frame => new { packet = frame[2], state = Snapshot() };
        object selectedItem=inventory.GetValue(0)!;
        var metadata=new{useTime=R(selectedItem,"useTime"),useAnimation=R(selectedItem,"useAnimation"),tileBoost=R(selectedItem,"tileBoost"),
            consumable=R(selectedItem,"consumable"),autoReuse=R(selectedItem,"autoReuse"),useStyle=R(selectedItem,"useStyle"),shoot=R(selectedItem,"shoot"),channel=R(selectedItem,"channel")};
        object before = Snapshot(); Call(playerType, player, "ItemCheck"); object after = Snapshot();
        rows.Add(new { tool, profile, metadata, sourceCall = "Player.ItemCheck", netMode = 1, myPlayer = 0, dedServ = true, before, after,
            frames = ToolWireRecorder.Frames.Select(Convert.ToHexString).ToArray(), observations = ToolWireRecorder.Observations.ToArray() });
    }
    catch (Exception exception) { errors.Add(new { tool, profile.Name, error = exception.ToString() }); }
}
var mutations=new List<object>();
void ServerReset()
{
    ClearTiles();Set(main,null,"netMode",2);Set(main,null,"myPlayer",255);Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);
    worldItems=Objects(T("WorldItem"),401);Set(main,null,"item",worldItems);for(int i=0;i<6001;i++)Set(T("Dust"),dust.GetValue(i),"active",false);
    ToolWireRecorder.Reset();ToolWireRecorder.AtSend=frame=>new{packet=frame[2],random=Rng(),next=Peek(),drops=Drops()};
}
foreach(int color in new[]{1,2,3,4})
{
    try
    {
        ServerReset();object tile=tiles.GetValue(20,20)!;
        int neighborColor=color==4?1:color+1;Call(tileType,tile,neighborColor==1?"wire":$"wire{neighborColor}",true);
        object before=Cell(20,20);string suffix=color==1?"":color.ToString();
        bool placed=(bool)Call(T("WorldGen"),null,"PlaceWire"+suffix,20,20)!;object installed=Cell(20,20);
        bool duplicate=(bool)Call(T("WorldGen"),null,"PlaceWire"+suffix,20,20)!;
        using var assign=(IDisposable)Call(itemType,null,"DefaultAssignNewItemsToPlayer",0)!;
        bool removed=(bool)Call(T("WorldGen"),null,"KillWire"+suffix,20,20)!;object after=Cell(20,20);
        bool absent=(bool)Call(T("WorldGen"),null,"KillWire"+suffix,20,20)!;
        mutations.Add(new{kind="wire-cycle",color,before,installed,after,placed,duplicate,removed,absent,random=Rng(),next=Peek(),drops=Drops(),frames=ToolWireRecorder.Frames.Select(Convert.ToHexString).ToArray(),observations=ToolWireRecorder.Observations.ToArray()});
    }
    catch(Exception exception){errors.Add(new{mutation="wire-cycle",color,error=exception.ToString()});}
}
foreach(bool active in new[]{false,true})
{
    try
    {
        ServerReset();object tile=tiles.GetValue(20,20)!;Set(tileType,tile,"type",active?(ushort)1:(ushort)0);Call(tileType,tile,"active",active);Call(tileType,tile,"inActive",true);
        object before=Cell(20,20);bool placed=(bool)Call(T("WorldGen"),null,"PlaceActuator",20,20)!;object installed=Cell(20,20);
        bool duplicate=(bool)Call(T("WorldGen"),null,"PlaceActuator",20,20)!;
        using var assign=(IDisposable)Call(itemType,null,"DefaultAssignNewItemsToPlayer",0)!;
        bool removed=(bool)Call(T("WorldGen"),null,"KillActuator",20,20)!;object after=Cell(20,20);
        bool absent=(bool)Call(T("WorldGen"),null,"KillActuator",20,20)!;
        mutations.Add(new{kind="actuator-cycle",active,before,installed,after,placed,duplicate,removed,absent,random=Rng(),next=Peek(),drops=Drops(),frames=ToolWireRecorder.Frames.Select(Convert.ToHexString).ToArray(),observations=ToolWireRecorder.Observations.ToArray()});
    }
    catch(Exception exception){errors.Add(new{mutation="actuator-cycle",active,error=exception.ToString()});}
}
var framing=new List<object>();
foreach(var scene in new[]{(attachment:136,seed:1458,supportX:21,triggerX:20),(attachment:4,seed:1458,supportX:21,triggerX:20),
    (attachment:136,seed:7,supportX:20,triggerX:18),(attachment:4,seed:7,supportX:20,triggerX:18)})
{
    int attachment=scene.attachment;
    try
    {
        ServerReset();Set(main,null,"rand",Activator.CreateInstance(randomType,[scene.seed])!);
        object stone=tiles.GetValue(scene.supportX,21)!;Set(tileType,stone,"type",(ushort)1);Call(tileType,stone,"active",true);Call(tileType,stone,"actuator",true);Call(tileType,stone,"wire",true);
        object attached=tiles.GetValue(scene.supportX,20)!;Set(tileType,attached,"type",(ushort)attachment);Call(tileType,attached,"active",true);
        object trigger=tiles.GetValue(scene.triggerX,21)!;Set(tileType,trigger,"type",(ushort)136);Set(tileType,trigger,"wall",(ushort)1);Call(tileType,trigger,"active",true);Call(tileType,trigger,"wire",true);
        for(int x=scene.triggerX+1;x<scene.supportX;x++)Call(tileType,tiles.GetValue(x,21),"wire",true);
        Call(T("Wiring"),null,"Initialize");Call(T("Wiring"),null,"ClearAll");
        var before=new{stone=Cell(scene.supportX,21),attachment=Cell(scene.supportX,20),trigger=Cell(scene.triggerX,21),random=Rng(),next=Peek()};
        Call(T("Wiring"),null,"SetCurrentUser",0);
        Call(T("Wiring"),null,"HitSwitch",scene.triggerX,21);
        Call(T("Wiring"),null,"SetCurrentUser",-1);
        var after=new{stone=Cell(scene.supportX,21),attachment=Cell(scene.supportX,20),trigger=Cell(scene.triggerX,21),random=Rng(),next=Peek(),drops=Drops()};
        framing.Add(new{attachment,scene.seed,scene.supportX,scene.triggerX,sourceCall="MessageBuffer59 user scope -> Wiring.HitSwitch -> DeActive -> SquareTileFrame",before,after,frames=ToolWireRecorder.Frames.Select(Convert.ToHexString).ToArray(),observations=ToolWireRecorder.Observations.ToArray()});
    }
    catch(Exception exception){errors.Add(new{mutation="framing",attachment,error=exception.ToString()});}
}
File.WriteAllText(outputPath, JsonSerializer.Serialize(new { source = "Unmodified official Linux edition under Windows CoreCLR", sha256 = sourceHash, rows, mutations,framing,errors }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"tools={rows.Count}, errors={errors.Count}");
if (errors.Count != 0) throw new InvalidOperationException("Reference capture has failures; inspect JSON errors.");

public static class ToolWireRecorder
{
    public static readonly List<byte[]> Frames = [];
    public static readonly List<object> Observations = [];
    public static Func<byte[], object>? AtSend;
    public static void Reset() { Frames.Clear(); Observations.Clear(); }
    public static void Capture(byte[] bytes, int start, int count, object? callback, object? state)
    {
        byte[] frame = bytes.AsSpan(start, count).ToArray(); Frames.Add(frame); if (AtSend is not null) Observations.Add(AtSend(frame));
        if (callback is Delegate complete) complete.DynamicInvoke(state);
    }
}

public sealed record ToolScene(int Tool,string Name,int WireSlot=2,int WireStack=1,int ExtraWireStack=2,int SelectedStack=2,
    int TargetX=20,int TargetY=20,int Wires=0,bool Actuator=false,bool Inactive=false,int Time=0,bool Control=true,
    ushort Wall=0,bool Dual=false,bool Golem=false,bool NoBuilding=false,bool DustFull=false);
