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

**Distribuzione (F8.7):**

| Voce | Valore |
|---|---|
| Repository | `renegadeFree/wisper-translator` — **privato** |
| Release | `v1.0.0` con note, hash SHA-256 e installer allegato |
| Installer | `WisperTranslator-Setup-1.0.0.exe` — 83,3 MB |
| SHA-256 | `875389f1176fc7c58738c41de9866caad76a05d8d43a01fd088226af1b0911f3` |
| File versionati | 93 (nessun binario, nessun modello) |

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
| 2026-10-03 | F8.7 | Repository GitHub privato `renegadeFree/wisper-translator` creato e codice pushato (93 file) | ✅ |
| 2026-10-03 | F8.7 | Release `v1.0.0` pubblicata con installer 83,3 MB e hash SHA-256 nelle note | ✅ |

---

## 10. Glossario

---

## 11. Accelerazione GPU (F11) — da decidere

**Domanda dell'utente (2026-10-03):** durante la trascrizione la CPU è molto occupata; perché non viene
usata la GPU?

**Risposta misurata (macchina di sviluppo, RTX 3080 Ti + Ryzen 9 7900X3D):**

| Fase | Costo misurato | Dove va il tempo |
|---|---|---|
| Trascrizione Whisper `base` q5_1, 11,3 s di audio | 0,57 s di wall, **20,22 core-secondi** (≈12 thread saturi), 1,78 core-secondi per secondo di audio | **è qui il carico della CPU** |
| Traduzione Bergamot IT→EN, 20 frasi | **152 ms di CPU per frase**, ~2 core in media, 76 ms di wall | trascurabile |

**Perché la GPU resta ferma:**

1. Il runtime nativo incluso è la build **CPU** di whisper.cpp: nella cartella pubblicata ci sono solo
   `whisper.dll`, `ggml-whisper.dll`, `ggml-cpu-whisper.dll`. Zero file CUDA/Vulkan (verificato: 0 su
   298 file installati), quindi non esiste proprio il codice che parla con la scheda video.
2. La traduzione usa bergamot/intgemm, che è **CPU-only per progetto** (matrici intere ottimizzate per
   CPU) e non ha variante GPU: non è il collo di bottiglia e non guadagnerebbe nulla.
