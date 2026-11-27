Unicode True
Name "NovaLab Deck"
OutFile "..\out\NovaLabDeck-Setup-x64.exe"
InstallDir "$LOCALAPPDATA\Programs\NovaLab Deck"
RequestExecutionLevel user
SetCompressor zlib
Icon "..\Host\deck.ico"
UninstallIcon "..\Host\deck.ico"
!include "MUI2.nsh"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\NovaLabDeck.exe"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "English"
Section "NovaLab Deck"
 SetOutPath "$INSTDIR"
 File "..\out\portable\NovaLabDeck.exe"
 WriteUninstaller "$INSTDIR\Uninstall.exe"
 CreateDirectory "$SMPROGRAMS\NovaLab Deck"
 CreateShortcut "$SMPROGRAMS\NovaLab Deck\NovaLab Deck.lnk" "$INSTDIR\NovaLabDeck.exe"
 CreateShortcut "$DESKTOP\NovaLab Deck.lnk" "$INSTDIR\NovaLabDeck.exe"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaLabDeck" "DisplayName" "NovaLab Deck"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaLabDeck" "DisplayVersion" "0.1.0"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaLabDeck" "Publisher" "NovaLab Studios"
 WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaLabDeck" "UninstallString" '"$INSTDIR\Uninstall.exe"'
SectionEnd
Section "Uninstall"
 Delete "$INSTDIR\NovaLabDeck.exe"
 Delete "$INSTDIR\Uninstall.exe"
 Delete "$SMPROGRAMS\NovaLab Deck\NovaLab Deck.lnk"
 Delete "$DESKTOP\NovaLab Deck.lnk"
 RMDir "$SMPROGRAMS\NovaLab Deck"
 RMDir "$INSTDIR"
 DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaLabDeck"
 DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "NovaLabDeck"
SectionEnd
