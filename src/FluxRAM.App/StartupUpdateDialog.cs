using System.Windows;
using System.Windows.Controls;
using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace FluxRAM.App;

public static class StartupUpdateDialog
{
    public static UpdatePromptChoice Show(Window owner, UpdateCheckResult result, UiLanguage lang)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!StartupUpdatePolicy.ShouldPrompt(result, null))
        {
            return UpdatePromptChoice.Later;
        }

        var text = lang switch
        {
            UiLanguage.ChineseSimplified => (
                Title: "FluxRAM 更新",
                Message: $"发现 FluxRAM {result.LatestVersion}。\n当前版本：{result.CurrentVersion}",
                Update: "立即更新", Skip: "跳过此版本", Later: "稍后"),
            UiLanguage.ChineseTraditional => (
                Title: "FluxRAM 更新",
                Message: $"發現 FluxRAM {result.LatestVersion}。\n目前版本：{result.CurrentVersion}",
                Update: "立即更新", Skip: "略過此版本", Later: "稍後"),
            UiLanguage.Japanese => (
                Title: "FluxRAM の更新",
                Message: $"FluxRAM {result.LatestVersion} が利用可能です。\n現在のバージョン：{result.CurrentVersion}",
                Update: "今すぐ更新", Skip: "このバージョンをスキップ", Later: "後で"),
            UiLanguage.Korean => (
                Title: "FluxRAM 업데이트",
                Message: $"FluxRAM {result.LatestVersion} 업데이트가 있습니다.\n현재 버전: {result.CurrentVersion}",
                Update: "지금 업데이트", Skip: "이 버전 건너뛰기", Later: "나중에"),
            _ => (
                Title: "FluxRAM update",
                Message: $"FluxRAM {result.LatestVersion} is available.\nCurrent version: {result.CurrentVersion}",
                Update: "Update now", Skip: "Skip this version", Later: "Later")
        };
        var choice = UpdatePromptChoice.Later;
        var workArea = SystemParameters.WorkArea;
        var dialog = new Window
        {
            Owner = owner,
            Title = text.Title,
            Width = Math.Min(600, Math.Max(1, workArea.Width - 32)),
            MaxHeight = Math.Max(1, workArea.Height - 48),
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = owner.FontFamily,
            FontSize = 14
        };
        // Owned windows do not inherit the owner's local dynamic resources.
        dialog.Resources.MergedDictionaries.Add(owner.Resources);
        dialog.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");
        dialog.SetResourceReference(Window.ForegroundProperty, "TextPrimaryBrush");

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = text.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            }
        });

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        AddButton(text.Update, UpdatePromptChoice.UpdateNow, "PrimaryButtonStyle");
        AddButton(text.Skip, UpdatePromptChoice.SkipThisVersion, "QuietButtonStyle");
        var laterButton = AddButton(text.Later, UpdatePromptChoice.Later, "QuietButtonStyle");
        laterButton.IsDefault = true;
        laterButton.IsCancel = true;
        dialog.Loaded += (_, _) => laterButton.Focus();
        dialog.Content = root;
        dialog.ShowDialog();
        return choice;

        Button AddButton(string label, UpdatePromptChoice selectedChoice, string styleKey)
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = label,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(12, 8, 12, 8)
                },
                MinWidth = Math.Min(104, Math.Max(1, dialog.Width - 48)),
                MaxWidth = Math.Max(1, dialog.Width - 48),
                MinHeight = 36,
                Height = double.NaN,
                Margin = new Thickness(4, 4, 0, 0),
                Style = owner.TryFindResource(styleKey) as Style
            };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                choice = selectedChoice;
                dialog.Close();
            };
            buttons.Children.Add(button);
            return button;
        }
    }
}
