#include "SMXDeviceConnection.h"
#include "Helpers.h"

#include <string>
#include <memory>
using namespace std;
using namespace SMX;

#include <hidsdi.h>
#include <SetupAPI.h>

SMX::SMXDeviceConnection::PendingCommandPacket::PendingCommandPacket()
{
    memset(&m_OverlappedWrite, 0, sizeof(m_OverlappedWrite));
}

shared_ptr<SMX::SMXDeviceConnection> SMXDeviceConnection::Create()
{
    return CreateObj<SMXDeviceConnection>();
}

SMX::SMXDeviceConnection::SMXDeviceConnection(shared_ptr<SMXDeviceConnection> &pSelf):
    m_pSelf(GetPointers(pSelf, this))
{
    memset(&overlapped_read, 0, sizeof(overlapped_read));
}

SMX::SMXDeviceConnection::~SMXDeviceConnection()
{
    Close();
}

bool SMX::SMXDeviceConnection::Open(shared_ptr<AutoCloseHandle> DeviceHandle, wstring &sError)
{
    m_hDevice = DeviceHandle;

    if(!HidD_SetNumInputBuffers(DeviceHandle->value(), 512))
        Log(ssprintf("Error: HidD_SetNumInputBuffers: %ls", GetErrorString(GetLastError()).c_str()));

    // Begin the first async read.
    BeginAsyncRead(sError);

    // Request device info.
    RequestDeviceInfo([&] {
        Log(ssprintf("Received device info.  Master version: %i, P%i", m_DeviceInfo.m_iFirmwareVersion, m_DeviceInfo.m_bP2+1));
        m_bGotInfo = true;
    });

    return true;
}

void SMX::SMXDeviceConnection::Close()
{
    Log("Closing device");

    if(m_hDevice)
        CancelIo(m_hDevice->value());

    m_hDevice.reset();
    m_sReadBuffers.clear();
    m_aPendingCommands.clear();
    memset(&overlapped_read, 0, sizeof(overlapped_read));
    m_bActive = false;
    m_bGotInfo = false;
    m_pCurrentCommand = nullptr;
    m_iInputState = 0;
}

void SMX::SMXDeviceConnection::SetActive(bool bActive)
{
    if(m_bActive == bActive)
        return;

    m_bActive = bActive;
}

void SMX::SMXDeviceConnection::Update(wstring &sError)
{
    if(!sError.empty())
        return;

    if(m_hDevice == nullptr)
    {
        sError = L"Device not open";
        return;
    }

    // A read packet can allow us to initiate a write, so check reads before writes.
    CheckReads(sError);
    CheckWrites(sError);
}

bool SMX::SMXDeviceConnection::ReadPacket(string &out)
{
    if(m_sReadBuffers.empty())
        return false;
    out = m_sReadBuffers.front();
    m_sReadBuffers.pop_front();
    return true;
}

void SMX::SMXDeviceConnection::CheckReads(wstring &error)
{
    DWORD bytes;
    int result = GetOverlappedResult(m_hDevice->value(), &overlapped_read, &bytes, FALSE);
    if(result == 0)
    {
        int windows_error = GetLastError();
        if(windows_error != ERROR_IO_PENDING && windows_error != ERROR_IO_INCOMPLETE)
            error = wstring(L"Error reading device: ") + GetErrorString(windows_error).c_str();
        return;
    }

    HandleUsbPacket(string(overlapped_read_buffer, bytes));

    // Start the next read.
    BeginAsyncRead(error);
}

