using Microsoft.AspNetCore.Mvc.RazorPages;
using Ordering.Infrastructure.Common.Observability;

namespace Ordering.Api.Pages;

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
