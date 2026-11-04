#include "config.h"
#include <WebCore/ScrollingLayerWC.h>
#include <wtf/MainThread.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <thread>

#if !ENABLE(ASYNC_SCROLLING) || !ENABLE(SCROLLING_THREAD)
#error This harness requires the experimental Windows asynchronous-scrolling build.
#endif

using namespace WebCore;

static unsigned assertions;
static bool check(bool condition, const char* name)
{
    ++assertions;
    std::printf("%s %s\n", condition ? "PASS" : "FAIL", name);
    return condition;
}

int main()
{
    WTF::initializeMainThread();
    bool passed = true;
    auto scene = ScrollingSceneWC::create();
    auto first = ScrollingLayerWC::create(scene, PlatformLayerIdentifier::generate());
    RefPtr second = ScrollingLayerWC::create(scene, PlatformLayerIdentifier::generate());
    first->setPositionForScrolling({ 12, 24 });
    second->setBoundsOriginForScrolling({ 5, 9 });
    scene->requestComposition(FloatPoint { 45, 90 });
    passed &= check(!scene->revision(), "Unbound publication retains pending positions");

    uint64_t lastRevision = 0;
    unsigned publications = 0;
    FloatPoint lastRoot;
    Vector<WCScrollLayerPosition> last;
    scene->setPublisher([&](WCScrollUpdate&& update) {
        lastRevision = update.revision;
        lastRoot = update.rootScrollPosition;
        ++publications;
        last = WTF::move(update.layers);
    });
    scene->requestComposition();
    passed &= check(publications == 1 && last.size() == 2, "One publication includes all changed layers");
    auto firstRow = std::ranges::find_if(last, [&](auto& row) { return row.identifier == first->identifier(); });
    auto secondRow = std::ranges::find_if(last, [&](auto& row) { return row.identifier == second->identifier(); });
    passed &= check(firstRow != last.end() && firstRow->changed == 1 && firstRow->position == FloatPoint(12, 24),
        "Absolute scrolling position survives deferred binding");
    passed &= check(secondRow != last.end() && secondRow->changed == 2 && secondRow->boundsOrigin == FloatPoint(5, 9),
        "Overflow bounds origin remains independent of layer position");
    passed &= check(lastRoot == FloatPoint(45, 90), "Deferred publication retains the real root scroll position");
    passed &= check(scene->isCompositionRequiredOrOngoing(), "Publication remains pending until acknowledgement");
    auto previousRevision = lastRevision;
    first->setPositionForScrolling({ 20, 40 });
    first->setPositionForScrolling({ 30, 60 });
    scene->requestComposition();
    passed &= check(last.size() == 1 && last[0].position == FloatPoint(30, 60), "Queued moves coalesce to the latest absolute position");
    scene->didCompleteComposition(previousRevision);
    passed &= check(scene->isCompositionRequiredOrOngoing(), "An older acknowledgement cannot finish a newer publication");
    scene->didCompleteComposition(lastRevision);
    passed &= check(!scene->isCompositionRequiredOrOngoing(), "The current acknowledgement completes publication");
    scene->didCompleteComposition(lastRevision + 100);
    first->setPositionForScrolling({ 32, 64 });
    scene->requestComposition();
    passed &= check(scene->isCompositionRequiredOrOngoing(), "An invalid future acknowledgement cannot pre-complete later movement");
    scene->didCompleteComposition(lastRevision);
    auto count = publications;
    first->setPositionForScrolling({ 32, 64 });
    scene->requestComposition();
    passed &= check(publications == count, "Unchanged geometry does not publish another frame");

    second = nullptr;
    first->setBoundsOriginForScrolling({ 7, 11 });
    scene->requestComposition();
    passed &= check(last.size() == 1 && last[0].changed == 2 && last[0].boundsOrigin == FloatPoint(7, 11),
        "Retired layers are pruned while surviving geometry continues");

    scene->beginMainThreadUpdate();
    auto mainRevision = scene->mainThreadUpdateRevision();
    first->setPositionForScrolling({ 40, 80 });
    scene->requestComposition(FloatPoint { 50, 100 }, { 7, 10, 11 });
    auto normal = scene->commitMainThreadState({ 45, 90 });
    passed &= check(normal.revision > mainRevision && normal.rootScrollPosition == FloatPoint(50, 100),
        "A late main frame preserves root motion newer than its rendering snapshot");
    passed &= check(normal.layers.size() == 1 && normal.layers[0].changed == 3
        && normal.layers[0].position == FloatPoint(40, 80) && normal.layers[0].boundsOrigin == FloatPoint(7, 11),
        "Normal publication carries complete current layer positions without accumulating movement");
    scene->didCompleteComposition(normal.revision);
    scene->beginMainThreadUpdate();
    normal = scene->commitMainThreadState({ 52, 104 });
    passed &= check(normal.rootScrollPosition == FloatPoint(52, 104),
        "A later main rendering snapshot can adopt a current programmatic root position");
    scene->didCompleteComposition(normal.revision);

    std::atomic<bool> finished { false }, valid { true };
    uint64_t observedRevision = lastRevision;
    FloatPoint observedPosition;
    scene->setPublisher([&](WCScrollUpdate&& update) {
        auto revision = update.revision;
        if (revision <= observedRevision || scene->revision() < revision)
            valid = false;
        observedRevision = revision;
        for (auto& row : update.layers) {
            if (row.identifier != first->identifier() || !(row.changed & 1) || row.position.y() != row.position.x() * 2)
                valid = false;
            observedPosition = row.position;
        }
        // Acknowledgement may query scene state from the publication callback.
        scene->didCompleteComposition(revision);
    });
    std::thread producer([&] {
        for (unsigned value = 31; value <= 1000; ++value) {
            first->setPositionForScrolling({ static_cast<float>(value), static_cast<float>(value * 2) });
            if (!(value % 7)) scene->requestComposition();
        }
        finished = true;
    });
    std::thread consumer([&] {
        while (!finished) scene->requestComposition();
    });
    producer.join();
    consumer.join();
    scene->requestComposition();
    passed &= check(valid, "Concurrent publication preserves ordering and complete coordinate pairs");
    passed &= check(observedPosition == FloatPoint(1000, 2000), "The final movement is delivered without accumulating deltas");
    passed &= check(!scene->isCompositionRequiredOrOngoing(), "Reentrant acknowledgement leaves no pending publication");
    std::printf("%s scrolling-layer primitives: %u assertions; not native scrolling acceptance.\n", passed ? "PASS" : "FAIL", assertions);
    return passed ? 0 : 1;
}
