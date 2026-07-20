namespace FluxRAM.Core.Models;

public enum BackgroundActivityState
{
    Observing = 0,
    Idle = 1,
    Working = 2,
    Visible = 3
}

public sealed record BackgroundActivityAssessment(
    string FamilyKey,
    BackgroundActivityState State,
    TimeSpan ObservedFor,
    TimeSpan IdleFor,
    int SampleCount);
