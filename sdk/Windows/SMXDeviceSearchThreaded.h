#ifndef SMXDeviceSearchThreaded_h
#define SMXDeviceSearchThreaded_h

#include "Helpers.h"
#include <windows.h>
#include <memory>
#include <vector>
using namespace std;

namespace SMX {

class SMXDeviceSearch;

// This is a wrapper around SMXDeviceSearch which performs USB scanning in a thread.
// It's free on Win10, but takes a while on Windows 7 (about 8ms), so running it on
// a separate thread prevents random timing errors when reading HID updates.
class SMXDeviceSearchThreaded
{
public:
    SMXDeviceSearchThreaded();
    ~SMXDeviceSearchThreaded();

    // The same interface as SMXDeviceSearch:
    vector<shared_ptr<SMX::AutoCloseHandle>> GetDevices();
    void DeviceWasClosed(shared_ptr<SMX::AutoCloseHandle> pDevice);

    // Synchronously shut down the thread.
    void Shutdown();

private:
    void UpdateDeviceList();

    static DWORD WINAPI ThreadMainStart(void *self_);
    void ThreadMain();

    SMX::Mutex m_Lock;
    shared_ptr<SMXDeviceSearch> m_pDeviceList;
    shared_ptr<SMX::AutoCloseHandle> m_hEvent;
    vector<shared_ptr<SMX::AutoCloseHandle>> m_apDevices;
    vector<shared_ptr<SMX::AutoCloseHandle>> m_apClosedDevices;
    bool m_bShutdown = false;
    HANDLE m_hThread = INVALID_HANDLE_VALUE;
};
}

#endif
