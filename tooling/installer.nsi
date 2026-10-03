Unicode true
!include "MUI2.nsh"
Name "HOK BetaStudio 1.2"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\HOK BetaStudio"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "1.2.0.0"
VIAddVersionKey "ProductName" "HOK BetaStudio"
VIAddVersionKey "FileDescription" "HOK BetaStudio per-user installer"
VIAddVersionKey "FileVersion" "1.2"
VIAddVersionKey "LegalCopyright" "See included third-party notices"
!define MUI_ICON "${BUILD_DIR}\assets\icons\app\hok-studio.ico"
!define MUI_UNICON "${BUILD_DIR}\assets\icons\app\hok-studio.ico"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Vietnamese"

Function .onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Function .onVerifyInstDir
  ; New installs only: refuse an existing directory containing files.
  ; This avoids merging user data or silently overwriting another app.
  IfFileExists "$INSTDIR\*.*" 0 safe
  Abort
  safe:
FunctionEnd

Section "HOK BetaStudio"
  SetOutPath "$INSTDIR"
  File /r "${BUILD_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\HOK BetaStudio"
  CreateShortcut "$SMPROGRAMS\HOK BetaStudio\HOK BetaStudio.lnk" "$INSTDIR\HOK BetaStudio.exe"
  CreateShortcut "$SMPROGRAMS\HOK BetaStudio\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "DisplayName" "HOK BetaStudio"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "DisplayVersion" "1.2"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "DisplayIcon" "$INSTDIR\HOK BetaStudio.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  SetRegView 64
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\HOK BetaStudio\HOK BetaStudio.lnk"
  Delete "$SMPROGRAMS\HOK BetaStudio\Uninstall.lnk"
  RMDir "$SMPROGRAMS\HOK BetaStudio"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio"
SectionEnd
