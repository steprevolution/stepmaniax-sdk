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
    if(m_UserCallbackThread.IsCurrentThread())
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

        // Send panel test mode commands if needed.
        UpdatePanelTestMode();

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
        if(!m_aPendingLightsCommands.empty())
        {
            double fSendIn = m_aPendingLightsCommands[0].fTimeToSend - GetMonotonicTime();

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
void SMX::SMXManager::SetLights(const string sPanelLights[2])
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    // Don't send lights when a panel test mode is active.
    if(m_PanelTestMode != PanelTestMode_Off)
        return;

    // If m_bOnlySendLightsOnChange is true, only send lights commands if the lights have
    // actually changed.  This is only used for internal testing, and the controllers normally
    // expect to receive regular lights updates, even if the lights aren't actually changing.
    if(m_bOnlySendLightsOnChange)
    {
        static string sLastPanelLights[2];
        if(sPanelLights[0] == sLastPanelLights[0] && sPanelLights[1] == sLastPanelLights[1])
        {
            Log("no change");
            return;
        }

        sLastPanelLights[0] = sPanelLights[0];
        sLastPanelLights[1] = sPanelLights[1];
    }

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
    // If we're on a 25-light device, we have an additional grid of 3x3 LEDs:
    //
    //
    // x x x x
    //  0 1 2
    // x x x x
    //  3 4 5
    // x x x x
    //  6 7 8
    // x x x x
    //
    // Set sLightsCommand[iPad][0] to include 0123 4567, [1] to 89AB CDEF,
    // and [2] to the 3x3 grid.
    string sLightCommands[3][2]; // sLightCommands[command][pad]

    // Read the linearly arranged color data we've been given and split it into top and
    // bottom commands for each pad.
    for(int iPad = 0; iPad < 2; ++iPad)
    {
        // If there's no data for this pad, leave the command empty.
        string sLightsDataForPad = sPanelLights[iPad];
        if(sLightsDataForPad.empty())
            continue;

        // Sanity check the lights data.  For 4x4 lights, it should have 9*4*4*3 bytes of
        // data: RGB for each of 4x4 LEDs on 9 panels.  For 25-light panels there should
        // be 4x4+3x3 (25) lights of data.
        int LightSize4x4 = 9*4*4*3;
        int LightSize25 = 9*5*5*3;
        if(sLightsDataForPad.size() != LightSize4x4 && sLightsDataForPad.size() != LightSize25)
        {
            Log(ssprintf("SetLights: Lights data should be %i or %i bytes, received %i",
                LightSize4x4, LightSize25, sLightsDataForPad.size()));
            continue;
        }

        // If we've been given 16 lights, pad to 25.
        if(sLightsDataForPad.size() == LightSize4x4)
            sLightsDataForPad.append(LightSize25 - LightSize4x4, '\0');

        // Lights are sent in three commands:
        // 
        // 4: the 3x3 inner grid
        // 2: the top 4x2 lights
        // 3: the bottom 4x2 lights
        //
        // Command 4 is only used by firmware version 4+.
        //
        // Always send all three commands if the firmware expects it, even if we've
        // been given 4x4 data.
        sLightCommands[0][iPad] = "4";
        sLightCommands[1][iPad] = "2";
        sLightCommands[2][iPad] = "3";
        int iNextInputByte = 0;
        auto scaleLight = [](uint8_t iColor) {
            // Apply color scaling.  Values over about 170 don't make the LEDs any brighter, so this
            // gives better contrast and draws less power.
            return uint8_t(iColor * 0.6666f);
        };
        for(int iPanel = 0; iPanel < 9; ++iPanel)
        {
            // Create the 2 and 3 commands.
            for(int iByte = 0; iByte < 4*4*3; ++iByte)
            {
                uint8_t iColor = sLightsDataForPad[iNextInputByte++];
                iColor = scaleLight(iColor);
                
                int iCommandIndex = iByte < 4*2*3? 1:2;
                sLightCommands[iCommandIndex][iPad].append(1, iColor);
            }

            // Create the 4 command.
            for(int iByte = 0; iByte < 3*3*3; ++iByte)
            {
                uint8_t iColor = sLightsDataForPad[iNextInputByte++];
                iColor = scaleLight(iColor);
                sLightCommands[0][iPad].append(1, iColor);
            }
        }

        sLightCommands[0][iPad].push_back('\n');
        sLightCommands[1][iPad].push_back('\n');
        sLightCommands[2][iPad].push_back('\n');
    }

    // Each update adds one entry to m_aPendingLightsCommands for each lights command.
    //
    // If there are at least as many entries in m_aPendingLightsCommands as there are
    // commands to send, then lights updates are happening faster than they can be sent
    // to the pad.  If that happens, replace the existing commands rather than adding
    // new ones.
    //
    // Make sure we always finish a lights update once we start it, so if we receive lights
    // updates very quickly we won't just keep sending the first half and never finish one.
    // Otherwise, we'll update with the newest data we have available.
    //
    // Note that m_aPendingLightsCommands contains the update for both pads, to guarantee
    // we always send light updates for both pads together and they never end up out of
    // phase.
    if(m_aPendingLightsCommands.size() < 3)
    {
        // There's a subtle but important difference between command timing in
        // firmware version 4 compared to earlier versions:
        //
        // Earlier firmwares would process host commands as soon as they're received.
        // Because of this, we have to wait before sending the '3' command to give
        // the master controller time to finish sending the '2' command to panels.
        // If we don't do this everything will still work, but the master will block
        // while processing the second command waiting for panel data to finish sending
        // since the TX queue will be full.  If this happens it isn't processing HID
        // data, which reduces input timing accuracy.
        //
        // Firmware version 4 won't process a host command if there's data still being
        // sent to the panels.  It'll wait until the data is flushed.  This means that
        // we can queue all three lights commands at once, and just send them as fast
        // as the host acknowledges them.  The second command will sit around on the
        // master controller's buffer until it finishes sending the first command to
        // the panels, then the third command will do the same.
        //
        // This change is only needed due to the larger amount of data sent in 25-light
        // mode.  Since we're spending more time sending data from the master to the
        // panels, the timing requirements are tighter.  Doing it in the same manual-delay
        // fashion causes too much latency and makes it harder to maintain 30 FPS.
        //
        // If two controllers are connected, they should either both be 4+ or not.  We
        // don't handle the case where they're different and both timings are needed.
        double fNow = GetMonotonicTime();
        double fSendCommandAt = max(fNow, m_fDelayLightCommandsUntil);
        double fCommandTimes[3] = { fNow, fNow, fNow };

        bool masterIsV4 = false;
        bool anyMasterConnected = false;
        for(int iPad = 0; iPad < 2; ++iPad)
        {
            SMXConfig config;
            if(!m_pDevices[iPad]->GetConfigLocked(config))
                continue;

            anyMasterConnected = true;
            if(config.masterVersion >= 4)
                masterIsV4 = true;
        }

        // If we don't have the config yet, the master is in the process of connecting, so don't
        // queue lights.
        if(!anyMasterConnected)
            return;

        // If we're on master firmware < 4, set delay times.  For 4+, just queue commands.
        // We don't need to set fCommandTimes[0] since the '4' packet won't be sent.
        if(!masterIsV4)
        {
            const double fDelayBetweenLightsCommands = 1/60.0;
            fCommandTimes[1] = fSendCommandAt;
            fCommandTimes[2] = fCommandTimes[1] + fDelayBetweenLightsCommands;
        }

        // Update m_fDelayLightCommandsUntil, so we know when the next
        // lights command can be sent.
        m_fDelayLightCommandsUntil = fSendCommandAt + 1/30.0f;

        // Add three commands to the list, scheduled at fFirstCommandTime and fSecondCommandTime.
        m_aPendingLightsCommands.push_back(PendingCommand(fCommandTimes[0]));
        m_aPendingLightsCommands.push_back(PendingCommand(fCommandTimes[1]));
        m_aPendingLightsCommands.push_back(PendingCommand(fCommandTimes[2]));
    }

    // Set the pad commands.
    for(int iPad = 0; iPad < 2; ++iPad)
    {
        // If the command for this pad is empty, leave any existing pad command alone.
        if(sLightCommands[0][iPad].empty())
            continue;

        SMXConfig config;
        if(!m_pDevices[iPad]->GetConfigLocked(config))
            continue;

        // If this pad is firmware version 4, send the 4 command.  Otherwise, leave the 4 command
        // empty and no command will be sent.
        PendingCommand *pPending4Commands = &m_aPendingLightsCommands[m_aPendingLightsCommands.size()-3]; // 3
        if(config.masterVersion >= 4)
            pPending4Commands->sPadCommand[iPad] = sLightCommands[0][iPad];
        else
            pPending4Commands->sPadCommand[iPad] = "";

        PendingCommand *pPending2Commands = &m_aPendingLightsCommands[m_aPendingLightsCommands.size()-2]; // 2
        pPending2Commands->sPadCommand[iPad] = sLightCommands[1][iPad];

        PendingCommand *pPending3Commands = &m_aPendingLightsCommands[m_aPendingLightsCommands.size()-1]; // 3
        pPending3Commands->sPadCommand[iPad] = sLightCommands[2][iPad];
    }

    // Wake up the I/O thread if it's blocking on WaitForMultipleObjectsEx.
    SetEvent(m_hEvent->value());
}

