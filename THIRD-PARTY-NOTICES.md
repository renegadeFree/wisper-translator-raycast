# Componenti di terze parti

Wisper Translator è distribuito con licenza MIT. Include o scarica i componenti elencati qui sotto;
per ognuno è indicata la licenza e l'obbligo che ne deriva.

## Inclusi nell'installer

| Componente | Versione | Licenza | Note |
|---|---|---|---|
| .NET Runtime (self-contained) | 8.0 | MIT | runtime incluso nell'installer |
| WPF-UI | 4.3.0 | MIT | controlli e backdrop Fluent |
| NAudio | 2.2.1 | MIT | cattura audio WASAPI |
| Whisper.net | 1.9.1 | MIT | binding .NET a whisper.cpp |
| whisper.cpp (runtime nativo) | 1.9.1 | MIT | inferenza Whisper |
| Microsoft.ML.OnnxRuntime | 1.30.0 | MIT | esecuzione dei modelli ONNX |
| Microsoft.Data.Sqlite | 10.0.12 | MIT | storico locale |
| SQLite | 3.x | Public Domain | motore del database |
| System.Management | 10.0.12 | MIT | rilevamento hardware |
| System.Speech | 8.0.0 | MIT | generazione delle clip di prova |
| MTranServer | 4.0.33 | **Apache-2.0** | server di traduzione locale; il testo della licenza è in `licenses/MTranServer-LICENSE.txt` |

## Scaricati dall'applicazione (non inclusi nell'installer)

| Componente | Licenza | Note |
|---|---|---|
| Modelli Whisper ggml (`whisper.cpp`) | MIT | scaricati da Hugging Face al primo avvio |
| Silero VAD v5 (ONNX) | MIT | rilevamento attività vocale |
| Modelli Firefox Translations / Bergamot IT↔EN | MPL-2.0 | scaricati da MTranServer |

## Nota sul marchio

Whisper è un modello di OpenAI distribuito con licenza MIT. Questo progetto non è affiliato a OpenAI,
a Mozilla, a Meta o a NVIDIA.
