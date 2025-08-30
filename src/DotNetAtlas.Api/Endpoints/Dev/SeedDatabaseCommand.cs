using System.ComponentModel;
using FastEndpoints;
using CQS_ICommand = DotNetAtlas.Application.Common.CQS.ICommand;
using ICommand = DotNetAtlas.Application.Common.CQS.ICommand;

namespace DotNetAtlas.Api.Endpoints.Dev
{
    public class SeedDatabaseCommand : CQS_ICommand
    {
        /// <summary>
        /// Number of records to generate.
        /// </summary>
        [QueryParam]
        [DefaultValue(100)]
        public required int NumberOfRecords { get; set; } = 100;
    }
}