using Veldrid;

namespace Rekall.Age.Rendering.Windows;

internal static class RekallAgeVeldridBlendStates
{
    internal static BlendStateDescription SceneCoverage { get; } = new(
        RgbaFloat.White,
        [new BlendAttachmentDescription(
            blendEnabled: true,
            sourceColorFactor: BlendFactor.SourceAlpha,
            destinationColorFactor: BlendFactor.InverseSourceAlpha,
            colorFunction: BlendFunction.Add,
            sourceAlphaFactor: BlendFactor.One,
            destinationAlphaFactor: BlendFactor.InverseSourceAlpha,
            alphaFunction: BlendFunction.Add)]);

    internal static (string SourceColor, string DestinationColor, string SourceAlpha, string DestinationAlpha)
        DescribeSceneCoverage()
    {
        var attachment = SceneCoverage.AttachmentStates.Single();
        return (
            attachment.SourceColorFactor.ToString(),
            attachment.DestinationColorFactor.ToString(),
            attachment.SourceAlphaFactor.ToString(),
            attachment.DestinationAlphaFactor.ToString());
    }
}
