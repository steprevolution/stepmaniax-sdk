#include "SMXHelperThread.h"

#include <windows.h>
using namespace SMX;

SMX::SMXHelperThread::SMXHelperThread(const string &sThreadName)
{
    m_hEvent = make_shared<AutoCloseHandle>(CreateEvent(NULL, false, false, NULL));

    // Start the thread.
    m_hThread = CreateThread(NULL, 0, ThreadMainStart, this, 0, &m_iThreadId);
    SMX::SetThreadName(m_iThreadId, sThreadName);
}

SMX::SMXHelperThread::~SMXHelperThread()
{
}

void SMX::SMXHelperThread::SetHighPriority(bool bHighPriority)
{
    SetThreadPriority( m_hThread, THREAD_PRIORITY_HIGHEST );
}

DWORD WINAPI SMX::SMXHelperThread::ThreadMainStart(void *self_)
{
    SMXHelperThread *self = (SMXHelperThread *) self_;
    self->ThreadMain();
    return 0;
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

        WaitForSingleObjectEx(m_hEvent->value(), 250, true);
        m_Lock.Lock();
    }
    m_Lock.Unlock();
}

void SMX::SMXHelperThread::Shutdown()
{
    if(m_hThread == INVALID_HANDLE_VALUE)
        return;

    // Tell the thread to shut down, and wait for it before returning.
    m_bShutdown = true;
    SetEvent(m_hEvent->value());

    WaitForSingleObject(m_hThread, INFINITE);
    m_hThread = INVALID_HANDLE_VALUE;
}

void SMX::SMXHelperThread::RunInThread(function<void()> func)
{
    m_Lock.AssertNotLockedByCurrentThread();

    // Add func to the list, and poke the event to wake up the thread if needed.
    m_Lock.Lock();
    m_FunctionsToCall.push_back(func);
    SetEvent(m_hEvent->value());
    m_Lock.Unlock();
}

bool SMX::SMXHelperThread::IsCurrentThread() const
{
    return GetCurrentThreadId() == m_iThreadId;
}
