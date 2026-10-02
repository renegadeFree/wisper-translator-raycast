# Wisper Translator — Piano di sviluppo e tracker

| Campo | Valore |
|---|---|
| Versione piano | 0.1 (bozza da approvare) |
| Data | 2026-10-02 |
| Stato | 🔄 in lavorazione — F0 e F1 completate |
| Macchina di sviluppo | Ryzen 9 7900X3D / 63 GB RAM / RTX 3080 Ti 12 GB / Win11 26200 |
| Macchina di riferimento (test reale) | RTX 2060 6 GB / i7 10ª gen / 16 GB RAM |
| Vincolo di portabilità | Deve girare anche senza GPU dedicata (iGPU datata), CPU AVX2 |
| Licenza prevista | MIT (codice proprio) |

---

## 0. Come si usa questo file

Questo file è **il registro di lavoro**. Regole:

1. Ogni attività ha una casella `[ ]` → `[x]` quando è verificata (non quando è "scritta").
2. Ogni modifica sostanziale (file creato, pacchetto aggiunto, decisione cambiata) si annota in **§9 Changelog** con data e riferimento `D-xx`/`F-x`.
3. Ogni decisione tecnica vive in **§2** con un ID (`D-01`, `D-02`…): se cambia, si aggiorna la riga, non si cancella.
4. Una fase è chiusa solo se **tutti** i suoi criteri di accettazione (§5) sono spuntati.
5. Le domande ancora aperte stanno in **§7** con un default già scelto: si procede col default finché tu non dici diversamente.

---

## 1. Obiettivo e requisiti

### 1.1 Obiettivo

Applicazione Windows nativa che cattura l'audio di sistema e/o il microfono, trascrive in tempo reale
(interamente in locale) e traduce **IT ↔ EN** bidirezionale, mostrando i sottotitoli in un widget
flottante sempre in primo piano, con gestione completa dei modelli e storico locale.

### 1.2 Requisiti funzionali (RF)

| ID | Requisito | Fase |
|---|---|---|
| RF-01 | Cattura audio di sistema (WASAPI loopback) | F1 |
| RF-02 | Cattura microfono, con selezione periferica | F1 |
| RF-03 | Toggle indipendente delle due sorgenti + mix | F1 |
| RF-04 | Segmentazione del parlato con VAD (niente silenzi in inferenza) | F1 |
| RF-05 | Trascrizione locale real-time con risultati parziali stabilizzati | F2/F3 |
| RF-06 | Traduzione locale IT→EN e EN→IT | F4 |
| RF-07 | Direzione linguistica: fissa IT→EN, fissa EN→IT, o auto-rilevamento | F4 |
| RF-08 | Widget flottante always-on-top, trascinabile, ridimensionabile | F5 |
| RF-09 | Backdrop Mica/Acrylic + tema chiaro/scuro automatico | F5 |
| RF-10 | Buffer visivo rolling fino a 20 frasi, nuova in alto, vecchie che scivolano in basso | F5 |
| RF-11 | Menu rapido a scomparsa: sorgenti, direzione, impostazioni | F5 |
| RF-12 | Overlay fullscreen cliccabile-attraverso (click-through) opzionale | F5 |
| RF-13 | Hotkey globali (mostra/nascondi, toggle mic, toggle sistema, inverti direzione) | F5 |
| RF-14 | Model manager: download da Hugging Face, import locale, eliminazione, verifica integrità | F6 |
| RF-15 | Selezione automatica del tier hardware e dei modelli conseguenti | F6 |
| RF-16 | Storico sessioni su database locale | F7 |
| RF-17 | Retention automatica: conservazione 5 giorni, pulizia progressiva | F7 |
| RF-18 | Export sessioni in TXT / SRT / JSON | F7 |
| RF-19 | Installer one-click con runtime .NET incluso e cartelle di lavoro | F8 |
| RF-20 | Icona nell'area di notifica, funzionamento in background, menu e sottomenu | F9 |
| RF-21 | Icone Fluent su pulsanti e menu, barra di stato con livello audio | F9 |
| RF-22 | Elenco sessioni consultabile con visualizzatore completo della trascrizione | F10 |
| RF-23 | Riassunti, punti chiave e mappe concettuali con IA locale o API di più provider | F10 |
| RF-24 | Configurazione IA completa: provider, endpoint, modello, chiave, lingua, token, timeout | F10 |
| RF-25 | Esportazione PDF del report (riassunto + mappa + trascrizione) | F10 |
| RF-26 | Conservazione delle sessioni configurabile (0 = per sempre) | F10 |
| RF-27 | Avvio automatico con Windows | F9 |

### 1.3 Requisiti non funzionali (RNF)

| ID | Requisito | Target |
|---|---|---|
| RNF-01 | RAM totale a runtime | ≤ 2 GB (Tier B), ≤ 1 GB (Tier C) |
| RNF-02 | Latenza parziale (parlato → primo testo tradotto) | ≤ 1,5 s Tier B; ≤ 2,5 s Tier C |
| RNF-03 | Latenza finale (fine frase → frase tradotta stabile) | ≤ 2,5 s Tier B |
| RNF-04 | CPU a riposo/in ascolto senza parlato | < 3% su i5 10ª gen |
| RNF-05 | Funzionamento 100% offline dopo il download dei modelli | sì |
| RNF-06 | Nessun dato audio/testo inviato in rete (se motori locali) | sì |
| RNF-07 | Avvio a freddo (senza download) | ≤ 5 s |
| RNF-08 | Log/history senza perdita di dati in caso di chiusura anomala | sì |

### 1.4 Fuori scope v1 (dichiarato, non dimenticato)

- Sintesi vocale (TTS) / speech-to-speech.
- Traduzione per lingue oltre IT/EN.
- Cattura per singola applicazione (process loopback) — valutata in F9.
- Auto-update dell'applicazione.
- Dispositivi mobili / macOS / Linux.

---

## 2. Decisioni tecniche

Stato: `✔ confermata` · `~ proposta` · `✖ scartata`

