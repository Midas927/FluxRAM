using FluxRAM.App.ViewModels;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class UiLanguageCatalogTests
{
    [Fact]
    public void Options_ExposeFiveUserFacingLanguages()
    {
        var codes = UiLanguageCatalog.Options.Select(option => option.Code).ToArray();

        Assert.Equal(new[] { "en", "zh-CN", "zh-TW", "ja", "ko" }, codes);
    }

    [Theory]
    [InlineData("zh-CN", UiLanguage.ChineseSimplified)]
    [InlineData("zh-TW", UiLanguage.ChineseTraditional)]
    [InlineData("ja", UiLanguage.Japanese)]
    [InlineData("ko", UiLanguage.Korean)]
    [InlineData("unknown", UiLanguage.English)]
    public void FromCode_ParsesKnownCodesAndDefaultsToEnglish(string code, UiLanguage expected)
    {
        Assert.Equal(expected, UiLanguageCatalog.FromCode(code));
    }

    [Fact]
    public void Localize_ReturnsJapaneseTranslationWhenAvailable()
    {
        var text = UiLanguageLocalizer.Localize(
            UiLanguage.Japanese,
            "Auto Boost",
            "自动 Boost");

        Assert.Equal("自動 Boost", text);
    }
}
