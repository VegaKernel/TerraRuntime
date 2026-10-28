using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceProbe;

internal static class ScalarChecks
{
    internal static object Run()
    {
        var results = new List<string>();
        const string good = "public class C { public int timeLeft; public bool despawnEncouraged; public void Tick(int timeLeft) { if(this.timeLeft>timeLeft) this.timeLeft=timeLeft; despawnEncouraged=true; } }";
        var maps = new[] { new ScalarField("C.timeLeft", "Remaining", "int"), new ScalarField("C.despawnEncouraged", "Flag", "bool") };
        using var values = JsonDocument.Parse("{\"state\":{\"Remaining\":20,\"Flag\":false},\"arguments\":[10]}");
        var state = values.RootElement.GetProperty("state").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var cases = new[] { new ScalarCase(state, [values.RootElement.GetProperty("arguments")[0].Clone()], 1458) };
        ScalarPlan Plan(string code, ScalarField[]? fields = null, string[]? parameters = null)
        {
            var tree = CSharpSyntaxTree.ParseText(code, path: "OwnSynthetic.cs");
            var context = new CompilationContext(CSharpCompilation.Create("OwnSynthetic", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new(OutputKind.DynamicallyLinkedLibrary)),
                new Dictionary<string, SyntaxTree> { ["OwnSynthetic.cs"] = tree }, new Dictionary<string, string>(), new Dictionary<string, string>());
            return ScalarCandidate.Plan(context, new(1, new(1, "unused", "unused", ["OwnSynthetic.cs"],
                [new("OwnSynthetic.cs", "C", "Tick", parameters ?? ["int"])], 0, 1), fields ?? maps, cases));
        }
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidDataException("Scalar candidate regression: " + name);
            results.Add(name);
        }
        void Reject(string name, Func<ScalarPlan> run)
        {
            try { run(); } catch (InvalidDataException) { results.Add(name); return; }
            throw new InvalidDataException("Scalar candidate incorrectly admitted: " + name);
        }
        var admitted = Plan(good);
        Check("semantic-shadowed-parameter-kept-distinct", admitted.Candidate.Contains("__state.Remaining > timeLeft") &&
            admitted.Candidate.Contains("__state.Remaining = timeLeft") && admitted.Candidate.Contains("__state.Flag = true"));
        Check("original-method-proper-carrier-compiles-with-candidate", ScalarCandidate.Compile(admitted.Candidate, admitted.Carrier).Length > 0);
        Reject("wrong-same-name-field-owner-rejected", () => Plan(good, [new("Other.timeLeft", "Remaining", "int"), maps[1]]));
        Reject("wrong-field-type-rejected", () => Plan(good, [maps[0] with { Type = "bool" }, maps[1]]));
        Reject("two-fields-cannot-alias-one-target", () => Plan(good, [maps[0], maps[1] with { Target = "Remaining" }]));
        Reject("property-target-path-rejected", () => Plan(good, [maps[0] with { Target = "Nested.Value" }, maps[1]]));
        Reject("unmapped-field-rejected", () => Plan(good, [maps[0]]));
        Reject("nested-property-effect-rejected", () => Plan(good.Replace("despawnEncouraged=true;", "despawnEncouraged=true; P=1;")
            .Replace("public int timeLeft;", "public int P {get;set;} public int timeLeft;")));
        Reject("aliased-receiver-rejected", () => Plan(good.Replace("despawnEncouraged=true;", "despawnEncouraged=true; other.timeLeft=3;")
            .Replace("public int timeLeft;", "public C other=null!; public int timeLeft;")));
        Reject("readonly-ref-argument-rejected", () => Plan(good.Replace("Tick(int timeLeft)", "Tick(in int timeLeft)")));
        Reject("numeric-conversion-rejected", () => Plan(good.Replace("this.timeLeft=timeLeft", "this.timeLeft=(int)(double)timeLeft")));
        Reject("call-not-mapped-by-text", () => Plan(good.Replace("despawnEncouraged=true;", "despawnEncouraged=Helper();")
            .Replace("public int timeLeft;", "public bool Helper()=>true; public int timeLeft;")));
        Reject("throw-rejected", () => Plan(good.Replace("despawnEncouraged=true;", "throw null!;")));
        Reject("loop-rejected", () => Plan(good.Replace("despawnEncouraged=true;", "while(timeLeft>0) despawnEncouraged=true;")));
        Reject("static-global-rejected", () => Plan(good.Replace("public bool despawnEncouraged;", "public static bool despawnEncouraged;")));
        Reject("implicit-synchronized-method-protocol-rejected", () => Plan(good.Replace("public void Tick",
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] public void Tick")));
        Reject("unused-mapping-rejected", () => Plan(good.Replace("despawnEncouraged=true;", "")));
        Check("both-state-write-mutations-are-generated", maps.All(map => ScalarCandidate.OmitWrite(admitted, map.Target) != admitted.Candidate));
        return new { Accepted = true, Cases = results.Count, Checks = results, Scope = "Authored synthetic semantic admission/compile checks only; actual original differential is separate." };
    }
}
