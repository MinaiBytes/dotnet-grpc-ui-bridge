using Grpc.Core;

namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// 通信オプション系の POCO が想定どおり初期化・保持できることを確認するテストです。
/// </summary>
public class GrpcCommunicationOptionsTests
{
    [Fact]
    public void DefaultConstructor_InitializesNestedOptions()
    {
        // テスト説明: 既定コンストラクターで下位オプションが null にならないことを検証します。
        var options = new GrpcCommunicationOptions();

        Assert.NotNull(options.Connection);
        Assert.NotNull(options.Authentication);
    }

    [Fact]
    public void AuthenticationOptions_HaveExpectedDefaults()
    {
        // テスト説明: 認証オプションの安全側デフォルト値を検証します。
        var auth = new GrpcAuthenticationOptions();

        Assert.Equal(GrpcAuthenticationMode.None, auth.Mode);
        Assert.Equal("x-api-key", auth.ApiKeyHeaderName);
        Assert.Null(auth.ClientCertificatePassword);
        Assert.False(auth.AllowInsecureServerCertificate);
    }

    [Fact]
    public void CallSettings_CanStoreValues()
    {
        // テスト説明: 呼び出し設定に代入した値がそのまま保持されることを検証します。
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
