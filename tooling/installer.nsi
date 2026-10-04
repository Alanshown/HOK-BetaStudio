Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 32
!include "MUI2.nsh"
!include "installer-paths.nsh"
!ifndef HOK_SHORTCUT_DIR
!define HOK_SHORTCUT_DIR "HOK BetaStudio"
!endif
Name "HOK BetaStudio 1.3"
OutFile "${OUTPUT_FILE}"
; Empty lets .onInit distinguish an explicit /D= override from the default.
InstallDir ""
RequestExecutionLevel user
VIProductVersion "1.3.0.3"
VIAddVersionKey "ProductName" "HOK BetaStudio"
VIAddVersionKey "FileDescription" "HOK BetaStudio per-user installer"
VIAddVersionKey "FileVersion" "1.3.0.3"
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

LangString InstallFileLocked ${LANG_ENGLISH} "An installed file is in use or cannot be updated:$\r$\n$InstallConflict$\r$\nClose HOK BetaStudio and its preview/export tasks, then retry. No files have been replaced."
LangString InstallFileLocked ${LANG_SIMPCHINESE} "已安装文件正在使用或无法更新：$\r$\n$InstallConflict$\r$\n请关闭 HOK BetaStudio 及其预览/导出任务后重试。尚未覆盖任何文件。"
LangString InstallFileLocked ${LANG_VIETNAMESE} "Tệp đang được sử dụng hoặc không thể cập nhật:$\r$\n$InstallConflict$\r$\nĐóng HOK BetaStudio và các tác vụ xem trước/xuất rồi thử lại. Chưa ghi đè tệp nào."

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
  Call FindRegisteredInstallation
  ${If} $INSTDIR == ""
    ${If} $RegisteredInstallDir != ""
      StrCpy $INSTDIR $RegisteredInstallDir
    ${Else}
      StrCpy $INSTDIR "$LOCALAPPDATA\Programs\HOK BetaStudio"
    ${EndIf}
  ${EndIf}
FunctionEnd

; Do not reject existing directories in .onVerifyInstDir: NSIS also uses
; that callback inside Browse, where it disables the OK button.
Function VerifyInstallDirectory
  Call CheckInstallDirectory
  StrCmp $InstallPathError 0 done
  StrCmp $InstallPathError 2 conflict
  StrCmp $InstallPathError 3 not_writable
  StrCmp $InstallPathError 4 locked
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathInvalid)" /SD IDOK
  Goto rejected
  conflict:
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathConflict)" /SD IDOK
  Goto rejected
  not_writable:
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallPathNotWritable)" /SD IDOK
  Goto rejected
  locked:
  MessageBox MB_OK|MB_ICONEXCLAMATION "$(InstallFileLocked)" /SD IDOK
  rejected:
  SetErrorLevel $InstallPathError
  Abort
  done:
FunctionEnd

Section "HOK BetaStudio"
  ; Also validate silent /D installs and changes since leaving the page.
  Call VerifyInstallDirectory
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ClearErrors
  File /r "${BUILD_DIR}\*.*"
  IfErrors extraction_failed
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  IfErrors extraction_failed
  CreateDirectory "$SMPROGRAMS\${HOK_SHORTCUT_DIR}"
  CreateShortcut "$SMPROGRAMS\${HOK_SHORTCUT_DIR}\HOK BetaStudio.lnk" "$INSTDIR\HOK BetaStudio.exe"
  CreateShortcut "$SMPROGRAMS\${HOK_SHORTCUT_DIR}\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${HOK_UNINSTALL_KEY}" "DisplayName" "HOK BetaStudio"
  WriteRegStr HKCU "${HOK_UNINSTALL_KEY}" "DisplayVersion" "1.3"
  WriteRegStr HKCU "${HOK_UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${HOK_UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${HOK_UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\HOK BetaStudio.exe"
  WriteRegDWORD HKCU "${HOK_UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${HOK_UNINSTALL_KEY}" "NoRepair" 1
  Goto installed
  extraction_failed:
  SetErrorLevel 5
  Abort
  installed:
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  SetRegView 64
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  ; Uninstalling an older copy must not erase a newer location's registration.
  ReadRegStr $0 HKCU "${HOK_UNINSTALL_KEY}" "InstallLocation"
  StrCmp $0 $INSTDIR 0 done
  Delete "$SMPROGRAMS\${HOK_SHORTCUT_DIR}\HOK BetaStudio.lnk"
  Delete "$SMPROGRAMS\${HOK_SHORTCUT_DIR}\Uninstall.lnk"
  RMDir "$SMPROGRAMS\${HOK_SHORTCUT_DIR}"
  DeleteRegKey HKCU "${HOK_UNINSTALL_KEY}"
  done:
SectionEnd
