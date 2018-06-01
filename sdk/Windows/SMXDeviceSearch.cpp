#include "SMXDeviceSearch.h"

#include "SMXDeviceConnection.h"
#include "Helpers.h"

#include <string>
#include <memory>
#include <set>
using namespace std;
using namespace SMX;

#include <hidsdi.h>
#include <SetupAPI.h>

// Return all USB HID device paths.  This doesn't open the device to filter just our devices.
static set<wstring> GetAllHIDDevicePaths(wstring &error)
{
    HDEVINFO DeviceInfoSet = NULL;

    GUID HidGuid;
    HidD_GetHidGuid(&HidGuid);
    DeviceInfoSet = SetupDiGetClassDevs(&HidGuid, NULL, NULL, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
    if(DeviceInfoSet == NULL)
        return {};

    set<wstring> paths;
    SP_DEVICE_INTERFACE_DATA DeviceInterfaceData;
    DeviceInterfaceData.cbSize = sizeof(SP_DEVICE_INTERFACE_DATA);
    for(DWORD iIndex = 0;
        SetupDiEnumDeviceInterfaces(DeviceInfoSet, NULL, &HidGuid, iIndex, &DeviceInterfaceData);
        iIndex++)
    {
        DWORD iSize;
        if(!SetupDiGetDeviceInterfaceDetail(DeviceInfoSet, &DeviceInterfaceData, NULL, 0, &iSize, NULL))
        {
            // This call normally fails with ERROR_INSUFFICIENT_BUFFER.
            int iError = GetLastError();
            if(iError != ERROR_INSUFFICIENT_BUFFER)
            {
		Log(ssprintf("SetupDiGetDeviceInterfaceDetail failed: %ls", GetErrorString(iError).c_str()));
                continue;
            }
        }

        PSP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData = (PSP_DEVICE_INTERFACE_DETAIL_DATA) alloca(iSize);
        DeviceInterfaceDetailData->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA);

        SP_DEVINFO_DATA DeviceInfoData;
        ZeroMemory(&DeviceInfoData, sizeof(SP_DEVINFO_DATA));
        DeviceInfoData.cbSize = sizeof(SP_DEVINFO_DATA);
        if(!SetupDiGetDeviceInterfaceDetail(DeviceInfoSet, &DeviceInterfaceData, DeviceInterfaceDetailData, iSize, NULL, &DeviceInfoData))
        {
            Log(ssprintf("SetupDiGetDeviceInterfaceDetail failed: %ls", GetErrorString(GetLastError()).c_str()));
            continue;
        }

        paths.insert(DeviceInterfaceDetailData->DevicePath);
    }

    SetupDiDestroyDeviceInfoList(DeviceInfoSet);

    return paths;
}

static shared_ptr<AutoCloseHandle> OpenUSBDevice(LPCTSTR DevicePath, wstring &error)
{
    // Log(ssprintf("Opening device: %ls", DevicePath));
    HANDLE OpenDevice = CreateFile(
        DevicePath,
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, NULL
    );

    if(OpenDevice == INVALID_HANDLE_VALUE)
    {
	// Many unrelated devices will fail to open, so don't return this as an error.
        Log(ssprintf("Error opening device %ls: %ls", DevicePath, GetErrorString(GetLastError()).c_str()));
        return nullptr;
    }

    auto result = make_shared<AutoCloseHandle>(OpenDevice);

    // Get the HID attributes to check the IDs.
    HIDD_ATTRIBUTES HidAttributes;
    HidAttributes.Size = sizeof(HidAttributes);
    if(!HidD_GetAttributes(result->value(), &HidAttributes))
    {
        Log(ssprintf("Error opening device %ls: HidD_GetAttributes failed", DevicePath));
        error = L"HidD_GetAttributes failed";
        return nullptr;
    }

    if(HidAttributes.VendorID != 0x2341 || HidAttributes.ProductID != 0x8037)
    {
        Log(ssprintf("Device %ls: not our device (ID %04x:%04x)", DevicePath, HidAttributes.VendorID, HidAttributes.ProductID));
        return nullptr;
    }

    // Since we're using the default Arduino IDs, check the product name to make sure
    // this isn't some other Arduino device.
    WCHAR ProductName[255];
    ZeroMemory(ProductName, sizeof(ProductName));
    if(!HidD_GetProductString(result->value(), ProductName, 255))
    {
        Log(ssprintf("Error opening device %ls: HidD_GetProductString failed", DevicePath));
        return nullptr;
    }

    if(wstring(ProductName) != L"StepManiaX")
    {
        Log(ssprintf("Device %ls: not our device (%ls)", DevicePath, ProductName));
        return nullptr;
    }

    return result;
}

vector<shared_ptr<AutoCloseHandle>> SMX::SMXDeviceSearch::GetDevices(wstring &error)
{
    set<wstring> aDevicePaths = GetAllHIDDevicePaths(error);

    // Remove any entries in m_Devices that are no longer in the list.
    for(wstring sPath: m_setLastDevicePaths)
    {
        if(aDevicePaths.find(sPath) != aDevicePaths.end())
            continue;

        Log(ssprintf("Device removed: %ls", sPath.c_str()));
        m_Devices.erase(sPath);
    }

    // Check for new entries.
    for(wstring sPath: aDevicePaths)
    {
        // Only look at devices that weren't in the list last time.  OpenUSBDevice has
        // to open the device and causes requests to be sent to it.
        if(m_setLastDevicePaths.find(sPath) != m_setLastDevicePaths.end())
            continue;

        // This will return NULL if this isn't our device.
        shared_ptr<AutoCloseHandle> hDevice = OpenUSBDevice(sPath.c_str(), error);
        if(hDevice == nullptr)
            continue;

        Log(ssprintf("Device added: %ls", sPath.c_str()));
        m_Devices[sPath] = hDevice;
    }

    m_setLastDevicePaths = aDevicePaths;

    vector<shared_ptr<AutoCloseHandle>> aDevices;
    for(auto it: m_Devices)
        aDevices.push_back(it.second);

    return aDevices;
}

void SMX::SMXDeviceSearch::DeviceWasClosed(shared_ptr<AutoCloseHandle> pDevice)
{
    map<wstring, shared_ptr<AutoCloseHandle>> aDevices;
    for(auto it: m_Devices)
    {
        if(it.second == pDevice)
        {
            m_setLastDevicePaths.erase(it.first);
        }
        else
        {
            aDevices[it.first] = it.second;
        }
    }
    m_Devices = aDevices;
}
