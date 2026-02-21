namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// <see cref="GrpcConnectionOptions"/> の接続先解決と既定値を検証するテストです。
/// </summary>
public class GrpcConnectionOptionsTests
{
    [Fact]
    public void ResolveEndpoint_ReturnsExplicitEndpoint_WhenEndpointIsSet()
    {
        // テスト説明: Endpoint を明示指定した場合、Host/Port より優先されることを確認します。
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
        // テスト説明: TLS 有効時は https スキームで URI が構築されることを確認します。
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
        // テスト説明: TLS 無効時は http スキームで URI が構築されることを確認します。
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
        // テスト説明: GUI 向け軽量設定の既定値が維持されていることを確認します。
        var options = new GrpcConnectionOptions();

        Assert.Equal(TimeSpan.Zero, options.DefaultDeadline);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.KeepAlivePingDelay);
        Assert.Equal(Timeout.InfiniteTimeSpan, options.KeepAlivePingTimeout);
    }
}
