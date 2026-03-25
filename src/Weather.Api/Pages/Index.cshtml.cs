using Microsoft.AspNetCore.Mvc.RazorPages;
using Weather.Application.Common.Observability;

namespace Weather.Api.Pages;

internal sealed class IndexModel : PageModel
{
    public IWebHostEnvironment Env { get; }

    public string Title { get; private set; } = string.Empty;

    public IndexModel(IWebHostEnvironment env)
    {
        Env = env;
    }

    public void OnGet()
    {
        var version = ApplicationInfo.Version;
        Title = $"{ApplicationInfo.AppName} - {version}";
    }
}
