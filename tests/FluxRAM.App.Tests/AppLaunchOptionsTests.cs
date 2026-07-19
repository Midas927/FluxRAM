using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class AppLaunchOptionsTests
{
    [Theory]
    [InlineData(new[] { "--ui-preview" }, true)]
    [InlineData(new[] { "--UI-PREVIEW" }, true)]
    [InlineData(new[] { "--auto-boost" }, false)]
    public void IsUiPreview_RecognizesOnlyThePreviewFlag(string[] args, bool expected)
    {
        Assert.Equal(expected, AppLaunchOptions.IsUiPreview(args));
    }
}
