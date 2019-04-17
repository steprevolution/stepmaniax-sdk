#include "SMXDevice.h"

#include "../SMX.h"
#include "Helpers.h"
#include "SMXDeviceConnection.h"
#include "SMXDeviceSearch.h"
#include <windows.h>
#include <memory>
#include <vector>
#include <map>
using namespace std;
using namespace SMX;

// Extract test data for panel iPanel.
static void ReadDataForPanel(const vector<uint16_t> &data, int iPanel, void *pOut, int iOutSize)
{
    int m_iBit = 0;

    uint8_t *p = (uint8_t *) pOut;

    // Read each byte.
    for(int i = 0; i < iOutSize; ++i)
    {
        // Read each bit in this byte.
        uint8_t result = 0;
        for(int j = 0; j < 8; ++j)
        {
            bool bit = false;

            if(m_iBit < data.size())
            {
                bit = data[m_iBit] & (1 << iPanel);
                m_iBit++;
            }

            result |= bit << j;
        }

        *p++ = result;
    }
}


shared_ptr<SMXDevice> SMX::SMXDevice::Create(shared_ptr<AutoCloseHandle> hEvent, Mutex &lock)
{
    return CreateObj<SMXDevice>(hEvent, lock);
}

SMX::SMXDevice::SMXDevice(shared_ptr<SMXDevice> &pSelf, shared_ptr<AutoCloseHandle> hEvent, Mutex &lock):
    m_pSelf(GetPointers(pSelf, this)),
    m_hEvent(hEvent),
    m_Lock(lock)
{
    m_pConnection = SMXDeviceConnection::Create();
}

SMX::SMXDevice::~SMXDevice()
{
}

bool SMX::SMXDevice::OpenDeviceHandle(shared_ptr<AutoCloseHandle> pHandle, wstring &sError)
{
    m_Lock.AssertLockedByCurrentThread();
    return m_pConnection->Open(pHandle, sError);
}

void SMX::SMXDevice::CloseDevice()
{
    m_Lock.AssertLockedByCurrentThread();

    m_pConnection->Close();
    m_bHaveConfig = false;
    m_bSendConfig = false;
    m_bSendingConfig = false;
    m_bWaitingForConfigResponse = false;

    CallUpdateCallback(SMXUpdateCallback_Updated);
}

shared_ptr<AutoCloseHandle> SMX::SMXDevice::GetDeviceHandle() const
{
    return m_pConnection->GetDeviceHandle();
}

void SMX::SMXDevice::SetUpdateCallback(function<void(int PadNumber, SMXUpdateCallbackReason reason)> pCallback)
{
    LockMutex Lock(m_Lock);
    m_pUpdateCallback = pCallback;
}

bool SMX::SMXDevice::IsConnected() const
{
    m_Lock.AssertNotLockedByCurrentThread();

    // Don't expose the device as connected until we've read the current configuration.
    LockMutex Lock(m_Lock);
    return IsConnectedLocked();
}

bool SMX::SMXDevice::IsConnectedLocked() const
{
    m_Lock.AssertLockedByCurrentThread();
    return m_pConnection->IsConnectedWithDeviceInfo() && m_bHaveConfig;
}

void SMX::SMXDevice::SendCommand(string cmd, function<void(string response)> pComplete)
{
    LockMutex Lock(m_Lock);
    SendCommandLocked(cmd, pComplete);
}

void SMX::SMXDevice::SendCommandLocked(string cmd, function<void(string response)> pComplete)
{
    m_Lock.AssertLockedByCurrentThread();

    if(!m_pConnection->IsConnected())
    {
        // If we're not connected, just call pComplete.
        if(pComplete)
            pComplete("");
        return;
    }

    // This call is nonblocking, so it's safe to do this in the UI thread.
    m_pConnection->SendCommand(cmd, pComplete);

    // Wake up the communications thread to send the message.
    if(m_hEvent)
        SetEvent(m_hEvent->value());
}

void SMX::SMXDevice::GetInfo(SMXInfo &info)
{
    LockMutex Lock(m_Lock);
    GetInfoLocked(info);
}

void SMX::SMXDevice::GetInfoLocked(SMXInfo &info)
{
    m_Lock.AssertLockedByCurrentThread();

    info = SMXInfo();
    info.m_bConnected = IsConnectedLocked();
    if(!info.m_bConnected)
        return;

    // Copy fields from the low-level device info to the high-level struct.
    // These are kept separate because the interface depends on the format
    // of SMXInfo, but it doesn't care about anything inside SMXDeviceConnection.
    SMXDeviceInfo deviceInfo = m_pConnection->GetDeviceInfo();
    memcpy(info.m_Serial, deviceInfo.m_Serial, sizeof(info.m_Serial));
    info.m_iFirmwareVersion = deviceInfo.m_iFirmwareVersion;
}

