#include "SMXManager.h"
#include "SMXDevice.h"
#include "SMXDeviceConnection.h"
#include "SMXDeviceSearchThreaded.h"
#include "Helpers.h"

#include <windows.h>
#include <memory>
using namespace std;
using namespace SMX;

namespace {
    Mutex g_Lock;
}

shared_ptr<SMXManager> SMXManager::g_pSMX;

SMX::SMXManager::SMXManager(function<void(int PadNumber, SMXUpdateCallbackReason reason)> pCallback):
    m_UserCallbackThread("SMXUserCallbackThread")
{
    // Raise the priority of the user callback thread, since we don't want input
    // events to be preempted by other things and reduce timing accuracy.
    m_UserCallbackThread.SetHighPriority(true);
    m_hEvent = make_shared<AutoCloseHandle>(CreateEvent(NULL, false, false, NULL));
    m_pSMXDeviceSearchThreaded = make_shared<SMXDeviceSearchThreaded>();

    // Create the SMXDevices.  We don't create these as we connect, we just reuse the same
    // ones.
    for(int i = 0; i < 2; ++i)
    {
        shared_ptr<SMXDevice> pDevice = SMXDevice::Create(m_hEvent, g_Lock);
        m_pDevices.push_back(pDevice);
    }

    // The callback we send to SMXDeviceConnection will be called from our thread.  Wrap
    // it so it's called from UserCallbackThread instead.
    auto pCallbackInThread = [this, pCallback](int PadNumber, SMXUpdateCallbackReason reason) {
        m_UserCallbackThread.RunInThread([pCallback, PadNumber, reason]() {
            pCallback(PadNumber, reason);
        });
    };

    // Set the update callbacks.  Do this before starting the thread, to avoid race conditions.
    for(int pad = 0; pad < 2; ++pad)
        m_pDevices[pad]->SetUpdateCallback(pCallbackInThread);

    // Start the thread.
    DWORD id;
    m_hThread = CreateThread(NULL, 0, ThreadMainStart, this, 0, &id);
    SMX::SetThreadName(id, "SMXManager");

    // Raise the priority of the I/O thread, since we don't want input
    // events to be preempted by other things and reduce timing accuracy.
    SetThreadPriority( m_hThread, THREAD_PRIORITY_HIGHEST );
}

SMX::SMXManager::~SMXManager()
{
    // Shut down the thread, if it's still running.
    Shutdown();
}

shared_ptr<SMXDevice> SMX::SMXManager::GetDevice(int pad)
{
    return m_pDevices[pad];
}

void SMX::SMXManager::Shutdown()
{
    g_Lock.AssertNotLockedByCurrentThread();

    // Make sure we're not being called from within m_UserCallbackThread, since that'll
    // deadlock when we shut down m_UserCallbackThread.
    if(m_UserCallbackThread.GetThreadId() == GetCurrentThreadId())
        throw runtime_error("SMX::SMXManager::Shutdown must not be called from an SMX callback");

    // Shut down the thread we make user callbacks from.
    m_UserCallbackThread.Shutdown();

    // Shut down the device search thread.
    m_pSMXDeviceSearchThreaded->Shutdown();

    if(m_hThread == INVALID_HANDLE_VALUE)
        return;

    // Tell the thread to shut down, and wait for it before returning.
    m_bShutdown = true;
    SetEvent(m_hEvent->value());

    WaitForSingleObject(m_hThread, INFINITE);
    m_hThread = INVALID_HANDLE_VALUE;
}

DWORD WINAPI SMX::SMXManager::ThreadMainStart(void *self_)
{
    SMXManager *self = (SMXManager *) self_;
    self->ThreadMain();
    return 0;
}

// When we connect to a device, we don't know whether it's P1 or P2, since we get that
// info from the device after we connect to it.  If we have a P2 device in SMX_PadNumber_1
// or a P1 device in SMX_PadNumber_2, swap the two.  
void SMX::SMXManager::CorrectDeviceOrder()
{
    // We're still holding the lock from when we updated the devices, so the application
    // won't see the devices out of order before we do this.
    g_Lock.AssertLockedByCurrentThread();

    SMXInfo info[2];
    m_pDevices[0]->GetInfoLocked(info[0]);
    m_pDevices[1]->GetInfoLocked(info[1]);

    // If we have two P1s or two P2s, the pads are misconfigured and we'll just leave the order alone.
    bool Player2[2] = {
        m_pDevices[0]->IsPlayer2Locked(),
        m_pDevices[1]->IsPlayer2Locked(),
    };
    if(info[0].m_bConnected && info[1].m_bConnected && Player2[0] == Player2[1])
        return;

    bool bP1NeedsSwap = info[0].m_bConnected && Player2[0];
    bool bP2NeedsSwap = info[1].m_bConnected && !Player2[1];
    if(bP1NeedsSwap || bP2NeedsSwap)
        swap(m_pDevices[0], m_pDevices[1]);
}

