#pragma once
#include <windows.h>

// Physical-pixel presentation only. The browser retains its logical viewport.
struct DDChrome {
    int kind, action;
    float x, y, width, height;
    float visualX, visualY, visualWidth, visualHeight, radius;
    float fontSize, clipWidth;
    unsigned foreground;
    const wchar_t* text;
};
struct DDFill {
    float x, y, width, height;
    unsigned color;
};
struct DDMaterial {
    unsigned tint;
    float alpha, strength, shadow, scale;
    float blur, saturation, brightness, contrast, topHighlight, edgeHighlight, sheen, shadowBlur, shadowOffset;
};
struct DDCompositionState {
    unsigned apiVersion, softwareDevice;
    unsigned long long resourceGeneration, sceneGeneration;
    int width, height;
};

extern "C" {
__declspec(dllexport) unsigned DDCompositionVersion();
__declspec(dllexport) unsigned DDCompositionPresentationVersion();
__declspec(dllexport) unsigned DDCompositionScrollIndicatorVersion();
__declspec(dllexport) HRESULT DDCompositionSetScrollIndicator(void*, float x, float y, float width, float height, float opacity);
__declspec(dllexport) HRESULT DDCompositionSetPresentation(void*, HWND target, int clockwiseDegrees, int offsetX, int offsetY);
__declspec(dllexport) void* DDCompositionCreate(HWND parent, HWND browser);
__declspec(dllexport) HRESULT DDCompositionUpdate(
    void*, int width, int height, const DDChrome*, int count, const DDFill*, int fillCount, DDMaterial);
__declspec(dllexport) unsigned DDCompositionHit(void*, int x, int y);
__declspec(dllexport) HRESULT DDCompositionMeasureText(void*, const wchar_t*, float fontSize, float* width);
__declspec(dllexport) HRESULT DDCompositionCheck(void*);
__declspec(dllexport) DDCompositionState DDCompositionGetState(void*);
__declspec(dllexport) const wchar_t* DDCompositionError();
__declspec(dllexport) void DDCompositionDestroy(void*);
}
