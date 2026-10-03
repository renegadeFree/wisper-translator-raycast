# Compilare e distribuire su Windows

Questa edizione deriva da [renegadeFree/wisper-translator](https://github.com/renegadeFree/wisper-translator).
La repository privata separata conserva la cronologia originale. GitHub non crea un fork
verso lo stesso account proprietario; questa copia non ha il collegamento nativo di fork.

## Prerequisiti

- Windows 10 o 11 **x64**. L'app usa WPF, WASAPI e DLL native Windows.
- [SDK .NET 8 x64](https://dotnet.microsoft.com/download/dotnet/8.0), non soltanto il runtime.
  L'SDK Windows include anche il runtime desktop necessario a WPF:
  [documentazione Microsoft](https://learn.microsoft.com/dotnet/core/install/windows).
- [Git for Windows](https://git-scm.com/downloads/win) se cloni la repository.
- Accesso alla repository privata con il tuo account GitHub e Internet per il restore NuGet.
- Facoltativo: Visual Studio 2022 con workload **Sviluppo desktop .NET** e supporto .NET 8.
  La compilazione da PowerShell non richiede Visual Studio.

Apri PowerShell o Windows Terminal e verifica `dotnet --list-sdks`: deve comparire un SDK `8.0.xxx`.
Se un SDK più recente è selezionato, il progetto continua a usare il target `net8.0-windows`;
per riprodurre la CI usa un SDK della serie 8.0.

## Clone, compilazione e test

```powershell
git clone https://github.com/renegadeFree/wisper-translator-raycast.git
cd wisper-translator-raycast
dotnet restore WisperTranslator.sln
dotnet build WisperTranslator.sln -c Release --no-restore
dotnet test WisperTranslator.sln -c Release --no-build
```

Su Windows esegui l'intera suite: include i test PDF e i font di sistema.
Non applicare il filtro `Category!=Windows` né modificare il runtimeconfig dei test.
Non occorrono download di modelli o dispositivi audio per i test unitari.

Per aprire l'app dalla sorgente:

```powershell
dotnet run --project src/WisperTranslator.App -c Release
dotnet run --project src/WisperTranslator.Cli -c Release -- hardware
dotnet run --project src/WisperTranslator.Cli -c Release -- models list
```

Il modulo `src/WisperTranslator.Core/Models` è incluso e deve essere versionato.
Il pattern `/models/` nel `.gitignore` esclude solo la cartella dei modelli nella radice.
Il package Vosk viene distribuito con le quattro DLL Windows x64, anche in cross-compilazione.

## Build portabile autonoma

```powershell
dotnet publish src/WisperTranslator.App -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -o publish/app
Copy-Item installer/portable-readme.txt publish/app/LEGGIMI.txt
Copy-Item LICENSE, THIRD-PARTY-NOTICES.md publish/app/
Compress-Archive -Path publish/app/* -DestinationPath publish/WisperTranslator-Windows-x64.zip -Force
Get-FileHash publish/WisperTranslator-Windows-x64.zip -Algorithm SHA256
```

La cartella `publish/app` e lo ZIP contengono l'eseguibile, il runtime .NET e le librerie native.
Distribuisci **tutta la cartella**, non solo l'EXE. Chi riceve lo ZIP non deve installare .NET.
I modelli grandi e il server di traduzione vengono scaricati dall'app al primo utilizzo;
non sono inclusi nel pacchetto portabile. I dati restano in `%LOCALAPPDATA%\WisperTranslator`.

La [workflow Windows build](../.github/workflows/windows-build.yml) esegue restore, build Release,
tutti i test, publish e un controllo nativo della regione arrotondata della barra su un runner Windows.
Da **Actions → Windows build → Run workflow** puoi ripeterla. Una esecuzione riuscita offre
gli artifact `WisperTranslator-Windows-x64` (ZIP e SHA-256) e `Windows-test-results`.
Gli artifact CI scadono dopo 30 giorni; le build pubblicate nelle **Releases** restano disponibili.
Il download di artifact e release private richiede l'accesso GitHub.

## Installer opzionale

Installa [Inno Setup 6](https://jrsoftware.org/isdl.php) nel percorso standard.
Lo script usa `ISCC.exe`: [documentazione della compilazione da riga di comando](https://jrsoftware.org/is6help/topic_compilercmdline.htm).
Scarica prima il server di traduzione locale sulla macchina di compilazione:

```powershell
dotnet run --project src/WisperTranslator.Cli -c Release -- mt-server download
powershell -ExecutionPolicy Bypass -File installer/build.ps1
```

Lo script pubblica l'app e copia `%LOCALAPPDATA%\WisperTranslator\tools\mtranserver.exe`
nel pacchetto prima di compilare l'installer. L'output è `publish/WisperTranslator-Setup-2.2.0.exe`.
Per riusare una cartella `publish/app` già generata, aggiungi `-SkipPublish`.
La workflow fornita genera il pacchetto portabile; l'installer richiede questo passaggio aggiuntivo.
Né ZIP né installer sono firmati digitalmente: Windows può mostrare SmartScreen.

## Collaudo sul PC Windows

```powershell
dotnet run --project src/WisperTranslator.App -c Release -- --bar-selftest
Get-Content "$env:LOCALAPPDATA\WisperTranslator\logs\bar-selftest.txt" -Tail 1
dotnet run --project src/WisperTranslator.App -c Release -- --shot=docs/screenshots
dotnet list WisperTranslator.sln package --vulnerable --include-transitive
```

Il risultato atteso dell'autotest è `PASS: regione finestra COMPLEXREGION`.
Verifica poi avvio, microfono e loopback, mute/riattivazione, stop e riavvio mentre il decoder lavora,
barra senza cornice, blur con trasparenza attiva/disattiva e monitor a DPI diversi.
L'autotest controlla la regione HWND, non la resa del blur o la qualità ASR.
Vedi il [rapporto di review](CODE_REVIEW.md) per misure e limiti residui.

## Errori comuni

| Errore | Soluzione |
|---|---|
| `dotnet` non riconosciuto o SDK assente | Installa l'SDK .NET 8 x64 e riapri il terminale. |
| Repository non trovata / 404 | Usa un account GitHub autorizzato alla repository privata. |
| `NU1301` / restore fallito | Verifica accesso a `https://api.nuget.org/v3/index.json`, proxy e rete. |
| Build tentata su macOS/Linux, `NETSDK1100` | La cross-build usa `-p:EnableWindowsTargeting=true`; l'app e i test desktop si eseguono su Windows. |
| `mtranserver.exe non trovato` nello script installer | Esegui il comando `mt-server download` sopra e attendi il completamento. |
| `Inno Setup 6 non trovato` | Installa Inno Setup 6 nel percorso standard. |
| DLL native mancanti | Ripubblica per `win-x64`, estrai tutto lo ZIP e non spostare soltanto l'EXE. |
| Problemi all'avvio | Consulta `%LOCALAPPDATA%\WisperTranslator\logs\errori.log`. |
