using System.Text.Json;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Editor;

public sealed record RekallAgeWorkbenchOperationResult(
    bool Ok,
    string Summary,
    object? Value,
    IReadOnlyList<RekallAgeCommandError> Errors);

public sealed class RekallAgeWorkbenchSession
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RekallAgeCommandRegistry _registry;
    private readonly RekallAgeWorkbenchModelBuilder _modelBuilder;
    private readonly RekallAgeTransactionLogStore _transactionStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RekallAgeWorkbenchSession(RekallAgeCommandRegistry registry)
        : this(registry, new RekallAgeWorkbenchModelBuilder(), new RekallAgeTransactionLogStore())
    {
    }

    public RekallAgeWorkbenchSession(
        RekallAgeCommandRegistry registry,
        RekallAgeWorkbenchModelBuilder modelBuilder,
        RekallAgeTransactionLogStore transactionStore)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(transactionStore);
        _registry = registry;
        _modelBuilder = modelBuilder;
        _transactionStore = transactionStore;
    }

    public string? ProjectRoot { get; private set; }

    public string? SceneName { get; private set; }

    public string? SelectedEntityId { get; private set; }

    public RekallAgeWorkbenchModel? Model { get; private set; }

    public async ValueTask<RekallAgeWorkbenchOperationResult> CreateProjectAsync(
        string projectRoot,
        string projectName,
        string sceneName,
        IReadOnlyList<string> projectCapabilities,
        IReadOnlyList<string> sceneCapabilities,
        string actor,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fullRoot = Path.GetFullPath(projectRoot);
            var transaction = RekallAgeTransaction.Begin($"Create {projectName}");
            var context = new RekallAgeCommandContext(actor, transaction, cancellationToken);
            var project = await _registry.ExecuteJsonAsync(
                "rekall.project.create",
                JsonSerializer.Serialize(new
                {
                    projectRoot = fullRoot,
                    name = projectName,
                    capabilities = projectCapabilities
                }, JsonOptions),
                context).ConfigureAwait(false);
            if (!project.Ok)
            {
                return FromDynamic(project);
            }

            var scene = await _registry.ExecuteJsonAsync(
                "rekall.scene.create",
                JsonSerializer.Serialize(new
                {
                    projectRoot = fullRoot,
                    name = sceneName,
                    capabilities = sceneCapabilities
                }, JsonOptions),
                context).ConfigureAwait(false);
            if (!scene.Ok)
            {
                await PersistIfChangedAsync(fullRoot, transaction, actor, cancellationToken).ConfigureAwait(false);
                return FromDynamic(scene);
            }

            await PersistIfChangedAsync(fullRoot, transaction, actor, cancellationToken).ConfigureAwait(false);
            var model = await _modelBuilder.BuildAsync(fullRoot, sceneName, cancellationToken).ConfigureAwait(false);
            ProjectRoot = fullRoot;
            SceneName = sceneName;
            Model = model;
            SelectedEntityId = model.Inspector.SelectedEntityId;
            return new RekallAgeWorkbenchOperationResult(
                true,
                $"Created project '{projectName}' and scene '{sceneName}'.",
                model,
                []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeWorkbenchOperationResult> OpenAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await OpenCoreAsync(Path.GetFullPath(projectRoot), sceneName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeWorkbenchOperationResult> OpenSceneAsync(
        string sceneName,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ProjectRoot is null)
            {
                return Failure("REKALL_WORKBENCH_PROJECT_NOT_OPEN", "Open a project before selecting a scene.");
            }

            return await OpenCoreAsync(ProjectRoot, sceneName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeWorkbenchOperationResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ProjectRoot is null || SceneName is null)
            {
                return Failure("REKALL_WORKBENCH_PROJECT_NOT_OPEN", "Open a project before reloading the workbench.");
            }

            try
            {
                var model = await _modelBuilder.BuildAsync(
                    ProjectRoot,
                    SceneName,
                    cancellationToken,
                    SelectedEntityId).ConfigureAwait(false);
                Model = model;
                SelectedEntityId = model.Inspector.SelectedEntityId;
                return new RekallAgeWorkbenchOperationResult(true, $"Reloaded scene '{SceneName}'.", model, []);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            {
                return Failure("REKALL_WORKBENCH_RELOAD_FAILED", "The workbench could not reload the current project scene.", SceneName);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeWorkbenchOperationResult> SelectEntityAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ProjectRoot is null || SceneName is null || Model is null)
            {
                return Failure("REKALL_WORKBENCH_PROJECT_NOT_OPEN", "Open a project before selecting an entity.");
            }

            if (!Flatten(Model.Scene.RootEntities).Any(entity => entity.EntityId.Equals(entityId, StringComparison.Ordinal)))
            {
                return Failure("REKALL_WORKBENCH_ENTITY_NOT_FOUND", "The selected entity is not present in the active scene.", entityId);
            }

            var model = await _modelBuilder.BuildAsync(ProjectRoot, SceneName, cancellationToken, entityId).ConfigureAwait(false);
            Model = model;
            SelectedEntityId = model.Inspector.SelectedEntityId;
            return new RekallAgeWorkbenchOperationResult(true, $"Selected entity '{model.Inspector.SelectedEntityName}'.", model.Inspector, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RekallAgeWorkbenchOperationResult> ExecuteAsync(
        string commandName,
        string argumentsJson,
        string transactionName,
        string actor,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ProjectRoot is null || SceneName is null || Model is null)
            {
                return Failure("REKALL_WORKBENCH_PROJECT_NOT_OPEN", "Open a project before executing a workbench command.");
            }

            var transaction = RekallAgeTransaction.Begin(transactionName);
            var context = new RekallAgeCommandContext(actor, transaction, cancellationToken);
            var result = await _registry.ExecuteJsonAsync(commandName, argumentsJson, context).ConfigureAwait(false);
            if (!result.Ok)
            {
                return FromDynamic(result);
            }

            await PersistIfChangedAsync(ProjectRoot, transaction, actor, cancellationToken).ConfigureAwait(false);
            var model = await _modelBuilder.BuildAsync(
                ProjectRoot,
                SceneName,
                cancellationToken,
                SelectedEntityId).ConfigureAwait(false);
            Model = model;
            SelectedEntityId = model.Inspector.SelectedEntityId;
            return new RekallAgeWorkbenchOperationResult(true, result.Summary, result.Value, result.Errors);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<RekallAgeWorkbenchOperationResult> OpenCoreAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await _modelBuilder.BuildAsync(projectRoot, sceneName, cancellationToken).ConfigureAwait(false);
            ProjectRoot = projectRoot;
            SceneName = sceneName;
            Model = model;
            SelectedEntityId = model.Inspector.SelectedEntityId;
            return new RekallAgeWorkbenchOperationResult(true, $"Opened scene '{sceneName}'.", model, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            return Failure("REKALL_WORKBENCH_SCENE_OPEN_FAILED", "The requested project scene could not be opened.", sceneName);
        }
    }

    private async ValueTask PersistIfChangedAsync(
        string projectRoot,
        RekallAgeTransaction transaction,
        string actor,
        CancellationToken cancellationToken)
    {
        if (transaction.ChangedResources.Count > 0)
        {
            await _transactionStore.AppendAsync(projectRoot, transaction, actor, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<RekallAgeSceneEntityNode> Flatten(IEnumerable<RekallAgeSceneEntityNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children))
            {
                yield return child;
            }
        }
    }

    private static RekallAgeWorkbenchOperationResult FromDynamic(RekallAgeDynamicCommandResult result) =>
        new(result.Ok, result.Summary, result.Value, result.Errors);

    private static RekallAgeWorkbenchOperationResult Failure(string code, string message, string? target = null) =>
        new(false, message, null, [new RekallAgeCommandError(code, message, target)]);
}
