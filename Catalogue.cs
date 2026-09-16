using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace RecipeBrowser;

/// <summary>
/// 图标的一层。三种来源三选一：Item 是物品类型；Tex 是模组自带贴图名
/// （嵌入 DLL 的 assets\*.rawimg，原模组那几张 sort*.rawimg 就在里面）；
/// Vanilla 是原版贴图路径（Main.Assets，例如原模组直接拿原版的 Images/UI/Craft_Toggle_2
/// 当“合成顺序”的图标）。
/// </summary>
internal struct IconLayer
{
    public int Item;
    public string Tex;
    public string Vanilla;

    public static IconLayer OfItem(int type) { return new IconLayer { Item = type }; }
    public static IconLayer OfTex(string tex) { return new IconLayer { Tex = tex }; }
    public static IconLayer OfVanilla(string path) { return new IconLayer { Vanilla = path }; }
}

/// <summary>
/// 按钮上的图标。原模组把图标统一缩进 24x24 再画；一个图标由多层叠出来时
/// （Utilities.StackResizeImage，比如“武器”是剑 + 法杖 + 手里剑）是斜着一层层往右下叠的，
/// 这里照同一个几何画。分类栏里每个按钮的图标都对齐原模组 SharedUI.SetupSortsAndCategories
/// 里那张表 —— 那是原模组真正的图标来源，对不上就是因为以前这里是随手挑的物品。
/// </summary>
internal sealed class CatIcon
{
    public readonly IconLayer[] Layers;

    private CatIcon(IconLayer[] layers) { Layers = layers; }

    public bool Empty => Layers == null || Layers.Length == 0;

    public static readonly CatIcon None = new CatIcon(new IconLayer[0]);

    public static CatIcon Item(int type) { return new CatIcon(new IconLayer[] { IconLayer.OfItem(type) }); }

    /// <summary>模组自带贴图（assets\*.rawimg，按文件名取）。</summary>
    public static CatIcon Tex(string tex) { return new CatIcon(new IconLayer[] { IconLayer.OfTex(tex) }); }

    /// <summary>原版贴图，例如 "Images/UI/Craft_Toggle_2"。</summary>
    public static CatIcon Vanilla(string path) { return new CatIcon(new IconLayer[] { IconLayer.OfVanilla(path) }); }

    /// <summary>斜着一叠（原模组的 StackResizeImage）。</summary>
    public static CatIcon Stack(params IconLayer[] layers)
    {
        if (layers == null || layers.Length == 0) return None;
        return new CatIcon(layers);
    }

    /// <summary>几个物品斜着一叠。</summary>
    public static CatIcon Items(params int[] types)
    {
        if (types == null || types.Length == 0) return None;
        IconLayer[] layers = new IconLayer[types.Length];
        for (int i = 0; i < types.Length; i++) layers[i] = IconLayer.OfItem(types[i]);
        return new CatIcon(layers);
    }

    /// <summary>几张模组贴图斜着一叠。</summary>
    public static CatIcon Texes(params string[] texes)
    {
        if (texes == null || texes.Length == 0) return None;
        IconLayer[] layers = new IconLayer[texes.Length];
        for (int i = 0; i < texes.Length; i++) layers[i] = IconLayer.OfTex(texes[i]);
        return new CatIcon(layers);
    }

    // 单个物品类型可以直接当图标写（New/Sub/SortByInt 的 icon 参数就是给它用的）
    public static implicit operator CatIcon(int type) { return Item(type); }
}

/// <summary>
/// 一个排序方式。物品页按物品类型两两比较，配方页按配方索引两两比较
/// （没写 CompareRecipes 的就退回“比产物物品”）。
/// </summary>
internal sealed class CatSort
{
    public string Name;
    public CatIcon Icon;                              // 图标：原模组那张表里的物品或贴图
    public string Tip;
    public Func<int, int, int> CompareItems;          // (物品类型, 物品类型)
    public Func<int, int, int> CompareRecipes;        // (配方索引, 配方索引)
}

/// <summary>
/// 一个筛选开关。Group 非空时同组互斥（比如“时装 / 仅护甲”“实心 / 非实心”）。
/// </summary>
internal sealed class CatFilter
{
    public string Name;
    public CatIcon Icon;
    public string Tip;
    public string Group;
    public bool Selected;
    public Func<int, bool> Items;                     // 物品类型 -> 是否保留
    public Func<int, bool> Recipes;                   // 配方索引 -> 是否保留（null = 用产物物品判断）
}

/// <summary>一个分类（可以带子分类、自己的排序和筛选）。</summary>
internal sealed class CatCategory
{
    public string Key;
    public string Name;
    public CatIcon Icon;
    public string Tip;
    public Func<int, bool> Belongs;                   // 物品类型 -> 是否属于本分类
    public readonly List<CatCategory> Subs = new List<CatCategory>();
    public readonly List<CatSort> Sorts = new List<CatSort>();
    public readonly List<CatFilter> Filters = new List<CatFilter>();
    public CatCategory Parent;

    private bool[] _cache;                            // 按物品类型缓存的归属表
    private bool _warmed;                             // 归属表算过没有

