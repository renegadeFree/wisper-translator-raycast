# Wisper Translator

Trascrizione e traduzione **in tempo reale** e **in locale** dell'audio del PC e del microfono, con
sottotitoli in una finestra flottante su Windows. Italiano ↔ inglese.

## Review e nuova barra fluttuante — 3 ottobre 2026

Corretti i problemi di audio disattivato, stop dei decoder, persistenza delle traduzioni e retention;
ridotte copie e allocazioni nei percorsi audio e aggiornamenti UI. Ricostruito il modulo Models
mancante nella copia del progetto. La barra ora usa un layout compatto in stile Raycast, senza
cornice standard, con Acrylic nativo e fallback opaco.

[Rapporto completo, misure e limiti del collaudo](docs/CODE_REVIEW.md) ·
[Anteprima interattiva del nuovo design](docs/bar-preview.html) ·
[Compilazione Windows e build portabile](docs/BUILD_WINDOWS.md)

La nuova barra è implementata in WPF. L'anteprima HTML serve a ispezionare il design anche su
questo computer; la resa DWM e la cattura audio richiedono il collaudo Windows.
Gli screenshot nativi seguenti mostrano **la versione precedente**.

![Barra fluttuante con i parlanti](docs/screenshots/barra-ascolto.png)

![Barra a riposo](docs/screenshots/barra-riposo.png)

![Pannello esteso](docs/screenshots/pannello.png)

![Scheda Barra delle impostazioni](docs/screenshots/impostazioni-barra.png)

> Le immagini sono generate dal programma stesso (`WisperTranslator.App.exe --shot`) su frasi
> d'esempio: non contengono lo schermo del PC né dati di nessuno.

## Novità della versione 2.2

- **Barra rifatta da zero**: non è più un ovale disegnato dentro il rettangolo acrilico di Windows.
  La finestra viene ritagliata ad angoli arrotondati reali (`SetWindowRgn`), quindi è **un unico
  pannello di vetro** con bordo chiaro, nessuna cornice classica e nessun secondo rettangolo sotto.
- **Su richiesta**: la barra resta nascosta all'avvio e compare con il pulsante **Barra** nel
  pannello, dalla tray o con `Ctrl+Alt+B`; resta finché non la nascondi. 2 frasi visibili, 5 in
  memoria, le altre con la rotellina.
- **Testo più leggibile**: originale su una riga e traduzione fino a due righe, senza troncamenti
  prematuri; badge del parlante colorato, pulsante corallo sempre visibile, 4 comandi rapidi e menu
  `⋯` per gli altri. Niente titolo né grafici: il livello audio è un punto di stato che pulsa.
- **Conversazione più reale**: il testo immediato passa dal **WebSocket NeMo** (parziali mentre il
  server decodifica, non a richieste ripetute), il testo definitivo non aspetta più la
  diarizzazione, e un **tracker a finestra scorrevole** mantiene le stesse etichette di parlante tra
  le battute. Sortformer 4 parlanti è il diarizzatore predefinito; Nemotron resta per le riunioni
  fino a 8 voci.
- **Numeri misurati**: su 3 voci sintetiche distinte, 9 turni con sequenza stabile
  `1,2,3,1,2,3,1,2`, finali a **0,43 s** di mediana e **0,76 s** nel caso peggiore, accuratezza ASR
  **90,6%**. Dettagli in [docs/BENCHMARKS.md](docs/BENCHMARKS.md).
- **Screenshot senza desktop**: il comando `--shot` disegna solo le finestre dell'app su uno sfondo
  neutro, quindi nelle immagini del README non finisce niente dello schermo.

## Novità della versione 2.0

- **Modalità conversazione**: nelle riunioni e nelle call il microfono resta **Tu** e l'audio di
  sistema viene diviso per parlante (`Speaker 1…8`, ognuno con il suo colore). Se in una frase si
  sentono due voci, la frase viene spezzata in due battute invece di mescolarle.
  Dettagli in [docs/CONVERSAZIONE.md](docs/CONVERSAZIONE.md).
- **Solo trascrizione o traduzione**: l'interruttore **Traduci** (pannello e barra) spegne la
  traduzione quando serve solo il testo.
- **Barra fluttuante in stile Apple** (`Wispr Flow`): acrilico di Windows 11, angoli arrotondati,
  trascinabile ovunque, si aggancia ai bordi, **2 frasi visibili** su **5 in memoria** (regolabili)
  con la rotellina per scorrere quelle sotto, comparsa con slide+fade, alone "in ascolto",
  controlli al passaggio del mouse e **modalità discreta** che lascia passare i clic.
- **Parlanti in tutto il programma**: etichetta nella barra e nel pannello, dettaglio sessione,
  esportazione **SRT** con `<v Speaker 1>`, JSON con il campo `speaker`, e nuova sezione PDF
  **Partecipanti rilevati** con battute, tempo di parola e quota di ciascuno.