| ID | Decisione | Stato | Motivazione (con dati verificati) |
|---|---|---|---|
| D-01 | Stack **C# / .NET 8 + WPF + WPF-UI 4.3.0** | ~ | UI Fluent con backdrop Mica/Acrylic, layout XAML, ecosistema .NET per ASR nativo. Percorso già validato da app simili (LiveCaptions-Translator: WPF + WPF-UI). |
| D-02 | Astrazione UI: **2 finestre** — Widget (backdrop Mica reale) + Overlay (layer trasparente click-through) | ~ | In WPF `AllowsTransparency=true` **disabilita** il backdrop Mica (finestra a livelli). Separando le due finestre si ottiene Mica vero sul widget e trasparenza per-pixel sull'overlay. |
| D-03 | Cattura audio con **NAudio 2.2.1** (`WasapiLoopbackCapture` + `WasapiCapture`) | ✔ | NAudio 3.1 richiede .NET 9+; il progetto resta su .NET 8 LTS, quindi si usa la 2.2.1 (stabile, MIT, stesse API per il loopback). Da rivalutare alla migrazione a .NET 10 (vedi F8.0). |
| D-04 | Risampling a **16 kHz mono float32** con resampler NAudio | ~ | Requisito di tutti i motori ASR (Whisper/Silero). Un solo punto di conversione nel mixer. |
| D-05 | VAD: **Silero VAD v5 ONNX** via `Microsoft.ML.OnnxRuntime` | ~ | 2 MB di modello, ~30 MB RAM, MIT. Taglia ~60-70% del calcolo sulle pause: è il risparmio più grosso su CPU debole. |
| D-06 | ASR v1: **Whisper.net 1.9.1** (binding .NET a whisper.cpp) | ~ | MIT, runtime nativi CPU/CUDA/Vulkan, load da file ggml, downloader integrato. Un solo pacchetto copre tutti i tier. |
| D-07 | Modello ASR default per tier | ~ | Tier A: `large-v3-turbo` q5 (~550 MB). Tier B: `small` q5_1 (~190 MB). Tier C: `base` q5_1 (~80 MB). |
| D-08 | Strategia di streaming ASR: **chunk su VAD + LocalAgreement-2** | ~ | Whisper non è streaming. Si decodifica l'enunciato in crescita ogni ~700 ms e si committano solo le parole su cui due decodifiche consecutive concordano → nessun testo che "salta". |
| D-09 | Traduzione v1: **motore intercambiabile**, default server locale HTTP | ~ | MTranServer (Bergamot/Mozilla) risponde in ~50 ms senza GPU; modelli `enit`/`iten` verificati esistenti nel repo Mozilla. Fallback cloud opzionale (API OpenAI-compatibile/DeepL) per qualità. |
| D-10 | Traduzione v2 (F9): subprocess **Marian CLI** o P/Invoke a bergamot-translator | ~ | Elimina il processo esterno. CTranslate2 non ha binding .NET; Marian/bergamot sì (binario MIT, modello Apache-2.0). Non serve per l'MVP. |
| D-11 | Modelli di traduzione alternativi | ~ | `Helsinki-NLP/opus-mt-it-en` e `opus-mt-en-it`: Apache-2.0, ~80 MB in int8, IT↔EN dedicato. |
| D-12 | **SeamlessStreaming: escluso** | ✖ | 2,5B parametri, licenza **CC-BY-NC 4.0** (non commerciale), stack fairseq2/PyTorch, pensato per GPU. Su CPU senza accelerazione non è real-time. |
| D-13 | **NLLB-200: escluso come default** | ✖ | `nllb-200-distilled-600M` è **CC-BY-NC 4.0**. Inoltre è multilingua su 200 lingue per un caso che ne usa 2: 8× la RAM di un modello dedicato. |
| D-14 | Alternativa permissiva multilingua (solo se serve) | ~ | `facebook/m2m100_418M`, licenza **MIT**, supportato da CTranslate2. Non serve in v1. |
| D-15 | Persistenza: **SQLite** (`Microsoft.Data.Sqlite`) | ~ | Pruning per data e query banali; niente parser da scrivere. Già usato con successo in progetti analoghi. |
| D-16 | Impostazioni in JSON versionato in `%LOCALAPPDATA%\WisperTranslator\` | ~ | Leggibile, ispezionabile, migrabile. Nessuna dipendenza. |
| D-17 | Modelli in `%LOCALAPPDATA%\WisperTranslator\models\<ruolo>\<id>\` | ~ | Non richiede permessi admin per l'utente, sopravvive alla reinstallazione dell'app. |
| D-18 | Downloader: `HttpClient` + Range resume + SHA-256 + rename atomico | ~ | Nessun SDK Hugging Face da aggiungere. |
| D-19 | Packaging: **publish self-contained single-file** + installer Inno Setup | ~ | Nessun runtime da installare a parte; installer gestisce cartelle e scorciatoie. (MSIX valutato ma richiede firma e complica l'accesso ai file.) |
| D-20 | Codice proprio **MIT**; terze parti documentate | ~ | `THIRD-PARTY-NOTICES.md` generato in F8. Licenza di MTranServer da verificare **prima** di impacchettarlo. |
| D-21 | Benchmark misurabile come deliverable | ~ | `WisperTranslator.Cli bench` produce RTF/latenza/RAM reali sulla macchina target → finiscono in `docs/BENCHMARKS.md`. |

---

## 3. Architettura

### 3.1 Pipeline

```
┌─────────────── sorgenti ───────────────┐
│ WASAPI loopback (sistema)   [toggle]   │
│ WASAPI capture (microfono)  [toggle]   │
└───────────────────┬────────────────────┘
                    │ float32, 16 kHz, mono
                    ▼
            Mixer + gain per sorgente
                    │
                    ▼
          Silero VAD (OnnxRuntime)
                    │ enunciati (0.3 s – 15 s)
                    ▼
     ASR Whisper.net (local agreement)
                    │ testo stabile + testo in corso
                    ▼
        Translation engine (HTTP locale)
                    │
                    ▼
   UI: widget rolling 20 frasi + overlay + history SQLite
