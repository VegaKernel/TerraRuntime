using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 2) throw new ArgumentException("LeaseProbe <official-linux-edition-TerrariaServer.exe> <output.json>");
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
        .Zip(values, (type, value) => value is null ? !type.IsValueType || Nullable.GetUnderlyingType(type) is not null : type.IsInstanceOfType(value)).All(v => v)).Invoke(instance, values);
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
Set(main,null,"maxSectionsX",1);Set(main,null,"maxSectionsY",1);
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
// Only this developer probe emits an implementation of the original socket interface to record original bytes.
Type messageBuffer = T("MessageBuffer"), remoteClient = T("RemoteClient");
Array buffers = Objects(messageBuffer, 257), clients = Objects(remoteClient, 257);
Set(T("NetMessage"), null, "buffer", buffers); Set(T("Netplay"), null, "Clients", clients);
Type socketInterface = T("Net.Sockets.ISocket");
TypeBuilder socketBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("WorldItemLeaseCaptureSocket"), AssemblyBuilderAccess.Run)
    .DefineDynamicModule("capture").DefineType("CaptureSocket", TypeAttributes.Public | TypeAttributes.Sealed, null, [socketInterface]);
FieldBuilder recipientField = socketBuilder.DefineField("Recipient", typeof(int), FieldAttributes.Public);
foreach (MethodInfo method in socketInterface.GetMethods())
{
    MethodBuilder body = socketBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
    ILGenerator il = body.GetILGenerator();
    if (method.Name == "AsyncSend") { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, recipientField); for (int arg = 1; arg <= 5; arg++) il.Emit(OpCodes.Ldarg, arg); il.Emit(OpCodes.Call, typeof(LeaseWireRecorder).GetMethod("Capture")!); }
    else if (method.ReturnType == typeof(bool)) il.Emit(method.Name == "IsConnected" ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
    else if (method.ReturnType != typeof(void)) il.Emit(OpCodes.Ldnull);
    il.Emit(OpCodes.Ret); socketBuilder.DefineMethodOverride(body, method);
}
Type socketType = socketBuilder.CreateType()!;
foreach(int recipient in new[]{0,1,2,7,256})
{
    object socket = Activator.CreateInstance(socketType)!; socketType.GetField("Recipient")!.SetValue(socket,recipient);
    Set(remoteClient,clients.GetValue(recipient),"Socket",socket);Set(remoteClient,clients.GetValue(recipient),"State",10);Set(remoteClient,clients.GetValue(recipient),"Id",recipient);
    Set(messageBuffer,buffers.GetValue(recipient),"broadcast",recipient==2);
    ((bool[,])R(clients.GetValue(recipient)!,"TileSections"))[0,0]=true;
}
object connection=Read(T("Netplay"),null,"Connection");Set(connection.GetType(),connection,"Socket",R(clients.GetValue(256)!,"Socket"));Set(connection.GetType(),connection,"IsActive",true);

