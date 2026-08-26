using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Agent;

public sealed class OpenAiProjectAgentAcceptanceTests
{
    private const string Model = "gpt-5.6-sol";
    private const string FakeApiKey = "fake-openai-acceptance-key";
    private const string EngineStatusAlias = "rekall_context_engine_status_8179b61222fc";
    private const string SearchAlias = "rekall_tools_search_b2d44b5dab44";
    private const string GauntletAlias = "rekall_workflow_agent_authoring_gauntlet_68326d2159c2";
    private const string InspectSceneAlias = "rekall_runtime_inspect_scene_265e291310a4";

    [Fact]
    public async Task OpenAiResponsesRunsTheOrdinaryProjectAgentPathThroughStrictPostMutationGameplayProof()
    {
        var retainedEvidenceRoot = Environment.GetEnvironmentVariable("REKALL_OPENAI_ACCEPTANCE_EVIDENCE_ROOT");
        var retainEvidence = !string.IsNullOrWhiteSpace(retainedEvidenceRoot);
        var root = retainEvidence
            ? Path.GetFullPath(retainedEvidenceRoot!)
            : Path.Combine(
                Path.GetTempPath(),
                "rekall-age-openai-project-acceptance-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(root))
        {
            throw new InvalidOperationException("The retained OpenAI acceptance evidence root must not already exist.");
        }
        var output = Path.Combine(root, "Builds", "OpenAiAcceptance");
        var handler = new ScriptedResponsesHandler(root, output);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = FakeApiKey },
            () => new HttpClient(handler, disposeHandler: false));
        try
        {
            using var lease = catalog.Acquire("openai", RekallAgeDefaultCommandRegistry.Create());
            IRekallAgeProjectAgentRunner runner = lease.Runner;

            var result = await runner.RunAsync(
                new RekallAgeProjectAgentSessionRequest(
                    root,
                    "Main",
                    Model,
                    "Run the generic authoring gauntlet, then prove its semantic input transition with strict runtime assertions.")
                {
                    MaxTurns = 8,
                    Think = "medium",
                    RequireCompletionAudit = false
                },
                progress: null,
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Summary + Environment.NewLine +
                RekallAgeLanguageModelAgentDiagnostics.FormatFailures(result.AgentResult.ToolExecutions));
            Assert.True(result.AgentResult.Completed);
            Assert.Equal(Model, handler.RequestedModels.Distinct(StringComparer.Ordinal).Single());
            Assert.All(handler.StoreValues, store => Assert.False(store));
            Assert.DoesNotContain(FakeApiKey, string.Join('\n', handler.RequestBodies), StringComparison.Ordinal);
            Assert.Equal(
                [EngineStatusAlias, SearchAlias, GauntletAlias, SearchAlias, InspectSceneAlias],
                handler.ReturnedToolAliases);

            var executions = result.AgentResult.ToolExecutions;
            Assert.Equal(
                [
                    "rekall.context.engine_status",
                    "rekall.tools.search",
                    "rekall.workflow.agent_authoring_gauntlet",
                    "rekall.tools.search",
                    "rekall.runtime.inspect_scene"
                ],
                executions.Select(execution => execution.Name));
            Assert.All(executions, execution => Assert.True(execution.Succeeded, execution.ResultPreview));

            var gauntlet = executions.Single(execution => execution.Name == "rekall.workflow.agent_authoring_gauntlet");
            var runtimeProof = executions.Single(execution => execution.Name == "rekall.runtime.inspect_scene");
            Assert.True(runtimeProof.Sequence > gauntlet.Sequence);
            var semanticAction = runtimeProof.Arguments["inputs"]![0]!["semanticActions"]![0]!;
            Assert.Equal("agent.gauntlet.advance", semanticAction["name"]!.GetValue<string>());
            Assert.True(semanticAction["isDown"]!.GetValue<bool>());
            Assert.True(semanticAction["wasPressed"]!.GetValue<bool>());
            Assert.Equal(1, runtimeProof.Arguments["inputs"]![0]!["deltaSeconds"]!.GetValue<double>());
            Assert.Equal(3, runtimeProof.Arguments["assertions"]!.AsArray().Count);
            Assert.Contains(runtimeProof.Arguments["assertions"]!.AsArray(), assertion =>
                assertion!["subject"]!.GetValue<string>() == "delta.component.property"
                && assertion["componentType"]!.GetValue<string>() == "Game.Modules.AgentGauntlet.GauntletState"
                && assertion["propertyName"]!.GetValue<string>() == "progress"
                && assertion["expected"]!.GetValue<double>() == 1);
            Assert.Contains(runtimeProof.Arguments["assertions"]!.AsArray(), assertion =>
                assertion!["subject"]!.GetValue<string>() == "delta.position2d.x"
                && assertion["expected"]!.GetValue<double>() == 1);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var marker = Assert.Single(scene.Entities, entity => entity.Name == "Agent Authored Marker");
            var state = Assert.Single(marker.Components, component =>
                component.Type == "Game.Modules.AgentGauntlet.GauntletState");
            Assert.Equal(0, state.Properties["progress"]!.GetValue<double>());
            var inputMap = Assert.Single(marker.Components, component => component.Type == "Rekall.InputActionMap");
            Assert.Equal(
                "agent.gauntlet.advance",
                inputMap.Properties["actions"]![0]!["name"]!.GetValue<string>());

            var verification = await new InspectSceneRuntimeCommand().ExecuteAsync(
                CreateRuntimeProof(root),
                new Rekall.Age.Core.Commands.RekallAgeCommandContext(
                    "openai-acceptance-verification",
                    RekallAgeTransaction.Begin("verify OpenAI acceptance gameplay"),
                    CancellationToken.None));
            Assert.True(verification.Ok, verification.Summary + Environment.NewLine +
                string.Join(Environment.NewLine, verification.Errors.Select(error => $"{error.Code}: {error.Message}")));
            Assert.True(verification.Value.AssertionsPassed);
            Assert.All(verification.Value.AssertionResults, assertion =>
                Assert.True(assertion.Passed, assertion.Summary));
            var markerRuntime = Assert.Single(verification.Value.EntityStates, entity =>
                entity.EntityName == "Agent Authored Marker");
            Assert.Equal(1, markerRuntime.PositionDelta2D.X, precision: 6);
            Assert.Contains(verification.Value.SystemsRun, system =>
                system.Contains("AgentGauntletRuntimeSystem", StringComparison.Ordinal));

            var transactions = await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None);
            var latestSceneOrModuleMutation = transactions.Transactions
                .Where(transaction => transaction.ChangedResources.Any(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}Scenes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || path.Contains($"{Path.DirectorySeparatorChar}Modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(transaction => transaction.StartedAtUtc)
                .Last();
            Assert.Equal("rekall.workflow.agent_authoring_gauntlet", latestSceneOrModuleMutation.Name);
            Assert.True(latestSceneOrModuleMutation.StartedAtUtc <= File.GetLastWriteTimeUtc(
                Path.Combine(root, "Modules", "AgentGauntlet", "AgentGauntletModule.cs")));

            Assert.True(Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories).Any());
            var proofCaptures = Directory.EnumerateFiles(
                Path.Combine(root, "Builds", "AgentAuthoringGauntletAudit"),
                "*.png",
                SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(proofCaptures);
            Assert.All(proofCaptures, capture => Assert.True(new FileInfo(capture).Length > 0));
        }
        finally
        {
            handler.Dispose();
            if (!retainEvidence && Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static InspectSceneRuntimeRequest CreateRuntimeProof(string root) => new(
        root,
        "Main",
        Frames: 1,
        Inputs:
        [
            new RekallAgeRuntimeInputFrame(SemanticActions:
            [
                new RekallAgeRuntimeSemanticActionSample(
                    "agent.gauntlet.advance",
                    Value: 1,
                    IsDown: true,
                    WasPressed: true)
            ])
            {
                DeltaSeconds = 1
            }
        ],
        Assertions:
        [
            new InspectSceneRuntimeAssertion("Agent Authored Marker", "component", "exists")
            {
                ComponentType = "Game.Modules.AgentGauntlet.GauntletState"
            },
            new InspectSceneRuntimeAssertion("Agent Authored Marker", "delta.component.property", "equals")
            {
                ComponentType = "Game.Modules.AgentGauntlet.GauntletState",
                PropertyName = "progress",
                Expected = JsonValue.Create(1)
            },
            new InspectSceneRuntimeAssertion("Agent Authored Marker", "delta.position2d.x", "equals")
            {
                Expected = JsonValue.Create(1)
            }
        ]);

    private sealed class ScriptedResponsesHandler(string projectRoot, string outputDirectory)
        : HttpMessageHandler
    {
        private int _responseIndex;

        public List<string> RequestBodies { get; } = [];
        public List<string> RequestedModels { get; } = [];
        public List<bool> StoreValues { get; } = [];
        public List<string> ReturnedToolAliases { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/v1/responses", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", FakeApiKey), request.Headers.Authorization);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            var payload = JsonNode.Parse(body)!.AsObject();
            RequestedModels.Add(payload["model"]!.GetValue<string>());
            StoreValues.Add(payload["store"]!.GetValue<bool>());
            Assert.True(payload["stream"]!.GetValue<bool>());
            Assert.Equal("medium", payload["reasoning"]!["effort"]!.GetValue<string>());

            var index = Interlocked.Increment(ref _responseIndex) - 1;
            return index switch
            {
                0 => ToolResponse(payload, "resp_engine", "call_engine", EngineStatusAlias, new JsonObject()),
                1 => ToolResponse(payload, "resp_search_gauntlet", "call_search_gauntlet", SearchAlias, new JsonObject
                {
                    ["query"] = "rekall.workflow.agent_authoring_gauntlet",
                    ["maxResults"] = 6
                }),
                2 => ToolResponse(payload, "resp_gauntlet", "call_gauntlet", GauntletAlias, new JsonObject
                {
                    ["projectName"] = "OpenAI Acceptance",
                    ["outputDirectory"] = outputDirectory
                }),
                3 => ToolResponse(payload, "resp_search_runtime", "call_search_runtime", SearchAlias, new JsonObject
                {
                    ["query"] = "rekall.runtime.inspect_scene",
                    ["maxResults"] = 6
                }),
                4 => ToolResponse(payload, "resp_runtime", "call_runtime", InspectSceneAlias, RuntimeProofArguments()),
                5 => MessageResponse("resp_complete", "OpenAI project-agent gameplay proof completed."),
                _ => throw new InvalidOperationException($"Unexpected OpenAI Responses turn {index + 1}.")
            };
        }

        private HttpResponseMessage ToolResponse(
            JsonObject request,
            string responseId,
            string callId,
            string alias,
            JsonObject arguments)
        {
            Assert.Contains(request["tools"]!.AsArray(), tool =>
                tool!["name"]!.GetValue<string>() == alias);
            ReturnedToolAliases.Add(alias);
            return SseResponse(responseId, new JsonObject
            {
                ["id"] = "fc_" + callId,
                ["type"] = "function_call",
                ["call_id"] = callId,
                ["name"] = alias,
                ["arguments"] = arguments.ToJsonString(),
                ["status"] = "completed"
            });
        }

        private static HttpResponseMessage MessageResponse(string responseId, string text) =>
            SseResponse(responseId, new JsonObject
            {
                ["id"] = "msg_complete",
                ["type"] = "message",
                ["role"] = "assistant",
                ["status"] = "completed",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "output_text",
                    ["text"] = text,
                    ["annotations"] = new JsonArray()
                })
            });

