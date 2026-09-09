using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class ServiceKillerServiceTests
{
    private static OptionalServiceCandidate Candidate() => new("DiagTrack", "Telemetry", 710001,
        OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused,
        new ProcessSnapshot(710001, "svchost", 0, false, ExecutablePath: @"C:\Windows\System32\svchost.exe",
            StartTimeUtc: DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task AcceptedStop_WithoutStoppedState_IsTimeout()
    {
        var handle = new FakeService();
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true, timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.TimedOut, result.Outcome);
        Assert.False(result.Success);
        Assert.Equal(1, handle.StopCalls);
        Assert.True(handle.Disposed);
    }

    [Fact]
    public async Task Completion_RequiresPollingUntilStopped()
    {
        var handle = new FakeService { PollsUntilStopped = 2 };
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true, timeout: TimeSpan.FromSeconds(2));
        Assert.True(result.Success);
        Assert.Equal(ServiceStopOutcome.Stopped, result.Outcome);
        Assert.True(handle.PollsAfterStop >= 2);
    }

    [Fact]
    public async Task PendingStop_IsObservedWithoutSendingAnotherRequest()
    {
        var handle = new FakeService { State = NativeMethods.ServiceCurrentState.SERVICE_STOP_PENDING };
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true, timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.TimedOut, result.Outcome);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task QueryFailure_IsFailedAndReleasesHandle()
    {
        var handle = new FakeService { FailQuery = true };
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true);
        Assert.Equal(ServiceStopOutcome.Failed, result.Outcome);
        Assert.False(result.Success);
        Assert.True(handle.Disposed);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task CancellationBeforeStart_DoesNotOpenService()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ServiceKillerService((_, _) => throw new Exception("Must not enumerate"),
            _ => throw new Exception("Must not open"));
        var result = await service.StopSingleServiceAsync(Candidate(), _ => true, cancellation.Token);
        Assert.Equal(ServiceStopOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task CancellationAfterRequest_IsDistinctAndDoesNotClaimCompletion()
    {
        using var cancellation = new CancellationTokenSource();
        var handle = new FakeService { OnStop = cancellation.Cancel };
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true, cancellation.Token);
        Assert.Equal(ServiceStopOutcome.Cancelled, result.Outcome);
        Assert.False(result.Success);
        Assert.True(handle.Disposed);
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("pid")]
    [InlineData("identity")]
    [InlineData("protected")]
    [InlineData("unknown")]
    public async Task ChangedOrUnknownCandidate_IsNotStopped(string reason)
    {
        var candidate = Candidate();
        var handle = new FakeService();
        var current = candidate;
        if (reason == "pid") handle.ProcessId++;
        if (reason == "identity") handle.IdentityMatches = false;
        if (reason == "unknown") candidate = candidate with { CapturedSnapshot = null };
        if (reason == "catalog") current = current with { StopGuidance = OptionalServiceStopGuidance.KeepRunning };
        var service = new ServiceKillerService((_, _) => [current], _ => handle);
        var result = await service.StopSingleServiceAsync(candidate, _ => reason != "protected", timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.Skipped, result.Outcome);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task AssociationChangingDuringRevalidation_IsNotStopped()
    {
        var handle = new FakeService();
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ =>
        {
            handle.ProcessId++;
            return true;
        }, timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.Skipped, result.Outcome);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task UnknownSystemService_CannotBeAuthorizedByCallback()
    {
        var candidate = Candidate() with { ServiceName = "CriticalSystemService" };
        var handle = new FakeService();
        var service = new ServiceKillerService((_, _) => [candidate], _ => handle);
        var result = await service.StopSingleServiceAsync(candidate, _ => true, timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.Skipped, result.Outcome);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task AssociationChangingDuringIdentityRead_IsNotStopped()
    {
        var handle = new FakeService();
        handle.OnIdentityRead = () => handle.ProcessId++;
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true, timeout: TimeSpan.Zero);
        Assert.Equal(ServiceStopOutcome.Skipped, result.Outcome);
        Assert.Equal(0, handle.StopCalls);
    }

    [Fact]
    public async Task AlreadyStopped_IsObservedSuccessWithoutSendingRequest()
    {
        var handle = new FakeService { State = NativeMethods.ServiceCurrentState.SERVICE_STOPPED };
        var result = await Service(handle).StopSingleServiceAsync(Candidate(), _ => true);
        Assert.True(result.Success);
        Assert.Equal(0, handle.StopCalls);
    }

    private static ServiceKillerService Service(FakeService handle) => new((_, _) => [Candidate()], _ => handle);

    private sealed class FakeService : IServiceStopHandle
    {
        public NativeMethods.ServiceCurrentState State { get; set; } = NativeMethods.ServiceCurrentState.SERVICE_RUNNING;
        public int ProcessId { get; set; } = 710001;
        public bool IdentityMatches { get; set; } = true;
        public bool FailQuery { get; init; }
        public int PollsUntilStopped { get; init; } = int.MaxValue;
        public int PollsAfterStop { get; private set; }
        public int StopCalls { get; private set; }
        public bool Disposed { get; private set; }
        public Action? OnStop { get; init; }
        public Action? OnIdentityRead { get; set; }
        public ServiceStopState QueryStatus()
        {
            if (FailQuery) throw new InvalidOperationException("Simulated query failure");
            if (StopCalls > 0 && ++PollsAfterStop >= PollsUntilStopped) State = NativeMethods.ServiceCurrentState.SERVICE_STOPPED;
            return new(State, ProcessId, true);
        }
        public bool MatchesProcess(ProcessSnapshot snapshot) { OnIdentityRead?.Invoke(); return IdentityMatches; }
        public void RequestStop() { StopCalls++; State = NativeMethods.ServiceCurrentState.SERVICE_STOP_PENDING; OnStop?.Invoke(); }
        public void Dispose() => Disposed = true;
    }
}
