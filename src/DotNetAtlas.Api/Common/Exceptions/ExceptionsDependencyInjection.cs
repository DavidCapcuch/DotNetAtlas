using Microsoft.AspNetCore.Diagnostics;

namespace DotNetAtlas.Api.Common.Exceptions;

internal static class ExceptionsDependencyInjection
{
    /// <summary>
    /// Extends original ProblemDetails by info from thrown Exception.
    /// </summary>
    /// <remarks>
    /// <b>Intended to be used only in (LOCAL) DEV or TST environments, avoid to use this in PRD!</b>
    /// </remarks>
    public static IServiceCollection AddProblemDetailsWithExceptions(this IServiceCollection services)
    {
        services.AddProblemDetails(setup =>
            setup.CustomizeProblemDetails = context =>
            {
                var exceptionHandler = context.HttpContext.Features.Get<IExceptionHandlerFeature>();

                if (exceptionHandler?.Error != null)
                {
                    var errors = new List<object>();

                    var ex = exceptionHandler.Error;
                    do
                    {
                        var error = new
                        {
                            ex.Message,
                            Type = ex.GetType().FullName,
                            ex.Source,
                            TargetSite = new
                            {
                                DeclaringType = ex.TargetSite!.DeclaringType!.Name,
                                MemberType = ex.TargetSite.MemberType.ToString(),
                                ex.TargetSite.Name,
                            },
                            ex.StackTrace,
                        };

                        errors.Add(error);

                        ex = ex.InnerException;
                    }
                    while (ex != null);

                    context.ProblemDetails.Extensions.Add("errors", errors);
                }
            });

        return services;
    }
}
