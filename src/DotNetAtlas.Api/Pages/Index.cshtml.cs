using DotNetAtlas.Infrastructure.Common;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DotNetAtlas.Api.Pages;

internal class IndexModel : PageModel
{
    public IWebHostEnvironment Env { get; }

    public string Title { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public IndexModel(IWebHostEnvironment env)
    {
        Env = env;
    }

    public void OnGet()
    {
        var version = ApplicationInfo.Version;
        Title = $"{ApplicationInfo.AppName} - {version}";

        Language = Request.Headers.TryGetValue("accept-language", out var langs)
            ? $"accept-language: {string.Join(";", langs.Select(l => l?.ToString()))}"
            : "accept-language: [Default]";
    }
}
