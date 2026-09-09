using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using FluxRAM.App.Configuration;
using FluxRAM.App.Diagnostics;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace FluxRAM.App;

public partial class MainWindow
{
    private DeepReleaseResult? ShowDeepReleaseProgress(DeepReleaseSelection selection)
    {
        using var cancellation = new CancellationTokenSource();
        _deepReleaseCancellation = cancellation;
        var finished = false;
        DeepReleaseResult? result = null;
        var lines = new System.Collections.ObjectModel.ObservableCollection<string>();
        var status = new TextBlock { Text = T("Preparing...", "准备中…"), TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, Margin = new Thickness(0, 12, 0, 12) };
        bar.SetResourceReference(ForegroundProperty, "AccentBrush");
        var list = new ListBox { ItemsSource = lines, ItemTemplate = (DataTemplate)FindResource("ReadableEntryTemplate") };
        var cancel = new Button { Content = T("Cancel remaining", "取消后续操作"), MinWidth = 120, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(status, Dock.Top);
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(cancel, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(bar);
        root.Children.Add(cancel);
        root.Children.Add(list);
        var dialog = new Window
        {
            Owner = this,
            Title = T("Deep Release", "深度释放"),
            Width = 700,
            Height = 470,
            MinWidth = 440,
            MinHeight = 300,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily,
            Background = ThemeBrush("SurfaceBrush"),
            Content = root
        };
        ApplyDialogResources(dialog);
        void Cancel()
        {
            cancellation.Cancel();
            cancel.IsEnabled = false;
            status.Text = T("Cancelling remaining operations...", "正在取消后续操作…");
        }
        cancel.Click += (_, _) => { if (finished) dialog.Close(); else Cancel(); };
        dialog.Closing += (_, e) => { if (!finished && !_isExitRequested) { e.Cancel = true; Cancel(); } };
        dialog.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape && !finished) { Cancel(); e.Handled = true; } };
        var names = _protectedProcessNames.ToArray();
        var paths = _protectedProcessPaths.ToArray();
        var advanced = _licenseStatus.Features.SupportsAdvancedProtection;
        var times = _lastPurgeTimesByProcessId.ToDictionary(pair => pair.Key, pair => pair.Value);
        IReadOnlyList<ProcessSnapshot> Refresh()
        {
            var snapshots = ScrapeProcesses(times).ToList();
            foreach (var id in selection.Services.Select(service => service.ProcessId).Where(id => id > 0).Distinct())
            {
                if (snapshots.Any(snapshot => snapshot.ProcessId == id)) continue;
                try { using var process = Process.GetProcessById(id); snapshots.Add(ProcessIdentity.Capture(process)); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            return snapshots;
        }
        dialog.Loaded += async (_, _) =>
        {
            try
            {
                var progress = new Progress<DeepReleaseProgress>(update =>
                {
                    if (_isExitRequested || finished) return;
                    bar.Maximum = Math.Max(1, update.TotalCount);
                    bar.Value = update.ProcessedCount;
                    status.Text = $"{update.ProcessedCount}/{update.TotalCount} | {update.Name}";
                    if (update.Result is { } item) lines.Add(FormatDeepReleaseItem(item));
                });
                result = await new DeepReleaseExecutor(_serviceKillerService).ExecuteAsync(selection.Applications, selection.Services,
                    candidate => ConfirmForceCloseAsync(dialog, candidate, cancellation),
                    progress, cancellation.Token, Refresh,
                    (snapshot, snapshots) => ProcessProtectionMatcher.Match(snapshot,
                        ProcessProtectionMatcher.CreateContext(snapshots, names, paths), advanced) == ProcessProtectionMatch.None);
                lines.Clear();
                foreach (var item in result.Items) lines.Add(FormatDeepReleaseItem(item));
                if (!result.WasCancelled) bar.Value = bar.Maximum;
                status.Text = result.WasCancelled ? T("Remaining operations cancelled.", "已取消后续操作。") : T("Completed.", "处理完成。");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warning("Deep Release failed.", ex);
                status.Text = T("Deep Release could not finish.", "深度释放未能完成。");
            }
            finally
            {
                finished = true;
                cancel.IsEnabled = true;
                cancel.Content = T("Close", "关闭");
                _deepReleaseCancellation = null;
                if (_isExitRequested) dialog.Close();
            }
        };
        dialog.ShowDialog();
        return result;
    }

    private Task<bool> ConfirmForceCloseAsync(Window owner, ExtremeCloseCandidate candidate, CancellationTokenSource cancellation)
    {
        owner.Dispatcher.VerifyAccess();
        var token = cancellation.Token;
        if (token.IsCancellationRequested || _isExitRequested || !owner.IsVisible)
            return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? choice = null;
        var closed = false;
        CancellationTokenRegistration registration = default;
        var message = new TextBlock
        {
            Text = T($"{candidate.ProcessName} is still running. Force close it? Unsaved work may be lost.",
                $"{candidate.ProcessName} 仍未退出，是否强制关闭？未保存的内容可能丢失。"),
            TextWrapping = TextWrapping.Wrap
        };
        message.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var force = new Button
        {
            Content = T("Force close", "强制关闭"),
            MinWidth = 130,
            Margin = new Thickness(8, 8, 0, 0),
            Style = TryFindResource("QuietButtonStyle") as Style
        };
        force.SetResourceReference(ForegroundProperty, "WarningBrush");
        var keep = new Button
        {
            Content = T("Keep this app", "保留此应用"),
            IsDefault = true,
            MinWidth = 130,
            Margin = new Thickness(8, 8, 0, 0),
            Style = TryFindResource("PrimaryButtonStyle") as Style
        };
        var cancel = new Button
        {
            Content = T("Cancel remaining", "取消后续操作"),
            MinWidth = 130,
            Margin = new Thickness(8, 8, 0, 0),
            Style = TryFindResource("QuietButtonStyle") as Style
        };
        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(force);
        buttons.Children.Add(keep);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(message);
        root.Children.Add(buttons);
        var prompt = new Window
        {
            Owner = owner,
            Title = T("Confirm force close", "确认强制关闭"),
            Width = 600,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = owner.FontFamily,
            FontSize = owner.FontSize,
            Background = ThemeBrush("SurfaceBrush"),
            Content = root
        };
        ApplyDialogResources(prompt);

        void CancelRemaining()
        {
            if (closed) return;
            choice = false;
            cancellation.Cancel();
            prompt.Close();
        }
        void OwnerClosing(object? sender, System.ComponentModel.CancelEventArgs args) => CancelRemaining();

        force.Click += (_, _) => { choice = true; prompt.Close(); };
        keep.Click += (_, _) => { choice = false; prompt.Close(); };
        cancel.Click += (_, _) => CancelRemaining();
        prompt.Loaded += (_, _) => keep.Focus();
        prompt.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            e.Handled = true;
            CancelRemaining();
        };
        prompt.Closing += (_, _) => { if (choice is null) cancellation.Cancel(); };
        prompt.Closed += (_, _) =>
        {
            closed = true;
            owner.Closing -= OwnerClosing;
            registration.Dispose();
            // Closing an owner can bypass the owned window's Closing event.
            if (choice is null && !token.IsCancellationRequested) cancellation.Cancel();
            completion.TrySetResult(choice == true && !token.IsCancellationRequested && !_isExitRequested);
        };
        owner.Closing += OwnerClosing;
        registration = token.Register(() =>
        {
            _ = prompt.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed) return;
                choice = false;
                prompt.Close();
            }));
        });

        try
        {
            if (token.IsCancellationRequested) prompt.Close();
            else prompt.Show();
        }
        catch
        {
            owner.Closing -= OwnerClosing;
            registration.Dispose();
            if (!closed) prompt.Close();
            throw;
        }
        return completion.Task;
    }

    private string FormatDeepReleaseItem(DeepReleaseItemResult item)
    {
        var state = item.Outcome switch
        {
            DeepReleaseOutcome.Closed => T("Closed", "已退出"),
            DeepReleaseOutcome.Stopped => T("Stopped", "已停止"),
            DeepReleaseOutcome.Declined => T("Kept running", "已保留"),
            DeepReleaseOutcome.Skipped => T("Skipped: target changed or protected", "已跳过：目标变化或受保护"),
            DeepReleaseOutcome.TimedOut => T("Timed out: still running", "等待超时：仍在运行"),
            DeepReleaseOutcome.Cancelled => T("Cancelled", "已取消"),
            _ => T("Failed", "未完成")
        };
        return $"{item.Name} | {state}";
    }
}