// These are scene setup and calls into the original methods, not reimplementations of those methods.
Type worldItemType=T("WorldItem"), ownershipType=T("NewItemOwnership");
var rows=new List<object>();var errors=new List<object>();var steps=new List<object>();
object W()=>worldItems.GetValue(0)!;
object ItemSnapshot(int slot)
{
    object item=worldItems.GetValue(slot)!;
    return new { owner=R(item,"playerIndexTheItemIsReservedFor"),keep=R(item,"timeToKeepReservation"),age=R(item,"timeSinceTheItemHasBeenReservedForSomeone"),
        active=R(item,"active"),type=R(item,"type"),stack=R(item,"stack"),instanced=R(item,"instanced"),spawnAge=R(item,"timeSinceItemSpawned"),
        grabPlayer=R(item,"grabDelayPlayer"),grabTime=R(item,"grabDelayTime"),enemyDelay=R(item,"enemyGrabDelayTime"),
        reuse=((int[])Read(main,null,"timeItemSlotCannotBeReusedFor"))[slot],x=R(R(item,"position"),"X"),y=R(R(item,"position"),"Y"),
        vx=R(R(item,"velocity"),"X"),vy=R(R(item,"velocity"),"Y"),prefix=R(item,"prefix"),width=R(item,"width"),height=R(item,"height"),random=Rng(),next=Peek() };
}
object Snapshot()=>ItemSnapshot(0);
void Player(int index,bool active,float x=640)
{
    object player=Activator.CreateInstance(playerType)!;players.SetValue(player,index);
    Set(playerType,player,"whoAmI",index);Set(playerType,player,"active",active);Set(playerType,player,"position",V(x,640));
    Set(playerType,player,"width",20);Set(playerType,player,"height",42);Set(playerType,player,"statLife",400);Set(playerType,player,"statLifeMax2",400);
    Array inventory=(Array)R(player,"inventory");for(int i=0;i<59;i++)inventory.SetValue(Item(0,0),i);
}
void Reset(int netMode=2)
{
    ClearTiles();Set(main,null,"netMode",netMode);Set(main,null,"myPlayer",netMode==1?0:255);Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);
    Set(main,null,"netPlayCounter",0);Set(main,null,"maxNetPlayers",0);Set(main,null,"timeItemSlotCannotBeReusedFor",new int[401]);
    worldItems=Objects(worldItemType,401);Set(main,null,"item",worldItems);
    object world=Activator.CreateInstance(worldItemType,[Item(530,1)])!;worldItems.SetValue(world,0);Set(worldItemType,world,"whoAmI",0);Set(worldItemType,world,"position",V(640,640));
    for(int i=0;i<256;i++)Player(i,false);Player(0,true);Player(1,false);Player(2,false);
    for(int i=0;i<3;i++)Set(messageBuffer,buffers.GetValue(i),"broadcast",i==2);
    ((System.Collections.IList)Read(T("GameContent.EmergencyStacking"),null,"PendingTransfers")).Clear();
    steps.Clear();LeaseWireRecorder.Reset();LeaseWireRecorder.AtSend=Snapshot;
}
void Step(string call,Action invoke,object? input=null)
{
    object before=Snapshot();LeaseWireRecorder.Reset();invoke();object after=Snapshot();
    steps.Add(new{call,input,before,after,packets=LeaseWireRecorder.Entries.ToArray()});
}
void Scene(string name,Action run,int netMode=2)
{
    try{Reset(netMode);run();rows.Add(new{name,netMode,myPlayer=netMode==1?0:255,steps=steps.ToArray()});}
    catch(Exception exception){errors.Add(new{name,error=exception.ToString()});}
}
void Send(int message,int number2=0,int remote=-1,int ignore=-1)=>Call(T("NetMessage"),null,"SendData",message,remote,ignore,null,0,(float)number2,0f,0f,0,0,0);
void Receive(byte message,int who=0,bool force=false,byte owner=0,int keep=0)
{
    using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))
    {
        writer.Write(message);writer.Write((short)0);
        if(message==39)writer.Write(force);
        else if(message==22){writer.Write(owner);writer.Write7BitEncodedInt(keep);writer.Write((byte)0);writer.Write7BitEncodedInt(0);writer.Write(640f);writer.Write(640f);}
    }
    byte[] bytes=stream.ToArray();object buffer=buffers.GetValue(who)!;Set(messageBuffer,buffer,"whoAmI",who);
    bytes.CopyTo((byte[])R(buffer,"readBuffer"),0);object?[] values=[0,bytes.Length,0];messageBuffer.GetMethod("GetData",flags)!.Invoke(buffer,values);
}
void Reserve(int owner,int offer=15)=>Call(worldItemType,W(),"ReserveFor",owner,offer);
void Find(bool force=false)=>Call(worldItemType,W(),"FindOwner",force);
void Tick()=>Call(worldItemType,W(),"UpdateItem",0);
void Spawn(int mode,bool noBroadcast=false)
{
    Call(worldItemType,W(),"TurnToAir");
    using var assign=mode==1?(IDisposable)Call(itemType,null,"DefaultAssignNewItemsToPlayer",0)!:null;
    Call(itemType,null,"NewItem",null,640,640,16,16,530,1,noBroadcast,0,Enum.ToObject(ownershipType,mode),null,null);
}

