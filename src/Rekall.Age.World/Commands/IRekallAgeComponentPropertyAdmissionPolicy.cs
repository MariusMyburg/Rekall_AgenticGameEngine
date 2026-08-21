using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public interface IRekallAgeComponentPropertyAdmissionPolicy
{
    ValueTask<IReadOnlyList<RekallAgeCommandError>> ValidateAsync(
        string projectRoot,
        string componentType,
        JsonObject properties,
        string target,
        CancellationToken cancellationToken);
}
