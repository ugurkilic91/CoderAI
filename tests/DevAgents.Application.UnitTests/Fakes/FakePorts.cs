using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Context;
using DevAgents.Domain.Agents;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.UnitTests.Fakes;

/// <summary>Uses the prompt name as the system prompt, so tests can tell the agents apart.</summary>
internal sealed class FakePromptProvider : IPromptProvider
{
    public string GetPrompt(string name) => name;
}

/// <summary>Every agent runs "model-&lt;agent&gt;" unless the request overrides it.</summary>
internal sealed class FakeModelResolver : IAgentModelResolver
{
    public AgentModel Resolve(AgentRole role, IReadOnlyDictionary<string, string>? modelOverrides = null)
    {
        var name = role.ToString();
        var model = modelOverrides is not null && modelOverrides.TryGetValue(name, out var overridden) ? overridden : $"model-{name.ToLowerInvariant()}";
        return new AgentModel(name, model, 0.1, 1000, 100);
    }
}

/// <summary>An in-memory project folder; "missing" is the only path that cannot be resolved.</summary>
internal sealed class FakeWorkspace : IWorkspaceFileSystem
{
    private readonly List<(string Path, string Content)> _files = [];

    public FakeWorkspace With(string path, string content)
    {
        _files.Add((path, content));
        return this;
    }

    public string ResolveProjectPath(string path) =>
        path == "missing" ? throw new InvalidRequestException("Proje klasörü bulunamadı: missing") : "/projects/" + path;

    public IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter) =>
        [.. _files.Where(file => filter.Accepts(file.Path)).Take(filter.MaxFiles).Select(file => ProjectFile.FromContent(file.Path, file.Content))];

    public IReadOnlyList<WorkspaceProject> ListProjects(IEnumerable<string> roots) =>
        [.. roots.Select(root => new WorkspaceProject("App", root + "/App"))];
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
