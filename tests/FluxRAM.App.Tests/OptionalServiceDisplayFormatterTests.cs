using FluxRAM.App.Configuration;
using FluxRAM.App.ViewModels;
using FluxRAM.Core.Models;
using Xunit;

namespace FluxRAM.App.Tests;

public sealed class OptionalServiceDisplayFormatterTests
{
    [Theory]
    [InlineData(OptionalServiceKind.Application, OptionalServiceStopGuidance.WithApplication, "应用服务", "可随应用关闭")]
    [InlineData(OptionalServiceKind.System, OptionalServiceStopGuidance.WhenFeatureUnused, "系统服务", "不用该功能时可关")]
    [InlineData(OptionalServiceKind.System, OptionalServiceStopGuidance.KeepRunning, "系统服务", "建议保留")]
    public void Format_ChineseLabelsServiceTypeAndGuidance(
        OptionalServiceKind kind,
        OptionalServiceStopGuidance guidance,
        string expectedKind,
        string expectedGuidance)
    {
        var candidate = new OptionalServiceCandidate("ExampleSvc", "Example Service", 10, kind, guidance);

        var result = OptionalServiceDisplayFormatter.Format(candidate, UiLanguage.ChineseSimplified);

        Assert.Contains($"[{expectedKind}]", result.Line);
        Assert.Contains($"[{expectedGuidance}]", result.Line);
        Assert.Contains("ExampleSvc", result.Line);
        Assert.Contains(Environment.NewLine, result.Line);
        Assert.NotEmpty(result.ToolTip);
    }
}