```

### 3.2 Stack e dipendenze

| Componente | Pacchetto / Versione | Licenza | Peso indicativo |
|---|---|---|---|
| Runtime | .NET 8 (LTS) | MIT | self-contained ~70 MB |
| UI | WPF + `WPF-UI` 4.3.0 | MIT | ~2 MB |
| Audio | `NAudio` 3.1.0 | MIT | ~1 MB |
| ASR | `Whisper.net` 1.9.1 (+ runtime CPU/CUDA/Vulkan) | MIT | 5–300 MB per runtime |
| VAD | Silero VAD v5 ONNX + `Microsoft.ML.OnnxRuntime` | MIT | ~35 MB |
| ASR alternativo (F9) | `org.k2fsa.sherpa.onnx` 1.13.8 + runtime win-x64 | Apache-2.0 | ~30 MB + modelli |
| Storage | `Microsoft.Data.Sqlite` | MIT | ~5 MB |
| Modelli ASR | ggml da whisper.cpp | MIT | 80–550 MB |
| Modelli MT | OPUS-MT / Bergamot | Apache-2.0 / MPL-2.0 | 30–80 MB per direzione |

### 3.3 Tiering hardware

| Tier | Rilevamento | ASR | MT | Latenza attesa |
|---|---|---|---|---|
| A | GPU dedicata con ≥ 4 GB VRAM | `large-v3-turbo` q5 su CUDA | locale | ~0,4–0,7 s |
| B | CPU ≥ 6 core con AVX2 | `small` q5_1 su CPU | locale | ~0,8–1,5 s |
| C | CPU 4 core / iGPU datata | `base` q5_1, + Vosk `small-it` per i parziali | locale (Bergamot tiny) | ~1,5–2,5 s |

Il rilevamento usa: nome/adapter GPU, VRAM stimata, numero di core, flag AVX2, RAM totale.
Override manuale sempre possibile da Impostazioni (il tier è una proposta, non un vincolo).

### 3.4 Budget risorse (stima, da confermare in F8 con `bench`)

| Configurazione | RAM stimata | Note |
|---|---|---|
| Tier A (turbo q5 + MT) | ~1,6–2,4 GB di picco | la VRAM assorbe il modello |
| Tier B (small q5_1 + MT) | ~0,8–1,2 GB | obiettivo primario |
| Tier C (base q5_1 + Bergamot tiny) | ~0,5–0,8 GB | obiettivo dichiarato |

---

## 4. Struttura del repository

```
Wisper Translator/
├─ PLAN.md                        ← questo file (piano + tracker + changelog)
├─ README.md                      ← quick start per l'utente finale
├─ THIRD-PARTY-NOTICES.md         ← licenze di terze parti (F8)
├─ WisperTranslator.sln
├─ src/
│  ├─ WisperTranslator.App/       ← WPF: widget, overlay, pagine impostazioni
│  │  ├─ App.xaml(.cs)
│  │  ├─ Windows/WidgetWindow.xaml(.cs)
│  │  ├─ Windows/OverlayWindow.xaml(.cs)
│  │  ├─ Views/SettingsView.xaml(.cs)
│  │  ├─ Views/ModelsView.xaml(.cs)
│  │  ├─ Views/HistoryView.xaml(.cs)
│  │  ├─ Controls/CueList.xaml(.cs)        ← rolling 20 frasi + animazione
│  │  ├─ Controls/QuickMenu.xaml(.cs)      ← menu a scomparsa
│  │  └─ Interop/Win32.cs                  ← click-through, WDA_EXCLUDEFROMCAPTURE
│  ├─ WisperTranslator.Core/      ← nessuna dipendenza UI
│  │  ├─ AppPaths.cs                       ✅
│  │  ├─ Audio/AudioDevices.cs             ✅
│  │  ├─ Audio/VoiceCapture.cs             ✅  (sostituisce AudioCaptureService)
│  │  ├─ Audio/AudioMixer.cs               ✅
│  │  ├─ Audio/FloatRingBuffer.cs          ✅
│  │  ├─ Audio/DownmixToMonoSampleProvider.cs ✅
│  │  ├─ Audio/IAudioSource.cs             ✅
│  │  ├─ Vad/IVadModel.cs                  ✅
│  │  ├─ Vad/SileroVad.cs                  ✅
│  │  ├─ Vad/EndpointDetector.cs           ✅
│  │  ├─ Vad/SpeechSegmenter.cs            ✅
│  │  ├─ Models/HttpDownload.cs            ✅  (VAD ora, esteso in F6)
│  │  ├─ Asr/IAsrEngine.cs
│  │  ├─ Asr/WhisperAsrEngine.cs
│  │  ├─ Asr/StreamingPolicy.cs            ← local agreement + commit
│  │  ├─ Translation/ITranslationEngine.cs
│  │  ├─ Translation/LocalHttpEngine.cs
│  │  ├─ Translation/OpenAiCompatibleEngine.cs
│  │  ├─ Models/ModelCatalog.cs
│  │  ├─ Models/ModelDownloader.cs
│  │  ├─ Models/ModelStore.cs
│  │  ├─ History/SessionStore.cs           ← SQLite + retention
│  │  ├─ History/SrtExporter.cs
│  │  ├─ Hardware/TierDetector.cs
│  │  └─ Settings/AppSettings.cs
│  └─ WisperTranslator.Cli/       ← benchmark + prove end-to-end
│     └─ Program.cs               ← `bench`, `transcribe <file.wav>`, `models list`
├─ tests/
│  └─ WisperTranslator.Tests/     ← pochi test mirati (vedi §5)
├─ docs/
│  ├─ ARCHITECTURE.md
│  ├─ BENCHMARKS.md
│  └─ MODELS.md                   ← catalogo modelli + licenze + hash
└─ tools/
   └─ convert/                    ← script conversione modelli (solo sviluppo)
```

---

## 5. Fasi di lavoro

Legenda stato: ⬜ da fare · 🔄 in corso · ✅ verificato · ⏸ bloccato

### F0 — Setup progetto
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F0.1 | Creare solution + 4 progetti (App, Core, Cli, Tests) | `dotnet build` pulito | ✅ `WisperTranslator.sln`, tutti i riferimenti attivi |
| F0.2 | Aggiungere pacchetti (WPF-UI, NAudio, Whisper.net, OnnxRuntime, Sqlite) | build senza warning | ✅ WPF-UI 4.3.0, NAudio 2.2.1, Whisper.net 1.9.1 + Runtime, OnnxRuntime 1.30.0, Microsoft.Data.Sqlite 10.0.12 |
| F0.3 | `.gitignore` (+ eventuali file comuni) | nessun artefatto tracciato | ✅ solo `.gitignore`; `Directory.Build.props` ed `.editorconfig` rimandati (4 progetti non ne hanno bisogno ora) |
| F0.4 | Progetto test verde | `dotnet test` ok | ✅ 14 test reali (nessun test fittizio: il primo test utile è arrivato con F1) |
| F0.5 | `README.md` e `docs/MODELS.md` | documentazione avviata | ✅ entrambi creati |

**Criterio di accettazione:** `dotnet build` + `dotnet test` passano da terminale pulito.
**Verificato:** build 0 errori / 0 avvisi, `dotnet test` → 14 superati, 0 non superati.

**Deviazione registrata:** i target framework sono `net8.0-windows` (non `net8.0`) perché Core, Cli e Tests
useranno API Windows; NAudio è stato fissato alla 2.2.1 (vedi D-03).

### F1 — Cattura audio + VAD
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F1.1 | Enumerazione dispositivi di output/input con nomi leggibili | lista in CLI | ✅ 7 uscite + 4 ingressi elencati, predefiniti marcati |
| F1.2 | `WasapiLoopbackCapture` → 16 kHz mono float32 | — | ✅ verificato su "Altoparlanti (Realtek)" (sorgente 48 kHz 2ch IEEFloat) |
| F1.3 | `WasapiCapture` microfono → stesso formato | — | ✅ verificato su "Microfono (Realtek)" |
| F1.4 | Mixer con gain indipendente e toggle per sorgente | — | ✅ `AudioMixer` + test unitari su somma, clipping e sorgenti disabilitate |
| F1.5 | Silero VAD ONNX + endpoint (silenzio 600 ms, max 15 s) | eventi `SpeechStart`/`SpeechEnd` | ✅ modello 2,2 MB scaricato, `EndpointDetector` + `SpeechSegmenter` con 7 test |
| F1.6 | Dump WAV per debug (`--dump`) | file riproducibile | ✅ `pcm_f32le 16000 Hz mono` confermato con ffprobe |

**Criterio di accettazione:** `WisperTranslator.Cli capture --seconds 12 --play --dump` produce WAV 16 kHz
mono e il VAD rileva enunciati coerenti.
**Verificato (esito reale):**

```
Sistema  : Altoparlanti (Realtek(R) Audio) [32 bit IEEFloat: 48000Hz 2 channels]
Microfono: Microfono (Realtek(R) Audio)    [32 bit IEEFloat: 48000Hz 2 channels]
Campioni catturati : 216480 (13,5 s)   Picco: 0,0462   Enunciati: 3
    2,53 s  durata 0,99 s    5,76 s  durata 0,61 s    8,45 s  durata 3,62 s
