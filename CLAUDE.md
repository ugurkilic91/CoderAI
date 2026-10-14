# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

DevAgents is an on-prem, multi-agent C# coding tool that runs on a **local Ollama** server. A developer writes a request in Turkish ("hobilerimi kaydeden kodu geliştir"). The request is translated to English and routed. Then agents analyze it, write code in the selected architecture, write the files into the project, build in a Docker sandbox, fix errors, write and run unit tests, and report back in Turkish.

The audience is Turkish developers in a company team:
- User-facing strings are Turkish: UI, exception and validation messages, and the report.
- **Code comments are Turkish**: XML doc on types and methods, plus short step comments inside methods (e.g. `// 4) Burada test agent'a ihtiyaç duyduk: ...`). Keep this style in new code.
- Agent prompts and the messages sent to models are English.

## Commands

```bash
dotnet build                                       # DevAgents.slnx; output goes to artifacts/ (ArtifactsPath)
dotnet test --filter "Category!=Slow"              # fast tests (~160)
dotnet test                                        # + Slow: real dotnet build in Process and Docker sandbox modes
dotnet test tests/Application.UnitTests --filter "FullyQualifiedName~DevelopmentWorkflowTests"   # one class
cd src/Web && dotnet run                           # → http://localhost:8080 (Ollama at Ollama:BaseUrl, Docker for the sandbox)
docker compose up -d --build                       # team server; needs .env (see .env.example)
```

The root `Directory.Build.props` sets `net10.0`, nullable, ImplicitUsings and **`TreatWarningsAsErrors`**. `Directory.Packages.props` does central package management, so versions live there and not in the csproj files. The empty `projects/Directory.Build.props` and `projects/Directory.Packages.props` stop these settings from leaking into the projects under `projects/`.

**MediatR is pinned to 12.x (Apache 2.0) on purpose**, because 13+ has a commercial license. Do not upgrade it.

Tests use NUnit 5 + Shouldly (+ Moq). In NUnit 5, `Assert.ThrowsAsync` returns a Task, so prefer `await Should.ThrowAsync<T>(...)`.

## Architecture

The solution follows Jason Taylor's Clean Architecture template (`projects/CleanArchitecture`, the `ca-sln` template): `Domain ← Application ← Infrastructure ← Web`. It follows the template's conventions:
- a command or query and its handler live in one file, with the validator in a separate file;
- `DependencyInjection.AddXServices(this IHostApplicationBuilder)` lives in namespace `Microsoft.Extensions.DependencyInjection`;
- endpoints are `IEndpointGroup` classes with named static handlers that take `ISender`;
- MediatR pipeline behaviours handle logging, validation, performance and unhandled exceptions. Stream requests have their own behaviours in `StreamBehaviours.cs`.

Layers:
- **Domain**: `AgentRole`, `AgentPlan` (planning rules), `GeneratedFile`, `RelativePath.Normalize` (rejects `..` and `:`), and `BuildResult`/`CompilerDiagnostic`, `TestRunResult`, `FileChange`.
- **Application**: use cases and ports.
  - `Agents/Commands/RunAgentPipeline` is the main use case. It is a stream request: a `Channel` bridges pipeline callbacks to `IAsyncEnumerable<PipelineEvent>`.
  - Other use cases live in `Context/`, `SystemInfo/`, `Architectures/` and `Changes/` (revert).
  - Ports are in `Common/Interfaces`. Application never touches HTTP, Docker, git or the disk directly.
- **Infrastructure**:
  - `Ollama/`: streaming client and per-agent model resolution.
  - `Prompts/`, `Architectures/FileArchitectureCatalog`.
  - `Workspace/`: path resolution under `AllowedRoots`, and the writer, which never writes into `.git`/`bin`/`obj` and keeps BOMs.
  - `SourceControl/GitChangeTracker`, `CodeAnalysis/` (Roslyn), `Sandbox/`.
- **Web**: composition root.
  - `Endpoints/` covers chat, stream, context, systeminfo, architectures, changes, and the OpenAI-compatible `/v1/chat/completions`.
  - `Infrastructure/ProblemDetailsExceptionHandler` maps exception → status: validation/path 400, busy project 409, model 502/504, sandbox/git 503.
  - Also here: `wwwroot/` (UI, served with `Cache-Control: no-cache`), `prompts/`, `architectures/`.

### Pipeline (`Application/Agents/AgentPipeline.cs`)
The flow runs these stages in order. Each one is streamed as `stage_start`/`token`/`stage_end` with names from `PipelineStages`.
1. `translate_in`: only when the text looks Turkish (`TurkishText`, `Pipeline:DetectInputLanguage`).
2. `scaffold`: only for a new project. It runs `dotnet new <template>` in the sandbox.
3. `route`: Router JSON → `AgentPlan`. If it cannot be parsed, the full pipeline runs. `options.mode`/`options.stages` override the router.
4. `context`.
5. `answer` or `analyze`. When `options.askQuestions` is set and the analysis has "Questions for the user", the run stops so the user can answer.
6. One of two paths:
   - With `ApplyChanges`: `DevelopmentWorkflow`.
   - Without it: code → test → review/revise loop (suggestion only).
7. `translate_out`: section by section (`PipelineReport`, `ProtectedMarkdown`).

