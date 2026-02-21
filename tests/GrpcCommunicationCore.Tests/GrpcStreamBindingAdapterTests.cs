using Models.Core.Communication.gRPC.Tests.Helpers;
using System.Reflection;

namespace Models.Core.Communication.gRPC.Tests;

public class GrpcStreamBindingAdapterTests
{
    [Fact]
    public void Constructor_InvalidMaxItemCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(maxItemCount: 0));
    }

    [Fact]
    public void Constructor_InvalidUiBatchSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(uiBatchSize: 0));
    }

    [Fact]
    public void Constructor_InvalidTrimBatchSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(trimBatchSize: 0));
    }

    [Fact]
    public async Task BindAsync_ClearBeforeBindFalse_PreservesExistingItems()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 10, uiBatchSize: 2, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2]), clearBeforeBind: true);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([3]), clearBeforeBind: false);

        Assert.Equal([1, 2, 3], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_ClearBeforeBindTrue_ClearsExistingItems()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 10, uiBatchSize: 2, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3]), clearBeforeBind: true);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([9]), clearBeforeBind: true);

        Assert.Equal([9], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_WhenStreamThrows_SetsLastErrorAndRethrows()
    {
        var adapter = new GrpcStreamBindingAdapter<int>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            adapter.BindAsync(TestAsyncEnumerable.Throw<int>(new InvalidOperationException("boom"))));

        Assert.Equal("boom", ex.Message);
        Assert.Equal("boom", adapter.LastError);
        Assert.False(adapter.IsRunning);
    }

    [Fact]
    public async Task BindAsync_WhenCanceled_CompletesWithoutThrow()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(uiBatchSize: 1);
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await adapter.BindAsync(
            TestAsyncEnumerable.SlowInfinite(1, TimeSpan.FromMilliseconds(10), cts.Token),
            cancellationToken: cts.Token);

        Assert.False(adapter.IsRunning);
    }

    [Fact]
    public async Task BindAsync_WhenBatchExceedsMax_KeepsOnlyLatestItems()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 5, uiBatchSize: 50, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 10)));

        Assert.Equal([6, 7, 8, 9, 10], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_UsesRebuildBranchWhenLargeTrim()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 9, uiBatchSize: 3, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 9)));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([10, 11, 12]), clearBeforeBind: false);

        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_UsesRemoveAtBranchWhenSmallTrim()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 9, uiBatchSize: 1, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 9)));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([10]), clearBeforeBind: false);

        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_ClearsAllWhenRemoveCountReachesCurrentCount()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 5, uiBatchSize: 1, trimBatchSize: 20);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3, 4, 5]));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([6]), clearBeforeBind: false);

        Assert.Equal([6], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_WithExplicitSyncContext_UsesPostedExecution()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(
            maxItemCount: 10,
            syncContext: new ImmediateSynchronizationContext(),
            uiBatchSize: 2,
            trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3]));

        Assert.Equal([1, 2, 3], adapter.Items.ToArray());
    }

    [Fact]
    public async Task ClearAsync_WhenCanceledDuringUiDispatch_ThrowsOperationCanceledException()
    {
        var context = new DelayedSynchronizationContext();
        var adapter = new GrpcStreamBindingAdapter<int>(syncContext: context);
        using var cts = new CancellationTokenSource();

        var task = adapter.ClearAsync(cts.Token);
        cts.Cancel();
        await context.ExecutePostedAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ClearAsync_ClearsItems()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(uiBatchSize: 1);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3]));

        await adapter.ClearAsync();

        Assert.Empty(adapter.Items);
    }

    [Fact]
    public void Private_FlushBufferedItemsAsync_WhenBufferIsEmpty_ReturnsCompletedTask()
    {
        var adapter = new GrpcStreamBindingAdapter<int>();
        var method = typeof(GrpcStreamBindingAdapter<int>).GetMethod("FlushBufferedItemsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(adapter, [new List<int>(), CancellationToken.None])!;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Private_TrimHeadForIncoming_WhenRemoveCountIsZero_Returns()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 1);
        var method = typeof(GrpcStreamBindingAdapter<int>).GetMethod("TrimHeadForIncoming", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(adapter, [2]);
    }

    [Fact]
    public async Task Private_InvokeOnUiThreadAsync_WhenActionThrows_PropagatesException()
    {
        var adapter = new GrpcStreamBindingAdapter<int>(syncContext: new ImmediateSynchronizationContext());
        var method = typeof(GrpcStreamBindingAdapter<int>).GetMethod("InvokeOnUiThreadAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(adapter, [new Action(() => throw new InvalidOperationException("ui-fail")), CancellationToken.None])!;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("ui-fail", exception.Message);
    }

    [Fact]
    public void Dispose_CanBeCalled()
    {
        var adapter = new GrpcStreamBindingAdapter<int>();
        adapter.Dispose();
    }
}
