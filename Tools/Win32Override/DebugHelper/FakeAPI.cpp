#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdio.h>
#include <Shlobj.h>
#include <Shlwapi.h>
#include <list>
#include "../_Common_Files/GenericFakeAPI.h"
// You just need to edit this file to add new fake api 
// WARNING YOUR FAKE API MUST HAVE THE SAME PARAMETERS AND CALLING CONVENTION AS THE REAL ONE,
//                  ELSE YOU WILL GET STACK ERRORS

///////////////////////////////////////////////////////////////////////////////
// fake API prototype MUST HAVE THE SAME PARAMETERS 
// for the same calling convention see MSDN : 
// "Using a Microsoft modifier such as __cdecl on a data declaration is an outdated practice"
///////////////////////////////////////////////////////////////////////////////

#define LOGFILE _T("DebugHelper.txt")

#define STATUS_SUCCESS                   ((NTSTATUS)0x00000000L)    // ntsubauth

typedef enum _THREADINFOCLASS
{
    ThreadHideFromDebugger=17
} THREADINFOCLASS;

typedef NTSTATUS (WINAPI *ptrNtSetInformationThread)
(
    __in HANDLE ThreadHandle,
    __in THREADINFOCLASS ThreadInformationClass,
    __in_bcount(ThreadInformationLength) PVOID ThreadInformation,
    __in ULONG ThreadInformationLength
);

typedef NTSTATUS (WINAPI *ptrNtQueryInformationThread)(
    _In_      HANDLE          ThreadHandle,
    _In_      THREADINFOCLASS ThreadInformationClass,
    _Inout_   PVOID           ThreadInformation,
    _In_      ULONG           ThreadInformationLength,
    _Out_opt_ PULONG          ReturnLength
);

static FILE* OpenLogFile();
static void LogPrintf(TCHAR *format, ...);
static void LogFlush();
static void LogData(BYTE *data, unsigned int length);

static BOOL WINAPI mIsDebuggerPresent(void);

static HANDLE WINAPI mCreateFileA(
    _In_     LPCSTR                lpFileName,
    _In_     DWORD                 dwDesiredAccess,
    _In_     DWORD                 dwShareMode,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    _In_     DWORD                 dwCreationDisposition,
    _In_     DWORD                 dwFlagsAndAttributes,
    _In_opt_ HANDLE                hTemplateFile
);

static BOOL WINAPI mReadFile(
    _In_        HANDLE       hFile,
    _Out_       LPVOID       lpBuffer,
    _In_        DWORD        nNumberOfBytesToRead,
    _Out_opt_   LPDWORD      lpNumberOfBytesRead,
    _Inout_opt_ LPOVERLAPPED lpOverlapped
);

static BOOL WINAPI mWriteFile(
    _In_        HANDLE       hFile,
    _In_        LPCVOID      lpBuffer,
    _In_        DWORD        nNumberOfBytesToWrite,
    _Out_opt_   LPDWORD      lpNumberOfBytesWritten,
    _Inout_opt_ LPOVERLAPPED lpOverlapped
);

static BOOL WINAPI mCloseHandle(
    _In_ HANDLE hObject
);

static LPVOID WINAPI mHeapAlloc(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ SIZE_T dwBytes
);

static LPVOID WINAPI mHeapReAlloc(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ LPVOID lpMem,
    _In_ SIZE_T dwBytes
);

static BOOL WINAPI mHeapFree(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ LPVOID lpMem
);

static NTSTATUS WINAPI mNtSetInformationThread(
    __in HANDLE ThreadHandle,
    __in THREADINFOCLASS ThreadInformationClass,
    __in_bcount(ThreadInformationLength) PVOID ThreadInformation,
    __in ULONG ThreadInformationLength
);

static void mDbgUiRemoteBreakin(void);

static ptrNtSetInformationThread pNtSetInformationThread = NULL;
static ptrNtQueryInformationThread pNtQueryInformationThread = NULL;
static FILE *fLog = NULL;
static std::list<HANDLE> FileWatchList;
static std::list<LPVOID> MemWatchList;
static HANDLE hLastLogRFile = INVALID_HANDLE_VALUE;
static HANDLE hLastLogWFile = INVALID_HANDLE_VALUE;