    public bool Includes(int type)
    {
        if (type <= 0) return false;
        if (!_warmed)
        {
            _warmed = true;
            try { Catalogue.WarmCategory(this); }
            catch { }
        }
        try
        {
            if (_cache != null && type < _cache.Length) return _cache[type];
            return Belongs != null && Belongs(type);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>本分类或任意子分类是否收录该物品（“其他”分类判断时用）。</summary>
    public bool IncludesRecursive(int type)
    {
        if (Includes(type)) return true;
        for (int i = 0; i < Subs.Count; i++)
        {
            if (Subs[i].IncludesRecursive(type)) return true;
        }
        return false;
    }

    /// <summary>第一遍查询时把整张归属表算好，之后每帧都是数组查表。</summary>
    public void Warm(int maxType)
    {
        if (Belongs == null) return;
        if (_cache == null || _cache.Length < maxType) _cache = new bool[maxType];
        for (int t = 1; t < maxType; t++)
        {
            try { _cache[t] = Belongs(t); }
            catch { _cache[t] = false; }
        }
    }
}

/// <summary>
/// 配方页/物品页共用的“分类 + 排序 + 筛选”数据。
/// 分类表照搬原模组 RecipeBrowser 的 RecipeCatalogueFilters（武器/工具/盔甲/物块/饰品/药水/
/// Boss召唤物/摸彩袋/钓鱼……），名称用原模组简中本地化的词；判定条件用的都是原版 Item 字段和
/// ItemID.Sets，和原模组是同一套数据来源。
/// </summary>
internal static class Catalogue
{
    public static readonly List<CatCategory> Categories = new List<CatCategory>();
    public static readonly List<CatSort> Sorts = new List<CatSort>();
    public static readonly List<CatFilter> Filters = new List<CatFilter>();

    public static CatCategory Selected;      // 当前选中的分类（可能是子分类）
    public static CatSort Sort;              // 当前排序

    private static bool _built;
    private static readonly List<CatSort> _sortScratch = new List<CatSort>();
    private static readonly List<CatFilter> _filterScratch = new List<CatFilter>();
    private static readonly Dictionary<int, Item> _samples = new Dictionary<int, Item>();
    private static HashSet<int> _craftTiles;

    public static bool Ready => _built && Categories.Count > 0;

    // ================= 构建 =================

    public static void Ensure()
    {
        if (_built) return;
        _built = true;
        try
        {
            Build();
        }
        catch
        {
        }
    }

    private static void Build()
    {
        Categories.Clear();
        Sorts.Clear();
        Filters.Clear();

        // ---------------- 全局排序 ----------------
        // 图标照原模组 SetupSortsAndCategories：前两个用的是原版界面贴图（原模组把 CraftToggle[2]、
        // InventorySort[0] 缩到 24x24 当图标），所以以前拿工作台/箱子顶上就是对不上。
        Sorts.Add(new CatSort
        {
            Name = "合成顺序",
            Tip = "和原版配方表一致的顺序",
            Icon = CatIcon.Vanilla("Images/UI/Craft_Toggle_2"),
            CompareItems = delegate (int a, int b) { return RecipeOrder(a).CompareTo(RecipeOrder(b)); },
            // 配方页原模组这里挂的是 recipeSort = 配方索引（也就是原版配方表本身的先后）；
            // 按“产品物品的最早配方”排的只有物品页那一边。
            CompareRecipes = delegate (int a, int b) { return a.CompareTo(b); }
        });
        Sorts.Add(new CatSort
        {
            Name = "默认排序",
            Tip = "原版创意模式物品栏的分组顺序",
            Icon = CatIcon.Vanilla("Images/UI/Sort_0"),
            CompareItems = CreativeCompare
        });
        Sorts.Add(new CatSort
        {
            Name = "物品内部ID",
            Tip = "按物品的内部编号排",
            Icon = CatIcon.Tex("sortItemID"),
            CompareItems = delegate (int a, int b) { return a.CompareTo(b); }
        });
        Sorts.Add(new CatSort
        {
            Name = "价值",
            Tip = "按物品价值从高到低",
            Icon = CatIcon.Tex("sortValue"),
            CompareItems = delegate (int a, int b)
            {
                int c = ItemValue(b).CompareTo(ItemValue(a));
                return c != 0 ? c : a.CompareTo(b);
            }
        });
        Sorts.Add(new CatSort
        {
            Name = "按字母顺序",
            Tip = "按名称排",
            Icon = CatIcon.Tex("sortAZ"),
            CompareItems = delegate (int a, int b)
            {
                return string.Compare(BrowserData.ItemName(a), BrowserData.ItemName(b), StringComparison.CurrentCulture);
            }
        });
        Sorts.Add(new CatSort
        {
            Name = "稀有度",
            Tip = "按稀有度从高到低（同稀有度比价值）",
            Icon = ItemID.MetalDetector,          // 原模组用的就是金属探测器（Item 3102）
            CompareItems = delegate (int a, int b)
            {
                int c = Math.Abs(ItemRare(b)).CompareTo(Math.Abs(ItemRare(a)));
                if (c != 0) return c;
                c = ItemValue(b).CompareTo(ItemValue(a));
                return c != 0 ? c : a.CompareTo(b);
            }
        });

        // ---------------- 全局筛选 ----------------
        // 图标同样照原模组：材料＝魔法书、可合成＝铁砧、嵌套合成＝秘银砧。
        Filters.Add(new CatFilter
        {
            Name = "材料",
            Tip = "仅显示材料",
            Icon = ItemID.SpellTome,
            Items = delegate (int t) { Item it = Sample(t); return it != null && it.material; },
            Recipes = delegate (int r) { Item it = Sample(BrowserData.RecipeCreateType(r)); return it != null && it.material; }
        });
        Filters.Add(new CatFilter
        {
            Name = CraftableName,
            Tip = "仅显示以当前背包和制作站就能做出来的东西",
            Icon = ItemID.IronAnvil,
            Items = IsCraftableItem,
            Recipes = IsCraftableRecipe
        });
        // 原模组的“嵌套合成”：手上材料再做几层就能做出来的东西（含直接能做的）。
        // 旅程模式的“未研究”筛选按用户要求去掉了。
        Filters.Add(new CatFilter
        {
            Name = NestedName,
            Tip = "仅显示用手上的材料再做几层就能做出来的东西（含直接能做的）",
            Icon = ItemID.MythrilAnvil,
            Items = IsNestedCraftableItem,
            Recipes = IsNestedCraftableRecipe
        });

        // ---------------- 分类 ----------------
        CatCategory all = new CatCategory
        {
            Key = "All",
            Name = "全部",
            Icon = ItemID.AlphabetStatueA,          // 原模组用的是 A 雕像（Item 2712），不是“全部”两个字
            Tip = "不做分类筛选",
            Belongs = delegate (int t) { return true; }
        };
        Categories.Add(all);

        BuildWeapons();
        BuildTools();
        BuildArmor();
        BuildTiles();
        BuildWallsEtc();
        BuildBossesAndBags();

        // 原模组那条分类栏的先后是写死的，这里照它排一遍，图标顺序才能和原模组一一对上。
        // （没有“盔甲套装”和“大师独有”两项：前者要整套盔甲的特殊界面，后者这个游戏版本里
        //   的 Item 没有 master 标记，认不出来。）
        OrderCategories();

        Selected = Categories[0];
        Sort = Sorts[0];
    }

    // 分类栏第一排的先后，照原模组 SetupSortsAndCategories 里 obj 的添加顺序
    private static readonly string[] CategoryOrder =
    {
        "All", "Weapons", "Tools", "Armor", "Tiles", "Walls", "Accessories", "Ammo",
        "Potions", "Expert", "Pets", "Mounts", "Hooks", "Dyes", "BossSummons",
        "Consumables", "GrabBags", "Fishing", "Extractinator", "Other"
    };

    /// <summary>把分类按原模组的先后重排；表里没提到的分类接在后面，不会丢。</summary>
    private static void OrderCategories()
    {
        List<CatCategory> ordered = new List<CatCategory>(Categories.Count);
        for (int i = 0; i < CategoryOrder.Length; i++)
        {
            for (int j = 0; j < Categories.Count; j++)
            {
                if (Categories[j].Key == CategoryOrder[i])
                {
                    ordered.Add(Categories[j]);
                    break;
                }
            }
        }
        for (int j = 0; j < Categories.Count; j++)
        {
            if (!ordered.Contains(Categories[j])) ordered.Add(Categories[j]);
        }
        Categories.Clear();
        Categories.AddRange(ordered);
    }

    // ---------------- 武器 ----------------

    private static void BuildWeapons()
    {
        // 原模组的“武器”是剑 + 法杖 + 手里剑叠出来的（StackResizeImage）
        CatCategory weapons = New("Weapons", "武器",
            CatIcon.Items(ItemID.GoldBroadsword, ItemID.GoldenShower, ItemID.Shuriken), "近战、远程、魔法、召唤类武器",
            delegate (int t)
            {
                Item it = Sample(t);
                if (it == null || it.damage <= 0) return false;
                return it.melee || it.magic || it.ranged || it.summon || IsWhip(t);
            });
        weapons.Sorts.Add(SortByInt("伤害", CatIcon.Tex("sortDamage"), "按武器伤害从高到低",
            delegate (Item it) { return it.damage; }));
        weapons.Sorts.Add(new CatSort
        {
            Name = "使用弹药类型",
            Tip = "按武器使用的弹药类型分组",
            Icon = CatIcon.Tex("sortAmmo"),
            CompareItems = delegate (int a, int b)
            {
                Item ia = Sample(a);
                Item ib = Sample(b);
                int va = ia == null ? 0 : ia.useAmmo;
                int vb = ib == null ? 0 : ib.useAmmo;
                return va.CompareTo(vb);
            }
        });
        weapons.Subs.Add(Sub(weapons, "Melee", "近战", ItemID.GoldBroadsword, "剑、长矛之类",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.melee && it.damage > 0 && it.pick <= 0 && it.axe <= 0 && it.hammer <= 0;
            }));
        // 原模组的悠悠球图标每次启动从悠悠球里随机挑一个，这里固定用木悠悠球
        weapons.Subs.Add(Sub(weapons, "Yoyo", "悠悠球", ItemID.WoodYoyo, "悠悠球",
            delegate (int t) { return Set(ItemID.Sets.Yoyo, t); }));
        weapons.Subs.Add(Sub(weapons, "Magic", "魔法", ItemID.GoldenShower, "魔法武器",
            delegate (int t) { Item it = Sample(t); return it != null && it.magic; }));
        weapons.Subs.Add(Sub(weapons, "Ranged", "远程", ItemID.FlintlockPistol, "弓、枪之类",
            delegate (int t) { Item it = Sample(t); return it != null && it.ranged; }));
        // 原模组的“投掷”。1.4 把投掷并进了远程（原来那个 Item.thrown 字段已经没了），
        // 所以这里退一步按“远程武器但不吃弹药”来认 —— 手雷、飞刀、标枪这些。
        weapons.Subs.Add(Sub(weapons, "Throwing", "投掷", ItemID.Shuriken, "手雷、飞刀这类直接扔出去的东西",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.ranged && it.useAmmo == 0 && it.shoot > 0 && it.damage > 0;
            }));
        weapons.Subs.Add(Sub(weapons, "Summon", "仆从", ItemID.SlimeStaff, "召唤仆从的武器（不含鞭子、哨兵）",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.summon && !it.sentry && !IsWhip(t);
            }));
        weapons.Subs.Add(Sub(weapons, "Whip", "鞭子", ItemID.BlandWhip, "鞭类召唤武器",
            delegate (int t) { return IsWhip(t); }));
        weapons.Subs.Add(Sub(weapons, "Sentry", "哨兵", ItemID.DD2LightningAuraT1Popper, "能放置的哨兵召唤物",
            delegate (int t) { Item it = Sample(t); return it != null && it.sentry; }));
        Categories.Add(weapons);
    }

    // ---------------- 工具 ----------------

    private static void BuildTools()
    {
        // 原模组的“工具”是镐 + 斧 + 锤三张图标叠出来的
        CatCategory tools = New("Tools", "工具", CatIcon.Texes("sortPick", "sortAxe", "sortHammer"), "镐、斧、锤",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && (it.pick > 0 || it.axe > 0 || it.hammer > 0);
            });
        CatCategory picks = Sub(tools, "Pickaxes", "镐", CatIcon.Tex("sortPick"), "镐类工具",
            delegate (int t) { Item it = Sample(t); return it != null && it.pick > 0; });
        picks.Sorts.Add(SortByInt("镐力", CatIcon.Tex("sortPick"), "按镐力从高到低",
            delegate (Item it) { return it.pick; }));
        CatCategory axes = Sub(tools, "Axes", "斧", CatIcon.Tex("sortAxe"), "斧类工具",
            delegate (int t) { Item it = Sample(t); return it != null && it.axe > 0; });
        axes.Sorts.Add(SortByInt("斧力", CatIcon.Tex("sortAxe"), "按斧力从高到低",
            delegate (Item it) { return it.axe; }));
        CatCategory hammers = Sub(tools, "Hammers", "锤", CatIcon.Tex("sortHammer"), "锤类工具",
            delegate (int t) { Item it = Sample(t); return it != null && it.hammer > 0; });
        hammers.Sorts.Add(SortByInt("锤力", CatIcon.Tex("sortHammer"), "按锤力从高到低",
            delegate (Item it) { return it.hammer; }));
        tools.Subs.Add(picks);
        tools.Subs.Add(axes);
        tools.Subs.Add(hammers);
        Categories.Add(tools);
    }

    // ---------------- 盔甲和时装 ----------------

    private static void BuildArmor()
    {
        // 原模组的“盔甲”是头 + 身 + 腿三件银盔甲叠出来的
        CatCategory armor = New("Armor", "盔甲和时装",
            CatIcon.Items(ItemID.SilverHelmet, ItemID.SilverChainmail, ItemID.SilverGreaves), "头部、躯干、护腿（含时装）",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && (it.headSlot >= 0 || it.bodySlot >= 0 || it.legSlot >= 0);
            });
        armor.Filters.Add(new CatFilter
        {
            Name = "时装",
            Tip = "仅显示时装（社交栏用的）",
            Icon = ItemID.BunnyHood,               // 原模组用的就是兔耳帽（Item 243）
            Group = "armor",
            Items = delegate (int t) { Item it = Sample(t); return it != null && it.vanity; }
        });
        armor.Filters.Add(new CatFilter
        {
            Name = "护甲",
            Tip = "仅显示有防御力的护甲",
            Icon = ItemID.GoldHelmet,              // 原模组用的是金头盔（Item 92）
            Group = "armor",
            Items = delegate (int t) { Item it = Sample(t); return it != null && !it.vanity; }
        });
        armor.Sorts.Add(SortByInt("防御", CatIcon.Tex("sortDefense"), "按防御力从高到低",
            delegate (Item it) { return it.defense; }));
        armor.Subs.Add(Sub(armor, "Head", "头部", ItemID.SilverHelmet, "头盔",
            delegate (int t) { Item it = Sample(t); return it != null && it.headSlot >= 0; }));
        armor.Subs.Add(Sub(armor, "Body", "躯干", ItemID.SilverChainmail, "胸甲",
            delegate (int t) { Item it = Sample(t); return it != null && it.bodySlot >= 0; }));
        armor.Subs.Add(Sub(armor, "Legs", "护腿", ItemID.SilverGreaves, "护腿",
            delegate (int t) { Item it = Sample(t); return it != null && it.legSlot >= 0; }));
        Categories.Add(armor);
    }

    // ---------------- 物块 ----------------

    private static void BuildTiles()
    {
        CatCategory tiles = New("Tiles", "物块", ItemID.Sign, "能放置成物块的物品",              // 原模组用告示牌（Item 171）
            delegate (int t) { Item it = Sample(t); return it != null && it.createTile >= 0; });
        tiles.Filters.Add(new CatFilter
        {
            Name = "实心块",
            Tip = "仅显示实心物块",
            Icon = ItemID.ActiveStoneBlock,
            Group = "tilesolid",
            Items = IsSolidTile
        });
        tiles.Filters.Add(new CatFilter
        {
            Name = "非实心",
            Tip = "仅显示家具之类的非实心物块",
            Icon = ItemID.InactiveStoneBlock,
            Group = "tilesolid",
            Items = IsNonSolidTile
        });
        tiles.Sorts.Add(new CatSort
        {
            Name = "对应放置的物块ID",
            Tip = "按放置出来的物块编号排",
            Icon = CatIcon.Items(ItemID.Candelabra, ItemID.GrandfatherClock),   // 原模组是烛台 + 落地钟叠出来的
            CompareItems = delegate (int a, int b)
            {
                Item ia = Sample(a);
                Item ib = Sample(b);
                int ta = ia == null ? -1 : ia.createTile;
                int tb = ib == null ? -1 : ib.createTile;
                int c = ta.CompareTo(tb);
                if (c != 0) return c;
                int pa = ia == null ? 0 : ia.placeStyle;
                int pb = ib == null ? 0 : ib.placeStyle;
                return pa.CompareTo(pb);
            }
        });
        tiles.Subs.Add(Sub(tiles, "CraftingStations", "合成站", ItemID.IronAnvil, "能用到的制作站",
            CraftingStation));
        tiles.Subs.Add(Sub(tiles, "Furniture", "家具", ItemID.Bookcase, "能放置的家具",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.createTile >= 0 && it.createTile < Main.tileFrameImportant.Length
                    && Main.tileFrameImportant[it.createTile];
            }));
        tiles.Subs.Add(Sub(tiles, "Blocks", "实心块", ItemID.DirtBlock, "实心物块", IsSolidTile));
        tiles.Subs.Add(Sub(tiles, "Containers", "箱子", ItemID.GoldChest, "能装东西的箱子",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.createTile >= 0 && it.createTile < Main.tileContainer.Length
                    && Main.tileContainer[it.createTile];
            }));
        tiles.Subs.Add(Sub(tiles, "Wiring", "电路", ItemID.Wire, "电线、开关、机关",
            delegate (int t) { return SetInt(ItemID.Sets.SortingPriorityWiring, t); }));
        tiles.Subs.Add(Sub(tiles, "Statues", "雕像", ItemID.HeartStatue, "雕像",
            delegate (int t) { return NameHas(t, "Statue", "雕像"); }));
        tiles.Subs.Add(Sub(tiles, "Doors", "门", ItemID.WoodenDoor, "门",
            delegate (int t) { Item it = Sample(t); return it != null && it.createTile == TileID.ClosedDoor; }));
        // 1.4 里管“能不能当椅子”的 RoomNeeds 表已经没了，只能按名字认（和上面“雕像”同一个办法）
        tiles.Subs.Add(Sub(tiles, "Chairs", "椅子", ItemID.WoodenChair, "椅子",
            delegate (int t) { return NameHas(t, "Chair", "椅"); }));
        tiles.Subs.Add(Sub(tiles, "Tables", "桌子", ItemID.PalmWoodTable, "桌子",
            delegate (int t)
            {
                Item it = Sample(t);
                if (it == null || it.createTile < 0) return false;
                if (it.createTile < Main.tileTable.Length && Main.tileTable[it.createTile]) return true;
                return NameHas(t, "Table", "桌");
            }));
        tiles.Subs.Add(Sub(tiles, "LightSources", "照明", ItemID.ChineseLantern, "会发光的物块",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.createTile >= 0 && it.createTile < Main.tileLighted.Length
                    && Main.tileLighted[it.createTile];
            }));
        tiles.Subs.Add(Sub(tiles, "Torches", "火把", ItemID.RainbowTorch, "火把",
            delegate (int t) { return Set(ItemID.Sets.Torches, t); }));
        Categories.Add(tiles);
    }

    // ---------------- 背景墙 / 饰品 / 弹药 / 药水 ----------------

    private static void BuildWallsEtc()
    {
        Categories.Add(New("Walls", "背景墙", ItemID.PearlstoneBrickWall, "能放置成背景墙的物品",
            delegate (int t) { Item it = Sample(t); return it != null && it.createWall >= 0; }));

        CatCategory acc = New("Accessories", "饰品", ItemID.HermesBoots, "饰品",           // 原模组用赫尔墨斯靴（Item 54）
            delegate (int t) { Item it = Sample(t); return it != null && it.accessory; });
        acc.Subs.Add(Sub(acc, "Wings", "翅膀", ItemID.LeafWings, "翅膀",
            delegate (int t) { Item it = Sample(t); return it != null && it.wingSlot > 0; }));
        Categories.Add(acc);

        CatCategory ammo = New("Ammo", "弹药", CatIcon.Tex("sortAmmo"), "弓枪用的弹药",
            delegate (int t) { Item it = Sample(t); return it != null && it.ammo > 0; });
        ammo.Sorts.Add(new CatSort
        {
            Name = "弹药类型",
            Tip = "按弹药类型分组",
            Icon = CatIcon.Tex("sortAmmo"),
            CompareItems = delegate (int a, int b)
            {
                Item ia = Sample(a);
                Item ib = Sample(b);
                int va = ia == null ? 0 : ia.ammo;
                int vb = ib == null ? 0 : ib.ammo;
                return va.CompareTo(vb);
            }
        });
        Categories.Add(ammo);

        // 原模组的“药水”是治疗药水 + 魔力药水 + 怒气药水叠出来的
        CatCategory potions = New("Potions", "药水和食物",
            CatIcon.Items(ItemID.HealingPotion, ItemID.ManaPotion, ItemID.RagePotion), "能喝/能吃的东西",
            delegate (int t)
            {
                Item it = Sample(t);
                if (it == null) return false;
                return it.healLife > 0 || it.healMana > 0 || IsFood(t) || IsBuffPotion(it);
            });
        CatCategory heal = Sub(potions, "HealthPotions", "治疗药水", ItemID.HealingPotion, "回血药水",
            delegate (int t) { Item it = Sample(t); return it != null && it.healLife > 0; });
        heal.Sorts.Add(SortByInt("治疗量", ItemID.HealingPotion, "按回血量从高到低",
            delegate (Item it) { return it.healLife; }));
        CatCategory mana = Sub(potions, "ManaPotions", "魔力药水", ItemID.ManaPotion, "回魔药水",
            delegate (int t) { Item it = Sample(t); return it != null && it.healMana > 0; });
        mana.Sorts.Add(SortByInt("回魔量", ItemID.ManaPotion, "按回魔量从高到低",
            delegate (Item it) { return it.healMana; }));
        potions.Subs.Add(heal);
        potions.Subs.Add(mana);
        potions.Subs.Add(Sub(potions, "BuffPotions", "实用药水", ItemID.RagePotion, "加状态的药水",
            delegate (int t) { Item it = Sample(t); return it != null && IsBuffPotion(it); }));
        potions.Subs.Add(Sub(potions, "Food", "食物", CatIcon.Tex("sortFood"), "食物（吃出饱食 buff）",
            delegate (int t) { return IsFood(t); }));
        Categories.Add(potions);

        Categories.Add(New("Expert", "专家独有", ItemID.EoCShield, "专家模式专属物品",         // 原模组用克苏鲁之盾（Item 3097）
            delegate (int t) { Item it = Sample(t); return it != null && (it.expert || it.expertOnly); }));

        CatCategory pets = New("Pets", "宠物", CatIcon.Items(ItemID.ZephyrFish, ItemID.FairyBell), "宠物和照明宠物",
            delegate (int t) { return IsPet(t) || IsLightPet(t); });
        pets.Subs.Add(Sub(pets, "CommonPets", "宠物", ItemID.ZephyrFish, "普通宠物",
            delegate (int t) { return IsPet(t); }));
        pets.Subs.Add(Sub(pets, "LightPets", "照明宠物", ItemID.FairyBell, "照明宠物",
            delegate (int t) { return IsLightPet(t); }));
        Categories.Add(pets);

        // 原模组把“矿车”挂在“坐骑”底下当子分类，这里照做（分类栏第一排就不会多出一个矿车按钮）
        CatCategory mounts = New("Mounts", "坐骑", ItemID.SlimySaddle, "坐骑召唤物（不含矿车）",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.mountType > 0 && !IsCart(it.mountType);
            });
        mounts.Subs.Add(Sub(mounts, "Carts", "矿车", ItemID.Minecart, "矿车",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.mountType > 0 && IsCart(it.mountType);
            }));
        Categories.Add(mounts);

        Categories.Add(New("Hooks", "抓钩", ItemID.AmethystHook, "抓钩",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && it.shoot > 0 && it.shoot < Main.projHook.Length && Main.projHook[it.shoot];
            }));

        CatCategory dyes = New("Dyes", "染料", CatIcon.Items(ItemID.OrangeDye, ItemID.BiomeHairDye), "染料和染发剂",
            delegate (int t)
            {
                Item it = Sample(t);
                return it != null && (it.dye > 0 || it.hairDye > 0);
            });
        dyes.Subs.Add(Sub(dyes, "CommonDyes", "染料", ItemID.OrangeDye, "染料",
            delegate (int t) { Item it = Sample(t); return it != null && it.dye > 0; }));
        dyes.Subs.Add(Sub(dyes, "HairDyes", "染发剂", ItemID.BiomeHairDye, "染发剂",
            delegate (int t) { Item it = Sample(t); return it != null && it.hairDye > 0; }));
        Categories.Add(dyes);

        CatCategory cons = New("Consumables", "消耗品", ItemID.PurificationPowder, "用掉就没的东西",
            delegate (int t)
            {
                Item it = Sample(t);
                if (it == null || !it.consumable) return false;
                if (it.createTile >= 0 || it.createWall >= 0) return false;
                if (it.ammo > 0 || it.notAmmo) return false;
                if (it.accessory || it.headSlot >= 0 || it.bodySlot >= 0 || it.legSlot >= 0) return false;
                return true;
            });
        cons.Subs.Add(Sub(cons, "CapturedNPC", "捕获的生物", ItemID.GoldBunny, "虫网抓到的生物",   // 原模组用金兔（Item 2890）
            delegate (int t) { Item it = Sample(t); return it != null && it.makeNPC > 0; }));
        Categories.Add(cons);

        // 原模组的“钓鱼”是钓竿 + 诱饵图标 + 任务鱼叠出来的
        CatCategory fish = New("Fishing", "钓鱼",
            CatIcon.Stack(IconLayer.OfTex("sortFish"), IconLayer.OfTex("sortBait"), IconLayer.OfItem(ItemID.FallenStarfish)),
            "钓竿、诱饵、浮标、任务鱼",
            delegate (int t)
            {
                Item it = Sample(t);
                if (it == null) return false;
                return it.fishingPole > 0 || it.bait > 0 || it.questItem || IsBobber(t);
            });
        CatCategory poles = Sub(fish, "Poles", "钓竿", CatIcon.Tex("sortFish"), "钓竿",
            delegate (int t) { Item it = Sample(t); return it != null && it.fishingPole > 0; });
        poles.Sorts.Add(SortByInt("渔力", CatIcon.Tex("sortFish"), "按渔力从高到低",
            delegate (Item it) { return it.fishingPole; }));
        CatCategory bait = Sub(fish, "Bait", "诱饵", CatIcon.Tex("sortBait"), "鱼饵",
            delegate (int t) { Item it = Sample(t); return it != null && it.bait > 0; });
        bait.Sorts.Add(SortByInt("鱼饵力", CatIcon.Tex("sortBait"), "按鱼饵力从高到低",
            delegate (Item it) { return it.bait; }));
        fish.Subs.Add(poles);
        fish.Subs.Add(bait);
        fish.Subs.Add(Sub(fish, "Bobbers", "浮标", ItemID.FishingBobber, "浮标",
            delegate (int t) { return IsBobber(t); }));
        fish.Subs.Add(Sub(fish, "QuestFish", "任务鱼", ItemID.FallenStarfish, "渔夫要的任务鱼",    // 原模组用落星鱼（Item 2458）
            delegate (int t) { Item it = Sample(t); return it != null && it.questItem; }));
        Categories.Add(fish);

        Categories.Add(New("Extractinator", "提炼机", ItemID.Extractinator, "能放进提炼机的物品",
            delegate (int t)
            {
                return SetInt(ItemID.Sets.ExtractinatorMode, t) || Set(ItemID.Sets.CanBeExtractinated, t);
            }));

        Categories.Add(New("Other", "其他", ItemID.UnicornonaStick, "不属于以上任何分类的物品",   // 原模组用独角兽棍（Item 856）
            delegate (int t) { return !BelongsInOther(t); }));
    }

    // ---------------- Boss 召唤物 / 摸彩袋 ----------------

    private static void BuildBossesAndBags()
    {
        CatCategory bosses = New("BossSummons", "Boss召唤物", ItemID.MechanicalSkull, "召唤 Boss 用的东西",   // 原模组用机械骷髅头（Item 557）
            delegate (int t) { return BossSummonOrder(t) >= 0; });
        bosses.Sorts.Add(new CatSort
        {
            Name = "进程顺序",
            Tip = "按游戏进程从早到晚排",
            Icon = CatIcon.Tex("sortDamage"),   // 原模组这里就是拿“伤害”那张贴图当图标
            CompareItems = delegate (int a, int b)
            {
                int oa = BossSummonOrder(a);
                int ob = BossSummonOrder(b);
                if (oa < 0) oa = int.MaxValue;
                if (ob < 0) ob = int.MaxValue;
                int c = oa.CompareTo(ob);
                return c != 0 ? c : a.CompareTo(b);
            }
        });
        Categories.Add(bosses);

        CatCategory bags = New("GrabBags", "摸彩袋", ItemID.KingSlimeBossBag, "右键能开的东西",   // 原模组用史莱姆王宝藏袋（Item 3318）
            delegate (int t) { return Set(ItemID.Sets.OpenableBag, t) || Set(ItemID.Sets.BossBag, t); });
        bags.Subs.Add(Sub(bags, "FishingCrate", "宝匣（困难前）", ItemID.WoodenCrate, "困难模式前的宝匣",   // 原模组用木匣（Item 2334）
            delegate (int t) { return Set(ItemID.Sets.IsFishingCrate, t); }));
        bags.Subs.Add(Sub(bags, "FishingCrateHardmode", "宝匣（困难）", ItemID.WoodenCrateHard, "困难模式的宝匣",   // 原模组用困难木匣（Item 3979）
            delegate (int t) { return Set(ItemID.Sets.IsFishingCrateHardmode, t); }));
        bags.Subs.Add(Sub(bags, "BossBag", "宝藏袋", ItemID.EyeOfCthulhuBossBag, "Boss 掉的宝藏袋",   // 原模组用克苏鲁之眼宝藏袋（Item 3319）
            delegate (int t) { return Set(ItemID.Sets.BossBag, t); }));
        // 原模组把这类东西放在摸彩袋的“其他”里，用的是草药袋图标（Item 3093）
        bags.Subs.Add(Sub(bags, "OpenableBag", "可开启袋", ItemID.HerbBag, "其它能右键开的东西",
            delegate (int t) { return Set(ItemID.Sets.OpenableBag, t); }));
        Categories.Add(bags);
    }

    // ================= 查询接口 =================

    /// <summary>某物品是不是属于“其他”（跳过“全部”和“其他”自己）。</summary>
    private static bool BelongsInOther(int type)
    {
        for (int i = 1; i < Categories.Count - 1; i++)
        {
            if (Categories[i].IncludesRecursive(type)) return true;
        }
        return false;
    }

    public static bool Belongs(CatCategory c, int type)
    {
        if (c == null) return true;
        return c.IncludesRecursive(type);
    }

    /// <summary>当前分类可用的排序：全局 + 自己 + 各级父分类的。</summary>
    public static List<CatSort> SortsFor(CatCategory c)
    {
        _sortScratch.Clear();
        _sortScratch.AddRange(Sorts);
        for (CatCategory k = c; k != null; k = k.Parent)
        {
            for (int i = 0; i < k.Sorts.Count; i++)
            {
                if (!_sortScratch.Contains(k.Sorts[i])) _sortScratch.Add(k.Sorts[i]);
            }
        }
        return _sortScratch;
    }

    /// <summary>当前分类可用的筛选：全局 + 自己 + 各级父分类的。</summary>
    public static List<CatFilter> FiltersFor(CatCategory c)
    {
        _filterScratch.Clear();
        _filterScratch.AddRange(Filters);
        for (CatCategory k = c; k != null; k = k.Parent)
        {
            for (int i = 0; i < k.Filters.Count; i++)
            {
                if (!_filterScratch.Contains(k.Filters[i])) _filterScratch.Add(k.Filters[i]);
            }
        }
        return _filterScratch;
    }

    /// <summary>切换某个筛选。同组的其它筛选会被关掉（原版那种互斥筛选）。</summary>
    public static void Toggle(CatFilter f)
    {
        if (f == null) return;
        f.Selected = !f.Selected;
        if (!f.Selected || string.IsNullOrEmpty(f.Group)) return;
        List<CatFilter> all = AllFilters();
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != f && all[i].Group == f.Group) all[i].Selected = false;
        }
    }

    private static List<CatFilter> _allFilters;

    /// <summary>所有分类上挂过的筛选（互斥判断和筛选应用都要用全表）。</summary>
    private static List<CatFilter> AllFilters()
    {
        if (_allFilters != null) return _allFilters;
        List<CatFilter> list = new List<CatFilter>();
        list.AddRange(Filters);
        for (int i = 0; i < Categories.Count; i++) Collect(Categories[i], list);
        _allFilters = list;
        return list;
    }

    private static void Collect(CatCategory c, List<CatFilter> list)
    {
        for (int i = 0; i < c.Filters.Count; i++) list.Add(c.Filters[i]);
        for (int i = 0; i < c.Subs.Count; i++) Collect(c.Subs[i], list);
    }

    /// <summary>物品是否通过全部已勾选的筛选。</summary>
    public static bool PassFilters(int type)
    {
        List<CatFilter> all = AllFilters();
        for (int i = 0; i < all.Count; i++)
        {
            CatFilter f = all[i];
            if (!f.Selected || f.Items == null) continue;
            try { if (!f.Items(type)) return false; }
            catch { }
        }
        return true;
    }

    /// <summary>配方是否通过全部已勾选的筛选。</summary>
    public static bool PassRecipeFilters(int recipeIndex)
    {
        List<CatFilter> all = AllFilters();
        int result = BrowserData.RecipeCreateType(recipeIndex);
        for (int i = 0; i < all.Count; i++)
        {
            CatFilter f = all[i];
            if (!f.Selected) continue;
            try
            {
                if (f.Recipes != null) { if (!f.Recipes(recipeIndex)) return false; }
                else if (f.Items != null && !f.Items(result)) return false;
            }
            catch { }
        }
        return true;
    }

    /// <summary>按当前排序方式排一份“物品类型”列表。</summary>
    public static void SortItems(List<int> list)
    {
        CatSort s = Sort;
        if (s == null || s.CompareItems == null) return;
        try { list.Sort(new Comparison<int>(s.CompareItems)); } catch { }
    }

    /// <summary>按当前排序方式排一份“配方索引”列表。</summary>
    public static void SortRecipes(List<int> list)
    {
        CatSort s = Sort;
        if (s == null) return;
        try
        {
            if (s.CompareRecipes != null) list.Sort(new Comparison<int>(s.CompareRecipes));
            else if (s.CompareItems != null)
                list.Sort(delegate (int a, int b)
                {
                    return s.CompareItems(BrowserData.RecipeCreateType(a), BrowserData.RecipeCreateType(b));
                });
        }
        catch
        {
        }
    }

    /// <summary>
    /// 换分类。顺手把新分类里没有的排序/筛选换掉 —— 否则会留下一个当前栏里看不见、
    /// 却还在生效的筛选，网格莫名其妙变空，玩家根本找不到原因。
    /// </summary>
    public static void SelectCategory(CatCategory c)
    {
        if (c == null) return;
        Selected = c;
        List<CatSort> sorts = SortsFor(c);
        if (Sort == null || !sorts.Contains(Sort)) Sort = sorts.Count > 0 ? sorts[0] : null;
        List<CatFilter> mine = FiltersFor(c);
        List<CatFilter> all = AllFilters();
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Selected && !mine.Contains(all[i])) all[i].Selected = false;
        }
    }

    // ================= 可合成 / 嵌套合成 =================

    private static HashSet<int> _craftable;
    private static int _craftStamp = int.MinValue;
    private static CatFilter _craftableFilter;
    private static CatFilter _nestedFilter;

    // 这次游戏里贴近过的制作站（原模组用 RecipeBrowserPlayer.seenTiles 记录“见过的站台”）
    private static readonly HashSet<int> _seenTiles = new HashSet<int>();

    // “可合成的”那个筛选的名字：物品页的“可制作”勾选和分类栏里这个按钮共用同一个开关
    private const string CraftableName = "可合成的";

    // 原模组“嵌套合成”那个按钮的名字（原模组的提示里说它计算量大，是异步算的）
    private const string NestedName = "嵌套合成";

    /// <summary>
    /// 跟着原版“现在能做”的配方表刷新（背包/制作站一变就重算）。
    /// 返回 true 表示这份表变了 —— 勾着依赖它的筛选时，网格要跟着重排一次。
    /// </summary>
    public static bool RefreshCraftable()
    {
        int n = 0;
        int stamp = 17;
        try
        {
            n = Main.numAvailableRecipes;
            stamp = n;
            for (int i = 0; i < n; i++) stamp = stamp * 31 + Main.availableRecipe[i];
        }
        catch
        {
        }
        if (stamp == _craftStamp && _craftable != null) return false;
        _craftStamp = stamp;
        HashSet<int> set = new HashSet<int>();
        try
        {
            for (int i = 0; i < n; i++) set.Add(Main.availableRecipe[i]);
        }
        catch
        {
        }
        _craftable = set;
        return true;
    }

    /// <summary>
    /// 记下玩家贴近过的制作站。原模组展开嵌套合成时会丢掉“需要还没见过的站台”的路径
    /// （RecipePath.GetCraftPaths 里 allowMissingStations=false 那段），我们照做，只是没有存档，
    /// 只记这次游戏里贴近过的。adjTile 里已经包含“这个站台算作那个站台”的继承关系
    /// （原版 Player.SetAdjTile 会顺着 Recipe.TileCountsAs 递归设置），所以直接记下来就行。
    /// </summary>
    public static void NoteNearbyTiles(bool refresh)
    {
        try
        {
            Player p = Main.LocalPlayer;
            if (p == null) return;
            if (refresh) p.AdjTiles();
            bool[] adj = p.adjTile;
            if (adj == null) return;
            for (int t = 0; t < adj.Length; t++)
            {
                if (adj[t]) _seenTiles.Add(t);
            }
        }
        catch
        {
        }
    }

    public static bool IsCraftableRecipe(int recipeIndex)
    {
        try { return _craftable != null && _craftable.Contains(recipeIndex); }
        catch { return false; }
    }

    public static bool IsCraftableItem(int type)
    {
        if (_craftable == null || type <= 0) return false;
        List<int> recipes = BrowserData.RecipesForItem(type);
        for (int i = 0; i < recipes.Count; i++)
        {
            if (_craftable.Contains(recipes[i])) return true;
        }
        return false;
    }

    // ---------------- 嵌套合成（原模组的“可嵌套合成”，配方页的黄底） ----------------

    private static HashSet<int> _nestedCraftable;
    private static int _nestedStamp = int.MinValue;

    /// <summary>
    /// 背包 + 见过的站台的指纹。嵌套合成只看这两样，所以只有它们变了才需要重算 ——
    /// 否则站在制作站旁边走来走去（原版每帧都会重算 availableRecipe）就会一直重算，白烧帧。
    /// </summary>
    private static int InventoryStamp()
    {
        int stamp = _seenTiles.Count * 131 + 7;
        try
        {
            Player p = Main.LocalPlayer;
            if (p != null && p.inventory != null)
            {
                for (int i = 0; i < p.inventory.Length; i++)
                {
                    Item it = p.inventory[i];
                    if (it == null) continue;
                    stamp = stamp * 31 + it.type;
                    stamp = stamp * 31 + it.stack;
                }
            }
        }
        catch
        {
        }
        return stamp;
    }

    /// <summary>
    /// 这条配方用手上的材料再做几层之后能不能做出来。直接的“现在能做”也算（原模组的
    /// ObtainableFilter 就是这个语义，绿底只是因为优先级更高把它盖住了）。
    /// </summary>
    public static bool IsNestedCraftableRecipe(int recipeIndex)
    {
        EnsureNested();
        try { return _nestedCraftable != null && _nestedCraftable.Contains(recipeIndex); }
        catch { return false; }
    }

    public static bool IsNestedCraftableItem(int type)
    {
        if (type <= 0) return false;
        List<int> recipes = BrowserData.RecipesForItem(type);
        for (int i = 0; i < recipes.Count; i++)
        {
            if (IsNestedCraftableRecipe(recipes[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// 算一遍“嵌套合成”。
    /// 原模组是异步的完整搜索（RecipePath.GetCraftPaths，界面上也提醒过它计算量大、会自动禁用），
    /// 这里用一个够便宜也够准的近似：从背包出发反复迭代，算出每种物品“最多能凑出多少个”
    /// （做出来的东西又能当下一层的材料），最后按这张表看每条配方的材料凑不凑得齐。
    /// 材料分摊只有在好几条配方抢同一份材料时才会算得宽松一点，实战里基本看不出来。
    /// 需要没见过的制作站的配方直接跳过，免得把一堆现阶段根本做不了的东西标成黄的。
    /// </summary>
    private static void EnsureNested()
    {
        int stamp = InventoryStamp();
        if (_nestedCraftable != null && _nestedStamp == stamp) return;
        _nestedStamp = stamp;
        HashSet<int> result = new HashSet<int>();
        try
        {
            int count = BrowserData.RecipeCount;
            if (count <= 0) { _nestedCraftable = result; return; }

            Dictionary<int, int> have = new Dictionary<int, int>();
            Player p = Main.LocalPlayer;
            if (p != null && p.inventory != null)
            {
                for (int i = 0; i < p.inventory.Length; i++)
                {
                    Item it = p.inventory[i];
                    if (it == null || it.type <= 0 || it.stack <= 0) continue;
                    have.TryGetValue(it.type, out int had);
                    have[it.type] = had + it.stack;
                }
            }

            // 迭代：这一轮新做出来的东西，下一轮就能当材料用
            for (int pass = 0; pass < 20; pass++)
            {
                bool changed = false;
                for (int r = 0; r < count; r++)
                {
                    if (!StationKnown(BrowserData.RecipeStation(r))) continue;
                    int make = BrowserData.RecipeCreateType(r);
                    if (make <= 0) continue;
                    int crafts = NestedCrafts(r, have);
                    if (crafts <= 0) continue;
                    int gain = crafts * RecipeStack(r);
                    // 这个数字只用来判断“够不够”，封个顶：万一有配方能互相来回加工，别让它滚成天文数字
                    if (gain > MaxHave) gain = MaxHave;
                    have.TryGetValue(make, out int had);
                    if (had >= gain) continue;
                    have[make] = gain;
                    changed = true;
                }
                if (!changed) break;
            }

            for (int r = 0; r < count; r++)
            {
                if (!StationKnown(BrowserData.RecipeStation(r))) continue;
                if (NestedCrafts(r, have) > 0) result.Add(r);
            }
        }
        catch
        {
        }
        _nestedCraftable = result;
    }

    // “能凑出多少个”的上限：只是拿来比大小的，不用真的算出天文数字
    private const int MaxHave = 9999;

    /// <summary>这条配方需要一个还没见过的制作站吗（徒手不需要站台）。</summary>
    private static bool StationKnown(int requiredTile)
    {
        if (requiredTile < 0) return true;
        return _seenTiles.Contains(requiredTile);
    }

    /// <summary>这条配方一次做出几个。</summary>
    private static int RecipeStack(int recipeIndex)
    {
        try
        {
            Recipe r = Main.recipe[recipeIndex];
            if (r != null && r.createItem != null) return Math.Max(1, r.createItem.stack);
        }
        catch
        {
        }
        return 1;
    }

    /// <summary>按“每种物品能凑出多少”这张表，这条配方最多能做几次（0 = 材料凑不齐）。</summary>
    private static int NestedCrafts(int recipeIndex, Dictionary<int, int> have)
    {
        List<Ingredient> ings = BrowserData.IngredientsOf(recipeIndex);
        if (ings.Count == 0) return 0;
        int crafts = int.MaxValue;
        for (int i = 0; i < ings.Count; i++)
        {
            Ingredient ing = ings[i];
            int n = IngredientHave(ing, have) / Math.Max(1, ing.Stack);
            if (n < crafts) crafts = n;
            if (crafts <= 0) return 0;
        }
        return crafts == int.MaxValue ? 0 : crafts;
    }

    /// <summary>一种材料能拿出多少（配方组取组里最多的那种）。</summary>
    private static int IngredientHave(Ingredient ing, Dictionary<int, int> have)
    {
        if (ing.GroupId < 0) return HaveCount(have, ing.Type);
        int best = 0;
        try
        {
            RecipeGroup g = RecipeGroup.recipeGroups[ing.GroupId];
            if (g != null && g.ValidItems != null)
            {
                foreach (int v in g.ValidItems)
                {
                    int c = HaveCount(have, v);
                    if (c > best) best = c;
                }
            }
        }
        catch
        {
        }
        return best;
    }

    private static int HaveCount(Dictionary<int, int> have, int type)
    {
        if (type <= 0) return 0;
        return have.TryGetValue(type, out int c) ? c : 0;
    }

    /// <summary>
    /// “可合成的”那个筛选对象。物品页的“可制作”勾选和分类栏里这个按钮共用同一个开关，
    /// 两边的状态永远一致。
    /// </summary>
    public static CatFilter CraftableFilter
    {
        get
        {
            if (_craftableFilter == null) _craftableFilter = FindFilter(CraftableName);
            return _craftableFilter;
        }
    }

    /// <summary>“嵌套合成”那个筛选对象。</summary>
    public static CatFilter NestedFilter
    {
        get
        {
            if (_nestedFilter == null) _nestedFilter = FindFilter(NestedName);
            return _nestedFilter;
        }
    }

    private static CatFilter FindFilter(string name)
    {
        for (int i = 0; i < Filters.Count; i++)
        {
            if (Filters[i].Name == name) return Filters[i];
        }
        return null;
    }

    /// <summary>“可合成的”筛选开着没有（背包一变，勾着它时网格要重排）。</summary>
    public static bool CraftableSelected
    {
        get { CatFilter f = CraftableFilter; return f != null && f.Selected; }
    }

    /// <summary>“嵌套合成”筛选开着没有。</summary>
    public static bool NestedSelected
    {
        get { CatFilter f = NestedFilter; return f != null && f.Selected; }
    }

    // ================= 判定用的原料 =================

    /// <summary>取一个物品的样本实例（原版 ContentSamples 里那份，带全部 SetDefaults 结果）。</summary>
    public static Item Sample(int type)
    {
        if (type <= 0) return null;
        if (_samples.TryGetValue(type, out Item cached)) return cached;
        Item item = null;
        try
        {
            ContentSamples.ItemsByType.TryGetValue(type, out item);
        }
        catch
        {
        }
        _samples[type] = item;
        return item;
    }

    public static bool IsSolidTile(int type)
    {
        Item it = Sample(type);
        return it != null && it.createTile >= 0 && it.createTile < Main.tileSolid.Length && Main.tileSolid[it.createTile];
    }

    public static bool IsNonSolidTile(int type)
    {
        Item it = Sample(type);
        if (it == null || it.createTile < 0 || it.createTile >= Main.tileSolid.Length) return false;
        return !Main.tileSolid[it.createTile];
    }

    private static bool Set(bool[] set, int type)
    {
        try { return set != null && type > 0 && type < set.Length && set[type]; }
        catch { return false; }
    }

    /// <summary>原版那些用 -1 表示“没有”的 int 表（电路优先级、提炼机模式）。</summary>
    private static bool SetInt(int[] set, int type)
    {
        try { return set != null && type > 0 && type < set.Length && set[type] != -1; }
        catch { return false; }
    }

    private static bool IsWhip(int type)
    {
        Item it = Sample(type);
        if (it == null || it.shoot <= 0) return false;
        try { return it.shoot < ProjectileID.Sets.IsAWhip.Length && ProjectileID.Sets.IsAWhip[it.shoot]; }
        catch { return false; }
    }

    private static bool IsCart(int mountType)
    {
        try { return mountType > 0 && mountType < MountID.Sets.Cart.Length && MountID.Sets.Cart[mountType]; }
        catch { return false; }
    }

    private static bool IsFood(int type) { return Set(ItemID.Sets.IsFood, type); }

    /// <summary>喝下去给 buff 的药水（排除食物、宠物、照明宠物、坐骑、回血回魔药水）。</summary>
    private static bool IsBuffPotion(Item it)
    {
        if (it.buffType <= 0) return false;
        if (IsFood(it.type)) return false;
        if (IsPet(it.type) || IsLightPet(it.type)) return false;
        if (it.mountType > 0 || it.healLife > 0 || it.healMana > 0) return false;
        return it.consumable;
    }

    private static bool IsPet(int type)
    {
        Item it = Sample(type);
        if (it == null || it.buffType <= 0) return false;
        try { return it.buffType < Main.vanityPet.Length && Main.vanityPet[it.buffType]; }
        catch { return false; }
    }

    private static bool IsLightPet(int type)
    {
        Item it = Sample(type);
        if (it == null || it.buffType <= 0) return false;
        try { return it.buffType < Main.lightPet.Length && Main.lightPet[it.buffType]; }
        catch { return false; }
    }

    private static bool IsBobber(int type)
    {
        return type == ItemID.FishingBobber || type == ItemID.FishingBobberGlowingRainbow;
    }

    /// <summary>该物品放置出来的物块是不是配方里用得到的制作站（左栏那份列表）。</summary>
    private static bool CraftingStation(int type)
    {
        Item it = Sample(type);
        if (it == null || it.createTile < 0) return false;
        if (_craftTiles == null)
        {
            HashSet<int> set = new HashSet<int>();
            try
            {
                List<KeyValuePair<int, int>> tiles = BrowserData.CraftingTiles;
                for (int i = 0; i < tiles.Count; i++) set.Add(tiles[i].Key);
            }
            catch
            {
            }
            _craftTiles = set;
        }
        return _craftTiles.Contains(it.createTile);
    }

    /// <summary>按名称判断（原版这个版本没有对应的集合时的退路：雕像、桌椅之类）。</summary>
    private static bool NameHas(int type, params string[] keys)
    {
        string name = BrowserData.ItemName(type);
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (name.IndexOf(keys[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    // ---------------- 排序用的小工具 ----------------

    private static CatSort SortByInt(string name, CatIcon icon, string tip, Func<Item, int> get)
    {
        return new CatSort
        {
            Name = name,
            Icon = icon,
            Tip = tip,
            CompareItems = delegate (int a, int b)
            {
                Item ia = Sample(a);
                Item ib = Sample(b);
                int va = ia == null ? 0 : get(ia);
                int vb = ib == null ? 0 : get(ib);
                int c = vb.CompareTo(va);
                return c != 0 ? c : a.CompareTo(b);
            }
        };
    }

    private static int ItemValue(int type)
    {
        Item it = Sample(type);
        return it == null ? 0 : it.value;
    }

    private static int ItemRare(int type)
    {
        Item it = Sample(type);
        return it == null ? 0 : it.rare;
    }

    /// <summary>“合成顺序”：物品页取最早能造出它的那条配方。</summary>
    private static int RecipeOrder(int type)
    {
        List<int> list = BrowserData.RecipesForItem(type);
        return list.Count > 0 ? list[0] : int.MaxValue;
    }

    /// <summary>原版创意模式的分组顺序（ContentSamples.ItemCreativeSortingId）。</summary>
    private static int CreativeCompare(int a, int b)
    {
        int cmp = 0;
        try
        {
            bool oka = ContentSamples.ItemCreativeSortingId.TryGetValue(a, out var va);
            bool okb = ContentSamples.ItemCreativeSortingId.TryGetValue(b, out var vb);
            if (oka && okb)
            {
                cmp = va.Group.CompareTo(vb.Group);
                if (cmp == 0) cmp = va.OrderInGroup.CompareTo(vb.OrderInGroup);
            }
            else if (oka) return -1;
            else if (okb) return 1;
        }
        catch
        {
        }
        if (cmp != 0) return cmp;
        return string.Compare(BrowserData.ItemName(a), BrowserData.ItemName(b), StringComparison.CurrentCulture);
    }

    /// <summary>Boss 召唤物的进程顺序表（原版没有现成的集合，照原模组那样手写）。</summary>
    private static readonly int[] BossOrder =
    {
        ItemID.SlimeCrown, ItemID.SuspiciousLookingEye, ItemID.WormFood, ItemID.BloodySpine,
        ItemID.Abeemination, ItemID.DeerThing, ItemID.QueenSlimeCrystal, ItemID.MechanicalEye,
        ItemID.MechanicalWorm, ItemID.MechanicalSkull, ItemID.GuideVoodooDoll, ItemID.ClothierVoodooDoll,
        ItemID.LihzahrdPowerCell, ItemID.TruffleWorm, ItemID.EmpressButterfly, ItemID.SolarTablet,
        ItemID.DD2ElderCrystal, ItemID.CelestialSigil, ItemID.SuspiciousLookingTentacle
    };

    private static int BossSummonOrder(int type)
    {
        for (int i = 0; i < BossOrder.Length; i++)
        {
            if (BossOrder[i] == type) return i;
        }
        return -1;
    }

    // ---------------- 小工具 ----------------

    private static CatCategory New(string key, string name, CatIcon icon, string tip, Func<int, bool> belongs)
    {
        return new CatCategory
        {
            Key = key,
            Name = name,
            Icon = icon,
            Tip = tip,
            Belongs = belongs
        };
    }

    private static CatCategory Sub(CatCategory parent, string key, string name, CatIcon icon, string tip,
        Func<int, bool> belongs)
    {
        CatCategory c = New(key, name, icon, tip, belongs);
        c.Parent = parent;
        return c;
    }

    /// <summary>把某个分类的归属表算好（第一次用到时调一次）。</summary>
    public static void WarmCategory(CatCategory c)
    {
        int max = 1;
        try { max = Math.Max((int)ItemID.Count, 1); } catch { }
        c.Warm(max);
    }
}
