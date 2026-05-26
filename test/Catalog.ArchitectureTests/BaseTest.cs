using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using NetArchTest.Rules;

namespace Catalog.ArchitectureTests;

/// <summary>
/// Shared base for Catalog architecture tests. Anchors the four layer assemblies via the
/// <c>IAssemblyMarker</c> interfaces and exposes the custom <see cref="ICustomRule"/>
/// implementations the rule set leans on.
/// </summary>
public abstract class BaseTest
{
    protected static readonly System.Reflection.Assembly DomainAssembly = typeof(global::Catalog.Domain.IAssemblyMarker).Assembly;
    protected static readonly System.Reflection.Assembly ApplicationAssembly = typeof(global::Catalog.Application.IAssemblyMarker).Assembly;
    protected static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(global::Catalog.Infrastructure.IAssemblyMarker).Assembly;
    protected static readonly System.Reflection.Assembly PresentationAssembly = typeof(global::Catalog.Api.IAssemblyMarker).Assembly;

    /// <summary>
    /// Walks the type and every nested compiler-generated type. Required so IL-scanning rules see
    /// async state machines (<c>&lt;HandleAsync&gt;d__N::MoveNext</c>), iterator state machines,
    /// and lambda closures (<c>&lt;&gt;c__DisplayClass*</c>). Without this, async methods'
    /// IL is invisible to the rule set — see <c>OnlyThrowsRule</c> / <c>DoesNotThrowRule</c> /
    /// <c>NoStaticUtcNowRule</c>.
    /// </summary>
    private static IEnumerable<MethodDefinition> AllMethodsIncludingNested(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            yield return method;
        }

        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in AllMethodsIncludingNested(nested))
            {
                yield return method;
            }
        }
    }

    /// <summary>
    /// Asserts every constructor on the type is either static or private.
    /// Mirrors <c>Weather.ArchitectureTests.BaseTest.PrivateConstructorsRule</c>.
    /// </summary>
    protected sealed class PrivateConstructorsRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var constructor in type.GetConstructors())
            {
                if (!constructor.IsStatic && !constructor.IsPrivate)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Asserts the type's IL contains no calls to forbidden static "now" getters.
    /// Per ADR-0015, domain code MUST resolve "now" through the injected
    /// <see cref="System.TimeProvider"/>; static accessors break determinism and the
    /// <c>FakeTimeProvider</c> test seam. Walks compiler-generated nested types to cover async
    /// state machines / lambdas.
    /// </summary>
    protected sealed class NoStaticUtcNowRule : ICustomRule
    {
        private static readonly HashSet<string> ForbiddenGetters = new(StringComparer.Ordinal)
        {
            "System.DateTime System.DateTime::get_UtcNow()",
            "System.DateTime System.DateTime::get_Now()",
            "System.DateTime System.DateTime::get_Today()",
            "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
            "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        };

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethodsIncludingNested(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    {
                        continue;
                    }

                    if (instruction.Operand is MethodReference methodRef &&
                        ForbiddenGetters.Contains(methodRef.FullName))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Asserts every <c>newobj</c> instruction whose declaring type derives from
    /// <see cref="System.Exception"/> targets one of the permitted exception types. Walks
    /// compiler-generated nested types so async/iterator state machines are covered. Used to lock
    /// aggregates to <c>DataIntegrityException</c>-only per error-taxonomy.md § 1.5.
    /// </summary>
    protected sealed class OnlyThrowsRule : ICustomRule
    {
        private readonly HashSet<string> _permittedFullNames;

        public OnlyThrowsRule(params Type[] permitted)
        {
            _permittedFullNames = permitted
                .Select(t => t.FullName!)
                .ToHashSet(StringComparer.Ordinal);
        }

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethodsIncludingNested(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Newobj)
                    {
                        continue;
                    }

                    if (instruction.Operand is not MethodReference methodRef)
                    {
                        continue;
                    }

                    var declaringType = methodRef.DeclaringType;
                    if (!IsExceptionType(declaringType))
                    {
                        continue;
                    }

                    if (!_permittedFullNames.Contains(declaringType.FullName))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsExceptionType(TypeReference typeRef)
        {
            // Cheap structural check first — covers unresolvable third-party assemblies that would
            // otherwise pass through SafeResolve == null silently.
            if (typeRef.Name.EndsWith("Exception", StringComparison.Ordinal))
            {
                return true;
            }

            var current = SafeResolve(typeRef);
            while (current is not null)
            {
                if (current.FullName == "System.Exception")
                {
                    return true;
                }

                current = SafeResolve(current.BaseType);
            }

            return false;
        }

        private static TypeDefinition? SafeResolve(TypeReference? typeRef)
        {
            try
            {
                return typeRef?.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Asserts no <c>newobj</c> instruction targets any of the forbidden exception types.
    /// Walks compiler-generated nested types so async-state-machine bodies are covered (this is
    /// the primary code path for <c>HandleAsync</c> handlers — without recursion the rule would
    /// silently no-op against today's 100%-async handler set). Used to ban raw
    /// <see cref="ArgumentException"/> / <see cref="InvalidOperationException"/> /
    /// <see cref="ArgumentNullException"/> from result-pattern handlers.
    /// </summary>
    protected sealed class DoesNotThrowRule : ICustomRule
    {
        private readonly HashSet<string> _forbiddenFullNames;

        public DoesNotThrowRule(params Type[] forbidden)
        {
            _forbiddenFullNames = forbidden
                .Select(t => t.FullName!)
                .ToHashSet(StringComparer.Ordinal);
        }

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in AllMethodsIncludingNested(type))
            {
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Newobj)
                    {
                        continue;
                    }

                    if (instruction.Operand is not MethodReference methodRef)
                    {
                        continue;
                    }

                    if (_forbiddenFullNames.Contains(methodRef.DeclaringType.FullName))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Asserts the type has at least one <c>public static</c> method whose name starts with
    /// <c>Create</c> or <c>From</c> — i.e. the sanctioned aggregate factory shape per
    /// architecture-tests.md § 1.2 line 62. Aggregates without a factory cannot return
    /// <c>Result&lt;TAggregate&gt;</c> from validation, breaking the result-pattern boundary.
    /// </summary>
    protected sealed class HasPublicStaticFactoryMethodRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in type.Methods)
            {
                if (!method.IsPublic || !method.IsStatic)
                {
                    continue;
                }

                if (method.Name.StartsWith("Create", StringComparison.Ordinal) ||
                    method.Name.StartsWith("From", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Asserts the type's <c>HandleAsync</c> method (the convention-named CQRS handler entry
    /// point) returns <c>Task&lt;Result&gt;</c> or <c>Task&lt;Result&lt;T&gt;&gt;</c>. Per
    /// architecture-tests.md § 1.4 — blocks regressions where a future contributor's handler
    /// returns a raw domain type or <c>Task</c>, hiding error pathways.
    /// </summary>
    protected sealed class HandlerReturnsResultRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in type.Methods)
            {
                if (method.Name != "HandleAsync")
                {
                    continue;
                }

                var returnTypeName = method.ReturnType.FullName;
                var isResultShape =
                    returnTypeName == "System.Threading.Tasks.Task`1<FluentResults.Result>" ||
                    returnTypeName.StartsWith(
                        "System.Threading.Tasks.Task`1<FluentResults.Result`1<",
                        StringComparison.Ordinal);

                if (!isResultShape)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Asserts the type does not reference any of the forbidden types via fields, properties, or
    /// method parameters. Stricter than NetArchTest's dependency-graph check — that one walks all
    /// IL references; this one limits to public/private surface area on the type itself, the way
    /// architecture-tests.md § 2.1 ("Product references Category solely by ID") expects.
    /// </summary>
    protected sealed class OnlyReferencesByIdRule : ICustomRule
    {
        private readonly HashSet<string> _forbiddenFullNames;

        public OnlyReferencesByIdRule(params Type[] forbidden)
        {
            _forbiddenFullNames = forbidden
                .Select(t => t.FullName!)
                .ToHashSet(StringComparer.Ordinal);
        }

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var field in type.Fields)
            {
                if (_forbiddenFullNames.Contains(field.FieldType.FullName))
                {
                    return false;
                }
            }

            foreach (var property in type.Properties)
            {
                if (_forbiddenFullNames.Contains(property.PropertyType.FullName))
                {
                    return false;
                }
            }

            foreach (var method in type.Methods)
            {
                foreach (var parameter in method.Parameters)
                {
                    if (_forbiddenFullNames.Contains(parameter.ParameterType.FullName))
                    {
                        return false;
                    }
                }

                if (_forbiddenFullNames.Contains(method.ReturnType.FullName))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
