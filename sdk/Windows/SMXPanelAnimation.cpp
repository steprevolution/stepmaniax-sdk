// Handle playing GIF animations from inside SMXConfig.
//
// This can load two GIF animations, one for when panels are released
// and one for when they're pressed, and play them automatically on the
// pad in the background.  Applications that control lights can do more
// sophisticated things with the lights, but this gives an easy way for
// people to create simple animations.
//
// If you're implementing the SDK in a game, you don't need this and should
// use SMX.h instead.
//
// An animation is a single GIF with animations for all panels, in the
// following layout:
//
// 0000|1111|2222
// 0000|1111|2222
// 0000|1111|2222
// 0000|1111|2222
// --------------
// 3333|4444|5555
// 3333|4444|5555
// 3333|4444|5555
// 3333|4444|5555
// --------------
// 6666|7777|8888
// 6666|7777|8888
// 6666|7777|8888
// 6666|7777|8888
// x-------------
//
// The - | regions are ignored and are only there to space out the animation
// to make it easier to view.
//
// The extra bottom row is a flag row and should normally be black.  The first
// pixel (bottom-left) optionally marks a loop frame.  By default, the animation
// plays all the way through and then loops back to the beginning.  If the loop
// frame pixel is white, it marks a frame to loop to instead of the beginning.
// This allows pressed animations to have a separate lead-in and loop.
//
// Each animation is for a single pad.  You can load the same animation for both
// pads or use different ones.

#include "SMXPanelAnimation.h"
#include "SMXManager.h"
#include "SMXDevice.h"
#include "SMXThread.h"
using namespace std;
using namespace SMX;

namespace {
    Mutex g_Lock;
}

#define LIGHTS_PER_PANEL 25

// XXX: go to sleep if there are no pads connected

struct AnimationState
{
    SMXPanelAnimation animation;

    // Seconds into the animation:
    float fTime = 0;

    // The currently displayed frame:
    int iCurrentFrame = 0;

    bool bPlaying = false;

    double m_fLastUpdateTime = -1;

    // Return the current animation frame.
    const vector<SMXGif::Color> &GetAnimationFrame() const
    {
        // If we're not playing, return an empty array.  As a sanity check, do this
        // if the frame is out of bounds too.
        if(!bPlaying || iCurrentFrame >= animation.m_aPanelGraphics.size())
        {
            static vector<SMXGif::Color> dummy;
            return dummy;
        }

        return animation.m_aPanelGraphics[iCurrentFrame];
    }

    // Start the animation if it's not playing.
    void Play()
    {
        bPlaying = true;
    }

    // Stop and disable the animation.
    void Stop()
    {
        bPlaying = false;
        Rewind();
    }

    // Reset to the first frame.
    void Rewind()
    {
        fTime = 0;
        iCurrentFrame = 0;
    }

    // Advance the animation by fSeconds.
    void Update()
    {
        // fSeconds is the time since the last update:
        double fNow = SMX::GetMonotonicTime();
        double fSeconds = m_fLastUpdateTime == -1? 0: (fNow - m_fLastUpdateTime);
        m_fLastUpdateTime = fNow;

        if(!bPlaying || animation.m_aPanelGraphics.empty())
            return;

        // If the current frame is past the end, a new animation was probably
        // loaded.
        if(iCurrentFrame >= animation.m_aPanelGraphics.size())
            Rewind();

        // Advance time.
        fTime += fSeconds;

        // If we're still on this frame, we're done.
        float fFrameDuration = animation.m_iFrameDurations[iCurrentFrame];
        if(fTime - 0.00001f < fFrameDuration)
            return;

        // If we've passed the end of the frame, move to the next frame.  Don't
        // skip frames if we're updating too quickly.
        fTime -= fFrameDuration;
        if(fTime > 0)
            fTime = 0;

        // Advance the frame.
        iCurrentFrame++;

        // If we're at the end of the frame, rewind to the loop frame.
        if(iCurrentFrame == animation.m_aPanelGraphics.size())
            iCurrentFrame = animation.m_iLoopFrame;
    }
};

