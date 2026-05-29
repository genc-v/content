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

        if (context.Request.Path.StartsWithSegments("/api/public/content"))
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

        bool isWriteOperation = context.Request.Method == HttpMethods.Post
            || context.Request.Method == HttpMethods.Put
            || context.Request.Method == HttpMethods.Delete;

        if (isWriteOperation)
        {
            var routeData = context.GetRouteData();
            if (routeData.Values.TryGetValue("organisationId", out var orgIdValue) && orgIdValue != null)
            {
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
                    var roleDoc = System.Text.Json.JsonDocument.Parse(roleJson);
                    var role = roleDoc.RootElement.GetProperty("role").GetString();

                    if (role != "Admin" && role != "Editor")
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync("Editor or Admin role required.");
                        return;
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "cmsorg service unreachable at {CmsOrgUrl}.", _jwtSettings.CmsOrgUrl);
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await context.Response.WriteAsync("Organisation service unavailable.");
                    return;
                }
            }
        }

        await _next(context);
    }
}
