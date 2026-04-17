using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace G3.AspNetCore.Aws.HealthChecks;

/// <summary>
/// Health check that verifies S3 bucket accessibility and latency.
/// </summary>
public sealed class S3HealthCheck(
    IAmazonS3 s3Client,
    S3HealthCheckOptions options,
    ILogger<S3HealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var response = await s3Client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = options.BucketName,
                    MaxKeys = 1
                },
                cancellationToken);

            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                { "bucketName", options.BucketName },
                { "region", options.Region },
                { "responseTimeMs", stopwatch.ElapsedMilliseconds },
                { "accessible", true }
            };

            if (stopwatch.ElapsedMilliseconds > 2000)
            {
                return HealthCheckResult.Degraded(
                    $"S3 bucket '{options.BucketName}' is slow to respond",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"S3 bucket '{options.BucketName}' is accessible",
                data: data);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            logger.LogError(ex, "S3 bucket access denied: {BucketName}", options.BucketName);
            return HealthCheckResult.Unhealthy(
                $"S3 bucket '{options.BucketName}' access denied — check IAM permissions",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "bucketName", options.BucketName },
                    { "error", "AccessDenied" }
                });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogError(ex, "S3 bucket not found: {BucketName}", options.BucketName);
            return HealthCheckResult.Unhealthy(
                $"S3 bucket '{options.BucketName}' does not exist",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "bucketName", options.BucketName },
                    { "error", "BucketNotFound" }
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check S3 health for bucket: {BucketName}", options.BucketName);
            return HealthCheckResult.Unhealthy(
                $"Failed to check S3 bucket '{options.BucketName}' health",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "bucketName", options.BucketName },
                    { "error", ex.Message }
                });
        }
    }
}
