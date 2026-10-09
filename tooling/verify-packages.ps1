param([Parameter(Mandatory)][string]$BuildDirectory,[Parameter(Mandatory)][string]$ReleaseDirectory,[string]$SevenZip='C:\Program Files\7-Zip\7z.exe')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build=[IO.Path]::GetFullPath((Join-Path $root $BuildDirectory))
$release=[IO.Path]::GetFullPath((Join-Path $root $ReleaseDirectory))
$zip=Join-Path $release 'HOK-BetaStudio-1.4-win-x64-portable.zip'
$setup=Join-Path $release 'HOK-BetaStudio-1.4-win-x64-setup.exe'
$seven=$SevenZip
& $seven t $zip | Out-Null
if($LASTEXITCODE){throw 'Portable archive integrity failed'}
& $seven t $setup | Out-Null
if($LASTEXITCODE){throw 'Setup archive integrity failed'}
$extract=Join-Path $root ('.cache/setup-payload-verify-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $extract | Out-Null
& $seven x $setup "-o$extract" -y | Out-Null
if($LASTEXITCODE){throw 'Setup extraction failed'}
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try {
 $entries=@{}
 foreach($e in $archive.Entries){if($e.Name){$relative=$e.FullName.Substring($e.FullName.IndexOf('/')+1).Replace('/', '\');$entries[$relative]=$e}}
 $files=@(Get-ChildItem -LiteralPath $build -Recurse -File)
 if($entries.Count -ne $files.Count){throw "ZIP file count mismatch: $($entries.Count)/$($files.Count)"}
 foreach($file in $files){
  $relative=$file.FullName.Substring($build.Length+1)
  $expected=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
  $stream=$entries[$relative].Open()
  try{$actual=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
  if($actual -ne $expected){throw "ZIP mismatch: $relative"}
  $installed=Join-Path $extract $relative
  if(-not(Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $expected){throw "Setup mismatch: $relative"}
 }
 $report=@{passed=$true;payloadFiles=$files.Count;zipAndSetupMatchBuild=$true;setupFileVersion=(Get-Item $setup).VersionInfo.FileVersion;portableSha256=(Get-FileHash $zip -Algorithm SHA256).Hash;setupSha256=(Get-FileHash $setup -Algorithm SHA256).Hash}
 $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'PACKAGE-VERIFICATION.json') -Encoding utf8
 $report | ConvertTo-Json
}finally{$archive.Dispose()}
