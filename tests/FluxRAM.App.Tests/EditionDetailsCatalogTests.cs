using FluxRAM.App.Configuration;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class EditionDetailsCatalogTests
{
    [Fact]
    public void Sections_DescribeFreeAndProCapabilities()
    {
        Assert.Equal(2, EditionDetailsCatalog.Sections.Count);
        Assert.Contains(EditionDetailsCatalog.Sections, section =>
            section.TitleEnglish == "FluxRAM" &&
            section.BodyEnglish.Contains("Light / Standard", StringComparison.Ordinal));
        Assert.Contains(EditionDetailsCatalog.Sections, section =>
            section.TitleEnglish == "FluxRAM Pro" &&
            section.BodyEnglish.Contains("Exact EXE path protection", StringComparison.Ordinal));
    }

    [Fact]
    public void Sections_IncludeChineseFeatureCopy()
    {
        Assert.Equal("FluxRAM editions", EditionDetailsCatalog.DialogTitleEnglish);
        Assert.Equal("FluxRAM 版本功能", EditionDetailsCatalog.DialogTitleChinese);
        Assert.Contains(EditionDetailsCatalog.Sections, section =>
            section.TitleChinese.Contains("普通版", StringComparison.Ordinal) &&
            section.BodyChinese.Contains("轻量", StringComparison.Ordinal));
        Assert.Contains(EditionDetailsCatalog.Sections, section =>
            section.TitleChinese.Contains("专业版", StringComparison.Ordinal) &&
            section.BodyChinese.Contains("精确 EXE 路径保护", StringComparison.Ordinal));
    }
}