struct AnimationStateForPad
{
    // asLightsData is an array of lights data to send to the pad and graphic
    // is an animation graphic.  Overlay graphic on top of the lights.
    void OverlayLights(char *asLightsData, const vector<SMXGif::Color> &graphic) const
    {
        // Stop if this graphic isn't loaded or is paused.
        if(graphic.empty())
            return;

        for(int i = 0; i < graphic.size(); ++i)
        {
            if(i >= LIGHTS_PER_PANEL)
                return;

            // If this color is transparent, leave the released animation alone.
            if(graphic[i].color[3] == 0)
                continue;

            asLightsData[i*3+0] = graphic[i].color[0];
            asLightsData[i*3+1] = graphic[i].color[1];
            asLightsData[i*3+2] = graphic[i].color[2];
        }
    }

    // Return the command to set the current animation state as pad lights.
    string GetLightsCommand(int iPadState, const SMXConfig &config) const
    {
        g_Lock.AssertLockedByCurrentThread();

        // If AutoLightingUsePressedAnimations is set, use lights animations.
        // If it's not (the config tool is set to step color), mimic the built-in
        // step color behavior instead of using pressed animations.  Any released
        // animation will always be used.
        bool bUsePressedAnimations = config.flags & PlatformFlags_AutoLightingUsePressedAnimations;

        const int iBytesPerPanel = LIGHTS_PER_PANEL*3;
        const int iTotalLights = 9*iBytesPerPanel;
        string result(iTotalLights, 0);

        for(int panel = 0; panel < 9; ++panel)
        {
            // The portion of lights data for this panel:
            char *out = &result[panel*iBytesPerPanel];

            // Add the released animation, then overlay the pressed animation if we're pressed.
            OverlayLights(out, animations[panel][SMX_LightsType_Released].GetAnimationFrame());
            bool bPressed = bool(iPadState & (1 << panel));
            if(bPressed && bUsePressedAnimations)
                OverlayLights(out, animations[panel][SMX_LightsType_Pressed].GetAnimationFrame());
            else if(bPressed && !bUsePressedAnimations)
            {
                // Light all LEDs on this panel using stepColor.
                double LightsScaleFactor = 0.666666f;
                const uint8_t *color = &config.stepColor[panel*3];

                for(int light = 0; light < LIGHTS_PER_PANEL; ++light)
                {
                    for(int i = 0; i < 3; ++i)
                    {
                        // stepColor is scaled to the 0-170 range.  Scale it back to the 0-255 range.
                        // User applications don't need to worry about this since they normally don't
                        // need to care about stepColor.
                        uint8_t c = color[i];
                        c = (uint8_t) lrintf(min(255.0f, c / LightsScaleFactor));
                        out[light*3+i] = c;
                    }
                }
            }
        }

        return result;
    }

    // State for both animations on each panel:
    AnimationState animations[9][NUM_SMX_LightsType];
};

namespace
{
    // Animations and animation states for both pads.
    AnimationStateForPad pad_states[2];
}

namespace {
    // The X,Y positions of each possible panel.
    vector<pair<int,int>> graphic_positions = {
        { 0,0 },
        { 1,0 },
        { 2,0 },
        { 0,1 },
        { 1,1 },
        { 2,1 },
        { 0,2 },
        { 1,2 },
        { 2,2 },
    };

    // Given a 14x15 graphic frame and a panel number, return an array of 16 colors, containing
    // each light in the order it's sent to the master controller.
    void ConvertToPanelGraphic16(const SMXGif::GIFImage &src, vector<SMXGif::Color> &dst, int panel)
    {
        dst.clear();

        // The top-left corner for this panel:
        int x = graphic_positions[panel].first * 5;
        int y = graphic_positions[panel].second * 5;

        // Add the 4x4 grid.
        for(int dy = 0; dy < 4; ++dy)
            for(int dx = 0; dx < 4; ++dx)
                dst.push_back(src.get(x+dx, y+dy));

        // These animations have no data for the 3x3 grid, so just set them to transparent.
        for(int dy = 0; dy < 3; ++dy)
            for(int dx = 0; dx < 3; ++dx)
                dst.push_back(SMXGif::Color(0,0,0,0));
    }

