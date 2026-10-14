# DevAgents — yerel modellerle, seçilen mimaride kod geliştiren ajanlar

DevAgents şirket içinde (on-prem) çalışan bir kod geliştirme aracıdır. İsteğinizi Türkçe yazarsınız.
Ajanlar isteği analiz eder, kodu **seçtiğiniz mimariye göre** yazar ve dosyaları projeye ekler.
Ardından kodu **Docker'da derler**, birim testlerini yazıp çalıştırır ve sonucu Türkçe raporlar.
Tüm modeller yerel Ollama sunucusunda çalışır; kod dışarı çıkmaz.

```
Türkçe istek → (gerekirse) İngilizceye çeviri → Router → Analiz → Kodlama → dosyalar projeye yazılır
  → derleme (Docker) ⟲ eksik using'ler otomatik eklenir, kalan hatalar Düzeltme ajanında
  → Test ajanı testleri yazar → testler çalışır (Docker) ⟲ başarısızsa düzeltme → Review → Türkçe rapor
```

## 1) Gereksinimler

* **.NET 10 SDK**
* **Ollama** ve modeller. Varsayılan ayarlar şu modelleri kullanır:
  ```bash
  ollama pull qwen2.5-coder:14b   # analiz, kodlama, test, review, düzeltme
  ollama pull qwen3.5:9b          # çeviri ve router
  ```
* **Docker**: derleme ve testler ayrı konteynerlerde çalışır. Docker yoksa `Sandbox:Mode=Process` ayarıyla doğrudan makinede de çalışabilir; bu mod yalnızca kişisel kullanım içindir.

## 2) Çalıştırma

```bash
cd src/Web
dotnet run
# → http://localhost:8080
```

VS Code'da **F5** de çalışır (`.vscode/launch.json`). Arayüz güncellendikten sonra eski bir görünüm kalırsa sayfayı **Ctrl+Shift+R** ile yenileyin.

## 3) Nasıl kullanılır?

Sol panelde üç ayar vardır: **Mimari**, **Ne yapılsın?** ve **Proje klasörü**.

| Ne yapılsın? | Ne olur |
|---|---|
| **Sadece öner** | Kod sohbette gösterilir, dosyalara dokunulmaz. Soru sormak ve fikir almak için. |
| **Mevcut projeye uygula** | Kod, "Proje klasörü"ndeki projeye yazılır, derlenir ve testleri çalıştırılır. Önce yedek alınır. |
| **Yeni proje oluştur** | Seçili mimarinin şablonundan yeni bir proje açılır, sonra kod yazılır, derlenir ve test edilir. |

**Örnek: sıfırdan bir proje**
1. **Mimari:** *Clean Architecture (CQRS + MediatR)*
2. **Ne yapılsın?** *Yeni proje oluştur*, proje adı: `HobbyTracker`
3. **Yeni projenin açılacağı klasör:** örn. `/home/ben/projeler`. Proje bu klasörün içinde açılır.
4. İstek: *"Hobilerimi kaydeden bir özellik geliştir: hobi ekleme, listeleme, güncelleme ve silme."*

Ekranda adımları canlı izlersiniz: proje oluşturma, analiz, kodlama, Roslyn sözdizimi kontrolü, dosyaların yazılması,
derleme, eksik using'ler, düzeltme, test ajanı ve testlerin çalışması. Sonuç kartında derleme ve test rozetleri,
değişen dosyalar ve **"Değişiklikleri geri al"** düğmesi bulunur. Proje oluşturulduktan sonra mod kendiliğinden
*Mevcut projeye uygula*'ya geçer; sonraki istekler ("hobiye kategori ekle") aynı projeye devam eder.

**Belirsizse önce bana soru sor** açıksa analiz ajanı eksik bilgiyi sorar. Cevabınızı yeni mesaj olarak yazın;
önceki konuşma ajanlara aktarılır.

Süreler hakkında: 12 GB ekran kartlı bir dizüstünde (`qwen2.5-coder:14b`) "yeni proje + hobi ekleme/listeleme/güncelleme/silme"
isteği yaklaşık **9 dakika** sürdü: analiz 1,5 dk, kodlama 3 dk, derleme ve testler saniyeler, review ve çeviri 1 dk.
Kalan süre, test düzeltme turlarına gitti. İlk derlemede NuGet paketleri indirilir (~1 GB).
Paketler `~/.devagents/sandbox-home` altında saklanır; sonraki derlemeler saniyeler sürer.

