using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using FluxRAM.App.Automation;
using FluxRAM.App.Configuration;
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
    private const double CompactWindowWidth = 560d;
    private const double CompactWindowHeight = 360d;
    private const double CompactMinWindowWidth = 520d;
    private const double CompactMinWindowHeight = 330d;
    private const double DetailWindowWidth = 1060d;
    private const double DetailWindowHeight = 690d;
    private const double DetailMinWindowWidth = 900d;
    private const double DetailMinWindowHeight = 600d;

    private readonly MainWindowViewModel _viewModel;
    private readonly ProcessScraperService _processScraperService;
    private readonly MemoryStatusService _memoryStatusService;
    private readonly MemoryPurgeService _memoryPurgeService;
    private readonly PurgePolicyService _purgePolicyService;
    private readonly FluxRAMLicenseManager _licenseManager;
    private readonly ProtectedAppsStore _protectedAppsStore;
    private readonly UserSettingsStore _userSettingsStore;
    private readonly DispatcherTimer _optimizerTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _openTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _boostTrayMenuItem;
    private readonly Forms.ToolStripMenuItem _exitTrayMenuItem;

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
        _licenseStatus = _licenseManager.GetStatus();
        var initialLanguage = _userSettingsStore.LoadLanguage();
        var initialTheme = _userSettingsStore.LoadTheme();
        _selectedProfile = OptimizerProfile.Conservative;
        _optimizerSettings = OptimizerSettingsCatalog.FromProfile(_selectedProfile);
        _optimizerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
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
        ProfileSelector.SelectedIndex = 0;
        SelectLanguage(initialLanguage);
        ApplyTheme(initialTheme, false);
        ApplyEditionUi();
        ApplyLanguage(initialLanguage, false);
        _viewModel.UpdateRamDelta(0);
        _viewModel.UpdateAvailableMemory(0);
        _viewModel.UpdateBoostMetrics(0, 0, 0);
        _viewModel.UpdateReboundRate(0);
        LoadProtectedApps();
        RefreshProtectedEntries();
        RefreshMetricCards();
        ApplyDetailPanelState(false);
        _viewModel.AddEvent(T("Engine initialized in simplified boost mode.", "引擎已按精简 Boost 模式初始化。"));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TryEnableMicaBackdrop();
        CaptureBaselineMemory();
        UpdateSelfOverhead();
    }

    private void BoostNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        RunBoostPass(true, T("Boost Now", "立即 Boost"));
        UpdateMonitoringState();
    }

    private void AutoBoostToggle_OnChecked(object sender, RoutedEventArgs e)
    {
        _isAutoBoostEnabled = true;
        _viewModel.SetAutoBoost(true);
        _viewModel.AddEvent(T(
            "Auto Boost enabled. FluxRAM will boost only when memory pressure is high.",
            "自动 Boost 已开启。FluxRAM 只会在内存压力高时触发。"));
        UpdateMonitoringState();
    }

    private void AutoBoostToggle_OnUnchecked(object sender, RoutedEventArgs e)
    {
        _isAutoBoostEnabled = false;
        _viewModel.SetAutoBoost(false);
        _viewModel.AddEvent(T("Auto Boost disabled.", "自动 Boost 已关闭。"));
        UpdateMonitoringState();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void DetailSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyDetailPanelState(!_isDetailPanelVisible);
    }

    private void ThemeToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var nextTheme = _uiTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(nextTheme);
        _userSettingsStore.SaveTheme(nextTheme);
    }

    private void EditionHelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = T(EditionDetailsCatalog.DialogTitleEnglish, EditionDetailsCatalog.DialogTitleChinese),
            Width = 620d,
            Height = 430d,
            MinWidth = 560d,
            MinHeight = 380d,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = CreateDialogBrush(11, 16, 23)
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
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = CreateDialogBrush(11, 16, 23)
        };

        dialog.Content = CreateProfileDetailsContent(dialog);
        dialog.ShowDialog();
    }

    private void CopyMachineIdButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_licenseStatus.MachineId);
            _viewModel.SetStatus(T("Machine ID copied.", "机器标识已复制。"));
        }
        catch
        {
            _viewModel.SetStatus(T("Unable to copy Machine ID.", "无法复制机器标识。"));
        }
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

        var snapshots = _processScraperService.Scrape(_lastPurgeTimesByProcessId);
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
            ProfileSelector.SelectedIndex = 1;
            _viewModel.SetStatus(T(
                "Extreme Performance is available in Pro edition only.",
                "极致性能仅在专业版可用。"));
            return;
        }

        ApplyProfile(profile);
    }

    private void OptimizerTimer_OnTick(object? sender, EventArgs e) => RunMonitoringTick();

    private void RunMonitoringTick()
    {
        var now = DateTimeOffset.Now;
        if (!_memoryStatusService.TryGetSnapshot(out var memorySnapshot))
        {
            _viewModel.UpdateRamDelta(0);
            _viewModel.UpdateAvailableMemory(0);
            _viewModel.TouchLastUpdated(now);
            UpdateSelfOverhead();
            RefreshMetricCards();
            _viewModel.SetStatus(T("Unable to read memory snapshot.", "无法读取内存快照。"));
            UpdateMonitoringState();
            return;
        }

        UpdateStatusMetrics(memorySnapshot, now);
        var snapshots = _processScraperService.Scrape(_lastPurgeTimesByProcessId);
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

        UpdateMonitoringState();
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
            _viewModel.SetStatus(T("Unable to read memory snapshot.", "无法读取内存快照。"));
            return false;
        }

        var beforeMemory = memorySnapshot ?? sampled;
        var sampledSnapshots = snapshots ?? _processScraperService.Scrape(_lastPurgeTimesByProcessId);
        var foreground = sampledSnapshots.Where(x => x.IsForeground).Select(x => x.ProcessName).FirstOrDefault() ?? T("Unknown", "未知");
        IReadOnlyCollection<string> protectedProcessNames = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessNames
            : Array.Empty<string>();
        IReadOnlyCollection<string> protectedProcessPaths = _licenseStatus.Features.SupportsProtectList
            ? _protectedProcessPaths
            : Array.Empty<string>();
        var plan = _purgePolicyService.CreatePlan(
            sampledSnapshots,
            beforeMemory,
            _optimizerSettings,
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
            var reason = T(
                $"cold {candidate.ColdnessScore:0} | cpu {candidate.CpuUsagePercent:0.0}% | io {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/s",
                $"冷度 {candidate.ColdnessScore:0} | CPU {candidate.CpuUsagePercent:0.0}% | IO {MainWindowViewModel.FormatBytes((long)candidate.IoBytesPerSecond)}/秒");

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
            details.Add(T(
                "No candidate met threshold/coldness/cooldown/protect-list constraints.",
                "无候选满足阈值/冷度/冷却/保护列表约束。"));
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

    private void ApplyEditionUi()
    {
        var edition = _licenseStatus.Features;
        AggressiveProfileItem.Visibility = edition.SupportsExtremeProfile ? Visibility.Visible : Visibility.Collapsed;
        AddProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        AddRunningProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        RemoveProtectedAppButton.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        ProtectListEditorBorder.Visibility = edition.SupportsProtectList ? Visibility.Visible : Visibility.Collapsed;
        ProtectListLockedBorder.Visibility = edition.SupportsProtectList ? Visibility.Collapsed : Visibility.Visible;

        if (!edition.SupportsExtremeProfile && _selectedProfile == OptimizerProfile.Aggressive)
        {
            _selectedProfile = OptimizerProfile.Balanced;
            _optimizerSettings = OptimizerSettingsCatalog.FromProfile(_selectedProfile);
            ProfileSelector.SelectedIndex = 1;
        }

        RemoveProtectedAppButton.IsEnabled = false;
        RefreshProtectedEntries();
        UpdateLicenseUi();
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
        _viewModel.SetLanguage(language);

        var edition = _licenseStatus.Features;
        Title = edition.ProductTitle;
        AppTitleTextBlock.Text = edition.ProductTitle;
        AppSubtitleTextBlock.Text = T(
            "Simplified boost-first memory tool for local Windows workloads",
            "面向本地 Windows 负载的精简 Boost 优先内存工具");
        StatusCaptionTextBlock.Text = T("STATUS", "状态");
        ProfileCaptionTextBlock.Text = T("PROFILE", "档位");
        ProfileHelpButton.ToolTip = T("Profile details", "档位说明");
        ConservativeProfileItem.Content = T("Light", "轻量");
        BalancedProfileItem.Content = T("Standard", "标准");
        AggressiveProfileItem.Content = T("Extreme Performance", "极致性能");
        LanguageCaptionTextBlock.Text = T("LANGUAGE", "语言");
        LanguageEnglishItem.Content = "English";
        LanguageChineseSimplifiedItem.Content = "简体中文";
        LanguageChineseTraditionalItem.Content = "繁體中文";
        LanguageJapaneseItem.Content = "日本語";
        LanguageKoreanItem.Content = "한국어";
        EditionCaptionTextBlock.Text = T("EDITION", "版本");
        EditionHelpButton.ToolTip = T("Edition details", "版本功能明细");
        EditionValueTextBlock.Text = T(edition.EditionLabelEnglish, edition.EditionLabelChinese);
        UpdateThemeButtonText();
        DetailSettingsButton.Content = _isDetailPanelVisible
            ? T("Hide Details", "收起详情")
            : T("Details", "详细设置");
        MinimizeButton.Content = T("Minimize", "最小化");
        MachineIdCaptionTextBlock.Text = T("MACHINE ID", "机器标识");
        CopyMachineIdButton.Content = T("Copy", "复制");
        LicenseKeyCaptionTextBlock.Text = T("PRO KEY", "专业版 Key");
        ActivateProButton.Content = T("Activate", "激活");
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
        RecentActivityTitleTextBlock.Text = T("Recent Activity", "最近活动");
        LicenseStatusCaptionTextBlock.Text = T("LICENSE STATUS", "授权状态");

        _openTrayMenuItem.Text = T("Open FluxRAM", "打开 FluxRAM");
        _boostTrayMenuItem.Text = T("Boost Now", "立即 Boost");
        _exitTrayMenuItem.Text = T("Exit", "退出");
        _trayIcon.Text = edition.ProductTitle;
        UpdateLicenseUi();
        RefreshProtectedEntries();
        RefreshMetricCards();

        if (addEvent)
        {
            _viewModel.AddEvent(T("Language switched.", "语言已切换。"));
        }
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
        SetThemeBrush("IconTileBackgroundBrush", "#0D141D", "#FFFFFF", light);
        SetThemeBrush("IconTileBorderBrush", "#2E3B4C", "#C5D1DE", light);
        SetThemeBrush("EditionBadgeBorderBrush", "#315E4B", "#86D7B6", light);
        SetThemeBrush("EditionBadgeTextBrush", "#9FF0C9", "#087A5A", light);
        Background = ThemeBrush("WindowBackgroundBrush");
        UpdateThemeButtonText();

        if (addEvent)
        {
            _viewModel.AddEvent(theme == AppTheme.Light
                ? T("Theme switched to light mode.", "主题已切换为亮色模式。")
                : T("Theme switched to dark mode.", "主题已切换为暗色模式。"));
        }
    }

    private void UpdateThemeButtonText()
    {
        ThemeToggleButton.Content = _uiTheme == AppTheme.Light
            ? ThemeLabel("Light", "亮色", "亮色", "ライト", "라이트")
            : ThemeLabel("Dark", "暗色", "暗色", "ダーク", "다크");
        ThemeToggleButton.ToolTip = T("Switch light/dark theme", "切换亮色 / 暗色模式");
    }

    private string ThemeLabel(string english, string chineseSimplified, string chineseTraditional, string japanese, string korean)
    {
        return _uiLanguage switch
        {
            UiLanguage.ChineseSimplified => chineseSimplified,
            UiLanguage.ChineseTraditional => chineseTraditional,
            UiLanguage.Japanese => japanese,
            UiLanguage.Korean => korean,
            _ => english
        };
    }

    private void SetThemeBrush(string key, string darkColor, string lightColor, bool light)
    {
        if (Resources[key] is Media.SolidColorBrush brush &&
            Media.ColorConverter.ConvertFromString(light ? lightColor : darkColor) is Media.Color color)
        {
            brush.Color = color;
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

    private string LocalizeProfileName(OptimizerProfile profile) => profile switch
    {
        OptimizerProfile.Conservative => T("Light", "轻量"),
        OptimizerProfile.Balanced => T("Standard", "标准"),
        OptimizerProfile.Aggressive => T("Extreme Performance", "极致性能"),
        _ => T("Light", "轻量")
    };

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
            Foreground = CreateDialogBrush(242, 247, 255)
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
            Foreground = CreateDialogBrush(150, 168, 190)
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

        var freeCard = CreateEditionDetailsCard(EditionDetailsCatalog.Sections[0], CreateDialogBrush(61, 214, 163));
        Grid.SetColumn(freeCard, 0);
        sectionGrid.Children.Add(freeCard);

        var proCard = CreateEditionDetailsCard(EditionDetailsCatalog.Sections[1], CreateDialogBrush(255, 202, 73));
        Grid.SetColumn(proCard, 2);
        sectionGrid.Children.Add(proCard);

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
            Foreground = CreateDialogBrush(242, 247, 255)
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
            Foreground = CreateDialogBrush(176, 190, 207)
        };
        Grid.SetRow(subtitleTextBlock, 1);
        root.Children.Add(subtitleTextBlock);

        var panel = new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        panel.Children.Add(CreateProfileDetailsCard(
            T("Light", "轻量"),
            T("Gentlest cleanup. Best for daily office, browsing and gaming when you want low disturbance.", "最温和的清理。适合日常办公、浏览器和游戏场景，优先降低打扰。"),
            CreateDialogBrush(61, 214, 163)));
        panel.Children.Add(CreateProfileDetailsCard(
            T("Standard", "标准"),
            T("Balanced default. Cleans more when memory pressure rises, while keeping protected apps out of the target list.", "推荐默认档位。内存压力升高时清理更积极，同时避开受保护应用。"),
            CreateDialogBrush(123, 179, 255)));
        panel.Children.Add(CreateProfileDetailsCard(
            T("Extreme Performance", "极致性能"),
            T("Pro only. More aggressive trimming for heavy local AI, creator tools, games or streaming workloads.", "专业版专属。适合本地 AI、创作软件、游戏或直播等高负载场景，裁剪更积极。"),
            CreateDialogBrush(255, 202, 73)));
        Grid.SetRow(panel, 2);
        root.Children.Add(panel);

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
            Foreground = CreateDialogBrush(224, 234, 246)
        });

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(14),
            Background = CreateDialogBrush(15, 22, 31),
            BorderBrush = CreateDialogBrush(49, 70, 92),
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
            Background = CreateDialogBrush(40, 55, 73)
        });
        panel.Children.Add(new TextBlock
        {
            Text = T(section.BodyEnglish, section.BodyChinese),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            LineHeight = 21,
            Foreground = CreateDialogBrush(218, 229, 242)
        });

        return new Border
        {
            Padding = new Thickness(15),
            Background = CreateDialogBrush(15, 22, 31),
            BorderBrush = CreateDialogBrush(44, 63, 84),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = panel
        };
    }

    private static Media.Brush CreateDialogBrush(byte red, byte green, byte blue)
    {
        return new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
    }

    private string LocalizePolicyMessage(string message)
    {
        if (_uiLanguage is not (UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional))
        {
            return message;
        }

        return message
            .Replace("Memory pressure is low; purge skipped.", "内存压力较低，本轮跳过。", StringComparison.Ordinal)
            .Replace("Boost Now plan with", "Boost Now 计划，候选数：", StringComparison.Ordinal)
            .Replace("Purge plan ready with", "清理计划已生成，候选数：", StringComparison.Ordinal)
            .Replace("Extreme Performance bypassed threshold with", "极致性能策略已绕过阈值，候选数：", StringComparison.Ordinal)
            .Replace("No eligible process met safety criteria.", "没有满足安全条件的候选进程。", StringComparison.Ordinal);
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
            ? T("Hide Details", "收起详情")
            : T("Details", "详细设置");

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
