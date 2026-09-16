using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace RecipeBrowser;

/// <summary>
/// Terraria 的 TextureAssets 里全是 ReLogic 的 Asset&lt;Texture2D&gt;（运行期不能直接引用该类型），
/// 所以统一走反射取值，并把结果缓存起来。
/// </summary>
internal static class GameRefs
{
    private static bool _init;
    private static PropertyInfo _valueProp;
    private static FieldInfo _fItem, _fNpc, _fTile;
    private static readonly FieldInfo[] _fBack = new FieldInfo[32];

    private static readonly Dictionary<int, Texture2D> _itemCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> _npcCache = new Dictionary<int, Texture2D>();
    private static readonly Dictionary<int, Texture2D> _tileCache = new Dictionary<int, Texture2D>();

    // ---------------- 通过 Main.Assets 载入任意原版贴图 ----------------
    // UIPanel 用的 Images/UI/PanelBackground、PanelBorder 不在 TextureAssets 里，
    // 只能走 AssetRepository.Request<Texture2D>()，同样用反射（ReLogic 运行期不可直接引用）。

    private static bool _assetInit;
    private static object _assetRepo;
    private static MethodInfo _assetRequest;
    private static Type _assetRequestMode;
    private static PropertyInfo _assetValueProp;
    private static readonly Dictionary<string, Texture2D> _vanillaCache = new Dictionary<string, Texture2D>();

