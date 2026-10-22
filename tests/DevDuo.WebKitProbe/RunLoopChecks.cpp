#include "config.h"
#include <wtf/MainThread.h>
#include <wtf/RunLoop.h>
#include <wtf/win/Win32HandleSource.h>
#include "win/HostProcessCapabilityTransfer.h"
#include <string>
#include <windows.h>
#include <chrono>
#include <cstdio>
#include <functional>
#include <memory>
#include <limits>
#include <thread>
#include <atomic>

using Clock = std::chrono::steady_clock;
struct Timer final : WTF::RunLoop::TimerBase {
    explicit Timer(std::function<void()> callback)
        : TimerBase(WTF::Ref { WTF::RunLoop::currentSingleton() }, "Native run-loop acceptance")
        , callback(std::move(callback)) { }
    void fired() final { callback(); }
    std::function<void()> callback;
};
static void pump(int milliseconds)
{
    auto end = Clock::now() + std::chrono::milliseconds(milliseconds);
    while (Clock::now() < end) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) continue;
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        Sleep(1);
    }
}
static bool check(bool condition, const char* name)
{
    std::printf("%s %s\n", condition ? "PASS" : "FAIL", name);
    return condition;
}
static bool checkHandleCapabilities()
{
    bool passed = true;
    auto processID = GetCurrentProcessId();
    auto makeCapability = [] {
        HANDLE handle = INVALID_HANDLE_VALUE;
        if (!DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), GetCurrentProcess(), &handle, PROCESS_DUP_HANDLE, FALSE, 0))
            return Win32Handle { };
        return Win32Handle::adopt(handle);
    };
    auto event = Win32Handle::adopt(CreateEventW(nullptr, TRUE, TRUE, nullptr));
    auto rawEvent = reinterpret_cast<uintptr_t>(event.get());
    {
        WTF::Win32HandleSource invalidID(0, makeCapability());
        WTF::Win32HandleSource nullHandle(processID, Win32Handle::adopt(nullptr));
        WTF::Win32HandleSource pseudoHandle(processID, Win32Handle::adopt(GetCurrentProcess()));
        passed &= check(!invalidID && !nullHandle && !pseudoHandle, "source registration rejects zero PID, null and pseudo capabilities");
    }
    auto oldSource = std::make_unique<WTF::Win32HandleSource>(processID, makeCapability());
    auto newSource = std::make_unique<WTF::Win32HandleSource>(processID, makeCapability());
    oldSource.reset();
    Win32Handle copyForTransfer { event };
    auto copied = Win32Handle::createFromIPCData(copyForTransfer.toIPCData());
    DWORD flags = 0;
    passed &= check(copied && GetHandleInformation(event.get(), &flags) && WaitForSingleObject(copied.get(), 0) == WAIT_OBJECT_0,
        "new registration survives old owner removal; ordinary transfer retains the original event owner");
    for (auto value : { uintptr_t { 0 }, static_cast<uintptr_t>(-1), static_cast<uintptr_t>(-2), static_cast<uintptr_t>(-12) })
        passed &= check(!Win32Handle::createFromIPCData({ value, processID }), "ordinary IPC rejects null and pseudo payload handles");
    auto transferred = Win32Handle::adopt(CreateEventW(nullptr, TRUE, TRUE, nullptr));
    auto originalTransferred = transferred.get();
    auto received = Win32Handle::createFromIPCData(transferred.toIPCData());
    auto receivedEvent = received && WaitForSingleObject(received.get(), 0) == WAIT_OBJECT_0;
    // Windows can reuse the source handle value for a same-process transfer.
    received = { };
    passed &= check(receivedEvent && !GetHandleInformation(originalTransferred, &flags), "ordinary IPC transfer has one receiving owner and closes on its disposal");
    std::atomic<bool> running { true };
    std::atomic<unsigned> imports { 0 };
    std::atomic<bool> valid { true };
    std::thread importer([&] {
        while (running) {
            Win32Handle copyForTransfer { event };
            auto imported = Win32Handle::createFromIPCData(copyForTransfer.toIPCData());
            if (imported && WaitForSingleObject(imported.get(), 0) != WAIT_OBJECT_0)
                valid = false;
            ++imports;
        }
    });
    while (imports < 100) std::this_thread::yield();
    newSource.reset();
    running = false;
    importer.join();
    passed &= check(valid,
        "concurrent lookup retains a local capability while final owner removal unregisters the source");
    {
        HANDLE queryOnly = INVALID_HANDLE_VALUE;
        passed &= check(DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), GetCurrentProcess(), &queryOnly,
            PROCESS_QUERY_LIMITED_INFORMATION, FALSE, 0), "create a real process capability without duplicate permission");
        WTF::Win32HandleSource insufficient(processID, Win32Handle::adopt(queryOnly));
        passed &= check(insufficient && !Win32Handle::createFromIPCData({ rawEvent, processID }),
            "registered capability permission failure does not fall back to OpenProcess");
    }
    DWORD before = 0, after = 0;
    GetProcessHandleCount(GetCurrentProcess(), &before);
    for (unsigned index = 0; index < 100; ++index) {
        WTF::Win32HandleSource source(processID, makeCapability());
        Win32Handle copyForTransfer { event };
        auto imported = Win32Handle::createFromIPCData(copyForTransfer.toIPCData());
    }
    GetProcessHandleCount(GetCurrentProcess(), &after);
    passed &= check(before == after, "repeated connection registration and teardown release every native process capability");
    return passed;
}
static bool checkPushCancellation()
{
    HANDLE rawCapability = nullptr;
    if (!DuplicateHandle(GetCurrentProcess(), GetCurrentProcess(), GetCurrentProcess(), &rawCapability, PROCESS_DUP_HANDLE, FALSE, 0))
        return check(false, "create local source for push cancellation acceptance");
    auto capability = Win32Handle::adopt(rawCapability);
    auto movedFrom = Win32Handle { capability };
    auto held = WTF::move(movedFrom);
    using Transfer = WebKit::HostProcessCapabilityTransfer;
    bool passed = check(!Transfer::push(capability.get(), WTF::move(movedFrom)) &&
        !Transfer::push(nullptr, Win32Handle { capability }) &&
        !Transfer::push(GetCurrentProcess(), Win32Handle { capability }) &&
        !Transfer::push(capability.get(), Win32Handle::adopt(GetCurrentProcess())),
        "push rejects null and pseudo handles, including a moved-from target, without termination");
    for (bool processed : { false, true }) {
        wchar_t executable[MAX_PATH];
        GetModuleFileNameW(nullptr, executable, MAX_PATH);
        auto command = std::wstring(L"\"") + executable + L"\" --capability-target";
        STARTUPINFOW startup { }; startup.cb = sizeof(startup);
        PROCESS_INFORMATION child { };
        if (!CreateProcessW(nullptr, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &child))
            return check(false, "launch exact owned cancellation child") && passed;
        CloseHandle(child.hThread);
        auto process = Win32Handle::adopt(child.hProcess);
        auto transfer = Transfer::push(capability.get(), Win32Handle { process });
        passed &= check(!!transfer, "push creates a capability owned by the exact target child");
        if (transfer && processed)
            transfer->didProcess();
        transfer = nullptr;
        auto exited = WaitForSingleObject(process.get(), processed ? 0 : 2000) == WAIT_OBJECT_0;
        passed &= check(exited != processed, processed ? "processed acknowledgement releases the cancellation termination guard" :
            "unprocessed cancellation terminates only the exact owned child and releases its pushed capability");
        if (!exited) {
            TerminateProcess(process.get(), 0);
            WaitForSingleObject(process.get(), 2000);
        }
    }
    return passed;
}
int main(int argc, char** argv)
{
    if (argc == 2 && std::string(argv[1]) == "--capability-target") {
        Sleep(10000);
        return 0;
    }

    WTF::initializeMainThread();
    bool passed = checkHandleCapabilities();
    passed &= checkPushCancellation();
    int once = 0;
    Timer oneShot([&] { ++once; });
    oneShot.startOneShot(WTF::Seconds { .02 });
    pump(90);
    passed &= check(once == 1, "external DispatchMessage-only one-shot exactly once");
    int canceled = 0;
    Timer cancel([&] { ++canceled; });
    cancel.startOneShot(WTF::Seconds { .02 });
    cancel.stop();
    pump(70);
    passed &= check(!canceled, "canceled deadline never fires");
    int infiniteFires = 0, finiteAfterInfinity = 0;
    Timer infinite([&] { ++infiniteFires; });
    Timer finite([&] { ++finiteAfterInfinity; });
    infinite.startOneShot(WTF::Seconds { std::numeric_limits<double>::infinity() });
    pump(20);
    infinite.stop();
    finite.startOneShot(WTF::Seconds { .005 });
    pump(50);
    passed &= check(!infiniteFires && finiteAfterInfinity == 1, "infinite deadline cancellation preserves the next finite timer");
    int repeat = 0;
    Timer* repeatingPointer = nullptr;
    Timer repeating([&] { if (++repeat == 3) repeatingPointer->stop(); });
    repeatingPointer = &repeating;
    repeating.startRepeating(WTF::Seconds { .01 });
    pump(120);
    passed &= check(repeat == 3, "external repeating callback rearms and stops exactly");
    int sequence = 0, earlyOrder = 0, lateOrder = 0;
    Timer late([&] { lateOrder = ++sequence; });
    Timer early([&] { earlyOrder = ++sequence; });
    late.startOneShot(WTF::Seconds { .08 });
    pump(10);
    early.startOneShot(WTF::Seconds { .005 });
    pump(130);
    passed &= check(earlyOrder == 1 && lateOrder == 2, "new earlier deadline replaces native arm");
    int nested = 0;
    Timer child([&] { ++nested; });
    Timer parent([&] { child.startOneShot(WTF::Seconds { 0 }); });
    parent.startOneShot(WTF::Seconds { 0 });
    pump(80);
    passed &= check(nested == 1, "zero due and reentrant scheduling fire without polling the WTF loop");
    int destroyed = 0;
    auto removed = std::make_unique<Timer>([&] { ++destroyed; });
    Timer remover([&] { removed.reset(); });
    remover.startOneShot(WTF::Seconds { .005 });
    removed->startOneShot(WTF::Seconds { .02 });
    pump(80);
    passed &= check(!destroyed, "callback can destroy another pending timer safely");
    HWND loopWindow = nullptr;
    for (auto candidate = FindWindowExW(HWND_MESSAGE, nullptr, L"RunLoopMessageWindow", nullptr); candidate;
         candidate = FindWindowExW(HWND_MESSAGE, candidate, L"RunLoopMessageWindow", nullptr)) {
        DWORD process = 0;
        auto thread = GetWindowThreadProcessId(candidate, &process);
        if (thread == GetCurrentThreadId() && process == GetCurrentProcessId()) {
            loopWindow = candidate;
            break;
        }
    }
    if (!check(loopWindow, "flood targets the actual current WTF hidden HWND")) return 1;
    int floodFires = 0;
    double floodElapsed = 0;
    auto started = Clock::now();
    Timer flooded([&] { ++floodFires; floodElapsed = std::chrono::duration<double, std::milli>(Clock::now() - started).count(); });
    flooded.startOneShot(WTF::Seconds { .05 });
    std::thread flood([&] {
        for (int index = 0; index < 180; ++index) {
            PostMessageW(loopWindow, WM_USER + 2, 0, 0);
            Sleep(1);
        }
    });
    pump(250);
    flood.join();
    std::printf("METRIC unchanged-deadline flood callback %.3f ms\n", floodElapsed);
    passed &= check(floodFires == 1 && floodElapsed < 140, "same-deadline message flood cannot postpone the callback");
    int ownRepeats = 0;
    Timer* ownPointer = nullptr;
    Timer own([&] { if (++ownRepeats == 4) { ownPointer->stop(); WTF::RunLoop::currentSingleton().stop(); } });
    ownPointer = &own;
    own.startRepeating(WTF::Seconds { .005 });
    WTF::RunLoop::run();
    pump(50);
    passed &= check(ownRepeats == 4, "WTF own run loop and native deadline do not double-fire");
    int cycled = 0;
    Timer cycle([&] { ++cycled; });
    cycle.startOneShot(WTF::Seconds { .005 });
    auto end = Clock::now() + std::chrono::milliseconds(50);
    while (Clock::now() < end) { WTF::RunLoop::cycle(); Sleep(1); }
    passed &= check(cycled == 1, "WTF cycle and native deadline share one due queue");
    return passed ? 0 : 1;
}
