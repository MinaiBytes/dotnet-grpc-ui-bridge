using Grpc.Core;

namespace Models.Core.Communication.gRPC.Tests;

public class GrpcCommunicationOptionsTests
{
    [Fact]
    public void DefaultConstructor_InitializesNestedOptions()
    {
        var options = new GrpcCommunicationOptions();

        Assert.NotNull(options.Connection);
        Assert.NotNull(options.Authentication);
    }

    [Fact]
    public void AuthenticationOptions_HaveExpectedDefaults()
    {
        var auth = new GrpcAuthenticationOptions();

        Assert.Equal(GrpcAuthenticationMode.None, auth.Mode);
        Assert.Equal("x-api-key", auth.ApiKeyHeaderName);
        Assert.Null(auth.ClientCertificatePassword);
        Assert.False(auth.AllowInsecureServerCertificate);
    }

    [Fact]
    public void CallSettings_CanStoreValues()
    {
        var headers = new Metadata { { "x-test", "ok" } };
        var now = DateTime.UtcNow;
        var cts = new CancellationTokenSource();
        var settings = new GrpcCallSettings
        {
            Headers = headers,
            DeadlineUtc = now,
            DeadlineFromNow = TimeSpan.FromSeconds(3),
            CancellationToken = cts.Token
        };

        Assert.Same(headers, settings.Headers);
        Assert.Equal(now, settings.DeadlineUtc);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.DeadlineFromNow);
        Assert.Equal(cts.Token, settings.CancellationToken);
    }
}
