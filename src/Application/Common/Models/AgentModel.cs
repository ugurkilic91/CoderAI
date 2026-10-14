namespace DevAgents.Application.Common.Models;

/// <summary>Bir ajanın çalışacağı model ve örnekleme ayarları.</summary>
/// <param name="Name">Ajanın adı (<see cref="Domain.Enums.AgentRole"/>).</param>
/// <param name="NumCtx">Bağlam penceresi (token).</param>
/// <param name="NumPredict">Üretilecek en fazla token.</param>
/// <param name="Think">Düşünen modeller (qwen3, deepseek-r1...) cevaptan önce düşünsün mü; varsayılan hayır.</param>
public sealed record AgentModel(string Name, string Model, double Temperature, int NumCtx, int NumPredict, bool Think = false);