### Güvenlik ve geri alma
* Dosyalar yazılmadan önce projenin anlık görüntüsü alınır. Görüntü projenin kendi `.git`'ine değil,
  `~/.devagents/history` altındaki ayrı bir depoya yazılır. "Değişiklikleri geri al" o ana döner.
* Derleme ve testler, kaynakları sınırlı (4 GB bellek, 2 CPU) konteynerlerde çalışır. Paket geri yüklemesi dışında
  **ağ kapalıdır**; yapay zekânın yazdığı kod dışarıyla konuşamaz.
* Ajanlar `.git`, `bin`, `obj` klasörlerine ve proje dışına yazamaz. Aynı projede aynı anda tek bir iş çalışır.

## 4) Mimariler (şirket şablonunuzu eklemek)

Her mimari `src/Web/architectures/<id>/` klasöründedir:

```
profile.json   ad, açıklama, şablon klasörü, "dotnet new" argümanları, test projeleri, örnek dosyalar
rules.md       ajanların uyacağı kurallar (İngilizce: modeller İngilizcede daha iyi uyuyor)
examples/      şablonda karşılığı olmayan örnekler (örn. handler testi)
```

Ajanlar eğitilmez. Her istekte kurallar, mimarinin gerçek örnek dosyaları ve derleme/test geri bildirimi verilir.
Şirketinizin şablonu için yeni bir klasör eklemeniz yeterlidir; kod değişikliği gerekmez.
Hazır gelen *clean-architecture* profili, Jason Taylor'ın Clean Architecture şablonunu (`projects/CleanArchitecture`, `ca-sln`) kullanır.

> Not: Bu şablonla oluşturulan projeler MediatR 14 ve AutoMapper 16 kullanır. Bu sürümler ticari kullanımda lisans gerektirir.
> Şirket şablonunuzda farklı sürümler varsa profili kendi şablonunuza yönlendirin.

## 5) Modeller ve ayarlar

Varsayılanlar `src/Web/appsettings.json` içindedir. **Kendi ayarlarınızı `config/local.json` dosyasına yazın.**
Değişiklikler yeniden başlatmadan uygulanır.

```json
{
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Defaults": { "Model": "qwen2.5-coder:14b", "NumCtx": 16384 },
    "Agents": {
      "Router":     { "Model": "qwen3.5:9b" },
      "Translator": { "Model": "qwen3.5:9b" },
      "Coder":      { "Model": "qwen2.5-coder:14b", "Temperature": 0.15 }
    }
  },
  "Development": { "MaxBuildFixAttempts": 3, "MaxTestFixAttempts": 2 },
  "Sandbox": { "Mode": "Docker", "Memory": "4g", "Cpus": "2" }
}
```

* Model seçim sırası: arayüzde o sohbete özel seçim → `Agents.<Ajan>` → `Defaults`. Router ve BackTranslator tanımlı değilse Translator ayarı kullanılır.
* Ortam değişkeniyle de verilebilir: `Ollama__Agents__Coder__Model=qwen2.5-coder:32b`.
* Düşünen modeller (qwen3.5, deepseek-r1) varsayılan olarak **düşünmeden** çalışır (`"Think": false`); bir cümlelik çeviri
  düşünmeyle 38 sn, düşünmeden 1 sn sürdü. Bir ajan için açmak isterseniz: `"Reviewer": { "Model": "deepseek-r1:14b", "Think": true }`.
* İngilizce yazılan istekler çevrilmez (`Pipeline:DetectInputLanguage`).
* Ajan prompt'ları `src/Web/prompts/*.md` dosyalarındadır. `dotnet run` ile çalışırken değişiklikler bir sonraki derlemede geçerli olur.

## 6) Ekip sunucusu (docker compose)

```bash
cp .env.example .env     # PROJECTS_DIR, DATA_DIR, DEVAGENTS_DIR, kullanıcı ve docker grubu numaraları
docker compose up -d --build
```

