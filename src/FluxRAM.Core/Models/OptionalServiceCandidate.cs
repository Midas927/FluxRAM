namespace FluxRAM.Core.Models;

public sealed record OptionalServiceCandidate(
    string ServiceName,
    string DisplayName,
    int ProcessId = 0);
