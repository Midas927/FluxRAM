using System.Windows;
using FluxRAM.App.Configuration;

namespace FluxRAM.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        if (StartupAutoBoostService.WasLaunchedForAutoBoost(e.Args))
        {
            mainWindow.StartInTray();
            return;
        }

        mainWindow.Show();
    }
}
