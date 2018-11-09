#ifndef SMXPanelAnimationUpload_h
#define SMXPanelAnimationUpload_h

#include "SMXPanelAnimation.h"

// For SMX_API:
#include "../SMX.h"

// This is used to upload panel animations to the firmware.  This is
// only needed for offline animations.  For live animations, either
// use SMX_LightsAnimation_SetAuto, or to control lights directly
// (recommended), use SMX_SetLights.
//
// Before starting, load animations into SMXPanelAnimation.
//
// Prepare the currently loaded animations to be stored on the pad.
// Return false with an error message on error.
//
// All LightTypes must be loaded before beginning the upload.
//
// If a lights upload is already in progress, returns an error.
SMX_API bool SMX_LightsUpload_PrepareUpload(int pad, const char **error);

typedef void SMX_LightsUploadCallback(int progress, void *pUser);

// After a successful call to SMX_LightsUpload_Init, begin uploading data
// to the master controller for the given pad and animation type.
//
// The callback will be called as the upload progresses, with progress values
// from 0-100.
//
// callback will always be called exactly once with a progress value of 100.
// Once the 100% progress is called, the callback won't be accessed, so the
// caller can safely clean up.  This will happen even if the pad disconnects
// partway through the upload.
//
// The callback will be called from the user callback helper thread.
SMX_API void SMX_LightsUpload_BeginUpload(int pad, SMX_LightsUploadCallback callback, void *pUser);

#endif
