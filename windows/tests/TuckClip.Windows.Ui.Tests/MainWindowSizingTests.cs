using Avalonia;
using TuckClip.Windows.Views;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class MainWindowSizingTests
{
    [TestMethod]
    public void PanelFitsWithinA1366By768WorkingAreaAt150PercentScaling()
    {
        var constraints = MainWindow.CalculateWindowConstraints(
            new PixelSize(1366, 728),
            scaling: 1.5,
            requestedWidth: 960,
            requestedHeight: 360);

        Assert.IsLessThanOrEqualTo((1366d / 1.5) - 24, constraints.Width);
        Assert.IsLessThanOrEqualTo((728d / 1.5) - 24, constraints.Height);
        Assert.AreEqual(640, constraints.MinimumWidth);
        Assert.AreEqual(360, constraints.Height);
    }

    [TestMethod]
    public void VerySmallWorkingAreaLowersDesignMinimumsWithoutExceedingBounds()
    {
        var constraints = MainWindow.CalculateWindowConstraints(
            new PixelSize(800, 480),
            scaling: 1.5,
            requestedWidth: 960,
            requestedHeight: 360);

        Assert.AreEqual(constraints.MaximumWidth, constraints.Width);
        Assert.AreEqual(constraints.MaximumHeight, constraints.Height);
        Assert.IsLessThan(640, constraints.MinimumWidth);
        Assert.IsLessThan(330, constraints.MinimumHeight);
    }
}
