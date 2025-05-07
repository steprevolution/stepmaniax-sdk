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

    // Commands to upload data:
#pragma pack(push, 1)
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

        uint16_t offset = 0;
        uint8_t size = 0;
        uint8_t data[240] = { };
    };
#pragma pack(pop)

#pragma pack(push, 1)
    struct delay_packet
    {
        // 'd' to ask the master to delay.
        uint8_t cmd = 'd';

        // How long to delay:
        uint16_t milliseconds = 0;
    };
#pragma pack(pop)

    // Make sure the packet fits in a command packet.
    static_assert(sizeof(upload_packet) <= 0xFF, "");
}

// The GIFs can use variable framerates.  The panels update at 30 FPS.
#define FPS 30

// Helpers for converting PanelGraphics to the packed sprite representation
// we give to the pad.
namespace ProtocolHelpers
{
    // Return a color's index in palette.  If the color isn't found, return 0xFF.
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
        return 0xFF;
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
                if(existing_idx != 0xFF)
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
        memset(out.data, 0, sizeof(out.data));
        for(auto color: image)
        {
            // Transparency is always palette index 15.
            uint8_t palette_idx = GetColorIndex(palette, color);
            if(palette_idx == 0xFF)
                palette_idx = 0;

            // If this is an odd index, put the palette index in the low 4
            // bits.  Otherwise, put it in the high 4 bits.
            if(position & 1)
                out.data[position/2] |= (palette_idx & 0x0F) << 0;
            else
                out.data[position/2] |= (palette_idx & 0x0F) << 4;
            position++;
        }
    }

    vector<uint8_t> get_frame_delays(const SMXPanelAnimation &animation)
    {
        vector<uint8_t> result;
        int current_frame = 0;
        
        float time_left_in_frame = animation.m_iFrameDurations[0];
        result.push_back(0);
        while(1)
        {
            // Advance time by 1/FPS seconds.
            time_left_in_frame -= 1.0f / FPS;
            result.back()++;

            if(time_left_in_frame <= 0.00001f)
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
    bool CreateMasterAnimationData(SMX_LightsType type,
        const SMXPanelAnimation &animation,
        PanelLightGraphic::animation_timing_t &animation_timing, const char **error)
    {
        // Released (idle) animations use frames 0-31, and pressed animations use 32-63.
        int first_graphic = type == SMX_LightsType_Released? 0:32;

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
            animation_timing.frames[i] = i + first_graphic;

        // Set frame delays.
        memset(&animation_timing.delay[0], 0, sizeof(animation_timing.delay));
        vector<uint8_t> delays = get_frame_delays(animation);
        for(int i = 0; i < delays.size() && i < 64; ++i)
            animation_timing.delay[i] = delays[i];

        // These frame numbers are relative to the animation, so don't add first_graphic.
        animation_timing.loop_animation_frame = animation.m_iLoopFrame;

        return true;
    }

    // Pack panel graphics.
    bool CreatePanelAnimationData(PanelLightGraphic::panel_animation_data_t &panel_data,
        int pad, SMX_LightsType type, int panel, const SMXPanelAnimation &animation, const char **error)
    {
        // We have a single buffer of animation frames for each panel, which we pack
        // both the pressed and released frames into.  This is the index of the next
        // frame.
        int next_graphic_idx = type == SMX_LightsType_Released? 0:32;

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

        // Apply color scaling to the palette, in the same way SMXManager::SetLights does.
        // Do this after we've finished creating the graphic, so this is only applied to
        // the final result and doesn't affect palettization.
        for(PanelLightGraphic::color_t &color: panel_data.palettes[type].colors)
        {
            for(int i = 0; i < 3; ++i)
                color.rgb[i] = uint8_t(color.rgb[i] * 0.6666f);
        }

        return true;
    }

    // Create upload packets to upload a block of data.
    void CreateUploadPackets(vector<PanelLightGraphic::upload_packet> &packets,
        const void *data_block, int start, int size,
        uint8_t panel, int animation_idx)
    {
        const uint8_t *buf = (const uint8_t *) data_block;
        for(int offset = 0; offset < size; )
        {
            PanelLightGraphic::upload_packet packet;
            packet.panel = panel;
            packet.animation_idx = animation_idx;
            packet.offset = start + offset;

            int bytes_left = size - offset;
            packet.size = min(sizeof(PanelLightGraphic::upload_packet::data), bytes_left);
            memcpy(packet.data, buf, packet.size);
            packets.push_back(packet);

            offset += packet.size;
            buf += packet.size;
        }
    }
}

namespace LightsUploadData
{
    vector<string> commands[2];
}

// Prepare the loaded graphics for upload.
bool SMX_LightsUpload_PrepareUpload(int pad, SMX_LightsType type, const SMXPanelAnimation animations[9], const char **error)
{
    // Create master animation data.
    PanelLightGraphic::animation_timing_t master_animation_data;
    memset(&master_animation_data, 0xFF, sizeof(master_animation_data));

    // All animations of each type have the same timing for all panels, since
    // they come from the same GIF, so just use the first frame to generate the
    // master data.
    if(!ProtocolHelpers::CreateMasterAnimationData(type, animations[0], master_animation_data, error))
        return false;

    // Create panel animation data.
    PanelLightGraphic::panel_animation_data_t all_panel_data[9];
    memset(&all_panel_data, 0xFF, sizeof(all_panel_data));
    for(int panel = 0; panel < 9; ++panel)
    {
        if(!ProtocolHelpers::CreatePanelAnimationData(all_panel_data[panel], pad, type, panel, animations[panel], error))
            return false;
    }

    // We successfully created the data, so there's nothing else that can fail from
    // here on.
    //
    // A list of the final commands we'll send:
    vector<string> &pad_commands = LightsUploadData::commands[pad];
    pad_commands.clear();

    // Add an upload packet to pad_commands:
    auto add_packet_command = [&pad_commands](const PanelLightGraphic::upload_packet &packet) {
        string command((char *) &packet, sizeof(packet));
        pad_commands.push_back(command);
    };

    // Add a command to briefly delay the master, to give panels a chance to finish writing to EEPROM.
    auto add_delay = [&pad_commands](int milliseconds) {
        PanelLightGraphic::delay_packet packet;
        packet.milliseconds = milliseconds;

        string command((char *) &packet, sizeof(packet));
        pad_commands.push_back(command);
    };

    // Create the packets we'll send, grouped by panel.
    vector<PanelLightGraphic::upload_packet> packetsPerPanel[9];
    for(int panel = 0; panel < 9; ++panel)
    {
        // Only upload the panel graphic data and the palette we're changing.  If type
        // is 0 (SMX_LightsType_Released), we're uploading the first 32 graphics and palette
        // 0.  If it's 1 (SMX_LightsType_Pressed), we're uploading the second 32 graphics
        // and palette 1.
        const auto &panel_data_block = all_panel_data[panel];
        {
            int first_graphic = type == SMX_LightsType_Released? 0:32;
            const PanelLightGraphic::graphic_t *graphics = &panel_data_block.graphics[first_graphic];
            int offset = offsetof(PanelLightGraphic::panel_animation_data_t, graphics[first_graphic]);
            ProtocolHelpers::CreateUploadPackets(packetsPerPanel[panel], graphics, offset, sizeof(PanelLightGraphic::graphic_t) * 32, panel, type);
        }

        {
            const PanelLightGraphic::palette_t *palette = &panel_data_block.palettes[type];
            int offset = offsetof(PanelLightGraphic::panel_animation_data_t, palettes[type]);
            ProtocolHelpers::CreateUploadPackets(packetsPerPanel[panel], palette, offset, sizeof(PanelLightGraphic::palette_t), panel, type);
        }
    }

    // It takes 3.4ms per byte to write to EEPROM, and we need to avoid writing data
    // to any single panel faster than that or data won't be written.  However, we're
    // writing each data separately to each panel, so we can write data to panel 1, then
    // immediately write to panel 2 while panel 1 is busy doing the write.  Taking advantage
    // of this makes the upload go much faster.  Panels will miss commands while they're
    // writing data, but we don't care if panel 1 misses a command that's writing to panel
    // 2 that it would ignore anyway.
    //
    // We write the first set of packets for each panel, then explicitly delay long enough
    // for them to finish before writing the next set of packets.  

    while(1)
    {
        bool added_any_packets = false;
        int max_size = 0;
        for(int panel = 0; panel < 9; ++panel)
        {
            // Pull this panel's next packet.  It doesn't actually matter what order we
            // send the packets in.
            // Add the next packet for each panel.
            vector<PanelLightGraphic::upload_packet> &packets = packetsPerPanel[panel];
            if(packets.empty())
                continue;

            PanelLightGraphic::upload_packet packet = packets.back();
            packets.pop_back();
            add_packet_command(packet);
            max_size = max(max_size, packet.size);
            added_any_packets = true;
        }

        // Delay long enough for the biggest write in this burst to finish.  We do this
        // by sending a command to the master to tell it to delay synchronously by the
        // right amount.
        int millisecondsToDelay = lrintf(max_size * 3.4);
        add_delay(millisecondsToDelay);

        // Stop if there were no more packets to add.
        if(!added_any_packets)
            break;
    }

    // Add the master data.
    vector<PanelLightGraphic::upload_packet> masterPackets;
    ProtocolHelpers::CreateUploadPackets(masterPackets, &master_animation_data, 0, sizeof(master_animation_data), 0xFF, type);
    masterPackets.back().final_packet = true;
    for(const auto &packet: masterPackets)
        add_packet_command(packet);

    return true;
}

// Start sending a prepared upload.
//
// The commands to send to upload the data are in LightsUploadData::commands[pad].
void SMX_LightsUpload_BeginUpload(int pad, SMX_LightsUploadCallback pCallback, void *pUser)
{
    shared_ptr<SMXDevice> pDevice = SMXManager::g_pSMX->GetDevice(pad);
    vector<string> asCommands = LightsUploadData::commands[pad];
    int iTotalCommands = asCommands.size();

    // Queue all commands at once.  As each command finishes, our callback
    // will be called.
    for(int i = 0; i < asCommands.size(); ++i)
    {
        const string &sCommand = asCommands[i];
        pDevice->SendCommand(sCommand, [i, iTotalCommands, pCallback, pUser](string response) {
            // Command #i has finished being sent.
            //
            // If this isn't the last command, make sure progress isn't 100.
            // Once we send 100%, the callback is no longer valid.
            int progress;
            if(i != iTotalCommands-1)
                progress = min((i*100) / (iTotalCommands - 1), 99);
            else
                progress = 100;

            // We're currently in the SMXManager thread.  Call the user thread from
            // the user callback thread.
            SMXManager::g_pSMX->RunInHelperThread([pCallback, pUser, progress]() {
                pCallback(progress, pUser);
            });
        });
    }
}
