using System.Windows;
using FluxRAM.Core.Models;

namespace FluxRAM.App;

public partial class MainWindow
{
    private void ViewYieldHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_yieldHistoryDialog is not null) return;
        _yieldHistoryDialog = new ApplicationPreviewDialog(this, _uiLanguage, BuildYieldHistoryRows(),
            (owner, text) => ShowEntryDetails(text, T("Yield details", "收益明细"), owner), showEligibilityFilter: false)
        {
            Title = T("Yield history - this session", "收益观察 · 本次运行")
        };
        try { _yieldHistoryDialog.ShowDialog(); }
        finally { _yieldHistoryDialog = null; }
    }

    private IReadOnlyList<ApplicationPreviewRow> BuildYieldHistoryRows() => _applicationYieldTracker.Reports.Reverse().Select(report =>
    {
        var state = report.Status switch
        {
            ApplicationYieldStatus.Completed => T("Completed", "观察完成"),
            ApplicationYieldStatus.Unknown => T("Incomplete measurements", "测量不完整"),
            _ => T("Observing", "观察中")
        };
        string Bytes(long? value) => value.HasValue ? ViewModels.MainWindowViewModel.FormatBytes(value.Value) : "--";
        var text = $"{report.ApplicationName} | {report.StartedAt.LocalDateTime:HH:mm:ss} | {state}\n" +
            T($"Measured targets {report.MeasuredProcessCount}/{report.TargetProcessCount}", $"已测进程 {report.MeasuredProcessCount}/{report.TargetProcessCount}") +
            $" | {Bytes(report.BeforeWorkingSetBytes)} -> {Bytes(report.AfterWorkingSetBytes)}";
        foreach (var checkpoint in report.Checkpoints)
        {
            var status = checkpoint.Status == ApplicationYieldSampleStatus.Pending ? T("pending", "待观察") :
                checkpoint.Status == ApplicationYieldSampleStatus.Unknown ? T("unknown", "未知") :
                T($"working set {Bytes(checkpoint.WorkingSetBytes)}, remaining reduction {Bytes(checkpoint.RetainedBytes)}",
                    $"工作集 {Bytes(checkpoint.WorkingSetBytes)}，仍减少 {Bytes(checkpoint.RetainedBytes)}");
            text += $"\n{checkpoint.ElapsedSeconds}s | {status}";
        }
        if (report.DeferredUntil is { } until && until > DateTimeOffset.Now)
            text += "\n" + T($"Auto Boost paused for this app until {until.LocalDateTime:HH:mm:ss}", $"此应用自动 Boost 暂缓至 {until.LocalDateTime:HH:mm:ss}");
        return new ApplicationPreviewRow(text, false);
    }).ToArray();
}
