using System.Diagnostics;
using DevAgents.Application.Agents.Parsing;
using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using DevAgents.Application.Common.Options;
using DevAgents.Domain.Enums;
using DevAgents.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevAgents.Application.Agents.Development;

/// <summary>Bir geliştirme çalıştırmasının girdileri.</summary>
internal sealed record DevelopmentRun(
    AgentRunner Agents,
    AgentMessages Messages,
    AgentOutputs Work,
    AgentPlan Plan,
    string ProjectRoot,
    bool ProjectCreated,
    ArchitectureProfile? Architecture,
    CancellationToken CancellationToken);

/// <summary>Geliştirme akışının sonucu: özet, projeye yazılan dosyaların son hâlleri ve kodlama ajanının notları.</summary>
internal sealed record DevelopmentOutcome(DevelopmentSummary Summary, IReadOnlyList<GeneratedFile> Files, string CoderNotes);

/// <summary>
/// Kodu projeye uygulayan akış (ApplyChanges):
/// kodlama ajanı yazar → Roslyn sözdizimi kontrolü → projenin anlık görüntüsü (yedek) → dosyalar projeye yazılır →
/// Docker'da derlenir (hata varsa düzeltme ajanı düzeltir) → test ajanı testleri yazar → testler derlenip çalıştırılır
/// (başarısızsa düzeltilir) → isteğe bağlı review. Her adım istemciye canlı olay olarak bildirilir.
/// </summary>
internal sealed partial class DevelopmentWorkflow(
    IProjectWriter writer,
    IChangeTracker changeTracker,
    ICodeSyntaxChecker syntaxChecker,
    IBuildSandbox sandbox,
    IWorkspaceFileSystem workspace,
    ProjectLocks locks,
    IOptionsMonitor<DevelopmentOptions> options,
    ILogger<DevelopmentWorkflow> logger)
{
    private const int MaxSyntaxFixAttempts = 2;

    /// <summary>
    /// Seçilen mimarinin şablonundan yeni proje oluşturur ("scaffold" aşaması) ve proje kökünü döndürür.
    /// </summary>
    public async Task<string> CreateProjectAsync(
        AgentRunner agents, ArchitectureProfile architecture, string? parentPath, string projectName, CancellationToken cancellationToken)
    {
        var projectRoot = workspace.ResolveNewProjectDirectory(parentPath, projectName);
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Scaffold, $"{architecture.Name}: {projectName}"));
        var stopwatch = Stopwatch.StartNew();
        // Şablonun komutunu (örn. dotnet new ca-sln) Docker sandbox'ta çalıştırıyoruz.
        var created = await sandbox.CreateProjectAsync(
            architecture, Path.GetDirectoryName(projectRoot)!, projectName,
            line => agents.PublishAsync(PipelineEvent.Token(PipelineStages.Scaffold, line + "\n")), cancellationToken);
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Scaffold, stopwatch.Elapsed.TotalSeconds, created));
        return created;
    }

    public async Task<DevelopmentOutcome> RunAsync(DevelopmentRun run)
    {
        // Aynı projede aynı anda tek bir yazma işi çalışabilir.
        using var projectLock = locks.Acquire(run.ProjectRoot);
        var state = new DevelopmentState(run);

        // 1) Kodlama ajanı, mimari kurallarına ve örnek dosyalara bakarak kodu yazıyor.
        run.Work.Code = await run.Agents.RunStageAsync(PipelineStages.Code, AgentRole.Coder, AgentPrompts.Coder, run.Messages.ForCoding(run.Work));
        var notes = MarkdownSections.Read(run.Work.Code, "NOTES");
        var codeFiles = GeneratedFileParser.Parse(run.Work.Code, GeneratedFileKind.Code).ToList();
        if (codeFiles.Count == 0)
            return state.Stop("Kodlama ajanı ayrıştırılabilir bir dosya üretmedi (\"### FILE:\" biçimi); projeye bir şey yazılmadı.", notes);

        // 2) Yazmadan önce projenin anlık görüntüsünü alıyoruz: tüm değişiklikler tek adımda geri alınabilsin.
        state.Snapshot = await changeTracker.SnapshotAsync(run.ProjectRoot, "DevAgents: değişikliklerden önce", run.CancellationToken);

        // 3) Kodu yazıp derliyoruz; derleme hatası varsa düzeltme ajanına verip yeniden derliyoruz.
        var build = await WriteAndBuildAsync(state, codeFiles);

        // 4) Burada test agent'a ihtiyaç duyduk: kod derleniyorsa test agent birim testlerini yazıyor, testleri çalıştırıyoruz.
        if (build.Succeeded && run.Plan.Test)
            await WriteAndRunTestsAsync(state);
        else if (!build.Succeeded)
            state.Problem ??= "Kod, düzeltme turlarına rağmen derlenemedi; testler çalıştırılmadı.";

        // 5) Review (bilgi amaçlı): kod derlenip testlerden geçtiği için otomatik düzeltme turu yapmıyoruz.
        if (run.Plan.Review && state.Build?.Succeeded == true)
            await ReviewAsync(state);

        // 6) Anlık görüntüden bu yana projede değişen her şey: rapor ve geri alma bunu kullanır.
        var changes = await changeTracker.GetChangesAsync(run.ProjectRoot, state.Snapshot, run.CancellationToken);
        LogCompleted(logger, run.ProjectRoot, changes.Count, state.Build?.Succeeded == true, state.BuildAttempts,
            state.Tests?.Passed ?? 0, state.Tests?.Total ?? 0);
        return state.Complete(changes, notes);
    }

    /// <summary>Dosyaları Roslyn'den geçirir, projeye yazar ve derler; derlenmezse düzeltme turları yapar.</summary>
    private async Task<BuildResult> WriteAndBuildAsync(DevelopmentState state, IReadOnlyList<GeneratedFile> files)
    {
        var settings = options.CurrentValue;
        files = await FixSyntaxAsync(state, files);
        await WriteAsync(state, files);

        for (var fix = 0; ; fix++)
        {
            var build = await BuildAsync(state);
            if (build.Succeeded || fix >= settings.MaxBuildFixAttempts)
                return build;

            // Derleme hatalarını ve ilgili dosyaların diskteki güncel hâlini düzeltme ajanına veriyoruz.
            var errors = build.Errors.Take(settings.MaxErrorsPerFix).ToList();
            var fixes = await FixAsync(state, state.Run.Messages.ForBuildFix(errors, FilesFor(state, errors.Select(e => e.File)), state.Written.Keys));
            if (fixes.Count == 0)
                return build; // ajan bir düzeltme önermedi: aynı derlemeyi tekrarlamanın anlamı yok

            fixes = await FixSyntaxAsync(state, fixes);
            await WriteAsync(state, fixes);
        }
    }

    /// <summary>
    /// Roslyn ile sözdizimi kontrolü: hata varsa dosyaları derlemeye göndermeden önce düzelttiriyoruz
    /// (sözdizimi kontrolü saniyeler, derleme dakikalar sürer).
    /// </summary>
    private async Task<IReadOnlyList<GeneratedFile>> FixSyntaxAsync(DevelopmentState state, IReadOnlyList<GeneratedFile> files)
    {
        for (var attempt = 0; ; attempt++)
        {
            await state.Run.Agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Syntax, "Roslyn"));
            var stopwatch = Stopwatch.StartNew();
            var errors = syntaxChecker.Check(files);
            await state.Run.Agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Syntax, stopwatch.Elapsed.TotalSeconds,
                errors.Count == 0 ? "temiz" : $"{errors.Count} sözdizimi hatası", errors));
            if (errors.Count == 0 || attempt >= MaxSyntaxFixAttempts)
                return files;

            var fixes = await FixAsync(state, state.Run.Messages.ForSyntaxFix(errors, files));
            if (fixes.Count == 0)
                return files;
            // Düzeltilen dosyalar eskilerinin yerine geçer, diğerleri olduğu gibi kalır.
            var fixedPaths = fixes.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            files = [.. files.Where(f => !fixedPaths.Contains(f.Path)), .. fixes];
        }
    }

    /// <summary>Dosyaları projeye yazar ve hangilerinin yeni, hangilerinin değişmiş olduğunu bildirir.</summary>
    private async Task WriteAsync(DevelopmentState state, IReadOnlyList<GeneratedFile> files)
    {
        var agents = state.Run.Agents;
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Write, $"{files.Count} dosya"));
        var stopwatch = Stopwatch.StartNew();
        var changes = await writer.WriteAsync(state.Run.ProjectRoot, files, state.Run.CancellationToken);
        foreach (var file in files)
            state.Written[file.Path] = file;
        foreach (var change in changes)
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.Write, $"{(change.Kind == FileChangeKind.Created ? "+" : "~")} {change.Path}\n"));
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Write, stopwatch.Elapsed.TotalSeconds, DescribeChanges(changes), changes));
    }

    /// <summary>Docker sandbox'ta derleme: paketler ağ açıkken geri yüklenir, derleme ağ kapalı yapılır.</summary>
    private async Task<BuildResult> BuildAsync(DevelopmentState state)
    {
        var agents = state.Run.Agents;
        state.BuildAttempts++;
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Build,
            state.BuildAttempts == 1 ? "dotnet build" : $"dotnet build ({state.BuildAttempts}. deneme)"));
        var build = await sandbox.BuildAsync(state.Run.ProjectRoot,
            line => agents.PublishAsync(PipelineEvent.Token(PipelineStages.Build, line + "\n")), state.Run.CancellationToken);
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Build, build.Seconds,
            build.Succeeded ? "başarılı" : $"{build.Errors.Count} hata",
            new { build.Succeeded, Errors = build.Errors.Take(20).ToList() }));
        state.Build = build;
        return build;
    }

    /// <summary>
    /// Test agent'ı çalıştırıyoruz: derlenen kod için mimarinin test kurallarına uygun birim testleri yazıyor.
    /// Testler derlenip çalıştırılıyor; başarısız olanlar düzeltme ajanına verilip yeniden deneniyor.
    /// </summary>
    private async Task WriteAndRunTestsAsync(DevelopmentState state)
    {
        var run = state.Run;
        var settings = options.CurrentValue;

        // Test ajanı, düzeltmelerden sonraki son kodu görsün.
        run.Work.Code = Render(state.FilesOfKind(GeneratedFileKind.Code));
        run.Work.Tests = await run.Agents.RunStageAsync(PipelineStages.Test, AgentRole.Tester, AgentPrompts.Tester, run.Messages.ForTesting(run.Work));
        var testFiles = GeneratedFileParser.Parse(run.Work.Tests, GeneratedFileKind.Test).ToList();
        if (testFiles.Count == 0)
        {
            state.Problem = "Test ajanı ayrıştırılabilir bir test dosyası üretmedi; testler çalıştırılmadı.";
            return;
        }

        var build = await WriteAndBuildAsync(state, testFiles);
        if (!build.Succeeded)
        {
            state.Problem = "Testler, düzeltme turlarına rağmen derlenemedi; testler çalıştırılmadı.";
            return;
        }

        for (var fix = 0; ; fix++)
        {
            var tests = await RunTestsAsync(state);
            if (tests.AllPassed() || tests.Total == 0 || fix >= settings.MaxTestFixAttempts)
            {
                if (tests.Total == 0)
                    state.Problem = "Test projelerinde çalıştırılacak test bulunamadı.";
                return;
            }

            // Başarısız testleri ve ilgili dosyaları düzeltme ajanına veriyoruz; kod mu test mi hatalı, o karar veriyor.
            var fixes = await FixAsync(state, run.Messages.ForTestFix(tests, FilesFor(state, [])));
            if (fixes.Count == 0)
                return;
            var rebuilt = await WriteAndBuildAsync(state, fixes);
            if (!rebuilt.Succeeded)
            {
                state.Problem = "Test düzeltmesinden sonra proje derlenemedi.";
                return;
            }
        }
    }

    /// <summary>Docker sandbox'ta testleri çalıştırır (ağ kapalı).</summary>
    private async Task<TestRunResult> RunTestsAsync(DevelopmentState state)
    {
        var agents = state.Run.Agents;
        var projects = state.Run.Architecture?.TestProjects ?? [];
        state.TestAttempts++;
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.TestRun,
            projects.Count > 0 ? $"dotnet test ({string.Join(", ", projects.Select(Path.GetFileName))})" : "dotnet test"));
        var tests = await sandbox.RunTestsAsync(state.Run.ProjectRoot, projects,
            line => agents.PublishAsync(PipelineEvent.Token(PipelineStages.TestRun, line + "\n")), state.Run.CancellationToken);
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.TestRun, tests.Seconds,
            tests.Total == 0 ? "test bulunamadı" : $"{tests.Passed}/{tests.Total} başarılı",
            new { tests.Total, tests.Passed, tests.Failed, tests.Skipped, Failures = tests.Failures.Take(20).ToList() }));
        state.Tests = tests;
        return tests;
    }

    /// <summary>Review ajanı son hâli inceler; kararı rapora girer (otomatik düzeltme yapılmaz).</summary>
    private static async Task ReviewAsync(DevelopmentState state)
    {
        var run = state.Run;
        run.Work.Code = Render(state.FilesOfKind(GeneratedFileKind.Code));
        run.Work.Tests = Render(state.FilesOfKind(GeneratedFileKind.Test));
        run.Work.Review = await run.Agents.RunStageAsync(PipelineStages.Review, AgentRole.Reviewer, AgentPrompts.Reviewer, run.Messages.ForReview(run.Work));
        run.Work.Approved = ReviewVerdictParser.IsApproved(run.Work.Review);
    }

    /// <summary>
    /// Düzeltme ajanı: hata çıktısını ve dosyaların güncel hâlini alıp yalnızca değiştirdiği dosyaların tam hâlini döndürüyor.
    /// Kodlama ajanının modeliyle çalışır.
    /// </summary>
    private static async Task<IReadOnlyList<GeneratedFile>> FixAsync(DevelopmentState state, string message)
    {
        var answer = await state.Run.Agents.RunStageAsync(PipelineStages.Fix, AgentRole.Coder, AgentPrompts.Fixer, message);
        return [.. GeneratedFileParser.Parse(answer, GeneratedFileKind.Code).Select(file => file with { Kind = state.KindOf(file.Path) })];
    }

    /// <summary>
    /// Düzeltme ajanına gidecek dosyalar: önce hata veren dosyalar (diskteki güncel hâlleri), ardından bu görevde yazılan
    /// diğer dosyalar. Böylece ajan hem hatanın yerini hem de çağrılan tipleri görür.
    /// </summary>
    private List<GeneratedFile> FilesFor(DevelopmentState state, IEnumerable<string> errorPaths)
    {
        var files = new List<GeneratedFile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in errorPaths.Concat(state.Written.Keys))
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
                continue;
            var content = workspace.ReadFile(state.Run.ProjectRoot, path);
            if (content is not null)
                files.Add(new GeneratedFile(path, content, state.KindOf(path)));
        }
        return files;
    }

    private static string Render(IEnumerable<GeneratedFile> files) =>
        string.Join("\n\n", files.Select(file => $"### FILE: {file.Path}\n```{(file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ? "csharp" : "")}\n{file.Content.TrimEnd()}\n```"));

    private static string DescribeChanges(IReadOnlyList<FileChange> changes)
    {
        var created = changes.Count(c => c.Kind == FileChangeKind.Created);
        var modified = changes.Count(c => c.Kind == FileChangeKind.Modified);
        return changes.Count == 0 ? "değişiklik yok" : $"{created} yeni, {modified} değişti";
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Geliştirme bitti: {ProjectRoot} ({ChangeCount} dosya değişti, derleme: {BuildSucceeded}, {BuildAttempts} derleme, testler {Passed}/{Total})")]
    private static partial void LogCompleted(ILogger logger, string projectRoot, int changeCount, bool buildSucceeded, int buildAttempts, int passed, int total);

    /// <summary>Bir geliştirme çalıştırmasının ilerleyen durumu.</summary>
    private sealed class DevelopmentState(DevelopmentRun run)
    {
        public DevelopmentRun Run { get; } = run;

        public string? Snapshot { get; set; }

        public BuildResult? Build { get; set; }

        public int BuildAttempts { get; set; }

        public TestRunResult? Tests { get; set; }

        public int TestAttempts { get; set; }

        public string? Problem { get; set; }

        /// <summary>Bu görevde projeye yazılan dosyaların son hâlleri (yola göre).</summary>
        public Dictionary<string, GeneratedFile> Written { get; } = new(StringComparer.Ordinal);

        public IEnumerable<GeneratedFile> FilesOfKind(string kind) => Written.Values.Where(file => file.Kind == kind);

        /// <summary>Daha önce yazılmış bir dosyanın türü; yeni dosyalarda yoldan tahmin edilir.</summary>
        public string KindOf(string path) =>
            Written.TryGetValue(path, out var file) ? file.Kind
            : path.Contains("test", StringComparison.OrdinalIgnoreCase) ? GeneratedFileKind.Test
            : GeneratedFileKind.Code;

        public DevelopmentOutcome Stop(string problem, string notes)
        {
            Problem = problem;
            return Complete([], notes);
        }

        public DevelopmentOutcome Complete(IReadOnlyList<FileChange> changes, string notes) => new(
            new DevelopmentSummary(Run.ProjectRoot, Run.Architecture?.Name, Run.ProjectCreated, Snapshot, changes,
                Build, BuildAttempts, Tests, TestAttempts, Problem),
            [.. Written.Values],
            notes);
    }
}
