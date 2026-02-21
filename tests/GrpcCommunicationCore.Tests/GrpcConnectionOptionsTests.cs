namespace Models.Core.Communication.gRPC.Tests;

public class GrpcConnectionOptionsTests
{
    [Fact]
    public void ResolveEndpoint_ReturnsExplicitEndpoint_WhenEndpointIsSet()
    {
        var endpoint = new Uri("https://example.local:50051");
        var options = new GrpcConnectionOptions
        {
            Host = "ignored",
            Port = 1,
            Endpoint = endpoint
        };

        var actual = options.ResolveEndpoint();

        Assert.Equal(endpoint, actual);
    }

    [Fact]
    public void ResolveEndpoint_BuildsHttpsUri_WhenTlsEnabled()
    {
        var options = new GrpcConnectionOptions
        {
            Host = "localhost",
            Port = 50051,
            UseTls = true
        };

        var actual = options.ResolveEndpoint();

        Assert.Equal("https://localhost:50051/", actual.ToString());
    }

    [Fact]
    public void ResolveEndpoint_BuildsHttpUri_WhenTlsDisabled()
    {
        var options = new GrpcConnectionOptions
        {
            Host = "127.0.0.1",
            Port = 50052,
            UseTls = false
        };

        var actual = options.ResolveEndpoint();

        Assert.Equal("http://127.0.0.1:50052/", actual.ToString());
    }

    [Fact]
    public void DefaultValues_AreLightweightForGui()
    {
        var options = new GrpcConnectionOptions();

        Assert.Equal(TimeSpan.Zero, options.DefaultDeadline);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.KeepAlivePingDelay);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.KeepAlivePingTimeout);
    }
}
