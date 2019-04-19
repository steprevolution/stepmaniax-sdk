#ifndef SMXDevice_h
#define SMXDevice_h

#include <windows.h>
#include <memory>
#include <functional>
using namespace std;

#include "Helpers.h"
#include "../SMX.h"

namespace SMX
{
class SMXDeviceConnection;

// The high-level interface to a single controller.  This is managed by SMXManager, and uses SMXDeviceConnection
// for low-level USB communication.
class SMXDevice
{
public:
    // Create an SMXDevice.
    //
    // lock is our serialization mutex.  This is shared across SMXManager and all SMXDevices.
    //
    // hEvent is signalled when we have new packets to be sent, to wake the communications thread.  The
    // device handle opened with OpenPort must also be monitored, to check when packets have been received
    // (or successfully sent).
    static shared_ptr<SMXDevice> Create(shared_ptr<SMX::AutoCloseHandle> hEvent, SMX::Mutex &lock);
    SMXDevice(shared_ptr<SMXDevice> &pSelf, shared_ptr<SMX::AutoCloseHandle> hEvent, SMX::Mutex &lock);
    ~SMXDevice();

    bool OpenDeviceHandle(shared_ptr<SMX::AutoCloseHandle> pHandle, wstring &sError);
    void CloseDevice();
    shared_ptr<SMX::AutoCloseHandle> GetDeviceHandle() const;

    // Set a function to be called when something changes on the device.  This allows efficiently
    // detecting when a panel is pressed or other changes happen on the device.
    void SetUpdateCallback(function<void(int PadNumber, SMXUpdateCallbackReason reason)> pCallback);

    // Return true if we're connected.
    bool IsConnected() const;

    // Send a raw command.
    void SendCommand(string sCmd, function<void(string response)> pComplete=nullptr);
    void SendCommandLocked(string sCmd, function<void(string response)> pComplete=nullptr);

    // Get basic info about the device.
    void GetInfo(SMXInfo &info);
    void GetInfoLocked(SMXInfo &info); // used by SMXManager

    // Return true if this device is configured as player 2.
    bool IsPlayer2Locked() const; // used by SMXManager

    // Get the configuration of the connected device (or the most recently read configuration if
    // we're not connected).
    bool GetConfig(SMXConfig &configOut);
    bool GetConfigLocked(SMXConfig &configOut);

    // Set the configuration of the connected device.
    //
    // This is asynchronous and returns immediately.
    void SetConfig(const SMXConfig &newConfig);

    // Return a mask of the panels currently pressed.
    uint16_t GetInputState() const;

    // Reset the configuration data to what the device used when it was first flashed.
    // GetConfig() will continue to return the previous configuration until this command
    // completes, which is signalled by a SMXUpdateCallback_FactoryResetCommandComplete callback.
    void FactoryReset();

    // Force immediate fast recalibration.  This is the same calibration that happens at
    // boot.  This is only used for diagnostics, and the panels will normally auto-calibrate
    // on their own.
    void ForceRecalibration();

    // Set the test mode of the connected device.
    //
    // This is asynchronous and returns immediately.
    void SetSensorTestMode(SensorTestMode mode);

    // Return the most recent test data we've received from the pad.  Return false if we haven't
    // received test data since changing the test mode (or if we're not in a test mode).
    bool GetTestData(SMXSensorTestModeData &data);

    // Internal:

    // Update this device, processing received packets and sending any outbound packets.
    // m_Lock must be held.
    //
    // sError will be set on a communications error.  The owner must close the device.
    void Update(wstring &sError);

private:
    shared_ptr<SMX::AutoCloseHandle> m_hEvent;
    SMX::Mutex &m_Lock;

    function<void(int PadNumber, SMXUpdateCallbackReason reason)> m_pUpdateCallback;
    weak_ptr<SMXDevice> m_pSelf;

    shared_ptr<SMXDeviceConnection> m_pConnection;

    // The configuration we've read from the device.  m_bHaveConfig is true if we've received
    // a configuration from the device since we've connected to it.
    SMXConfig config;
    vector<uint8_t> rawConfig;
    bool m_bHaveConfig = false;

    // This is the configuration the user has set, if he's changed anything.  We send this to
    // the device if m_bSendConfig is true.  Once we send it once, m_bSendConfig is cleared, and
    // if we see a different configuration from the device again we won't re-send this.
    SMXConfig wanted_config;
    bool m_bSendConfig = false;
    bool m_bSendingConfig = false;
    bool m_bWaitingForConfigResponse = false;

    void CallUpdateCallback(SMXUpdateCallbackReason reason);
    void HandlePackets();

    void SendConfig();
    void CheckActive();
    bool IsConnectedLocked() const;

    // Test/diagnostics mode handling.
    void UpdateSensorTestMode();
    void HandleSensorTestDataResponse(const string &sReadBuffer);
    SensorTestMode m_WaitingForSensorTestModeResponse = SensorTestMode_Off;
    SensorTestMode m_SensorTestMode = SensorTestMode_Off;
    bool m_HaveSensorTestModeData = false;
    SMXSensorTestModeData m_SensorTestData;
    uint32_t m_SentSensorTestModeRequestAtTicks = 0;
};
}

#endif
