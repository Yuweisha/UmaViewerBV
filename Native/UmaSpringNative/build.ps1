param([string]$Compiler='g++.exe',[string]$Output)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$Output){$Output=Join-Path $projectRoot 'Assets/Plugins/UmaSpringNative.dll'}
$Output=[IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($Output)) -Force | Out-Null
$arguments=@('-std=c++17','-O3','-flto','-shared','-static-libgcc','-static-libstdc++',
    '-fno-fast-math','-ffp-contract=off','-fno-strict-aliasing','-fno-math-errno','-msse2','-mfpmath=sse',
    (Join-Path $PSScriptRoot 'src/kernel.cpp'),'-lucrt','-Wl,--no-insert-timestamp','-o',$Output)
& $Compiler @arguments
if($LASTEXITCODE -ne 0){throw "Native kernel build failed: $LASTEXITCODE"}
Get-FileHash -LiteralPath $Output -Algorithm SHA256 | ForEach-Object {Write-Output "$($_.Path) $($_.Hash)"}