void SMX::SMXManager::ThreadMain()
{
    g_Lock.Lock();

    while(!m_bShutdown)
    {
        // If there are any lights commands to be sent, send them now.  Do this before callig Update(),
        // since this actually just queues commands, which are actually handled in Update.
        SendLightUpdates();

        // See if there are any new devices.
        AttemptConnections();

        // Update all connected devices.
        for(shared_ptr<SMXDevice> pDevice: m_pDevices)
        {
            wstring sError;
            pDevice->Update(sError);

            if(!sError.empty())
            {
                Log(ssprintf("Device error: %ls", sError.c_str()));

                // Tell m_pDeviceList that the device was closed, so it'll discard the device
                // and notice if a new device shows up on the same path.
                m_pSMXDeviceSearchThreaded->DeviceWasClosed(pDevice->GetDeviceHandle());
                pDevice->CloseDevice();
            }
        }

        // Devices may have finished initializing, so see if we need to update the ordering.
        CorrectDeviceOrder();

        // Make a list of handles for WaitForMultipleObjectsEx.
        vector<HANDLE> aHandles = { m_hEvent->value() };
        for(shared_ptr<SMXDevice> pDevice: m_pDevices)
        {
            shared_ptr<AutoCloseHandle> pHandle = pDevice->GetDeviceHandle();
            if(pHandle)
                aHandles.push_back(pHandle->value());
        }

        // See how long we should block waiting for I/O.  If we have any scheduled lights commands,
        // wait until the next command should be sent, otherwise wait for a second.
        int iDelayMS = 1000;
        if(!m_aPendingCommands.empty())
        {
            double fSendIn = m_aPendingCommands[0].fTimeToSend - GetMonotonicTime();

            // Add 1ms to the delay time.  We're using a high resolution timer, but
            // WaitForMultipleObjectsEx only has 1ms resolution, so this keeps us from
            // repeatedly waking up slightly too early.
            iDelayMS = int(fSendIn * 1000) + 1;
            iDelayMS = max(0, iDelayMS);
        }

        // Wait until there's something to do for a connected device, or delay briefly if we're
        // not connected to anything.  Unlock while we block.  Devices are only ever opened or
        // closed from within this thread, so the handles won't go away while we're waiting on
        // them.
        g_Lock.Unlock();
        WaitForMultipleObjectsEx(aHandles.size(), aHandles.data(), false, iDelayMS, true);
        g_Lock.Lock();
    }
    g_Lock.Unlock();
}

