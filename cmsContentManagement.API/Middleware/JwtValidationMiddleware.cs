using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using cmsContentManagement.Application.Common.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace cmsContentManagement.API.Middleware;

public class JwtValidationMiddleware
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtValidationMiddleware> _logger;
    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;

    public JwtValidationMiddleware(RequestDelegate next, ILogger<JwtValidationMiddleware> logger,
        IOptions<JwtSettings> jwtSettings, IHttpClientFactory httpClientFactory)
    {
        _next = next;
        _logger = logger;
        _jwtSettings = jwtSettings.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        Endpoint? endpoint = context.GetEndpoint();
        if (endpoint?.Metadata?.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/public"))
        {
            await _next(context);
            return;
        }

        string? authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid Authorization header.");
            return;
        }

        string token = authHeader.Substring("Bearer ".Length).Trim();

        try
        {
            JwtSecurityTokenHandler tokenHandler = new();
            byte[] key = Encoding.UTF8.GetBytes(_jwtSettings.Secret);

            TokenValidationParameters validationParameters = new()
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = _jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            ClaimsPrincipal principal =
                tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            context.User = principal;
        }
        catch (SecurityTokenExpiredException)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Token expired.");
            return;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "JWT validation failed.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Invalid JWT token.");
            return;
        }

        // Enforce organisation-level role on every org-scoped endpoint.
        // Reads (GET/HEAD) require at least Viewer membership; writes require Editor or higher.
        // This mirrors the role hierarchy enforced by the cmsOrg service.
        var routeData = context.GetRouteData();
        if (routeData.Values.TryGetValue("organisationId", out var orgIdValue) && orgIdValue != null)
        {
            bool isWriteOperation = HttpMethods.IsPost(context.Request.Method)
                || HttpMethods.IsPut(context.Request.Method)
                || HttpMethods.IsDelete(context.Request.Method)
                || HttpMethods.IsPatch(context.Request.Method);

            string requiredRole = isWriteOperation ? "Editor" : "Viewer";

            string? role;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var roleResponse = await client.GetAsync(
                    $"{_jwtSettings.CmsOrgUrl}/organisations/{orgIdValue}/role");

                if (!roleResponse.IsSuccessStatusCode)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Could not verify organisation role.");
                    return;
                }

                var roleJson = await roleResponse.Content.ReadAsStringAsync();
                using var roleDoc = System.Text.Json.JsonDocument.Parse(roleJson);
                role = roleDoc.RootElement.TryGetProperty("role", out var roleEl)
                    ? roleEl.GetString()
                    : null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "cmsorg service unreachable at {CmsOrgUrl}.", _jwtSettings.CmsOrgUrl);
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsync("Organisation service unavailable.");
                return;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "cmsorg /role returned an unexpected payload.");
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsync("Organisation service returned an invalid response.");
                return;
            }

            // role is null when the user is not a member of the organisation.
            if (GetRoleWeight(role) < GetRoleWeight(requiredRole))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync($"'{requiredRole}' role or higher is required for this organisation.");
                return;
            }
        }

        await _next(context);
    }

    // Admin (3) > Editor (2) > Viewer (1); unknown/non-member = 0.
    private static int GetRoleWeight(string? role) => role switch
    {
        "Admin" => 3,
        "Editor" => 2,
        "Viewer" => 1,
        _ => 0
    };
}
