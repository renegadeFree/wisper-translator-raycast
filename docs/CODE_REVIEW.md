# Code review, prestazioni e barra fluttuante

Review del 3 ottobre 2026 sulla copia fornita di Wisper Translator. Il target rimane **Windows x64 / .NET 8 / WPF**. Sono stati analizzati pipeline audio, VAD, ASR, diarizzazione, traduzione, storico, impostazioni, template, rendering, applicazione, CLI, test e distribuzione. Le modifiche privilegiano componenti già presenti e API native; nessuna nuova dipendenza di produzione.

## Esito

La copia iniziale non compilava: mancava completamente `src/WisperTranslator.Core/Models`. Il modulo è stato ricostruito dai suoi chiamanti, dai test esistenti e dalle fonti ufficiali dei modelli. Il pattern non ancorato `models/` del `.gitignore` poteva escludere anche questa cartella sorgente su un filesystem Windows: ora è `/models/`.

La soluzione completa compila con **0 errori e 0 avvisi**. La pubblicazione autonoma `win-x64` è stata generata. I **98 test managed eseguibili sul Mac passano**; la successiva CI Windows ha superato **tutti i 101 test**, inclusi i tre PDF con font Windows, e l'autotest nativo della regione HWND arrotondata della barra. Restano i controlli su WASAPI, hotkey, decoder nativi con modelli reali e resa DWM su hardware Windows. Questo rapporto non equivale a una certificazione del comportamento su ogni PC.

## Problemi rilevati e corretti

P1 indica un problema che può impedire l'uso, perdere dati o provocare crash; P2 un problema di correttezza, reattività o robustezza.

| Priorità | Problema e conseguenza | Correzione e area |
|---|---|---|
| P1 | Modulo Models assente: download, catalogo e avvio non compilavano. | `Models/`: catalogo con versioni/revisioni fissate, SHA-256, ripresa HTTP, import verificato, estrazione protetta e manifest dei pacchetti. |
| P1 | Il calcolo RMS indicizzava lo scratch buffer con il numero totale di campioni prodotti; blocchi grandi potevano superare gli 8192 elementi. | `VoiceCapture`: energia calcolata per blocco mentre i campioni sono validi; frazione di ricampionamento conservata fra callback. |
| P1 | Whisper poteva liberare la memoria nativa dopo un'attesa limitata anche se una decodifica era ancora in corso. | `WhisperAsrEngine`, `RealtimeTranscriber`, `TranscriptionSession`: fine effettiva dei worker distinta dal timeout UI; rilascio del modello differito fino alla fine della decodifica. |
| P1 | Traduzioni definitive e correzioni tardive del parlante arrivavano alla UI ma non erano aggiornate nello storico. | `TranscriptionSession`: persistenza della versione corrente del cue anche dopo traduzione e attribuzione del parlante. |
| P1 | La retention della sessione non seguiva le preferenze; `0` non proteggeva sempre i dati e le note rimanevano orfane dopo la cancellazione delle sessioni. | `SessionOptions`, `AppSettings`, `SessionStore`: retention propagata, `0` conserva tutto, cancellazione coerente delle note. |
| P1 | ID importati nei template potevano diventare percorsi di scrittura/cancellazione fuori dalla loro cartella. | `TemplateStore`: ID limitati a lettere, cifre, trattino e underscore prima dell'accesso al filesystem; cleanup delle estrazioni fallite. |
| P2 | Le sorgenti spente non venivano drenate e potevano riprodurre audio vecchio alla riattivazione; le corsie separate potevano continuare a trascriverle. | `AudioMixer`, `VoiceCapture`: consumo regolare dei buffer e campioni silenziosi per la sorgente disattivata. |
| P2 | Parziali ASR tardivi potevano sostituire frasi già definitive; i conteggi aumentavano anche alle successive correzioni della stessa frase. | Guardie in transcriber e sessione; conteggio solo alla prima transizione definitiva. |
| P2 | Turni dei parlanti con tempi relativi alla frase venivano salvati come tempi assoluti della sessione. | Aggiunta di `utteranceStart` nei percorsi di split e diarizzazione differita. |
| P2 | Cancellazione, sostituzione della lingua e nuove richieste potevano far pubblicare traduzioni obsolete o liberare CTS/servizi ancora usati. | Versioni assegnate sotto lock, cattura della coppia linguistica, controllo prima della pubblicazione, task e CTS con durata coerente. |
| P2 | Uno shutdown NeMo poteva rilasciare un semaforo mai acquisito. | `NeMoSpeechHost`: controllo dell'esito di `Wait` prima di `Release`. |
| P2 | Streaming fallito lasciava socket/client vivi, frame senza limite e attese di chiusura troppo lunghe. | `NeMoRealtimeClient`, transcriber: cleanup del client fallito, limite frame 1 MiB, chiusura limitata e fallback anche su chiusura del server. |
| P2 | Dispose di Vosk poteva sovrapporsi alla decodifica sullo stesso modello. | `VoskAsrEngine`: serializzazione del modello e controllo dello stato di dispose. |
| P2 | Impostazioni non valide potevano causare selezioni inesistenti, dimensioni non finite o layout rotto. | `AppSettings.NormalizeValues`: normalizzazione a ogni caricamento, limiti numerici e valori validi per modello/testo; salvataggio tramite file temporaneo. |
| P2 | L'esito negativo della verifica di un modello veniva immediatamente sostituito dal messaggio “Operazione completata”. Il fallimento del download Bergamot veniva ignorato. | `SettingsWindow`: risultato esplicito dell'operazione e controllo del codice di uscita del downloader. |
| P2 | Cancellare Graphviz o il download Bergamot poteva lasciare il processo figlio attivo. | Terminazione del processo alla cancellazione; timeout Graphviz di 30 s; argomenti MTranServer tramite `ArgumentList`. |
| P2 | Il package Vosk selezionava le librerie native in base all'OS di compilazione, ignorando `win-x64`. | Build automatica del package esclusa e quattro DLL Windows x64 incluse esplicitamente; verificati header PE e presenza nel publish. |

