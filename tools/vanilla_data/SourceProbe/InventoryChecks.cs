using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceProbe;

internal static class InventoryChecks
{
    internal static object Run()
    {
        var references = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var results = new List<string>();
        InventoryResult Scan(string code, string method = "Tick", string[]? parameters = null, int depth = 1, int count = 32)
        {
            var tree = CSharpSyntaxTree.ParseText(code, path: "Synthetic.cs");
            var context = new CompilationContext(CSharpCompilation.Create("Synthetic", [tree], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)),
                new Dictionary<string, SyntaxTree> { ["Synthetic.cs"] = tree },
                new Dictionary<string, string> { ["Synthetic.cs"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant() },
                new Dictionary<string, string> { ["corelib"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(object).Assembly.Location))).ToLowerInvariant() });
            return SemanticInventory.Analyze(context, new(1, "unused", "unused", ["Synthetic.cs"],
                [new("Synthetic.cs", "C", method, parameters ?? [])], depth, count));
        }
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException("Semantic regression: " + name);
            results.Add(name);
        }

        const string overloaded = "class C { static int value; static int Pick(int x) => x; static string Pick(string x) => x; public static int Tick(int x) { value++; return Pick(x); } }";
        var overload = Scan(overloaded, parameters: ["int"]);
        Check("exact-overload-and-source-callee", overload.Accepted && overload.Methods.Count == 2 &&
            overload.Methods.Any(method => method.Id == "C.Pick(int)"));
        Check("increment-is-read-write", overload.Methods[0].Accesses.Any(access => access.Symbol == "C.value" && access.Mode == "read-write"));
        Check("source-depth-budget-rejects", !Scan(overloaded, parameters: ["int"], depth: 0).Accepted);
        Check("source-method-budget-rejects", !Scan(overloaded, parameters: ["int"], count: 1).Accepted);
        var recursion = Scan("class C { public static int Tick(int x) => x <= 0 ? 0 : Tick(x-1); }", parameters: ["int"], depth: 0, count: 1);
        Check("recursive-symbol-visited-once", recursion.Accepted && recursion.Methods.Count == 1);

        var aliases = Scan("class C { static int a,b; static void Take(ref int x,out int y) { y=x; x++; } public static void Tick(){ Take(ref a,out b); a+=2; } }");
        Check("ref-out-effects-retained", aliases.Accepted && !aliases.SourceClosureComplete &&
            aliases.Methods[0].Accesses.Any(access => access.Symbol == "C.a" && access.Mode == "read-write") &&
            aliases.Methods[0].Accesses.Any(access => access.Symbol == "C.b" && access.Mode == "write"));
        var properties = Scan("class C { static int[] a = new int[2]; static int P { get; set; } public static void Tick(){ P=1; a[0]++; } }");
        Check("property-and-array-mutations-opaque", properties.Accepted && !properties.SourceClosureComplete &&
            properties.Methods[0].Accesses.Any(access => access.Target == "property" && access.Mode == "write") &&
            properties.Methods[0].Accesses.Any(access => access.Target == "array-element" && access.Mode == "read-write"));
        var metadata = Scan("class C { public static int Tick(int x) => System.Math.Abs(x); }", parameters: ["int"]);
        Check("resolved-metadata-not-pure", metadata.Accepted && !metadata.SourceClosureComplete && metadata.Methods[0].Calls.Count == 1);
        var delegates = Scan("class C { public static void Tick(System.Action f){ f(); } }", parameters: ["System.Action"]);
        Check("delegate-target-opaque", delegates.Accepted && !delegates.SourceClosureComplete && delegates.Methods[0].Calls[0].Dispatch == "delegate");
        Check("unresolved-invocation-rejects", !Scan("class C { public static void Tick(){ Missing(); } }").Accepted);
        Check("dynamic-invocation-rejects", !Scan("class C { public static void Tick(dynamic x){ x.Missing(); } }", parameters: ["dynamic"]).Accepted);
        Check("absent-entry-rejects", !Scan("class C { public static void Other(){} }").Accepted);
        var contextError = Scan("class C { static Missing Other(){ return null; } public static int Tick() => 1; }");
        Check("global-errors-never-hidden", contextError.Accepted && contextError.CompilationErrorCount > 0 && contextError.Methods[0].Diagnostics.Count == 0);
        var virtualCall = Scan("class C { public virtual void Other(){} public void Tick(){ Other(); } }");
        Check("virtual-dispatch-not-inlined", virtualCall.Accepted && !virtualCall.SourceClosureComplete && virtualCall.Methods.Count == 1);
        var refLocal = Scan("class C { static int value; public static void Tick(){ ref int alias = ref value; alias=1; } }");
        Check("ref-local-alias-stays-opaque", refLocal.Accepted && !refLocal.SourceClosureComplete);
        var refReturn = Scan("class C { static int value; static ref int Take() => ref value; public static void Tick(){ Take()=1; } }");
        Check("ref-return-alias-stays-opaque", refReturn.Accepted && !refReturn.SourceClosureComplete);
        var tuple = Scan("class C { static int a,b; public static void Tick(){ (a,b)=(1,2); } }");
        Check("deconstruction-targets-are-writes", tuple.Accepted && tuple.Methods[0].Accesses.Count(access => access.Mode == "write") == 2);
        var usingStatement = Scan("class C { public static void Tick(System.IDisposable x){ using(x){} } }", parameters: ["System.IDisposable"]);
        Check("implicit-dispose-stays-opaque", usingStatement.Accepted && !usingStatement.SourceClosureComplete);
        var each = Scan("class C { public static void Tick(System.Collections.IEnumerable x){ foreach(var value in x){} } }", parameters: ["System.Collections.IEnumerable"]);
        Check("implicit-iterator-stays-opaque", each.Accepted && !each.SourceClosureComplete);
        var pointer = Scan("class C { public static unsafe void Tick(int* value){ *value=1; } }", parameters: ["int*"]);
        Check("pointer-store-stays-opaque", pointer.Accepted && !pointer.SourceClosureComplete);
        return new { accepted = true, checks = results.Count, names = results,
            scope = "Author-written synthetic compiler/semantic cases; no original game execution or vanilla behavior acceptance." };
    }
}
