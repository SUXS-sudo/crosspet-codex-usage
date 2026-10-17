namespace CrossPet
{
    /// <summary>窗口扩展样式里和「藏起来」有关的那一位（不依赖界面，tests/windows/LogicTests 测）</summary>
    static class ConcealStyle
    {
        public const int WS_EX_TRANSPARENT = 0x20;

        /// <summary>藏起来时让鼠标穿过整个窗口。只把不透明度设成 0 不够：Windows 判断点没点到看的是每个像素自己的透明度，
        /// 不看整体不透明度，铺在她身上那层几乎透明的「能点的区域」照样接住鼠标，原来那块看不见却点不透（1.3.4 测出来的）</summary>
        public static int ClickThrough(int exStyle, bool on) => on ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;
    }
}