void SMX::SMXManager::ReenableAutoLights()
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    // Clear any pending lights commands, so we don't re-disable auto-lighting by sending a
    // lights command after we enable it.  If we've sent the first half of a lights update
    // and this causes us to not send the second half, the controller will just discard it.
    m_aPendingLightsCommands.clear();
    for(int iPad = 0; iPad < 2; ++iPad)
        m_pDevices[iPad]->SendCommandLocked(string("S 1\n", 4));
}

// Check to see if we should send any commands in m_aPendingLightsCommands.
void SMX::SMXManager::SendLightUpdates()
{
    g_Lock.AssertLockedByCurrentThread();

    // If previous lights commands are being sent, wait for them to complete before
    // queueing more.
    if(m_iLightsCommandsInProgress > 0)
        return;

    // If we have more than one command queued, we can queue several of them if we're
    // before fTimeToSend.  For the V4 pads that require more commands, this lets us queue
    // the whole lights update at once.  V3 pads require us to time commands, so we can't
    // spam both lights commands at once, which is handled by fTimeToSend.
    while( !m_aPendingLightsCommands.empty() )
    {
        // Send the lights command for each pad.  If either pad isn't connected, this won't do
        // anything.
        const PendingCommand &command = m_aPendingLightsCommands[0];

        // See if it's time to send this command.
        if(command.fTimeToSend > GetMonotonicTime())
            break;

        for(int iPad = 0; iPad < 2; ++iPad)
        {
            if(!command.sPadCommand[iPad].empty())
            {
                // Count the number of commands we've queued.  We won't send any more until
                // this reaches 0 and all queued commands were sent.
                m_iLightsCommandsInProgress++;

                // The completion callback is guaranteed to always be called, even if the controller
                // disconnects and the command wasn't sent.
                m_pDevices[iPad]->SendCommandLocked(command.sPadCommand[iPad], [this, iPad](string response) {
                    g_Lock.AssertLockedByCurrentThread();
                    m_iLightsCommandsInProgress--;
                });
            }
        }

        // Remove the command we've sent.
        m_aPendingLightsCommands.erase(m_aPendingLightsCommands.begin(), m_aPendingLightsCommands.begin()+1);
    }
}

