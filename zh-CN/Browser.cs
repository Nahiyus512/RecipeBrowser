using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Bestiary;
using Terraria.ID;
using Terraria.Localization;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.UI;
using TerrariaModder.Core.UI.Widgets;

namespace RecipeBrowser;

/// <summary>
/// 原版 Recipe Browser 界面的复刻：
    ///   顶部 4 个标签页（配方/制作/物品/怪物图鉴）
///   配方页 = 查询栏 + 制作站勾选 + 单选筛选 + 图标网格 + 底部制作站/材料栏 + 右侧“掉落自”栏
///   勾选“制作站”后，窗口左侧会多出一块独立的制作站列表，配方网格的位置和宽度都不变
/// 位置、尺寸、配色都照搬 tModLoader 版 RecipeBrowser，只是改成了即时模式绘制。
/// </summary>
public static partial class Browser
{
    public const string PanelId = "recipe-browser.panel";

    // ---------------- 配色（照搬原模组） ----------------
    private static readonly Color RecipeColor = new Color(73, 94, 171);
    private static readonly Color CraftColor = new Color(90, 158, 57);
    private static readonly Color ItemColor = Color.DarkGreen;
    private static readonly Color BestiaryColor = new Color(28, 187, 180);
    private static readonly Color SlotPanelColor = new Color(100, 149, 237);
    private static readonly Color YesColor = Color.LightGreen;
    private static readonly Color NoColor = Color.LightSalmon;
    private static readonly Color MaybeColor = Color.Yellow;

    private static readonly string[] TabNames = { "配方", "制作", "物品", "怪物图鉴" };
    private static readonly Color[] TabColors = { RecipeColor, CraftColor, ItemColor, BestiaryColor };
    private static readonly int[] TabX = { 10, 85, 160, 235 };
    private const int TabW = 80;
    private const int TabH = 22;
    // 小控件（单选、勾选、按钮、搜索框）统一用这个字号，免得和核心 TextInput 一样比正文还大
    private const float SmallText = 0.8f;
    // 右下角缩放把的尺寸：右侧内容要给它留出空隙
    private const int GripSize = 18;
    // 右侧“掉落自”栏的宽度（原版：固定 50，一直显示）
    private const int LootColW = 50;
    // 窗口离屏幕边缘至少留这么多（拖动时不至于把窗口推出屏幕）
    private const int EdgeMargin = 4;
    // 最小窗口尺寸（照搬原版）
    private const int MinWinW = 415;
    private const int MinWinH = 263;
    // “制作”页顶部那一排占的高度
    private const int TopRowH = 44;
    // 各页面顶部控件区的高度（原版 recipeGridPanel.Top=120 / itemGridPanel.Top=60 / npcGridPanel.Top=46）
    // 配方页上方 0~60 是查询栏那排，60~120 是分类/排序/筛选栏（原版 sortsAndFiltersPanel 也在这一带），
    // 配方网格从 120 开始；物品页的分类栏和搜索框挤在同一排（0~60），网格仍从 60 开始。
    private const int RecipeTopH = 120;
    // 分类栏高度（原版 sortsAndFiltersPanel 高 60：上面一排分类，下面一排子分类+排序+筛选）
    private const int CatH = 60;
    private const int ItemTopH = 60;
    private const int BestiaryTopH = 46;
    // 底部区（配方页的“制作站+材料”、图鉴页的掉落物）照原版都是 50 高
    private const int BottomBarH = 50;
    // 面板底部留出这么多，免得压住右下角的缩放把（原版 Height -16）
    private const int GripGap = 16;
    // 单选 / 勾选每一行的行高（原版 UIRadioButtonGroup.Add 里就是 20 * index）
    private const int RowH = 20;

    private const int Slot = 39;
    private const int Pad = 2;
    private const int BarW = 20;
    private const int HistW = 12;   // UIElements/historyBack 原图 12x19
    private const int HistH = 19;
    private const int ChkW = 19;    // UIElements/checkBox 原图 19x21
    private const int ChkH = 21;
    private const int CloseW = 15;  // UIElements/closeButton 原图 15x14
    private const int CloseH = 14;
    private const string KeyIdName = PanelId + ".name";        // 配方页：搜索名称
    private const string KeyIdDesc = PanelId + ".desc";        // 配方页：搜索工具提示
    private const string KeyIdItemName = PanelId + ".iname";   // 物品页
    private const string KeyIdItemDesc = PanelId + ".idesc";   // 物品页：搜索工具提示
    private const string KeyIdNpcName = PanelId + ".nname";    // 怪物图鉴页

    // ---------------- 窗口状态 ----------------
    private static bool _open;
    private static int _tab;
    private static int _wx = -1, _wy = -1;
    // 原版默认 475x350（最小 415x263，最大 884x1000）；默认值取大一点，
    // 免得刚打开时内容挤在一起，还要自己拖右下角改大小。
    private static int _ww = 560, _wh = 420;
    private static bool _geom;
    private static bool _dragWin, _resizeWin;
    private static int _dragDX, _dragDY;
    private static int _resizeDX, _resizeDY;
    // 上一帧看到的屏幕尺寸：分辨率/UI 比例一变就把窗口夹回屏幕里
    private static int _lastScreenW, _lastScreenH;

    // 拖动判定：上一帧所有“可交互控件”的矩形。落在这些矩形上的按下不当作拖动窗口，
    // 其余位置（面板留白、控件间隙）都可以拖动 —— 与原版 UIDragableElement 的 DragTarget 行为一致。
    private static List<Rectangle> _hot = new List<Rectangle>();
    private static List<Rectangle> _hotPrev = new List<Rectangle>();

    // 搜索框焦点：鼠标按在别处就取消焦点，免得框里一直挂着那个“|”光标
    private static readonly List<Rectangle> _filterBoxes = new List<Rectangle>();
    private static bool _prevMouseLeft;

    // 这一帧被别的控件压住的区域（比如图鉴底部那条掉落栏压在网格上）。
    // 落在里面的位置不算“鼠标悬停在格子上”，否则格子会先把点击吃掉，底下的按钮就点不动了。
    private static readonly List<Rectangle> _clickGuards = new List<Rectangle>();

    // ---------------- 查询状态 ----------------
    private static int _queryType;
    private static int _lootType;
    private static bool _tileChooser;   // 左侧“制作站”列表是否展开
    private static int _tileSel = -1;
    private static int _radio;          // 0 全部配方 / 1 附近宝箱
    private static bool _queryFake = true;   // 查询栏里的物品是不是“浏览选中的”（原版 real 的反面）
    // 怪物图鉴的查询栏与筛选
    private static int _npcQueryType;
    private static bool _npcQueryFake = true;
    private static int _npcSort;             // 0 图鉴ID / 1 ID
    private static bool _npcEncountered;
    private static bool _npcUnencountered;
    private static bool _npcHasLoot;
    // 物品页的两个勾选：可制作 / 掉落物
    private static bool _itemCrafted;
    private static bool _itemLoot;
    // 搜索框按页各用各的，互不影响
    private static readonly SearchBox RecipeNameBox = new SearchBox("搜索名称", KeyIdName);
    private static readonly SearchBox RecipeDescBox = new SearchBox("搜索工具提示", KeyIdDesc);
    private static readonly SearchBox ItemNameBox = new SearchBox("搜索名称", KeyIdItemName);
    private static readonly SearchBox ItemDescBox = new SearchBox("搜索工具提示", KeyIdItemDesc);
    private static readonly SearchBox NpcNameBox = new SearchBox("搜索名称", KeyIdNpcName);
    private static SearchBox _focusBox;     // 当前正在输入的搜索框

    // 画窗口的时候记下“鼠标是不是在窗口上”（绘制阶段算的坐标才和点按钮用的一致）
    private static bool _mouseOverPanel;

    // ---------------- 选择状态 ----------------
    private static int _selRecipe = -1;
    private static int _selItem;
    private static int _selNpc;
    private static int _jumpItem, _jumpNpc;

    // ---------------- “制作”页（合成树） ----------------
    private static bool _craftNested = true;
    private static bool _craftLoot = true;
    private static bool _craftMine = true;
    private static List<CraftNode> _craftRows;
    private static int _craftRoot;          // “制作”页查询栏里的物品（原版是独立的一个槽）
    private static int _craftFor = -1;
    private static bool _craftDirty = true;
    private static int _scrollCraft;

    // ---------------- “附近宝箱”筛选 ----------------
    private static HashSet<int> _nearbyItems;

    // ---------------- 滚动 ----------------
    private static int _scrollGrid, _scrollLoot, _scrollTile, _scrollIngr, _scrollNpcLoot;
    private static string _scrollDragId;
    private static int _scrollDragDY;

    // ---------------- 网格 ----------------
    private static readonly List<int> _gridList = new List<int>();
    private static bool _gridDirty = true;
    private static int _lastRecipesSeen = -1;

    // ---------------- 点击 / 动画 ----------------
    private static int _clickIndex = -1;
    private static int _clickFrame = -1000;
    private static int _rclickIndex = -1;
    private static int _rclickFrame = -1000;
    private static int _frame;
    private static int _npcFrame, _npcFrameTimer;
    // 被点过的那只怪（原版点击后会闪一下白框），以及闪光的剩余帧数
    private static int _flashNpc = -1;
    private static int _flashFrames;

    // 同一帧只画一次。原版把鼠标提示画完之后会再调一次光标层，第二次调用会把整个面板
    // 重画在提示上面（提示就看不见了）。所以每帧开画前（FrameEvents.OnPreDraw）把这个标记清掉，
    // 一帧里只认第一次绘制 —— 这样暂停（游戏不更新）时也照样每帧刷新。
    private static bool _drewThisFrame;
    // 保险丝：万一某一帧没收到 OnPreDraw（世界切换之类），更新计数变了也把这个标记清掉，
    // 记为上次绘制时的更新计数。
    private static uint _drawnUpdate = uint.MaxValue;
    // 这一帧鼠标左/右键有没有“刚按下”。面板上的控件是在绘制阶段判定的，而原版在更新阶段
    // 就可能把这次点击消费掉（mouseLeftRelease 变 false），光看那个标记会漏掉点击。
    private static bool _leftDown, _rightDown, _clickEdge, _rclickEdge;
    // 搜索框的退格：原版那套判定依赖它自己记的按键状态，还会被输入法状态挡掉，
    // 结果按一下经常删不掉，这里自己认一次按键边沿（一帧最多删一个字）。
    private static bool _backDown, _backEdge;
    private static int _backHold;

    public static bool IsOpen => _open;

    /// <summary>鼠标是不是压在打开的面板上（绘制阶段算出来的，绑定键用它判断该不该让开）。</summary>
    public static bool MouseOverPanel => _open && _mouseOverPanel;

    /// <summary>窗口在屏幕上的边界。整个界面就是一个总框架，制作站列表画在框架内部左侧。</summary>
    private static int WinX => _wx;
    private static int WinW => _ww;

    // ================= 生命周期 =================

    public static void Init()
    {
        FrameEvents.OnPreDraw += OnFrameStart;
        UIRenderer.RegisterPanelDraw(PanelId, Draw);
    }

    public static void Unload()
    {
        Close();
        FrameEvents.OnPreDraw -= OnFrameStart;
        UIRenderer.UnregisterPanelDraw(PanelId);
    }

    /// <summary>原版一帧里会调两次光标层，这里在每帧开画前把“这帧画过了”清掉，只认第一次。</summary>
    private static void OnFrameStart()
    {
        _drewThisFrame = false;
        // 每帧记一下贴近过的制作站：原模组把“见过的站台”存进存档，我们没有存档能力，
        // 只能一直记着，否则第一次开面板时一个站台都不认识，嵌套合成会把需要站台的东西全排除掉。
        // 平时只是读一下 adjTile（原版开背包时会自己刷新），面板开着时才主动刷新一次。
        Catalogue.NoteNearbyTiles(_open);
    }

    public static void Open(int type = 0)
    {
        // 先当成“按键已经按着”：面板刚打开的那一帧不要把手按着的鼠标当成一次新点击
        _leftDown = true;
        _rightDown = true;
        _drewThisFrame = false;
        if (!_geom) LoadGeometry();
        if (type > 0)
        {
            _tab = 0;
            SetQuery(type);
            _selItem = type;
            _jumpItem = type;
        }
        _open = true;
    }

    public static void Close()
    {
        _open = false;
        _leftDown = true;
        _rightDown = true;
        _mouseOverPanel = false;
        UIRenderer.UnregisterPanelBounds(PanelId);
        UnfocusAllFilters();
        UIRenderer.DisableTextInput();
        UIRenderer.UnregisterKeyInputBlock(KeyIdName);
        UIRenderer.UnregisterKeyInputBlock(KeyIdDesc);
        UIRenderer.UnregisterKeyInputBlock(KeyIdItemName);
        UIRenderer.UnregisterKeyInputBlock(KeyIdItemDesc);
        UIRenderer.UnregisterKeyInputBlock(KeyIdNpcName);
        _scrollDragId = null;
        _dragWin = false;
        _resizeWin = false;
    }

    public static void Toggle(int type)
    {
        if (_open && _queryType == type)
        {
            Close();
            return;
        }
        Open(type);
    }

    // ScreenWidth/Height 给的已经是“UI 坐标”（面板绘制和鼠标判定用的就是这一套）
    private static int ScreenW => UIRenderer.ScreenWidth;

    private static int ScreenH => UIRenderer.ScreenHeight;

