using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using DevAgents.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.SystemInfo.Queries.GetConfiguration;

/// <summary>
/// Şu an geçerli ayarlar: her ajanın modeli, akış/çalışma alanı/geliştirme ayarları ve derleme sandbox'ının durumu
/// (dosyalar değişince yeniden okunur).
/// </summary>
public record GetConfigurationQuery : IRequest<ConfigurationDto>;

public class GetConfigurationQueryHandler : IRequestHandler<GetConfigurationQuery, ConfigurationDto>
{
    private readonly ILanguageModelClient _languageModel;
    private readonly IAgentModelResolver _modelResolver;
    private readonly IBuildSandbox _sandbox;
    private readonly IOptionsMonitor<PipelineOptions> _pipelineOptions;
    private readonly IOptionsMonitor<WorkspaceOptions> _workspaceOptions;
    private readonly IOptionsMonitor<DevelopmentOptions> _developmentOptions;

    public GetConfigurationQueryHandler(
        ILanguageModelClient languageModel,
        IAgentModelResolver modelResolver,
        IBuildSandbox sandbox,
        IOptionsMonitor<PipelineOptions> pipelineOptions,
        IOptionsMonitor<WorkspaceOptions> workspaceOptions,
        IOptionsMonitor<DevelopmentOptions> developmentOptions)
    {
        _languageModel = languageModel;
        _modelResolver = modelResolver;
        _sandbox = sandbox;
        _pipelineOptions = pipelineOptions;
        _workspaceOptions = workspaceOptions;
        _developmentOptions = developmentOptions;
    }

    public async Task<ConfigurationDto> Handle(GetConfigurationQuery request, CancellationToken cancellationToken)
    {
        // Tüm ajanlar akıştaki sırayla listelenir (AgentRole enum sırası).
        var agents = Enum.GetValues<AgentRole>().Select(role => _modelResolver.Resolve(role)).ToList();
        // Arayüz, "projeye uygula" seçeneğini kullanmadan önce Docker'ın çalışıp çalışmadığını buradan öğrenir.
        var sandbox = await _sandbox.GetStatusAsync(cancellationToken);
        return new ConfigurationDto(
            _languageModel.Endpoint, agents, _pipelineOptions.CurrentValue, _workspaceOptions.CurrentValue,
            _developmentOptions.CurrentValue, sandbox);
    }
}

public record ConfigurationDto(
    string Endpoint,
    IReadOnlyList<AgentModel> Agents,
    PipelineOptions Pipeline,
    WorkspaceOptions Workspace,
    DevelopmentOptions Development,
    SandboxStatus Sandbox);
