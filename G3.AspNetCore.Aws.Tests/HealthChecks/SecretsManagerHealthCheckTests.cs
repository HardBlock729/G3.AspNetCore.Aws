using System.Threading;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using G3.AspNetCore.Aws.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace G3.AspNetCore.Aws.Tests.HealthChecks;

public class SecretsManagerHealthCheckTests
{
    private static HealthCheckContext MakeContext(IHealthCheck check) => new()
    {
        Registration = new HealthCheckRegistration("test", check, null, null)
    };

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenSecretArnIsNull()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        var logger = Substitute.For<ILogger<SecretsManagerHealthCheck>>();
        var options = new SecretsManagerHealthCheckOptions { SecretArn = null };
        var check = new SecretsManagerHealthCheck(client, options, logger);

        var result = await check.CheckHealthAsync(MakeContext(check), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.False((bool)result.Data["configured"]);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenSecretArnIsEmpty()
    {
        var client = Substitute.For<IAmazonSecretsManager>();
        var logger = Substitute.For<ILogger<SecretsManagerHealthCheck>>();
        var options = new SecretsManagerHealthCheckOptions { SecretArn = string.Empty };
        var check = new SecretsManagerHealthCheck(client, options, logger);

        var result = await check.CheckHealthAsync(MakeContext(check), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
