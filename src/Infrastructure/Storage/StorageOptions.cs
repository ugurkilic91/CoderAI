namespace DevAgents.Infrastructure.Storage;

/// <summary>DevAgents'in kendi verilerinin (değişiklik geçmişi, sandbox önbelleği) tutulduğu yer; "Storage" bölümünden okunur.</summary>
internal sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Boşsa kullanıcının ev klasöründe .devagents kullanılır. Docker'da /data olarak bağlanır.</summary>
    public string DataDirectory { get; set; } = "";

    public string ResolveDataDirectory() =>
        string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devagents")
            : Path.GetFullPath(DataDirectory);
}
