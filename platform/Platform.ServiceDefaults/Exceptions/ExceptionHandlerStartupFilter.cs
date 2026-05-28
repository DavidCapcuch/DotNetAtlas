using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Platform.ServiceDefaults.Exceptions;

/// <summary>
/// Prepends <see cref="ExceptionHandlerExtensions.UseExceptionHandler(IApplicationBuilder)"/>
/// to every web host's middleware pipeline, so BCs don't need to wire it in
/// <c>Program.cs</c>. Paired with <see cref="PlatformExceptionHandler"/> registered
/// in <see cref="WebApplicationBuilderExtensions.AddServiceDefaults"/>.
/// </summary>
/// <remarks>
/// <see cref="IStartupFilter"/> runs before the host's
/// <c>WebApplication.Use*</c> calls in <c>Program.cs</c>, so the exception-handler
/// middleware sits at position 0 in the final pipeline — early enough to catch
/// exceptions from every downstream middleware including routing, auth, and
/// endpoint execution.
/// </remarks>
internal sealed class ExceptionHandlerStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseExceptionHandler();
            next(app);
        };
    }
}
