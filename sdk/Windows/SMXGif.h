#ifndef SMXGif_h
#define SMXGif_h

#include <stdint.h>
#include <string>
#include <vector>

// This is a simple internal GIF decoder.  It's only meant to be used by
// SMXConfig.
namespace SMXGif
{
    struct Color
    {
        uint8_t color[4];
        Color()
        {
            memset(color, 0, sizeof(color));
        }

        Color(uint8_t r, uint8_t g, uint8_t b, uint8_t a)
        {
            color[0] = r;
            color[1] = g;
            color[2] = b;
            color[3] = a;
        }
        bool operator==(const Color &rhs) const
        {
            return !memcmp(color, rhs.color, sizeof(color));
        }
    };

    struct GIFImage
    {
        int width = 0, height = 0;
        void Init(int width, int height);

        Color get(int x, int y) const { return image[y*width+x]; }
        Color &get(int x, int y) { return image[y*width+x]; }

        // Clear to a solid color.
        void Clear(const Color &color);

        // Copy a rectangle from this image into dst.
        void CropImage(GIFImage &dst, int crop_left, int crop_top, int crop_width, int crop_height) const;

        // Copy src into a rectangle in this image.
        void Blit(GIFImage &src, int dst_left, int dst_top, int dst_width, int dst_height);

        bool operator==(const GIFImage &rhs) const;

    private:
        std::vector<Color> image;
    };

    struct SMXGifFrame
    {
        int width = 0, height = 0;

        // GIF images have a delay in 10ms units.  We use 1ms for clarity.
        int milliseconds = 0;

        GIFImage frame;
    };

    // Decode a GIF into a list of frames.
    bool DecodeGIF(std::string buf, std::vector<SMXGifFrame> &frames);
}

void gif_test();

#endif
