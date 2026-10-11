using UnityEngine;

namespace Burrow
{
    // The one font + material every TextMesh in the app shares. The built-in font's own
    // material uses GUI/Text Shader, which is not guaranteed to ship; ours is.
    public static class BurrowText
    {
        static Font _font; static Material _mat;
        public static Font Font => _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Material Material
        {
            get
            {
                if (_mat != null) return _mat;
                var sh = Shader.Find("Burrow/Text");
                _mat = sh != null ? new Material(sh) { mainTexture = Font.material.mainTexture } : Font.material;
                Font.textureRebuilt += f => { if (f == Font && _mat != Font.material) _mat.mainTexture = Font.material.mainTexture; };
                return _mat;
            }
        }
    }
}
