Unicode true
!include "MUI2.nsh"
!include "installer-paths.nsh"
Name "HOK BetaStudio 1.3"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\HOK BetaStudio"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32
VIProductVersion "1.3.0.2"
VIAddVersionKey "ProductName" "HOK BetaStudio"
VIAddVersionKey "FileDescription" "HOK BetaStudio per-user installer"
VIAddVersionKey "FileVersion" "1.3.0.2"
VIAddVersionKey "LegalCopyright" "See included third-party notices"
!define MUI_ICON "${BUILD_DIR}\assets\icons\app\hok-studio.ico"
!define MUI_UNICON "${BUILD_DIR}\assets\icons\app\hok-studio.ico"
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE VerifyInstallDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Vietnamese"

LangString InstallPathInvalid ${LANG_ENGLISH} "Choose a valid application folder, not a drive root or a file."
LangString InstallPathInvalid ${LANG_SIMPCHINESE} "请选择有效的程序文件夹，不要直接选择磁盘根目录或文件。"
LangString InstallPathInvalid ${LANG_VIETNAMESE} "Chọn thư mục ứng dụng hợp lệ, không chọn thư mục gốc ổ đĩa hoặc tệp."
LangString InstallPathConflict ${LANG_ENGLISH} "An existing file would be overwritten:$\r$\n$InstallConflict$\r$\nChoose another folder or a new HOK BetaStudio subfolder."
LangString InstallPathConflict ${LANG_SIMPCHINESE} "此位置存在同名文件，安装会覆盖它：$\r$\n$InstallConflict$\r$\n请选择其他目录，或新建 HOK BetaStudio 子文件夹。"
LangString InstallPathConflict ${LANG_VIETNAMESE} "Tệp đã tồn tại:$\r$\n$InstallConflict$\r$\nChọn thư mục khác hoặc tạo thư mục con HOK BetaStudio."
LangString InstallPathNotWritable ${LANG_ENGLISH} "This folder is not writable by the current user. Choose a writable folder, such as D:\Apps\HOK BetaStudio."
LangString InstallPathNotWritable ${LANG_SIMPCHINESE} "当前用户无法写入此目录。请选择有写入权限的文件夹，例如 D:\Apps\HOK BetaStudio。"
LangString InstallPathNotWritable ${LANG_VIETNAMESE} "Người dùng hiện tại không có quyền ghi. Chọn thư mục có quyền ghi, ví dụ D:\Apps\HOK BetaStudio."

Function .onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

; Do not reject existing directories in .onVerifyInstDir: NSIS also uses
; that callback inside Browse, where it disables the OK button.
Function VerifyInstallDirectory
  Call CheckInstallDirectory
  StrCmp $InstallPathError 0 done
  StrCmp $InstallPathError 2 conflict
  StrCmp $InstallPathError 3 not_writable
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathInvalid)" /SD IDOK
  Goto rejected
  conflict:
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathConflict)" /SD IDOK
  Goto rejected
  not_writable:
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathNotWritable)" /SD IDOK
  rejected:
  SetErrorLevel $InstallPathError
  Abort
  done:
FunctionEnd

Section "HOK BetaStudio"
  ; Also validate silent /D installs and changes since leaving the page.
  Call VerifyInstallDirectory
  SetOutPath "$INSTDIR"
  File /r "${BUILD_DIR}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\HOK BetaStudio"
  CreateShortcut "$SMPROGRAMS\HOK BetaStudio\HOK BetaStudio.lnk" "$INSTDIR\HOK BetaStudio.exe"
  CreateShortcut "$SMPROGRAMS\HOK BetaStudio\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "DisplayName" "HOK BetaStudio"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\HOKBetaStudio" "DisplayVersion" "1.3"
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
