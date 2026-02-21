using Grpc.Core;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// 画面単位など短い寿命で利用する gRPC 通信セッションです。
/// </summary>
public sealed class GrpcCommunicationSession : IDisposable
{
    /// <summary>
    /// <see cref="GrpcCommunicationSession"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="channelProvider">チャネルプロバイダーです。</param>
    /// <param name="transport">通信コアです。</param>
    public GrpcCommunicationSession(GrpcChannelProvider channelProvider, GrpcTransportCore transport)
    {
        ChannelProvider = channelProvider;
        Transport = transport;
    }

    /// <summary>
    /// セッションに紐づくチャネルプロバイダーです。
    /// </summary>
    public GrpcChannelProvider ChannelProvider { get; }

    /// <summary>
    /// セッションに紐づく通信コアです。
    /// </summary>
    public GrpcTransportCore Transport { get; }

    /// <summary>
    /// gRPC クライアント生成に利用する <see cref="CallInvoker"/> です。
    /// </summary>
    public CallInvoker CallInvoker => ChannelProvider.CallInvoker;

    /// <summary>
    /// セッション接続先エンドポイントです。
    /// </summary>
    public Uri Endpoint => ChannelProvider.Endpoint;

    /// <inheritdoc />
    public void Dispose()
    {
        // セッション破棄時にチャネルとハンドラーを解放します。
        ChannelProvider.Dispose();
    }
}