## Ottimizzazioni applicate

### Audio, VAD e diarizzazione

- La finestra audio del tracker usa `FloatRingBuffer` da 45 secondi: scrive solo i nuovi campioni e copia la finestra soltanto quando avvia la diarizzazione. Prima allocava un array e spostava fino a 720.000 float a ogni blocco da 512 campioni.
- `AudioBlock` espone memoria valida durante il callback: sessione e CLI la consumano direttamente tramite span. Eliminata una copia per ogni blocco da 32 ms.
- Il pre-roll del segmenter ricicla i frame della coda limitata durante il silenzio. La posizione iniziale corrisponde adesso ai campioni effettivamente conservati.
- Silero riusa tensori e oggetti di input; lo stato ONNX viene copiato direttamente nel buffer. Una inferenza così piccola usa un thread intra-op e uno inter-op anziché generare contesa fra pool.
- La selezione del segmento più vicino non ordina tutti i segmenti per ogni parola. Le associazioni dei parlanti non riusano la stessa identità globale per due voci locali nella stessa finestra.
- Un errore di diarizzazione rispetta l'intervallo fra tentativi: non causa una richiesta a ogni frame.
- I device audio vengono enumerati una sola volta per l'apertura della sorgente.

### ASR e traduzione

- I limiti di durata dei parziali vengono verificati prima di copiare l'enunciato. Nessuna copia se il worker è occupato o il frammento è troppo lungo.
- I worker attendono il segnale con cancellazione invece di svegliarsi ogni 50 ms senza lavoro.
- NeMo mantiene un `HttpClient` per motore. I WAV non allocano piccoli array per ogni identificatore RIFF.
- La cache di traduzione espelle una voce alla volta con limite FIFO; prima svuotava tutta la cache. Chiavi strutturate eliminano collisioni quando il testo contiene `|`.
- La regex di pulizia della traduzione è generata in compilazione.
- Gli hash dei modelli a file singolo già verificati sono riutilizzati finché dimensione e timestamp non cambiano. La verifica esplicita calcola comunque l'hash completo.

### UI, storico e contenuti

- Gli aggiornamenti dei cue sono raggruppati ogni 66 ms; il thread audio non attende più `Dispatcher.Invoke` per ogni aggiornamento.
- La barra aggiorna gli oggetti delle righe esistenti solo quando il contenuto cambia e riusa i brush della palette. Gli overlay nascosti non vengono ricostruiti per ogni cue.
- La regione arrotondata dell'HWND viene ricreata solo se cambiano dimensioni o DPI. Lo snap viene applicato alla fine del trascinamento.
- Il timer del punto di stato si ferma quando la barra è nascosta, la sessione è ferma o le animazioni sono disabilitate.
- SQLite riusa un comando parametrizzato preparato per i cue. Connessioni e dispose sono protetti dallo stesso lock; i percorsi usano `SqliteConnectionStringBuilder`.
- Il contesto dell'assistente viene costruito fino al limite utile; ID dei nodi e riferimenti della mappa sono validati con un set, senza ricerche ripetute per ogni arco.
- Download in streaming, progress limitato a circa 10 aggiornamenti al secondo, timeout di inattività di 45 secondi e sostituzione solo dopo verifica. Import e download sono serializzati per destinazione; cancellazione dei modelli in uso da un'operazione viene respinta.

