using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using ZLogger;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC チャネルを生成して保持するプロバイダーです。
/// </summary>
public sealed class GrpcChannelProvider : IDisposable
{
    // HTTP/2 接続設定を保持し、プロセス寿命で再利用するハンドラーです。
    private readonly SocketsHttpHandler _httpHandler;

    // gRPC 呼び出しに使用するチャネル本体です。
    private readonly GrpcChannel _channel;

    // DI から null が渡された場合も動作できるように NullLogger へフォールバックします。
    private readonly ILogger<GrpcChannelProvider> _logger;

    /// <summary>
    /// <see cref="GrpcChannelProvider"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">通信オプションです。</param>
    /// <param name="logger">ロガーです。<see langword="null"/> の場合は <see cref="NullLogger{TCategoryName}"/> を使用します。</param>
    public GrpcChannelProvider(
        IOptions<GrpcCommunicationOptions> options,
        ILogger<GrpcChannelProvider>? logger)
    {
        _logger = logger ?? NullLogger<GrpcChannelProvider>.Instance;

        var config = options.Value;
        Endpoint = config.Connection.ResolveEndpoint();
        _httpHandler = BuildHttpHandler(config);

        _channel = GrpcChannel.ForAddress(Endpoint, new GrpcChannelOptions
        {
            HttpHandler = _httpHandler,
            MaxReceiveMessageSize = config.Connection.MaxReceiveMessageSize,
            MaxSendMessageSize = config.Connection.MaxSendMessageSize
        });

        CallInvoker = _channel.CreateCallInvoker();
        // ZLogger はメッセージ文字列をそのまま出力する（テキスト出力）使い方に統一しています。
        _logger.ZLogInformation($"Created gRPC channel. endpoint={Endpoint}");
    }

    /// <summary>
    /// 接続先エンドポイントです。
    /// </summary>
    public Uri Endpoint { get; }

    /// <summary>
    /// 生成済みクライアント作成に使用する <see cref="CallInvoker"/> です。
    /// </summary>
    public CallInvoker CallInvoker { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Dispose();
        _httpHandler.Dispose();
    }

    /// <summary>
    /// 通信オプションから HTTP ハンドラーを構築します。
    /// </summary>
    /// <param name="options">通信オプションです。</param>
    /// <returns>構築済みハンドラーです。</returns>
    private static SocketsHttpHandler BuildHttpHandler(GrpcCommunicationOptions options)
    {
        // GUI の常時稼働を想定し、接続を使い捨てず再利用する設定に寄せています。
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = options.Connection.ConnectTimeout,
            KeepAlivePingDelay = options.Connection.KeepAlivePingDelay,
            KeepAlivePingTimeout = options.Connection.KeepAlivePingTimeout,
            PooledConnectionIdleTimeout = options.Connection.PooledConnectionIdleTimeout,
            EnableMultipleHttp2Connections = true
        };

        if (!options.Connection.UseTls)
        {
            return handler;
        }

        var sslOptions = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        // mTLS が有効なときだけクライアント証明書をロードします。
        if (options.Authentication.Mode == GrpcAuthenticationMode.MutualTls)
        {
            if (string.IsNullOrWhiteSpace(options.Authentication.ClientCertificatePath))
            {
                throw new InvalidOperationException("MutualTls authentication requires ClientCertificatePath.");
            }

            var cert = new X509Certificate2(
                options.Authentication.ClientCertificatePath,
                options.Authentication.ClientCertificatePassword);
            sslOptions.ClientCertificates = new X509CertificateCollection { cert };
        }

        if (options.Authentication.AllowInsecureServerCertificate)
        {
            sslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        handler.SslOptions = sslOptions;
        return handler;
    }
}