    // Given a 23x24 graphic frame and a panel number, return an array of 25 colors, containing
    // each light in the order it's sent to the master controller.
    void ConvertToPanelGraphic25(const SMXGif::GIFImage &src, vector<SMXGif::Color> &dst, int panel)
    {
        dst.clear();

        // The top-left corner for this panel:
        int x = graphic_positions[panel].first * 8;
        int y = graphic_positions[panel].second * 8;

        // Add the 4x4 grid first.
        for(int dy = 0; dy < 4; ++dy)
            for(int dx = 0; dx < 4; ++dx)
                dst.push_back(src.get(x+dx*2, y+dy*2));

        // Add the 3x3 grid.
        for(int dy = 0; dy < 3; ++dy)
            for(int dx = 0; dx < 3; ++dx)
                dst.push_back(src.get(x+dx*2+1, y+dy*2+1));
    }
}

// Return the SMXPanelAnimation.  The rest of the animation state is internal.
SMXPanelAnimation SMXPanelAnimation::GetLoadedAnimation(int pad, int panel, SMX_LightsType type)
{
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);
    return pad_states[pad].animations[panel][type].animation;
}

// Load an array of animation frames as a panel animation.  Each frame must
// be 14x15 or 23x24.
void SMXPanelAnimation::Load(const vector<SMXGif::SMXGifFrame> &frames, int panel)
{
    m_aPanelGraphics.clear();
    m_iFrameDurations.clear();
    m_iLoopFrame = -1;

    for(int frame_no = 0; frame_no < frames.size(); ++frame_no)
    {
        const SMXGif::SMXGifFrame &gif_frame = frames[frame_no];

        // If the bottom-left pixel is opaque, this is the loop frame, which marks the
        // frame the animation should start at after a loop.  This is global to the
        // animation, not specific to each panel.
        if(gif_frame.frame.get(0, gif_frame.frame.height-1).color[3] != 0)
        {
            // We shouldn't see more than one of these.  If we do, use the first.
            if(m_iLoopFrame != -1)
                m_iLoopFrame = frame_no;
        }

        // Extract this frame.  If the graphic is 14x15 it's a 4x4 animation,
        // and if it's 23x24 it's 25-light.
        vector<SMXGif::Color> panel_graphic;
        if(frames[0].width == 14)
            ConvertToPanelGraphic16(gif_frame.frame, panel_graphic, panel);
        else
            ConvertToPanelGraphic25(gif_frame.frame, panel_graphic, panel);
        m_aPanelGraphics.push_back(panel_graphic);

        // GIFs have a very low-resolution duration field, with 10ms units.
        // The panels run at 30 FPS internally, or 33 1/3 ms, but GIF can only
        // represent 30ms or 40ms.  Most applications will probably output 30,
        // but snap both 30ms and 40ms to exactly 30 FPS to make sure animations
        // that are meant to run at native framerate do.
        float seconds;
        if(gif_frame.milliseconds == 30 || gif_frame.milliseconds == 40)
            seconds = 1 / 30.0f;
        else
            seconds = gif_frame.milliseconds / 1000.0;

        m_iFrameDurations.push_back(seconds);
    }

    // By default, loop back to the first frame.
    if(m_iLoopFrame == -1)
        m_iLoopFrame = 0;
}

