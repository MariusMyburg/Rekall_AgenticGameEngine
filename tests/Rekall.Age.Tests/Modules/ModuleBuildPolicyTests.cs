using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleBuildPolicyTests
{
    [Fact]
    public async Task CanonicalScaffoldedModulePassesBuildPolicy()
    {
        var (root, _, _) = await ScaffoldAsync("CanonicalModule");

        var result = await BuildAsync(root);

        Assert.True(result.Ok, result.Summary);
        Assert.Single(result.Value.Modules);
    }

    [Fact]
    public async Task CustomBuildTargetIsRejectedBeforeMarkerCanBeWritten()
    {
        var (root, projectPath, moduleDirectory) = await ScaffoldAsync("TargetInjection");
        var markerPath = Path.Combine(moduleDirectory, "injected.marker");
        await InjectProjectElementAsync(
            projectPath,
            $"<Target Name=\"Injected\" BeforeTargets=\"BeforeBuild\"><WriteLinesToFile File=\"{markerPath}\" Lines=\"executed\" /></Target>");

        var result = await BuildAsync(root);

        AssertPolicyRejected(result);
        Assert.False(File.Exists(markerPath));
    }

    [Theory]
    [InlineData("<UsingTask TaskName=\"Injected\" AssemblyFile=\"missing.dll\" />")]
    [InlineData("<Import Project=\"unexpected.targets\" />")]
    [InlineData("<ItemGroup><PackageReference Include=\"Example.Package\" Version=\"1.0.0\" /></ItemGroup>")]
    [InlineData("<ItemGroup><ProjectReference Include=\"..\\Other\\Other.csproj\" /></ItemGroup>")]
    public async Task NonCanonicalProjectElementsAreRejected(string projectElement)
    {
        var (root, projectPath, _) = await ScaffoldAsync("ProjectInjection");
        await InjectProjectElementAsync(projectPath, projectElement);

        var result = await BuildAsync(root);

        AssertPolicyRejected(result);
    }

    [Fact]
    public async Task NestedModuleProjectIsRejected()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("NestedModule");
        var nestedParent = Path.Combine(root, "Modules", "Nested");
        Directory.CreateDirectory(nestedParent);
        Directory.Move(moduleDirectory, Path.Combine(nestedParent, "NestedModule"));

        var result = await BuildAsync(root);

        AssertPolicyRejected(result);
    }

    [Fact]
    public async Task InjectedSourceLimitRejectsBeforeBuild()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("BoundedModule");
        await File.WriteAllTextAsync(Path.Combine(moduleDirectory, "Second.cs"), "namespace Bounded; public sealed class Second;");
        var policy = new RekallAgeModuleBuildPolicy(
            new RekallAgeModuleBuildPolicyLimits(MaximumSourcesPerModule: 1));

        var result = await BuildAsync(root, policy);

        AssertPolicyRejected(result);
    }

    [Fact]
    public async Task SimulatedSourceReparsePointIsRejectedBeforeBuild()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("ReparseModule");
        var sourcePath = Assert.Single(Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly));
        var policy = new RekallAgeModuleBuildPolicy(
            new RekallAgeModuleBuildPolicyLimits(),
            path => Path.GetFullPath(path).Equals(Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        var result = await BuildAsync(root, policy);

        AssertPolicyRejected(result);
    }

    [Fact]
    public async Task SimulatedOutputDescendantReparsePointIsRejectedBeforeReset()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("OutputReparseModule");
        var outputDirectory = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0");
        Directory.CreateDirectory(outputDirectory);
        var outputFile = Path.Combine(outputDirectory, "untrusted.dll");
        await File.WriteAllBytesAsync(outputFile, [1, 2, 3]);
        var policy = new RekallAgeModuleBuildPolicy(
            new RekallAgeModuleBuildPolicyLimits(),
            path => Path.GetFullPath(path).Equals(Path.GetFullPath(outputFile), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        var result = await BuildAsync(root, policy);

        AssertPolicyRejected(result);
        Assert.True(File.Exists(outputFile));
    }

    [Fact]
    public async Task DirectoryBuildTargetsAreDisabledForCanonicalProject()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("DirectoryTargetModule");
        var markerPath = Path.Combine(root, "directory-build.marker");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Directory.Build.targets"),
            $"<Project><Target Name=\"InjectedDirectoryTarget\" BeforeTargets=\"BeforeBuild\"><WriteLinesToFile File=\"{markerPath}\" Lines=\"executed\" /></Target></Project>");

        var result = await BuildAsync(root);

        Assert.True(result.Ok, result.Summary);
        Assert.False(File.Exists(markerPath));
        Assert.True(File.Exists(Path.Combine(moduleDirectory, "bin", "rekall", "net10.0", "DirectoryTargetModule.dll")));
    }

    [Fact]
    public async Task LegacyIntermediateSourcesAreExcludedFromCanonicalCompilation()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("LegacyIntermediateModule");
        var legacyIntermediate = Path.Combine(moduleDirectory, "obj", "Debug", "net10.0");
        Directory.CreateDirectory(legacyIntermediate);
        await File.WriteAllTextAsync(
            Path.Combine(legacyIntermediate, "StaleGenerated.cs"),
            "#error Stale generated sources must never enter canonical compilation");

        var result = await BuildAsync(root);

        Assert.True(result.Ok, result.Summary);
    }

    [Fact]
    public async Task SimulatedIntermediateDescendantReparsePointIsRejectedBeforeReset()
    {
        var (root, _, moduleDirectory) = await ScaffoldAsync("IntermediateReparseModule");
        var intermediateDirectory = Path.Combine(moduleDirectory, "obj", "rekall");
        Directory.CreateDirectory(intermediateDirectory);
        var intermediateFile = Path.Combine(intermediateDirectory, "untrusted.cache");
        await File.WriteAllBytesAsync(intermediateFile, [1, 2, 3]);
        var policy = new RekallAgeModuleBuildPolicy(
            new RekallAgeModuleBuildPolicyLimits(),
            path => Path.GetFullPath(path).Equals(Path.GetFullPath(intermediateFile), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        var result = await BuildAsync(root, policy);

        AssertPolicyRejected(result);
        Assert.True(File.Exists(intermediateFile));
    }

    private static void AssertPolicyRejected(RekallAgeCommandResult<BuildModulesResult> result)
    {
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_MODULE_BUILD_POLICY_REJECTED");
    }

    private static async Task<(string Root, string ProjectPath, string ModuleDirectory)> ScaffoldAsync(
        string moduleName)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = CreateContext("scaffold build policy");
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"test.{moduleName.ToLowerInvariant()}", moduleName, moduleName),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        return (root, scaffold.Value.ProjectPath, Path.GetDirectoryName(scaffold.Value.ProjectPath)!);
    }

    private static async Task InjectProjectElementAsync(string projectPath, string element)
    {
        var project = await File.ReadAllTextAsync(projectPath);
        project = project.Replace("</Project>", $"  {element}{Environment.NewLine}</Project>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectPath, project);
    }

    private static ValueTask<RekallAgeCommandResult<BuildModulesResult>> BuildAsync(
        string root,
        RekallAgeModuleBuildPolicy? policy = null)
    {
        var command = policy is null ? new BuildModulesCommand() : new BuildModulesCommand(policy);
        return command.ExecuteAsync(new BuildModulesRequest(root), CreateContext("build policy"));
    }

    private static RekallAgeCommandContext CreateContext(string name)
    {
        return new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin(name),
            CancellationToken.None);
    }
}
