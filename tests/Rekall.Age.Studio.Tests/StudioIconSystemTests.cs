using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Rekall.Age.Studio.Tests;

[Collection(WpfApplicationTestCollection.Name)]
public sealed class StudioIconSystemTests(WpfApplicationTestFixture wpf)
{
    [Fact]
    public void AppDefinesReusableThemeableVectorIconSystem()
    {
        var app = Source("App.xaml");

        Assert.Contains("x:Key=\"StudioIconPath\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconPlay\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconStop\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconSettings\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconLocalModel\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"IconCloud\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"StudioFilledIconPath\"", app, StringComparison.Ordinal);
        Assert.Contains("Property=\"Fill\" Value=\"Transparent\"", app, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeResourcesSeparateOutlineAndSilhouetteRenderingSemantics()
    {
        wpf.Invoke(() =>
        {
            var outline = Assert.IsType<Style>(Application.Current.Resources["StudioIconPath"]);
            var filled = Assert.IsType<Style>(Application.Current.Resources["StudioFilledIconPath"]);
            var outlineFill = outline.Setters.OfType<Setter>().Single(setter => setter.Property == System.Windows.Shapes.Shape.FillProperty);
            var filledStroke = filled.Setters.OfType<Setter>().Single(setter => setter.Property == System.Windows.Shapes.Shape.StrokeProperty);

            Assert.Equal(Brushes.Transparent, outlineFill.Value);
            Assert.Equal(Brushes.Transparent, filledStroke.Value);
            Assert.Same(outline, filled.BasedOn);
        });
    }

    [Theory]
    [InlineData("IconCreate", 3)]
    [InlineData("IconLocalModel", 5)]
    [InlineData("IconFileModel", 4)]
    [InlineData("IconAgent", 5)]
    public void CompoundOutlineIconsRetainIndependentDetailFigures(string resourceKey, int minimumFigures)
    {
        wpf.Invoke(() =>
        {
            var geometry = Assert.IsAssignableFrom<Geometry>(Application.Current.Resources[resourceKey]);
            var flattened = geometry.GetFlattenedPathGeometry();
            Assert.True(flattened.Figures.Count >= minimumFigures,
                $"{resourceKey} should keep its interior details as independent visible figures.");
        });
    }

    [Fact]
    public void WizardUsesVectorIconsWithoutReplacingAccessibleLabels()
    {
        var wizard = Source("LanguageModelSetupWindow.xaml");

        Assert.Contains("{StaticResource IconLocalModel}", wizard, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconCloud}", wizard, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconBack}", wizard, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconNext}", wizard, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconCheck}", wizard, StringComparison.Ordinal);
        Assert.Contains("Data=\"{StaticResource IconLocalModel}\" Style=\"{StaticResource StudioIconPath}\"", wizard, StringComparison.Ordinal);
        Assert.Contains("Data=\"{StaticResource IconCloud}\" Style=\"{StaticResource StudioFilledIconPath}\"", wizard, StringComparison.Ordinal);
        Assert.Contains("Text=\"Back\"", wizard, StringComparison.Ordinal);
        Assert.Contains("Text=\"Next\"", wizard, StringComparison.Ordinal);
        Assert.Contains("Text=\"Finish\"", wizard, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Finish language model setup\"", wizard, StringComparison.Ordinal);
    }

    [Fact]
    public void PrimaryStudioActionsUseIconsAndKeepTextTooltipsAndFlexibleSizing()
    {
        var main = Source("MainWindow.xaml");
        var author = Source("AuthorWorkspace.xaml");

        Assert.Contains("{StaticResource IconCreate}", main, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconOpen}", main, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconPlay}", main, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconStop}", main, StringComparison.Ordinal);
        Assert.Contains("Data=\"{StaticResource IconPlay}\" Style=\"{StaticResource StudioFilledIconPath}\"", main, StringComparison.Ordinal);
        Assert.Contains("Text=\"Simulate\"", main, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"Run the scene", main, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Simulate scene\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"↶\"", main, StringComparison.Ordinal);
        Assert.Contains("{StaticResource IconAgent}", author, StringComparison.Ordinal);
        Assert.Contains("Text=\"Run Agent\"", author, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"70\"", author, StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", fileName));
    }
}
