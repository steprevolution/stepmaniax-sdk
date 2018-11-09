#include "SMXPanelAnimationUpload.h"
#include "SMXPanelAnimation.h"
#include "SMXGif.h"
#include "SMXManager.h"
#include "SMXDevice.h"
#include "Helpers.h"
#include <string>
#include <vector>
using namespace std;
using namespace SMX;

// This handles setting up commands to upload panel animations to the
// controller.
//
// This is only meant to be used by configuration tools to allow setting
// up animations that work while the pad isn't being controlled by the
// SDK.  If you want to control lights for your game, this isn't what
// you want.  Use SMX_SetLights instead.
//
// Panel animations are sent to the master controller one panel at a time, and
// each animation can take several commands to upload to fit in the protocol packet
// size.  These commands are stateful.

// XXX: should be able to upload both pads in parallel
// XXX: we can only update all animations in one go, so save the last loaded animations
// so the user doesn't have to manually load both to change one of them
// do this in SMXConfig, not the SDK

namespace
{
    // Panel names for error messages.
    static const char *panel_names[] = {
        "up-left", "up", "up-right",
        "left", "center", "right",
        "down-left", "down", "down-right",
    };
}

// These structs are the protocol we use to send offline graphics to the pad.
// This isn't related to realtime lighting.
namespace PanelLightGraphic
{
    // One 24-bit RGB color:
    struct color_t {
        uint8_t rgb[3];
    };

    // 4-bit palette, 15 colors.  Our graphics are 4-bit.  Color 0xF is transparent,
    // so we don't have a palette entry for it.
    struct palette_t {
        color_t colors[15];
    };

    // A single 4-bit paletted graphic.
    struct graphic_t {
        uint8_t data[13];
    };

    struct panel_animation_data_t
    {
        // Our graphics and palettes.  We can apply either palette to any graphic.  Note that
        // each graphic is 13 bytes and each palette is 45 bytes.
        graphic_t graphics[64];
        palette_t palettes[2];
    };

    struct animation_timing_t
    {
        // An index into frames[]:
        uint8_t loop_animation_frame;

        // A list of graphic frames to display, and how long to display them in
        // 30 FPS frames.  A frame index of 0xFF (or reaching the end) loops.
        uint8_t frames[64];
        uint8_t delay[64];
    };

    struct master_animation_data_t
    {
        animation_timing_t animation_timings[2];
    };

    // Commands to upload data:
    struct upload_packet
    {
        // 'm' to upload master animation data.
        uint8_t cmd = 'm';

        // The panel this data is for.  If this is 0xFF, it's for the master.
        uint8_t panel = 0;

        // For master uploads, the animation number to modify.  Panels ignore this field.
        uint8_t animation_idx = 0;

        // True if this is the last upload packet.  This lets the firmware know that
        // this part of the upload is finished and it can update anything that might
        // be affected by it, like resetting lights animations.
        bool final_packet = false;

        uint8_t offset = 0, size = 0;
        uint8_t data[240];
    };

    // Make sure the packet fits in a command packet.
    static_assert(sizeof(upload_packet) <= 0xFF, "");
}

// The GIFs can use variable framerates.  The panels update at 30 FPS.
#define FPS 30

// Helpers for converting PanelGraphics to the packed sprite representation
// we give to the pad.
namespace ProtocolHelpers
{
    // Return a color's index in palette.  If the color isn't found, return 0.
    // We can use a dumb linear search here since the graphics are so small.
    uint8_t GetColorIndex(const PanelLightGraphic::palette_t &palette, const SMXGif::Color &color)
    {
        // Transparency is always palette index 15.
        if(color.color[3] == 0)
            return 15;

        for(int idx = 0; idx < 15; ++idx)
        {
            PanelLightGraphic::color_t pad_color = palette.colors[idx];
            if(pad_color.rgb[0] == color.color[0] &&
                pad_color.rgb[1] == color.color[1] &&
                pad_color.rgb[2] == color.color[2])
                return idx;
        }
        return 0;
    }

    // Create a palette for an animation.
    //
    // We're loading from paletted GIFs, but we create a separate small palette
    // for each panel's animation, so we don't use the GIF's palette.
    bool CreatePalette(const SMXPanelAnimation &animation, PanelLightGraphic::palette_t &palette)
    {
        int next_color = 0;
        for(const auto &panel_graphic: animation.m_aPanelGraphics)
        {
            for(const SMXGif::Color &color: panel_graphic)
            {
                // If this color is transparent, leave it out of the palette.
                if(color.color[3] == 0)
                    continue;

                // Check if this color is already in the palette.
                uint8_t existing_idx = GetColorIndex(palette, color);
                if(existing_idx < next_color)
                    continue;

                // Return false if we're using too many colors.
                if(next_color == 15)
                    return false;

                // Add this color.
                PanelLightGraphic::color_t pad_color;
                pad_color.rgb[0] = color.color[0];
                pad_color.rgb[1] = color.color[1];
                pad_color.rgb[2] = color.color[2];
                palette.colors[next_color] = pad_color;
                next_color++;
            }
        }
        return true;
    }

