using Grpc.Core;
using System.Runtime.CompilerServices;

namespace Models.Core.Communication.gRPC.Tests.Helpers;

/// <summary>
/// 任意シーケンスを gRPC 受信ストリームとして扱うテスト用リーダーです。
/// </summary>
internal sealed class SequenceAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    // 返却する要素列です。
    private readonly IEnumerator<T> _enumerator;
    // 指定回数目の MoveNext で例外を発生させるための設定値です。
    private readonly int _throwAtMoveNextCall;
    // 発生させる例外です。
    private readonly Exception? _exception;
    // MoveNext 呼び出し回数です。
    private int _moveNextCount;

    public SequenceAsyncStreamReader(
        IEnumerable<T> values,
        int throwAtMoveNextCall = -1,
        Exception? exception = null)
    {
        _enumerator = values.GetEnumerator();
        _throwAtMoveNextCall = throwAtMoveNextCall;
        _exception = exception;
    }

    public T Current { get; private set; } = default!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        // 呼び出し側のキャンセル制御をそのまま反映します。
        cancellationToken.ThrowIfCancellationRequested();
        _moveNextCount++;

        if (_throwAtMoveNextCall > 0 && _moveNextCount == _throwAtMoveNextCall)
        {
            // 指定回数で障害系を再現します。
            return Task.FromException<bool>(_exception ?? new InvalidOperationException("MoveNext failed."));
        }

        if (_enumerator.MoveNext())
        {
            Current = _enumerator.Current;
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

/// <summary>
/// 送信済みメッセージと Complete 呼び出しを記録するテスト用ライターです。
/// </summary>
internal sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
{
    public List<T> Writes { get; } = [];

    public bool CompleteCalled { get; private set; }

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        // 実際の送信の代わりにローカルへ記録します。
        Writes.Add(message);
        return Task.CompletedTask;
    }

    public Task CompleteAsync()
    {
        CompleteCalled = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// キャンセルしても完了しない MoveNextAsync を返すテスト用非同期列挙です。
/// </summary>
internal sealed class NonCancelableAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
    private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public T Current => default!;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<bool> MoveNextAsync() => new(_never.Task);
}

/// <summary>
/// Post された処理を即時実行する同期コンテキストです。
/// </summary>
internal sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
}

/// <summary>
/// Post された処理を遅延実行させる同期コンテキストです。
/// </summary>
internal sealed class DelayedSynchronizationContext : SynchronizationContext
{
    private readonly TaskCompletionSource<(SendOrPostCallback Callback, object? State)> _posted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override void Post(SendOrPostCallback d, object? state)
    {
        _posted.TrySetResult((d, state));
    }

    public async Task ExecutePostedAsync()
    {
        // テスト側の任意タイミングで Post 済み処理を実行します。
        var work = await _posted.Task.ConfigureAwait(false);
        work.Callback(work.State);
    }
}

/// <summary>
/// 非同期列挙のテストデータ生成ヘルパーです。
/// </summary>
internal static class TestAsyncEnumerable
{
    public static async IAsyncEnumerable<T> FromValues<T>(
        IEnumerable<T> values,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 実ストリームに近づけるために 1 要素ごとに非同期境界を作ります。
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return value;
        }
    }

    public static async IAsyncEnumerable<T> Throw<T>(Exception ex)
    {
        // 列挙開始直後に例外を発生させる障害系データです。
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        yield return default!;
#pragma warning restore CS0162
    }

    public static async IAsyncEnumerable<T> SlowInfinite<T>(
        T value,
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // キャンセルまで値を返し続ける長寿命ストリームを再現します。
        while (true)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            yield return value;
        }
    }

    public static async IAsyncEnumerable<T> DelayedFiniteIgnoringCancellation<T>(
        IEnumerable<T> values,
        TimeSpan delay)
    {
        // キャンセルを無視して短時間で完了する列挙を再現します。
        foreach (var value in values)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            yield return value;
        }
    }
}

/// <summary>
/// gRPC の各 Call 型をテストで生成するためのヘルパーです。
/// </summary>
internal static class TestGrpcCalls
{
    public static AsyncServerStreamingCall<TResponse> CreateServerStreamingCall<TResponse>(IAsyncStreamReader<TResponse> responseStream)
    {
        // Header/Trailer/Status は正常系の最小値を返すように固定します。
        return new AsyncServerStreamingCall<TResponse>(
            responseStream,
            Task.FromResult(new Metadata()),
            static () => new Status(StatusCode.OK, string.Empty),
            static () => new Metadata(),
            static () => { });
    }

    public static AsyncClientStreamingCall<TRequest, TResponse> CreateClientStreamingCall<TRequest, TResponse>(
        IClientStreamWriter<TRequest> requestStream,
        Task<TResponse> responseAsync)
    {
        // Header/Trailer/Status は正常系の最小値を返すように固定します。
        return new AsyncClientStreamingCall<TRequest, TResponse>(
            requestStream,
            responseAsync,
            Task.FromResult(new Metadata()),
            static () => new Status(StatusCode.OK, string.Empty),
            static () => new Metadata(),
            static () => { });
    }

    public static AsyncDuplexStreamingCall<TRequest, TResponse> CreateDuplexStreamingCall<TRequest, TResponse>(
        IClientStreamWriter<TRequest> requestStream,
        IAsyncStreamReader<TResponse> responseStream)
    {
        // Header/Trailer/Status は正常系の最小値を返すように固定します。
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            requestStream,
            responseStream,
            Task.FromResult(new Metadata()),
            static () => new Status(StatusCode.OK, string.Empty),
            static () => new Metadata(),
            static () => { });
    }
}
