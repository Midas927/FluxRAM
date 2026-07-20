using FluxRAM.App.ViewModels;

namespace FluxRAM.App.Configuration;

public static class DeepReleaseSummaryFormatter
{
    public static string FormatSelection(
        IReadOnlyList<ExtremeCloseCandidate> selectedCandidates,
        UiLanguage language)
    {
        if (selectedCandidates.Count == 0)
        {
            return UiLanguageLocalizer.Localize(language, "No app selected", "尚未选择应用");
        }

        var estimatedBytes = selectedCandidates.Sum(candidate => Math.Max(0L, candidate.WorkingSetBytes));
        var formattedBytes = MainWindowViewModel.FormatBytes(estimatedBytes);
        return UiLanguageLocalizer.Localize(
            language,
            $"Selected {selectedCandidates.Count} apps | Estimated memory {formattedBytes}",
            $"已选择 {selectedCandidates.Count} 个应用 | 预计释放 {formattedBytes}");
    }
}
