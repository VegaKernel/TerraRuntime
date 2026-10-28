using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Reference-only reflection harness: no TerraRuntime shipping assembly or decompiled method body is used.
if (args.Length != 2)
    throw new ArgumentException("Usage: Probe <official-linux-edition-TerrariaServer.exe> <output.json>");
if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("This capture recipe is pinned to Windows CoreCLR running the Linux edition.");
string originalPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);
string outputDirectory = Path.GetDirectoryName(outputPath)!;
if (string.Equals(originalPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(Path.GetFileName(outputPath), "timer-save-load.json", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Choose a distinct output filename; timer-save-load.json is the sidecar output.");
const string originalSha256 = "4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034";
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath))) != originalSha256)
    throw new InvalidDataException("Expected the pinned unmodified official TerrariaServer 1.4.5.8 Linux-edition binary.");
Directory.CreateDirectory(outputDirectory);
string directory = Path.GetDirectoryName(originalPath)!;
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(directory, name.Name + ".dll");
    if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    Assembly? game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TerrariaServer");
    string? resource = game?.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
    if (resource is null) return null;
    using Stream stream = game!.GetManifestResourceStream(resource)!;
    return AssemblyLoadContext.Default.LoadFromStream(stream);
};
Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(originalPath);
const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
Type main=assembly.GetType("Terraria.Main")!, tileType=assembly.GetType("Terraria.Tile")!, wiring=assembly.GetType("Terraria.Wiring")!;
void Set(Type t,object? obj,string key,object value)=>t.GetField(key,flags)!.SetValue(obj,value);
object Get(Type t,object? obj,string key)=>t.GetField(key,flags)!.GetValue(obj)!;
object? Invoke(Type t,object? obj,string name,params object[] args)=>t.GetMethods(flags).Single(m=>m.Name==name && m.GetParameters().Length==args.Length && m.GetParameters().Select(p=>p.ParameterType).Zip(args,(p,a)=>p.IsInstanceOfType(a)).All(x=>x)).Invoke(obj,args);
Set(assembly.GetType("Terraria.Program")!,null,"SavePath",Path.Combine(outputDirectory,"save"));
Set(main,null,"dedServ",true); Set(main,null,"netMode",0); Set(main,null,"myPlayer",0);
Invoke(wiring,null,"Initialize");
Array tiles=Array.CreateInstance(tileType,80,100);
Set(main,null,"maxTilesX",80);Set(main,null,"maxTilesY",100);Set(main,null,"tile",tiles);
void Reset(){for(int x=0;x<80;x++)for(int y=0;y<100;y++)tiles.SetValue(Activator.CreateInstance(tileType),x,y);Invoke(wiring,null,"ClearAll");}
object Put(int x,int y,int type,int fx=0,int fy=0,int colors=1){object tile=tiles.GetValue(x,y)!;Set(tileType,tile,"type",(ushort)type);Set(tileType,tile,"frameX",(short)fx);Set(tileType,tile,"frameY",(short)fy);Invoke(tileType,tile,"active",true);for(int c=0;c<4;c++)if((colors&(1<<c))!=0)Invoke(tileType,tile,c==0?"wire":$"wire{c+1}",true);return tile;}
short Fx(int x,int y)=>(short)Get(tileType,tiles.GetValue(x,y),"frameX");
short Fy(int x,int y)=>(short)Get(tileType,tiles.GetValue(x,y),"frameY");
int Clock()=>((int[])Get(wiring,null,"_mechTime"))[0];
var rows=new List<object>();
for(int style=0;style<5;style++){
 Reset();Put(20,20,144,style*18);Put(21,20,4);Invoke(wiring,null,"HitSwitch",20,20);
 int interval=new[]{60,180,300,30,15}[style];var changes=new List<object>();
 short previous=Fx(21,20);for(int tick=1;tick<=interval*2+1;tick++){Invoke(wiring,null,"UpdateMech");short current=Fx(21,20);if(current!=previous){changes.Add(new{tick,frameX=current,clock=Clock()});previous=current;}}
 rows.Add(new{kind="timer",style,timerFrameY=Fy(20,20),changes});
}
Reset();Put(20,20,144);Put(21,20,4);Invoke(wiring,null,"HitSwitch",20,20);for(int i=0;i<17;i++)Invoke(wiring,null,"UpdateMech");Invoke(wiring,null,"HitSwitch",20,20);Invoke(wiring,null,"HitSwitch",20,20);var resumed=new List<object>();for(int tick=18;tick<=61;tick++){short prev=Fx(21,20);Invoke(wiring,null,"UpdateMech");if(Fx(21,20)!=prev)resumed.Add(new{tick,frameX=Fx(21,20)});}rows.Add(new{kind="timerOffOnSameTick",changes=resumed});
for(int colors=1;colors<=15;colors++){
 Reset();Put(20,20,136,colors:colors);Put(21,20,4,colors:colors);Invoke(wiring,null,"HitSwitch",20,20);rows.Add(new{kind="switch",colors,switchFrameY=Fy(20,20),torchFrameX=Fx(21,20)});Invoke(wiring,null,"HitSwitch",20,20);rows.Add(new{kind="switchRepeat",colors,switchFrameY=Fy(20,20),torchFrameX=Fx(21,20)});
}
for(int colors=1;colors<=15;colors++){
 Reset();for(int x=20;x<22;x++)for(int y=20;y<22;y++)Put(x,y,132,(x-20)*18,(y-20)*18,colors);Put(22,20,4,colors:colors);Invoke(wiring,null,"HitSwitch",21,21);rows.Add(new{kind="lever",colors,frames=new[]{Fx(20,20),Fx(21,20),Fx(20,21),Fx(21,21)},torchFrameX=Fx(22,20)});
}
Type collision=assembly.GetType("Terraria.Collision")!, vector=assembly.GetReferencedAssemblies().Select(Assembly.Load).Select(a=>a.GetType("Microsoft.Xna.Framework.Vector2")).First(t=>t!=null)!;
object V(float x,float y)=>Activator.CreateInstance(vector,[x,y])!;
Type playerType=assembly.GetType("Terraria.Player")!,randomType=assembly.GetType("Terraria.Utilities.UnifiedRandom")!;
Array players=Array.CreateInstance(playerType,256);for(int i=0;i<256;i++)players.SetValue(Activator.CreateInstance(playerType),i);Set(main,null,"player",players);Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);
foreach(var shape in new[]{(type:34,width:3,height:3,style:108),(type:93,width:1,height:3,style:0),(type:95,width:2,height:2,style:0)}){
 Reset();Put(20,20,136);for(int x=0;x<shape.width;x++)for(int y=0;y<shape.height;y++)Put(21+x,20+y,shape.type,shape.style+x*18,y*18);Invoke(wiring,null,"HitSwitch",20,20);var frames=new List<int>();for(int x=0;x<shape.width;x++)for(int y=0;y<shape.height;y++)frames.Add(Fx(21+x,20+y));rows.Add(new{kind="light",shape.type,shape.width,shape.height,shape.style,frames});
}
foreach(int type in new[]{1,226})foreach(bool down in new[]{false,true}){
 Reset();Set(main,null,"worldSurface",10d);Set(assembly.GetType("Terraria.NPC")!,null,"downedPlantBoss",down);((bool[])Get(main,null,"tileSolid"))[type]=true;Put(20,20,136);object tile=Put(21,20,type);Invoke(tileType,tile,"actuator",true);Invoke(wiring,null,"HitSwitch",20,20);rows.Add(new{kind="actuator",type,down,actuated=(bool)Invoke(tileType,tile,"inActive")!});
}
for(int style=0;style<8;style++)for(int actor=1;actor<=5;actor++){
 Reset();Put(20,20,135,0,style*18);Put(21,20,4);
 try{bool triggered=(bool)collision.GetMethod("SwitchTiles",flags)!.Invoke(null,[null,V(320,301),16,32,V(320,300),actor])!;rows.Add(new{kind="plate",style,actor,triggered,torchFrameX=Fx(21,20),plateActive=(bool)Invoke(tileType,tiles.GetValue(20,20),"active")!});}
 catch(Exception e){throw new Exception($"plate style{style} actor{actor}",e);}
}
// Additional scenes leave the original ninety-eight numeric rows unchanged.
var timerEdges = new List<object>();
foreach (string registration in new[] { "edgeOnly", "edgeThenInterior", "interiorThenEdge" })
{
    Reset(); Put(0,20,144); Put(20,20,144); Put(21,20,4);
    if (registration == "interiorThenEdge") Invoke(wiring,null,"HitSwitch",20,20);
    Invoke(wiring,null,"HitSwitch",0,20);
    if (registration == "edgeThenInterior") Invoke(wiring,null,"HitSwitch",20,20);
    var trace = new List<object>();
    foreach (int targetTick in new[] {1,2,60})
    {
        int previous = trace.Count == 0 ? 0 : trace.Count == 1 ? 1 : 2;
        for (int tick = previous; tick < targetTick; tick++) Invoke(wiring,null,"UpdateMech");
        trace.Add(new { tick=targetTick, count=(int)Get(wiring,null,"_numMechs"),
            edgeFrameY=Fy(0,20), interiorFrameY=Fy(20,20), torchFrameX=Fx(21,20) });
    }
    timerEdges.Add(new { registration, trace });
}
Reset();for(int x=20;x<22;x++)for(int y=20;y<22;y++)Put(x,y,411,(x-20)*18,(y-20)*18);
Invoke(wiring,null,"HitSwitch",20,20);for(int tick=0;tick<30;tick++)Invoke(wiring,null,"UpdateMech");
Invoke(wiring,null,"HitSwitch",20,20);short momentaryAt30=Fx(20,20);for(int tick=30;tick<60;tick++)Invoke(wiring,null,"UpdateMech");
var momentary = new { at30=momentaryAt30, at60=Fx(20,20), count=(int)Get(wiring,null,"_numMechs") };