// Load a GIF into SMXLoadedPanelAnimations::animations.
bool SMX_LightsAnimation_Load(const char *gif, int size, int pad, SMX_LightsType type, const char **error)
{
    // Parse the GIF.
    string buf(gif, size);
    vector<SMXGif::SMXGifFrame> frames;
    if(!SMXGif::DecodeGIF(buf, frames) || frames.empty())
    {
        *error = "The GIF couldn't be read.";
        return false;
    }

    // Check the dimensions of the image.  We only need to check the first, the
    // others will always have the same size.
    if((frames[0].width != 14 || frames[0].height != 15) && (frames[0].width != 23 || frames[0].height != 24))
    {
        *error = "The GIF must be 14x15 or 23x24.";
        return false;
    }

    // Lock while we access pad_states.
    g_Lock.AssertNotLockedByCurrentThread();
    LockMutex L(g_Lock);

    // Load the animation for each panel into SMXPanelAnimations.
    for(int panel = 0; panel < 9; ++panel)
    {
        SMXPanelAnimation &animation = pad_states[pad].animations[panel][type].animation;
        animation.Load(frames, panel);
    }

    return true;
}

// A thread to handle setting light animations.  We do this in a separate
// thread rather than in the SMXManager thread so this can be treated as
// if it's external application thread, and it's making normal threaded
// calls to SetLights.
class PanelAnimationThread: public SMXThread
{
public:
    static shared_ptr<PanelAnimationThread> g_pSingleton;
    PanelAnimationThread():
        SMXThread(g_Lock)
    {
        Start("SMX light animations");
    }

private:
    void ThreadMain()
    {
        m_Lock.Lock();
        
        // Update lights at 30 FPS.
        const int iDelayMS = 33;

        while(!m_bShutdown)
        {
            // Run a single panel lights update.
            UpdateLights();

            // Wait up to 30 FPS, or until we're signalled.  We can only be signalled
            // if we're shutting down, so we don't need to worry about partial frame
            // delays.
            m_Event.Wait(iDelayMS);
        }

        m_Lock.Unlock();
    }

    // Return lights for the given pad and pad state, using the loaded panel animations.
    void GetCurrentLights(string &asLightsDataOut, int pad, int iPadState)
    {
        m_Lock.AssertLockedByCurrentThread();

        // Get this pad's configuration.
        SMXConfig config;
        if(!SMXManager::g_pSMX->GetDevice(pad)->GetConfig(config))
            return;

        AnimationStateForPad &pad_state = pad_states[pad];

        // Make sure the correct animations are playing.
        for(int panel = 0; panel < 9; ++panel)
        {
            // The released animation is always playing.
            pad_state.animations[panel][SMX_LightsType_Released].Play();

            // The pressed animation only plays while the button is pressed,
            // and rewind when it's released.
            bool bPressed = iPadState & (1 << panel);
            if(bPressed)
                pad_state.animations[panel][SMX_LightsType_Pressed].Play();
            else
                pad_state.animations[panel][SMX_LightsType_Pressed].Stop();
        }

        // Set the current state.
        asLightsDataOut = pad_state.GetLightsCommand(iPadState, config);

        // Advance animations.
        for(int panel = 0; panel < 9; ++panel)
        {
            for(auto &animation_state: pad_state.animations[panel])
                animation_state.Update();
        }
    }

    // Run a single light animation update.
    void UpdateLights()
    {
        string asLightsData[2];
        for(int pad = 0; pad < 2; pad++)
        {
            int iPadState = SMXManager::g_pSMX->GetDevice(pad)->GetInputState();
            GetCurrentLights(asLightsData[pad], pad, iPadState);
        }

        // Update lights.
        SMXManager::g_pSMX->SetLights(asLightsData);
    }
};

void SMX_LightsAnimation_SetAuto(bool enable)
{
    if(!enable)
    {
        // If we're turning off, shut down the thread if it's running.
        if(PanelAnimationThread::g_pSingleton)
            PanelAnimationThread::g_pSingleton->Shutdown();
        PanelAnimationThread::g_pSingleton.reset();
        return;
    }

    // Create the animation thread if it's not already running.
    if(PanelAnimationThread::g_pSingleton)
        return;
    PanelAnimationThread::g_pSingleton.reset(new PanelAnimationThread());
}

shared_ptr<PanelAnimationThread> PanelAnimationThread::g_pSingleton;
