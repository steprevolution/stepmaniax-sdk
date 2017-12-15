#include "SMXDeviceSearchThreaded.h"
#include "SMXDeviceSearch.h"
#include "SMXDeviceConnection.h"

#include <windows.h>
#include <memory>
using namespace std;
using namespace SMX;

SMX::SMXDeviceSearchThreaded::SMXDeviceSearchThreaded()
{
    m_hEvent = make_shared<AutoCloseHandle>(CreateEvent(NULL, false, false, NULL));
    m_pDeviceList = make_shared<SMXDeviceSearch>();

    // Start the thread.
    DWORD id;
    m_hThread = CreateThread(NULL, 0, ThreadMainStart, this, 0, &id);
    SMX::SetThreadName(id, "SMXDeviceSearch");
}

SMX::SMXDeviceSearchThreaded::~SMXDeviceSearchThreaded()
{
    // Shut down the thread, if it's still running.
    Shutdown();
}

void SMX::SMXDeviceSearchThreaded::Shutdown()
{
    if(m_hThread == INVALID_HANDLE_VALUE)
        return;

    // Tell the thread to shut down, and wait for it before returning.
    m_bShutdown = true;
    SetEvent(m_hEvent->value());

    WaitForSingleObject(m_hThread, INFINITE);
    m_hThread = INVALID_HANDLE_VALUE;
}

DWORD WINAPI SMX::SMXDeviceSearchThreaded::ThreadMainStart(void *self_)
{
    SMXDeviceSearchThreaded *self = (SMXDeviceSearchThreaded *) self_;
    self->ThreadMain();
    return 0;
}

void SMX::SMXDeviceSearchThreaded::UpdateDeviceList()
{
    m_Lock.AssertNotLockedByCurrentThread();

    // Tell m_pDeviceList about closed devices, so it knows that any device on the
    // same path is new.
    m_Lock.Lock();
    for(auto pDevice: m_apClosedDevices)
        m_pDeviceList->DeviceWasClosed(pDevice);
    m_apClosedDevices.clear();
    m_Lock.Unlock();

    // Get the current device list.
    wstring sError;
    vector<shared_ptr<AutoCloseHandle>> apDevices = m_pDeviceList->GetDevices(sError);
    if(!sError.empty())
    {
        Log(ssprintf("Error listing USB devices: %ls", sError.c_str()));
        return;
    }

    // Update the device list returned by GetDevices.
    m_Lock.Lock();
    m_apDevices = apDevices;
    m_Lock.Unlock();
}

void SMX::SMXDeviceSearchThreaded::ThreadMain()
{
    while(!m_bShutdown)
    {
        UpdateDeviceList();
        WaitForSingleObjectEx(m_hEvent->value(), 250, true);
    }
}

void SMX::SMXDeviceSearchThreaded::DeviceWasClosed(shared_ptr<AutoCloseHandle> pDevice)
{
    // Add pDevice to the list of closed devices.  We'll call m_pDeviceList->DeviceWasClosed
    // on these from the scanning thread.
    m_apClosedDevices.push_back(pDevice);
}

vector<shared_ptr<AutoCloseHandle>> SMX::SMXDeviceSearchThreaded::GetDevices()
{
    // Lock to make a copy of the device list.
    m_Lock.Lock();
    vector<shared_ptr<AutoCloseHandle>> apResult = m_apDevices;
    m_Lock.Unlock();
    return apResult;
}
