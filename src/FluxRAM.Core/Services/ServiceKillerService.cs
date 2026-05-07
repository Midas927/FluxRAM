using System.Runtime.InteropServices;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;

namespace FluxRAM.Core.Services;

public sealed class ServiceKillerService
{
    public IReadOnlyList<ServiceStopResult> StopTargets()
    {
        return ServiceTargets.WindowsBackgroundServices
            .Select(StopSingleService)
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
                $"QueryServiceStatusEx failed with Win32Error={Marshal.GetLastWin32Error()}");
        }

        return new ServiceStateResult(true, status.dwCurrentState, string.Empty);
    }

    private readonly record struct ServiceStateResult(
        bool Success,
        NativeMethods.ServiceCurrentState State,
        string Message);
}
