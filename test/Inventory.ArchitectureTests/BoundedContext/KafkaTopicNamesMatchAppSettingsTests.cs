using System.Text.Json;
using Inventory.Application.Common.Messaging;

namespace Inventory.ArchitectureTests.BoundedContext;

/// <summary>
/// Pins the LOCKED-contract Kafka topic + consumer-group names in
/// <see cref="KafkaTopicNames"/> against the runtime values in
/// <c>services/Inventory/Inventory.API/appsettings.json</c>. A typo in either
/// surface fails this test — the topic / group is a contract per the Inventory
/// BC <c>&lt;contract&gt;</c> and ADR-0004, so an unwitting rename would
/// silently fork a new topic on prod.
/// </summary>
/// <remarks>
/// <para>
/// Reads the appsettings.json file directly from the source tree (relative to
/// the test assembly's bin folder) — the API project isn't configured to copy
/// its appsettings.json into this test project's output, and adding a build
/// hop just for this test would be over-engineering.
/// </para>
/// </remarks>
public class KafkaTopicNamesMatchAppSettingsTests
{
    private static readonly Lazy<JsonElement> AppSettingsRoot = new(() =>
    {
        var assemblyDir = Path.GetDirectoryName(typeof(KafkaTopicNamesMatchAppSettingsTests).Assembly.Location)
            ?? throw new InvalidOperationException("Could not resolve test assembly directory.");
        // bin/Debug/net10.0 -> climb four levels to repo root, then descend.
        var repoRoot = Path.GetFullPath(Path.Combine(assemblyDir, "..", "..", "..", ".."));
        var appsettingsPath = Path.Combine(repoRoot, "..", "services", "Inventory", "Inventory.API", "appsettings.json");
        var json = File.ReadAllText(Path.GetFullPath(appsettingsPath));
        return JsonDocument.Parse(json).RootElement;
    });

    [Fact]
    public void InventoryStockEvents_Constant_MatchesAppSettings()
    {
        var actual = AppSettingsRoot.Value.GetProperty("Topics").GetProperty("InventoryStockEvents").GetString();
        actual.Should().Be(KafkaTopicNames.InventoryStockEvents);
    }

    [Fact]
    public void InventoryReservations_Constant_MatchesAppSettings()
    {
        var actual = AppSettingsRoot.Value.GetProperty("Topics").GetProperty("InventoryReservations").GetString();
        actual.Should().Be(KafkaTopicNames.InventoryReservations);
    }

    [Fact]
    public void DltTopicSuffix_Constant_MatchesAppSettings()
    {
        var actual = AppSettingsRoot.Value.GetProperty("Topics").GetProperty("DltTopicSuffix").GetString();
        actual.Should().Be(KafkaTopicNames.DltTopicSuffix);
    }

    [Fact]
    public void InventoryReservationCommands_Constant_MatchesAppSettings()
    {
        var actual = AppSettingsRoot.Value.GetProperty("KafkaReservationCommandsConsumer").GetProperty("Topic").GetString();
        actual.Should().Be(KafkaTopicNames.InventoryReservationCommands);
    }

    [Fact]
    public void StockInitConsumerGroup_Constant_MatchesAppSettings_OnBothConsumers()
    {
        var catalogGroup = AppSettingsRoot.Value.GetProperty("KafkaCatalogProductsConsumer").GetProperty("GroupId").GetString();
        var orderingGroup = AppSettingsRoot.Value.GetProperty("KafkaOrderingOrdersConsumer").GetProperty("GroupId").GetString();
        catalogGroup.Should().Be(KafkaTopicNames.StockInitConsumerGroup);
        orderingGroup.Should().Be(KafkaTopicNames.StockInitConsumerGroup);
    }

    [Fact]
    public void ReservationCommandsConsumerGroup_Constant_MatchesAppSettings()
    {
        var actual = AppSettingsRoot.Value.GetProperty("KafkaReservationCommandsConsumer").GetProperty("GroupId").GetString();
        actual.Should().Be(KafkaTopicNames.ReservationCommandsConsumerGroup);
    }
}
