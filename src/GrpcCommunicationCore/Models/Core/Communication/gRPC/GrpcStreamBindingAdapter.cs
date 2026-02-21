using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC ストリームを <see cref="ObservableCollection{T}"/> に反映し、
/// MVVM のデータバインディングで扱えるようにするアダプターです。
/// </summary>
/// <typeparam name="T">要素型です。</typeparam>
public class GrpcStreamBindingAdapter<T> : ObservableObject, IDisposable
{
    // 実データ格納用の可変コレクションです。公開は ReadOnly で行います。
    private readonly ObservableCollection<T> _items = [];

    // UI スレッドへ戻すための同期コンテキストです。
    private readonly SynchronizationContext? _syncContext;

    // メモリ肥大化を避けるための保持上限です。
    private readonly int _maxItemCount;

    // UI スレッドへまとめて反映する件数です。
    private readonly int _uiBatchSize;

    // 上限超過時に先頭からまとめて削除する件数です。
    private readonly int _trimBatchSize;

    // バインド処理中の状態フラグです。
    private bool _isRunning;

    // 最後に発生したエラーを UI 表示しやすい形で保持します。
    private string? _lastError;

    /// <summary>
    /// <see cref="GrpcStreamBindingAdapter{T}"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="maxItemCount">保持する最大要素数です。</param>
    /// <param name="syncContext">UI スレッドの同期コンテキストです。</param>
    /// <param name="uiBatchSize">UI スレッドへ1回で反映する最大件数です。</param>
    /// <param name="trimBatchSize">上限超過時に先頭から削除する最小件数です。</param>
    public GrpcStreamBindingAdapter(
        int maxItemCount = 2000,
        SynchronizationContext? syncContext = null,
        int uiBatchSize = 64,
        int trimBatchSize = 256)
    {
        if (maxItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItemCount));
        }
        if (uiBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uiBatchSize));
        }
        if (trimBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trimBatchSize));
        }

        _maxItemCount = maxItemCount;
        _uiBatchSize = uiBatchSize;
        _trimBatchSize = trimBatchSize;
        _syncContext = syncContext ?? SynchronizationContext.Current;
        Items = new ReadOnlyObservableCollection<T>(_items);
    }

    /// <summary>
    /// バインド対象の読み取り専用コレクションです。
    /// </summary>
    public ReadOnlyObservableCollection<T> Items { get; }

    /// <summary>
    /// ストリーム反映処理中かどうかを表します。
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>
    /// 最後に発生したエラーメッセージです。
    /// </summary>
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>
    /// ストリームを購読して <see cref="Items"/> に反映します。
    /// </summary>
    public async Task BindAsync(
        IAsyncEnumerable<T> stream,
        bool clearBeforeBind = true,
        CancellationToken cancellationToken = default)
    {
        if (clearBeforeBind)
        {
            await InvokeOnUiThreadAsync(_items.Clear, cancellationToken).ConfigureAwait(false);
        }

        LastError = null;
        IsRunning = true;

        try
        {
            // 1件ごとに UI スレッドへディスパッチすると高頻度更新でCPU使用率が上がるため、
            // 一定件数をまとめて反映します。
            var buffer = new List<T>(_uiBatchSize);

            await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                buffer.Add(item);

                if (buffer.Count >= _uiBatchSize)
                {
                    await FlushBufferedItemsAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
            }

            if (buffer.Count > 0)
            {
                await FlushBufferedItemsAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// 保持している要素をすべてクリアします。
    /// </summary>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return InvokeOnUiThreadAsync(_items.Clear, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// バッファした要素をまとめて UI スレッドへ反映します。
    /// </summary>
    /// <param name="buffer">反映対象のバッファです。</param>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    /// <returns>反映完了を待機するタスクです。</returns>
    private Task FlushBufferedItemsAsync(List<T> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return Task.CompletedTask;
        }

        return InvokeOnUiThreadAsync(() =>
        {
            AppendBatch(buffer);
            buffer.Clear();
        }, cancellationToken);
    }

    /// <summary>
    /// バッチ要素をコレクションへ反映します。
    /// </summary>
    /// <param name="batch">反映する要素群です。</param>
    private void AppendBatch(IReadOnlyList<T> batch)
    {
        // 入力バッチ自体が保持上限以上なら、末尾の最新データのみ残します。
        if (batch.Count >= _maxItemCount)
        {
            _items.Clear();
            var start = batch.Count - _maxItemCount;
            for (var i = start; i < batch.Count; i++)
            {
                _items.Add(batch[i]);
            }

            return;
        }

        TrimHeadForIncoming(batch.Count);

        for (var i = 0; i < batch.Count; i++)
        {
            _items.Add(batch[i]);
        }
    }

    /// <summary>
    /// 追加予定件数を収容できるよう先頭側を削除します。
    /// </summary>
    /// <param name="incomingCount">これから追加する件数です。</param>
    private void TrimHeadForIncoming(int incomingCount)
    {
        var currentCount = _items.Count;
        var overflow = (currentCount + incomingCount) - _maxItemCount;
        if (overflow <= 0)
        {
            return;
        }

        // 1件ずつ削除し続けるとコストが高いため、最低でも trimBatchSize 件はまとめて削除します。
        var removeCount = Math.Min(currentCount, Math.Max(overflow, _trimBatchSize));
        if (removeCount <= 0)
        {
            return;
        }

        // 全件削除のケースは Clear が最小コストです。
        if (removeCount >= currentCount)
        {
            _items.Clear();
            return;
        }

        // 先頭削除件数が多いときは RemoveAt(0) 連打より、残す要素で再構築した方が CPU 負荷が低くなります。
        if (removeCount >= currentCount / 3)
        {
            var remainCount = currentCount - removeCount;
            var survivors = new List<T>(remainCount);
            for (var i = removeCount; i < currentCount; i++)
            {
                survivors.Add(_items[i]);
            }

            _items.Clear();
            for (var i = 0; i < survivors.Count; i++)
            {
                _items.Add(survivors[i]);
            }

            return;
        }

        for (var i = 0; i < removeCount; i++)
        {
            _items.RemoveAt(0);
        }
    }

    /// <summary>
    /// 必要に応じて UI スレッドへディスパッチして処理を実行します。
    /// </summary>
    /// <param name="action">実行する処理です。</param>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    /// <returns>実行完了を待機するタスクです。</returns>
    private Task InvokeOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (_syncContext is null || ReferenceEquals(SynchronizationContext.Current, _syncContext))
        {
            action();
            return Task.CompletedTask;
        }

        // UI スレッド反映完了を待てるよう TaskCompletionSource を使います。
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration? registration = null;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        _syncContext.Post(static state =>
        {
            var (work, completion, tokenRegistration) = ((Action, TaskCompletionSource, CancellationTokenRegistration?))state!;
            try
            {
                work();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                tokenRegistration?.Dispose();
            }
        }, (action, tcs, registration));

        return tcs.Task;
    }
}
