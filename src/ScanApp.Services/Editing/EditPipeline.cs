using ScanApp.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ScanApp.Services.Editing;

/// <summary>
/// Debounced, cancellable edit rendering for one page. Rapid changes (e.g. slider drags) coalesce:
/// only the newest requested state renders, an in-flight render for a stale state is cancelled, and
/// results are delivered in order. Replaces the hand-rolled recompute flags that were race-prone.
/// Thread-safe; call <see cref="Request"/> from any thread.
/// </summary>
public sealed class EditPipeline : IDisposable
{
    private readonly Image<Rgba32> _original;
    private readonly Action<Image<Rgba32>, PageEdits> _onRendered;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();

    private PageEdits? _pending;
    private CancellationTokenSource? _cts;
    private Task _worker = Task.CompletedTask;
    private bool _disposed;

    /// <param name="original">Pristine source; the pipeline clones per render and never mutates it.</param>
    /// <param name="onRendered">
    /// Receives the rendered image + the edits it represents, on a thread-pool thread. The receiver
    /// owns the image. Never called for stale (superseded) states.
    /// </param>
    /// <param name="debounce">Quiet period before rendering; null = 120 ms.</param>
    public EditPipeline(Image<Rgba32> original, Action<Image<Rgba32>, PageEdits> onRendered, TimeSpan? debounce = null)
    {
        _original = original ?? throw new ArgumentNullException(nameof(original));
        _onRendered = onRendered ?? throw new ArgumentNullException(nameof(onRendered));
        _debounce = debounce ?? TimeSpan.FromMilliseconds(120);
    }

    /// <summary>Requests a render of the given edit state, superseding any not-yet-rendered request.</summary>
    public void Request(PageEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = edits;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Chain onto the previous worker so renders are serialized (no overlapping CPU work,
            // no out-of-order delivery).
            _worker = _worker.ContinueWith(
                _ => RunOne(token),
                CancellationToken.None,
                TaskContinuationOptions.RunContinuationsAsynchronously,
                TaskScheduler.Default).Unwrap();
        }
    }

    private async Task RunOne(CancellationToken token)
    {
        PageEdits edits;
        try
        {
            await Task.Delay(_debounce, token).ConfigureAwait(false);
            lock (_gate)
            {
                if (_pending is null)
                {
                    return;
                }
                edits = _pending;
                _pending = null;
            }

            var rendered = EditRenderer.Render(_original, edits);
            if (token.IsCancellationRequested)
            {
                rendered.Dispose(); // superseded while rendering
                return;
            }
            _onRendered(rendered, edits);
        }
        catch (OperationCanceledException)
        {
            // superseded; nothing to deliver
        }
    }

    /// <summary>Waits for any in-flight work to finish (used by tests and teardown).</summary>
    public Task DrainAsync()
    {
        Task worker;
        lock (_gate)
        {
            worker = _worker;
        }
        return worker;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pending = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