///////////////////////////////////////////////////////////////////////////////
// fake API array. Redirection are defined here
///////////////////////////////////////////////////////////////////////////////
STRUCT_FAKE_API pArrayFakeAPI[]=
{
    // library name ,function name, function handler, stack size (required to allocate enough stack space), FirstBytesCanExecuteAnywhereSize (optional put to 0 if you don't know it's meaning)
    //                                                stack size= sum(StackSizeOf(ParameterType))           Same as monitoring file keyword (see monitoring file advanced syntax)
    {_T("Kernel32.dll"),_T("IsDebuggerPresent"),(FARPROC)mIsDebuggerPresent,0,0},
    {_T("Kernel32.dll"),_T("CreateFileA"),(FARPROC)mCreateFileA,StackSizeOf(LPCSTR)+StackSizeOf(DWORD)+StackSizeOf(DWORD)+StackSizeOf(LPSECURITY_ATTRIBUTES)+StackSizeOf(DWORD)+StackSizeOf(DWORD)+StackSizeOf(HANDLE),0 },
    {_T("Kernel32.dll"),_T("ReadFile"),(FARPROC)mReadFile,StackSizeOf(HANDLE)+StackSizeOf(LPVOID)+StackSizeOf(DWORD)+StackSizeOf(LPDWORD)+StackSizeOf(LPOVERLAPPED),0 },
    {_T("Kernel32.dll"),_T("WriteFile"),(FARPROC)mWriteFile,StackSizeOf(HANDLE)+StackSizeOf(LPCVOID)+StackSizeOf(DWORD)+StackSizeOf(LPDWORD)+StackSizeOf(LPOVERLAPPED),0 },
    {_T("Kernel32.dll"),_T("CloseHandle"),(FARPROC)mCloseHandle,StackSizeOf(HANDLE),0 },
    {_T("Kernel32.dll"),_T("HeapAlloc"),(FARPROC)mHeapAlloc,StackSizeOf(HANDLE)+StackSizeOf(DWORD)+StackSizeOf(SIZE_T),0 },
    {_T("Kernel32.dll"),_T("HeapReAlloc"),(FARPROC)mHeapReAlloc,StackSizeOf(HANDLE) + StackSizeOf(DWORD)+StackSizeOf(LPVOID)+StackSizeOf(SIZE_T),0 },
    {_T("Kernel32.dll"),_T("HeapFree"),(FARPROC)mHeapFree,StackSizeOf(HANDLE)+StackSizeOf(DWORD)+StackSizeOf(LPVOID),0 },
    {_T("Ntdll.dll"),_T("NtSetInformationThread"),(FARPROC)mNtSetInformationThread,StackSizeOf(HANDLE)+StackSizeOf(THREADINFOCLASS)+StackSizeOf(PVOID)+StackSizeOf(ULONG),0 },
    {_T("Ntdll.dll"),_T("DbgUiRemoteBreakin"),(FARPROC)mDbgUiRemoteBreakin,0,0 },
    {_T(""),_T(""),NULL,0,0}// last element for ending loops
};

///////////////////////////////////////////////////////////////////////////////
// Before API call array. Redirection are defined here
///////////////////////////////////////////////////////////////////////////////
STRUCT_FAKE_API_WITH_USERPARAM pArrayBeforeAPICall[]=
{
    // library name ,function name, function handler, stack size (required to allocate enough stack space), FirstBytesCanExecuteAnywhereSize (optional put to 0 if you don't know it's meaning),userParam : a value that will be post back to you when your hook will be called
    //                                                stack size= sum(StackSizeOf(ParameterType))           Same as monitoring file keyword (see monitoring file advanced syntax)
    {_T(""),_T(""),NULL,0,0,0}// last element for ending loops
};

///////////////////////////////////////////////////////////////////////////////
// After API call array. Redirection are defined here
///////////////////////////////////////////////////////////////////////////////
STRUCT_FAKE_API_WITH_USERPARAM pArrayAfterAPICall[]=
{
    // library name ,function name, function handler, stack size (required to allocate enough stack space), FirstBytesCanExecuteAnywhereSize (optional put to 0 if you don't know it's meaning),userParam : a value that will be post back to you when your hook will be called
    //                                                stack size= sum(StackSizeOf(ParameterType))           Same as monitoring file keyword (see monitoring file advanced syntax)
    {_T(""),_T(""),NULL,0,0,0}// last element for ending loops
};

BOOL WINAPI DllMain(HINSTANCE hInstDLL, DWORD dwReason, PVOID pvReserved)
{
	UNREFERENCED_PARAMETER(hInstDLL);
    UNREFERENCED_PARAMETER(pvReserved);
    switch (dwReason)
    {
        case DLL_PROCESS_ATTACH:
            {
                // get func address
                HMODULE hNtdll=GetModuleHandle(_T("ntdll.dll"));
                if (hNtdll != NULL)
                {
                    pNtSetInformationThread=(ptrNtSetInformationThread)GetProcAddress(hNtdll,"NtSetInformationThread");
                    pNtQueryInformationThread=(ptrNtQueryInformationThread)GetProcAddress(hNtdll, "NtQueryInformationThread");
                }
                if (pNtSetInformationThread == NULL || pNtQueryInformationThread == NULL)
                {
                    return FALSE;
                }
                fLog = OpenLogFile();
            }
            break;

        case DLL_PROCESS_DETACH:
            if (fLog != NULL)
            {
                fclose(fLog);
                fLog = NULL;
            }
            break;
    }

    return TRUE;
}