void SMX::SMXDeviceConnection::HandleUsbPacket(const string &buf)
{
    if(buf.empty())
        return;
    // Log(ssprintf("Read: %s", BinaryToHex(buf).c_str()));

    int iReportId = buf[0];
    switch(iReportId)
    {
    case 3:
        // Input state.  We could also read this as a normal HID button change.
        m_iInputState = ((buf[2] & 0xFF) << 8) |
                ((buf[1] & 0xFF) << 0);

        // Log(ssprintf("Input state: %x (%x %x)\n", m_iInputState, buf[2], buf[1]));
        break;

    case 6:
        // A HID serial packet.
        if(buf.size() < 3)
            return;

        int cmd = buf[1];

#define PACKET_FLAG_START_OF_COMMAND      0x04
#define PACKET_FLAG_END_OF_COMMAND        0x01
#define PACKET_FLAG_HOST_CMD_FINISHED     0x02
#define PACKET_FLAG_DEVICE_INFO           0x80

        int bytes = buf[2];
        if(3 + bytes > buf.size())
        {
            Log("Communication error: oversized packet (ignored)");
            return;
        }

        string sPacket( buf.begin()+3, buf.begin()+3+bytes );

        if(cmd & PACKET_FLAG_DEVICE_INFO)
        {
            // This is a response to RequestDeviceInfo.  Since any application can send this,
            // we ignore the packet if we didn't request it, since it might be requested for
            // a different program.
            if(m_pCurrentCommand == nullptr || !m_pCurrentCommand->m_bIsDeviceInfoCommand)
                break;

            // We're little endian and the device is too, so we can just match the struct.
            // We're depending on correct padding.
            struct data_info_packet
            {
                char cmd; // always 'I'
                uint8_t packet_size; // not used
                char player; // '0' for P1, '1' for P2:
                char unused2;
                uint8_t serial[16];
                uint16_t firmware_version;
                char unused3; // always '\n'
            };

            // The packet contains data_info_packet.  The packet is actually one byte smaller
            // due to a padding byte added (it contains 23 bytes of data but the struct is
            // 24 bytes).  Resize to be sure.
            sPacket.resize(sizeof(data_info_packet));

            // Convert the info packet from the wire protocol to our friendlier API.
            const data_info_packet *packet = (data_info_packet *) sPacket.data();
            m_DeviceInfo.m_bP2 = packet->player == '1';
            m_DeviceInfo.m_iFirmwareVersion = packet->firmware_version;

            // The serial is binary in this packet.  Hex format it, which is the same thing
            // we'll get if we read the USB serial number (eg. HidD_GetSerialNumberString).
            string sHexSerial = BinaryToHex(packet->serial, 16);
            memcpy(m_DeviceInfo.m_Serial, sHexSerial.c_str(), 33);

            if(m_pCurrentCommand->m_pComplete)
                m_pCurrentCommand->m_pComplete();
            m_pCurrentCommand = nullptr;

            break;
        }

        // If we're not active, ignore all packets other than device info.  This is always false
        // while we're in Open() waiting for the device info response.
        if(!m_bActive)
            break;

        m_sCurrentReadBuffer.append(sPacket);

        if(cmd & PACKET_FLAG_END_OF_COMMAND)
        {
            if(!m_sCurrentReadBuffer.empty())
                m_sReadBuffers.push_back(m_sCurrentReadBuffer);
            m_sCurrentReadBuffer.clear();
        }

        if(cmd & PACKET_FLAG_HOST_CMD_FINISHED)
        {
            // This tells us that a command we wrote to the device has finished executing, and
            // it's safe to start writing another.
            if(m_pCurrentCommand && m_pCurrentCommand->m_pComplete)
                m_pCurrentCommand->m_pComplete();
            m_pCurrentCommand = nullptr;
        }

        break;
    }

}

void SMX::SMXDeviceConnection::BeginAsyncRead(wstring &error)
{
    while(1)
    {
        // Our read buffer is 64 bytes.  The HID input packet is much smaller than that,
        // but Windows pads packets to the maximum size of any HID report, and the HID
        // serial packet is 64 bytes, so we'll get 64 bytes even for 3-byte input packets.
        // If this didn't happen, we'd have to be smarter about pulling data out of the
        // read buffer.
        DWORD bytes;
        memset(overlapped_read_buffer, sizeof(overlapped_read_buffer), 0);
        if(!ReadFile(m_hDevice->value(), overlapped_read_buffer, sizeof(overlapped_read_buffer), &bytes, &overlapped_read))
        {
            int windows_error = GetLastError();
            if(windows_error != ERROR_IO_PENDING && windows_error != ERROR_IO_INCOMPLETE)
                error = wstring(L"Error reading device: ") + GetErrorString(windows_error).c_str();
            return;
        }

        // The async read finished synchronously.  This just means that there was already data waiting.
        // Handle the result, and loop to try to start the next async read again.
        HandleUsbPacket(string(overlapped_read_buffer, bytes));
    }
}

