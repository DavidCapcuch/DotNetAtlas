using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Basket.ArchitectureTests.Domain;

/// <summary>
/// Enforces ADR-0015 (time + timezone policy) inside Basket.Domain:
/// (1) The bare BCL <c>DateTime</c> type is forbidden — only <c>DateTimeOffset</c> is allowed.
/// (2) Ambient-time calls (<c>DateTime.UtcNow</c>, <c>DateTimeOffset.UtcNow</c>, and their
/// <c>.Now</c> counterparts) are forbidden — the domain receives <c>DateTimeOffset</c>
/// instants from the application layer or via injected <c>TimeProvider</c>.
/// </summary>
public class TimePolicyTests : BaseTest
{
    [Fact]
    public void Domain_DoesNotReference_BareDateTime()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new ForbidsBareDateTimeRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "ADR-0015 forbids the bare BCL DateTime type in Basket.Domain — use DateTimeOffset instead. " +
            "Failing types reference System.DateTime in a field, property, parameter, return, or local");
    }

    [Fact]
    public void Domain_DoesNotCall_AmbientNow()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .MeetCustomRule(new ForbidsAmbientTimeCallsRule())
            .GetResult();

        result.FailingTypes.Should().BeEmpty(
            "ADR-0015 forbids ambient time calls (DateTime.UtcNow, DateTime.Now, DateTimeOffset.UtcNow, " +
            "DateTimeOffset.Now) inside Basket.Domain — the application layer must pass the instant or " +
            "inject a TimeProvider");
    }

    /// <summary>
    /// Returns true when the type does NOT reference <c>System.DateTime</c> in any of its
    /// member signatures or method bodies (fields, properties, parameters, returns, locals).
    /// </summary>
    private sealed class ForbidsBareDateTimeRule : ICustomRule
    {
        private const string ForbiddenTypeName = "System.DateTime";

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var field in type.Fields)
            {
                if (IsForbidden(field.FieldType))
                {
                    return false;
                }
            }

            foreach (var property in type.Properties)
            {
                if (IsForbidden(property.PropertyType))
                {
                    return false;
                }
            }

            foreach (var method in type.Methods)
            {
                if (IsForbidden(method.ReturnType))
                {
                    return false;
                }

                foreach (var parameter in method.Parameters)
                {
                    if (IsForbidden(parameter.ParameterType))
                    {
                        return false;
                    }
                }

                if (method.HasBody)
                {
                    foreach (var local in method.Body.Variables)
                    {
                        if (IsForbidden(local.VariableType))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool IsForbidden(TypeReference typeReference)
        {
            // Only the exact System.DateTime type is forbidden — DateTimeOffset and Nullable<DateTime>
            // both resolve via FullName to forms that include "DateTime", so we anchor on equality.
            if (typeReference.FullName == ForbiddenTypeName)
            {
                return true;
            }

            // Handle Nullable<DateTime>, arrays, and generic-instance wrappers (Task<DateTime>, etc.).
            if (typeReference is GenericInstanceType genericInstance)
            {
                foreach (var argument in genericInstance.GenericArguments)
                {
                    if (IsForbidden(argument))
                    {
                        return true;
                    }
                }
            }

            if (typeReference is ArrayType arrayType && IsForbidden(arrayType.ElementType))
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns true when the type does NOT call any of the ambient-now property getters
    /// from <c>DateTime</c> or <c>DateTimeOffset</c> in any of its method bodies.
    /// </summary>
    private sealed class ForbidsAmbientTimeCallsRule : ICustomRule
    {
        private static readonly HashSet<string> ForbiddenMethods = new(StringComparer.Ordinal)
        {
            "System.DateTime System.DateTime::get_UtcNow()",
            "System.DateTime System.DateTime::get_Now()",
            "System.DateTimeOffset System.DateTimeOffset::get_UtcNow()",
            "System.DateTimeOffset System.DateTimeOffset::get_Now()",
        };

        public bool MeetsRule(TypeDefinition type)
        {
            foreach (var method in type.Methods)
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

                    if (instruction.Operand is MethodReference target &&
                        ForbiddenMethods.Contains(target.FullName))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
