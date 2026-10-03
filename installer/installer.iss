; Installer di Wisper Translator (Inno Setup 6+)
; Compila con:  iscc installer.iss   (oppure esegui installer/build.ps1)

#define AppName "Wisper Translator"
#define AppVersion "2.2.0"
#define AppPublisher "Wisper Translator"
#define AppExeName "WisperTranslator.App.exe"
#define SourceDir "..\publish\app"

[Setup]
AppId={{8D2A5C11-5B7E-4C21-9E10-6F0B9C2A7E31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\publish
OutputBaseFilename=WisperTranslator-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\WisperTranslator.App\app.ico
LicenseFile=..\LICENSE
InfoBeforeFile=..\installer\before-install.txt
MinVersion=10.0

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un'icona sul desktop"; GroupDescription: "Icone:"; Languages: italian
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Languages: english
Name: "startup"; Description: "Avvia Wisper Translator all'accensione del PC"; GroupDescription: "Avvio:"; Flags: unchecked; Languages: italian
Name: "startup"; Description: "Start Wisper Translator with Windows"; GroupDescription: "Startup:"; Flags: unchecked; Languages: english

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Impostazioni e sessioni"; Filename: "{app}\{#AppExeName}"; Parameters: "--settings"
Name: "{group}\Disinstalla {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Parameters: "--autostart"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Avvia {#AppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExeName}"; Parameters: "--settings"; Description: "Apri le impostazioni per scaricare i modelli"; Flags: nowait postinstall skipifsilent unchecked

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"

[UninstallRun]
; L'applicazione può restare attiva nella barra delle applicazioni: la si chiude prima di rimuovere i file.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExeName} /F"; Flags: runhidden; RunOnceId: "KillApp"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  { Un'istanza già in esecuzione bloccherebbe l'aggiornamento: la si chiude prima di copiare. }
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#AppExeName} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

[Messages]
italian.FinishedLabel=Wisper Translator è installato.%n%nI modelli di trascrizione e traduzione vengono scaricati al primo avvio (circa 250 MB).%nSessioni e modelli restano nella cartella dati dell'utente e non vengono rimossi disinstallando.
english.FinishedLabel=Wisper Translator is installed.%n%nSpeech and translation models are downloaded on first run (about 250 MB).%nSessions and models stay in the user data folder and are not removed by uninstalling.
