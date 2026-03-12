using System.Collections.Concurrent;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Owns the generation queue, cancellation, and all job orchestration.
/// Fires events so the ViewModel can update the UI without owning the logic.
/// </summary>
sealed class GenerationService : IDisposable
{
    readonly ImageGenerator m_generator = new();
    readonly ConcurrentQueue<Func<CancellationToken, Task>> m_queue = new();
    CancellationTokenSource? m_cts;
    bool m_queueRunning;

    public event Action<string>? LogMessage;
    public event Action? StatusChanged;
    public event Action<bool>? GeneratingChanged;

    public bool IsGenerating { get; private set; }
    public int QueuedCount => m_queue.Count;
    public ImageGenerator Generator => m_generator;

    public void Enqueue(Func<CancellationToken, Task> job)
    {
        m_queue.Enqueue(job);
        DrainQueue();
    }

    public void Cancel()
    {
        m_cts?.Cancel();
        m_cts = new CancellationTokenSource();
        while (m_queue.TryDequeue(out _)) { }
        SetGenerating(false);
    }

    void DrainQueue()
    {
        if (m_queueRunning || m_queue.IsEmpty) return;
        m_queueRunning = true;
        SetGenerating(true);

        m_cts ??= new CancellationTokenSource();
        CancellationToken ct = m_cts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (m_queue.TryDequeue(out Func<CancellationToken, Task>? job))
                {
                    ct.ThrowIfCancellationRequested();
                    await job(ct);
                    Dispatch(() => StatusChanged?.Invoke());
                }
            }
            catch (OperationCanceledException)
            {
                while (m_queue.TryDequeue(out _)) { }
                Dispatch(() => LogMessage?.Invoke("Cancelled."));
            }
            catch (Exception ex)
            {
                Dispatch(() => LogMessage?.Invoke($"Queue error: {ex.Message}"));
            }
            finally
            {
                m_queueRunning = false;
                Dispatch(() =>
                {
                    SetGenerating(!m_queue.IsEmpty);
                    StatusChanged?.Invoke();
                });
            }
        });
    }

    public void ResetCts()
    {
        m_cts?.Cancel();
        m_cts = new CancellationTokenSource();
    }

    void SetGenerating(bool value)
    {
        if (IsGenerating == value) return;
        IsGenerating = value;
        GeneratingChanged?.Invoke(value);
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);

    public void Dispose()
    {
        m_cts?.Cancel();
        m_generator.Dispose();
    }
}
