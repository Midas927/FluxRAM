using System.Windows;
using FluxRAM.App.Configuration;
using FluxRAM.App.Diagnostics;

namespace FluxRAM.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = AppUpdateCompletionService.TryParseArguments(e.Args, out var updateCompletionRequest);
        var isUiPreview = AppLaunchOptions.IsUiPreview(e.Args);

        var mainWindow = new MainWindow(isUiPreview);
        MainWindow = mainWindow;

        if (StartupAutoBoostService.WasLaunchedForAutoBoost(e.Args))
        {
            mainWindow.StartInTray();
        }
        else
        {
            mainWindow.Show();
        }

        if (updateCompletionRequest is not null)
        {
            _ = CompleteUpdateAsync(updateCompletionRequest);
        }
    }

    private static async Task CompleteUpdateAsync(AppUpdateCompletionRequest request)
    {
        try
        {
            await AppUpdateCompletionService.CompleteAsync(request);
            DiagnosticLog.Info("Previous FluxRAM executable and update cache were cleaned after restart.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warning("Update cleanup will be retried on the next update.", ex);
        }
    }
}
