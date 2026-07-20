namespace FluxRAM.Core.Models;

public enum OptionalServiceKind
{
    System = 0,
    Application = 1
}

public enum OptionalServiceStopGuidance
{
    KeepRunning = 0,
    WhenFeatureUnused = 1,
    WithApplication = 2
}

public sealed record OptionalServiceCandidate(
    string ServiceName,
    string DisplayName,
    int ProcessId = 0,
    OptionalServiceKind Kind = OptionalServiceKind.System,
    OptionalServiceStopGuidance StopGuidance = OptionalServiceStopGuidance.KeepRunning);
