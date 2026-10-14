namespace DevAgents.Application.Agents;

/// <summary>
/// <see cref="PipelineEvent.Stage"/> değerleri; web arayüzü ve VS Code eklentisi bunları Türkçe adlarla gösterir.
/// <see cref="Answer"/>, <see cref="Analyze"/>, <see cref="Code"/>, <see cref="Test"/> ve <see cref="Review"/>
/// aynı zamanda <see cref="RunOptions.Stages"/> ile elle seçilebilen aşamalardır.
/// </summary>
public static class PipelineStages
{
    public const string TranslateIn = "translate_in";
    public const string Route = "route";
    public const string Context = "context";
    public const string Answer = "answer";
    public const string Analyze = "analyze";
    public const string Code = "code";
    public const string Test = "test";
    public const string Review = "review";
    public const string Revise = "revise";
    public const string TranslateOut = "translate_out";

    // Kodu projeye uygulayan akışın (ApplyChanges) aşamaları:

    /// <summary>Mimarinin şablonundan yeni proje oluşturma.</summary>
    public const string Scaffold = "scaffold";

    /// <summary>Üretilen kodun Roslyn ile sözdizimi kontrolü.</summary>
    public const string Syntax = "syntax";

    /// <summary>Dosyaların projeye yazılması.</summary>
    public const string Write = "write";

    /// <summary>Docker sandbox'ta dotnet build.</summary>
    public const string Build = "build";

    /// <summary>Eksik using satırlarının model kullanmadan (Roslyn ile) eklenmesi.</summary>
    public const string Usings = "usings";

    /// <summary>Kodlama ajanının yanlışlıkla sildiği üyelerin dosyanın eski hâlinden geri eklenmesi (model kullanmadan).</summary>
    public const string Restore = "restore";

    /// <summary>Derleme/test hatalarının düzeltme ajanına verilip düzeltilmesi.</summary>
    public const string Fix = "fix";

    /// <summary>Docker sandbox'ta dotnet test.</summary>
    public const string TestRun = "test_run";
}
