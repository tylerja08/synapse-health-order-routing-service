using OrderRouting.Api.Models;

namespace OrderRouting.Api.Routing;

public sealed class RouteRequestScheduler : IDisposable
{
    private readonly int _maxQueuedRequests;
    private readonly PriorityQueue<ScheduledRouteRequest, ScheduledPriority> _queue = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private long _sequence;

    public RouteRequestScheduler(IConfiguration configuration)
    {
        _maxQueuedRequests = Math.Max(1, configuration.GetValue("Routing:MaxQueuedRequests", 100));
        _worker = Task.Run(ProcessLoopAsync);
    }

    public Task<RouteOrderResponse> EnqueueAsync(OrderPriority priority, Func<CancellationToken, Task<RouteOrderResponse>> route, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_queue.Count >= _maxQueuedRequests)
            {
                return Task.FromResult(RouteOrderResponse.Failure(["Routing service is temporarily at capacity. Please retry."]));
            }

            var request = new ScheduledRouteRequest(route);
            var priorityValue = new ScheduledPriority(priority == OrderPriority.Rush ? 0 : 1, Interlocked.Increment(ref _sequence));
            _queue.Enqueue(request, priorityValue);
            _available.Release();

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => request.TryCancel(cancellationToken));
            }

            return request.Task;
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await _available.WaitAsync(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            ScheduledRouteRequest? request = null;
            lock (_gate)
            {
                if (_queue.Count > 0)
                {
                    request = _queue.Dequeue();
                }
            }

            if (request is null || request.IsCompleted)
            {
                continue;
            }

            await request.RunAsync(_shutdown.Token);
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _available.Release();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best-effort shutdown for in-flight local requests.
        }

        _available.Dispose();
        _shutdown.Dispose();
    }
}

internal sealed class ScheduledRouteRequest
{
    private readonly Func<CancellationToken, Task<RouteOrderResponse>> _route;
    private readonly TaskCompletionSource<RouteOrderResponse> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ScheduledRouteRequest(Func<CancellationToken, Task<RouteOrderResponse>> route)
    {
        _route = route;
    }

    public Task<RouteOrderResponse> Task => _completion.Task;

    public bool IsCompleted => _completion.Task.IsCompleted;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _completion.TrySetResult(await _route(cancellationToken));
        }
        catch (OperationCanceledException exception)
        {
            _completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }

    public void TryCancel(CancellationToken cancellationToken)
    {
        _completion.TrySetCanceled(cancellationToken);
    }
}

internal readonly record struct ScheduledPriority(int Rank, long Sequence) : IComparable<ScheduledPriority>
{
    public int CompareTo(ScheduledPriority other)
    {
        var rankComparison = Rank.CompareTo(other.Rank);
        return rankComparison != 0 ? rankComparison : Sequence.CompareTo(other.Sequence);
    }
}
