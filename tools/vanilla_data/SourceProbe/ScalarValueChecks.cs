using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceProbe;

internal static class ScalarValueChecks
{
    internal static object Run()
    {
        var checks = new List<string>();
        const string source = "public class Item { public bool flag; } public class C { public float factor; public float Tick(Item item,float value) { if(item.flag) { value*=factor; } return value; } }";
        var maps = new[] { new ValueField("C.factor", "Factor", "float", "this", "readOnly"), new ValueField("Item.flag", "Flag", "bool", "item", "readOnly") };
        using var json = JsonDocument.Parse("{\"state\":{\"Factor\":{\"bits\":1073741824},\"Flag\":true},\"arguments\":[{\"receiver\":\"item\"},{\"bits\":1077936128}]}");
        var element = json.RootElement;
        var cases = new[] { new ScalarCase(element.GetProperty("state").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()), element.GetProperty("arguments").EnumerateArray().Select(p => p.Clone()).ToArray(), 1458) };
        ValuePlan Plan(string text, ValueField[]? fields = null, ScalarCase[]? rows = null)
        {
            var tree = CSharpSyntaxTree.ParseText(text, path: "OwnValue.cs");
            var context = new CompilationContext(CSharpCompilation.Create("OwnValue", [tree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new(OutputKind.DynamicallyLinkedLibrary)),
                new Dictionary<string, SyntaxTree> { ["OwnValue.cs"] = tree }, new Dictionary<string, string>(), new Dictionary<string, string>());
            return ScalarValueCandidate.Plan(context, new(2, new(1, "unused", "unused", ["OwnValue.cs"], [new("OwnValue.cs", "C", "Tick", ["Item", "float"])], 0, 1), "float", fields ?? maps, rows ?? cases));
        }
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidDataException("Value regression: " + name);
            checks.Add(name);
        }
        void Reject(string name, Func<ValuePlan> run)
        {
            try { run(); } catch (InvalidDataException) { checks.Add(name); return; }
            throw new InvalidDataException("Incorrect value admission: " + name);
        }
        var good = Plan(source);
        Check("readonly-receiver-fields-and-scalar-parameter-assignment-rewritten", good.Candidate.Contains("in CandidateState __state") && good.Candidate.Contains("value *= __state.Factor") && !good.Candidate.Contains("Item item"));
        Check("proper-body-carrier-and-value-candidate-compile", ScalarCandidate.Compile(good.Candidate, good.Carrier).Length > 0);
        Check("compound-assignment-control-is-generated", ScalarValueCandidate.Mutants(good).Keys.SequenceEqual(["omit-operation-0"]));
        var shadowed = Plan(source.Replace("if(item.flag)", "float factor=2f; if(item.flag)").Replace("value*=factor;", "value*=this.factor; value+=factor;"));
        Check("scalar-local-shadowing-preserves-field-and-local-symbols", shadowed.Candidate.Contains("value *= __state.Factor") && shadowed.Candidate.Contains("value += factor") && ScalarCandidate.Compile(shadowed.Candidate, shadowed.Carrier).Length > 0);
        Reject("readonly-own-field-write-refused", () => Plan(source.Replace("value*=factor", "factor=value")));
        Reject("readonly-argument-field-write-refused", () => Plan(source.Replace("value*=factor;", "item.flag=false; value*=factor;")));
        Reject("reference-argument-rebinding-refused", () => Plan(source.Replace("value*=factor;", "item=new Item(); value*=factor;")));
        Reject("reference-local-alias-refused", () => Plan(source.Replace("value*=factor;", "var alias=item; value*=factor;")));
        Reject("unknown-receiver-map-refused", () => Plan(source, [maps[0], maps[1] with { Receiver = "other" }]));
        Reject("same-name-wrong-symbol-refused", () => Plan(source, [maps[0] with { Symbol = "Other.factor" }, maps[1]]));
        Reject("implicit-numeric-widening-refused", () => Plan(source.Replace("value*=factor", "value*=2.0")));
        Reject("numeric-cast-refused", () => Plan(source.Replace("return value;", "return (float)(int)value;")));
        Reject("property-getter-refused", () => Plan(source.Replace("public bool flag;", "public bool flag {get;set;}")));
        Reject("static-global-refused", () => Plan(source.Replace("public float factor;", "public static float factor;")));
        Reject("method-call-refused", () => Plan(source.Replace("value*=factor", "value*=System.MathF.Abs(factor)")));
        Reject("loop-refused", () => Plan(source.Replace("value*=factor;", "while(item.flag) value*=factor;")));
        Reject("array-creation-and-alias-refused", () => Plan(source.Replace("value*=factor;", "var array=new float[1]; value*=factor;")));
        Reject("unsafe-block-refused", () => Plan(source.Replace("value*=factor;", "unsafe { value*=factor; }")));
        Reject("unsafe-method-refused", () => Plan(source.Replace("public float Tick", "public unsafe float Tick")));
        Reject("unsafe-containing-type-refused", () => Plan(source.Replace("public class C", "public unsafe class C")));
        Reject("reserved-local-name-refused", () => Plan(source.Replace("value*=factor;", "float __state=1f; value*=factor;")));
        Reject("schema1-write-capability-not-inferred", () => Plan(source, [maps[0] with { Access = "readWrite" }, maps[1]]));
        Reject("method-attributes-refused", () => Plan(source.Replace("public float Tick", "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] public float Tick")));
        Reject("missing-observed-input-refused", () => Plan(source, [maps[0]]));
        using var nonfinite = JsonDocument.Parse("{\"bits\":2139095040}");
        var bad = cases[0].State.ToDictionary(p => p.Key, p => p.Value); bad["Factor"] = nonfinite.RootElement.Clone();
        Reject("nonfinite-input-bits-refused", () => Plan(source, rows: [cases[0] with { State = bad }]));
        using var minusZero = JsonDocument.Parse("{\"bits\":2147483648}");
        Check("negative-zero-survives-bit-domain", BitConverter.SingleToUInt32Bits((float)ScalarValueCandidate.Value(minusZero.RootElement, "float")) == 0x80000000);
        const string precedence = "public class Item {public bool a;public bool b;} public class C {public int x;public int y;public int Pick(Item item) {if(item.a)return x; if(item.b)return y; return 0;}}";
        var priorityTree = CSharpSyntaxTree.ParseText(precedence, path: "OwnPriority.cs");
        var priorityContext = new CompilationContext(CSharpCompilation.Create("OwnPriority", [priorityTree], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new(OutputKind.DynamicallyLinkedLibrary)),
            new Dictionary<string, SyntaxTree> { ["OwnPriority.cs"] = priorityTree }, new Dictionary<string, string>(), new Dictionary<string, string>());
        using var priorityJson = JsonDocument.Parse("{\"state\":{\"X\":17,\"Y\":29,\"A\":true,\"B\":true},\"arguments\":[{\"receiver\":\"item\"}]}");
        var p = priorityJson.RootElement;
        var priority = ScalarValueCandidate.Plan(priorityContext, new(2, new(1, "unused", "unused", ["OwnPriority.cs"], [new("OwnPriority.cs", "C", "Pick", ["Item"])], 0, 1), "int",
            [new("C.x", "X", "int", "this", "readOnly"), new("C.y", "Y", "int", "this", "readOnly"), new("Item.a", "A", "bool", "item", "readOnly"), new("Item.b", "B", "bool", "item", "readOnly")],
            [new(p.GetProperty("state").EnumerateObject().ToDictionary(q => q.Name, q => q.Value.Clone()), p.GetProperty("arguments").EnumerateArray().Select(q => q.Clone()).ToArray(), 42)]));
        string swapped = ScalarValueCandidate.Mutants(priority)["swap-category-precedence"];
        Check("early-return-priority-control-compiles-with-distinct-branches", swapped.IndexOf("__state.B", StringComparison.Ordinal) < swapped.IndexOf("__state.A", StringComparison.Ordinal) && ScalarCandidate.Compile(swapped, priority.Carrier).Length > 0);
        return new { Accepted = true, Cases = checks.Count, Checks = checks, Scope = "Authored synthetic capability/compile checks; actual official differential is separate." };
    }
}
