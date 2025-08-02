namespace DotNetAtlas.Infrastructure.Common
{
    public class ApplicationOptions
    {
        public const string SECTION = "Application";

        public required string AppName { get; set; }
        public required bool CacheEnabled { get; set; }
    }
}
