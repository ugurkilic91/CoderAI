# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

DevAgents: a multi-agent coding assistant built on a **local Ollama** server. An ASP.NET Core minimal-API app runs the agent pipeline, serves a static web chat UI (`src/DevAgents.Api/wwwroot/`), and is used by a plain-JS VS Code extension (`vscode-extension/`). The target audience is Turkish: user-facing strings (exception messages, router reasons, UI, README) are Turkish, while agent prompts, code and code comments are English.

## Commands

```bash
dotnet build                                   # DevAgents.slnx at the repo root
dotnet test                                    # all test projects
dotnet test tests/DevAgents.Application.UnitTests --filter "FullyQualifiedName~AgentPipelineTests"   # one class
cd src/DevAgents.Api && dotnet run             # → http://localhost:8080 (needs Ollama at Ollama:BaseUrl)

docker compose up -d --build                   # API + UI; Ollama on the host by default
docker compose --profile with-ollama up -d --build

cd vscode-extension && npx @vscode/vsce package   # no build step; F5 in that folder to debug
```

`src/Directory.Build.props` sets `net10.0`, nullable and **`TreatWarningsAsErrors`** for every project; `tests/Directory.Build.props` imports it and adds NUnit 5 (in NUnit 5, `Assert.ThrowsAsync`/`CatchAsync` return a Task and must be awaited). Both files live below the root on purpose, so they do not affect the projects under `projects/`.

## Architecture (Clean Architecture)

Dependencies point inward: `Api → Infrastructure → Application → Domain` (Api also references Application).

