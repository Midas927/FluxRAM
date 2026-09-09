using System.Diagnostics;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;

namespace FluxRAM.App.Configuration;

public enum DeepReleaseOutcome { Closed, Stopped, Skipped, Declined, TimedOut, Cancelled, Failed }

public sealed record DeepReleaseItemResult(
    string Name, int? ProcessId, bool IsService, DeepReleaseOutcome Outcome, string Message);

public sealed record DeepReleaseProgress(
    string Name, int ProcessedCount, int TotalCount, DeepReleaseItemResult? Result = null);

public sealed record DeepReleaseResult(IReadOnlyList<DeepReleaseItemResult> Items, bool WasCancelled)
{
    public int TotalProcessCount => Items.Count(item => !item.IsService);
    public int ClosedProcessCount => Items.Count(item => !item.IsService && item.Outcome == DeepReleaseOutcome.Closed);
    public int TotalServiceCount => Items.Count(item => item.IsService);
    public int StoppedServiceCount => Items.Count(item => item.IsService && item.Outcome == DeepReleaseOutcome.Stopped);
}

public interface IDeepReleaseProcess : IDisposable
{
    ProcessSnapshot ReadSnapshot();
    bool HasExited { get; }
    bool CloseMainWindow();
    void Kill();
}

public sealed class DeepReleaseExecutor
{
    private readonly ServiceKillerService _services;
    private readonly Func<int, IDeepReleaseProcess> _openProcess;
    private readonly TimeSpan _gracefulTimeout;
    private readonly TimeSpan _forceTimeout;
    private readonly TimeSpan _serviceTimeout;

    public DeepReleaseExecutor(
        ServiceKillerService? services = null,
        Func<int, IDeepReleaseProcess>? openProcess = null,
        TimeSpan? gracefulTimeout = null,
        TimeSpan? forceTimeout = null,
        TimeSpan? serviceTimeout = null)
    {
        _services = services ?? new ServiceKillerService();
        _openProcess = openProcess ?? (id => new NativeProcess(id));
        _gracefulTimeout = ValidTimeout(gracefulTimeout ?? TimeSpan.FromSeconds(3));
        _forceTimeout = ValidTimeout(forceTimeout ?? TimeSpan.FromSeconds(2));
        _serviceTimeout = ValidTimeout(serviceTimeout ?? TimeSpan.FromSeconds(10));
    }

    // Call from the UI context. Only confirmation/progress runs there; refresh and eligibility run on workers.
    public async Task<DeepReleaseResult> ExecuteAsync(
        IReadOnlyList<ExtremeCloseCandidate> apps,
        IReadOnlyList<OptionalServiceCandidate> services,
        Func<ExtremeCloseCandidate, Task<bool>> confirmForceClose,
        IProgress<DeepReleaseProgress>? progress,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<ProcessSnapshot>> refreshSnapshots,
        Func<ProcessSnapshot, IReadOnlyList<ProcessSnapshot>, bool> isEligible)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(confirmForceClose);
        ArgumentNullException.ThrowIfNull(refreshSnapshots);
        ArgumentNullException.ThrowIfNull(isEligible);

