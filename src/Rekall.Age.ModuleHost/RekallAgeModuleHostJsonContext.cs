using System.Text.Json.Serialization;
using Rekall.Age.Modules.Hosting;

namespace Rekall.Age.ModuleHost;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(RekallAgeModuleHostEnvelope))]
[JsonSerializable(typeof(RekallAgeModuleHostLoadPlan))]
[JsonSerializable(typeof(RekallAgeModuleHostInitializeRequest))]
[JsonSerializable(typeof(RekallAgeModuleHostInitializeResponse))]
[JsonSerializable(typeof(RekallAgeModuleHostRuntimeUpdateRequest))]
[JsonSerializable(typeof(RekallAgeModuleHostRuntimeUpdateResponse))]
[JsonSerializable(typeof(RekallAgeModuleHostPlayableCreateRequest))]
[JsonSerializable(typeof(RekallAgeModuleHostPlayableCreateResponse))]
[JsonSerializable(typeof(RekallAgeModuleHostPlayableTickRequest))]
[JsonSerializable(typeof(RekallAgeModuleHostPlayableRenderResponse))]
internal sealed partial class RekallAgeModuleHostJsonContext : JsonSerializerContext;