Type worldGen=assembly.GetType("Terraria.WorldGen")!, npcType=assembly.GetType("Terraria.NPC")!;
Invoke(assembly.GetType("Terraria.ObjectData.TileObjectData")!,null,"Initialize");
Array npcs=Array.CreateInstance(npcType,200);for(int i=0;i<200;i++)npcs.SetValue(Activator.CreateInstance(npcType),i);Set(main,null,"npc",npcs);
bool[] framed=(bool[])Get(main,null,"tileFrameImportant");
foreach(int type in new[]{4,10,11,136,144,386,387,388,389})framed[type]=true;
bool[] solid=(bool[])Get(main,null,"tileSolid");solid[1]=true;solid[10]=true;solid[387]=true;solid[388]=true;
object Image(int left,int top,int width,int height)
{
    var cells=new List<object>();
    for(int x=left;x<left+width;x++)for(int y=top;y<top+height;y++)
    {
        object tile=tiles.GetValue(x,y)!;int colors=0;
        for(int c=0;c<4;c++)if((bool)Invoke(tileType,tile,c==0?"wire":$"wire{c+1}")!)colors|=1<<c;
        cells.Add(new{x,y,type=(ushort)Get(tileType,tile,"type"),active=(bool)Invoke(tileType,tile,"active")!,frameX=Fx(x,y),frameY=Fy(x,y),colors});
    }
    return cells;
}
int[] PeekRolls(int maximum,int count)
{
    object source=Get(main,null,"rand"),clone=Activator.CreateInstance(randomType,[0])!;
    Set(randomType,clone,"SeedArray",((int[])Get(randomType,source,"SeedArray")).ToArray());Set(randomType,clone,"inext",Get(randomType,source,"inext"));
    return Enumerable.Range(0,count).Select(_=>(int)Invoke(randomType,clone,"Next",maximum)!).ToArray();
}
var doorScenes=new List<object>();
foreach(string family in new[]{"door","gate","trapdoor"})foreach(bool allWired in new[]{false,true})
{
    Reset();Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);
    Put(20,20,136);for(int y=21;y<25;y++){object wire=tiles.GetValue(20,y)!;Invoke(tileType,wire,"wire",true);}
    int width=family=="gate"?1:2,height=family=="gate"?5:family=="door"?3:2;
    if(family=="door")
    {
        for(int row=0;row<3;row++)Put(21,20+row,10,0,row*18,allWired||row==0?1:0);
        Put(21,19,1,colors:0);Put(21,23,1,colors:0);
        if(allWired)for(int row=0;row<3;row++)Invoke(tileType,tiles.GetValue(22,20+row),"wire",true);
    }
    else if(family=="gate")
    {
        int[] ys={0,20,38,56,74};for(int row=0;row<5;row++)Put(21,20+row,388,0,ys[row],allWired||row==0?1:0);
        Put(21,19,1,colors:0);Put(21,25,1,colors:0);
    }
    else
    {
        for(int col=0;col<2;col++)Put(21+col,20,387,col*18,0,allWired||col==0?1:0);
        // Both horizontal anchors must be solid; the source sits beyond the left wired anchor.
        Put(19,20,136);Put(20,20,1);Put(23,20,1,colors:0);
        if(allWired)for(int col=0;col<2;col++)Invoke(tileType,tiles.GetValue(21+col,21),"wire",true);
        Invoke(worldGen,null,"CheckTrapDoor",21,20,387);
        if (!(bool)Invoke(tileType,tiles.GetValue(21,20),"active")!) throw new InvalidDataException("Invalid trapdoor fixture support.");
    }
    int sourceX=family=="trapdoor"?19:20;
    object before=Image(21,20,width,height);int[] directionRolls=PeekRolls(2,1);
    Invoke(wiring,null,"HitSwitch",sourceX,20);object first=Image(21,20,width,height);int[] closeRolls=PeekRolls(3,3);
    Invoke(wiring,null,"HitSwitch",sourceX,20);object second=Image(21,20,width,height);
    doorScenes.Add(new{family,allWired,blocked=false,sourceX,width,height,directionRolls,closeRolls,before,first,second});
}
Reset();Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);Put(20,20,136);
for(int row=0;row<5;row++){Put(21,20+row,388,0,new[]{0,20,38,56,74}[row],row<2?1:0);if(row>0)Invoke(tileType,tiles.GetValue(20,20+row),"wire",true);}
Put(21,19,1,colors:0);Put(21,25,1,colors:0);
object pairBefore=Image(21,20,1,5);Invoke(wiring,null,"HitSwitch",20,20);object pairFirst=Image(21,20,1,5);Invoke(wiring,null,"HitSwitch",20,20);object pairSecond=Image(21,20,1,5);
doorScenes.Add(new{family="gate",allWired=false,wireMask=3,blocked=false,sourceX=20,width=1,height=5,directionRolls=Array.Empty<int>(),closeRolls=Array.Empty<int>(),before=pairBefore,first=pairFirst,second=pairSecond});
Reset();Set(main,null,"rand",Activator.CreateInstance(randomType,[1458])!);Put(20,20,136);
for(int row=0;row<5;row++){Put(21,20+row,388,0,new[]{0,20,38,56,74}[row]);if(row>0)Invoke(tileType,tiles.GetValue(20,20+row),"wire",true);}
Put(21,19,1,colors:0);Put(21,25,1,colors:0);object blocker=players.GetValue(0)!;
Set(playerType,blocker,"active",true);Set(playerType,blocker,"position",V(21*16,22*16));Set(playerType,blocker,"width",16);Set(playerType,blocker,"height",16);
object blockedBefore=Image(21,20,1,5);Invoke(wiring,null,"HitSwitch",20,20);object blockedFirst=Image(21,20,1,5);
Set(playerType,blocker,"active",false);Invoke(wiring,null,"HitSwitch",20,20);object releasedSecond=Image(21,20,1,5);
doorScenes.Add(new{family="gate",allWired=true,blocked=true,sourceX=20,width=1,height=5,directionRolls=Array.Empty<int>(),closeRolls=Array.Empty<int>(),before=blockedBefore,first=blockedFirst,second=releasedSecond});

