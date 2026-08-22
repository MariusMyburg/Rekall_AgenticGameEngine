using System.Globalization;
using System.Reflection;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules.Commands;

public sealed record InspectRuntimeSdkRequest(string Query, int Limit = 16);

public sealed record RekallAgeRuntimeSdkContract(
    string Category,
    string Name,
    string Signature,
    string Description)
{
    public string? Usage { get; init; }
}

public sealed record InspectRuntimeSdkResult(IReadOnlyList<RekallAgeRuntimeSdkContract> Contracts);

public sealed class InspectRuntimeSdkCommand
    : IRekallAgeCommand<InspectRuntimeSdkRequest, InspectRuntimeSdkResult>
{
    public string Name => "rekall.module.inspect_runtime_sdk";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Searches the compiled runtime-module SDK for exact C# signatures, usage patterns, immutable data contracts, and module source-topology rules. Query every needed concept together. Exact compact shape: {\"query\":\"input action vector entity component source topology\",\"limit\":24}.",
        typeof(InspectRuntimeSdkRequest).FullName!,
        typeof(InspectRuntimeSdkResult).FullName!);

    public ValueTask<RekallAgeCommandResult<InspectRuntimeSdkResult>> ExecuteAsync(
        InspectRuntimeSdkRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            var error = new RekallAgeCommandError(
                "REKALL_RUNTIME_SDK_QUERY_REQUIRED",
                "Runtime SDK inspection requires a non-empty query containing all needed authoring concepts.",
                Name);
            return ValueTask.FromResult(RekallAgeCommandResult<InspectRuntimeSdkResult>.Failure(
                new InspectRuntimeSdkResult([]),
                error.Message,
                [error]));
        }

        var terms = request.Query.Split(
            [' ', '.', '_', '-', '/'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contracts = CreateContracts()
            .Select(contract => new
            {
                Contract = contract,
                Score = terms.Sum(term => SearchText(contract).Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    + (terms.Any(term => contract.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ? 3 : 0)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Contract.Category, StringComparer.Ordinal)
            .ThenBy(item => item.Contract.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(request.Limit, 1, 64))
            .Select(item => item.Contract)
            .ToArray();

        return ValueTask.FromResult(RekallAgeCommandResult<InspectRuntimeSdkResult>.Success(
            new InspectRuntimeSdkResult(contracts),
            $"Found {contracts.Length} compiled runtime SDK contracts for '{request.Query}'."));
    }

    private static IReadOnlyList<RekallAgeRuntimeSdkContract> CreateContracts()
    {
        var methods = typeof(RekallAgeRuntimeModuleSdk)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => !method.IsSpecialName)
            .Select(method => new RekallAgeRuntimeSdkContract(
                "sdk-method",
                method.Name,
                FormatMethod(method),
                DescribeMethod(method.Name))
            {
                Usage = UsageFor(method.Name)
            });

        return methods.Concat(
        [
            new RekallAgeRuntimeSdkContract(
                "runtime-type",
                nameof(IRekallAgeRuntimeModuleSystem),
                "interface IRekallAgeRuntimeModuleSystem { string Id; int Priority; ValueTask<RekallAgeRuntimeWorld> UpdateAsync(RekallAgeRuntimeWorld world, RekallAgeRuntimeModuleFrameContext context); }",
                "Implement exactly one uniquely named runtime system class for each registered system type."),
            new RekallAgeRuntimeSdkContract(
                "runtime-type",
                nameof(RekallAgeRuntimeModuleFrameContext),
                "record RekallAgeRuntimeModuleFrameContext(int FrameIndex, TimeSpan DeltaTime, TimeSpan ElapsedTime, CancellationToken CancellationToken) { RekallAgeRuntimeInputState Input; }",
                "Frame facts are immutable. Semantic input actions are projected onto RekallAgeRuntimeWorld and consumed through world SDK helpers; do not invent context.InputActions."),
            new RekallAgeRuntimeSdkContract(
                "runtime-type",
                nameof(RekallAgeRuntimeGpuWorkload),
                "record RekallAgeRuntimeGpuWorkload(string Id) { Buffers; Textures; Samplers; Shaders; BindingLayouts; BindingSets; Pipelines; RenderTargets; Commands; }",
                "Immutable backend-neutral named GPU resource graph authored by a C# runtime module. Resources use stable string IDs; the engine validates and resolves them to opaque RenderingDevice handles."),
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "gpu-workload-authoring-recipe",
                "RekallAgeRuntimeGpuWorkload + RekallAgeRuntimeGpuCommand + world.WithGpuWorkload(workload)",
                "Construct typed bounded resources and one flat command stream, then consume the replacement world returned by WithGpuWorkload. Do not access Vulkan, Veldrid, WebGPU, pointers, or native handles; engine validation owns allocation and submission.")
            {
                Usage = "var workload = new RekallAgeRuntimeGpuWorkload(\"simulation\") { Shaders = [computeShader], Pipelines = [computePipeline], Commands = [new(RekallAgeRuntimeGpuCommandKind.BeginComputePass), new(RekallAgeRuntimeGpuCommandKind.SetComputePipeline) { Resource = \"pipeline\" }, new(RekallAgeRuntimeGpuCommandKind.Dispatch) { GroupCountX = 8 }, new(RekallAgeRuntimeGpuCommandKind.EndComputePass)] }; world = world.WithGpuWorkload(workload);"
            },
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "gpu-workload-frame-imports",
                "external sampled texture engine.scene-color; external render target engine.output",
                "The windowed Player imports its rendered scene color and final output framebuffer under stable resource IDs. Reference these IDs from ordinary binding sets and BeginRenderPass commands for fullscreen compositors; never redeclare them or assume native handles.")
            {
                Usage = "new RekallAgeRuntimeGpuBinding(0, \"engine.scene-color\"); new RekallAgeRuntimeGpuCommand(RekallAgeRuntimeGpuCommandKind.BeginRenderPass) { Resource = \"engine.output\" };"
            },
            new RekallAgeRuntimeSdkContract(
                "runtime-type",
                nameof(RekallAgeRuntimeVector2),
                "record RekallAgeRuntimeVector2(double X, double Y)",
                "An immutable planar vector used by Transform2D and Raycast2D. Compute scalar locals and construct a replacement vector; X and Y are not mutable fields.")
            {
                Usage = "var direction = new RekallAgeRuntimeVector2(horizontal, vertical);"
            },
            new RekallAgeRuntimeSdkContract(
                "runtime-type",
                nameof(RekallAgeRuntimeVector3),
                "record RekallAgeRuntimeVector3(double X, double Y, double Z)",
                "An immutable vector record. Compute scalar locals and construct a new vector; X, Y, and Z are not mutable fields.")
            {
                Usage = "var next = new RekallAgeRuntimeVector3(x, y, z);"
            },
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "entity-transform-and-component-state-recipe",
                "RekallAgeRuntimeEntity.Transform + WithPosition2D/WithRotation2D/WithScale2D + WithPosition3D/WithRotation3D/WithScale3D + typed component state",
                "Read transforms directly from the immutable entity and use the dimension-matched immutable transform helper: WithPosition2D for Rekall.Transform2D and WithPosition3D for Rekall.Transform3D. Use typed component-state helpers. These helpers require only Rekall.Age.Modules and Rekall.Age.Runtime.Abstractions; do not invent entity.Properties, entity.Transform3D, ReadVector3, ToMutable, or JsonObject boilerplate.")
            {
                Usage = "var position = entity.Transform.Position2D; entity = entity.WithPosition2D(new RekallAgeRuntimeVector2(nextX, nextY)); var speed = entity.ComponentNumber(componentType, \"movementSpeed\", 5); entity = entity.WithComponentBoolean(componentType, \"charged\", true); // no JsonObject required"
            },
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "scalar-two-axis-input-and-double-math-recipe",
                "InputActionValue returns double; RekallAgeRuntimeVector3 coordinates and ComponentNumber values are double",
                "InputActionValue returns double, not a vector: use two separately named semantic scalar actions for two-axis movement and never access .X or .Y on an action value. Keep transform, component-number, delta-time, distance, and movement locals as var or double; cast only at an explicit authored integer boundary.")
            {
                Usage = "var horizontal = world.InputActionValue(\"move.horizontal\"); var vertical = world.InputActionValue(\"move.vertical\"); var seconds = context.DeltaTime.TotalSeconds; var nextX = position.X + horizontal * speed * seconds; var nextZ = position.Z + vertical * speed * seconds;"
            },
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "semantic-input-map-recipe",
                "Rekall.InputActionMap.Actions -> InputActionValue/IsInputActionDown/WasInputActionPressed",
                "Runtime input helpers consume projected semantic actions; calling a helper does not create a binding. Attach the exact Rekall.InputActionMap component to a scene entity and define an Actions entry for every semantic name the module consumes. Use rekall.module.search_component_schemas for the exact authored component shape.")
            {
                Usage = "Author Rekall.InputActionMap Actions for move.horizontal, move.vertical, reset, or the module's own semantic names before runtime inspection."
            },
            new RekallAgeRuntimeSdkContract(
                "module-source",
                "agent-component-registration-recipe",
                "builder.RegisterComponent<TComponent>() for each agent-owned component contract",
                "Register every agent-owned component class that the scene attaches or a runtime system reads or writes. Declaring RekallAgeComponent and RekallAgeProperty attributes alone does not register the contract.")
            {
                Usage = "builder.RegisterComponent<PlayerState>(); builder.RegisterComponent<ProgressState>(); builder.RegisterComponent<CollectibleState>();"
            },
            new RekallAgeRuntimeSdkContract(
                "authoring-recipe",
                "immutable-world-lineage-recipe",
                "RekallAgeRuntimeWorld mutation results form one sequential immutable lineage",
                "Consume every mutation result and continue from the newest world. Never rebuild an already-mutated variable from a stale base. Entity-update callbacks return only replacement entities; perform additional world mutations sequentially outside callbacks so an enclosing update cannot overwrite them.")
            {
                Usage = "var updatedWorld = world.UpdateEntitiesWithComponent(type, entity => entity.WithComponentBoolean(type, \"active\", true)); updatedWorld = updatedWorld.UpdateEntity(id, entity => entity); return ValueTask.FromResult(updatedWorld);"
            },
            new RekallAgeRuntimeSdkContract(
                "module-source",
                "module-source-topology",
                "Modules/<ModuleName>/<ModuleName>.csproj compiles every Modules/<ModuleName>/*.cs file",
                "Call rekall.module.list_sources before scaffolding or rewriting. Every C# file in the module directory compiles into one assembly, so duplicate module, component, or system class definitions across files are build errors. rekall.module.scaffold_runtime_system refuses to overwrite an existing module: read and edit existing source instead of scaffolding it again.")
            {
                Usage = "rekall.module.list_sources -> rekall.module.read_source -> targeted rekall.module.write_source -> rekall.build.modules"
            },
            new RekallAgeRuntimeSdkContract(
                "module-source",
                nameof(RekallAgeComponent),
                "class AgentOwnedComponent : RekallAgeComponent",
                "Agent-owned component contracts inherit RekallAgeComponent and expose serializable properties marked with RekallAgeProperty."),
            new RekallAgeRuntimeSdkContract(
                "module-source",
                nameof(RekallAgeModuleBuilder.RegisterRuntimeSystem),
                "builder.RegisterRuntimeSystem<TSystem>() where TSystem : IRekallAgeRuntimeModuleSystem",
                "Register the exact unique system class that implements IRekallAgeRuntimeModuleSystem.")
        ]).ToArray();
    }

    private static string SearchText(RekallAgeRuntimeSdkContract contract) =>
        $"{contract.Category} {contract.Name} {contract.Signature} {contract.Description} {contract.Usage}";

    private static string FormatMethod(MethodInfo method)
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter =>
        {
            var value = $"{FriendlyType(parameter.ParameterType)} {parameter.Name}";
            return parameter.HasDefaultValue ? $"{value} = {FormatDefault(parameter.DefaultValue)}" : value;
        }));
        return $"{FriendlyType(method.ReturnType)} RekallAgeRuntimeModuleSdk.{method.Name}({parameters})";
    }

    private static string FriendlyType(Type type)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(double)) return "double";
        if (type == typeof(float)) return "float";
        if (type.IsArray) return $"{FriendlyType(type.GetElementType()!)}[]";
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FriendlyType))}>";
    }

    private static string FormatDefault(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool boolean => boolean ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"
    };

    private static string DescribeMethod(string name) => name switch
    {
        nameof(RekallAgeRuntimeModuleSdk.InputActionValue) or
        nameof(RekallAgeRuntimeModuleSdk.IsInputActionDown) or
        nameof(RekallAgeRuntimeModuleSdk.WasInputActionPressed) or
        nameof(RekallAgeRuntimeModuleSdk.WasInputActionReleased) =>
            "Reads semantic actions projected from Rekall.InputActionMap. Call this extension on world, not frame context.",
        nameof(RekallAgeRuntimeModuleSdk.InputControllers) or
        nameof(RekallAgeRuntimeModuleSdk.InputController) =>
            "Inspects connected physical gamepad or joystick state projected into the generic runtime input view. Prefer semantic InputActionMap bindings for gameplay behavior.",
        nameof(RekallAgeRuntimeModuleSdk.WithPosition2D) or
        nameof(RekallAgeRuntimeModuleSdk.WithRotation2D) or
        nameof(RekallAgeRuntimeModuleSdk.WithScale2D) =>
            "Returns a replacement immutable entity with the requested 2D transform value and updates Rekall.Transform2D. Use these for 2D scenes; they do not mutate Transform3D.",
        nameof(RekallAgeRuntimeModuleSdk.WithPosition3D) or
        nameof(RekallAgeRuntimeModuleSdk.WithRotation3D) or
        nameof(RekallAgeRuntimeModuleSdk.WithScale3D) =>
            "Returns a replacement immutable entity with the requested 3D transform value.",
        nameof(RekallAgeRuntimeModuleSdk.CreateEntity) =>
            "Creates a generic visible runtime entity with identity transforms and no components. Compose it with transform, tag, visibility, and typed component helpers before adding it to the world.",
        nameof(RekallAgeRuntimeModuleSdk.AddEntity) =>
            "Adds a generic runtime entity without manual world-list surgery. A duplicate stable id is rejected by returning the unchanged world.",
        nameof(RekallAgeRuntimeModuleSdk.DeterministicUnit) or
        nameof(RekallAgeRuntimeModuleSdk.DeterministicRange) =>
            "Returns stateless deterministic pseudo-random values from an authored seed and stable sequence index, suitable for replayable spawning and procedural variation.",
        nameof(RekallAgeRuntimeModuleSdk.GpuWorkloads) or
        nameof(RekallAgeRuntimeModuleSdk.WithGpuWorkload) or
        nameof(RekallAgeRuntimeModuleSdk.WithoutGpuWorkload) =>
            "Reads or returns a replacement immutable world containing bounded backend-neutral GPU workloads. Backends resolve authored resource IDs to opaque handles only after validation.",
        nameof(RekallAgeRuntimeModuleSdk.UpdateEntity) or
        nameof(RekallAgeRuntimeModuleSdk.ReplaceEntity) or
        nameof(RekallAgeRuntimeModuleSdk.RemoveEntity) or
        nameof(RekallAgeRuntimeModuleSdk.UpdateEntitiesWithTag) or
        nameof(RekallAgeRuntimeModuleSdk.UpdateEntitiesWithComponent) or
        nameof(RekallAgeRuntimeModuleSdk.UpdateEntitiesWithTagAndComponent) =>
            "Returns a replacement immutable world after a generic entity mutation. Continue later mutations from that replacement and perform world mutations outside entity callbacks.",
        nameof(RekallAgeRuntimeModuleSdk.UpdateComponent) or
        nameof(RekallAgeRuntimeModuleSdk.UpsertComponent) =>
            "Returns a replacement immutable entity after a JSON component mutation.",
        nameof(RekallAgeRuntimeModuleSdk.ComponentNumber) or
        nameof(RekallAgeRuntimeModuleSdk.ComponentBoolean) or
        nameof(RekallAgeRuntimeModuleSdk.ComponentString) =>
            "Reads one typed property from an entity component without direct JsonObject access.",
        nameof(RekallAgeRuntimeModuleSdk.WithComponentNumber) or
        nameof(RekallAgeRuntimeModuleSdk.WithComponentBoolean) or
        nameof(RekallAgeRuntimeModuleSdk.WithComponentString) =>
            "Returns a replacement immutable entity with one typed component property changed; no JsonObject namespace is required.",
        nameof(RekallAgeRuntimeModuleSdk.FindEntity) =>
            "Returns the exact-id match first, otherwise the single case-insensitive exact-name match. Returns null for no match or an ambiguous duplicate name; use EntitiesNamed when names may repeat.",
        nameof(RekallAgeRuntimeModuleSdk.EntitiesNamed) =>
            "Returns every case-insensitive exact-name match in stable entity-id order. This is not prefix matching: EntitiesNamed(\"EnergySeal\") does not match EnergySeal1. Use this for duplicate exact authored names; use EntitiesWithComponent, EntitiesWithTag, or EntitiesWithTagAndComponent for numbered or grouped objects. FindEntity is the single-result id-or-unique-name helper.",
        nameof(RekallAgeRuntimeModuleSdk.Raycast2D) =>
            "Returns stable distance-ordered hits against visible Rekall.BoxCollider2D and Rekall.CircleCollider2D entities, with optional tag and component filters.",
        nameof(RekallAgeRuntimeModuleSdk.Raycast3D) =>
            "Returns stable distance-ordered hits against visible 3D collider entities, with optional tag and component filters.",
        _ => "Compiled generic runtime-module SDK helper. The signature is derived from the loaded engine assembly."
    };

    private static string? UsageFor(string name) => name switch
    {
        nameof(RekallAgeRuntimeModuleSdk.CreateEntity) =>
            "var entity = RekallAgeRuntimeModuleSdk.CreateEntity(id, name).WithPosition3D(position).WithComponentNumber(\"Rekall.Rigidbody3D\", \"mass\", 1);",
        nameof(RekallAgeRuntimeModuleSdk.AddEntity) =>
            "world = world.AddEntity(entity); // unchanged when entity.Id already exists",
        nameof(RekallAgeRuntimeModuleSdk.DeterministicUnit) =>
            "var unit = RekallAgeRuntimeModuleSdk.DeterministicUnit(seed, spawnIndex);",
        nameof(RekallAgeRuntimeModuleSdk.DeterministicRange) =>
            "var yaw = RekallAgeRuntimeModuleSdk.DeterministicRange(seed, spawnIndex, -180, 180);",
        nameof(RekallAgeRuntimeModuleSdk.GpuWorkloads) =>
            "var workloads = world.GpuWorkloads(); // stable ordinal workload-id order",
        nameof(RekallAgeRuntimeModuleSdk.WithGpuWorkload) =>
            "world = world.WithGpuWorkload(workload); // add or replace by stable workload.Id",
        nameof(RekallAgeRuntimeModuleSdk.WithoutGpuWorkload) =>
            "world = world.WithoutGpuWorkload(\"simulation\");",
        nameof(RekallAgeRuntimeModuleSdk.InputActionValue) =>
            "var horizontal = world.InputActionValue(\"move.horizontal\");",
        nameof(RekallAgeRuntimeModuleSdk.IsInputActionDown) =>
            "var held = world.IsInputActionDown(\"agent.authored.action\");",
        nameof(RekallAgeRuntimeModuleSdk.WasInputActionPressed) =>
            "if (world.WasInputActionPressed(\"reset\")) { /* agent-authored rule */ }",
        nameof(RekallAgeRuntimeModuleSdk.InputControllers) =>
            "var gamepads = world.InputControllers(\"gamepad\");",
        nameof(RekallAgeRuntimeModuleSdk.InputController) =>
            "var device = world.InputController(\"sdl:12\");",
        nameof(RekallAgeRuntimeModuleSdk.WithPosition3D) =>
            "world = world.UpdateEntity(entity.Id, current => current.WithPosition3D(new RekallAgeRuntimeVector3(x, y, z)));",
        nameof(RekallAgeRuntimeModuleSdk.WithPosition2D) =>
            "world = world.UpdateEntity(entity.Id, current => current.WithPosition2D(new RekallAgeRuntimeVector2(x, y)));",
        nameof(RekallAgeRuntimeModuleSdk.WithRotation2D) =>
            "entity = entity.WithRotation2D(rotationDegrees);",
        nameof(RekallAgeRuntimeModuleSdk.WithScale2D) =>
            "entity = entity.WithScale2D(new RekallAgeRuntimeVector2(scaleX, scaleY));",
        nameof(RekallAgeRuntimeModuleSdk.FindEntity) =>
            "var player = world.FindEntity(\"Player\"); // exact id first, then one unique exact name; null when ambiguous",
        nameof(RekallAgeRuntimeModuleSdk.EntitiesNamed) =>
            "var doors = world.EntitiesNamed(\"Door\"); // exact name only; use EntitiesWithComponent/EntitiesWithTag for groups",
        nameof(RekallAgeRuntimeModuleSdk.UpdateComponent) =>
            "var replacement = entity.UpdateComponent(componentType, properties => { properties[\"score\"] = score; return properties; });",
        nameof(RekallAgeRuntimeModuleSdk.RemoveEntity) =>
            "world = world.RemoveEntity(collectedEntity.Id);",
        nameof(RekallAgeRuntimeModuleSdk.ComponentNumber) =>
            "var speed = entity.ComponentNumber(componentType, \"movementSpeed\", 5);",
        nameof(RekallAgeRuntimeModuleSdk.ComponentBoolean) =>
            "var charged = entity.ComponentBoolean(componentType, \"charged\", false);",
        nameof(RekallAgeRuntimeModuleSdk.ComponentString) =>
            "var state = entity.ComponentString(componentType, \"state\", \"idle\");",
        nameof(RekallAgeRuntimeModuleSdk.WithComponentNumber) =>
            "entity = entity.WithComponentNumber(componentType, \"score\", score);",
        nameof(RekallAgeRuntimeModuleSdk.WithComponentBoolean) =>
            "entity = entity.WithComponentBoolean(componentType, \"charged\", true);",
        nameof(RekallAgeRuntimeModuleSdk.WithComponentString) =>
            "entity = entity.WithComponentString(componentType, \"state\", \"complete\");",
        nameof(RekallAgeRuntimeModuleSdk.Raycast2D) =>
            "var hit = world.Raycast2D(origin, direction, range, tag: \"target\").FirstOrDefault();",
        nameof(RekallAgeRuntimeModuleSdk.Raycast3D) =>
            "var hit = world.Raycast3D(origin, direction, range, tag: \"target\").FirstOrDefault();",
        _ => null
    };
}
