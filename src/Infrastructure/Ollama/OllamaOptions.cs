namespace DevAgents.Infrastructure.Ollama;

/// <summary>Ollama bağlantısı ve ajan başına model ayarları; yapılandırmadaki "Ollama" bölümünden okunur.</summary>
internal sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Tek bir ajan çağrısının en uzun süresi.</summary>
    public int TimeoutMinutes { get; set; } = 20;

    /// <summary>Ollama'nın bir modeli çağrıdan sonra bellekte ne kadar tutacağı.</summary>
    public string KeepAlive { get; set; } = "10m";

    /// <summary>Ajanın kendi ayarında olmayan her değer için kullanılır.</summary>
    public AgentSettings Defaults { get; set; } = new();

    /// <summary>Ajan adına göre ayarlar (bkz. <see cref="Domain.Enums.AgentRole"/>), büyük/küçük harf duyarsız.</summary>
    public Dictionary<string, AgentSettings> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Bir ajanın model ayarları; boş değerler için bkz. <see cref="OllamaAgentModelResolver"/>.</summary>
internal sealed class AgentSettings
{
    public string? Model { get; set; }

    public double? Temperature { get; set; }

    /// <summary>Bağlam penceresi (Ollama "num_ctx").</summary>
    public int? NumCtx { get; set; }

    /// <summary>Üretilecek en fazla token (Ollama "num_predict").</summary>
    public int? NumPredict { get; set; }

    /// <summary>
    /// Düşünen modeller (qwen3.5, deepseek-r1...) cevap vermeden önce düşünsün mü (Ollama "think"). Varsayılan false:
    /// ölçüldü, bir cümlelik çeviri düşünmeyle 38 sn sürüp token bütçesini bitirerek boş dönebiliyor, düşünmeden 1 sn.
    /// </summary>
    public bool? Think { get; set; }
}
