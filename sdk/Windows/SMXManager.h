#ifndef SMXManager_h
#define SMXManager_h

#include <windows.h>
#include <memory>
#include <vector>
#include <functional>
using namespace std;

#include "Helpers.h"
#include "../SMX.h"
#include "SMXHelperThread.h"

namespace SMX {
class SMXDevice;
class SMXDeviceSearchThreaded;

struct SMXControllerState
{
    // True 
    bool m_bConnected[2];

    // Pressed panels for player 1 and player 2:
    uint16_t m_Inputs[2];
};

// This implements the main thread that controller communication and device searching
// happens in, finding and opening devices, and running device updates.
//
// Connected controllers can be accessed with GetDevice(), 
// This also abstracts controller numbers.  GetDevice(SMX_PadNumber_1) will return the
// first device that connected, 
class SMXManager
{
public:
    // Our singleton:
    static shared_ptr<SMXManager> g_pSMX;

    // pCallback is a function to be called when something changes on any device.  This allows
    // efficiently detecting when a panel is pressed or other changes happen.
    SMXManager(function<void(int PadNumber, SMXUpdateCallbackReason reason)> pCallback);
    ~SMXManager();

    void Shutdown();
    shared_ptr<SMXDevice> GetDevice(int pad);
    void SetLights(const string sLights[2]);
    void ReenableAutoLights();

    // Run a function in the user callback thread.
    void RunInHelperThread(function<void()> func);

private:
    static DWORD WINAPI ThreadMainStart(void *self_);
    void ThreadMain();
    void AttemptConnections();
    void CorrectDeviceOrder();
    void SendLightUpdates();

    HANDLE m_hThread = INVALID_HANDLE_VALUE;
    shared_ptr<SMX::AutoCloseHandle> m_hEvent;
    shared_ptr<SMXDeviceSearchThreaded> m_pSMXDeviceSearchThreaded;
    bool m_bShutdown = false;
    vector<shared_ptr<SMXDevice>> m_pDevices;

    // We make user callbacks asynchronously in this thread, to avoid any locking or timing
    // issues that could occur by calling them in our I/O thread.
    SMXHelperThread m_UserCallbackThread;

    // A list of queued lights commands to send to the controllers.  This is always sorted
    // by iTimeToSend.
    struct PendingCommand
    {
        PendingCommand(float fTime): fTimeToSend(fTime) { }
        double fTimeToSend = 0;
        string sPadCommand[2];
    };
    vector<PendingCommand> m_aPendingLightsCommands;
    double m_fDelayLightCommandsUntil = 0;
};
}

#endif
