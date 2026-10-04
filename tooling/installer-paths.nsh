!ifndef HOK_INSTALLER_PATHS
!define HOK_INSTALLER_PATHS
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!ifndef HOK_UNINSTALL_KEY
!define HOK_UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio"
!endif

Var InstallPathError
Var InstallConflict
Var RegisteredInstallDir
Var IsUpgrade

; Trust only our per-user registration, with both installed executables present.
; An arbitrary nonempty folder must never gain permission to overwrite files.
Function FindRegisteredInstallation
  Push $0
  Push $1
  Push $2
  StrCpy $RegisteredInstallDir ""
  SetRegView 64
  ReadRegStr $0 HKCU "${HOK_UNINSTALL_KEY}" "DisplayName"
  StrCmp $0 "HOK BetaStudio" 0 done
  ReadRegStr $0 HKCU "${HOK_UNINSTALL_KEY}" "InstallLocation"
  StrCmp $0 "" done
  GetFullPathName $0 "$0"
  ${GetRoot} "$0" $1
  StrCmp $1 "" done
  StrCmp $0 $1 done
  StrCmp $0 "$1\" done
  ReadRegStr $1 HKCU "${HOK_UNINSTALL_KEY}" "UninstallString"
  StrCmp $1 '$\"$0\Uninstall.exe$\"' 0 done
  IfFileExists "$0\Uninstall.exe" 0 done
  IfFileExists "$0\HOK BetaStudio.exe" 0 done
  ClearErrors
  GetDLLVersion "$0\HOK BetaStudio.exe" $1 $2
  IfErrors done
  StrCpy $RegisteredInstallDir $0
  done:
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

!macro CheckInstallFile RelativePath
  ${If} ${FileExists} "$INSTDIR\${RelativePath}"
    StrCpy $InstallConflict "$INSTDIR\${RelativePath}"
    ${If} $IsUpgrade != 1
      Goto conflict
    ${EndIf}
    ; A directory in place of a file is not a valid upgrade target.
    IfFileExists "$INSTDIR\${RelativePath}\*.*" conflict
    ; Preflight write access/locks without modifying existing bytes. Check every
    ; payload file before extraction so a running app fails before any overwrite.
    System::Call 'kernel32::CreateFileW(w "$INSTDIR\${RelativePath}", i 0x40000000, i 7, p 0, i 3, i 0, p 0) p .r1'
    ${If} $1 == -1
      Goto locked
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r1)'
  ${EndIf}
!macroend

!macro CheckInstallSubdirectory RelativePath
  ${If} ${FileExists} "$INSTDIR\${RelativePath}"
  ${AndIfNot} ${FileExists} "$INSTDIR\${RelativePath}\*.*"
    StrCpy $InstallConflict "$INSTDIR\${RelativePath}"
    Goto conflict
  ${EndIf}
!macroend

; No UI or installed-state changes. Returns 0 = OK, 1 = invalid path,
; 2 = exact payload collision, 3 = cannot write, 4 = upgrade file locked/denied.
; A unique empty write probe
; is removed immediately; target directories are not created by validation.
Function CheckInstallDirectory
  Push $0
  Push $1
  StrCpy $InstallPathError 0
  StrCpy $InstallConflict ""
  StrCpy $IsUpgrade 0
  ${GetRoot} "$INSTDIR" $0
  StrCmp $0 "" invalid
  StrCmp $INSTDIR $0 invalid
  StrCmp $INSTDIR "$0\" invalid
  GetFullPathName $0 "$INSTDIR"
  ${If} $RegisteredInstallDir != ""
  ${AndIf} $0 == $RegisteredInstallDir
    StrCpy $IsUpgrade 1
  ${EndIf}

  !include "${INSTALL_CHECK_MANIFEST}"
  !insertmacro CheckInstallFile "Uninstall.exe"

  ; For a new directory test its nearest existing parent, without creating
  ; abandoned folders when the user cancels the directory page.
  StrCpy $0 $INSTDIR
  find_parent:
  IfFileExists "$0\*.*" writable
  IfFileExists "$0" invalid
  ${GetParent} "$0" $1
  StrCmp $1 "" not_writable
  StrCmp $1 $0 not_writable
  StrCpy $0 $1
  Goto find_parent
  writable:
  ClearErrors
  GetTempFileName $1 "$0"
  IfErrors not_writable
  Delete "$1"
  IfErrors not_writable
  Goto done
  invalid:
  StrCpy $InstallPathError 1
  Goto done
  conflict:
  StrCpy $InstallPathError 2
  Goto done
  not_writable:
  StrCpy $InstallPathError 3
  Goto done
  locked:
  StrCpy $InstallPathError 4
  done:
  Pop $1
  Pop $0
FunctionEnd
!endif
