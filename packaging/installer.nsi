; Windows installer for Karaoke Shuffler (built with NSIS).
; Installs for the current user only, so no admin rights are needed.
; The app is self-contained: .NET is bundled, nothing else has to be installed.
;
; Build:  makensis -DVERSION=1.0.0 -DSRCDIR=<published win-x64 folder> -DOUTFILE=<setup.exe> installer.nsi

!include "MUI2.nsh"

!define APPNAME "Karaoke Shuffler"
!define EXENAME "Singer_Selector.exe"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\KaraokeShuffler"

Name "${APPNAME}"
OutFile "${OUTFILE}"
Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user                          ; per-user install, no UAC prompt
InstallDir "$LOCALAPPDATA\Programs\${APPNAME}"
InstallDirRegKey HKCU "Software\KaraokeShuffler" "InstallDir"

VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

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
    SetOutPath "$INSTDIR"
    File /r "${SRCDIR}\*.*"

    ; Start menu shortcut
    CreateShortcut "$SMPROGRAMS\${APPNAME}.lnk" "$INSTDIR\${EXENAME}"

    ; uninstaller + entry in "Apps & features"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "Software\KaraokeShuffler" "InstallDir" "$INSTDIR"
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
    ; removes the program only; the singer list and colours in %AppData%\Singer_Selector are kept
    Delete "$SMPROGRAMS\${APPNAME}.lnk"
    Delete "$DESKTOP\${APPNAME}.lnk"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKCU "${UNINSTKEY}"
    DeleteRegKey HKCU "Software\KaraokeShuffler"
SectionEnd
