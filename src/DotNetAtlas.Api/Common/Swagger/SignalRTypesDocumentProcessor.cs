using System.Reflection;
using DotNetAtlas.Api.SignalR.WeatherAlerts;
using Microsoft.AspNetCore.SignalR;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace DotNetAtlas.Api.Common.Swagger;

/// <summary>
/// Automatically discover all contracts marked for SignalR [Hub] or [Receiver]
/// and add them to Swagger documentation.
/// </summary>
public class SignalRTypesDocumentProcessor : IDocumentProcessor
{
    public void Process(DocumentProcessorContext context)
    {
        var discoveredTypes = new HashSet<Type>();
        var visitedTypes = new HashSet<Type>();

        var apiAssembly = typeof(WeatherAlertHub).Assembly;
        var hubBaseType = typeof(Hub);
        foreach (var hubType in apiAssembly.GetTypes().Where(t => hubBaseType.IsAssignableFrom(t) && !t.IsAbstract))
        {
            var clientContract = GetHubClientContractType(hubType);
            var clientContractMethods = clientContract?.GetMethods() ?? [];
            var serverMethods =
                hubType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            foreach (var method in serverMethods.Concat(clientContractMethods))
            {
                foreach (var parameterInfo in method.GetParameters())
                {
                    AccumulateContractTypes(parameterInfo.ParameterType, visitedTypes, discoveredTypes);
                }

                AccumulateContractTypes(method.ReturnType, visitedTypes, discoveredTypes);
            }
        }

        foreach (var discoveredType in discoveredTypes)
        {
            context.SchemaGenerator.Generate(discoveredType, context.SchemaResolver);
        }
    }

    private static Type? GetHubClientContractType(Type hubType)
    {
        for (var current = hubType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Hub<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static void AccumulateContractTypes(Type? type, HashSet<Type> visited, HashSet<Type> output)
    {
        if (type is null)
        {
            return;
        }

        // Ignore non-documentable primitives
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask) || type == typeof(Exception))
        {
            return;
        }

        if (!visited.Add(type))
        {
            return; // already processed
        }

        if (type.IsGenericType)
        {
            var typeDef = type.GetGenericTypeDefinition();
            if (typeDef == typeof(Task<>) || typeDef == typeof(ValueTask<>) || typeDef == typeof(Nullable<>))
            {
                AccumulateContractTypes(type.GetGenericArguments()[0], visited, output);
                return;
            }

            // IAsyncEnumerable<T>, IEnumerable<T>
            var enumType = type.GetInterfaces().Concat([type])
                .FirstOrDefault(t => t.IsGenericType &&
                                     (t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                                      t.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)));
            if (enumType is not null)
            {
                var innerType = enumType.GetGenericArguments()[0];
                AccumulateContractTypes(innerType, visited, output);
                return;
            }

            // Traverse generic arguments (Dictionary, Tuple, etc.)
            foreach (var arg in type.GetGenericArguments())
            {
                AccumulateContractTypes(arg, visited, output);
            }
        }

        if (type.IsArray)
        {
            AccumulateContractTypes(type.GetElementType(), visited, output);
            return;
        }

        output.Add(type);
    }
}
