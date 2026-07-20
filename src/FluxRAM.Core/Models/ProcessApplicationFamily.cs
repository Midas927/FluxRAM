namespace FluxRAM.Core.Models;

public sealed record ProcessApplicationFamily(
    string Key,
    string DisplayName,
    string? ExecutableDirectory,
    IReadOnlyList<ProcessSnapshot> Processes);
