; Windows installer for KaraoQueue (built with NSIS).
; Installs for the current user only, so no admin rights are needed.
; The app is self-contained: .NET is bundled, nothing else has to be installed.
;
; Build:  makensis -DVERSION=1.0.0 -DSRCDIR=<published win-x64 folder> -DOUTFILE=<setup.exe> installer.nsi

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define APPNAME "KaraoQueue"
!define EXENAME "KaraoQueue.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KaraoQueue"

Name "${APPNAME}"
OutFile "${OUTFILE}"
Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user                          ; per-user install, no UAC prompt
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "Software\KaraoQueue" "InstallDir"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Shehryar Usman, Apache License 2.0"

!define MUI_ICON "icon.ico"
!define MUI_UNICON "icon.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "Start ${APPNAME}"

; installer pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; uninstaller pages
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "${APPNAME}" SecApp
    SectionIn RO                                    ; required

    ; version 1.0.0 was called "Karaoke Shuffler": uninstall it first so there aren't two copies
    ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\KaraokeShuffler" "InstallLocation"
    ${If} $0 != ""
    ${AndIf} ${FileExists} "$0\Uninstall.exe"
        ExecWait '"$0\Uninstall.exe" /S _?=$0'     ; _?= makes it wait until the old uninstall is done
        Delete "$0\Uninstall.exe"
        RMDir "$0"
    ${EndIf}
    SetOutPath "$INSTDIR"
    File /r "${SRCDIR}\*.*"

    ; Start menu shortcut
    CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

    ; uninstaller + entry in "Apps & features"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\KaraoQueue" "InstallDir" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
    WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME}"
    WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
SectionEnd

Section "Desktop shortcut" SecDesktop
    CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"
SectionEnd

Section "Uninstall"
    ; removes the program only; the singer list and colours in %AppData%\KaraoQueue are kept
    Delete "$SMPROGRAMS\${APPNAME}.lnk"
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKCU "${UNINSTKEY}"
    DeleteRegKey HKCU "Software\KaraoQueue"
SectionEnd
