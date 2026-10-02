# Catalogo modelli

Modelli gestiti dall'applicazione: ruolo, tier hardware, dimensione, licenza e origine.
Il file viene aggiornato man mano che i modelli entrano nel catalogo (`ModelCatalog`).

## Ruoli

- **ASR**: trascrizione audio → testo
- **MT**: traduzione testo → testo
- **VAD**: rilevamento attività vocale

## Modelli previsti

| Ruolo | ID | Tier | Dimensione | Licenza | Origine |
|---|---|---|---|---|---|
| VAD | `silero-vad-v5` | tutti | ~2 MB | MIT | `snakers4/silero-vad` (ONNX) |
| ASR | `whisper-base-q5_1` | C | ~80 MB | MIT | whisper.cpp ggml |
| ASR | `whisper-small-q5_1` | B | ~190 MB | MIT | whisper.cpp ggml |
| ASR | `whisper-large-v3-turbo-q5_0` | A | ~550 MB | MIT | whisper.cpp ggml |
| MT | `opus-mt-it-en` | B/C | ~80 MB (int8) | Apache-2.0 | Helsinki-NLP |
| MT | `opus-mt-en-it` | B/C | ~80 MB (int8) | Apache-2.0 | Helsinki-NLP |
| MT | `bergamot-iten` / `bergamot-enit` | C | ~30 MB | MPL-2.0 | mozilla/firefox-translations-models |

> Gli hash SHA-256 definitivi vengono inseriti nel catalogo in F6.1, dopo il primo download verificato.
