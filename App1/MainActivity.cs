using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Util;
using Android.Widget;
using Android.OS;
using Java.Net;
using Handler = Android.OS.Handler;
using LayoutParams = Android.Views.ViewGroup.LayoutParams;

namespace App1
{
    [Activity(Label = "HTTP sigsegv", MainLauncher = true)]
    public class MainActivity : Activity
    {
        private Handler handler = new Handler();
        private Action toRepeat;
        private Random rnd = new Random();
        private int byteCount;
        private Button button;

        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            var view = new LinearLayout(this) {
                LayoutParameters = new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent),
                Orientation = Orientation.Vertical
            };
            button = new Button(this) {Text = "Click me and wait for crash"};
            view.AddView(button, new LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));
            SetContentView(view);

            toRepeat = LoadHttpPage;

            button.Click += delegate
            {
                handler.Post(toRepeat);
                handler.PostDelayed(toRepeat, 50);
                handler.PostDelayed(toRepeat, 100);
                button.Text = "...";
                button.Enabled = false;
            };
        }

        async void LoadHttpPage()
        {
            // Repost sometimes before and sometimes after the request to provoke the sigsegv.
            // If we'd always posted before, we'd trigger an exception eventually instead
            // If we'd always posted after, we'd not cause an error at all
            bool postBefore = rnd.Next(1, 3) == 1;
            if (postBefore)
            {
                handler.Post(toRepeat);
            }
            using (var performer = new HttpPerformer())
            {
                using (var stream = await performer.PerformRequestAsync())
                {
                    byte[] bytes;
                    using (var memoryStream = new MemoryStream())
                    {
                        var buffer = new byte[1024];
                        while (true)
                        {
                            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (bytesRead == 0) {
                                bytes = memoryStream.ToArray();
                                break;
                            }
                            memoryStream.Write(buffer, 0, bytesRead);
                        }
                    }
                    byteCount += bytes.Length;
                    var bodyStr = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                    var chopAt = Math.Min(bodyStr.Length, 40);
                    Log.Debug("Test", bodyStr.Substring(0, chopAt).Replace(System.Environment.NewLine, ""));
                    button.Text = byteCount + " bytes and " + HttpPerformer.TotalHeaderCount +" headers read...";
                }
            }
            if (!postBefore)
            {
                handler.Post(toRepeat);
            }
        }

    }

    public class HttpPerformer : IDisposable
    {
        public static int TotalHeaderCount;
        private HttpURLConnection conn;

        public void Dispose()
        {
            if (conn != null)
            {
                conn.Dispose();
                conn = null;
            }
        }

        public Task<Stream> PerformRequestAsync()
        {
            return Task.Run(() =>
            {
                using (var url = new URL("http://httpbin.org/"))
                {
                    conn = (HttpURLConnection)url.OpenConnection();
                }
                conn.RequestMethod = "GET";
                conn.DoInput = true;

                if ((int)conn.ResponseCode != 200) {
                    return null;
                }

                int headerIndex = 0;
                while (true) {
                    var key = conn.GetHeaderFieldKey(headerIndex);
                    var value = conn.GetHeaderField(headerIndex);
                    if (key == null || value == null)
                        break;
                    TotalHeaderCount++;
                    headerIndex++;
                }

                try {
                    return conn.InputStream;
                } catch (IOException) {
                    return null;
                } finally {
                    conn = null;
                }
            });
        }
    }

}

