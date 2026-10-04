param([string]$NsisCompiler='C:\Program Files (x86)\NSIS\makensis.exe')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot=Join-Path $root ('.cache/installer-path-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
$manifest=Join-Path $testRoot 'checks.nsh'
[IO.File]::WriteAllLines($manifest,@(
  '!insertmacro CheckInstallFile "HOK BetaStudio.exe"',
  '!insertmacro CheckInstallFile "worker\Hok.Legacy.dll"',
  '!insertmacro CheckInstallSubdirectory "worker"'
),$utf8)
$testExe=Join-Path $testRoot 'path-check.exe'
$script=@'
Unicode true
!include "${PATH_CHECKER}"
Name "HOK installer path regression"
OutFile "${OUTPUT_FILE}"
RequestExecutionLevel user
SilentInstall silent
Section
  Call CheckInstallDirectory
  SetErrorLevel $InstallPathError
SectionEnd
'@
$testScript=Join-Path $testRoot 'path-check.nsi'
[IO.File]::WriteAllText($testScript,$script,$utf8)
& $NsisCompiler /INPUTCHARSET UTF8 /V2 "/DPATH_CHECKER=$PSScriptRoot\installer-paths.nsh" "/DINSTALL_CHECK_MANIFEST=$manifest" "/DOUTPUT_FILE=$testExe" $testScript
if($LASTEXITCODE){throw 'Could not compile path regression harness.'}

$empty=New-Item -ItemType Directory -Path (Join-Path $testRoot 'existing empty')
$populated=New-Item -ItemType Directory -Path (Join-Path $testRoot '已有资料 Tiếng Việt')
$sentinel=Join-Path $populated.FullName 'keep.txt'
[IO.File]::WriteAllText($sentinel,'user file must survive',$utf8)
$existingApp=New-Item -ItemType Directory -Path (Join-Path $testRoot 'existing app')
[IO.File]::WriteAllText((Join-Path $existingApp.FullName 'HOK BetaStudio.exe'),'do not overwrite',$utf8)
$existingLibrary=New-Item -ItemType Directory -Path (Join-Path $testRoot 'library collision/worker')
[IO.File]::WriteAllText((Join-Path $existingLibrary.FullName 'Hok.Legacy.dll'),'keep library',$utf8)
$blocked=New-Item -ItemType Directory -Path (Join-Path $testRoot 'blocked subdirectory')
[IO.File]::WriteAllText((Join-Path $blocked.FullName 'worker'),'a file, not a directory',$utf8)
$uninstaller=New-Item -ItemType Directory -Path (Join-Path $testRoot 'other uninstaller')
[IO.File]::WriteAllText((Join-Path $uninstaller.FullName 'Uninstall.exe'),'other app',$utf8)
$fileTarget=Join-Path $testRoot 'not a directory'
[IO.File]::WriteAllText($fileTarget,'keep',$utf8)
$cases=@(
  @{Name='Existing empty directory';Path=$empty.FullName;Expected=0},
  @{Name='Nonempty Unicode directory';Path=$populated.FullName;Expected=0},
  @{Name='New nested Unicode path';Path=(Join-Path $testRoot '新目录/安装位置 with spaces/HOK BetaStudio');Expected=0},
  @{Name='Existing main EXE';Path=$existingApp.FullName;Expected=2},
  @{Name='Nested payload collision';Path=$existingLibrary.Parent.FullName;Expected=2},
  @{Name='File blocks payload directory';Path=$blocked.FullName;Expected=2},
  @{Name='Other application uninstaller';Path=$uninstaller.FullName;Expected=2},
  @{Name='Target is a file';Path=$fileTarget;Expected=1},
  @{Name='Ancestor is a file';Path=($fileTarget+'\child');Expected=1},
  @{Name='Drive root';Path=[IO.Path]::GetPathRoot($testRoot);Expected=1}
)
foreach($case in $cases){
  # /D must be the last argument and must NOT be quoted (NSIS syntax).
  $process=Start-Process -FilePath $testExe -ArgumentList @('/S',('/D='+$case.Path)) -WindowStyle Hidden -PassThru
  if(-not $process.WaitForExit(15000)){ $process.Kill(); throw ('Timeout: '+$case.Name) }
  if($process.ExitCode -ne $case.Expected){throw ($case.Name+': expected '+$case.Expected+', got '+$process.ExitCode)}
  Write-Output ('PASS '+$case.Name)
}
if([IO.File]::ReadAllText($sentinel) -ne 'user file must survive'){throw 'Existing data changed.'}
if((Get-ChildItem -LiteralPath $empty.FullName -Force).Count){throw 'Write probe leaked.'}
if(Test-Path -LiteralPath (Join-Path $testRoot '新目录')){throw 'Validation created target folders.'}
$installer=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer.nsi') -Raw
if($installer -match '(?m)^Function \.onVerifyInstDir'){throw 'Do not restrict Browse with .onVerifyInstDir.'}
if(([regex]::Matches($installer,'Call VerifyInstallDirectory')).Count -ne 1 -or $installer -notmatch 'MUI_PAGE_CUSTOMFUNCTION_LEAVE VerifyInstallDirectory'){throw 'Both silent and interactive installs must validate.'}
Write-Output 'PASS preserved user files; no validation leftovers; Browse callback unrestricted; interactive + silent validation'
Write-Output ('Test fixtures: '+$testRoot)
