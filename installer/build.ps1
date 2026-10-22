# Gera installer\out\G27Hub-Setup-<versão>.exe: código nativo, app publicado com o .NET embutido e Inno Setup.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (winget install JRSoftware.InnoSetup --scope user).' }

& "$root\src\native\build.ps1"
Remove-Item "$PSScriptRoot\publish" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\G27Hub.App" -c Release -r win-x64 --self-contained true -o "$PSScriptRoot\publish"
if ($LASTEXITCODE) { throw 'dotnet publish falhou' }
& $iscc "$PSScriptRoot\G27Hub.iss"
if ($LASTEXITCODE) { throw 'Inno Setup falhou' }
Get-ChildItem "$PSScriptRoot\out\*.exe" | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