### Misura riproducibile del buffer

Su macOS ARM64, .NET 8.0.31, Release. Finestra già piena di 45 s, successivi 60 s a 16 kHz in blocchi da 512 campioni; 3 warmup e mediana di 7 passaggi. Il benchmark usa il buffer corrente del progetto e replica la precedente manutenzione della `List`, escludendo l'allocazione iniziale e verificando l'equivalenza della finestra finale.

| Manutenzione del buffer | Tempo | Allocazione durante l'inserimento |
|---|---:|---:|
| List precedente | 73,929 ms | 3.885.000 byte |
| Ring attuale | 0,164 ms | 0 byte |

Il rapporto di circa 452× riguarda **solo questa operazione**. Non misura latenza ASR, carico ONNX, GUI o cattura WASAPI e non va esteso alle prestazioni dell'applicazione intera. Gli snapshot per la diarizzazione continuano ad allocare, ma con frequenza molto più bassa. Le vecchie misure in `BENCHMARKS.md` non sono state ripetute e rimangono risultati storici, non risultati di questa review.

```powershell
dotnet run --project docs/benchmarks/BufferBenchmark.csproj -c Release
```

## Nuovo design della barra

Implementazione reale in `BarWindow.xaml`, code-behind, `Win32` e `BarGeometry`:

- Pannello scuro in stile Raycast, contorno sottile, angoli da 18 DIP, larghezza iniziale 760 DIP, due righe da 76 DIP e footer compatto. Senza cue scende a 132 DIP di altezza.
- Nessuna title bar, cornice standard, voce taskbar o Alt+Tab. Il template della finestra contiene soltanto il pannello; il codice ripristina `WindowStyle.None` dopo l'inizializzazione di FluentWindow.
- Desktop Acrylic tramite DWM da Windows 11 build 22621. Sulle versioni precedenti compatibili viene tentato il fallback `WCA_ACCENT_POLICY`; in caso di insuccesso il fondo resta opaco e leggibile. I colori di contrasto elevato e la preferenza di trasparenza vengono rispettati.
- Il tema globale non sostituisce il materiale della barra con Mica; il bordo OS viene nuovamente soppresso anche dopo attivazione/disattivazione.
- Comandi audio, microfono, traduzione, parlanti e Avvia/Ferma sempre visibili, con nomi di automazione, tooltips e focus da tastiera. Il menu `⋯` si apre anche con clic sinistro.
- Originale secondario e traduzione principale, etichette dei parlanti e testo provvisorio completo. Animazioni brevi, nessun lampeggio periodico delle righe.
- Ridimensionamento e snap usano l'area di lavoro del monitor corrente in pixel device, evitando il precedente vincolo al monitor principale.

[Anteprima interattiva](bar-preview.html): rappresentazione HTML del layout, verificata a 480 e 760 px, a riposo, con testo originale e menu. È un'anteprima del design, **non uno screenshot WPF eseguito su Windows**. Le vecchie immagini native nella documentazione rappresentano la versione precedente.

