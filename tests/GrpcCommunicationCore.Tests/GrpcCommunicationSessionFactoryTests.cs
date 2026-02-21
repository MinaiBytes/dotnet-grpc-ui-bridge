using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// 画面単位セッション生成ファクトリーの挙動を確認するテストです。
/// </summary>
public class GrpcCommunicationSessionFactoryTests
{
    [Fact]
    public void CreateSession_CreatesUsableSession()
    {
        // テスト説明: ファクトリーからセッション生成し、Transport/CallInvoker が利用可能なことを確認します。
        var options = Options.Create(new GrpcCommunicationOptions
        {
            Connection = new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = false
            }
        });

        var factory = new GrpcCommunicationSessionFactory(options, NullLoggerFactory.Instance);
        using var session = factory.CreateSession();

        Assert.NotNull(session.Transport);
        Assert.NotNull(session.CallInvoker);
        Assert.Equal("http://127.0.0.1:50051/", session.Endpoint.ToString());
    }

    [Fact]
    public async Task CreateSession_WithTokenProvider_PassesProviderToTransport()
    {
        // テスト説明: BearerToken 方式でプロバイダー経由トークンを使えることを確認します。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBearerTokenProvider>(new TestBearerTokenProvider("dynamic-token"));
        services.AddOptions<GrpcCommunicationOptions>().Configure(options =>
        {
            options.Connection = new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = false
            };
            options.Authentication = new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.BearerToken
            };
        });

        using var provider = services.BuildServiceProvider();
        var sessionFactory = new GrpcCommunicationSessionFactory(
            provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<IBearerTokenProvider>());

        using var session = sessionFactory.CreateSession();
        string? authHeader = null;
        await session.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                authHeader = options.Headers?.FirstOrDefault(x => x.Key == "authorization")?.Value;
                return Task.FromResult(1);
            });

        Assert.Equal("Bearer dynamic-token", authHeader);
    }

    private sealed class TestBearerTokenProvider : IBearerTokenProvider
    {
        private readonly string _token;

        public TestBearerTokenProvider(string token)
        {
            _token = token;
        }

        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
            => new(_token);
    }
}