- **Domain**: `AgentRole` (agent names, also config/override keys; `SettingsFallback()`: Router and BackTranslator use the Translator's settings), `AgentPlan` (+ `Normalize()` planning rules), `GeneratedFile`, `RelativePath.Normalize` (rejects `..` and `:` paths).
- **Application**: use cases per feature folder (`Chat/`, `Context/`, `SystemInfo/`) and ports in `Common/Interfaces` (`ILanguageModelClient`, `IAgentModelResolver`, `IPromptProvider`, `IWorkspaceFileSystem`). Application never touches HTTP or the disk directly. `DependencyInjection.AddApplication` binds the `Pipeline` and `Workspace` sections.
- **Infrastructure**: `Ollama/` (HTTP client streaming NDJSON, `OllamaOptions`, `OllamaAgentModelResolver`), `Prompts/FilePromptProvider`, `Workspace/FileSystemWorkspace` (path resolution under `AllowedRoots`, file enumeration). Binds the `Ollama` section.
- **Api**: composition root. `Hosting/` (config loading, exception → status mapping), `Endpoints/` (thin minimal-API handlers), `OpenAi/` (contracts for the `/v1` shim), `wwwroot/`, `prompts/`, `appsettings.json`.

### Pipeline (`Application/Chat/AgentPipeline.cs`)
Each stage name below is emitted as a streaming event (`PipelineStages`):

1. `translate_in`: Translator agent, Turkish → English.
2. `route`: Router agent returns JSON, which `Chat/Parsing/RouterDecisionParser` turns into a normalized `AgentPlan`. If it can't be read, the **full pipeline** runs. The Router is skipped when the request sets `options.stages` (manual) or `options.mode: "full"`.
3. `context`: `IProjectContextBuilder.Build`, only when `plan.NeedsContext` (otherwise `ProjectContext.None`).
4. `answer` (Analyzer model + `answerer` prompt) **or** `analyze` → `code` → loop of `test` → `review` → `revise`. The loop ends when the review is approved, when no code stage is planned, or when it hits `MaxReviewIterations` (clamped 0–5).
5. `translate_out`: BackTranslator, English → Turkish, one report section at a time (`PipelineReport`).

Helpers: `AgentRunner` handles the per-run work (it resolves models, streams tokens and records `Models`). `AgentMessages` builds every agent's user message. `AgentOutputs` holds what the agents produced so far.

### Prompts are parsing contracts
System prompts are `src/DevAgents.Api/prompts/*.md`; names are in `Chat/AgentPrompts`. If you change an output format, update its parser too:
- Router → one JSON object (`RouterDecisionParser`).
- Reviewer → `VERDICT: APPROVED` / `VERDICT: NEEDS_CHANGES` within the first 400 chars (`ReviewVerdictParser`).
- Coder/Tester → `### FILE: relative/path` + one fenced block (`GeneratedFileParser`; also parsed client-side in `wwwroot/app.js`).
- Back-translation → `Common/Text/ProtectedMarkdown` swaps code blocks for `[[CODE_n]]` placeholders and appends any block the translator dropped.
- The `## REQUEST`, `## ANALYSIS`, `## IMPLEMENTATION`, … section names in `AgentMessages` are referenced by the prompts.
- `<think>…</think>` blocks are removed from every answer (`Common/Text/ModelOutput`).

`FilePromptProvider` reads `AppContext.BaseDirectory/prompts`, i.e. the **build-output copy**. With `dotnet run`, edits take effect after the next build. In Docker the source folder is bind-mounted, so edits apply immediately.

### Wire contract
`PipelineResult`, `PipelineEvent`, `AgentPlan`, `GeneratedFile` and `ContextInfo` are serialized as they are (camelCase) and consumed by `wwwroot/app.js` and `vscode-extension/extension.js`. Keep shapes in sync across all three. Don't add public properties to these records (use methods, as `AgentPlan.RunsAnyAgent()` does). `PipelineEvent.Data` is `object`, so it serializes with its runtime type.

### Configuration and model resolution
- `Hosting/ConfigurationExtensions.AddDevAgentsConfiguration` loads, in order: `appsettings.json` → `config/local.json` → env vars (`Ollama__Agents__Coder__Model=...`).
  - `config/local.json` is looked up at `<contentRoot>/config/local.json` (Docker), at `<repo>/config/local.json` (`dotnet run` from `src/DevAgents.Api`), and at `$DEVAGENTS_CONFIG`.
  - Files reload on change and everything reads `IOptionsMonitor.CurrentValue`.
- `OllamaAgentModelResolver`: request `modelOverrides[agent]` (model only) → `Agents[agent]` → `Agents[fallback agent]` → `Defaults` → built-in fallback values.
- `/api/config` lists `Enum.GetValues<AgentRole>()`, so a new agent needs an `AgentRole` member, a prompt, a call in the pipeline and (for the UI's model pickers) an entry in `AGENTS` in `wwwroot/app.js`.

### Project context (`Application/Context/`)
`ProjectContextBuilder` merges the indexed files (`IWorkspaceFileSystem.EnumerateFiles`, filtered by `ProjectFileFilter`: extensions, excluded folders, `SensitiveFiles`, size and count limits) with the client's inline files. Pinned files bypass the filter. The context is an overview (`ProjectOverview`: csproj TFM/packages/refs, layer hints, shallow-first file tree) plus file contents within `Workspace:MaxContextChars`. `ContextFileSelector` adds files in this order:
1. Pinned files (over budget if needed).
2. csproj, `Program.cs`/`Startup.cs`, README.
3. Keyword-scored files (`QueryTokenizer`): a path match counts more than a content match, at most 3 per folder.
4. One small `.cs` exemplar per folder not yet represented.

When `Workspace:AllowedRoots` is empty, any local path is allowed. Docker sets it to `/workspace`, where the projects are mounted read-only.

### HTTP surface (`Api/Endpoints/`)
- `POST /api/chat` (JSON) and `POST /api/chat/stream` (NDJSON: `stage_start`, `token`, `stage_end`, `plan`, `final`, `error`).
- `POST /api/context/preview`, `GET /api/health|models|config|projects`.
- `POST /v1/chat/completions`: an OpenAI-compatible SSE shim for the Continue extension (model id `csharp-multi-agent`). It sends only the last message as the question, with no project, files or history.
- Errors: `ApiExceptionHandler` + `ApiErrors.StatusCodeFor` map `InvalidRequestException` → 400, `LanguageModelException` → 502, `LanguageModelTimeoutException` → 504, client abort → 499, anything else → 500. The response is problem details with the message in `detail`, which the UI shows. Streaming endpoints catch errors themselves and write an `error` event or chunk instead.
- The API never writes project files. Only the VS Code extension (`devagents.applyLast`) writes generated files, after per-file confirmation. Conversation history is kept client-side; the server uses the last `Pipeline:MaxHistoryItems` turns.

### Other
`projects/CleanArchitecture` is a git submodule gitlink (there is no `.gitmodules`). It is a sample target codebase for the agents, not part of this app. In Docker, `projects/` is mounted at `/workspace` by default and is excluded from the image build context (`.dockerignore`).
