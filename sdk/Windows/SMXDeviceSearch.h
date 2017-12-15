#ifndef SMXDeviceSearch_h
#define SMXDeviceSearch_h

#include <memory>
#include <string>
#include <vector>
#include <set>
#include <map>
using namespace std;

#include "Helpers.h"

namespace SMX {

class SMXDeviceSearch
{
public:
    // Return a list of connected devices.  If the same device stays connected and this
    // is called multiple times, the same handle will be returned.
    vector<shared_ptr<AutoCloseHandle>> GetDevices(wstring &error);

    // After a device is opened and then closed, tell this class that the device was closed.
    // We'll discard our record of it, so we'll notice a new device plugged in on the same
    // path.
    void DeviceWasClosed(shared_ptr<AutoCloseHandle> pDevice);

private:
    set<wstring> m_setLastDevicePaths;
    map<wstring, shared_ptr<AutoCloseHandle>> m_Devices;
};
}

#endif
