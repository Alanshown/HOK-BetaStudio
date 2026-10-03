param([string]$BuildDirectory='build/HOK-BetaStudio-1.2-rebuild-beta-win-x64',[string]$OutputDirectory='deliverables',[string]$NsisCompiler='')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=[IO.Path]::GetFullPath((Join-Path $root $BuildDirectory))
$output=[IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if(-not(Test-Path -LiteralPath (Join-Path $source 'HOK BetaStudio.exe'))){throw 'Build the desktop package first.'}
if($source -eq $root -or $output.StartsWith($source+[IO.Path]::DirectorySeparatorChar)){throw 'Output must be separate from the build input.'}
if(-not $NsisCompiler){$command=Get-Command makensis.exe -ErrorAction SilentlyContinue;if($command){$NsisCompiler=$command.Source}else{$NsisCompiler='C:\Program Files (x86)\NSIS\makensis.exe'}}
if(-not(Test-Path -LiteralPath $NsisCompiler)){throw 'NSIS is required to create the installer.'}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zip=Join-Path $output 'HOK-BetaStudio-1.2-win-x64-portable.zip'
$setup=Join-Path $output 'HOK-BetaStudio-1.2-win-x64-setup.exe'
if((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath $setup)){throw 'Package output already exists; choose another OutputDirectory to preserve it.'}
$files=Get-ChildItem -LiteralPath $source -Recurse -File
if($files | Where-Object {$_.Extension -in @('.db','.log')}){throw 'Unexpected DB or log in build input; audit before packaging.'}
$temp=Join-Path $root ('.cache/installer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
# Generate an exact uninstall manifest. Never recursively delete the installation tree:
# exports or files the user adds later must remain untouched.
$lines=[Collections.Generic.List[string]]::new()
foreach($file in $files){$relative=$file.FullName.Substring($source.Length+1);if($relative.Contains('$') -or $relative.Contains('"')){throw 'Unsupported filename in installer manifest'};$lines.Add('Delete "$INSTDIR\'+$relative+'"')}
$dirs=Get-ChildItem -LiteralPath $source -Recurse -Directory | Sort-Object {$_.FullName.Length} -Descending
foreach($dir in $dirs){$relative=$dir.FullName.Substring($source.Length+1);$lines.Add('RMDir "$INSTDIR\'+$relative+'"')}
$manifest=Join-Path $temp 'uninstall-files.nsh'
[IO.File]::WriteAllLines($manifest,$lines,[Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath $source -DestinationPath $zip -CompressionLevel Optimal
& $NsisCompiler /V2 "/DBUILD_DIR=$source" "/DOUTPUT_FILE=$setup" "/DUNINSTALL_MANIFEST=$manifest" (Join-Path $PSScriptRoot 'installer.nsi')
if($LASTEXITCODE){throw 'NSIS compilation failed.'}
$hashes=@($zip,$setup) | ForEach-Object { $hash=Get-FileHash -LiteralPath $_ -Algorithm SHA256; $hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Write-Output $zip
Write-Output $setup
Write-Output 'PRIVATE PREPARATION ONLY: publication is gated by docs/RELEASE-CHECKLIST.md'
