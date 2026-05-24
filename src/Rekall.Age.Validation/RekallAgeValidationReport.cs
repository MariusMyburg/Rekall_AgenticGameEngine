namespace Rekall.Age.Validation;

public sealed record RekallAgeValidationReport(IReadOnlyList<RekallAgeValidationIssue> Issues)
{
    public string Status => Issues.Any(issue => issue.Severity == "blocking") ? "blocked" : "ok";

    public IReadOnlyList<string> BlockingMessages =>
        Issues
            .Where(issue => issue.Severity == "blocking")
            .Select(issue => issue.Message)
            .ToArray();
}
