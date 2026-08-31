using System.IO;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioIconSystemTests
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
        Assert.Contains("Property=\"Fill\" Value=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Control}}\"", app, StringComparison.Ordinal);
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