///////////////////////////////////////////////////////////////////////////////
///////////////////////////// NEW API DEFINITION //////////////////////////////
/////////////////////// You don't need to export these functions //////////////
///////////////////////////////////////////////////////////////////////////////

FILE* OpenLogFile()
{
    TCHAR szPath[MAX_PATH];

    if (SUCCEEDED(SHGetFolderPath(NULL,
        CSIDL_PERSONAL | CSIDL_FLAG_CREATE,
        NULL,
        0,
        szPath)))
    {
        if (PathAppend(szPath, LOGFILE))
        {
            return _tfopen(szPath, _T("wtc"));
        }
    }
    return NULL;
}

void LogPrintf(TCHAR *format, ...)
{
    va_list args;

    if (fLog != NULL)
    {
        if (hLastLogRFile != INVALID_HANDLE_VALUE || hLastLogWFile != INVALID_HANDLE_VALUE)
        {
            hLastLogRFile = INVALID_HANDLE_VALUE;
            hLastLogWFile = INVALID_HANDLE_VALUE;
            _fputts(_T("\n"), fLog);
        }
        va_start(args, format);
        _vftprintf(fLog, format, args);
        va_end(args);
    }
}

void LogFlush()
{
    if (fLog != NULL)
    {
        fflush(fLog);
    }
}

void LogData(BYTE *data, unsigned int length)
{
    if (fLog == NULL || data == NULL || length == 0 || length > 0x100)
    {
        return;
    }

    for (unsigned int i = 0; i < length; i++)
    {
        if ((i > 0) && (i % 64 == 0))
        {
            _fputts(_T("\n"), fLog);
        }
        _ftprintf(fLog, _T("%02X "), (unsigned int)data[i]);
    }
}

BOOL WINAPI mIsDebuggerPresent(void)
{
    LogPrintf(_T("IsDebuggerPresent\n"));
    LogFlush();
    return FALSE;
}

