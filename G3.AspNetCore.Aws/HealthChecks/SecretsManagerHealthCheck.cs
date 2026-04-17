using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace G3.AspNetCore.Aws.HealthChecks;

/// <summary>
/// Health check that verifies AWS Secrets Manager accessibility by describing a secret (no value retrieval).
/// </summary>
public sealed class SecretsManagerHealthCheck(
    IAmazonSecretsManager secretsManager,
    SecretsManagerHealthCheckOptions options,
    ILogger<SecretsManagerHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.SecretArn))
        {
            return HealthCheckResult.Unhealthy(
                "SecretArn is not configured.",
                data: new Dictionary<string, object>
                {
                    { "configured", false },
                    { "error", "SecretArn is null or empty." }
                });
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var response = await secretsManager.DescribeSecretAsync(
                new DescribeSecretRequest { SecretId = options.SecretArn },
                cancellationToken);

            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                { "secretArn", options.SecretArn },
                { "secretName", response.Name },
                { "responseTimeMs", stopwatch.ElapsedMilliseconds },
                { "lastAccessedDate", response.LastAccessedDate?.ToString("o") ?? "Never" },
                { "accessible", true }
            };

            if (stopwatch.ElapsedMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded("AWS Secrets Manager is slow to respond", data: data);
            }

            return HealthCheckResult.Healthy("AWS Secrets Manager is accessible", data: data);
        }
        catch (ResourceNotFoundException ex)
        {
            logger.LogError(ex, "Secret not found: {SecretArn}", options.SecretArn);
            return HealthCheckResult.Unhealthy(
                $"Secret '{options.SecretArn}' does not exist",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "secretArn", options.SecretArn },
                    { "error", "SecretNotFound" }
                });
        }
        catch (InvalidRequestException ex)
        {
            logger.LogError(ex, "Invalid Secrets Manager request: {SecretArn}", options.SecretArn);
            return HealthCheckResult.Unhealthy(
                "Invalid Secrets Manager request",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "secretArn", options.SecretArn },
                    { "error", "InvalidRequest" }
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check Secrets Manager health: {SecretArn}", options.SecretArn);
            return HealthCheckResult.Unhealthy(
                "Failed to check AWS Secrets Manager health",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "secretArn", options.SecretArn },
                    { "error", ex.Message }
                });
        }
    }
}
