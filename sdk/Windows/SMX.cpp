// This implements the public API.

#include <windows.h>
#include <memory>

#include "../SMX.h"
#include "SMXManager.h"
#include "SMXDevice.h"
#include "SMXBuildVersion.h"
#include "SMXPanelAnimation.h" // for SMX_LightsAnimation_SetAuto
using namespace std;
using namespace SMX;

#include <dbghelp.h>
#include <shlobj_core.h>

#pragma comment(lib, "dbghelp.lib")

long __stdcall GenerateDump(EXCEPTION_POINTERS *pExceptionPointers)
{
    SYSTEMTIME time;
    GetLocalTime(&time);

    wchar_t desktopPath[MAX_PATH+1];
    SHGetFolderPathW(HWND_DESKTOP, CSIDL_DESKTOP, NULL, SHGFP_TYPE_CURRENT, desktopPath);

    WCHAR filename[MAX_PATH]; 
    wsprintf(filename, L"%s/StepManiaX-%04d-%02d-%02d-%02d%02d%02d.dmp", 
        desktopPath, time.wYear, time.wMonth, time.wDay, time.wHour, time.wMinute, time.wSecond);

    HANDLE file = CreateFile(filename, GENERIC_READ|GENERIC_WRITE, FILE_SHARE_WRITE|FILE_SHARE_READ, 0, CREATE_ALWAYS, 0, 0);

    MINIDUMP_EXCEPTION_INFORMATION ExpParam;
    ExpParam.ThreadId = GetCurrentThreadId();
    ExpParam.ExceptionPointers = pExceptionPointers;
    ExpParam.ClientPointers = true;

    MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(), file, MiniDumpWithDataSegs, &ExpParam, NULL, NULL);

    return EXCEPTION_EXECUTE_HANDLER;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch(ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        SetUnhandledExceptionFilter(GenerateDump);
        break;

    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

// DLL interface:
SMX_API void SMX_Start(SMXUpdateCallback callback, void *pUser)
{
    if(SMXManager::g_pSMX != NULL)
        return;

    // The C++ interface takes a std::function, which doesn't need a user pointer.  We add
    // one for the C interface for convenience.
    auto UpdateCallback = [callback, pUser](int pad, SMXUpdateCallbackReason reason) {
        callback(pad, reason, pUser);
    };

    // Log(ssprintf("Struct sizes (native): %i %i %i\n", sizeof(SMXConfig), sizeof(SMXInfo), sizeof(SMXSensorTestModeData)));
    SMXManager::g_pSMX = make_shared<SMXManager>(UpdateCallback);
}

SMX_API void SMX_Stop()
{
    // If lights animation is running, shut it down first.
    SMX_LightsAnimation_SetAuto(false);

    SMXManager::g_pSMX.reset();
}

SMX_API void SMX_SetLogCallback(SMXLogCallback callback)
{
    // Wrap the C callback with a C++ one.
    SMX::SetLogCallback([callback](const string &log) {
        callback(log.c_str());
    });
}

SMX_API bool SMX_GetConfig(int pad, SMXConfig *config) { return SMXManager::g_pSMX->GetDevice(pad)->GetConfig(*config); }
SMX_API void SMX_SetConfig(int pad, const SMXConfig *config) { SMXManager::g_pSMX->GetDevice(pad)->SetConfig(*config); }
SMX_API void SMX_GetInfo(int pad, SMXInfo *info) { SMXManager::g_pSMX->GetDevice(pad)->GetInfo(*info); }
SMX_API uint16_t SMX_GetInputState(int pad) { return SMXManager::g_pSMX->GetDevice(pad)->GetInputState(); }
SMX_API void SMX_FactoryReset(int pad) { SMXManager::g_pSMX->GetDevice(pad)->FactoryReset(); }
SMX_API void SMX_ForceRecalibration(int pad) { SMXManager::g_pSMX->GetDevice(pad)->ForceRecalibration(); }
SMX_API void SMX_SetTestMode(int pad, SensorTestMode mode) { SMXManager::g_pSMX->GetDevice(pad)->SetSensorTestMode(mode); }
SMX_API bool SMX_GetTestData(int pad, SMXSensorTestModeData *data) { return SMXManager::g_pSMX->GetDevice(pad)->GetTestData(*data); }
SMX_API void SMX_SetPanelTestMode(PanelTestMode mode) { SMXManager::g_pSMX->SetPanelTestMode(mode); }

SMX_API void SMX_SetLights(const char lightData[864])
{
    SMX_SetLights2(lightData, 864);
}
SMX_API void SMX_SetLights2(const char *lightData, int lightDataSize)
{
    // The lightData into data per pad depending on whether we've been
    // given 16 or 25 lights of data.
    string lights[2];
    const int BytesPerPad16 = 9*16*3;
    const int BytesPerPad25 = 9*25*3;
    if(lightDataSize == 2*BytesPerPad16)
    {
        lights[0] = string(lightData, BytesPerPad16);
        lights[1] = string(lightData + BytesPerPad16, BytesPerPad16);
    }
    else if(lightDataSize == 2*BytesPerPad25)
    {
        lights[0] = string(lightData, BytesPerPad25);
        lights[1] = string(lightData + BytesPerPad25, BytesPerPad25);
    }
    else
    {
        Log(ssprintf("SMX_SetLights2: lightDataSize is invalid (must be %i or %i)\n",
            2*BytesPerPad16, 2*BytesPerPad25));
        return;
    }

    SMXManager::g_pSMX->SetLights(lights);

    // If we're running auto animations, stop them when we get an API call to set lights.
    SMXAutoPanelAnimations::TemporaryStopAnimating();
}

// This is internal for SMXConfig.  These lights aren't meant to be animated.
SMX_API void SMX_SetPlatformLights(const char lightData[88*3], int lightDataSize)
{
    if(lightDataSize != 88*3)
    {
        Log(ssprintf("SMX_SetPlatformLights: lightDataSize is invalid (must be %i)\n",
            88*3));
        return;
    }

    string lights[2];
    lights[0] = string(lightData, 44*3);
    lights[1] = string(lightData + 44*3, 44*3);
    SMXManager::g_pSMX->SetPlatformLights(lights);
}

SMX_API void SMX_ReenableAutoLights() { SMXManager::g_pSMX->ReenableAutoLights(); }
SMX_API const char *SMX_Version() { return SMX_BUILD_VERSION; }

// These aren't exposed in the public API, since they're only used internally.
SMX_API void SMX_SetOnlySendLightsOnChange(bool value) { SMXManager::g_pSMX->SetOnlySendLightsOnChange(value); }
SMX_API void SMX_SetSerialNumbers() { SMXManager::g_pSMX->SetSerialNumbers(); }