HANDLE WINAPI mCreateFileA(
    _In_     LPCSTR                lpFileName,
    _In_     DWORD                 dwDesiredAccess,
    _In_     DWORD                 dwShareMode,
    _In_opt_ LPSECURITY_ATTRIBUTES lpSecurityAttributes,
    _In_     DWORD                 dwCreationDisposition,
    _In_     DWORD                 dwFlagsAndAttributes,
    _In_opt_ HANDLE                hTemplateFile
)
{
    HANDLE hFile = CreateFileA(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes, dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
    BOOL bEnableLog = FALSE;
    if ((dwDesiredAccess & GENERIC_READ) != 0 && hFile != INVALID_HANDLE_VALUE)
    {
        bEnableLog = TRUE;
    }
    if (bEnableLog)
    {
        LogPrintf(_T("CreateFileA OK: %S %08p\n"), lpFileName, hFile);
        LogFlush();
        FileWatchList.push_back(hFile);
    }
    return hFile;
}

BOOL WINAPI mReadFile(
    _In_        HANDLE       hFile,
    _Out_       LPVOID       lpBuffer,
    _In_        DWORD        nNumberOfBytesToRead,
    _Out_opt_   LPDWORD      lpNumberOfBytesRead,
    _Inout_opt_ LPOVERLAPPED lpOverlapped
)
{
    BOOL bResult = ReadFile(hFile, lpBuffer, nNumberOfBytesToRead, lpNumberOfBytesRead, lpOverlapped);
    bool found = std::find(FileWatchList.begin(), FileWatchList.end(), hFile) != FileWatchList.end();
    if (bResult && found)
    {
        if (hLastLogRFile != hFile)
        {
            LogPrintf(_T("ReadFile: %08p (%u)="), hFile, nNumberOfBytesToRead);
            hLastLogRFile = hFile;
        }
        LogData((BYTE *)lpBuffer, nNumberOfBytesToRead);
    }

    return bResult;
}

BOOL WINAPI mWriteFile(
    _In_        HANDLE       hFile,
    _In_        LPCVOID      lpBuffer,
    _In_        DWORD        nNumberOfBytesToWrite,
    _Out_opt_   LPDWORD      lpNumberOfBytesWritten,
    _Inout_opt_ LPOVERLAPPED lpOverlapped
)
{
    BOOL bResult = WriteFile(hFile, lpBuffer, nNumberOfBytesToWrite, lpNumberOfBytesWritten, lpOverlapped);
    bool found = std::find(FileWatchList.begin(), FileWatchList.end(), hFile) != FileWatchList.end();
    if (bResult && found)
    {
        if (hLastLogWFile != hFile)
        {
            LogPrintf(_T("WriteFile: %08p (%u)="), hFile, nNumberOfBytesToWrite);
            hLastLogWFile = hFile;
        }
        LogData((BYTE *) lpBuffer, nNumberOfBytesToWrite);
    }

    return bResult;
}

BOOL WINAPI mCloseHandle(
    _In_ HANDLE hObject
)
{
    bool found = std::find(FileWatchList.begin(), FileWatchList.end(), hObject) != FileWatchList.end();
    if (found)
    {
        FileWatchList.remove(hObject);
        LogPrintf(_T("CloseHandle: %08p\n"), hObject);
        LogFlush();
    }
    return CloseHandle(hObject);
}

LPVOID WINAPI mHeapAlloc(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ SIZE_T dwBytes
)
{
    LPVOID pMem = HeapAlloc(hHeap, dwFlags, dwBytes);
    if (pMem != NULL && dwBytes > 0x100)
    {
        LogPrintf(_T("HeapAlloc: %u=%08p\n"), dwBytes, pMem);
        bool found = std::find(MemWatchList.begin(), MemWatchList.end(), pMem) != MemWatchList.end();
        if (!found)
        {
            MemWatchList.push_back(pMem);
        }
        else
        {
            LogPrintf(_T("HeapAlloc: Existing!\n"));
        }
    }
    return pMem;
}

LPVOID WINAPI mHeapReAlloc(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ LPVOID lpMem,
    _In_ SIZE_T dwBytes
)
{
    LPVOID pMem = HeapReAlloc(hHeap, dwFlags, lpMem, dwBytes);
    if (pMem != NULL)
    {
        bool found = std::find(MemWatchList.begin(), MemWatchList.end(), lpMem) != MemWatchList.end();
        if (!found)
        {
            if (dwBytes > 100)
            {
                LogPrintf(_T("HeapReAlloc: New %08p %u=%08p\n"), lpMem, dwBytes, pMem);
            }
        }
        else
        {
            LogPrintf(_T("HeapReAlloc: Replace %08p %u=%08p\n"), lpMem, dwBytes, pMem);
            MemWatchList.remove(lpMem);
        }
        MemWatchList.push_back(pMem);
    }
    return pMem;
}

BOOL WINAPI mHeapFree(
    _In_ HANDLE hHeap,
    _In_ DWORD  dwFlags,
    _In_ LPVOID lpMem
)
{
    bool found = std::find(MemWatchList.begin(), MemWatchList.end(), lpMem) != MemWatchList.end();
    if (found)
    {
        SIZE_T size = HeapSize(hHeap, dwFlags, lpMem);
        LogPrintf(_T("HeapFree: %08p=%u\n"), lpMem, size);
        MemWatchList.remove(lpMem);
    }
    return HeapFree(hHeap, dwFlags, lpMem);
}

NTSTATUS WINAPI mNtSetInformationThread(
    __in HANDLE ThreadHandle,
    __in THREADINFOCLASS ThreadInformationClass,
    __in_bcount(ThreadInformationLength) PVOID ThreadInformation,
    __in ULONG ThreadInformationLength
)
{
    LogPrintf(_T("NtSetInformationThread: %08p %08X %08p %u\n"), ThreadHandle, ThreadInformationClass, ThreadInformation, ThreadInformationLength);

    if (ThreadInformationClass == ThreadHideFromDebugger && ThreadInformation == NULL && ThreadInformationLength == 0)
    {
        BOOLEAN value = FALSE;
        if (pNtQueryInformationThread(ThreadHandle, ThreadInformationClass, &value, sizeof(value), 0) == STATUS_SUCCESS)
        {
            LogPrintf(_T("NtQueryInformationThread: %u\n"), (unsigned int)value);
        }
        else
        {
            LogPrintf(_T("NtQueryInformationThread failed!\n"));
        }
        LogPrintf(_T("Override NtSetInformationThread: STATUS_SUCCESS\n"));
        LogFlush();
        return STATUS_SUCCESS;
    }

    LogPrintf(_T("Redirect to NtSetInformationThread\n"));
    return pNtSetInformationThread(ThreadHandle, ThreadInformationClass, ThreadInformation, ThreadInformationLength);
}

void __declspec(naked) mDbgUiRemoteBreakin(void)
{
    __asm
    {
        int 3
        ret
    }
}
