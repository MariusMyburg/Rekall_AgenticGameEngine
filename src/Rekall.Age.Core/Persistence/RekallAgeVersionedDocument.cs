namespace Rekall.Age.Core.Persistence;

public sealed record RekallAgeVersionedDocument<T>(T Value, string Revision);
