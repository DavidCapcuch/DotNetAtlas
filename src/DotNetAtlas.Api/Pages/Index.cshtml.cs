using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationOptions _applicationOptions;
        private readonly IWebHostEnvironment _env;

        public string Title { get; private set; } = string.Empty;
        public string Deployment { get; private set; } = string.Empty;
        public string Language { get; private set; } = string.Empty;
        public bool IsLocal { get; private set; }

        public IndexModel(IOptions<ApplicationOptions> applicationOptions, IWebHostEnvironment env)
        {
            _applicationOptions = applicationOptions.Value;
            _env = env;
        }

        public void OnGet()
        {
            var version = ApplicationInfo.Version;
            Title = $"{_applicationOptions.AppName} - {version}";

            Language = Request.Headers.TryGetValue("accept-language", out var langs)
                ? $"accept-language: {string.Join(";", langs.Select(l => l?.ToString()))}"
                : "accept-language: [Default]";

            IsLocal = _env.IsLocal();
            Deployment = _env.EnvironmentName;
        }
    }
}