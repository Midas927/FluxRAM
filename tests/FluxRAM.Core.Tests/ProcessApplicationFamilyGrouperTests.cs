using FluxRAM.Core.Models;
using FluxRAM.Core.Services;
using Xunit;

namespace FluxRAM.Core.Tests;

public sealed class ProcessApplicationFamilyGrouperTests
{
    [Fact]
    public void Group_CombinesDifferentExecutablesFromSameApplicationDirectory()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(1, "Marvis", 22L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\Marvis\Marvis.exe"),
            new ProcessSnapshot(2, "MarvisAgent", 26L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\Marvis\MarvisAgent.exe"),
            new ProcessSnapshot(3, "MarvisHost", 21L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\Marvis\MarvisHost.exe")
        };

        var family = Assert.Single(ProcessApplicationFamilyGrouper.Group(snapshots));

        Assert.Equal("Marvis", family.DisplayName);
        Assert.Equal(3, family.Processes.Count);
        Assert.Equal(@"C:\Apps\Marvis", family.ExecutableDirectory);
    }

    [Fact]
    public void Group_KeepsSameExecutableNameSeparateAcrossDirectories()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(10, "helper", 80L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\One\helper.exe"),
            new ProcessSnapshot(11, "helper", 80L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\Two\helper.exe")
        };

        var families = ProcessApplicationFamilyGrouper.Group(snapshots);

        Assert.Equal(2, families.Count);
    }

    [Fact]
    public void Group_UsesParentIdentityWhenChildPathCannotBeRead()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(20, "app", 100L * 1024 * 1024, false, ExecutablePath: @"C:\Apps\Product\app.exe"),
            new ProcessSnapshot(21, "worker", 40L * 1024 * 1024, false, ParentProcessId: 20)
        };

        var family = Assert.Single(ProcessApplicationFamilyGrouper.Group(snapshots));

        Assert.Equal(2, family.Processes.Count);
    }

    [Fact]
    public void Group_UsesRootProcessNameWhenWholeFamilyPathsAreUnavailable()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(30, "Marvis", 60L * 1024 * 1024, false),
            new ProcessSnapshot(31, "MarvisAgent", 40L * 1024 * 1024, false, ParentProcessId: 30),
            new ProcessSnapshot(32, "MarvisHost", 30L * 1024 * 1024, false, ParentProcessId: 30)
        };

        var family = Assert.Single(ProcessApplicationFamilyGrouper.Group(snapshots));

        Assert.Equal("Marvis", family.DisplayName);
        Assert.Equal(3, family.Processes.Count);
    }

    [Fact]
    public void Group_DoesNotMergeUnrelatedDescendantsOfSameLauncher()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(40, "Taskmgr", 80L * 1024 * 1024, false),
            new ProcessSnapshot(41, "Marvis", 60L * 1024 * 1024, false, ParentProcessId: 40),
            new ProcessSnapshot(42, "Weixin", 50L * 1024 * 1024, false, ParentProcessId: 40)
        };

        var families = ProcessApplicationFamilyGrouper.Group(snapshots);

        Assert.Equal(3, families.Count);
    }

    [Fact]
    public void Group_AssignsGenericRenderersToTheirDirectHostApplications()
    {
        var snapshots = new[]
        {
            new ProcessSnapshot(50, "ProductOne", 80L * 1024 * 1024, false),
            new ProcessSnapshot(51, "CefRendererProcess", 40L * 1024 * 1024, false, ParentProcessId: 50),
            new ProcessSnapshot(60, "ProductTwo", 70L * 1024 * 1024, false),
            new ProcessSnapshot(61, "CefRendererProcess", 30L * 1024 * 1024, false, ParentProcessId: 60)
        };

        var families = ProcessApplicationFamilyGrouper.Group(snapshots);

        Assert.Equal(2, families.Count);
        Assert.All(families, family => Assert.Equal(2, family.Processes.Count));
    }
}
