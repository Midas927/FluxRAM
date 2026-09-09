using FluxRAM.App.Configuration;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using System.Windows.Threading;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class DeepReleaseExecutorTests
{
    private static ProcessSnapshot Snapshot(int id = 710001) => new(
        id, "editor", 100 * 1024 * 1024, false,
        ExecutablePath: @"C:\Apps\Editor\editor.exe",
        StartTimeUtc: DateTimeOffset.UnixEpoch.AddDays(1));

    private static ExtremeCloseCandidate Candidate(params ProcessSnapshot[] snapshots) => new(
        "editor", snapshots.Select(s => s.ProcessId).ToArray(), 100 * 1024 * 1024,
        0, 0, false, true, false, CapturedSnapshots: snapshots);

    [Fact]
    public async Task GracefulExit_DoesNotAskForForce_AndCountsObservedExit()
    {
        var process = new FakeProcess(Snapshot()) { ExitOnClose = true };
        var result = await Run(process, _ => throw new Exception("Unexpected force prompt"));
        Assert.Equal(1, result.ClosedProcessCount);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(0, process.KillCalls);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task DeclinedForce_DoesNotKill()
    {
        var process = new FakeProcess(Snapshot());
        var result = await Run(process, _ => Task.FromResult(false));
        Assert.Equal(DeepReleaseOutcome.Declined, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, process.KillCalls);
    }

    [Fact]
    public async Task ConfirmedForce_OnlyCountsAfterObservedExit()
    {
        var process = new FakeProcess(Snapshot());
        var confirmations = 0;
        var result = await Run(process, _ => { confirmations++; return Task.FromResult(true); });
        Assert.Equal(1, confirmations);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(1, result.ClosedProcessCount);
    }

    [Fact]
    public async Task ReusedPidAtOpen_IsSkippedBeforeAnyCloseAction()
    {
        var captured = Snapshot();
        var process = new FakeProcess(captured with { StartTimeUtc = captured.StartTimeUtc!.Value.AddSeconds(1) });
        var result = await Executor(process).ExecuteAsync([Candidate(captured)], [], _ => throw new Exception("Must not prompt"),
            null, default, () => [captured], (_, _) => true);
        Assert.Equal(DeepReleaseOutcome.Skipped, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, process.CloseCalls);
        Assert.Equal(0, process.KillCalls);
    }

    [Fact]
    public async Task CancellationBeforeStart_DoesNotOpenNativeTargets()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executor = new DeepReleaseExecutor(openProcess: _ => throw new Exception("Must not open"));
        var result = await executor.ExecuteAsync([Candidate(Snapshot())], [], _ => throw new Exception("Must not prompt"),
            null, cancellation.Token, () => throw new Exception("Must not refresh"), (_, _) => true);
        Assert.True(result.WasCancelled);
        Assert.Equal(DeepReleaseOutcome.Cancelled, Assert.Single(result.Items).Outcome);
    }

    [Fact]
    public async Task ServiceTimeout_DoesNotCountCompletedOrFallThroughToKillingItsHost()
    {
        var snapshot = Snapshot();
        var candidate = new OptionalServiceCandidate("EditorService", "Editor service", snapshot.ProcessId,
            OptionalServiceKind.Application, OptionalServiceStopGuidance.WithApplication, snapshot);
        var handle = new PendingService(snapshot.ProcessId);
        var service = new ServiceKillerService((_, _) => [candidate], _ => handle);
        var executor = new DeepReleaseExecutor(service, _ => throw new Exception("Must not open service host as an app"),
            serviceTimeout: TimeSpan.Zero);
        var result = await executor.ExecuteAsync([Candidate(snapshot)], [candidate], _ => throw new Exception("Must not prompt"),
            null, default, () => [snapshot], (_, _) => true);
        Assert.Equal(1, handle.StopCalls);
        Assert.Equal(DeepReleaseOutcome.TimedOut, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, result.TotalProcessCount);
        Assert.Equal(0, result.StoppedServiceCount);
    }

    [Theory]
    [InlineData("reused")]
    [InlineData("path")]
    [InlineData("protected")]
    [InlineData("native")]
    public async Task ForcePrompt_RevalidatesIdentityAndProtectionAfterAwait(string change)
    {
        var captured = Snapshot();
        var current = captured;
        var eligible = true;
        var process = new FakeProcess(captured);
        var result = await Executor(process).ExecuteAsync([Candidate(captured)], [], _ =>
        {
            if (change == "reused") current = current with { StartTimeUtc = current.StartTimeUtc!.Value.AddSeconds(1) };
            if (change == "path") current = current with { ExecutablePath = @"C:\Other\editor.exe" };
            if (change == "protected") eligible = false;
            if (change == "native") process.Identity = current with { StartTimeUtc = current.StartTimeUtc!.Value.AddSeconds(1) };
            return Task.FromResult(true);
        }, null, default, () => [current], (_, _) => eligible);
        Assert.Equal(DeepReleaseOutcome.Skipped, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, process.KillCalls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("timestamp")]
    [InlineData("path")]
    [InlineData("system")]
    [InlineData("gaming")]
    [InlineData("self")]
    public async Task UnknownOrProtectedIdentity_IsNeverOpened(string kind)
    {
        var snapshot = Snapshot();
        snapshot = kind switch
        {
            "timestamp" => snapshot with { StartTimeUtc = null },
            "path" => snapshot with { ExecutablePath = null },
            "system" => snapshot with { ProcessName = "svchost" },
            "gaming" => snapshot with { ProcessName = "steam" },
            "self" => snapshot with { ProcessId = Environment.ProcessId },
            _ => snapshot
        };
        var candidate = Candidate(snapshot);
        if (kind == "missing") candidate = candidate with { CapturedSnapshots = null };
        var openCalls = 0;
        var executor = new DeepReleaseExecutor(openProcess: _ =>
        {
            openCalls++;
            throw new Exception("Must not open any process");
        });
        var result = await executor.ExecuteAsync([candidate], [], _ => throw new Exception("Must not prompt"),
            null, default, () => [snapshot], (_, _) => true);
        Assert.Equal(DeepReleaseOutcome.Skipped, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, openCalls);
    }

    [Fact]
    public async Task CancellationDuringPrompt_StopsRemainingAndPreservesCompletedItems()
    {
        using var cancellation = new CancellationTokenSource();
        var first = new FakeProcess(Snapshot()) { ExitOnClose = true };
        var second = new FakeProcess(Snapshot(710002));
        var third = new FakeProcess(Snapshot(710003));
        var processes = new[] { first, second, third };
        var executor = new DeepReleaseExecutor(openProcess: id => processes.Single(p => p.Identity.ProcessId == id),
            gracefulTimeout: TimeSpan.Zero, forceTimeout: TimeSpan.Zero);
        var result = await executor.ExecuteAsync(processes.Select(p => Candidate(p.Identity)).ToArray(), [], _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        }, null, cancellation.Token, () => processes.Select(p => p.Identity).ToArray(), (_, _) => true);
        Assert.True(result.WasCancelled);
        Assert.Equal(1, result.ClosedProcessCount);
        Assert.Equal(2, result.Items.Count(i => i.Outcome == DeepReleaseOutcome.Cancelled));
        Assert.Equal(0, second.KillCalls);
        Assert.Equal(0, third.CloseCalls);
        Assert.True(first.Disposed && second.Disposed);
    }

    [Fact]
    public async Task PendingForcePrompt_CanBeCancelledWithoutWaitingForItsAnswer()
    {
        using var cancellation = new CancellationTokenSource();
        var prompt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new FakeProcess(Snapshot());
        var result = await Executor(process).ExecuteAsync([Candidate(process.Identity)], [], _ =>
        {
            cancellation.Cancel();
            return prompt.Task;
        }, null, cancellation.Token, () => [process.Identity], (_, _) => true);
        prompt.SetResult(true);
        Assert.True(result.WasCancelled);
        Assert.Equal(0, process.KillCalls);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task ForceRequestWithoutObservedExit_IsTimeoutNotSuccess()
    {
        var process = new FakeProcess(Snapshot()) { ExitOnKill = false };
        var result = await Run(process, _ => Task.FromResult(true));
        Assert.Equal(DeepReleaseOutcome.TimedOut, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, result.ClosedProcessCount);
        Assert.Equal(1, process.KillCalls);
    }

    [Fact]
    public async Task SharedPid_IsOnlyActedOnOnce_AndProgressReportsActualCompletion()
    {
        var process = new FakeProcess(Snapshot()) { ExitOnClose = true };
        var updates = new List<DeepReleaseProgress>();
        var result = await Executor(process).ExecuteAsync([Candidate(process.Identity), Candidate(process.Identity)], [],
            _ => Task.FromResult(false), new InlineProgress(updates.Add), default, () => [process.Identity], (_, _) => true);
        Assert.Equal(1, result.TotalProcessCount);
        Assert.Equal(1, process.CloseCalls);
        Assert.Equal(1, updates.Last().ProcessedCount);
        Assert.Equal(DeepReleaseOutcome.Closed, updates.Last().Result!.Outcome);
    }

    [Fact]
    public async Task NativeWork_DoesNotBlockCallingThread()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var process = new FakeProcess(Snapshot()) { ExitOnClose = true };
        var caller = Environment.CurrentManagedThreadId;
        var openerThread = caller;
        var executor = new DeepReleaseExecutor(openProcess: _ =>
        {
            openerThread = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
            return process;
        });
        var operation = executor.ExecuteAsync([Candidate(process.Identity)], [], _ => Task.FromResult(false),
            null, default, () => [process.Identity], (_, _) => true);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(operation.IsCompleted);
            Assert.NotEqual(caller, openerThread);
        }
        finally { release.Set(); }
        Assert.Equal(1, (await operation).ClosedProcessCount);
    }

    [Fact]
    public async Task ForceConfirmation_ReturnsToCallerDispatcher()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var caller = Environment.CurrentManagedThreadId;
                    var process = new FakeProcess(Snapshot());
                    var result = await Run(process, _ =>
                    {
                        Assert.Equal(caller, Environment.CurrentManagedThreadId);
                        Assert.True(dispatcher.CheckAccess());
                        return Task.FromResult(false);
                    });
                    Assert.Equal(DeepReleaseOutcome.Declined, Assert.Single(result.Items).Outcome);
                    completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static DeepReleaseExecutor Executor(FakeProcess process) => new(
        openProcess: _ => process, gracefulTimeout: TimeSpan.Zero, forceTimeout: TimeSpan.Zero);

    private static Task<DeepReleaseResult> Run(FakeProcess process, Func<ExtremeCloseCandidate, Task<bool>> confirm) =>
        Executor(process).ExecuteAsync([Candidate(process.Identity)], [], confirm, null, default,
            () => [process.Identity], (_, _) => true);

    private sealed class InlineProgress(Action<DeepReleaseProgress> report) : IProgress<DeepReleaseProgress>
    {
        public void Report(DeepReleaseProgress value) => report(value);
    }

    private sealed class PendingService(int processId) : IServiceStopHandle
    {
        public int StopCalls { get; private set; }
        public ServiceStopState QueryStatus() => new(StopCalls == 0
            ? NativeMethods.ServiceCurrentState.SERVICE_RUNNING : NativeMethods.ServiceCurrentState.SERVICE_STOP_PENDING,
            processId, true);
        public bool MatchesProcess(ProcessSnapshot snapshot) => true;
        public void RequestStop() => StopCalls++;
        public void Dispose() { }
    }

    private sealed class FakeProcess(ProcessSnapshot identity) : IDeepReleaseProcess
    {
        public ProcessSnapshot Identity { get; set; } = identity;
        public bool HasExited { get; private set; }
        public bool ExitOnClose { get; init; }
        public bool ExitOnKill { get; init; } = true;
        public int CloseCalls { get; private set; }
        public int KillCalls { get; private set; }
        public bool Disposed { get; private set; }
        public ProcessSnapshot ReadSnapshot() => Identity;
        public bool CloseMainWindow() { CloseCalls++; HasExited = ExitOnClose; return true; }
        public void Kill() { KillCalls++; HasExited = ExitOnKill; }
        public void Dispose() => Disposed = true;
    }
}