void SMX::SMXManager::SetPanelTestMode(PanelTestMode mode)
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex Lock(g_Lock);
    m_PanelTestMode = mode;
}

void SMX::SMXManager::UpdatePanelTestMode()
{
    // If the test mode has changed, send the new test mode.
    //
    // When the test mode is enabled, send the test mode again periodically, or it'll time
    // out on the master and be turned off.  Don't repeat the PanelTestMode_Off command.
    g_Lock.AssertLockedByCurrentThread();
    uint32_t now = GetTickCount();
    if(m_PanelTestMode == m_LastSentPanelTestMode && 
        (m_PanelTestMode == PanelTestMode_Off || now - m_SentPanelTestModeAtTicks < 1000))
        return;

    // When we first send the test mode command (not for repeats), turn off lights.
    if(m_LastSentPanelTestMode == PanelTestMode_Off)
    {
        // The 'l' command used to set lights, but it's now only used to turn lights off
        // for cases like this.
        string sData = "l";
        sData.append(108, 0);
        sData += "\n";
        for(int iPad = 0; iPad < 2; ++iPad)
            m_pDevices[iPad]->SendCommandLocked(sData);
    }

    m_SentPanelTestModeAtTicks = now;
    m_LastSentPanelTestMode = m_PanelTestMode;
    for(int iPad = 0; iPad < 2; ++iPad)
        m_pDevices[iPad]->SendCommandLocked(ssprintf("t %c\n", m_PanelTestMode));
}

// Assign a serial number to master controllers if one isn't already assigned.  This
// will have no effect if a serial is already set.
//
// We just assign a random number.  The serial number will be used as the USB serial
// number, and can be queried in SMXInfo.
void SMX::SMXManager::SetSerialNumbers()
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    m_aPendingLightsCommands.clear();
    for(int iPad = 0; iPad < 2; ++iPad)
    {
        string sData = "s";
        uint8_t serial[16];
        SMX::GenerateRandom(serial, sizeof(serial));
        sData.append((char *) serial, sizeof(serial));
        sData.append(1, '\n');

        m_pDevices[iPad]->SendCommandLocked(sData);
    }
}

void SMX::SMXManager::RunInHelperThread(function<void()> func)
{
    m_UserCallbackThread.RunInThread(func);
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



