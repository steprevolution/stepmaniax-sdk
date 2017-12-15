// This implements the public API.

#include <windows.h>
#include <memory>

#include "../SMX.h"
#include "SMXManager.h"
#include "SMXDevice.h"
using namespace std;
using namespace SMX;

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch(ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

static shared_ptr<SMXManager> g_pSMX;

// DLL interface:
SMX_API void SMX_Start(SMXUpdateCallback callback, void *pUser)
{
    if(g_pSMX != NULL)
        return;

    // The C++ interface takes a std::function, which doesn't need a user pointer.  We add
    // one for the C interface for convenience.
    auto UpdateCallback = [callback, pUser](int pad, SMXUpdateCallbackReason reason) {
        callback(pad, reason, pUser);
    };

    // Log(ssprintf("Struct sizes (native): %i %i %i\n", sizeof(SMXConfig), sizeof(SMXInfo), sizeof(SMXSensorTestModeData)));
    g_pSMX = make_shared<SMXManager>(UpdateCallback);
}

SMX_API void SMX_Stop()
{
    g_pSMX.reset();
}

SMX_API void SMX_SetLogCallback(SMXLogCallback callback)
{
    // Wrap the C callback with a C++ one.
    SMX::SetLogCallback([callback](const string &log) {
        callback(log.c_str());
    });
}

SMX_API void SMX_GetConfig(int pad, SMXConfig *config) { g_pSMX->GetDevice(pad)->GetConfig(*config); }
SMX_API void SMX_SetConfig(int pad, const SMXConfig *config) { g_pSMX->GetDevice(pad)->SetConfig(*config); }
SMX_API void SMX_GetInfo(int pad, SMXInfo *info) { g_pSMX->GetDevice(pad)->GetInfo(*info); }
SMX_API uint16_t SMX_GetInputState(int pad) { return g_pSMX->GetDevice(pad)->GetInputState(); }
SMX_API void SMX_FactoryReset(int pad) { g_pSMX->GetDevice(pad)->FactoryReset(); }
SMX_API void SMX_ForceRecalibration(int pad) { g_pSMX->GetDevice(pad)->ForceRecalibration(); }
SMX_API void SMX_SetTestMode(int pad, SensorTestMode mode) { g_pSMX->GetDevice(pad)->SetSensorTestMode((SensorTestMode) mode); }
SMX_API bool SMX_GetTestData(int pad, SMXSensorTestModeData *data) { return g_pSMX->GetDevice(pad)->GetTestData(*data); }
SMX_API void SMX_SetLights(const char lightsData[864]) { g_pSMX->SetLights(string(lightsData, 864)); }
SMX_API void SMX_ReenableAutoLights() { g_pSMX->ReenableAutoLights(); }
