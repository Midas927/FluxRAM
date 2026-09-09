using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluxRAM.App.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace FluxRAM.App;

public sealed record ApplicationPreviewRow(string Display, bool IsEligible)
{
    public bool Matches(string? search, bool onlyEligible) => (!onlyEligible || IsEligible) &&
        (string.IsNullOrWhiteSpace(search) || Display.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
}

public sealed class ApplicationPreviewDialog : Window
{
    private readonly Action<IReadOnlyList<ApplicationPreviewRow>> _updateRows;
    public void UpdateRows(IReadOnlyList<ApplicationPreviewRow> rows) => _updateRows(rows);

    public ApplicationPreviewDialog(Window owner, UiLanguage language, IReadOnlyList<ApplicationPreviewRow> rows,
        Action<Window, string> showDetails, bool showEligibilityFilter = true)
    {
        string T(string english, string chinese) => UiLanguageLocalizer.Localize(language, english, chinese);
        Owner = owner;
        Title = T("Application preview", "应用预览");
        Width = Math.Min(820, SystemParameters.WorkArea.Width - 48);
        Height = Math.Min(560, SystemParameters.WorkArea.Height - 48);
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = owner.FontFamily;
        Resources.MergedDictionaries.Add(owner.Resources);
        SetResourceReference(BackgroundProperty, "SurfaceBrush");

        var search = new TextBox { Name = "ApplicationSearch", MinWidth = 180 };
        System.Windows.Automation.AutomationProperties.SetName(search, T("Search app or path", "搜索应用或路径"));
        var label = new TextBlock { Text = T("Search", "搜索"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var eligible = new CheckBox { Name = "EligibleOnly", Content = T("Eligible only", "仅符合条件"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        eligible.Visibility = showEligibilityFilter ? Visibility.Visible : Visibility.Collapsed;
        eligible.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");
        var toolbar = new DockPanel();
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(eligible, Dock.Right);
        toolbar.Children.Add(label);
        toolbar.Children.Add(eligible);
        toolbar.Children.Add(search);
        var list = new ListBox { Name = "ApplicationPreviewList", Margin = new Thickness(0, 14, 0, 0), ItemTemplate = (DataTemplate)owner.FindResource("ReadableEntryTemplate") };
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        count.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var details = new Button { Content = T("Details", "查看详情"), MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var close = new Button { Content = T("Close", "关闭"), IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        DockPanel.SetDock(details, Dock.Right);
        footer.Children.Add(close);
        footer.Children.Add(details);
        footer.Children.Add(count);
        var root = new DockPanel { Margin = new Thickness(18) };
        root.SetResourceReference(BackgroundProperty, "SurfaceBrush");
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(footer);
        root.Children.Add(list);
        Content = root;

        void Refresh()
        {
            var selected = list.SelectedItem;
            list.ItemsSource = rows.Where(row => row.Matches(search.Text, eligible.IsChecked == true)).Select(row => row.Display).ToArray();
            if (selected is not null && list.Items.Contains(selected)) list.SelectedItem = selected;
            else if (list.Items.Count > 0) list.SelectedIndex = 0;
            details.IsEnabled = list.Items.Count > 0;
            count.Text = T($"{list.Items.Count} / {rows.Count}", $"显示：{list.Items.Count} / {rows.Count}");
        }
        _updateRows = values => { rows = values; Refresh(); };
        void ShowSelected() { if (list.SelectedItem is string text) showDetails(this, text); }
        search.TextChanged += (_, _) => Refresh();
        eligible.Checked += (_, _) => Refresh();
        eligible.Unchecked += (_, _) => Refresh();
        details.Click += (_, _) => ShowSelected();
        close.Click += (_, _) => Close();
        list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem)
                ShowSelected();
        };
        list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ShowSelected(); e.Handled = true; } };
        Loaded += (_, _) => search.Focus();
        Refresh();
    }
}
