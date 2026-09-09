using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Diagnostics;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

using Microsoft.Win32;

namespace FluxRAM.Core.Services;

public sealed class ServiceKillerService
{
    private readonly Func<IReadOnlyCollection<int>?, IReadOnlyCollection<string>?, IReadOnlyList<OptionalServiceCandidate>>? _getTargets;
    private readonly Func<string, IServiceStopHandle> _openService;

    public ServiceKillerService(
        Func<IReadOnlyCollection<int>?, IReadOnlyCollection<string>?, IReadOnlyList<OptionalServiceCandidate>>? getRunningTargets = null,
        Func<string, IServiceStopHandle>? openService = null)
    {
        _getTargets = getRunningTargets;
        _openService = openService ?? (name => new NativeServiceStopHandle(name));
    }

    public IReadOnlyList<OptionalServiceCandidate> GetRunningTargets(
        IReadOnlyCollection<int>? relatedProcessIds = null,
        IReadOnlyCollection<string>? relatedApplicationNames = null)
        => GetTargets(relatedProcessIds, relatedApplicationNames, includeStopping: false);

    private IReadOnlyList<OptionalServiceCandidate> GetTargets(
        IReadOnlyCollection<int>? relatedProcessIds,
        IReadOnlyCollection<string>? relatedApplicationNames,
        bool includeStopping)
    {
        if (_getTargets is not null) return _getTargets(relatedProcessIds, relatedApplicationNames);
        var installedServiceNames = GetInstalledServiceNames();
        if (installedServiceNames.Count == 0)
        {
            return Array.Empty<OptionalServiceCandidate>();
        }

        var knownCandidatesByName = ServiceTargets.ResolveCandidates(installedServiceNames)
            .ToDictionary(candidate => candidate.ServiceName, StringComparer.OrdinalIgnoreCase);
        var relatedIds = relatedProcessIds is null
            ? new HashSet<int>()
            : relatedProcessIds.ToHashSet();
        var relatedNames = relatedApplicationNames ?? Array.Empty<string>();

        var managerHandle = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (managerHandle == IntPtr.Zero)
        {
            return Array.Empty<OptionalServiceCandidate>();
        }

        try
        {
            return installedServiceNames
                .Select(serviceName => CreateRunningCandidate(
                    managerHandle,
                    serviceName,
                    knownCandidatesByName,
                    relatedIds,
                    relatedNames,
                    includeStopping))
                .Where(candidate => candidate is not null)
                .Cast<OptionalServiceCandidate>()
                .OrderBy(candidate => candidate.Kind == OptionalServiceKind.Application ? 0 : 1)
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ = NativeMethods.CloseServiceHandle(managerHandle);
        }
    }

    public IReadOnlyList<ServiceStopResult> StopTargets()
    {
        return GetRunningTargets()
            .Select(candidate =>
            {
                var result = StopSingleServiceAsync(candidate, _ => true).GetAwaiter().GetResult();
                return new ServiceStopResult(candidate.ServiceName, result.Success, result.Message);
            })
            .ToArray();
    }

