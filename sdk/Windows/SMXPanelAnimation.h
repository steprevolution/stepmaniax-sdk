#ifndef SMXPanelAnimation_h
#define SMXPanelAnimation_h

#include <vector>
#include "SMXGif.h"

enum SMX_LightsType
{
    SMX_LightsType_Released, // animation while panels are released
    SMX_LightsType_Pressed, // animation while panel is pressed
    NUM_SMX_LightsType,
};

// SMXPanelAnimation holds an animation, with graphics for a single panel.
class SMXPanelAnimation
{
public:
    // Return the animation loaded by SMX_LightsAnimation_Load.
    static SMXPanelAnimation GetLoadedAnimation(int pad, int panel, SMX_LightsType type);

    void Load(const std::vector<SMXGif::SMXGifFrame> &frames, int panel);

    // The high-level animated GIF frames:
    std::vector<std::vector<SMXGif::Color>> m_aPanelGraphics;

    // The animation starts on frame 0.  When it reaches the end, it loops
    // back to this frame.
    int m_iLoopFrame = 0;

    // The duration of each frame in seconds.
    std::vector<float> m_iFrameDurations;
};

// For SMX_API:
#include "../SMX.h"

// High-level interface for C# bindings:
//
// Load an animated GIF as a panel animation.  pad is the pad this animation is for (0 or 1),
// and type is which animation this is for.  Any previously loaded animation will be replaced.
// On error, false is returned and error is set to a plain-text error message which is valid
// until the next call.
SMX_API bool SMX_LightsAnimation_Load(const char *gif, int size, int pad, SMX_LightsType type, const char **error);

// Enable or disable automatically handling lights animations.  If enabled, any animations
// loaded with SMX_LightsAnimation_Load will run automatically as long as the SDK is loaded.
// This only has an effect if the platform doesn't handle animations directly.  On newer firmware,
// this has no effect (upload the animation to the panel instead).
// XXX: should we automatically disable SMX_SetLights when this is enabled?
SMX_API void SMX_LightsAnimation_SetAuto(bool enable);

#endif
