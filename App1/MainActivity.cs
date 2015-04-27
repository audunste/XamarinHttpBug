using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Android.OS;
using Java.Net;
using Java.Util.Logging;
using Handler = Android.OS.Handler;
using LayoutParams = Android.Views.ViewGroup.LayoutParams;

namespace App1
{
    [Activity(Label = "App1", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        int count = 1;
        private Handler handler = new Handler();
        private Action toRepeat;
        private Random rnd = new Random();

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            var view = new LinearLayout(this) {
                LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent),
                Orientation = Orientation.Vertical
            };
            var button = new Button(this) {Text = "Click me and wait for crash"};
            view.AddView(button, new LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));
            SetContentView(view);

            toRepeat = LoadHttpPage;

            button.Click += delegate
            {
                handler.Post(toRepeat);
                handler.PostDelayed(toRepeat, 50);
                handler.PostDelayed(toRepeat, 100);
                button.Text = "... will crash soon if debugging";
                button.Enabled = false;
            };
        }

        async void LoadHttpPage()
        {
            var request = new HttpRequest
            {
                Method = "GET",
                Url = "http://httpbin.org/",
                Headers = new List<HttpHeader>()
            };

            bool postBefore = rnd.Next(1, 3) == 1;
            if (postBefore)
            {
                handler.Post(toRepeat);
            }

            using (var performer = new AndroidHttpRequestPerformer())
            {
                using (var response = await performer.PerformRequestAsync(request))
                {
                    var bodyStr = await response.GetBodyStringAsync();
                    var chopAt = Math.Min(bodyStr.Length, 40);
                    Log.Debug("Test", bodyStr.Substring(0, chopAt).Replace(System.Environment.NewLine, ""));
                }
            }
            if (!postBefore)
            {
                handler.Post(toRepeat);
            }
        }

    }

    public class AndroidHttpRequestPerformer : IHttpRequestPerformer
    {
        private HttpURLConnection conn;

        public void Dispose()
        {
            if (conn != null)
            {
                conn.Dispose();
                conn = null;
            }
        }

        public Task<IHttpResponse> PerformRequestAsync(HttpRequest r)
        {
            return Task.Run(async () =>
            {
                using (var url = new URL(r.Url))
                {
                    conn = (HttpURLConnection)url.OpenConnection();
                }
                conn.RequestMethod = r.Method;
                conn.DoInput = true;

                foreach (var h in r.Headers)
                    conn.SetRequestProperty(h.Name, h.Value);

                if ((r.Method == "POST" || r.Method == "PUT") && r.Body != null)
                {
                    conn.DoOutput = true;
                    conn.SetFixedLengthStreamingMode(r.Body.Length);
                    await r.Body.CopyToAsync(conn.OutputStream, 1024);
                }

                int code = (int)conn.ResponseCode;

                var headers = new List<HttpHeader>();
                int headerIndex = 0;
                while (true)
                {
                    var key = conn.GetHeaderFieldKey(headerIndex);
                    var value = conn.GetHeaderField(headerIndex);
                    if (key == null || value == null)
                        break;
                    headers.Add(new HttpHeader(key, value));
                    ++headerIndex;
                }

                Stream input = null;
                try
                {
                    input = conn.InputStream;
                }
                catch (Java.IO.FileNotFoundException)
                {
                }

                var temp = conn;
                conn = null;
                return (IHttpResponse)new AndroidHttpResponse(temp, code, headers, input);
            });
        }
    }

    public struct HttpHeader
    {
        public readonly string Name;
        public readonly string Value;

        public HttpHeader(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public interface IHttpResponse : IDisposable
    {
        int StatusCode { get; }
        List<HttpHeader> Headers { get; }
        Task<Stream> GetBodyAsync();
    }


    public interface IHttpRequestPerformer : IDisposable
    {
        Task<IHttpResponse> PerformRequestAsync(HttpRequest request);
    }

    public class HttpRequest
    {
        public string Method;
        public string Url;
        public Stream Body;
        public List<HttpHeader> Headers;

        public Func<long> UploadProgressCallback;
        public Func<long> DownloadProgressCallback;
    }

    internal class AndroidHttpResponse : IHttpResponse
    {
        private HttpURLConnection conn;
        private Stream inputStream;

        public AndroidHttpResponse(HttpURLConnection conn, int code, List<HttpHeader> headers, Stream input)
        {
            this.conn = conn;
            StatusCode = code;
            Headers = headers;
            inputStream = input;
        }

        public void Dispose()
        {
            if (inputStream != null)
            {
                inputStream.Close();
                inputStream.Dispose();
                inputStream = null;
            }
            if (conn != null)
            {
                conn.Dispose();
                conn = null;
            }
        }

        public int StatusCode { get; private set; }
        public List<HttpHeader> Headers { get; private set; }

        public Task<Stream> GetBodyAsync()
        {
            if (inputStream == null)
                inputStream = new MemoryStream();
            return Task.FromResult(inputStream);
        }
    }

    public static class HttpExtensions
    {
        public static async Task<byte[]> GetBodyBytesAsync(this IHttpResponse response)
        {
            using (var stream = await response.GetBodyAsync())
            {
                using (var memoryStream = new MemoryStream())
                {
                    var buffer = new byte[1024];
                    while (true)
                    {
                        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                            return memoryStream.ToArray();
                        memoryStream.Write(buffer, 0, bytesRead);
                    }
                }
            }
        }

        public static async Task<string> GetBodyStringAsync(this IHttpResponse response)
        {
            var bytes = await response.GetBodyBytesAsync();
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }
}

