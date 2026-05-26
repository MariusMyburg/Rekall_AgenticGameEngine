namespace Rekall.Age.Modules;

public sealed record RekallAgePlayableModuleInput(
    int VerticalAxis = 0,
    bool PrimaryAction = false,
    double DeltaSeconds = 1.0 / 60.0);
