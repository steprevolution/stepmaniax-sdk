#include "SMXHelperThread.h"
using namespace SMX;

SMX::SMXHelperThread::SMXHelperThread(const string &sThreadName):
    SMXThread(m_Lock)
{
    Start(sThreadName);
}

void SMX::SMXHelperThread::ThreadMain()
{
    m_Lock.Lock();
    while(true)
    {
        vector<function<void()>> funcs;
        swap(m_FunctionsToCall, funcs);

        // If we're shutting down and have no more functions to call, stop.
        if(funcs.empty() && m_bShutdown)
            break;

        // Unlock while we call the queued functions.
        m_Lock.Unlock();
        for(auto &func: funcs)
            func();
        m_Lock.Lock();

        m_Event.Wait(250);
    }
    m_Lock.Unlock();
}

void SMX::SMXHelperThread::RunInThread(function<void()> func)
{
    m_Lock.AssertNotLockedByCurrentThread();

    // Add func to the list, and poke the event to wake up the thread if needed.
    m_Lock.Lock();
    m_FunctionsToCall.push_back(func);
    m_Event.Set();
    m_Lock.Unlock();
}
