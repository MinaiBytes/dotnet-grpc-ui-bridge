using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using ZLogger;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// 4種類の gRPC 呼び出し方式を統一的に実行する軽量な通信コア実装です。
/// </summary>
public sealed class GrpcTransportCore
{
    // 呼び出し共通設定（接続/認証/デッドライン）です。
    private readonly GrpcCommunicationOptions _options;

    // 共有チャネルから CallInvoker を受け取るための依存です。
    private readonly GrpcChannelProvider _channelProvider;

    // DI から null が渡された場合も例外にならないよう NullLogger へフォールバックします。
    private readonly ILogger<GrpcTransportCore> _logger;

    // 固定トークン以外の Bearer 認証に対応するための任意依存です。
    private readonly IBearerTokenProvider? _bearerTokenProvider;

    /// <summary>
    /// <see cref="GrpcTransportCore"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="options">通信オプションです。</param>
    /// <param name="channelProvider">チャネルプロバイダーです。</param>
    /// <param name="logger">ロガーです。<see langword="null"/> の場合は <see cref="NullLogger{TCategoryName}"/> を使用します。</param>
    /// <param name="bearerTokenProvider">動的トークンプロバイダーです。</param>
    public GrpcTransportCore(
        IOptions<GrpcCommunicationOptions> options,
        GrpcChannelProvider channelProvider,
        ILogger<GrpcTransportCore>? logger,
        IBearerTokenProvider? bearerTokenProvider = null)
    {
        _options = options.Value;
        _channelProvider = channelProvider;
        _logger = logger ?? NullLogger<GrpcTransportCore>.Instance;
        _bearerTokenProvider = bearerTokenProvider;
    }

    /// <summary>
    /// Unary RPC を実行します。
    /// </summary>
    public async Task<TResponse> UnaryAsync<TResponse>(
        string operationName,
        Func<CallOptions, Task<TResponse>> callExecutor,
        GrpcCallSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        // 毎呼び出しでヘッダーと期限を組み立てることで、呼び出し単位の設定上書きを許可します。
        var (callOptions, linkedTokenSource) = await BuildCallOptionsAsync(settings, cancellationToken).ConfigureAwait(false);
        using var linkedTokenSourceScope = linkedTokenSource;

        try
        {
            return await callExecutor(callOptions).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            // 文字列ベースで出力することで、ログフォーマッタ依存を減らします。
            _logger.ZLogWarning($"gRPC unary failed. operation={operationName} endpoint={_channelProvider.Endpoint} status={ex.StatusCode} detail={ex.Status.Detail}");
            throw;
        }
    }

    /// <summary>
    /// Server Streaming RPC を実行します。
    /// </summary>
    public async IAsyncEnumerable<TResponse> ServerStreamingAsync<TResponse>(
        string operationName,
        Func<CallOptions, AsyncServerStreamingCall<TResponse>> callFactory,
        GrpcCallSettings? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (callOptions, linkedTokenSource) = await BuildCallOptionsAsync(settings, cancellationToken).ConfigureAwait(false);
        using var linkedTokenSourceScope = linkedTokenSource;
        using var call = callFactory(callOptions);

        // async iterator では yield を含む try/catch が使えないため、MoveNext 部分のみを捕捉します。
        while (true)
        {
            bool movedNext;
            try
            {
                movedNext = await call.ResponseStream.MoveNext(callOptions.CancellationToken).ConfigureAwait(false);
            }

            catch (RpcException ex)
            {
                _logger.ZLogWarning($"gRPC server stream failed. operation={operationName} endpoint={_channelProvider.Endpoint} status={ex.StatusCode} detail={ex.Status.Detail}");
                throw;
            }

            if (!movedNext)
            {
                break;
            }

            // MoveNextAsync で順次受信し、到着した値をそのまま上位へ流します。
            yield return call.ResponseStream.Current;
        }
    }

