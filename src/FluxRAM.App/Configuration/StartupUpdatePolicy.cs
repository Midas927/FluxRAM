namespace FluxRAM.App.Configuration;

public enum UpdatePromptChoice
{
    Later = 0,
    UpdateNow,
    SkipThisVersion
}

public static class StartupUpdatePolicy
{
    public static bool ShouldPrompt(UpdateCheckResult? result, string? skippedVersion)
    {
        if (result is not { State: UpdateCheckState.UpdateAvailable } ||
            string.IsNullOrWhiteSpace(result.LatestVersion) ||
            AppUpdateChecker.CompareReleaseVersions(result.CurrentVersion, result.LatestVersion) != UpdateVersionComparison.LatestIsNewer)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(skippedVersion) ||
            AppUpdateChecker.CompareReleaseVersions(skippedVersion, result.LatestVersion) != UpdateVersionComparison.Same;
    }
}
