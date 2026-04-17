# G3.AspNetCore.Aws

AWS-specific extensions for ASP.NET Core Web APIs. Provides Cognito JWT authentication, S3 and Secrets Manager health checks, and an Npgsql data source builder that transparently rotates credentials from RDS Secrets Manager without restarting the application.

[![Build](https://github.com/HardBlock729/G3.AspNetCore.Aws/actions/workflows/ci.yml/badge.svg)](https://github.com/HardBlock729/G3.AspNetCore.Aws/actions/workflows/ci.yml)
[![NuGet Package](https://img.shields.io/nuget/v/G3Software.Net.AspNetCore.Aws)](https://www.nuget.org/packages/G3Software.Net.AspNetCore.Aws)

**Targets:** net8.0 · net9.0 · net10.0

---

## Installation

```
dotnet add package G3Software.Net.AspNetCore.Aws
```

---

## Features

### Cognito JWT Authentication — `AddG3CognitoJwtAuth()`

Configures `Microsoft.AspNetCore.Authentication.JwtBearer` for AWS Cognito user pools. JWKS keys are fetched once on startup and cached — no per-request key fetches.

```csharp
builder.AddG3CognitoJwtAuth();
```

Reads from environment variables:

| Variable | Description |
|---|---|
| `COGNITO_USER_POOL_ID` | e.g. `us-east-1_abc123` |
| `AWS_REGION` | e.g. `us-east-1` |

**Group-based authorization policies:**

```csharp
builder.AddG3CognitoJwtAuth(options =>
{
    options.AddGroupPolicy("AdminOnly", "admin");
    options.AddGroupPolicy("StaffOrAdmin", "staff", "admin");
});
```

Then use on controllers or endpoints:

```csharp
[Authorize(Policy = "AdminOnly")]
[HttpDelete("{id}")]
public IActionResult Delete(int id) { ... }
```

Policies check the `cognito:groups` claim. `MapInboundClaims` is disabled and audience validation is off (Cognito access tokens don't include `aud`).

---

### Npgsql with RDS Secret Rotation — `BuildNpgsqlDataSourceAsync()`

Builds an `NpgsqlDataSource` that fetches its password from AWS Secrets Manager on startup and refreshes it on a configurable interval. When RDS rotates the secret, the pool picks up the new password automatically — no restart required.

```csharp
var dataSource = await NpgsqlAwsExtensions.BuildNpgsqlDataSourceAsync(
    logger,
    new NpgsqlAwsOptions
    {
        SecretArn  = "arn:aws:secretsmanager:us-east-1:123456789:secret:my-db-secret",
        Host       = "my-cluster.cluster-xyz.us-east-1.rds.amazonaws.com",
        Database   = "myapp",
        MaxPoolSize = 50
    });

builder.Services.AddSingleton(dataSource);
```

**`NpgsqlAwsOptions`**

| Property | Default | Description |
|---|---|---|
| `SecretArn` | `DB_SECRET_ARN` env var | Secrets Manager secret ARN |
| `Host` | `DB_HOST` env var | Database hostname |
| `Database` | `DB_NAME` env var / `"app"` | Database name |
| `Port` | `DB_PORT` env var / `5432` | Database port |
| `MinPoolSize` | `5` | Minimum connection pool size |
| `MaxPoolSize` | `100` | Maximum connection pool size |
| `PasswordRefreshInterval` | `10 minutes` | How often to re-fetch the password |
| `PasswordRefreshFailureRetryInterval` | `30 seconds` | Retry interval after a failed refresh |

**Local development bypass:** set `DB_CONNECTION_STRING` and Secrets Manager is skipped entirely — the connection string is used directly. Useful for local Docker or CI environments.

---

### Health Checks

#### S3 — `AddG3S3HealthCheck()`

Probes an S3 bucket with a lightweight `ListObjectsV2` call. Reports `Degraded` if the response takes over 2 seconds, and `Unhealthy` on `403`/`404` or other errors.

```csharp
builder.Services.AddHealthChecks()
    .AddG3S3HealthCheck(
        bucketName: "my-assets-bucket",
        region: "us-east-1",
        tags: ["ready"]);
```

#### Secrets Manager — `AddG3SecretsManagerHealthCheck()`

Probes a secret with `DescribeSecret` (metadata only — no value is retrieved). Reports `Unhealthy` if the secret is missing or inaccessible.

```csharp
builder.Services.AddHealthChecks()
    .AddG3SecretsManagerHealthCheck(
        secretArn: "arn:aws:secretsmanager:us-east-1:123456789:secret:my-db-secret",
        tags: ["ready"]);
```

If `secretArn` is omitted, it reads `DB_SECRET_ARN` from the environment.

---

## Typical Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Auth
builder.AddG3CognitoJwtAuth(options =>
    options.AddGroupPolicy("AdminOnly", "admin"));

// Database
var dataSource = await NpgsqlAwsExtensions.BuildNpgsqlDataSourceAsync(logger);
builder.Services.AddSingleton(dataSource);

// Health checks
builder.Services.AddHealthChecks()
    .AddG3S3HealthCheck("my-bucket", "us-east-1", tags: ["ready"])
    .AddG3SecretsManagerHealthCheck(tags: ["ready"]);
```

---

## IAM Permissions Required

The application's IAM role needs the following permissions:

```json
{
  "Effect": "Allow",
  "Action": [
    "secretsmanager:GetSecretValue",
    "secretsmanager:DescribeSecret"
  ],
  "Resource": "arn:aws:secretsmanager:*:*:secret:your-secret-*"
}
```

For S3 health checks, add `s3:ListBucket` on the target bucket.

---

## Related Packages

- [G3Software.Net.AspNetCore.Core](https://www.nuget.org/packages/G3Software.Net.AspNetCore.Core) — Core middleware pipeline, logging, CORS, rate limiting, exception handling, and more
