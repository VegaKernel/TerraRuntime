using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SourceProbe;

internal sealed record ScalarField(string Symbol, string Target, string Type);
internal sealed record ScalarCase(Dictionary<string, JsonElement> State, JsonElement[] Arguments, int RngSeed);
internal sealed record ScalarRequest(int SchemaVersion, AnalysisRequest Analysis, ScalarField[] Fields, ScalarCase[] Cases);
internal sealed record ScalarPlan(ScalarRequest Request, CompilationContext Context, IMethodSymbol Method,
    MethodDeclarationSyntax Syntax, SemanticModel Model, IReadOnlyDictionary<IFieldSymbol, ScalarField> Fields,
    string Candidate, string Carrier, InventoryResult Inventory);

// Offline candidate generation only. The operation whitelist is deliberately much
// smaller than the inventory: a resolved symbol is not permission to translate it.
internal static class ScalarCandidate
{
    internal static ScalarPlan Plan(CompilationContext context, ScalarRequest request)
    {
        if (request.SchemaVersion != 1 || request.Analysis.SchemaVersion != 1 || request.Analysis.Entries.Length != 1 ||
            request.Analysis.MaxDepth != 0 || request.Analysis.MaxMethods != 1 ||
            request.Fields.Length is < 1 or > 32 || request.Cases.Length is < 1 or > 4096)
            throw new InvalidDataException("Scalar candidate request bounds differ");
        var inventory = SemanticInventory.Analyze(context, request.Analysis with { MaxDepth = 0, MaxMethods = 1 });
        if (!inventory.Accepted || !inventory.SourceClosureComplete || inventory.Methods.Count != 1 ||
            inventory.Methods[0].Uncertainties.Count != 0)
            throw new InvalidDataException("Entry lacks a closed, qualified semantic method inventory");
        var entry = request.Analysis.Entries[0];
        var tree = context.Trees[entry.Path]; var model = context.Compilation.GetSemanticModel(tree);
        var matches = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(node => (Node: node, Symbol: model.GetDeclaredSymbol(node)))
            .Where(p => p.Symbol is { } s && s.ContainingType.ToDisplayString() == entry.Type && s.Name == entry.Method &&
                s.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)).SequenceEqual(entry.ParameterTypes)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Entry is not unique");
        var (syntax, method) = (matches[0].Node, matches[0].Symbol!);
        if (method.IsStatic || method.IsVirtual || method.IsOverride || method.IsSealed || method.IsAbstract || method.IsAsync || method.IsGenericMethod ||
            method.MethodKind != MethodKind.Ordinary || method.GetAttributes().Length != 0 || method.GetReturnTypeAttributes().Length != 0 ||
            !method.ReturnsVoid || method.ContainingType.TypeKind != TypeKind.Class || method.ContainingType.IsGenericType ||
            syntax.Body is null || syntax.Span.Length > 65536 ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.GetAttributes().Length != 0 || !Scalar(p.Type) || !Identifier(p.Name) || p.Name == "__state"))
            throw new InvalidDataException("Only bounded instance void methods with by-value bool/int parameters are admitted");
        var fields = new Dictionary<IFieldSymbol, ScalarField>(SymbolEqualityComparer.Default);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in request.Fields)
        {
            if (map.Symbol.Length > 512 || !Identifier(map.Target) || !targets.Add(map.Target) || method.Parameters.Any(p => p.Name == map.Target))
                throw new InvalidDataException("Invalid, duplicate or parameter-colliding state target");
            var field = method.ContainingType.GetMembers().OfType<IFieldSymbol>().SingleOrDefault(f => Identity(f) == map.Symbol);
            if (field is null || field.IsStatic || field.IsConst || field.IsReadOnly || field.IsVolatile ||
                !Scalar(field.Type) || Type(field.Type) != map.Type || !fields.TryAdd(field, map))
                throw new InvalidDataException("Field mapping must name an exact mutable own-instance bool/int field and type");
        }
        var used = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        var written = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        bool AdmitField(IFieldReferenceOperation field)
        {
            if (!fields.ContainsKey(field.Field) || field.Instance is not IInstanceReferenceOperation
                { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }) return false;
            used.Add(field.Field); return true;
        }
        bool AdmitAssignment(ISimpleAssignmentOperation assignment)
        {
            if (assignment.IsRef || assignment.Target is not IFieldReferenceOperation field || !AdmitField(field)) return false;
            written.Add(field.Field); return true;
        }
        int operations = 0;
        void Inspect(IOperation operation)
        {
            if (++operations > 2048) throw new InvalidDataException("Scalar operation budget exceeded");
            bool admitted = operation switch
            {
                IMethodBodyOperation or IBlockOperation or IExpressionStatementOperation or IEmptyOperation => true,
                IConditionalOperation { Type: null } => true,
                ISimpleAssignmentOperation a => AdmitAssignment(a),
                IFieldReferenceOperation f => AdmitField(f),
                IInstanceReferenceOperation i => i.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance,
                IParameterReferenceOperation p => SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, method) && p.Parameter.RefKind == RefKind.None,
                ILiteralOperation l => l.Type is { } type && Scalar(type),
                IReturnOperation r => r.ReturnedValue is null,
                IConversionOperation c => c.Conversion.IsIdentity && c.OperatorMethod is null && !c.IsTryCast && !c.IsChecked,
                IBinaryOperation b => b.OperatorMethod is null && !b.IsLifted && !b.IsChecked && b.OperatorKind is
                    BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or BinaryOperatorKind.GreaterThan or
                    BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                    BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.And or BinaryOperatorKind.Or,
                IUnaryOperation u => u.OperatorMethod is null && !u.IsLifted && !u.IsChecked && u.OperatorKind == UnaryOperatorKind.Not,
                _ => false
            };
            if (!admitted) throw new InvalidDataException("Unsupported semantic operation: " + operation.Kind);
            foreach (var child in operation.ChildOperations) Inspect(child);
        }
        Inspect(model.GetOperation(syntax) ?? throw new InvalidDataException("Missing bound method body"));
        if (fields.Keys.Any(f => !used.Contains(f))) throw new InvalidDataException("Unused mapping is not an observed field capability");
        if (fields.Keys.Any(f => !written.Contains(f))) throw new InvalidDataException("Read-only state input mapping is not admitted by this prototype");
        foreach (var item in request.Cases)
        {
            if (item.State.Count != fields.Count || !targets.SetEquals(item.State.Keys) || item.Arguments.Length != method.Parameters.Length)
                throw new InvalidDataException("Case must supply every mapped state field and exact arguments");
            foreach (var map in fields.Values) Value(item.State[map.Target], map.Type);
            for (int i = 0; i < method.Parameters.Length; i++) Value(item.Arguments[i], Type(method.Parameters[i].Type));
        }
        var rewriter = new Rewrite(model, fields);
        var generated = syntax.WithBody((BlockSyntax)rewriter.Visit(syntax.Body)!).WithExpressionBody(null).WithSemicolonToken(default)
            .WithIdentifier(SyntaxFactory.Identifier("Run")).WithAttributeLists(default)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(syntax.ParameterList.WithParameters(syntax.ParameterList.Parameters.Insert(0,
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("__state")).WithType(SyntaxFactory.ParseTypeName("CandidateState"))
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword))))));
        string state = string.Join("\n", fields.Values.Select(f => $"public {f.Type} {f.Target};"));
        string candidate = "namespace GeneratedCandidate { public struct CandidateState {\n" + state +
            "\n} public static class Candidate {\n" + generated.NormalizeWhitespace().ToFullString() + "\n} }\n";
        string carrierFields = string.Join("\n", fields.Keys.Select(f => $"public {Type(f.Type)} {f.Name};"));
        // The cache-only proper carrier keeps the original method body unchanged;
        // it is not described as compiling the entire original NPC source unit.
        string carrier = "namespace OriginalMethodCarrier { public class Carrier {\n" + carrierFields + "\n" +
            syntax.WithAttributeLists(default).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .NormalizeWhitespace().ToFullString() + "\n} }\n";
        return new(request, context, method, syntax, model, fields, candidate, carrier, inventory);
    }

    internal static byte[] Compile(string candidate, string carrier)
    {
        string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(candidate + carrier)))[..16];
        var compilation = CSharpCompilation.Create("ScalarCandidate_" + identity,
            [CSharpSyntaxTree.ParseText(candidate), CSharpSyntaxTree.ParseText(carrier)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, checkOverflow: false, deterministic: true));
        using var stream = new MemoryStream(); var result = compilation.Emit(stream);
        if (!result.Success) throw new InvalidDataException("Carrier/candidate compilation failed: " +
            string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        return stream.ToArray();
    }

    internal static string OmitWrite(ScalarPlan plan, string target)
    {
        var tree = CSharpSyntaxTree.ParseText(plan.Candidate);
        return new Omit(target).Visit(tree.GetRoot())!.NormalizeWhitespace().ToFullString();
    }
    internal static object Value(JsonElement value, string type) => type switch
    {
        "int" when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) => number,
        "bool" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
        _ => throw new InvalidDataException("Case scalar JSON type differs: " + type)
    };
    internal static string Identity(IFieldSymbol field) => field.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    private static string Type(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    private static bool Scalar(ITypeSymbol type) => type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Boolean;
    private static bool Identifier(string name) => name.Length is > 0 and <= 128 && SyntaxFacts.IsValidIdentifier(name) && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None &&
        name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private sealed class Rewrite(SemanticModel model, IReadOnlyDictionary<IFieldSymbol, ScalarField> fields) : CSharpSyntaxRewriter
    {
        private SyntaxNode? Field(ExpressionSyntax node)
        {
            if (model.GetSymbolInfo(node).Symbol is IFieldSymbol field && fields.TryGetValue(field, out var map))
                return SyntaxFactory.ParseExpression("__state." + map.Target).WithTriviaFrom(node);
            return null;
        }
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Field(node) ?? base.VisitIdentifierName(node);
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) => Field(node) ?? base.VisitMemberAccessExpression(node);
    }
    private sealed class Omit(string target) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) => node.Expression is AssignmentExpressionSyntax
            { Left: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "__state" }, Name.Identifier.ValueText: var name } } && name == target
            ? SyntaxFactory.EmptyStatement() : base.VisitExpressionStatement(node);
    }
}
