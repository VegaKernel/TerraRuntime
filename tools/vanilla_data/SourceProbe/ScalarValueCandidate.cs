using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SourceProbe;

internal sealed record ValueField(string Symbol, string Target, string Type, string Receiver, string Access);
internal sealed record ValueRequest(int SchemaVersion, AnalysisRequest Analysis, string ReturnType, ValueField[] Fields, ScalarCase[] Cases);
internal sealed record ValuePlan(ValueRequest Request, CompilationContext Context, IMethodSymbol Method,
    IReadOnlyDictionary<IFieldSymbol, ValueField> Fields, string Candidate, string Carrier, InventoryResult Inventory);

// Schema2 is a separate narrow capability contract. It deliberately does not
// broaden schema1's mutation contract or admit object graphs, getters or APIs.
internal static class ScalarValueCandidate
{
    internal static ValuePlan Plan(CompilationContext context, ValueRequest request)
    {
        if (request.SchemaVersion != 2 || request.Analysis.SchemaVersion != 1 || request.Analysis.Entries.Length != 1 ||
            request.Analysis.MaxDepth != 0 || request.Analysis.MaxMethods != 1 ||
            request.Fields.Length is < 1 or > 32 || request.Cases.Length is < 1 or > 4096)
            throw new InvalidDataException("Value candidate bounds differ");
        var inventory = SemanticInventory.Analyze(context, request.Analysis);
        if (!inventory.Accepted || !inventory.SourceClosureComplete || inventory.Methods.Count != 1 || inventory.Methods[0].Uncertainties.Count != 0)
            throw new InvalidDataException("Value entry is not a closed qualified inventory");
        var entry = request.Analysis.Entries[0];
        var model = context.Compilation.GetSemanticModel(context.Trees[entry.Path]);
        var matches = context.Trees[entry.Path].GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(n => (Syntax: n, Symbol: model.GetDeclaredSymbol(n))).Where(p => p.Symbol is { } s &&
                s.ContainingType.ToDisplayString() == entry.Type && s.Name == entry.Method &&
                s.Parameters.Select(p => Type(p.Type)).SequenceEqual(entry.ParameterTypes)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Value entry is not unique");
        var (syntax, method) = (matches[0].Syntax, matches[0].Symbol!);
        if (method.IsStatic || method.IsVirtual || method.IsOverride || method.IsSealed || method.IsAbstract || method.IsAsync ||
            method.IsGenericMethod || method.MethodKind != MethodKind.Ordinary || method.GetAttributes().Length != 0 ||
            method.GetReturnTypeAttributes().Length != 0 || !Scalar(method.ReturnType) || Type(method.ReturnType) != request.ReturnType ||
            method.ContainingType.TypeKind != TypeKind.Class || method.ContainingType.IsGenericType || syntax.Body is null || syntax.Span.Length > 65536 ||
            syntax.Modifiers.Any(SyntaxKind.UnsafeKeyword) || syntax.DescendantNodes().OfType<UnsafeStatementSyntax>().Any() ||
            syntax.Ancestors().OfType<TypeDeclarationSyntax>().Any(t => t.Modifiers.Any(SyntaxKind.UnsafeKeyword)) ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.GetAttributes().Length != 0 || !Identifier(p.Name)))
            throw new InvalidDataException("Unsupported value method shape");
        var references = method.Parameters.Where(p => !Scalar(p.Type)).ToArray();
        if (references.Length != 1 || references[0].Type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: false } referenceType)
            throw new InvalidDataException("Exactly one explicit class receiver is required");
        var reference = references[0];
        var maps = new Dictionary<IFieldSymbol, ValueField>(SymbolEqualityComparer.Default);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in request.Fields)
        {
            var owner = map.Receiver == "this" ? method.ContainingType : map.Receiver == reference.Name ? referenceType : null;
            var field = owner?.GetMembers().OfType<IFieldSymbol>().SingleOrDefault(f => ScalarCandidate.Identity(f) == map.Symbol);
            if (map.Access != "readOnly" || map.Symbol.Length > 512 || !Identifier(map.Target) || !targets.Add(map.Target) || field is null || field.IsStatic ||
                field.IsConst || field.IsVolatile || field.GetAttributes().Length != 0 || !Scalar(field.Type) || Type(field.Type) != map.Type || !maps.TryAdd(field, map))
                throw new InvalidDataException("Missing, aliased or non-read-only exact receiver field capability");
        }
        var used = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        bool Field(IFieldReferenceOperation field)
        {
            if (!maps.TryGetValue(field.Field, out var map)) return false;
            bool receiver = map.Receiver == "this" ? field.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } :
                field.Instance is IParameterReferenceOperation p && SymbolEqualityComparer.Default.Equals(p.Parameter, reference);
            if (receiver) used.Add(field.Field);
            return receiver;
        }
        bool Local(ILocalSymbol local) => Scalar(local.Type) && local.RefKind == RefKind.None && !local.IsConst && Identifier(local.Name);
        bool Write(IOperation target) => target is IParameterReferenceOperation p && Scalar(p.Parameter.Type) &&
            SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, method) || target is ILocalReferenceOperation l && Local(l.Local);
        int count = 0;
        void Inspect(IOperation op)
        {
            if (++count > 2048) throw new InvalidDataException("Value operation budget exceeded");
            bool allowed = op switch
            {
                IMethodBodyOperation or IBlockOperation or IExpressionStatementOperation or IEmptyOperation => true,
                IConditionalOperation { Type: null } => true,
                IReturnOperation r => r.ReturnedValue is { Type: { } type } && SymbolEqualityComparer.Default.Equals(type, method.ReturnType),
                ISimpleAssignmentOperation a => !a.IsRef && Write(a.Target),
                ICompoundAssignmentOperation a => Write(a.Target) && a.OperatorMethod is null && !a.IsLifted && !a.IsChecked &&
                    a.InConversion.IsIdentity && a.OutConversion.IsIdentity && a.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply,
                IVariableDeclarationGroupOperation or IVariableDeclarationOperation or IVariableInitializerOperation => true,
                IVariableDeclaratorOperation d => Local(d.Symbol) && d.Initializer is not null,
                ILocalReferenceOperation l => Local(l.Local),
                IFieldReferenceOperation f => Field(f),
                IInstanceReferenceOperation i => i.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance,
                IParameterReferenceOperation p => SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, method) &&
                    (Scalar(p.Parameter.Type) || SymbolEqualityComparer.Default.Equals(p.Parameter, reference) && p.Parent is IFieldReferenceOperation),
                ILiteralOperation l => l.Type is { } type && Scalar(type),
                IConversionOperation c => c.Conversion.IsIdentity && c.OperatorMethod is null && !c.IsTryCast && !c.IsChecked,
                IBinaryOperation b => b.OperatorMethod is null && !b.IsLifted && !b.IsChecked && Scalar(b.LeftOperand.Type!) &&
                    SymbolEqualityComparer.Default.Equals(b.LeftOperand.Type, b.RightOperand.Type) && b.OperatorKind is
                    BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
                    BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual or
                    BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.And or BinaryOperatorKind.Or,
                IUnaryOperation u => u.OperatorMethod is null && !u.IsLifted && !u.IsChecked && u.OperatorKind is UnaryOperatorKind.Not or UnaryOperatorKind.Minus,
                _ => false
            };
            if (!allowed) throw new InvalidDataException("Unsupported value semantic operation: " + op.Kind);
            foreach (var child in op.ChildOperations) Inspect(child);
        }
        Inspect(model.GetOperation(syntax) ?? throw new InvalidDataException("Missing value body"));
        if (maps.Keys.Any(f => !used.Contains(f))) throw new InvalidDataException("Unused value field capability");
        foreach (var item in request.Cases)
        {
            if (item.State.Count != maps.Count || !targets.SetEquals(item.State.Keys) || item.Arguments.Length != method.Parameters.Length)
                throw new InvalidDataException("Incomplete explicit value preimage");
            foreach (var map in maps.Values) _ = Value(item.State[map.Target], map.Type);
            for (int i = 0; i < method.Parameters.Length; i++)
                if (Scalar(method.Parameters[i].Type)) _ = Value(item.Arguments[i], Type(method.Parameters[i].Type));
                else Receiver(item.Arguments[i], reference.Name);
        }
        var generated = syntax.WithBody((BlockSyntax)new Rewrite(model, maps).Visit(syntax.Body)!).WithIdentifier(SyntaxFactory.Identifier("Run"))
            .WithAttributeLists(default).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(syntax.ParameterList.WithParameters(SyntaxFactory.SeparatedList(syntax.ParameterList.Parameters.Where((_, i) => Scalar(method.Parameters[i].Type)))
                .Insert(0, SyntaxFactory.Parameter(SyntaxFactory.Identifier("__state")).WithType(SyntaxFactory.ParseTypeName("CandidateState"))
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InKeyword))))));
        string fields = string.Join("\n", maps.Values.Select(m => $"public {m.Type} {m.Target};"));
        string candidate = "namespace GeneratedCandidate { public struct CandidateState {\n" + fields + "\n} public static class Candidate {\n" + generated.NormalizeWhitespace().ToFullString() + "\n} }\n";
        string own = string.Join("\n", maps.Where(m => m.Value.Receiver == "this").Select(m => $"public {m.Value.Type} {m.Key.Name};"));
        string other = string.Join("\n", maps.Where(m => m.Value.Receiver != "this").Select(m => $"public {m.Value.Type} {m.Key.Name};"));
        var carrierMethod = syntax.WithAttributeLists(default).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(syntax.ParameterList.WithParameters(SyntaxFactory.SeparatedList(syntax.ParameterList.Parameters.Select((p, i) =>
                Scalar(method.Parameters[i].Type) ? p : p.WithType(SyntaxFactory.ParseTypeName("CarrierReceiver"))))));
        string carrier = "namespace OriginalMethodCarrier { public class CarrierReceiver {\n" + other + "\n} public class Carrier {\n" + own + "\n" + carrierMethod.NormalizeWhitespace().ToFullString() + "\n} }\n";
        return new(request, context, method, maps, candidate, carrier, inventory);
    }

    internal static void Receiver(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 || !value.TryGetProperty("receiver", out var receiver) || receiver.GetString() != name)
            throw new InvalidDataException("Argument must name the exact explicit non-null read-only receiver");
    }
    internal static object Value(JsonElement value, string type)
    {
        if (type != "float") return ScalarCandidate.Value(value, type);
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != 1 || !value.TryGetProperty("bits", out var raw) || !raw.TryGetUInt32(out uint bits))
            throw new InvalidDataException("Float requires exact uint32 bits");
        float number = BitConverter.UInt32BitsToSingle(bits);
        if (!float.IsFinite(number)) throw new InvalidDataException("Nonfinite float input is outside the admitted domain");
        return number;
    }
    internal static object Observe(object value) => value is float number ? float.IsFinite(number) ? new Dictionary<string, uint> { ["bits"] = BitConverter.SingleToUInt32Bits(number) } :
        throw new InvalidDataException("Nonfinite float result is outside the admitted domain") : value;
    internal static string Type(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    internal static bool Scalar(ITypeSymbol type) => type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Boolean or SpecialType.System_Single;
    private static bool Identifier(string name) => name.Length is > 0 and <= 128 && name != "__state" && SyntaxFacts.IsValidIdentifier(name) &&
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private sealed class Rewrite(SemanticModel model, IReadOnlyDictionary<IFieldSymbol, ValueField> maps) : CSharpSyntaxRewriter
    {
        private SyntaxNode? Field(ExpressionSyntax node) => model.GetOperation(node) is IFieldReferenceOperation f && maps.TryGetValue(f.Field, out var map) &&
            (map.Receiver == "this" ? f.Instance is IInstanceReferenceOperation : f.Instance is IParameterReferenceOperation p && p.Parameter.Name == map.Receiver)
                ? SyntaxFactory.ParseExpression("__state." + map.Target).WithTriviaFrom(node) : null;
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Field(node) ?? base.VisitIdentifierName(node);
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) => Field(node) ?? base.VisitMemberAccessExpression(node);
    }
    internal static IReadOnlyDictionary<string, string> Mutants(ValuePlan plan)
    {
        var tree = CSharpSyntaxTree.ParseText(plan.Candidate);
        var root = tree.GetRoot();
        var output = new Dictionary<string, string>();
        int i = 0;
        foreach (var statement in root.DescendantNodes().OfType<ExpressionStatementSyntax>().Where(s => s.Expression is AssignmentExpressionSyntax a && !a.IsKind(SyntaxKind.SimpleAssignmentExpression)))
            output.Add("omit-operation-" + i++, root.ReplaceNode(statement, SyntaxFactory.EmptyStatement()).NormalizeWhitespace().ToFullString());
        var body = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Body!;
        if (body.Statements.Count >= 2 && body.Statements[0] is IfStatementSyntax a && body.Statements[1] is IfStatementSyntax b &&
            a.DescendantNodes().OfType<ReturnStatementSyntax>().Any() && b.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
        {
            // Replace by ordinal: structurally equal replacements must not accidentally duplicate a branch.
            var statements = SyntaxFactory.List(body.Statements.Select((s, index) => index == 0 ? b : index == 1 ? a : s));
            output.Add("swap-category-precedence", root.ReplaceNode(body, body.WithStatements(statements)).NormalizeWhitespace().ToFullString());
        }
        if (output.Count is < 1 or > 32) throw new InvalidDataException("Value candidate requires1..32 behavioral mutation controls");
        return output;
    }
}
