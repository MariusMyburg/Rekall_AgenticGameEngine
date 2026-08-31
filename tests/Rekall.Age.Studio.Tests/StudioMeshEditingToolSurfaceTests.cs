using System.IO;
using System.Xml.Linq;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioMeshEditingToolSurfaceTests
{
    [Fact]
    public void MeshEditingExposesNamedLoopCutToolThroughCanonicalOperationSelection()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Rekall.Age.Studio", "ModelingWorkspace.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var button = Assert.Single(document.Descendants(presentation + "Button"),
            element => (string?)element.Attribute("Content") == "Loop Cut");

        Assert.Equal("OnLoopCutToolClicked", (string?)button.Attribute("Click"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "ModelingWorkspace.xaml.cs"));
        Assert.Contains("MeshEditDomain = RekallAgeGeometryDomain.Edge", code, StringComparison.Ordinal);
        Assert.Contains("SelectedMeshOperationId = \"loop_cut_edges\"", code, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Rekall.Age.Studio"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
