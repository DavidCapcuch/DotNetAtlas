using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace Invoicing.ArchitectureTests.Rules;

/// <summary>
/// Static guard against ADR-0011's "no PII in OTEL span tags" rule. Walks every
/// method body (including nested compiler-generated state machines and lambda
/// closures so async handlers are covered) and:
/// <list type="number">
///   <item>detects calls to <see cref="System.Diagnostics.Activity"/> tag setters
///         (<c>SetTag</c>, <c>AddTag</c>) and to <c>ActivityTagsCollection.Add</c>;</item>
///   <item>if any such call exists in the method, scans every <c>Ldstr</c> operand
///         in the same method against the forbidden-key list and fails the rule
///         when one matches.</item>
/// </list>
/// <para>
/// The check is approximate by design: instead of a stack-tracking analysis to
/// decide whether each <c>Ldstr</c> ended up as the <c>key</c> argument vs the
/// <c>value</c> argument, the rule rejects any forbidden literal anywhere in a
/// method that emits OTEL tags. ADR-0011 explicitly targets literal keys, and a
/// PII string literal showing up as a tag <i>value</i> is just as bad as showing
/// up as a key — so the over-approximation is a feature inside Domain /
/// Application where the library doesn't even belong.
/// </para>
/// <para>
/// Today's Invoicing code base contains zero <c>SetTag</c> calls (the only OTEL
/// touchpoint is <c>Activity.Current?.SetStatus(...)</c> in <c>ResultsExtensions</c>,
/// which this rule does not match). The fact this rule guards is therefore a
/// regression gate, not an in-place violation.
/// </para>
/// </summary>
internal sealed class NoForbiddenActivityTagKeysRule : ICustomRule
{
    private static readonly HashSet<string> TagSetterFullNames = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.Activity::SetTag",
        "System.Diagnostics.Activity::AddTag",
        "System.Diagnostics.ActivityTagsCollection::Add",
    };

    private static readonly HashSet<string> ForbiddenExactKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "buyer.email", "buyer.name", "buyer.address",
        "customer.email", "customer.name", "customer.address",
        "user.email", "user.name",
        "payment.method.token", "payment.pan", "payment.cvv",
        "invoice.billing_address", "invoice.buyer_name", "invoice.buyer_email",
    };

    private static readonly string[] ForbiddenSuffixes =
    [
        ".address", ".email", ".pan", ".cvv", ".password",
        "_address", "_email",
    ];

    private static readonly string[] ForbiddenPrefixes =
    [
        "buyer.address.",
        "customer.address.",
        "invoice.billing_address.",
    ];

    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in BaseTest.AllMethodsIncludingNested(type))
        {
            if (!method.HasBody)
            {
                continue;
            }

            var hasTagSetterCall = false;
            List<string>? stringLiterals = null;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string literal)
                {
                    stringLiterals ??= [];
                    stringLiterals.Add(literal);
                    continue;
                }

                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is not MethodReference methodRef)
                {
                    continue;
                }

                var fullName = $"{methodRef.DeclaringType.FullName}::{methodRef.Name}";
                if (TagSetterFullNames.Contains(fullName))
                {
                    hasTagSetterCall = true;
                }
            }

            if (!hasTagSetterCall || stringLiterals is null)
            {
                continue;
            }

            foreach (var literal in stringLiterals)
            {
                if (IsForbiddenKey(literal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsForbiddenKey(string key)
    {
        if (ForbiddenExactKeys.Contains(key))
        {
            return true;
        }

        foreach (var suffix in ForbiddenSuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var prefix in ForbiddenPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
