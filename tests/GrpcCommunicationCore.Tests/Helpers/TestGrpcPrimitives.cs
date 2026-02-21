using Grpc.Core;
using System.Runtime.CompilerServices;

namespace Models.Core.Communication.gRPC.Tests.Helpers;

internal sealed class SequenceAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly IEnumerator<T> _enumerator;
    private readonly int _throwAtMoveNextCall;
    private readonly Exception? _exception;
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
        cancellationToken.ThrowIfCancellationRequested();
        _moveNextCount++;

        if (_throwAtMoveNextCall > 0 && _moveNextCount == _throwAtMoveNextCall)
        {
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

internal sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
{
    public List<T> Writes { get; } = [];

    public bool CompleteCalled { get; private set; }

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        Writes.Add(message);
        return Task.CompletedTask;
    }

    public Task CompleteAsync()
    {
        CompleteCalled = true;
        return Task.CompletedTask;
    }
}

internal sealed class NonCancelableAsyncEnumerable<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
    private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public T Current => default!;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<bool> MoveNextAsync() => new(_never.Task);
}

internal sealed class ImmediateSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => d(state);
}

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
        var work = await _posted.Task.ConfigureAwait(false);
        work.Callback(work.State);
    }
}

internal static class TestAsyncEnumerable
{
    public static async IAsyncEnumerable<T> FromValues<T>(
        IEnumerable<T> values,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return value;
        }
    }

    public static async IAsyncEnumerable<T> Throw<T>(Exception ex)
    {
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
        foreach (var value in values)
        {
            await Task.Delay(delay).ConfigureAwait(false);
            yield return value;
        }
    }
}

internal static class TestGrpcCalls
{
    public static AsyncServerStreamingCall<TResponse> CreateServerStreamingCall<TResponse>(IAsyncStreamReader<TResponse> responseStream)
    {
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
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            requestStream,
            responseStream,
            Task.FromResult(new Metadata()),
            static () => new Status(StatusCode.OK, string.Empty),
            static () => new Metadata(),
            static () => { });
    }
}