* Projeler konteynerde `/workspace` altına bağlanır. Arayüzde yol olarak klasör adını (`HobbyTracker`) yazmanız yeterlidir.
* Derleme konteynerlerini ana makinenin Docker'ı başlatır; bunun için DevAgents'e Docker soketi bağlanır.
  **Docker soketine erişim, ana makinede root yetkisine denktir.** Sunucuyu yalnızca güvenilen iç ağda açın.
  Port varsayılan olarak yalnızca bu makineye açıktır; ekibe açarken önüne kimlik doğrulamalı bir ters vekil sunucu koyun.
* `config/`, `src/Web/prompts/` ve `src/Web/architectures/` bağlıdır: düzenleyip kaydetmeniz yeterlidir.
* Ollama ana makinede çalışıyorsa varsayılan olarak yalnızca `127.0.0.1` adresini dinler ve konteynerden erişilemez.
  Docker ağını da dinlemesi için `sudo systemctl edit ollama` ile `[Service]` altına `Environment="OLLAMA_HOST=0.0.0.0"`
  ekleyip `sudo systemctl restart ollama` çalıştırın. 11434 portunu güvenlik duvarıyla dış ağa kapatın.
* Ollama'yı da konteynerde çalıştırmak için `docker compose --profile with-ollama up -d` ve `.env` içinde `OLLAMA_URL=http://ollama:11434`.

## 7) VS Code eklentisi

```bash
cd vscode-extension
npx @vscode/vsce package
code --install-extension devagents-local-0.1.0.vsix
```

Komutlar (`Ctrl+Shift+P`): **DevAgents: Soru sor / kod geliştir**, **Seçili kod hakkında sor**,
**Son sonuçtaki dosyaları projeye uygula**, **Konuşma geçmişini sıfırla**.

## 8) API

| Uç nokta | Açıklama |
|---|---|
| `POST /api/chat/stream` | NDJSON olay akışı: `stage_start`, `token`, `stage_end`, `plan`, `final`, `error` |
| `POST /api/chat` | Aynı akış, tek seferde JSON sonuç |
| `GET /api/architectures` | Mimari profilleri |
| `POST /api/changes/revert` | `{ projectPath, snapshot }`: bir geliştirmenin değişikliklerini geri alır |
| `POST /api/context/preview` | Ajanlara gidecek proje bağlamı |
| `GET /api/health`, `/api/models`, `/api/config`, `/api/projects` | Yardımcı uç noktalar |
| `POST /v1/chat/completions` | OpenAI uyumlu uç nokta (model `csharp-multi-agent`; Continue eklentisi için) |
| `/scalar` | API dokümantasyonu |

```bash
curl -N -X POST http://localhost:8080/api/chat/stream -H "Content-Type: application/json" -d '{
  "question": "Hobilerimi kaydeden bir özellik geliştir",
  "projectPath": "/home/ben/projeler",
  "architecture": "clean-architecture",
  "applyChanges": true,
  "newProjectName": "HobbyTracker",
  "options": { "askQuestions": false }
}'
```

Hatalar `application/problem+json` olarak döner ve `detail` alanında Türkçe mesaj bulunur:
- **400**: hatalı istek;
- **409**: proje meşgul;
- **502/504**: model hatası ya da zaman aşımı;
- **503**: Docker veya git sorunu.

## 9) Geliştirme

```
src/Domain           ajan rolleri, plan kuralları, derleme/test sonuçları, güvenli göreli yol
src/Application      MediatR komut/sorguları: ajan akışı, geliştirme akışı (yaz-derle-düzelt-test), bağlam, mimariler
src/Infrastructure   Ollama, dosya sistemi, git ile değişiklik takibi, Roslyn, Docker sandbox, eksik using düzeltici
src/Web              minimal API uç noktaları, web arayüzü, prompts/, architectures/
tests/               Domain.UnitTests · Application.UnitTests · Infrastructure.IntegrationTests · Application.FunctionalTests
```

```bash
dotnet build
dotnet test --filter "Category!=Slow"   # hızlı testler; "Slow" testleri gerçek derleme yapar (Docker gerekir)
```

Kod Jason Taylor şablonunun düzenini izler (MediatR 12.x, Apache 2.0 lisanslı). Açıklama satırları Türkçedir;
ajanlara giden prompt'lar ve mesajlar İngilizcedir.
