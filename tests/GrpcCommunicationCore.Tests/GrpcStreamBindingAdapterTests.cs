using Models.Core.Communication.gRPC.Tests.Helpers;
using System.Reflection;

namespace Models.Core.Communication.gRPC.Tests;

/// <summary>
/// <see cref="GrpcStreamBindingAdapter{T}"/> の状態遷移・UI 反映・境界条件を検証するテストです。
/// </summary>
public class GrpcStreamBindingAdapterTests
{
    [Fact]
    public void Constructor_InvalidMaxItemCount_Throws()
    {
        // テスト説明: maxItemCount が 0 以下なら引数例外になることを確認します。
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(maxItemCount: 0));
    }

    [Fact]
    public void Constructor_InvalidUiBatchSize_Throws()
    {
        // テスト説明: uiBatchSize が 0 以下なら引数例外になることを確認します。
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(uiBatchSize: 0));
    }

    [Fact]
    public void Constructor_InvalidTrimBatchSize_Throws()
    {
        // テスト説明: trimBatchSize が 0 以下なら引数例外になることを確認します。
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrpcStreamBindingAdapter<int>(trimBatchSize: 0));
    }

    [Fact]
    public async Task BindAsync_ClearBeforeBindFalse_PreservesExistingItems()
    {
        // テスト説明: clearBeforeBind=false の場合は既存要素を保持して追記されることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 10, uiBatchSize: 2, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2]), clearBeforeBind: true);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([3]), clearBeforeBind: false);

        Assert.Equal([1, 2, 3], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_ClearBeforeBindTrue_ClearsExistingItems()
    {
        // テスト説明: clearBeforeBind=true の場合は既存要素がクリアされることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 10, uiBatchSize: 2, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3]), clearBeforeBind: true);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([9]), clearBeforeBind: true);

        Assert.Equal([9], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_WhenStreamThrows_SetsLastErrorAndRethrows()
    {
        // テスト説明: 受信列挙で例外が発生したとき、LastError 設定と再送出を確認します。
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
        // テスト説明: キャンセル要求時は OperationCanceledException を握りつぶして終了することを確認します。
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
        // テスト説明: 受信バッチが上限を超える場合は最新要素のみ保持することを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 5, uiBatchSize: 50, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 10)));

        Assert.Equal([6, 7, 8, 9, 10], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_UsesRebuildBranchWhenLargeTrim()
    {
        // テスト説明: 大きな先頭トリム時に再構築分岐で正しい結果になることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 9, uiBatchSize: 3, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 9)));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([10, 11, 12]), clearBeforeBind: false);

        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_UsesRemoveAtBranchWhenSmallTrim()
    {
        // テスト説明: 小さな先頭トリム時に RemoveAt 分岐で正しい結果になることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 9, uiBatchSize: 1, trimBatchSize: 1);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues(Enumerable.Range(1, 9)));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([10]), clearBeforeBind: false);

        Assert.Equal([2, 3, 4, 5, 6, 7, 8, 9, 10], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_TrimHead_ClearsAllWhenRemoveCountReachesCurrentCount()
    {
        // テスト説明: 削除件数が現在件数以上の場合は Clear 分岐になることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 5, uiBatchSize: 1, trimBatchSize: 20);

        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3, 4, 5]));
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([6]), clearBeforeBind: false);

        Assert.Equal([6], adapter.Items.ToArray());
    }

    [Fact]
    public async Task BindAsync_WithExplicitSyncContext_UsesPostedExecution()
    {
        // テスト説明: 明示同期コンテキスト指定時も正しく反映できることを確認します。
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
        // テスト説明: UI ディスパッチ中にキャンセルされた場合、キャンセル例外が返ることを確認します。
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
        // テスト説明: ClearAsync が Items を空にできることを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(uiBatchSize: 1);
        await adapter.BindAsync(TestAsyncEnumerable.FromValues([1, 2, 3]));

        await adapter.ClearAsync();

        Assert.Empty(adapter.Items);
    }

    [Fact]
    public void Private_FlushBufferedItemsAsync_WhenBufferIsEmpty_ReturnsCompletedTask()
    {
        // テスト説明: 空バッファ時は即完了タスクを返す private 分岐を確認します。
        var adapter = new GrpcStreamBindingAdapter<int>();
        var method = typeof(GrpcStreamBindingAdapter<int>).GetMethod("FlushBufferedItemsAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(adapter, [new List<int>(), CancellationToken.None])!;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void Private_TrimHeadForIncoming_WhenRemoveCountIsZero_Returns()
    {
        // テスト説明: removeCount<=0 の早期 return 分岐を private 呼び出しで確認します。
        var adapter = new GrpcStreamBindingAdapter<int>(maxItemCount: 1);
        var method = typeof(GrpcStreamBindingAdapter<int>).GetMethod("TrimHeadForIncoming", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        method!.Invoke(adapter, [2]);
    }

    [Fact]
    public async Task Private_InvokeOnUiThreadAsync_WhenActionThrows_PropagatesException()
    {
        // テスト説明: UI 実行アクションの例外が呼び出し元へ伝播することを確認します。
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
        // テスト説明: Dispose を呼び出しても例外が発生しないことを確認します。
        var adapter = new GrpcStreamBindingAdapter<int>();
        adapter.Dispose();
    }
}
