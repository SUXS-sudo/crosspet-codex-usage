using System;

namespace CrossPet
{
    /// <summary>网页大小核对（不依赖界面，tests/windows/LogicTests 测）：只用来记日志，不改缩放。
    /// 1.3.4 有几位用户（系统缩放大于 100%）遇到模型、气泡被裁。1.3.5 测试查到一个原因：运行中换了系统缩放（或拖到缩放不同的屏幕），
    /// 网页控件没跟着重算网页区域（见 Pet.ResyncWebForDpi）。曾经试过量网页大小、对不上就自动改缩放，测试里没碰到一次真该改的，
    /// 倒是拖滑块时网页还没排完就量了、改错了两次——所以改成只记录：两次量到的一样、又和画布对不上，才写日志，留给下一个排查的人</summary>
    static class ZoomCheck
    {
        /// <summary>w1/h1、w2/h2：隔一会儿量的两次网页大小（innerWidth / innerHeight）。两次差 1 以内且和应有的画布差 2% 以上返回 true</summary>
        public static bool Off(double expectedW, double expectedH, double w1, double h1, double w2, double h2)
        {
            if (expectedW <= 0 || expectedH <= 0 || w1 <= 0 || h1 <= 0) return false;
            if (Math.Abs(w1 - w2) > 1 || Math.Abs(h1 - h2) > 1) return false;   // 还在变：拖滑块、换缩放的半路
            return Math.Abs(w1 / expectedW - 1) > 0.02 || Math.Abs(h1 / expectedH - 1) > 0.02;
        }
    }
}
