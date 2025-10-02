using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Common.Config;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Pages;

internal class IndexModel : PageModel
{
    private readonly ApplicationOptions _applicationOptions;
    public IWebHostEnvironment Env { get; }

    public string Title { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public IndexModel(IOptions<ApplicationOptions> applicationOptions, IWebHostEnvironment env)
    {
        _applicationOptions = applicationOptions.Value;
        Env = env;
    }

    public void OnGet()
    {
        var version = ApplicationInfo.Version;
        Title = $"{_applicationOptions.AppName} - {version}";

        Language = Request.Headers.TryGetValue("accept-language", out var langs)
            ? $"accept-language: {string.Join(";", langs.Select(l => l?.ToString()))}"
            : "accept-language: [Default]";
    }
}
