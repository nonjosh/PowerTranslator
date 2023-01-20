using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wox.Plugin.Logger;

namespace Translater.Youdao
{
    public static class Extend
    {
        public static void each<T>(this IEnumerable<T> src, Action<T> action)
        {
            foreach (var item in src)
            {
                action(item);
            }
        }
        public static string addQueryParameters(this string src, object obj)
        {
            return $"{src}?{obj.toFormDataBodyString()}";
        }
        public static string toFormDataBodyString(this object src)
        {
            var res = new List<string>();
            foreach (var key in src.GetType().GetProperties())
            {
                res.Add($"{key.Name}={src.GetType().GetProperty(key.Name)?.GetValue(src)}");
            }
            return string.Join("&", res);
        }
    }
    public class YoudaoTranslater
    {
        public class TranslateResponse
        {
            public struct ResStruct
            {
                public string tgt { get; set; }
                public string src { get; set; }
            }
            public struct Entry
            {
                public string[] entries { get; set; }
                public int type { get; set; }
            }
            public int errorCode { get; set; }
            public ResStruct[][]? translateResult { get; set; }
            public string? type { get; set; }
            public Entry? smartResult { get; set; }
        }


        private HttpClient client;
        private Random random;
        private MD5 md5;
        private string userAgent = "Mozilla/5.0 (X11; CrOS i686 3912.101.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/27.0.1453.116 Safari/537.36";
        public YoudaoTranslater()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
            client.DefaultRequestHeaders.Add("Referer", "https://fanyi.youdao.com/");
            client.DefaultRequestHeaders.Add("Origin", "https://fanyi.youdao.com");

            var time = DateTime.Now;

            var res = client.GetAsync("https://rlogs.youdao.com/rlog.php".addQueryParameters(new
            {
                _npid = "fanyiweb",
                _ncat = "event",
                _ncoo = "214748364.7",
                nssn = "NULL",
                _nver = "1.2.0",
                _ntms = time.Millisecond.ToString(),
                _nhrf = "newweb_translate_text"
            })).GetAwaiter().GetResult();
            client.DefaultRequestHeaders.Add("cookies", res.Headers.GetValues("Set-Cookie").First());
            this.random = new Random();
            this.md5 = MD5.Create();
        }

        private string md5Encrypt(string src)
        {
            var bytes = Encoding.UTF8.GetBytes(src);
            var res = md5.ComputeHash(bytes);
            var resStr = new StringBuilder();
            foreach (var item in res)
            {
                resStr.Append(item.ToString("x2"));
            }
            return resStr.ToString();
        }

        public TranslateResponse? translate(string src, string toLan = "AUTO", string fromLan = "AUTO")
        {
            var time = DateTime.Now;
            var ts = time.Millisecond.ToString();
            var salt = $"{ts}{random.Next(0, 9)}";
            var bv = md5Encrypt(this.userAgent);
            var sign = md5Encrypt($"fanyideskweb{src}{salt}Ygy_4c=r#e#4EX^NUGUc5");
            var data = new
            {
                i = src,
                from = fromLan,
                to = toLan,
                smartresult = "dict",
                client = "fanyideskweb",
                salt = salt,
                sign = sign,
                lts = ts,
                bv = bv,
                doctype = "json",
                version = "2.1",
                keyfrom = "fanyi.web",
                action = "FY_BY_REALTlME"
            };
            var res = client.PostAsync("https://fanyi.youdao.com/translate_o?smartresult=dict&smartresult=rule",
                                        new StringContent(data.toFormDataBodyString(),
                                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded")))
                                        .GetAwaiter()
                                        .GetResult();
            string contentStr = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            try
            {
                var translateRes = JsonSerializer.Deserialize<TranslateResponse>(contentStr);
                return translateRes;
            }
            catch (JsonException)
            {
                throw new Exception($"不能找到数据=>{contentStr}");
            }
        }
    }
}