3. È una scelta registrata in **D-06 / F2.4**: il target dichiarato erano macchine con GPU integrata
   senza CUDA, quindi il default è la build CPU. Il tier rilevato dice "A" (c'è una 3080 Ti) ma la
   raccomandazione dei modelli dipende solo dai core, proprio perché il runtime spedito non usa la GPU.

**Opzioni (misure da fare in F11):**

| Opzione | Costo sull'installer | Requisito | Guadagno atteso |
|---|---|---|---|
| `Whisper.net.Runtime.Cuda` | +136 MB di pacchetto | GPU NVIDIA + driver recente (CUDA 12 già presente qui) | stimato 5-15× sul decode, CPU quasi a zero |
| `Whisper.net.Runtime.Vulkan` | +35 MB | qualsiasi GPU (Intel/AMD/NVIDIA, anche iGPU) | stimato 3-8×, da misurare |
| Entrambe con selezione a runtime | +171 MB | — | il massimo, ma le due build espongono lo stesso `whisper.dll`: servono cartelle separate e selezione del percorso nativo prima di creare la factory, con fallback su CPU |
| Nessuna (stato attuale 1.0.0) | 0 | — | funziona su tutto, CPU satura mentre si parla |

**Ottimizzazione del degrado progressivo (2026-10-03) — risolta.** Segnalazione dell'utente: con un
video YouTube la trascrizione parte bene e poi rallenta sempre di più.

**Causa trovata (riprodotta con una clip di parlato continuo di 88 s):** con pause sotto i 600 ms il
VAD non chiude l'enunciato, che cresce fino al tetto di 15 s. A ogni parziale (ogni 700 ms) il decoder
ridecodificava **tutto** l'enunciato, quindi: costo per decodifica in crescita, coda di lavoro che non
si smaltisce, e — l'effetto peggiore — **loop di ripetizione** di Whisper, che faceva crollare la
qualità e gonfiava il testo da tradurre.

**Correzioni applicate:**

| # | Modifica | Perché |
|---|---|---|
| 1 | `MaxUtteranceSeconds` 15 → **8 s**, `MinSilenceSeconds` 0,6 → **0,5 s** | enunciati più corti: ogni decodifica costa poco e le frasi finali arrivano più spesso |
| 2 | `AsrTextGuard.TrimRepetitions` sul testo del decoder | taglia un ciclo di ripetizione alla prima occorrenza (5 test unitari) |
| 3 | Intervallo dei parziali adattivo (700 ms → fino a 1600 ms con enunciati lunghi) | non si accoda lavoro che non riduce la latenza |
| 4 | Parziale annullato se il decoder è occupato (`_workerBusy`) | la coda non cresce più |
| 5 | Errori di decodifica catturati e resi visibili (`LastError`) invece di fermare il worker | il flusso non si blocca più in silenzio |

**Misure prima/dopo** (stessa clip continua di 88,5 s, `base` per i parziali + `small` per le finali):

| Metrica | Prima | Dopo |
|---|---|---|
| Accuratezza del testo committato | **77,2 %** | **96,2 %** |
| Parziali committati (carico CPU) | 70 | **34** |
| Latenza parziale (mediana / peggiore) | 0,52 s / 1,03 s | 0,50 s / **0,52 s** |
| Latenza finale (mediana / peggiore) | 1,84 s / 2,49 s | **1,65 s / 2,06 s** |
| Testo con cicli di ripetizione | sì (frase ripetuta 5 volte) | no |

**Accelerazione GPU (in corso):** il caricatore di Whisper.net prova già da solo
Cuda → Vulkan → Cpu, quindi le "opzioni in base al PC" esistono: serve fornirgli la build nativa
giusta. Ricognizione fatta:

| Fatto verificato | Valore |
|---|---|
| Build CPU attualmente spedita | solo `ggml-cpu-whisper.dll`, nessun file CUDA/Vulkan (0 su 298) |
| `Whisper.net.Runtime.Vulkan` | 35 MB di pacchetto, `ggml-vulkan-whisper.dll` 55 MB |
| `Whisper.net.Runtime.Cuda.Windows` | 136 MB di pacchetto, `ggml-cuda-whisper.dll` 147 MB |
| Ricerca del caricatore | `{LibraryPath}\runtimes\<rid>\native` come prima cartella, poi la base dell'app |
| Esito del primo tentativo | impostare `LibraryPath` alla cartella del pacchetto **non basta**: il runtime caricato resta `Cpu` (`RuntimeOptions.LoadedLibrary`) |

**Prossimo passo per la GPU (F11):** leggere `RuntimePathResolver.GetRuntimePath` per rispettare la
convenzione di cartelle per libreria (o installare la build scelta direttamente in
`runtimes\win-x64\native` dell'app). Finché la build non è *verificata* il selettore resta su CPU:
il file `verified.txt` nella cartella del pacchetto è il interruttore. La CPU resta sempre la rete di
sicurezza per i PC senza GPU. Comandi utili: `-- accel auto`, `-- bench <clip> --model base`.

## 12. Lavorazione v1.1 — fermata sicura e real-time (F12–F17)

Riferimento: piano approvato il 2026-10-03 (fermata sicura, cascata a tre corsie, motori per fascia).

### F12 — Fermata sicura (✅ codice, ⏳ verifica sul campo in F17)

| # | Attività | Esito |
|---|---|---|
| F12.1 | Diagnosi del crash allo stop dal registro eventi Windows | ✅ access violation `0xc0000005` in `ggml-cpu-whisper.dll`: il factory nativo veniva liberato con una decodifica in volo |
| F12.2 | `WhisperAsrEngine`: guardia per motore (una decodifica alla volta) e `Dispose()` che aspetta la decodifica in corso | ✅ |
| F12.3 | `AsrEnginePool`: i modelli restano caldi tra le sessioni e vengono liberati solo all'uscita | ✅ riavvio sessione senza ricaricare il ggml |
| F12.4 | `RealtimeTranscriber.RunAsync`: attende sempre i worker (anche su eccezione) con tetto di sicurezza (3 s) | ✅ |
| F12.5 | Stop senza eccezioni fatali: handler `async void` protetti, eccezioni globali su `logs\errori.log` | ✅ |
| F12.6 | Test di regressione: stop con decodifica bloccante, stop con motore veloce, coda provvisoria | ✅ 4 test |
| F12.7 | Recupero automatico dello storico danneggiato: `history.db` illeggibile messo da parte e ricreato, niente errore a schermo | ✅ 2 test |
| F12.8 | Causa del mancato recupero: il **connection pooling** di Microsoft.Data.Sqlite teneva aperto il file → `Pooling=False` | ✅ |
| F12.9 | Comando `history check` per diagnosticare/recuperare il database da riga di comando | ✅ |

### F13 — Cascata a tre corsie e UI progressiva (✅ codice)

| # | Attività | Esito |
|---|---|---|
| F13.1 | Due worker indipendenti nel trascrittore: parziali e rifinitura non si bloccano più a vicenda | ✅ test `LaRifinituraNonBloccaIParziali` |
| F13.2 | Testo provvisorio a schermo (coda instabile in corsivo attenuato) accanto al testo stabile | ✅ widget + overlay |
| F13.3 | Parziali più reattivi: intervallo 500→1200 ms adattivo, audio minimo 0,8 s | ✅ |
| F13.4 | Traduzione progressiva: debounce 300 ms, annullamento delle versioni superate | ✅ |
| F13.5 | Policy di consenso resettata a ogni nuovo enunciato (nessun trascinamento del testo) | ✅ |

| Metrica | Prima | Dopo (atteso, da misurare in F17) |
|---|---|---|
| Blocco dei parziali durante la rifinitura | 2,0–3,5 s per frase | 0 |
| Prima comparsa di testo | ~1,5–3 s | ~0,5–0,9 s |
| Stop con decodifica in volo | crash `0xc0000005` | ritorno entro 1,5 s |

### F14 — NeMo-Speech.cpp: Nemotron streaming + Parakeet (✅ live, ⏳ finale)

Ricognizione (2026-10-03): **NVIDIA NeMo-Speech.cpp v0.2.0**, Apache-2.0, binari Windows
CPU/Vulkan/CUDA, runtime ggml. Il modello di default è **Nemotron 3.5 ASR Streaming 0.6B**
(40 lingue, **italiano incluso**, chunk da 160 ms, punteggiatura automatica, 708 MB q8_0).
Il server locale espone API HTTP compatibili OpenAI e un WebSocket realtime.

| # | Attività | Esito |
|---|---|---|
| F14.1 | `NeMoSpeechServer`: runtime scaricato on demand (5,5 MB), avvio come processo figlio, attesa di disponibilità, spegnimento | ✅ |
| F14.2 | `NeMoSpeechHost`: un solo server condiviso tra le due corsie, spento all'uscita (anche su chiusura anomala) | ✅ |
| F14.3 | `NeMoSpeechEngine`: POST `/v1/audio/transcriptions` con WAV PCM16 in memoria | ✅ |
| F14.4 | `NeMoModels`: download del GGUF (708 MB) con verifica di presenza e cancellazione | ✅ |
| F14.5 | Verifica reale su clip inglese 10,3 s | ✅ **0,90 s, RTF 0,088**, testo con punteggiatura corretto |

| Motore, stessa clip 10,3 s (24 thread) | Tempo | RTF | Punteggiatura |
|---|---|---|---|
| Whisper base q5_1 | 0,53 s | 0,051 | no |
| Whisper small q5_1 | 1,65 s | 0,145 | no |
| Vosk small-en | 1,23 s (con caricamento modello) | 0,120 | no |
| **NeMo Nemotron 3.5 q8_0** | **0,90 s** | **0,088** | **sì** |

**Deviazione registrata (D-22):** per la v1.1 la corsia definitiva usa lo **stesso** Nemotron
invece di un secondo modello Parakeet: un solo download e ~0,9 GB di RAM invece di ~1,6 GB,
con qualità già superiore a Whisper small. Parakeet TDT 0.6B v3 (CC-BY-4.0, 25 lingue UE)
resta l'opzione prevista per la corsia finale in una versione successiva.

### F15 — Vosk per i PC minimi (✅)

| # | Attività | Esito |
|---|---|---|
| F15.1 | Pacchetto NuGet `Vosk` 0.3.38 + `VoskAsrEngine` (PCM16 in memoria, nessun processo esterno) | ✅ |
| F15.2 | `VoskModels`: download ed estrazione automatica del modello it/en (34-50 MB) | ✅ |
| F15.3 | Verifica reale su clip inglese 10,3 s | ✅ RTF 0,120, testo corretto (senza punteggiatura: la aggiunge la corsia finale) |

### F16 — Profili automatici per fascia hardware (✅)

| # | Attività | Esito |
|---|---|---|
| F16.1 | `PerformancePreset` (Auto/Reattivo/Equilibrato/Qualità) e `PerformanceProfile` con regole su RAM, core e tier | ✅ |
| F16.2 | `AsrBackend` (Whisper/Vosk/NeMo) scelto per ciascuna corsia, con override manuale | ✅ |
| F16.3 | Impostazioni: selettore profilo, motori per corsia, "Scarica i modelli del profilo" | ✅ |
| F16.4 | Fallback automatico a Whisper con messaggio se un motore non è disponibile | ✅ |
| F16.5 | Comandi CLI `transcribe --engine vosk|nemo` e `live --engine vosk` per le verifiche | ✅ |

### F17 — Collaudo sul campo, installer e release (✅ verificato)

Installer **1.1.0** ricostruito e installato in silenzio (`/VERYSILENT`, codice 0) su
`C:\Program Files\Wisper Translator`. Collaudo fatto sul binario **installato**, non su quello di
sviluppo, con la sessione avviata da riga di comando (`--autostart --autostop <secondi>`).

| Metrica | Prima (v1.0.0) | Dopo (v1.1.0) | Come misurata |
|---|---|---|---|
| Arresto con "Ferma" | crash `0xc0000005` in `ggml-cpu-whisper.dll` | **84–106 ms**, nessun crash | 3 sessioni reali + registro eventi |
| Latenza del testo provvisorio | ~500 ms (e bloccata 2–3,5 s a ogni frase finale) | **mediana 137 ms**, p90 236 ms, max 550 ms | `logs\sessione-*.jsonl` |
| Latenza della frase definitiva | mediana 1.650 ms | **mediana 234 ms**, max 445 ms | idem |
| Prima comparsa di testo | ~1,5–3 s | ~1,2 s di audio (0,8 s minimo + prima decodifica) | idem |
| Parole con punteggiatura | no (Whisper base/small) | **sì** (Nemotron) | log e screenshot |
| Processi residui dopo lo stop | — | **0** (app, NeMo e server di traduzione chiusi) | elenco processi |

**Nota sul collaudo con YouTube:** il video richiesto `fNTBhi-uEf0` viene servito da YouTube con
il **doppiaggio automatico in italiano**, quindi il test "inglese → italiano" su quel flusso misura
in realtà italiano + inglese misti. Sul video il sistema ha comunque tenuto **mediana 252 ms** sui
parziali e **750 ms** sulle finali. La verifica pulita inglese → italiano è stata fatta con le clip
di prova (sotto), perché l'audio era l'unico presente sul sistema.

**Verifica visiva (clip inglesi, EN → IT):**

```
I did not understand what you said. Could you repeat it?
Non ho capito cosa hai detto. Potresti ripeterlo?

The weather is nice today, and we are going to the beach with friends.
Il tempo è bello oggi, e stiamo andando in spiaggia con gli amici.
```

| # | Attività | Esito |
|---|---|---|
| F17.1 | Installer 1.1.0 (89,7 MB) con Vosk e i runtime nativi inclusi | ✅ |
| F17.2 | Installazione silenziosa e avvio della versione installata | ✅ |
| F17.3 | 3 sessioni reali con arresto: nessun evento di crash | ✅ |
| F17.4 | Analisi delle latenze per fase dal log diagnostico | ✅ |
| F17.5 | Recupero dello storico danneggiato dell'utente (3 sessioni precedenti recuperate) | ✅ |

**Rinviato e dichiarato (non fatto in questa versione):**

| Voce | Stato | Motivo |
|---|---|---|
| Runtime GPU Vulkan/CUDA | ⏳ | La cascata è CPU-first e già sotto i target; l'accelerazione va aggiunta con verifica misurata (NeMo-Speech.cpp ha build Vulkan/CUDA pronte) |
| Modello Parakeet per la corsia finale | ⏳ | Deviazione D-22: un solo modello (Nemotron) dimezza RAM e download mantenendo qualità superiore a Whisper small |
| Collaudo su PC di fascia bassa reale (F8.5) | ⏳ | Serve la macchina dell'utente: profilo **Reattivo** (Vosk + Whisper base, ~0,4 GB) |
| Pubblicazione su GitHub della v1.1.0 | ⏳ | Da fare quando l'utente approva il collaudo |

---

## 13. Lavorazione v1.2 — impostazioni visibili, template di mappe e PDF (F18–F22)

Segnalazioni dell'utente (2026-10-03): «nelle impostazioni non ho notato i nuovi modelli o le nuove
impostazioni», «vorrei che tra i vari slider ci fosse indicato il valore», «aggiungessi parecchi
template diversi per la generazione di mappe concettuali (anche orientamento e stile)», «cerca su
GitHub i migliori skill e integrali», «stessa cosa per i pdf».

### F18 — Impostazioni visibili (✅)

| # | Attività | Esito |
|---|---|---|
| F18.1 | Nuova scheda **Prestazioni**: hardware, profilo, motore effettivo per corsia, stato installazione, RAM stimata, applica/scarica/verifica | ✅ |
| F18.2 | **Valori sugli slider**: controllo `SliderRow` con etichetta e valore formattato (px, %, frasi, giorni, temperatura) | ✅ |
| F18.3 | Catalogo completo: **Vosk it/en**, **Nemotron 3.5**, **runtime NeMo**, **runtime Graphviz** oltre a Whisper, VAD e Bergamot | ✅ |
| F18.4 | `ModelPackaging` (SingleFile/Zip/Runtime) + `ModelStore.EnsurePackAsync` con verifica SHA-256 ed estrazione atomica | ✅ |
| F18.5 | Hash reali calcolati sui file scaricati (Vosk, NeMo, Nemotron, Graphviz) | ✅ |
| F18.6 | Selettore runtime GPU (Nessuno/Vulkan/CUDA) marcato sperimentale, con CPU sempre attiva | ✅ |
| F18.7 | Scheda **Template** con anteprima, importazione, esportazione ed eliminazione | ✅ |

### F19 — Motore di template (✅)

| # | Attività | Esito |
|---|---|---|
| F19.1 | `MapTemplate` (motore Graphviz, orientamento, stile, palette, limiti, istruzioni) | ✅ |
| F19.2 | `PdfTemplate` (pagina, stile, sezioni `ai/map/transcript/summary/punti`) | ✅ |
| F19.3 | `TemplateStore`: cartella `templates\{maps,pdfs}`, pacchetto incluso materializzato una volta sola | ✅ |
| F19.4 | Importazione da **cartella o zip** con `template.json` o `SKILL.md` (front-matter + istruzioni) | ✅ |
| F19.5 | Avviso esplicito se lo skill parla di Mermaid/Excalidraw/Lark: il disegno resta locale | ✅ |
| F19.6 | **14 template mappa** inclusi e **8 template PDF** inclusi, con fonte e licenza | ✅ |

### F20 — Graphviz e mappe (✅)

| # | Attività | Esito |
|---|---|---|
| F20.1 | `GraphvizRuntime`: zip portatile ufficiale 16.1.0, **9.767.733 byte**, SHA-256 `733e49626c492242eb8dca30ea627b6ead20710e207998c7933b4909d92d6abc` | ✅ verificato |
| F20.2 | `DotGraphBuilder`: gruppi come cluster, tinte mescolate con lo sfondo per la leggibilità del testo | ✅ |
| F20.3 | Numeri sempre con il punto (la cultura italiana produceva `10,5` e mandava in errore DOT) | ✅ |
| F20.4 | `dpi` solo per il PNG: su PDF alterava la scala e tagliava la mappa | ✅ |
| F20.5 | Rendering verificato di **tutti i 14 template**: PNG 27-104 KB + PDF, nessun errore | ✅ |
| F20.6 | Ripiego sul disegno interno se Graphviz non è installato | ✅ |

### F21 — PDF guidato dai template (✅)

| # | Attività | Esito |
|---|---|---|
| F21.1 | `PdfReportBuilder` guidato dal template: copertina, sezioni in ordine, intestazione, piè di pagina, numeri, indice | ✅ |
| F21.2 | Mappa **vettoriale**: pagina PDF di Graphviz importata con `PdfReader`/`ImportPage`, titolo sulla pagina della mappa | ✅ verificato a 60 dpi, nessun taglio |
| F21.3 | Sezioni IA come chiamate brevi separate (`AiAssistant.RunTemplateAsync`) | ✅ |
| F21.4 | Markdown intermedio salvato accanto al PDF | ✅ |
| F21.5 | Verifica visiva: verbale di riunione 4 pagine, executive summary, appunti di lezione | ✅ |

### F22 — Test e distribuzione (🔄)

| # | Attività | Esito |
|---|---|---|
| F22.1 | Test automatici: escape DOT, limiti, orientamento, front-matter, import cartella/zip, report PDF, mappa vettoriale | ✅ **67 test verdi** (erano 51) |
| F22.2 | Installer **1.2.0** (89,8 MB) e installazione silenziosa | ✅ codice 0, 302 file installati, `libvosk.dll` incluso |
| F22.3 | Collaudo in UI delle nuove schede | ✅ Prestazioni (motori reali), Template (anteprima Graphviz), Aspetto (valori su tutti gli slider) |
| F22.4 | Verifica sul binario installato: Nemotron visto come installato (707,7 MB) senza riscaricare | ✅ migrazione dal vecchio percorso |
| F22.5 | Rendering di tutti i 14 template mappa (PNG + PDF) e generazione di 3 report PDF con mappa vettoriale | ✅ |

**Comandi aggiunti per le verifiche:**

| Comando | Cosa fa |
|---|---|
| `wisper map list` | elenca i template di mappa e lo stato di Graphviz |
| `wisper map all` | disegna tutti i template (PNG + PDF) in `exports\mappe` |
| `wisper map <id>` | disegna un singolo template |
| `wisper report list` | elenca i template di report |
| `wisper report <id> [--dot]` | genera un report di prova con mappa vettoriale e Markdown |
| `app.exe --settings=<n>` | apre direttamente una scheda delle impostazioni (0 aspetto, 1 prestazioni, 2 modelli, 3 template, 4 storico, 5 IA) |

**Deviazione registrata (D-23):** la mappa vettoriale nel PDF ha la misura naturale del grafo
scalata nel riquadro utile (Graphviz non applica `page=` in questa build): la pagina della mappa
può quindi essere più larga o più alta di A4, senza tagli. È il comportamento tipico degli inserti
di diagramma e resta leggibile a stampa.

---

## 14. Lavorazione v2.0 — conversazione con parlanti e barra Apple-style (F23–F26)

Richiesta dell'utente (2026-10-03): «se volessi trascrivere una conversazione, magari una meet tra
due o più persone o una call whatsapp, vorrei avere l'opzione, traduzione o solo trascrizione nel
pannello», «implementa tutte le variabili del caso, quindi regole di riconoscimento vocale tra vari
utenti», «vorrei una versione con una barra (che possa spostare dove voglio) simile a wispr flow,
apple style, con massimo 5 frasi di cui solo le prime 2 visibili», «fluttuante con grafica
acrilica», «poi fai push e release».

### F23 — Modalità conversazione e diarizzazione (✅)

| # | Attività | Esito |
|---|---|---|
| F23.1 | Diarizzatore predefinito **Nemotron-3-Diarization q8**: 107.012.128 byte, SHA-256 `08456d9e22cd9a323c0364d98375f3746d6e68507ebb705cd46438c534c7a3a1`, fino a 8 parlanti, OpenMDW-1.1 | ✅ hash calcolato sul file scaricato |
| F23.2 | Alternativa **Sortformer 4spk v2 q8**: 147.075.776 byte, SHA-256 `0679cfeb1ce356d0dea9470b31274f4bfc7eb927497d82005483770666da998a`, CC-BY-4.0 | ✅ nel catalogo |
| F23.3 | Processo NeMo avviato con `--diar-model` **e** `--diar-preset offline`: diarizziamo solo frasi concluse, quindi non si paga latenza e la classificazione è più stabile | ✅ verificato |
| F23.4 | La corsia finale invia `diarization=true` + `response_format=verbose_json` e raggruppa `words[].speaker` in turni | ✅ verificato sul server |
| F23.5 | **Corsie indipendenti**: un `RealtimeTranscriber` per sorgente; microfono = "Tu" (nessuna diarizzazione), audio di sistema = parlanti | ✅ |
| F23.6 | Id battuta per corsia (`(corsia, enunciato) → id`): microfono e sistema non si sovrascrivono più a vicenda | ✅ |
| F23.7 | Frase finale con più voci **spezzata in più battute**: la provvisoria diventa il primo turno, le altre si accodano | ✅ |
| F23.8 | Degradazione dichiarata: senza motore NeMo o su profilo Reattivo resta sempre una trascrizione, senza nomi | ✅ |
| F23.9 | Schema: `ALTER TABLE cues ADD COLUMN speaker INTEGER NOT NULL DEFAULT 0` guardato, gli archivi v1 continuano ad aprirsi | ✅ test dedicato |
| F23.10 | **Misure reali** (sessione #10, clip a 2 voci EN→IT, audio di sistema + microfono, diarizzazione attiva): parziali **73–600 ms**, finali **82–1560 ms**, stop **101 ms** | ✅ misurato |
| F23.11 | Test automatici: **78 verdi** (67 esistenti + 11 nuovi su parlanti, SRT, PDF, catalogo, migrazione DB) | ✅ |

### F24 — Barra fluttuante Apple-style (✅)

| # | Attività | Esito |
|---|---|---|
| F24.1 | `BarWindow`: `FluentWindow` senza bordi, **acrilico** (verificato: i pixel dentro la barra seguono lo sfondo), angoli arrotondati, fuori dalla barra delle applicazioni, sempre in primo piano | ✅ verificato a pixel |
| F24.2 | Trascinabile da tutta la superficie, si aggancia ai bordi entro 16 px, posizione ricordata | ✅ |
| F24.3 | Viewport **2 frasi** (regolabile 1–3) e buffer **5** (3–8): le frasi sotto si raggiungono con la rotellina | ✅ |
| F24.4 | Riga = pallino colorato + etichetta parlante + originale attenuato + traduzione | ✅ |
| F24.5 | Animazioni: comparsa slide+fade 220 ms cubic-out, alone che respira in ascolto, controlli all'hover in 160 ms, opzione per ridurle | ✅ |
| F24.6 | **Modalità discreta** via `WM_NCHITTEST`: i clic passano attraverso la finestra tranne che sulla maniglia | ✅ |
| F24.7 | Un errore di XAML non sparisce più nel nulla (l'icona inesistente `PanelRightExpand24` aveva reso la barra invisibile senza messaggi) | ✅ corretto |
| F24.8 | Barra come superficie d'avvio predefinita; il pannello resta e si apre dal menu | ✅ |

### F25 — Parlanti in sessioni, IA ed export (✅)

| # | Attività | Esito |
|---|---|---|
| F25.1 | Etichetta del parlante nel pannello, nella barra e nel dettaglio sessione, con colore stabile per voce | ✅ |
| F25.2 | Export: **SRT** con `<v Speaker 1>` / `<v Tu>`, **TXT** con `Speaker 1: …`, **JSON** con `speaker` e `speakerName` | ✅ |
| F25.3 | Nuova sezione PDF **Partecipanti rilevati** (battute, tempo di parola, quota) calcolata **in locale**, aggiunta ai template *Verbale di riunione* e *Intervista* | ✅ |
| F25.4 | I prompt IA ricevono la trascrizione con i nomi dei parlanti | ✅ |
| F25.5 | Nuove schede **Conversazione** e **Barra**; gli indici sono centralizzati in `SettingsTabs` così le scorciatoie `--settings=n` restano valide | ✅ |

### F26 — Test, documentazione e release v2.0.0 (✅)

| # | Attività | Esito |
|---|---|---|
| F26.1 | Test automatici: **78 verdi** | ✅ |
| F26.2 | `docs/CONVERSAZIONE.md`, README illustrato con screenshot reali, `THIRD-PARTY-NOTICES.md` con le licenze dei diarizzatori | ✅ |
| F26.3 | Installer **2.0.0** (89,8 MB) e installazione silenziosa: codice 0, versione del binario 2.0.0.0, avvio+stop in **76 ms** senza errori | ✅ verificato |
| F26.4 | Push su `main` (`13b659f`), tag `v2.0.0`, release con installer e SHA-256 `1B789866978A54FDDBB5DDC1937EDF72EBFD00B985B95D0FB6A8528C6A262E0E` | ✅ [release v2.0.0](https://github.com/renegadeFree/wisper-translator/releases/tag/v2.0.0) |

**Comandi aggiunti per le verifiche:**

| Comando | Cosa fa |
|---|---|
| `wisper diarize <file> [--lang en] [--diarizer <id>]` | prova il percorso completo della modalità conversazione (server + engine + turni) |
| `wisper tts --voices "voce1,voce2" --lines 4` | genera una clip con più voci alternate, per provare la diarizzazione |
| `app.exe --autostart --autostop <secondi>` | collaudo automatico di avvio e fermata (registra il tempo di stop in `logs/autostop.txt`) |

**Decisioni della v2.0:**

| # | Decisione | Perché |
|---|---|---|
| D-24 | Diarizzatore predefinito **Nemotron-3** (8 parlanti, OpenMDW-1.1) invece di Sortformer (4, CC-BY-4.0), che resta scaricabile | Copre le call affollate, pesa 40 MB in meno ed è utilizzabile commercialmente |
| D-25 | Etichette **anonime** `Speaker 1…8` e "Tu" per il microfono; nessun profilo vocale persistente (scelta dell'utente) | Nessun modello in più, nessun dato biometrico conservato |
| D-26 | `--diar-preset offline` sul processo NeMo | Le frasi arrivano già concluse: si guadagna precisione senza pagare latenza |
| D-27 | La barra è la superficie d'avvio predefinita, il pannello resta completo | È il caso d'uso quotidiano: sottotitoli sempre visibili e spostabili |
| D-28 | Con microfono e audio di sistema accesi, la stessa frase può comparire due volte ("Tu" e "Speaker N") | È la conseguenza voluta di due corsie separate; con le cuffie il microfono non risente degli altoparlanti |
| D-29 | Voci sintetiche dello stesso sesso sono un caso difficile per il diarizzatore: nei test le ha in parte unite | Limite del modello dichiarato: la verifica vera è una call reale |

**Limiti dichiarati (misurati, non stimati):** la diarizzazione aggiunge ~150 MB di RAM e circa
0,1–0,3 s per frase definitiva; con voci sovrapposte o timbri simili le etichette possono scambiarsi;
il diarizzatore riconosce al massimo 8 parlanti.

- **Loopback**: cattura dell'audio che il PC sta riproducendo, senza cavi né "Stereo Mix".
- **VAD**: rilevatore di attività vocale; separa parlato e silenzio.
- **Enunciato**: porzione di audio compresa tra due silenzi, inviata all'ASR.
- **Parziale**: trascrizione provvisoria di una frase ancora in corso.
- **Committato**: testo congelato, non verrà più riscritto.
- **RTF** (Real Time Factor): tempo di calcolo diviso durata audio; < 1 significa più veloce del tempo reale.
- **Tier**: fascia hardware rilevata che determina i modelli usati.
- **Backdrop Mica/Acrylic**: materiali di sfondo nativi di Windows 11.