- **Nuovi modelli in catalogo**: **Sortformer 4 parlanti v2** (147 MB, CC-BY-4.0) come predefinito
  per le call normali e **Nemotron 3 Diarization** (107 MB, fino a 8 parlanti, OpenMDW-1.1) per le
  riunioni affollate.
- **Nuove schede nelle impostazioni**: **Conversazione** (diarizzatore, download, verifica, limiti
  dichiarati) e **Barra** (frasi visibili, frasi in memoria, testo mostrato, discreta, animazioni).

## Novità della versione 1.2

- **Impostazioni riorganizzate**: nuova scheda **Prestazioni** (profilo automatico o forzato, motore
  per corsia, stato dei componenti) e **valore sempre visibile** su ogni slider.
- **Motori selezionabili per fascia**: Vosk (leggerissimo, ~50 MB) sui PC minimi, **Nemotron 3.5
  streaming** (~708 MB) su quelli capaci, Whisper come rete di sicurezza.
- **14 template di mappe concettuali** (radiale, gerarchica, albero, linea del tempo, SWOT, spina di
  pesce, note colorate…) con orientamento, palette e densità regolabili: il modello scrive la
  struttura, **Graphviz** la disegna in locale (download di 9 MB al primo uso).
- **8 template di report PDF** (verbale di riunione, appunti di lezione, intervista, report tecnico,
  executive summary, trascrizione fedele, brainstorming, post-mortem) con copertina, indice, numeri
  di pagina e **mappa vettoriale** dentro il PDF.
- **Template importabili**: una cartella o uno zip con `template.json` oppure un `SKILL.md`
  (front-matter + istruzioni) diventa un template dell'app. I template sono file modificabili in
  `%LOCALAPPDATA%\WisperTranslator\templates`.
- **Storico a prova di errore**: un database danneggiato viene messo da parte e ricreato da solo
  (`wisper history check` per la diagnostica).

## Cosa fa

- Cattura l'**audio di sistema** (loopback WASAPI) e/o il **microfono**, con accensione indipendente.
- Trascrive in locale con Whisper (`whisper.cpp`): nessun audio lascia il PC.
- Traduce in locale con i modelli Firefox/Bergamot: circa **25 ms per frase**, nessuna chiave API.
- Mostra le battute in una **barra di vetro fluttuante** su richiesta (nuova in alto, le vecchie
  scendono, le frasi oltre quelle visibili si raggiungono con la rotellina), nel **pannello esteso**
  e in un **overlay a schermo intero**, trasparente e cliccabile-attraverso.
- Nella **modalità conversazione** distingue chi parla: *Tu* per il microfono, `Speaker 1…4` con
  Sortformer (fino a 8 con Nemotron) per l'audio di sistema, anche in esportazione e nei report.
- Salva lo storico delle sessioni con **retention configurabile** (0 = per sempre) ed esporta in
  **SRT**, testo o JSON.
- Gestisce i modelli: download con verifica SHA-256, ripresa, verifica integrità, import locale,
  eliminazione.

## Requisiti

- Windows 10/11 a 64 bit.
- 4 GB di RAM liberi (i modelli piccoli girano anche su CPU senza GPU dedicata).
- ~700 MB di disco per il programma; ~250 MB per i modelli piccoli, ~1,1 GB se usi Nemotron +
  Sortformer.
- Connessione a internet **solo** per il primo download dei modelli.

## Installazione

1. Scarica `WisperTranslator-Setup-x.y.z.exe` dalle release.
2. Esegui l'installer (non richiede diritti di amministratore).
3. Al primo avvio premi **Avvia**: i modelli mancanti vengono scaricati automaticamente in base
   all'hardware rilevato.

> **Avviso di Windows:** l'installer non è firmato digitalmente, quindi SmartScreen può mostrare
> "Windows ha protetto il PC". Per proseguire: *Ulteriori informazioni* → *Esegui comunque*.
> Il file è verificabile con l'hash SHA-256 pubblicato nella release.

## Uso

| Comando | Effetto |
|---|---|
| **Sistema** | attiva/disattiva l'audio riprodotto dal PC |
| **Microfono** | attiva/disattiva l'ingresso del microfono |
| **Conversazione** | separa le voci e dà un nome ai parlanti della call |
| **Traduci** | spento: trascrive soltanto, senza tradurre |
| **IT → EN / EN → IT** | direzione della traduzione (deve corrispondere alla lingua parlata) |
| **Avvia / Ferma** | apre le sorgenti audio e comincia l'ascolto |
| **Barra** | mostra o nasconde il pannello di vetro (anche `Ctrl+Alt+B`) |
| **Overlay** | mostra i sottotitoli a tutto schermo |
| **Impostazioni** | aspetto, modelli, dispositivi e storico |

### Scorciatoie da tastiera

