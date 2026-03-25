using System.ComponentModel;
using FastEndpoints;
using ICommand = Platform.CQS.ICommand;

namespace Weather.Api.Endpoints.Dev.SeedDatabase;

internal class SeedDatabaseCommand : ICommand
{
    /// <summary>
    /// Number of records to generate.
    /// </summary>
    [QueryParam]
    [DefaultValue(100)]
    public required int NumberOfRecords { get; set; } = 100;
}