    // Return packed paletted graphics for each frame, using a palette created
    // with CreatePalette.  The palette must have fewer than 16 colors.
    void CreatePackedGraphic(const vector<SMXGif::Color> &image, const PanelLightGraphic::palette_t &palette,
        PanelLightGraphic::graphic_t &out)
    {
        int position = 0;
        for(auto color: image)
        {
            // Transparency is always palette index 15.
            uint8_t palette_idx = GetColorIndex(palette, color);

            // Apply color scaling, in the same way SMXManager::SetLights does.
            for(int i = 0; i < 3; ++i)
                color.color[i] = uint8_t(color.color[i] * 0.6666f);

            // If this is an odd index, put the palette index in the high 4
            // bits.  Otherwise, put it in the low 4 bits.
            if(position & 1)
                out.data[position/2] |= (palette_idx & 0xF0) << 4;
            else
                out.data[position/2] |= (palette_idx & 0xF0) << 0;
        }
    }

    vector<uint8_t> get_frame_delays(const SMXPanelAnimation &animation)
    {
        vector<uint8_t> result;
        int current_frame = 0;
        
        int time_left_in_frame = animation.m_iFrameDurations[0];
        result.push_back(0);
        while(1)
        {
            // Advance time by 1/FPS seconds.
            time_left_in_frame -= 1 / FPS;
            result.back()++;

            if(time_left_in_frame <= 0.00001)
            {
                // We've displayed this frame long enough, so advance to the next frame.
                if(current_frame + 1 == animation.m_iFrameDurations.size())
                    break;

                current_frame += 1;
                result.push_back(0);
                time_left_in_frame += animation.m_iFrameDurations[current_frame];

                // If time_left_in_frame is still negative, the animation is too fast.
                if(time_left_in_frame < 0.00001)
                    time_left_in_frame = 0;
            }
        }
        return result;
    }

    // Create the master data.  This just has timing information.
    bool CreateMasterAnimationData(int pad, PanelLightGraphic::master_animation_data_t &master_data, const char **error)
    {
        // The second animation's graphic indices start where the first one's end.
        int first_graphic = 0;

        // XXX: It's possible to reuse graphics, which allows us to dedupe animation frames.
        // We can store 64 total animation graphics shared across the pressed and released
        // animation, and each animation can have up to 64 timed frames which point into
        // the graphic list.
        for(int type = 0; type < NUM_SMX_LightsType; ++type)
        {
            // All animations of each type have the same timing for all panels, since
            // they come from the same GIF, so just look at the first frame.
            const SMXPanelAnimation &animation = SMXPanelAnimation::GetLoadedAnimation(pad, 0, SMX_LightsType(type));

            PanelLightGraphic::animation_timing_t &animation_timing = master_data.animation_timings[type];

            // Check that we don't have more frames than we can fit in animation_timing.
            // This is currently the same as the "too many frames" error below, but if
            // we support longer delays (staying on the same graphic for multiple animation_timings)
            // or deduping they'd be different.
            if(animation.m_aPanelGraphics.size() > arraylen(animation_timing.frames))
	        {
                *error = "The animation is too long.";
                return false;
	        }

            memset(&animation_timing.frames[0], 0xFF, sizeof(animation_timing.frames));
            for(int i = 0; i < animation.m_aPanelGraphics.size(); ++i)
            {
                animation_timing.frames[i] = first_graphic;
                first_graphic++;
            }

            // Set frame delays.
            memset(&animation_timing.delay[0], 0, sizeof(animation_timing.delay));
            vector<uint8_t> delays = get_frame_delays(animation);
            for(int i = 0; i < delays.size() && i < 64; ++i)
                animation_timing.delay[i] = delays[i];

            // These frame numbers are relative to the animation, so don't add first_graphic.
            // XXX: frame index, not source frame
            animation_timing.loop_animation_frame = animation.m_iLoopFrame;
        }
        return true;
    }

    // Pack panel graphics.
    bool CreatePanelAnimationData(PanelLightGraphic::panel_animation_data_t &panel_data,
        int pad, int panel, const char **error)
    {
        // We have a single buffer of animation frames for each panel, which we pack
        // both the pressed and released frames into.  This is the index of the next
        // frame.
        int next_graphic_idx = 0;

        for(int type = 0; type < NUM_SMX_LightsType; ++type)
        {
            const SMXPanelAnimation &animation = SMXPanelAnimation::GetLoadedAnimation(pad, panel, SMX_LightsType(type));

            // Create this animation's 4-bit palette.
            if(!ProtocolHelpers::CreatePalette(animation, panel_data.palettes[type]))
            {
                *error = SMX::CreateError(SMX::ssprintf("The %s panel uses too many colors.", panel_names[panel]));
                return false;
            }

            // Create a small 4-bit paletted graphic with the 4-bit palette we created.
            // These are the graphics we'll send to the controller.
            for(const auto &panel_graphic: animation.m_aPanelGraphics)
            {
                if(next_graphic_idx > arraylen(panel_data.graphics))
                {
                    *error = "The animation has too many frames.";
                    return false;
                }

                ProtocolHelpers::CreatePackedGraphic(panel_graphic, panel_data.palettes[type], panel_data.graphics[next_graphic_idx]);
                next_graphic_idx++;
            }
        }
        return true;
    }

