#include "SMXGif.h"
#include <stdint.h>
#include <string>
#include <vector>
using namespace std;

// This is a simple animated GIF decoder.  It always decodes to RGBA color,
// discarding palettes, and decodes the whole file at once.

class GIFError: public exception { };

struct Palette
{
    SMXGif::Color color[256];
};

void SMXGif::GIFImage::Init(int width_, int height_)
{
    width = width_;
    height = height_;
    image.resize(width * height);
}

void SMXGif::GIFImage::Clear(const Color &color)
{
    for(int y = 0; y < height; ++y)
        for(int x = 0; x < width; ++x)
            get(x,y) = color;
}

void SMXGif::GIFImage::CropImage(SMXGif::GIFImage &dst, int crop_left, int crop_top, int crop_width, int crop_height) const
{
    dst.Init(crop_width, crop_height);

    for(int y = 0; y < crop_height; ++y)
    {
        for(int x = 0; x < crop_width; ++x)
            dst.get(x,y) = get(x + crop_left, y + crop_top);
    }
}

void SMXGif::GIFImage::Blit(SMXGif::GIFImage &src, int dst_left, int dst_top, int dst_width, int dst_height)
{
    for(int y = 0; y < dst_height; ++y)
    {
        for(int x = 0; x < dst_width; ++x)
            get(x + dst_left, y + dst_top) = src.get(x, y);
    }
}
bool SMXGif::GIFImage::operator==(const GIFImage &rhs) const
{
    return
        width == rhs.width &&
        height == rhs.height &&
        image == rhs.image;
}

class DataStream
{
public:
    DataStream(const string &data_):
        data(data_)
    {
    }

    uint8_t ReadByte()
    {
        if(pos >= data.size())
            throw GIFError();

        uint8_t result = data[pos];
        pos++;
        return result;
    }

    uint16_t ReadLE16()
    {
        uint8_t byte1 = ReadByte();
        uint8_t byte2 = ReadByte();
        return byte1 | (byte2 << 8);
    }

    void ReadBytes(string &s, int count)
    {
        s.clear();
        while(count--)
            s.push_back(ReadByte());
    }

    void skip(int bytes)
    {
        pos += bytes;
    }

private:
    const string &data;
    uint32_t pos = 0;
};

class LWZStream
{
public:
    LWZStream(DataStream &stream_):
        stream(stream_)
    {
    }

    // Read one LZW code from the input data.
    uint32_t ReadLZWCode(uint32_t bit_count)
    {
        while(bits_in_buffer < bit_count)
        {
            if(bytes_remaining == 0)
            {
                // Read the next block's byte count.
                bytes_remaining = stream.ReadByte();
                if(bytes_remaining == 0)
                    throw GIFError();
            }

            // Shift in another 8 bits into the end of self.bits.
            bits |= stream.ReadByte() << bits_in_buffer;
            bits_in_buffer += 8;
            bytes_remaining -= 1;
        }

        // Shift out bit_count worth of data from the end.
        uint32_t result = bits & ((1 << bit_count) - 1);
        bits >>= bit_count;
        bits_in_buffer -= bit_count;

        return result;
    }

    // Skip the rest of the LZW data.
    void Flush()
    {
        stream.skip(bytes_remaining);
        bytes_remaining = 0;

        // If there are any blocks past the end of data, skip them.
        while(1)
        {
            uint8_t blocksize = stream.ReadByte();
            if(blocksize == 0)
                break;
            stream.skip(blocksize);
        }
    }

private:
    DataStream &stream;
    uint32_t bits = 0;
    int bytes_remaining = 0;
    int bits_in_buffer = 0;
};

struct LWZDecoder
{
    LWZDecoder(DataStream &stream):
        lzw_stream(LWZStream(stream))
    {
        // Each frame has a single bits field.
        code_bits = stream.ReadByte();
    }

    string DecodeImage();

private:
    uint16_t code_bits;
    LWZStream lzw_stream;
};


static const int GIFBITS = 12;

