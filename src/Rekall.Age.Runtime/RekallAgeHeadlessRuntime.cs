using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeHeadlessRuntime
{
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeProjectValidator? _validator;

    public RekallAgeHeadlessRuntime(RekallAgeSceneStore sceneStore, RekallAgeProjectValidator? validator = null)
    {
        _sceneStore = sceneStore;
        _validator = validator;
    }

    public async ValueTask<RekallAgeRuntimeResult> RunAsync(
        string projectRoot,
        string sceneName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);

        if (_validator is not null)
        {
            var report = await _validator.ValidateSceneAsync(projectRoot, sceneName, cancellationToken);
            if (report.Status == "blocked")
            {
                return new RekallAgeRuntimeResult(false, 0, TimeSpan.Zero, report.BlockingMessages);
            }
        }

        var frameTime = TimeSpan.FromSeconds(1.0 / 60.0);
        var frames = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / frameTime.TotalSeconds));

        for (var frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        return new RekallAgeRuntimeResult(true, frames, duration, Array.Empty<string>());
    }
}
