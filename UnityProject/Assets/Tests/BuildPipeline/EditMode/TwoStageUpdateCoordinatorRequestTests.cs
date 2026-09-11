using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using TEngine;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.BuildPipelineTests
{
    /// <summary>
    /// 通过本地单请求服务器验证两阶段入口下载的错误分类与取消语义。
    /// </summary>
    public sealed class TwoStageUpdateCoordinatorRequestTests
    {
        private const int EntryLimit = 4 * 1024;

        [UnityTest]
        public IEnumerator OversizedEntry_ReportsLimitAndAddress()
        {
            return UniTask.ToCoroutine(async () =>
            {
                byte[] body = Encoding.UTF8.GetBytes(new string('x', EntryLimit + 1));
                using (SingleResponseServer server = new SingleResponseServer(200, "OK", body))
                using (UpdateSettingScope setting = new UpdateSettingScope())
                {
                    InvalidOperationException exception = await CaptureFailureAsync(
                        server.Url, setting.Value, EntryLimit, true, "固定入口", CancellationToken.None);
                    StringAssert.Contains("超过 4096 字节上限", exception.Message);
                    StringAssert.Contains(server.Url, exception.Message);
                }
            });
        }

        [UnityTest]
        public IEnumerator Redirect_ReportsRedirectStageAndAddress()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using (SingleResponseServer server = new SingleResponseServer(
                           302, "Found", Array.Empty<byte>(), "Location: /other.json\r\n"))
                using (UpdateSettingScope setting = new UpdateSettingScope())
                {
                    InvalidOperationException exception = await CaptureFailureAsync(
                        server.Url, setting.Value, EntryLimit, true, "固定入口", CancellationToken.None);
                    StringAssert.Contains("固定入口 不允许重定向", exception.Message);
                    StringAssert.Contains("code=302", exception.Message);
                    StringAssert.Contains(server.Url, exception.Message);
                }
            });
        }

        [UnityTest]
        public IEnumerator NotFound_ReportsRequestStageAndAddress()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using (SingleResponseServer server = new SingleResponseServer(
                           404, "Not Found", Encoding.UTF8.GetBytes("missing")))
                using (UpdateSettingScope setting = new UpdateSettingScope())
                {
                    InvalidOperationException exception = await CaptureFailureAsync(
                        server.Url, setting.Value, EntryLimit, true, "Release descriptor", CancellationToken.None);
                    StringAssert.Contains("Release descriptor请求失败", exception.Message);
                    StringAssert.Contains("code=404", exception.Message);
                    StringAssert.Contains(server.Url, exception.Message);
                }
            });
        }

        [UnityTest]
        public IEnumerator Cancellation_RemainsOperationCanceledException()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using (SingleResponseServer server = new SingleResponseServer(
                           200, "OK", Encoding.UTF8.GetBytes("delayed"), responseDelayMilliseconds: 5000))
                using (UpdateSettingScope setting = new UpdateSettingScope())
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    UniTask<byte[]> request = InvokeDownloadBytesAsync(
                        server.Url, setting.Value, EntryLimit, true, "固定入口", cancellation.Token);
                    await UniTask.DelayFrame(1);
                    cancellation.Cancel();

                    try
                    {
                        await request;
                        Assert.Fail("取消后的请求不应成功。");
                    }
                    catch (OperationCanceledException)
                    {
                        // 取消必须保持原始语义，不能包装为请求失败。
                    }
                }
            });
        }

        private static async UniTask<InvalidOperationException> CaptureFailureAsync(
            string url,
            UpdateSetting setting,
            int maxBytes,
            bool rejectRedirect,
            string responseName,
            CancellationToken cancellationToken)
        {
            try
            {
                await InvokeDownloadBytesAsync(
                    url, setting, maxBytes, rejectRedirect, responseName, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return exception;
            }

            Assert.Fail("请求应失败，但实际成功。");
            return null;
        }

        private static UniTask<byte[]> InvokeDownloadBytesAsync(
            string url,
            UpdateSetting setting,
            int maxBytes,
            bool rejectRedirect,
            string responseName,
            CancellationToken cancellationToken)
        {
            Type coordinatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Procedure.TwoStageUpdateCoordinator", false))
                .FirstOrDefault(type => type != null);
            Assert.IsNotNull(coordinatorType, "未找到主包 TwoStageUpdateCoordinator 类型。");

            MethodInfo method = coordinatorType.GetMethod(
                "DownloadBytesAsync",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "未找到 TwoStageUpdateCoordinator.DownloadBytesAsync。");

            return (UniTask<byte[]>)method.Invoke(null, new object[]
            {
                url,
                setting,
                cancellationToken,
                maxBytes,
                false,
                rejectRedirect,
                responseName,
            });
        }

        private sealed class UpdateSettingScope : IDisposable
        {
            public UpdateSettingScope()
            {
                Value = ScriptableObject.CreateInstance<UpdateSetting>();
                Value.AllowInsecureLoopbackHttp = true;
            }

            public UpdateSetting Value { get; }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Value);
            }
        }

        private sealed class SingleResponseServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Thread _thread;
            private readonly ManualResetEvent _shutdown = new ManualResetEvent(false);
            private readonly byte[] _response;
            private readonly int _responseDelayMilliseconds;

            public SingleResponseServer(
                int statusCode,
                string reasonPhrase,
                byte[] body,
                string extraHeaders = "",
                int responseDelayMilliseconds = 0)
            {
                body = body ?? Array.Empty<byte>();
                _responseDelayMilliseconds = responseDelayMilliseconds;
                string headers =
                    $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                    extraHeaders +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
                _response = new byte[headerBytes.Length + body.Length];
                Buffer.BlockCopy(headerBytes, 0, _response, 0, headerBytes.Length);
                Buffer.BlockCopy(body, 0, _response, headerBytes.Length, body.Length);

                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Url = $"http://127.0.0.1:{port}/current.json";
                _thread = new Thread(ServeOneRequest)
                {
                    IsBackground = true,
                    Name = "TwoStageRequestTestServer",
                };
                _thread.Start();
            }

            public string Url { get; }

            public void Dispose()
            {
                _shutdown.Set();
                _listener.Stop();
                if (_thread.IsAlive)
                    _thread.Join(5000);
                _shutdown.Dispose();
            }

            private void ServeOneRequest()
            {
                try
                {
                    using (TcpClient client = _listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        ReadRequestHeaders(stream);
                        if (_responseDelayMilliseconds > 0 && _shutdown.WaitOne(_responseDelayMilliseconds))
                            return;
                        stream.Write(_response, 0, _response.Length);
                        stream.Flush();
                    }
                }
                catch (SocketException)
                {
                    // 测试取消或清理服务器时关闭监听器会中断 Accept。
                }
                catch (IOException)
                {
                    // 客户端取消请求后关闭连接属于预期行为。
                }
                catch (ObjectDisposedException)
                {
                    // 测试清理阶段已释放监听器。
                }
            }

            private static void ReadRequestHeaders(Stream stream)
            {
                int matched = 0;
                byte[] terminator = { 13, 10, 13, 10 };
                while (matched < terminator.Length)
                {
                    int value = stream.ReadByte();
                    if (value < 0)
                        return;
                    matched = value == terminator[matched]
                        ? matched + 1
                        : value == terminator[0] ? 1 : 0;
                }
            }
        }
    }
}
