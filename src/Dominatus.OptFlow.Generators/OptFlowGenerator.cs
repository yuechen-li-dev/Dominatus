#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Dominatus.OptFlow.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class OptFlowGenerator : IIncrementalGenerator
{
    private const string FlowAttribute = "Dominatus.OptFlow.DominatusFlowAttribute";
    private const string StateAttribute = "Dominatus.OptFlow.DominatusStateAttribute";
    private const string FlowDefinition = "Dominatus.OptFlow.FlowDefinition";
    private const string AiCtx = "Dominatus.Core.Runtime.AiCtx";
    private const string AiStep = "Dominatus.Core.Nodes.AiStep";

    private static readonly DiagnosticDescriptor[] Rules =
    {
        Rule("DOMFLOW001", "Flow factory must be partial", "Flow factory '{0}' must be partial."),
        Rule("DOMFLOW002", "Flow factory must return FlowDefinition", "Flow factory '{0}' must return exactly FlowDefinition."),
        Rule("DOMFLOW003", "Containing type is unsupported", "Containing type '{0}' must be a partial static non-generic type."),
        Rule("DOMFLOW004", "Generated flow has no states", "Generated flow '{0}' declares no states."),
        Rule("DOMFLOW005", "Generated flow has no root", "Generated flow '{0}' has no root state."),
        Rule("DOMFLOW006", "Generated flow has multiple roots", "Generated flow '{0}' has multiple root states."),
        Rule("DOMFLOW007", "Duplicate durable state ID", "State ID '{0}' is declared more than once."),
        Rule("DOMFLOW008", "Invalid state method signature", "State method '{0}' must return IEnumerator<AiStep> and accept AiCtx first."),
        Rule("DOMFLOW009", "Unsupported factory parameter", "Factory parameter '{0}' is unsupported; use an ordinary by-value, non-ref-like parameter."),
        Rule("DOMFLOW010", "Generic state method", "Annotated state method '{0}' may not be generic."),
        Rule("DOMFLOW011", "Overloaded annotated state method", "Annotated state method '{0}' may not be overloaded."),
        Rule("DOMFLOW012", "States collision", "Generated States class collides with authored member '{0}'."),
        Rule("DOMFLOW013", "Multiple flow factories", "Containing type '{0}' has multiple generated flow factories."),
        Rule("DOMFLOW014", "Blank durable ID", "Flow and state IDs must be non-blank."),
        Rule("DOMFLOW015", "State parameters mismatch", "State method '{0}' parameters must exactly match the factory parameters after AiCtx."),
        Rule("DOMFLOW016", "Flow factory must be static", "Flow factory '{0}' must be static."),
        Rule("DOMFLOW017", "State method must be static", "State method '{0}' must be static."),
        Rule("DOMFLOW018", "Generated state member collision", "Generated States member '{0}' collides with another generated state member."),
        Rule("DOMFLOW019", "Generic containing type", "Containing type '{0}' may not be generic."),
        Rule("DOMFLOW020", "Factory already implemented", "Flow factory '{0}' must be declaration-only.")
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var factories = context.SyntaxProvider.ForAttributeWithMetadataName(
            FlowAttribute,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol);
        context.RegisterSourceOutput(factories.Combine(context.CompilationProvider), static (spc, pair) => Generate(spc, pair.Left, pair.Right));
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "Dominatus.OptFlow", DiagnosticSeverity.Error, true);

    private static void Generate(SourceProductionContext context, IMethodSymbol factory, Compilation compilation)
    {
        var type = factory.ContainingType;
        var flowFactories = type.GetMembers().OfType<IMethodSymbol>().Where(m => HasAttribute(m, FlowAttribute)).ToArray();
        if (!SymbolEqualityComparer.Default.Equals(flowFactories.OrderBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(), factory)) return;
        if (flowFactories.Length != 1) { Report(context, Rules[12], factory, type.Name); return; }

        var invalid = false;
        if (!factory.IsStatic) { Report(context, Rules[15], factory, factory.Name); invalid = true; }
        if (!factory.IsPartialDefinition) { Report(context, Rules[0], factory, factory.Name); invalid = true; }
        if (factory.PartialImplementationPart is not null) { Report(context, Rules[19], factory, factory.Name); invalid = true; }
        if (factory.IsGenericMethod) { Report(context, Rules[1], factory, factory.Name); invalid = true; }
        if (factory.ReturnType.ToDisplayString() != FlowDefinition) { Report(context, Rules[1], factory, factory.Name); invalid = true; }
        if (type.TypeParameters.Length != 0) { Report(context, Rules[18], factory, type.Name); invalid = true; }
        if (!type.IsStatic || !IsPartial(type)) { Report(context, Rules[2], factory, type.Name); invalid = true; }
        if (type.GetMembers("States").Length != 0) { Report(context, Rules[11], factory, "States"); invalid = true; }
        foreach (var parameter in factory.Parameters)
            if (parameter.RefKind != RefKind.None || parameter.Type.IsRefLikeType || parameter.Type.TypeKind is TypeKind.Pointer or TypeKind.FunctionPointer)
            { Report(context, Rules[8], parameter, parameter.Name); invalid = true; }

        var flow = GetAttribute(factory, FlowAttribute);
        var flowId = GetString(flow, 0);
        if (string.IsNullOrWhiteSpace(flowId)) { Report(context, Rules[13], factory, factory.Name); invalid = true; }

        var states = type.GetMembers().OfType<IMethodSymbol>().Where(m => HasAttribute(m, StateAttribute)).ToArray();
        if (states.Length == 0) { Report(context, Rules[3], factory, factory.Name); invalid = true; }
        var roots = states.Where(s => GetBool(GetAttribute(s, StateAttribute), "Root")).ToArray();
        if (roots.Length == 0) { Report(context, Rules[4], factory, factory.Name); invalid = true; }
        if (roots.Length > 1) { foreach (var root in roots) Report(context, Rules[5], root, factory.Name); invalid = true; }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var overload in states.GroupBy(state => state.Name, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            foreach (var state in overload) Report(context, Rules[10], state, state.Name);
            invalid = true;
        }
        foreach (var state in states)
        {
            var attribute = GetAttribute(state, StateAttribute);
            var id = GetString(attribute, 0);
            if (string.IsNullOrWhiteSpace(id)) { Report(context, Rules[13], state, state.Name); invalid = true; }
            else if (!ids.Add(id!)) { Report(context, Rules[6], state, id!); invalid = true; }
            if (!names.Add(state.Name)) { Report(context, Rules[17], state, state.Name); invalid = true; }
            if (!state.IsStatic) { Report(context, Rules[16], state, state.Name); invalid = true; }
            if (state.IsGenericMethod) { Report(context, Rules[9], state, state.Name); invalid = true; }
            if (state.IsAbstract || (state.IsPartialDefinition && state.PartialImplementationPart is null))
            { Report(context, Rules[7], state, state.Name); invalid = true; }
            if (!IsStateReturn(state) || state.Parameters.Length == 0 || state.Parameters[0].Type.ToDisplayString() != AiCtx)
            { Report(context, Rules[7], state, state.Name); invalid = true; continue; }
            if (state.Parameters.Length > 1 && !MatchesFactoryParameters(state.Parameters.Skip(1).ToArray(), factory.Parameters))
            { Report(context, Rules[14], state, state.Name); invalid = true; }
        }
        if (invalid) return;

        var ordered = states.OrderByDescending(s => GetBool(GetAttribute(s, StateAttribute), "Root"))
            .ThenBy(s => GetString(GetAttribute(s, StateAttribute), 0), StringComparer.Ordinal).ToArray();
        var source = Render(factory, type, flow!, flowId!, ordered);
        context.AddSource("Dominatus.OptFlow." + Sanitize(type.ToDisplayString()) + ".g.cs", source);
    }

    private static bool MatchesFactoryParameters(IReadOnlyList<IParameterSymbol> states, ImmutableArray<IParameterSymbol> factory) =>
        states.Count == factory.Length && states.Zip(factory, (state, parameter) => state.RefKind == parameter.RefKind && SymbolEqualityComparer.Default.Equals(state.Type, parameter.Type)).All(x => x);

    private static bool IsStateReturn(IMethodSymbol state)
    {
        if (state.ReturnType is not INamedTypeSymbol named || named.Name != "IEnumerator" || named.ContainingNamespace.ToDisplayString() != "System.Collections.Generic" || named.TypeArguments.Length != 1) return false;
        return named.TypeArguments[0].ToDisplayString() == AiStep;
    }

    private static string Render(IMethodSymbol factory, INamedTypeSymbol type, AttributeData flow, string flowId, IMethodSymbol[] states)
    {
        var b = new StringBuilder("#nullable enable\n// <auto-generated/>\n");
        if (!type.ContainingNamespace.IsGlobalNamespace) b.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).Append(";\n\n");
        b.Append(Access(factory.ContainingType.DeclaredAccessibility)).Append(" static partial class ").Append(Escape(type.Name)).Append("\n{\n");
        b.Append("    public static class States\n    {\n");
        foreach (var state in states)
            b.Append("        public static global::Dominatus.Core.StateId ").Append(Escape(state.Name)).Append(" { get; } = global::Dominatus.Core.StateId.Of(\"").Append(EscapeString(GetString(GetAttribute(state, StateAttribute), 0)!)).Append("\");\n");
        b.Append("    }\n\n    ").Append(Access(factory.DeclaredAccessibility)).Append(" static partial global::Dominatus.OptFlow.FlowDefinition ").Append(Escape(factory.Name)).Append("(");
        b.Append(string.Join(", ", factory.Parameters.Select(RenderParameter))).Append(")\n    {\n");
        foreach (var state in states)
        {
            b.Append("        var __state_").Append(Sanitize(state.Name)).Append(" = global::Dominatus.OptFlow.Flow.State(States.").Append(Escape(state.Name)).Append(", ");
            if (state.Parameters.Length == 1) b.Append(Escape(state.Name));
            else b.Append("ctx => ").Append(Escape(state.Name)).Append("(ctx").Append(factory.Parameters.Length == 0 ? "" : ", " + string.Join(", ", factory.Parameters.Select(p => Escape(p.Name)))).Append(")");
            b.Append(");\n");
        }
        var root = states.Single(s => GetBool(GetAttribute(s, StateAttribute), "Root"));
        b.Append("        return global::Dominatus.OptFlow.Flow.Define(\"").Append(EscapeString(flowId)).Append("\", __state_").Append(Sanitize(root.Name)).Append(", new global::Dominatus.OptFlow.FlowState[] { ");
        b.Append(string.Join(", ", states.Select(s => "__state_" + Sanitize(s.Name))));
        b.Append(" }, new global::Dominatus.Core.Hfsm.HfsmOptions { KeepRootFrame = ").Append(GetBool(flow, "KeepRootFrame") ? "true" : "false")
            .Append(", InterruptScanIntervalSeconds = ").Append(Float(GetFloat(flow, "InterruptScanIntervalSeconds"))).Append(", TransitionScanIntervalSeconds = ").Append(Float(GetFloat(flow, "TransitionScanIntervalSeconds"))).Append(" });\n    }\n}\n");
        return b.ToString();
    }

    private static string RenderParameter(IParameterSymbol p) => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " " + Escape(p.Name);
    private static string Float(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
    private static string Access(Accessibility a) => a == Accessibility.Public ? "public" : a == Accessibility.Internal ? "internal" : a == Accessibility.Private ? "private" : "internal";
    private static string Escape(string value) => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ? "@" + value : value;
    private static string EscapeString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Sanitize(string value) => new string(value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword));
    private static bool HasAttribute(ISymbol symbol, string name) => GetAttribute(symbol, name) is not null;
    private static AttributeData? GetAttribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == name);
    private static string? GetString(AttributeData? a, int index) => a?.ConstructorArguments.Length > index ? a.ConstructorArguments[index].Value as string : null;
    private static bool GetBool(AttributeData? a, string name) => a?.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as bool? ?? false;
    private static float GetFloat(AttributeData? a, string name) => a?.NamedArguments.FirstOrDefault(x => x.Key == name).Value.Value as float? ?? 0f;
    private static void Report(SourceProductionContext context, DiagnosticDescriptor rule, ISymbol symbol, params object[] args) => context.ReportDiagnostic(Diagnostic.Create(rule, symbol.Locations.FirstOrDefault(), args));
}
