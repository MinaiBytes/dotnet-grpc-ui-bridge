using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// DI 登録拡張とオプション検証ロジックを確認するテストです。
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGrpcCommunicationCore_RegistersCoreServices()
    {
        // テスト説明: 拡張メソッドで通信コアの主要サービスが解決可能になることを検証します。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcCommunicationCore(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = false
            });
            SetAuthentication(options, new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.None
            });
        });

        using var provider = services.BuildServiceProvider();
        using var channelProvider = provider.GetRequiredService<GrpcChannelProvider>();
        var transport = provider.GetRequiredService<GrpcTransportCore>();

        Assert.NotNull(channelProvider.CallInvoker);
        Assert.NotNull(transport);
    }

    [Fact]
    public void AddGrpcCommunicationCore_InvalidHost_ThrowsOptionsValidationException()
    {
        // テスト説明: Endpoint 未指定時に Host が不正なら検証エラーになることを確認します。
        using var provider = BuildProvider(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Host = " ",
                Port = 50051,
                UseTls = false
            });
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>().Value);

        Assert.Contains("Invalid gRPC communication options.", exception.Failures);
    }

    [Fact]
    public void AddGrpcCommunicationCore_InvalidPort_ThrowsOptionsValidationException()
    {
        // テスト説明: Endpoint 未指定時に Port が不正なら検証エラーになることを確認します。
        using var provider = BuildProvider(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 70000,
                UseTls = false
            });
        });

        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>().Value);
    }

    [Fact]
    public void AddGrpcCommunicationCore_ApiKeyModeWithoutKey_ThrowsOptionsValidationException()
    {
        // テスト説明: ApiKey 認証でキー本体が無い場合に検証エラーになることを確認します。
        using var provider = BuildProvider(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = false
            });
            SetAuthentication(options, new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.ApiKey,
                ApiKeyHeaderName = "x-api-key",
                ApiKey = null
            });
        });

        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>().Value);
    }

    [Fact]
    public void AddGrpcCommunicationCore_MutualTlsWithoutCert_ThrowsOptionsValidationException()
    {
        // テスト説明: mTLS 選択時に証明書パスが無い場合に検証エラーになることを確認します。
        using var provider = BuildProvider(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = true
            });
            SetAuthentication(options, new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.MutualTls,
                ClientCertificatePath = null
            });
        });

        Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>().Value);
    }

    [Fact]
    public void AddGrpcCommunicationCore_EndpointSpecified_SkipsHostPortValidation()
    {
        // テスト説明: Endpoint を直接指定した場合、Host/Port の検証がスキップされることを確認します。
        using var provider = BuildProvider(options =>
        {
            SetConnection(options, new GrpcConnectionOptions
            {
                Endpoint = new Uri("http://localhost:50099"),
                Host = " ",
                Port = -1,
                UseTls = false
            });
        });

        var value = provider.GetRequiredService<IOptions<GrpcCommunicationOptions>>().Value;
        Assert.Equal("http://localhost:50099/", value.Connection.ResolveEndpoint().ToString());
    }

    private static ServiceProvider BuildProvider(Action<GrpcCommunicationOptions> configure)
    {
        // テスト補助: 最小構成の DI コンテナーを組み立てます。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcCommunicationCore(configure);
        return services.BuildServiceProvider();
    }

    private static void SetConnection(GrpcCommunicationOptions options, GrpcConnectionOptions value)
    {
        // テスト補助: init 専用プロパティへテスト値を設定するため、リフレクションを使用します。
        var property = typeof(GrpcCommunicationOptions).GetProperty(nameof(GrpcCommunicationOptions.Connection), BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(options, value);
    }

    private static void SetAuthentication(GrpcCommunicationOptions options, GrpcAuthenticationOptions value)
    {
        // テスト補助: init 専用プロパティへテスト値を設定するため、リフレクションを使用します。
        var property = typeof(GrpcCommunicationOptions).GetProperty(nameof(GrpcCommunicationOptions.Authentication), BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        property!.SetValue(options, value);
    }
}