ffprobe  : pcm_f32le, 16000 Hz, 1 canale, 13,53 s
```

**Osservazioni di campo (da tenere presenti in F2/F3):**

1. La cattura ha prodotto ~1,3 s in più della finestra richiesta: probabile buffer iniziale del loopback
   WASAPI. Va misurato e compensato quando conterà la latenza (F3), non ora.
2. Il livello catturato è basso (picco 0,046 ≈ −27 dB) perché il volume di sistema è basso: l'ASR deve
   normalizzare l'audio in ingresso, non fidarsi dell'ampiezza.
3. Il microfono predefinito è lo stesso dispositivo fisico degli altoparlanti: il rischio R-02
   (microfono che risente delle casse) è reale su questa macchina e va gestito nella UI.

### F2 — ASR offline con benchmark
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F2.1 | `WhisperAsrEngine` con caricamento ggml da `ModelStore` | trascrive un WAV | ✅ Whisper.net 1.9.1, factory riusata, processor per chiamata |
| F2.2 | Download di `base/small/large-v3-turbo` q5 dal catalogo | modello presente su disco | ✅ `base` e `small` scaricati da Hugging Face (56,9 / 181,3 MB) |
| F2.3 | `bench`: RTF, RAM di picco, accuratezza | tabella in `docs/BENCHMARKS.md` | ✅ 3 righe reali scritte con `--write` |
| F2.4 | Selezione CPU/CUDA/Vulkan con fallback | nessun crash senza GPU | ⏭ rinviata: il target dichiarato è CPU/iGPU, il default resta CPU. Runtime CUDA/Vulkan entrano in F6 (tiering) o F9 |
| F2.5 | Clip vocali di prova riproducibili | audio reale per i test | ✅ comando `tts` con le voci SAPI installate (Elsa it-IT, Zira en-US) |

**Criterio di accettazione:** clip italiane e inglesi trascritte correttamente; RTF misurato e scritto
in `BENCHMARKS.md` per almeno 2 configurazioni.
**Verificato (macchina di sviluppo, 12 thread su 24):**

| Modello | Lingua | Audio | Tempo | RTF | RAM picco | Accuratezza |
|---|---|---|---|---|---|---|
| whisper-base-q5_1 | it | 11,3 s | 0,53 s | 0,047 | 311 MB | **96,0%** |
| whisper-base-q5_1 | en | 10,3 s | 0,53 s | 0,051 | 294 MB | **100,0%** |
| whisper-small-q5_1 | it | 11,3 s | 1,65 s | 0,145 | 555 MB | **100,0%** |

**Lettura dei risultati:** `base` è ~3 volte più veloce di `small` ma sbaglia 1 parola su 25 in italiano;
`small` è perfetto ma occupa 555 MB. Entrambi stanno nel budget di 2 GB anche sommando il traduttore.
Sul PC di fascia bassa `base` resta la scelta del tier C e `small` del tier B.

**Test:** 4 test unitari su `AsrScore` + verifica end-to-end con clip TTS reali.

### F3 — Pipeline real-time (parziali + finali)
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F3.1 | Decodifiche parziali ogni ~700 ms sull'enunciato in crescita | testo parziale continuo | ✅ worker in background, la lettura audio non viene mai bloccata |
| F3.2 | Politica LocalAgreement-2 per il commit delle parole | nessun testo che "salta" | ✅ `LocalAgreementPolicy` + 5 test |
| F3.3 | Chiusura su endpoint VAD → decodifica finale autoritativa | frase stabile | ✅ la finale **sostituisce** i parziali |
| F3.4 | Metriche di latenza parziale/finale | numeri reali | ✅ comando `live` con mediane e valore peggiore |
| F3.5 | Cascata a due modelli (parziale veloce + finale accurato) | latenza bassa e accuratezza alta | ✅ `--final-model` |

**Criterio di accettazione:** latenza parziale ≤ 1,5 s e nessuna regressione del testo già committato.
**Verificato — clip italiana reale riprodotta dalle casse e ripresa dal loopback (18 s):**

| Configurazione | Latenza parziale | Latenza finale | Accuratezza |
|---|---|---|---|
| `small` per tutto | 1,96 s ❌ | 2,20 s ✅ | 100% |
| `base` per tutto | **0,51 s** ✅ | **0,86 s** ✅ | 96% (1 parola su 25) |
| **`base` parziali + `small` finali** | **0,51 s** ✅ | 2,01 s ✅ | **100%** |

**Scoperta importante (F3.3):** sulle prime decodifiche, quando l'audio è ancora breve, Whisper inventa
("Buon appetito!" / "buon giorno!") e LocalAgreement può congelare l'invenzione. Due correzioni:

1. `MinPartialSeconds` portato da 0,4 a **1,0 s**: sotto il secondo non si decodifica.
2. La frase finale **non** viene più concatenata ai parziali: la decodifica finale è l'autorità e
   sostituisce in blocco il testo dell'enunciato. Il contratto per la UI diventa quindi "una cue per
   enunciato", con `Text` sempre completo e `IsFinal` che indica la versione definitiva.

L'accuratezza misurata è passata da 88% a 96% (base) e a 100% (base+small) proprio per questa correzione.

**Test:** 5 test unitari su LocalAgreement + 3 esecuzioni end-to-end reali via loopback.

### F4 — Traduzione
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F4.1 | `ITranslationEngine` + `LocalHttpEngine` (MTranServer) | traduzione IT↔EN | ✅ API `POST /translate` → `{"result": …}` |
| F4.2 | Gestione del server locale: download, avvio, attesa, spegnimento | zero configurazione | ✅ `TranslationServer` (binario verificato SHA-256, avvio nascosto, stop alla chiusura) |
| F4.3 | `OpenAiCompatibleEngine` opzionale (cloud, opt-in) | fallback qualità | ✅ implementato, spento di default e non verificato senza chiave |
| F4.4 | Direzione IT→EN / EN→IT | selezione in UI | ✅ parametri `--from/--to`, pronti per il selettore di F5 |
| F4.5 | Cache delle traduzioni già viste | latenza ridotta sui parziali | ✅ `TranslationService` + 3 test |
| F4.6 | Misura latenza su 20+20 frasi | numeri in `BENCHMARKS.md` | ✅ comando `translate --bench` |

**Criterio di accettazione:** p95 ≤ 150 ms in locale su 20 frasi per direzione.
**Verificato (`translate --bench`, 20 frasi per direzione):**

| Direzione | Mediana | p95 | Peggiore |
|---|---|---|---|
| it → en | **23 ms** | **37 ms** | 334 ms (solo la prima chiamata: caricamento modello) |
| en → it | **23 ms** | **29 ms** | 34 ms |

**Decisione D-20 aggiornata:** MTranServer è **Apache-2.0** e distribuisce un binario Windows autonomo
da 123 MB → si può includere nell'installer con la sola attribuzione (nessuna dipendenza Python).
I modelli Bergamot IT↔EN pesano ~35 MB per direzione e passano dal model manager.

**Test:** 3 test unitari sulla cache + benchmark riproducibile su frasi di prova integrate
(`TranslationSamples`).

### F5 — Interfaccia (widget, overlay, rolling, menu, hotkey)
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F5.1 | Widget borderless, always-on-top, trascinabile, ridimensionabile, backdrop Mica | finestra moderna | ✅ `FluentWindow` + `TitleBar` + `WindowBackdropType=Mica` |
| F5.2 | Tema chiaro/scuro automatico | segue il sistema | ✅ `ApplicationThemeManager` + `SystemEvents.UserPreferenceChanged` |
| F5.3 | CueList: nuova frase in alto, scorrimento animato, cap 20 | animazione fluida | ✅ inserimento in testa, animazione fade+slide, cap da impostazioni |
| F5.4 | Battuta in corso distinta dalla definitiva | leggibilità | ✅ contratto `Cue.IsFinal`; originale attenuato, traduzione in evidenza |
| F5.5 | Menu rapido: sorgenti, direzione, impostazioni | tutto a portata | ✅ toggle Sistema/Microfono, tendina IT↔EN, Avvia/Ferma, Overlay, Impostazioni |
| F5.6 | Hotkey globali + salvataggio posizione/dimensione | — | ✅ registrati in `OnSourceInitialized`; posizione e dimensioni in `settings.json` |
| F5.7 | Overlay fullscreen trasparente, click-through, escluso dalle catture | non blocca il mouse | ✅ verificato a livello Win32 |
| F5.8 | Opacità, font, battute, dispositivi e modelli configurabili | — | ✅ finestra Impostazioni (Aspetto, Modelli, Storico) |

**Criterio di accettazione:** widget sopra tutto, trascinabile, che non ruba il focus; overlay che non
intercetta i click e non compare nelle registrazioni.
**Verificato:**

| Controllo | Evidenza |
|---|---|
| Overlay click-through | `GetWindowLong(GWL_EXSTYLE)` = `0x80800A8` → `WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` attivi |
| Overlay escluso dalle catture | `GetWindowDisplayAffinity` = `0x11` (`WDA_EXCLUDEFROMCAPTURE`): nello screenshot l'overlay non compare |
| Overlay a tutto schermo | rect `-1920,0 → 3440,1440` (desktop virtuale 5360×1440) |
| Rendering sottotitoli | verificato con l'esclusione disattivata: originale sopra, traduzione grande sotto, sfondo scuro arrotondato |
| Hotkey | `Ctrl+Alt+W/S/N/L/O` registrate (M era occupata da un altro programma → cambiata in N) |

**Prova EN → IT con 8 frasi consecutive** (clip TTS inglese di 37 s, audio di sistema via loopback):
tutte e 8 le frasi riconosciute, segmentate, tradotte e mostrate nel widget (nuova in alto, vecchie in
basso) e nell'overlay (ultime 3, in ordine cronologico).

| Inglese riconosciuto | Italiano prodotto |
|---|---|
| Good morning, this is a real-time translation test. | Buongiorno, questo è un test di traduzione in tempo reale. |
| The system must recognize Italian sentences and translate them into English. | Il sistema deve riconoscere le frasi italiane e tradurle in inglese. |
| The weather is nice today and we are going with the beach with friends. | Il tempo è bello oggi e stiamo andando in spiaggia con gli amici. |
| I did not understand what you said, could you repeat it? | Non ho capito cosa hai detto, potresti ripeterlo? |
| The meeting has been moved to tomorrow morning at 9. | L'incontro è stato spostato a domani mattina alle 9. |
| The train to Milan leaves in 10 minutes from platform 4. | Il treno per Milano parte in 10 minuti dal binari. |
| I need to buy bread and a litter of milk. | Devo comprare il pane e una cioccolata di latte. |
| This movie is really boring, let us change the channel. | Questo film è davvero noioso, cambiamo canale. |

**Limiti osservati (onesti):** "a liter of milk" riconosciuto come "a litter" produce una traduzione
sbagliata ("cioccolata di latte") e "platform 4" perde il numero nel passaggio Bergamot. Sono i
compromessi dichiarati in D-09/D-07: modello `base` per i parziali e traduttore locale leggero.
Con il modello `small` anche sui parziali l'accuratezza ASR sale (vedi F3).

**Test:** verifica visiva su schermo reale (widget + overlay), lettura degli stili estesi Win32 e prova
end-to-end con 8 frasi consecutive.

### F6 — Model manager + tiering
**Stato:** ✅ (2026-10-02)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F6.1 | Catalogo con ruolo, tier, URL, dimensione, SHA-256, licenza | catalogo unico | ✅ `ModelCatalog` con gli hash reali dei file serviti oggi da Hugging Face |
| F6.2 | Download con ripresa, verifica hash, rename atomico, progresso | download robusto | ✅ `HttpDownload.DownloadVerifiedAsync` + 4 test con server HTTP in-process |
| F6.3 | Import manuale da file (upload locale) | modelli custom | ✅ `ModelStore.ImportAsync`, con verifica **prima** di sostituire |
| F6.4 | Eliminazione + spazio occupato + verifica integrità | gestione completa | ✅ comandi `models delete/verify` + pulsanti nella scheda Modelli |
| F6.5 | Rilevamento hardware → modelli consigliati | setup automatico | ✅ CPU, core, RAM, GPU, **VRAM reale dal registro** (WMI la tronca a 4 GB), AVX2 |
| F6.6 | Primo avvio: modelli scelti e scaricati automaticamente | primo utilizzo immediato | ✅ scelta al primo avvio, download con avanzamento |

**Criterio di accettazione:** un download interrotto riprende; un hash errato viene rifiutato; l'import
locale non deve poter distruggere un modello funzionante.
**Verificato:**

| Prova | Esito |
|---|---|
| 4 test su download/ripresa/hash (server HTTP locale, nessuna rete) | ✅ ripresa da 80 KB esatti; hash errato rifiutato; server che ignora la `Range` gestito |
| Download reale dopo cancellazione | ✅ 56,9 MB, hash verificato |
| Verifica di tutti i modelli | ✅ base/small/turbo/VAD "Integro" |
| Import di un file da 21 byte | ✅ rifiutato, modello intatto |
| Import di un file da 2,2 MB con contenuto casuale | ✅ rifiutato per hash, modello intatto |
| Scheda Modelli nella UI | ✅ profilo hardware, Scarica/Verifica/Elimina, "Applica i modelli consigliati" |

**Difetto trovato e corretto (importante):** la prima versione dell'import copiava il file scelto
direttamente sulla destinazione: un file sbagliato distruggeva il modello installato (è successo al VAD
durante i test). Ora l'import scrive in `<file>.import`, verifica dimensione e hash, e sostituisce solo
se tutto torna; 4 test lo impediscono in futuro.

**Deviazione (F6.1):** il catalogo è una lista C# compilata invece di un JSON esterno: gli hash devono
essere verificati e versionati con l'app, così un manifest remoto non può introdurre un modello non
previsto. Se in futuro i modelli cambieranno senza aggiornare l'app, si aggiungerà un manifest firmato.

**Deviazione (F6.5):** la raccomandazione dipende **solo dai core**, non dalla GPU: il runtime incluso è
CPU-only e `large-v3-turbo` costa RTF 0,585 anche su 24 thread (misurato). La GPU servirà con un runtime
accelerato (F9).

**Test:** 4 test su download/ripresa + 4 su validazione/import + prove reali via CLI.

### F7 — Storico, retention, export
**Stato:** ✅ (2026-10-03)

| # | Attività | Esito atteso | Esito |
|---|---|---|---|
| F7.1 | `SessionStore` SQLite (sessioni + battute con orari e lingue) | scrittura affidabile | ✅ schema `sessions`/`cues` con upsert per enunciato |
| F7.2 | Retention 5 giorni: pulizia all'avvio e ogni ora | DB stabile | ✅ `PurgeExpired` all'avvio della sessione + timer orario nell'app |
| F7.3 | Vista storico con anteprima | consultazione | ✅ scheda Storico con sessioni, anteprima e stato |
| F7.4 | Export TXT / SRT / JSON per sessione | integrazione con i flussi sottotitoli | ✅ comando `history export` + pulsanti nella UI |
| F7.5 | Scrittura a prova di crash (WAL) | nessuna perdita | ✅ `journal_mode=WAL`, `synchronous=NORMAL`, connessione protetta da lock |

**Criterio di accettazione:** le battute salvate sopravvivono alla chiusura; i record più vecchi di
5 giorni spariscono; l'export SRT è valido.
**Verificato:**

| Prova | Esito |
|---|---|
| Sessione reale (clip EN→IT di 8 frasi) registrata e chiusa | ✅ `#1 · 02/10/2026 23:57 · en->it · 8 battute · chiusa` |
| Export SRT della sessione | ✅ timecode `00:00:00,128 --> 00:00:03,616`, traduzione e originale per riga |
| 5 test unitari (upsert, retention su dati retrodatati, conteggio, SRT, JSON) | ✅ |
| Retention su DB di prova con sessione di 6 giorni | ✅ rimossa solo quella scaduta |

