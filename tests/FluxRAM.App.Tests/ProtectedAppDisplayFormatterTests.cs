using FluxRAM.App.Licensing;
using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class ProtectedAppDisplayFormatterTests
{
    [Fact]
    public void Format_BasicEditionLabelsProcessNameProtection()
    {
        var entries = ProtectedAppDisplayFormatter.Format(
            new[] { @"C:\Apps\Game\game.exe" },
            enableAdvancedProtection: false,
            UiLanguage.English);

        Assert.Single(entries);
        Assert.Contains("Basic name protection", entries[0].DisplayText);
        Assert.Equal(@"C:\Apps\Game\game.exe", entries[0].Path);
    }

    [Fact]
    public void Format_ProEditionLabelsAdvancedProtection()
    {
        var entries = ProtectedAppDisplayFormatter.Format(
            new[] { @"C:\Apps\Game\game.exe" },
            enableAdvancedProtection: true,
            UiLanguage.English);

        Assert.Single(entries);
        Assert.Contains("Pro advanced protection", entries[0].DisplayText);
        Assert.Contains("path + child + window", entries[0].DisplayText);
    }
}
