using UnityEditor;
using UnityEngine;

// Все картинки из Resources импортируются как спрайты: пиксель-арт без сглаживания, эффекты с мягким фильтром.
public class SpriteImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;

        var s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);

        if (assetPath.Contains("/Tiles/"))
        {
            ti.spritePixelsPerUnit = 16;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Repeat;
        }
        else
        {
            ti.spritePixelsPerUnit = 512;
            ti.filterMode = FilterMode.Bilinear;
            // иконки из Image API — 256, панели карточек — 1024, частицы — 256
            ti.maxTextureSize = assetPath.Contains("/UI/") ? 1024 : 256;
            if (assetPath.Contains("/Icons/") || assetPath.Contains("/UI/"))
                ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
