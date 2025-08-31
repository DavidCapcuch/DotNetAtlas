namespace DotNetAtlas.Infrastructure.Common.Config;

public class ApplicationOptions
{
    public const string Section = "Application";

    public required string AppName { get; set; }
}
