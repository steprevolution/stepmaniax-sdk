#ifndef SMXHelperThread_h
#define SMXHelperThread_h

#include "Helpers.h"
#include "SMXThread.h"

#include <functional>
#include <vector>
#include <memory>
using namespace std;

namespace SMX
{
class SMXHelperThread: public SMXThread
{
public:
    SMXHelperThread(const string &sThreadName);
   
    // Call func asynchronously from the helper thread.
    void RunInThread(function<void()> func);

private:
    void ThreadMain();

    // Helper threads use their independent lock.
    SMX::Mutex m_Lock;
    vector<function<void()>> m_FunctionsToCall;
};
}

#endif
