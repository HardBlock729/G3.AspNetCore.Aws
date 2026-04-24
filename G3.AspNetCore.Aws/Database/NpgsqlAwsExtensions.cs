using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace G3.AspNetCore.Aws.Database;

/// <summary>
/// Options for building an Npgsql data source backed by AWS Secrets Manager credentials.
/// </summary>
public sealed class NpgsqlAwsOptions
{
    /// <summary>ARN of the Secrets Manager secret containing username and password. Defaults to DB_SECRET_ARN env var.</summary>
    public string SecretArn { get; set; } =
        Environment.GetEnvironmentVariable("DB_SECRET_ARN") ?? string.Empty;

    /// <summary>Database host. Defaults to DB_HOST env var.</summary>
    public string Host { get; set; } =
        Environment.GetEnvironmentVariable("DB_HOST") ?? string.Empty;

    /// <summary>Database name. Defaults to DB_NAME env var or "app".</summary>
    public string Database { get; set; } =
        Environment.GetEnvironmentVariable("DB_NAME") ?? "app";

    /// <summary>Database port. Defaults to DB_PORT env var or 5432.</summary>
    public int Port { get; set; } =
        int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out var p) ? p : 5432;

    public int MinPoolSize { get; set; } = 5;
    public int MaxPoolSize { get; set; } = 100;

    /// <summary>How often to re-fetch the password from Secrets Manager (for transparent RDS rotation).</summary>
    public TimeSpan PasswordRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait before retrying after a failed password refresh.</summary>
    public TimeSpan PasswordRefreshFailureRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Extension methods for building Npgsql data sources backed by AWS Secrets Manager.
/// </summary>
public static class NpgsqlAwsExtensions
{
    /// <summary>
    /// Builds an <see cref="NpgsqlDataSource"/> that fetches its password from AWS Secrets Manager
    /// and refreshes it periodically so RDS secret rotation is transparent (no restart required).
    ///
    /// Bypass: set DB_CONNECTION_STRING env var to skip Secrets Manager entirely (useful for local dev/CI).
    /// </summary>
    public static async Task<NpgsqlDataSource> BuildNpgsqlDataSourceAsync(
        ILogger logger,
        NpgsqlAwsOptions? options = null)
    {
        options ??= new NpgsqlAwsOptions();

        var direct = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(direct))
        {
            logger.LogInformation("Using DB_CONNECTION_STRING directly (bypassing Secrets Manager)");
            return new NpgsqlDataSourceBuilder(direct).Build();
        }

        if (string.IsNullOrEmpty(options.SecretArn))
            throw new InvalidOperationException(
                "DB_SECRET_ARN environment variable is required, or set NpgsqlAwsOptions.SecretArn.");

        if (string.IsNullOrEmpty(options.Host))
            throw new InvalidOperationException(
                "DB_HOST environment variable is required, or set NpgsqlAwsOptions.Host.");

        logger.LogInformation(
            "Fetching initial database credentials from Secrets Manager: {SecretArn}",
            options.SecretArn);

        using var smClient = new AmazonSecretsManagerClient();
        var secretResponse = await smClient.GetSecretValueAsync(
            new GetSecretValueRequest { SecretId = options.SecretArn });

        var initialCreds = JsonSerializer.Deserialize<DbCredentials>(secretResponse.SecretString)
            ?? throw new InvalidOperationException(
                "Failed to deserialize database credentials from Secrets Manager.");

        logger.LogInformation(
            "Database connection configured: {Host}:{Port}/{Database} (user: {Username})",
            options.Host, options.Port, options.Database, initialCreds.Username);

        var builder = new NpgsqlDataSourceBuilder();
        builder.ConnectionStringBuilder.Host = options.Host;
        builder.ConnectionStringBuilder.Port = options.Port;
        builder.ConnectionStringBuilder.Database = options.Database;
        builder.ConnectionStringBuilder.Username = initialCreds.Username;
        builder.ConnectionStringBuilder.Pooling = true;
        builder.ConnectionStringBuilder.MinPoolSize = options.MinPoolSize;
        builder.ConnectionStringBuilder.MaxPoolSize = options.MaxPoolSize;
        builder.ConnectionStringBuilder.ConnectionIdleLifetime = 300;
        builder.ConnectionStringBuilder.ConnectionPruningInterval = 10;
        builder.ConnectionStringBuilder.MaxAutoPrepare = 20;
        builder.ConnectionStringBuilder.AutoPrepareMinUsages = 2;
        builder.ConnectionStringBuilder.Timeout = 30;
        builder.ConnectionStringBuilder.CommandTimeout = 30;
        builder.ConnectionStringBuilder.KeepAlive = 30;
        builder.ConnectionStringBuilder.SslMode = SslMode.Require;
        builder.ConnectionStringBuilder.IncludeErrorDetail = true;

        var secretArn = options.SecretArn;
        builder.UsePeriodicPasswordProvider(
            async (_, ct) =>
            {
                using var client = new AmazonSecretsManagerClient();
                var response = await client.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretArn }, ct);
                var creds = JsonSerializer.Deserialize<DbCredentials>(response.SecretString)
                    ?? throw new InvalidOperationException(
                        "Failed to deserialize rotated database credentials.");
                return creds.Password;
            },
            successRefreshInterval: options.PasswordRefreshInterval,
            failureRefreshInterval: options.PasswordRefreshFailureRetryInterval);

        return builder.Build();
    }

    private sealed class DbCredentials
    {
        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string Username { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("password")]
        public string Password { get; init; } = string.Empty;
    }
}
