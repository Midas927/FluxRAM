using System.Xml.Linq;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class MainWindowLayoutContractTests
{
    private static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ProtectedAppsList_UsesBoundedNestedScrolling()
    {
        var document = LoadMainWindowXaml();
        var protectedAppsList = FindNamedElement(document, "ProtectedAppsListBox");
        var protectedAppsCard = FindNamedElement(document, "ProtectedAppsCard");

        Assert.Equal("150", (string?)protectedAppsList.Attribute("MaxHeight"));
        Assert.Equal("DetailListBox_OnPreviewMouseWheel", (string?)protectedAppsList.Attribute("PreviewMouseWheel"));

        var columnGrid = protectedAppsCard.Parent;
        var firstRow = columnGrid?
            .Element(PresentationNamespace + "Grid.RowDefinitions")?
            .Elements(PresentationNamespace + "RowDefinition")
            .First();
        Assert.Equal("Auto", (string?)firstRow?.Attribute("Height"));
    }

    [Fact]
    public void SelfOverheadMetric_UsesFullWidthWithoutTruncation()
    {
        var document = LoadMainWindowXaml();
        var selfOverhead = FindNamedElement(document, "SelfOverheadValueTextBlock");

        Assert.Equal("3", (string?)selfOverhead.Attribute("Grid.ColumnSpan"));
        Assert.Equal("Wrap", (string?)selfOverhead.Attribute("TextWrapping"));
        Assert.Equal("None", (string?)selfOverhead.Attribute("TextTrimming"));
    }

    [Fact]
    public void ProProtectionSummary_IsVisibleInsideTheProtectionCard()
    {
        var document = LoadMainWindowXaml();
        var summary = FindNamedElement(document, "ProProtectionSummaryTextBlock");

        Assert.Equal("Wrap", (string?)summary.Attribute("TextWrapping"));
        Assert.Equal("{Binding ProProtectionSummaryDisplay}", (string?)summary.Attribute("Text"));
    }

    private static XDocument LoadMainWindowXaml()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "FluxRAM.App", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return XDocument.Load(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate src/FluxRAM.App/MainWindow.xaml from the test output directory.");
    }

    private static XElement FindNamedElement(XDocument document, string name)
    {
        return document
            .Descendants()
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == name);
    }
}
