using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FluxRAM.Core.Models;

namespace FluxRAM.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaxRecentEvents = 30;
    private const int MaxBoostDetailLines = 20;

    private UiLanguage _language = UiLanguage.English;
    private long _ramDeltaBytes;
    private ulong _availableRamBytes;
    private long _lastBoostTrimmedBytes;
    private long _totalTrimmedBytes;
    private long _boostNetGainBytes;
    private double _reboundRatePercent;
    private bool _isAutoBoostEnabled;
    private int _protectedAppCount;
    private bool _supportsProtectList = true;
    private string _statusMessage;
    private string _processSummaryDisplay;
    private string _foregroundProcessDisplay;
    private string _protectionSummaryDisplay;
    private string _proProtectionSummaryDisplay;
    private string _selfOverheadDisplay;
    private DateTimeOffset _lastUpdated;
    private IReadOnlyList<string> _recentEvents;
    private IReadOnlyList<string> _boostDetails;
    private IReadOnlyList<string> _protectedEntries;
    private AppOverheadSnapshot? _lastOverheadSnapshot;
    private ProcessProtectionSummary _proProtectionSummary;
    private bool _isProProtectionEnabled;

    public MainWindowViewModel()
    {
        _statusMessage = L("Standby.", "待命中。");
        _processSummaryDisplay = L("Processes: waiting for first scan", "进程：等待首次扫描");
        _foregroundProcessDisplay = L("Foreground: unknown", "前台：未知");
        _protectionSummaryDisplay = L("Protected apps: 0", "受保护应用：0");
        _proProtectionSummaryDisplay = string.Empty;
        _selfOverheadDisplay = L("App: pending", "自身：等待数据");
        _lastUpdated = DateTimeOffset.Now;
        _recentEvents = Array.Empty<string>();
        _boostDetails = Array.Empty<string>();
        _protectedEntries = Array.Empty<string>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RamDeltaDisplay
    {
        get
        {
            var sign = _ramDeltaBytes >= 0 ? "+" : string.Empty;
            return Metric("RAM Delta", "内存变化", $"{sign}{FormatBytes(_ramDeltaBytes)}");
        }
    }

    public string AvailableRamDisplay => Metric("Available RAM", "可用内存", FormatBytes(_availableRamBytes));

    public string LastBoostTrimmedDisplay
    {
        get
        {
            var sign = _lastBoostTrimmedBytes >= 0 ? "+" : string.Empty;
            return Metric("Last Boost Trimmed", "最近 Boost 裁剪量", $"{sign}{FormatBytes(_lastBoostTrimmedBytes)}");
        }
    }

    public string TotalTrimmedDisplay
    {
        get
        {
            var sign = _totalTrimmedBytes >= 0 ? "+" : string.Empty;
            return Metric("Total Trimmed", "累计裁剪量", $"{sign}{FormatBytes(_totalTrimmedBytes)}");
        }
    }

    public string BoostNetGainDisplay
    {
        get
        {
            var sign = _boostNetGainBytes >= 0 ? "+" : string.Empty;
            return Metric("Boost Net Gain", "Boost 净收益", $"{sign}{FormatBytes(_boostNetGainBytes)}");
        }
    }

    public string ReboundRateDisplay => Metric("Rebound Rate", "回弹率", $"{_reboundRatePercent:0.0}%");

    public string AutoBoostDisplay => _isAutoBoostEnabled
        ? L("Auto Boost: on, pressure-gated", "自动 Boost：开启，按内存压力触发")
        : L("Auto Boost: off", "自动 Boost：关闭");

    public string ProcessSummaryDisplay => _processSummaryDisplay;

    public string ForegroundProcessDisplay => _foregroundProcessDisplay;

    public string ProtectionSummaryDisplay => _protectionSummaryDisplay;

    public string ProProtectionSummaryDisplay => _proProtectionSummaryDisplay;

    public string SelfOverheadDisplay => _selfOverheadDisplay;

    public string LastUpdatedDisplay => Metric("Last update", "最后更新", $"{_lastUpdated:HH:mm:ss}");

    public string StatusMessage => _statusMessage;

    public IReadOnlyList<string> RecentEvents => _recentEvents;

    public IReadOnlyList<string> BoostDetails => _boostDetails;

    public IReadOnlyList<string> ProtectedEntries => _protectedEntries;

    public void SetLanguage(UiLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        RaisePropertyChanged(nameof(RamDeltaDisplay));
        RaisePropertyChanged(nameof(AvailableRamDisplay));
        RaisePropertyChanged(nameof(LastBoostTrimmedDisplay));
        RaisePropertyChanged(nameof(TotalTrimmedDisplay));
        RaisePropertyChanged(nameof(BoostNetGainDisplay));
        RaisePropertyChanged(nameof(ReboundRateDisplay));
        RaisePropertyChanged(nameof(AutoBoostDisplay));
        RefreshProtectionSummary();
        RefreshProProtectionSummary();
        RaisePropertyChanged(nameof(LastUpdatedDisplay));

        if (_lastOverheadSnapshot.HasValue)
        {
            UpdateSelfOverhead(_lastOverheadSnapshot.Value);
        }
    }

    public void UpdateRamDelta(long ramDeltaBytes)
    {
        _ramDeltaBytes = ramDeltaBytes;
        RaisePropertyChanged(nameof(RamDeltaDisplay));
    }

    public void UpdateAvailableMemory(ulong availableRamBytes)
    {
        _availableRamBytes = availableRamBytes;
        RaisePropertyChanged(nameof(AvailableRamDisplay));
    }

    public void UpdateBoostMetrics(
        long lastBoostTrimmedBytes,
        long totalTrimmedBytes,
        long boostNetGainBytes)
    {
        _lastBoostTrimmedBytes = lastBoostTrimmedBytes;
        _totalTrimmedBytes = totalTrimmedBytes;
        _boostNetGainBytes = boostNetGainBytes;
        RaisePropertyChanged(nameof(LastBoostTrimmedDisplay));
        RaisePropertyChanged(nameof(TotalTrimmedDisplay));
        RaisePropertyChanged(nameof(BoostNetGainDisplay));
    }

    public void UpdateReboundRate(double reboundRatePercent)
    {
        _reboundRatePercent = Math.Clamp(reboundRatePercent, 0d, 100d);
        RaisePropertyChanged(nameof(ReboundRateDisplay));
    }

    public void SetAutoBoost(bool isEnabled)
    {
        if (_isAutoBoostEnabled == isEnabled)
        {
            return;
        }

        _isAutoBoostEnabled = isEnabled;
        RaisePropertyChanged(nameof(AutoBoostDisplay));
    }

    public void UpdateProcessMetrics(int scannedProcessCount, int purgeCandidateCount, string foregroundProcessName)
    {
        _processSummaryDisplay = L(
            $"Processes: scanned {scannedProcessCount}, boost candidates {purgeCandidateCount}",
            $"进程：已扫描 {scannedProcessCount}，Boost 候选 {purgeCandidateCount}");
        _foregroundProcessDisplay = L(
            $"Foreground: {foregroundProcessName}",
            $"前台：{foregroundProcessName}");
        RaisePropertyChanged(nameof(ProcessSummaryDisplay));
        RaisePropertyChanged(nameof(ForegroundProcessDisplay));
    }

    public void UpdateProtectionSummary(int protectedAppCount, bool supportsProtectList)
    {
        _protectedAppCount = Math.Max(0, protectedAppCount);
        _supportsProtectList = supportsProtectList;
        RefreshProtectionSummary();
    }

    public void UpdateProtectedEntries(IReadOnlyList<string> entries)
    {
        _protectedEntries = entries.ToArray();
        RaisePropertyChanged(nameof(ProtectedEntries));
    }

    public void UpdateProProtectionSummary(ProcessProtectionSummary summary, bool isPro)
    {
        _proProtectionSummary = summary;
        _isProProtectionEnabled = isPro;
        RefreshProProtectionSummary();
    }

    public void UpdateSelfOverhead(AppOverheadSnapshot overheadSnapshot)
    {
        _lastOverheadSnapshot = overheadSnapshot;
        _selfOverheadDisplay = L(
            $"App: WS {FormatBytes(overheadSnapshot.WorkingSetBytes)} | " +
            $"CPU {overheadSnapshot.CpuUsagePercent:0.0}% | " +
            $"Private {FormatBytes(overheadSnapshot.PrivateBytes)} | " +
            $"Handles {overheadSnapshot.HandleCount}",
            $"自身：工作集 {FormatBytes(overheadSnapshot.WorkingSetBytes)} | " +
            $"CPU {overheadSnapshot.CpuUsagePercent:0.0}% | " +
            $"私有内存 {FormatBytes(overheadSnapshot.PrivateBytes)} | " +
            $"句柄 {overheadSnapshot.HandleCount}");
        RaisePropertyChanged(nameof(SelfOverheadDisplay));
    }

    public void UpdateBoostDetails(IReadOnlyList<string> details)
    {
        _boostDetails = details.Take(MaxBoostDetailLines).ToArray();
        RaisePropertyChanged(nameof(BoostDetails));
    }

    public void AddEvent(string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var nextEvents = new List<string>(_recentEvents.Count + 1)
        {
            $"{timestamp}  {message}"
        };
        nextEvents.AddRange(_recentEvents);
        _recentEvents = nextEvents.Take(MaxRecentEvents).ToArray();
        RaisePropertyChanged(nameof(RecentEvents));
    }

    public void TouchLastUpdated(DateTimeOffset timestamp)
    {
        _lastUpdated = timestamp;
        RaisePropertyChanged(nameof(LastUpdatedDisplay));
    }

    public void SetStatus(string message)
    {
        _statusMessage = message;
        RaisePropertyChanged(nameof(StatusMessage));
    }

    private void RefreshProtectionSummary()
    {
        if (_supportsProtectList)
        {
            var label = UiLanguageLocalizer.LocalizeLabel(_language, "Protected apps", "受保护应用");
            var separator = _language is UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional or UiLanguage.Japanese
                ? "："
                : ": ";
            _protectionSummaryDisplay = $"{label}{separator}{_protectedAppCount}";
        }
        else
        {
            _protectionSummaryDisplay = L("Protected apps: Pro only", "受保护应用：专业版专属");
        }

        RaisePropertyChanged(nameof(ProtectionSummaryDisplay));
    }

    private void RefreshProProtectionSummary()
    {
        if (!_isProProtectionEnabled)
        {
            _proProtectionSummaryDisplay = string.Empty;
            RaisePropertyChanged(nameof(ProProtectionSummaryDisplay));
            return;
        }

        if (_proProtectionSummary.TotalCount == 0)
        {
            _proProtectionSummaryDisplay = L(
                "Pro Guard ready: exact path, child and related app protection.",
                "Pro 守护已就绪：精确路径、子进程与关联应用保护。");
            RaisePropertyChanged(nameof(ProProtectionSummaryDisplay));
            return;
        }

        var englishParts = new List<string>();
        var chineseParts = new List<string>();
        AddProtectionPart(englishParts, chineseParts, _proProtectionSummary.ProcessNameCount, "name", "名称");
        AddProtectionPart(englishParts, chineseParts, _proProtectionSummary.ExactPathCount, "exact path", "精确路径");
        AddProtectionPart(englishParts, chineseParts, _proProtectionSummary.ChildProcessCount, "child", "子进程");
        AddProtectionPart(englishParts, chineseParts, _proProtectionSummary.RelatedWindowCount, "related window", "关联窗口");
        _proProtectionSummaryDisplay = L(
            $"Pro Guard: protected {_proProtectionSummary.TotalCount} processes ({string.Join(", ", englishParts)}).",
            $"Pro 守护：已保护 {_proProtectionSummary.TotalCount} 个进程（{string.Join("、", chineseParts)}）。");
        RaisePropertyChanged(nameof(ProProtectionSummaryDisplay));
    }

    private static void AddProtectionPart(
        ICollection<string> englishParts,
        ICollection<string> chineseParts,
        int count,
        string englishLabel,
        string chineseLabel)
    {
        if (count <= 0)
        {
            return;
        }

        englishParts.Add($"{englishLabel} {count}");
        chineseParts.Add($"{chineseLabel} {count}");
    }

    public static string FormatBytes(long bytes)
    {
        var absoluteBytes = Math.Abs((double)bytes);
        if (absoluteBytes >= 1024d * 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d * 1024d):0.0} GB";
        }

        if (absoluteBytes >= 1024d * 1024d)
        {
            return $"{bytes / (1024d * 1024d):0.0} MB";
        }

        if (absoluteBytes >= 1024d)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        return $"{bytes} B";
    }

    public static string FormatBytes(ulong bytes)
    {
        if (bytes <= long.MaxValue)
        {
            return FormatBytes((long)bytes);
        }

        var asDouble = (double)bytes;
        if (asDouble >= 1024d * 1024d * 1024d)
        {
            return $"{asDouble / (1024d * 1024d * 1024d):0.0} GB";
        }

        return $"{asDouble / (1024d * 1024d):0.0} MB";
    }

    private string L(string english, string chinese)
    {
        return UiLanguageLocalizer.Localize(_language, english, chinese);
    }

    private string Metric(string englishLabel, string chineseLabel, string value)
    {
        var label = UiLanguageLocalizer.LocalizeLabel(_language, englishLabel, chineseLabel);
        return _language is UiLanguage.ChineseSimplified or UiLanguage.ChineseTraditional or UiLanguage.Japanese
            ? $"{label}：{value}"
            : $"{label}: {value}";
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