// Lights are updated with two commands.  The top two rows of LEDs in each panel are
// updated by the first command, and the bottom two rows are updated by the second
// command.  We need to send the two commands in order.  The panel won't update lights
// until both commands have been received, so we don't flicker the partial top update
// before the bottom update is received.
//
// A complete update can be performed at up to 30 FPS, but we actually update at 60
// FPS, alternating between updating the top and bottom half.
//
// This interlacing is performed to reduce the amount of work the panels and master
// controller need to do on each update.  This improves timing accuracy, since less
// time is taken by each update.
//
// The order of lights is:
//
// 0123 0123 0123
// 4567 4567 4567
// 89AB 89AB 89AB
// CDEF CDEF CDEF
//
// 0123 0123 0123
// 4567 4567 4567
// 89AB 89AB 89AB
// CDEF CDEF CDEF
//
// 0123 0123 0123
// 4567 4567 4567
// 89AB 89AB 89AB
// CDEF CDEF CDEF
//
// with panels left-to-right, top-to-bottom.  The first packet sends all 0123 and 4567
// lights, and the second packet sends 78AB and CDEF.
//
// We hide these details from the API to simplify things for the user:
//
// - The user sends us a complete lights set.  This should be sent at (up to) 30Hz.
// If we get lights data too quickly, we'll always complete the one we started before
// sending the next.
// - We don't limit to exactly 30Hz to prevent phase issues where a 60 FPS game is
// coming in and out of phase with our timer.  To avoid this, we limit to 40Hz.
// - When we have new lights data to send, we send the first half right away, wait
// 16ms (60Hz), then send the second half, which is the pacing the device expects.
// - If we get a new lights update in between the two lights commands, we won't split
// the lights.  The two lights commands will always come from the same update, so
// we don't get weird interlacing effects.
// - If SMX_ReenableAutoLights is called between the two commands, we need to guarantee
// that we don't send the second lights commands, since that may re-disable auto lights.
// - If we have two pads, the lights update is for both pads and we'll send both commands
// for both pads at the same time, so both pads update lights simultaneously.
void SMX::SMXManager::SetLights(const string &sLightData)
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    // Sanity check the lights data.  It should have 18*16*3 bytes of data: RGB for each of 4x4
    // LEDs on 18 panels.
    if(sLightData.size() != 2*3*3*16*3)
    {
        Log(ssprintf("SetLights: Lights data should be %i bytes, received %i", 2*3*3*16*3, sLightData.size()));
        return;
    }

    // Split the lights data into P1 and P2.
    string sPanelLights[2];
    sPanelLights[0] = sLightData.substr(0, 9*16*3);
    sPanelLights[1] = sLightData.substr(9*16*3);

    // Separate top and bottom lights commands.
    //
    // sPanelLights[iPad] is
    //
    // 0123 0123 0123
    // 4567 4567 4567
    // 89AB 89AB 89AB
    // CDEF CDEF CDEF
    //
    // 0123 0123 0123
    // 4567 4567 4567
    // 89AB 89AB 89AB
    // CDEF CDEF CDEF
    //
    // 0123 0123 0123
    // 4567 4567 4567
    // 89AB 89AB 89AB
    // CDEF CDEF CDEF
    //
    // Set sLightsCommand[iPad][0] to include 0123 4567, and [1] to 89AB CDEF.
    string sLightCommands[2][2]; // sLightCommands[command][pad]

    auto addByte = [&sLightCommands](int iPanel, int iByte, uint8_t iColor) {
        // If iPanel is 0-8, this is for pad 0.  For 9-17, it's for pad 1.
        // If the color byte within the panel is in the top half, it's the first
        // command, otherwise it's the second command.
        int iPad = iPanel < 9? 0:1;
        int iCommandIndex = iByte < 4*2*3? 0:1;
        sLightCommands[iCommandIndex][iPad].append(1, iColor);
    };

    // Read the linearly arranged color data we've been given and split it into top and
    // bottom commands for each pad.
    int iNextInputByte = 0;
    for(int iPanel = 0; iPanel < 18; ++iPanel)
    {
        for(int iByte = 0; iByte < 4*4*3; ++iByte)
        {
            uint8_t iColor = sLightData[iNextInputByte++];
            addByte(iPanel, iByte, iColor);
        }
    }

    for(int iPad = 0; iPad < 2; ++iPad)
    {
        for(int iCommand = 0; iCommand < 2; ++iCommand)
        {
            string &sCommand = sLightCommands[iCommand][iPad];

            // Apply color scaling.  Values over about 170 don't make the LEDs any brighter, so this
            // gives better contrast and draws less power.
            for(char &c: sCommand)
                c = char(uint8_t(c) * 0.6666f);

            // Add the command byte.
            sCommand.insert(sCommand.begin(), 1, iCommand == 0? '2':'3');
            sCommand.push_back('\n');
        }
    }

    // Each update adds two entries to m_aPendingCommands, one for the top half and one
    // for the lower half.
    //
    // If there's one entry in the list, we've already sent the first half of a previous update,
    // and the remaining entry is the second half.  We'll leave it in place so we always finish
    // an update once we start it, and add this update after it.
    //
    // If there are two entries in the list, then it's an existing update that we haven't sent yet.
    // If there are three entries, we added an update after a partial update.  In either case, the
    // last two commands in the list are a complete lights update, and we'll just update it in-place.
    //
    // This way, we'll always finish a lights update once we start it, so if we receive lights updates
    // very quickly we won't just keep sending the first half and never finish one.  Otherwise, we'll
    // update with the newest data we have available.
    if(m_aPendingCommands.size() <= 1)
    {
        static const double fDelayBetweenLightsCommands = 1/60.0;

        double fNow = GetMonotonicTime();
        double fSendCommandAt = max(fNow, m_fDelayLightCommandsUntil);
        float fFirstCommandTime = fSendCommandAt;
        float fSecondCommandTime = fFirstCommandTime + fDelayBetweenLightsCommands;

        // Update m_fDelayLightCommandsUntil, so we know when the next 
        m_fDelayLightCommandsUntil = fSecondCommandTime + fDelayBetweenLightsCommands;

        // Add two commands to the list, scheduled at fFirstCommandTime and fSecondCommandTime.
        m_aPendingCommands.push_back(PendingCommand(fFirstCommandTime));
        m_aPendingCommands.push_back(PendingCommand(fSecondCommandTime));
        // Log(ssprintf("Scheduled commands at %f and %f", fFirstCommandTime, fSecondCommandTime));

        // Wake up the I/O thread if it's blocking on WaitForMultipleObjectsEx.
        SetEvent(m_hEvent->value());
    }

    // Set the pad commands.
    PendingCommand *pPendingCommands[2];
    pPendingCommands[0] = &m_aPendingCommands[m_aPendingCommands.size()-2];
    pPendingCommands[1] = &m_aPendingCommands[m_aPendingCommands.size()-1];

    pPendingCommands[0]->sPadCommand[0] = sLightCommands[0][0];
    pPendingCommands[0]->sPadCommand[1] = sLightCommands[0][1];
    pPendingCommands[1]->sPadCommand[0] = sLightCommands[1][0];
    pPendingCommands[1]->sPadCommand[1] = sLightCommands[1][1];
}