    /// <summary>
    /// 限制窗口大小。anchorToPos=true 时再按“从窗口当前位置到屏幕右下角还剩多少”限制，
    /// 这是拖右下角放大时用的：鼠标拖到哪窗口就长到哪，不会反过来把窗口顶回左边。
    /// 平时（拖动窗口、开机载入）用 anchorToPos=false：大小只受屏幕总尺寸限制，
    /// 这样拖动窗口时窗口尺寸绝不会变。
    /// </summary>
    private static void ClampSize(bool anchorToPos = false)
    {
        // 上限就是屏幕：原版那个 884x1000 的硬上限会让“拖右下角放大”半路卡住
        int maxW = ScreenW - EdgeMargin * 2;
        int maxH = ScreenH - EdgeMargin * 2;
        if (anchorToPos)
        {
            maxW = Math.Min(maxW, ScreenW - EdgeMargin - _wx);
            maxH = Math.Min(maxH, ScreenH - EdgeMargin - _wy);
        }
        _ww = Clamp(_ww, MinWinW, Math.Max(MinWinW, maxW));
        _wh = Clamp(_wh, MinWinH, Math.Max(MinWinH, maxH));
    }

    private static void LoadGeometry()
    {
        _geom = true;
        int sw = ScreenW;
        int sh = ScreenH;
        int x = Mod.Config != null ? Mod.Config.WindowX : -1;
        int y = Mod.Config != null ? Mod.Config.WindowY : -1;
        int w = Mod.Config != null ? Mod.Config.WindowW : 0;
        int h = Mod.Config != null ? Mod.Config.WindowH : 0;
        if (w >= MinWinW) _ww = w;
        if (h >= MinWinH) _wh = h;
        if (x < 0 || y < 0)
        {
            x = Math.Max(EdgeMargin, (sw - _ww) / 2);
            y = Math.Max(EdgeMargin, (sh - _wh) / 2);
        }
        _wx = x;
        _wy = y;
        ClampGeometry();
    }

    private static void ClampGeometry()
    {
        ClampSize();
        // 整窗留在屏幕内：能一直拖到贴着屏幕右下角
        int minX = EdgeMargin;
        int minY = EdgeMargin;
        _wx = Clamp(_wx, minX, Math.Max(minX, ScreenW - EdgeMargin - _ww));
        _wy = Clamp(_wy, minY, Math.Max(minY, ScreenH - EdgeMargin - _wh));
    }

    private static void SaveGeometry()
    {
        if (Mod.Config == null) return;
        Mod.Config.WindowX = _wx;
        Mod.Config.WindowY = _wy;
        Mod.Config.WindowW = _ww;
        Mod.Config.WindowH = _wh;
        try { Mod.Config.Save(); } catch { }
    }

    // ================= 数据 / 网格内容 =================

    /// <summary>“现在就能做”的配方集合由 Catalogue 统一维护（分类栏的“可合成的”筛选也用同一份）。</summary>
    private static bool IsCraftableNow(int recipeIndex)
    {
        return Catalogue.IsCraftableRecipe(recipeIndex);
    }

    private static void EnsureGrid()
    {
        Catalogue.Ensure();
        int recipes = 0;
        try { recipes = Recipe.numRecipes; } catch { }
        if (recipes != _lastRecipesSeen)
        {
            _lastRecipesSeen = recipes;
            _gridDirty = true;
            BrowserData.Invalidate();
            _selRecipe = -1;
        }
        // “可合成的”“嵌套合成”这两个筛选和格子上的绿底/黄底都跟着这份表走，背包一变就重排
        if (Catalogue.RefreshCraftable() &&
            (Catalogue.CraftableSelected || Catalogue.NestedSelected)) _gridDirty = true;
        if (!_gridDirty) return;
        _gridDirty = false;
        _gridList.Clear();
        if (_tab == 0 || _tab == 1) BuildRecipeGrid();
        else if (_tab == 2) BuildItemGrid();
        else if (_tab == 3) BuildNpcGrid();
        _scrollGrid = 0;
    }

