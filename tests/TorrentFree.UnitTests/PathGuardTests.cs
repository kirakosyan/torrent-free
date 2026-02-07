using TorrentFree.Services;
using Xunit;

namespace TorrentFree.UnitTests;

public sealed class PathGuardTests
{
    [Fact]
    public void IsPathWithinDirectory_ReturnsTrue_ForChildPath()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "torrentfree-tests", "downloads");
        var childPath = Path.Combine(basePath, "linux.iso");

        Assert.True(PathGuard.IsPathWithinDirectory(childPath, basePath));
    }

    [Fact]
    public void IsPathWithinDirectory_ReturnsFalse_ForSiblingWithSharedPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "torrentfree-tests");
        var basePath = Path.Combine(root, "downloads");
        var siblingPath = Path.Combine(root, "downloads2", "linux.iso");

        Assert.False(PathGuard.IsPathWithinDirectory(siblingPath, basePath));
    }

    [Fact]
    public void IsPathWithinDirectory_ReturnsFalse_ForTraversalOutsideBase()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "torrentfree-tests", "downloads");
        var traversalPath = Path.Combine(basePath, "..", "outside", "linux.iso");

        Assert.False(PathGuard.IsPathWithinDirectory(traversalPath, basePath));
    }
}
