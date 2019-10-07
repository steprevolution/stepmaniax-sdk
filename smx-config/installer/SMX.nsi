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

# Global variables and lots of gotos.  That's all NSIS can do.  I feel like I'm 8 again, writing in BASIC.
# Also, we need to use some macro nastiness to convince NSIS to include this in both the installer and uninstaller.
Var /Global "ShutdownRetries"
!macro myfunc un
Function ${un}CheckRunning
    # Reset ShutdownRetries.
    StrCpy $ShutdownRetries "0"

retry:
    # Check if SMXConfig is running.  For now we use SMXConfigEvent for this, which is used to foreground
    # the application, since it's been around for a while.  SMXConfigShutdown is only available in newer
    # versions.
    System::Call 'kernel32::OpenEventW(i 0x00100000, b 0, w "SMXConfigEvent") i .R0'
    IntCmp $R0 0 done
    System::Call 'kernel32::CloseHandle(i $R0)'

try_to_shut_down:
    IntOp $ShutdownRetries $ShutdownRetries + 1
    IntCmp $ShutdownRetries 10 failed_to_shut_down

    # SMXConfig is running.  See if SMXConfigShutdown is available, to let us exit it automatically.
    System::Call 'kernel32::OpenEventW(i 0x00100000|0x0002, b 0, w "SMXConfigShutdown") i .R0'
    IntCmp $R0 0 CantShutdownAutomatically

    # We have the shutdown handle.  Signal it to tell SMXConfig to exit.
    System::Call 'kernel32::SetEvent(i $R0) i .R0'
    System::Call 'kernel32::CloseHandle(i $R0)'

    # Wait briefly to give it a chance to shut down, then loop and check that it's shut down.  If it isn't,
    # we'll retry a few times.
    Sleep 100    
    goto retry
#    goto done

#    System::Call "Kernel32::GetLastError() i() .r1"

failed_to_shut_down:
    StrCpy $ShutdownRetries "0"
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Please close SMXConfig before updating." /SD IDCANCEL IDRETRY retry IDCANCEL cancel
    Quit

CantShutdownAutomatically:
    # SMXConfig is running, but it's an older version that doesn't have a shutdown signal.  Ask the
    # user to shut it down.  Retry will restart and check if it's not running.
    MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Please close SMXConfig." /SD IDCANCEL IDRETRY retry IDCANCEL cancel
    Quit

cancel:
    Quit
done:
FunctionEnd
!macroend
 
!insertmacro myfunc ""
!insertmacro myfunc "un."

Page custom CheckRunning
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

#Section "un.CheckRunning"
#SectionEnd

Section "Uninstall"
    # Make sure SMXConfig isn't running.
    Call un.CheckRunning
    Delete $INSTDIR\SMX.dll
    Delete $INSTDIR\SMXConfig.exe
    Delete $INSTDIR\uninstall.exe
    rmdir $INSTDIR
    Delete "$SMPROGRAMS\StepManiaX Platform.lnk"
    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StepManiaX Platform"
SectionEnd

