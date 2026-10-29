using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SourceProbe;

internal sealed record StateField(string[] Path, int? Index, string Target, string Type, string Access);
internal sealed record StateRequest(int SchemaVersion, AnalysisRequest Analysis, string ReturnType, StateField[] Fields, ScalarCase[] Cases);
internal sealed record StatePath(StateField Map, IFieldSymbol[] Symbols);
internal sealed record StatePlan(StateRequest Request, CompilationContext Context, IMethodSymbol Method,
    MethodDeclarationSyntax Syntax, StatePath[] Paths, string Candidate, string Carrier, InventoryResult Inventory);

// The third capability profile belongs to the same offline candidate engine.
// It maps exact symbols, not syntactic dotted strings or arbitrary object graphs.
internal static partial class ScalarCandidate
{
    internal const string VectorType = "Microsoft.Xna.Framework.Vector2";
    internal static bool StateScalar(ITypeSymbol type) => ScalarValueCandidate.Scalar(type);
    internal static bool IsVector(ITypeSymbol type) => Type(type) == VectorType && type is INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRefLikeType: false, IsGenericType: false };

    internal static StatePlan PlanState(CompilationContext context, StateRequest request)
    {
        if (request.SchemaVersion != 3 || request.Analysis.SchemaVersion != 1 || request.Analysis.Entries.Length != 1 ||
            request.Analysis.MaxDepth != 0 || request.Analysis.MaxMethods != 1 ||
            request.Fields.Length is < 1 or > 32 || request.Cases.Length is < 1 or > 4096)
            throw new InvalidDataException("State candidate bounds differ");
        var inventory = SemanticInventory.Analyze(context, request.Analysis);
        if (!inventory.Accepted || inventory.Methods.Count != 1 || inventory.Methods[0].Calls.Count != 0 ||
            inventory.Methods[0].Uncertainties.Any(u => u != "array-alias-and-index-effects-not-proven"))
            throw new InvalidDataException("State entry has an unresolved effect frontier");
        var entry = request.Analysis.Entries[0];
        var model = context.Compilation.GetSemanticModel(context.Trees[entry.Path]);
        var matches = context.Trees[entry.Path].GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(n => (Node: n, Method: model.GetDeclaredSymbol(n))).Where(p => p.Method is { } m &&
                m.ContainingType.ToDisplayString() == entry.Type && m.Name == entry.Method &&
                m.Parameters.Select(p => Type(p.Type)).SequenceEqual(entry.ParameterTypes)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("State entry is not unique");
        var (syntax, method) = (matches[0].Node, matches[0].Method!);
        if (method.IsStatic || method.IsVirtual || method.IsOverride || method.IsSealed || method.IsAbstract || method.IsAsync ||
            method.IsGenericMethod || method.IsVararg || method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.MethodKind != MethodKind.Ordinary || method.GetAttributes().Length != 0 ||
            method.GetReturnTypeAttributes().Length != 0 || Type(method.ReturnType) != request.ReturnType ||
            !(method.ReturnsVoid || StateScalar(method.ReturnType)) || method.ContainingType.TypeKind != TypeKind.Class ||
            method.ContainingType.IsGenericType || syntax.Body is null || syntax.Span.Length > 65536 ||
            syntax.Modifiers.Any(SyntaxKind.UnsafeKeyword) || syntax.DescendantNodes().OfType<UnsafeStatementSyntax>().Any() ||
            syntax.DescendantNodes().Any(n => n is CheckedStatementSyntax or CheckedExpressionSyntax) || method.Parameters.Length > 32 ||
            syntax.Ancestors().OfType<TypeDeclarationSyntax>().Any(t => t.Modifiers.Any(SyntaxKind.UnsafeKeyword)) ||
            syntax.DescendantNodes().OfType<BaseExpressionSyntax>().Any() ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.GetAttributes().Length != 0 || !Identifier(p.Name) ||
                p.Name == "__state" || !(StateScalar(p.Type) || IsVector(p.Type))))
            throw new InvalidDataException("Unsupported state method shape");
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var paths = new List<StatePath>();
        foreach (var map in request.Fields)
        {
            if (map.Path.Length is < 1 or > 2 || map.Path.Any(s => s.Length > 512) ||
                !Identifier(map.Target) || map.Target == "__state" || !targets.Add(map.Target) ||
                map.Access is not ("readOnly" or "readWrite") || map.Index is < 0 or > 3)
                throw new InvalidDataException("Invalid or aliased state path");
            var roots = new List<IFieldSymbol>();
            for (INamedTypeSymbol? type = method.ContainingType; type is not null; type = type.BaseType)
                roots.AddRange(type.GetMembers().OfType<IFieldSymbol>().Where(f => Identity(f) == map.Path[0]));
            if (roots.Count != 1) throw new InvalidDataException("Exact instance root field absent");
            IFieldSymbol root = roots[0]; var symbols = new List<IFieldSymbol> { root };
            if (map.Path.Length == 2)
            {
                if (!IsVector(root.Type) || map.Index.HasValue) throw new InvalidDataException("Only a Vector2 field chain is admitted");
                var leaf = ((INamedTypeSymbol)root.Type).GetMembers().OfType<IFieldSymbol>().SingleOrDefault(f => Identity(f) == map.Path[1]);
                if (leaf is null || leaf.Name is not ("X" or "Y") || leaf.Type.SpecialType != SpecialType.System_Single)
                    throw new InvalidDataException("Exact Vector2 scalar leaf absent");
                symbols.Add(leaf);
            }
            ITypeSymbol valueType = symbols[^1].Type;
            if (map.Index.HasValue)
            {
                if (map.Path.Length != 1 || root.Name is not ("ai" or "localAI") ||
                    valueType is not IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Single } array)
                    throw new InvalidDataException("Only fixed slots of a float array are admitted");
                valueType = array.ElementType;
            }
            if (symbols.Any(f => f.IsStatic || f.IsConst || f.IsReadOnly || f.IsVolatile || f.GetAttributes().Length != 0) ||
                !StateScalar(valueType) || Type(valueType) != map.Type || paths.Any(p => SamePath(p, symbols, map.Index)))
                throw new InvalidDataException("Unsupported or duplicate semantic state capability");
            if (map.Index.HasValue && map.Access == "readWrite") RequireFreshArray(context, method.ContainingType, root);
            paths.Add(new(map, symbols.ToArray()));
        }
        if (paths.Select(p => p.Symbols[0]).Distinct(SymbolEqualityComparer.Default).GroupBy(f => f!.Name).Any(g => g.Count() != 1))
            throw new InvalidDataException("Hidden root field cannot be represented by the carrier");
        var used = new HashSet<string>(); var written = new HashSet<string>();
        bool Match(IOperation op, out StatePath? result) => TryStatePath(op, paths, out result);
        bool Write(IOperation op)
        {
            if (Match(op, out var path))
            {
                if (path!.Map.Access != "readWrite") return false;
                used.Add(path.Map.Target); written.Add(path.Map.Target); return true;
            }
            return op is ILocalReferenceOperation l && StateScalar(l.Local.Type) && l.Local.RefKind == RefKind.None ||
                op is IParameterReferenceOperation p && StateScalar(p.Parameter.Type) && SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, method);
        }
        int count = 0;
        void Inspect(IOperation op)
        {
            if (++count > 2048) throw new InvalidDataException("State operation bound exceeded");
            if (Match(op, out var path)) { used.Add(path!.Map.Target); return; }
            if (op is IFieldReferenceOperation field && field.Instance is IParameterReferenceOperation parameter &&
                SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, method) && IsVector(parameter.Parameter.Type) &&
                field.Field.Name is "X" or "Y" && field.Field.Type.SpecialType == SpecialType.System_Single &&
                SymbolEqualityComparer.Default.Equals(field.Field.ContainingType, parameter.Parameter.Type)) return;
            bool allowed = op switch
            {
                IMethodBodyOperation or IBlockOperation or IExpressionStatementOperation or IEmptyOperation => true,
                IConditionalOperation { Type: null } => true,
                IReturnOperation r => method.ReturnsVoid ? r.ReturnedValue is null : r.ReturnedValue is { Type: { } t } && SymbolEqualityComparer.Default.Equals(t, method.ReturnType),
                ISimpleAssignmentOperation a => !a.IsRef && Write(a.Target),
                ICompoundAssignmentOperation a => Write(a.Target) && a.OperatorMethod is null && !a.IsLifted && !a.IsChecked &&
                    a.InConversion.IsIdentity && a.OutConversion.IsIdentity && a.OperatorKind is BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply,
                IIncrementOrDecrementOperation i => Write(i.Target) && i.OperatorMethod is null && !i.IsLifted && !i.IsChecked,
                IVariableDeclarationGroupOperation or IVariableDeclarationOperation or IVariableInitializerOperation => true,
                IVariableDeclaratorOperation d => StateScalar(d.Symbol.Type) && d.Symbol.RefKind == RefKind.None && !d.Symbol.IsConst && Identifier(d.Symbol.Name) && d.Initializer is not null,
                ILocalReferenceOperation l => StateScalar(l.Local.Type) && l.Local.RefKind == RefKind.None,
                IParameterReferenceOperation p => StateScalar(p.Parameter.Type) && SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, method),
                ILiteralOperation l => l.Type is { } t && StateScalar(t),
                IConversionOperation c => c.Conversion.IsIdentity && c.OperatorMethod is null && !c.IsTryCast && !c.IsChecked,
                IBinaryOperation b => b.OperatorMethod is null && !b.IsLifted && !b.IsChecked && StateScalar(b.LeftOperand.Type!) &&
                    SymbolEqualityComparer.Default.Equals(b.LeftOperand.Type, b.RightOperand.Type) && b.OperatorKind is
                    BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
                    BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual or BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                    BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr or BinaryOperatorKind.And or BinaryOperatorKind.Or,
                IUnaryOperation u => u.OperatorMethod is null && !u.IsLifted && !u.IsChecked && u.OperatorKind is UnaryOperatorKind.Not or UnaryOperatorKind.Minus,
                _ => false
            };
            if (!allowed) throw new InvalidDataException("Unsupported state semantic operation: " + op.Kind);
            foreach (var child in op.ChildOperations) Inspect(child);
        }
        Inspect(model.GetOperation(syntax) ?? throw new InvalidDataException("State method body absent"));
        if (paths.Any(p => !used.Contains(p.Map.Target) || p.Map.Access == "readWrite" && !written.Contains(p.Map.Target)))
            throw new InvalidDataException("Unused or unwritten state capability");
        foreach (var item in request.Cases)
        {
            if (!targets.SetEquals(item.State.Keys) || item.Arguments.Length != method.Parameters.Length) throw new InvalidDataException("Incomplete state case");
            foreach (var p in paths) _ = ScalarValueCandidate.Value(item.State[p.Map.Target], p.Map.Type);
            for (int i = 0; i < method.Parameters.Length; i++) ValidateStateArgument(item.Arguments[i], Type(method.Parameters[i].Type));
        }
        var rewritten = (MethodDeclarationSyntax)new StateRewrite(model, paths).Visit(syntax)!;
        rewritten = rewritten.WithAttributeLists(default).WithIdentifier(SyntaxFactory.Identifier("Run"))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithParameterList(rewritten.ParameterList.WithParameters(rewritten.ParameterList.Parameters.Insert(0,
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("__state")).WithType(SyntaxFactory.ParseTypeName("CandidateState"))
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword))))));
        string vector = "namespace Microsoft.Xna.Framework { public struct Vector2 { public float X; public float Y; } }\n";
        string candidate = "namespace GeneratedCandidate { using Microsoft.Xna.Framework; public struct CandidateState {\n" +
            string.Join("\n", paths.Select(p => $"public {p.Map.Type} {p.Map.Target};")) + "\n} public static class Candidate {\n" + rewritten.NormalizeWhitespace() + "\n} }";
        var rootFields = paths.Select(p => p.Symbols[0]).Distinct<IFieldSymbol>(SymbolEqualityComparer.Default);
        string declarations = string.Join("\n", rootFields.Select(f => $"public {Type(f.Type)} {f.Name}" + (f.Type is IArrayTypeSymbol ? " = new float[4]" : "") + ";"));
        string carrier = vector + "namespace OriginalMethodCarrier { using Microsoft.Xna.Framework; public class Carrier {\n" + declarations + "\n" +
            syntax.WithAttributeLists(default).WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword))).NormalizeWhitespace() + "\n} }";
        return new(request, context, method, syntax, paths.ToArray(), candidate, carrier, inventory);
    }

    internal static void ValidateStateArgument(JsonElement value, string type)
    {
        if (type != VectorType) { _ = ScalarValueCandidate.Value(value, type); return; }
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "x", "y" }))
            throw new InvalidDataException("Vector2 argument requires exact finite x/y bit values");
        _ = ScalarValueCandidate.Value(value.GetProperty("x"), "float"); _ = ScalarValueCandidate.Value(value.GetProperty("y"), "float");
    }

    // An array element is not an owned scalar merely because its index is fixed.
    // A fresh per-instance allocation and a non-publishing constructor chain are
    // required before allowing writes; metadata-only constructors stay opaque.
    private static void RequireFreshArray(CompilationContext context, INamedTypeSymbol actor, IFieldSymbol root)
    {
        if (root.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } initializer } ||
            context.Compilation.GetSemanticModel(initializer.SyntaxTree).GetOperation(initializer) is not IArrayCreationOperation array ||
            array.DimensionSizes.Length != 1 || !array.DimensionSizes[0].ConstantValue.HasValue || array.DimensionSizes[0].ConstantValue.Value is not 4)
            throw new InvalidDataException("Array writes require a source-qualified fresh four-float initializer");
        for (INamedTypeSymbol? type = actor; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            if (type.IsGenericType) throw new InvalidDataException("Generic constructor ownership remains opaque");
            var constructors = type.InstanceConstructors.Where(c => c.Parameters.Length == 0).ToArray();
            if (constructors.Length != 1) throw new InvalidDataException("Exact fresh parameterless constructor absent");
            var constructor = constructors[0];
            if (constructor.IsImplicitlyDeclared && type.DeclaringSyntaxReferences.Length > 0) continue;
            if (constructor.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax() is not ConstructorDeclarationSyntax
                    { Initializer: null, Body: { } body, ExpressionBody: null } || constructor.GetAttributes().Length != 0)
                throw new InvalidDataException("Constructor effects or metadata-only ownership remain opaque");
            var model = context.Compilation.GetSemanticModel(body.SyntaxTree);
            foreach (var statement in body.Statements)
            {
                if (statement is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } ||
                    !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                    model.GetOperation(assignment.Left) is not IFieldReferenceOperation { Field.IsStatic: false,
                        Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } } field ||
                    !StateScalar(field.Field.Type) || !model.GetConstantValue(assignment.Right).HasValue)
                    throw new InvalidDataException("Constructor can publish or alias the projected array");
            }
        }
    }

    private static bool SamePath(StatePath p, IReadOnlyList<IFieldSymbol> symbols, int? index) => p.Map.Index == index &&
        p.Symbols.Length == symbols.Count && p.Symbols.Zip(symbols).All(pair => SymbolEqualityComparer.Default.Equals(pair.First, pair.Second));

    internal static bool TryStatePath(IOperation op, IReadOnlyList<StatePath> paths, out StatePath? result)
    {
        int? index = null;
        if (op is IArrayElementReferenceOperation array)
        {
            if (array.Indices.Length != 1 || array.Indices[0] is not ILiteralOperation { ConstantValue.HasValue: true } literal ||
                literal.ConstantValue.Value is not int slot || slot is < 0 or > 3) { result = null; return false; }
            index = slot; op = array.ArrayReference;
        }
        var symbols = new List<IFieldSymbol>();
        while (op is IFieldReferenceOperation field) { symbols.Insert(0, field.Field); op = field.Instance!; }
        result = op is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
            ? paths.SingleOrDefault(p => SamePath(p, symbols, index)) : null;
        return result is not null;
    }

    private sealed class StateRewrite(SemanticModel model, IReadOnlyList<StatePath> paths) : CSharpSyntaxRewriter
    {
        private SyntaxNode? Map(ExpressionSyntax node) => model.GetOperation(node) is { } op && TryStatePath(op, paths, out var path)
            ? SyntaxFactory.ParseExpression("__state." + path!.Map.Target).WithTriviaFrom(node) : null;
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) => Map(node) ?? base.VisitIdentifierName(node);
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) => Map(node) ?? base.VisitMemberAccessExpression(node);
        public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node) => Map(node) ?? base.VisitElementAccessExpression(node);
    }
}
