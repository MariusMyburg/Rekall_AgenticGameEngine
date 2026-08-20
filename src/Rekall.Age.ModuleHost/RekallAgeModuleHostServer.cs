using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Rekall.Age.Core.Commands;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Hosting;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.ModuleHost;

public sealed class RekallAgeModuleHostServer
{
    public async Task<int> RunAsync(
        Stream input,
        Stream output,
        Stream error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var reader = new RekallAgeModuleHostFrameCodec();
        var writer = new RekallAgeModuleHostFrameCodec();
        RekallAgeModuleHostSession? catalog = null;

        while (true)
        {
            RekallAgeModuleHostEnvelope request;
            try
            {
                request = await reader.ReadAsync(input, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await WriteDiagnosticAsync(error, ex, cancellationToken);
                return 1;
            }
            if (request.Ok is not null)
            {
                throw new RekallAgeModuleHostException(
                    "REKALL_MODULE_HOST_PROTOCOL_INVALID",
                    "The worker input stream accepts requests only.");
            }

            if (request.Operation == RekallAgeModuleHostOperations.Initialize)
            {
                if (catalog is not null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_PROTOCOL_INVALID", "InvalidOperationException", "The module host is already initialized.", cancellationToken);
                    return 1;
                }

                var initialize = DeserializePayload(
                    request,
                    RekallAgeModuleHostJsonContext.Default.RekallAgeModuleHostInitializeRequest);
                try
                {
                    catalog = RekallAgeModuleHostSession.Load(initialize.LoadPlanPath);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WriteHostFailureAsync(writer, output, request, ex, cancellationToken);
                    return 1;
                }
                if (!await WriteSuccessAsync(
                    writer,
                    output,
                    request,
                    new RekallAgeModuleHostInitializeResponse(
                        RekallAgeModuleHostProtocol.Version,
                        RekallAgeModuleTrustPostures.WindowsAppContainerRestricted,
                        catalog.Systems.Select(item => new RekallAgeModuleHostSystemDescriptor(item.System.Id, item.System.Priority, item.ModuleId)).ToArray(),
                        catalog.ComponentSchemas,
                        catalog.Playable?.Kind),
                    cancellationToken))
                {
                    return 1;
                }
                continue;
            }

            if (catalog is null)
            {
                await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_PROTOCOL_INVALID", "InvalidOperationException", "The module host must be initialized first.", cancellationToken);
                return 1;
            }

            if (request.Operation == RekallAgeModuleHostOperations.Shutdown)
            {
                return await WriteSuccessAsync(writer, output, request, new { shutdown = true }, cancellationToken) ? 0 : 1;
            }


            if (request.Operation == RekallAgeModuleHostOperations.RuntimeUpdate)
            {
                var update = DeserializePayload(
                    request,
                    RekallAgeModuleHostJsonContext.Default.RekallAgeModuleHostRuntimeUpdateRequest);
                var system = catalog.Systems.SingleOrDefault(item =>
                    string.Equals(item.System.Id, update.SystemId, StringComparison.Ordinal));
                if (system is null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_OUTPUT_INVALID", "KeyNotFoundException", "The requested runtime system is not declared by this host.", cancellationToken);
                    return 1;
                }

                Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeWorld world;
                try
                {
                    world = await system.System.UpdateAsync(
                        update.World,
                        new RekallAgeRuntimeModuleFrameContext(
                            update.FrameIndex,
                            update.DeltaTime,
                            update.ElapsedTime,
                            cancellationToken)
                        {
                            Input = update.Input
                        });
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WriteModuleFailureAsync(writer, output, request, ex, system.ModuleId, cancellationToken);
                    return 1;
                }
                if (world is null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_OUTPUT_INVALID", "InvalidOperationException", "The runtime system returned no world.", cancellationToken);
                    return 1;
                }

                if (!await WriteSuccessAsync(
                    writer,
                    output,
                    request,
                    new RekallAgeModuleHostRuntimeUpdateResponse(world),
                    cancellationToken))
                {
                    return 1;
                }
                continue;
            }

            if (request.Operation == RekallAgeModuleHostOperations.PlayableCreate)
            {
                if (catalog.Playable is null || catalog.PlayableState is not null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_OUTPUT_INVALID", "InvalidOperationException", "A playable module is unavailable or already created.", cancellationToken);
                    return 1;
                }

                var create = DeserializePayload(
                    request,
                    RekallAgeModuleHostJsonContext.Default.RekallAgeModuleHostPlayableCreateRequest);
                try
                {
                    catalog.PlayableState = catalog.Playable.CreateInitialState(create.Context)
                        ?? throw new InvalidOperationException("Playable module returned no state.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WriteModuleFailureAsync(writer, output, request, ex, catalog.Playable.Kind, cancellationToken);
                    return 1;
                }
                if (!await WriteSuccessAsync(
                    writer,
                    output,
                    request,
                    new RekallAgeModuleHostPlayableCreateResponse(catalog.Playable.Kind),
                    cancellationToken))
                {
                    return 1;
                }
                continue;
            }

            if (request.Operation == RekallAgeModuleHostOperations.PlayableTick)
            {
                if (catalog.Playable is null || catalog.PlayableState is null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_OUTPUT_INVALID", "InvalidOperationException", "The playable module must be created before ticking.", cancellationToken);
                    return 1;
                }

                var tick = DeserializePayload(
                    request,
                    RekallAgeModuleHostJsonContext.Default.RekallAgeModuleHostPlayableTickRequest);
                try
                {
                    catalog.Playable.Tick(catalog.PlayableState, tick.Input);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WriteModuleFailureAsync(writer, output, request, ex, catalog.Playable.Kind, cancellationToken);
                    return 1;
                }
                if (!await WriteSuccessAsync(writer, output, request, new { ticked = true }, cancellationToken))
                {
                    return 1;
                }
                continue;
            }

            if (request.Operation == RekallAgeModuleHostOperations.PlayableRender)
            {
                if (catalog.Playable is null || catalog.PlayableState is null)
                {
                    await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_OUTPUT_INVALID", "InvalidOperationException", "The playable module must be created before rendering.", cancellationToken);
                    return 1;
                }

                RekallAgePlayableModuleFrame frame;
                try
                {
                    frame = catalog.Playable.Render(catalog.PlayableState)
                        ?? throw new InvalidOperationException("Playable module returned no frame.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    await WriteModuleFailureAsync(writer, output, request, ex, catalog.Playable.Kind, cancellationToken);
                    return 1;
                }
                if (!await WriteSuccessAsync(
                    writer,
                    output,
                    request,
                    new RekallAgeModuleHostPlayableRenderResponse(frame),
                    cancellationToken))
                {
                    return 1;
                }
                continue;
            }

            await WriteFailureAsync(writer, output, request, "REKALL_MODULE_HOST_PROTOCOL_INVALID", "InvalidOperationException", "The operation is not available in this worker build.", cancellationToken);
            return 1;
        }
    }

    private static T DeserializePayload<T>(
        RekallAgeModuleHostEnvelope request,
        JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return request.Payload.Deserialize(typeInfo)
                ?? throw new JsonException($"Payload '{typeof(T).Name}' is null.");
        }
        catch (JsonException ex)
        {
            throw new RekallAgeModuleHostException(
                "REKALL_MODULE_HOST_PROTOCOL_INVALID",
                $"Module-host payload could not be decoded as '{typeof(T).Name}'.",
                innerException: ex);
        }
    }

    private static ValueTask WriteFailureAsync(
        RekallAgeModuleHostFrameCodec writer,
        Stream output,
        RekallAgeModuleHostEnvelope request,
        string code,
        string type,
        string message,
        CancellationToken cancellationToken) => writer.WriteAsync(
            output,
            RekallAgeModuleHostEnvelope.Failure(
                request.Sequence,
                request.Operation,
                new RekallAgeModuleHostError(code, type, message)),
            cancellationToken);

    private static async ValueTask<bool> WriteSuccessAsync<T>(
        RekallAgeModuleHostFrameCodec writer,
        Stream output,
        RekallAgeModuleHostEnvelope request,
        T payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(
                output,
                RekallAgeModuleHostEnvelope.Success(request.Sequence, request.Operation, payload),
                cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or RekallAgeModuleHostException)
        {
            await WriteFailureAsync(
                writer,
                output,
                request,
                "REKALL_MODULE_HOST_OUTPUT_INVALID",
                Bound(ex.GetType().Name, 128),
                "Module output could not be encoded within the bounded protocol.",
                cancellationToken);
            return false;
        }
    }

    private static ValueTask WriteModuleFailureAsync(
        RekallAgeModuleHostFrameCodec writer,
        Stream output,
        RekallAgeModuleHostEnvelope request,
        Exception exception,
        string moduleId,
        CancellationToken cancellationToken) => WriteFailureAsync(
            writer,
            output,
            request,
            "REKALL_MODULE_HOST_MODULE_REJECTED",
            Bound(exception.GetType().Name, 128),
            Bound(exception.Message, 1024),
            cancellationToken,
            moduleId);

    private static ValueTask WriteHostFailureAsync(
        RekallAgeModuleHostFrameCodec writer,
        Stream output,
        RekallAgeModuleHostEnvelope request,
        Exception exception,
        CancellationToken cancellationToken) => WriteFailureAsync(
            writer,
            output,
            request,
            exception is RekallAgeCodedBoundaryException coded
                ? coded.Code
                : "REKALL_MODULE_HOST_MODULE_REJECTED",
            Bound(exception.GetType().Name, 128),
            Bound(exception.Message, 1024),
            cancellationToken,
            null);

    private static ValueTask WriteFailureAsync(
        RekallAgeModuleHostFrameCodec writer,
        Stream output,
        RekallAgeModuleHostEnvelope request,
        string code,
        string type,
        string message,
        CancellationToken cancellationToken,
        string? moduleId) => writer.WriteAsync(
            output,
            RekallAgeModuleHostEnvelope.Failure(
                request.Sequence,
                request.Operation,
                new RekallAgeModuleHostError(code, type, message, moduleId)),
            cancellationToken);

    private static string Bound(string? value, int maximumLength)
    {
        var safe = string.IsNullOrWhiteSpace(value)
            ? "Module operation failed."
            : value.Replace('\r', ' ').Replace('\n', ' ');
        return safe.Length <= maximumLength ? safe : safe[..maximumLength];
    }

    private static async ValueTask WriteDiagnosticAsync(
        Stream error,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var code = exception is RekallAgeCodedBoundaryException coded
            ? coded.Code
            : "REKALL_MODULE_HOST_CRASHED";
        var diagnostic = $"{code}: {Bound(exception.Message, 2048)}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetBytes(diagnostic);
        if (bytes.Length > RekallAgeModuleHostProtocol.MaximumStandardErrorBytes)
        {
            bytes = bytes[..RekallAgeModuleHostProtocol.MaximumStandardErrorBytes];
        }

        await error.WriteAsync(bytes, cancellationToken);
        await error.FlushAsync(cancellationToken);
    }

}
