#ifndef SMXThread_h
#define SMXThread_h

// A base class for a thread.
#include "Helpers.h"
#include <string>

namespace SMX
{

class SMXThread
{
public:
    SMXThread(SMX::Mutex &lock);

    // Raise the priority of the thread.
    void SetHighPriority(bool bHighPriority);

    // Start the thread, giving it a name for debugging.
    void Start(std::string name);

    // Shut down the thread.  This function won't return until the thread
    // has been stopped.
    void Shutdown();

    // Return true if this is the calling thread.
    bool IsCurrentThread() const;

    // The derived class implements this.
    virtual void ThreadMain() = 0;

protected:
    static DWORD WINAPI ThreadMainStart(void *self);

    SMX::Mutex &m_Lock;
    SMX::Event m_Event;
    bool m_bShutdown = false;

private:
    HANDLE m_hThread = INVALID_HANDLE_VALUE;
    DWORD m_iThreadId = 0;
};
}

#endif
