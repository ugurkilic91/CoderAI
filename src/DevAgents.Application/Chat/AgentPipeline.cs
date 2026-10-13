using System.Diagnostics;
using DevAgents.Application.Chat.Parsing;
using DevAgents.Application.Common.Exceptions;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Context;
using DevAgents.Domain.Agents;
using DevAgents.Domain.Files;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.Chat;

/// <summary>
/// Turkish question → English → Router (decides which agents are needed) →
/// [Answer | Analyzer → Coder → (Tester → Reviewer → Coder revision loop)] → Turkish result.
/// Every agent has its own model, temperature and context size (see <see cref="IAgentModelResolver"/>).
/// </summary>
internal sealed partial class AgentPipeline(
    ILanguageModelClient languageModel,
    IAgentModelResolver modelResolver,
    IPromptProvider prompts,
    IProjectContextBuilder contextBuilder,
    IOptionsMonitor<PipelineOptions> pipelineOptions,
    ILogger<AgentPipeline> logger) : IAgentPipeline
{
    // Upper bound for a request's MaxReviewIterations: each round is another code → test → review cycle.
    private const int MaxReviewIterationsLimit = 5;

    public async Task<PipelineResult> RunAsync(ChatRequest request, Func<PipelineEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new InvalidRequestException("Soru boş olamaz.");

        var settings = pipelineOptions.CurrentValue;
        var options = request.Options;
        var maxRevisions = Math.Clamp(options?.MaxReviewIterations ?? settings.MaxReviewIterations, 0, MaxReviewIterationsLimit);
        var translateOutput = options?.TranslateOutput ?? settings.TranslateOutput;
        var stopwatch = Stopwatch.StartNew();
        var agents = new AgentRunner(languageModel, modelResolver, prompts, options?.ModelOverrides, onEvent, cancellationToken);

        // 1) Turkish → English
        var english = await agents.RunStageAsync(
            PipelineStages.TranslateIn, AgentRole.Translator, AgentPrompts.TranslatorToEnglish, request.Question);
        if (string.IsNullOrWhiteSpace(english))
            english = request.Question;

        // 2) Which agents does this request need?
        var plan = await SelectPlanAsync(request, english, agents);
        await onEvent(PipelineEvent.PlanSelected(plan));

        // 3) Project context (skipped for generic questions that do not need it)
        var context = plan.NeedsContext ? await BuildContextAsync(request, english, onEvent) : ProjectContext.None;
        var messages = new AgentMessages(english, context, request.ExtraContext, request.History, settings.MaxHistoryItems);

        // 4) The agents
        var work = new AgentOutputs();
        if (plan.Answer)
            work.Answer = await agents.RunStageAsync(PipelineStages.Answer, AgentRole.Analyzer, AgentPrompts.Answerer, messages.ForAnswer());
        if (plan.Analyze)
            work.Analysis = await agents.RunStageAsync(PipelineStages.Analyze, AgentRole.Analyzer, AgentPrompts.Analyzer, messages.ForAnalysis());
        if (plan.Code)
            work.Code = await agents.RunStageAsync(PipelineStages.Code, AgentRole.Coder, AgentPrompts.Coder, messages.ForCoding(work));
        await TestAndReviewAsync(plan, work, messages, agents, maxRevisions);

        // 5) English → Turkish report
        var report = await PipelineReport.ComposeAsync(work, translateOutput, agents);
        List<GeneratedFile> files =
        [
            .. GeneratedFileParser.Parse(work.Code, GeneratedFileKind.Code),
            .. GeneratedFileParser.Parse(work.Tests, GeneratedFileKind.Test)
        ];

        LogCompleted(logger, Math.Round(stopwatch.Elapsed.TotalSeconds, 1), plan.Intent, work.Approved, work.ReviewRounds, files.Count);
        return new PipelineResult(
            english, work.Analysis, work.Code, work.Tests, work.Review, work.Approved, work.ReviewRounds,
            report, files, context.ToInfo(), agents.UsedModels, stopwatch.Elapsed.TotalSeconds, plan);
    }

    /// <summary>Stages chosen by the user win over "full" mode, which wins over the Router's decision.</summary>
    private async Task<AgentPlan> SelectPlanAsync(ChatRequest request, string english, AgentRunner agents)
    {
        var options = request.Options;
        if (options?.Stages is { Count: > 0 } stages)
        {
            bool Has(string stage) => stages.Contains(stage, StringComparer.OrdinalIgnoreCase);
            return AgentPlan.Manual(
                Has(PipelineStages.Answer), Has(PipelineStages.Analyze), Has(PipelineStages.Code),
                Has(PipelineStages.Test), Has(PipelineStages.Review), "Aşamalar elle seçildi.");
        }

        if (string.Equals(options?.Mode, RunModes.Full, StringComparison.OrdinalIgnoreCase))
            return AgentPlan.Full("Tam pipeline seçildi.");

        var hasProject = !string.IsNullOrWhiteSpace(request.ProjectPath) || request.Files?.Count > 0;
        var decision = await agents.RunStageAsync(
            PipelineStages.Route, AgentRole.Router, AgentPrompts.Router, AgentMessages.ForRouter(english, hasProject));
        if (RouterDecisionParser.Parse(decision) is { } plan)
            return plan;

        // When in doubt, do more rather than less.
        LogRouterFallback(logger);
        return AgentPlan.Full("Router kararı okunamadı; tam pipeline çalıştırıldı.");
    }

    private async Task<ProjectContext> BuildContextAsync(ChatRequest request, string english, Func<PipelineEvent, Task> onEvent)
    {
        await onEvent(PipelineEvent.StageStarted(PipelineStages.Context, request.ProjectPath));
        var stopwatch = Stopwatch.StartNew();
        // The original wording is searched as well: identifiers can change in translation.
        var context = contextBuilder.Build(request.ProjectPath, request.Files, $"{english} {request.Question} {request.ExtraContext}");
        await onEvent(PipelineEvent.StageEnded(PipelineStages.Context, stopwatch.Elapsed.TotalSeconds, context.Warning, context.ToInfo()));
        return context;
    }

    /// <summary>
    /// Test → review, then revise → test → review again while the reviewer asks for changes,
    /// the plan includes coding and revisions are left.
    /// </summary>
    private static async Task TestAndReviewAsync(AgentPlan plan, AgentOutputs work, AgentMessages messages, AgentRunner agents, int maxRevisions)
    {
        while (plan.Test || plan.Review)
        {
            if (plan.Test)
                work.Tests = await agents.RunStageAsync(PipelineStages.Test, AgentRole.Tester, AgentPrompts.Tester, messages.ForTesting(work));
            if (!plan.Review)
                return;

            work.Review = await agents.RunStageAsync(PipelineStages.Review, AgentRole.Reviewer, AgentPrompts.Reviewer, messages.ForReview(work));
            work.Approved = ReviewVerdictParser.IsApproved(work.Review);
            if (work.Approved || !plan.Code || work.ReviewRounds >= maxRevisions)
                return;

            work.ReviewRounds++;
            work.Code = await agents.RunStageAsync(PipelineStages.Revise, AgentRole.Coder, AgentPrompts.Coder, messages.ForRevision(work));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Router output could not be parsed; running the full pipeline.")]
    private static partial void LogRouterFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Pipeline finished in {Seconds} s (intent: {Intent}, approved: {Approved}, review rounds: {ReviewRounds}, files: {FileCount})")]
    private static partial void LogCompleted(ILogger logger, double seconds, string intent, bool approved, int reviewRounds, int fileCount);
}
