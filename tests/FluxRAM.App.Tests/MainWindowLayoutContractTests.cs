using System.Xml.Linq;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class MainWindowLayoutContractTests
{
    private static readonly XNamespace PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ProtectedAppsList_FillsItsOwnScrollablePage()
    {
        var document = LoadMainWindowXaml();
        var protectedAppsList = FindNamedElement(document, "ProtectedAppsListBox");
        var protectedAppsCard = FindNamedElement(document, "ProtectedAppsCard");

        Assert.Equal("DetailListBox_OnPreviewMouseWheel", (string?)protectedAppsList.Attribute("PreviewMouseWheel"));
        Assert.Equal("ProtectionTab", (string?)protectedAppsCard.Parent?.Attribute(XamlNamespace + "Name"));
        Assert.Contains(protectedAppsCard.Element(PresentationNamespace + "Grid.RowDefinitions")!.Elements(),
            row => (string?)row.Attribute("Height") == "*");
    }

    [Fact]
    public void SelfOverheadMetric_UsesFullWidthWithoutTruncation()
    {
        var document = LoadMainWindowXaml();
        var selfOverhead = FindNamedElement(document, "SelfOverheadValueTextBlock");

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

    [Fact]
    public void DeepRelease_IsAvailableOnOverviewAndNotDuplicatedInToolsMenu()
    {
        var document = LoadMainWindowXaml();
        var deepReleaseButton = FindNamedElement(document, "DeepReleaseButton");
        Assert.Equal("DeepReleaseButton_OnClick", (string?)deepReleaseButton.Attribute("Click"));
        Assert.Contains(deepReleaseButton.Ancestors(), element => (string?)element.Attribute(XamlNamespace + "Name") == "OverviewTab");
        Assert.DoesNotContain(document.Descendants(), element =>
            (string?)element.Attribute(XamlNamespace + "Name") == "ExtremeCloseMenuItem");
    }

    [Fact]
    public void ScrollBars_UseFortyPixelFixedThumbLength()
    {
        var document = LoadMainWindowXaml();
        var scrollBarStyle = document
            .Descendants(PresentationNamespace + "Style")
            .Single(element => (string?)element.Attribute("TargetType") == "ScrollBar");
        var track = scrollBarStyle
            .Descendants(PresentationNamespace + "Track")
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "PART_Track");
        var thumb = track
            .Descendants(PresentationNamespace + "Thumb")
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "ScrollThumb");

        Assert.Equal("NaN", (string?)track.Attribute("ViewportSize"));
        Assert.Equal("{TemplateBinding Orientation}", (string?)track.Attribute("Orientation"));
        Assert.Equal("16", (string?)thumb.Attribute("Width"));
        Assert.Equal("40", (string?)thumb.Attribute("Height"));

        var controlTemplate = scrollBarStyle
            .Descendants(PresentationNamespace + "ControlTemplate")
            .Single(element => (string?)element.Attribute("TargetType") == "ScrollBar");
        var horizontalTrigger = controlTemplate
            .Element(PresentationNamespace + "ControlTemplate.Triggers")?
            .Elements(PresentationNamespace + "Trigger")
            .Single(element =>
                (string?)element.Attribute("Property") == "Orientation" &&
                (string?)element.Attribute("Value") == "Horizontal");
        var horizontalSetters = horizontalTrigger?
            .Elements(PresentationNamespace + "Setter")
            .ToArray();

        Assert.Contains(horizontalSetters!, setter =>
            (string?)setter.Attribute("TargetName") == "PART_Track" &&
            (string?)setter.Attribute("Property") == "IsDirectionReversed" &&
            (string?)setter.Attribute("Value") == "False");
        Assert.Contains(horizontalSetters!, setter =>
            (string?)setter.Attribute("TargetName") == "ScrollThumb" &&
            (string?)setter.Attribute("Property") == "Width" &&
            (string?)setter.Attribute("Value") == "40");
        Assert.Contains(horizontalSetters!, setter =>
            (string?)setter.Attribute("TargetName") == "ScrollThumb" &&
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "16");
    }

    [Fact]
    public void ScrollBars_StretchAlongTheirScrollingAxis()
    {
        var document = LoadMainWindowXaml();
        var scrollBarStyle = document
            .Descendants(PresentationNamespace + "Style")
            .Single(element => (string?)element.Attribute("TargetType") == "ScrollBar");
        var baseSetters = scrollBarStyle
            .Elements(PresentationNamespace + "Setter")
            .ToArray();

        Assert.Contains(baseSetters, setter =>
            (string?)setter.Attribute("Property") == "Width" &&
            (string?)setter.Attribute("Value") == "16");
        Assert.DoesNotContain(baseSetters, setter =>
            (string?)setter.Attribute("Property") == "Height");

        var horizontalTrigger = scrollBarStyle
            .Element(PresentationNamespace + "Style.Triggers")?
            .Elements(PresentationNamespace + "Trigger")
            .Single(element =>
                (string?)element.Attribute("Property") == "Orientation" &&
                (string?)element.Attribute("Value") == "Horizontal");
        var horizontalSetters = horizontalTrigger?
            .Elements(PresentationNamespace + "Setter")
            .ToArray();

        Assert.Contains(horizontalSetters!, setter =>
            (string?)setter.Attribute("Property") == "Width" &&
            (string?)setter.Attribute("Value") == "Auto");
        Assert.Contains(horizontalSetters!, setter =>
            (string?)setter.Attribute("Property") == "Height" &&
            (string?)setter.Attribute("Value") == "16");
    }

    [Theory]
    [InlineData("ProtectedAppsListBox")]
    [InlineData("BoostDetailsListBox")]
    [InlineData("RecentEventsListBox")]
    public void DetailLists_OpenCompleteEntryDetails(string listBoxName)
    {
        var document = LoadMainWindowXaml();
        var listBox = FindNamedElement(document, listBoxName);

        Assert.Equal("DetailListBox_OnMouseDoubleClick", (string?)listBox.Attribute("MouseDoubleClick"));
        Assert.Equal("DetailListBox_OnKeyDown", (string?)listBox.Attribute("KeyDown"));
    }

    [Fact]
    public void DetailPanel_UsesControlledWheelScrolling()
    {
        var document = LoadMainWindowXaml();
        var detailPanel = FindNamedElement(document, "DetailPanel");

        Assert.Equal("DetailPanel_OnPreviewMouseWheel", (string?)detailPanel.Attribute("PreviewMouseWheel"));
        Assert.Null((string?)detailPanel.Attribute("MouseWheel"));
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
