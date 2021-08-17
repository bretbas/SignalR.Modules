using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SignalR.Modules.Generator
{
    [Generator]
    public class HubGenerator : ISourceGenerator
    {
        private const string AttributeNamespace = "SignalR.Modules";
        private const string AttributeName = "SignalRModuleHubAttribute";
        private const string AttributeFullName = AttributeNamespace + "." + AttributeName;
        private const string AutoGeneratedFileHeader =
@"//------------------------------------------------------------------------------ 
// <auto-generated> 
// This code was generated by the SignalR.Modules Source Generator. 
// </auto-generated> 
//------------------------------------------------------------------------------";

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver.
            if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
            {
                return;
            }

            // get the added attribute
            INamedTypeSymbol? attributeSymbol = context.Compilation.GetTypeByMetadataName(AttributeFullName);

            foreach (var mainHubClass in receiver.MainHubClasses)
            {
                var moduleHubAttributes = mainHubClass.GetAttributes().Where(att => att.AttributeClass?.ToDisplayString() == AttributeFullName).ToList();

                foreach (var attribute in moduleHubAttributes)
                {
                    var moduleHubType = (INamedTypeSymbol?)attribute.ConstructorArguments[0].Value;

                    if (moduleHubType != null)
                    {
                        var hubSource = ProcessHubClass(mainHubClass, moduleHubType);
                        context.AddSource($"{mainHubClass.Name}_{moduleHubType.Name}", hubSource);

                        if (moduleHubType.BaseType!.IsGenericType)
                        {
                            var typedClientInterface = moduleHubType.BaseType.TypeArguments[0];
                            var hubClientSource = ProcessHubClientClass(mainHubClass, moduleHubType, typedClientInterface);
                            context.AddSource($"{mainHubClass.Name}_{typedClientInterface.Name}Impl", hubClientSource);
                        }
                    }
                }
            }
        }

        private string ProcessHubClass(INamedTypeSymbol mainHubTypeSymbol, INamedTypeSymbol moduleHubTypeSymbol)
        {
            string namespaceName = mainHubTypeSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            var source = new StringBuilder(AutoGeneratedFileHeader);

            source.Append(
$@"
using Microsoft.Extensions.DependencyInjection;

namespace {namespaceName}
{{
    public partial class {mainHubTypeSymbol.Name}
    {{
");
            var moduleHubMethods = moduleHubTypeSymbol.GetMembers()
                .Where(m => m.Kind == SymbolKind.Method && m.DeclaredAccessibility == Accessibility.Public && m.Name != ".ctor")
                .OfType<IMethodSymbol>()
                .ToList();

            foreach (var hubMethod in moduleHubMethods)
            {
                source.Append(ProcessHubMethod(moduleHubTypeSymbol, hubMethod));
            }

            source.Append(@"
    } 
}");
            return source.ToString();
        }

        private string ProcessHubMethod(INamedTypeSymbol moduleHubTypeSymbol, IMethodSymbol methodSymbol)
        {
            return $@"
        public {(methodSymbol.ReturnsVoid ? string.Empty : methodSymbol.ReturnType.ToDisplayString())} {moduleHubTypeSymbol.Name}_{methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})
        {{
            var hub = ServiceProvider.GetRequiredService<{moduleHubTypeSymbol.ToDisplayString()}>();
            {(moduleHubTypeSymbol.BaseType!.IsGenericType ? $"InitModuleHub<{moduleHubTypeSymbol.BaseType.TypeArguments[0].ToDisplayString()}>(hub)" : "InitModuleHub(hub)")};
            {(methodSymbol.ReturnsVoid ? string.Empty : "return ")}hub.{methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(p => p.Name))});
        }}";
        }

        private string ProcessHubClientClass(INamedTypeSymbol mainHubTypeSymbol, INamedTypeSymbol moduleHubTypeSymbol, ITypeSymbol typedClientInterface)
        {
            var namespaceName = typedClientInterface.ContainingNamespace.ToDisplayString();
            var className = $"{mainHubTypeSymbol.Name}_{typedClientInterface.Name}Impl";

            // begin building the generated source
            var source = new StringBuilder(AutoGeneratedFileHeader);

            source.Append(
$@"
using Microsoft.AspNetCore.SignalR;
using SignalR.Modules;
using System.Threading.Tasks;

namespace {namespaceName}
{{
    public class {className} : ClientProxy<{moduleHubTypeSymbol.Name}>, {typedClientInterface.Name}
    {{
        public {className}(IClientProxy clientProxy)
            : base(clientProxy)
        {{}}
");
            var clientMethods = typedClientInterface.GetMembers()
                .Where(m => m.Kind == SymbolKind.Method && m.DeclaredAccessibility == Accessibility.Public && m.Name != ".ctor")
                .OfType<IMethodSymbol>()
                .ToList();

            foreach (var clientMethod in clientMethods)
            {
                source.Append(ProcessHubClientMethod(clientMethod));
            }

            source.Append(@"
    } 
}");
            return source.ToString();
        }

        private string ProcessHubClientMethod(IMethodSymbol methodSymbol)
        {
            return $@"
        public Task {methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})
        {{
            return SendAsync(""{methodSymbol.Name}"", new[] {{ {string.Join(", ", methodSymbol.Parameters.Select(p => p.Name))} }});
        }}";
        }

        /// <summary>
        /// Created on demand before each generation pass.
        /// </summary>
        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<INamedTypeSymbol> MainHubClasses { get; } = new List<INamedTypeSymbol>();

            /// <summary>
            /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation.
            /// </summary>
            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // any field with at least one attribute is a candidate for property generation
                if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count > 0)
                {
                    var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax);

                    if (classSymbol != null && classSymbol.GetAttributes().Any(att => att.AttributeClass?.ToDisplayString() == AttributeFullName))
                    {
                        MainHubClasses.Add(classSymbol);
                    }
                }
            }
        }
    }
}
