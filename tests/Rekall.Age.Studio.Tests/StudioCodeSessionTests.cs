using System.IO;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioCodeSessionTests
{
    [Fact]
    public async Task SessionLoadsTracksAndSavesOnlyEnumeratedProjectModuleSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-code-session-" + Guid.NewGuid().ToString("N"));
        try
        {
            var moduleRoot = Path.Combine(root, "Modules", "Movement");
            Directory.CreateDirectory(moduleRoot);
            var sourcePath = Path.Combine(moduleRoot, "MovementModule.cs");
            await File.WriteAllTextAsync(sourcePath, "public sealed class MovementModule { }");
            await File.WriteAllTextAsync(Path.Combine(moduleRoot, "Movement.csproj"), "<Project />");
            Directory.CreateDirectory(Path.Combine(moduleRoot, "obj"));
            await File.WriteAllTextAsync(Path.Combine(moduleRoot, "obj", "Generated.cs"), "generated");
            var outsidePath = Path.Combine(root, "Outside.cs");
            await File.WriteAllTextAsync(outsidePath, "outside");
            var session = new RekallAgeStudioCodeSession();

            var sources = await session.RefreshAsync(root, CancellationToken.None);
            await session.OpenAsync(Assert.Single(sources), CancellationToken.None);

            Assert.Equal(Path.GetFullPath(sourcePath), session.SelectedSource!.SourcePath);
            Assert.Equal("public sealed class MovementModule { }", session.SourceText);
            Assert.False(session.IsDirty);
            Assert.DoesNotContain(sources, source => source.SourcePath == outsidePath);

            session.SourceText = "public sealed class MovementModule { public bool Enabled => true; }";
            Assert.True(session.IsDirty);
            await session.SaveAsync(root, CancellationToken.None);

            Assert.False(session.IsDirty);
            Assert.Equal(session.SourceText, await File.ReadAllTextAsync(sourcePath));
            Assert.Equal("outside", await File.ReadAllTextAsync(outsidePath));
            Assert.EndsWith(Path.Combine("Modules", "Movement", "Movement.csproj"), session.SelectedProjectPath, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SessionRefusesToSaveASelectedSourceIntoAnotherProject()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "rekall-age-code-first-" + Guid.NewGuid().ToString("N"));
        var secondRoot = Path.Combine(Path.GetTempPath(), "rekall-age-code-second-" + Guid.NewGuid().ToString("N"));
        try
        {
            var moduleRoot = Path.Combine(firstRoot, "Modules", "Rules");
            Directory.CreateDirectory(moduleRoot);
            await File.WriteAllTextAsync(Path.Combine(moduleRoot, "Rules.cs"), "original");
            Directory.CreateDirectory(secondRoot);
            var session = new RekallAgeStudioCodeSession();
            await session.OpenAsync(Assert.Single(await session.RefreshAsync(firstRoot, CancellationToken.None)), CancellationToken.None);
            session.SourceText = "changed";

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SaveAsync(secondRoot, CancellationToken.None).AsTask());

            Assert.Contains("active project", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(moduleRoot, "Rules.cs")));
        }
        finally
        {
            if (Directory.Exists(firstRoot)) Directory.Delete(firstRoot, recursive: true);
            if (Directory.Exists(secondRoot)) Directory.Delete(secondRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SessionPreservesNestedSourceIdentityAndRefusesToOverwriteExternalChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-code-nested-" + Guid.NewGuid().ToString("N"));
        try
        {
            var moduleRoot = Path.Combine(root, "Modules", "Movement");
            var sourcePath = Path.Combine(moduleRoot, "Systems", "Mover.cs");
            var projectPath = Path.Combine(moduleRoot, "Movement.Runtime.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            await File.WriteAllTextAsync(sourcePath, "original");
            await File.WriteAllTextAsync(projectPath, "<Project />");
            var session = new RekallAgeStudioCodeSession();

            var source = Assert.Single(await session.RefreshAsync(root, CancellationToken.None));
            await session.OpenAsync(source, CancellationToken.None);

            Assert.Equal(Path.Combine("Systems", "Mover.cs"), source.FileName);
            Assert.Equal(Path.GetFullPath(projectPath), session.SelectedProjectPath);
            session.SourceText = "studio edit";
            await File.WriteAllTextAsync(sourcePath, "external edit");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SaveAsync(root, CancellationToken.None).AsTask());

            Assert.Contains("changed outside", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("external edit", await File.ReadAllTextAsync(sourcePath));
            Assert.True(session.IsDirty);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
