using System;
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
    private const double CompactWindowWidth = 720d;
    private const double CompactWindowHeight = 380d;
    private const double CompactMinWindowWidth = 720d;
    private const double CompactMinWindowHeight = 380d;
    private const double DetailWindowWidth = 1060d;
    private const double DetailWindowHeight = 690d;
    private const double DetailMinWindowWidth = 860d;
    private const double DetailMinWindowHeight = 560d;
    private const string GitHubRepositoryUrl = "https://github.com/Midas927/FluxRAM";

    private readonly MainWindowViewModel _viewModel;
    private readonly ProcessScraperService _processScraperService;
    private readonly MemoryStatusService _memoryStatusService;
    private readonly MemoryPurgeService _memoryPurgeService;
    private readonly PurgePolicyService _purgePolicyService;
    private readonly FluxRAMLicenseManager _licenseManager;
    private readonly ProtectedAppsStore _protectedAppsStore;
    private readonly UserSettingsStore _userSettingsStore;
    private readonly StartupAutoBoostService _startupAutoBoostService;
    private readonly AppUpdateChecker _updateChecker;
    private readonly DispatcherTimer _optimizerTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _openTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _boostTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _exitTrayMenuItem;
    private readonly object _processScraperLock = new();

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

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel();
        _processScraperService = new ProcessScraperService();
        _memoryStatusService = new MemoryStatusService();
        _memoryPurgeService = new MemoryPurgeService();
        _purgePolicyService = new PurgePolicyService();
        _licenseManager = new FluxRAMLicenseManager();
        _protectedAppsStore = new ProtectedAppsStore();
        _userSettingsStore = new UserSettingsStore();
        _startupAutoBoostService = new StartupAutoBoostService();
        _updateChecker = new AppUpdateChecker();
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
        SetStartupAutoBoostCheckBox(initialStartupAutoBoost);
        EnsureStartupAutoBoostRegistration(initialStartupAutoBoost);
        RefreshStartupAutoBoostStatus();
        ApplyDetailPanelState(false);
        SetAutoBoostState(
            initialAutoBoost || initialStartupAutoBoost || launchedForAutoBoost,
            addEvent: false,
            persist: false);

        _viewModel.AddEvent(T("Engine initialized in simplified boost mode.", "引擎已按精简 Boost 模式初始化。"));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableMicaBackdrop();
        CaptureBaselineMemory();
        UpdateSelfOverhead();
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

    private void PreviewBoostCandidatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_memoryStatusService.TryGetSnapshot(out var memorySnapshot))
        {
            DiagnosticLog.Warning("Boost candidate preview could not read memory snapshot.");
            _viewModel.SetStatus(T("Unable to read memory snapshot.", "无法读取内存快照。"));
            return;
        }

        var now = DateTimeOffset.Now;
        var snapshots = ScrapeProcesses(_lastPurgeTimesByProcessId);
        var foreground = snapshots.Where(x => x.IsForeground).Select(x => x.ProcessName).FirstOrDefault() ?? T("Unknown", "未知");
        IReadOnlyCollection<string> protectedProcessNames = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessNames
            : Array.Empty<string>();
        IReadOnlyCollection<string> protectedProcessPaths = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessPaths
            : Array.Empty<string>();
        var manualSettings = CreateManualBoostSettings(_optimizerSettings);
        var plan = _purgePolicyService.CreatePlan(
            snapshots,
            memorySnapshot,
            manualSettings,
            now,
            _lastPurgeTimesByProcessId,
            forcePurge: true,
            protectedProcessNames,
            protectedProcessPaths,
            enableAdvancedProtection: _licenseStatus.Features.SupportsAdvancedProtection);

        var details = plan.Candidates
            .Take(20)
            .Select(candidate =>
            {
                var signals = FormatCandidateSignals(candidate);
                return T(
                    $"PREVIEW | {candidate.ProcessName}.exe | WS {MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)} | {signals}",
                    $"预览 | {candidate.ProcessName}.exe | 工作集 {MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)} | {signals}");
            })
            .ToArray();

        if (details.Length == 0)
        {
            details = [$"PREVIEW | {LocalizePolicyMessage(plan.DecisionMessage)}"];
        }

        _viewModel.UpdateProcessMetrics(snapshots.Count, plan.Candidates.Count, foreground);
        _viewModel.UpdateBoostDetails(details);
        _viewModel.SetStatus(plan.Candidates.Count == 0
            ? LocalizePolicyMessage(plan.DecisionMessage)
            : T(
                $"Preview ready: {plan.Candidates.Count} manual Boost candidate(s).",
                $"预览完成：{plan.Candidates.Count} 个手动 Boost 候选。"));
        _viewModel.AddEvent(T("Manual Boost candidates previewed.", "已预览手动 Boost 候选。"));
        DiagnosticLog.Info($"Boost candidate preview completed. Candidates={plan.Candidates.Count}.");
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
        if (!_isDetailPanelVisible || sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        var listScrollViewer = FindVisualChild<ScrollViewer>(listBox);
        if (CanScrollList(listScrollViewer, e.Delta))
        {
            return;
        }

        e.Handled = true;

        var forwardedEvent = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };

        DetailPanel.RaiseEvent(forwardedEvent);
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

    private void ExtremeCloseMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_licenseStatus.Features.SupportsExtremeClose)
        {
            _viewModel.SetStatus(T(
                "Extreme Close is available after activating Pro.",
                "激活专业版后可使用 Extreme Close。"));
            return;
        }

        var candidates = ExtremeCloseCandidateFactory.FromSnapshots(
                ScrapeProcesses(_lastPurgeTimesByProcessId),
                _licenseStatus.Features.SupportsProtectList ? _protectedProcessNames : Array.Empty<string>(),
                _licenseStatus.Features.SupportsProtectList ? _protectedProcessPaths : Array.Empty<string>(),
                Environment.ProcessId)
            .Take(16)
            .ToArray();

        if (candidates.Length == 0)
        {
            var message = T(
                "Extreme Close found no high-memory app that is safe enough to offer.",
                "Extreme Close 没有找到适合关闭的高占用应用。");
            _viewModel.SetStatus(message);
            _viewModel.UpdateBoostDetails([message]);
            return;
        }

        var selectedCandidates = ShowExtremeCloseDialog(candidates);
        if (selectedCandidates.Count == 0)
        {
            _viewModel.SetStatus(T("Extreme Close cancelled.", "Extreme Close 已取消。"));
            return;
        }

        var result = CloseExtremeCandidates(selectedCandidates);
        _viewModel.UpdateBoostDetails(result.Details);
        _viewModel.SetStatus(T(
            $"Extreme Close: closed {result.ClosedProcessCount}/{result.TotalProcessCount} process(es).",
            $"Extreme Close：已关闭 {result.ClosedProcessCount}/{result.TotalProcessCount} 个进程。"));
        _viewModel.AddEvent(T(
            $"Extreme Close closed {result.ClosedProcessCount}/{result.TotalProcessCount} process(es).",
            $"Extreme Close 已关闭 {result.ClosedProcessCount}/{result.TotalProcessCount} 个进程。"));
        DiagnosticLog.Info($"Extreme Close completed. Closed={result.ClosedProcessCount}, Total={result.TotalProcessCount}.");
        CaptureBaselineMemory();
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

    private async void CheckUpdateMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CheckUpdateMenuItem.IsEnabled = false;
        CheckUpdateMenuItem.Header = T("Checking updates...", "检查更新中...");
        _viewModel.SetStatus(T("Checking GitHub for FluxRAM updates...", "正在检查 FluxRAM 的 GitHub 更新..."));
        DiagnosticLog.Info("User requested update check.");

        try
        {
            var result = await _updateChecker.CheckLatestReleaseAsync();
            var message = LocalizeUpdateCheckResult(result);
            _viewModel.SetStatus(message);
            _viewModel.AddEvent(message);
            DiagnosticLog.Info($"Update check completed. State={result.State}, Current={result.CurrentVersion}, Latest={result.LatestVersion}.");

            if (result.State == UpdateCheckState.UpdateAvailable && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
            {
                var shouldOpen = System.Windows.MessageBox.Show(
                    this,
                    T(
                        $"FluxRAM {result.LatestVersion} is available. Open the download page?",
                        $"发现 FluxRAM {result.LatestVersion}。是否打开下载页面？"),
                    "FluxRAM",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (shouldOpen == MessageBoxResult.Yes)
                {
                    OpenUrl(result.ReleaseUrl);
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("Update check failed unexpectedly.", ex);
            _viewModel.SetStatus(T(
                "Update check failed. See the local diagnostic log for details.",
                "检查更新失败。可查看本地诊断日志了解详情。"));
        }
        finally
        {
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
            var foreground = snapshots.Where(x => x.IsForeground).Select(x => x.ProcessName).FirstOrDefault() ?? T("Unknown", "未知");
            _viewModel.UpdateProcessMetrics(snapshots.Count, 0, foreground);

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
            return _processScraperService.Scrape(lastPurgeTimesByProcessId);
        }
    }

    private bool RunBoostPass(
        bool forcePurge,
        string trigger,
        MemorySnapshot? memorySnapshot = null,
        IReadOnlyList<ProcessSnapshot>? snapshots = null,
        DateTimeOffset? now = null)
    {
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
            enableAdvancedProtection: _licenseStatus.Features.SupportsAdvancedProtection);

        _viewModel.UpdateProcessMetrics(sampledSnapshots.Count, plan.Candidates.Count, foreground);

        if (!string.Equals(_lastPolicyMessage, plan.DecisionMessage, StringComparison.Ordinal))
        {
            _lastPolicyMessage = plan.DecisionMessage;
            _viewModel.AddEvent(LocalizePolicyMessage(plan.DecisionMessage));
        }

        var trimmed = 0L;
        var success = 0;
        var details = new List<string>();

        foreach (var candidate in plan.Candidates)
        {
            var result = _memoryPurgeService.Purge(candidate.ProcessId);
            var reason = FormatCandidateSignals(candidate);

            if (result.Success)
            {
                var delta = Math.Max(0L, result.DeltaBytes);
                trimmed += delta;
                success += 1;
                _lastPurgeTimesByProcessId[candidate.ProcessId] = startedAt;
                details.Add(T(
                    $"{candidate.ProcessName}.exe | {MainWindowViewModel.FormatBytes(result.BeforeWorkingSetBytes)} -> {MainWindowViewModel.FormatBytes(result.AfterWorkingSetBytes)} | trim {MainWindowViewModel.FormatBytes(delta)} | {reason}",
                    $"{candidate.ProcessName}.exe | {MainWindowViewModel.FormatBytes(result.BeforeWorkingSetBytes)} -> {MainWindowViewModel.FormatBytes(result.AfterWorkingSetBytes)} | 裁剪 {MainWindowViewModel.FormatBytes(delta)} | {reason}"));
            }
            else
            {
                details.Add(T(
                    $"{candidate.ProcessName}.exe | failed | {reason} | {result.ErrorMessage}",
                    $"{candidate.ProcessName}.exe | 失败 | {reason} | {result.ErrorMessage}"));
            }
        }

        if (details.Count == 0)
        {
            details.Add(LocalizePolicyMessage(plan.DecisionMessage));
        }

        _viewModel.UpdateBoostDetails(details);
        _totalTrimmedBytes += Math.Max(0L, trimmed);
        _lastBoostAt = startedAt;

        if (_memoryStatusService.TryGetSnapshot(out var after))
        {
            _lastBoostBaselineAvailableMemoryBytes = beforeMemory.AvailablePhysicalMemoryBytes;
            _lastBoostNetGainBytes = checked((long)after.AvailablePhysicalMemoryBytes - (long)beforeMemory.AvailablePhysicalMemoryBytes);
            UpdateReboundRate(after.AvailablePhysicalMemoryBytes);
            _reboundTrackingUntil = _lastBoostNetGainBytes > 0 ? startedAt.AddSeconds(120) : null;
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

        _viewModel.UpdateBoostMetrics(_lastBoostTrimmedBytes = trimmed, _totalTrimmedBytes, _lastBoostNetGainBytes);
        RefreshMetricCards();
        _viewModel.SetStatus(T(
            $"{trigger} | load {beforeMemory.MemoryLoadPercent}% | trim {MainWindowViewModel.FormatBytes(trimmed)} | net {MainWindowViewModel.FormatBytes(_lastBoostNetGainBytes)}",
            $"{trigger} | 负载 {beforeMemory.MemoryLoadPercent}% | 裁剪 {MainWindowViewModel.FormatBytes(trimmed)} | 净增 {MainWindowViewModel.FormatBytes(_lastBoostNetGainBytes)}"));
        _viewModel.AddEvent(T(
            $"{trigger}: purged {success}/{plan.Candidates.Count}.",
            $"{trigger}：已处理 {success}/{plan.Candidates.Count}。"));
        DiagnosticLog.Info(
            $"{trigger}: candidates={plan.Candidates.Count}, success={success}, trimmed={trimmed}, net={_lastBoostNetGainBytes}, load={beforeMemory.MemoryLoadPercent}%.");
        return plan.ShouldPurge;
    }

    private void UpdateStatusMetrics(MemorySnapshot snapshot, DateTimeOffset now)
    {
        var delta = checked((long)snapshot.AvailablePhysicalMemoryBytes - (long)_baselineAvailableMemoryBytes);
        _viewModel.UpdateRamDelta(delta);
        _viewModel.UpdateAvailableMemory(snapshot.AvailablePhysicalMemoryBytes);
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

        return settings with
        {
            MinimumCandidateWorkingSetBytes = Math.Min(settings.MinimumCandidateWorkingSetBytes, minimumWorkingSetBytes),
            MinimumColdnessScore = Math.Max(coldnessFloor, settings.MinimumColdnessScore - 12d),
            MaxPurgeTargetsPerPass = settings.MaxPurgeTargetsPerPass <= 0
                ? 0
                : Math.Min(settings.MaxPurgeTargetsPerPass + extraTargets, 12),
            ProcessCooldownSeconds = Math.Min(settings.ProcessCooldownSeconds, 12),
            LowYieldThresholdBytes = Math.Min(settings.LowYieldThresholdBytes, 24L * 1024 * 1024)
        };
    }

    private void ApplyEditionUi()
    {
        var edition = _licenseStatus.Features;
        AggressiveProfileItem.Visibility = edition.SupportsExtremeProfile ? Visibility.Visible : Visibility.Collapsed;
        ExtremeCloseMenuItem.Visibility = edition.SupportsExtremeClose ? Visibility.Visible : Visibility.Collapsed;
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

    private IReadOnlyList<ExtremeCloseCandidate> ShowExtremeCloseDialog(IReadOnlyList<ExtremeCloseCandidate> candidates)
    {
        var selected = new List<ExtremeCloseCandidate>();
        var checkBoxes = new List<System.Windows.Controls.CheckBox>();
        var candidatePanel = new StackPanel();

        foreach (var candidate in candidates)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                IsChecked = candidate.IsDefaultSelected,
                Tag = candidate,
                Content = FormatExtremeCloseCandidate(candidate),
                Foreground = ThemeBrush(candidate.HasForegroundProcess ? "WarningBrush" : "TextPrimaryBrush"),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                ToolTip = candidate.HasForegroundProcess
                    ? T("Foreground app. Select only if you are sure it can be closed.", "前台应用。确认不需要时再勾选。")
                    : null
            };
            checkBoxes.Add(checkBox);
            candidatePanel.Children.Add(checkBox);
        }

        var warningTextBlock = new TextBlock
        {
            Text = T(
                "Extreme Close closes selected applications. This is not normal Boost. Unsaved work may be lost. Foreground apps are listed but not selected by default.",
                "Extreme Close 会关闭你选择的应用，不是普通 Boost。未保存内容可能丢失。前台应用会列出，但默认不会勾选。"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 19,
            Foreground = ThemeBrush("WarningBrush")
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
            confirmButton.IsEnabled = checkBoxes.Any(checkBox => checkBox.IsChecked == true);
        }

        foreach (var checkBox in checkBoxes)
        {
            checkBox.Checked += (_, _) => RefreshConfirmState();
            checkBox.Unchecked += (_, _) => RefreshConfirmState();
        }

        RefreshConfirmState();

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
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(warningTextBlock, 0);
        root.Children.Add(warningTextBlock);
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);
        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Owner = this,
            Title = T("Extreme Close", "Extreme 关闭应用"),
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

        confirmButton.Click += (_, _) =>
        {
            selected.AddRange(checkBoxes
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (ExtremeCloseCandidate)checkBox.Tag));
            dialog.DialogResult = true;
            dialog.Close();
        };

        _ = dialog.ShowDialog();
        return selected;
    }

    private ExtremeCloseResult CloseExtremeCandidates(IReadOnlyList<ExtremeCloseCandidate> candidates)
    {
        var details = new List<string>();
        var totalProcessCount = 0;
        var closedProcessCount = 0;

        foreach (var candidate in candidates)
        {
            var processes = candidate.ProcessIds
                .Distinct()
                .Select(TryGetProcess)
                .Where(process => process is not null)
                .Cast<Process>()
                .ToArray();
            totalProcessCount += processes.Length;

            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                    {
                        _ = process.CloseMainWindow();
                    }
                }
                catch
                {
                }
            }

            System.Threading.Thread.Sleep(900);

            var closedForCandidate = 0;
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            closedForCandidate += 1;
                            continue;
                        }

                        process.Kill(entireProcessTree: false);
                        if (process.WaitForExit(800))
                        {
                            closedForCandidate += 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticLog.Warning($"Extreme Close could not close process {process.Id}.", ex);
                    }
                }
            }

            closedProcessCount += closedForCandidate;
            details.Add(T(
                $"{candidate.ProcessName}.exe | closed {closedForCandidate}/{processes.Length} | {MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)}",
                $"{candidate.ProcessName}.exe | 已关闭 {closedForCandidate}/{processes.Length} | {MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)}"));
        }

        return new ExtremeCloseResult(totalProcessCount, closedProcessCount, details);
    }

    private static Process? TryGetProcess(int processId)
    {
        try
        {
            return processId == Environment.ProcessId
                ? null
                : Process.GetProcessById(processId);
        }
        catch
        {
            return null;
        }
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

        var flagText = flags.Count == 0 ? T("background", "后台") : string.Join(", ", flags);
        return $"{candidate.ProcessName}.exe | " +
            $"{MainWindowViewModel.FormatBytes(candidate.WorkingSetBytes)} | " +
            $"{candidate.ProcessIds.Count} proc | " +
            $"CPU {candidate.CpuUsagePercent:0.0}% | " +
            $"IO {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/s | " +
            flagText;
    }

    private void UpdateMonitoringState()
    {
        var hasReboundTracking = _reboundTrackingUntil.HasValue && DateTimeOffset.Now < _reboundTrackingUntil.Value;
        if ((hasReboundTracking || _isAutoBoostEnabled) && !_optimizerTimer.IsEnabled)
        {
            _optimizerTimer.Start();
        }
        else if (!hasReboundTracking && !_isAutoBoostEnabled && _optimizerTimer.IsEnabled)
        {
            _optimizerTimer.Stop();
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
        AppTitleTextBlock.Text = edition.ProductTitle;
        AppSubtitleTextBlock.Text = BuildAppSubtitleText();
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
        DetailSettingsButton.Content = _isDetailPanelVisible
            ? T("Hide", "收起")
            : T("Settings", "设置");
        DetailSettingsButton.ToolTip = T("Show or hide detailed settings", "显示或收起详细设置");
        ToolsMenuButton.Content = T("Tools", "工具");
        ToolsMenuButton.ToolTip = T("Open app tools menu", "打开应用工具菜单");
        MinimizeButton.Content = T("Minimize", "最小化");
        MachineIdCaptionTextBlock.Text = T("MACHINE ID", "机器标识");
        CopyMachineIdButton.Content = T("Copy", "复制");
        LicenseKeyCaptionTextBlock.Text = T("PRO KEY", "专业版 Key");
        ActivateProButton.Content = T("Activate", "激活");
        StartupAutoBoostCheckBox.Content = T(
            "Start with Windows and enable Auto Boost",
            "开机自启并自动开启 Auto Boost");
        BoostNowButton.Content = T("Boost Now", "立即 Boost");
        AutoBoostToggle.Content = T("Auto Boost", "自动 Boost");
        ProtectListTitleTextBlock.Text = T("Protected Apps", "受保护应用");
        ProtectionModeTextBlock.Text = _licenseStatus.Features.SupportsAdvancedProtection
            ? T(
                "Pro advanced protection: exact path, child process and window recognition are active.",
                "Pro 高级保护：精确路径、子进程与窗口识别已启用。")
            : T(
                "Basic protection: process name only. Pro also protects exact paths, child processes and matching windows.",
                "基础保护：仅按进程名保护。Pro 还可保护精确路径、子进程和匹配窗口。");
        AddProtectedAppButton.Content = T("Add EXE", "添加 EXE");
        AddRunningProtectedAppButton.Content = T("Running App", "运行中应用");
        RemoveProtectedAppButton.Content = T("Remove Selected", "删除所选");
        ProtectListLockedTextBlock.Text = T(
            "Protected app management is unavailable in this build.",
            "当前构建不可用应用保护管理。");
        RamDeltaCaptionTextBlock.Text = T("RAM DELTA", "内存变化");
        AvailableCaptionTextBlock.Text = T("AVAILABLE", "可用内存");
        LastBoostTrimmedCaptionTextBlock.Text = T("LAST BOOST TRIMMED", "最近 Boost 裁剪量");
        TotalTrimmedCaptionTextBlock.Text = T("TOTAL TRIMMED", "累计裁剪量");
        BoostNetGainCaptionTextBlock.Text = T("BOOST NET GAIN", "Boost 净收益");
        MemoryMetricsTitleTextBlock.Text = T("Memory Metrics", "内存指标");
        SelfOverheadCaptionTextBlock.Text = T("SELF OVERHEAD", "自身开销");
        RuntimeSummaryTitleTextBlock.Text = T("Runtime Summary", "运行摘要");
        BoostDetailsTitleTextBlock.Text = T("Boost Details", "Boost 明细");
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
        SetThemeBrush("WindowBackgroundBrush", "#0E1117", "#F4F7FB", light);
        SetThemeBrush("SurfaceBrush", "#121821", "#FFFFFF", light);
        SetThemeBrush("SurfaceSoftBrush", "#0D131A", "#EEF3F8", light);
        SetThemeBrush("BorderBrushSoft", "#243244", "#C8D4E3", light);
        SetThemeBrush("InsetBorderBrush", "#243040", "#D1DCE8", light);
        SetThemeBrush("TextPrimaryBrush", "#F4F7FB", "#101827", light);
        SetThemeBrush("TextSecondaryBrush", "#D8E2F0", "#243247", light);
        SetThemeBrush("TextMutedBrush", "#C4D0E2", "#526174", light);
        SetThemeBrush("AccentBrush", "#3DD6A3", "#0EAD7C", light);
        SetThemeBrush("AccentSoftBrush", "#18362B", "#DDF7ED", light);
        SetThemeBrush("WarningBrush", "#F7C873", "#B7791F", light);
        SetThemeBrush("TextBoxBackgroundBrush", "#0D141D", "#FFFFFF", light);
        SetThemeBrush("TextBoxBorderBrush", "#334155", "#9AAABD", light);
        SetThemeBrush("SelectionBrush", "#516B8D", "#B7D7FF", light);
        SetThemeBrush("ComboBoxForegroundBrush", "#17202C", "#101827", light);
        SetThemeBrush("ComboBoxBackgroundBrush", "#EFF5FB", "#FFFFFF", light);
        SetThemeBrush("ComboBoxBorderBrush", "#CAD5E2", "#9AAABD", light);
        SetThemeBrush("ButtonBackgroundBrush", "#1B2531", "#EAF0F7", light);
        SetThemeBrush("ButtonBorderBrush", "#304156", "#B7C5D6", light);
        SetThemeBrush("ButtonHoverBrush", "#223044", "#DDE7F2", light);
        SetThemeBrush("ButtonHoverBorderBrush", "#526A84", "#8096AD", light);
        SetThemeBrush("ButtonPressedBrush", "#192331", "#CEDBEA", light);
        SetThemeBrush("PrimaryButtonBackgroundBrush", "#3DD6A3", "#18C48F", light);
        SetThemeBrush("PrimaryButtonBorderBrush", "#7BE8BC", "#0EA574", light);
        SetThemeBrush("PrimaryButtonTextBrush", "#06130D", "#052016", light);
        SetThemeBrush("QuietButtonBackgroundBrush", "#151E29", "#EEF3F8", light);
        SetThemeBrush("QuietButtonBorderBrush", "#2D3A49", "#B7C5D6", light);
        SetThemeBrush("IconButtonBackgroundBrush", "#101923", "#EEF3F8", light);
        SetThemeBrush("IconButtonBorderBrush", "#4B6280", "#94A3B8", light);
        SetThemeBrush("ToggleBackgroundBrush", "#1B2531", "#EAF0F7", light);
        SetThemeBrush("ToggleBorderBrush", "#304156", "#B7C5D6", light);
        SetThemeBrush("ToggleHoverBorderBrush", "#5C7087", "#8096AD", light);
        SetThemeBrush("ListItemSelectedBrush", "#253A31", "#DDF7ED", light);
        SetThemeBrush("ListItemHoverBrush", "#202B38", "#E8F0F8", light);
        SetThemeBrush("ScrollBarTrackBrush", "#0E1117", "#EEF3F8", light);
        SetThemeBrush("ScrollBarThumbBrush", "#3A4A5F", "#AEBBCD", light);
        SetThemeBrush("ScrollBarThumbHoverBrush", "#526A84", "#8096AD", light);
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
        ExtremeCloseMenuItem.Header = T("Extreme Close", "Extreme 关闭应用");
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

    private string FormatCandidateSignals(ProcessSnapshot candidate)
    {
        var yieldLevel = candidate.WorkingSetBytes >= 1024L * 1024 * 1024
            ? T("high yield", "高收益")
            : candidate.WorkingSetBytes >= 512L * 1024 * 1024
                ? T("medium yield", "中收益")
                : T("low yield", "低收益");
        var riskLevel = CandidateRiskLevel(candidate);
        return T(
            $"{yieldLevel} | risk {riskLevel} | cold {candidate.ColdnessScore:0} | cpu {candidate.CpuUsagePercent:0.0}% | io {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/s",
            $"{yieldLevel} | 风险 {riskLevel} | 冷度 {candidate.ColdnessScore:0} | CPU {candidate.CpuUsagePercent:0.0}% | IO {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/秒");
    }

    private string CandidateRiskLevel(ProcessSnapshot candidate)
    {
        if (candidate.CpuUsagePercent < 1d &&
            candidate.IoBytesPerSecond < 64 * 1024d &&
            !candidate.HasVisibleWindow)
        {
            return T("low", "低");
        }

        if (candidate.CpuUsagePercent < 4d &&
            candidate.IoBytesPerSecond < 1024 * 1024d)
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

    private string BuildAppSubtitleText()
    {
        return T(
            "Simplified boost-first memory tool for local Windows workloads",
            "面向本地 Windows 负载的精简 Boost 优先内存工具") +
            $" · {AppVersionInfo.CurrentDisplayVersion}";
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
                "GitHub release information was found, but the version could not be read.",
                "已找到 GitHub 发布信息，但无法读取版本号。"),
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

    private sealed record ExtremeCloseResult(
        int TotalProcessCount,
        int ClosedProcessCount,
        IReadOnlyList<string> Details);

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
            .Replace("Boost Now plan with", "Boost Now 计划，候选数：", StringComparison.Ordinal)
            .Replace("Purge plan ready with", "清理计划已生成，候选数：", StringComparison.Ordinal)
            .Replace("Extreme bypassed threshold with", "Extreme 策略已绕过阈值，候选数：", StringComparison.Ordinal)
            .Replace("No eligible process met safety criteria", "没有满足安全条件的候选进程", StringComparison.Ordinal)
            .Replace("no user processes could be scanned", "没有可扫描的用户进程", StringComparison.Ordinal)
            .Replace("no safe background candidate remained", "没有剩余安全后台候选", StringComparison.Ordinal)
            .Replace("foreground", "前台进程", StringComparison.Ordinal)
            .Replace("below size threshold", "低于大小阈值", StringComparison.Ordinal)
            .Replace("not cold enough", "冷度不足", StringComparison.Ordinal)
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

    private void RefreshMetricCards()
    {
        RamDeltaValueTextBlock.Text = _viewModel.RamDeltaDisplay;
        AvailableValueTextBlock.Text = _viewModel.AvailableRamDisplay;
        LastBoostTrimmedValueTextBlock.Text = _viewModel.LastBoostTrimmedDisplay;
        TotalTrimmedValueTextBlock.Text = _viewModel.TotalTrimmedDisplay;
        BoostNetGainValueTextBlock.Text = _viewModel.BoostNetGainDisplay;
    }

    private void ApplyDetailPanelState(bool isVisible)
    {
        _isDetailPanelVisible = isVisible;
        DetailPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailSettingsButton.Content = isVisible
            ? T("Hide", "收起")
            : T("Settings", "设置");

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
        _optimizerTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _updateChecker.Dispose();
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
