namespace DevAgents.Application.Context;

/// <summary>İstemcinin gönderdiği dosya (sohbet ekranından eklenen dosya, VS Code'daki proje dosyası ya da seçili kod).</summary>
/// <param name="Pinned">Sabitlenmiş dosyalar, kaynak dosya olmasalar bile her zaman ajanlara gider.</param>
public sealed record InlineFile(string Path, string Content, bool Pinned = false);