bool SMX::SMXDevice::IsPlayer2Locked() const
{
    m_Lock.AssertLockedByCurrentThread();
    if(!IsConnectedLocked())
        return false;

    return m_pConnection->GetDeviceInfo().m_bP2;
}

bool SMX::SMXDevice::GetConfig(SMXConfig &configOut)
{
    LockMutex Lock(m_Lock);
    return GetConfigLocked(configOut);
}

bool SMX::SMXDevice::GetConfigLocked(SMXConfig &configOut)
{
    m_Lock.AssertLockedByCurrentThread();

    // If SetConfig was called to write a new configuration but we haven't sent it
    // yet, return it instead of the configuration we read alst, so GetConfig
    // immediately after SetConfig returns the value the caller expects set.
    if(m_bSendConfig)
        configOut = wanted_config;
    else
        configOut = config;

    return m_bHaveConfig;
}

void SMX::SMXDevice::SetConfig(const SMXConfig &newConfig)
{
    LockMutex Lock(m_Lock);
    wanted_config = newConfig;
    m_bSendConfig = true;
}

uint16_t SMX::SMXDevice::GetInputState() const
{
    LockMutex Lock(m_Lock);
    return m_pConnection->GetInputState();
}

void SMX::SMXDevice::FactoryReset()
{
    // Send a factory reset command, and then read the new configuration.
    LockMutex Lock(m_Lock);
    SendCommandLocked("f\n");

    SMXDeviceInfo deviceInfo = m_pConnection->GetDeviceInfo();

    SendCommandLocked(deviceInfo.m_iFirmwareVersion >= 5? "G":"g\n",
    [&](string response) {
        // We now have the new configuration.
        m_Lock.AssertLockedByCurrentThread();
        CallUpdateCallback(SMXUpdateCallback_FactoryResetCommandComplete);
    });
}

void SMX::SMXDevice::ForceRecalibration()
{
    LockMutex Lock(m_Lock);
    SendCommandLocked("C\n");
}

void SMX::SMXDevice::SetSensorTestMode(SensorTestMode mode)
{
    LockMutex Lock(m_Lock);
    m_SensorTestMode = mode;
}

bool SMX::SMXDevice::GetTestData(SMXSensorTestModeData &data)
{
    LockMutex Lock(m_Lock);

    // Stop if we haven't read test mode data yet.
    if(!m_HaveSensorTestModeData)
        return false;

    data = m_SensorTestData;
    return true;
}

void SMX::SMXDevice::CallUpdateCallback(SMXUpdateCallbackReason reason)
{
    m_Lock.AssertLockedByCurrentThread();

    if(!m_pUpdateCallback)
        return;

    SMXDeviceInfo deviceInfo = m_pConnection->GetDeviceInfo();
    m_pUpdateCallback(deviceInfo.m_bP2? 1:0, reason);
}

void SMX::SMXDevice::HandlePackets()
{
    m_Lock.AssertLockedByCurrentThread();

    while(1)
    {
        string buf;
        if(!m_pConnection->ReadPacket(buf))
            break;
        if(buf.empty())
            continue;

        switch(buf[0])
        {
        case 'y':
            HandleSensorTestDataResponse(buf);
            break;

        // 'g' is sent by firmware versions 1-4.  Version 5 and newer send 'G', to ensure
        // older code doesn't misinterpret the modified config packet format.
        case 'g':
        case 'G':
        {
            // This command reads back the configuration we wrote with 'w', or the defaults if
            // we haven't written any.
            if(buf.size() < 2)
            {
                Log("Communication error: invalid configuration packet");
                continue;
            }
            uint8_t iSize = buf[1];
            if(buf.size() < iSize+2)
            {
                Log("Communication error: invalid configuration packet");
                continue;
            }

            // Copy in the configuration.
            // Log(ssprintf("Read back configuration: %i bytes, first byte %i", iSize, buf[2]));
            memcpy(&config, buf.data()+2, min(iSize, sizeof(config)));
            m_bHaveConfig = true;
            buf.erase(buf.begin(), buf.begin()+iSize+2);

            CallUpdateCallback(SMXUpdateCallback_Updated);
            break;
        }
        }
    }
}