    /// <summary>
    /// Client Streaming RPC を実行します。
    /// </summary>
    public async Task<TResponse> ClientStreamingAsync<TRequest, TResponse>(
        string operationName,
        Func<CallOptions, AsyncClientStreamingCall<TRequest, TResponse>> callFactory,
        IAsyncEnumerable<TRequest> requestStream,
        GrpcCallSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        var (callOptions, linkedTokenSource) = await BuildCallOptionsAsync(settings, cancellationToken).ConfigureAwait(false);
        using var linkedTokenSourceScope = linkedTokenSource;

        try
        {
            using var call = callFactory(callOptions);
            // 送信が終わるまではレスポンスを待たず、順方向に書き込みます。
            await foreach (var request in requestStream.WithCancellation(callOptions.CancellationToken).ConfigureAwait(false))
            {
                await call.RequestStream.WriteAsync(request).ConfigureAwait(false);
            }

            await call.RequestStream.CompleteAsync().ConfigureAwait(false);
            return await call.ResponseAsync.ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            _logger.ZLogWarning($"gRPC client stream failed. operation={operationName} endpoint={_channelProvider.Endpoint} status={ex.StatusCode} detail={ex.Status.Detail}");
            throw;
        }
    }

    /// <summary>
    /// Bidirectional Streaming RPC を実行します。
    /// </summary>
    public async IAsyncEnumerable<TResponse> DuplexStreamingAsync<TRequest, TResponse>(
        string operationName,
        Func<CallOptions, AsyncDuplexStreamingCall<TRequest, TResponse>> callFactory,
        IAsyncEnumerable<TRequest> requestStream,
        GrpcCallSettings? settings = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (callOptions, linkedTokenSource) = await BuildCallOptionsAsync(settings, cancellationToken).ConfigureAwait(false);
        using var linkedTokenSourceScope = linkedTokenSource;
        using var call = callFactory(callOptions);
        using var writeCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(callOptions.CancellationToken);

        // 双方向ストリームは送信と受信を並行させる必要があるため、送信を別タスクで実行します。
        var writeTask = WriteRequestsAsync(call.RequestStream, requestStream, writeCancellationSource.Token);
        try
        {
            // async iterator では yield を含む try/catch が使えないため、MoveNext 部分のみを捕捉します。
            while (true)
            {
                bool movedNext;
                try
                {
                    movedNext = await call.ResponseStream.MoveNext(callOptions.CancellationToken).ConfigureAwait(false);
                }
                catch (RpcException ex)
                {
                    _logger.ZLogWarning($"gRPC duplex stream failed. operation={operationName} endpoint={_channelProvider.Endpoint} status={ex.StatusCode} detail={ex.Status.Detail}");
                    throw;
                }

                if (!movedNext)
                {
                    break;
                }

                yield return call.ResponseStream.Current;
            }
        }
        finally
        {
            // 応答側が先に終了したら送信タスクへ停止を通知し、終了待ちのハングを防ぎます。
            if (!writeTask.IsCompleted)
            {
                writeCancellationSource.Cancel();
            }

            try
            {
                if (writeTask.IsCompleted)
                {
                    await writeTask.ConfigureAwait(false);
                }
                else
                {
                    await writeTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (writeCancellationSource.IsCancellationRequested || callOptions.CancellationToken.IsCancellationRequested)
            {
            }
            catch (TimeoutException)
            {
                _logger.ZLogWarning($"gRPC duplex write did not stop in time. operation={operationName} endpoint={_channelProvider.Endpoint}");
            }
        }
    }

    /// <summary>
    /// 呼び出し単位のオプションを組み立てます。
    /// </summary>
    /// <param name="settings">呼び出し設定です。</param>
    /// <param name="externalCancellationToken">外部キャンセルトークンです。</param>
    /// <returns>組み立て済みの <see cref="CallOptions"/> と、必要時のみ生成される連結トークンソースです。</returns>
    private async Task<(CallOptions CallOptions, CancellationTokenSource? LinkedTokenSource)> BuildCallOptionsAsync(
        GrpcCallSettings? settings,
        CancellationToken externalCancellationToken)
    {
        var (effectiveCancellationToken, linkedTokenSource) =
            CreateEffectiveCancellationToken(settings?.CancellationToken ?? default, externalCancellationToken);
        var headers = new Metadata();

        await AddAuthenticationHeadersAsync(headers, effectiveCancellationToken).ConfigureAwait(false);
        if (settings?.Headers is not null)
        {
            foreach (var header in settings.Headers)
            {
                headers.Add(header);
            }
        }

        var deadline = ResolveDeadline(settings);
        return (new CallOptions(headers, deadline, effectiveCancellationToken), linkedTokenSource);
    }

    /// <summary>
    /// 認証モードに応じたヘッダーを追加します。
    /// </summary>
    /// <param name="headers">追加先ヘッダーです。</param>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    private async Task AddAuthenticationHeadersAsync(Metadata headers, CancellationToken cancellationToken)
    {
        switch (_options.Authentication.Mode)
        {
            case GrpcAuthenticationMode.None:
            case GrpcAuthenticationMode.MutualTls:
                return;

            case GrpcAuthenticationMode.BearerToken:
                // Bearer は固定値優先、未設定時のみプロバイダーから動的取得します。
                headers.Add("authorization", $"Bearer {await ResolveBearerTokenAsync(cancellationToken).ConfigureAwait(false)}");
                return;

            case GrpcAuthenticationMode.ApiKey:
                if (string.IsNullOrWhiteSpace(_options.Authentication.ApiKey))
                {
                    throw new InvalidOperationException("ApiKey authentication requires ApiKey.");
                }

                headers.Add(_options.Authentication.ApiKeyHeaderName, _options.Authentication.ApiKey);
                return;

            default:
                throw new InvalidOperationException($"Unsupported auth mode: {_options.Authentication.Mode}");
        }
    }

    /// <summary>
    /// Bearer トークンを解決します。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    /// <returns>解決したトークン文字列です。</returns>
    private async ValueTask<string> ResolveBearerTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.Authentication.BearerToken))
        {
            return _options.Authentication.BearerToken;
        }

