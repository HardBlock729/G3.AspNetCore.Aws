namespace G3.AspNetCore.Aws.HealthChecks;

/// <summary>
/// Configuration for the S3 health check.
/// </summary>
public sealed class S3HealthCheckOptions
{
    /// <summary>S3 bucket name to probe for accessibility.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>AWS region where the bucket resides.</summary>
    public string Region { get; set; } = "us-east-1";
}
