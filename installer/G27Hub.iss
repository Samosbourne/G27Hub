; Instalador do G27 Hub (Inno Setup 6). Gere com installer\build.ps1 (publica o app com o .NET embutido e compila isto).
; Instala só para o usuário (sem administrador). Desinstalar roda "G27Hub.App.exe --uninstall-cleanup", que desfaz
; o que o app mudou no PC (force feedback, iniciar com o Windows, calibração, mod do BeamNG, plugin do ETS2, drivers).

#define AppVersion "1.1.1"
#define AppExe "G27Hub.App.exe"

[Setup]
AppId={{B5E5C0A2-7F27-4C1B-9E27-27A1B2C3D427}
AppName=G27 Hub
AppVersion={#AppVersion}
AppPublisher=John Salim / Samosbourne (desenvolvido com IA)
AppCopyright=John Salim / Samosbourne
AppComments=Logitech G27: modo nativo, force feedback, perfis, LEDs de RPM
DefaultDirName={autopf}\G27 Hub
DefaultGroupName=G27 Hub
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=out
OutputBaseFilename=G27Hub-Setup-{#AppVersion}
SetupIconFile=..\assets\g27hub.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=G27 Hub
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; o app usa este mutex para não abrir duas vezes: instalar/desinstalar pede para fechá-lo antes
AppMutex=Local\G27Hub_App
CloseApplications=yes

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
pt.AutoStart=Iniciar o G27 Hub com o Windows (na bandeja, mantém o volante configurado)
en.AutoStart=Start G27 Hub with Windows (in the tray, keeps the wheel configured)

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\G27 Hub"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\G27 Hub"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; mesmo valor que a opção "Iniciar com o Windows" do app usa
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "G27Hub"; ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--setup-lang {language}"; Description: "{cm:LaunchProgram,G27 Hub}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: waituntilterminated; RunOnceId: "G27HubCleanup"