        if (_bearerTokenProvider is not null)
        {
            var dynamicToken = await _bearerTokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(dynamicToken))
            {
                return dynamicToken;
            }
        }

        throw new InvalidOperationException("BearerToken authentication requires BearerToken or IBearerTokenProvider.");
    }

    /// <summary>
    /// 呼び出し期限を解決します。
    /// </summary>
    /// <param name="settings">呼び出し設定です。</param>
    /// <returns>解決した期限。未設定の場合は <see langword="null"/>。</returns>
    private DateTime? ResolveDeadline(GrpcCallSettings? settings)
    {
        if (settings?.DeadlineUtc is not null)
        {
            return settings.DeadlineUtc;
        }

        if (settings?.DeadlineFromNow is not null)
        {
            return DateTime.UtcNow + settings.DeadlineFromNow.Value;
        }

        if (_options.Connection.DefaultDeadline > TimeSpan.Zero)
        {
            return DateTime.UtcNow + _options.Connection.DefaultDeadline;
        }

        return null;
    }

    /// <summary>
    /// 有効なキャンセルトークンを決定します。
    /// </summary>
    /// <param name="first">呼び出し設定側トークンです。</param>
    /// <param name="second">メソッド引数側トークンです。</param>
    /// <returns>実際に使用するトークンと、必要時のみ生成される連結トークンソースです。</returns>
    private static (CancellationToken Token, CancellationTokenSource? LinkedTokenSource) CreateEffectiveCancellationToken(
        CancellationToken first,
        CancellationToken second)
    {
        if (first.CanBeCanceled && second.CanBeCanceled)
        {
            // 呼び出し設定側と外部側の両方でキャンセル可能な場合は、両者を連結します。
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(first, second);
            return (linkedTokenSource.Token, linkedTokenSource);
        }

        if (first.CanBeCanceled)
        {
            return (first, null);
        }

        if (second.CanBeCanceled)
        {
            return (second, null);
        }

        return (CancellationToken.None, null);
    }

    /// <summary>
    /// 双方向ストリーミング用の要求ストリーム送信処理です。
    /// </summary>
    /// <typeparam name="TRequest">要求要素型です。</typeparam>
    /// <param name="requestWriter">要求ストリームライターです。</param>
    /// <param name="requestStream">送信元ストリームです。</param>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    private static async Task WriteRequestsAsync<TRequest>(
        IClientStreamWriter<TRequest> requestWriter,
        IAsyncEnumerable<TRequest> requestStream,
        CancellationToken cancellationToken)
    {
        await foreach (var request in requestStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await requestWriter.WriteAsync(request).ConfigureAwait(false);
        }

        await requestWriter.CompleteAsync().ConfigureAwait(false);
    }
}
