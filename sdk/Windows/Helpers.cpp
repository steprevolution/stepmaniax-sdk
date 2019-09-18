#include "Helpers.h"
#include <windows.h>
#include <algorithm>
using namespace std;
using namespace SMX;

namespace {
    function<void(const string &log)> g_LogCallback = [](const string &log) {
        printf("%6.3f: %s\n", GetMonotonicTime(), log.c_str());
    };
};

void SMX::Log(string s)
{
    g_LogCallback(s);
}

void SMX::SetLogCallback(function<void(const string &log)> callback)
{
    g_LogCallback = callback;
}

const DWORD MS_VC_EXCEPTION = 0x406D1388;  
#pragma pack(push,8)  
typedef struct tagTHREADNAME_INFO  
{  
    DWORD dwType; // Must be 0x1000.  
    LPCSTR szName; // Pointer to name (in user addr space).  
    DWORD dwThreadID; // Thread ID (-1=caller thread).  
    DWORD dwFlags; // Reserved for future use, must be zero.  
} THREADNAME_INFO;  

#pragma pack(pop)  
void SMX::SetThreadName(DWORD iThreadId, const string &name)
{

    THREADNAME_INFO info;  
    info.dwType = 0x1000;  
    info.szName = name.c_str();  
    info.dwThreadID = iThreadId;
    info.dwFlags = 0;  
#pragma warning(push)  
#pragma warning(disable: 6320 6322)  
    __try{   
        RaiseException(MS_VC_EXCEPTION, 0, sizeof(info) / sizeof(ULONG_PTR), (ULONG_PTR*)&info);  
    }  
    __except (EXCEPTION_EXECUTE_HANDLER) {
    }  
#pragma warning(pop)  
}  

void SMX::StripCrnl(wstring &s)
{
    while(s.size() && (s[s.size()-1] == '\r' || s[s.size()-1] == '\n'))
        s.erase(s.size()-1);
}

wstring SMX::GetErrorString(int err)
{
    wchar_t buf[1024] = L"";
    FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM, 0, err, 0, buf, sizeof(buf), NULL);

    // Fix badly formatted strings returned by FORMAT_MESSAGE_FROM_SYSTEM.
    wstring sResult = buf;
    StripCrnl(sResult);
    return sResult;
}

string SMX::vssprintf(const char *szFormat, va_list argList)
{
    int iChars = vsnprintf(NULL, 0, szFormat, argList);

    string sStr;
    sStr.resize(iChars+1);
    vsnprintf((char *) sStr.data(), iChars+1, szFormat, argList);
    sStr.resize(iChars);

    return sStr;
}

string SMX::ssprintf(const char *fmt, ...)
{
    va_list va;
    va_start(va, fmt);
    return vssprintf(fmt, va);
}

string SMX::BinaryToHex(const void *pData_, int iNumBytes)
{
    const unsigned char *pData = (const unsigned char *) pData_;
    string s;
    for(int i=0; i<iNumBytes; i++)
    {
        unsigned val = pData[i];
        s += ssprintf("%02x", val);
    }
    return s;
}

string SMX::BinaryToHex(const string &sString)
{
    return BinaryToHex(sString.data(), sString.size());
}

bool SMX::GetRandomBytes(void *pData, int iBytes)
{
    HCRYPTPROV hCryptProvider = 0;
    if (!CryptAcquireContext(&hCryptProvider, NULL, MS_DEF_PROV, PROV_RSA_FULL, (CRYPT_VERIFYCONTEXT | CRYPT_MACHINE_KEYSET)) && 
        !CryptAcquireContext(&hCryptProvider, NULL, MS_DEF_PROV, PROV_RSA_FULL, CRYPT_VERIFYCONTEXT | CRYPT_MACHINE_KEYSET | CRYPT_NEWKEYSET))
        return 0;

    bool bSuccess = !!CryptGenRandom(hCryptProvider, iBytes, (uint8_t *) pData);
    CryptReleaseContext(hCryptProvider, 0);
    return bSuccess;
}

// Monotonic timer code from https://stackoverflow.com/questions/24330496.
// Why is this hard?
//
// This code has backwards compatibility to XP, but we only officially support and
// test back to Windows 7, so that code path isn't tested.
typedef struct _KSYSTEM_TIME {
    ULONG LowPart;
    LONG High1Time;
    LONG High2Time;
} KSYSTEM_TIME;
#define KUSER_SHARED_DATA 0x7ffe0000
#define InterruptTime ((KSYSTEM_TIME volatile*)(KUSER_SHARED_DATA + 0x08))
#define InterruptTimeBias ((ULONGLONG volatile*)(KUSER_SHARED_DATA + 0x3b0))

namespace {
    LONGLONG ReadInterruptTime()
    {
        // Reading the InterruptTime from KUSER_SHARED_DATA is much better than
        // using GetTickCount() because it doesn't wrap, and is even a little quicker.
        // This works on all Windows NT versions (NT4 and up).
        LONG timeHigh;
        ULONG timeLow;
        do {
            timeHigh = InterruptTime->High1Time;
            timeLow = InterruptTime->LowPart;
        } while (timeHigh != InterruptTime->High2Time);
        LONGLONG now = ((LONGLONG)timeHigh << 32) + timeLow;
        static LONGLONG d = now;
        return now - d;
    }

