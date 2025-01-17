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

            foreach (var entryHubClass in receiver.EntryHubClasses)
            {
                var moduleHubTypes = new List<INamedTypeSymbol>();
                var moduleHubAttributes = entryHubClass.GetAttributes().Where(att => att.AttributeClass?.ToDisplayString() == AttributeFullName).ToList();

                foreach (var attribute in moduleHubAttributes)
                {
                    var moduleHubType = (INamedTypeSymbol?)attribute.ConstructorArguments[0].Value;

                    if (moduleHubType != null)
                    {
                        moduleHubTypes.Add(moduleHubType);

                        var hubSource = ProcessModuleHubClass(entryHubClass, moduleHubType);
                        context.AddSource($"{entryHubClass.Name}_{moduleHubType.Name}.g", hubSource);

                        if (moduleHubType.BaseType!.IsGenericType)
                        {
                            var typedClientInterface = moduleHubType.BaseType.TypeArguments[0];
                            var hubClientSource = ProcessHubClientClass(entryHubClass, moduleHubType, typedClientInterface);
                            context.AddSource($"{entryHubClass.Name}_{typedClientInterface.Name}Impl.g", hubClientSource);
                        }
                    }
                }

                var miscSource = ProcessEntryHubClass(entryHubClass, moduleHubTypes);
                context.AddSource($"{entryHubClass.Name}.g", miscSource);
            }
        }

        private string ProcessModuleHubClass(INamedTypeSymbol entryHubTypeSymbol, INamedTypeSymbol moduleHubTypeSymbol)
        {
            string namespaceName = entryHubTypeSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            var source = new StringBuilder(AutoGeneratedFileHeader);

            source.Append(
$@"
using Microsoft.Extensions.DependencyInjection;

namespace {namespaceName}
{{
    public partial class {entryHubTypeSymbol.Name}
    {{
");
            var moduleHubMethods = moduleHubTypeSymbol.GetMembers()
                .Where(m => m.Kind == SymbolKind.Method && m.DeclaredAccessibility == Accessibility.Public && !m.IsOverride && m.Name != ".ctor")
                .OfType<IMethodSymbol>()
                .ToList();

            foreach (var hubMethod in moduleHubMethods)
            {
                source.Append(ProcessModuleHubMethod(moduleHubTypeSymbol, hubMethod));
            }

            source.Append(@"
    } 
}");
            return source.ToString();
        }

        private string ProcessModuleHubMethod(INamedTypeSymbol moduleHubTypeSymbol, IMethodSymbol methodSymbol)
        {
            return $@"
        public {(methodSymbol.ReturnsVoid ? string.Empty : methodSymbol.ReturnType.ToDisplayString())} {moduleHubTypeSymbol.Name}_{methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"))})
        {{
            var hub = ServiceProvider.GetRequiredService<{moduleHubTypeSymbol.ToDisplayString()}>();
            {(moduleHubTypeSymbol.BaseType!.IsGenericType ? $"InitModuleHub<{moduleHubTypeSymbol.BaseType.TypeArguments[0].ToDisplayString()}>(hub)" : "InitModuleHub(hub)")};
            {(methodSymbol.ReturnsVoid ? string.Empty : "return ")}hub.{methodSymbol.Name}({string.Join(", ", methodSymbol.Parameters.Select(p => p.Name))});
        }}";
        }

        private string ProcessHubClientClass(INamedTypeSymbol entryHubTypeSymbol, INamedTypeSymbol moduleHubTypeSymbol, ITypeSymbol typedClientInterface)
        {
            var namespaceName = typedClientInterface.ContainingNamespace.ToDisplayString();
            var className = $"{entryHubTypeSymbol.Name}_{typedClientInterface.Name}Impl";

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

        private string ProcessEntryHubClass(INamedTypeSymbol entryHubTypeSymbol, List<INamedTypeSymbol> moduleHubTypeSymbols)
        {
            string namespaceName = entryHubTypeSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            var source = new StringBuilder(AutoGeneratedFileHeader);

            source.Append(
$@"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace {namespaceName}
{{
    public partial class {entryHubTypeSymbol.Name}
    {{");
            var entryHubMethods = entryHubTypeSymbol.GetMembers().OfType<IMethodSymbol>();

            // generate a constuctor if no constructor exists in the entry hub (except the default constructor).
            if (entryHubMethods.Any(m => m.Name == ".ctor" && m.Parameters.Length > 0))
            {
                source.AppendLine(@"
        // A contstructor was already defined in the entry hub.
");
            }
            else
            {
                source.AppendLine($@"
        public {entryHubTypeSymbol.Name}(ILogger<{entryHubTypeSymbol.Name}> logger, IServiceProvider serviceProvider)
            : base(logger, serviceProvider)
        {{
        }}
");
            }

            // generate OnConnectedAsync
            if (entryHubMethods.Any(m => m.Name == "OnConnectedAsync"))
            {
                source.AppendLine(@"
        // OnConnectedAsync exists already in the entry hub. You must call ModuleHubsOnConnectedAsync from there manually.
");
            }
            else
            {
                source.AppendLine($@"
        public override async Task OnConnectedAsync()
        {{
            await ModuleHubsOnConnectedAsync();
        }}
");
            }

            // generate OnConnectedAsync helper method
            if (moduleHubTypeSymbols.Count > 0)
            {
                source.AppendLine($@"
        protected async Task ModuleHubsOnConnectedAsync()
        {{");
                var index = 0;
                foreach (var moduleHubTypeSymbol in moduleHubTypeSymbols)
                {
                    var moduleHub = $"moduleHub{index}";
                    source.AppendLine($@"
            var {moduleHub} = ServiceProvider.GetRequiredService<{moduleHubTypeSymbol.ToDisplayString()}> ();
            {(moduleHubTypeSymbol.BaseType!.IsGenericType ? $"InitModuleHub<{moduleHubTypeSymbol.BaseType.TypeArguments[0].ToDisplayString()}>({moduleHub})" : $"InitModuleHub({moduleHub})")};
            await {moduleHub}.OnConnectedAsync();");
                    index++;
                }

                source.AppendLine($@"
        }}
");
            }
            else
            {
                source.AppendLine($@"
        protected Task ModuleHubsOnConnectedAsync()
        {{
            return Task.CompletedTask;
        }}
");
            }

            // generate OnDisconnectedAsync
            if (entryHubMethods.Any(m => m.Name == "OnDisconnectedAsync"))
            {
                source.AppendLine(@"
        // OnDisconnectedAsync exists already in the entry hub. You must call ModuleHubsOnDisconnectedAsync from there manually.
");
            }
            else
            {
                source.AppendLine($@"
        public override async Task OnDisconnectedAsync(Exception exception)
        {{
            await ModuleHubsOnDisconnectedAsync(exception);
        }}
");
            }

            // generate OnDisconnectedAsync helper method
            if (moduleHubTypeSymbols.Count > 0)
            {
                source.AppendLine($@"
        protected async Task ModuleHubsOnDisconnectedAsync(Exception exception)
        {{");
                var index = 0;
                foreach (var moduleHubTypeSymbol in moduleHubTypeSymbols)
                {
                    var moduleHub = $"moduleHub{index}";
                    source.AppendLine($@"
            var {moduleHub} = ServiceProvider.GetRequiredService<{moduleHubTypeSymbol.ToDisplayString()}> ();
            {(moduleHubTypeSymbol.BaseType!.IsGenericType ? $"InitModuleHub<{moduleHubTypeSymbol.BaseType.TypeArguments[0].ToDisplayString()}>({moduleHub})" : $"InitModuleHub({moduleHub})")};
            await {moduleHub}.OnDisconnectedAsync(exception);");
                    index++;
                }

                source.AppendLine($@"
        }}
");
            }
            else
            {
                source.AppendLine($@"
        protected Task ModuleHubsOnDisconnectedAsync(Exception exception)
        {{
            return Task.CompletedTask;
        }}
");
            }

            source.Append(@"
    } 
}");
            return source.ToString();
        }

        /// <summary>
        /// Created on demand before each generation pass.
        /// </summary>
        private class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<INamedTypeSymbol> EntryHubClasses { get; } = new List<INamedTypeSymbol>();

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
                        EntryHubClasses.Add(classSymbol);
                    }
                }
            }
        }
    }
}
