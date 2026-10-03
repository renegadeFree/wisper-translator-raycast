# Modalità conversazione: chi ha detto cosa

Trascrivere una riunione, una call di WhatsApp o una videochiamata non è la stessa cosa che
trascrivere un video: parlano più persone e serve sapere **chi** ha detto ogni frase. La modalità
conversazione lo fa restando in locale.

## Come funziona

1. **Il microfono è "Tu".** La tua voce non ha bisogno di essere riconosciuta: è la tua. Le sue frasi
   arrivano nella corsia etichettata *Tu*.
2. **L'audio di sistema viene diarizzato a finestra scorrevole.** Il programma tiene gli ultimi
   45 secondi di audio, li rianalizza ogni 5 secondi mentre c'è segnale e riusa gli id dei parlanti
   sovrapposti nel tempo. Così `Speaker 2` resta la stessa persona anche nella battuta successiva,
   cosa che una diarizzazione frase-per-frase non può garantire.
3. **Se in una frase si sentono due voci, la frase si spezza.** La riga in corso diventa il primo
   turno, le altre si accodano: l'elenco non "salta" e non si perde nulla.
4. **Il testo immediato resta veloce.** I parziali arrivano dal WebSocket NeMo; il testo definitivo
   viene pubblicato subito e l'etichetta del parlante si aggiorna dopo, senza bloccare la battuta.
5. **Le etichette sono anonime**: `Speaker 1`, `Speaker 2`, … fino a `Speaker 4` con Sortformer
   (fino a `Speaker 8` con Nemotron), ognuna con il suo
   colore. Nessun profilo vocale viene salvato: riconoscere una voce già nominata in una sessione
   diversa richiederebbe un modello biometrico in più, che abbiamo scelto di non usare.

## Cosa serve

| Componente | Dimensione | Licenza | Note |
|---|---|---|---|
| **Sortformer 4 parlanti v2** (q8_0) | 147 MB | CC-BY-4.0 | predefinito per le call normali |
| Nemotron 3 Diarization (q8_0) | 107 MB | OpenMDW-1.1 | alternativa, fino a 8 parlanti |
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

Su una macchina con Ryzen 9 7900X3D, 24 thread e 63 GB di RAM, clip sintetica con 3 voci SAPI
distinte (52,9 s, 9 turni, pause di 1,8 s), audio di sistema con loopback e Sortformer:

| Fase | Tempo |
|---|---|
| Testo immediato (WebSocket) | 0,00 s per parola (arriva mentre il server decodifica) |
| Frase definitiva | mediana 0,43 s, peggiore 0,76 s |
| Diarizzazione su file intero (52,9 s) | 1,22 s (RTF 0,023) |
| Fermata della sessione | 90 ms |
| Accuratezza ASR sulla clip | 90,6% |
| Sequenza parlanti | 1,2,3,1,2,3,1,2 — stabile, senza inversioni |

## Limiti dichiarati

- **Voci sovrapposte**: se due persone parlano insieme, il turno più probabile è uno solo e l'altro
  testo può finire sotto il parlante sbagliato.
- **Timbri simili**: nei test la quarta voce era la prima con il tono alzato; essendo lo stesso
  timbro, Sortformer l'ha unita alla prima. Le tre voci distinte sono invece rimaste separate.
  La verifica valida per 4 timbri reali è una call vera.
- **Massimo 4 parlanti** con Sortformer, 8 con Nemotron (limite del modello scelto).
- **Nomi non ricordati**: ogni sessione riparte da `Speaker 1`. Non esiste una rubrica di voci.
- **Microfono + casse**: se lasci acceso il microfono mentre l'audio esce dagli altoparlanti, la
  stessa frase può comparire due volte (una come *Tu*, una come *Speaker N*). Con le cuffie non
  succede.
- **PC deboli**: sui profili minimi la diarizzazione è spenta per default (si attiva scaricandola a
  mano) perché somma RAM e calcolo a carico di una macchina già al limite.

## Come provarlo senza una call

```powershell
# una clip con due voci alternate (usa le voci installate in Windows)
wisper tts --lang en --lines 9 --pause 1800 `
  --voices "Microsoft Zira Desktop,Microsoft Hazel Desktop,Microsoft Elsa Desktop" `
  --out "$env:LOCALAPPDATA\WisperTranslator\test-clips\dialogo.wav"

# il percorso completo della modalità conversazione, senza aprire l'interfaccia
wisper diarize "$env:LOCALAPPDATA\WisperTranslator\test-clips\dialogo.wav" --lang en `
  --diarizer diar-streaming-sortformer-4spk-v2
```

`wisper diarize` avvia lo stesso server, fa la stessa richiesta dell'engine finale e stampa i turni
con i nomi: è il modo più rapido per capire se il diarizzatore sta separando bene le voci.
