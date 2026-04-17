namespace G3.AspNetCore.Aws.HealthChecks;

/// <summary>
/// Configuration for the Secrets Manager health check.
/// </summary>
public sealed class SecretsManagerHealthCheckOptions
{
    /// <summary>The ARN or name of the secret to probe. Defaults to the DB_SECRET_ARN environment variable.</summary>
    public string? SecretArn { get; set; } =
        System.Environment.GetEnvironmentVariable("DB_SECRET_ARN");
}
