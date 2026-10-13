using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Context;

namespace DevAgents.Application.Common.Interfaces;

/// <summary>Read-only access to the project folders the agents may look at.</summary>
public interface IWorkspaceFileSystem
{
    /// <summary>
    /// Turns user input (an absolute path, or a folder name under the first allowed root) into a full path.
    /// </summary>
    /// <exception cref="InvalidRequestException">The path is outside the allowed roots or does not exist.</exception>
    string ResolveProjectPath(string path);

    /// <summary>
    /// Lists the files under <paramref name="root"/> that pass <paramref name="filter"/>.
    /// Reading a returned file yields an empty string if it can no longer be read.
    /// </summary>
    IReadOnlyList<ProjectFile> EnumerateFiles(string root, ProjectFileFilter filter);

    /// <summary>The sub-folders of <paramref name="roots"/>, i.e. the projects a user can pick.</summary>
    IReadOnlyList<WorkspaceProject> ListProjects(IEnumerable<string> roots);
}