string LWZDecoder::DecodeImage()
{
    uint32_t dictionary_bits = code_bits + 1;
    int prev_code1 = -1;
    int prev_code2 = -1;

    uint32_t clear = 1 << code_bits;
    uint32_t end = clear + 1;
    uint32_t next_free_slot = clear + 2;

    vector<pair<int,int>> dictionary;
    dictionary.resize(1 << GIFBITS);

    // We append to this buffer as we decode data, then append the data in reverse
    // order.
    string append_buffer;

    string result;
    while(1)
    {
        // Flush append_buffer.
        for(int i = append_buffer.size() - 1; i >= 0; --i)
            result.push_back(append_buffer[i]);
        append_buffer.clear();

        int code1 = lzw_stream.ReadLZWCode(dictionary_bits);
        // printf("%02x");
        if(code1 == end)
            break;

        if(code1 == clear)
        {
            // Clear the dictionary and reset.
            dictionary_bits = code_bits + 1;
            next_free_slot = clear + 2;
            prev_code1 = -1;
            prev_code2 = -1;
            continue;
        }

        int code2;
        if(code1 < next_free_slot)
            code2 = code1;
        else if(code1 == next_free_slot && prev_code2 != -1)
        {
            append_buffer.push_back(prev_code2);
            code2 = prev_code1;
        }
        else
            throw GIFError();

        // Walk through the linked list of codes in the dictionary and append.
        while(code2 >= clear + 2)
        {
            uint8_t append_char = dictionary[code2].first;
            code2 = dictionary[code2].second;
            append_buffer.push_back(append_char);
        }
        append_buffer.push_back(code2);

        // If we're already at the last free slot, the dictionary is full and can't be expanded.
        if(next_free_slot < (1 << dictionary_bits))
        {
            // If we have any free dictionary slots, save.
            if(prev_code1 != -1)
            {
                dictionary[next_free_slot] = make_pair(code2, prev_code1);
                next_free_slot += 1;
            }
            // If we've just filled the last dictionary slot, expand the dictionary size if possible.
            if(next_free_slot >= (1 << dictionary_bits) && dictionary_bits < GIFBITS)
                dictionary_bits += 1;
        }

        prev_code1 = code1;
        prev_code2 = code2;
    }

    // Skip any remaining data in this block.
    lzw_stream.Flush();

    return result;
}

struct GlobalGIFData
{
    int width = 0, height = 0;
    int background_index = -1;
    bool use_transparency = false;
    int transparency_index = -1;
    int duration = 0;
    int disposal_method = 0;
    bool have_global_palette = false;
    Palette palette;
};

class GIFDecoder
{
public:
    GIFDecoder(DataStream &stream_):
        stream(stream_)
    {
    }

    void ReadAllFrames(vector<SMXGif::SMXGifFrame> &frames);

private:
    bool ReadPacket(string &packet);
    Palette ReadPalette(int palette_size);
    void DecodeImage(GlobalGIFData global_data, SMXGif::GIFImage &out);

    DataStream &stream;
    SMXGif::GIFImage image;
    int frame;
};

// Read a palette with size colors.
//
// This is a simple string, with 4 RGBA bytes per color.
Palette GIFDecoder::ReadPalette(int palette_size)
{
    Palette result;
    for(int i = 0; i < palette_size; ++i)
    {
        result.color[i].color[0] = stream.ReadByte(); // R
        result.color[i].color[1] = stream.ReadByte(); // G
        result.color[i].color[2] = stream.ReadByte(); // B
        result.color[i].color[3] = 0xFF;
    }
    return result;
}

bool GIFDecoder::ReadPacket(string &packet)
{
    uint8_t packet_size = stream.ReadByte();
    if(packet_size == 0)
        return false;

    stream.ReadBytes(packet, packet_size);
    return true;
}

void GIFDecoder::ReadAllFrames(vector<SMXGif::SMXGifFrame> &frames)
{
    string header;
    stream.ReadBytes(header, 6);

    if(header != "GIF87a" && header != "GIF89a")
        throw GIFError();

    GlobalGIFData global_data;

    global_data.width = stream.ReadLE16();
    global_data.height = stream.ReadLE16();
    image.Init(global_data.width, global_data.height);

    // Ignore the aspect ratio field.  (Supporting pixel aspect ratios in a format
    // this rudimentary was almost ambitious of them...)
    uint8_t global_flags = stream.ReadByte();
    global_data.background_index = stream.ReadByte();

    // Ignore the aspect ratio field.  (Supporting pixel aspect ratios in a format
    // this rudimentary was almost ambitious of them...)
    stream.ReadByte();

    // Decode global_flags.
    uint8_t global_palette_size = global_flags & 0x7;

    global_data.have_global_palette = (global_flags >> 7) & 0x1;


    // If there's no global palette, leave it empty.
    if(global_data.have_global_palette)
        global_data.palette = ReadPalette(1 << (global_palette_size + 1));

    frame = 0;

    // Save a copy of global data, so we can restore it after each frame.
    GlobalGIFData saved_global_data = global_data;

    // Decode all packets.
    while(1)
    {
        uint8_t packet_type = stream.ReadByte();
        if(packet_type == 0x21)
        {
            // Extension packet
            uint8_t extension_type = stream.ReadByte();

            if(extension_type == 0xF9)
            {
                string packet;
                if(!ReadPacket(packet))
                    throw GIFError();

                DataStream packet_buf(packet);

                // Graphics control extension
                uint8_t gce_flags = packet_buf.ReadByte();
                global_data.duration = packet_buf.ReadLE16();
                global_data.transparency_index = packet_buf.ReadByte();

                global_data.use_transparency = bool(gce_flags & 1);
                global_data.disposal_method = (gce_flags >> 2) & 0xF;
                if(!global_data.use_transparency)
                    global_data.transparency_index = -1;
            }

            // Read any remaining packets in this extension packet.
            while(1)
            {
                string packet;
                if(!ReadPacket(packet))
                    break;
            }
        }
        else if(packet_type == 0x2C)
        {
            // Image data
            SMXGif::GIFImage frame_image;
            DecodeImage(global_data, frame_image);

            SMXGif::SMXGifFrame gif_frame;
            gif_frame.width = global_data.width;
            gif_frame.height = global_data.height;
            gif_frame.milliseconds = global_data.duration * 10;
            gif_frame.frame = frame_image;

            // If this frame is identical to the previous one, just extend the previous frame.
            if(!frames.empty() && gif_frame.frame == frames.back().frame)
            {
                frames.back().milliseconds += gif_frame.milliseconds;
                continue;
            }

            frames.push_back(gif_frame);

            frame++;

            // Reset GCE (frame-specific) data.
            global_data = saved_global_data;
        }
        else if(packet_type == 0x3B)
        {
            // EOF
            return;
        }
        else
            throw GIFError();
    }
}

