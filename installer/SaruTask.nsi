Unicode true
!include "MUI2.nsh"
!ifndef PRODUCT_VERSION
  !error "PRODUCT_VERSION is required"
!endif
Name "SaruTask ${PRODUCT_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\SaruTask"
InstallDirRegKey HKCU "Software\SaruTask" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "SaruTask"
Icon "${ICON_FILE}"
UninstallIcon "${ICON_FILE}"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey "ProductName" "SaruTask"
VIAddVersionKey "FileDescription" "SaruTask Setup"
VIAddVersionKey "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey "LegalCopyright" "saru2017"

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\SaruTask.exe"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"
!insertmacro MUI_LANGUAGE "English"

!macro CheckResident PREFIX
Function ${PREFIX}.onInit
retry:
  System::Call 'kernel32::OpenMutexW(i 0x100000, i 0, w "Local\SaruTask.Application") p.r0'
  IntCmp $0 0 done
  System::Call 'kernel32::CloseHandle(p r0)'
  MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "SaruTaskが起動中です。トレイアイコンを右クリックして「終了」し、再試行してください。" /SD IDCANCEL IDRETRY retry
  Abort
done:
FunctionEnd
!macroend
!insertmacro CheckResident ""
!insertmacro CheckResident "un"

Section "SaruTask" SEC_APP
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File "${PAYLOAD_DIR}/SaruTask.exe"
  File "${PAYLOAD_DIR}/README.md"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\SaruTask"
  CreateShortcut "$SMPROGRAMS\SaruTask\SaruTask.lnk" "$INSTDIR\SaruTask.exe"
  WriteRegStr HKCU "Software\SaruTask" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "DisplayName" "SaruTask"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "DisplayIcon" "$INSTDIR\SaruTask.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "URLInfoAbout" "https://github.com/sarulab-ou/SaruTask"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask" "NoRepair" 1
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  Delete "$INSTDIR\SaruTask.exe"
  Delete "$INSTDIR\SaruTask.exe.previous"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$SMPROGRAMS\SaruTask\SaruTask.lnk"
  RMDir "$SMPROGRAMS\SaruTask"
  ReadRegStr $0 HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SaruTask"
  StrCmp $0 '$\"$INSTDIR\SaruTask.exe$\" --background' 0 +2
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "SaruTask"
  DeleteRegKey HKCU "Software\SaruTask"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\SaruTask"
  ; Never delete settings, task data, or an installation directory recursively.
  RMDir "$INSTDIR"
SectionEnd