    public ServiceStopResult StopSingleService(string serviceName)
    {
        var candidate = GetRunningTargets().FirstOrDefault(target =>
            string.Equals(target.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
        if (candidate is null) return new(serviceName, false, "Not a current optional service target.");
        var result = StopSingleServiceAsync(candidate, _ => true).GetAwaiter().GetResult();
        return new(serviceName, result.Success, result.Message);
    }

    public Task<ServiceStopCompletion> StopSingleServiceAsync(
        OptionalServiceCandidate candidate,
        Func<OptionalServiceCandidate, bool> revalidate,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(revalidate);
        var wait = timeout ?? TimeSpan.FromSeconds(10);
        if (wait < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        return Task.Run(() => StopCoreAsync(candidate, revalidate, cancellationToken, wait));
    }

    private async Task<ServiceStopCompletion> StopCoreAsync(OptionalServiceCandidate candidate,
        Func<OptionalServiceCandidate, bool> revalidate, CancellationToken cancellationToken, TimeSpan timeout)
    {
        ServiceStopCompletion Result(ServiceStopOutcome outcome, string message) => new(candidate.ServiceName, outcome, message);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAllowedCandidate(candidate))
                return Result(ServiceStopOutcome.Skipped, "Unknown identity or service outside the optional target catalog.");

            cancellationToken.ThrowIfCancellationRequested();
            using var handle = _openService(candidate.ServiceName);
            var status = handle.QueryStatus();
            if (status.State == NativeMethods.ServiceCurrentState.SERVICE_STOPPED)
                return Result(ServiceStopOutcome.Stopped, "Already stopped (observed).");
            if (!IsCurrentTarget(candidate) || !revalidate(candidate))
                return Result(ServiceStopOutcome.Skipped, "Target catalog or protection eligibility changed.");

            // The callback can refresh a changing system, so recheck catalog and association after it.
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentTarget(candidate) || !revalidate(candidate))
                return Result(ServiceStopOutcome.Skipped, "Target catalog changed during revalidation.");
            status = handle.QueryStatus();
            if (status.State == NativeMethods.ServiceCurrentState.SERVICE_STOPPED)
                return Result(ServiceStopOutcome.Stopped, "Stopped (observed).");
            if (status.ProcessId != candidate.ProcessId || !handle.MatchesProcess(candidate.CapturedSnapshot!.Value))
                return Result(ServiceStopOutcome.Skipped, "Service process identity or association changed.");

            status = handle.QueryStatus();
            if (status.State == NativeMethods.ServiceCurrentState.SERVICE_STOPPED)
                return Result(ServiceStopOutcome.Stopped, "Stopped (observed).");
            if (status.ProcessId != candidate.ProcessId)
                return Result(ServiceStopOutcome.Skipped, "Service association changed while checking process identity.");

            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            if (status.State != NativeMethods.ServiceCurrentState.SERVICE_STOP_PENDING)
            {
                if (status.State != NativeMethods.ServiceCurrentState.SERVICE_RUNNING || !status.CanStop)
                    return Result(ServiceStopOutcome.Skipped, "Service is not currently stoppable.");
                handle.RequestStop();
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                status = handle.QueryStatus();
                if (status.State == NativeMethods.ServiceCurrentState.SERVICE_STOPPED)
                    return Result(ServiceStopOutcome.Stopped, "Stopped (observed).");
                if (status.ProcessId != candidate.ProcessId)
                    return Result(ServiceStopOutcome.Skipped, "Service restarted or changed its process association.");
                var remaining = timeout - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                    return Result(ServiceStopOutcome.TimedOut, "Stop not observed before timeout; the request may still complete later.");
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(100) ? remaining : TimeSpan.FromMilliseconds(100),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result(ServiceStopOutcome.Cancelled, "Cancelled; an already submitted stop request is not undone.");
        }
        catch (Exception ex) { return Result(ServiceStopOutcome.Failed, ex.Message); }
    }

    private static bool IsAllowedCandidate(OptionalServiceCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ServiceName) || candidate.ProcessId == Environment.ProcessId ||
            candidate.CapturedSnapshot is not { } snapshot || !ProcessIdentity.IsKnown(snapshot) ||
            snapshot.ProcessId != candidate.ProcessId)
            return false;
        var known = ServiceTargets.ResolveCandidates([candidate.ServiceName]).SingleOrDefault();
        if (known is not null)
            return known.Kind == candidate.Kind && known.StopGuidance == candidate.StopGuidance;
        return candidate.Kind == OptionalServiceKind.Application &&
            candidate.StopGuidance == OptionalServiceStopGuidance.WithApplication &&
            !SystemProcessWhitelist.Contains(snapshot.ProcessName) &&
            !GamingProcessProtectionCatalog.Contains(snapshot.ProcessName);
    }

    private bool IsCurrentTarget(OptionalServiceCandidate candidate) =>
        GetTargets([candidate.ProcessId], [candidate.CapturedSnapshot!.Value.ProcessName], includeStopping: true).Any(current =>
            string.Equals(current.ServiceName, candidate.ServiceName, StringComparison.OrdinalIgnoreCase) &&
            current.ProcessId == candidate.ProcessId && current.Kind == candidate.Kind &&
            current.StopGuidance == candidate.StopGuidance && current.CapturedSnapshot is { } snapshot &&
            ProcessIdentity.Matches(candidate.CapturedSnapshot.Value, snapshot));

    private static ServiceStateResult TryGetServiceStatus(IntPtr serviceHandle)
    {
        var bufferSize = (uint)Marshal.SizeOf<NativeMethods.SERVICE_STATUS_PROCESS>();
        var success = NativeMethods.QueryServiceStatusEx(
            serviceHandle,
            NativeMethods.SC_STATUS_PROCESS_INFO,
            out var status,
            bufferSize,
            out _);

        if (!success)
        {
            return new ServiceStateResult(
                false,
                default,
                0,
                0,
                $"QueryServiceStatusEx failed with Win32Error={Marshal.GetLastWin32Error()}");
        }

        return new ServiceStateResult(
            true,
            status.dwCurrentState,
            status.dwControlsAccepted,
            status.dwProcessId,
            string.Empty);
    }

    private static OptionalServiceCandidate? CreateRunningCandidate(
        IntPtr managerHandle,
        string serviceName,
        IReadOnlyDictionary<string, OptionalServiceCandidate> knownCandidatesByName,
        IReadOnlySet<int> relatedProcessIds,
        IReadOnlyCollection<string> relatedApplicationNames,
        bool includeStopping)
    {
        var isKnownTarget = knownCandidatesByName.TryGetValue(serviceName, out var knownCandidate);
        var isRelatedByName = ServiceTargets.IsRelatedApplicationService(
            serviceName,
            relatedApplicationNames);
        if (!isKnownTarget && !isRelatedByName && relatedProcessIds.Count == 0)
        {
            return null;
        }

        var serviceHandle = NativeMethods.OpenService(
            managerHandle,
            serviceName,
            NativeMethods.SERVICE_QUERY_STATUS);
        if (serviceHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var status = TryGetServiceStatus(serviceHandle);
            var running = status.State == NativeMethods.ServiceCurrentState.SERVICE_RUNNING &&
                (status.ControlsAccepted & NativeMethods.SERVICE_ACCEPT_STOP) != 0;
            var stopping = includeStopping && status.State == NativeMethods.ServiceCurrentState.SERVICE_STOP_PENDING;
            if (!status.Success || (!running && !stopping))
            {
                return null;
            }

            if (!isKnownTarget && !isRelatedByName &&
                (status.ProcessId == 0 || !relatedProcessIds.Contains((int)status.ProcessId)))
            {
                return null;
            }

            var captured = TryCaptureProcess((int)status.ProcessId);
            var latest = TryGetServiceStatus(serviceHandle);
            if (!latest.Success || latest.ProcessId != status.ProcessId || latest.State != status.State)
                return null;

            if (!isKnownTarget && (captured is not { } identity ||
                SystemProcessWhitelist.Contains(identity.ProcessName) || GamingProcessProtectionCatalog.Contains(identity.ProcessName)))
                return null;

            return isKnownTarget
                ? knownCandidate! with { ProcessId = (int)status.ProcessId, CapturedSnapshot = captured }
                : new OptionalServiceCandidate(
                    serviceName,
                    GetServiceDisplayName(serviceName),
                    (int)status.ProcessId,
                    OptionalServiceKind.Application,
                    OptionalServiceStopGuidance.WithApplication,
                    captured);
        }
        finally
        {
            _ = NativeMethods.CloseServiceHandle(serviceHandle);
        }
    }

    private static ProcessSnapshot? TryCaptureProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var snapshot = ProcessIdentity.Capture(process);
            return process.HasExited ? null : snapshot;
        }
        catch { return null; }
    }

    private sealed class NativeServiceStopHandle : IServiceStopHandle
    {
        private readonly IntPtr _handle;
        private Process? _process;

        public NativeServiceStopHandle(string serviceName)
        {
            var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
            if (manager == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                _handle = NativeMethods.OpenService(manager, serviceName,
                    NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_STOP);
                if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { _ = NativeMethods.CloseServiceHandle(manager); }
        }

        public ServiceStopState QueryStatus()
        {
            var status = TryGetServiceStatus(_handle);
            if (!status.Success) throw new InvalidOperationException(status.Message);
            return new(status.State, (int)status.ProcessId, (status.ControlsAccepted & NativeMethods.SERVICE_ACCEPT_STOP) != 0);
        }

        public bool MatchesProcess(ProcessSnapshot captured)
        {
            try
            {
                _process ??= Process.GetProcessById(captured.ProcessId);
                var current = ProcessIdentity.Capture(_process);
                return !_process.HasExited && ProcessIdentity.Matches(captured, current);
            }
            catch { return false; }
        }

        public void RequestStop()
        {
            if (!NativeMethods.ControlService(_handle, NativeMethods.SERVICE_CONTROL_STOP, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void Dispose()
        {
            _process?.Dispose();
            _ = NativeMethods.CloseServiceHandle(_handle);
        }
    }

    private static IReadOnlyList<string> GetInstalledServiceNames()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var servicesKey = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable: false);
            return servicesKey?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string GetServiceDisplayName(string serviceName)
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var serviceKey = localMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
                writable: false);
            return serviceKey?.GetValue("DisplayName") as string ?? serviceName;
        }
        catch
        {
            return serviceName;
        }
    }

    private readonly record struct ServiceStateResult(
        bool Success,
        NativeMethods.ServiceCurrentState State,
        uint ControlsAccepted,
        uint ProcessId,
        string Message);
}

public enum ServiceStopOutcome { Stopped, Skipped, TimedOut, Cancelled, Failed }

public sealed record ServiceStopCompletion(string ServiceName, ServiceStopOutcome Outcome, string Message)
{
    public bool Success => Outcome == ServiceStopOutcome.Stopped;
}

public readonly record struct ServiceStopState(NativeMethods.ServiceCurrentState State, int ProcessId, bool CanStop);

public interface IServiceStopHandle : IDisposable
{
    ServiceStopState QueryStatus();
    bool MatchesProcess(ProcessSnapshot captured);
    void RequestStop();
}
