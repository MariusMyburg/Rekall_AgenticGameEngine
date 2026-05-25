using System.Diagnostics;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Cli;

public sealed class GeometryCliTests
{
    [Fact]
    public async Task GeometryPrimitiveCreateAddsRenderableEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Geometry CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "geometry",
            "primitive",
            "create",
            root,
            "Main",
            "Orb",
            "sphere",
            "1",
            "2",
            "3",
            "#33ff66");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Created sphere geometry primitive 'Orb'.", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Name == "Orb");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.Transform3D");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryPrimitive");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.MeshRenderer");
    }

    [Fact]
    public async Task GeometryMeshCreateAddsRenderableEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Geometry Mesh CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "geometry",
            "mesh",
            "create",
            root,
            "Main",
            "Triangle",
            """[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0,"r":0,"g":1,"b":0}]""",
            "[0,1,2]",
            "1",
            "2",
            "3",
            "#ff6633");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Created geometry mesh 'Triangle' with 3 vertices and 3 indices.", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Name == "Triangle");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.Transform3D");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryMesh");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.MeshRenderer");
    }

    [Fact]
    public async Task GeometryExtrusionCreateAddsRenderableEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Geometry Extrusion CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);
        var result = await RunAsync(
            FindCliAssemblyPath(),
            "geometry",
            "extrusion",
            "create",
            root,
            "Main",
            "Block",
            """[{"x":-0.5,"y":-0.5},{"x":0.5,"y":-0.5},{"x":0.5,"y":0.5},{"x":-0.5,"y":0.5}]""",
            "1",
            "0",
            "0",
            "0",
            "#44ccff");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Created geometry extrusion 'Block' with 24 vertices and 36 indices.", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Name == "Block");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryExtrusion");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryMesh");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.MeshRenderer");
    }

    [Fact]
    public async Task GeometryRecipeCreateAddsRenderableEntity()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Geometry Recipe CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);
        var recipePath = Path.Combine(root, "blockout.recipe.json");
        await File.WriteAllTextAsync(
            recipePath,
            """[{"kind":"ellipsoid","y":1,"scaleX":1,"scaleY":1.5,"scaleZ":0.5},{"kind":"capsule","x":0.8,"y":1,"roll":-70,"scaleX":0.2,"scaleY":1,"scaleZ":0.2,"color":"#44ccff"}]""");

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "geometry",
            "recipe",
            "create",
            root,
            "Main",
            "Blockout",
            $"@{recipePath}",
            "0",
            "0",
            "0",
            "#88aaff");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Created geometry recipe 'Blockout' with 2 parts", result.Output);
        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Name == "Blockout");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryRecipe");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.GeometryMesh");
        Assert.Contains(entity.Components, component => component.Type == "Rekall.MeshRenderer");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }

    private static string FindCliAssemblyPath()
    {
        var cliAssembly = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Rekall.Age.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Rekall.Age.Cli.dll");
        if (File.Exists(cliAssembly))
        {
            return cliAssembly;
        }

        throw new InvalidOperationException($"Could not find built CLI assembly at '{cliAssembly}'.");
    }
}
