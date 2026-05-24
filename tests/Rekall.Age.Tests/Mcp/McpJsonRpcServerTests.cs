using System.Text.Json;
using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Build.Commands;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Mcp;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpJsonRpcServerTests
{
    [Fact]
    public async Task InitializeReturnsToolCapability()
    {
        var server = CreateServer();
        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal("2025-06-18", document.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.True(document.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _));
        var instructions = document.RootElement.GetProperty("result").GetProperty("instructions").GetString();
        Assert.Contains("rekall.templates.inspect", instructions, StringComparison.Ordinal);
        Assert.Contains("rekall.templates.verify_mvp", instructions, StringComparison.Ordinal);
        Assert.Contains("rekall.workflow.create_playable_package_from_template", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsListReturnsRegisteredCommandsWithInputSchema()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tool = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().Single();
        Assert.Equal("rekall.test.echo", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
        Assert.True(tool.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("message", out _));
        Assert.Equal("unknown", tool.GetProperty("rekallCategory").GetString());
        Assert.False(tool.GetProperty("rekallRecommended").GetBoolean());
    }

    [Fact]
    public async Task ToolsListExposesAgentToolMetadata()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new GetEngineStatusCommand());
        registry.Register(new CreatePlayablePackageFromTemplateCommand());
        registry.Register(new CreateRenderPlanCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":20,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        var engineStatus = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.context.engine_status");
        var oneShot = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.workflow.create_playable_package_from_template");
        var render = tools.Single(tool => tool.GetProperty("name").GetString() == "rekall.render.plan.create");
        Assert.Equal("rekall.context.engine_status", tools[0].GetProperty("name").GetString());
        Assert.Equal("context", engineStatus.GetProperty("rekallCategory").GetString());
        Assert.True(engineStatus.GetProperty("rekallRecommended").GetBoolean());
        Assert.Equal(5, engineStatus.GetProperty("rekallAgentPriority").GetInt32());
        Assert.Equal("workflow", oneShot.GetProperty("rekallCategory").GetString());
        Assert.True(oneShot.GetProperty("rekallRecommended").GetBoolean());
        Assert.True(render.GetProperty("rekallAgentPriority").GetInt32() > oneShot.GetProperty("rekallAgentPriority").GetInt32());
    }

    [Fact]
    public async Task ToolsListDoesNotRequireOptionalRecordParameters()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new OptionalCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var required = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Contains("required", required);
        Assert.DoesNotContain("optional", required);
    }

    [Fact]
    public async Task ToolsListExposesNestedValueObjectProperties()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ClearColorSchemaCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var clearColor = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("properties")
            .GetProperty("clearColor");
        Assert.Equal("object", clearColor.GetProperty("type").GetString());
        var properties = clearColor.GetProperty("properties");
        Assert.Equal("number", properties.GetProperty("r").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("g").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("b").GetProperty("type").GetString());
        Assert.Equal("number", properties.GetProperty("a").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToolsListExposesArrayItemProperties()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new PlaySceneCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var inputItem = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("properties")
            .GetProperty("inputs")
            .GetProperty("items");
        Assert.Equal("object", inputItem.GetProperty("type").GetString());
        var properties = inputItem.GetProperty("properties");
        Assert.Equal("boolean", properties.GetProperty("primaryAction").GetProperty("type").GetString());
        Assert.Equal("integer", properties.GetProperty("verticalAxis").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ToolsListExposesDrawAssertionArrayItemProperties()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new PlaytestSceneCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var inputItem = document.RootElement
            .GetProperty("result")
            .GetProperty("tools")[0]
            .GetProperty("inputSchema")
            .GetProperty("properties")
            .GetProperty("drawAssertions")
            .GetProperty("items");
        Assert.Equal("object", inputItem.GetProperty("type").GetString());
        var properties = inputItem.GetProperty("properties");
        Assert.Equal("integer", properties.GetProperty("frameIndex").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("kind").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("id").GetProperty("type").GetString());
        Assert.Equal("string", properties.GetProperty("textContains").GetProperty("type").GetString());
        Assert.Equal("boolean", properties.GetProperty("mustExist").GetProperty("type").GetString());
    }


    [Fact]
    public async Task JsonLinesCanStartWithUtf8Bom()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            "\uFEFF{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        Assert.Equal("rekall.test.echo", document.RootElement.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task ToolsCallExecutesRegisteredCommand()
    {
        var server = CreateServer();

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"rekall.test.echo","arguments":{"message":"ping"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Echoed message.", result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("ping from command", result.GetProperty("structuredContent").GetProperty("value").GetProperty("message").GetString());
    }

    [Fact]
    public async Task ToolsCallReturnsStructuredErrorWhenCommandThrowsExpectedException()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new InspectGameTemplateCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"rekall.templates.inspect","arguments":{"templateId":"missing-template"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var error = result.GetProperty("structuredContent").GetProperty("errors")[0];
        Assert.Equal("REKALL_COMMAND_EXECUTION_FAILED", error.GetProperty("code").GetString());
        Assert.Contains("missing-template", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolsCallListsTemplateDrawContractsForAgents()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ListGameTemplatesCommand());
        registry.Register(new InspectGameTemplateCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"rekall.templates.list","arguments":{}}}""",
            CreateContext());
        var inspectResponse = await server.HandleJsonLineAsync(
            """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"rekall.templates.inspect","arguments":{"templateId":"puzzle"}}}""",
            CreateContext());

        using var document = JsonDocument.Parse(response!);
        using var inspectDocument = JsonDocument.Parse(inspectResponse!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var templates = result.GetProperty("structuredContent").GetProperty("value").GetProperty("templates");
        var puzzle = templates.EnumerateArray().Single(template => template.GetProperty("id").GetString() == "puzzle");
        var drawCommands = puzzle.GetProperty("drawCommands");
        Assert.Contains(drawCommands.EnumerateArray(), command =>
            command.GetProperty("id").GetString() == "grid" &&
            command.GetProperty("kind").GetString() == "rect");
        Assert.Contains(drawCommands.EnumerateArray(), command =>
            command.GetProperty("id").GetString() == "objective" &&
            command.GetProperty("kind").GetString() == "text");
        var inspect = inspectDocument.RootElement.GetProperty("result");
        Assert.False(inspect.GetProperty("isError").GetBoolean());
        var inspectValue = inspect.GetProperty("structuredContent").GetProperty("value");
        Assert.Equal("puzzle", inspectValue.GetProperty("template").GetProperty("id").GetString());
        Assert.Contains(inspectValue.GetProperty("suggestedCommands").EnumerateArray(), command =>
            command.GetProperty("tool").GetString() == "rekall.workflow.create_playable_package_from_template");
    }

    [Fact]
    public async Task ToolsCallCanCreateBuildAndPlayTemplateGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreatePlayableGameFromTemplateCommand());
        registry.Register(new PlaySceneCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var createResponse = await server.HandleJsonLineAsync(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 10,
                method = "tools/call",
                @params = new
                {
                    name = "rekall.workflow.create_playable_game_from_template",
                    arguments = new
                    {
                        projectRoot = root,
                        projectName = "MCP Pong",
                        templateId = "pong"
                    }
                }
            }),
            CreateContext());
        var playResponse = await server.HandleJsonLineAsync(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 11,
                method = "tools/call",
                @params = new
                {
                    name = "rekall.play.scene",
                    arguments = new
                    {
                        projectRoot = root,
                        sceneName = "Main",
                        frames = 1,
                        inputs = new[]
                        {
                            new
                            {
                                verticalAxis = 1,
                                primaryAction = true
                            }
                        }
                    }
                }
            }),
            CreateContext());

        using var createDocument = JsonDocument.Parse(createResponse!);
        using var playDocument = JsonDocument.Parse(playResponse!);
        Assert.False(createDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(playDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        var frame = playDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value")
            .GetProperty("frames")[0]
            .GetString();
        Assert.Contains("PONG", frame, StringComparison.Ordinal);
        Assert.Contains("Score 10", frame, StringComparison.Ordinal);
        var renderFrame = playDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value")
            .GetProperty("renderFrames")[0];
        Assert.Equal("pong", renderFrame.GetProperty("kind").GetString());
        Assert.Equal(1, renderFrame.GetProperty("frameIndex").GetInt32());
        var drawCommands = renderFrame.GetProperty("drawCommands");
        Assert.Contains(drawCommands.EnumerateArray(), command => command.GetProperty("kind").GetString() == "clear");
        Assert.Contains(drawCommands.EnumerateArray(), command => command.GetProperty("id").GetString() == "left-paddle");
        Assert.Contains(drawCommands.EnumerateArray(), command => command.GetProperty("id").GetString() == "ball");
        Assert.Contains(drawCommands.EnumerateArray(), command => command.GetProperty("text").GetString()?.Contains("Score 10", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ToolsCallCanCreateBuildAndPlaytestTemplateGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreatePlayableGameFromTemplateCommand());
        registry.Register(new PlaytestSceneCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var createResponse = await server.HandleJsonLineAsync(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 20,
                method = "tools/call",
                @params = new
                {
                    name = "rekall.workflow.create_playable_game_from_template",
                    arguments = new
                    {
                        projectRoot = root,
                        projectName = "MCP Playtest Pong",
                        templateId = "pong"
                    }
                }
            }),
            CreateContext());
        var playtestResponse = await server.HandleJsonLineAsync(
            JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 21,
                method = "tools/call",
                @params = new
                {
                    name = "rekall.playtest.scene",
                    arguments = new
                    {
                        projectRoot = root,
                        sceneName = "Main",
                        frames = 2,
                        inputs = new[]
                        {
                            new
                            {
                                verticalAxis = 1,
                                primaryAction = true
                            },
                            new
                            {
                                verticalAxis = -1,
                                primaryAction = false
                            }
                        },
                        assertions = new[]
                        {
                            new
                            {
                                frameIndex = 0,
                                contains = "Score 10"
                            },
                            new
                            {
                                frameIndex = 1,
                                contains = "Left paddle lane 0"
                            }
                        },
                        drawAssertions = new[]
                        {
                            new
                            {
                                frameIndex = 0,
                                id = (string?)"ball",
                                kind = "circle",
                                textContains = (string?)null
                            },
                            new
                            {
                                frameIndex = 0,
                                id = (string?)null,
                                kind = "text",
                                textContains = (string?)"Score 10"
                            }
                        }
                    }
                }
            }),
            CreateContext());

        using var createDocument = JsonDocument.Parse(createResponse!);
        using var playtestDocument = JsonDocument.Parse(playtestResponse!);
        Assert.False(createDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(playtestDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.True(playtestDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value")
            .GetProperty("passed")
            .GetBoolean());
        var playtestValue = playtestDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value");
        Assert.True(playtestValue.GetProperty("drawAssertions")[0].GetProperty("passed").GetBoolean());
        Assert.Equal("ball", playtestValue.GetProperty("drawAssertions")[0].GetProperty("matchingCommands")[0].GetProperty("id").GetString());
        Assert.Equal("circle", playtestValue.GetProperty("drawAssertions")[0].GetProperty("matchingCommands")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ToolsCallCanCreatePackageInspectAndRunPlayableTemplateGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var outputDirectory = Path.Combine(root, "Packaged");
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreatePlayableGameFromTemplateCommand());
        registry.Register(new PackagePlayableGameCommand());
        registry.Register(new InspectPlayablePackageCommand());
        registry.Register(new RunPlayablePackageCommand());
        registry.Register(new CapturePlayablePackageFrameCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var createResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 25,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.create_playable_game_from_template",
                arguments = new
                {
                    projectRoot = root,
                    projectName = "MCP Packaged Pong",
                    templateId = "pong"
                }
            }
        }), CreateContext());
        var packageResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 26,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.package_playable_game",
                arguments = new
                {
                    projectRoot = root,
                    sceneName = "Main",
                    outputDirectory
                }
            }
        }), CreateContext());
        var inspectResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 27,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.inspect_playable_package",
                arguments = new
                {
                    packagePath = $"{outputDirectory}.zip"
                }
            }
        }), CreateContext());
        var runResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 28,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.run_playable_package",
                arguments = new
                {
                    packagePath = $"{outputDirectory}.zip",
                    frames = 1
                }
            }
        }), CreateContext());
        var captureResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 29,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.capture_playable_package_frame",
                arguments = new
                {
                    packagePath = $"{outputDirectory}.zip",
                    outputDirectory = Path.Combine(root, "McpPackageFrames"),
                    frameIndex = 1
                }
            }
        }), CreateContext());

        using var createDocument = JsonDocument.Parse(createResponse!);
        using var packageDocument = JsonDocument.Parse(packageResponse!);
        using var inspectDocument = JsonDocument.Parse(inspectResponse!);
        using var runDocument = JsonDocument.Parse(runResponse!);
        using var captureDocument = JsonDocument.Parse(captureResponse!);
        Assert.False(createDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(packageDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(inspectDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(runDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(captureDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());

        var inspectValue = inspectDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value");
        Assert.True(inspectValue.GetProperty("ready").GetBoolean());
        Assert.Equal("pong", inspectValue.GetProperty("manifest").GetProperty("sourceTemplateId").GetString());

        var runValue = runDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value");
        Assert.True(runValue.GetProperty("ready").GetBoolean());
        Assert.Equal(0, runValue.GetProperty("exitCode").GetInt32());
        Assert.Contains("FRAME 1", runValue.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Contains("PONG", runValue.GetProperty("frames")[0].GetString(), StringComparison.Ordinal);
        var renderFrame = runValue.GetProperty("renderFrames")[0];
        Assert.Equal("pong", renderFrame.GetProperty("kind").GetString());
        Assert.Contains(renderFrame.GetProperty("drawCommands").EnumerateArray(), command =>
            command.GetProperty("id").GetString() == "ball" &&
            command.GetProperty("kind").GetString() == "circle");

        var captureValue = captureDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value");
        Assert.True(captureValue.GetProperty("captured").GetBoolean());
        Assert.True(captureValue.GetProperty("nonBlank").GetBoolean());
        Assert.True(File.Exists(captureValue.GetProperty("outputPath").GetString()));
    }

    [Fact]
    public async Task ToolsCallCanCreatePlayablePackageWithSingleWorkflowTool()
    {
        var root = TestPaths.CreateTempDirectory();
        var packageOutput = Path.Combine(root, "OneShotPackage");
        var frameOutput = Path.Combine(root, "OneShotFrames");
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreatePlayablePackageFromTemplateCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var response = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 35,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.create_playable_package_from_template",
                arguments = new
                {
                    projectRoot = root,
                    projectName = "MCP One Shot Pong",
                    templateId = "pong",
                    outputDirectory = packageOutput,
                    captureOutputDirectory = frameOutput,
                    frames = 1
                }
            }
        }), CreateContext());

        using var document = JsonDocument.Parse(response!);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var value = result.GetProperty("structuredContent").GetProperty("value");
        Assert.True(value.GetProperty("ready").GetBoolean());
        Assert.Equal("pong", value.GetProperty("templateId").GetString());
        Assert.True(File.Exists(value.GetProperty("package").GetProperty("archivePath").GetString()));
        Assert.True(File.Exists(value.GetProperty("capture").GetProperty("outputPath").GetString()));
        Assert.Contains("PONG", value.GetProperty("run").GetProperty("frames")[0].GetString(), StringComparison.Ordinal);
        Assert.Contains(value.GetProperty("run").GetProperty("renderFrames")[0].GetProperty("drawCommands").EnumerateArray(), command =>
            command.GetProperty("id").GetString() == "ball" &&
            command.GetProperty("kind").GetString() == "circle");
    }

    [Fact]
    public async Task ToolsCallCanAuthorBuildAndPlaytestCustomModuleGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateGameFromTemplateCommand());
        registry.Register(new ScaffoldPlayableModuleCommand());
        registry.Register(new WriteModuleSourceCommand());
        registry.Register(new BuildModulesCommand());
        registry.Register(new PlaytestSceneCommand());
        var server = new RekallAgeMcpJsonRpcServer(registry);

        var createResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 30,
            method = "tools/call",
            @params = new
            {
                name = "rekall.workflow.create_game_from_template",
                arguments = new
                {
                    projectRoot = root,
                    projectName = "MCP Authored Pong",
                    templateId = "pong"
                }
            }
        }), CreateContext());
        var scaffoldResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 31,
            method = "tools/call",
            @params = new
            {
                name = "rekall.module.scaffold_playable",
                arguments = new
                {
                    projectRoot = root,
                    moduleId = "mcp.authored",
                    displayName = "MCP Authored",
                    moduleName = "McpAuthored",
                    kind = "pong"
                }
            }
        }), CreateContext());
        var writeResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 32,
            method = "tools/call",
            @params = new
            {
                name = "rekall.module.write_source",
                arguments = new
                {
                    projectRoot = root,
                    moduleName = "McpAuthored",
                    fileName = "McpAuthoredModule.cs",
                    source = CreateMcpAuthoredPlayableSource()
                }
            }
        }), CreateContext());
        var buildResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 33,
            method = "tools/call",
            @params = new
            {
                name = "rekall.build.modules",
                arguments = new
                {
                    projectRoot = root
                }
            }
        }), CreateContext());
        var playtestResponse = await server.HandleJsonLineAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 34,
            method = "tools/call",
            @params = new
            {
                name = "rekall.playtest.scene",
                arguments = new
                {
                    projectRoot = root,
                    sceneName = "Main",
                    frames = 1,
                    inputs = new[]
                    {
                        new
                        {
                            verticalAxis = 1,
                            primaryAction = true
                        }
                    },
                    assertions = new[]
                    {
                        new
                        {
                            frameIndex = 0,
                            contains = "MCP AUTHORED PONG"
                        },
                        new
                        {
                            frameIndex = 0,
                            contains = "Score 77"
                        }
                    }
                }
            }
        }), CreateContext());

        using var createDocument = JsonDocument.Parse(createResponse!);
        using var scaffoldDocument = JsonDocument.Parse(scaffoldResponse!);
        using var writeDocument = JsonDocument.Parse(writeResponse!);
        using var buildDocument = JsonDocument.Parse(buildResponse!);
        using var playtestDocument = JsonDocument.Parse(playtestResponse!);
        Assert.False(createDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(scaffoldDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(writeDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(buildDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(playtestDocument.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.True(playtestDocument.RootElement
            .GetProperty("result")
            .GetProperty("structuredContent")
            .GetProperty("value")
            .GetProperty("passed")
            .GetBoolean());
    }

    private static RekallAgeMcpJsonRpcServer CreateServer()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());
        return new RekallAgeMcpJsonRpcServer(registry);
    }

    private static RekallAgeCommandContext CreateContext()
    {
        return new RekallAgeCommandContext("mcp-test", RekallAgeTransaction.Begin("mcp"), CancellationToken.None);
    }

    private static string CreateMcpAuthoredPlayableSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.McpAuthored;

[RekallAgeModule("mcp.authored", "MCP Authored")]
[RekallAgeRequiresCapability("world")]
public sealed class McpAuthoredModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "pong";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["score"] = 0;
        state.Numbers["frame"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        state.Numbers["frame"] += 1;
        if (input.PrimaryAction)
        {
            state.Numbers["score"] += 77;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        return new RekallAgePlayableModuleFrame($"MCP AUTHORED PONG\nFrame {(int)state.Numbers["frame"]}\nScore {(int)state.Numbers["score"]}");
    }
}
""";
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed record OptionalRequest(string Required, string? Optional = null);

    private sealed record OptionalResult(string Value);

    private sealed record ClearColorSchemaRequest(RekallAgeVulkanClearColor? ClearColor = null);

    private sealed record ClearColorSchemaResult(RekallAgeVulkanClearColor? ClearColor);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "rekall.test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a message.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(
                new EchoResult($"{request.Message} from command"),
                "Echoed message."));
        }
    }

    private sealed class OptionalCommand : IRekallAgeCommand<OptionalRequest, OptionalResult>
    {
        public string Name => "rekall.test.optional";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Tests optional schema fields.",
            typeof(OptionalRequest).FullName!,
            typeof(OptionalResult).FullName!);

        public ValueTask<RekallAgeCommandResult<OptionalResult>> ExecuteAsync(
            OptionalRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<OptionalResult>.Success(new OptionalResult(request.Required)));
        }
    }

    private sealed class ClearColorSchemaCommand : IRekallAgeCommand<ClearColorSchemaRequest, ClearColorSchemaResult>
    {
        public string Name => "rekall.test.clear_color_schema";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Tests nested value object schema fields.",
            typeof(ClearColorSchemaRequest).FullName!,
            typeof(ClearColorSchemaResult).FullName!);

        public ValueTask<RekallAgeCommandResult<ClearColorSchemaResult>> ExecuteAsync(
            ClearColorSchemaRequest request,
            RekallAgeCommandContext context)
        {
            return ValueTask.FromResult(RekallAgeCommandResult<ClearColorSchemaResult>.Success(new ClearColorSchemaResult(request.ClearColor)));
        }
    }
}
