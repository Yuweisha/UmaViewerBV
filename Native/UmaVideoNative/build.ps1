param([string]$Compiler='g++.exe',[string]$Output)
$ErrorActionPreference='Stop'
if(!$Output){$Output=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Assets/Plugins/UmaVideoNative.dll'))}
& $Compiler @('-std=c++17','-O2','-shared','-static','-static-libgcc','-static-libstdc++','-I',(Join-Path $PSScriptRoot 'include'),(Join-Path $PSScriptRoot 'src/encoder.cpp'),'-ld3d11','-ldxgi','-lucrt','-Wl,--no-insert-timestamp','-o',$Output)
if($LASTEXITCODE -ne 0){throw 'UmaVideoNative build failed'}
Get-FileHash -LiteralPath $Output
