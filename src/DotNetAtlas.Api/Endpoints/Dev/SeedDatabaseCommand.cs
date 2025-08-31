using System.ComponentModel;
using FastEndpoints;
using ICommand = DotNetAtlas.Application.Common.CQS.ICommand;

namespace DotNetAtlas.Api.Endpoints.Dev;

internal class SeedDatabaseCommand : ICommand
{
    /// <summary>
    /// Number of records to generate.
    /// </summary>
    [QueryParam]
    [DefaultValue(100)]
    public required int NumberOfRecords { get; set; } = 100;
}
