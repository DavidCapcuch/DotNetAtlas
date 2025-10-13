using System.Text;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace DotNetAtlas.Api.Common.Swagger;

internal class AuthDescriptionOperationProcessor : IOperationProcessor
{
    private readonly IAuthorizationPolicyProvider _policyProvider;

    public AuthDescriptionOperationProcessor(IAuthorizationPolicyProvider policyProvider)
    {
        _policyProvider = policyProvider;
    }

    public bool Process(OperationProcessorContext context)
    {
        if (context is AspNetCoreOperationProcessorContext aspNetCoreContext)
        {
            var authAttributes =
                aspNetCoreContext.ApiDescription.ActionDescriptor.EndpointMetadata
                    .OfType<AuthorizeAttribute>()
                    .ToList();

            var authScopes = aspNetCoreContext.ApiDescription.ActionDescriptor.EndpointMetadata
                .OfType<EndpointDefinition>()
                .SelectMany(ed => ed.AllowedScopes ?? Enumerable.Empty<string>())
                .Distinct()
                .ToList();

            if (authAttributes.Count != 0 || authScopes.Count != 0)
            {
                var policies = authAttributes
                    .Where(a => !string.IsNullOrWhiteSpace(a.Policy) && !a.Policy.StartsWith(
                        "epPolicy",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.Policy!)
                    .Distinct()
                    .ToList();

                var roles = authAttributes
                    .Where(a => !string.IsNullOrWhiteSpace(a.Roles))
                    .Select(a => a.Roles!)
                    .Distinct()
                    .ToList();

                var authBuilder = new StringBuilder();
                authBuilder.Append("(Auth");
                if (policies.Count != 0)
                {
                    authBuilder.Append(" policies: ");
                    foreach (var policyName in policies)
                    {
                        authBuilder.Append(" '").Append(policyName).Append('\'');

                        var policy = _policyProvider.GetPolicyAsync(policyName).GetAwaiter().GetResult();
                        if (policy != null)
                        {
                            var requiredClaims = policy.Requirements
                                .OfType<ClaimsAuthorizationRequirement>()
                                .ToArray();

                            foreach (var claimRequirement in requiredClaims)
                            {
                                var claimType = claimRequirement.ClaimType;
                                var allowedValues = claimRequirement.AllowedValues?.ToList() ?? [];

                                if (allowedValues.Count > 0)
                                {
                                    authBuilder.Append(' ')
                                        .Append(claimType)
                                        .Append("s: [")
                                        .Append(string.Join(", ", allowedValues))
                                        .Append(']');
                                }
                            }

                            var roleRequirement = policy.Requirements
                                .OfType<RolesAuthorizationRequirement>()
                                .FirstOrDefault();

                            if (roleRequirement != null && roleRequirement.AllowedRoles.Any())
                            {
                                authBuilder.Append(" roles: [")
                                    .Append(string.Join(", ", roleRequirement.AllowedRoles))
                                    .Append(']');
                            }
                        }
                    }
                }

                if (roles.Count != 0)
                {
                    authBuilder.Append(" roles: ").Append(string.Join(", ", roles));
                }

                if (authScopes.Count != 0)
                {
                    authBuilder.Append(" scopes: ").Append(string.Join(", ", authScopes));
                }

                if (roles.Count == 0 && authScopes.Count == 0 && policies.Count == 0)
                {
                    authBuilder.Append(" non-anonymous");
                }

                authBuilder.Append(')');

                var summary = context.OperationDescription.Operation.Summary ?? "";
                context.OperationDescription.Operation.Summary = $"{summary} {authBuilder}";
            }
        }

        return true;
    }
}
