using Rekall.Age.ModuleHost;

return await new RekallAgeModuleHostServer().RunAsync(
    Console.OpenStandardInput(),
    Console.OpenStandardOutput(),
    Console.OpenStandardError(),
    CancellationToken.None);
