using Rekall.Age.Modules;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Playback;

public sealed class RekallAgeModulePlayableGame : IRekallAgePlayableGame
{
    private readonly IRekallAgePlayableModule _module;
    private readonly RekallAgePlayableModuleState _state;
    private readonly RekallAgeInputActionSystem _inputActionSystem = new();
    private RekallAgeRuntimeWorld _inputWorld;

    private RekallAgeModulePlayableGame(
        IRekallAgePlayableModule module,
        RekallAgePlayableModuleState state,
        IReadOnlyList<string> entityNames,
        RekallAgeRuntimeWorld inputWorld)
    {
        _module = module;
        _state = state;
        EntityNames = entityNames;
        _inputWorld = inputWorld;
    }

    public string Kind => _module.Kind;

    public IReadOnlyList<string> EntityNames { get; }

    public static IRekallAgePlayableGame Create(string projectRoot, RekallAgeSceneDocument scene)
    {
        foreach (var assembly in RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot))
        {
            var moduleType = assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(IRekallAgePlayableModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (moduleType is null)
            {
                continue;
            }

            var module = (IRekallAgePlayableModule?)Activator.CreateInstance(moduleType)
                ?? throw new InvalidOperationException($"Playable module '{moduleType.FullName}' could not be created.");
            var entityNames = scene.Entities.Select(entity => entity.Name).ToArray();
            var state = module.CreateInitialState(new RekallAgePlayableModuleContext(scene.Name, entityNames));
            return new RekallAgeModulePlayableGame(
                module,
                state,
                entityNames,
                new RekallAgeRuntimeWorldBuilder().Build(scene));
        }

        throw new RekallAgePlayableModuleMissingException(projectRoot, scene.Name);
    }

    public void Tick(RekallAgeRuntimeInputFrame input)
    {
        var projection = _inputActionSystem.UpdateAsync(
            _inputWorld,
            new RekallAgeRuntimeWorldFrameContext(
                _inputWorld.FrameIndex + 1,
                TimeSpan.FromSeconds(Math.Max(0, input.DeltaSeconds)),
                _inputWorld.ElapsedTime + TimeSpan.FromSeconds(Math.Max(0, input.DeltaSeconds)),
                CancellationToken.None)
            {
                Input = input.ToState()
            });
        _inputWorld = projection.GetAwaiter().GetResult();
        _module.Tick(
            _state,
            new RekallAgePlayableModuleInput(
                input.VerticalAxis,
                input.PrimaryAction,
                input.DeltaSeconds,
                _inputWorld.Subsystems.Input.Actions));
    }

    public string RenderAscii()
    {
        return RenderFrame(0).Text;
    }

    public RekallAgePlaybackRenderFrame RenderFrame(int frameIndex)
    {
        var moduleFrame = _module.Render(_state);
        var drawCommands = (moduleFrame.DrawCommands ?? Array.Empty<RekallAgePlayableDrawCommand>())
            .Select(command => new RekallAgePlaybackDrawCommand(
                command.Kind,
                command.Id,
                command.X,
                command.Y,
                command.Width,
                command.Height,
                command.Fill,
                command.Text))
            .ToArray();
        return new RekallAgePlaybackRenderFrame(frameIndex, Kind, moduleFrame.Text + Environment.NewLine, drawCommands);
    }

    public void Dispose()
    {
        if (_module is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
