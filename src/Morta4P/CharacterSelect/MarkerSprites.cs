using UnityEngine;

namespace Morta4P
{
    // Builds "P3"/"P4" marker sprites in the style of the game's "Player 01" sprite:
    // a shaded triangle on top and a pixel "P1" label drawn with 2x2 blocks underneath.
    // The triangle is hue-shifted, the "P" is recolored and the digit is redrawn.
    internal static class MarkerSprites
    {
        // Layout of the 27x32 "Player 01" sprite, in image rows (0 = top).
        private const int TriangleLastRow = 13;
        private const int LabelFirstRow = 18;
        private const int DigitFirstColumn = 16;
        private const int Block = 2;

        // 5x7 digits, same proportions as the game's "1" and "2".
        private static readonly string[] Three =
        {
            ".XXX.",
            "X...X",
            "....X",
            "..XX.",
            "....X",
            "X...X",
            ".XXX.",
        };

        private static readonly string[] Four =
        {
            "...X.",
            "..XX.",
            ".X.X.",
            "X..X.",
            "XXXXX",
            "...X.",
            "...X.",
        };

        private static readonly Sprite[] cache_ = new Sprite[2];

        // index 0 = P3, 1 = P4. Returns null if the source sprite cannot be read.
        internal static Sprite Get(int index, Sprite p1)
        {
            if (cache_[index] != null)
            {
                return cache_[index];
            }
            if (p1 == null)
            {
                return null;
            }

            Texture2D tex = SpriteTools.ReadSprite(p1);
            int w = tex.width;
            int h = tex.height;
            if (w < DigitFirstColumn + 5 * Block || h < LabelFirstRow + 7 * Block)
            {
                Plugin.Log.LogWarning($"Marker sprite {p1.name} is {w}x{h}, too small to redraw");
                Object.Destroy(tex);
                return null;
            }

            float hue = index == 0 ? 120f / 360f : 32f / 360f;
            Color label = index == 0 ? new Color32(0x2E, 0xD8, 0x3A, 0xFF) : new Color32(0xFF, 0x96, 0x00, 0xFF);
            string[] digit = index == 0 ? Three : Four;
            var clear = new Color(0f, 0f, 0f, 0f);

            for (int row = 0; row < h; row++)
            {
                int y = h - 1 - row; // texture rows start at the bottom
                for (int x = 0; x < w; x++)
                {
                    Color c = tex.GetPixel(x, y);
                    if (row <= TriangleLastRow)
                    {
                        if (c.a > 0f)
                        {
                            Color.RGBToHSV(c, out _, out float s, out float v);
                            Color shifted = Color.HSVToRGB(hue, s, v);
                            shifted.a = c.a;
                            tex.SetPixel(x, y, shifted);
                        }
                    }
                    else if (row >= LabelFirstRow)
                    {
                        if (x >= DigitFirstColumn - 2)
                        {
                            tex.SetPixel(x, y, clear);
                        }
                        else if (c.a > 0f)
                        {
                            tex.SetPixel(x, y, label);
                        }
                    }
                }
            }

            for (int gy = 0; gy < digit.Length; gy++)
            {
                for (int gx = 0; gx < digit[gy].Length; gx++)
                {
                    if (digit[gy][gx] != 'X')
                    {
                        continue;
                    }
                    for (int by = 0; by < Block; by++)
                    {
                        for (int bx = 0; bx < Block; bx++)
                        {
                            int x = DigitFirstColumn + gx * Block + bx;
                            int row = LabelFirstRow + gy * Block + by;
                            tex.SetPixel(x, h - 1 - row, label);
                        }
                    }
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "Morta4P Player " + (index + 3);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), p1.pixelsPerUnit);
            sprite.name = tex.name;
            cache_[index] = sprite;
            return sprite;
        }
    }
}