string output=JsonSerializer.Serialize(new{source="Unmodified official Linux edition running under Windows CoreCLR (not Linux execution)",sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath))),rows,doorScenes,timerEdges,momentary},new JsonSerializerOptions{WriteIndented=true});
File.WriteAllText(outputPath,output);Console.WriteLine($"Captured {rows.Count} original rows, {doorScenes.Count} door scenes, {timerEdges.Count} edge traces and one momentary trace");
Reset();Set(main,null,"maxTilesX",1);Set(main,null,"maxTilesY",1);Put(0,0,144,54,18,15);
object timer=tiles.GetValue(0,0)!;Invoke(tileType,timer,"actuator",true);Invoke(tileType,timer,"inActive",true);
Type lang=assembly.GetType("Terraria.Lang")!,textType=assembly.GetType("Terraria.Localization.LocalizedText")!;
Array text=(Array)Get(lang,null,"gen");for(int i=0;i<text.Length;i++)text.SetValue(Activator.CreateInstance(textType,flags,null,["probe",""],null),i);
bool[] important=(bool[])Get(main,null,"tileFrameImportant");important[144]=true;
Type worldFileType=assembly.GetType("Terraria.IO.WorldFile")!;
using var payload=new MemoryStream();using var binaryWriter=new BinaryWriter(payload,System.Text.Encoding.UTF8,true);Invoke(worldFileType,null,"SaveWorldTiles",binaryWriter);
payload.Position=0;using var binaryReader=new BinaryReader(payload);Invoke(worldFileType,null,"LoadWorldTiles",binaryReader,important);
File.WriteAllText(Path.Combine(outputDirectory,"timer-save-load.json"),JsonSerializer.Serialize(new{base64=Convert.ToBase64String(payload.ToArray()),loadedFrameY=Fy(0,0)},new JsonSerializerOptions{WriteIndented=true}));
