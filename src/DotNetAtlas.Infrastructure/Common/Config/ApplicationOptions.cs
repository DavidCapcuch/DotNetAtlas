namespace DotNetAtlas.Infrastructure.Common.Config
{
    public class ApplicationOptions
    {
        public const string SECTION = "Application";

        public required string AppName { get; set; }
    }
}
