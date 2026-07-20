using System.Runtime.InteropServices;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

using Microsoft.Win32;

namespace FluxRAM.Core.Services;

public sealed class ServiceKillerService
{
    public IReadOnlyList<OptionalServiceCandidate> GetRunningTargets(
        IReadOnlyCollection<int>? relatedProcessIds = null,
        IReadOnlyCollection<string>? relatedApplicationNames = null)
    {
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
                    relatedNames))
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
            .Select(candidate => StopSingleService(candidate.ServiceName))
            .ToArray();
    }

    public ServiceStopResult StopSingleService(string serviceName)
    {
        var managerHandle = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_CONNECT);
        if (managerHandle == IntPtr.Zero)
        {
            return new ServiceStopResult(
                serviceName,
                false,
                $"OpenSCManager failed with Win32Error={Marshal.GetLastWin32Error()}");
        }

        try
        {
            var serviceHandle = NativeMethods.OpenService(
                managerHandle,
                serviceName,
                NativeMethods.SERVICE_QUERY_STATUS | NativeMethods.SERVICE_STOP);

            if (serviceHandle == IntPtr.Zero)
            {
                return new ServiceStopResult(
                    serviceName,
                    false,
                    $"OpenService failed with Win32Error={Marshal.GetLastWin32Error()}");
            }

            try
            {
                var statusResult = TryGetServiceStatus(serviceHandle);
                if (!statusResult.Success)
                {
                    return new ServiceStopResult(serviceName, false, statusResult.Message);
                }

                if (statusResult.State == NativeMethods.ServiceCurrentState.SERVICE_STOPPED)
                {
                    return new ServiceStopResult(serviceName, true, "Already stopped.");
                }

                var stopRequested = NativeMethods.ControlService(
                    serviceHandle,
                    NativeMethods.SERVICE_CONTROL_STOP,
                    out _);

                if (!stopRequested)
                {
                    return new ServiceStopResult(
                        serviceName,
                        false,
                        $"ControlService failed with Win32Error={Marshal.GetLastWin32Error()}");
                }

                return new ServiceStopResult(serviceName, true, "Stop requested.");
            }
            finally
            {
                _ = NativeMethods.CloseServiceHandle(serviceHandle);
            }
        }
        finally
        {
            _ = NativeMethods.CloseServiceHandle(managerHandle);
        }
    }

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
        IReadOnlyCollection<string> relatedApplicationNames)
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
            if (!status.Success ||
                status.State != NativeMethods.ServiceCurrentState.SERVICE_RUNNING ||
                (status.ControlsAccepted & NativeMethods.SERVICE_ACCEPT_STOP) == 0)
            {
                return null;
            }

            if (!isKnownTarget && !isRelatedByName &&
                (status.ProcessId == 0 || !relatedProcessIds.Contains((int)status.ProcessId)))
            {
                return null;
            }

            return isKnownTarget
                ? knownCandidate! with { ProcessId = (int)status.ProcessId }
                : new OptionalServiceCandidate(
                    serviceName,
                    GetServiceDisplayName(serviceName),
                    (int)status.ProcessId,
                    OptionalServiceKind.Application,
                    OptionalServiceStopGuidance.WithApplication);
        }
        finally
        {
            _ = NativeMethods.CloseServiceHandle(serviceHandle);
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
