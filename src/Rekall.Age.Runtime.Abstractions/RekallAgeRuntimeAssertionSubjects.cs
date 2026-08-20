namespace Rekall.Age.Runtime.Abstractions;

public static class RekallAgeRuntimeAssertionSubjects
{
    public static string Normalize(string? subject)
    {
        var normalized = subject?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "delta.transform.position2d.x" or "transform.delta.position2d.x" => "delta.position2d.x",
            "delta.transform.position2d.y" or "transform.delta.position2d.y" => "delta.position2d.y",
            "delta.transform.position3d.x" or "transform.delta.position3d.x" => "delta.position3d.x",
            "delta.transform.position3d.y" or "transform.delta.position3d.y" => "delta.position3d.y",
            "delta.transform.position3d.z" or "transform.delta.position3d.z" => "delta.position3d.z",
            _ => normalized
        };
    }
}
