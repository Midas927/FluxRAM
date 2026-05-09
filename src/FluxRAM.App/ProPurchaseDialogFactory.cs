using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using Media = System.Windows.Media;

namespace FluxRAM.App;

internal static class ProPurchaseDialogFactory
{
    public static void ShowAlipayDialog(
        Window owner,
        UiLanguage language,
        string machineId,
        Action copyMachineId)
    {
        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(760d, Math.Max(680d, workArea.Width - 72d));
        var height = Math.Min(720d, Math.Max(540d, workArea.Height - 88d));
        var dialog = new Window
        {
            Owner = owner,
            Title = "升级 FluxRAM Pro",
            Width = width,
            Height = height,
            MinWidth = Math.Min(680d, width),
            MinHeight = Math.Min(540d, height),
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = UiFontFamily(language),
            Background = Brush(11, 16, 23)
        };

        dialog.Content = CreateContent(dialog, machineId, copyMachineId);
        dialog.ShowDialog();
    }

    private static UIElement CreateContent(Window dialog, string machineId, Action copyMachineId)
    {
        var root = new Grid
        {
            Margin = new Thickness(18)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleTextBlock = new TextBlock
        {
            Text = PurchaseOptionsCatalog.DomesticPriceText,
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(255, 202, 73)
        };
        Grid.SetRow(titleTextBlock, 0);
        root.Children.Add(titleTextBlock);

        var subtitleTextBlock = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = "付款后请备注或发送机器标识；我会根据当前电脑机器标识返回专属 Pro Key。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = Brush(224, 234, 246)
        };
        Grid.SetRow(subtitleTextBlock, 1);
        root.Children.Add(subtitleTextBlock);

        var machinePanel = CreateMachineIdPanel(machineId);
        Grid.SetRow(machinePanel, 2);
        root.Children.Add(machinePanel);

        var imagePanel = new StackPanel();
        imagePanel.Children.Add(CreateSectionLabel("支付宝收款码"));
        imagePanel.Children.Add(CreatePurchaseImage(PurchaseOptionsCatalog.AlipayQrImagePath, 220d, 220d));
        imagePanel.Children.Add(CreateSectionLabel("付款流程"));
        foreach (var imagePath in PurchaseOptionsCatalog.PaymentFlowImagePaths)
        {
            imagePanel.Children.Add(CreatePurchaseImage(imagePath, 560d, 380d));
        }

        var scrollViewer = new ScrollViewer
        {
            Margin = new Thickness(0, 14, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = imagePanel
        };
        Grid.SetRow(scrollViewer, 3);
        root.Children.Add(scrollViewer);

        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var copyButton = new System.Windows.Controls.Button
        {
            Width = 132,
            Height = 32,
            Content = "复制机器标识",
            Style = ownerStyle(dialog.Owner, "PrimaryButtonStyle")
        };
        copyButton.Click += (_, _) => copyMachineId();
        buttonPanel.Children.Add(copyButton);

        var closeButton = new System.Windows.Controls.Button
        {
            Width = 96,
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            Content = "关闭",
            Style = ownerStyle(dialog.Owner, "QuietButtonStyle")
        };
        closeButton.Click += (_, _) => dialog.Close();
        buttonPanel.Children.Add(closeButton);

        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        return root;
    }

    private static Border CreateMachineIdPanel(string machineId)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = "当前机器标识",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(176, 190, 207)
        };
        Grid.SetRow(label, 0);
        grid.Children.Add(label);

        var machineIdTextBox = new System.Windows.Controls.TextBox
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = machineId,
            IsReadOnly = true,
            FontFamily = new Media.FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brush(242, 247, 255),
            Background = Brush(9, 15, 22),
            BorderBrush = Brush(54, 74, 96),
            BorderThickness = new Thickness(1),
            CaretBrush = Brush(61, 214, 163)
        };
        Grid.SetRow(machineIdTextBox, 1);
        grid.Children.Add(machineIdTextBox);

        return new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(12),
            Background = Brush(15, 22, 31),
            BorderBrush = Brush(44, 63, 84),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = grid
        };
    }

    private static TextBlock CreateSectionLabel(string text)
    {
        return new TextBlock
        {
            Margin = new Thickness(0, 12, 0, 8),
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(242, 247, 255)
        };
    }

    private static System.Windows.Controls.Image CreatePurchaseImage(string resourcePath, double maxWidth, double maxHeight)
    {
        return new System.Windows.Controls.Image
        {
            Source = LoadResourceImage(resourcePath),
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            Stretch = Media.Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
    }

    private static BitmapImage LoadResourceImage(string resourcePath)
    {
        return new BitmapImage(new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute));
    }

    private static Style? ownerStyle(Window? owner, string resourceKey)
    {
        return owner?.TryFindResource(resourceKey) as Style;
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

    private static Media.SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        return new Media.SolidColorBrush(Media.Color.FromRgb(red, green, blue));
    }
}