void SMX::SMXDeviceConnection::CheckWrites(wstring &error)
{
    if(m_pCurrentCommand && !m_pCurrentCommand->m_Packets.empty())
    {
        // A command is in progress.  See if any writes have completed.
        while(!m_pCurrentCommand->m_Packets.empty())
        {
            shared_ptr<PendingCommandPacket> pFirstPacket = m_pCurrentCommand->m_Packets.front();

            DWORD bytes;
            int iResult = GetOverlappedResult(m_hDevice->value(), &pFirstPacket->m_OverlappedWrite, &bytes, FALSE);
            if(iResult == 0)
            {
                int windows_error = GetLastError();
                if(windows_error != ERROR_IO_PENDING && windows_error != ERROR_IO_INCOMPLETE)
                    error = wstring(L"Error writing to device: ") + GetErrorString(windows_error).c_str();
                return;
            }

            m_pCurrentCommand->m_Packets.pop_front();
        }


        // Don't clear m_pCurrentCommand here.  It'll stay set until we get a PACKET_FLAG_HOST_CMD_FINISHED
        // packet from the device, which tells us it's ready to receive another command.
    }

    // Don't send packets if there's a command in progress.
    if(m_pCurrentCommand)
        return;

    // Stop if we have nothing to do.
    if(m_aPendingCommands.empty())
        return;

    // Send the next command.
    shared_ptr<PendingCommand> pPendingCommand = m_aPendingCommands.front();

    for(shared_ptr<PendingCommandPacket> &pPacket: pPendingCommand->m_Packets)
    {
        // In theory the API allows this to return success if the write completed successfully without needing to
        // be async, like reads can.  However, this can't really happen (the write always needs to go to the device
        // first, unlike reads which might already be buffered), and there's no way to test it if we implement that,
        // so this assumes all writes are async.
        DWORD unused;
        // Log(ssprintf("Write: %s", BinaryToHex(pPacket->sData).c_str()));
        if(!WriteFile(m_hDevice->value(), pPacket->sData.data(), pPacket->sData.size(), &unused, &pPacket->m_OverlappedWrite))
        {
            int windows_error = GetLastError();
            if(windows_error != ERROR_IO_PENDING && windows_error != ERROR_IO_INCOMPLETE)
            {
                error = wstring(L"Error writing to device: ") + GetErrorString(windows_error).c_str();
                return;
            }
        }
    }

    // Remove this command and store it in m_pCurrentCommand, and we'll stop sending data until the command finishes.
    m_pCurrentCommand = pPendingCommand;
    m_aPendingCommands.pop_front();
}

// Request device info.  This is the same as sending an 'i' command, but we can send it safely
// at any time, even if another application is talking to the device, so we can do this during
// enumeration.
void SMX::SMXDeviceConnection::RequestDeviceInfo(function<void()> pComplete)
{
    shared_ptr<PendingCommand> pPendingCommand = make_shared<PendingCommand>();
    pPendingCommand->m_pComplete = pComplete;
    pPendingCommand->m_bIsDeviceInfoCommand = true;

    shared_ptr<PendingCommandPacket> pCommandPacket = make_shared<PendingCommandPacket>();

    string sPacket({
        5, // report ID
        (char) (uint8_t) PACKET_FLAG_DEVICE_INFO, // flags
        (char) 0, // bytes in packet
    });
    sPacket.resize(64, 0);
    pCommandPacket->sData = sPacket;

    pPendingCommand->m_Packets.push_back(pCommandPacket);

    m_aPendingCommands.push_back(pPendingCommand);
}

void SMX::SMXDeviceConnection::SendCommand(const string &cmd, function<void()> pComplete)
{
    shared_ptr<PendingCommand> pPendingCommand = make_shared<PendingCommand>();
    pPendingCommand->m_pComplete = pComplete;

    // Send the command in packets.  We allow sending zero-length packets here
    // for testing purposes.
    int i = 0;
    do {
        shared_ptr<PendingCommandPacket> pCommandPacket = make_shared<PendingCommandPacket>();

        int iFlags = 0;
        int iPacketSize = min(cmd.size() - i, 61);

        bool bFirstPacket = (i == 0);
        if(bFirstPacket)
            iFlags |= PACKET_FLAG_START_OF_COMMAND;

        bool bLastPacket = (i + iPacketSize == cmd.size());
        if(bLastPacket)
            iFlags |= PACKET_FLAG_END_OF_COMMAND;

        string sPacket({
            5, // report ID
            (char) iFlags,
            (char) iPacketSize, // bytes in packet
        });

        sPacket.append(cmd.begin() + i, cmd.begin() + i + iPacketSize);
        sPacket.resize(64, 0);
        pCommandPacket->sData = sPacket;

        pPendingCommand->m_Packets.push_back(pCommandPacket);

        i += iPacketSize;
    }
    while(i < cmd.size());

    m_aPendingCommands.push_back(pPendingCommand);
}
