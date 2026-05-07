namespace FluxRAM.Core.Models;

public readonly record struct ServiceStopResult(
    string ServiceName,
    bool Success,
    string Message);
