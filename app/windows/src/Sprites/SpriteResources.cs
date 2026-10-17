using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace CrossPet
{
    /// <summary>立绘（https://crosspet.data/characters/…）由程序自己读文件交给 WebView2，不用「虚拟域名 → 文件夹」映射。
    /// 为什么：有的用户那里，WebView2 把数据目录（%LOCALAPPDATA%\CrossPet）映射成虚拟域名时请求整个失败——
    /// 1.3.3 立绘全是破图，1.3.4 测试版 1 把网页也挪到数据目录后连网页都打不开；安装目录的映射一直正常。
    /// 根本原因没查清（数据目录里有 WebView2 自己的浏览器数据？新版 WebView2 的限制？），换成自己读文件后，
    /// 报告问题的用户那里 358 张立绘全部正常。这个办法是那位用户排查并实测的，感谢。
    /// 注意：同一个域名不能再做文件夹映射，否则映射优先、这里收不到请求</summary>
    static class SpriteResources
    {
        public static void Attach(CoreWebView2 core)
        {
            core.AddWebResourceRequestedFilter($"https://{Store.DataHost}/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += async (_, e) =>
            {
                if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != Store.DataHost || !uri.IsDefaultPort) return;
                // 启动时网页会预加载全部立绘（几百张）：文件放到后台读，界面线程不卡；读完回到界面线程再答复（WebView2 的对象只能在界面线程用）
                var deferral = e.GetDeferral();
                try
                {
                    if (e.Request.Method != "GET" && e.Request.Method != "HEAD") { e.Response = Reply(core, 405, "Method Not Allowed"); return; }
                    if (!SpritePath.TryResolve(Store.Characters, uri.AbsolutePath, out var file, out var mime)) { e.Response = Reply(core, 403, "Forbidden"); return; }
                    if (!File.Exists(file)) { e.Response = Reply(core, 404, "Not Found"); return; }
                    // 整个文件读进内存再交出去：WebView2 在这个回调结束后才异步读流，不能交一个马上要关掉的文件流
                    var bytes = await Task.Run(() => File.ReadAllBytes(file));
                    var body = e.Request.Method == "HEAD" ? new byte[0] : bytes;
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(body, false), 200, "OK",
                        Headers + $"Content-Type: {mime}\r\nContent-Length: {bytes.Length}\r\n");
                }
                catch (Exception ex)
                {
                    // 读图时窗口被关掉（比如设置窗口）、WebView2 已经没了：答复也会出错。这是 async void，漏出去的异常会让整个程序崩掉，全部接住
                    Store.Log("读立绘失败: " + uri.AbsolutePath + " — " + ex.Message);
                    try { e.Response = Reply(core, 500, "Read Failed"); } catch { }
                }
                finally { try { deferral.Complete(); } catch { } }
            };
        }

        // 网页在另一个域名（crosspet.app）：允许它读；立绘可能被用户换掉，不缓存
        static readonly string Headers = $"Access-Control-Allow-Origin: https://{Store.AppHost}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n";

        static CoreWebView2WebResourceResponse Reply(CoreWebView2 core, int status, string reason) =>
            core.Environment.CreateWebResourceResponse(new MemoryStream(new byte[0], false), status, reason,
                Headers + "Content-Type: text/plain; charset=utf-8\r\nContent-Length: 0\r\n");
    }
}
