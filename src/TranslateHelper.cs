using Wox.Plugin;
using Wox.Plugin.Logger;
using Translater.Utils;

namespace Translater;

public class TranslateFailedException : Exception
{
    public TranslateFailedException() : base("translate failed")
    {

    }
}

public class TranslateHelper
{
    public struct TranslateTarget
    {
        public string src;
        public string toLan;
    }
    public const string toLanSplit = "->";
    public bool inited => this.youdaoTranslater != null;
    private object initLock = new Object();
    private Youdao.YoudaoTranslater? youdaoTranslater;
    private Youdao.V2.YoudaoTranslater? youdaoTranslaterV2;
    private Youdao.Backup.BackUpTranslater? backUpTranslater;
    private long lastInitTime = 0;
    private IPublicAPI publicAPI;
    private List<Youdao.ITranslater> translaters;
    public TranslateHelper(IPublicAPI publicAPI)
    {
        this.translaters = new List<Youdao.ITranslater>();
        this.initTranslater();
        this.publicAPI = publicAPI;

        // backup translater, We don't need to initialize it with the others, because it doesn't have an error
        this.backUpTranslater = new Youdao.Backup.BackUpTranslater();
    }
    public TranslateTarget ParseRawSrc(string src)
    {
        if (src.Contains(toLanSplit))
        {
            var srcArr = src.Split(toLanSplit);
            return new TranslateTarget
            {
                src = srcArr.First().TrimEnd().TrimStart(),
                toLan = srcArr.Last().TrimEnd().TrimStart()
            };
        }
        return new TranslateTarget
        {
            src = src,
            toLan = "AUTO"
        };
    }
    public List<ResultItem> QueryTranslate(string raw, string translateFrom = "user input")
    {
        var res = new List<ResultItem>();
        if (raw.Length == 0)
            return res;
        var target = ParseRawSrc(raw);
        string src = target.src;
        string toLan = target.toLan;
        try
        {
            try
            {
                if (!TranslateV2(res, src, toLan, translateFrom) && !TranslateV1(res, src, toLan, translateFrom))
                    throw new TranslateFailedException();
            }
            catch (TranslateFailedException)
            {
                if (!TranslateBackup(res, src, toLan, translateFrom))
                {
                    res.Add(new ResultItem
                    {
                        Title = raw,
                        SubTitle = $"can not translate {src} to {toLan}"
                    });
                }

            }
        }
        catch (Exception err)
        {
            res.Add(new ResultItem
            {
                Title = "Some error happen!",
                SubTitle = $"press enter to get help, msg: {err.Message}",
                Action = (ev) =>
                {
                    UtilsFun.SetClipboardText("https://github.com/N0I0C0K/PowerToysRun.Plugin.Translater/issues?q=");
                    this.publicAPI.ShowMsg("Copy!", "The URL has been copied, Go to your browser and visit the website for help.");
                    return true;
                }

            });
            Log.Error(err.ToString(), typeof(Translater));
        }
        return res;
    }

    /// <summary>
    /// old version of translate. will be removed in the next version
    /// </summary>
    private bool TranslateV1(List<ResultItem> res, string src, string toLan, string translateFrom)
    {
        if (youdaoTranslater == null)
            return false;
        var translateRes = youdaoTranslater!.Translate(src, toLan);
        if (translateRes == null || translateRes.errorCode != 0)
            return false;
        res.Add(new ResultItem
        {
            Title = translateRes.translateResult![0][0].tgt,
            SubTitle = $"{src} [{translateRes.type}] [Translate form {translateFrom}]"
        });
        if (translateRes.smartResult != null)
        {
            translateRes.smartResult?.entries.each((s) =>
            {
                string t = s.Replace("\r\n", " ").TrimStart().Replace(" ", "");
                if (string.IsNullOrEmpty(t))
                    return;
                res.Add(new ResultItem
                {
                    Title = t,
                    SubTitle = "[smart result]"
                });
            });
        }
        return true;
    }

    /// <summary>
    /// The latest api of Youdao translate
    /// </summary>
    private bool TranslateV2(List<ResultItem> res, string src, string toLan, string translateFrom)
    {
        if (this.youdaoTranslaterV2 == null)
            return false;
        var translateRes = this.youdaoTranslaterV2.Translate(src, toLan);
        if (translateRes == null || translateRes.code != 0)
            return false;
        var tres = translateRes.translateResult![0][0];
        res.Add(new ResultItem
        {
            Title = tres.tgt,
            SubTitle = $"{src}({tres.srcPronounce ?? "-"}) [{translateRes.type}] [Translate form {translateFrom}] v2"
        });
        if (translateRes.dictResult != null)
        {
            if (translateRes.dictResult?.ce != null)
            {
                var ce = translateRes.dictResult?.ce;
                if (ce?.word?.trs != null)
                {
                    foreach (var trs in ce?.word?.trs!)
                    {
                        res.Add(new ResultItem
                        {
                            Title = trs.text ?? "[None]",
                            SubTitle = trs.tran ?? "[smart result]"
                        });
                    }
                }
            }
            else if (translateRes.dictResult?.ec != null)
            {
                var ec = translateRes.dictResult?.ec;
                if (ec?.word?.trs != null)
                {
                    foreach (var trs in ec?.word?.trs!)
                    {
                        res.Add(new ResultItem
                        {
                            Title = trs.tran ?? "[None]",
                            SubTitle = trs.pos ?? "-"
                        });
                    }
                }
                if (ec?.exam_type != null)
                {
                    res.Add(new ResultItem
                    {
                        Title = String.Join(" | ", ec?.exam_type!),
                        SubTitle = "exam"
                    });
                }
            }
        }
        return true;
    }

    private bool TranslateBackup(List<ResultItem> res, string src, string toLan, string translateFrom)
    {
        var translateRes = this.backUpTranslater?.Translate(src, toLan);
        if (translateRes == null || translateRes.errorCode != "0")
            return false;
        res.Add(new ResultItem
        {
            Title = String.Join(",", translateRes.translation!),
            SubTitle = $"{src}({translateRes?.basic?.phonetic ?? "-"}) [{translateRes?.tranType}] [Translate form {translateFrom}] backup"
        });
        if (translateRes!.web != null)
        {
            foreach (var web in translateRes.web)
            {
                res.Add(new ResultItem
                {
                    Title = String.Join(" | ", web.value),
                    SubTitle = $"{web.key} [smart result]"
                });
            }
        }
        return true;
    }

    public bool initTranslater()
    {
        lock (this.initLock)
        {
            var now = UtilsFun.GetUtcTimeNow();
            // if one of the api is inited, then return true. because we need. Because we want to minimize the number of initializations
            if (this.youdaoTranslaterV2 != null || this.youdaoTranslater != null)
                return true;
            if (now - this.lastInitTime < 1000 * 30)
                return false;
            this.lastInitTime = now;
            try
            {
                youdaoTranslater ??= new Youdao.YoudaoTranslater();
                youdaoTranslaterV2 ??= new Youdao.V2.YoudaoTranslater();
                return true;
            }
            catch (Exception err)
            {
                Log.Warn(err.Message, typeof(Translater));
                return false;
            }
        }
    }
}
