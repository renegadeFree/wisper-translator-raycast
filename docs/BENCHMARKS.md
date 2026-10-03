# Benchmark

Misure reali, non stime. Ogni riga è prodotta da `WisperTranslator.Cli bench`.

RTF = tempo di calcolo / durata dell'audio (più basso è meglio, < 1 è più veloce del tempo reale).

> **Attenzione al contesto:** le misure qui sotto sono fatte sulla macchina di sviluppo
> (`RE_NEGADE_FREE`, Ryzen 9 7900X3D, 24 thread). Con 12 thread assegnati a whisper.cpp il RTF è
> ottimistico: su un i5/4 core di fascia bassa aspettarsi un RTF da 4 a 6 volte più alto.
> La misura sulla macchina debole è il criterio di accettazione di F8.5.
>
> L'accuratezza è calcolata confrontando la trascrizione con il testo pronunciato dalla voce SAPI
> (clip generate da `WisperTranslator.Cli tts`), a livello di parola dopo normalizzazione.

| Data | Macchina | Modello | Lingua | Durata audio | Tempo | RTF | RAM picco | Accuratezza |
|---|---|---|---|---|---|---|---|---|
| 2026-10-02 23:14 | RE_NEGADE_FREE (24 core) | whisper-base-q5_1 | it | 11.3 s | 0.53 s | 0.047 | 311 MB | 96,0% |
| 2026-10-02 23:14 | RE_NEGADE_FREE (24 core) | whisper-base-q5_1 | en | 10.3 s | 0.53 s | 0.051 | 294 MB | 100,0% |
| 2026-10-02 23:15 | RE_NEGADE_FREE (24 core) | whisper-small-q5_1 | it | 11.3 s | 1.65 s | 0.145 | 555 MB | 100,0% |
| 2026-10-02 23:54 | RE_NEGADE_FREE (24 core) | whisper-large-v3-turbo-q5_0 | it | 11.3 s | 6.63 s | 0.585 | 1002 MB | 100,0% |

## Pipeline real-time v2.2 — 3 e 4 voci sintetiche

Comando usato (loopback di sistema, traduzione spenta per isolare ASR e parlanti):

```powershell
WisperTranslator.Cli.exe tts --lang en --lines 9 --pause 1800 `
    --voices "Microsoft Zira Desktop,Microsoft Hazel Desktop,Microsoft Elsa Desktop" `
    --out %TEMP%\wisper-bench\conv3-en.wav
WisperTranslator.Cli.exe live --engine nemo --final-engine nemo --diarize `
    --diarizer diar-streaming-sortformer-4spk-v2 --source system `
    --play-clip %TEMP%\wisper-bench\conv3-en.wav --seconds 55 --lang en
```

| Data | Macchina | Prova | Audio | Primo testo | Finale mediana/peggiore | Accuratezza ASR | Parlanti |
|---|---|---|---|---|---|---|---|
| 2026-10-03 04:55 | RE_NEGADE_FREE (24 core) | 3 voci SAPI, 9 turni, pause 1,8 s | 52,9 s | 0,00 s (WebSocket, per parola) | 0,43 s / 0,76 s | 90,6% | sequenza stabile 1,2,3,1,2,3,1,2 |
| 2026-10-03 04:47 | RE_NEGADE_FREE (24 core) | 3 voci + variante di tono, 8 turni | 42,5 s | 0,00 s (WebSocket, per parola) | 0,50 s / 0,91 s | 96,5% | le 3 voci distinte restano separate; la variante di tono della stessa voce viene unita |

Note misurate, non stimate:

- la diarizzazione su file intero (52,9 s) costa **1,22 s** (RTF 0,023) con Sortformer;
- la vecchia corsia a richieste HTTP ripetute pagava una decodifica completa a ogni parziale;
  con il WebSocket i parziali arrivano mentre il server decodifica, senza coda;
- il test a 4 timbri reali non è possibile su questa macchina: Windows ha solo 3 voci SAPI
  installate (Zira, Hazel, Elsa), quindi il quarto parlante è una variante di tono della prima.

## Traduzione a due corsie — parziali istantanei e rifinitura

Motore rapido: MTranServer/Bergamot (misura su frase intera: **17 ms** a caldo, 426 ms la prima
richiesta perché carica il modello). Motore di rifinitura: Marian opus-mt int8 in ONNX Runtime.

| Data | Macchina | Prova | Prima traduzione dal primo parziale | Traduzioni pubblicate | Passaggi di qualità |
|---|---|---|---|---|---|
| 2026-10-03 18:52 | RE_NEGADE_FREE (24 core) | clip italiana → inglese, 6 enunciati, 35 s di audio di sistema | 0–178 ms | 116 su 135 righe di sessione | 10 |
| 2026-10-03 18:53 | RE_NEGADE_FREE (24 core) | corsia isolata con i motori reali, parziale ogni 200 ms | 342 ms (comprende il caricamento del modello ONNX) | una ogni ~200 ms | 2 |

| Misura isolata | Tempo |
|---|---|
| Bergamot su frase intera (a caldo) | 17 ms |
| Marian ONNX it→en, frase intera | 177 ms |
| Marian ONNX en→it, frase intera | 227 ms |

Prima di questa lavorazione la traduzione del parziale veniva **annullata** a ogni aggiornamento
con 300 ms di debounce: parlando di continuo non arrivava mai a destinazione e il testo tradotto
compariva solo alla pausa. Le misure sopra sono con la nuova politica "latest-wins senza
annullamento".
