!ifndef HOK_INSTALLER_PATHS
!define HOK_INSTALLER_PATHS
!include "FileFunc.nsh"
!include "LogicLib.nsh"

Var InstallPathError
Var InstallConflict

!macro CheckInstallFile RelativePath
  ${If} ${FileExists} "$INSTDIR\${RelativePath}"
    StrCpy $InstallConflict "$INSTDIR\${RelativePath}"
    Goto conflict
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
; 2 = exact payload collision, 3 = cannot write. A unique empty write probe
; is removed immediately; target directories are not created by validation.
Function CheckInstallDirectory
  Push $0
  Push $1
  StrCpy $InstallPathError 0
  StrCpy $InstallConflict ""
  ${GetRoot} "$INSTDIR" $0
  StrCmp $0 "" invalid
  StrCmp $INSTDIR $0 invalid
  StrCmp $INSTDIR "$0\" invalid

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
  done:
  Pop $1
  Pop $0
FunctionEnd
!endif
