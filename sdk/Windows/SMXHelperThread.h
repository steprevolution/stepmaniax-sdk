#ifndef SMXHelperThread_h
#define SMXHelperThread_h

#include "Helpers.h"

#include <functional>
#include <vector>
#include <memory>
using namespace std;

namespace SMX
{
class SMXHelperThread
{
public:
    SMXHelperThread(const string &sThreadName);
    ~SMXHelperThread();

    // Raise the priority of the helper thread.
    void SetHighPriority(bool bHighPriority);

    // Shut down the thread.  Any calls queued by RunInThread will complete before
    // this returns.
    void Shutdown();

    // Call func asynchronously from the helper thread.
    void RunInThread(function<void()> func);

    // Return true if this is the calling thread.
    bool IsCurrentThread() const;

private:
    static DWORD WINAPI ThreadMainStart(void *self_);
    void ThreadMain();

    DWORD m_iThreadId = 0;
    SMX::Mutex m_Lock;
    shared_ptr<SMX::AutoCloseHandle> m_hEvent;
    bool m_bShutdown = false;
    HANDLE m_hThread = INVALID_HANDLE_VALUE;
    vector<function<void()>> m_FunctionsToCall;
};
}

#endif
