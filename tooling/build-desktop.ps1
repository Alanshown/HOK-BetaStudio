param([switch]$FrameworkDependent,[string]$OutputDirectory='build/HOK-BetaStudio-1.2-win-x64',[string]$NativeDirectory='.tools/native/x64')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'
if(-not (Test-Path -LiteralPath $dotnet)){$dotnet='dotnet'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$media=Join-Path $root '.tools/media/runtime'
foreach($name in @('vgmstream-cli.exe','ffmpeg.exe')){if(-not(Test-Path -LiteralPath (Join-Path $media $name))){throw "Missing media dependency: $name. See assets/licenses/media/NOTICE.txt"}}
Push-Location (Join-Path $root 'frontend')
try{& npm run build;if($LASTEXITCODE){throw 'Frontend build failed'}}finally{Pop-Location}
$out=[IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if(Get-Process -Name 'HOK BetaStudio' -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $out 'HOK BetaStudio.exe')}){throw 'Target app is running. Close it or choose another OutputDirectory.'}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$contained=if($FrameworkDependent){'false'}else{'true'}
& $dotnet publish (Join-Path $root 'backend/Hok.Desktop/Hok.Desktop.csproj') -c Release -o $out --self-contained $contained --nologo -v quiet -clp:ErrorsOnly
if($LASTEXITCODE){throw 'Desktop publish failed'}
& $dotnet publish (Join-Path $root 'backend/Hok.Worker/Hok.Worker.csproj') -c Release -o (Join-Path $out 'worker') --self-contained $contained --nologo -v quiet -clp:ErrorsOnly
if($LASTEXITCODE){throw 'Worker publish failed'}
New-Item -ItemType Directory -Path (Join-Path $out 'worker/media') -Force | Out-Null
Copy-Item -Path (Join-Path $media '*') -Destination (Join-Path $out 'worker/media') -Force
New-Item -ItemType Directory -Path (Join-Path $out 'worker/x64'),(Join-Path $out 'ui'),(Join-Path $out 'assets') -Force | Out-Null
$native=Join-Path $root $NativeDirectory
if(-not(Test-Path -LiteralPath $native)){throw "Missing native runtime directory: $native. See docs/BUILD.md"}
Get-ChildItem -LiteralPath $native -Filter '*.dll' | Copy-Item -Destination (Join-Path $out 'worker/x64') -Force
Copy-Item -Path (Join-Path $root 'frontend/dist/*') -Destination (Join-Path $out 'ui') -Recurse -Force
foreach($folder in @('catalog','portraits','placeholders','icons','fonts','licenses')){
 $target=Join-Path $out ('assets/'+$folder)
 New-Item -ItemType Directory -Path $target -Force|Out-Null
 $source=Join-Path $root ('assets/'+$folder)
 if(Test-Path -LiteralPath $source){Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force}
}
New-Item -ItemType Directory -Path (Join-Path $out 'ui/assets/fonts') -Force | Out-Null
Copy-Item -Path (Join-Path $root 'assets/fonts/*') -Destination (Join-Path $out 'ui/assets/fonts') -Force
Copy-Item -LiteralPath (Join-Path $root 'vendor/Studio-HoK/LICENSE') -Destination (Join-Path $out 'LICENSE-AssetStudio.txt') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $out 'LICENSE-HOK-BetaStudio.txt') -Force
Write-Output (Join-Path $out 'HOK BetaStudio.exe')
