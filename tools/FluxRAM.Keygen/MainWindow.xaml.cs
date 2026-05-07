using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;

namespace FluxRAM.Keygen;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PrivateKeyPathTextBox.Text = ResolveDefaultPrivateKeyPath();
        StatusTextBlock.Text = "Ready.";
    }

    private void BrowsePrivateKeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "RSA Private Key XML (*.xml)|*.xml|All Files (*.*)|*.*",
            Title = "Select FluxRAM private key"
        };

        if (File.Exists(PrivateKeyPathTextBox.Text))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(PrivateKeyPathTextBox.Text));
        }

        if (dialog.ShowDialog(this) == true)
        {
            PrivateKeyPathTextBox.Text = dialog.FileName;
        }
    }

    private void GenerateButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var privateKeyPath = PrivateKeyPathTextBox.Text.Trim();
            var machineId = MachineIdTextBox.Text.Trim();

            if (machineId.Length == 0)
            {
                SetStatus("Machine ID is required.", isError: true);
                return;
            }

            if (!File.Exists(privateKeyPath))
            {
                SetStatus("Private key file was not found.", isError: true);
                return;
            }

            var privateKeyXml = File.ReadAllText(privateKeyPath);
            var licenseKey = LicenseKeyGenerator.GenerateProKey(machineId, privateKeyXml);
            GeneratedKeyTextBox.Text = licenseKey;
            SetStatus("Pro key generated.");
        }
        catch (Exception exception)
        {
            SetStatus($"Failed to generate key: {exception.Message}", isError: true);
        }
    }

    private void CopyKeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(GeneratedKeyTextBox.Text))
        {
            SetStatus("Generate a key first.", isError: true);
            return;
        }

        Clipboard.SetText(GeneratedKeyTextBox.Text);
        SetStatus("Pro key copied.");
    }

    private void ClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        MachineIdTextBox.Text = string.Empty;
        GeneratedKeyTextBox.Text = string.Empty;
        SetStatus("Cleared.");
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isError
            ? System.Windows.Media.Brushes.LightCoral
            : System.Windows.Media.Brushes.LightSteelBlue;
    }

    private static string ResolveDefaultPrivateKeyPath()
    {
        var currentDirectoryPath = Path.Combine(
            Environment.CurrentDirectory,
            ".secrets",
            "fluxram-license.private-key.xml");
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var executableDirectory = AppContext.BaseDirectory;
        var localPath = Path.Combine(executableDirectory, "fluxram-license.private-key.xml");
        if (File.Exists(localPath))
        {
            return localPath;
        }

        var repoPath = Path.GetFullPath(Path.Combine(
            executableDirectory,
            "..",
            "..",
            "..",
            "..",
            ".secrets",
            "fluxram-license.private-key.xml"));
        return repoPath;
    }
}
