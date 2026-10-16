using System;
using System.IO;

namespace CrossPet
{
    /// <summary>立绘请求 https://crosspet.data/characters/&lt;角色&gt;/&lt;图&gt; → 角色目录里的文件。
    /// 只认这一种形状、只给 .webp / .png，别的（设置、character.json、目录跳转）一律不给。
    /// 不依赖 WebView2，tests/windows/LogicTests 在 macOS 上也能测</summary>
    static class SpritePath
    {
        public static bool TryResolve(string charactersRoot, string absolutePath, out string file, out string mime)
        {
            file = mime = null;
            var parts = (absolutePath ?? "").Split('/');
            if (parts.Length != 4 || parts[0] != "" || parts[1] != "characters") return false;
            var character = Uri.UnescapeDataString(parts[2]);
            var name = Uri.UnescapeDataString(parts[3]);
            if (!SafeSegment(character) || !SafeSegment(name)) return false;
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (ext != ".webp" && ext != ".png") return false;
            var root = Path.GetFullPath(charactersRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, character, name));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            file = full;
            mime = ext == ".webp" ? "image/webp" : "image/png";
            return true;
        }

        // 一段目录名 / 文件名：解码后不能是空、. 、..，不能带路径分隔符、盘符冒号、Windows 不认的字符，结尾不能是点或空格
        // （Windows 会悄悄去掉结尾的点和空格，换成另一个文件）。这些按 Windows 的规矩写死，在 macOS 上测也一样
        const string Bad = "/\\:*?\"<>|";
        static bool SafeSegment(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s == "." || s == ".." || s.EndsWith(".") || s.EndsWith(" ")) return false;
            foreach (var c in s) if (c < 32 || Bad.IndexOf(c) >= 0) return false;
            return s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }
    }
}
