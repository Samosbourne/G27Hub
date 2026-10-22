# Compila g27ffb.dll, g27fftest.exe e o plugin do ETS2/ATS (g27hub_telemetry.dll, só x64) para x64 e x86 em src/native/out/<arch>/.
# Usa o CMake que vem com o Visual Studio Build Tools (C++).
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$cmake = & $vswhere -latest -products * -find 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe' | Select-Object -First 1
if (-not $cmake) { throw 'CMake do Visual Studio nao encontrado (instale o Build Tools com C++).' }

foreach ($arch in @(@{ A = 'x64'; Out = 'x64' }, @{ A = 'Win32'; Out = 'x86' })) {
    $build = "$PSScriptRoot\build\$($arch.Out)"
    & $cmake -S $PSScriptRoot -B $build -A $arch.A | Out-Null
    if ($LASTEXITCODE) { throw "cmake configure $($arch.Out) falhou" }
    & $cmake --build $build --config Release
    if ($LASTEXITCODE) { throw "cmake build $($arch.Out) falhou" }
    New-Item -ItemType Directory -Force "$PSScriptRoot\out\$($arch.Out)" | Out-Null
    Copy-Item "$build\Release\g27ffb.dll", "$build\Release\g27fftest.exe" "$PSScriptRoot\out\$($arch.Out)\"
    if ($arch.Out -eq 'x64') { Copy-Item "$build\Release\g27hub_telemetry.dll" "$PSScriptRoot\out\x64\" }
}
Write-Host 'OK: src/native/out/x64 e src/native/out/x86'