**Miglioramento aggiunto:** il traduttore locale a volte incolla le frasi ("ciao.Come stai"): ora
l'output viene normalizzato (`TranslationService`) prima di finire a schermo e nell'SRT.

**Nota:** la vista Storico è di consultazione e anteprima; la ricerca testuale è rimandata finché le
sessioni sono poche.

### F8 — Packaging, licenze, validazione su hardware debole
**Stato:** 🔄 quasi completa (2026-10-03)

| # | Attività | Esito |
|---|---|---|
| F8.1 | `dotnet publish` self-contained win-x64 | ✅ 175 MB, nessun runtime da installare |
| F8.2 | Installer Inno Setup (per-utente, senza admin) con scorciatoie e disinstallazione | ✅ 83,3 MB compressi |
| F8.3 | `THIRD-PARTY-NOTICES.md`, `LICENSE` MIT, README utente con screenshot | ✅ |
| F8.4 | Verifica licenza MTranServer (Apache-2.0) prima di includerlo | ✅ incluso con attribuzione |
| F8.5 | Test su macchina di fascia bassa | ⏳ da fare sulla macchina dell'utente |
| F8.6 | Icona applicazione e installer | ✅ |
| F8.0 | Migrazione a **.NET 10 LTS** + NAudio 3.x | ⏭ rinviata dopo la prima release |

