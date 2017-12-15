!include "MUI2.nsh"

Name "StepManiaX Platform"
OutFile "SMXConfigInstaller.exe"

!define MUI_ICON "..\window icon.ico"

InstallDir "$PROGRAMFILES32\SMXConfig"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
;!insertmacro MUI_PAGE_DIRECTORY

Function InstallNetRuntime
    # Check if .NET 4.5.2 is installed.  https://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx
    Var /Global Net4Version
    ReadRegDWORD $Net4Version HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
    IntCmp $Net4Version 379893 already_installed do_install already_installed
 
    already_installed:
        DetailPrint ".NET runtime 4.5.2 already installed."
        return

    do_install:

    # Download the runtime.
    NSISdl::download "https://go.microsoft.com/fwlink/?LinkId=397708" "$TEMP\NET452Installer.exe"
    Var /GLOBAL download_result
    Pop $download_result
    DetailPrint "$download_result"
    StrCmp $download_result success download_successful

    MessageBox MB_OK|MB_ICONEXCLAMATION "The .NET 4.5.2 runtime couldn't be downloaded."
    return

    download_successful:

    # Run the installer.
    # We can run this without opening the install dialog like this, but this runtime can take a
    # while to install and it makes it look like the installation has stalled.
    # ExecWait '"$TEMP\NET452Installer.exe" /q /norestart /c:"install /q"'
    ExecWait '"$TEMP\NET452Installer.exe" /passive /norestart /c:"install"'
FunctionEnd

Function InstallMSVCRuntime
    # Check if the runtime is already installed.
    Var /Global MSVCVersion1
    Var /Global MSVCVersion2
    GetDLLVersion "$sysdir\MSVCP140.dll" $MSVCVersion1 $MSVCVersion2
    StrCmp $MSVCVersion1 "" do_install
 
    DetailPrint "MSVC runtime already installed."
    return

    do_install:

    DetailPrint "Installing MSVC runtime"

    # Download the runtime.
    NSISdl::download "https://go.microsoft.com/fwlink/?LinkId=615459" "$TEMP\vcredist_x86.exe"
    Var /GLOBAL download_result_2
    Pop $download_result_2
    DetailPrint "$download_result_2"
    StrCmp $download_result_2 success download_successful

    MessageBox MB_OK|MB_ICONEXCLAMATION "The MSVC runtime couldn't be downloaded."
    return

    download_successful:

    # Run the installer.
    ExecWait '"$TEMP\vcredist_x86.exe" /passive /norestart'
FunctionEnd

Page directory
Page instfiles

Section
    Call InstallNetRuntime
    Call InstallMSVCRuntime

    SetOutPath $INSTDIR
    File "..\..\out\SMX.dll"
    File "..\..\out\SMXConfig.exe"

    CreateShortCut "$SMPROGRAMS\StepManiaX Platform.lnk" "$INSTDIR\SMXConfig.exe"
    WriteUninstaller $INSTDIR\uninstall.exe

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform" \
                     "DisplayName" "StepManiaX Platform"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform" \
                     "Publisher" "Step Revolution"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform" \
                     "DisplayIcon" "$INSTDIR\SMXConfig.exe"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform" \
                     "UninstallString" "$\"$INSTDIR\uninstall.exe$\""
SectionEnd

Section "Uninstall"
    Delete $INSTDIR\SMX.dll
    Delete $INSTDIR\SMXConfig.exe
    Delete $INSTDIR\uninstall.exe
    rmdir $INSTDIR
    Delete "$SMPROGRAMS\StepManiaX Platform.lnk"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform"
SectionEnd