// If m_bSendConfig is true, send the configuration to the pad.  Note that while the game
// always sends its configuration, so the pad is configured according to the game's configuration,
// we only change the configuration if the user changes something so we don't overwrite
// his configuration.
void SMX::SMXDevice::SendConfig()
{
    m_Lock.AssertLockedByCurrentThread();

    if(!m_pConnection->IsConnected() || !m_bSendConfig || m_bSendingConfig)
        return;

    // We can't update the configuration until we've received the device's previous
    // configuration.
    if(!m_bHaveConfig)
        return;

    // If we're still waiting for a previous configuration to read back, don't send
    // another yet.
    if(m_bWaitingForConfigResponse)
        return;

    SMXDeviceInfo deviceInfo = m_pConnection->GetDeviceInfo();

    // Write configuration command.  This is "w" in versions 1-4, and "W" in versions 5
    // and newer.
    string sData = ssprintf(deviceInfo.m_iFirmwareVersion >= 5? "W":"w");
    int8_t iSize = sizeof(SMXConfig);

    // Firmware through version 3 allowed config packets up to 128 bytes.  Additions
    // to the packet later on brought it up to 126, so the maximum was raised to 250.
    // Older firmware won't use the extra fields, but will ignore the packet if it's
    // larger than it supports, so just truncate the packet for these devices to make
    // sure this doesn't happen.
    if(config.masterVersion <= 3)
        iSize = min(iSize, offsetof(SMXConfig, flags));

    sData.append((char *) &iSize, sizeof(iSize));
    sData.append((char *) &wanted_config, sizeof(wanted_config));

    // Don't send another config packet until this one finishes, so if we get a bunch of
    // SetConfig calls quickly we won't spam the device, which can get slow.
    m_bSendingConfig = true;
    SendCommandLocked(sData, [&](string response) {
        m_bSendingConfig = false;
    });
    m_bSendConfig = false;

    // Assume the configuration is what we just sent, so calls to GetConfig will
    // continue to return it.  Otherwise, they'd return the old values until the
    // command below completes.
    config = wanted_config;

    // Don't send another configuration packet until we receive the response to the above
    // command.  If we're sending updates quickly (eg. dragging the color slider), we can
    // send multiple updates before we get a response.
    m_bWaitingForConfigResponse = true;

    // After we write the configuration, read back the updated configuration to
    // verify it.  This command is "g" in versions 1-4, and "G" in versions 5 and
    // newer.
    SendCommandLocked(
        deviceInfo.m_iFirmwareVersion >= 5? "G":"g\n", [this](string response) {
        m_bWaitingForConfigResponse = false;
    });
}

void SMX::SMXDevice::Update(wstring &sError)
{
    m_Lock.AssertLockedByCurrentThread();

    if(!m_pConnection->IsConnected())
        return;

    CheckActive();
    SendConfig();
    UpdateSensorTestMode();

    {
        uint16_t iOldState = m_pConnection->GetInputState();

        // Process any received packets, and start sending any waiting packets.
        m_pConnection->Update(sError);
        if(!sError.empty())
            return;

        // If the inputs changed from packets we just processed, call the update callback.
        if(iOldState != m_pConnection->GetInputState())
            CallUpdateCallback(SMXUpdateCallback_Updated);
    }

    HandlePackets();
}

void SMX::SMXDevice::CheckActive()
{
    m_Lock.AssertLockedByCurrentThread();

    // If there's no connected device, or we've already activated it, we have nothing to do.
    if(!m_pConnection->IsConnectedWithDeviceInfo() || m_pConnection->GetActive())
        return;

    m_pConnection->SetActive(true);

    SMXDeviceInfo deviceInfo = m_pConnection->GetDeviceInfo();

    // Read the current configuration.  The device will return a "g" or "G" response
    // containing its current SMXConfig.
    SendCommandLocked(deviceInfo.m_iFirmwareVersion >= 5? "G":"g\n");
}

// Check if we need to request test mode data.
void SMX::SMXDevice::UpdateSensorTestMode()
{
    m_Lock.AssertLockedByCurrentThread();

    if(m_SensorTestMode == SensorTestMode_Off)
        return;

    // Request sensor data from the master.  Don't send this if we have a request outstanding
    // already.
    uint32_t now = GetTickCount();
    if(m_WaitingForSensorTestModeResponse != SensorTestMode_Off)
    {
        // This request should be quick.  If we haven't received a response in a long
        // time, assume the request wasn't received.
        if(now - m_SentSensorTestModeRequestAtTicks < 2000)
            return;
    }


    // Send the request.
    m_WaitingForSensorTestModeResponse = m_SensorTestMode;
    m_SentSensorTestModeRequestAtTicks = now;

    SendCommandLocked(ssprintf("y%c\n", m_SensorTestMode));
}