Scene("reserve-default15",()=>{Step("WorldItem.ReserveFor(0)",()=>Reserve(0));Step("join SendData22(default0)",()=>Send(22));});
Scene("reserve-configured-raw37",()=>{Set(worldItemType,W(),"timeToKeepReservation",37);Step("WorldItem.ReserveFor(0,15)",()=>Reserve(0));Step("WorldItem.UpdateItem",Tick);});
Scene("apply-reserve-local100",()=>Step("WorldItem.ApplySpawnOwnership(ReserveForLocalPlayer,0)",()=>Call(worldItemType,W(),"ApplySpawnOwnership",Enum.ToObject(ownershipType,1),0)));
Scene("newitem-local100-join0",()=>{Step("Item.NewItem ReserveForLocalPlayer",()=>Spawn(1));Step("join SendData21(default mode0)",()=>Send(21));Step("join SendData22(default hold0)",()=>Send(22));});
Scene("newitem-none-default15",()=>Step("Item.NewItem None",()=>Spawn(0)));
Scene("find-disconnected-owner",()=>{Reserve(0,100);Player(0,false);Player(1,true);Step("WorldItem.FindOwner(false)",()=>Find());});
Scene("find-disconnected-no-candidate",()=>{Reserve(0,100);Player(0,false);Step("WorldItem.FindOwner(false)",()=>Find());});
Scene("find-force-disconnected-positive",()=>{Reserve(0,100);Player(0,false);Set(worldItemType,W(),"timeToKeepReservation",37);Step("WorldItem.FindOwner(true)",()=>Find(true));});
Scene("find-positive-blocked-force-handshake",()=>{Reserve(0);Set(worldItemType,W(),"timeToKeepReservation",37);Player(1,true);Player(0,true,960);Step("WorldItem.FindOwner(false)",()=>Find());Step("WorldItem.FindOwner(true)",()=>Find(true));Step("WorldItem.FindOwner(true) pending repeat",()=>Find(true));});
Scene("find-instanced-positive-force",()=>{Set(worldItemType,W(),"timeToKeepReservation",37);Set(worldItemType,W(),"instanced",true);Step("WorldItem.FindOwner(false)",()=>Find());Step("WorldItem.FindOwner(true)",()=>Find(true));});
Scene("update-positive-timers",()=>{Set(worldItemType,W(),"timeToKeepReservation",3);Set(worldItemType,W(),"grabDelayTime",2);Set(worldItemType,W(),"enemyGrabDelayTime",4);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",13);for(int i=0;i<5;i++)Step("WorldItem.UpdateItem",Tick);});
Scene("update-inactive-hold",()=>{Set(worldItemType,W(),"timeToKeepReservation",3);Set(worldItemType,W(),"grabDelayTime",2);Set(worldItemType,W(),"enemyGrabDelayTime",4);Step("WorldItem.TurnToAir",()=>Call(worldItemType,W(),"TurnToAir"));Step("WorldItem.UpdateItem inactive",Tick);});
foreach(bool force in new[]{false,true})
{
    Scene("server39-positive37-"+force,()=>{Reserve(0);Set(messageBuffer,buffers.GetValue(0),"broadcast",true);Set(worldItemType,W(),"timeToKeepReservation",37);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",-1);Step("MessageBuffer.GetData39",()=>Receive(39,0,force),new{who=0,force});});
    Scene("server39-owner-transfer-"+force,()=>{Reserve(0);Set(messageBuffer,buffers.GetValue(0),"broadcast",true);Player(0,true,960);Player(1,true);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",-1);Step("MessageBuffer.GetData39",()=>Receive(39,0,force),new{who=0,force});Step("WorldItem.FindOwner(false) after ACK",()=>Find());});
}
Scene("server39-wrong-sender",()=>{Reserve(0);Set(worldItemType,W(),"timeToKeepReservation",37);Step("MessageBuffer.GetData39",()=>Receive(39,1,true),new{who=1,force=true});});
Scene("server39-self-owner",()=>{Reserve(0);Set(messageBuffer,buffers.GetValue(0),"broadcast",true);Step("MessageBuffer.GetData39",()=>Receive(39,0,false),new{who=0,force=false});});
Scene("join-positive-raw37-still-default0",()=>{Reserve(0);Set(worldItemType,W(),"timeToKeepReservation",37);Step("join SendData22(default0)",()=>Send(22));});
Scene("server22-ignored",()=>Step("MessageBuffer.GetData22",()=>Receive(22,0,owner:1,keep:100),new{who=0,owner=1,keep=100}));
Scene("client22-hold15-update-release",()=>{Step("MessageBuffer.GetData22",()=>Receive(22,256,owner:0,keep:15),new{owner=0,keep=15});Step("WorldItem.UpdateItem",Tick);Step("MessageBuffer.GetData39 received false",()=>Receive(39,256,false),new{force=false});},1);
Scene("server-periodic-unowned",()=>{Player(0,false);for(int i=0;i<7;i++)Step("Main.UpdateServer",()=>Call(main,null,"UpdateServer"));});
Scene("server-periodic-unowned-candidate-at6",()=>{Player(0,false);for(int i=0;i<7;i++){if(i==5)Player(1,true);Step("Main.UpdateServer",()=>Call(main,null,"UpdateServer"));}});
Scene("server-periodic300-handoff",()=>{Reserve(0);Player(0,true,960);Player(1,true);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",298);for(int i=0;i<4;i++)Step("Main.UpdateServer",()=>Call(main,null,"UpdateServer"));});
Scene("server-periodic-disconnect-before-offer100",()=>{Reserve(0,100);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",3);Player(0,false);Player(1,true);Step("Main.UpdateServer",()=>Call(main,null,"UpdateServer"));});
Scene("instanced-inactive-reuse3",()=>
{
    Step("Item.NewItem noBroadcast",()=>Spawn(0,true));
    Type predicateType=typeof(Predicate<>).MakeGenericType(playerType);Delegate predicate=Delegate.CreateDelegate(predicateType,typeof(LeaseWireRecorder).GetMethod("Always")!.MakeGenericMethod(playerType));
    Step("WorldItem.MakeInstanced lifetime3",()=>Call(worldItemType,W(),"MakeInstanced",predicate,3));
    for(int i=0;i<4;i++)Step("WorldItem.UpdateItem inactive reuse clock",Tick);
    Step("Item.NewItem same slot fresh local100",()=>Spawn(1));
});
Scene("instanced-default-reuse54000",()=>
{
    Step("Item.NewItem noBroadcast",()=>Spawn(0,true));
    Type predicateType=typeof(Predicate<>).MakeGenericType(playerType);Delegate predicate=Delegate.CreateDelegate(predicateType,typeof(LeaseWireRecorder).GetMethod("Always")!.MakeGenericMethod(playerType));
    Step("WorldItem.MakeInstanced default lifetime",()=>Call(worldItemType,W(),"MakeInstanced",predicate,0));Step("WorldItem.UpdateItem inactive reuse clock",Tick);
});
Scene("reuse-slot-raw39-has-no-generation",()=>
{
    Reserve(0);Set(worldItemType,W(),"timeToKeepReservation",37);Set(worldItemType,W(),"timeSinceTheItemHasBeenReservedForSomeone",55);
    Step("WorldItem.TurnToAir old item",()=>Call(worldItemType,W(),"TurnToAir"));Step("Item.NewItem same slot fresh local100",()=>Spawn(1));
    Step("MessageBuffer.GetData39 stale slot/same owner",()=>Receive(39,0,true),new{who=0,force=true});
});
foreach(int mode in new[]{2,3})Scene("apply-grab-delay-"+mode,()=>Step("WorldItem.ApplySpawnOwnership",()=>Call(worldItemType,W(),"ApplySpawnOwnership",Enum.ToObject(ownershipType,mode),0),new{mode}));

object PairSnapshot()=>new{primary=ItemSnapshot(0),peer=ItemSnapshot(1),pending=((System.Collections.IList)Read(T("GameContent.EmergencyStacking"),null,"PendingTransfers")).Count};
void PairStep(string call,Action invoke)
{
    object before=PairSnapshot();LeaseWireRecorder.Reset();invoke();object after=PairSnapshot();steps.Add(new{call,before,after,packets=LeaseWireRecorder.Entries.ToArray()});
}
void SetupPendingPair(bool unownedIsSource,bool released=false)
{
    // Configuring the original private transfer list is a fixture preimage, not a transfer algorithm.
    for(int slot=0;slot<2;slot++)
    {
        object item=Activator.CreateInstance(worldItemType,[Item(2,10)])!;worldItems.SetValue(item,slot);
        Set(worldItemType,item,"whoAmI",slot);Set(worldItemType,item,"position",V(released?640:0,released?640:0));
        Set(worldItemType,item,"playerIndexTheItemIsReservedFor",slot==0||released?255:7);
        Set(worldItemType,item,"timeToKeepReservation",slot==0&&!released?37:0);
        Set(worldItemType,item,"timeSinceTheItemHasBeenReservedForSomeone",slot==0&&!released?17:0);
        Set(worldItemType,item,"timeSinceItemSpawned",(slot==0)==unownedIsSource?200:100);
    }
    if(!released){Player(0,false);Player(7,true,10000);}
    Type emergency=T("GameContent.EmergencyStacking"),transferType=emergency.GetNestedType("Transfer",flags)!;
    object transfer=Activator.CreateInstance(transferType)!;
    Set(transferType,transfer,"src",worldItems.GetValue(unownedIsSource?0:1));Set(transferType,transfer,"dst",worldItems.GetValue(unownedIsSource?1:0));
    ((System.Collections.IList)Read(emergency,null,"PendingTransfers")).Add(transfer);
    LeaseWireRecorder.AtSend=PairSnapshot;
}
foreach(bool unownedIsSource in new[]{true,false})Scene("pending-force-unowned-positive37-"+(unownedIsSource?"source":"destination"),()=>
{
    SetupPendingPair(unownedIsSource);
    PairStep("EmergencyStacking.RequestOwnershipReleaseForPendingTransfers",()=>Call(T("GameContent.EmergencyStacking"),null,"RequestOwnershipReleaseForPendingTransfers"));
});
Scene("pending-releasable-age1",()=>
{
    SetupPendingPair(true,released:true);
    for(int i=0;i<6;i++)PairStep("Main.UpdateServer",()=>Call(main,null,"UpdateServer"));
});

File.WriteAllText(outputPath,JsonSerializer.Serialize(new{source="Unmodified official Linux edition under Windows CoreCLR",sha256=sourceHash,rows,errors},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"lease profiles={rows.Count}, errors={errors.Count}");if(errors.Count!=0)throw new InvalidOperationException("Inspect independent lease capture errors.");

public static class LeaseWireRecorder
{
    public static readonly List<object> Entries=[];
    public static Func<object>? AtSend;
    public static void Reset()=>Entries.Clear();
    public static bool Always<T>(T value)=>true;
    public static void Capture(int recipient,byte[] bytes,int start,int count,object? callback,object? state)
    {
        byte[] frame=bytes.AsSpan(start,count).ToArray();Entries.Add(new{recipient,hex=Convert.ToHexString(frame),atSend=AtSend?.Invoke()});
        if(callback is Delegate complete)complete.DynamicInvoke(state);
    }
}
