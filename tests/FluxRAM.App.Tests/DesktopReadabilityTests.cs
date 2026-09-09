using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
                    foreach (var language in new[] { UiLanguage.ChineseSimplified, UiLanguage.English })
                    {
                        Invoke(window, "ApplyTheme", theme, false);
                        Invoke(window, "ApplyLanguage", language, false);
                        Invoke(window, "SelectLanguage", language);
                        vm.UpdateProcessMetrics(140, 6, "Example.exe");
                        vm.SetStatus(language == UiLanguage.English ? "Ready" : "就绪");
                        vm.UpdateProtectedEntries(["Example.exe | Path and child-process protection | " + longPath]);
                        vm.AddEvent("Background scan completed. No eligible candidates.");
                        for (var page = 0; page < tabs.Items.Count; page++)
                        {
                            tabs.SelectedIndex = page;
                            root.Measure(new Size(820, 630));
                            root.Arrange(new Rect(0, 0, 820, 630));
                            root.UpdateLayout();
                            var viewer = Descendants<ScrollViewer>(root).FirstOrDefault(v => v.Name == "DetailPanel");
                            if (page == 0)
                            {
                                Assert.NotNull(viewer);
                                Assert.Equal(0, viewer.ScrollableWidth);
                                Assert.True(((FrameworkElement)window.FindName("BoostNowButton")).ActualWidth > 120);
                            }
                            foreach (var list in Descendants<ListBox>(root))
                            {
                                var scroll = Descendants<ScrollViewer>(list).FirstOrDefault();
                                if (scroll is not null) Assert.Equal(0, scroll.ScrollableWidth);
                            }
                            Capture(root, $"{theme}-{language}-{page}.png");
                        }
                    }

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
    private static void Capture(FrameworkElement root, string name)
    {
        var folder = Environment.GetEnvironmentVariable("FLUXRAM_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap(820, 630, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(folder, name));
        encoder.Save(stream);
    }
}
