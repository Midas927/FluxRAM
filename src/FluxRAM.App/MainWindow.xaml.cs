using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FluxRAM.App.Automation;
using FluxRAM.App.Configuration;
using FluxRAM.App.Diagnostics;
using FluxRAM.App.Licensing;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Interop;
using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;
using FluxRAMLicenseManager = FluxRAM.App.Licensing.LicenseManager;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace FluxRAM.App;

public partial class MainWindow : Window
{
    private const double CompactWindowWidth = 800d;
    private const double CompactWindowHeight = 570d;
    private const double CompactMinWindowWidth = 720d;
    private const double CompactMinWindowHeight = 520d;
    private const double DetailWindowWidth = 1060d;
    private const double DetailWindowHeight = 760d;
    private const double DetailMinWindowWidth = 860d;
    private const double DetailMinWindowHeight = 600d;
    private const double DetailWheelScrollStep = 32d;
    private const string GitHubRepositoryUrl = "https://github.com/Midas927/FluxRAM";

    private readonly MainWindowViewModel _viewModel;
    private readonly ProcessScraperService _processScraperService;
    private readonly BackgroundActivityTracker _backgroundActivityTracker;
    private readonly MemoryStatusService _memoryStatusService;
    private readonly MemoryPurgeService _memoryPurgeService;
    private readonly PurgePolicyService _purgePolicyService;
    private readonly ServiceKillerService _serviceKillerService;
    private readonly FluxRAMLicenseManager _licenseManager;
    private readonly ProtectedAppsStore _protectedAppsStore;
    private readonly UserSettingsStore _userSettingsStore;
    private readonly StartupAutoBoostService _startupAutoBoostService;
    private readonly AppUpdateChecker _updateChecker;
    private readonly AppUpdatePackageService _updatePackageService;
    private readonly DispatcherTimer _optimizerTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _openTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _boostTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _exitTrayMenuItem;
    private readonly object _processScraperLock = new();
    private readonly ApplicationYieldTracker _applicationYieldTracker = new();
    private ApplicationPreviewDialog? _yieldHistoryDialog;

    private readonly Dictionary<int, DateTimeOffset> _lastPurgeTimesByProcessId = new();
    private readonly HashSet<string> _protectedProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _protectedProcessPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _protectedEntryDisplayByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _protectedPathByDisplay = new(StringComparer.Ordinal);

    private OptimizerSettings _optimizerSettings;
    private OptimizerProfile _selectedProfile;
    private UiLanguage _uiLanguage = UiLanguage.English;
    private AppTheme _uiTheme = AppTheme.Dark;
    private LicenseStatus _licenseStatus;

    private DateTimeOffset? _lastBoostAt;
    private DateTimeOffset? _lastAutoBoostAt;
    private DateTimeOffset? _reboundTrackingUntil;
    private ulong _baselineAvailableMemoryBytes;
    private ulong _lastBoostBaselineAvailableMemoryBytes;
    private long _lastBoostTrimmedBytes;
    private long _totalTrimmedBytes;
    private long _lastBoostNetGainBytes;
    private double _reboundRatePercent;
    private string _lastPolicyMessage = string.Empty;
    private bool _isAutoBoostEnabled;
    private bool _isExitRequested;
    private bool _hasShownTrayTip;
    private bool _isDetailPanelVisible;
    private bool _isSettingLanguageSelector;
    private bool _isSettingProfileSelector;
    private bool _isSettingAutoBoostToggle;
    private bool _isSettingStartupAutoBoostCheckBox;
    private bool _isMonitoringTickRunning;
    private readonly bool _isUiPreview;
    private bool _isCheckingUpdate;
    private bool _isDeepReleaseRunning;
    private System.Threading.CancellationTokenSource? _deepReleaseCancellation;
    private bool _hasHandledStartupUpdate;
    private readonly System.Threading.CancellationTokenSource _updateCancellation = new();

