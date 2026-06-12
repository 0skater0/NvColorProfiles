; NvColorProfiles - NSIS Installer
;
; Per-user, GUI-only installer. No admin rights, no PATH changes. Installs the
; single-file EXE into %LOCALAPPDATA%\NvColorProfiles\app, creates Start-Menu and
; (optional) desktop shortcuts, can enable autostart, and registers an uninstaller.
;
; Language follows the OS: English is the default/fallback, German on German systems.
; All custom strings are LangStrings (the MUI standard buttons localize themselves).
;
; Built natively on Linux via `makensis` (nsis-builder CI image), no Wine. CI passes
; -DAppVersion / -DAppVersionClean and -INPUTCHARSET UTF8 (so the umlauts below are
; read correctly).

Unicode true
SetCompressor /SOLID lzma

;------------------------------------------------------------------------------
; Version (CI passes -DAppVersion=... -DAppVersionClean=...)
;------------------------------------------------------------------------------
!ifndef AppVersion
  !define AppVersion "0.0.0"
!endif
!ifndef AppVersionClean
  !define AppVersionClean "${AppVersion}"
!endif

;------------------------------------------------------------------------------
; App identity
;------------------------------------------------------------------------------
!define APP_NAME       "NvColorProfiles"
!define APP_PUBLISHER  "0skater0"
!define APP_EXE        "NvColorProfiles.exe"
!define RUN_VALUE      "NvColorProfiles"
; Per-user uninstall + data keys live under HKCU (no admin install).
!define UNINST_REGKEY  "Software\Microsoft\Windows\CurrentVersion\Uninstall\NvColorProfiles"
!define APP_REGKEY     "Software\NvColorProfiles"
!define RUN_REGKEY     "Software\Microsoft\Windows\CurrentVersion\Run"

;------------------------------------------------------------------------------
; Output / install settings
;------------------------------------------------------------------------------
Name "${APP_NAME}"
OutFile "Output\nvcolorprofiles-setup-${AppVersionClean}.exe"
InstallDir "$LOCALAPPDATA\NvColorProfiles\app"
InstallDirRegKey HKCU "${APP_REGKEY}" "InstallDir"
RequestExecutionLevel user
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${AppVersionClean}.0"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "ProductVersion"  "${AppVersionClean}"
VIAddVersionKey "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey "FileVersion"     "${AppVersionClean}"
VIAddVersionKey "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "${APP_PUBLISHER}"

;------------------------------------------------------------------------------
; UI
;------------------------------------------------------------------------------
!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"

; install options (custom page)
Var DesktopShortcut
Var DesktopCheckbox
Var AutostartEnabled
Var AutostartCheckbox
; uninstall option
Var UnWipeData
Var UnWipeCheckbox

!define MUI_ABORTWARNING
!define MUI_ICON "nvcolorprofiles.ico"
!define MUI_UNICON "nvcolorprofiles.ico"

; branded welcome/finish side graphic (164x314) instead of the default NSIS clip art
!define MUI_WELCOMEFINISHPAGE_BITMAP "welcome.bmp"

; Finish page: optionally launch the (tray) app right away.
!define MUI_FINISHPAGE_RUN ""
!define MUI_FINISHPAGE_RUN_TEXT "$(RUN_TEXT)"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
Page custom OptionsPage OptionsPageLeave
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
UninstPage custom un.OptionsPage un.OptionsPageLeave
!insertmacro MUI_UNPAGE_INSTFILES

; English first = default/fallback for any non-German system; German on German systems.
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "German"

;------------------------------------------------------------------------------
; Localized custom strings (the MUI built-in buttons/pages localize on their own)
;------------------------------------------------------------------------------
LangString RUN_TEXT      ${LANG_ENGLISH} "Run ${APP_NAME}"
LangString RUN_TEXT      ${LANG_GERMAN}  "${APP_NAME} starten"
LangString OPT_TITLE     ${LANG_ENGLISH} "Options"
LangString OPT_TITLE     ${LANG_GERMAN}  "Optionen"
LangString OPT_SUBTITLE  ${LANG_ENGLISH} "Choose shortcuts and autostart."
LangString OPT_SUBTITLE  ${LANG_GERMAN}  "Verknüpfungen und Autostart festlegen."
LangString OPT_DESKTOP   ${LANG_ENGLISH} "Create a desktop shortcut"
LangString OPT_DESKTOP   ${LANG_GERMAN}  "Desktop-Verknüpfung erstellen"
LangString OPT_AUTOSTART ${LANG_ENGLISH} "Start with Windows (autostart)"
LangString OPT_AUTOSTART ${LANG_GERMAN}  "Mit Windows starten (Autostart)"
LangString SC_UNINSTALL  ${LANG_ENGLISH} "Uninstall ${APP_NAME}"
LangString SC_UNINSTALL  ${LANG_GERMAN}  "${APP_NAME} deinstallieren"
LangString UN_SUBTITLE   ${LANG_ENGLISH} "Choose what to remove."
LangString UN_SUBTITLE   ${LANG_GERMAN}  "Lege fest, was entfernt werden soll."
LangString UN_LABEL      ${LANG_ENGLISH} "${APP_NAME} will be removed."
LangString UN_LABEL      ${LANG_GERMAN}  "${APP_NAME} wird entfernt."
LangString UN_WIPE       ${LANG_ENGLISH} "Also delete all profiles and settings"
LangString UN_WIPE       ${LANG_GERMAN}  "Auch alle Profile und Einstellungen löschen"