        private static HttpResponseMessage SseResponse(string responseId, JsonObject output)
        {
            var response = new JsonObject
            {
                ["id"] = responseId,
                ["model"] = Model,
                ["status"] = "completed",
                ["output"] = new JsonArray(output),
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = 10,
                    ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 2 },
                    ["output_tokens"] = 4,
                    ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 1 },
                    ["total_tokens"] = 14
                }
            };
            var envelope = new JsonObject
            {
                ["type"] = "response.completed",
                ["response"] = response
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"data: {envelope.ToJsonString()}\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }

        private JsonObject RuntimeProofArguments()
        {
            var proof = CreateRuntimeProof(projectRoot);
            return new JsonObject
            {
                ["frames"] = proof.Frames,
                ["inputs"] = new JsonArray(new JsonObject
                {
                    ["deltaSeconds"] = 1,
                    ["semanticActions"] = new JsonArray(new JsonObject
                    {
                        ["name"] = "agent.gauntlet.advance",
                        ["value"] = 1,
                        ["isDown"] = true,
                        ["wasPressed"] = true
                    })
                }),
                ["assertions"] = new JsonArray(
                    new JsonObject
                    {
                        ["entityName"] = "Agent Authored Marker",
                        ["subject"] = "component",
                        ["operator"] = "exists",
                        ["componentType"] = "Game.Modules.AgentGauntlet.GauntletState"
                    },
                    new JsonObject
                    {
                        ["entityName"] = "Agent Authored Marker",
                        ["subject"] = "delta.component.property",
                        ["operator"] = "equals",
                        ["componentType"] = "Game.Modules.AgentGauntlet.GauntletState",
                        ["propertyName"] = "progress",
                        ["expected"] = 1
                    },
                    new JsonObject
                    {
                        ["entityName"] = "Agent Authored Marker",
                        ["subject"] = "delta.position2d.x",
                        ["operator"] = "equals",
                        ["expected"] = 1
                    })
            };
        }
    }
}
