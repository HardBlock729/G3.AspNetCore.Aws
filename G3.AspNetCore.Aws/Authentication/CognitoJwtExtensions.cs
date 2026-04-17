using System;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace G3.AspNetCore.Aws.Authentication;

/// <summary>
/// Extension methods for configuring AWS Cognito JWT authentication.
/// </summary>
public static class CognitoJwtExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication configured for AWS Cognito.
    /// Reads COGNITO_USER_POOL_ID and AWS_REGION environment variables.
    /// Fetches JWKS from the Cognito hosted endpoint and caches it for the application lifetime.
    /// </summary>
    public static WebApplicationBuilder AddG3CognitoJwtAuth(
        this WebApplicationBuilder builder,
        Action<CognitoAuthorizationOptions>? configureAuthorization = null)
    {
        var userPoolId = Environment.GetEnvironmentVariable("COGNITO_USER_POOL_ID")
            ?? throw new InvalidOperationException(
                "COGNITO_USER_POOL_ID environment variable is required.");

        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        var issuer = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";

        JsonWebKeySet? jwks = null;

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            // Preserve original JWT claim names (sub, email, etc.) — do not remap to CLR types.
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                // Cognito access tokens don't set aud to the client ID — only ID tokens do.
                // Accept tokens from multiple app clients (mobile, web, Swagger) on the same pool.
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (_, _, kid, _) =>
                {
                    try
                    {
                        if (jwks == null)
                        {
                            var jwksUrl = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}/.well-known/jwks.json";
                            using var http = new System.Net.Http.HttpClient();
                            var json = http.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
                            jwks = new JsonWebKeySet(json);
                        }

                        var key = jwks.Keys.FirstOrDefault(k => k.Kid == kid);
                        return key != null ? [key] : [];
                    }
                    catch (Exception ex)
                    {
                        // Log via ILogger at runtime — we can't inject it here since this
                        // is a delegate captured at configuration time.
                        System.Diagnostics.Debug.WriteLine($"Failed to fetch Cognito JWKS: {ex.Message}");
                        return [];
                    }
                },
                ClockSkew = TimeSpan.FromMinutes(5)
            };
        });

        var authOptions = new CognitoAuthorizationOptions();
        configureAuthorization?.Invoke(authOptions);

        builder.Services.AddAuthorization(options =>
        {
            foreach (var policy in authOptions.Policies)
            {
                options.AddPolicy(policy.Key, p =>
                    p.RequireClaim("cognito:groups", policy.Value));
            }
        });

        return builder;
    }
}

/// <summary>
/// Options for defining Cognito group-based authorization policies.
/// </summary>
public sealed class CognitoAuthorizationOptions
{
    internal System.Collections.Generic.Dictionary<string, string[]> Policies { get; } = new();

    /// <summary>
    /// Adds an authorization policy that requires membership in one or more Cognito groups.
    /// </summary>
    public CognitoAuthorizationOptions AddGroupPolicy(string policyName, params string[] groups)
    {
        Policies[policyName] = groups;
        return this;
    }
}