**Verificato:**

| Prova | Esito |
|---|---|
| Installazione silenziosa in cartella di prova | ✅ exit 0, 297 file, 304,7 MB su disco |
| Avvio dalla cartella installata | ✅ finestra "Wisper Translator", versione 1.0.0.0 |
| Server di traduzione incluso | ✅ `tools\mtranserver.exe` presente e usato |
| Disinstallazione con l'app in esecuzione | ✅ l'app viene chiusa e i file rimossi |

**Difetto trovato e corretto:** con "chiudi nella barra delle applicazioni" attivo l'applicazione
sopravviveva alla richiesta di chiusura del sistema e la disinstallazione **non rimuoveva i file**
(verificato: exit 0 ma eseguibile ancora presente). Correzioni: chiusura forzata su `SessionEnding`
(spegnimento/disconnessione) e `taskkill` prima dell'installazione e durante la disinstallazione.

**Nota sul runtime:** l'app è pubblicata self-contained, quindi il PC di destinazione non ha bisogno di
.NET installato. La migrazione a .NET 10 riguarda solo la toolchain di sviluppo e serve prima che
.NET 8 esca dal supporto (novembre 2026).

**Criterio di accettazione (F8.5):** la verifica su una macchina di fascia bassa reale resta da fare:
qui tutto è stato misurato sulla macchina di sviluppo. `bench` e `hardware` permettono di rifare le
misure in due comandi.

### F9 — Estensioni (solo se richieste)
**Stato:** ✅ (2026-10-03) — estensioni richieste dall'utente il 2026-10-03

