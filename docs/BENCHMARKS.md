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
