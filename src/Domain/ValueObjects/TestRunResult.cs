namespace DevAgents.Domain.ValueObjects;

/// <summary>Test projelerinin çalıştırılmasının sonucu (dotnet test).</summary>
/// <param name="Failures">Başarısız testler; düzeltme için ajana gönderilir.</param>
/// <param name="Output">Komut çıktısının sonu (hata ayıklamak için).</param>
public sealed record TestRunResult(int Total, int Passed, int Failed, int Skipped, IReadOnlyList<TestFailure> Failures, string Output, double Seconds)
{
    /// <summary>En az bir test çalıştı ve hiçbiri başarısız olmadı.</summary>
    public bool AllPassed() => Total > 0 && Failed == 0;
}

/// <summary>Başarısız bir test: adı, hata mesajı ve (varsa) yığın izi.</summary>
public sealed record TestFailure(string Name, string Message, string? StackTrace);
