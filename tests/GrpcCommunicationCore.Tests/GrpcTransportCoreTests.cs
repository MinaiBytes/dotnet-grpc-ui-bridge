using Grpc.Core;
using Microsoft.Extensions.Options;
using Models.Core.Communication.gRPC.Tests.Helpers;
using NSubstitute;

namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// <see cref="GrpcTransportCore"/> の 4 種 RPC・認証・期限・キャンセル分岐を検証するテストです。
/// </summary>
public class GrpcTransportCoreTests
{
    [Fact]
    public async Task UnaryAsync_ReturnsResponse()
    {
        // テスト説明: Unary 正常系でレスポンスがそのまま返ることを確認します。
        using var fixture = CreateFixture(CreateOptions());

        var value = await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: _ => Task.FromResult(123));

        Assert.Equal(123, value);
    }

    [Fact]
    public async Task UnaryAsync_WhenRpcException_Rethrows()
    {
        // テスト説明: Unary 実行中の RpcException が再送出されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var rpcException = new RpcException(new Status(StatusCode.Internal, "failed"));

        await Assert.ThrowsAsync<RpcException>(() =>
            fixture.Transport.UnaryAsync<int>(
                operationName: "op",
                callExecutor: _ => Task.FromException<int>(rpcException)));
    }

    [Fact]
    public async Task UnaryAsync_AddsApiKeyHeader()
    {
        // テスト説明: ApiKey 認証時に指定ヘッダーが CallOptions へ追加されることを確認します。
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.ApiKey,
            apiKey: "secret",
            apiKeyHeaderName: "x-my-api-key"));

        string? headerValue = null;
        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                headerValue = FindHeader(options.Headers!, "x-my-api-key");
                return Task.FromResult(1);
            });

        Assert.Equal("secret", headerValue);
    }

    [Fact]
    public async Task UnaryAsync_AddsBearerTokenFromFixedValue()
    {
        // テスト説明: 固定 Bearer トークンが Authorization ヘッダーに入ることを確認します。
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.BearerToken,
            bearerToken: "fixed-token"));

        string? authHeader = null;
        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                authHeader = FindHeader(options.Headers!, "authorization");
                return Task.FromResult(1);
            });

        Assert.Equal("Bearer fixed-token", authHeader);
    }

    [Fact]
    public async Task UnaryAsync_AddsBearerTokenFromProvider()
    {
        // テスト説明: 固定値未設定時にプロバイダーから動的トークン取得されることを確認します。
        var provider = Substitute.For<IBearerTokenProvider>();
        provider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string?>("dynamic-token"));
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.BearerToken,
            bearerToken: null), provider);

        string? authHeader = null;
        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                authHeader = FindHeader(options.Headers!, "authorization");
                return Task.FromResult(1);
            });

        await provider.Received(1).GetTokenAsync(Arg.Any<CancellationToken>());
        Assert.Equal("Bearer dynamic-token", authHeader);
    }

    [Fact]
    public async Task UnaryAsync_BearerWithoutAnyToken_ThrowsInvalidOperationException()
    {
        // テスト説明: Bearer 方式でトークン取得手段が無い場合に例外になることを確認します。
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.BearerToken,
            bearerToken: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Transport.UnaryAsync(
                operationName: "op",
                callExecutor: _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task UnaryAsync_BearerProviderReturnsWhitespace_ThrowsInvalidOperationException()
    {
        // テスト説明: プロバイダーが空白トークンを返した場合も不正として扱うことを確認します。
        var provider = Substitute.For<IBearerTokenProvider>();
        provider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<string?>(" "));
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.BearerToken,
            bearerToken: null), provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Transport.UnaryAsync(
                operationName: "op",
                callExecutor: _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task UnaryAsync_ApiKeyWithoutValue_ThrowsInvalidOperationException()
    {
        // テスト説明: ApiKey モードでキー値未設定なら例外になることを確認します。
        using var fixture = CreateFixture(CreateOptions(
            authMode: GrpcAuthenticationMode.ApiKey,
            apiKey: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Transport.UnaryAsync(
                operationName: "op",
                callExecutor: _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task UnaryAsync_UnsupportedAuthenticationMode_ThrowsInvalidOperationException()
    {
        // テスト説明: 未サポート認証モードに対して防御的に例外が出ることを確認します。
        using var fixture = CreateFixture(CreateOptions(authMode: (GrpcAuthenticationMode)999));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Transport.UnaryAsync(
                operationName: "op",
                callExecutor: _ => Task.FromResult(1)));
    }

    [Fact]
    public async Task UnaryAsync_MutualTlsMode_DoesNotAddAuthHeader()
    {
        // テスト説明: mTLS モードでは追加の認証ヘッダーを付与しないことを確認します。
        using var fixture = CreateFixture(CreateOptions(authMode: GrpcAuthenticationMode.MutualTls));

        string? authHeader = null;
        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                authHeader = FindHeader(options.Headers!, "authorization");
                return Task.FromResult(1);
            });

        Assert.Null(authHeader);
    }

    [Fact]
    public async Task UnaryAsync_AddsUserHeadersFromCallSettings()
    {
        // テスト説明: 呼び出し単位ヘッダーが CallOptions に反映されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var settings = new GrpcCallSettings
        {
            Headers = new Metadata { { "x-custom", "value" } }
        };

        string? customValue = null;
        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                customValue = FindHeader(options.Headers!, "x-custom");
                return Task.FromResult(1);
            },
            settings: settings);

        Assert.Equal("value", customValue);
    }

    [Fact]
    public async Task UnaryAsync_UsesDeadlineUtcWhenSpecified()
    {
        // テスト説明: 絶対時刻 DeadlineUtc が最優先で採用されることを確認します。
        using var fixture = CreateFixture(CreateOptions(defaultDeadline: TimeSpan.FromSeconds(60)));
        var deadlineUtc = DateTime.UtcNow.AddMinutes(1);
        var settings = new GrpcCallSettings { DeadlineUtc = deadlineUtc };
        DateTime? actualDeadline = null;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                actualDeadline = options.Deadline;
                return Task.FromResult(1);
            },
            settings: settings);

        Assert.Equal(deadlineUtc, actualDeadline);
    }

    [Fact]
    public async Task UnaryAsync_UsesRelativeDeadlineWhenSpecified()
    {
        // テスト説明: 相対期限 DeadlineFromNow が反映されることを確認します。
        using var fixture = CreateFixture(CreateOptions(defaultDeadline: TimeSpan.FromSeconds(60)));
        var start = DateTime.UtcNow;
        var settings = new GrpcCallSettings { DeadlineFromNow = TimeSpan.FromMilliseconds(300) };
        DateTime? actualDeadline = null;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                actualDeadline = options.Deadline;
                return Task.FromResult(1);
            },
            settings: settings);

        Assert.NotNull(actualDeadline);
        Assert.InRange(actualDeadline!.Value, start.AddMilliseconds(100), start.AddSeconds(2));
    }

    [Fact]
    public async Task UnaryAsync_UsesDefaultDeadlineWhenSettingsDoNotSpecify()
    {
        // テスト説明: 個別設定が無い場合に既定期限が使われることを確認します。
        using var fixture = CreateFixture(CreateOptions(defaultDeadline: TimeSpan.FromMilliseconds(300)));
        var start = DateTime.UtcNow;
        DateTime? actualDeadline = null;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                actualDeadline = options.Deadline;
                return Task.FromResult(1);
            });

        Assert.NotNull(actualDeadline);
        Assert.InRange(actualDeadline!.Value, start.AddMilliseconds(100), start.AddSeconds(2));
    }

    [Fact]
    public async Task UnaryAsync_DoesNotSetDeadline_WhenDefaultDeadlineIsZero()
    {
        // テスト説明: DefaultDeadline=0 なら CallOptions.Deadline が未設定になることを確認します。
        using var fixture = CreateFixture(CreateOptions(defaultDeadline: TimeSpan.Zero));
        DateTime? actualDeadline = DateTime.UtcNow;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                actualDeadline = options.Deadline;
                return Task.FromResult(1);
            });

        Assert.Null(actualDeadline);
    }

    [Fact]
    public async Task UnaryAsync_WhenBothCancellationTokensProvided_UsesLinkedToken()
    {
        // テスト説明: settings 側と外部側の両トークンが連結されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        using var settingsCts = new CancellationTokenSource();
        using var externalCts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Transport.UnaryAsync(
                operationName: "op",
                callExecutor: async options =>
                {
                    externalCts.CancelAfter(20);
                    await Task.Delay(500, options.CancellationToken);
                    return 1;
                },
                settings: new GrpcCallSettings { CancellationToken = settingsCts.Token },
                cancellationToken: externalCts.Token));
    }

    [Fact]
    public async Task UnaryAsync_WhenOnlySettingsTokenCancelable_UsesSettingsToken()
    {
        // テスト説明: settings 側のみキャンセル可能な場合にそのトークンが採用されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        using var settingsCts = new CancellationTokenSource();
        settingsCts.Cancel();
        var settings = new GrpcCallSettings { CancellationToken = settingsCts.Token };
        var captured = false;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                captured = options.CancellationToken.IsCancellationRequested;
                return Task.FromResult(1);
            },
            settings: settings);

        Assert.True(captured);
    }

    [Fact]
    public async Task UnaryAsync_WhenOnlyExternalTokenCancelable_UsesExternalToken()
    {
        // テスト説明: 外部トークンのみキャンセル可能な場合にそれが採用されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        using var externalCts = new CancellationTokenSource();
        externalCts.Cancel();
        var captured = false;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                captured = options.CancellationToken.IsCancellationRequested;
                return Task.FromResult(1);
            },
            cancellationToken: externalCts.Token);

        Assert.True(captured);
    }

    [Fact]
    public async Task UnaryAsync_WhenNoTokenProvided_UsesNoneToken()
    {
        // テスト説明: キャンセルトークン未指定時は CancellationToken.None が使われることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var canBeCanceled = true;

        await fixture.Transport.UnaryAsync(
            operationName: "op",
            callExecutor: options =>
            {
                canBeCanceled = options.CancellationToken.CanBeCanceled;
                return Task.FromResult(1);
            });

        Assert.False(canBeCanceled);
    }

    [Fact]
    public async Task ServerStreamingAsync_ReturnsAllResponses()
    {
        // テスト説明: Server Streaming 正常系で受信要素が順序通り列挙されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var stream = fixture.Transport.ServerStreamingAsync(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateServerStreamingCall(new SequenceAsyncStreamReader<int>([1, 2, 3])));

        var values = await CollectAsync(stream);

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public async Task ServerStreamingAsync_WhenRpcException_Rethrows()
    {
        // テスト説明: Server Streaming の MoveNext で発生した RpcException が再送出されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var rpcException = new RpcException(new Status(StatusCode.Unavailable, "down"));

        var stream = fixture.Transport.ServerStreamingAsync(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateServerStreamingCall(
                new SequenceAsyncStreamReader<int>([1, 2], throwAtMoveNextCall: 1, exception: rpcException)));

        await Assert.ThrowsAsync<RpcException>(() => CollectAsync(stream));
    }

    [Fact]
    public async Task ClientStreamingAsync_WritesAllRequestsAndReturnsResponse()
    {
        // テスト説明: Client Streaming で全リクエスト送信と Complete 実行後に応答取得できることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var responseTask = Task.FromResult("ok");

        var result = await fixture.Transport.ClientStreamingAsync<int, string>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateClientStreamingCall(writer, responseTask),
            requestStream: TestAsyncEnumerable.FromValues([10, 20, 30]));

        Assert.Equal("ok", result);
        Assert.Equal([10, 20, 30], writer.Writes);
        Assert.True(writer.CompleteCalled);
    }

    [Fact]
    public async Task ClientStreamingAsync_WhenRpcException_Rethrows()
    {
        // テスト説明: Client Streaming 開始時の RpcException が再送出されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var rpcException = new RpcException(new Status(StatusCode.PermissionDenied, "denied"));

        await Assert.ThrowsAsync<RpcException>(() =>
            fixture.Transport.ClientStreamingAsync<int, int>(
                operationName: "op",
                callFactory: _ => throw rpcException,
                requestStream: TestAsyncEnumerable.FromValues([1, 2])));
    }

    [Fact]
    public async Task DuplexStreamingAsync_ExchangesRequestsAndResponses()
    {
        // テスト説明: Duplex 正常系で送受信が両立し、期待値を返すことを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var responseReader = new SequenceAsyncStreamReader<int>([100, 200]);

        var stream = fixture.Transport.DuplexStreamingAsync<int, int>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateDuplexStreamingCall(writer, responseReader),
            requestStream: TestAsyncEnumerable.FromValues([1, 2]));

        var response = await CollectAsync(stream);

        Assert.Equal([100, 200], response);
        Assert.Equal([1, 2], writer.Writes);
        Assert.True(writer.CompleteCalled);
    }

    [Fact]
    public async Task DuplexStreamingAsync_WhenResponseThrowsRpcException_Rethrows()
    {
        // テスト説明: Duplex の受信側で発生した RpcException が再送出されることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var rpcException = new RpcException(new Status(StatusCode.Internal, "duplex failed"));
        var responseReader = new SequenceAsyncStreamReader<int>([], throwAtMoveNextCall: 1, exception: rpcException);

        var stream = fixture.Transport.DuplexStreamingAsync<int, int>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateDuplexStreamingCall(writer, responseReader),
            requestStream: TestAsyncEnumerable.FromValues([1, 2]));

        await Assert.ThrowsAsync<RpcException>(() => CollectAsync(stream));
    }

    [Fact]
    public async Task DuplexStreamingAsync_WhenResponseEndsFirst_CancelsRequestWriter()
    {
        // テスト説明: 応答先行終了時に送信側がキャンセルされ、長時間待機しないことを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var responseReader = new SequenceAsyncStreamReader<int>([]);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var stream = fixture.Transport.DuplexStreamingAsync<int, int>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateDuplexStreamingCall(writer, responseReader),
            requestStream: TestAsyncEnumerable.SlowInfinite(1, TimeSpan.FromSeconds(5)));

        var response = await CollectAsync(stream);
        stopwatch.Stop();

        Assert.Empty(response);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DuplexStreamingAsync_WhenWriterDoesNotStop_TimeoutBranchIsHandled()
    {
        // テスト説明: 送信側停止不能時にタイムアウト分岐で復帰できることを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var responseReader = new SequenceAsyncStreamReader<int>([]);
        var nonCancelableRequests = new NonCancelableAsyncEnumerable<int>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var stream = fixture.Transport.DuplexStreamingAsync<int, int>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateDuplexStreamingCall(writer, responseReader),
            requestStream: nonCancelableRequests);

        var response = await CollectAsync(stream);
        stopwatch.Stop();

        Assert.Empty(response);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DuplexStreamingAsync_WhenWriterStopsWithinWait_CompletesWithoutException()
    {
        // テスト説明: 送信側が短時間で停止できる場合、タイムアウトせず正常終了することを確認します。
        using var fixture = CreateFixture(CreateOptions());
        var writer = new RecordingClientStreamWriter<int>();
        var responseReader = new SequenceAsyncStreamReader<int>([]);

        var stream = fixture.Transport.DuplexStreamingAsync<int, int>(
            operationName: "op",
            callFactory: _ => TestGrpcCalls.CreateDuplexStreamingCall(writer, responseReader),
            requestStream: TestAsyncEnumerable.DelayedFiniteIgnoringCancellation([1], TimeSpan.FromMilliseconds(100)));

        var response = await CollectAsync(stream);

        Assert.Empty(response);
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        // テスト補助: IAsyncEnumerable を List へ展開します。
        var result = new List<T>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }

    private static string? FindHeader(Metadata headers, string key)
        // テスト補助: キー一致するヘッダー値を取り出します。
        => headers.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static GrpcCommunicationOptions CreateOptions(
        GrpcAuthenticationMode authMode = GrpcAuthenticationMode.None,
        string? bearerToken = null,
        string? apiKey = null,
        string apiKeyHeaderName = "x-api-key",
        TimeSpan? defaultDeadline = null)
    {
        // テスト補助: 目的の認証/期限条件を持つオプションを生成します。
        return new GrpcCommunicationOptions
        {
            Connection = new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = false,
                DefaultDeadline = defaultDeadline ?? TimeSpan.Zero
            },
            Authentication = new GrpcAuthenticationOptions
            {
                Mode = authMode,
                BearerToken = bearerToken,
                ApiKey = apiKey,
                ApiKeyHeaderName = apiKeyHeaderName
            }
        };
    }

    private static TransportFixture CreateFixture(GrpcCommunicationOptions options, IBearerTokenProvider? tokenProvider = null)
    {
        // テスト補助: ChannelProvider と Transport をまとめて生成します。
        var channelProvider = new GrpcChannelProvider(Options.Create(options), logger: null);
        var transport = new GrpcTransportCore(Options.Create(options), channelProvider, logger: null, tokenProvider);
        return new TransportFixture(transport, channelProvider);
    }

    private sealed class TransportFixture : IDisposable
    {
        public TransportFixture(GrpcTransportCore transport, GrpcChannelProvider channelProvider)
        {
            // テスト補助: テスト終了時にチャネルを確実に破棄できるよう参照を保持します。
            Transport = transport;
            ChannelProvider = channelProvider;
        }

        public GrpcTransportCore Transport { get; }

        private GrpcChannelProvider ChannelProvider { get; }

        public void Dispose()
        {
            ChannelProvider.Dispose();
        }
    }
}