    private static void BuildRecipeGrid()
    {
        int count = BrowserData.RecipeCount;
        string nameFilter = RecipeNameBox.Text ?? "";
        string descFilter = RecipeDescBox.Text ?? "";
        bool useName = nameFilter.Length > 0;
        bool useDesc = descFilter.Length > 0;
        Player player = null;
        try { player = Main.LocalPlayer; } catch { }
        for (int i = 0; i < count; i++)
        {
            int type = BrowserData.RecipeCreateType(i);
            if (type <= 0) continue;

            if (_radio == 1 && !PassNearbyChestFilter(i)) continue;

            // 制作站筛选：只看需要这个制作站（含下位制作站）的配方
            if (_tileSel >= 0 &&
                !BrowserData.TileSatisfies(_tileSel, BrowserData.RecipeStation(i), true)) continue;

            if (_queryType > 0 && !RecipeMatchesQuery(i, _queryType)) continue;

            // 分类栏：分类 + 排序/筛选（原版 PassRecipeFilters 的位置）
            Catalogue.Ensure();
            if (!Catalogue.Belongs(Catalogue.Selected, type)) continue;
            if (!Catalogue.PassRecipeFilters(i)) continue;

            if (useName && BrowserData.ItemName(type).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (useDesc && BrowserData.TooltipText(type).IndexOf(descFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            _gridList.Add(i);
        }
        Catalogue.SortRecipes(_gridList);
    }

    /// <summary>
    /// 原版“附近宝箱”筛选：除查询栏物品外，配方材料要在角色周围 960 像素（= 60 格）内的宝箱、
    /// 或者背包 / 猪猪存钱罐 / 保险箱 / 护卫熔炉 / 虚空保险库里有货。
    /// </summary>
    private static bool PassNearbyChestFilter(int recipeIndex)
    {
        HashSet<int> have = _nearbyItems;
        if (have == null) return true;
        List<Ingredient> ings = BrowserData.IngredientsOf(recipeIndex);
        for (int i = 0; i < ings.Count; i++)
        {
            Ingredient ing = ings[i];
            if (ing.GroupId >= 0)
            {
                bool any = false;
                try
                {
                    RecipeGroup g = RecipeGroup.recipeGroups[ing.GroupId];
                    if (g != null && g.ValidItems != null)
                    {
                        foreach (int v in g.ValidItems)
                        {
                            if (have.Contains(v))
                            {
                                any = true;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                }
                if (!any) return false;
            }
            else if (!have.Contains(ing.Type))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>“附近宝箱”是点击刷新式的：点一下重新扫描一次。</summary>
    private static void RefreshNearbyItems()
    {
        HashSet<int> set = new HashSet<int>();
        try
        {
            Player p = Main.LocalPlayer;
            if (p != null)
            {
                AddItems(set, p.inventory);
                AddItems(set, p.bank != null ? p.bank.item : null);
                AddItems(set, p.bank2 != null ? p.bank2.item : null);
                AddItems(set, p.bank3 != null ? p.bank3.item : null);
                AddItems(set, p.bank4 != null ? p.bank4.item : null);
                AddItems(set, p.armor);
                AddItems(set, p.dye);
                AddItems(set, p.miscDyes);
                AddItems(set, p.miscEquips);
                Vector2 center = p.Center;
                for (int i = 0; i < 1000; i++)
                {
                    Chest c = Main.chest[i];
                    if (c == null) continue;
                    Vector2 pos = new Vector2(c.x * 16 + 16, c.y * 16 + 16);
                    if (Vector2.Distance(pos, center) >= 960f) continue;
                    try
                    {
                        if (Chest.IsLocked(c.x, c.y)) continue;
                    }
                    catch
                    {
                    }
                    AddItems(set, c.item);
                }
            }
        }
        catch
        {
        }
        if (_queryType > 0) set.Add(_queryType);
        _nearbyItems = set;
    }

    private static void AddItems(HashSet<int> set, Item[] items)
    {
        if (items == null) return;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] == null) continue;
            if (items[i].type > 0 && items[i].stack > 0) set.Add(items[i].type);
        }
    }

    private static bool RecipeMatchesQuery(int recipeIndex, int queryType)
    {
        if (BrowserData.RecipeCreateType(recipeIndex) == queryType) return true;
        List<Ingredient> ings = BrowserData.IngredientsOf(recipeIndex);
        for (int i = 0; i < ings.Count; i++)
        {
            if (ings[i].Type == queryType) return true;
            if (ings[i].GroupId >= 0)
            {
                try
                {
                    RecipeGroup g = RecipeGroup.recipeGroups[ings[i].GroupId];
                    if (g != null && g.ValidItems.Contains(queryType)) return true;
                }
                catch
                {
                }
            }
        }
        return false;
    }

    private static void BuildItemGrid()
    {
        int[] all = BrowserData.AllItems;
        string nameFilter = ItemNameBox.Text ?? "";
        string descFilter = ItemDescBox.Text ?? "";
        bool useName = nameFilter.Length > 0;
        bool useDesc = descFilter.Length > 0;
        for (int i = 0; i < all.Length; i++)
        {
            int type = all[i];
            // 原版 PassItemFilters：可制作 = 有配方能产出；掉落物 = 有生物会掉
            if (_itemCrafted && BrowserData.RecipesForItem(type).Count == 0) continue;
            if (_itemLoot && !BrowserData.IsLootItem(type)) continue;
            // 分类栏：分类 + 排序/筛选（原版 PassItemFilters 的位置）
            Catalogue.Ensure();
            if (!Catalogue.Belongs(Catalogue.Selected, type)) continue;
            if (!Catalogue.PassFilters(type)) continue;
            if (useName && BrowserData.ItemName(type).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (useDesc && BrowserData.TooltipText(type).IndexOf(descFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _gridList.Add(type);
        }
        Catalogue.SortItems(_gridList);
    }

    private static void BuildNpcGrid()
    {
        int[] all = BrowserData.AllNpcs;
        string nameFilter = NpcNameBox.Text ?? "";
        for (int i = 0; i < all.Length; i++)
        {
            int npc = all[i];
            // 原版 PassNPCFilters
            if (_npcEncountered && (!_npcUnencountered) == (!BestiaryUnlocked(npc))) continue;
            if (_npcHasLoot && BrowserData.NpcDrops(npc).Count == 0) continue;
            // 查询栏里放了物品时，只列出会掉落它的生物
            if (_npcQueryType > 0 && !NpcDropsItem(npc, _npcQueryType)) continue;
            if (nameFilter.Length > 0 &&
                BrowserData.NpcName(npc).IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _gridList.Add(npc);
        }
        // 原版：图鉴ID 排序（没有图鉴序号的排在后面），否则按 netID 排
        try { _gridList.Sort(CompareNpcs); } catch { }
    }

    private static bool NpcDropsItem(int npcType, int itemType)
    {
        try
        {
            List<NpcDrop> drops = BrowserData.NpcDrops(npcType);
            for (int i = 0; i < drops.Count; i++)
            {
                if (drops[i].ItemType == itemType) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    /// <summary>原版 RecipePath.NPCUnlocked：图鉴里解锁了（看到过）就算“曾遇见过”。</summary>
    private static bool BestiaryUnlocked(int npcType)
    {
        try
        {
            BestiaryEntry entry = Main.BestiaryDB.FindEntryByNPCID(npcType);
            if (entry == null || entry.UIInfoProvider == null) return false;
            return (int)entry.UIInfoProvider.GetEntryUICollectionInfo().UnlockState > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>原版 BestiaryUI.CustomSort。</summary>
    private static int CompareNpcs(int a, int b)
    {
        if (_npcSort == 0)
        {
            try
            {
                bool oka = ContentSamples.NpcBestiarySortingId.TryGetValue(a, out int va);
                bool okb = ContentSamples.NpcBestiarySortingId.TryGetValue(b, out int vb);
                if (oka && okb) return va.CompareTo(vb);
                if (oka) return -1;
                if (okb) return 1;
            }
            catch
            {
            }
        }
        return NetIdOrder(a).CompareTo(NetIdOrder(b));
    }

    /// <summary>原版按 netID 排序时的换算（变体取正、普通怪排在变体之后）。</summary>
    private static int NetIdOrder(int netID)
    {
        if (netID < 0) return -netID;
        return netID >= 688 ? netID : netID - 1000;
    }

    private static int ItemRare(int type)
    {
        try { return ContentSamples.ItemsByType[type].rare; } catch { return 0; }
    }

    /// <summary>
    /// 鼠标左/右键这一帧是否“刚按下”。除了原版那个 mouseLeftRelease 标记，再多认一次
    /// 按键边沿：面板边上有些位置会被原版当成“不在面板上”，点击会被原版先消费掉，
    /// 只看那个标记的话按钮就会时不时点不动。
    /// </summary>
    private static bool PressedLeft => UIRenderer.MouseLeftClick || _clickEdge;

    private static bool PressedRight => UIRenderer.MouseRightClick || _rclickEdge;

    /// <summary>
    /// 消费掉这次左键。除了解除原版那个 mouseLeftRelease 标记，还要把本帧记下的“刚按下”一起清掉，
    /// 否则同一帧里后面与之重叠的控件会跟着一起触发（两个挨着的勾选框一次点中两个）。
    /// </summary>
    private static void ConsumeLeft()
    {
        _clickEdge = false;
        UIRenderer.ConsumeClick();
    }

    private static void ConsumeRight()
    {
        _rclickEdge = false;
        UIRenderer.ConsumeRightClick();
    }

    // ================= 绘制入口 =================

    private static void Draw()
    {
        if (!_open) return;
        // 退出世界 / 回到主菜单时把面板收掉，不然它会一直挂在那里
        if (Main.gameMenu)
        {
            Close();
            return;
        }
        // 点开原版设置（游戏里右下角那个）或其它原版界面时，也把面板收掉
        if (Main.ingameOptionsWindow || (Main.InGameUI != null && Main.InGameUI.IsVisible))
        {
            Close();
            return;
        }
        // 原版把“待显示的鼠标提示”画完之后，会再调一次光标层（让光标压在提示上面），
        // 于是这个回调在同一帧里会被调用两次。第二次会把整个面板重画一遍，正好盖住刚画好的
        // 悬浮提示 —— 玩家就完全看不到提示。认出第二次直接跳过，这一帧只画一次。
        uint update = Main.GameUpdateCount;
        if (update != _drawnUpdate)
        {
            _drawnUpdate = update;
            _drewThisFrame = false;
        }
        if (_drewThisFrame) return;
        _drewThisFrame = true;
        // 记下这一帧左键/右键有没有“刚按下”（不受原版更新阶段消费点击的影响）
        bool mouseLeft = UIRenderer.MouseLeft;
        bool mouseRight = UIRenderer.MouseRight;
        _clickEdge = (mouseLeft && !_leftDown) || UIRenderer.MouseLeftClick;
        _rclickEdge = (mouseRight && !_rightDown) || UIRenderer.MouseRightClick;
        _leftDown = mouseLeft;
        _rightDown = mouseRight;
        // 搜索框用的退格边沿：自己认，不看原版那套“上一帧按键状态”
        bool backDown = false;
        try
        {
            backDown = Microsoft.Xna.Framework.Input.Keyboard.GetState()
                .IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Back);
        }
        catch
        {
        }
        _backEdge = backDown && !_backDown;
        _backHold = backDown ? _backHold + 1 : 0;
        _backDown = backDown;
        if (_flashFrames > 0) _flashFrames--;
        else _flashNpc = -1;
        // 分辨率 / UI 比例变了（切全屏、改界面比例）：重新夹一次，
        // 免得窗口留在屏幕外面，鼠标拖不回来。
        if (ScreenW != _lastScreenW || ScreenH != _lastScreenH)
        {
            _lastScreenW = ScreenW;
            _lastScreenH = ScreenH;
            ClampGeometry();
        }
        _frame++;
        if (++_npcFrameTimer > 7)
        {
            _npcFrameTimer = 0;
            _npcFrame++;
        }

        int winX = WinX;
        int winW = WinW;
        // 记下“鼠标在窗口上”（用绘制阶段的鼠标坐标，和点按钮用的是同一套）
        _mouseOverPanel = new Rectangle(winX, _wy, winW, _wh)
            .Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        UIRenderer.RegisterPanelBounds(PanelId, winX, _wy, winW, _wh);
        // 交换热区表：HandleWindow 用上一帧的结果判定拖动，本帧重新收集
        List<Rectangle> swap = _hotPrev;
        _hotPrev = _hot;
        _hot = swap;
        _hot.Clear();
        _filterBoxes.Clear();
        _clickGuards.Clear();
        HandleWindow();

        Color accent = TabColors[Math.Max(0, Math.Min(_tab, TabColors.Length - 1))];
        Rectangle body = new Rectangle(winX, _wy + 20, winW, _wh - 20);
        Panel(body, accent);

        int cx = _wx + 6;
        int cy = _wy + 20 + 6;
        int cw = _ww - 12;
        int ch = _wh - 20 - 12;

        EnsureGrid();

        if (_tab == 0) DrawRecipeTab(cx, cy, cw, ch);
        else if (_tab == 1) DrawCraftTab(cx, cy, cw, ch);
        else if (_tab == 2) DrawItemTab(cx, cy, cw, ch);
        else DrawBestiaryTab(cx, cy, cw, ch);

        // 鼠标按在搜索框以外的地方就取消焦点（那个“|”光标不会一直挂着）
        HandleFilterFocus();
        DrawTabBar();
        DrawResizeGrip();
        // 提示不在这里画：物品/怪物提示在绘制时塞给原版，由原版在这帧末尾自己渲染
    }

    private static void DrawTabBar()
    {
        int mx = UIRenderer.MouseX;
        int my = UIRenderer.MouseY;
        for (int i = 0; i < TabNames.Length; i++)
        {
            Rectangle r = new Rectangle(_wx + TabX[i], _wy, TabW, TabH);
            bool active = i == _tab;
            bool hover = r.Contains(mx, my);
            Hot(r);
            Color c = TabColors[i];
            PanelBottomless(r, Mul(c, active ? 1.0f : hover ? 0.95f : 0.82f));
            const float scale = 0.85f;
            string label = TabNames[i];
            int tw = TextW(label, scale);
            Text(label, r.X + (r.Width - tw) / 2, r.Y + (r.Height - 16) / 2, Color.White, scale);
            if (hover && PressedLeft)
            {
                ConsumeLeft();
                SetTab(i);
            }
        }

        // 原版：closeButton 在标签行最右侧，Left -26、垂直居中，贴图 15x14
        Rectangle close = new Rectangle(_wx + _ww - 26, _wy + (TabH - CloseH) / 2, CloseW, CloseH);
        bool ch = close.Contains(mx, my);
        Hot(close);
        Fill(close, ch ? new Color(210, 80, 80) : new Color(150, 65, 65));
        Outline(close, new Color(60, 20, 20));
        // 叉号按量出来的宽度居中（竖直方向和标签文字用同一套“16 高的文字盒”算法）
        const float cscale = 0.85f;
        Text("x", close.X + (close.Width - TextW("x", cscale)) / 2,
            close.Y + (close.Height - 16) / 2, Color.White, cscale);
        if (ch && PressedLeft)
        {
            ConsumeLeft();
            Close();
        }
    }

    private static void DrawResizeGrip()
    {
        Rectangle grip = new Rectangle(_wx + _ww - GripSize, _wy + _wh - GripSize, GripSize, GripSize);
        bool hover = grip.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        // 原版 UIDragableElement.DrawDragAnchor：用 PanelBorder 右下角画三层黑色拐角
        Texture2D bd = GameRefs.Vanilla("Images/UI/PanelBorder");
        if (bd != null && Main.spriteBatch != null)
        {
            try
            {
                int gx = _wx + _ww - 12, gy = _wy + _wh - 12;
                Color ink = hover ? Color.White : Color.Black;
                Main.spriteBatch.Draw(bd, new Rectangle(gx - 2, gy - 2, 10, 10), new Rectangle(16, 16, 12, 12), ink);
                Main.spriteBatch.Draw(bd, new Rectangle(gx - 4, gy - 4, 8, 8), new Rectangle(16, 16, 12, 12), ink);
                Main.spriteBatch.Draw(bd, new Rectangle(gx - 6, gy - 6, 6, 6), new Rectangle(16, 16, 12, 12), ink);
                return;
            }
            catch
            {
            }
        }
        Fill(grip, hover ? new Color(200, 210, 235, 200) : new Color(150, 160, 190, 140));
        Outline(grip, new Color(40, 44, 66));
        for (int i = 0; i < 3; i++)
        {
            Fill(new Rectangle(grip.X + 4 + i * 4, grip.Bottom - 4, 3, 1), new Color(40, 44, 66));
        }
    }

    private static void SetTab(int tab)
    {
        if (_tab == tab) return;
        _tab = tab;
        _gridDirty = true;
        _scrollGrid = 0;
        _scrollLoot = 0;
        _scrollTile = 0;
        // 制作站面板只属于配方页：切走就收起来，窗口宽度跟着恢复
        if (tab != 0 && _tileChooser)
        {
            _tileChooser = false;
            _tileSel = -1;
        }
        UnfocusAllFilters();
        UIRenderer.DisableTextInput();
        UIRenderer.UnregisterKeyInputBlock(KeyIdName);
        UIRenderer.UnregisterKeyInputBlock(KeyIdDesc);
        UIRenderer.UnregisterKeyInputBlock(KeyIdItemName);
        UIRenderer.UnregisterKeyInputBlock(KeyIdItemDesc);
        UIRenderer.UnregisterKeyInputBlock(KeyIdNpcName);
    }

    private static void HandleWindow()
    {
        int mx = UIRenderer.MouseX;
        int my = UIRenderer.MouseY;
        bool left = UIRenderer.MouseLeft;
        bool down = PressedLeft;

        // 原版：右下角 18x18 是缩放把，其余位置只要不落在控件上就整窗拖动
        Rectangle win = new Rectangle(WinX, _wy, WinW, _wh);
        Rectangle grip = new Rectangle(_wx + _ww - GripSize, _wy + _wh - GripSize, GripSize, GripSize);

        if (down && !_dragWin && !_resizeWin && win.Contains(mx, my))
        {
            if (grip.Contains(mx, my))
            {
                _resizeWin = true;
                _resizeDX = mx - (_wx + _ww);
                _resizeDY = my - (_wy + _wh);
                ConsumeLeft();
            }
            else if (!OverHot(mx, my))
            {
                _dragWin = true;
                _dragDX = mx - _wx;
                _dragDY = my - _wy;
                ConsumeLeft();
            }
        }

        if (!left)
        {
            if (_dragWin || _resizeWin)
            {
                _dragWin = false;
                _resizeWin = false;
                SaveGeometry();
            }
        }
        else if (_dragWin)
        {
            _wx = mx - _dragDX;
            _wy = my - _dragDY;
            ClampGeometry();
        }
        else if (_resizeWin)
        {
            _ww = Math.Max(MinWinW, mx - _resizeDX - _wx);
            _wh = Math.Max(MinWinH, my - _resizeDY - _wy);
            // 只限制大小：位置不动，右下角的把才跟手
            ClampSize(true);
        }
    }

    /// <summary>登记一个“可交互控件”矩形（画到哪就登记到哪）。</summary>
    private static void Hot(Rectangle r)
    {
        if (r.Width > 0 && r.Height > 0) _hot.Add(r);
    }

    private static bool OverHot(int x, int y)
    {
        for (int i = 0; i < _hotPrev.Count; i++)
        {
            if (_hotPrev[i].Contains(x, y)) return true;
        }
        return false;
    }

    /// <summary>登记一块“压在网格上面”的区域（底部栏、勾选按钮之类），落在里面就不算点到格子。</summary>
    private static void GuardClicks(Rectangle r)
    {
        if (r.Width > 0 && r.Height > 0) _clickGuards.Add(r);
    }

    private static bool ClickGuarded(int x, int y)
    {
        for (int i = 0; i < _clickGuards.Count; i++)
        {
            if (_clickGuards[i].Contains(x, y)) return true;
        }
        return false;
    }

    // ---------------- 搜索框 ----------------

    /// <summary>
    /// 自己画的搜索框。核心的 TextInput 用 1.0 的字号（比面板里所有文字都大），
    /// 右边还挂一个英文 Clear 按钮，所以这里自己画：同一套字号、同一个手感。
    /// 输入本身还是走 UIRenderer 的文本输入，中文输入法照常可用。
    /// </summary>
    private sealed class SearchBox
    {
        public readonly string Hint;
        public readonly string KeyId;
        public string Text = "";
        public bool Changed;

        public SearchBox(string hint, string keyId)
        {
            Hint = hint;
            KeyId = keyId;
        }
    }

    private static void NoteFilterBox(Rectangle r)
    {
        _filterBoxes.Add(r);
    }

    private static void UnfocusAllFilters()
    {
        UnfocusSearchBox();
    }

    private static void FocusSearchBox(SearchBox box)
    {
        if (_focusBox == box) return;
        UnfocusSearchBox();
        _focusBox = box;
        UIRenderer.ClearInput();
        UIRenderer.RegisterKeyInputBlock(box.KeyId);
    }

    private static void UnfocusSearchBox()
    {
        SearchBox box = _focusBox;
        if (box == null) return;
        _focusBox = null;
        UIRenderer.UnregisterKeyInputBlock(box.KeyId);
        UIRenderer.DisableTextInput();
    }

    /// <summary>
    /// 鼠标在搜索框以外按下时取消焦点，否则框里会一直挂着那个“|”光标，
    /// 看起来像是输入框里多了个奇怪的字。
    /// </summary>
    private static void HandleFilterFocus()
    {
        bool left = UIRenderer.MouseLeft;
        bool pressed = left && !_prevMouseLeft;
        _prevMouseLeft = left;
        if (!pressed || _focusBox == null) return;
        int mx = UIRenderer.MouseX;
        int my = UIRenderer.MouseY;
        for (int i = 0; i < _filterBoxes.Count; i++)
        {
            if (_filterBoxes[i].Contains(mx, my)) return;
        }
        UnfocusSearchBox();
    }

    /// <summary>输入法正在拼的字（没在拼、或者取不到输入法服务时都是空串）。</summary>
    private static string ImeText()
    {
        return GameRefs.ImeComposition() ?? "";
    }

    /// <summary>去掉最后一个字符（成对的代理字符一起删，免得留下半个字）。</summary>
    private static string TrimLastChar(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int n = s.Length - 1;
        if (n > 0 && char.IsLowSurrogate(s[n]) && char.IsHighSurrogate(s[n - 1])) n--;
        return s.Substring(0, n);
    }

    /// <summary>原版给“正在拼的字”用的颜色（拿不到就用一个亮黄色兜底）。</summary>
    private static Color ImeColor()
    {
        try
        {
            return Main.imeCompositionStringColor;
        }
        catch
        {
            return new Color(255, 220, 120);
        }
    }

    /// <summary>画一个搜索框，返回框里的文本。</summary>
    private static string DrawSearchBox(Rectangle r, SearchBox box)
    {
        NoteFilterBox(r);
        Hot(r);
        bool hover = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        bool focused = _focusBox == box;

        Rectangle clear = new Rectangle(r.Right - 18, r.Y + (r.Height - 14) / 2, 14, 14);
        bool hasText = !string.IsNullOrEmpty(box.Text);
        bool overClear = hasText && clear.Contains(UIRenderer.MouseX, UIRenderer.MouseY);

        if (hover && PressedLeft && !overClear)
        {
            ConsumeLeft();
            FocusSearchBox(box);
            focused = true;
        }
        if (overClear && PressedLeft)
        {
            ConsumeLeft();
            box.Text = "";
            box.Changed = true;
            UIRenderer.ClearInput();
            focused = _focusBox == box;
        }

        if (focused)
        {
            UIRenderer.EnableTextInput();
            UIRenderer.HandleIME();
            // 输入法的组字串有时是空白的，只有真有字才算“正在拼字”
            bool imeComposing = ImeText().Trim().Length > 0;
            bool backEdge = _backEdge;
            _backEdge = false;              // 一帧最多删一个字
            string before = box.Text ?? "";
            string typed = UIRenderer.GetInputText(before);
            // 原版这一帧没删掉（被输入法状态挡掉、或者漏了这次按下），就自己删一个；
            // 按住不放时也照常连删，不用等原版那个两秒多的重复延迟。
            if (!imeComposing && typed == before && before.Length > 0 &&
                (backEdge || (_backHold >= 24 && _backHold % 3 == 0)))
            {
                typed = TrimLastChar(before);
            }
            if (typed != box.Text)
            {
                box.Text = typed.Length > 60 ? typed.Substring(0, 60) : typed;
                box.Changed = true;
            }
            // 正在拼字（输入法还没上屏）时，Esc 是“取消这次拼字”，不该顺手把框关掉
            if (UIRenderer.CheckInputEscape() && !imeComposing) UnfocusSearchBox();
        }

        Fill(r, focused ? new Color(32, 38, 60) : new Color(22, 26, 42));
        Outline(r, focused ? new Color(126, 156, 224) : new Color(58, 64, 90));

        // 输入法正在拼的拼音/候选字：跟在框里已有文字后面显示（原版聊天栏也是画在光标位置的）
        string composing = focused ? ImeText() : "";
        int textMax = r.Width - 12 - (hasText ? 18 : 0);
        string shown;
        Color color;
        if (hasText)
        {
            shown = box.Text;
            color = new Color(238, 240, 250);
        }
        else if (focused)
        {
            shown = "";
            color = new Color(238, 240, 250);
        }
        else
        {
            shown = box.Hint ?? "";
            color = new Color(148, 154, 176);
        }
        int keep = focused ? TextW("|", SmallText) + TextW(composing, SmallText) : 0;
        while (shown.Length > 1 && TextW(shown, SmallText) + keep > textMax) shown = shown.Substring(1);
        int textY = r.Y + (r.Height - 14) / 2;
        Text(shown, r.X + 6, textY, color, SmallText);
        int caretX = r.X + 6 + TextW(shown, SmallText);
        if (composing.Length > 0)
        {
            Text(composing, caretX, textY, ImeColor(), SmallText);
            caretX += TextW(composing, SmallText);
        }
        if (focused)
        {
            Text("|", caretX, textY, new Color(238, 240, 250), SmallText);
            // 让原版的候选词面板跟着光标走（不设的话它会停在默认位置）
            try
            {
                if (Main.instance != null) Main.instance.SetIMEPanelAnchor(new Vector2(caretX, r.Bottom + 34), 0f);
            }
            catch
            {
            }
        }

        if (hasText)
        {
            Text("x", clear.X + 4, clear.Y, overClear ? Color.White : new Color(170, 176, 200), SmallText);
        }
        if (box.Changed)
        {
            box.Changed = false;
            _gridDirty = true;
        }
        return box.Text ?? "";
    }

    private static void SetQuery(int type, bool pushHistory = true)
    {
        if (pushHistory && type > 0) HistoryPush(type);
        _queryType = type;
        _lootType = type;
        _selRecipe = -1;
        _gridDirty = true;
        _craftDirty = true;
        if (type > 0) _craftRoot = type;
        _jumpItem = type;
        _queryFake = true;
        // 原版 ReplaceWithFake：换成“浏览选中”的物品时，顺便关掉制作站筛选
        _tileChooser = false;
        _tileSel = -1;
    }

    // ---------------- 查询历史 ----------------

    private static readonly List<int> _history = new List<int>();
    private static int _historyPos = -1;

    private static void HistoryPush(int type)
    {
        if (_historyPos >= 0 && _historyPos < _history.Count && _history[_historyPos] == type) return;
        while (_history.Count > _historyPos + 1) _history.RemoveAt(_history.Count - 1);
        _history.Add(type);
        _historyPos = _history.Count - 1;
        if (_history.Count > 50)
        {
            _history.RemoveAt(0);
            _historyPos--;
        }
    }

    private static void HistoryBack()
    {
        if (_historyPos <= 0)
        {
            VanillaTextTip("尚无查询历史");
            return;
        }
        _historyPos--;
        _queryType = _history[_historyPos];
        _selRecipe = -1;
        _gridDirty = true;
    }

    private static void HistoryForward()
    {
        if (_historyPos < 0 || _historyPos >= _history.Count - 1)
        {
            VanillaTextTip("已到查询历史末尾");
            return;
        }
        _historyPos++;
        _queryType = _history[_historyPos];
        _selRecipe = -1;
        _gridDirty = true;
    }

    // ---------------- 配方页 ----------------

    private static void DrawRecipeTab(int cx, int cy, int cw, int ch)
    {
        int mx = UIRenderer.MouseX;
        int my = UIRenderer.MouseY;

        // 原版：historyBackButton Left 0 Top 0，historyForwardButton Top 20，贴图 12x19
        Rectangle histBack = new Rectangle(cx, cy, HistW, HistH);
        Rectangle histFwd = new Rectangle(cx, cy + RowH, HistW, HistH);
        DrawHistoryButton(histBack, "<");
        DrawHistoryButton(histFwd, ">");
        if (histBack.Contains(mx, my) && PressedLeft)
        {
            ConsumeLeft();
            HistoryBack();
        }
        if (histFwd.Contains(mx, my) && PressedLeft)
        {
            ConsumeLeft();
            HistoryForward();
        }

        // 查询栏：原版 Left 16
        bool fromMouse;
        int q = DrawQuerySlot(new Rectangle(cx + 16, cy, Slot, Slot), _queryType, _queryFake, "在此放置物品", out fromMouse);
        if (q >= 0)
        {
            SetQuery(q);
            _queryFake = !fromMouse;
        }

        // 单选竖排：原版 RadioButtonGroup Left 61、宽 180，里面每项高 20 从上往下排。
        // 第三项原本是“仅 Item Checklist”，这里换成“制作站”，点开左边那排制作站列表。
        int gx = cx + 61;
        if (DrawRadioRow(gx, cy, _radio == 0, "全部配方", null))
        {
            _radio = 0;
            _gridDirty = true;
        }
        if (DrawRadioRow(gx, cy + RowH, _radio == 1, "附近宝箱", "点击刷新"))
        {
            RefreshNearbyItems();
            _radio = 1;
            _gridDirty = true;
        }
        if (DrawRadioRow(gx, cy + RowH * 2, _tileChooser, "制作站", null))
        {
            _tileChooser = !_tileChooser;
            if (!_tileChooser) _tileSel = -1;
            _gridDirty = true;
        }

        // 右上角竖排两个搜索框：原版 Left -202、Top 0 / 30、150x25（正好落在右侧掉落栏上方）
        DrawSearchBox(new Rectangle(cx + cw - 202, cy, 150, 25), RecipeNameBox);
        DrawSearchBox(new Rectangle(cx + cw - 202, cy + 30, 150, 25), RecipeDescBox);

        // 分类/排序/筛选栏：原版 sortsAndFiltersPanel Top 60、Width -52（和网格同宽，避开右侧掉落栏）
        DrawCatalogueBar(new Rectangle(cx, cy + CatH, cw - LootColW, CatH), false);

        // 配方网格：原版 Top 120、Width -52；展开制作站时 Left 52、Width -104
        int gridX = cx + (_tileChooser ? 52 : 0);
        int gridY = cy + RecipeTopH;
        int gridW = cw - (_tileChooser ? 104 : 52);
        // 底部“制作站 + 材料”栏的高度按原版字号量出来：两行文字 + 上下留白，
        // 字号比原版大时栏也跟着长高（原来写死 50，第二行会从栏底溢出去）。
        int infoH = Math.Max(BottomBarH, 8 + GameRefs.TextLine() * 2);
        int gridH = ch - RecipeTopH - infoH - 2;
        DrawRecipeGrid(new Rectangle(gridX, gridY, gridW, gridH));

        // 左侧制作站列表：原版 tileChooserPanel Top 120、Left 0、宽 50、Height -170
        if (_tileChooser) DrawTileChooser(new Rectangle(cx, gridY, LootColW, gridH));

        // 右侧“掉落自”栏：原版固定 50 宽、Top 0、Height -16，一直显示
        DrawLootColumn(new Rectangle(cx + cw - LootColW, cy, LootColW, ch - GripGap));

        // 底部“制作站 + 材料”：原版 recipeInfo Top -50、Width -50、Height 50
        DrawRecipeInfo(new Rectangle(cx, cy + ch - infoH, cw - LootColW, infoH));
    }

    private static void DrawHistoryButton(Rectangle r, string glyph)
    {
        bool hover = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        Hot(r);
        Fill(r, hover ? new Color(130, 140, 175) : new Color(80, 88, 115));
        Outline(r, new Color(40, 45, 65));
        Text(glyph, r.X + (r.Width - TextW(glyph, SmallText)) / 2, r.Y + 2, Color.White, SmallText);
    }

    /// <summary>
    /// 原版 UIRadioButton：一个 UIText，前面挂 Images/UI/Settings_Toggle 的开关贴图。
    /// 返回 true 表示这一帧被按下左键。
    /// </summary>
    private static bool DrawRadioRow(int x, int y, bool selected, string label, string hover)
    {
        // 命中区左右放宽一点（防止贴着文字边缘点空），上下只能到开关本身那么高：
        // 单选是每行 20 像素挨着排的，上下再放宽就会和相邻那一行叠在一起，
        // 点在叠住的那几像素上会被上面那一行先吃掉，看起来就是“点了没反应”。
        Rectangle r = new Rectangle(x - 3, y - 1,
            Math.Max(ChkW, ChkW + 6 + TextW(label, SmallText)) + 6, ChkH + 2);
        bool over = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        Hot(r);
        Texture2D toggle = GameRefs.Vanilla("Images/UI/Settings_Toggle");
        bool drew = false;
        if (toggle != null && Main.spriteBatch != null)
        {
            try
            {
                int fw = (toggle.Width - 2) / 2;   // 原版：两帧并排，中间隔 2px
                if (fw > 0)
                {
                    Rectangle src = new Rectangle(selected ? fw + 2 : 0, 0, fw, toggle.Height);
                    Main.spriteBatch.Draw(toggle, new Vector2(x, y), src, Color.White);
                    drew = true;
                }
            }
            catch
            {
            }
        }
        if (!drew)
        {
            Rectangle box = new Rectangle(x, y, ChkW, ChkH);
            Fill(box, selected ? new Color(215, 225, 245) : new Color(60, 65, 85));
            Outline(box, new Color(30, 32, 45));
            if (selected) DrawCheckMark(box);
        }
        Text(label, x + ChkW + 2, y + 3, selected ? Color.White : new Color(215, 215, 230), SmallText);
        if (!over) return false;
        if (!string.IsNullOrEmpty(hover)) VanillaTextTip(hover);
        if (PressedLeft)
        {
            ConsumeLeft();
            return true;
        }
        return false;
    }

    /// <summary>原版 UICheckbox：19x21 的方框 + 勾，文字跟在后面。</summary>
    private static bool DrawCheckRow(int x, int y, bool selected, string label, string hover)
    {
        bool right;
        return DrawCheckRow(x, y, selected, label, hover, out right);
    }

    private static bool DrawCheckRow(int x, int y, bool selected, string label, string hover, out bool rightClicked)
    {
        Rectangle box = new Rectangle(x, y, ChkW, ChkH);
        // 命中区比画出来的勾选框大一圈（勾选框 21 高、行距 20，贴边容易点空）
        Rectangle r = new Rectangle(x - 3, y - 3, ChkW + 12 + TextW(label, SmallText), ChkH + 6);
        bool over = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        rightClicked = false;
        Hot(r);
        Fill(box, selected ? new Color(215, 225, 245) : new Color(60, 65, 85));
        Outline(box, new Color(30, 32, 45));
        if (selected) DrawCheckMark(box);
        Text(label, box.Right + 3, y + 3, selected ? Color.White : new Color(215, 215, 230), SmallText);
        if (!over) return false;
        if (!string.IsNullOrEmpty(hover)) VanillaTextTip(hover);
        if (PressedRight)
        {
            ConsumeRight();
            rightClicked = true;
            return false;
        }
        if (PressedLeft)
        {
            ConsumeLeft();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 原版 UIQueryItemSlot：左键把鼠标上的物品放进查询栏，右键清空，空着时显示提示。
    /// 返回新的物品类型；-1 表示这一帧没动过。
    /// </summary>
    private static int DrawQuerySlot(Rectangle r, int current, bool fake, string emptyHint, out bool placedFromMouse)
    {
        placedFromMouse = false;
        bool hover = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        Hot(r);
        // 原版：查询栏里的物品是“浏览选中”的临时查询时用 InventoryBack8 作背景
        Texture2D bg = current > 0 && fake ? GameRefs.Back(8) : GameRefs.Back(9);
        if (bg != null) UIRenderer.DrawTexture(bg, r.X, r.Y, r.Width, r.Height);
        else Fill(r, new Color(35, 38, 55));
        if (current > 0) DrawItemIcon(r, current, 0);
        if (!hover) return -1;
        if (current > 0)
        {
            VanillaItemTip(current);
            if (PressedRight)
            {
                ConsumeRight();
                return 0;
            }
        }
        else if (!string.IsNullOrEmpty(emptyHint))
        {
            VanillaTextTip(emptyHint);
        }
        if (PressedLeft)
        {
            ConsumeLeft();
            int type = 0;
            try
            {
                if (Main.mouseItem != null && !Main.mouseItem.IsAir) type = Main.mouseItem.type;
                else if (Main.HoverItem != null && Main.HoverItem.type > 0 && Main.HoverItem.stack > 0) type = Main.HoverItem.type;
            }
            catch
            {
            }
            if (type <= 0) return -1;
            placedFromMouse = true;
            return type;
        }
        return -1;
    }

    /// <summary>“制作”页自己的查询栏（原版 CraftUI 里是独立的一个槽）。</summary>
    private static void DrawCraftQuerySlot(Rectangle r)
    {
        bool real;
        int t = DrawQuerySlot(r, _craftRoot, true, "在此放置物品", out real);
        if (t < 0) return;
        _craftRoot = t;
        _craftDirty = true;
    }

    // ---------------- 悬浮提示：物品和怪物都交给原版自己画 ----------------

    // 复用同一个 Item 实例，别每帧 new
    private static readonly Item TipItem = new Item();

    /// <summary>
    /// 物品提示：和鼠标停在物品栏里一件物品上看到的完全一样
    /// （稀有度配色的名字 + 伤害/防御/说明那些属性行 + 原版底板）。
    /// 物品刚被塞进去后原版会在这一帧末尾自己画出来。
    /// </summary>
    private static void VanillaItemTip(int type, int stack = 1, string nameSuffix = null)
    {
        if (type <= 0) return;
        try
        {
            TipItem.SetDefaults(type);
            TipItem.stack = Math.Max(1, stack);
            // 别让原版在提示里加“装备栏共享”那种行
            TipItem.tooltipContext = -1;
            TipItem.tooltipSlot = -1;
            if (!string.IsNullOrEmpty(nameSuffix))
                TipItem.SetNameOverride(BrowserData.ItemName(type) + nameSuffix);
            Main.HoverItem = TipItem;
            Main.mouseText = true;
            Main.instance.MouseText(BrowserData.ItemName(type), ItemRare(type));
        }
        catch
        {
        }
    }

    /// <summary>纯文字提示：走原版画 NPC 名字那一套（同样的底板和字体）。</summary>
    private static void VanillaTextTip(string title, string desc = null)
    {
        if (string.IsNullOrEmpty(title)) return;
        try
        {
            Main.ClearHoverItem();
            Main.mouseText = true;
            Main.instance.MouseTextHackZoom(string.IsNullOrEmpty(desc) ? title : title + "\n" + desc);
        }
        catch
        {
        }
    }

    /// <summary>勾选标记（原版是 UIElements/checkMark 贴图，这里用两笔画出来）。</summary>
    private static void DrawCheckMark(Rectangle box)
    {
        Color ink = new Color(40, 70, 40);
        int x = box.X + box.Width / 2 - 3;
        int y = box.Y + box.Height / 2;
        for (int i = 0; i < 3; i++) Fill(new Rectangle(x + i, y - 1 + i, 2, 2), ink);
        for (int i = 0; i < 4; i++) Fill(new Rectangle(x + 3 + i, y + 1 - i, 2, 2), ink);
    }

    // ---------------- 配方图标网格 ----------------

    private static void DrawRecipeGrid(Rectangle panel)
    {
        Panel(panel, SlotPanelColor);
        Rectangle view = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
        int maxScroll;
        GridResult res = DrawGrid(view, _gridList.Count, _scrollGrid, out maxScroll,
            delegate (Rectangle r, int index, bool hover) { DrawRecipeSlot(r, index, hover); });
        HandleWheel(view, ref _scrollGrid, maxScroll);
        _scrollGrid = DrawScrollbar(PanelId + ".grid", view, maxScroll, _scrollGrid);

        if (res.Hovered >= 0)
        {
            int recipeIndex = _gridList[res.Hovered];
            int stack = 1;
            try { stack = Main.recipe[recipeIndex].createItem.stack; } catch { }
            VanillaItemTip(BrowserData.RecipeCreateType(recipeIndex), stack);
        }
        if (res.Clicked >= 0)
        {
            int recipeIndex = _gridList[res.Clicked];
            if (IsDoubleClick(res.Clicked))
            {
                // 原版 UIRecipeSlot.LeftDoubleClick：清空两个搜索框，把产物放进查询栏
                RecipeNameBox.Text = "";
                RecipeDescBox.Text = "";
                SetQuery(BrowserData.RecipeCreateType(recipeIndex));
            }
            else
            {
                SelectRecipe(recipeIndex);
                // 和原模组 UIRecipeSlot.LeftClick 一样：选中之后顺手让游戏自己的制作菜单也跳到这一条
                // （右键那条路是“怎么看出来的”，原模组没同步，这里也不动它）
                GameCraftingMenu.FocusRecipe(recipeIndex);
            }
        }
        if (res.RightClicked >= 0)
        {
            int recipeIndex = _gridList[res.RightClicked];
            SelectRecipe(recipeIndex);
            // 跳到“制作”页，并把该配方的产物放进制作页的查询栏
            _craftRoot = BrowserData.RecipeCreateType(recipeIndex);
            _craftDirty = true;
            SetTab(1);
        }
    }

    private static void SelectRecipe(int recipeIndex)
    {
        _selRecipe = recipeIndex;
        _scrollIngr = 0;
        try { _lootType = Main.recipe[recipeIndex].createItem.type; } catch { }
    }

    private static void DrawRecipeSlot(Rectangle r, int index, bool hover)
    {
        int recipeIndex = _gridList[index];
        int type = BrowserData.RecipeCreateType(recipeIndex);
        int stack = 1;
        try { stack = Main.recipe[recipeIndex].createItem.stack; } catch { }
        // 原版 UIRecipeSlot：底色 InventoryBack9，现在就能做的那种换成模组自带的绿色底板
        // （Images/CanCraftBackground），把材料再做几层就能做的那种换成黄色底板
        // （Images/CanCraftExtendedBackground）—— 和原模组一样，绿底优先（原模组里
        // craftPaths 非空先刷黄底，紧接着 availableRecipe 命中再覆盖成绿底）。
        if (IsCraftableNow(recipeIndex)) DrawModBack(r, "CanCraftBackground", 10);
        else if (Catalogue.IsNestedCraftableRecipe(recipeIndex)) DrawModBack(r, "CanCraftExtendedBackground", 10);
        else DrawBack(r, 9);
        // 选中框也是模组自带的 Images/SelectedOverlay，叠的时候乘 Main.essScale
        if (recipeIndex == _selRecipe) DrawOverlay(r, PulseAlpha(1f));
        else if (hover) DrawOverlay(r, 70);
        DrawItemIcon(r, type, stack);
    }

    private static void DrawItemSlot(Rectangle r, int type, int stack, bool selected, bool hover)
    {
        DrawBack(r);
        // 材料格：原版 UIIngredientSlot 的选中底板就是 InventoryBack15
        if (selected) DrawSelectedPlate(r, PulseAlpha(1f));
        else if (hover) DrawSelectedPlate(r, 70);
        DrawItemIcon(r, type, stack);
    }

    /// <summary>物品页的格子：原版 UIItemCatalogueItemSlot，底色固定 InventoryBack9，选中叠选中框。</summary>
    private static void DrawCatalogueSlot(Rectangle r, int type, int stack, bool selected, bool hover)
    {
        DrawBack(r);
        if (selected) DrawOverlay(r, PulseAlpha(1f));
        else if (hover) DrawOverlay(r, 70);
        DrawItemIcon(r, type, stack);
    }

    private static void DrawNpcSlot(Rectangle r, int npcType, bool selected, bool hover)
    {
        DrawBack(r);
        // 原版：点过的怪会闪一下（clickIndicatorTime = 30）
        if (npcType == _flashNpc && _flashFrames > 0)
            DrawSelectedPlate(r, (byte)Math.Max(0, Math.Min(255, 255 * _flashFrames / 30)));
        if (selected) DrawSelectedPlate(r, PulseAlpha(1f));
        else if (hover) DrawSelectedPlate(r, 70);
        // 负数 ID 是变体（绿史莱姆之类）：贴图和帧数用对应的正面 ID，另外照原版用 npc.color 上色
        int sprite = BrowserData.NpcSpriteType(npcType);
        if (sprite <= 0 || sprite >= Main.npcFrameCount.Length) return;
        Texture2D tex = GameRefs.Npc(sprite);
        Color tint = BrowserData.NpcTint(npcType);
        if (tex != null && Main.spriteBatch != null)
        {
            try
            {
                int frames = Main.npcFrameCount[sprite];
                if (frames <= 0) frames = 1;
                int fh = tex.Height / frames;
                if (fh > 0)
                {
                    int frame = _npcFrame % frames;
                    Rectangle src = new Rectangle(0, fh * frame, tex.Width, fh);
                    float fit = 2f;
                    float limit = r.Width - 6f;
                    if (tex.Width * fit > limit || fh * fit > limit)
                        fit = limit / Math.Max(tex.Width, fh);
                    fit = Math.Min(fit, 0.8f);
                    Vector2 pos = new Vector2(r.X + r.Width / 2f - tex.Width * fit / 2f,
                                              r.Y + r.Height / 2f - fh * fit / 2f);
                    Main.spriteBatch.Draw(tex, pos, src, tint, 0f, Vector2.Zero, fit, SpriteEffects.None, 0f);
                }
            }
            catch
            {
            }
        }
    }

    // ---------------- 通用槽位绘制 ----------------

    /// <summary>格子底色。原版 UIItemSlot.defaultBackgroundTexture = InventoryBack9。</summary>
    private static void DrawBack(Rectangle r, int back = 9)
    {
        Texture2D tex = GameRefs.Back(back);
        if (tex != null) UIRenderer.DrawTexture(tex, r.X, r.Y, r.Width, r.Height);
        else Fill(r, new Color(35, 38, 55));
    }

    /// <summary>模组自带的格子底板（Images/CanCraftBackground 之类）。取不到就退回原版 InventoryBack。</summary>
    private static void DrawModBack(Rectangle r, string name, int fallbackBack)
    {
        Texture2D tex = GameRefs.ModTex(name);
        if (tex != null)
        {
            try
            {
                UIRenderer.DrawTexture(tex, r.X, r.Y, r.Width, r.Height);
                return;
            }
            catch
            {
            }
        }
        DrawBack(r, fallbackBack);
    }

    /// <summary>模组自带的选中框（Images/SelectedOverlay）。取不到就退回原版 InventoryBack15。</summary>
    private static void DrawOverlay(Rectangle r, byte alpha)
    {
        Texture2D tex = GameRefs.ModTex("SelectedOverlay");
        if (tex != null)
        {
            try
            {
                UIRenderer.DrawTexture(tex, r.X, r.Y, r.Width, r.Height, alpha);
                return;
            }
            catch
            {
            }
        }
        DrawSelectedPlate(r, alpha);
    }

    /// <summary>
    /// 生物/材料格子的选中效果。原版是 InventoryBack15 底板（同样画在图标下面）+ Main.essScale 呼吸感。
    /// </summary>
    private static void DrawSelectedPlate(Rectangle r, byte alpha)
    {
        Texture2D tex = GameRefs.Back(15);
        if (tex != null) UIRenderer.DrawTexture(tex, r.X, r.Y, r.Width, r.Height, alpha);
        else Fill(new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4),
            new Color(255, 240, 180, (byte)Math.Min(255, alpha / 3)));
    }

    private static byte PulseAlpha(float factor)
    {
        float s = 1f;
        try { s = Main.essScale; } catch { }
        int a = (int)(255f * factor * s);
        return (byte)Math.Max(0, Math.Min(255, a));
    }

    private static void DrawItemIcon(Rectangle r, int type, int stack)
    {
        Texture2D tex = GameRefs.Item(type);
        if (tex != null && Main.spriteBatch != null)
        {
            try
            {
                Rectangle src = ItemFrame(type, tex);
                float fit = 1f;
                float limit = r.Width;
                if (src.Width > limit || src.Height > limit)
                    fit = limit / Math.Max(src.Width, src.Height);
                fit *= 0.75f;
                Vector2 pos = new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                Main.spriteBatch.Draw(tex, pos, src, Color.White, 0f,
                    new Vector2(src.Width / 2f, src.Height / 2f), fit, SpriteEffects.None, 0f);
            }
            catch
            {
            }
        }
        if (stack > 1)
        {
            string s = stack.ToString();
            Text(s, r.Right - TextW(s, 0.75f) - 2, r.Bottom - 12, Color.White, 0.75f);
        }
    }

    /// <summary>
    /// 原版 UIItemSlot 的做法：直接用 Main.itemAnimations 里那个动画对象自己的当前帧
    /// （GetFrame 的第二参传 -1）。食物类物品注册的是 DrawAnimationVertical(int.MaxValue, 3)，
    /// 它的帧永远停在第 0 帧；以前这里自己按 _animTick 在这 3 帧里循环，于是图标反复变成
    /// “被咬过”的样子，看起来就是忽大忽小地闪。
    /// </summary>
    private static Rectangle ItemFrame(int type, Texture2D tex)
    {
        try
        {
            DrawAnimation[] anims = Main.itemAnimations;
            if (anims != null && type > 0 && type < anims.Length && anims[type] != null)
            {
                Rectangle f = anims[type].GetFrame(tex, -1);
                if (f.Width > 0 && f.Height > 0 && f.X >= 0 && f.Y >= 0 &&
                    f.Right <= tex.Width && f.Bottom <= tex.Height)
                    return f;
            }
        }
        catch
        {
        }
        return new Rectangle(0, 0, tex.Width, tex.Height);
    }

    // ---------------- 制作站列表（总框架内部左侧那一栏） ----------------

    /// <summary>
    /// 原版 tileChooserPanel：点“制作站”后，总框架左侧多出这一栏物块列表（宽 50）。
    /// 左键选/取消该制作站（配方网格只留需要它的配方），右键把该制作站对应的物品放进查询栏。
    /// </summary>
    private static void DrawTileChooser(Rectangle panel)
    {
        Panel(panel, SlotPanelColor);
        Rectangle view = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
        List<KeyValuePair<int, int>> tiles = BrowserData.CraftingTiles;
        int maxScroll;
        GridResult res = DrawGrid(view, tiles.Count, _scrollTile, out maxScroll,
            delegate (Rectangle r, int index, bool hover)
            {
                DrawTileSlot(r, tiles[index].Key, tiles[index].Key == _tileSel, hover);
            }, false);
        HandleWheel(view, ref _scrollTile, maxScroll);

        if (res.Hovered >= 0) VanillaTextTip(BrowserData.TileName(tiles[res.Hovered].Key));
        if (res.Clicked >= 0)
        {
            int tile = tiles[res.Clicked].Key;
            _tileSel = tile == _tileSel ? -1 : tile;
            _gridDirty = true;
        }
        if (res.RightClicked >= 0)
        {
            // 右键：把这个制作站对应的物品放进查询栏（看它本身怎么造出来）
            int place = BrowserData.TilePlaceItem(tiles[res.RightClicked].Key);
            if (place > 0) SetQuery(place);
        }
    }

    private static void DrawTileSlot(Rectangle r, int tile, bool selected, bool hover)
    {
        DrawBack(r);
        // 原版 UITileSlot：底色 InventoryBack9，选中的叠模组自带选中框（不乘 essScale）
        if (selected) DrawOverlay(r, 255);
        else if (hover) DrawOverlay(r, 70);
        DrawTileIcon(r, tile);
    }

    private static void DrawTileIcon(Rectangle r, int tile)
    {
        Texture2D tex = GameRefs.Tile(tile);
        if (tex == null || Main.spriteBatch == null)
        {
            int place = BrowserData.TilePlaceItem(tile);
            if (place > 0) DrawItemIcon(r, place, 0);
            return;
        }
        int tw = 1, th = 1, cpad = 0;
        try
        {
            Terraria.ObjectData.TileObjectData data = Terraria.ObjectData.TileObjectData.GetTileData(tile, 0, 0);
            if (data != null)
            {
                tw = Math.Max(1, data.Width);
                th = Math.Max(1, data.Height);
                cpad = data.CoordinatePadding;
            }
        }
        catch
        {
        }
        float scale = Math.Min((r.Width - 4f) / (tw * 16f), (r.Height - 4f) / (th * 16f));
        if (scale <= 0f) return;
        float ox = r.X + r.Width / 2f - tw * 16f * scale / 2f;
        float oy = r.Y + r.Height / 2f - th * 16f * scale / 2f;
        try
        {
            for (int i = 0; i < tw; i++)
            {
                for (int j = 0; j < th; j++)
                {
                    int sx = i * 16 + i * cpad;
                    int sy = j * 16 + j * cpad;
                    if (sx + 16 > tex.Width || sy + 16 > tex.Height) continue;
                    Rectangle src = new Rectangle(sx, sy, 16, 16);
                    Vector2 pos = new Vector2(ox + i * 16f * scale, oy + j * 16f * scale);
                    Main.spriteBatch.Draw(tex, pos, src, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                }
            }
        }
        catch
        {
        }
    }

    // ---------------- 底部：制作站 + 材料 ----------------

    private static void DrawRecipeInfo(Rectangle bar)
    {
        Panel(bar, SlotPanelColor);
        if (_selRecipe < 0 || _selRecipe >= BrowserData.RecipeCount) return;
        Recipe recipe = null;
        try { recipe = Main.recipe[_selRecipe]; } catch { }
        if (recipe == null) return;

        // 左边文字区：原版固定“0~180，材料面板从 180 开始”。中文字更长，这里按窗口宽度
        // 把文字区放开（最窄仍然是原版的 174），材料面板跟在它后面。
        int lh = GameRefs.TextLine();
        int textW = Math.Max(174, Math.Min(300, bar.Width - 160));
        int x = bar.X + 6;
        int y = bar.Y + 4;
        Player player = null;
        try { player = Main.LocalPlayer; } catch { }

        string title = "制作站";
        try { title = Language.GetTextValue("LegacyInterface.22"); } catch { }
        Text(title, x, y, Color.White);

        List<string> names = new List<string>();
        List<bool> okFlags = new List<bool>();

        int tile = BrowserData.RecipeStation(_selRecipe);
        if (tile < 0)
        {
            string byHand = "徒手";
            try { byHand = Language.GetTextValue("LegacyInterface.23"); } catch { }
            names.Add(byHand);
            okFlags.Add(true);
        }
        else
        {
            bool ok = false;
            try { ok = player != null && player.adjTile[tile]; } catch { }
            names.Add(BrowserData.TileName(tile));
            okFlags.Add(ok);
        }
        AddCondition(names, okFlags, recipe.needWater, player != null && SafeAdj(player, 0), "水");
        AddCondition(names, okFlags, recipe.needHoney, player != null && SafeAdj(player, 1), "蜂蜜");
        AddCondition(names, okFlags, recipe.needLava, player != null && SafeAdj(player, 2), "岩浆");
        AddCondition(names, okFlags, recipe.needSnowBiome, player != null && SafeZone(player, 0), "雪原生物群系");
        AddCondition(names, okFlags, recipe.needGraveyardBiome, player != null && SafeZone(player, 1), "墓地");

        // 原版：第一行只有“制作站”三个字，制作站/条件画在下一行（一行的高度），逐项着色，
        //       总宽超过文字区时整行等比缩小。
        int total = 0;
        for (int i = 0; i < names.Count; i++) total += TextW((i == 0 ? "" : ", ") + names[i]);
        float lineScale = total > textW ? (float)textW / total : 1f;
        int used = 0;
        for (int i = 0; i < names.Count; i++)
        {
            string piece = (i == 0 ? "" : ", ") + names[i];
            int w = (int)Math.Ceiling(TextW(piece) * lineScale);
            if (used + w > textW) break;
            Text(piece, x + used, y + lh, okFlags[i] ? YesColor : NoColor, lineScale);
            used += w;
        }
        if (total > textW && UIRenderer.IsMouseOver(x, y, textW, lh * 2))
            VanillaTextTip("制作站：" + string.Join(", ", names));

        // 材料子面板（照搬原版：跟在文字区后面，右边留 6；高度和整条栏一样）
        Rectangle ingPanel = new Rectangle(bar.X + textW + 12, bar.Y,
            Math.Max(60, bar.Width - textW - 18), bar.Height);
        Panel(ingPanel, SlotPanelColor);
        List<Ingredient> ings = BrowserData.IngredientsOf(_selRecipe);
        Rectangle view = new Rectangle(ingPanel.X + 6, ingPanel.Y + 6, ingPanel.Width - 12, ingPanel.Height - 12);
        int totalW = ings.Count == 0 ? 0 : ings.Count * (Slot + Pad) - Pad;
        int maxScrollH = Math.Max(0, totalW - view.Width);
        if (UIRenderer.IsMouseOver(view.X, view.Y, view.Width, view.Height))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                _scrollIngr = Clamp(_scrollIngr - Math.Sign(wheel) * 60, 0, maxScrollH);
                UIRenderer.ConsumeScroll();
            }
        }
        _scrollIngr = Clamp(_scrollIngr, 0, maxScrollH);

        UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height);
        try
        {
            for (int i = 0; i < ings.Count; i++)
            {
                Rectangle r = new Rectangle(view.X + i * (Slot + Pad) - _scrollIngr, view.Y, Slot, Slot);
                if (r.Right < view.X || r.X > view.Right) continue;
                bool hover = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY) &&
                             UIRenderer.IsMouseOver(view.X, view.Y, view.Width, view.Height);
                Ingredient ing = ings[i];
                Hot(r);
                DrawItemSlot(r, ing.Type, ing.Stack, ing.Type == _lootType, hover);
                if (!hover) continue;
                string label = ing.GroupId >= 0 && !string.IsNullOrEmpty(ing.Text) ? ing.Text : BrowserData.ItemName(ing.Type);
                if (ing.GroupId >= 0) VanillaTextTip(label);
                else VanillaItemTip(ing.Type, ing.Stack);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    if (IsDoubleClick(100000 + i)) SetQuery(ing.Type);
                    else _lootType = ing.Type;
                }
            }
        }
        finally
        {
            UIRenderer.EndClip();
        }
        if (maxScrollH > 0)
        {
            Text("<", view.X + 2, view.Bottom - 14, new Color(245, 245, 255), 0.8f);
            Text(">", view.Right - 12, view.Bottom - 14, new Color(245, 245, 255), 0.8f);
        }
    }

    private static bool SafeAdj(Player p, int kind)
    {
        try
        {
            if (kind == 0) return p.adjWaterSource;
            if (kind == 1) return p.adjHoney;
            return p.adjLava;
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeZone(Player p, int kind)
    {
        try { return kind == 0 ? p.ZoneSnow : p.ZoneGraveyard; }
        catch { return false; }
    }

    private static void AddCondition(List<string> names, List<bool> flags, bool needed, bool met, string fallback)
    {
        if (!needed) return;
        names.Add(fallback);
        flags.Add(met);
    }

    // ---------------- 右侧：掉落自 ----------------

    private static void DrawLootColumn(Rectangle panel)
    {
        Panel(panel, SlotPanelColor);
        Rectangle view = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
        if (_lootType <= 0) return;
        List<DropSource> drops = BrowserData.LootOf(_lootType);
        int maxScroll;
        GridResult res = DrawGrid(view, drops.Count, _scrollLoot, out maxScroll,
            delegate (Rectangle r, int index, bool hover)
            {
                DrawNpcSlot(r, drops[index].NpcType, drops[index].NpcType == _selNpc, hover);
            }, false);
        HandleWheel(view, ref _scrollLoot, maxScroll);

        if (res.Hovered >= 0)
        {
            VanillaTextTip(BrowserData.NpcName(drops[res.Hovered].NpcType));
        }
        if (res.Clicked >= 0)
        {
            int npc = drops[res.Clicked].NpcType;
            // 原版 UINPCSlot.LeftClick：单击这只怪，就把它的掉落以 [i:物品] 的形式发到聊天栏
            // （双击才跳图鉴；原版也是先单击出字、再双击跳转）。
            _selNpc = npc;
            _flashNpc = npc;
            _flashFrames = 30;
            PrintNpcDrops(npc);
            if (IsDoubleClick(200000 + res.Clicked))
            {
                // 原版 UINPCSlot.LeftDoubleClick：跳到怪物图鉴、清空它的搜索框和查询栏，并定位到这只怪
                NpcNameBox.Text = "";
                _npcQueryType = 0;
                _npcQueryFake = true;
                _gridDirty = true;
                _jumpNpc = npc;
                SetTab(3);
            }
        }
    }

    /// <summary>原版 UINPCSlot.LeftClick：把一只怪的全部掉落以 [i:物品] 的形式发到聊天栏。</summary>
    private static void PrintNpcDrops(int npcType)
    {
        try
        {
            bool zh = true;
            try { zh = Language.ActiveCulture != null && Language.ActiveCulture.Name.StartsWith("zh"); } catch { }
            StringBuilder sb = new StringBuilder(string.Format(zh ? "{0}掉落:" : "{0} drops:", BrowserData.NpcName(npcType)));
            List<NpcDrop> list = BrowserData.NpcDrops(npcType);
            for (int i = 0; i < list.Count; i++) sb.Append("[i:").Append(list[i].ItemType).Append(']');
            Main.NewText(sb.ToString(), byte.MaxValue, byte.MaxValue, byte.MaxValue);
        }
        catch
        {
        }
    }

    /// <summary>原版 BestiaryUI 的掉落物栏：横向排一列物品格（Top -50、宽是总框架的一半、高 50）。</summary>
    private static void DrawBestiaryLoot(Rectangle panel)
    {
        Panel(panel, SlotPanelColor);
        Rectangle view = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);
        List<NpcDrop> drops = BrowserData.NpcDrops(_selNpc);
        int totalW = drops.Count == 0 ? 0 : drops.Count * (Slot + Pad) - Pad;
        int maxScroll = Math.Max(0, totalW - view.Width);
        if (UIRenderer.IsMouseOver(view.X, view.Y, view.Width, view.Height))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                _scrollNpcLoot = Clamp(_scrollNpcLoot - Math.Sign(wheel) * 48, 0, maxScroll);
                UIRenderer.ConsumeScroll();
            }
        }
        _scrollNpcLoot = Clamp(_scrollNpcLoot, 0, maxScroll);

        UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height);
        try
        {
            for (int i = 0; i < drops.Count; i++)
            {
                Rectangle r = new Rectangle(view.X + i * (Slot + Pad) - _scrollNpcLoot, view.Y, Slot, Slot);
                if (r.Right < view.X || r.X > view.Right) continue;
                bool hover = view.Contains(UIRenderer.MouseX, UIRenderer.MouseY) &&
                             r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
                Hot(r);
                DrawItemSlot(r, drops[i].ItemType, 1, drops[i].ItemType == _npcQueryType, hover);
                if (!hover) continue;
                VanillaItemTip(drops[i].ItemType);
                if (PressedLeft)
                {
                    ConsumeLeft();
                    // 原版 UIBestiaryItemSlot.LeftDoubleClick：跳到配方页查这个物品
                    if (IsDoubleClick(300000 + i))
                    {
                        RecipeNameBox.Text = "";
                        RecipeDescBox.Text = "";
                        SetQuery(drops[i].ItemType);
                        SetTab(0);
                    }
                }
                if (PressedRight)
                {
                    ConsumeRight();
                    // 原版 UIBestiaryItemSlot.RightClick：把它放进图鉴的查询栏（筛出掉落它的怪）
                    _npcQueryType = drops[i].ItemType;
                    _npcQueryFake = true;
                    _gridDirty = true;
                }
            }
        }
        finally
        {
            UIRenderer.EndClip();
        }
    }

    private static string BestiarySortLabel(string key, string fallback)
    {
        try
        {
            string s = Language.GetTextValue("BestiaryInfo." + key);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch
        {
        }
        return fallback;
    }

    // ================= 制作页 =================

    private static void DrawCraftTab(int cx, int cy, int cw, int ch)
    {
        // 原版 CraftUI：查询栏 + 一排“来源开关” + 一条可滚动的合成路径列表。
        // 它回答的是“这个东西到底要怎么弄到手”，而不是把配方再列一遍。
        DrawCraftQuerySlot(new Rectangle(cx + 16, cy + 2, Slot, Slot));

        // 三个来源开关放在物品右边，和查询栏同一行
        int tx = cx + 61;
        bool t1, t2, t3;
        tx += DrawToggle(tx, cy + 11, "嵌套合成", _craftNested,
            "递归展开每一层材料\n关掉就只显示直接材料", out t1) + 10;
        tx += DrawToggle(tx, cy + 11, "掉落", _craftLoot,
            "标出只能靠打怪、开箱掉落的材料\n会显示“掉落自 XX”", out t2) + 10;
        DrawToggle(tx, cy + 11, "采集", _craftMine,
            "标出能直接从世界里采集的材料（矿、木头之类）", out t3);
        if (t1) { _craftNested = !_craftNested; _craftDirty = true; }
        if (t2) { _craftLoot = !_craftLoot; _craftDirty = true; }
        if (t3) { _craftMine = !_craftMine; _craftDirty = true; }

        // 和配方页一样，底部留出 16px，免得右下角的缩放把压在列表滚动条上
        Rectangle panel = new Rectangle(cx, cy + TopRowH, cw, ch - TopRowH - 16);
        Panel(panel, SlotPanelColor);
        Rectangle view = new Rectangle(panel.X + 6, panel.Y + 6, panel.Width - 12, panel.Height - 12);

        EnsureCraftPath();
        if (_craftRoot <= 0) return;
        List<CraftNode> rows = _craftRows ?? new List<CraftNode>();
        if (rows.Count == 0)
        {
            TextFit("没有找到和它有关的合成配方。", view.X + 4, view.Y + 8,
                new Color(240, 240, 250), view.Width - 8, 0.85f);
            return;
        }

        const int rowH = 34;
        int maxScroll = Math.Max(0, rows.Count * rowH - view.Height);
        _scrollCraft = Clamp(_scrollCraft, 0, maxScroll);
        HandleWheel(view, ref _scrollCraft, maxScroll);
        _scrollCraft = DrawScrollbar(PanelId + ".craft", view, maxScroll, _scrollCraft);

        UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height);
        try
        {
            int first = Math.Max(0, _scrollCraft / rowH);
            int last = Math.Min(rows.Count - 1, (_scrollCraft + view.Height) / rowH);
            for (int i = first; i <= last; i++)
            {
                DrawCraftRow(view, rows[i], rowH, view.Y + i * rowH - _scrollCraft);
            }
        }
        finally
        {
            UIRenderer.EndClip();
        }
    }

    private static void EnsureCraftPath()
    {
        if (!_craftDirty && _craftRows != null && _craftFor == _craftRoot) return;
        _craftDirty = false;
        _craftFor = _craftRoot;
        _craftRows = BrowserData.BuildCraftPath(_craftRoot, _craftNested, _craftLoot, _craftMine);
        _scrollCraft = 0;
    }

    private static void DrawCraftRow(Rectangle view, CraftNode node, int rowH, int y)
    {
        int indent = 4 + node.Depth * 16;
        Rectangle icon = new Rectangle(view.X + indent, y + 1, 30, 30);
        Hot(new Rectangle(view.X, y, Math.Max(20, view.Width - BarW), rowH - 2));
        // 和网格一样：滚出可视区的那半行不算点中
        bool hover = view.Contains(UIRenderer.MouseX, UIRenderer.MouseY) &&
                     icon.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        DrawBack(icon);
        DrawItemIcon(icon, node.ItemType, 0);

        string name = BrowserData.ItemName(node.ItemType);
        if (node.Stack > 1) name += " x" + node.Stack;

        string right = "";
        Color rightColor = MaybeColor;
        if (node.RecipeIndex >= 0)
        {
            int tile = BrowserData.RecipeStation(node.RecipeIndex);
            string station = tile < 0 ? "徒手" : BrowserData.TileName(tile);
            right = "制作站: " + station;
            bool ok = false;
            try { ok = tile < 0 || Main.LocalPlayer.adjTile[tile]; } catch { }
            rightColor = ok ? YesColor : NoColor;
        }
        else if (!string.IsNullOrEmpty(node.Source))
        {
            right = node.Source;
        }

        int textX = icon.Right + 6;
        int rightW = right.Length == 0 ? 0 : Math.Min(TextW(right, 0.8f), Math.Max(40, view.Width / 2));
        int nameMax = Math.Max(40, view.Width - (textX - view.X) - rightW - 14);
        TextFit(name, textX, y + 8, Color.White, nameMax, 0.85f);
        if (rightW > 0)
        {
            TextFit(right, view.Right - BarW - rightW - 2, y + 9, rightColor, rightW, 0.8f);
        }

        if (!hover) return;
        VanillaItemTip(node.ItemType, node.Stack);
        if (PressedLeft)
        {
            ConsumeLeft();
            _craftRoot = node.ItemType;
            _craftDirty = true;
        }
    }

    /// <summary>小勾选框（原版 UICheckbox：19x21 勾选框 + 文字），返回它占用的宽度。</summary>
    private static int DrawToggle(int x, int y, string label, bool value, string tip, out bool clicked)
    {
        Rectangle box = new Rectangle(x, y, ChkW, ChkH);
        int width = box.Width + 4 + TextW(label, SmallText) + 6;
        Rectangle hit = new Rectangle(x - 2, y - 2, width, ChkH + 4);
        clicked = false;
        Hot(hit);
        Fill(box, value ? new Color(215, 225, 245) : new Color(60, 65, 85));
        Outline(box, new Color(30, 32, 45));
        if (value) DrawCheckMark(box);
        Text(label, box.Right + 4, y + 4, value ? Color.White : new Color(215, 215, 230), SmallText);
        if (!hit.Contains(UIRenderer.MouseX, UIRenderer.MouseY)) return width;
        VanillaTextTip(tip);
        if (PressedLeft)
        {
            ConsumeLeft();
            clicked = true;
        }
        return width;
    }

    // ================= 物品页 =================

    private static void DrawItemTab(int cx, int cy, int cw, int ch)
    {
        // 右上角竖排两个搜索框：原版 Left -150、Top 0 / 30、150x25
        DrawSearchBox(new Rectangle(cx + cw - 150, cy, 150, 25), ItemNameBox);
        DrawSearchBox(new Rectangle(cx + cw - 150, cy + 30, 150, 25), ItemDescBox);

        // 搜索栏左边两个勾选：原版 Crafted / Loot 都在 Left -270、Top 0 / 20（挨在一起）。
        // 这里按用户要求把它们分开，各自对齐右边对应的那个搜索框（Top 0 和 Top 30）。
        if (DrawCheckRow(cx + cw - 270, cy, _itemCrafted, "可制作", "仅显示可制作的物品"))
        {
            _itemCrafted = !_itemCrafted;
            _gridDirty = true;
        }
        if (DrawCheckRow(cx + cw - 270, cy + 30, _itemLoot, "掉落物", "仅显示掉落物"))
        {
            _itemLoot = !_itemLoot;
            _gridDirty = true;
        }

        // 分类/排序/筛选栏：物品页和搜索框同排（原版 sortsAndFiltersPanel 在这里 Top 0、Width -272），
        // 挤在左边那块空地上，右边留给搜索框和两个勾选
        DrawCatalogueBar(new Rectangle(cx, cy, Math.Max(90, cw - 276), CatH), true);

        // 物品网格：原版 itemGridPanel Top 60、Width 0,1f、Height -76（下面留 16 给缩放把）
        Rectangle gridPanel = new Rectangle(cx, cy + ItemTopH, cw, ch - ItemTopH - GripGap);
        Panel(gridPanel, SlotPanelColor);
        Rectangle view = new Rectangle(gridPanel.X + 6, gridPanel.Y + 6, gridPanel.Width - 12, gridPanel.Height - 12);
        if (_jumpItem > 0)
        {
            int idx = _gridList.IndexOf(_jumpItem);
            if (idx >= 0)
            {
                _selItem = _jumpItem;
                ScrollGridTo(view, idx, _gridList.Count);
            }
            _jumpItem = 0;
        }
        int maxScroll;
        GridResult res = DrawGrid(view, _gridList.Count, _scrollGrid, out maxScroll,
            delegate (Rectangle r, int index, bool hover)
            {
                int type = _gridList[index];
                DrawCatalogueSlot(r, type, 1, type == _selItem, hover);
            });
        HandleWheel(view, ref _scrollGrid, maxScroll);
        _scrollGrid = DrawScrollbar(PanelId + ".grid", view, maxScroll, _scrollGrid);

        if (res.Hovered >= 0) VanillaItemTip(_gridList[res.Hovered]);
        if (res.Clicked >= 0)
        {
            int type = _gridList[res.Clicked];
            if (IsDoubleClick(res.Clicked))
            {
                // 原版 UIItemCatalogueItemSlot.LeftDoubleClick：清空两个搜索框，把物品放进配方页查询栏
                RecipeNameBox.Text = "";
                RecipeDescBox.Text = "";
                SetQuery(type);
                SetTab(0);
            }
            else
            {
                _selItem = type;
            }
        }
        if (res.RightClicked >= 0)
        {
            int type = _gridList[res.RightClicked];
            _selItem = type;
            // 原版 RightDoubleClick：清空图鉴搜索框，跳到怪物图鉴并筛选出掉落它的生物
            if (IsRightDoubleClick(res.RightClicked))
            {
                NpcNameBox.Text = "";
                _npcQueryType = type;
                _npcQueryFake = true;
                _gridDirty = true;
                SetTab(3);
            }
        }
    }

    // ================= 怪物图鉴页 =================

    private static void DrawBestiaryTab(int cx, int cy, int cw, int ch)
    {
        // 左上角：当前选中的物品（原版 BestiaryUI.queryItem Left 0 Top 0）
        bool fromMouse;
        int q = DrawQuerySlot(new Rectangle(cx, cy, Slot, Slot), _npcQueryType, _npcQueryFake, "在此放置物品", out fromMouse);
        if (q >= 0)
        {
            _npcQueryType = q;
            _npcQueryFake = q > 0 && !fromMouse;
            _gridDirty = true;
        }

        // 物品右边：图鉴ID / ID 两个排序单选（原版 RadioButtonGroup Left 45、宽 180）
        int gx = cx + 45;
        if (DrawRadioRow(gx, cy, _npcSort == 0, BestiarySortLabel("Sort_BestiaryID", "图鉴ID"), null))
        {
            _npcSort = 0;
            _gridDirty = true;
        }
        if (DrawRadioRow(gx, cy + RowH, _npcSort == 1, BestiarySortLabel("Sort_ID", "ID"), null))
        {
            _npcSort = 1;
            _gridDirty = true;
        }

        // 右上角搜索框：原版 Left -150、Top 0、150x25
        DrawSearchBox(new Rectangle(cx + cw - 150, cy, 150, 25), NpcNameBox);

        // 生物网格：原版 npcGridPanel Top 46、Width 0,1f、Height -98
        Rectangle gridPanel = new Rectangle(cx, cy + BestiaryTopH, cw, ch - BestiaryTopH - 52);
        Panel(gridPanel, SlotPanelColor);
        Rectangle view = new Rectangle(gridPanel.X + 6, gridPanel.Y + 6, gridPanel.Width - 12, gridPanel.Height - 12);
        // 底部那一整条（掉落栏 + 两个勾选）压在网格上面时，格子不能再抢点击
        Rectangle lootBar = new Rectangle(cx, cy + ch - BottomBarH, cw / 2, BottomBarH);
        Rectangle chkArea = new Rectangle(cx + cw / 2, cy + ch - BottomBarH, cw - cw / 2, BottomBarH);
        GuardClicks(lootBar);
        GuardClicks(chkArea);
        if (_jumpNpc > 0)
        {
            int idx = _gridList.IndexOf(_jumpNpc);
            if (idx >= 0)
            {
                _selNpc = _jumpNpc;
                ScrollGridTo(view, idx, _gridList.Count);
            }
            _jumpNpc = 0;
        }
        int maxScroll;
        GridResult res = DrawGrid(view, _gridList.Count, _scrollGrid, out maxScroll,
            delegate (Rectangle r, int index, bool hover)
            {
                int npc = _gridList[index];
                DrawNpcSlot(r, npc, npc == _selNpc, hover);
            });
        HandleWheel(view, ref _scrollGrid, maxScroll);
        _scrollGrid = DrawScrollbar(PanelId + ".grid", view, maxScroll, _scrollGrid);

        if (res.Hovered >= 0)
        {
            // 原版 UINPCSlot：悬浮只显示名字
            VanillaTextTip(BrowserData.NpcName(_gridList[res.Hovered]));
        }
        if (res.Clicked >= 0)
        {
            _selNpc = _gridList[res.Clicked];
        }

        // 底部左侧：掉落物（原版 Top -50、Width 0,0.5f、Height 50）
        DrawBestiaryLoot(lootBar);

        // 底部右侧：曾遇见过 / 含掉落物（原版 Left 6,0.5f、Top -40 / -20，两行挨着）。
        // 这里把两行拉开到和左侧掉落栏同样的上下边（-50 / -20），读起来不会挤在一起。
        int chkX = cx + cw / 2 + 6;
        bool right;
        string encTip = _npcUnencountered
            ? "仅显示未被击杀的生物\n(右键切换至已被击杀的生物)"
            : "仅显示已被击杀的生物\n(右键切换至未被击杀的生物)";
        if (DrawCheckRow(chkX, cy + ch - RowH - 30, _npcEncountered,
                _npcUnencountered ? "未曾遇见过" : "曾遇见过", encTip, out right))
        {
            _npcEncountered = !_npcEncountered;
            _gridDirty = true;
        }
        if (right)
        {
            // 原版：右键切换成“未曾遇见过”
            _npcUnencountered = !_npcUnencountered;
            if (!_npcEncountered) _npcEncountered = true;
            _gridDirty = true;
        }
        if (DrawCheckRow(chkX, cy + ch - RowH, _npcHasLoot, "含掉落物", "仅显示具有掉落物的NPC"))
        {
            _npcHasLoot = !_npcHasLoot;
            _gridDirty = true;
        }
    }

    // ================= 通用网格 =================

    private sealed class GridResult
    {
        public int Hovered = -1;
        public int Clicked = -1;
        public int RightClicked = -1;
    }

    private delegate void SlotDrawer(Rectangle r, int index, bool hover);

    /// <summary>reserveBar=false 时不再为滚动条留 20px（窄面板用，原版这类格子用的是隐形滚动条）。</summary>
    private static GridResult DrawGrid(Rectangle view, int count, int scroll, out int maxScroll,
        SlotDrawer drawer, bool reserveBar = true)
    {
        GridResult res = new GridResult();
        int innerW = Math.Max(Slot, view.Width - (reserveBar ? BarW : 0));
        int cell = Slot + Pad;
        int cols = Math.Max(1, innerW / cell);
        int rows = count == 0 ? 0 : (count + cols - 1) / cols;
        int totalH = rows == 0 ? 0 : rows * cell - Pad;
        maxScroll = Math.Max(0, totalH - view.Height);
        scroll = Clamp(scroll, 0, maxScroll);
        if (count == 0 || view.Width <= 0 || view.Height <= 0) return res;

        int firstRow = Math.Max(0, scroll / cell);
        int lastRow = Math.Min(rows - 1, (scroll + view.Height) / cell);
        // 鼠标必须落在可视区里才算点到格子：滚出去（被裁掉）的那半截格子矩形还在，
        // 只看矩形的话压在它上面的按钮会被先吃掉（图鉴底部的“曾遇见过”就是这样点不动的）。
        int mmx = UIRenderer.MouseX;
        int mmy = UIRenderer.MouseY;
        bool inView = view.Contains(mmx, mmy) && !ClickGuarded(mmx, mmy);
        UIRenderer.BeginClip(view.X, view.Y, view.Width, view.Height);
        try
        {
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int index = row * cols + col;
                    if (index >= count) break;
                    Rectangle r = new Rectangle(view.X + col * cell, view.Y + row * cell - scroll, Slot, Slot);
                    bool hover = inView && r.Contains(mmx, mmy);
                    Hot(r);
                    drawer(r, index, hover);
                    if (!hover) continue;
                    res.Hovered = index;
                    if (PressedLeft)
                    {
                        ConsumeLeft();
                        res.Clicked = index;
                    }
                    if (PressedRight)
                    {
                        ConsumeRight();
                        res.RightClicked = index;
                    }
                }
            }
        }
        finally
        {
            UIRenderer.EndClip();
        }
        return res;
    }

    private static void HandleWheel(Rectangle view, ref int scroll, int maxScroll)
    {
        if (!UIRenderer.IsMouseOver(view.X, view.Y, view.Width, view.Height)) return;
        int wheel = UIRenderer.ScrollWheel;
        if (wheel == 0) return;
        scroll = Clamp(scroll - Math.Sign(wheel) * 48, 0, maxScroll);
        UIRenderer.ConsumeScroll();
    }

    private static int DrawScrollbar(string id, Rectangle view, int maxScroll, int scroll)
    {
        int sbX = view.Right - BarW;
        bool over = UIRenderer.IsMouseOver(sbX, view.Y, BarW, view.Height);
        Hot(new Rectangle(sbX, view.Y, BarW, view.Height));
        Fill(new Rectangle(sbX, view.Y, BarW, view.Height), new Color((byte)20, (byte)22, (byte)34, (byte)110));
        if (maxScroll <= 0)
        {
            if (_scrollDragId == id) _scrollDragId = null;
            return scroll;
        }
        int thumbH = Math.Max(24, (int)((float)view.Height * view.Height / (view.Height + maxScroll)));
        int track = Math.Max(1, view.Height - thumbH);
        int thumbY = view.Y + (int)((float)track * scroll / maxScroll);
        Rectangle thumb = new Rectangle(sbX + 3, thumbY, BarW - 6, thumbH);
        bool overThumb = thumb.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        if (over && PressedLeft)
        {
            ConsumeLeft();
            if (overThumb)
            {
                _scrollDragId = id;
                _scrollDragDY = UIRenderer.MouseY - thumb.Y;
            }
            else
            {
                scroll = Clamp((UIRenderer.MouseY - view.Y - thumbH / 2) * maxScroll / track, 0, maxScroll);
            }
        }
        if (_scrollDragId == id)
        {
            if (!UIRenderer.MouseLeft) _scrollDragId = null;
            else scroll = Clamp((UIRenderer.MouseY - _scrollDragDY - view.Y) * maxScroll / track, 0, maxScroll);
        }
        Fill(thumb, overThumb || _scrollDragId == id ? new Color(205, 215, 240, 240) : new Color(155, 165, 195, 220));
        Outline(thumb, new Color(60, 65, 90));
        return scroll;
    }

    /// <summary>把网格滚到第 index 项（尽量居中），用于“查询后自动跳到该物品”。</summary>
    private static void ScrollGridTo(Rectangle view, int index, int count, bool reserveBar = true)
    {
        if (index < 0 || count <= 0) return;
        int innerW = Math.Max(Slot, view.Width - (reserveBar ? BarW : 0));
        int cell = Slot + Pad;
        int cols = Math.Max(1, innerW / cell);
        int rows = (count + cols - 1) / cols;
        int totalH = rows * cell - Pad;
        int maxScroll = Math.Max(0, totalH - view.Height);
        int row = index / cols;
        _scrollGrid = Clamp(row * cell - view.Height / 2 + Slot / 2, 0, maxScroll);
    }

    /// <summary>把文字缩放到不超过 maxW；实在太长就截断加省略号，避免文字溢出窗口。</summary>
    private static void TextFit(string s, int x, int y, Color c, int maxW, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s) || maxW <= 8) return;
        int w = TextW(s, scale);
        if (w > maxW)
        {
            scale *= (float)maxW / w;
            if (scale < 0.55f)
            {
                float baseScale = 0.55f;
                int limit = (int)(maxW / baseScale);
                string cut = s;
                while (cut.Length > 1 && TextW(cut) > limit) cut = cut.Substring(0, cut.Length - 1);
                Text(cut + "…", x, y, c, baseScale);
                return;
            }
        }
        Text(s, x, y, c, scale);
    }

    // ================= 绘制辅助 =================

    private static void Fill(Rectangle r, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        UIRenderer.DrawRect(r.X, r.Y, r.Width, r.Height, c.R, c.G, c.B, c.A);
    }

    private static void Outline(Rectangle r, Color c, int thickness = 1)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        UIRenderer.DrawRectOutline(r.X, r.Y, r.Width, r.Height, c.R, c.G, c.B, c.A, thickness);
    }

    private static void Text(string s, int x, int y, Color c, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (scale >= 0.999f) UIRenderer.DrawText(s, x, y, c.R, c.G, c.B, c.A);
        else UIRenderer.DrawTextScaled(s, x, y, c.R, c.G, c.B, c.A, scale);
    }

    private static int TextW(string s, float scale = 1f)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return (int)(UIRenderer.MeasureText(s) * scale);
    }

    private static void Panel(Rectangle r, Color c)
    {
        // 原版 UIPanel：Images/UI/PanelBackground + PanelBorder，corner 12 / bar 4
        Texture2D bg = GameRefs.Vanilla("Images/UI/PanelBackground");
        Texture2D bd = GameRefs.Vanilla("Images/UI/PanelBorder");
        if (bg == null)
        {
            Fill(r, Mul(c, 0.70f));
            Fill(new Rectangle(r.X + 2, r.Y + 2, Math.Max(0, r.Width - 4), 1), Mul(c, 0.95f));
            Outline(r, Mul(c, 1.15f), 2);
            return;
        }
        DrawNineSlice(bg, r, c);
        if (bd != null) DrawNineSlice(bd, r, Color.Black);
    }

    /// <summary>原版 UIBottomlessPanel：同样两张贴图，只是不画下边框（标签页用）。</summary>
    private static void PanelBottomless(Rectangle r, Color c)
    {
        Texture2D bg = GameRefs.Vanilla("Images/UI/PanelBackground");
        Texture2D bd = GameRefs.Vanilla("Images/UI/PanelBorder");
        if (bg == null)
        {
            Fill(r, Mul(c, 0.9f));
            Outline(r, Mul(c, 1.15f), 2);
            return;
        }
        DrawBottomless(bg, r, c);
        if (bd != null) DrawBottomless(bd, r, Color.Black);
    }

    private const int PanelCorner = 12;
    private const int PanelBar = 4;

    private static void DrawNineSlice(Texture2D t, Rectangle r, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        const int C = PanelCorner;
        const int B = PanelBar;
        int px = r.X, py = r.Y;
        int px2 = px + r.Width - C, py2 = py + r.Height - C;
        int cw = px2 - px - C, chh = py2 - py - C;
        Slice(t, px, py, C, C, 0, 0, C, C, c);
        Slice(t, px2, py, C, C, C + B, 0, C, C, c);
        Slice(t, px, py2, C, C, 0, C + B, C, C, c);
        Slice(t, px2, py2, C, C, C + B, C + B, C, C, c);
        Slice(t, px + C, py, cw, C, C, 0, B, C, c);
        Slice(t, px + C, py2, cw, C, C, C + B, B, C, c);
        Slice(t, px, py + C, C, chh, 0, C, C, B, c);
        Slice(t, px2, py + C, C, chh, C + B, C, C, B, c);
        Slice(t, px + C, py + C, cw, chh, C, C, B, B, c);
    }

    private static void DrawBottomless(Texture2D t, Rectangle r, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        const int C = PanelCorner;
        const int B = PanelBar;
        int px = r.X, py = r.Y;
        int px2 = px + r.Width - C;
        int cw = px2 - px - C, chh = r.Height - C;
        Slice(t, px, py, C, C, 0, 0, C, C, c);
        Slice(t, px2, py, C, C, C + B, 0, C, C, c);
        Slice(t, px + C, py, cw, C, C, 0, B, C, c);
        Slice(t, px, py + C, C, chh, 0, C, C, B, c);
        Slice(t, px2, py + C, C, chh, C + B, C, C, B, c);
        Slice(t, px + C, py + C, cw, chh, C, C, B, B, c);
    }

    private static void Slice(Texture2D t, int x, int y, int w, int h, int sx, int sy, int sw, int sh, Color c)
    {
        if (w <= 0 || h <= 0) return;
        try
        {
            if (Main.spriteBatch != null)
                Main.spriteBatch.Draw(t, new Rectangle(x, y, w, h), new Rectangle(sx, sy, sw, sh), c);
        }
        catch
        {
        }
    }

    private static Color Mul(Color c, float f)
    {
        return new Color(
            (byte)Math.Max(0, Math.Min(255, (int)(c.R * f))),
            (byte)Math.Max(0, Math.Min(255, (int)(c.G * f))),
            (byte)Math.Max(0, Math.Min(255, (int)(c.B * f))),
            c.A);
    }

    private static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    private static bool IsDoubleClick(int index)
    {
        bool doubleClick = _clickIndex == index && _frame - _clickFrame <= 20;
        _clickIndex = index;
        _clickFrame = _frame;
        return doubleClick;
    }

    /// <summary>右键的“双击”判定（原版 UIItemSlot 的 RightDoubleClick）。</summary>
    private static bool IsRightDoubleClick(int index)
    {
        bool doubleClick = _rclickIndex == index && _frame - _rclickFrame <= 20;
        _rclickIndex = index;
        _rclickFrame = _frame;
        return doubleClick;
    }
}
