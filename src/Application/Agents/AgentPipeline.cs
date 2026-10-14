using System.Diagnostics;
using DevAgents.Application.Agents.Commands.RunAgentPipeline;
using DevAgents.Application.Agents.Development;
using DevAgents.Application.Agents.Parsing;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using DevAgents.Application.Common.Text;
using DevAgents.Application.Context;
using DevAgents.Domain.Enums;
using DevAgents.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.Agents;

/// <summary>
/// Çok ajanlı akış:
/// Türkçe soru → (gerekiyorsa) İngilizce → Router (hangi ajanlar gerekli?) →
/// [Cevap | Analiz → (gerekirse kullanıcıya soru) → Kodlama → Test → Review] → Türkçe rapor.
/// "Projeye uygula" seçildiyse kod proje klasörüne yazılır, Docker'da derlenir ve test edilir (bkz. <see cref="DevelopmentWorkflow"/>).
/// Her ajanın kendi modeli, sıcaklığı ve bağlam boyutu vardır (bkz. <see cref="IAgentModelResolver"/>).
/// </summary>
internal sealed partial class AgentPipeline(
    ILanguageModelClient languageModel,
    IAgentModelResolver modelResolver,
    IPromptProvider prompts,
    IProjectContextBuilder contextBuilder,
    IArchitectureCatalog architectures,
    IWorkspaceFileSystem workspace,
    DevelopmentWorkflow development,
    IOptionsMonitor<PipelineOptions> pipelineOptions,
    IOptionsMonitor<DevelopmentOptions> developmentOptions,
    ILogger<AgentPipeline> logger) : IAgentPipeline
{
    // İstekte izin verilen en fazla düzeltme turu: her tur yeniden kod → test → review demek.
    private const int MaxReviewIterationsLimit = 5;

    public async Task<PipelineResult> RunAsync(RunAgentPipelineCommand request, Func<PipelineEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        // Doğrulama MediatR'da yapılıyor; burası motoru doğrudan çağıranlara karşı son güvence.
        Guard.Against.NullOrWhiteSpace(request.Question);

        var settings = pipelineOptions.CurrentValue;
        var options = request.Options;
        var maxRevisions = Math.Clamp(options?.MaxReviewIterations ?? settings.MaxReviewIterations, 0, MaxReviewIterationsLimit);
        var translateOutput = options?.TranslateOutput ?? settings.TranslateOutput;
        var askQuestions = options?.AskQuestions ?? false;
        var stopwatch = Stopwatch.StartNew();
        var agents = new AgentRunner(languageModel, modelResolver, prompts, options?.ModelOverrides, onEvent, cancellationToken);
        var architecture = string.IsNullOrWhiteSpace(request.Architecture) ? null : architectures.Find(request.Architecture);

        // 1) Türkçe → İngilizce: yerel modeller İngilizce istemlerle daha iyi kod ürettiği için çeviriyoruz.
        //    İstek zaten İngilizceyse çevirmeni hiç çağırmıyoruz.
        var english = request.Question;
        if (!settings.DetectInputLanguage || TurkishText.IsLikelyTurkish(request.Question))
        {
            english = await agents.RunStageAsync(
                PipelineStages.TranslateIn, AgentRole.Translator, AgentPrompts.TranslatorToEnglish, request.Question);
            if (string.IsNullOrWhiteSpace(english))
                english = request.Question;
        }

        // 2) Yeni proje istendiyse seçilen mimarinin şablonundan oluşturuyoruz; kod bu projeye yazılacak.
        var projectPath = request.ProjectPath;
        var projectCreated = false;
        if (request.ApplyChanges && !string.IsNullOrWhiteSpace(request.NewProjectName) && architecture is not null)
        {
            projectPath = await development.CreateProjectAsync(agents, architecture, request.ProjectPath, request.NewProjectName, cancellationToken);
            projectCreated = true;
        }

        // 3) Bu istek için hangi ajanlar gerekli? (kullanıcı seçimi, "tam" mod ya da Router kararı)
        var plan = await SelectPlanAsync(request, english, agents);
        await onEvent(PipelineEvent.PlanSelected(plan));

        // 4) Proje bağlamı ve mimari rehberi (kurallar + örnek dosyalar).
        var projectRoot = string.IsNullOrWhiteSpace(projectPath) ? null : workspace.ResolveProjectPath(projectPath);
        var guide = architecture is null ? ArchitectureGuide.None : ArchitectureGuide.Build(architecture, projectRoot, workspace);
        var context = plan.NeedsContext
            ? await BuildContextAsync(request, projectPath, english, guide, onEvent)
            : ProjectContext.None;
        var messages = new AgentMessages(english, context, request.ExtraContext, request.History, settings.MaxHistoryItems, guide);

        // 5) Ajanlar: düz soruysa cevap ajanı; geliştirme isteğiyse analiz → kodlama.
        var work = new AgentOutputs();
        if (plan.Answer)
            work.Answer = await agents.RunStageAsync(PipelineStages.Answer, AgentRole.Analyzer, AgentPrompts.Answerer, messages.ForAnswer());
        if (plan.Analyze)
            work.Analysis = await agents.RunStageAsync(PipelineStages.Analyze, AgentRole.Analyzer, AgentPrompts.Analyzer, messages.ForAnalysis());

        // Soru-cevap: analiz ajanı kritik bir bilgi eksik gördüyse kod yazmadan önce kullanıcıya soruyoruz.
        var questions = askQuestions && plan.Code ? AnalysisQuestions.Extract(work.Analysis) : "";

        DevelopmentOutcome? outcome = null;
        if (questions.Length > 0)
        {
            // Kod yazılmıyor; kullanıcının cevabı bir sonraki istekte (geçmişle birlikte) gelecek.
        }
        else if (request.ApplyChanges && plan.Code && projectRoot is not null)
        {
            // Kod projeye uygulanacak: yaz → derle → test et → (gerekirse) düzelt.
            outcome = await development.RunAsync(new DevelopmentRun(
                agents, messages, work, plan, projectRoot, projectCreated, architecture, cancellationToken));
        }
        else
        {
            if (plan.Code)
                work.Code = await agents.RunStageAsync(PipelineStages.Code, AgentRole.Coder, AgentPrompts.Coder, messages.ForCoding(work));
            await TestAndReviewAsync(plan, work, messages, agents, maxRevisions);
        }

        // 6) İngilizce → Türkçe rapor ve dosyalar.
        var report = await PipelineReport.ComposeAsync(work, translateOutput, agents,
            new ReportExtras(questions, outcome?.Summary, outcome?.CoderNotes ?? ""));
        IReadOnlyList<GeneratedFile> files = outcome?.Files ??
        [
            .. GeneratedFileParser.Parse(work.Code, GeneratedFileKind.Code),
            .. GeneratedFileParser.Parse(work.Tests, GeneratedFileKind.Test)
        ];

        LogCompleted(logger, Math.Round(stopwatch.Elapsed.TotalSeconds, 1), plan.Intent, work.Approved, work.ReviewRounds, files.Count);
        return new PipelineResult(
            english, work.Analysis, work.Code, work.Tests, work.Review, work.Approved, work.ReviewRounds,
            report, files, context.ToInfo(), agents.UsedModels, stopwatch.Elapsed.TotalSeconds, plan,
            questions, outcome?.Summary);
    }

    /// <summary>Öncelik: kullanıcının seçtiği aşamalar → "tam" mod → Router ajanının kararı.</summary>
    private async Task<AgentPlan> SelectPlanAsync(RunAgentPipelineCommand request, string english, AgentRunner agents)
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

        // Router ajanına isteği ve proje verilip verilmediğini söylüyoruz; o da JSON ile plan döndürüyor.
        var hasProject = !string.IsNullOrWhiteSpace(request.ProjectPath) || request.Files?.Count > 0;
        var decision = await agents.RunStageAsync(
            PipelineStages.Route, AgentRole.Router, AgentPrompts.Router, AgentMessages.ForRouter(english, hasProject));
        if (RouterDecisionParser.Parse(decision) is { } plan)
            return plan;

        // Karar okunamadıysa az değil çok yapalım: tam akış.
        LogRouterFallback(logger);
        return AgentPlan.Full("Router kararı okunamadı; tam pipeline çalıştırıldı.");
    }

    private async Task<ProjectContext> BuildContextAsync(
        RunAgentPipelineCommand request, string? projectPath, string english, ArchitectureGuide guide, Func<PipelineEvent, Task> onEvent)
    {
        await onEvent(PipelineEvent.StageStarted(PipelineStages.Context, projectPath));
        var stopwatch = Stopwatch.StartNew();
        // Uygulama modunda mimari örnekleri ayrıca verildiği için proje dosyalarına daha küçük bir bütçe ayırıyoruz;
        // örnek olarak verilen dosyaları da ikinci kez eklemiyoruz.
        var buildOptions = request.ApplyChanges || guide.ExamplePaths.Count > 0
            ? new ContextBuildOptions(request.ApplyChanges ? developmentOptions.CurrentValue.MaxContextChars : null, guide.ExamplePaths)
            : null;
        // Orijinal (Türkçe) metinde de arıyoruz: çeviride kod tanımlayıcıları değişebilir.
        var context = contextBuilder.Build(projectPath, request.Files, $"{english} {request.Question} {request.ExtraContext}", buildOptions);
        await onEvent(PipelineEvent.StageEnded(PipelineStages.Context, stopwatch.Elapsed.TotalSeconds, context.Warning, context.ToInfo()));
        return context;
    }

    /// <summary>
    /// Test → review; review değişiklik isterse, plan kodlama içeriyorsa ve tur hakkı kaldıysa
    /// kodlama ajanı düzeltir, ardından yeniden test → review.
    /// </summary>
    private static async Task TestAndReviewAsync(AgentPlan plan, AgentOutputs work, AgentMessages messages, AgentRunner agents, int maxRevisions)
    {
        while (plan.Test || plan.Review)
        {
            // Burada test agent'a ihtiyaç duyduk: plan test istiyorsa test agent'ı çalıştırıyoruz.
            if (plan.Test)
                work.Tests = await agents.RunStageAsync(PipelineStages.Test, AgentRole.Tester, AgentPrompts.Tester, messages.ForTesting(work));
            if (!plan.Review)
                return;

            work.Review = await agents.RunStageAsync(PipelineStages.Review, AgentRole.Reviewer, AgentPrompts.Reviewer, messages.ForReview(work));
            work.Approved = ReviewVerdictParser.IsApproved(work.Review);
            if (work.Approved || !plan.Code || work.ReviewRounds >= maxRevisions)
                return;

            // Review değişiklik istedi: kodlama ajanı önceki kodu ve review notlarını alıp düzeltiyor.
            work.ReviewRounds++;
            work.Code = await agents.RunStageAsync(PipelineStages.Revise, AgentRole.Coder, AgentPrompts.Coder, messages.ForRevision(work));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Router çıktısı okunamadı; tam akış çalıştırılıyor.")]
    private static partial void LogRouterFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Akış {Seconds} sn'de bitti (amaç: {Intent}, onay: {Approved}, düzeltme turu: {ReviewRounds}, dosya: {FileCount})")]
    private static partial void LogCompleted(ILogger logger, double seconds, string intent, bool approved, int reviewRounds, int fileCount);
}