        var selectedServices = services.DistinctBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase).ToArray();
        // A service timeout must never fall through to killing its host as an application.
        var seen = selectedServices.Where(service => service.ProcessId > 0).Select(service => service.ProcessId).ToHashSet();
        var applications = apps.Select(app => app with
        {
            ProcessIds = app.ProcessIds.Where(seen.Add).ToArray(),
            CapturedSnapshots = app.CapturedSnapshots?.ToArray()
        }).Where(app => app.ProcessIds.Count > 0).ToArray();
        var total = applications.Sum(app => app.ProcessIds.Count) + selectedServices.Length;
        var results = new List<DeepReleaseItemResult>();
        void Report(DeepReleaseItemResult result)
        {
            results.Add(result);
            progress?.Report(new(result.Name, results.Count, total, result));
        }

        // Stop services first so application shutdown cannot destroy the captured association prematurely.
        foreach (var service in selectedServices)
        {
            progress?.Report(new(service.DisplayName, results.Count, total));
            var completion = await _services.StopSingleServiceAsync(service, selected =>
            {
                if (selected.CapturedSnapshot is not { } captured) return false;
                var snapshots = refreshSnapshots();
                var current = snapshots.Where(snapshot => snapshot.ProcessId == captured.ProcessId).ToArray();
                return current.Length == 1 && ProcessIdentity.Matches(captured, current[0]) &&
                    isEligible(current[0], snapshots);
            }, cancellationToken, _serviceTimeout);
            var outcome = completion.Outcome switch
            {
                ServiceStopOutcome.Stopped => DeepReleaseOutcome.Stopped,
                ServiceStopOutcome.Skipped => DeepReleaseOutcome.Skipped,
                ServiceStopOutcome.TimedOut => DeepReleaseOutcome.TimedOut,
                ServiceStopOutcome.Cancelled => DeepReleaseOutcome.Cancelled,
                _ => DeepReleaseOutcome.Failed
            };
            Report(new(service.DisplayName, service.ProcessId, true, outcome, completion.Message));
        }

        foreach (var app in applications)
        {
            progress?.Report(new(app.ProcessName, results.Count, total));
            var targets = app.ProcessIds.Select(id => new Target(app.ProcessName, id,
                app.CapturedSnapshots?.Where(snapshot => snapshot.ProcessId == id).ToArray())).ToArray();
            try
            {
                await Task.Run(async () =>
                {
                    foreach (var target in targets)
                    {
                        if (cancellationToken.IsCancellationRequested) { target.Cancel(); continue; }
                        try
                        {
                            if (!Revalidate(target, refreshSnapshots, isEligible)) continue;
                            cancellationToken.ThrowIfCancellationRequested();
                            try { target.Process = _openProcess(target.Id); }
                            catch (Exception ex)
                            {
                                target.Finish(DeepReleaseOutcome.Skipped, $"Cannot open captured process identity: {ex.Message}");
                                continue;
                            }
                            if (!Revalidate(target, refreshSnapshots, isEligible)) continue;
                            cancellationToken.ThrowIfCancellationRequested();
                            _ = target.Process.CloseMainWindow();
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { target.Cancel(); }
                        catch (Exception ex) { target.Finish(DeepReleaseOutcome.Failed, ex.Message); }
                    }
                    await ObserveExitsAsync(targets, _gracefulTimeout, cancellationToken).ConfigureAwait(false);
                    foreach (var target in targets.Where(target => target.Result is null))
                    {
                        try { _ = Revalidate(target, refreshSnapshots, isEligible); }
                        catch (Exception ex) { target.Finish(DeepReleaseOutcome.Failed, ex.Message); }
                    }
                });

                var remaining = targets.Where(target => target.Result is null).ToArray();
                if (remaining.Length > 0 && !cancellationToken.IsCancellationRequested)
                {
                    // No ConfigureAwait(false): the explicit force prompt belongs to the caller's UI context.
                    var confirmed = await confirmForceClose(app).WaitAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!confirmed)
                    {
                        foreach (var target in remaining) target.Finish(DeepReleaseOutcome.Declined, "Force close declined.");
                    }
                    else
                    {
                        await Task.Run(async () =>
                        {
                            foreach (var target in remaining)
                            {
                                if (cancellationToken.IsCancellationRequested) { target.Cancel(); continue; }
                                try
                                {
                                    if (!Revalidate(target, refreshSnapshots, isEligible)) continue;
                                    cancellationToken.ThrowIfCancellationRequested();
                                    target.Process!.Kill();
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { target.Cancel(); }
                                catch (Exception ex) { target.Finish(DeepReleaseOutcome.Failed, ex.Message); }
                            }
                            await ObserveExitsAsync(remaining, _forceTimeout, cancellationToken).ConfigureAwait(false);
                            foreach (var target in remaining.Where(target => target.Result is null))
                                target.Finish(DeepReleaseOutcome.TimedOut, "Exit not observed before timeout.");
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                foreach (var target in targets.Where(target => target.Result is null)) target.Cancel();
            }
            catch (Exception ex)
            {
                foreach (var target in targets.Where(target => target.Result is null))
                    target.Finish(DeepReleaseOutcome.Failed, ex.Message);
            }
            finally
            {
                foreach (var target in targets)
                {
                    target.Process?.Dispose();
                    if (target.Result is null) target.Cancel();
                    Report(target.Result!);
                }
            }
        }

        return new(results.ToArray(), cancellationToken.IsCancellationRequested);
    }

    private static bool Revalidate(Target target, Func<IReadOnlyList<ProcessSnapshot>> refreshSnapshots,
        Func<ProcessSnapshot, IReadOnlyList<ProcessSnapshot>, bool> isEligible)
    {
        if (target.Captured is not { } captured || !ProcessIdentity.IsKnown(captured) ||
            captured.ProcessId == Environment.ProcessId || SystemProcessWhitelist.Contains(captured.ProcessName) ||
            GamingProcessProtectionCatalog.Contains(captured.ProcessName))
        {
            target.Finish(DeepReleaseOutcome.Skipped, "Missing captured identity or protected process.");
            return false;
        }

        if (target.Process?.HasExited == true)
        {
            target.Finish(target.IdentityVerified ? DeepReleaseOutcome.Closed : DeepReleaseOutcome.Skipped,
                target.IdentityVerified ? "Exit observed on the retained process handle." : "Process exited before identity verification.");
            return false;
        }

        var snapshots = refreshSnapshots();
        var current = snapshots.Where(snapshot => snapshot.ProcessId == target.Id).ToArray();
        if (current.Length != 1 || !ProcessIdentity.Matches(captured, current[0]) ||
            !isEligible(current[0], snapshots))
        {
            target.Finish(DeepReleaseOutcome.Skipped, "Process identity or protection eligibility changed.");
            return false;
        }
        if (target.Process is not null)
        {
            try
            {
                if (!ProcessIdentity.Matches(captured, target.Process.ReadSnapshot()))
                {
                    target.Finish(DeepReleaseOutcome.Skipped, "Native process identity changed.");
                    return false;
                }
                target.IdentityVerified = true;
            }
            catch (Exception ex)
            {
                target.Finish(DeepReleaseOutcome.Skipped, $"Native process identity is unavailable: {ex.Message}");
                return false;
            }
        }
        return true;
    }

    private static async Task ObserveExitsAsync(Target[] targets, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            foreach (var target in targets.Where(target => target.Result is null))
            {
                try
                {
                    if (target.IdentityVerified && target.Process?.HasExited == true)
                        target.Finish(DeepReleaseOutcome.Closed, "Exit observed on the retained process handle.");
                    else if (cancellationToken.IsCancellationRequested) target.Cancel();
                }
                catch (Exception ex) { target.Finish(DeepReleaseOutcome.Failed, ex.Message); }
            }
            if (targets.All(target => target.Result is not null)) return;
            var remaining = timeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return;
            try
            {
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }

    private static TimeSpan ValidTimeout(TimeSpan timeout) => timeout >= TimeSpan.Zero
        ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));

    private sealed class Target(string name, int id, ProcessSnapshot[]? snapshots)
    {
        public int Id { get; } = id;
        public ProcessSnapshot? Captured { get; } = snapshots is { Length: 1 } ? snapshots[0] : null;
        public IDeepReleaseProcess? Process { get; set; }
        public bool IdentityVerified { get; set; }
        public DeepReleaseItemResult? Result { get; private set; }
        public void Finish(DeepReleaseOutcome outcome, string message) => Result ??= new(name, Id, false, outcome, message);
        public void Cancel() => Finish(DeepReleaseOutcome.Cancelled, "Cancelled; previous actions are not undone.");
    }

    private sealed class NativeProcess : IDeepReleaseProcess
    {
        private readonly Process _process;

        public NativeProcess(int processId)
        {
            _process = Process.GetProcessById(processId);
            try { _ = _process.SafeHandle; }
            catch { _process.Dispose(); throw; }
        }

        public ProcessSnapshot ReadSnapshot() => ProcessIdentity.Capture(_process);
        public bool HasExited => _process.HasExited;

        public bool CloseMainWindow()
        {
            _process.Refresh();
            var window = _process.MainWindowHandle;
            if (window == IntPtr.Zero) return false;
            _ = NativeMethods.GetWindowThreadProcessId(window, out var owner);
            if (owner != (uint)_process.Id || _process.HasExited) return false;
            return _process.CloseMainWindow();
        }

        public void Kill() => _process.Kill(entireProcessTree: false);
        public void Dispose() => _process.Dispose();
    }
}