`Development/DevelopmentWorkflow` (apply mode):
1. The coder writes the code. Test files the coder wrote are dropped, because the tester writes the tests.
2. Snapshot the project.
3. Roslyn syntax check (`syntax`), then `write`, then `build`.
4. If the build fails, the deterministic fixes run first, **without a model**. They don't count as fixer attempts:
   - `usings`: `UsingDirectiveFixer` adds missing `using`s. It finds the namespace in the project's sources (respecting ProjectReferences) and in the packages and frameworks listed in `project.assets.json`.
   - `restore`: `RoslynMemberRestorer` re-adds members the coder dropped from an existing file. It uses the file's pre-run copy in `DevelopmentState.Originals`, and only touches members that CS1061/CS0117 errors in pre-existing code (or CS0535) ask for.
   - Then `fix`: the fixer agent gets the errors, the current files and the architecture examples for the failing area. This runs up to `Development:MaxBuildFixAttempts` times, and stops early if the fixer changes nothing.
5. A second snapshot is taken. The tester writes the tests → build → `test_run`, and failing tests go to the fixer. If the tests can't be made to compile, only the test step is reverted to that snapshot and the project is rebuilt, so the project stays buildable.
6. An informational review that gets the real build/test results.
7. `GetChangesAsync`.

The analysis is passed through `ModelOutput.RemoveLongCodeBlocks`. The 14B analyzer tends to write full code, which otherwise overflows `NumCtx` for the later agents.

A project can only run one apply job at a time (`ProjectLocks`).

### Architecture profiles (`src/Web/architectures/<id>/`)
- `profile.json` holds the name, `templateDirectory`, `sourceName`, the `newProject` dotnet args, `testProjects`, `examples` (paths read from the project, falling back to the template) and `extraExamples` (files in `examples/`).
- `rules.md` is English, written for the agents.
- `ArchitectureGuide` builds the text the agents get. The template name in examples (`sourceName`) is replaced with the project folder name.
- The `.cs` files under `architectures/` are excluded from compilation but copied to the output (`Web.csproj`).
- To add a company architecture, add a folder. No code change is needed.

### Sandbox, data and change tracking
- `IBuildSandbox` = `DotnetBuildSandbox`. `Sandbox:Mode` is either `Docker` (default) or `Process`.
  - Docker mode runs `docker run --rm` with memory, CPU and pids limits, as the project folder owner (`User: auto`).
  - Restore runs with the network on. Build and test run with `--network none`.
  - `DOTNET_CLI_UI_LANGUAGE=en` keeps compiler output English for the parsers.
- `Storage:DataDirectory` (default `~/.devagents`) holds:
  - `sandbox-home` = `/home/sandbox` in the container: the NuGet cache and installed templates;
  - `history/` with the change tracking.
- When DevAgents itself runs in a container, `Sandbox:PathMappings` maps container paths to host paths for the sandbox mounts (see `docker-compose.yml`).
- `GitChangeTracker` keeps a separate bare repo per project in `DataDirectory/history` (`git --git-dir X --work-tree Y`) and never touches the project's own `.git`. `POST /api/changes/revert` restores a snapshot.

### Contracts that must stay in sync
- Prompts are parsing contracts. If you change one of these output formats, update its parser:
  - Router → one JSON object.
  - Reviewer → `VERDICT: APPROVED|NEEDS_CHANGES`.
  - Coder/Tester/Fixer → `### FILE: path` + one fenced block, then `### NOTES`.
  - Analyzer → `### Questions for the user`.
- The `## SECTION` names in `AgentMessages` are referenced by `prompts/*.md`.
- `PipelineResult`, `PipelineEvent`, `AgentPlan`, `GeneratedFile`, `DevelopmentSummary` and `ContextInfo` are serialized camelCase and consumed by `wwwroot/app.js` and `vscode-extension/extension.js`. A new stage needs a label in `STAGES` in `app.js`.
- `FilePromptProvider` and `FileArchitectureCatalog` read from `AppContext.BaseDirectory`, i.e. the build output. With `dotnet run`, edits apply after the next build. In Docker those folders are bind-mounted.

### Configuration
- Loading order: `appsettings.json` → `config/local.json` → environment variables.
  - `config/local.json` is looked up at `<contentRoot>/config`, at `<repo>/config` (`dotnet run` from `src/Web`) and at `$DEVAGENTS_CONFIG`.
  - Everything reads `IOptionsMonitor.CurrentValue`, so changes apply without a restart.
- Model resolution: request override → `Ollama:Agents:<Role>` → fallback role (Router/BackTranslator → Translator) → `Defaults`.
- Every request sends `think: false` unless an agent sets `Think: true`. Thinking models such as qwen3.5 otherwise think for tens of seconds and can return an empty answer. Non-thinking models accept `false` but reject `true`.
- An empty `Workspace:AllowedRoots` allows any local path. Docker sets it to `/workspace`.

### Tests
- `Application.UnitTests/Fakes`: `PipelineHarness` wires `AgentPipeline` + `DevelopmentWorkflow` with fakes. `FakeLanguageModel` answers per prompt name, because `FakePromptProvider` returns the prompt name as the system prompt.
- `Application.FunctionalTests`: `DevAgentsApplicationFactory` uses a temp workspace and data dir, a scripted model, and a sandbox that always succeeds. The real writer and git are used.
- `Infrastructure.IntegrationTests` use real git, Roslyn, metadata reading and dotnet. Docker tests are `[Category("Slow")]`.

`projects/CleanArchitecture` is a git submodule gitlink (there is no `.gitmodules`). It is the template source for the clean-architecture profile, not part of this app. The template itself uses MediatR 14 and AutoMapper 16, which have commercial licenses, so projects generated from it inherit those.