// Handle a response to UpdateTestMode.
void SMX::SMXDevice::HandleSensorTestDataResponse(const string &sReadBuffer)
{
    m_Lock.AssertLockedByCurrentThread();

    // "y" is a response to our "y" query.  This is binary data, with the format:
    // yAB......
    // where A is our original query mode (currently '0' or '1'), and B is the number
    // of bits from each panel in the response.  Each bit is encoded as a 16-bit int,
    // with each int having the response bits from each panel.
    if(sReadBuffer.size() < 3)
        return;

    // If we don't have the whole packet yet, wait.
    uint8_t iSize = sReadBuffer[2] * 2;
    if(sReadBuffer.size() < iSize + 3)
        return;

    SensorTestMode iMode = (SensorTestMode) sReadBuffer[1];

    // Copy off the data and remove it from the serial buffer.
    vector<uint16_t> data;
    for(int i = 3; i < iSize + 3; i += 2)
    {
        uint16_t iValue =
            (uint8_t(sReadBuffer[i+1]) << 8) |
            (uint8_t(sReadBuffer[i+0]) << 0);
        data.push_back(iValue);
    }

    if(m_WaitingForSensorTestModeResponse == SensorTestMode_Off)
    {
        Log("Ignoring unexpected sensor data request.  It may have been sent by another application.");
        return;
    }

    if(iMode != m_WaitingForSensorTestModeResponse)
    {
        Log(ssprintf("Ignoring unexpected sensor data request (got %i, expected %i)", iMode, m_WaitingForSensorTestModeResponse));
        return;
    }

    m_WaitingForSensorTestModeResponse = SensorTestMode_Off;

    // We match m_WaitingForSensorTestModeResponse, which is the sensor request we most
    // recently sent.  If we don't match g_SensorTestMode, then the sensor mode was changed
    // while a request was in the air.  Just ignore the response.
    if(iMode != m_SensorTestMode)
        return;

#pragma pack(push,1)
    struct detail_data {
        uint8_t sig1:1; // always 0
        uint8_t sig2:1; // always 1
        uint8_t sig3:1; // always 0
        uint8_t bad_sensor_0:1;
        uint8_t bad_sensor_1:1;
        uint8_t bad_sensor_2:1;
        uint8_t bad_sensor_3:1;
        uint8_t dummy:1;
            
        int16_t sensors[4];

        uint8_t dip:4;
        uint8_t bad_sensor_dip_0:1;
        uint8_t bad_sensor_dip_1:1;
        uint8_t bad_sensor_dip_2:1;
        uint8_t bad_sensor_dip_3:1;
    };
#pragma pack(pop)

    m_HaveSensorTestModeData = true;
    SMXSensorTestModeData &output = m_SensorTestData;

    bool bLastHaveDataFromPanel[9];
    memcpy(bLastHaveDataFromPanel, output.bHaveDataFromPanel, sizeof(output.bHaveDataFromPanel));
    memset(output.bHaveDataFromPanel, 0, sizeof(output.bHaveDataFromPanel));

    memset(output.sensorLevel, 0, sizeof(output.sensorLevel));
    memset(output.bBadSensorInput, 0, sizeof(output.bBadSensorInput));
    memset(output.iDIPSwitchPerPanel, 0, sizeof(output.iDIPSwitchPerPanel));
    memset(output.iBadJumper, 0, sizeof(output.iBadJumper));
    
    for(int iPanel = 0; iPanel < 9; ++iPanel)
    {
        // Decode the response from this panel.
        detail_data pad_data;
        ReadDataForPanel(data, iPanel, &pad_data, sizeof(pad_data));

        // Check the header.  This is always 0 1 0, to identify it as a response, and not as random
        // steps from the player.
        if(pad_data.sig1 != 0 || pad_data.sig2 != 1 || pad_data.sig3 != 0)
        {
            if(bLastHaveDataFromPanel[iPanel])
                Log(ssprintf("No data from panel %i (%02x %02x %02x)", iPanel, pad_data.sig1, pad_data.sig2, pad_data.sig3));
            output.bHaveDataFromPanel[iPanel] = false;
            continue;
        }
        output.bHaveDataFromPanel[iPanel] = true;

        // These bits are true if that sensor's most recent reading is invalid.
        output.bBadSensorInput[iPanel][0] = pad_data.bad_sensor_0;
        output.bBadSensorInput[iPanel][1] = pad_data.bad_sensor_1;
        output.bBadSensorInput[iPanel][2] = pad_data.bad_sensor_2;
        output.bBadSensorInput[iPanel][3] = pad_data.bad_sensor_3;
        output.iDIPSwitchPerPanel[iPanel]  = pad_data.dip;
        output.iBadJumper[iPanel][0] = pad_data.bad_sensor_dip_0;
        output.iBadJumper[iPanel][1] = pad_data.bad_sensor_dip_1;
        output.iBadJumper[iPanel][2] = pad_data.bad_sensor_dip_2;
        output.iBadJumper[iPanel][3] = pad_data.bad_sensor_dip_3;

        for(int iSensor = 0; iSensor < 4; ++iSensor)
            output.sensorLevel[iPanel][iSensor] = pad_data.sensors[iSensor];
    }

    CallUpdateCallback(SMXUpdateCallback_Updated);
}
