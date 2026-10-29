using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SourceProbe;

internal static partial class ScalarVerification
{
    private sealed record StateExecution(object? Result, Dictionary<string, object?> State);
    internal static object RunState(StatePlan plan, string outputDirectory)
    {
        string original = Path.GetFullPath(plan.Request.Analysis.Assembly);
        if (Sha(File.ReadAllBytes(original)) != "4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034" ||
            Sha(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(original)!, "FNA.dll"))) != "46d201f59ce6c3827414b2c2c5141c218acabda605b02e564d73ca2efb2a3171")
            throw new InvalidDataException("State oracle requires the pinned official1458/FNA identities");
        string runtimeReference = typeof(object).Assembly.Location, runtimeSha = Sha(File.ReadAllBytes(typeof(object).Assembly.Location));
        var originalContext = new AssemblyLoadContext("StateOriginal", isCollectible: true); Assembly? game = null;
        originalContext.Resolving += (_, name) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(original)!, name.Name + ".dll");
            if (File.Exists(path)) return originalContext.LoadFromAssemblyPath(path);
            string? resource = game?.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = game!.GetManifestResourceStream(resource)!; return originalContext.LoadFromStream(stream);
        };
        game = originalContext.LoadFromAssemblyPath(original);
        game.GetType("Terraria.Program", true)!.GetField("SavePath", Flags)!.SetValue(null, outputDirectory);
        Type originalType = game.GetType(plan.Method.ContainingType.ToDisplayString(), true)!;
        Type RuntimeType(ITypeSymbol type) => type.SpecialType switch
        {
            SpecialType.System_Int32 => typeof(int), SpecialType.System_Boolean => typeof(bool), SpecialType.System_Single => typeof(float),
            _ when ScalarCandidate.IsVector(type) => game.GetType("Terraria.Entity", true)!.GetField("position", Flags)!.FieldType,
            _ => throw new InvalidDataException("Unowned runtime parameter type")
        };
        var method = originalType.GetMethod(plan.Method.Name, Flags, plan.Method.Parameters.Select(p => RuntimeType(p.Type)).ToArray())
            ?? throw new InvalidDataException("Exact original state method absent");
        if (method.IsStatic || method.IsVirtual || method.DeclaringType != originalType || method.ReturnType.FullName != plan.Method.ReturnType.ContainingNamespace + "." + plan.Method.ReturnType.MetadataName)
            throw new InvalidDataException("Original state runtime method shape differs");
        var roots = new Dictionary<string, FieldInfo>();
        foreach (var path in plan.Paths)
        {
            var symbol = path.Symbols[0]; FieldInfo field = FindField(originalType, symbol.ContainingType.ToDisplayString(), symbol.Name);
            Type expectedFieldType = symbol.Type is IArrayTypeSymbol ? typeof(float[]) : RuntimeType(symbol.Type);
            if (field.IsStatic || field.IsInitOnly || field.FieldType != expectedFieldType)
                throw new InvalidDataException("Original state root shape differs");
            roots.TryAdd(path.Map.Path[0], field);
            if (path.Symbols.Length == 2)
            {
                var leaf = field.FieldType.GetField(path.Symbols[1].Name, Flags)!;
                if (leaf is null || leaf.IsStatic || leaf.IsInitOnly || leaf.FieldType != typeof(float) || leaf.DeclaringType?.FullName != ScalarCandidate.VectorType)
                    throw new InvalidDataException("Original Vector2 leaf differs");
            }
        }
        Type main = game.GetType("Terraria.Main", true)!, randomType = game.GetType("Terraria.Utilities.UnifiedRandom", true)!;
        main.GetField("dedServ", Flags)!.SetValue(null, true); main.GetField("netMode", Flags)!.SetValue(null, 2);
        FieldInfo randomOwner = main.GetField("rand", Flags)!, cursor = randomType.GetField("inext", Flags)!, slots = randomType.GetField("SeedArray", Flags)!;
        object Rng(object random)
        {
            int[] values = ((int[])slots.GetValue(random)!).ToArray(); if (values.Length != 56) throw new InvalidDataException("Physical RNG shape differs");
            return new { Cursor = Convert.ToUInt32(cursor.GetValue(random)), Slots = values };
        }
        string Canonical(object? value) => JsonSerializer.Serialize(value);
        object? Observe(object? value) => value is null ? null : ScalarValueCandidate.Observe(value);
        byte[] binary = ScalarCandidate.Compile(plan.Candidate, plan.Carrier);
        Write("candidate.cs", Encoding.UTF8.GetBytes(plan.Candidate)); Write("original-carrier.cs", Encoding.UTF8.GetBytes(plan.Carrier)); Write("candidate.dll", binary);
        var generatedContext = new AssemblyLoadContext("StateGenerated", isCollectible: true);
        using var stream = new MemoryStream(binary); Assembly generated = generatedContext.LoadFromStream(stream);
        Type carrier = generated.GetType("OriginalMethodCarrier.Carrier", true)!, state = generated.GetType("GeneratedCandidate.CandidateState", true)!;
        MethodInfo carrierMethod = carrier.GetMethod(plan.Method.Name)!, candidateMethod = generated.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!;
        var rows = new List<object>(); var expected = new List<string>(); var initials = new List<Dictionary<string, object>>(); var argsRows = new List<object?[]>();
        var ignoredWrites = plan.Paths.Where(p => p.Map.Access == "readWrite").Select(p => string.Join("/", p.Map.Path) + (p.Map.Index is {} i ? "[" + i + "]" : "")).ToHashSet(StringComparer.Ordinal);
        int index = 0;
        foreach (var item in plan.Request.Cases)
        {
            var initial = plan.Paths.ToDictionary(p => p.Map.Target, p => ScalarValueCandidate.Value(item.State[p.Map.Target], p.Map.Type));
            object actor = Activator.CreateInstance(originalType)!;
            foreach (var path in plan.Paths) SetProjected(actor, roots[path.Map.Path[0]], path, initial[path.Map.Target]);
            RejectArrayAliases(actor, roots, plan.Paths);
            var beforeProjection = Project(actor, roots);
            if (Canonical(beforeProjection) != Canonical(initial.ToDictionary(p => p.Key, p => Observe(p.Value)))) throw new InvalidDataException("Original preimage differs (alias or field initialization)");
            object?[] originalArgs = method.GetParameters().Select((p, i) => Argument(item.Arguments[i], p.ParameterType)).ToArray();
            object?[] carrierArgs = carrierMethod.GetParameters().Select((p, i) => Argument(item.Arguments[i], p.ParameterType)).ToArray();
            object random = Activator.CreateInstance(randomType, [item.RngSeed])!; randomOwner.SetValue(null, random);
            var identities = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            object beforeActor = ScalarValueVerification.Fingerprint(actor, identities, ignoredWrites.Contains), beforeRng = Rng(random);
            object? officialResult = Observe(method.Invoke(actor, originalArgs));
            object afterRng = Rng(random), afterActor = ScalarValueVerification.Fingerprint(actor, identities, ignoredWrites.Contains);
            if (Canonical(beforeRng) != Canonical(afterRng) || Canonical(beforeActor) != Canonical(afterActor))
                throw new InvalidDataException("Original has an unowned state or RNG effect");
            object originalState = Project(actor, roots);
            object carrierActor = Activator.CreateInstance(carrier)!;
            var carrierRoots = plan.Paths.Select(p => p.Symbols[0]).Distinct<IFieldSymbol>(SymbolEqualityComparer.Default)
                .ToDictionary(p => ScalarCandidate.Identity(p), p => carrier.GetField(p.Name)!);
            foreach (var path in plan.Paths) SetProjected(carrierActor, carrierRoots[path.Map.Path[0]], path, initial[path.Map.Target]);
            object? carrierResult = Observe(carrierMethod.Invoke(carrierActor, carrierArgs)); object carrierState = Project(carrierActor, carrierRoots);
            object?[] candidateArgs = candidateMethod.GetParameters().Skip(1).Select((p, i) => Argument(item.Arguments[i], p.ParameterType)).ToArray();
            var candidate = Execute(candidateMethod, state, initial, candidateArgs);
            string expectedResult = Canonical(new { Result = officialResult, State = originalState });
            if (expectedResult != Canonical(new { Result = carrierResult, State = carrierState }) || expectedResult != Canonical(candidate))
                throw new InvalidDataException("Original/carrier/generated state mismatch at " + index);
            rows.Add(new { Index = index++, item.RngSeed, Initial = initial.ToDictionary(p => p.Key, p => Observe(p.Value)), Arguments = item.Arguments,
                Original = officialResult, Carrier = carrierResult, Candidate = candidate.Result, OriginalState = originalState, CarrierState = carrierState, CandidateState = candidate.State,
                ActorBefore = beforeActor, ActorAfter = afterActor, RngBefore = beforeRng, RngAfter = afterRng });
            expected.Add(expectedResult); initials.Add(initial); argsRows.Add(candidateArgs);
        }
        var controls = new List<object>();
        var source = CSharpSyntaxTree.ParseText(plan.Candidate).GetRoot();
        var operations = source.DescendantNodes().OfType<ExpressionStatementSyntax>().Where(s => s.Expression is AssignmentExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax).ToArray();
        var variants = operations.Select((s, i) => (Name: "omit-operation-" + i, Code: source.ReplaceNode(s, SyntaxFactory.EmptyStatement()).ToFullString())).ToList();
        if (plan.Request.ReturnType == "bool")
            variants.Add(("invert-result", new InvertBooleanReturns().Visit(source)!.ToFullString()));
        else if (plan.Request.ReturnType is "int" or "float")
            variants.Add(("perturb-result", new PerturbScalarReturns(plan.Request.ReturnType).Visit(source)!.ToFullString()));
        if (variants.Count > 128) throw new InvalidDataException("State mutation control bound exceeded");
        foreach (var variant in variants)
        {
            byte[] mutant = ScalarCandidate.Compile(variant.Code, plan.Carrier);
            Write(variant.Name + ".cs", Encoding.UTF8.GetBytes(variant.Code)); Write(variant.Name + ".dll", mutant);
            var load = new AssemblyLoadContext(variant.Name, isCollectible: true);
            using var bytes = new MemoryStream(mutant); var assembly = load.LoadFromStream(bytes);
            var invoke = assembly.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!; var mutantState = assembly.GetType("GeneratedCandidate.CandidateState", true)!;
            int differences = 0;
            for (int i = 0; i < initials.Count; i++)
            {
                object?[] args = invoke.GetParameters().Skip(1).Select((p, a) => Argument(plan.Request.Cases[i].Arguments[a], p.ParameterType)).ToArray();
                if (expected[i] != Canonical(Execute(invoke, mutantState, initials[i], args))) differences++;
            }
            if (differences == 0) throw new InvalidDataException("Cases fail to detect state mutation: " + variant.Name);
            controls.Add(new { Mutation = variant.Name, Cases = rows.Count, Differences = differences, Errors = 0, AssemblySha256 = Sha(mutant) }); load.Unload();
        }
        if (controls.Count == 0) throw new InvalidDataException("No independent mutation control");
        if (Sha(File.ReadAllBytes(runtimeReference)) != runtimeSha) throw new InvalidDataException("Runtime reference drift");
        generatedContext.Unload(); originalContext.Unload();
        return new { SchemaVersion = 3, Kind = "source-state-candidate-verification", Accepted = true, CandidateOnly = true,
            Entry = plan.Inventory.Entries.Single(), plan.Request.ReturnType, Cases = rows.Count, OriginalMethodCalls = rows.Count,
            OriginalCarrierCompiled = true, WholeOriginalTypeCompiled = false, MethodQualified = true, OriginalRngCursorAndAll56SlotsUnchanged = true,
            AllUnmappedDirectActorFieldsAndArraysUnchanged = true, DirectArrayAliasesRejected = true,
            InventorySourceClosureComplete = plan.Inventory.SourceClosureComplete,
            ArrayCapabilityScope = "Only literal ai/localAI slots0..3, four-element runtime arrays and exact roots. Array writes also require source fresh allocations and non-publishing constructor chains; direct runtime aliases are rejected. Generic inventory uncertainty is not a general alias proof.",
            RuntimeReference = new { Path = runtimeReference, Sha256 = runtimeSha }, CandidateSha256 = Sha(Encoding.UTF8.GetBytes(plan.Candidate)),
            OriginalCarrierSha256 = Sha(Encoding.UTF8.GetBytes(plan.Carrier)), AssemblySha256 = Sha(binary), plan.Context.SourceSha256, plan.Context.ReferenceSha256,
            CompilationErrorCount = plan.Inventory.CompilationErrorCount, Mutants = controls, Observations = rows,
            Scope = "Cache-only exact-symbol scalar/Vector2-component/fixed-float-array projection, actual untouched official method and proper unchanged-body data carrier. Vector2 carrier represents only two scalar fields; no API behavior is stubbed. Globals/calls/properties/ref aliases/numeric conversions remain unsupported. Finite inputs/outputs only. Fingerprint covers direct fields/arrays/value types; reference-object internals are not a transitive proof. No runtime binding, NPC admission or whole-AI equivalence." };

        Dictionary<string, object?> Project(object actor, Dictionary<string, FieldInfo> fields) => plan.Paths.ToDictionary(p => p.Map.Target, p => Observe(GetProjected(actor, fields[p.Map.Path[0]], p)));
        StateExecution Execute(MethodInfo invoke, Type shape, Dictionary<string, object> initial, object?[] args)
        {
            object box = Activator.CreateInstance(shape)!; foreach (var pair in initial) shape.GetField(pair.Key)!.SetValue(box, pair.Value);
            object?[] all = new object?[] { box }.Concat(args).ToArray(); object? value = Observe(invoke.Invoke(null, all));
            return new(value, initial.Keys.ToDictionary(k => k, k => Observe(shape.GetField(k)!.GetValue(all[0]))));
        }
        void Write(string name, byte[] data) { using var file = new FileStream(Path.Combine(outputDirectory, name), FileMode.CreateNew); file.Write(data); }
    }

    private static FieldInfo FindField(Type actor, string declaring, string name)
    {
        for (Type? type = actor; type is not null; type = type.BaseType)
            if (type.FullName == declaring) return type.GetField(name, Flags | BindingFlags.DeclaredOnly) ?? throw new InvalidDataException("Runtime field absent");
        throw new InvalidDataException("Runtime declaring field owner absent");
    }
    private static object? GetProjected(object actor, FieldInfo root, StatePath path)
    {
        object? value = root.GetValue(actor);
        if (path.Map.Index is {} i) return value is float[] { Length: 4 } a ? a[i] : throw new InvalidDataException("Original array must have four slots");
        return path.Symbols.Length == 2 ? root.FieldType.GetField(path.Symbols[1].Name)!.GetValue(value) : value;
    }
    private static void SetProjected(object actor, FieldInfo root, StatePath path, object value)
    {
        if (path.Map.Index is {} i)
        {
            if (root.GetValue(actor) is not float[] { Length: 4 } a) throw new InvalidDataException("Original array must have four slots"); a[i] = (float)value; return;
        }
        if (path.Symbols.Length == 1) { root.SetValue(actor, value); return; }
        object vector = root.GetValue(actor)!; root.FieldType.GetField(path.Symbols[1].Name)!.SetValue(vector, value); root.SetValue(actor, vector);
    }
    private static object Argument(JsonElement value, Type type)
    {
        if (type.FullName != ScalarCandidate.VectorType) return ScalarValueCandidate.Value(value, type == typeof(float) ? "float" : type == typeof(int) ? "int" : "bool");
        var fields = type.GetFields(Flags).Where(f => !f.IsStatic).ToArray();
        if (!type.IsValueType || fields.Length != 2 || fields.Any(f => f.Name is not ("X" or "Y") || f.FieldType != typeof(float)))
            throw new InvalidDataException("Runtime Vector2 is not the closed two-float data shape");
        object vector = Activator.CreateInstance(type)!;
        type.GetField("X")!.SetValue(vector, ScalarValueCandidate.Value(value.GetProperty("x"), "float"));
        type.GetField("Y")!.SetValue(vector, ScalarValueCandidate.Value(value.GetProperty("y"), "float")); return vector;
    }
    private static void RejectArrayAliases(object actor, Dictionary<string, FieldInfo> roots, StatePath[] paths)
    {
        foreach (var root in roots.Where(p => paths.Any(m => m.Map.Path[0] == p.Key && m.Map.Index.HasValue)))
        {
            var value = root.Value.GetValue(actor);
            for (Type? type = actor.GetType(); type is not null; type = type.BaseType)
                foreach (var field in type.GetFields(Flags | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic && !f.Equals(root.Value)))
                    if (ReferenceEquals(value, field.GetValue(actor))) throw new InvalidDataException("Mapped array has another direct actor alias");
        }
    }
    private sealed class InvertBooleanReturns : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node) => node.Expression is {} value
            ? node.WithExpression(SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, SyntaxFactory.ParenthesizedExpression(value))) : node;
    }
    private sealed class PerturbScalarReturns(string type) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (node.Expression is not {} value) return node;
            ExpressionSyntax result = type == "float"
                ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, SyntaxFactory.ParenthesizedExpression(value))
                : SyntaxFactory.CheckedExpression(SyntaxKind.UncheckedExpression, SyntaxFactory.BinaryExpression(SyntaxKind.AddExpression,
                    SyntaxFactory.ParenthesizedExpression(value), SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1))));
            return node.WithExpression(result);
        }
    }
}
