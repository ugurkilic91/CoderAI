namespace DevAgents.Application.Common.Models;

/// <summary>The model and sampling settings an agent runs with.</summary>
/// <param name="Name">The agent (<see cref="Domain.Agents.AgentRole"/> name).</param>
/// <param name="NumCtx">Context window in tokens.</param>
/// <param name="NumPredict">Maximum number of tokens to generate.</param>
public sealed record AgentModel(string Name, string Model, double Temperature, int NumCtx, int NumPredict);