    public MainWindow(bool isUiPreview = false)
    {
        _isUiPreview = isUiPreview;
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        _processScraperService = new ProcessScraperService();
        _backgroundActivityTracker = new BackgroundActivityTracker();
        _memoryStatusService = new MemoryStatusService();
        _memoryPurgeService = new MemoryPurgeService();
        _purgePolicyService = new PurgePolicyService();
        _serviceKillerService = new ServiceKillerService();
        _licenseManager = new FluxRAMLicenseManager();
        _protectedAppsStore = new ProtectedAppsStore();
        _userSettingsStore = new UserSettingsStore();
        _startupAutoBoostService = new StartupAutoBoostService();
        _updateChecker = new AppUpdateChecker();
        _updatePackageService = new AppUpdatePackageService();
        _licenseStatus = _licenseManager.GetStatus();
        DiagnosticLog.Info($"FluxRAM starting. Version={AppVersionInfo.CurrentDisplayVersion}, Edition={_licenseStatus.Features.Edition}.");
        var initialLanguage = _userSettingsStore.LoadLanguage();
        var initialTheme = _userSettingsStore.LoadTheme();
        var initialAutoBoost = _userSettingsStore.LoadAutoBoost();
        var initialStartupAutoBoost = _userSettingsStore.LoadStartupAutoBoost();
        var initialProfile = NormalizeProfileForEdition(_userSettingsStore.LoadProfile(), _licenseStatus.Features);
        var launchedForAutoBoost = StartupAutoBoostService.WasLaunchedForAutoBoost(Environment.GetCommandLineArgs());
        _selectedProfile = initialProfile;
        _optimizerSettings = OptimizerSettingsCatalog.FromProfile(_selectedProfile);
        _optimizerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _optimizerTimer.Tick += OptimizerTimer_OnTick;

        _openTrayMenuItem = new Forms.ToolStripMenuItem();
        _openTrayMenuItem.Click += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _boostTrayMenuItem = new Forms.ToolStripMenuItem();
        _boostTrayMenuItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            RunBoostPass(true, T("Tray Boost", "托盘 Boost"));
            UpdateMonitoringState();
        });
        _exitTrayMenuItem = new Forms.ToolStripMenuItem();
        _exitTrayMenuItem.Click += (_, _) => Dispatcher.Invoke(ExitFromTray);
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add(_openTrayMenuItem);
        trayMenu.Items.Add(_boostTrayMenuItem);
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(_exitTrayMenuItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = ResolveTrayIcon(),
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);

        StateChanged += MainWindow_OnStateChanged;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        Closed += MainWindow_OnClosed;

        DataContext = _viewModel;
        ApplyEditionUi();
        SelectProfile(initialProfile);
        SelectLanguage(initialLanguage);
        ApplyTheme(initialTheme, false);
        ApplyLanguage(initialLanguage, false);
        _viewModel.UpdateRamDelta(0);
        _viewModel.UpdateAvailableMemory(0);
        _viewModel.UpdateBoostMetrics(0, 0, 0);
        _viewModel.UpdateReboundRate(0);
        LoadProtectedApps();
        RefreshProtectedEntries();
        RefreshMetricCards();
        SetStartupAutoBoostCheckBox(isUiPreview ? false : initialStartupAutoBoost);
        if (!isUiPreview)
        {
            EnsureStartupAutoBoostRegistration(initialStartupAutoBoost);
        }
        RefreshStartupAutoBoostStatus();
        ApplyDetailPanelState(true);
        SetAutoBoostState(
            !isUiPreview && (initialAutoBoost || initialStartupAutoBoost || launchedForAutoBoost),
            addEvent: false,
            persist: false);

        _viewModel.AddEvent(T("Engine initialized in simplified boost mode.", "引擎已按精简 Boost 模式初始化。"));
        _viewModel.SetStatus(T("Ready.", "就绪。"));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableMicaBackdrop();
        CaptureBaselineMemory();
        UpdateSelfOverhead();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_OnLoaded;
        await RunMonitoringTickAsync();
        if (!StartupAutoBoostService.WasLaunchedForAutoBoost(Environment.GetCommandLineArgs()))
            await CheckForUpdatesAsync(automatic: true);
    }

    public void StartInTray()
    {
        ShowInTaskbar = false;
        _hasShownTrayTip = true;
        Hide();
        CaptureBaselineMemory();
        UpdateSelfOverhead();
        DiagnosticLog.Info("FluxRAM started silently in system tray for startup Auto Boost.");
    }

    private void BoostNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        RunBoostPass(true, T("Boost Now", "立即 Boost"));
        UpdateMonitoringState();
    }

    private async void PreviewBoostCandidatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!PreviewBoostCandidatesButton.IsEnabled) return;
        PreviewBoostCandidatesButton.IsEnabled = false;
        try
        {
            var times = _lastPurgeTimesByProcessId.ToDictionary(pair => pair.Key, pair => pair.Value);
            var snapshots = await Task.Run(() => ScrapeProcesses(times));
            if (_isExitRequested) return;
            var assessments = _purgePolicyService.AssessApplications(snapshots, CreateManualBoostSettings(_optimizerSettings),
                DateTimeOffset.Now, times, _protectedProcessNames, _protectedProcessPaths, _licenseStatus.Features.SupportsAdvancedProtection);
            var rows = assessments.OrderByDescending(item => item.Group.WorkingSetBytes).Select(item =>
            {
                var group = item.Group;
                var status = item.RejectionReason switch
                {
                    PurgePolicyService.CandidateGroupRejectionReason.Foreground => T("Foreground", "前台应用"),
                    PurgePolicyService.CandidateGroupRejectionReason.TooSmall => T("Below size threshold", "占用较小"),
                    PurgePolicyService.CandidateGroupRejectionReason.UnmeasuredActivity => T("Observing", "观察不足"),
                    PurgePolicyService.CandidateGroupRejectionReason.NotCold => T("Recent activity", "近期有活动"),
                    PurgePolicyService.CandidateGroupRejectionReason.Active => T("Active CPU/I/O", "正在工作"),
                    PurgePolicyService.CandidateGroupRejectionReason.Protected => T("Protected", "已保护"),
                    PurgePolicyService.CandidateGroupRejectionReason.Cooldown => T("Cooldown", "冷却中"),
                    _ => T("Eligible", "符合条件")
                };
                var text = $"{group.ProcessName} | {status} | {MainWindowViewModel.FormatBytes(group.WorkingSetBytes)}\n" +
                    T($"Processes: {group.ObservedProcesses.Count}", $"进程：{group.ObservedProcesses.Count}") +
                    $" | {FormatCandidateGroupSignals(group)}\n{group.ExecutablePath}";
                if (_applicationYieldTracker.HasPendingObservation(group, DateTimeOffset.Now))
                    text += "\n" + T("Auto Boost: observing the last trim", "自动 Boost：正在观察上次裁剪");
                else if (_applicationYieldTracker.IsDeferred(group, DateTimeOffset.Now))
                    text += "\n" + T("Auto Boost: temporarily paused after low yield", "自动 Boost：近期收益偏低，暂缓重复裁剪");
                return new ApplicationPreviewRow(text, item.RejectionReason == PurgePolicyService.CandidateGroupRejectionReason.None);
            }).ToArray();
            var dialog = new ApplicationPreviewDialog(this, _uiLanguage, rows,
                (owner, text) => ShowEntryDetails(text, T("App details", "应用详情"), owner))
            { Title = T("Manual Boost application preview", "手动 Boost 应用预览") };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Application preview failed.", ex);
            _viewModel.SetStatus(T("Unable to load application preview.", "无法加载应用预览。"));
        }
        finally { PreviewBoostCandidatesButton.IsEnabled = true; }
    }

    private void AutoBoostToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isSettingAutoBoostToggle)
        {
            return;
        }

        SetAutoBoostState(true, addEvent: true, persist: true);
    }

    private void AutoBoostToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isSettingAutoBoostToggle)
        {
            return;
        }

        SetAutoBoostState(false, addEvent: true, persist: true);
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void DetailSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyDetailPanelState(!_isDetailPanelVisible);
    }

    private void ToolsMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ToolsMenuButton.ContextMenu is null)
        {
            return;
        }

        ToolsMenuButton.ContextMenu.PlacementTarget = ToolsMenuButton;
        ToolsMenuButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ToolsMenuButton.ContextMenu.IsOpen = true;
    }

    private void DetailListBox_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        var listScrollViewer = FindVisualChild<ScrollViewer>(listBox);
        if (CanScrollList(listScrollViewer, e.Delta))
        {
            ScrollByMouseWheel(listScrollViewer!, e.Delta);
            e.Handled = true;
            return;
        }

        if (ReferenceEquals(listBox, BoostDetailsListBox)) ScrollByMouseWheel(DetailPanel, e.Delta);
        e.Handled = true;
    }

    private void DetailPanel_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            e.OriginalSource is DependencyObject source &&
            (ReferenceEquals(source, ProtectedAppsListBox) || ProtectedAppsListBox.IsAncestorOf(source) ||
             ReferenceEquals(source, BoostDetailsListBox) || BoostDetailsListBox.IsAncestorOf(source) ||
             ReferenceEquals(source, RecentEventsListBox) || RecentEventsListBox.IsAncestorOf(source)))
        {
            return;
        }

        ScrollByMouseWheel(scrollViewer, e.Delta);
        e.Handled = true;
    }

    private void DetailListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox ||
            e.OriginalSource is not DependencyObject source ||
            System.Windows.Controls.ItemsControl.ContainerFromElement(listBox, source) is not ListBoxItem ||
            !ShowSelectedListEntryDetails(listBox))
        {
            return;
        }

        e.Handled = true;
    }

    private void DetailListBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter ||
            sender is not System.Windows.Controls.ListBox listBox ||
            !ShowSelectedListEntryDetails(listBox))
        {
            return;
        }

        e.Handled = true;
    }

    private void ViewBoostDetailsButton_OnClick(object sender, RoutedEventArgs e) => ShowSelectedListEntryDetails(BoostDetailsListBox);
    private void ViewProtectionDetailsButton_OnClick(object sender, RoutedEventArgs e) => ShowSelectedListEntryDetails(ProtectedAppsListBox);
    private void ViewActivityDetailsButton_OnClick(object sender, RoutedEventArgs e) => ShowSelectedListEntryDetails(RecentEventsListBox);

    private bool ShowSelectedListEntryDetails(System.Windows.Controls.ListBox listBox)
    {
        if ((listBox.SelectedItem ?? listBox.Items.Cast<object>().FirstOrDefault()) is not string detail || string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        var title = ReferenceEquals(listBox, ProtectedAppsListBox)
            ? T("Protected app details", "受保护应用详情")
            : ReferenceEquals(listBox, BoostDetailsListBox)
                ? T("Boost details", "Boost 明细")
                : T("Activity details", "活动详情");
        ShowEntryDetails(detail, title);
        return true;
    }

    private void ShowEntryDetails(string detail, string title, Window? owner = null)
    {
        var dialog = new Window
        {
            Owner = owner ?? this,
            Title = title,
            Width = Math.Min(680, SystemParameters.WorkArea.Width - 48),
            Height = Math.Min(360, SystemParameters.WorkArea.Height - 48),
            MinWidth = 360,
            MinHeight = 220,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = FontFamily,
            Background = ThemeBrush("WindowBackgroundBrush")
        };
        ApplyDialogResources(dialog);
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = detail,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Height = double.NaN,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12),
            FontSize = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var close = new System.Windows.Controls.Button { Content = T("Close", "关闭"), IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => dialog.Close();
        var copy = new System.Windows.Controls.Button { Content = T("Copy", "复制"), MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(detail); copy.Content = T("Copied", "已复制"); }
            catch (System.Runtime.InteropServices.COMException) { copy.Content = T("Try again", "重试复制"); }
        };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        var layout = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        layout.Children.Add(textBox);
        dialog.Content = layout;
        dialog.ShowDialog();
    }

    private static bool CanScrollList(ScrollViewer? scrollViewer, int wheelDelta)
    {
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return false;
        }

        return wheelDelta > 0
            ? scrollViewer.VerticalOffset > 0
            : scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    private static void ScrollByMouseWheel(ScrollViewer scrollViewer, int wheelDelta)
    {
        var targetOffset = scrollViewer.VerticalOffset - wheelDelta / 120d * DetailWheelScrollStep;
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(targetOffset, 0d, scrollViewer.ScrollableHeight));
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T result)
            {
                return result;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void ThemeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        var nextTheme = _uiTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(nextTheme);
        _userSettingsStore.SaveTheme(nextTheme);
    }

    private void GithubMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenGitHubRepository();
    }

    private async void DeepReleaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isDeepReleaseRunning || _isCheckingUpdate) return;
        if (!_licenseStatus.Features.SupportsExtremeClose)
        {
            _viewModel.SetStatus(T(
                "Deep Release is included in Pro. Edition comparison opened.",
                "深度释放包含在 Pro 中，已打开版本区别。"));
            ShowEditionDetailsDialog();
            return;
        }

        _isDeepReleaseRunning = true;
        DeepReleaseButton.IsEnabled = BoostNowButton.IsEnabled = false;
        try
        {
            var times = _lastPurgeTimesByProcessId.ToDictionary(pair => pair.Key, pair => pair.Value);
            var snapshots = await Task.Run(() => ScrapeProcesses(times));
            if (_isExitRequested) return;
            var protectionContext = ProcessProtectionMatcher.CreateContext(
                snapshots,
                _protectedProcessNames,
                _protectedProcessPaths);
            var protectionSummary = ProcessProtectionMatcher.Summarize(
                snapshots,
                protectionContext,
                enableAdvancedProtection: true);
            _viewModel.UpdateProProtectionSummary(protectionSummary, isPro: true);
            var candidates = ExtremeCloseCandidateFactory.FromSnapshots(
                    snapshots,
                    _licenseStatus.Features.SupportsProtectList ? _protectedProcessNames : Array.Empty<string>(),
                    _licenseStatus.Features.SupportsProtectList ? _protectedProcessPaths : Array.Empty<string>(),
                    Environment.ProcessId,
                    enableAdvancedProtection: _licenseStatus.Features.SupportsAdvancedProtection,
                    activityAssessments: _backgroundActivityTracker.CurrentAssessments)
                .ToArray();
            var serviceCandidates = await Task.Run(() => _serviceKillerService.GetRunningTargets(
                candidates.SelectMany(candidate => candidate.ProcessIds).ToHashSet(),
                candidates.Select(candidate => candidate.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase)));
            if (_isExitRequested) return;
            candidates = DeepReleaseCandidateDeduplicator
                .RemoveServiceDuplicates(candidates, serviceCandidates)
                .ToArray();

            if (candidates.Length == 0 && serviceCandidates.Count == 0)
            {
                var message = T(
                    "Deep Release found no background application suitable for closing.",
                    "深度释放没有找到适合关闭的后台应用。");
                _viewModel.SetStatus(message);
                _viewModel.UpdateBoostDetails([
                    BuildProProtectionDetail(protectionSummary),
                message
                ]);
                return;
            }

            var selection = ShowExtremeCloseDialog(candidates, serviceCandidates);
            if (selection.Applications.Count == 0 && selection.Services.Count == 0)
            {
                _viewModel.SetStatus(T("Deep Release cancelled.", "深度释放已取消。"));
                return;
            }

            var result = ShowDeepReleaseProgress(selection);
            if (result is null) return;
            _viewModel.UpdateBoostDetails([
                BuildProProtectionDetail(protectionSummary),
            .. result.Items.Select(FormatDeepReleaseItem)
            ]);
            _viewModel.SetStatus(T(
                $"Deep Release: closed {result.ClosedProcessCount}/{result.TotalProcessCount} process(es), stopped {result.StoppedServiceCount}/{result.TotalServiceCount} service(s).",
                $"深度释放：已退出 {result.ClosedProcessCount}/{result.TotalProcessCount} 个进程，已停止 {result.StoppedServiceCount}/{result.TotalServiceCount} 项服务。"));
            _viewModel.AddEvent(T(
                $"Deep Release handled {result.ClosedProcessCount} process(es) and {result.StoppedServiceCount} service(s).",
                $"深度释放已处理 {result.ClosedProcessCount} 个进程和 {result.StoppedServiceCount} 项服务。"));
            DiagnosticLog.Info(
                $"Deep Release completed. Closed={result.ClosedProcessCount}/{result.TotalProcessCount}, services={result.StoppedServiceCount}/{result.TotalServiceCount}.");
            if (_memoryStatusService.TryGetSnapshot(out var memory)) UpdateStatusMetrics(memory, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Deep Release could not start.", ex);
            _viewModel.SetStatus(T("Deep Release could not start.", "深度释放未能启动。"));
        }
        finally
        {
            _isDeepReleaseRunning = false;
            DeepReleaseButton.IsEnabled = BoostNowButton.IsEnabled = true;
        }
    }

    private void DiagnosticLogMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticLog.Info("Diagnostic log opened by user.");
            OpenPath(DiagnosticLog.LogFilePath);
            _viewModel.SetStatus(T("Diagnostic log opened.", "已打开诊断日志。"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to open diagnostic log.", ex);
            _viewModel.SetStatus(T(
                $"Unable to open diagnostic log: {ex.Message}",
                $"无法打开诊断日志：{ex.Message}"));
        }
    }

    private async void CheckUpdateMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(automatic: false);

    private async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_isUiPreview || _isCheckingUpdate || _isDeepReleaseRunning || _isExitRequested || automatic && _hasHandledStartupUpdate) return;
        _isCheckingUpdate = true;
        UpdateCheckResult? result = null;
        var installing = false;
        CheckUpdateMenuItem.IsEnabled = false;
        try
        {
            if (!automatic) _viewModel.SetStatus(T("Checking for updates...", "正在检查更新..."));
            result = await _updateChecker.CheckLatestReleaseAsync(_updateCancellation.Token);
            if (_isExitRequested || _updateCancellation.IsCancellationRequested) return;
            if (!automatic)
            {
                _viewModel.SetStatus(LocalizeUpdateCheckResult(result));
                _viewModel.AddEvent(LocalizeUpdateCheckResult(result));
            }

            if (!StartupUpdatePolicy.ShouldPrompt(result, automatic ? _userSettingsStore.LoadSkippedUpdateVersion() : null))
                return;
            if (automatic && (!IsVisible || !IsEnabled || WindowState == WindowState.Minimized)) return;
            var package = AppDistributionInfo.SelectAsset(result.Assets, AppDistributionInfo.CurrentMode);
            if (package is null)
            {
                if (!automatic)
                {
                    _viewModel.SetStatus(T("This release has no matching package yet.", "此版本暂未提供匹配的安装包，请稍后再试。"));
                    if (!string.IsNullOrWhiteSpace(result.ReleaseUrl) && System.Windows.MessageBox.Show(this,
                        T("Open the release page?", "是否打开版本下载页面？"), "FluxRAM",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                        OpenUrl(result.ReleaseUrl);
                }
                return;
            }

            _hasHandledStartupUpdate = true;
            var choice = StartupUpdateDialog.Show(this, result, _uiLanguage);
            if (choice == UpdatePromptChoice.SkipThisVersion)
            {
                _userSettingsStore.SaveSkippedUpdateVersion(result.LatestVersion);
                return;
            }
            if (choice != UpdatePromptChoice.UpdateNow) return;

            installing = true;
            var progress = new Progress<int>(percent =>
            {
                if (_isExitRequested) return;
                CheckUpdateMenuItem.Header = T($"Downloading update... {percent}%", $"正在下载更新... {percent}%");
                _viewModel.SetStatus(T(
                    $"Downloading and verifying FluxRAM {result.LatestVersion}: {percent}%",
                    $"正在下载并校验 FluxRAM {result.LatestVersion}：{percent}%"));
            });
            var stagedUpdate = await _updatePackageService.DownloadAndStageAsync(
                result, AppDistributionInfo.CurrentMode, progress, _updateCancellation.Token);
            if (_isExitRequested) return;
            _viewModel.SetStatus(T("Update verified. Restarting...", "更新校验完成，正在重启..."));
            _updatePackageService.LaunchReplacement(stagedUpdate, Environment.ProcessId);
            _isExitRequested = true;
            _trayIcon.Visible = false;
            System.Windows.Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (_updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Update could not be completed.", ex);
            if ((!automatic || installing) && !_isExitRequested)
            {
                _viewModel.SetStatus(T("Update failed. Please try again later.", "更新未完成，请稍后重试。"));
                if (installing && !string.IsNullOrWhiteSpace(result?.ReleaseUrl) &&
                    System.Windows.MessageBox.Show(this,
                        T("Open the download page to update manually?", "是否打开下载页面手动更新？"),
                        "FluxRAM", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    OpenUrl(result.ReleaseUrl);
            }
        }
        finally
        {
            _isCheckingUpdate = false;
            CheckUpdateMenuItem.IsEnabled = true;
            UpdateToolsMenuText();
        }
    }

    private void StartupAutoBoostCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_isSettingStartupAutoBoostCheckBox)
        {
            return;
        }

        ApplyStartupAutoBoostPreference(true);
    }

    private void StartupAutoBoostCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isSettingStartupAutoBoostCheckBox)
        {
            return;
        }

        ApplyStartupAutoBoostPreference(false);
    }

    private void EditionHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowEditionDetailsDialog();
    }

    private void ShowEditionDetailsDialog()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = T(EditionDetailsCatalog.DialogTitleEnglish, EditionDetailsCatalog.DialogTitleChinese),
            Width = 660d,
            Height = 500d,
            MinWidth = 620d,
            MinHeight = 460d,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = UiFontFamily(_uiLanguage),
            Background = ThemeBrush("WindowBackgroundBrush")
        };

        ApplyDialogResources(dialog);
        dialog.Content = CreateEditionDetailsContent(dialog);
        dialog.ShowDialog();
    }

    private void ProfileHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = T("Profile details", "档位说明"),
            Width = 620d,
            Height = 470d,
            MinWidth = 560d,
            MinHeight = 430d,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = UiFontFamily(_uiLanguage),
            Background = ThemeBrush("WindowBackgroundBrush")
        };

        ApplyDialogResources(dialog);
        dialog.Content = CreateProfileDetailsContent(dialog);
        dialog.ShowDialog();
    }

    private void CopyMachineIdButton_OnClick(object sender, RoutedEventArgs e)
    {
        CopyMachineIdToClipboard();
    }

    private void CopyMachineIdToClipboard()
    {
        try
        {
            System.Windows.Clipboard.SetText(_licenseStatus.MachineId);
            _viewModel.SetStatus(T("Machine ID copied.", "机器标识已复制。"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to copy Machine ID.", ex);
            _viewModel.SetStatus(T("Unable to copy Machine ID.", "无法复制机器标识。"));
        }
    }

    private void ApplyStartupAutoBoostPreference(bool isEnabled)
    {
        try
        {
            _startupAutoBoostService.SetEnabled(isEnabled);
            _userSettingsStore.SaveStartupAutoBoost(isEnabled);
            SetStartupAutoBoostCheckBox(isEnabled);
            RefreshStartupAutoBoostStatus();

            if (isEnabled && !_isAutoBoostEnabled)
            {
                SetAutoBoostState(true, addEvent: false, persist: true);
            }

            _viewModel.SetStatus(isEnabled
                ? T("Windows startup Auto Boost enabled.", "开机自启自动 Boost 已开启。")
                : T("Windows startup Auto Boost disabled.", "开机自启自动 Boost 已关闭。"));
            _viewModel.AddEvent(isEnabled
                ? T("Startup Auto Boost enabled.", "开机自启自动 Boost 已开启。")
                : T("Startup Auto Boost disabled.", "开机自启自动 Boost 已关闭。"));
            DiagnosticLog.Info(isEnabled ? "Startup Auto Boost enabled." : "Startup Auto Boost disabled.");
        }
        catch (Exception ex)
        {
            var savedValue = _userSettingsStore.LoadStartupAutoBoost();
            SetStartupAutoBoostCheckBox(savedValue);
            RefreshStartupAutoBoostStatus();
            DiagnosticLog.Error("Startup Auto Boost preference could not be changed.", ex);
            _viewModel.SetStatus(T(
                $"Startup Auto Boost could not be changed: {ex.Message}",
                $"开机自启自动 Boost 修改失败：{ex.Message}"));
        }
    }

    private void EnsureStartupAutoBoostRegistration(bool isEnabled)
    {
        if (!isEnabled)
        {
            return;
        }

        try
        {
            _startupAutoBoostService.SetEnabled(true);
            DiagnosticLog.Info("Startup Auto Boost registration verified.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Startup Auto Boost registration failed.", ex);
            _viewModel.SetStatus(T(
                $"Startup Auto Boost registration failed: {ex.Message}",
                $"开机自启自动 Boost 注册失败：{ex.Message}"));
        }
    }

    private void SetStartupAutoBoostCheckBox(bool isEnabled)
    {
        try
        {
            _isSettingStartupAutoBoostCheckBox = true;
            StartupAutoBoostCheckBox.IsChecked = isEnabled;
        }
        finally
        {
            _isSettingStartupAutoBoostCheckBox = false;
        }
    }

    private void OpenGitHubRepository()
    {
        try
        {
            OpenUrl(GitHubRepositoryUrl);
            _viewModel.SetStatus(T("FluxRAM GitHub repository opened.", "已打开 FluxRAM GitHub 仓库。"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to open GitHub repository.", ex);
            System.Windows.MessageBox.Show(
                GitHubRepositoryUrl,
                "FluxRAM GitHub",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void ActivateProButton_OnClick(object sender, RoutedEventArgs e)
    {
        _licenseStatus = _licenseManager.Activate(LicenseKeyTextBox.Text);
        if (_licenseStatus.Features.Edition == AppEdition.Pro)
        {
            LicenseKeyTextBox.Text = string.Empty;
            _viewModel.AddEvent(T(
                "Pro edition activated for this computer.",
                "此电脑已永久激活专业版。"));
        }
        else
        {
            _viewModel.AddEvent(T(
                $"Pro activation failed: {_licenseStatus.Failure}.",
                $"专业版激活失败：{_licenseStatus.Failure}。"));
        }

        ApplyEditionUi();
        ApplyLanguage(_uiLanguage, false);
        _viewModel.SetStatus(LocalizeLicenseMessage(_licenseStatus.Message, _licenseStatus.Failure));
    }

    private void AddProtectedAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licenseStatus.Features.SupportsProtectList)
        {
            _viewModel.SetStatus(T(
                "App protection is available in Pro edition only.",
                "应用保护仅在专业版可用。"));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Multiselect = true,
            Title = T("Select protected applications", "选择受保护应用")
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var addedCount = 0;
        foreach (var fileName in dialog.FileNames)
        {
            if (TryAddProtectedPath(fileName))
            {
                addedCount += 1;
            }
        }

        RefreshProtectedEntries();
        SaveProtectedApps();
        ProtectedAppsListBox.SelectedIndex = -1;
        _viewModel.AddEvent(T(
            $"Protected apps updated: added {addedCount}.",
            $"受保护应用已更新：新增 {addedCount} 项。"));
    }

    private void AddRunningProtectedAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licenseStatus.Features.SupportsProtectList)
        {
            _viewModel.SetStatus(T(
                "App protection is available in Pro edition only.",
                "应用保护仅在专业版可用。"));
            return;
        }

        var snapshots = ScrapeProcesses(_lastPurgeTimesByProcessId);
        var candidates = ProtectedAppCandidateFactory.FromSnapshots(snapshots, _protectedProcessPaths);
        if (candidates.Count == 0)
        {
            _viewModel.SetStatus(T(
                "No running applications with readable executable paths are available to add.",
                "当前没有可添加且路径可读取的运行中应用。"));
            return;
        }

        var selectedPaths = ShowRunningAppPicker(candidates);
        if (selectedPaths.Count == 0)
        {
            return;
        }

        var addedCount = 0;
        foreach (var selectedPath in selectedPaths)
        {
            if (TryAddProtectedPath(selectedPath, requireExistingFile: false))
            {
                addedCount += 1;
            }
        }

        RefreshProtectedEntries();
        SaveProtectedApps();
        ProtectedAppsListBox.SelectedIndex = -1;
        _viewModel.AddEvent(T(
            $"Protected apps updated from running apps: added {addedCount}.",
            $"已从运行中应用更新保护列表：新增 {addedCount} 项。"));
    }

    private void RemoveProtectedAppButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProtectedAppsListBox.SelectedItems.Count == 0)
        {
            _viewModel.SetStatus(T(
                "Select protected apps to remove.",
                "请先选择要删除的受保护应用。"));
            return;
        }

        var selectedEntries = ProtectedAppsListBox.SelectedItems.Cast<string>().ToArray();
        var removedCount = 0;
        foreach (var selectedEntry in selectedEntries)
        {
            if (RemoveProtectedPath(selectedEntry))
            {
                removedCount += 1;
            }
        }

        RefreshProtectedEntries();
        SaveProtectedApps();
        ProtectedAppsListBox.SelectedIndex = -1;
        _viewModel.AddEvent(T(
            $"Protected apps updated: removed {removedCount}.",
            $"受保护应用已更新：删除 {removedCount} 项。"));
    }

    private void ProtectedAppsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveProtectedAppButton.IsEnabled =
            _licenseStatus.Features.SupportsProtectList &&
            ProtectedAppsListBox.SelectedItems.Count > 0;
    }

    private void LanguageSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSettingLanguageSelector)
        {
            return;
        }

        if (LanguageSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        var language = UiLanguageCatalog.FromCode(tag);
        ApplyLanguage(language);
        _userSettingsStore.SaveLanguage(language);
    }

    private void ProfileSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSettingProfileSelector)
        {
            return;
        }

        if (ProfileSelector.SelectedItem is not ComboBoxItem comboBoxItem || comboBoxItem.Tag is not string rawProfile)
        {
            return;
        }

        if (!Enum.TryParse<OptimizerProfile>(rawProfile, true, out var profile))
        {
            return;
        }

        if (profile == OptimizerProfile.Aggressive && !_licenseStatus.Features.SupportsExtremeProfile)
        {
            SelectProfile(OptimizerProfile.GamingHandheld);
            ApplyProfile(OptimizerProfile.GamingHandheld);
            _userSettingsStore.SaveProfile(OptimizerProfile.GamingHandheld);
            _viewModel.SetStatus(T(
                "Extreme is available in Pro edition only.",
                "Extreme 仅在专业版可用。"));
            return;
        }

        ApplyProfile(profile);
        _userSettingsStore.SaveProfile(profile);
    }

    private async void OptimizerTimer_OnTick(object? sender, EventArgs e)
    {
        await RunMonitoringTickAsync();
    }

    private async Task RunMonitoringTickAsync()
    {
        if (_isMonitoringTickRunning)
        {
            return;
        }

        _isMonitoringTickRunning = true;
        var now = DateTimeOffset.Now;
        try
        {
            var purgeTimesSnapshot = _lastPurgeTimesByProcessId.ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
            var sample = await Task.Run(() => CreateMonitoringSample(purgeTimesSnapshot));
            if (!sample.HasMemorySnapshot)
            {
                _viewModel.UpdateRamDelta(0);
                _viewModel.UpdateAvailableMemory(0);
                _viewModel.TouchLastUpdated(now);
                UpdateSelfOverhead();
                RefreshMetricCards();
                _viewModel.SetStatus(T("Unable to read memory snapshot.", "无法读取内存快照。"));
                return;
            }

            var memorySnapshot = sample.MemorySnapshot;
            UpdateStatusMetrics(memorySnapshot, now);
            var snapshots = sample.Snapshots;
            _applicationYieldTracker.Observe(snapshots, DateTimeOffset.Now);
            _yieldHistoryDialog?.UpdateRows(BuildYieldHistoryRows());
            var foreground = snapshots.Where(x => x.IsForeground).Select(x => x.ProcessName).FirstOrDefault() ?? T("Unknown", "未知");
            _viewModel.UpdateProcessMetrics(snapshots.Count, null, foreground);

            if (AutoBoostPolicy.CanRun(_isAutoBoostEnabled, _optimizerSettings, _lastAutoBoostAt, now))
            {
                var didRun = RunBoostPass(
                    forcePurge: false,
                    trigger: T("Auto Boost", "自动 Boost"),
                    memorySnapshot: memorySnapshot,
                    snapshots: snapshots,
                    now: now);
                if (didRun)
                {
                    _lastAutoBoostAt = now;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Background monitoring skipped this cycle.", ex);
            _viewModel.SetStatus(T(
                "Background monitoring skipped this cycle.",
                "后台监控本轮已跳过。"));
        }
        finally
        {
            _isMonitoringTickRunning = false;
            UpdateMonitoringState();
        }
    }

    private MonitoringSample CreateMonitoringSample(IReadOnlyDictionary<int, DateTimeOffset> lastPurgeTimesByProcessId)
    {
        if (!_memoryStatusService.TryGetSnapshot(out var memorySnapshot))
        {
            return new MonitoringSample(false, default, Array.Empty<ProcessSnapshot>());
        }

        return new MonitoringSample(
            true,
            memorySnapshot,
            ScrapeProcesses(lastPurgeTimesByProcessId));
    }

    private IReadOnlyList<ProcessSnapshot> ScrapeProcesses(
        IReadOnlyDictionary<int, DateTimeOffset>? lastPurgeTimesByProcessId = null)
    {
        lock (_processScraperLock)
        {
            var snapshots = _processScraperService.Scrape(lastPurgeTimesByProcessId);
            _backgroundActivityTracker.Observe(snapshots, DateTimeOffset.UtcNow);
            return snapshots;
        }
    }

    private bool RunBoostPass(
        bool forcePurge,
        string trigger,
        MemorySnapshot? memorySnapshot = null,
        IReadOnlyList<ProcessSnapshot>? snapshots = null,
        DateTimeOffset? now = null)
    {
        if (_isDeepReleaseRunning) return false;
        var startedAt = now ?? DateTimeOffset.Now;
        MemorySnapshot sampled = default;
        if (!memorySnapshot.HasValue && !_memoryStatusService.TryGetSnapshot(out sampled))
        {
            DiagnosticLog.Warning("Boost pass could not read memory snapshot.");
            _viewModel.SetStatus(T("Unable to read memory snapshot.", "无法读取内存快照。"));
            return false;
        }

        var beforeMemory = memorySnapshot ?? sampled;
        var sampledSnapshots = snapshots ?? ScrapeProcesses(_lastPurgeTimesByProcessId);
        var foreground = sampledSnapshots.Where(x => x.IsForeground).Select(x => x.ProcessName).FirstOrDefault() ?? T("Unknown", "未知");
        IReadOnlyCollection<string> protectedProcessNames = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessNames
            : Array.Empty<string>();
        IReadOnlyCollection<string> protectedProcessPaths = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessPaths
            : Array.Empty<string>();
        var effectiveSettings = forcePurge
            ? CreateManualBoostSettings(_optimizerSettings)
            : _optimizerSettings;
        var plan = _purgePolicyService.CreatePlan(
            sampledSnapshots,
            beforeMemory,
            effectiveSettings,
            startedAt,
            _lastPurgeTimesByProcessId,
            forcePurge,
            protectedProcessNames,
            protectedProcessPaths,
            enableAdvancedProtection: _licenseStatus.Features.SupportsAdvancedProtection,
            deferApplication: group => ShouldDeferAutomaticApplication(group, forcePurge, beforeMemory.MemoryLoadPercent, startedAt));
        _viewModel.UpdateProProtectionSummary(
            plan.ProtectionSummary,
            _licenseStatus.Features.SupportsAdvancedProtection);

        _viewModel.UpdateProcessMetrics(sampledSnapshots.Count, plan.CandidateGroups.Count, foreground);

        if (!string.Equals(_lastPolicyMessage, plan.DecisionMessage, StringComparison.Ordinal))
        {
            _lastPolicyMessage = plan.DecisionMessage;
            _viewModel.AddEvent(LocalizePolicyMessage(plan.DecisionMessage));
        }

        if (!plan.ShouldPurge)
        {
            _viewModel.SetStatus(LocalizePolicyMessage(plan.DecisionMessage));
            return false;
        }

        var trimmed = 0L;
        var success = 0;
        var successfulGroups = 0;
        var measuredCount = 0;
        var details = new List<string>();

        foreach (var group in plan.CandidateGroups)
        {
            var groupResults = new List<(ProcessSnapshot Snapshot, MemoryPurgeResult Result)>();
            var groupTrimmed = 0L;
            var groupBefore = 0L;
            var groupAfter = 0L;
            var groupSuccess = 0;
            var groupMeasured = 0;
            foreach (var candidate in group.Processes)
            {
                var result = _memoryPurgeService.Purge(candidate.ProcessId);
                groupResults.Add((candidate, result));
                if (result.Success)
                {
                    if (result.HasMeasurement)
                    {
                        var delta = Math.Max(0L, result.DeltaBytes);
                        groupBefore += Math.Max(0L, result.BeforeWorkingSetBytes);
                        groupAfter += Math.Max(0L, result.AfterWorkingSetBytes);
                        groupTrimmed += delta;
                        trimmed += delta;
                        groupMeasured++;
                        measuredCount++;
                    }
                    groupSuccess += 1;
                    success += 1;
                    _lastPurgeTimesByProcessId[candidate.ProcessId] = startedAt;
                }
                else
                {
                    DiagnosticLog.Warning(
                        $"Boost could not trim {candidate.ProcessName}.exe ({candidate.ProcessId}): {result.ErrorMessage}");
                }
            }

            if (groupSuccess > 0)
            {
                successfulGroups += 1;
            }

            _applicationYieldTracker.Record(group, groupResults, DateTimeOffset.Now);

            var reason = FormatCandidateGroupSignals(group);
            var measurement = groupMeasured == group.Processes.Count && groupMeasured > 0
                ? $"{MainWindowViewModel.FormatBytes(groupBefore)} -> {MainWindowViewModel.FormatBytes(groupAfter)} | " +
                  T($"trim {MainWindowViewModel.FormatBytes(groupTrimmed)}", $"裁剪 {MainWindowViewModel.FormatBytes(groupTrimmed)}")
                : T($"measurement incomplete ({groupMeasured}/{groupSuccess}); verified trim {MainWindowViewModel.FormatBytes(groupTrimmed)}",
                    $"测量不完整（{groupMeasured}/{groupSuccess}）；已确认裁剪 {MainWindowViewModel.FormatBytes(groupTrimmed)}");
            details.Add(T($"{group.ProcessName} | processes {groupSuccess}/{group.Processes.Count} | {measurement} | {reason}",
                $"{group.ProcessName} | 进程 {groupSuccess}/{group.Processes.Count} | {measurement} | {reason}"));
        }

        if (details.Count == 0)
        {
            details.Add(LocalizePolicyMessage(plan.DecisionMessage));
        }

        _viewModel.UpdateBoostDetails(details);
        _totalTrimmedBytes += Math.Max(0L, trimmed);
        _lastBoostAt = startedAt;

        var hasNetMeasurement = _memoryStatusService.TryGetSnapshot(out var after);
        if (hasNetMeasurement)
        {
            _lastBoostBaselineAvailableMemoryBytes = beforeMemory.AvailablePhysicalMemoryBytes;
            _lastBoostNetGainBytes = checked((long)after.AvailablePhysicalMemoryBytes - (long)beforeMemory.AvailablePhysicalMemoryBytes);
            _reboundTrackingUntil = _lastBoostNetGainBytes > 0 ? startedAt.AddSeconds(120) : null;
            UpdateReboundRate(after.AvailablePhysicalMemoryBytes);
            UpdateStatusMetrics(after, DateTimeOffset.Now);
        }
        else
        {
            _lastBoostBaselineAvailableMemoryBytes = beforeMemory.AvailablePhysicalMemoryBytes;
            _lastBoostNetGainBytes = 0;
            _reboundRatePercent = 0d;
            _reboundTrackingUntil = null;
            _viewModel.UpdateReboundRate(0);
        }

        _viewModel.UpdateBoostMetrics(_lastBoostTrimmedBytes = trimmed, _totalTrimmedBytes, _lastBoostNetGainBytes,
            hasTrimMeasurement: measuredCount == plan.Candidates.Count && measuredCount > 0, hasNetMeasurement: hasNetMeasurement);
        RefreshMetricCards();
        _viewModel.SetStatus(T(
            $"{trigger} | load {beforeMemory.MemoryLoadPercent}% | trim {_viewModel.LastBoostTrimmedValue} | net {_viewModel.BoostNetGainValue}",
            $"{trigger} | 负载 {beforeMemory.MemoryLoadPercent}% | 裁剪 {_viewModel.LastBoostTrimmedValue} | 净变化 {_viewModel.BoostNetGainValue}"));
        _viewModel.AddEvent(T(
            $"{trigger}: processed {successfulGroups}/{plan.CandidateGroups.Count} app(s), {success}/{plan.Candidates.Count} process(es).",
            $"{trigger}：已处理 {successfulGroups}/{plan.CandidateGroups.Count} 个应用、{success}/{plan.Candidates.Count} 个进程。"));
        DiagnosticLog.Info(
            $"{trigger}: applications={plan.CandidateGroups.Count}, processes={plan.Candidates.Count}, success={success}, measured={measuredCount}/{plan.Candidates.Count}, verifiedTrim={trimmed}, net={_viewModel.BoostNetGainValue}, load={beforeMemory.MemoryLoadPercent}%.");
        return plan.ShouldPurge;
    }

    private bool ShouldDeferAutomaticApplication(PurgeCandidateGroup group, bool manual, uint memoryLoad, DateTimeOffset now) =>
        !manual && memoryLoad < 90 &&
        (_applicationYieldTracker.HasPendingObservation(group, now) || _applicationYieldTracker.IsDeferred(group, now));

    private void UpdateStatusMetrics(MemorySnapshot snapshot, DateTimeOffset now)
    {
        var delta = checked((long)snapshot.AvailablePhysicalMemoryBytes - (long)_baselineAvailableMemoryBytes);
        _viewModel.UpdateRamDelta(delta);
        _viewModel.UpdateMemorySnapshot(snapshot);
        MemoryTrendChart.AddSample(now, snapshot.MemoryLoadPercent);
        UpdateReboundRate(snapshot.AvailablePhysicalMemoryBytes);
        _viewModel.TouchLastUpdated(now);
        UpdateSelfOverhead();
        RefreshMetricCards();
    }

    private void UpdateReboundRate(ulong currentAvailableMemoryBytes)
    {
        if (!_lastBoostAt.HasValue || _lastBoostNetGainBytes <= 0)
        {
            _reboundRatePercent = 0d;
            _viewModel.UpdateReboundRate(0d);
            return;
        }

        if (!_reboundTrackingUntil.HasValue || DateTimeOffset.Now > _reboundTrackingUntil.Value) return;

        var currentGain = checked((long)currentAvailableMemoryBytes - (long)_lastBoostBaselineAvailableMemoryBytes);
        var reboundBytes = Math.Max(0L, _lastBoostNetGainBytes - Math.Max(0L, currentGain));
        _reboundRatePercent = Math.Clamp(reboundBytes / (double)_lastBoostNetGainBytes * 100d, 0d, 100d);
        _viewModel.UpdateReboundRate(_reboundRatePercent);
    }

    private void ApplyProfile(OptimizerProfile profile)
    {
        if (_selectedProfile == profile)
        {
            return;
        }

        _selectedProfile = profile;
        _optimizerSettings = OptimizerSettingsCatalog.FromProfile(profile);
        _lastPolicyMessage = string.Empty;
        _viewModel.AddEvent(T(
            $"Profile switched to {LocalizeProfileName(profile)}.",
            $"档位切换为 {LocalizeProfileName(profile)}。"));
    }

    private OptimizerSettings CreateManualBoostSettings(OptimizerSettings settings)
    {
        if (_selectedProfile == OptimizerProfile.Aggressive)
        {
            return settings;
        }

        var isGaming = _selectedProfile is OptimizerProfile.Balanced or OptimizerProfile.GamingHandheld;
        var minimumWorkingSetBytes = isGaming
            ? 64L * 1024 * 1024
            : 128L * 1024 * 1024;
        var coldnessFloor = isGaming ? 35d : 52d;
        var extraTargets = isGaming ? 3 : 1;
        var minimumGroupedProcessWorkingSetBytes = isGaming
            ? 8L * 1024 * 1024
            : 16L * 1024 * 1024;

        return settings with
        {
            MinimumCandidateWorkingSetBytes = Math.Min(settings.MinimumCandidateWorkingSetBytes, minimumWorkingSetBytes),
            MinimumColdnessScore = Math.Max(coldnessFloor, settings.MinimumColdnessScore - 12d),
            MaxPurgeTargetsPerPass = settings.MaxPurgeTargetsPerPass <= 0
                ? 0
                : Math.Min(settings.MaxPurgeTargetsPerPass + extraTargets, 12),
            ProcessCooldownSeconds = Math.Min(settings.ProcessCooldownSeconds, 12),
            LowYieldThresholdBytes = Math.Min(settings.LowYieldThresholdBytes, 24L * 1024 * 1024),
            MinimumGroupedProcessWorkingSetBytes = Math.Min(
                settings.MinimumGroupedProcessWorkingSetBytes,
                minimumGroupedProcessWorkingSetBytes)
        };
    }

    private void ApplyEditionUi()
    {
        var edition = _licenseStatus.Features;
        AggressiveProfileItem.Visibility = edition.SupportsExtremeProfile ? Visibility.Visible : Visibility.Collapsed;
        DeepReleaseButton.Style = TryFindResource(edition.SupportsExtremeClose
            ? "DeepReleaseButtonStyle"
            : "DeepReleaseLockedButtonStyle") as Style;
        ProProtectionSummaryTextBlock.Visibility = edition.SupportsAdvancedProtection
            ? Visibility.Visible
            : Visibility.Collapsed;
        AddProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        AddRunningProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        RemoveProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        ProtectListEditorBorder.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        ProtectListLockedBorder.Visibility = edition.SupportsProtectList ? Visibility.Collapsed : Visibility.Visible;

        if (!edition.SupportsExtremeProfile && _selectedProfile == OptimizerProfile.Aggressive)
        {
            _selectedProfile = OptimizerProfile.GamingHandheld;
            _optimizerSettings = OptimizerSettingsCatalog.FromProfile(_selectedProfile);
            SelectProfile(_selectedProfile);
            _userSettingsStore.SaveProfile(_selectedProfile);
        }

        RemoveProtectedAppButton.IsEnabled = false;
        _viewModel.UpdateProProtectionSummary(default, edition.SupportsAdvancedProtection);
        RefreshProtectedEntries();
        UpdateLicenseUi();
    }

    private void SetAutoBoostState(bool isEnabled, bool addEvent, bool persist)
    {
        _isAutoBoostEnabled = isEnabled;
        _viewModel.SetAutoBoost(isEnabled);
        SetAutoBoostToggle(isEnabled);

        if (persist)
        {
            _userSettingsStore.SaveAutoBoost(isEnabled);
        }

        if (addEvent)
        {
            _viewModel.AddEvent(isEnabled
                ? T(
                    "Auto Boost enabled. FluxRAM will boost only when memory pressure is high.",
                    "自动 Boost 已开启。FluxRAM 只会在内存压力高时触发。")
                : T("Auto Boost disabled.", "自动 Boost 已关闭。"));
        }

        UpdateMonitoringState();
    }

    private void SetAutoBoostToggle(bool isEnabled)
    {
        try
        {
            _isSettingAutoBoostToggle = true;
            AutoBoostToggle.IsChecked = isEnabled;
        }
        finally
        {
            _isSettingAutoBoostToggle = false;
        }
    }

    private void UpdateLicenseUi()
    {
        var isPro = _licenseStatus.Features.Edition == AppEdition.Pro;
        MachineIdTextBox.Text = _licenseStatus.MachineId;
        LicenseKeyTextBox.IsEnabled = !isPro;
        ActivateProButton.IsEnabled = !isPro;
        LicenseStatusTextBlock.Text = LocalizeLicenseMessage(_licenseStatus.Message, _licenseStatus.Failure);
    }

    private bool TryAddProtectedPath(string rawPath, bool requireExistingFile = true)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath.Trim());
        }
        catch
        {
            return false;
        }

        if (requireExistingFile && !File.Exists(fullPath))
        {
            return false;
        }

        var normalizedPath = NormalizePath(fullPath);
        var processName = NormalizeProcessName(Path.GetFileName(fullPath));
        var wasAdded = _protectedProcessPaths.Add(normalizedPath);
        _protectedEntryDisplayByPath[normalizedPath] = fullPath;

        if (processName.Length > 0)
        {
            _protectedProcessNames.Add(processName);
        }

        return wasAdded;
    }

    private bool RemoveProtectedPath(string selectedEntry)
    {
        var path = _protectedPathByDisplay.TryGetValue(selectedEntry, out var mappedPath)
            ? mappedPath
            : selectedEntry;
        var normalizedPath = NormalizePath(path);
        if (!_protectedProcessPaths.Remove(normalizedPath))
        {
            return false;
        }

        _protectedEntryDisplayByPath.Remove(normalizedPath);
        var processName = NormalizeProcessName(Path.GetFileName(path));
        if (processName.Length > 0)
        {
            _protectedProcessNames.Remove(processName);
        }

        return true;
    }

    private void LoadProtectedApps()
    {
        foreach (var storedPath in _protectedAppsStore.Load())
        {
            _ = TryAddProtectedPath(storedPath, requireExistingFile: false);
        }
    }

    private void SaveProtectedApps()
    {
        _protectedAppsStore.Save(_protectedEntryDisplayByPath.Values.ToArray());
    }

    private void RefreshProtectedEntries()
    {
        var entries = ProtectedAppDisplayFormatter.Format(
            _protectedEntryDisplayByPath.Values.ToArray(),
            _licenseStatus.Features.SupportsAdvancedProtection,
            _uiLanguage);
        _protectedPathByDisplay.Clear();
        foreach (var entry in entries)
        {
            _protectedPathByDisplay[entry.DisplayText] = entry.Path;
        }

        _viewModel.UpdateProtectedEntries(entries.Select(entry => entry.DisplayText).ToArray());
        _viewModel.UpdateProtectionSummary(entries.Count, _licenseStatus.Features.SupportsProtectList);
    }

    private void SelectLanguage(UiLanguage language)
    {
        var code = UiLanguageCatalog.ToCode(language);
        var item = LanguageSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(comboBoxItem =>
                comboBoxItem.Tag is string tag &&
                tag.Equals(code, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return;
        }

        _isSettingLanguageSelector = true;
        try
        {
            LanguageSelector.SelectedItem = item;
        }
        finally
        {
            _isSettingLanguageSelector = false;
        }
    }

    private void SelectProfile(OptimizerProfile profile)
    {
        var profileCode = profile.ToString();
        var item = ProfileSelector.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(comboBoxItem =>
                comboBoxItem.Tag is string tag &&
                tag.Equals(profileCode, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return;
        }

        _isSettingProfileSelector = true;
        try
        {
            ProfileSelector.SelectedItem = item;
        }
        finally
        {
            _isSettingProfileSelector = false;
        }
    }

    private IReadOnlyList<string> ShowRunningAppPicker(IReadOnlyList<ProtectedAppCandidate> candidates)
    {
        var listBox = new System.Windows.Controls.ListBox
        {
            Margin = new Thickness(12),
            ItemsSource = candidates,
            DisplayMemberPath = nameof(ProtectedAppCandidate.DisplayText),
            SelectionMode = System.Windows.Controls.SelectionMode.Extended,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11
        };

        var addButton = new System.Windows.Controls.Button
        {
            Width = 112,
            Height = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            IsEnabled = false,
            Content = T("Add Selected", "添加所选")
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Width = 88,
            Height = 30,
            IsCancel = true,
            Content = T("Cancel", "取消")
        };

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 12)
        };
        buttonPanel.Children.Add(addButton);
        buttonPanel.Children.Add(cancelButton);

        var layout = new DockPanel();
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        layout.Children.Add(buttonPanel);
        layout.Children.Add(listBox);

        var dialog = new Window
        {
            Owner = this,
            Title = T("Select running apps to protect", "选择要保护的运行中应用"),
            Width = 720,
            Height = 420,
            MinWidth = 560,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = UiFontFamily(_uiLanguage),
            Content = layout
        };

        ApplyDialogResources(dialog);
        listBox.SelectionChanged += (_, _) => addButton.IsEnabled = listBox.SelectedItems.Count > 0;
        addButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        if (dialog.ShowDialog() != true)
        {
            return Array.Empty<string>();
        }

        return listBox.SelectedItems
            .Cast<ProtectedAppCandidate>()
            .Select(candidate => candidate.ExecutablePath)
            .ToArray();
    }

    private DeepReleaseSelection ShowExtremeCloseDialog(
        IReadOnlyList<ExtremeCloseCandidate> candidates,
        IReadOnlyList<OptionalServiceCandidate> serviceCandidates)
    {
        var selectedApplications = new List<ExtremeCloseCandidate>();
        var selectedServices = new List<OptionalServiceCandidate>();
        var checkBoxes = new List<System.Windows.Controls.CheckBox>();
        var serviceCheckBoxes = new List<System.Windows.Controls.CheckBox>();
        var candidatePanel = new StackPanel();
        var search = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 10, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search app or service", "搜索应用或服务"));
        search.ToolTip = T("Search app or service", "搜索应用或服务");

        if (candidates.Count > 0)
        {
            var backgroundCandidates = candidates
                .Where(candidate => !candidate.HasVisibleWindow && !candidate.HasForegroundProcess)
                .ToArray();
            candidatePanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 10),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextSecondaryBrush"),
                Text = T(
                    $"Background accumulation: {backgroundCandidates.Length} apps / {MainWindowViewModel.FormatBytes(backgroundCandidates.Sum(candidate => candidate.WorkingSetBytes))}",
                    $"后台积累：{backgroundCandidates.Length} 个应用 / {MainWindowViewModel.FormatBytes(backgroundCandidates.Sum(candidate => candidate.WorkingSetBytes))}")
            });
        }

        foreach (var candidate in candidates)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = candidate.IsDefaultSelected,
                Tag = candidate,
                Content = new TextBlock { Text = FormatExtremeCloseCandidate(candidate), TextWrapping = TextWrapping.Wrap },
                Foreground = ThemeBrush(candidate.ActivityState switch
                {
                    BackgroundActivityState.Idle => "AccentBrush",
                    BackgroundActivityState.Working or BackgroundActivityState.Visible => "WarningBrush",
                    _ => "TextPrimaryBrush"
                }),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                ToolTip = FormatExtremeCloseCandidateToolTip(candidate)
            };
            checkBoxes.Add(checkBox);
            candidatePanel.Children.Add(checkBox);
        }

        if (serviceCandidates.Count > 0)
        {
            candidatePanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, candidates.Count > 0 ? 12 : 0, 0, 10),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextSecondaryBrush"),
                Text = T("Optional background services", "可选后台服务")
            });

            foreach (var serviceCandidate in serviceCandidates)
            {
                var serviceDisplay = OptionalServiceDisplayFormatter.Format(serviceCandidate, _uiLanguage);
                var serviceCheckBox = new System.Windows.Controls.CheckBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    IsChecked = false,
                    Tag = serviceCandidate,
                    Content = new TextBlock { Text = serviceDisplay.Line, TextWrapping = TextWrapping.Wrap },
                    Foreground = ThemeBrush(serviceCandidate.StopGuidance == OptionalServiceStopGuidance.KeepRunning
                        ? "WarningBrush"
                        : "TextPrimaryBrush"),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    ToolTip = serviceDisplay.ToolTip
                };
                serviceCheckBoxes.Add(serviceCheckBox);
                candidatePanel.Children.Add(serviceCheckBox);
            }
        }

        var warningTextBlock = new TextBlock
        {
            Text = T(
                "Deep Release closes the applications you select. Only apps observed idle for at least 60 seconds can be preselected. Unsaved work may be lost.",
                "深度释放会关闭你选择的应用。只有持续观察闲置至少 60 秒的应用才可能预先勾选，未保存内容可能丢失。"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = ThemeBrush("WarningBrush")
        };

        var selectionSummaryTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("AccentBrush"),
            TextWrapping = TextWrapping.Wrap
        };

        var scrollViewer = new ScrollViewer
        {
            Margin = new Thickness(0, 14, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = candidatePanel
        };

        var confirmButton = new System.Windows.Controls.Button
        {
            Width = 118,
            Height = 32,
            IsDefault = true,
            Content = T("Close Selected", "关闭所选"),
            Style = TryFindResource("PrimaryButtonStyle") as Style
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Width = 88,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
            Content = T("Cancel", "取消"),
            Style = TryFindResource("QuietButtonStyle") as Style
        };

        void RefreshConfirmState()
        {
            var selectedCandidates = checkBoxes
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (ExtremeCloseCandidate)checkBox.Tag)
                .ToArray();
            var selectedServiceCount = serviceCheckBoxes.Count(checkBox => checkBox.IsChecked == true);
            confirmButton.IsEnabled = selectedCandidates.Length > 0 || selectedServiceCount > 0;
            var applicationSummary = DeepReleaseSummaryFormatter.FormatSelection(
                selectedCandidates,
                _uiLanguage);
            selectionSummaryTextBlock.Text = T(
                $"{applicationSummary} | services {selectedServiceCount}",
                $"{applicationSummary} | 服务 {selectedServiceCount} 项");
        }

        foreach (var checkBox in checkBoxes)
        {
            checkBox.Checked += (_, _) => RefreshConfirmState();
            checkBox.Unchecked += (_, _) => RefreshConfirmState();
        }

        foreach (var checkBox in serviceCheckBoxes)
        {
            checkBox.Checked += (_, _) => RefreshConfirmState();
            checkBox.Unchecked += (_, _) => RefreshConfirmState();
        }

        RefreshConfirmState();

        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            foreach (var item in checkBoxes.Concat(serviceCheckBoxes))
            {
                var text = ((TextBlock)item.Content).Text;
                item.Visibility = query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
                if (item.Visibility != Visibility.Visible) item.IsChecked = false;
            }
        };

        var buttonPanel = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0)
        };
        buttonPanel.Children.Add(confirmButton);
        buttonPanel.Children.Add(cancelButton);

        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(warningTextBlock, 0);
        root.Children.Add(warningTextBlock);
        var searchRegion = new StackPanel();
        searchRegion.Children.Add(selectionSummaryTextBlock);
        searchRegion.Children.Add(new TextBlock
        {
            Text = T("Search app or service", "搜索应用或服务"),
            Foreground = ThemeBrush("TextSecondaryBrush"),
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0)
        });
        searchRegion.Children.Add(search);
        Grid.SetRow(searchRegion, 1);
        root.Children.Add(searchRegion);
        Grid.SetRow(scrollViewer, 2);
        root.Children.Add(scrollViewer);
        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Owner = this,
            Title = T("Deep Release", "深度释放"),
            Width = 720d,
            Height = 520d,
            MinWidth = 620d,
            MinHeight = 420d,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = UiFontFamily(_uiLanguage),
            Background = ThemeBrush("WindowBackgroundBrush"),
            Content = root
        };

        ApplyDialogResources(dialog);
        confirmButton.Click += (_, _) =>
        {
            selectedApplications.AddRange(checkBoxes
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (ExtremeCloseCandidate)checkBox.Tag));
            selectedServices.AddRange(serviceCheckBoxes
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (OptionalServiceCandidate)checkBox.Tag));
            dialog.DialogResult = true;
            dialog.Close();
        };

        _ = dialog.ShowDialog();
        return new DeepReleaseSelection(selectedApplications, selectedServices);
    }


    private string FormatExtremeCloseCandidate(ExtremeCloseCandidate candidate)
    {
        var flags = new List<string>();
        if (candidate.HasForegroundProcess)
        {
            flags.Add(T("FOREGROUND", "前台"));
        }

        if (candidate.HasVisibleWindow)
        {
            flags.Add(T("WINDOW", "有窗口"));
        }

        if (candidate.CpuUsagePercent >= 20d)
        {
            flags.Add(T("HIGH CPU", "高 CPU"));
        }

        if (candidate.IoBytesPerSecond >= 16d * 1024 * 1024)
        {
            flags.Add(T("HIGH IO", "高 IO"));
        }

        var activityText = candidate.ActivityState switch
        {
            BackgroundActivityState.Idle => T(
                $"IDLE BACKGROUND · idle {FormatActivityDuration(candidate.IdleFor)}",
                $"闲置后台 · 已闲置 {FormatActivityDuration(candidate.IdleFor)}"),
            BackgroundActivityState.Working => T("BACKGROUND WORKING", "后台工作中"),
            BackgroundActivityState.Visible => T("VISIBLE · REVIEW", "有窗口 · 谨慎关闭"),
            _ => T(
                $"OBSERVING · {FormatActivityDuration(candidate.ObservedFor)} / {FormatActivityDuration(BackgroundActivityTracker.MinimumObservationDuration)}",
                $"观察中 · {FormatActivityDuration(candidate.ObservedFor)} / {FormatActivityDuration(BackgroundActivityTracker.MinimumObservationDuration)}")
        };
        var flagText = flags.Count == 0 ? string.Empty : $" | {string.Join(", ", flags)}";
        return $"[{activityText}] {candidate.ProcessName} | " +
            $"{MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)} | " +
            T($"{candidate.ProcessIds.Count} processes", $"{candidate.ProcessIds.Count} 个进程") +
            Environment.NewLine +
            $"CPU {candidate.CpuUsagePercent:0.0}% | " +
            $"IO {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/s" +
            flagText;
    }

    private string FormatExtremeCloseCandidateToolTip(ExtremeCloseCandidate candidate)
    {
        return candidate.ActivityState switch
        {
            BackgroundActivityState.Idle => T(
                "No foreground, visible window, or obvious CPU/disk activity was observed continuously. Review before closing.",
                "持续观察期间未发现前台、可见窗口或明显 CPU/磁盘活动，关闭前仍请确认。"),
            BackgroundActivityState.Working => T(
                "This app used CPU or disk recently. Closing it may interrupt background work.",
                "该应用近期使用过 CPU 或磁盘，关闭可能中断后台工作。"),
            BackgroundActivityState.Visible => T(
                "This app has a foreground or visible window. Select only if you are sure it can be closed.",
                "该应用存在前台或可见窗口，确认不需要时再勾选。"),
            _ => T(
                "FluxRAM has not observed this app long enough to recommend closing it.",
                "FluxRAM 对该应用的观察时间还不足，暂不建议关闭。")
        };
    }

    private string FormatActivityDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1d)
        {
            return T($"{Math.Floor(duration.TotalMinutes):0} min", $"{Math.Floor(duration.TotalMinutes):0} 分钟");
        }

        return T($"{Math.Max(0, Math.Floor(duration.TotalSeconds)):0} sec", $"{Math.Max(0, Math.Floor(duration.TotalSeconds)):0} 秒");
    }

    private void UpdateMonitoringState()
    {
        var hasReboundTracking = _reboundTrackingUntil.HasValue && DateTimeOffset.Now < _reboundTrackingUntil.Value ||
            _applicationYieldTracker.Reports.Any(report => report.CompletedAt is null);
        var desiredInterval = hasReboundTracking || _isAutoBoostEnabled
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(15);
        if (_optimizerTimer.Interval != desiredInterval)
        {
            _optimizerTimer.Interval = desiredInterval;
        }

        if (!_optimizerTimer.IsEnabled)
        {
            _optimizerTimer.Start();
        }
    }

    private void CaptureBaselineMemory()
    {
        if (_memoryStatusService.TryGetSnapshot(out var snapshot))
        {
            _baselineAvailableMemoryBytes = snapshot.AvailablePhysicalMemoryBytes;
            _viewModel.UpdateRamDelta(0);
            _viewModel.UpdateAvailableMemory(snapshot.AvailablePhysicalMemoryBytes);
            _viewModel.TouchLastUpdated(DateTimeOffset.Now);
            RefreshMetricCards();
            return;
        }

        _baselineAvailableMemoryBytes = 0;
        _viewModel.UpdateRamDelta(0);
        _viewModel.UpdateAvailableMemory(0);
        _viewModel.TouchLastUpdated(DateTimeOffset.Now);
        RefreshMetricCards();
    }

    private void UpdateSelfOverhead()
    {
        if (_memoryStatusService.TryGetSelfOverhead(out var overhead))
        {
            _viewModel.UpdateSelfOverhead(overhead);
        }
    }

    private void ApplyLanguage(UiLanguage language, bool addEvent = true)
    {
        _uiLanguage = language;
        ApplyUiFont(language);
        _viewModel.SetLanguage(language);

        var edition = _licenseStatus.Features;
        Title = edition.ProductTitle;
        AppTitleTextBlock.Text = "FluxRAM";
        AppSubtitleTextBlock.Text = T("Windows memory utility", "Windows 内存管理") + " · " + AppVersionInfo.CurrentDisplayVersion;
        StatusCaptionTextBlock.Text = T("STATUS", "状态");
        ProfileCaptionTextBlock.Text = T("PROFILE", "档位");
        ProfileHelpButton.ToolTip = T("Profile details", "档位说明");
        ConservativeProfileItem.Content = T("Daily", "日常");
        GamingHandheldProfileItem.Content = T("Gaming", "游戏");
        AggressiveProfileItem.Content = T("Extreme", "极致");
        LanguageCaptionTextBlock.Text = T("LANGUAGE", "语言");
        LanguageEnglishItem.Content = "English";
        LanguageChineseSimplifiedItem.Content = "简体中文";
        LanguageChineseTraditionalItem.Content = "繁體中文";
        LanguageJapaneseItem.Content = "日本語";
        LanguageKoreanItem.Content = "한국어";
        EditionCaptionTextBlock.Text = T("EDITION", "版本");
        EditionHelpButton.ToolTip = T("Edition details", "版本功能明细");
        EditionValueTextBlock.Text = T(edition.EditionLabelEnglish, edition.EditionLabelChinese);
        UpdateToolsMenuText();
        OverviewTab.Header = T("Overview", "概览");
        ProtectionTab.Header = T("App protection", "应用保护");
        ActivityTab.Header = T("Recent activity", "最近活动");
        SettingsTab.Header = T("Settings", "设置");
        PreferencesTitleTextBlock.Text = T("Preferences", "偏好设置");
        MemoryLoadCaptionTextBlock.Text = T("Memory load", "内存负载");
        TrendCaptionTextBlock.Text = T("Memory load · last 2 minutes", "内存负载 · 最近 2 分钟");
        BoostEmptyTextBlock.Text = T("No Boost results yet. Preview to check candidates.", "暂无 Boost 结果，可先预览候选应用。");
        ViewBoostDetailsButton.Content = ViewProtectionDetailsButton.Content = ViewActivityDetailsButton.Content = T("Details", "查看详情");
        ViewYieldHistoryButton.Content = T("Yield history", "收益观察");
        DetailSettingsButton.ToolTip = _isDetailPanelVisible ? T("Compact view", "精简视图") : T("Full view", "完整视图");
        System.Windows.Automation.AutomationProperties.SetName(DetailSettingsButton, (string)DetailSettingsButton.ToolTip);
        ToolsMenuButton.ToolTip = T("Open app tools menu", "打开应用工具菜单");
        System.Windows.Automation.AutomationProperties.SetName(ToolsMenuButton, T("Tools", "工具"));
        MachineIdCaptionTextBlock.Text = T("MACHINE ID", "机器标识");
        CopyMachineIdButton.Content = T("Copy", "复制");
        LicenseKeyCaptionTextBlock.Text = T("PRO KEY", "专业版 Key");
        ActivateProButton.Content = T("Activate", "激活");
        StartupAutoBoostCheckBox.Content = T(
            "Start with Windows and enable Auto Boost",
            "开机自启并自动开启 Auto Boost");
        BoostNowButton.Content = T("Boost Now", "立即 Boost");
        var deepReleaseEntry = DeepReleaseEntryFormatter.Format(
            _licenseStatus.Features.SupportsExtremeClose,
            _uiLanguage);
        DeepReleaseButton.Content = deepReleaseEntry.Label;
        DeepReleaseButton.ToolTip = deepReleaseEntry.ToolTip;
        AutoBoostToggle.Content = T("Auto Boost", "自动 Boost");
        ProtectListTitleTextBlock.Text = T("Protected Apps", "受保护应用");
        ProtectionModeTextBlock.Text = _licenseStatus.Features.SupportsAdvancedProtection
            ? T(
                "Smart association protection: exact path, child process and related app protection are active.",
                "智能关联保护：精确路径、子进程与关联应用保护已启用。")
            : T(
                "Basic protection: process name only. Pro also protects exact paths, child processes and related apps.",
                "基础保护：仅按进程名保护。Pro 还可保护精确路径、子进程和关联应用。");
        AddProtectedAppButton.Content = T("Add EXE", "添加 EXE");
        AddRunningProtectedAppButton.Content = T("Running App", "运行中应用");
        RemoveProtectedAppButton.Content = T("Remove Selected", "删除所选");
        ProtectListLockedTextBlock.Text = T(
            "Protected app management is unavailable in this build.",
            "当前构建不可用应用保护管理。");
        RamDeltaCaptionTextBlock.Text = T("Available change since launch", "本次运行可用内存变化");
        AvailableCaptionTextBlock.Text = T("AVAILABLE", "可用内存");
        LastBoostTrimmedCaptionTextBlock.Text = T("Last working-set trim", "最近工作集裁剪量");
        TotalTrimmedCaptionTextBlock.Text = T("Trim this session", "本次运行累计裁剪");
        BoostNetGainCaptionTextBlock.Text = T("Observed available-memory gain", "Boost 后可用内存净变化");
        MemoryMetricsTitleTextBlock.Text = T("Memory overview", "内存概况");
        SelfOverheadCaptionTextBlock.Text = T("SELF OVERHEAD", "自身开销");
        RuntimeSummaryTitleTextBlock.Text = T("Optimization", "优化中心");
        BoostDetailsTitleTextBlock.Text = T("Boost results / preview", "Boost 结果 / 候选预览");
        PreviewBoostCandidatesButton.Content = T("Preview", "预览");
        PreviewBoostCandidatesButton.ToolTip = T("Preview manual Boost candidates without trimming memory", "预览手动 Boost 候选，不执行内存裁剪");
        RecentActivityTitleTextBlock.Text = T("Recent Activity", "最近活动");
        LicenseStatusCaptionTextBlock.Text = T("LICENSE STATUS", "授权状态");

        _openTrayMenuItem.Text = T("Open FluxRAM", "打开 FluxRAM");
        _boostTrayMenuItem.Text = T("Boost Now", "立即 Boost");
        _exitTrayMenuItem.Text = T("Exit", "退出");
        _trayIcon.Text = edition.ProductTitle;
        RefreshStartupAutoBoostStatus();
        UpdateLicenseUi();
        RefreshProtectedEntries();
        RefreshMetricCards();

        if (addEvent)
        {
            _viewModel.AddEvent(T("Language switched.", "语言已切换。"));
        }
    }

    private void ApplyUiFont(UiLanguage language)
    {
        FontFamily = UiFontFamily(language);
    }

    private static Media.FontFamily UiFontFamily(UiLanguage language)
    {
        return language switch
        {
            UiLanguage.ChineseSimplified => new Media.FontFamily("Microsoft YaHei UI, Segoe UI"),
            UiLanguage.ChineseTraditional => new Media.FontFamily("Microsoft JhengHei UI, Microsoft YaHei UI, Segoe UI"),
            UiLanguage.Japanese => new Media.FontFamily("Yu Gothic UI, Meiryo UI, Segoe UI"),
            UiLanguage.Korean => new Media.FontFamily("Malgun Gothic, Segoe UI"),
            _ => new Media.FontFamily("Segoe UI")
        };
    }

    private void ApplyTheme(AppTheme theme, bool addEvent = true)
    {
        _uiTheme = theme;
        var light = theme == AppTheme.Light;
        SetThemeBrush("WindowBackgroundBrush", "#161A19", "#F5F7F6", light);
        SetThemeBrush("SurfaceBrush", "#1E2321", "#FFFFFF", light);
        SetThemeBrush("SurfaceSoftBrush", "#191E1D", "#F2F5F3", light);
        SetThemeBrush("BorderBrushSoft", "#39443F", "#CBD6CF", light);
        SetThemeBrush("InsetBorderBrush", "#344139", "#DFE6E1", light);
        SetThemeBrush("TextPrimaryBrush", "#F1F5F3", "#17241E", light);
        SetThemeBrush("TextSecondaryBrush", "#C6D1CA", "#3E5247", light);
        SetThemeBrush("TextMutedBrush", "#A5B6AC", "#54695D", light);
        SetThemeBrush("AccentBrush", "#3DD6A3", "#087C5D", light);
        SetThemeBrush("AccentSoftBrush", "#18362B", "#DDF7ED", light);
        SetThemeBrush("WarningBrush", "#F7C873", "#B7791F", light);
        SetThemeBrush("TextBoxBackgroundBrush", "#191E1D", "#FFFFFF", light);
        SetThemeBrush("TextBoxBorderBrush", "#45544C", "#A8B8AE", light);
        SetThemeBrush("SelectionBrush", "#516B8D", "#B7D7FF", light);
        SetThemeBrush("ComboBoxForegroundBrush", "#17202C", "#101827", light);
        SetThemeBrush("ComboBoxBackgroundBrush", "#EFF5FB", "#FFFFFF", light);
        SetThemeBrush("ComboBoxBorderBrush", "#CAD5E2", "#9AAABD", light);
        SetThemeBrush("ButtonBackgroundBrush", "#252D29", "#FFFFFF", light);
        SetThemeBrush("ButtonBorderBrush", "#45544C", "#CBD6CF", light);
        SetThemeBrush("ButtonHoverBrush", "#223044", "#DDE7F2", light);
        SetThemeBrush("ButtonHoverBorderBrush", "#526A84", "#8096AD", light);
        SetThemeBrush("ButtonPressedBrush", "#192331", "#CEDBEA", light);
        SetThemeBrush("PrimaryButtonBackgroundBrush", "#3DD6A3", "#087C5D", light);
        SetThemeBrush("PrimaryButtonBorderBrush", "#3DD6A3", "#087C5D", light);
        SetThemeBrush("PrimaryButtonTextBrush", "#06130D", "#FFFFFF", light);
        SetThemeBrush("QuietButtonBackgroundBrush", "#252D29", "#F2F5F3", light);
        SetThemeBrush("QuietButtonBorderBrush", "#45544C", "#CBD6CF", light);
        SetThemeBrush("IconButtonBackgroundBrush", "#252D29", "#F2F5F3", light);
        SetThemeBrush("IconButtonBorderBrush", "#45544C", "#CBD6CF", light);
        SetThemeBrush("ToggleBackgroundBrush", "#1B2531", "#EAF0F7", light);
        SetThemeBrush("ToggleBorderBrush", "#304156", "#B7C5D6", light);
        SetThemeBrush("ToggleHoverBorderBrush", "#5C7087", "#8096AD", light);
        SetThemeBrush("ListItemSelectedBrush", "#253A31", "#DDF7ED", light);
        SetThemeBrush("ListItemHoverBrush", "#29352F", "#ECF2EE", light);
        SetThemeBrush("ScrollBarTrackBrush", "#1E2321", "#FFFFFF", light);
        SetThemeBrush("ScrollBarThumbBrush", "#66786C", "#849B8C", light);
        SetThemeBrush("ScrollBarThumbHoverBrush", "#93A99B", "#54695D", light);
        SetThemeBrush("IconTileBackgroundBrush", "#0D141D", "#FFFFFF", light);
        SetThemeBrush("IconTileBorderBrush", "#2E3B4C", "#C5D1DE", light);
        SetThemeBrush("EditionBadgeBorderBrush", "#315E4B", "#86D7B6", light);
        SetThemeBrush("EditionBadgeTextBrush", "#9FF0C9", "#087A5A", light);
        Background = ThemeBrush("WindowBackgroundBrush");
        UpdateToolsMenuText();

        if (addEvent)
        {
            _viewModel.AddEvent(theme == AppTheme.Light
                ? T("Theme switched to light mode.", "主题已切换为亮色模式。")
                : T("Theme switched to dark mode.", "主题已切换为暗色模式。"));
        }
    }

    private void UpdateToolsMenuText()
    {
        CheckUpdateMenuItem.Header = T("Check Update", "检查更新");
        ThemeMenuItem.Header = _uiTheme == AppTheme.Light
            ? T("Switch to Dark", "切换到暗色")
            : T("Switch to Light", "切换到亮色");
        DiagnosticLogMenuItem.Header = T("Diagnostic Log", "诊断日志");
        GithubMenuItem.Header = "GitHub";
    }

    private void SetThemeBrush(string key, string darkColor, string lightColor, bool light)
    {
        if (Media.ColorConverter.ConvertFromString(light ? lightColor : darkColor) is Media.Color color)
        {
            Resources[key] = new Media.SolidColorBrush(color);
        }
    }

    private Media.Brush ThemeBrush(string key)
    {
        return Resources[key] as Media.Brush ?? Media.Brushes.Transparent;
    }

    private static string NormalizePath(string value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : Path.GetFullPath(value.Trim()).Replace('/', '\\').ToLowerInvariant();

    private static string NormalizeProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.ToLowerInvariant();
    }

    private static OptimizerProfile NormalizeProfileForEdition(OptimizerProfile profile, AppEditionFeatures features)
    {
        if (profile == OptimizerProfile.Balanced)
        {
            return OptimizerProfile.GamingHandheld;
        }

        return profile == OptimizerProfile.Aggressive && !features.SupportsExtremeProfile
            ? OptimizerProfile.GamingHandheld
            : profile;
    }

    private string LocalizeProfileName(OptimizerProfile profile) => profile switch
    {
        OptimizerProfile.Conservative => T("Daily", "日常"),
        OptimizerProfile.Balanced => T("Gaming", "游戏"),
        OptimizerProfile.GamingHandheld => T("Gaming", "游戏"),
        OptimizerProfile.Aggressive => T("Extreme", "极致"),
        _ => T("Gaming", "游戏")
    };

    private string FormatCandidateGroupSignals(PurgeCandidateGroup group)
    {
        var yieldLevel = group.WorkingSetBytes >= 1024L * 1024 * 1024
            ? T("high yield", "高收益")
            : group.WorkingSetBytes >= 256L * 1024 * 1024
                ? T("medium yield", "中收益")
                : T("low yield", "低收益");
        var riskLevel = CandidateRiskLevel(group);
        return T(
            $"{yieldLevel} | risk {riskLevel} | cold {group.ColdnessScore:0} | cpu {group.CpuUsagePercent:0.0}% | io {MainWindowViewModel.FormatBytes((long)group.IoBytesPerSecond)}/s",
            $"{yieldLevel} | 风险 {riskLevel} | 冷度 {group.ColdnessScore:0} | CPU {group.CpuUsagePercent:0.0}% | IO {MainWindowViewModel.FormatBytes((long)group.IoBytesPerSecond)}/秒");
    }

    private string CandidateRiskLevel(PurgeCandidateGroup group)
    {
        if (group.CpuUsagePercent < 1d &&
            group.IoBytesPerSecond < 64 * 1024d &&
            !group.HasVisibleWindow)
        {
            return T("low", "低");
        }

        if (group.CpuUsagePercent < 4d &&
            group.IoBytesPerSecond < 1024 * 1024d)
        {
            return T("medium", "中");
        }

        return T("elevated", "偏高");
    }

    private UIElement CreateEditionDetailsContent(Window dialog)
    {
        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleTextBlock = new TextBlock
        {
            Text = T(EditionDetailsCatalog.DialogTitleEnglish, EditionDetailsCatalog.DialogTitleChinese),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeBrush("TextPrimaryBrush")
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var subtitleTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            Text = T(EditionDetailsCatalog.DialogSubtitleEnglish, EditionDetailsCatalog.DialogSubtitleChinese),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 18,
            Foreground = ThemeBrush("TextSecondaryBrush")
        };
        Grid.SetRow(subtitleTextBlock, 1);
        root.Children.Add(subtitleTextBlock);

        var sectionGrid = new Grid
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(sectionGrid, 2);
        root.Children.Add(sectionGrid);

        var freeCard = CreateEditionDetailsCard(EditionDetailsCatalog.Sections[0], ThemeBrush("AccentBrush"));
        Grid.SetColumn(freeCard, 0);
        sectionGrid.Children.Add(freeCard);

        var proCard = CreateEditionDetailsCard(EditionDetailsCatalog.Sections[1], ThemeBrush("WarningBrush"));
        Grid.SetColumn(proCard, 2);
        sectionGrid.Children.Add(proCard);

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var upgradeButton = new System.Windows.Controls.Button
        {
            Width = 150,
            Height = 32,
            Content = UpgradeProLabel(),
            ToolTip = PurchaseOptionsCatalog.UsesAlipayFlow(_uiLanguage)
                ? PurchaseOptionsCatalog.DomesticPriceText
                : PurchaseOptionsCatalog.InternationalPriceText,
            Style = TryFindResource("PrimaryButtonStyle") as Style
        };
        upgradeButton.Click += (_, _) => UpgradeProButton_OnClick(dialog);
        buttonPanel.Children.Add(upgradeButton);

        var closeButton = new System.Windows.Controls.Button
        {
            Width = 96,
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            Content = T("Close", "关闭"),
            Style = TryFindResource("QuietButtonStyle") as Style
        };
        closeButton.Click += (_, _) => dialog.Close();
        buttonPanel.Children.Add(closeButton);

        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        return root;
    }

    private void UpgradeProButton_OnClick(Window owner)
    {
        if (PurchaseOptionsCatalog.UsesAlipayFlow(_uiLanguage))
        {
            ProPurchaseDialogFactory.ShowAlipayDialog(
                owner,
                _uiLanguage,
                _licenseStatus.MachineId,
                CopyMachineIdToClipboard);
            return;
        }

        OpenWhopPurchaseLink();
    }

    private void OpenWhopPurchaseLink()
    {
        try
        {
            CopyMachineIdToClipboard();
            OpenUrl(PurchaseOptionsCatalog.WhopPurchaseUrl);
            _viewModel.SetStatus(T(
                "Whop purchase page opened. Machine ID copied for checkout.",
                "已打开 Whop 购买页面，并复制机器标识。"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to open Whop purchase page.", ex);
            System.Windows.MessageBox.Show(
                this,
                T("Unable to open the Whop purchase page.", "无法打开 Whop 购买页面。") +
                Environment.NewLine +
                PurchaseOptionsCatalog.WhopPurchaseUrl,
                "FluxRAM Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private string UpgradeProLabel()
    {
        return _uiLanguage switch
        {
            UiLanguage.ChineseSimplified => "升级 Pro",
            UiLanguage.ChineseTraditional => "升級 Pro",
            UiLanguage.Japanese => "Pro · $3",
            UiLanguage.Korean => "Pro · $3",
            _ => "Upgrade Pro · $3"
        };
    }

    private void RefreshStartupAutoBoostStatus()
    {
        if (StartupAutoBoostCheckBox.IsChecked != true)
        {
            StartupAutoBoostStatusTextBlock.Text = T(
                "Startup Auto Boost is off.",
                "开机自启 Auto Boost 未开启。");
            return;
        }

        try
        {
            var status = _startupAutoBoostService.GetRegistrationStatus();
            StartupAutoBoostStatusTextBlock.Text = LocalizeStartupAutoBoostStatus(status);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Unable to inspect Startup Auto Boost registration.", ex);
            StartupAutoBoostStatusTextBlock.Text = T(
                "Startup task status could not be checked.",
                "无法检查开机任务状态。");
        }
    }

    private string LocalizeStartupAutoBoostStatus(StartupAutoBoostRegistrationStatus status)
    {
        return status.Kind switch
        {
            StartupAutoBoostRegistrationKind.Registered => T(
                "Startup task registered and points to this app.",
                "开机任务已注册，并指向当前程序。"),
            StartupAutoBoostRegistrationKind.NotRegistered => T(
                "Startup task is missing. Toggle this setting off and on to repair.",
                "开机任务缺失。可关闭后重新开启此设置进行修复。"),
            StartupAutoBoostRegistrationKind.PathMismatch => T(
                "Startup task points to an old or missing app path. Toggle off/on to repair.",
                "开机任务指向旧路径或缺失路径。可关闭后重新开启进行修复。"),
            StartupAutoBoostRegistrationKind.ArgumentMissing => T(
                "Startup task is missing the Auto Boost launch flag. Toggle off/on to repair.",
                "开机任务缺少 Auto Boost 启动参数。可关闭后重新开启进行修复。"),
            _ => T(
                "Startup task status is unknown. Toggle off/on to repair.",
                "开机任务状态未知。可关闭后重新开启进行修复。")
        };
    }

    private string LocalizeUpdateCheckResult(UpdateCheckResult result)
    {
        return result.State switch
        {
            UpdateCheckState.UpdateAvailable => T(
                $"Update available: {result.LatestVersion} (current {result.CurrentVersion}).",
                $"发现新版本：{result.LatestVersion}（当前 {result.CurrentVersion}）。"),
            UpdateCheckState.UpToDate => T(
                $"FluxRAM is up to date ({result.CurrentVersion}).",
                $"FluxRAM 已是最新版本（{result.CurrentVersion}）。"),
            UpdateCheckState.CurrentBuildIsNewer => T(
                $"Current build {result.CurrentVersion} is newer than the latest public release {result.LatestVersion}.",
                $"当前构建 {result.CurrentVersion} 新于最新公开版本 {result.LatestVersion}。"),
            UpdateCheckState.ReleaseVersionUnavailable => T(
                "GitCode release information was found, but the version could not be read.",
                "已找到 GitCode 发布信息，但无法读取版本号。"),
            _ => T(
                $"Unable to check updates: {result.ErrorMessage ?? "unknown error"}",
                $"无法检查更新：{result.ErrorMessage ?? "未知错误"}")
        };
    }

    private UIElement CreateProfileDetailsContent(Window dialog)
    {
        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleTextBlock = new TextBlock
        {
            Text = T("Profile details", "档位说明"),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = ThemeBrush("TextPrimaryBrush")
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var subtitleTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            Text = T(
                "Choose the profile by how much memory pressure you want FluxRAM to respond to.",
                "根据你希望 FluxRAM 对内存压力的响应强度来选择档位。"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 18,
            Foreground = ThemeBrush("TextSecondaryBrush")
        };
        Grid.SetRow(subtitleTextBlock, 1);
        root.Children.Add(subtitleTextBlock);

        var panel = new StackPanel
        {
            Margin = new Thickness(0)
        };
        panel.Children.Add(CreateProfileDetailsCard(
            T("Daily", "日常"),
            T("Lowest disturbance. Best for office, browsing and general daily work when stability matters more than visible cleanup numbers.", "最低打扰。适合办公、浏览器和日常使用，优先稳定性，不追求好看的释放数字。"),
            ThemeBrush("AccentBrush")));
        panel.Children.Add(CreateProfileDetailsCard(
            T("Gaming", "游戏"),
            T("Recommended. For gaming PCs and Windows handhelds. More willing to clear cold background apps before games, while protecting foreground, high CPU/I/O, game launcher and device-control processes.", "推荐默认。适合游戏 PC 和 Windows 掌机。更愿意清理冷后台程序，同时保护前台、高 CPU/I/O、游戏平台和掌机控制中心。"),
            CreateDialogBrush(90, 214, 191)));
        panel.Children.Add(CreateProfileDetailsCard(
            T("Extreme", "极致"),
            T("Pro only. Aggressive trimming for heavy local AI, creator tools, games or streaming workloads. Use when you want stronger cleanup and accept more risk.", "专业版专属。适合本地 AI、创作软件、游戏或直播等高负载场景。清理更强，风险也更高。"),
            ThemeBrush("WarningBrush")));
        var scrollViewer = new ScrollViewer
        {
            Margin = new Thickness(0, 16, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
        Grid.SetRow(scrollViewer, 2);
        root.Children.Add(scrollViewer);

        var closeButton = new System.Windows.Controls.Button
        {
            Width = 96,
            Height = 32,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Content = T("Close", "关闭"),
            Style = TryFindResource("QuietButtonStyle") as Style
        };
        closeButton.Click += (_, _) => dialog.Close();
        Grid.SetRow(closeButton, 3);
        root.Children.Add(closeButton);

        return root;
    }

    private Border CreateProfileDetailsCard(string title, string body, Media.Brush accentBrush)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = accentBrush
        });
        panel.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 7, 0, 0),
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = ThemeBrush("TextPrimaryBrush")
        });

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14),
            Background = ThemeBrush("SurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel
        };
    }

    private Border CreateEditionDetailsCard(EditionDetailsSection section, Media.Brush accentBrush)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = T(section.TitleEnglish, section.TitleChinese),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = accentBrush
        });
        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 10, 0, 12),
            Background = ThemeBrush("InsetBorderBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = T(section.BodyEnglish, section.BodyChinese),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 21,
            Foreground = ThemeBrush("TextPrimaryBrush")
        });

        return new Border
        {
            Padding = new Thickness(15),
            Background = ThemeBrush("SurfaceBrush"),
            BorderBrush = ThemeBrush("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel
        };
    }

    private static Media.Brush CreateDialogBrush(byte red, byte green, byte blue)
    {
        return new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
    }

    private sealed record MonitoringSample(
        bool HasMemorySnapshot,
        MemorySnapshot MemorySnapshot,
        IReadOnlyList<ProcessSnapshot> Snapshots);

    private sealed record DeepReleaseSelection(
        IReadOnlyList<ExtremeCloseCandidate> Applications,
        IReadOnlyList<OptionalServiceCandidate> Services);

    private string LocalizePolicyMessage(string message)
    {
        if (_uiLanguage is not (UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional))
        {
            return message;
        }

        return message
            .Replace("Memory pressure is low; purge skipped.", "内存压力较低，本轮跳过。", StringComparison.Ordinal)
            .Replace("Available", "可用内存", StringComparison.Ordinal)
            .Replace("is above threshold", "高于阈值", StringComparison.Ordinal)
            .Replace("Boost Now plan with", "Boost Now 计划：", StringComparison.Ordinal)
            .Replace("Purge plan ready with", "清理计划已生成：", StringComparison.Ordinal)
            .Replace("Extreme bypassed threshold with", "极致策略已绕过阈值：", StringComparison.Ordinal)
            .Replace("application(s)", "个应用", StringComparison.Ordinal)
            .Replace("process(es)", "个进程", StringComparison.Ordinal)
            .Replace("No eligible process met safety criteria", "没有满足安全条件的候选应用", StringComparison.Ordinal)
            .Replace("no user processes could be scanned", "没有可扫描的用户进程", StringComparison.Ordinal)
            .Replace("no safe background candidate remained", "没有剩余安全后台候选", StringComparison.Ordinal)
            .Replace("foreground application(s)", "前台应用", StringComparison.Ordinal)
            .Replace("below size threshold", "低于大小阈值", StringComparison.Ordinal)
            .Replace("awaiting CPU/I/O measurements", "等待 CPU/磁盘活动采样", StringComparison.Ordinal)
            .Replace("not cold enough", "冷度不足", StringComparison.Ordinal)
            .Replace("yield observation or backoff", "正在观察收益或暂缓重复裁剪", StringComparison.Ordinal)
            .Replace("active CPU/I/O", "CPU/I/O 活跃", StringComparison.Ordinal)
            .Replace("protected", "受保护", StringComparison.Ordinal)
            .Replace("cooldown", "冷却期", StringComparison.Ordinal);
    }

    private string LocalizeLicenseMessage(string message, LicenseVerificationFailure failure)
    {
        if (_uiLanguage is not (UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional))
        {
            return failure == LicenseVerificationFailure.None ? message : $"{message} ({failure})";
        }

        return failure switch
        {
            LicenseVerificationFailure.None when _licenseStatus.Features.Edition == AppEdition.Pro =>
                _licenseStatus.IsActivated ? "此电脑已永久激活专业版。" : "当前构建为专业版。",
            LicenseVerificationFailure.None => "普通版。输入专业版 Key 可激活 FluxRAM Pro。",
            LicenseVerificationFailure.MachineMismatch => "Key 不属于当前电脑。",
            LicenseVerificationFailure.InvalidSignature => "Key 签名无效。",
            LicenseVerificationFailure.WrongProduct => "Key 不属于 FluxRAM。",
            LicenseVerificationFailure.WrongEdition => "Key 不是专业版授权。",
            _ => "Key 格式无效。"
        };
    }

    private string T(string english, string chinese) => UiLanguageLocalizer.Localize(_uiLanguage, english, chinese);

    private void ApplyDialogResources(Window dialog)
    {
        foreach (DictionaryEntry resource in Resources)
        {
            dialog.Resources[resource.Key] = resource.Value;
        }
    }

    private string BuildProProtectionDetail(ProcessProtectionSummary summary)
    {
        return T(
            $"PRO GUARD | recognized {summary.TotalCount} protected or related process(es)",
            $"PRO 守护 | 本次识别并保护 {summary.TotalCount} 个受保护或关联进程");
    }

    private void RefreshMetricCards()
    {
        RamDeltaValueTextBlock.Text = _viewModel.RamDeltaValue;
        AvailableValueTextBlock.Text = _viewModel.AvailableRamValue;
        LastBoostTrimmedValueTextBlock.Text = _viewModel.LastBoostTrimmedValue;
        TotalTrimmedValueTextBlock.Text = _viewModel.TotalTrimmedValue;
        BoostNetGainValueTextBlock.Text = _viewModel.BoostNetGainValue;
    }

    private void ApplyDetailPanelState(bool isVisible)
    {
        _isDetailPanelVisible = isVisible;
        WorkspaceTabs.SelectedIndex = 0;
        ProtectionTab.Visibility = ActivityTab.Visibility = SettingsTab.Visibility = ResultsRegion.Visibility = TrendRegion.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailSettingsButton.ToolTip = isVisible ? T("Compact view", "精简视图") : T("Full view", "完整视图");
        System.Windows.Automation.AutomationProperties.SetName(DetailSettingsButton, (string)DetailSettingsButton.ToolTip);

        if (isVisible)
        {
            var workArea = SystemParameters.WorkArea;
            var targetWidth = Math.Min(DetailWindowWidth, Math.Max(CompactWindowWidth, workArea.Width - 48d));
            var targetHeight = Math.Min(DetailWindowHeight, Math.Max(CompactWindowHeight, workArea.Height - 72d));
            MinWidth = Math.Min(DetailMinWindowWidth, targetWidth);
            MinHeight = Math.Min(DetailMinWindowHeight, targetHeight);
            Width = targetWidth;
            Height = targetHeight;
            return;
        }

        MinWidth = CompactMinWindowWidth;
        MinHeight = CompactMinWindowHeight;
        Width = CompactWindowWidth;
        Height = CompactWindowHeight;
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _isExitRequested = true;
        _updateCancellation.Cancel();
        _deepReleaseCancellation?.Cancel();
        _optimizerTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _updateChecker.Dispose();
        _updatePackageService.Dispose();
        DiagnosticLog.Info("FluxRAM closed.");
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;

        if (_hasShownTrayTip)
        {
            return;
        }

        _trayIcon.BalloonTipTitle = _licenseStatus.Features.ProductTitle;
        _trayIcon.BalloonTipText = T("FluxRAM is running in system tray.", "FluxRAM 正在系统托盘中运行。");
        _trayIcon.ShowBalloonTip(1200);
        _hasShownTrayTip = true;
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _ = CheckForUpdatesAsync(automatic: true);
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        _trayIcon.Visible = false;
        Close();
    }

    private static Drawing.Icon ResolveTrayIcon()
    {
        try
        {
            var filePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(filePath);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
        }

        return Drawing.SystemIcons.Application;
    }

    private void TryEnableMicaBackdrop()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var backdropType = NativeMethods.DWMSBT_MAINWINDOW;
        _ = NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
    }
}
