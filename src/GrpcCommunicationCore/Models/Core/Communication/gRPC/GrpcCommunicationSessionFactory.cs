using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// 画面表示中のみ有効にしたい短寿命の通信セッションを生成するファクトリーです。
/// </summary>
public sealed class GrpcCommunicationSessionFactory
{
    // セッション生成時に共通オプションを渡すための依存です。
    private readonly IOptions<GrpcCommunicationOptions> _options;

    // ロガー生成のための依存です。未登録でも動作できるよう nullable で保持します。
    private readonly ILoggerFactory? _loggerFactory;

    // 認証が必要な環境で動的トークン取得を有効にするための任意依存です。
    private readonly IBearerTokenProvider? _bearerTokenProvider;

    /// <summary>
    /// <see cref="GrpcCommunicationSessionFactory"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">通信オプションです。</param>
    /// <param name="loggerFactory">ロガーファクトリーです。</param>
    /// <param name="bearerTokenProvider">動的トークンプロバイダーです。</param>
    public GrpcCommunicationSessionFactory(
        IOptions<GrpcCommunicationOptions> options,
        ILoggerFactory? loggerFactory,
        IBearerTokenProvider? bearerTokenProvider = null)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _bearerTokenProvider = bearerTokenProvider;
    }

    /// <summary>
    /// 新しい通信セッションを生成します。
    /// </summary>
    /// <returns>生成したセッションです。</returns>
    public GrpcCommunicationSession CreateSession()
    {
        // セッション単位でチャネルを持たせることで、画面破棄時に接続を明示的に解放できます。
        var channelLogger = _loggerFactory?.CreateLogger<GrpcChannelProvider>();
        var transportLogger = _loggerFactory?.CreateLogger<GrpcTransportCore>();
        var channelProvider = new GrpcChannelProvider(_options, channelLogger);
        var transport = new GrpcTransportCore(_options, channelProvider, transportLogger, _bearerTokenProvider);
        return new GrpcCommunicationSession(channelProvider, transport);
    }
}
