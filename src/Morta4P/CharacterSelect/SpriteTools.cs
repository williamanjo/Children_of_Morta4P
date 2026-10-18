using UnityEngine;

namespace Morta4P
{
    internal static class SpriteTools
    {
        // Copies a sprite's pixels into a new readable texture. Game textures are usually not
        // CPU-readable, so the whole atlas goes through a RenderTexture first.
        internal static Texture2D ReadSprite(Sprite sprite)
        {
            Texture2D source = sprite.texture;
            Rect r = sprite.textureRect;

            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var result = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                result.Apply();
                result.filterMode = FilterMode.Point;
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