Il significato di Desktop Acrylic e il requisito di build sono descritti nella [documentazione DWM Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type). Il fallback Windows 10 usa un attributo legacy non documentato: Microsoft [sconsiglia SetWindowCompositionAttribute](https://learn.microsoft.com/en-us/windows/win32/dwm/setwindowcompositionattribute) rispetto alle API DWM moderne. Il fallback è isolato e il suo fallimento non impedisce l'uso della barra.

## Verifiche e riproduzione

```powershell
dotnet build WisperTranslator.sln
dotnet test WisperTranslator.sln
dotnet publish src/WisperTranslator.App -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -o publish/app
dotnet list WisperTranslator.sln package --vulnerable --include-transitive
```

Il computer disponibile è un Mac. Per compilare è stato usato `-p:EnableWindowsTargeting=true`. Per eseguire soltanto i test managed, è stato rimosso il riferimento `Microsoft.WindowsDesktop.App` dal **runtimeconfig generato dei test**, quindi usato `dotnet test --no-build --filter 'Category!=Windows'`. I target di progetto rimangono Windows. Il runtimeconfig viene rigenerato dalla normale build; su Windows vanno eseguiti tutti i test senza questa modifica e senza filtro.

I test di regressione coprono audio spento, snapshot circolare, cache, retention, note, persistenza della traduzione, parziali tardivi, tempi assoluti dei turni, percorsi dei template, mappe, preferenze, catalogo, import errato che preserva il modello esistente e rename di un download già completo. Il test di stop controlla anche che il timeout non sia confuso con la fine effettiva del decoder.

La build portabile è in `publish/app`; il relativo ZIP contiene anche il runtime .NET e le librerie Windows x64. Non include i grandi modelli, scaricati al primo utilizzo, né un installer Inno Setup firmato. Il server di traduzione viene scaricato dall'app quando necessario. La build della release è generata dalla CI Windows e avviata con `--bar-selftest`; questo controllo non carica modelli né cattura audio.

La prima esecuzione Windows ha evidenziato un test di concorrenza dipendente da un'attesa fissa di 500 ms e da una sorgente sintetica senza pause. Il test ora alimenta due frasi separate e attende il parziale della seconda mentre il decoder finale della prima resta bloccato; il cleanup attende anche la fine effettiva dei worker. Il controllo mantiene l'asserzione funzionale senza dipendere dalla velocità del runner.

La procedura di compilazione, publish e installer è in [BUILD_WINDOWS.md](BUILD_WINDOWS.md).
Le esecuzioni sono consultabili nella [workflow Windows build](https://github.com/renegadeFree/wisper-translator-raycast/actions/workflows/windows-build.yml).

## Audit delle dipendenze

L'audit NuGet del 3 ottobre 2026 include dipendenze dirette e transitive dei quattro progetti:
**nessuna vulnerabilità nota segnalata** dopo le correzioni, senza errori di valutazione.
Il risultato è conservato in [dependency-audit.json](dependency-audit.json); non è una garanzia
sull'assenza di vulnerabilità non ancora catalogate.

Il grafo iniziale dei test includeva `System.Net.Http 4.3.0` e `System.Text.RegularExpressions 4.3.0`
tramite il vecchio xUnit/NETStandard.Library. xUnit è stato aggiornato da 2.5.3 a 2.9.3,
rimuovendo quelle dipendenze legacy. Le segnalazioni riguardavano il grafo dei test,
non dimostravano l'esecuzione di tali vecchie librerie nel runtime .NET 8 dell'app.
Riferimenti: [CVE-2018-8292](https://github.com/advisories/GHSA-7jgj-8wvc-jh57),
[CVE-2019-0820](https://github.com/advisories/GHSA-cmhx-cq75-c4mj).

## Limiti residui da verificare

| Priorità / area | Evidenza e limite pratico | Verifica successiva |
|---|---|---|
| P2, modalità discreta | `HTTRANSPARENT` ha limiti fra finestre di thread differenti. Non è una garanzia di click-through verso tutte le applicazioni; header e footer rimangono interattivi. | Collaudo con browser, call e app esterne su Windows; un eventuale click-through completo richiede gestione nativa dedicata compatibile con Acrylic. |
| P2, sessioni molto lunghe | UI e audio hanno buffer limitati, ma sessione e CLI mantengono metadati/testo dei cue per la durata della registrazione. | Misurare memoria su call di diverse ore prima di introdurre paginazione o pruning che potrebbe perdere correzioni tardive. |
| Materiale nativo | Acrylic dipende da versione Windows, compositore e impostazioni di sistema; l'attributo legacy non garantisce stabilità futura. | Windows 10/11, tema chiaro/scuro, trasparenza disattivata, contrasto elevato, desktop remoto. |
| Audio e accelerazione | Il Mac non può verificare loopback WASAPI, Vosk Windows, NeMo Windows, hotkey o CUDA/Vulkan. | Avvio, mute/riattivazione, stop durante decode, crash NeMo, cambio device, profili prestazioni su hardware Windows reale. |
| Posizione e DPI | Resize e snap seguono il monitor corrente; il ripristino iniziale delle coordinate conserva ancora il comportamento basato sul work area principale. | Monitor secondari, DPI misti 100/150/200%, monitor rimosso e coordinate negative. |
| Latenza totale | Nessuna nuova misura end-to-end di WER, latenza finale, CPU o GPU. | Ripetere le CLI benchmark esistenti sulla macchina Windows, incluse chiamate lunghe e rumore reale. |

I test PDF e `--bar-selftest` sono stati superati in CI Windows. La priorità del collaudo manuale resta: avvio reale con modelli, mute, stop e successivo riavvio, blur e DPI misti.