void SMX::SMXManager::ReenableAutoLights()
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    // Clear any pending lights commands, so we don't re-disable auto-lighting by sending a
    // lights command after we enable it.  If we've sent the first half of a lights update
    // and this causes us to not send the second half, the controller will just discard it.
    m_aPendingCommands.clear();
    for(int iPad = 0; iPad < 2; ++iPad)
        m_pDevices[iPad]->SendCommandLocked(string("S 1\n", 4));
}

// Check to see if we should send any commands in m_aPendingCommands.
void SMX::SMXManager::SendLightUpdates()
{
    g_Lock.AssertLockedByCurrentThread();
    if(m_aPendingCommands.empty())
        return;

    const PendingCommand &command = m_aPendingCommands[0];

    // See if it's time to send the next command.  We only need to look at the first
    // command, since these are always sorted.
    if(command.fTimeToSend > GetMonotonicTime())
        return;

    // Send the lights command for each pad.  If either pad isn't connected, this won't do
    // anything.
    for(int iPad = 0; iPad < 2; ++iPad)
        m_pDevices[iPad]->SendCommandLocked(command.sPadCommand[iPad]);

    // Remove the command we've sent.
    m_aPendingCommands.erase(m_aPendingCommands.begin(), m_aPendingCommands.begin()+1);
}

// See if there are any new devices to connect to.
void SMX::SMXManager::AttemptConnections()
{
    g_Lock.AssertLockedByCurrentThread();

    vector<shared_ptr<AutoCloseHandle>> apDevices = m_pSMXDeviceSearchThreaded->GetDevices();

    // Check each device that we've found.  This will include ones we already have open.
    for(shared_ptr<AutoCloseHandle> pHandle: apDevices)
    {
        // See if this device is already open.  If it is, we don't need to do anything with it.
        bool bAlreadyOpen = false;
        for(shared_ptr<SMXDevice> pDevice: m_pDevices)
        {
            if(pDevice->GetDeviceHandle() == pHandle)
                bAlreadyOpen = true;
        }
        if(bAlreadyOpen)
            continue;

        // Find an open device slot.
        shared_ptr<SMXDevice> pDeviceToOpen;
        for(shared_ptr<SMXDevice> pDevice: m_pDevices)
        {
            // Note that we check whether the device has a handle rather than calling IsConnected, since
            // devices aren't actually considered connected until they've read the configuration.
            if(pDevice->GetDeviceHandle() == NULL)
            {
                pDeviceToOpen = pDevice;
                break;
            }
        }

        if(pDeviceToOpen == nullptr)
        {
            // All device slots are used.  Are there more than two devices plugged in?
            Log("Error: No available slots for device.  Are more than two devices connected?");
            break;
        }

        // Open the device in this slot.
        Log("Opening SMX device");
        wstring sError;
        pDeviceToOpen->OpenDeviceHandle(pHandle, sError);
        if(!sError.empty())
            Log(ssprintf("Error opening device: %ls", sError.c_str()));
    }
}



