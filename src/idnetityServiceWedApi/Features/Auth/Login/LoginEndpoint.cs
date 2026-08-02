using System.Collections.Immutable;
using System.Security.Claims;
using idnetityServiceWedApi.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace idnetityServiceWedApi.Features.Auth.Login;

public static class LoginEndpoint
{
    public static void MapLoginEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/token", async (
            HttpContext httpContext,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration) =>
        {
            var request = httpContext.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenIddict server request could not be retrieved.");

            if (request.IsPasswordGrantType())
            {
                var user = await userManager.FindByNameAsync(request.Username!);
                if (user is null)
                {
                    return ForbidWithError(Errors.InvalidGrant, "The username/password combination is invalid.");
                }

                var passwordCheck = await signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);
                if (!passwordCheck.Succeeded)
                {
                    return ForbidWithError(Errors.InvalidGrant, "The username/password combination is invalid.");
                }

                var principal = await CreatePrincipalAsync(
                    user, userManager, request.GetScopes(), GetAudience(configuration));
                return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            if (request.IsRefreshTokenGrantType())
            {
                var authenticateResult = await httpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                var userId = authenticateResult.Principal?.GetClaim(Claims.Subject);
                if (!authenticateResult.Succeeded || userId is null)
                {
                    return ForbidWithError(Errors.InvalidGrant, "The refresh token is no longer valid.");
                }

                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                {
                    return ForbidWithError(Errors.InvalidGrant, "The refresh token is no longer valid.");
                }

                // A refresh request that omits "scope" preserves the original grant. Resolve
                // effective scopes before assigning claim destinations so identity-token claims
                // remain consistent with those scopes.
                IEnumerable<string> effectiveScopes = request.GetScopes().Any()
                    ? request.GetScopes()
                    : authenticateResult.Principal!.GetScopes();
                var principal = await CreatePrincipalAsync(
                    user, userManager, effectiveScopes, GetAudience(configuration));

                return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            return ForbidWithError(Errors.UnsupportedGrantType, "The specified grant type is not supported.");
        });
    }

    /// <summary>Scopes this authorization server is willing to issue.</summary>
    private static readonly string[] SupportedScopes =
        [Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles, Scopes.OfflineAccess];

    private static async Task<ClaimsPrincipal> CreatePrincipalAsync(
        ApplicationUser user,
        UserManager<ApplicationUser> userManager,
        IEnumerable<string> requestedScopes,
        string audience)
    {
        var identity = new ClaimsIdentity(
            authenticationType: "Bearer",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Id.ToString())
                .SetClaim(Claims.Email, user.Email)
                .SetClaim(Claims.Name, user.UserName);

        var roles = await userManager.GetRolesAsync(user);
        identity.SetClaims(Claims.Role, [.. roles]);

        var principal = new ClaimsPrincipal(identity);

        // Grant only what the client asked for, intersected with what this server supports,
        // so a client that requests no offline_access does not silently receive a refresh token.
        principal.SetScopes(requestedScopes.Intersect(SupportedScopes));
        principal.SetResources(audience);

        foreach (var claim in identity.Claims)
        {
            claim.SetDestinations(GetDestinations(claim, principal));
        }

        return principal;
    }

    private static string GetAudience(IConfiguration configuration) =>
        configuration["OpenIddict:Audience"]
        ?? throw new InvalidOperationException("OpenIddict:Audience is required.");

    private static IEnumerable<string> GetDestinations(Claim claim, ClaimsPrincipal principal)
    {
        switch (claim.Type)
        {
            case Claims.Name:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Profile))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Email:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Email))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            case Claims.Role:
                yield return Destinations.AccessToken;
                if (principal.HasScope(Scopes.Roles))
                {
                    yield return Destinations.IdentityToken;
                }
                yield break;

            default:
                yield return Destinations.AccessToken;
                yield break;
        }
    }

    private static IResult ForbidWithError(string error, string description)
    {
        var properties = new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        });

        return Results.Forbid(properties, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}