    LONGLONG ScaleQpc(LONGLONG qpc)
    {
        // We do the actual scaling in fixed-point rather than floating, to make sure
        // that we don't violate monotonicity due to rounding errors.  There's no
        // need to cache QueryPerformanceFrequency().
        LARGE_INTEGER frequency;
        QueryPerformanceFrequency(&frequency);
        double fraction = 10000000/double(frequency.QuadPart);
        LONGLONG denom = 1024;
        LONGLONG numer = max(1LL, (LONGLONG)(fraction*denom + 0.5));
        return qpc * numer / denom;
    }

    ULONGLONG ReadUnbiasedQpc()
    {
        // We remove the suspend bias added to QueryPerformanceCounter results by
        // subtracting the interrupt time bias, which is not strictly speaking legal,
        // but the units are correct and I think it's impossible for the resulting
        // "unbiased QPC" value to go backwards.
        LONGLONG interruptTimeBias, qpc;
        do {
            interruptTimeBias = *InterruptTimeBias;
            LARGE_INTEGER counter;
            QueryPerformanceCounter(&counter);
            qpc = counter.QuadPart;
        } while (interruptTimeBias != *InterruptTimeBias);
        static std::pair<LONGLONG,LONGLONG> d(qpc, interruptTimeBias);
        return ScaleQpc(qpc - d.first) - (interruptTimeBias - d.second);
    }

    bool Win7OrLater()
    {
        static int iWin7OrLater = -1;
        if(iWin7OrLater != -1)
            return bool(iWin7OrLater);

        OSVERSIONINFOW ver = { sizeof(OSVERSIONINFOW), };
        GetVersionEx(&ver);
        iWin7OrLater = (ver.dwMajorVersion > 6 || (ver.dwMajorVersion == 6 && ver.dwMinorVersion >= 1));
        return bool(iWin7OrLater);
    }
}

/// getMonotonicTime() returns the time elapsed since the application's first
/// call to getMonotonicTime(), in 100ns units.  The values returned are
/// guaranteed to be monotonic.  The time ticks in 15ms resolution and advances
/// during suspend on XP and Vista, but we manage to avoid this on Windows 7
/// and 8, which also use a high-precision timer.  The time does not wrap after
/// 49 days.
double SMX::GetMonotonicTime()
{
    // On Windows XP and earlier, QueryPerformanceCounter is not monotonic so we
    // steer well clear of it; on Vista, it's just a bit slow.
    uint64_t iTime = Win7OrLater()? ReadUnbiasedQpc() : ReadInterruptTime();
    return iTime / 10000000.0;
}

void SMX::GenerateRandom(void *pOut, int iSize)
{
    // These calls shouldn't fail.
    HCRYPTPROV cryptProv;
    if(!CryptAcquireContext(&cryptProv, nullptr,
        L"Microsoft Base Cryptographic Provider v1.0",
        PROV_RSA_FULL, CRYPT_VERIFYCONTEXT))
        throw exception("CryptAcquireContext error");

    if(!CryptGenRandom(cryptProv, iSize, (BYTE *) pOut)) 
        throw exception("CryptGenRandom error");

    if(!CryptReleaseContext(cryptProv, 0))
        throw exception("CryptReleaseContext error");
}

const char *SMX::CreateError(string error)
{
    // Store the string in a static so it doesn't get deallocated.
    static string buf;
    buf = error;
    return buf.c_str();
}

SMX::AutoCloseHandle::AutoCloseHandle(HANDLE h)
{
    handle = h;
}

SMX::AutoCloseHandle::~AutoCloseHandle()
{
    if(handle != INVALID_HANDLE_VALUE)
        CloseHandle(handle);
}

SMX::Mutex::Mutex()
{
    m_hLock = CreateMutex(NULL, false, NULL);
}

SMX::Mutex::~Mutex()
{
    CloseHandle(m_hLock);
}

void SMX::Mutex::Lock()
{
    WaitForSingleObject(m_hLock, INFINITE);
    m_iLockedByThread = GetCurrentThreadId();
}

void SMX::Mutex::Unlock()
{
    m_iLockedByThread = 0;
    ReleaseMutex(m_hLock);
}

void SMX::Mutex::AssertNotLockedByCurrentThread()
{
    if(m_iLockedByThread == GetCurrentThreadId())
        throw exception("Expected to not be locked");
}

void SMX::Mutex::AssertLockedByCurrentThread()
{
    if(m_iLockedByThread != GetCurrentThreadId())
        throw exception("Expected to be locked");
}

SMX::LockMutex::LockMutex(SMX::Mutex &mutex):
    m_Mutex(mutex)
{
    m_Mutex.AssertNotLockedByCurrentThread();
    m_Mutex.Lock();
}

SMX::LockMutex::~LockMutex()
{
    m_Mutex.AssertLockedByCurrentThread();
    m_Mutex.Unlock();
}

// This is a helper to let the config tool open a window, which has no freopen.
// This isn't exposed in SMX.h.
extern "C" __declspec(dllexport) void SMX_Internal_OpenConsole()
{
    AllocConsole();
    freopen("CONOUT$","wb", stdout);
    freopen("CONOUT$","wb", stderr);
}
