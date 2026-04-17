using System.Collections.Generic;
using Amazon.S3;
using Amazon.SecretsManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace G3.AspNetCore.Aws.HealthChecks;

/// <summary>
/// Extension methods for registering AWS health checks.
/// </summary>
public static class AwsHealthCheckExtensions
{
    /// <summary>
    /// Adds an S3 bucket accessibility health check.
    /// Requires <see cref="IAmazonS3"/> to be registered in DI.
    /// </summary>
    public static IHealthChecksBuilder AddG3S3HealthCheck(
        this IHealthChecksBuilder builder,
        string bucketName,
        string region = "us-east-1",
        string name = "s3",
        HealthStatus failureStatus = HealthStatus.Degraded,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddSingleton(new S3HealthCheckOptions
        {
            BucketName = bucketName,
            Region = region
        });

        return builder.AddCheck<S3HealthCheck>(
            name,
            failureStatus,
            tags ?? ["ready", "aws", "storage"]);
    }

    /// <summary>
    /// Adds an AWS Secrets Manager accessibility health check.
    /// Uses the DB_SECRET_ARN environment variable by default.
    /// Requires <see cref="IAmazonSecretsManager"/> to be registered in DI.
    /// </summary>
    public static IHealthChecksBuilder AddG3SecretsManagerHealthCheck(
        this IHealthChecksBuilder builder,
        string? secretArn = null,
        string name = "secrets_manager",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        builder.Services.AddSingleton(new SecretsManagerHealthCheckOptions
        {
            SecretArn = secretArn ?? System.Environment.GetEnvironmentVariable("DB_SECRET_ARN")
        });

        return builder.AddCheck<SecretsManagerHealthCheck>(
            name,
            failureStatus,
            tags ?? ["ready", "aws", "secrets"]);
    }
}