// Decode a single GIF image into out, leaving this->image ready for
// the next frame (with this frame's dispose applied).
void GIFDecoder::DecodeImage(GlobalGIFData global_data, SMXGif::GIFImage &out)
{
    uint16_t block_left = stream.ReadLE16();
    uint16_t block_top = stream.ReadLE16();
    uint16_t block_width = stream.ReadLE16();
    uint16_t block_height = stream.ReadLE16();
    uint8_t local_flags = stream.ReadByte();

    // area = (block_left, block_top, block_left + block_width, block_top + block_height)
    // Extract flags:
    uint8_t have_local_palette = (local_flags >> 7) & 1;
    // bool interlaced = (local_flags >> 6) & 1;
    uint8_t local_palette_size = (local_flags >> 0) & 0x7;
    // print 'Interlaced:', interlaced

    // We don't support interlaced GIFs right now.
    // assert interlaced == 0

    // If this frame has a local palette, use it.  Otherwise, use the global palette.
    Palette active_palette = global_data.palette;
    if(have_local_palette)
        active_palette = ReadPalette(1 << (local_palette_size + 1));

    if(!global_data.have_global_palette && !have_local_palette)
    {
        // We have no palette.  This is an invalid file.
        throw GIFError();
    }

    if(frame == 0)
    {
        // On the first frame, clear the buffer.  If we have a transparency index,
        // clear to transparent.  Otherwise, clear to the background color.
        if(global_data.transparency_index != -1)
            image.Clear(SMXGif::Color(0,0,0,0));
        else
            image.Clear(active_palette.color[global_data.background_index]);
    }

    // Decode the compressed image data.
    LWZDecoder decoder(stream);
    string decompressed_data = decoder.DecodeImage();

    if(decompressed_data.size() < block_width*block_height)
        throw GIFError();

    // Save the region to restore after decoding.
    SMXGif::GIFImage dispose;
    if(global_data.disposal_method <= 1)
    {
        // No disposal.
    }
    else if(global_data.disposal_method == 2)
    {
        // Clear the region to a background color afterwards.
        dispose.Init(block_width, block_height);

        if(global_data.transparency_index != -1)
            dispose.Clear(SMXGif::Color(0,0,0,0));
        else
        {
            uint8_t palette_idx = global_data.background_index;
            dispose.Clear(active_palette.color[palette_idx]);
        }

    }
    else if(global_data.disposal_method == 3)
    {
        // Restore the previous frame afterwards.
        image.CropImage(dispose, block_left, block_top, block_width, block_height);
    }
    else
    {
        // Unknown disposal method
    }

    int pos = 0;
    for(int y = block_top; y < block_top + block_height; ++y)
    {
        for(int x = block_left; x < block_left + block_width; ++x)
        {
            uint8_t palette_idx = decompressed_data[pos];
            pos++;

            if(palette_idx == global_data.transparency_index)
            {
                // If this pixel is transparent, leave the existing color in place.
            }
            else
            {
                image.get(x,y) = active_palette.color[palette_idx];
            }
        }
    }

    // Copy the image before we run dispose.
    out = image;

    // Restore the dispose area.
    if(dispose.width != 0)
        image.Blit(dispose, block_left, block_top, block_width, block_height);
}

bool SMXGif::DecodeGIF(string buf, vector<SMXGif::SMXGifFrame> &frames)
{
    DataStream stream(buf);
    GIFDecoder gif(stream);
    try {
        gif.ReadAllFrames(frames);
    } catch(GIFError &) {
        // We don't return error strings for this, just success or failure.
        return false;
    }
    return true;
}
