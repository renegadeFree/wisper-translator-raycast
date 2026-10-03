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
| **Vosk** (runtime + modelli small it/en) | Apache-2.0 | pacchetto NuGet `Vosk` incluso; i modelli (34-50 MB) si scaricano dal sito Vosk |
| **NeMo-Speech.cpp** (runtime Windows) | Apache-2.0 | scaricato da GitHub; eseguito come processo locale |
| **Nemotron 3.5 ASR Streaming 0.6B (q8_0)** | OpenMDW-1.1 | scaricato da Hugging Face; il modello non è ridistribuito con l'applicazione |
| **Graphviz** (Windows x64) | EPL-1.0 | scaricato da GitLab ufficiale; usato come processo locale per impaginare le mappe |
| **Parakeet TDT 0.6B v3** | CC-BY-4.0 | opzione futura per la corsia finale, non ancora distribuita |

## Template inclusi

I template di mappe e report sono adattati dalle pratiche dei pacchetti di skill più diffusi:

| Fonte | Licenza | Uso |
|---|---|---|
| [axtonliu/obsidian-visual-skills](https://github.com/axtonliu/axton-obsidian-visual-skills) | MIT | struttura delle istruzioni per mappe e verbali |
| [WH-2099/mermaid-skill](https://github.com/WH-2099/mermaid-skill) | MIT | tassonomia dei diagrammi (albero, flusso, SWOT) |
| [chrisjianghp/md2mindmap](https://github.com/chrisjianghp/md2mindmap) | MIT | approccio "note e idee" e mappe di studio |

Ogni template riporta la propria fonte e licenza nel campo `source` ed è modificabile dall'utente
nella cartella `%LOCALAPPDATA%\WisperTranslator\templates`.

## Nota sul marchio

Whisper è un modello di OpenAI distribuito con licenza MIT. Nemotron, Parakeet e NeMo-Speech.cpp
sono progetti NVIDIA. Graphviz è un progetto attivo della community AT&T/Eclipse. Questo progetto non è
affiliato a OpenAI, Mozilla, Meta, NVIDIA, Vosk o al progetto Graphviz.