    // Create upload packets to upload a block of data.
    void CreateUploadPackets(vector<PanelLightGraphic::upload_packet> &packets,
        const void *data_block, int size,
        uint8_t panel, int animation_idx)
    {
        const uint8_t *buf = (const uint8_t *) &data_block;
        for(int offset = 0; offset < size; )
        {
            PanelLightGraphic::upload_packet packet;
            packet.panel = panel;
            packet.animation_idx = animation_idx;
            packet.offset = offset;

            int bytes_left = size - offset;
            packet.size = min(sizeof(PanelLightGraphic::upload_packet::data), bytes_left);
            memcpy(packet.data, buf + offset, packet.size);
            packets.push_back(packet);

            offset += packet.size;
        }

        packets.back().final_packet = true;
    }
}

namespace LightsUploadData
{
    vector<string> commands[2];
}

// Prepare the loaded graphics for upload.
bool SMX_LightsUpload_PrepareUpload(int pad, const char **error)
{
    // Check that all panel animations are loaded.
    for(int type = 0; type < NUM_SMX_LightsType; ++type)
    {
        const SMXPanelAnimation &animation = SMXPanelAnimation::GetLoadedAnimation(pad, 0, SMX_LightsType(type));
        if(animation.m_aPanelGraphics.empty())
        {
            *error = "Load all panel animations before preparing the upload.";
            return false;
        }
    }

    // Create master animation data.
    PanelLightGraphic::master_animation_data_t master_data;
    if(!ProtocolHelpers::CreateMasterAnimationData(pad, master_data, error))
        return false;

    // Create panel animation data.
    PanelLightGraphic::panel_animation_data_t all_panel_data[9];
    for(int panel = 0; panel < 9; ++panel)
    {
        if(!ProtocolHelpers::CreatePanelAnimationData(all_panel_data[panel], pad, panel, error))
            return false;
    }

    // We successfully created the data, so there's nothing else that can fail from
    // here on.

    // Create upload packets.
    vector<PanelLightGraphic::upload_packet> packets;
    for(int type = 0; type < NUM_SMX_LightsType; ++type)
    {
        const auto &master_data_block = master_data.animation_timings[type];
        ProtocolHelpers::CreateUploadPackets(packets, &master_data_block, sizeof(master_data_block), 0xFF, type);

        for(int panel = 0; panel < 9; ++panel)
        {
            const auto &panel_data_block = all_panel_data[panel];
            ProtocolHelpers::CreateUploadPackets(packets, &panel_data_block, sizeof(panel_data_block), panel, type);
        }
    }

    // Make a list of strings containing the packets.  We don't need the
    // structs anymore, so this is all we need to keep around.
    vector<string> &pad_commands = LightsUploadData::commands[pad];
    pad_commands.clear();
    for(const auto &packet: packets)
    {
        string command((char *) &packet, sizeof(packet));
        pad_commands.push_back(command);
    }

    return true;
}

// Start sending a prepared upload.
//
// The commands to send to upload the data are in pad_commands[pad].
void SMX_LightsUpload_BeginUpload(int pad, SMX_LightsUploadCallback pCallback, void *pUser)
{
    // XXX: should we disable panel lights while doing this?
    shared_ptr<SMXDevice> pDevice = SMXManager::g_pSMX->GetDevice(pad);
    vector<string> asCommands = LightsUploadData::commands[pad];
    int iTotalCommands = asCommands.size();

    // Queue all commands at once.  As each command finishes, our callback
    // will be called.
    for(int i = 0; i < asCommands.size(); ++i)
    {
        const string &sCommand = asCommands[i];
        pDevice->SendCommand(sCommand, [i, iTotalCommands, pCallback, pUser]() {
            // Command #i has finished being sent.
            //
            // If this isn't the last command, make sure progress isn't 100.
            // Once we send 100%, the callback is no longer valid.
            int progress = (i*100) / (iTotalCommands-1);
            if(i != iTotalCommands-1)
                progress = min(progress, 99);

            // We're currently in the SMXManager thread.  Call the user thread from
            // the user callback thread.
            SMXManager::g_pSMX->RunInHelperThread([pCallback, pUser, progress]() {
                pCallback(progress, pUser);
            });
        });
    }
}
