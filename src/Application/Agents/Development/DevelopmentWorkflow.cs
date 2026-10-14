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
/// Docker'da derlenir (eksik using'ler otomatik eklenir, kalan hataları düzeltme ajanı düzeltir) → test ajanı testleri yazar →
/// testler derlenip çalıştırılır (başarısızsa düzeltilir) → isteğe bağlı review. Her adım istemciye canlı olay olarak bildirilir.
/// </summary>
internal sealed partial class DevelopmentWorkflow(
    IProjectWriter writer,
    IChangeTracker changeTracker,
    ICodeSyntaxChecker syntaxChecker,
    IUsingDirectiveFixer usingFixer,
    ICodeMemberRestorer memberRestorer,
    IBuildSandbox sandbox,
    IWorkspaceFileSystem workspace,
    ProjectLocks locks,
    IOptionsMonitor<DevelopmentOptions> options,
    ILogger<DevelopmentWorkflow> logger)
{
    private const int MaxSyntaxFixAttempts = 2;

    // Model kullanmayan düzeltme turları (using, silinen üye) düzeltme ajanı hakkından düşmez; yine de sınırlı.
    private const int MaxAutoFixRounds = 4;

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

        // Testleri test ajanı yazar (mimarinin test örneğiyle, kod derlendikten sonra). Kodlama ajanı yine de test dosyası
        // yazdıysa onları projeye almıyoruz: derlenmeyen bir test, derlenebilen kodu da derlenmez gösterir.
        // (Yalnızca test yazdıysa istenen buydur; o zaman dokunmuyoruz.)
        var skippedTests = run.Plan.Test ? codeFiles.Count(file => IsTestFile(file.Path)) : 0;
        if (skippedTests > 0 && skippedTests < codeFiles.Count)
        {
            codeFiles.RemoveAll(file => IsTestFile(file.Path));
            notes = $"{notes}\n- DevAgents: the {skippedTests} test file(s) written by the coding agent were skipped; tests are written by the test agent after the code compiles.".Trim();
        }

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
        files = await FixSyntaxAsync(state, files);
        await WriteAsync(state, files);
        return await BuildAndFixAsync(state);
    }

    /// <summary>
    /// Derler; hata varsa önce eksik using'leri model kullanmadan ekler, kalan hataları düzeltme ajanına verir ve yeniden derler.
    /// </summary>
    private async Task<BuildResult> BuildAndFixAsync(DevelopmentState state)
    {
        var settings = options.CurrentValue;
        var autoRounds = 0;
        var fixes = 0;
        while (true)
        {
            var build = await BuildAsync(state);
            if (build.Succeeded)
                return build;

            // a) Model gerektirmeyen düzeltmeler (saniyeler sürer): eksik using'ler (tipin namespace'i projeden ve paketlerden
            //    bulunur) ve kodlama ajanının değiştirdiği dosyalardan yanlışlıkla sildiği üyeler (dosyanın eski hâlinden).
            if (autoRounds < MaxAutoFixRounds
                && (await AddMissingUsingsAsync(state, build.Errors) | await RestoreLostMembersAsync(state, build.Errors)))
            {
                autoRounds++;
                continue;
            }

            if (fixes++ >= settings.MaxBuildFixAttempts)
                return build;

            // b) Kalan hataları ve ilgili dosyaların diskteki güncel hâlini düzeltme ajanına veriyoruz.
            var errors = build.Errors.Take(settings.MaxErrorsPerFix).ToList();
            var changed = await FixAndWriteAsync(state,
                state.Run.Messages.ForBuildFix(errors, FilesFor(state, errors.Select(e => e.File)), state.Written.Keys));
            if (changed == 0)
                return build; // ajan hiçbir dosyayı değiştirmedi: aynı derlemeyi tekrarlamanın anlamı yok
        }
    }

    /// <summary>
    /// Derleme hatalarındaki eksik using'leri ekler (IDE'deki "using ekle" gibi) ve dosyaları projeye yazar.
    /// Bir dosya değiştiyse true döner; o zaman yeniden derliyoruz.
    /// </summary>
    private async Task<bool> AddMissingUsingsAsync(DevelopmentState state, IReadOnlyList<CompilerDiagnostic> errors)
    {
        var agents = state.Run.Agents;
        var stopwatch = Stopwatch.StartNew();
        var fixes = await usingFixer.FixAsync(state.Run.ProjectRoot, errors, state.Run.CancellationToken);
        if (fixes.Count == 0)
            return false;

        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Usings, $"{fixes.Count} dosya"));
        foreach (var fix in fixes)
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.Usings,
                $"{fix.Path}: {string.Join(" ", fix.AddedNamespaces.Select(ns => $"using {ns};"))}\n"));
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Usings, stopwatch.Elapsed.TotalSeconds,
            $"{fixes.Sum(fix => fix.AddedNamespaces.Count)} using eklendi", fixes.Select(fix => new { fix.Path, fix.AddedNamespaces }).ToList()));

        state.UsingsAdded += fixes.Sum(fix => fix.AddedNamespaces.Count);
        var changes = await WriteAsync(state, [.. fixes.Select(fix => new GeneratedFile(fix.Path, fix.Content, state.KindOf(fix.Path)))]);
        return changes.Count > 0;
    }

    /// <summary>
    /// Kodlama ajanının değiştirdiği mevcut dosyalardan yanlışlıkla sildiği üyeleri (örn. IApplicationDbContext'teki diğer
    /// DbSet'ler) dosyanın bu görevden önceki hâlinden geri ekler. Yalnızca derleme hatasının aradığı üyelere dokunulur.
    /// </summary>
    private async Task<bool> RestoreLostMembersAsync(DevelopmentState state, IReadOnlyList<CompilerDiagnostic> errors)
    {
        var missing = MissingMembers.From(errors, state.Written.ContainsKey);
        if (missing.Count == 0 || state.Originals.Count == 0)
            return false;

        var stopwatch = Stopwatch.StartNew();
        var restores = new List<(string Path, MemberRestore Result)>();
        foreach (var (path, original) in state.Originals)
        {
            if (workspace.ReadFile(state.Run.ProjectRoot, path) is { } current && memberRestorer.Restore(original, current, missing) is { } result)
                restores.Add((path, result));
        }
        if (restores.Count == 0)
            return false;

        var agents = state.Run.Agents;
        var count = restores.Sum(restore => restore.Result.RestoredMembers.Count);
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Restore, $"{restores.Count} dosya"));
        foreach (var (path, result) in restores)
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.Restore, $"{path}: {string.Join(", ", result.RestoredMembers)}\n"));
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Restore, stopwatch.Elapsed.TotalSeconds, $"{count} üye geri eklendi",
            restores.Select(restore => new { restore.Path, restore.Result.RestoredMembers }).ToList()));

        state.MembersRestored += count;
        var changes = await WriteAsync(state, [.. restores.Select(restore => new GeneratedFile(restore.Path, restore.Result.Content, state.KindOf(restore.Path)))]);
        return changes.Count > 0;
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
    private async Task<IReadOnlyList<FileChange>> WriteAsync(DevelopmentState state, IReadOnlyList<GeneratedFile> files)
    {
        var agents = state.Run.Agents;
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Write, $"{files.Count} dosya"));
        var stopwatch = Stopwatch.StartNew();
        // Var olan bir dosyaya bu görevde ilk kez yazılıyorsa eski hâlini saklıyoruz (silinen üyeleri geri ekleyebilmek için).
        foreach (var file in files)
        {
            if (!state.Written.ContainsKey(file.Path) && !state.Originals.ContainsKey(file.Path)
                && workspace.ReadFile(state.Run.ProjectRoot, file.Path) is { } original)
                state.Originals[file.Path] = original;
        }
        var changes = await writer.WriteAsync(state.Run.ProjectRoot, files, state.Run.CancellationToken);
        foreach (var file in files)
            state.Written[file.Path] = file;
        foreach (var change in changes)
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.Write, $"{(change.Kind == FileChangeKind.Created ? "+" : "~")} {change.Path}\n"));
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Write, stopwatch.Elapsed.TotalSeconds, DescribeChanges(changes), changes));
        return changes;
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

        // Kod derlenmiş hâldeyken ikinci bir anlık görüntü: testler derlenemezse yalnızca test adımını geri alıp
        // projeyi derlenebilir bırakacağız (derlenen kod kalır).
        var beforeTests = await changeTracker.SnapshotAsync(run.ProjectRoot, "DevAgents: testlerden önce", run.CancellationToken);
        var writtenBeforeTests = new Dictionary<string, GeneratedFile>(state.Written, StringComparer.Ordinal);

        var build = await WriteAndBuildAsync(state, testFiles);
        if (!build.Succeeded)
        {
            await RollBackTestStepAsync(state, beforeTests, writtenBeforeTests,
                "Test ajanının yazdığı testler düzeltme turlarına rağmen derlenemedi; projenin derlenebilir kalması için test adımı geri alındı.");
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
            if (await FixAndWriteAsync(state, run.Messages.ForTestFix(tests, FilesFor(state, []))) == 0)
                return; // ajan hiçbir dosyayı değiştirmedi: aynı testleri yeniden çalıştırmanın anlamı yok
            var rebuilt = await BuildAndFixAsync(state);
            if (!rebuilt.Succeeded)
            {
                await RollBackTestStepAsync(state, beforeTests, writtenBeforeTests,
                    "Test düzeltmesinden sonra proje derlenemedi; projenin derlenebilir kalması için test adımı geri alındı.");
                return;
            }
        }
    }

    /// <summary>
    /// Test adımını (test dosyaları ve bu adımdaki düzeltmeler) geri alır ve projeyi yeniden derleyerek
    /// kodun derlenebilir kaldığını doğrular. Testlerin içeriği arayüzde "Test Agent" adımında görünmeye devam eder.
    /// </summary>
    private async Task RollBackTestStepAsync(
        DevelopmentState state, string snapshot, Dictionary<string, GeneratedFile> writtenBefore, string problem)
    {
        var agents = state.Run.Agents;
        await agents.PublishAsync(PipelineEvent.StageStarted(PipelineStages.Write, "test adımı geri alınıyor"));
        var stopwatch = Stopwatch.StartNew();
        var reverted = await changeTracker.RevertAsync(state.Run.ProjectRoot, snapshot, state.Run.CancellationToken);
        foreach (var change in reverted)
            await agents.PublishAsync(PipelineEvent.Token(PipelineStages.Write, $"↩ {change.Path}\n"));
        await agents.PublishAsync(PipelineEvent.StageEnded(PipelineStages.Write, stopwatch.Elapsed.TotalSeconds,
            $"{reverted.Count} dosya geri alındı", reverted));

        // Sonuçta artık projede olmayan dosyalar ve testler görünmesin: durum da test adımından önceki hâline döner.
        state.Tests = null;
        state.Written.Clear();
        foreach (var (path, file) in writtenBefore)
            state.Written[path] = file;

        var build = await BuildAsync(state);
        state.Problem = build.Succeeded ? problem : problem + " Geri almadan sonra da derleme başarısız.";
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
        // Review ajanına gerçek sonuçları veriyoruz: kod derlendi, testlerin durumu belli.
        var tests = state.Tests is { } result
            ? $"dotnet test: {result.Passed}/{result.Total} passed" +
              (result.Failed > 0 ? $"; failing: {string.Join(", ", result.Failures.Take(5).Select(failure => failure.Name))}" : "")
            : "dotnet test: not run";
        var verification = $"dotnet build: succeeded.\n{tests}";
        run.Work.Review = await run.Agents.RunStageAsync(PipelineStages.Review, AgentRole.Reviewer, AgentPrompts.Reviewer,
            run.Messages.ForReview(run.Work, verification));
        run.Work.Approved = ReviewVerdictParser.IsApproved(run.Work.Review);
    }

    /// <summary>
    /// Düzeltme ajanını çalıştırır, önerdiği dosyaları Roslyn'den geçirip projeye yazar; projede değişen dosya sayısını döndürür.
    /// </summary>
    private async Task<int> FixAndWriteAsync(DevelopmentState state, string message)
    {
        state.FixRounds++;
        var fixes = await FixAsync(state, message);
        if (fixes.Count == 0)
            return 0;
        fixes = await FixSyntaxAsync(state, fixes);
        return (await WriteAsync(state, fixes)).Count;
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

    // Test projesindeki dosyalar: "tests/..." altında ya da adı Tests ile biten bir klasörde (örn. Shop.UnitTests).
    private static bool IsTestFile(string path)
    {
        var folders = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1];
        return folders.Length > 0 && (folders[0] is "tests" or "test" or "Tests" or "Test"
            || folders.Any(folder => folder.EndsWith("Tests", StringComparison.Ordinal)));
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

        public int UsingsAdded { get; set; }

        public int MembersRestored { get; set; }

        /// <summary>Bu görevde değiştirilen ve önceden var olan dosyaların görevden önceki hâlleri.</summary>
        public Dictionary<string, string> Originals { get; } = new(StringComparer.Ordinal);

        public int FixRounds { get; set; }

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
                Build, BuildAttempts, Tests, TestAttempts, Problem, UsingsAdded, FixRounds, MembersRestored),
            [.. Written.Values],
            notes);
    }
}