| # | Attività | Esito |
|---|---|---|
| F9.1 | Icona nell'area di notifica con menu e sottomenu (sorgenti, direzione, sessioni, impostazioni) | ✅ |
| F9.2 | Chiusura nella barra delle applicazioni, doppio clic per riaprire, avvisi a comparsa | ✅ |
| F9.3 | Menu nella finestra: Sessione / Strumenti / Aiuto con icone Fluent | ✅ |
| F9.4 | Icone sui pulsanti principali e spia del livello audio nella barra di stato | ✅ |
| F9.5 | Avvio automatico con Windows (impostazione + chiave Run dell'utente) | ✅ |
| F9.6 | Icona applicazione moderna, applicata a eseguibile e installer | ✅ |
| F9.7 | Copia rapida di originale/traduzione dal menu contestuale delle battute | ✅ |

**Difetto trovato e corretto durante F9:** `Speaker24` non esiste nell'enum di WPF-UI (è `Speaker024`):
un nome icona sbagliato fa fallire il parsing XAML e l'applicazione non si avvia più. Dopo la correzione
tutte le icone usate sono state verificate una per una contro l'enum.

### F10 — Sessioni, IA e PDF
**Stato:** ✅ (2026-10-03)

| # | Attività | Esito |
|---|---|---|
| F10.1 | Elenco delle sessioni nella scheda Storico con anteprima e stato | ✅ |
| F10.2 | Visualizzatore sessione: trascrizione a sinistra, contenuti IA a destra | ✅ |
| F10.3 | Riassunto breve e dettagliato, punti chiave | ✅ generati e salvati nella sessione |
| F10.4 | Mappa concettuale: JSON dal modello, disegno di nodi e relazioni in PNG | ✅ |
| F10.5 | PDF completo: intestazione, riassunto, punti chiave, mappa e trascrizione | ✅ |
| F10.6 | 9 provider IA: Ollama, compatibile OpenAI, OpenAI, DeepSeek, OpenRouter, Groq, Mistral, Gemini, Anthropic | ✅ |
| F10.7 | Impostazioni IA: endpoint, modello, chiave, lingua, temperatura, token, timeout, salta ragionamento | ✅ |
| F10.8 | Ricerca modelli installati (Ollama/LM Studio) e test connessione | ✅ |
| F10.9 | Conservazione delle sessioni configurabile (0 = per sempre) | ✅ |
| F10.10 | Comandi CLI `ai summary/points/map/render/report` | ✅ |

**Verificato con Ollama locale (`gemma4:12b`):**

| Prova | Esito |
|---|---|
| Test connessione | ✅ |
| Riassunto di una sessione reale | ✅ paragrafo + punti, in italiano |
| Punti chiave | ✅ 8 voci |
| Mappa concettuale | ✅ 9 nodi, 8 relazioni, PNG leggibile |
| PDF | ✅ 2 pagine con mappa inclusa (verificato con `pypdf`) |

**Difetti trovati e corretti in F10:**

1. I modelli locali "thinking" spendevano tutti i token nel ragionamento restituendo testo vuoto:
   con `reasoning_effort=none` la stessa richiesta passa da 67 s a 2,4 s e produce testo.
2. La mappa salvata in JSON non veniva riletta (maiuscole/minuscole): confronto reso case-insensitive.
3. Le dimensioni del disegno della mappa erano invertite: i nodi oltre la seconda colonna finivano
   fuori dall'immagine.
4. PdfSharp 6 non trova i font di sistema da solo: aggiunto un resolver che legge i TTF di Windows.

- F9.1 Motore MT nativo in-process (Marian CLI / bergamot P/Invoke) → rimuove il processo esterno.
- F9.2 Tier C con Vosk `small-it` per parziali istantanei.
- F9.3 ASR alternativo Parakeet TDT 0.6B v3 (sherpa-onnx) come opzione qualità.
- F9.4 Cattura per singola applicazione (process loopback).
- F9.5 Auto-update dell'applicazione.

---

## 6. Rischi e mitigazioni

| ID | Rischio | Impatto | Mitigazione |
|---|---|---|---|
| R-01 | CPU debole non regge Whisper `small` in tempo reale | alto | VAD aggressivo + tier C con `base` + degradazione a modello più piccolo in corsa |
| R-02 | Il microfono riprende l'audio delle casse → testo duplicato | medio | Avviso in UI, gain separati, presenza di un test col solo microfono; AEC in F9 |
| R-03 | Whisper "riscrive" le frasi già mostrate | medio | Politica LocalAgreement-2 + separazione visiva frase in corso/committata (F3/F5) |
| R-04 | Qualità MT locale insufficiente su frasi idiomatiche | medio | Cache + fallback cloud opt-in + possibilità di motore alternativo (F4/F9) |
| R-05 | Licenza non commerciale in un componente | alto | Già esclusi SeamlessStreaming e NLLB; verifica licenza MTranServer in F8.4 |
| R-06 | Loopback cattura anche l'audio dell'app stessa | basso | v1: l'app non riproduce audio; F9: process loopback con esclusione del proprio PID |
| R-07 | Modelli grandi su disco (più GB con tutti i tier) | basso | Download solo del tier rilevato + indicatore spazio + eliminazione |
| R-08 | Nessuna firma digitale → avviso SmartScreen | basso | Documentato nel README; firma valutata fuori scope v1 |

---

## 7. Domande aperte (con default già scelto → non bloccano)

| ID | Domanda | Default che applico se non dici altro |
|---|---|---|
| Q-01 | Nome soluzione: `WisperTranslator` (come la cartella) o `WhisperTranslator`? | `WisperTranslator` |
| Q-02 | Hotkey di default | `Ctrl+Alt+W` mostra/nascondi, `Ctrl+Alt+M` mic, `Ctrl+Alt+S` sistema, `Ctrl+Alt+L` inverti direzione |
| Q-03 | Server MT locale incluso nell'installer? | RISOLTA: licenza Apache-2.0, il binario si può includere; l'app lo scarica da sé solo se manca |
| Q-04 | Cloud MT (opt-in) incluso in v1? | Sì, disattivato di default, con avviso privacy esplicito |
| Q-05 | Numero massimo righe visibili nel widget | 20 totali, 6 visibili con scroll automatico |
| Q-06 | Trascrizione originale visibile insieme alla traduzione? | Sì, bilingue (originale piccolo sopra, traduzione grande) |
| Q-07 | Il tier C include Vosk per i parziali? | Sì, se la latenza misurata in F3 non soddisfa RNF-02 |
| Q-08 | Auto-update | Fuori scope v1 |

---

## 8. Tracker avanzamento

| Fase | Stato | Data inizio | Data fine | Note |
|---|---|---|---|---|
| F0 Setup | ✅ | 2026-10-02 | 2026-10-02 | build e 14 test verdi |
| F1 Audio + VAD | ✅ | 2026-10-02 | 2026-10-02 | loopback + mic verificati su hardware reale, VAD sul silenzio PASS |
| F2 ASR + bench | ✅ | 2026-10-02 | 2026-10-02 | base RTF 0,047 / small RTF 0,145, accuratezza 96-100% |
| F3 Real-time | ✅ | 2026-10-02 | 2026-10-02 | parziali base 0,51 s + finali small, 100% |
| F4 Traduzione | ✅ | 2026-10-02 | 2026-10-02 | locale, mediana 23 ms, p95 37/29 ms |
| F5 UI | ✅ | 2026-10-02 | 2026-10-02 | widget + overlay verificati, prova EN→IT su 8 frasi |
| F6 Model manager | ✅ | 2026-10-02 | 2026-10-02 | catalogo con hash, ripresa, import sicuro, tiering |
| F7 Storico | ✅ | 2026-10-02 | 2026-10-03 | SQLite + retention 5 giorni + export SRT/TXT/JSON |
| F8 Packaging | 🔄 | 2026-10-03 | — | installer 1.0.0 creato e verificato; manca il test su PC debole |
| F9 Tray e background | ✅ | 2026-10-03 | 2026-10-03 | icona, menu, sottomenu, avvio con Windows |
| F10 Sessioni, IA e PDF | ✅ | 2026-10-03 | 2026-10-03 | riassunti, mappe, PDF, 9 provider |

---

## 9. Changelog di lavorazione

> Una riga per ogni modifica sostanziale. Formato: `data · fase · cosa è cambiato · esito verifica`.

| Data | Riferimento | Modifica | Esito |
|---|---|---|---|
| 2026-10-02 | piano | Creato `PLAN.md` con requisiti, decisioni, architettura, fasi e tracker | approvato dall'utente |
| 2026-10-02 | F0.1 | Creata solution `WisperTranslator.sln` con progetti App (WPF), Core, Cli, Tests e relativi riferimenti | ✅ |
| 2026-10-02 | F0.2 | Aggiunti WPF-UI 4.3.0, NAudio 2.2.1, Whisper.net 1.9.1 (+Runtime), OnnxRuntime 1.30.0, Microsoft.Data.Sqlite 10.0.12 | ✅ |
| 2026-10-02 | D-03 | NAudio 3.1 richiede .NET 9+: fissato a 2.2.1 mantenendo .NET 8 LTS | ✅ decisione aggiornata |
| 2026-10-02 | F0.3 | Creato `.gitignore`; `Directory.Build.props` ed `.editorconfig` rinviati | ✅ |
| 2026-10-02 | F0.5 | Creati `README.md` e `docs/MODELS.md` | ✅ |
| 2026-10-02 | F1.1 | `AudioDevices` + comando `devices`: enumerazione uscite/ingressi con predefinito marcato | ✅ 7 uscite, 4 ingressi |
| 2026-10-02 | F1.2/F1.3 | `VoiceCapture`: loopback di sistema e microfono normalizzati a 16 kHz mono float32 | ✅ |
| 2026-10-02 | F1.4 | `AudioMixer` con gain, toggle e clipping; `FloatRingBuffer` con scarto dei campioni vecchi | ✅ 6 test |
| 2026-10-02 | F1.5 | `SileroVad` (ONNX) + `EndpointDetector` + `SpeechSegmenter` con pre-roll | ✅ 7 test + self-test PASS |
| 2026-10-02 | F1.6 | Comando `capture --play --dump` e dump WAV in `%LOCALAPPDATA%\WisperTranslator\audio-dump` | ✅ WAV 16 kHz mono verificato |
| 2026-10-02 | F1 | Test end-to-end loopback con segnale sintetico: 13,5 s catturati, 3 enunciati rilevati | ✅ |
| 2026-10-02 | F2.1 | `IAsrEngine` + `WhisperAsrEngine` (Whisper.net 1.9.1) + `AsrScore` per il confronto dei testi | ✅ |
| 2026-10-02 | F2.2 | `AsrModels` (catalogo) + `ModelStore`: download ggml con progresso e file `.part` | ✅ base e small scaricati |
| 2026-10-02 | F2.5 | Comando `tts`: clip vocali reali con le voci SAPI (Elsa it-IT, Zira en-US) + `.txt` con il testo atteso | ✅ |
| 2026-10-02 | F2.3 | Comando `bench`: RTF, RAM di picco, accuratezza, riga in `docs/BENCHMARKS.md` | ✅ base it/en e small it |
| 2026-10-02 | F2 | Verifica reale: base RTF 0,047 (96% it), small RTF 0,145 (100% it), RAM 311/555 MB | ✅ |
| 2026-10-02 | F2.4 | Supporto CUDA/Vulkan rinviato: target CPU/iGPU, default CPU | ⏭ tracciato |
| 2026-10-02 | F3.1 | `RealtimeTranscriber`: worker in background, la lettura audio non si blocca mai | ✅ |
| 2026-10-02 | F3.2 | `LocalAgreementPolicy` (LocalAgreement-2) + 5 test unitari | ✅ |
| 2026-10-02 | F3.3 | Corretto il congelamento delle invenzioni: finale autoritativa + MinPartial 1,0 s | ✅ accuratezza 88% → 96/100% |
| 2026-10-02 | F3.5 | Cascata a due modelli (`--final-model`): parziali `base`, finali `small` | ✅ 0,51 s parziale, 100% accuratezza |
| 2026-10-02 | F3 | End-to-end reale su loopback con clip TTS: latenze entro i target RNF-02/RNF-03 | ✅ |
| 2026-10-02 | F4.1 | `ITranslationEngine` + `LocalHttpEngine` (MTranServer) + `TranslationService` con cache | ✅ |
| 2026-10-02 | F4.2 | `TranslationServer`: download con verifica SHA-256, avvio nascosto, stop, log recenti | ✅ 123 MB, Apache-2.0 |
| 2026-10-02 | F4.5 | Cache delle traduzioni + 3 test unitari | ✅ |
| 2026-10-02 | F4 | Benchmark su 20+20 frasi: mediana 23 ms, p95 37/29 ms | ✅ |
| 2026-10-02 | D-20 | MTranServer incluso fra le dipendenze ridistribuibili (Apache-2.0) | ✅ |
| 2026-10-02 | F5.1/F5.2 | `FluentWindow` con backdrop Mica, tema di sistema automatico, widget trascinabile | ✅ |
| 2026-10-02 | F5.3/F5.4 | Elenco battute: inserimento in testa, animazione, cap configurabile, distinzione parziale/finale | ✅ |
| 2026-10-02 | F5.5/F5.6 | Menu rapido e hotkey globali su `OnSourceInitialized` | ✅ Ctrl+Alt+M occupata → N |
| 2026-10-02 | F5.7 | `OverlayWindow` click-through, no-activate, escluso dalle catture, a tutto schermo | ✅ stili `0x80800A8`, affinity `0x11` |
| 2026-10-02 | F5.8 | Finestra Impostazioni: aspetto, modelli, dispositivi; preferenze in `settings.json` | ✅ |
| 2026-10-02 | F5 | Prova reale EN→IT con 8 frasi consecutive: 8/8 riconosciute, tradotte e mostrate | ✅ |
| 2026-10-02 | F5 | Aggiunta opzione "Nascondi l'overlay dalle registrazioni" | ✅ |
| 2026-10-02 | F6.1 | `ModelCatalog`: URL, dimensioni, SHA-256 reali, licenze; VAD e modelli MT inclusi | ✅ |
| 2026-10-02 | F6.2 | `HttpDownload.DownloadVerifiedAsync`: ripresa `Range`, controllo dimensione, hash, rename atomico | ✅ 4 test |
| 2026-10-02 | F6.3 | `ModelStore.ImportAsync` con area di sosta e verifica preventiva | ✅ difetto critico corretto |
| 2026-10-02 | F6.4 | Comandi `models …` + pulsanti nella scheda Modelli | ✅ |
| 2026-10-02 | F6.5 | `HardwareDetector` con VRAM reale dal registro di Windows e tier A/B/C | ✅ |
| 2026-10-02 | F6 | Hash riallineati ai file ufficiali Hugging Face (il mirror di Whisper.net serve byte diversi) | ✅ |
| 2026-10-02 | F6 | `large-v3-turbo` misurato su CPU: RTF 0,585, 1002 MB → non consigliato sui tier B/C | 📊 |
| 2026-10-03 | F7.1 | `SessionStore` SQLite con schema sessioni/battute, upsert per enunciato, WAL | ✅ |
| 2026-10-03 | F7.2 | Retention 5 giorni: pulizia all'avvio della sessione e ogni ora | ✅ |
| 2026-10-03 | F7.4 | `HistoryExporter` (SRT con timecode, TXT, JSON) + comando `history` + pulsanti nella UI | ✅ |
| 2026-10-03 | F7 | Sessione reale registrata (8 battute en→it) ed esportata in SRT verificato | ✅ |
| 2026-10-03 | F7 | Normalizzazione degli spazi dopo la punteggiatura nell'output del traduttore | ✅ |
| 2026-10-03 | F9.1 | `TrayIcon`: icona nell'area di notifica con menu e sottomenu, avvisi a comparsa | ✅ |
| 2026-10-03 | F9.2/F9.3 | Chiusura nella barra delle applicazioni, menu Sessione/Strumenti/Aiuto con icone | ✅ |
| 2026-10-03 | F9.4/F9.7 | Icone sui pulsanti, spia livello audio, copia rapida delle battute | ✅ |
| 2026-10-03 | F9.5/F9.6 | Avvio automatico con Windows; icona applicazione generata e applicata | ✅ |
| 2026-10-03 | F9 | Corretto il crash per il nome icona `Speaker24` → `Speaker024`; tutte le icone verificate | ✅ |
| 2026-10-03 | F10.1/F10.2 | Elenco sessioni e `SessionDetailWindow` con trascrizione e pannello IA | ✅ |
| 2026-10-03 | F10.6/F10.7 | Client IA per 9 provider e scheda impostazioni dedicata | ✅ |
| 2026-10-03 | F10.8 | Ricerca modelli Ollama/LM Studio e test connessione | ✅ |
| 2026-10-03 | F10.4 | `ConceptMapRenderer`: mappa concettuale disegnata in PNG | ✅ |
| 2026-10-03 | F10.5 | `PdfReportBuilder`: report PDF completo | ✅ 2 pagine verificate |
| 2026-10-03 | F10 | Corretto il ragionamento dei modelli thinking (67 s → 2,4 s) e la rilettura della mappa | ✅ |
| 2026-10-03 | F10.10 | Comandi CLI `ai …` per generare contenuti e report da script | ✅ |
| 2026-10-03 | F8.1 | Pubblicazione self-contained win-x64 (175 MB, nessun runtime richiesto) | ✅ |
| 2026-10-03 | F8.2 | Installer Inno Setup per-utente con licenza, nota informativa, scorciatoie e disinstallazione | ✅ 83,3 MB |
| 2026-10-03 | F8.3 | `LICENSE`, `THIRD-PARTY-NOTICES.md`, README utente con screenshot | ✅ |
| 2026-10-03 | F8 | Corretto il blocco della disinstallazione quando l'app restava nella barra delle applicazioni | ✅ |
| 2026-10-03 | F8 | Verifica reale: installazione, avvio dalla cartella installata, disinstallazione pulita | ✅ |

---

## 10. Glossario

- **Loopback**: cattura dell'audio che il PC sta riproducendo, senza cavi né "Stereo Mix".
- **VAD**: rilevatore di attività vocale; separa parlato e silenzio.
- **Enunciato**: porzione di audio compresa tra due silenzi, inviata all'ASR.
- **Parziale**: trascrizione provvisoria di una frase ancora in corso.
- **Committato**: testo congelato, non verrà più riscritto.
- **RTF** (Real Time Factor): tempo di calcolo diviso durata audio; < 1 significa più veloce del tempo reale.
- **Tier**: fascia hardware rilevata che determina i modelli usati.
- **Backdrop Mica/Acrylic**: materiali di sfondo nativi di Windows 11.
