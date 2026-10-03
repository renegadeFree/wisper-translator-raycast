# Modalità conversazione: chi ha detto cosa

Trascrivere una riunione, una call di WhatsApp o una videochiamata non è la stessa cosa che
trascrivere un video: parlano più persone e serve sapere **chi** ha detto ogni frase. La modalità
conversazione lo fa restando in locale.

## Come funziona

1. **Il microfono è "Tu".** La tua voce non ha bisogno di essere riconosciuta: è la tua. Le sue frasi
   arrivano nella corsia etichettata *Tu*.
2. **L'audio di sistema viene diarizzato.** Ogni frase conclusa degli altri partecipanti passa dal
   diarizzatore, che restituisce il parlante di ogni parola. Le parole consecutive dello stesso
   parlante diventano un turno.
3. **Se in una frase si sentono due voci, la frase si spezza.** La riga in corso diventa il primo
   turno, le altre si accodano: l'elenco non "salta" e non si perde nulla.
4. **Il testo immediato resta veloce.** La diarizzazione gira solo sulle frasi definitive, quindi i
   parziali hanno la stessa latenza di prima e i nomi compaiono quando la frase si chiude.
5. **Le etichette sono anonime**: `Speaker 1`, `Speaker 2`, … fino a `Speaker 8`, ognuna con il suo
   colore. Nessun profilo vocale viene salvato: riconoscere una voce già nominata in una sessione
   diversa richiederebbe un modello biometrico in più, che abbiamo scelto di non usare.

## Cosa serve

| Componente | Dimensione | Licenza | Note |
|---|---|---|---|
| **Nemotron 3 Diarization** (q8_0) | 107 MB | OpenMDW-1.1 | predefinito, fino a 8 parlanti |
| Sortformer 4 parlanti v2 (q8_0) | 147 MB | CC-BY-4.0 | alternativa, massimo 4 parlanti |
| Runtime NeMo-Speech.cpp | 5,5 MB | Apache-2.0 | processo locale già usato da Nemotron 3.5 |
| Nemotron 3.5 ASR Streaming (q8_0) | 708 MB | OpenMDW-1.1 | serve come motore definitivo |

Si scaricano da **Impostazioni → Conversazione → Scarica il diarizzatore** (o da **Modelli**). Senza
questi componenti il programma continua a trascrivere: semplicemente non aggiunge i nomi.

## Come si usa

1. Accendi **Sistema** (e **Microfono** se vuoi comparire come *Tu*).
2. Attiva **Conversazione** nel pannello o dalla barra.
3. Scegli la lingua parlata nella direzione (`IT → EN` o `EN → IT`) e premi **Avvia**.
4. Nella barra compare quante voci sono state sentite; nel pannello e nel dettaglio sessione ogni
   riga porta il pallino e il nome del parlante.
5. Alla fine, la sessione resta nello storico con i parlanti: esportazione **SRT** con tag
   `<v Speaker 1>`, testo con `Speaker 1: …`, JSON con `speaker`, e i report PDF con la sezione
   **Partecipanti rilevati** (battute, tempo di parola e quota di ciascuno).

## Cosa aspettarsi (misurato, non stimato)

Su una macchina con Ryzen 9 7900X3D, 63 GB di RAM e RTX 3080 Ti, sessione reale con diarizzazione
attiva su audio di sistema e microfono:

| Fase | Tempo |
|---|---|
| Testo immediato (parziali) | 73–600 ms |
| Frase definitiva con diarizzazione | 82–1560 ms (tipico 700–900 ms) |
| Fermata della sessione | 101 ms |
| RAM aggiuntiva del diarizzatore | ~150 MB |

## Limiti dichiarati

- **Voci sovrapposte**: se due persone parlano insieme, il turno più probabile è uno solo e l'altro
  testo può finire sotto il parlante sbagliato.
- **Timbri simili**: nei test con voci sintetiche dello stesso sesso il diarizzatore le ha in parte
  unite. Con voci reali e distinte va molto meglio; la verifica valida è una call vera.
- **Massimo 8 parlanti** per sessione (limite del modello).
- **Nomi non ricordati**: ogni sessione riparte da `Speaker 1`. Non esiste una rubrica di voci.
- **Microfono + casse**: se lasci acceso il microfono mentre l'audio esce dagli altoparlanti, la
  stessa frase può comparire due volte (una come *Tu*, una come *Speaker N*). Con le cuffie non
  succede.
- **PC deboli**: sui profili minimi la diarizzazione è spenta per default (si attiva scaricandola a
  mano) perché somma RAM e calcolo a carico di una macchina già al limite.

## Come provarlo senza una call

```powershell
# una clip con due voci alternate (usa le voci installate in Windows)
wisper tts --lang en --voices "Microsoft Zira Desktop,Microsoft Hazel Desktop" --lines 4 `
  --pause 500 --out "$env:LOCALAPPDATA\WisperTranslator\test-clips\dialogo.wav"

# il percorso completo della modalità conversazione, senza aprire l'interfaccia
wisper diarize "$env:LOCALAPPDATA\WisperTranslator\test-clips\dialogo.wav" --lang en
```

`wisper diarize` avvia lo stesso server, fa la stessa richiesta dell'engine finale e stampa i turni
con i nomi: è il modo più rapido per capire se il diarizzatore sta separando bene le voci.
