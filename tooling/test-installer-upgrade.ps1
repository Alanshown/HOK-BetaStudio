param([string]$BuildDirectory='build/HOK-BetaStudio-1.4-win-x64',[string]$NsisCompiler='C:\Program Files (x86)\NSIS\makensis.exe')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$id=[Guid]::NewGuid().ToString('N')
$testRoot=Join-Path $root ('.cache/installer-upgrade-'+$id)
$payload=Join-Path $testRoot 'payload'
$keyName='Software\HOKBetaStudioInstallerTests\'+$id
$shortcutName='HOK Installer Test '+$id
$utf8=[Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path (Join-Path $payload 'worker'),(Join-Path $payload 'assets/icons/app') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root ($BuildDirectory+'/HOK BetaStudio.exe')) -Destination $payload
Copy-Item -LiteralPath (Join-Path $root 'assets/icons/app/hok-studio.ico') -Destination (Join-Path $payload 'assets/icons/app')
$library=Join-Path $payload 'worker/Hok.Legacy.dll'
[IO.File]::WriteAllText($library,'new payload',$utf8)
$check=Join-Path $testRoot 'check.nsh'
$uninstall=Join-Path $testRoot 'uninstall.nsh'
[IO.File]::WriteAllLines($check,@('!insertmacro CheckInstallFile "HOK BetaStudio.exe"','!insertmacro CheckInstallFile "worker\Hok.Legacy.dll"','!insertmacro CheckInstallFile "assets\icons\app\hok-studio.ico"','!insertmacro CheckInstallSubdirectory "worker"','!insertmacro CheckInstallSubdirectory "assets"','!insertmacro CheckInstallSubdirectory "assets\icons"','!insertmacro CheckInstallSubdirectory "assets\icons\app"'),$utf8)
[IO.File]::WriteAllLines($uninstall,@('Delete "$INSTDIR\HOK BetaStudio.exe"','Delete "$INSTDIR\worker\Hok.Legacy.dll"','Delete "$INSTDIR\assets\icons\app\hok-studio.ico"','RMDir "$INSTDIR\worker"','RMDir "$INSTDIR\assets\icons\app"','RMDir "$INSTDIR\assets\icons"','RMDir "$INSTDIR\assets"'),$utf8)
$setup=Join-Path $testRoot 'setup-test.exe'
& $NsisCompiler /INPUTCHARSET UTF8 /V2 "/DBUILD_DIR=$payload" "/DOUTPUT_FILE=$setup" "/DUNINSTALL_MANIFEST=$uninstall" "/DINSTALL_CHECK_MANIFEST=$check" "/DHOK_UNINSTALL_KEY=$keyName" "/DHOK_SHORTCUT_DIR=$shortcutName" (Join-Path $PSScriptRoot 'installer.nsi')
if($LASTEXITCODE){throw 'Installer regression compilation failed'}
$registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$productionKey='Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio'
function Read-Registration([string]$name){$k=$registry.OpenSubKey($name);try{if($k){$r=@{};foreach($n in $k.GetValueNames()){$r[$n]=$k.GetValue($n)};return ($r|ConvertTo-Json -Compress)}}finally{if($k){$k.Dispose()}}}
$productionBefore=Read-Registration $productionKey
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$message){if(-not $condition){throw $message};$checks.Add($message);Write-Output ('PASS '+$message)}
function Run-Setup([string]$target='', [int]$expected=0){
 $arguments=@('/S');if($target){$arguments+=('/D='+$target)}
 $p=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(30000)){$p.Kill();throw 'Installer timed out'}
 Check ($p.ExitCode -eq $expected) ('Installer exit '+$p.ExitCode+' (expected '+$expected+')')
}
function Registered-Path{$k=$registry.OpenSubKey($keyName);try{if($k){return $k.GetValue('InstallLocation')}}finally{if($k){$k.Dispose()}}}
try{
 $target=Join-Path $testRoot '已有文件 Tiếng Việt/我的程序'
 New-Item -ItemType Directory -Path $target -Force | Out-Null
 $sentinel=Join-Path $target 'user-export.txt'
 [IO.File]::WriteAllText($sentinel,'keep user data',$utf8)
 Run-Setup $target
 Check ((Registered-Path) -eq $target) 'Fresh install registers selected Unicode path'
 $installedLibrary=Join-Path $target 'worker/Hok.Legacy.dll'
 [IO.File]::WriteAllText($installedLibrary,'old payload',$utf8)
 # Simulate the registered 1.3 installation without touching production keys.
 $k=$registry.OpenSubKey($keyName,$true);try{$k.SetValue('DisplayVersion','1.3')}finally{$k.Dispose()}
 Run-Setup
 Check ([IO.File]::ReadAllText($installedLibrary) -eq 'new payload') 'No /D: detects existing location and overwrites old payload'
 $k=$registry.OpenSubKey($keyName);try{Check ($k.GetValue('DisplayVersion') -eq '1.4') '1.3 registration upgrades to 1.4 in the existing location'}finally{$k.Dispose()}
 Check ([IO.File]::ReadAllText($sentinel) -eq 'keep user data') 'In-place upgrade preserves unrelated user exports'
 Run-Setup
 Check ((Registered-Path) -eq $target) 'Repeated same-version installation succeeds in place'
 # Lock a later payload file. Preflight must stop before any earlier file changes.
 [IO.File]::WriteAllText($installedLibrary,'locked old payload',$utf8)
 $installedExe=Join-Path $target 'HOK BetaStudio.exe'
 $marker=[IO.File]::Open($installedExe,[IO.FileMode]::Append,[IO.FileAccess]::Write)
 try{$marker.WriteByte(42)}finally{$marker.Dispose()}
 $before=(Get-FileHash -LiteralPath (Join-Path $target 'HOK BetaStudio.exe')).Hash
 $lock=[IO.File]::Open($installedLibrary,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 try{Run-Setup '' 4}finally{$lock.Dispose()}
 Check ([IO.File]::ReadAllText($installedLibrary) -eq 'locked old payload') 'Locked installed file is not overwritten'
 Check ((Get-FileHash -LiteralPath (Join-Path $target 'HOK BetaStudio.exe')).Hash -eq $before) 'Lock failure occurs before payload extraction'
 Run-Setup
 Check ([IO.File]::ReadAllText($installedLibrary) -eq 'new payload') 'Upgrade succeeds after file lock is released'
 $collision=Join-Path $testRoot 'unregistered collision'
 New-Item -ItemType Directory -Path $collision | Out-Null
 [IO.File]::WriteAllText((Join-Path $collision 'HOK BetaStudio.exe'),'another application',$utf8)
 Run-Setup $collision 2
 Check ([IO.File]::ReadAllText((Join-Path $collision 'HOK BetaStudio.exe')) -eq 'another application') 'Registered install does not permit overwriting other folders'
 $alternate=Join-Path $testRoot 'new explicit destination'
 Run-Setup $alternate
 Check ((Registered-Path) -eq $alternate) 'Explicit /D destination still overrides detected installation'
 # Test the real generated uninstaller without self-copy/wait races. It must not
 # erase the alternate install registration or user files in the old location.
 $uninstallerCopy=Join-Path $testRoot 'uninstall-test.exe'
 Copy-Item -LiteralPath (Join-Path $target 'Uninstall.exe') -Destination $uninstallerCopy
 $p=Start-Process -FilePath $uninstallerCopy -ArgumentList @('/S',('_?='+$target)) -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(30000)){$p.Kill();throw 'Uninstaller timed out'}
 Check ($p.ExitCode -eq 0) 'Old copy uninstalls successfully'
 Check ((Registered-Path) -eq $alternate) 'Old uninstaller preserves newer installation registration'
 Check ([IO.File]::ReadAllText($sentinel) -eq 'keep user data') 'Uninstaller preserves unrelated files'
 Check (-not(Test-Path -LiteralPath (Join-Path $target 'HOK BetaStudio.exe'))) 'Uninstaller removes owned application files'
 # Invalid registration cannot grant overwrite rights.
 $k=$registry.OpenSubKey($keyName,$true);try{$k.SetValue('DisplayName','Unrelated application')}finally{$k.Dispose()}
 Run-Setup $alternate 2
 Check ((Read-Registration $productionKey) -eq $productionBefore) 'Production installation registration untouched'
 [IO.File]::WriteAllText((Join-Path $testRoot 'report.json'),(@{passed=$checks.Count;checks=$checks;fixtures=$testRoot}|ConvertTo-Json -Depth 5),$utf8)
 Write-Output ('Test fixtures: '+$testRoot)
}finally{
 # Only the unique test registration and exact test shortcuts are removed.
 $registry.DeleteSubKeyTree($keyName,$false);$registry.Dispose()
 $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
 foreach($name in @('HOK BetaStudio.lnk','Uninstall.lnk')){$f=Join-Path $shortcut $name;if(Test-Path -LiteralPath $f){Remove-Item -LiteralPath $f}}
 if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut}
}