| Scorciatoia | Effetto |
|---|---|
| `Ctrl+Alt+W` | mostra/nascondi il widget |
| `Ctrl+Alt+S` | audio di sistema on/off |
| `Ctrl+Alt+N` | microfono on/off |
| `Ctrl+Alt+L` | inverti la direzione |
| `Ctrl+Alt+O` | overlay on/off |
| `Ctrl+Alt+B` | mostra/nascondi la barra di vetro |

Nella **barra di vetro** il pulsante **corallo a destra** avvia e ferma ed è sempre visibile insieme
ai comandi rapidi (sistema, microfono, traduci, conversazione) e al
menu `⋯` con direzione, testo mostrato, modalità discreta, overlay, pannello, impostazioni e
chiusura. Il testo si trascina da qualsiasi punto della barra, che si allarga e si restringe
**trascinando i bordi laterali**.

### Dove stanno i dati

Tutto in `%LOCALAPPDATA%\WisperTranslator`:

| Percorso | Contenuto |
|---|---|
| `models\` | modelli scaricati |
| `history.db` | storico delle sessioni (SQLite) |
| `exports\` | esportazioni da riga di comando |
| `settings.json` | preferenze |
| `audio-dump\` | registrazioni di prova, se richieste |

## Privacy

Con i motori locali (impostazione predefinita) audio e testo **non escono dal computer**: la traduzione
parla con un server locale (`127.0.0.1`) incluso nel programma. Solo il download dei modelli e
l'eventuale motore cloud opzionale usano la rete, e il motore cloud è spento di default.

## Risoluzione dei problemi

| Sintomo | Causa probabile | Rimedio |
|---|---|---|
| Nessun testo | sorgente sbagliata | controlla il pulsante **Sistema**/**Microfono** e la direzione |
| "Hotkey non disponibili" | un altro programma usa la stessa combinazione | cambia programma o ignora: il resto funziona |
| Il microfono risente delle casse | il microfono capta l'audio degli altoparlanti | usa le cuffie o disattiva una delle due sorgenti |
| Due righe identiche in conversazione | microfono e audio di sistema hanno sentito la stessa voce | usa le cuffie, oppure spegni **Microfono** |
| Nessun nome accanto alle battute | diarizzatore non installato o motore definitivo non NeMo | `Impostazioni → Conversazione → Scarica il diarizzatore` |
| La barra non compare | l'hai chiusa con la X (resta attiva) | `Visuale → Barra fluttuante`, oppure dall'icona nella barra delle applicazioni |
| La barra è troppo stretta o troppo larga | la larghezza predefinita è 760 DIP | trascina i bordi laterali oppure usa `Impostazioni → Barra → Larghezza barra` |
| Una frase lunga viene tagliata | la riga mostra al massimo due righe | allarga la barra: il testo va a capo finché ci sta |
| Download interrotto | rete instabile | rilanciare: riprende da dove era rimasto |
| Modello "hash diverso" | file scaricato male | `Impostazioni → Modelli → Elimina`, poi `Scarica` |

## Compilare da sorgente

Servono **Windows x64 e l'SDK .NET 8**. La guida [BUILD_WINDOWS.md](docs/BUILD_WINDOWS.md)
descrive prerequisiti, clone, test, ZIP portabile, installer e risoluzione degli errori.
La [workflow Windows build](.github/workflows/windows-build.yml) genera automaticamente la build.

```powershell
dotnet build WisperTranslator.sln -c Release
dotnet test WisperTranslator.sln -c Release     # include i test PDF Windows
dotnet run --project src/WisperTranslator.Cli -- hardware
dotnet run --project src/WisperTranslator.Cli -- capture --seconds 15 --dump
dotnet run --project src/WisperTranslator.Cli -- live --seconds 20 --model base --lang it
dotnet run --project src/WisperTranslator.Cli -- translate --bench
dotnet run --project src/WisperTranslator.Cli -- models list
dotnet run --project src/WisperTranslator.Cli -- history list
dotnet run --project src/WisperTranslator.Cli -- diarize clip.wav --lang en
dotnet run --project src/WisperTranslator.Cli -- live --engine nemo --final-engine nemo --diarize --source system --play-clip clip.wav --seconds 60 --lang en
```

Per rigenerare le immagini del README (solo finestre dell'app, su sfondo neutro, con frasi
d'esempio):

```powershell
dotnet run --project src/WisperTranslator.App -- --shot=docs/screenshots
```

Per creare l'installer:

```powershell
powershell -File installer/build.ps1
```

Il piano di sviluppo completo, le decisioni tecniche e i risultati misurati sono in
[PLAN.md](PLAN.md); i numeri di riferimento in [docs/BENCHMARKS.md](docs/BENCHMARKS.md).

## Licenza

MIT — vedi [LICENSE](LICENSE). I componenti di terze parti sono elencati in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
