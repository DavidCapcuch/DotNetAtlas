using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Payments.ArchitectureTests.Infrastructure;

/// <summary>
/// Locks the Payments BC outbox publishers to <see cref="Payments.Application.Common.Messaging.TopicsOptions"/>
/// resolution rather than hard-coded topic-name string literals (#258). Reading
/// <c>_topics.Transactions</c> from bound options keeps the topic name configurable per
/// environment (appsettings + ConfigurationManager → AddOptionsWithValidateOnStart) and lets
/// a future BC-wide topic rename land in a single config file rather than across handlers. The
/// scan looks for the IL string-load opcodes (<c>ldstr</c>) referencing the two canonical
/// topic-name literals; any publisher class that holds the literal directly fails the rule.
/// </summary>
public sealed class TopicOptionsUsageTests : BaseTest
{
    private static readonly string[] ForbiddenTopicLiterals =
    [
        "payments.transactions",
        "payments.payment-commands",
    ];

    [Fact]
    public void OutboxPublishers_ShouldNot_HardcodeTopicNameLiterals()
    {
        var publisherTypes = LoadOutboxPublisherTypes();

        publisherTypes.Should().NotBeEmpty(
            "expected to find Outbox publisher domain-event handler types in Payments.Application");

        foreach (var publisher in publisherTypes)
        {
            var offendingLiteral = FindForbiddenLdstr(publisher);
            offendingLiteral.Should().BeNull(
                $"{publisher.FullName} must reference TopicsOptions.Transactions (or the " +
                "commands-side equivalent) rather than the literal topic name. Found ldstr of " +
                $"\"{offendingLiteral}\" — bind from configuration instead.");
        }
    }

    private static List<TypeDefinition> LoadOutboxPublisherTypes()
    {
        var location = ApplicationAssembly.Location;
        var module = ModuleDefinition.ReadModule(location);

        return module.Types
            .Where(t => t.FullName.StartsWith("Payments.Application.Outbox.", StringComparison.Ordinal)
                && t.Name.EndsWith("OutboxPublisherDomainEventHandler", StringComparison.Ordinal))
            .ToList();
    }

    private static string? FindForbiddenLdstr(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Ldstr)
                {
                    continue;
                }

                if (instruction.Operand is string s && Array.Exists(ForbiddenTopicLiterals, lit => string.Equals(lit, s, StringComparison.Ordinal)))
                {
                    return s;
                }
            }
        }

        return null;
    }
}