;------------------------------------------------------------------------------
; Custom options page (shortcut + autostart)
;------------------------------------------------------------------------------
Function OptionsPage
  !insertmacro MUI_HEADER_TEXT "$(OPT_TITLE)" "$(OPT_SUBTITLE)"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateCheckbox} 0 8u 100% 12u "$(OPT_DESKTOP)"
  Pop $DesktopCheckbox
  ${NSD_Check} $DesktopCheckbox ; default on
  ${NSD_CreateCheckbox} 0 28u 100% 12u "$(OPT_AUTOSTART)"
  Pop $AutostartCheckbox
  nsDialogs::Show
FunctionEnd

Function OptionsPageLeave
  ${NSD_GetState} $DesktopCheckbox $DesktopShortcut
  ${NSD_GetState} $AutostartCheckbox $AutostartEnabled
FunctionEnd

;------------------------------------------------------------------------------
; Install
;------------------------------------------------------------------------------
Section "NvColorProfiles" SecMain
  SectionIn RO

  ; close a running instance first, else the locked EXE can't be overwritten
  ; (the app is a tray program and may already be running, incl. a portable copy)
  nsExec::Exec 'taskkill /F /IM "${APP_EXE}"'
  Pop $0
  Sleep 800

  SetOutPath "$INSTDIR"
  File "${APP_EXE}"
  File "NOTICE"
  File "COPYING"
  File "COPYING.LESSER"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\$(SC_UNINSTALL).lnk" "$INSTDIR\uninstall.exe"

  ${If} $DesktopShortcut == ${BST_CHECKED}
    CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  ${EndIf}

  ; Autostart via the same per-user Run value the app's own settings toggle manages,
  ; so the in-app checkbox stays in sync.
  ${If} $AutostartEnabled == ${BST_CHECKED}
    WriteRegStr HKCU "${RUN_REGKEY}" "${RUN_VALUE}" '"$INSTDIR\${APP_EXE}"'
  ${EndIf}

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKCU "${APP_REGKEY}" "InstallDir" "$INSTDIR"

  WriteRegStr   HKCU "${UNINST_REGKEY}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKCU "${UNINST_REGKEY}" "DisplayVersion"  "${AppVersionClean}"
  WriteRegStr   HKCU "${UNINST_REGKEY}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr   HKCU "${UNINST_REGKEY}" "DisplayIcon"     "$INSTDIR\${APP_EXE},0"
  WriteRegStr   HKCU "${UNINST_REGKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${UNINST_REGKEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr   HKCU "${UNINST_REGKEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_REGKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_REGKEY}" "NoRepair" 1
SectionEnd

Function LaunchApp
  ExecShell "" "$INSTDIR\${APP_EXE}"
FunctionEnd

;------------------------------------------------------------------------------
; Uninstall
;------------------------------------------------------------------------------
Function un.OptionsPage
  !insertmacro MUI_HEADER_TEXT "$(OPT_TITLE)" "$(UN_SUBTITLE)"
  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}
  ${NSD_CreateLabel} 0 0 100% 24u "$(UN_LABEL)"
  Pop $1
  ${NSD_CreateCheckbox} 0 32u 100% 24u "$(UN_WIPE)"
  Pop $UnWipeCheckbox
  ; default OFF: keep the user's profiles/settings unless they opt in to delete them
  nsDialogs::Show
FunctionEnd

Function un.OptionsPageLeave
  ${NSD_GetState} $UnWipeCheckbox $UnWipeData
FunctionEnd

Section "Uninstall"
  ; stop a running instance so its files/folder can be removed
  nsExec::Exec 'taskkill /F /IM "${APP_EXE}"'
  Pop $0
  Sleep 800

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\NOTICE"
  Delete "$INSTDIR\COPYING"
  Delete "$INSTDIR\COPYING.LESSER"
  ; remove the whole Start-Menu folder so both shortcuts go regardless of their language
  RMDir /r "$SMPROGRAMS\${APP_NAME}"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; only drop the Run value if it points at THIS install (don't clobber a portable autostart)
  ReadRegStr $0 HKCU "${RUN_REGKEY}" "${RUN_VALUE}"
  ${If} $0 == '"$INSTDIR\${APP_EXE}"'
    DeleteRegValue HKCU "${RUN_REGKEY}" "${RUN_VALUE}"
  ${EndIf}

  DeleteRegKey HKCU "${UNINST_REGKEY}"
  DeleteRegKey HKCU "${APP_REGKEY}"

  ; config/profiles/logs live in %APPDATA%\NvColorProfiles (Roaming) — KEPT by default so a
  ; later reinstall finds them again; only removed if the user opted in on the options page.
  ${If} $UnWipeData == ${BST_CHECKED}
    RMDir /r "$APPDATA\NvColorProfiles"
  ${EndIf}

  ; a running uninstaller can't delete its own folder; a hidden cmd cleans up after exit,
  ; including the now-empty %LOCALAPPDATA%\NvColorProfiles parent (leaves nothing behind).
  ExecShell "" "$SYSDIR\cmd.exe" '/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q $\"$INSTDIR$\" & rmdir /q $\"$LOCALAPPDATA\NvColorProfiles$\"' SW_HIDE
  Delete /REBOOTOK "$INSTDIR\uninstall.exe"
  RMDir /REBOOTOK "$INSTDIR"
SectionEnd
