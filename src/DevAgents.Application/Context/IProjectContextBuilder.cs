using DevAgents.Application.Common.Exceptions;

namespace DevAgents.Application.Context;

/// <summary>Builds the project context for a request.</summary>
public interface IProjectContextBuilder
{
    /// <summary>
    /// Indexes the project folder and/or the inline files, then selects what fits the context budget,
    /// ranked by relevance to <paramref name="query"/>.
    /// </summary>
    /// <exception cref="InvalidRequestException">The project path is outside the allowed roots or does not exist.</exception>
    ProjectContext Build(string? projectPath, IEnumerable<InlineFile>? inlineFiles, string query);
}
