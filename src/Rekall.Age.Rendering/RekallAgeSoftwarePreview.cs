using Rekall.Age.World;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeSoftwarePreview
{
    private const int Width = 160;
    private const int Height = 90;
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeSoftwarePreview(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeScreenshotResult> CaptureAsync(
        string projectRoot,
        string sceneName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        Directory.CreateDirectory(outputDirectory);

        var pixels = new byte[Width * Height * 4];
        var hash = Math.Abs(scene.Name.GetHashCode(StringComparison.Ordinal));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var index = (y * Width + x) * 4;
                var stripe = (x / 12 + y / 12 + hash) % 2 == 0;
                pixels[index + 0] = stripe ? (byte)30 : (byte)8;
                pixels[index + 1] = stripe ? (byte)120 : (byte)40;
                pixels[index + 2] = scene.Entities.Count > 0 ? (byte)220 : (byte)100;
                pixels[index + 3] = 255;
            }
        }

        DrawEntityMarkers(scene, pixels);

        var path = Path.Combine(outputDirectory, $"{scene.Name}_preview.png");
        await RekallAgePngWriter.WriteRgbaAsync(path, Width, Height, pixels, cancellationToken);
        var activeCamera = scene.Entities.FirstOrDefault(entity =>
            entity.Components.Any(component => component.Type is "Rekall.Camera2D" or "Rekall.Camera3D"))?.Name;

        return new RekallAgeScreenshotResult(
            path,
            pixels.Any(value => value != pixels[0]),
            Width,
            Height,
            scene.Entities.Count(entity => entity.Components.Count > 0),
            activeCamera);
    }

    private static void DrawEntityMarkers(RekallAgeSceneDocument scene, byte[] pixels)
    {
        for (var i = 0; i < scene.Entities.Count; i++)
        {
            var cx = 12 + (i * 23 % (Width - 24));
            var cy = 12 + (i * 17 % (Height - 24));
            for (var y = cy - 3; y <= cy + 3; y++)
            {
                for (var x = cx - 3; x <= cx + 3; x++)
                {
                    var index = (y * Width + x) * 4;
                    pixels[index + 0] = 240;
                    pixels[index + 1] = (byte)(80 + i * 17 % 120);
                    pixels[index + 2] = 40;
                    pixels[index + 3] = 255;
                }
            }
        }
    }
}
