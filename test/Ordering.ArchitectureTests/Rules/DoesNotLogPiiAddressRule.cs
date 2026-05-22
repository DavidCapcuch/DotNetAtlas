using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Ordering.ArchitectureTests.Rules;

/// <summary>
/// Walks every method body on the type and fails if a method BOTH calls a
/// <c>Microsoft.Extensions.Logging</c> Log helper (any <c>Log*</c> on
/// <c>LoggerExtensions</c> or <c>ILogger</c>) AND references the
/// PII-sensitive <c>Platform.SharedKernel.ValueObjects.Address</c> type in
/// the same body (as a local, argument, field load, or boxed value).
/// </summary>
/// <remarks>
/// <para>
/// Enforces ADR-0011 ("PII *_enc at rest; never log Address-typed
/// parameters"). Production code under <c>Ordering.*</c> is clean today —
/// this rule is a regression guard that will refuse to compile-green if a
/// future maintainer threads an <see cref="object"/>-shaped <c>address</c>
/// into a structured log line.
/// </para>
/// <para>
/// The check is intentionally conservative: a method that loads an
/// <c>Address</c> for non-logging reasons AND also happens to log something
/// unrelated would false-positive. In practice the Ordering layer has no
/// such methods, and a false positive is the right failure mode for a
/// PII-protection rule.
/// </para>
/// </remarks>
internal sealed class DoesNotLogPiiAddressRule : ICustomRule
{
    private const string AddressTypeFullName = "Platform.SharedKernel.ValueObjects.Address";
    private const string LoggerExtensionsType = "Microsoft.Extensions.Logging.LoggerExtensions";
    private const string LoggerInterfaceType = "Microsoft.Extensions.Logging.ILogger";

    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            if (MethodLogsAndTouchesAddress(method))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MethodLogsAndTouchesAddress(MethodDefinition method)
    {
        var callsLogger = false;
        var touchesAddress = false;

        foreach (var variable in method.Body.Variables)
        {
            if (IsAddressType(variable.VariableType))
            {
                touchesAddress = true;
                break;
            }
        }

        if (!touchesAddress)
        {
            foreach (var parameter in method.Parameters)
            {
                if (IsAddressType(parameter.ParameterType))
                {
                    touchesAddress = true;
                    break;
                }
            }
        }

        foreach (var instruction in method.Body.Instructions)
        {
            if (!callsLogger && IsLoggerLogCall(instruction))
            {
                callsLogger = true;
            }

            if (!touchesAddress && InstructionTouchesAddress(instruction))
            {
                touchesAddress = true;
            }

            if (callsLogger && touchesAddress)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoggerLogCall(Instruction instruction)
    {
        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
        {
            return false;
        }

        if (instruction.Operand is not MethodReference target)
        {
            return false;
        }

        var declaringType = target.DeclaringType?.FullName;
        if (declaringType != LoggerExtensionsType && declaringType != LoggerInterfaceType)
        {
            return false;
        }

        return target.Name.StartsWith("Log", StringComparison.Ordinal);
    }

    private static bool InstructionTouchesAddress(Instruction instruction) =>
        instruction.Operand switch
        {
            FieldReference field when IsAddressType(field.FieldType) => true,
            TypeReference type when IsAddressType(type) => true,
            MethodReference method when IsAddressType(method.ReturnType) => true,
            _ => false,
        };

    private static bool IsAddressType(TypeReference? type) =>
        type is not null && type.FullName == AddressTypeFullName;
}
