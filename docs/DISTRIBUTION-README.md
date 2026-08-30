# Rekall AGE — Installed Developer Preview

Rekall AGE is a Windows-first agentic game engine for authoring arbitrary games through inspectable, generic, composable contracts. This is version `0.1.0-preview.1`, an Early Access Developer Preview.

Start Studio with `tools\studio\Rekall.Age.Studio.exe`. Studio opens projects in the World workspace and includes the Author chat, typed Inspector, hierarchy, Vulkan viewport, C# module editor, simulation, play, packaging, and 18 writable bundled examples.

Open the comprehensive manual from **Help → Documentation** or press **F1** in Studio. The same single-file manual is installed at [tools/studio/Documentation/Rekall-AGE-Documentation.html](../tools/studio/Documentation/Rekall-AGE-Documentation.html).

The command line and MCP host are available through `tools\cli\Rekall.Age.Cli.exe`. Run this installed-product health check from PowerShell:

```powershell
.\tools\cli\Rekall.Age.Cli.exe context doctor
```

C# gameplay-module authoring requires the .NET 10 SDK. Vulkan rendering requires a Vulkan-capable GPU and current driver. Local AI authoring requires Ollama and a separately downloaded compatible model; cloud providers require the user's own credentials and may charge separately.

Rekall AGE is proprietary software governed by the [End User License Agreement](../END-USER-LICENSE-AGREEMENT.md) and [proprietary notice](../PROPRIETARY-NOTICE.md). Third-party attributions and licenses are recorded in [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt).

Support, privacy information, source-level technical material, and current issue tracking are available at:

- https://github.com/MariusMyburg/Rekall_AgenticGameEngine
- https://github.com/MariusMyburg/Rekall_AgenticGameEngine/issues
