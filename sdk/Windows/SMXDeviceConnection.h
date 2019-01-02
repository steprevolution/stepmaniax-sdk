#ifndef SMXDevice_H
#define SMXDevice_H

#include <windows.h>
#include <vector>
#include <memory>
#include <string>
#include <list>
#include <functional>
using namespace std;

#include "Helpers.h"

namespace SMX
{

struct SMXDeviceInfo
{
    // If true, this controller is set to player 2.
    bool m_bP2 = false;

    // This device's serial number.
    char m_Serial[33];

    // This device's firmware version (normally 1).
    uint16_t m_iFirmwareVersion;
};

// Low-level SMX device handling.
class SMXDeviceConnection
{
public:
    static shared_ptr<SMXDeviceConnection> Create();
    SMXDeviceConnection(shared_ptr<SMXDeviceConnection> &pSelf);
    ~SMXDeviceConnection();

    bool Open(shared_ptr<AutoCloseHandle> DeviceHandle, wstring &error);

    void Close();
    
    // Get the device handle opened by Open(), or NULL if we're not open.
    shared_ptr<AutoCloseHandle> GetDeviceHandle() const { return m_hDevice; }

    void Update(wstring &sError);

    // Devices are inactive by default, and will just read device info and then idle.  We'll
    // process input state packets, but we won't send any commands to the device or process
    // any commands from it.  It's safe to have a device open but inactive if it's being used
    // by another application.
    void SetActive(bool bActive);
    bool GetActive() const { return m_bActive; }

    bool IsConnected() const { return m_hDevice != nullptr; }
    bool IsConnectedWithDeviceInfo() const { return m_hDevice != nullptr && m_bGotInfo; }
    SMXDeviceInfo GetDeviceInfo() const { return m_DeviceInfo; }

    // Read from the read buffer.  This only returns data that we've already read, so there aren't
    // any errors to report here.
    bool ReadPacket(string &out);

    // Send a command.  This must be a single complete command: partial writes and multiple
    // commands in a call aren't allowed.
    void SendCommand(const string &cmd, function<void(string response)> pComplete=nullptr);

    uint16_t GetInputState() const { return m_iInputState; }

private:
    void RequestDeviceInfo(function<void(string response)> pComplete = nullptr);

    void CheckReads(wstring &error);
    void BeginAsyncRead(wstring &error);
    void CheckWrites(wstring &error);
    void HandleUsbPacket(const string &buf);

    weak_ptr<SMXDeviceConnection> m_pSelf;
    shared_ptr<AutoCloseHandle> m_hDevice;

    bool m_bActive = false;

    // After we open a device, we request basic info.  Once we get it, this is set to true.
    bool m_bGotInfo = false;
    
    list<string> m_sReadBuffers;
    string m_sCurrentReadBuffer;

    struct PendingCommandPacket {
        PendingCommandPacket();

        string sData;
    };

    // Commands that are waiting to be sent:
    struct PendingCommand {
        PendingCommand();

        list<shared_ptr<PendingCommandPacket>> m_Packets;

        // The overlapped struct for writing this command's packets.  m_bWriting is true
        // if we're waiting for the write to complete.
        OVERLAPPED m_Overlapped;
        bool m_bWriting = false;

        // This is only called if m_bWaitForResponse if true.  Otherwise, we send the command
        // and forget about it.  If the command has a response, it'll be in buf.
        function<void(string response)> m_pComplete;

        // If true, once we send this command we won't send any other commands until we get
        // a response.
        bool m_bIsDeviceInfoCommand = false;

        // The SMX::GetMonotonicTime when we started sending this command.
        double m_fSentAt = 0;
    };
    list<shared_ptr<PendingCommand>> m_aPendingCommands;

    // If set, we've sent a command out of m_aPendingCommands and we're waiting for a response.  We
    // can't send another command until the previous one has completed.
    shared_ptr<PendingCommand> m_pCurrentCommand = nullptr;

    // We always have a read in progress.
    OVERLAPPED overlapped_read;
    char overlapped_read_buffer[64];

    uint16_t m_iInputState = 0;

    // The current device info.  We retrieve this when we connect.
    SMXDeviceInfo m_DeviceInfo;
};
}

#endif
