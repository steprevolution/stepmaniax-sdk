#include "SMXThread.h"

using namespace std;
using namespace SMX;

SMXThread::SMXThread(Mutex &lock):
    m_Lock(lock),
    m_Event(lock)
{
}

void SMX::SMXThread::SetHighPriority(bool bHighPriority)
{
    if(m_hThread == INVALID_HANDLE_VALUE)
        throw exception("SetHighPriority called while the thread isn't running");

    SetThreadPriority(m_hThread, THREAD_PRIORITY_HIGHEST);
}

bool SMX::SMXThread::IsCurrentThread() const
{
    return GetCurrentThreadId() == m_iThreadId;
}

void SMXThread::Start(string name)
{
    // Start the thread.
    m_hThread = CreateThread(NULL, 0, ThreadMainStart, this, 0, &m_iThreadId);
    SMX::SetThreadName(m_iThreadId, name);
}

void SMXThread::Shutdown()
{
    m_Lock.AssertNotLockedByCurrentThread();

    // Shut down the thread and wait for it to exit.
    m_bShutdown = true;
    m_Event.Set();

    WaitForSingleObject(m_hThread, INFINITE);
    m_hThread = INVALID_HANDLE_VALUE;
}

DWORD WINAPI SMXThread::ThreadMainStart(void *self_)
{
    SMXThread *self = (SMXThread *) self_;
    self->ThreadMain();
    return 0;
}
