using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluxRAM.App.Configuration;
using FluxRAM.App.Licensing;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class DesktopReadabilityTests
{
    [Fact]
    public async Task Dashboard_RendersPagesAndPreservesResultsWhenBoostIsSkipped()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            string? settingsFolder = null;
            try
            {
                var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                AssertGaugeRendering();
                window = new MainWindow(isUiPreview: true);
                var vm = (MainWindowViewModel)window.DataContext;
                var tabs = (TabControl)window.FindName("WorkspaceTabs");
                var root = (FrameworkElement)window.Content;
                var longPath = @"C:\Program Files\Example Application\" + new string('x', 150) + @"\application.exe";
                vm.UpdateMemorySnapshot(new MemorySnapshot(8UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 50));
                vm.UpdateBoostMetrics(300 * 1024 * 1024, 500 * 1024 * 1024, 160 * 1024 * 1024);
                vm.UpdateBoostDetails(Enumerable.Range(1, 20).Select(i => $"Application {i} | working set 180 MB -> 80 MB | trim 100 MB | low activity | {longPath}").ToArray());
                var trend = (MemoryTrend)window.FindName("MemoryTrendChart");
                for (var i = 0; i < 30; i++) trend.AddSample(DateTimeOffset.UtcNow.AddSeconds(-120 + 4 * i), i < 20 ? 65 : 50);

                foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                    foreach (var language in UiLanguageCatalog.Options.Select(option => option.Language))
                    {
                        Invoke(window, "ApplyTheme", theme, false);
                        Invoke(window, "ApplyLanguage", language, false);
                        Invoke(window, "SelectLanguage", language);
                        vm.UpdateProcessMetrics(140, 6, "Example.exe");
                        vm.SetStatus(language == UiLanguage.English ? "Ready" : "就绪");
                        vm.UpdateProtectedEntries(Enumerable.Range(1, 20).Select(i => $"Example {i}.exe | Path and child-process protection | {longPath}").ToArray());
                        for (var i = 0; i < 20; i++) vm.AddEvent($"Background scan {i} completed. No eligible candidates. | {longPath}");
                        for (var page = 0; page < tabs.Items.Count; page++)
                        {
                            tabs.SelectedIndex = page;
                            foreach (var size in new[] { new Size(1040, 690), new Size(820, 630), new Size(820, 480) })
                            {
                                Arrange(root, size);
                                AssertNavigation(tabs, language);
                                var content = (FrameworkElement)((TabItem)tabs.SelectedItem).Content;
                                AssertFits(content, tabs);
                                foreach (var viewer in Descendants<ScrollViewer>(root))
                                {
                                    Assert.Equal(0, viewer.ScrollableWidth);
                                    Assert.True(viewer.ViewportHeight > 0, $"{theme}/{language}/{page}/{size}: empty scroll viewport");
                                }
                                if (page == 0)
                                {
                                    var viewer = (ScrollViewer)window.FindName("DetailPanel");
                                    viewer.ScrollToTop();
                                    root.UpdateLayout();
                                    var boost = (FrameworkElement)window.FindName("BoostNowButton");
                                    Assert.True(boost.ActualWidth > 120);
                                    AssertFits(boost, viewer);
                                    var gauge = (MemoryUsageGauge)window.FindName("MemoryGauge");
                                    Assert.Equal(vm.MemoryLoadPercent, gauge.Value);
                                    Assert.Equal(vm.MemoryLoadValue, System.Windows.Automation.AutomationProperties.GetName(gauge));
                                    AssertFits(gauge, (FrameworkElement)window.FindName("MemoryOverviewPanel"));
                                }
                                foreach (var list in Descendants<ListBox>(content))
                                {
                                    var scroll = Descendants<ScrollViewer>(list).First();
                                    AssertFits(scroll, list);
                                    Assert.True(scroll.ScrollableHeight > 0, $"{list.Name}: long entries must scroll inside the list");
                                    scroll.ScrollToBottom();
                                    root.UpdateLayout();
                                    Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, precision: 1);
                                    scroll.ScrollToTop();
                                    root.UpdateLayout();
                                    Assert.Equal(0, scroll.VerticalOffset);
                                }
                                Capture(root, $"{theme}-{language}-{page}-{size.Width}x{size.Height}.png");
                            }
                        }

                        var details = vm.BoostDetails;
                        var modeButton = (Button)window.FindName("DetailSettingsButton");
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.Equal(0, tabs.SelectedIndex);
                        foreach (var name in new[] { "ProtectionTab", "ActivityTab", "SettingsTab", "ResultsRegion", "TrendRegion" })
                            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName(name)).Visibility);
                        Arrange(root, new Size(720, 480));
                        AssertNavigation(tabs, language);
                        AssertFits((FrameworkElement)((TabItem)tabs.SelectedItem).Content, tabs);
                        AssertFits((FrameworkElement)window.FindName("BoostNowButton"), (ScrollViewer)window.FindName("DetailPanel"));
                        Capture(root, $"{theme}-{language}-compact-720x480.png");
                        modeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        foreach (var name in new[] { "ProtectionTab", "ActivityTab", "SettingsTab", "ResultsRegion", "TrendRegion" })
                            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName(name)).Visibility);
                        Arrange(root, new Size(820, 630));
                        AssertNavigation(tabs, language);
                        Assert.Same(details, vm.BoostDetails);
                    }

                Invoke(window, "ApplyLanguage", UiLanguage.English, false);
                Invoke(window, "SelectLanguage", UiLanguage.English);
                vm.UpdateBoostDetails(["Keep the last completed Boost"]);
                var previousDetails = vm.BoostDetails;
                SetField(window, "_optimizerSettings", OptimizerSettings.SafeDefaults());
                SetField(window, "_lastBoostAt", DateTimeOffset.UtcNow.AddMinutes(-1));
                var previousBoostAt = GetField(window, "_lastBoostAt");
                var ran = Invoke(window, "RunBoostPass", false, "Auto Boost",
                    new MemorySnapshot(16UL * 1024 * 1024 * 1024, 16UL * 1024 * 1024 * 1024, 0),
                    Array.Empty<ProcessSnapshot>(), DateTimeOffset.UtcNow);
                Assert.Equal(false, ran);
                Assert.Same(previousDetails, vm.BoostDetails);
                Assert.Equal(previousBoostAt, GetField(window, "_lastBoostAt"));
                Assert.Equal("+300.0 MB", vm.LastBoostTrimmedValue);

                // Only preview windows and synthetic candidates are used; no purge is executed.
                tabs.SelectedIndex = 0;
                window.Show();
                var overview = (TabItem)tabs.Items[0];
                overview.Focus();
                window.UpdateLayout();
                AssertNavigation(tabs, UiLanguage.English);
                Capture(root, "sidebar-focused.png");
                Assert.True(overview.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down)));
                Assert.Equal(1, tabs.SelectedIndex);
                Assert.True(((TabItem)tabs.Items[1]).MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)));
                Assert.Equal(0, tabs.SelectedIndex);
                // Exercise native selector input without persisting test choices to user settings.
                foreach (var (name, page, guard) in new[] { ("ProfileSelector", 0, "_isSettingProfileSelector"), ("LanguageSelector", 3, "_isSettingLanguageSelector") })
                {
                    tabs.SelectedIndex = page;
                    window.UpdateLayout();
                    var selector = (ComboBox)window.FindName(name);
                    var selectedIndex = selector.SelectedIndex;
                    SetField(window, guard, true);
                    try { AssertSelectorKeyboardAndPopup(selector); }
                    finally
                    {
                        selector.IsDropDownOpen = false;
                        selector.SelectedIndex = selectedIndex;
                        SetField(window, guard, false);
                    }
                }
                tabs.SelectedIndex = 0;
                var result = new UpdateCheckResult(UpdateCheckState.UpdateAvailable, "v0.4.1", "v9.9.9", null, null, []);
                foreach (var (label, expected) in new[] { ("Update now", UpdatePromptChoice.UpdateNow), ("Skip this version", UpdatePromptChoice.SkipThisVersion), ("Later", UpdatePromptChoice.Later) })
                {
                    Exception? callbackError = null;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        var dialog = app.Windows.OfType<Window>().Single(w => w != window);
                        try
                        {
                            var button = Descendants<Button>(dialog).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == label);
                            Assert.True(button.ActualWidth > 60);
                            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception ex) { callbackError = ex; dialog.Close(); }
                    }));
                    Assert.Equal(expected, StartupUpdateDialog.Show(window, result, UiLanguage.English));
                    Assert.Null(callbackError);
                }

                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    app.Windows.OfType<Window>().Single(w => w != window).Close()));
                Assert.Equal(UpdatePromptChoice.Later, StartupUpdateDialog.Show(window, result, UiLanguage.English));

                settingsFolder = Path.Combine(Path.GetTempPath(), "FluxRAM.Tests", Guid.NewGuid().ToString("N"));
                var settings = new UserSettingsStore(Path.Combine(settingsFolder, "settings.json"));
                var responses = new UpdateResponses();
                using var http = new HttpClient(responses);
                ((AppUpdateChecker)GetField(window, "_updateChecker")!).Dispose();
                SetField(window, "_updateChecker", new AppUpdateChecker(http, currentVersion: "0.4.1"));
                SetField(window, "_userSettingsStore", settings);
                SetField(window, "_isUiPreview", false);
                SetField(window, "_hasHandledStartupUpdate", false);
                Choose("Later");
                CheckUpdates(true);
                Assert.Equal(1, responses.Count);
                Assert.Null(settings.LoadSkippedUpdateVersion());
                CheckUpdates(true);
                Assert.Equal(1, responses.Count);

                Choose("Skip this version");
                CheckUpdates(false);
                Assert.Equal("v9.9.9", settings.LoadSkippedUpdateVersion());
                SetField(window, "_hasHandledStartupUpdate", false);
                CheckUpdates(true);
                Assert.Equal(3, responses.Count);
                Choose("Later");
                CheckUpdates(false);
                Assert.Equal(4, responses.Count);
                completion.SetResult();

                void Choose(string label) => Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var dialog = app.Windows.OfType<Window>().Single(w => w != window);
                    Descendants<Button>(dialog).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == label)
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                void CheckUpdates(bool automatic)
                {
                    var task = (Task)Invoke(window, "CheckForUpdatesAsync", automatic)!;
                    if (!task.IsCompleted)
                    {
                        var frame = new DispatcherFrame();
                        var dispatcher = Dispatcher.CurrentDispatcher;
                        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                        Dispatcher.PushFrame(frame);
                    }
                    task.GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                if (window is not null) { SetField(window, "_isExitRequested", true); window.Close(); }
                System.Windows.Application.Current?.Shutdown();
                if (settingsFolder is not null && Directory.Exists(settingsFolder)) Directory.Delete(settingsFolder, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private sealed class UpdateResponses : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"tag_name":"v9.9.9","assets":[
                    {"name":"FluxRAM-Lite-Windows-x64.zip","browser_download_url":"https://gitcode.com/Midas927/FluxRAM/releases/download/v9.9.9/FluxRAM-Lite-Windows-x64.zip"},
                    {"name":"FluxRAM-Portable-Windows-x64.zip","browser_download_url":"https://gitcode.com/Midas927/FluxRAM/releases/download/v9.9.9/FluxRAM-Portable-Windows-x64.zip"}]}
                    """)
            });
        }
    }

    private static object? Invoke(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args);
    private static void SetField(object instance, string name, object value) =>
        instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);
    private static object? GetField(object instance, string name) =>
        instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Arrange(FrameworkElement root, Size size)
    {
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
    }

    private static void AssertGaugeRendering()
    {
        var gauge = new MemoryUsageGauge();
        foreach (var size in new[] { new Size(132, 126), new Size(16, 16), new Size(1, 1) })
        {
            var empty = Render(0);
            var half = Render(50);
            var full = Render(100);
            Assert.Equal(empty, Render(-20));
            Assert.Equal(full, Render(120));
            if (size.Width > 14)
            {
                Assert.Contains(empty, value => value != 0);
                Assert.False(empty.SequenceEqual(half), $"{size}: 50% must differ from 0%");
                Assert.False(half.SequenceEqual(full), $"{size}: 100% must differ from 50%");
            }

            byte[] Render(double value)
            {
                gauge.Value = value;
                Arrange(gauge, size);
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(gauge);
                var bounds = VisualTreeHelper.GetDrawing(gauge)?.Bounds ?? Rect.Empty;
                Assert.True(bounds.IsEmpty || new Rect(size).Contains(bounds), $"Gauge {value}/{size}: drawing exceeds element bounds: {bounds}");
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return pixels;
            }
        }
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var previous = gauge.Value;
            Assert.Throws<ArgumentException>(() => gauge.Value = value);
            Assert.Equal(previous, gauge.Value);
        }
        gauge.Value = 50d;
        var peer = UIElementAutomationPeer.CreatePeerForElement(gauge);
        Assert.NotNull(peer);
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer.GetPattern(PatternInterface.RangeValue));
        Assert.True(range.IsReadOnly);
        Assert.Equal(0d, range.Minimum);
        Assert.Equal(100d, range.Maximum);
        Assert.Equal(50d, range.Value);
        Assert.Throws<InvalidOperationException>(() => range.SetValue(75d));
        Assert.Equal(50d, range.Value);
    }

    private static void AssertSelectorKeyboardAndPopup(ComboBox selector)
    {
        Assert.False(selector.IsEditable);
        selector.SelectedIndex = 0;
        selector.Focus();
        PressKey(Key.F4);
        Assert.True(selector.IsDropDownOpen, $"{selector.Name}: F4 did not open the dropdown");
        var popup = Assert.IsType<Popup>(selector.Template.FindName("PART_Popup", selector));
        Assert.True(popup.IsOpen);
        var popupRoot = Assert.IsAssignableFrom<FrameworkElement>(popup.Child);
        Assert.True(popupRoot.ActualWidth >= selector.ActualWidth && popupRoot.ActualHeight > 0);
        foreach (var item in selector.Items.OfType<ComboBoxItem>().Where(item => item.Visibility == Visibility.Visible))
        {
            AssertFits(item, popupRoot);
            var label = Assert.Single(Descendants<TextBlock>(item).Where(text => text.Text == (string)item.Content));
            AssertFits(label, item);
        }
        Capture(popupRoot, $"{selector.Name}-popup.png");
        PressKey(Key.Down);
        PressKey(Key.Enter);
        Assert.False(selector.IsDropDownOpen);
        Assert.Equal(1, selector.SelectedIndex);
        PressKey(Key.F4);
        Assert.True(popup.IsOpen);
        PressKey(Key.Up);
        PressKey(Key.Escape);
        Assert.False(popup.IsOpen);
        Assert.Equal(1, selector.SelectedIndex);

        void PressKey(Key key)
        {
            selector.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(selector), 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            selector.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void AssertNavigation(TabControl tabs, UiLanguage language)
    {
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);
        var captions = new[] { ("Overview", "概览"), ("App protection", "应用保护"), ("Recent activity", "最近活动"), ("Settings", "设置") };
        Assert.Equal(captions.Length, tabs.Items.Count);
        var content = (FrameworkElement)((TabItem)tabs.SelectedItem).Content;
        var contentLeft = content.TransformToAncestor(tabs).Transform(new Point()).X;
        var previousBottom = 0d;
        for (var i = 0; i < tabs.Items.Count; i++)
        {
            var tab = (TabItem)tabs.Items[i];
            if (tab.Visibility != Visibility.Visible) continue;
            AssertFits(tab, tabs);
            var bounds = tab.TransformToAncestor(tabs).TransformBounds(new Rect(tab.RenderSize));
            Assert.True(bounds.Top >= previousBottom - 1, $"{language}/{tab.Name}: navigation items overlap");
            Assert.True(bounds.Right <= contentLeft + 1, $"{language}/{tab.Name}: navigation overlaps page content");
            previousBottom = bounds.Bottom;
            var caption = UiLanguageLocalizer.Localize(language, captions[i].Item1, captions[i].Item2);
            var label = Assert.Single(Descendants<TextBlock>(tab).Where(text => text.Text == caption));
            AssertFits(label, tab);
            Assert.True(label.DesiredSize.Height <= label.ActualHeight + 1, $"{language}/{tab.Name}: clipped navigation label");
            var iconText = Assert.IsType<string>(tab.Tag);
            Assert.False(string.IsNullOrWhiteSpace(iconText));
            var icon = Assert.Single(Descendants<TextBlock>(tab).Where(text => text.Text == iconText));
            AssertFits(icon, tab);
            var border = (Border)tab.Template.FindName("TabRoot", tab);
            AssertFits(border, tab);
            var borderBounds = border.TransformToAncestor(tab).TransformBounds(new Rect(border.RenderSize));
            var clip = VisualTreeHelper.GetClip(tab);
            Assert.True(clip is null || clip.Bounds.Contains(borderBounds),
                $"{language}/{tab.Name}: navigation border {borderBounds} is clipped to {clip?.Bounds}");
        }
    }

    private static void AssertFits(FrameworkElement element, FrameworkElement container)
    {
        var bounds = element.TransformToAncestor(container).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Width > 0 && bounds.Height > 0 &&
            bounds.Left >= -1 && bounds.Top >= -1 &&
            bounds.Right <= container.ActualWidth + 1 && bounds.Bottom <= container.ActualHeight + 1,
            $"{element.Name} ({element.GetType().Name}): {bounds} exceeds {container.Name} ({container.RenderSize})");
    }

    private static void Capture(FrameworkElement root, string name)
    {
        var folder = Environment.GetEnvironmentVariable("FLUXRAM_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth + root.Margin.Left + root.Margin.Right),
            (int)Math.Ceiling(root.ActualHeight + root.Margin.Top + root.Margin.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
