using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceProbe;

internal static class ScalarStateChecks
{
    internal static object Run()
    {
        const string prefix = "using Microsoft.Xna.Framework; namespace Microsoft.Xna.Framework { public struct Vector2 { public float X; public float Y; } } public class B { public Vector2 velocity; public Vector2 position; } ";
        const string good = "public class C:B { public float[] ai=new float[4]; public float[] localAI=new float[4]; public void Step(float scale) { if(velocity.X<0f)position.X+=velocity.Y; ai[0]+=scale; } }";
        var maps = new[] {
            new StateField(["B.velocity", "Microsoft.Xna.Framework.Vector2.X"], null, "Vx", "float", "readOnly"),
            new StateField(["B.velocity", "Microsoft.Xna.Framework.Vector2.Y"], null, "Vy", "float", "readOnly"),
            new StateField(["B.position", "Microsoft.Xna.Framework.Vector2.X"], null, "Px", "float", "readWrite"),
            new StateField(["C.ai"], 0, "Clock", "float", "readWrite") };
        using var doc = JsonDocument.Parse("{\"state\":{\"Vx\":{\"bits\":3212836864},\"Vy\":{\"bits\":1073741824},\"Px\":{\"bits\":0},\"Clock\":{\"bits\":0}},\"arguments\":[{\"bits\":1065353216}]}");
        var cases = new[] { new ScalarCase(doc.RootElement.GetProperty("state").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()),
            [doc.RootElement.GetProperty("arguments")[0].Clone()], 1458) };
        StatePlan Plan(string code, StateField[]? fields = null, string[]? parameterTypes = null, ScalarCase[]? values = null)
        {
            var tree = CSharpSyntaxTree.ParseText(prefix + code, path: "StateSynthetic.cs");
            var context = new CompilationContext(CSharpCompilation.Create("StateSynthetic", [tree],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new(OutputKind.DynamicallyLinkedLibrary)),
                new Dictionary<string, SyntaxTree> { ["StateSynthetic.cs"] = tree }, new Dictionary<string, string>(), new Dictionary<string, string>());
            return ScalarCandidate.PlanState(context, new(3, new(1, "unused", "unused", ["StateSynthetic.cs"],
                [new("StateSynthetic.cs", "C", "Step", parameterTypes ?? ["float"])], 0, 1), "void", fields ?? maps, values ?? cases));
        }
        var checks = new List<string>();
        void Check(string name, bool value) { if (!value) throw new InvalidDataException(name); checks.Add(name); }
        void Reject(string name, Func<StatePlan> test)
        {
            try { test(); } catch (InvalidDataException) { checks.Add(name); return; }
            throw new InvalidDataException("State planner incorrectly admitted " + name);
        }
        var plan = Plan(good);
        Check("exact-inherited-field-chain-and-fixed-slot-generated", plan.Candidate.Contains("__state.Px += __state.Vy") && plan.Candidate.Contains("__state.Clock += scale"));
        Check("unchanged-body-proper-carrier-and-candidate-compile", ScalarCandidate.Compile(plan.Candidate, plan.Carrier).Length > 0);
        Reject("wrong-same-name-root-owner", () => Plan(good, [maps[0] with { Path = ["Other.velocity", "Microsoft.Xna.Framework.Vector2.X"] }, .. maps.Skip(1)]));
        Reject("wrong-same-name-vector-leaf-owner", () => Plan(good, [maps[0] with { Path = ["B.velocity", "Other.X"] }, .. maps.Skip(1)]));
        Reject("wrong-leaf-type", () => Plan(good, [maps[0] with { Type = "int" }, .. maps.Skip(1)]));
        Reject("readonly-capability-write", () => Plan(good, [.. maps.Take(3), maps[3] with { Access = "readOnly" }]));
        Reject("duplicate-target-alias", () => Plan(good, [maps[0], maps[1] with { Target = "Vx" }, .. maps.Skip(2)]));
        Reject("duplicate-physical-path", () => Plan(good, [maps[0], maps[1] with { Path = maps[0].Path }, .. maps.Skip(2)]));
        Reject("out-of-range-map-index", () => Plan(good, [.. maps.Take(3), maps[3] with { Index = 4 }]));
        Reject("unmapped-literal-slot", () => Plan(good.Replace("ai[0]+=", "ai[1]+=")));
        Reject("computed-even-constant-index", () => Plan(good.Replace("ai[0]", "ai[1-1]")));
        Reject("dynamic-array-index", () => Plan(good.Replace("ai[0]", "ai[(int)scale]")));
        Reject("array-length-property", () => Plan(good.Replace("ai[0]+=scale", "ai[0]+=ai.Length")));
        Reject("arbitrary-array-name", () => Plan(good.Replace("ai", "history"), [.. maps.Take(3), maps[3] with { Path = ["C.history"] }]));
        Reject("array-reference-rebinding", () => Plan(good.Replace("ai[0]+=scale", "ai=localAI; ai[0]+=scale")));
        Reject("array-initializer-global-alias", () => Plan(good.Replace("public float[] ai=new float[4]", "public static float[] Pool=new float[4]; public float[] ai=Pool")));
        Reject("constructor-array-publication", () => Plan(good.Replace("public void Step", "public static float[] Published; public C(){Published=ai;} public void Step")));
        Reject("constructor-array-alias", () => Plan(good.Replace("public void Step", "public C(){localAI=ai;} public void Step")));
        Reject("array-local-alias", () => Plan(good.Replace("ai[0]+=scale", "var a=ai; a[0]+=scale")));
        Reject("reference-this-alias", () => Plan(good.Replace("ai[0]+=scale", "var a=this; a.ai[0]+=scale")));
        Reject("ref-local-alias", () => Plan(good.Replace("ai[0]+=scale", "ref float a=ref ai[0]; a+=scale")));
        Reject("vector-local-alias", () => Plan(good.Replace("ai[0]+=scale", "var a=velocity; ai[0]+=a.X")));
        Reject("whole-vector-assignment", () => Plan(good.Replace("ai[0]+=scale", "position=velocity; ai[0]+=scale")));
        Reject("explicit-base-carrier-shape", () => Plan(good.Replace("velocity.X", "base.velocity.X")));
        Reject("nested-getter-call", () => Plan(good.Replace("public float[] ai", "public float P {get;set;} public float[] ai").Replace("ai[0]+=scale", "ai[0]+=P")));
        Reject("unmapped-global", () => Plan(good.Replace("public float[] ai", "public static float Global; public float[] ai").Replace("ai[0]+=scale", "ai[0]+=Global")));
        Reject("api-invocation", () => Plan(good.Replace("ai[0]+=scale", "ai[0]+=System.Math.Abs(scale)")));
        Reject("nonidentity-numeric-conversion", () => Plan(good.Replace("ai[0]+=scale", "ai[0]+=(float)(double)scale")));
        Reject("division-frontier", () => Plan(good.Replace("ai[0]+=scale", "ai[0]=scale/2f")));
        Reject("exception-flow", () => Plan(good.Replace("ai[0]+=scale", "throw new System.Exception()")));
        Reject("loop-frontier", () => Plan(good.Replace("ai[0]+=scale", "while(ai[0]<scale)ai[0]++")));
        Reject("method-attribute-effects", () => Plan(good.Replace("public void Step", "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] public void Step")));
        Reject("virtual-dispatch", () => Plan(good.Replace("public void Step", "public virtual void Step")));
        Reject("readonly-reference-parameter", () => Plan(good.Replace("Step(float scale)", "Step(in float scale)")));
        Reject("checked-arithmetic", () => Plan(good.Replace("ai[0]+=scale", "checked { ai[0]+=scale; }")));
        using var vectorDoc = JsonDocument.Parse("{\"x\":{\"bits\":1065353216},\"y\":{\"bits\":2147483648}}");
        var vectorCases = new[] { cases[0] with { Arguments = [vectorDoc.RootElement.Clone(), cases[0].Arguments[0]] } };
        string vectorCode = good.Replace("Step(float scale)", "Step(Vector2 desired, float scale)")
            .Replace("ai[0]+=scale;", "ai[0]+=scale; position.X+=desired.X;");
        var vectorPlan = Plan(vectorCode, parameterTypes: [ScalarCandidate.VectorType, "float"], values: vectorCases);
        Check("readonly-by-value-vector-argument-compiles", ScalarCandidate.Compile(vectorPlan.Candidate, vectorPlan.Carrier).Length > 0);
        Reject("vector-argument-component-write", () => Plan(vectorCode.Replace("position.X+=desired.X", "position.X+=desired.X; desired.X=1f"),
            parameterTypes: [ScalarCandidate.VectorType, "float"], values: vectorCases));
        Reject("vector-argument-rebinding", () => Plan(vectorCode.Replace("position.X+=desired.X", "position.X+=desired.X; desired=velocity"),
            parameterTypes: [ScalarCandidate.VectorType, "float"], values: vectorCases));
        return new { Accepted = true, Checks = checks.Count, Results = checks };
    }
}