    private static void InitAssetRepo()
    {
        if (_assetInit) return;
        _assetInit = true;
        try
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.Static;
            PropertyInfo p = typeof(Main).GetProperty("Assets", F);
            if (p != null) _assetRepo = p.GetValue(null);
            if (_assetRepo == null)
            {
                FieldInfo f = typeof(Main).GetField("Assets", F);
                if (f != null) _assetRepo = f.GetValue(null);
            }
            if (_assetRepo == null) return;

            Type repoType = _assetRepo.GetType();
            Type iface = repoType.GetInterface("ReLogic.Content.IAssetRepository");
            MethodInfo m = FindRequest(repoType);
            if (m == null && iface != null) m = FindRequest(iface);
            if (m != null && m.IsGenericMethodDefinition && m.GetParameters().Length == 2)
            {
                _assetRequestMode = m.GetParameters()[1].ParameterType;
                _assetRequest = m;
            }
        }
        catch
        {
        }
    }

    private static MethodInfo FindRequest(Type t)
    {
        try
        {
            MethodInfo[] ms = t.GetMethods();
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name != "Request" || !ms[i].IsGenericMethodDefinition) continue;
                ParameterInfo[] ps = ms[i].GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string)) return ms[i];
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>按原版资源路径取贴图，例如 "Images/UI/PanelBackground"。</summary>
    public static Texture2D Vanilla(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_vanillaCache.TryGetValue(path, out Texture2D cached)) return cached;
        InitAssetRepo();
        if (_assetRequest == null) return null;
        try
        {
            object mode = null;
            if (_assetRequestMode != null)
            {
                try { mode = Enum.Parse(_assetRequestMode, "ImmediateLoad"); }
                catch { mode = Enum.ToObject(_assetRequestMode, 0); }
            }
            MethodInfo call = _assetRequest.MakeGenericMethod(typeof(Texture2D));
            object asset = _assetRequestMode != null
                ? call.Invoke(_assetRepo, new object[] { path, mode })
                : call.Invoke(_assetRepo, new object[] { path });
            if (asset == null) return null;
            if (_assetValueProp == null) _assetValueProp = asset.GetType().GetProperty("Value");
            Texture2D tex = _assetValueProp != null ? _assetValueProp.GetValue(asset) as Texture2D : null;
            if (tex != null) _vanillaCache[path] = tex;
            return tex;
        }
        catch
        {
            return null;
        }
    }

    private static void Init()
    {
        if (_init) return;
        _init = true;
        try
        {
            Type t = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
            if (t == null) return;
            const BindingFlags F = BindingFlags.Public | BindingFlags.Static;
            _fItem = t.GetField("Item", F);
            _fNpc = t.GetField("Npc", F);
            _fTile = t.GetField("Tile", F);
            for (int i = 1; i < _fBack.Length; i++) _fBack[i] = t.GetField("InventoryBack" + i, F);
            _valueProp = ProbeValue(_fItem);
        }
        catch
        {
        }
    }

    private static PropertyInfo ProbeValue(FieldInfo f)
    {
        try
        {
            if (f != null && f.GetValue(null) is Array a && a.Length > 1)
            {
                object e = a.GetValue(1);
                if (e != null) return e.GetType().GetProperty("Value");
            }
        }
        catch
        {
        }
        return null;
    }

    private static object AssetAt(FieldInfo f, int index)
    {
        try
        {
            if (f == null || index < 0) return null;
            if (!(f.GetValue(null) is Array a) || index >= a.Length) return null;
            return a.GetValue(index);
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D Fetch(FieldInfo f, int index, Action load)
    {
        Init();
        if (f == null || _valueProp == null) return null;
        object asset = AssetAt(f, index);
        if (asset != null)
        {
            try
            {
                if (_valueProp.GetValue(asset) is Texture2D tex) return tex;
            }
            catch
            {
            }
        }
        if (load != null)
        {
            try { load(); } catch { }
            asset = AssetAt(f, index);
            if (asset != null)
            {
                try
                {
                    if (_valueProp.GetValue(asset) is Texture2D tex2) return tex2;
                }
                catch
                {
                }
            }
        }
        return null;
    }

    public static Texture2D Item(int type)
    {
        if (type <= 0) return null;
        if (_itemCache.TryGetValue(type, out Texture2D cached)) return cached;
        Texture2D tex = Fetch(_fItem, type, () => { if (Main.instance != null) Main.instance.LoadItem(type); });
        if (tex != null) _itemCache[type] = tex;
        return tex;
    }

    public static Texture2D Npc(int type)
    {
        if (type <= 0 || type >= Main.npcFrameCount.Length) return null;
        if (_npcCache.TryGetValue(type, out Texture2D cached)) return cached;
        Texture2D tex = Fetch(_fNpc, type, () => { if (Main.instance != null) Main.instance.LoadNPC(type); });
        if (tex != null) _npcCache[type] = tex;
        return tex;
    }

    public static Texture2D Tile(int type)
    {
        if (type <= 0) return null;
        if (_tileCache.TryGetValue(type, out Texture2D cached)) return cached;
        Texture2D tex = Fetch(_fTile, type, () => { if (Main.instance != null) Main.instance.LoadTiles(type); });
        if (tex != null) _tileCache[type] = tex;
        return tex;
    }

    /// <summary>n: 1..31，对应 TextureAssets.InventoryBack{n}；n==0 时取 InventoryBack。</summary>
    public static Texture2D Back(int n)
    {
        Init();
        if (n <= 0) return Fetch(AssetField("InventoryBack"), 0, null);
        if (n >= _fBack.Length) n = 9;
        return BackFetch(n);
    }

    private static FieldInfo AssetField(string name)
    {
        try
        {
            Type t = typeof(Main).Assembly.GetType("Terraria.GameContent.TextureAssets");
            return t?.GetField(name, BindingFlags.Public | BindingFlags.Static);
        }
        catch
        {
            return null;
        }
    }

    private static readonly Dictionary<int, Texture2D> _backCache = new Dictionary<int, Texture2D>();

    private static Texture2D BackFetch(int n)
    {
        if (_backCache.TryGetValue(n, out Texture2D cached)) return cached;
        Init();
        FieldInfo f = _fBack[n] ?? AssetField("InventoryBack" + n);
        if (f == null) return null;
        Texture2D tex = null;
        object asset = f.GetValue(null);
        if (asset != null && _valueProp != null)
        {
            try { tex = _valueProp.GetValue(asset) as Texture2D; } catch { }
        }
        if (tex != null) _backCache[n] = tex;
        return tex;
    }

    // ---------------- 原模组自带的槽位贴图 ----------------
    // 原模组 Assets/Images 下的 .rawimg 就是“12 字节头(版本/宽/高) + 52x52 的原始 RGBA”，
    // 这里把文件嵌进 DLL，取出来直接 SetData 贴上去，和 tModLoader 里的画法完全一样。
    // 拿不到就返回 null，调用处退回到原版 InventoryBack 贴图。

    private static readonly Dictionary<string, Texture2D> _modTexCache = new Dictionary<string, Texture2D>();

    public static Texture2D ModTex(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_modTexCache.TryGetValue(name, out Texture2D cached)) return cached;
        try
        {
            using (Stream s = typeof(GameRefs).Assembly.GetManifestResourceStream("RecipeBrowser.assets." + name + ".rawimg"))
            {
                if (s == null)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                byte[] data = new byte[s.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = s.Read(data, read, data.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < 12)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                int w = BitConverter.ToInt32(data, 4);
                int h = BitConverter.ToInt32(data, 8);
                int len = w * h * 4;
                if (w <= 0 || h <= 0 || read < 12 + len)
                {
                    _modTexCache[name] = null;
                    return null;
                }
                GraphicsDevice dev = Main.graphics != null ? Main.graphics.GraphicsDevice : null;
                if (dev == null) return null;   // 设备还没准备好：这次不缓存，下次再试
                Texture2D tex = new Texture2D(dev, w, h, false, SurfaceFormat.Color);
                tex.SetData(data, 12, len);
                _modTexCache[name] = tex;
                return tex;
            }
        }
        catch
        {
            _modTexCache[name] = null;
            return null;
        }
    }

    // ---------------- 字号 / 行高 ----------------
    // 原版自己也是用 FontAssets.MouseText.MeasureString(某行文字).Y 当行距的，这里照做：
    // 字号随玩家的界面设置变化，写死像素的话不是压字就是留一大块空。

    private static int _lineH;

    public static int TextLine()
    {
        if (_lineH > 0) return _lineH;
        try
        {
            Type fa = typeof(Main).Assembly.GetType("Terraria.GameContent.FontAssets");
            object asset = fa?.GetField("MouseText", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object font = asset?.GetType().GetProperty("Value")?.GetValue(asset);
            if (font != null)
            {
                MethodInfo m = font.GetType().GetMethod("MeasureString",
                    BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(string) }, null);
                if (m != null && m.Invoke(font, new object[] { "Ay" }) is Vector2 v && v.Y > 0f)
                    _lineH = (int)Math.Ceiling(v.Y);
            }
        }
        catch
        {
        }
        return _lineH > 0 ? _lineH : 20;   // 字体还没准备好就先按 20 算，下次再量
    }

    // ---------------- 中文输入法 ----------------
    // 原版输入法服务在 ReLogic 里（ReLogic.OS.Platform + ReLogic.Localization.IME.IImeService，
    // 这两个类型被并进了 Terraria.exe），和上面的 Asset 一样运行期只能反射取。
    // 拿到服务以后就能读到“正在拼的拼音”，把它画在搜索框里（原版聊天栏也是这么做的）。

    private static bool _imeInit;
    private static object _imeService;
    private static Type _imeIface;
    private static MethodInfo _imeGet;
    private static PropertyInfo _imeComposition;

    private static void InitIme()
    {
        if (_imeInit) return;
        _imeInit = true;
        try
        {
            Type iface = null;
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length && iface == null; i++)
            {
                try { iface = asms[i].GetType("ReLogic.Localization.IME.IImeService", false); }
                catch
                {
                }
            }
            if (iface == null) return;
            Type plt = typeof(Main).Assembly.GetType("ReLogic.OS.Platform");
            if (plt == null)
            {
                for (int i = 0; i < asms.Length && plt == null; i++)
                {
                    try { plt = asms[i].GetType("ReLogic.OS.Platform", false); }
                    catch
                    {
                    }
                }
            }
            if (plt == null) return;
            MethodInfo get = null;
            MethodInfo[] ms = plt.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name == "Get" && ms[i].IsGenericMethodDefinition && ms[i].GetParameters().Length == 0)
                {
                    get = ms[i];
                    break;
                }
            }
            if (get == null) return;
            _imeIface = iface;
            _imeGet = get;
            _imeComposition = iface.GetProperty("CompositionString");
            _imeService = get.MakeGenericMethod(iface).Invoke(null, null);
        }
        catch
        {
            _imeService = null;
        }
    }

    /// <summary>输入法正在拼的字（没在拼就是空串）。</summary>
    public static string ImeComposition()
    {
        try
        {
            InitIme();
            // 服务本身是原版启动时建的单例，万一这次没取到就下一帧再试
            if (_imeService == null && _imeGet != null)
            {
                try { _imeService = _imeGet.MakeGenericMethod(_imeIface).Invoke(null, null); }
                catch
                {
                    _imeService = null;
                }
            }
            if (_imeService == null || _imeComposition == null) return "";
            return (_imeComposition.GetValue(_imeService) as string) ?? "";
        }
        catch
        {
            return "";
        }
    }
}
