# DevAgents — yerel Ollama ile çok ajanlı kod geliştirme

```
Türkçe soru → İngilizce → Router → [Cevap | Analiz → Kodlama → Test → Review] → Türkçe sonuç
                                                                  ↑__ (review "değişiklik gerekli" derse) __|
```

**Router Agent** isteği okuyup hangi ajanların gerekli olduğuna kendisi karar verir:

| İstek | Çalışan ajanlar |
|---|---|
| Soru / açıklama / genel sohbet | yalnızca Cevap |
| Küçük değişiklik | yalnızca Kodlama |
| Hata düzeltme | Kodlama (+ Test) |
| Yeni özellik / çok dosyalı iş | Analiz → Kodlama → Test → Review |
| "Şunun testlerini yaz" / "şunu review et" | yalnızca Test / yalnızca Review |

Router'ın kararı okunamazsa güvenli tarafta kalıp tam pipeline çalışır. Zorlamak için: arayüzde **Çalışma modu → Tam pipeline**,
API'de `"options": { "mode": "full" }` veya `"options": { "stages": ["code","test"] }`. Karar kuralları `prompts/router.md` içindedir.

* **C# / .NET 8** API + hazır **web sohbet arayüzü** (ChatGPT benzeri, canlı pipeline adımları)
* **Yerel Ollama**: her ajan için model/sıcaklık/context boyutu **config** dosyasından seçilir
* **Proje bağlamı**: klasör yolu verirsiniz; mimari özeti (csproj, paketler, katmanlar, dosya ağacı) ve isteğe en uygun dosyalar ajanlara gider
* **VS Code eklentisi** (API'yi kullanır) ve **docker compose**

## 1) Hızlı başlangıç (Docker'sız)

```bash
ollama pull qwen2.5-coder:7b
ollama pull qwen2.5:7b            # çeviri ajanları için

cd src/DevAgents.Api
dotnet run
# → http://localhost:8080
```

## 2) Model seçimi (config)

Varsayılanlar `src/DevAgents.Api/appsettings.json` içindedir. **Kendi ayarlarınızı `config/local.json` dosyasına yazın**
(değişiklikler yeniden başlatmadan uygulanır; dosya yoksa varsayılanlar kullanılır):

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Agents": {
      "Translator":     { "Model": "qwen2.5:7b" },
      "Analyzer":       { "Model": "qwen2.5-coder:14b", "NumCtx": 24576 },
      "Coder":          { "Model": "qwen2.5-coder:14b", "Temperature": 0.1 },
      "Tester":         { "Model": "qwen2.5-coder:7b" },
      "Reviewer":       { "Model": "deepseek-r1:14b" },
      "BackTranslator": { "Model": "qwen2.5:7b" }
    }
  },
  "Pipeline":  { "MaxReviewIterations": 1 },
  "Workspace": { "MaxContextChars": 24000 }
}
```

* Çözümleme sırası: web arayüzündeki o sohbete özel seçim → `Agents.<Ajan>` → `Defaults`
* `BackTranslator` tanımlı değilse `Translator` ayarı kullanılır
* Ortam değişkeniyle de verilebilir: `Ollama__Agents__Coder__Model=qwen2.5-coder:14b`
* Ajanların sistem prompt'ları `src/DevAgents.Api/prompts/*.md` dosyalarındadır; **yeniden derlemeden** düzenleyebilirsiniz

## 3) Proje bağlamı nasıl çalışır?

1. **Genel bakış**: `.csproj` içinden framework + paketler + proje referansları, klasör adlarından katman ipuçları (Controllers, Services, Domain, Handlers…), dosya ağacı
2. **Sabit dosyalar**: csproj, `Program.cs`/`Startup.cs`, README
3. **İlgili dosyalar**: isteğin (İngilizce çeviri dahil) anahtar kelimeleriyle dosya yolu ve içerik eşleşmesine göre puanlanır
4. **Örnek dosyalar**: henüz temsil edilmeyen klasörlerden birer küçük `.cs` dosyası — kod stilini/konvansiyonu öğretmek için
5. Web arayüzünden eklediğiniz dosyalar ve VS Code'da açık dosya/seçili kod **her zaman** bağlama girer

`bin/obj/node_modules/.git` gibi klasörler, `.env`, `*secret*`, `*.pfx/.pem/.key` dosyaları atlanır.
Bütçe `Workspace:MaxContextChars` ile ayarlanır (7B modeller için 16–24K karakter + `NumCtx: 16384` iyi bir başlangıçtır).
Bağlamın ne olacağını sohbet arayüzündeki **"Bağlamı önizle"** düğmesiyle görebilirsiniz.

## 4) Docker

```bash
cp .env.example .env     # PROJECTS_DIR ve OLLAMA_URL'i düzenleyin
docker compose up -d --build
# → http://localhost:8080   (yalnızca bu makineden erişilir)
```

* Projeleriniz konteynerde `/workspace` altına **salt-okunur** bağlanır. Arayüzde yol olarak sadece klasör adını (`MyApp`) yazabilirsiniz.
* Ollama ana makinedeyse varsayılan `host.docker.internal:11434` kullanılır. Ollama'yı da konteynerde çalıştırmak için:
  `docker compose --profile with-ollama up -d` ve `.env` içinde `OLLAMA_URL=http://ollama:11434`; modeller için
  `docker exec devagents-ollama ollama pull qwen2.5-coder:7b`
* `config/` ve `prompts/` klasörleri bağlıdır; düzenleyip kaydetmeniz yeterlidir.

## 5) VS Code eklentisi

```bash
cd vscode-extension
npx @vscode/vsce package          # devagents-local-0.1.0.vsix üretir
code --install-extension devagents-local-0.1.0.vsix
```
(Denemek için: klasörü VS Code'da açıp **F5**.)

Komutlar (`Ctrl+Shift+P`): **DevAgents: Soru sor / kod geliştir**, **Seçili kod hakkında sor** (sağ tık menüsünde de var),
**Son sonuçtaki dosyaları projeye uygula** (dosya dosya seçim + var olanlar için onay/diff), **Konuşma geçmişini sıfırla**.

Ayarlar: `devagents.serverUrl`, `devagents.contextMode` (`inline`: workspace dosyalarını API'ye gönderir, Docker ile de çalışır; `path`: sadece yol gönderir), `devagents.maxReviewIterations`.

## 6) API

| Uç nokta | Açıklama |
|---|---|
| `POST /api/chat` | Tek seferde JSON sonuç |
| `POST /api/chat/stream` | NDJSON olay akışı: `stage_start`, `token`, `stage_end`, `final`, `error` |
| `POST /api/context/preview` | Ajanlara gidecek bağlamı gösterir |
| `GET /api/models`, `/api/config`, `/api/projects`, `/api/health` | Yardımcı uç noktalar |

```bash
curl -X POST http://localhost:8080/api/chat -H "Content-Type: application/json" -d '{
  "question": "Sipariş iptal endpointi ekle; stok geri yüklensin",
  "projectPath": "C:\\src\\Shop",
  "options": { "maxReviewIterations": 1, "modelOverrides": { "Coder": "qwen2.5-coder:14b" } }
}'
```
Sonuçta `finalMarkdown` (Türkçe rapor), `files` (ayrıştırılmış kod/test dosyaları), `approved`, `analysis`, `review` alanları bulunur.

## Bilmeniz gerekenler

* **Test Agent kodu çalıştırmaz**; test kodu yazar ve gerçek derleme yapmadan hata arar. Derleme/`dotnet test` doğrulaması için bir sonraki adım olarak ayrı bir "çalıştır-düzelt" döngüsü eklenebilir.
* Kod dosyalarını yazma işlemi yalnızca VS Code eklentisinde, sizin onayınızla yapılır; API proje dosyalarını değiştirmez.
* Küçük (7B) modellerde format bozulabilir; bu durumda `Coder`/`Analyzer` için daha büyük model seçin ya da `NumCtx` değerini artırın.
* `Workspace:AllowedRoots` boşsa API makinedeki her klasörü okuyabilir. Yalnızca localhost'a açık tutun; Docker kurulumunda otomatik olarak `/workspace` ile sınırlıdır.



name: Main Config
version: 1.0.0
schema: v1

models:
  - name: C# Multi-Agent Pipeline
    provider: openai
    model: csharp-multi-agent
    apiBase: http://localhost:8080/v1
    apiKey: NONE
    roles:
      - chat
      - edit
      - apply
    default: true