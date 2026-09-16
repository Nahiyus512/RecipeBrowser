using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;

namespace RecipeBrowser;

/// <summary>一个配方材料（可能是配方组，如“任意木材”）。</summary>
internal sealed class Ingredient
{
    public int Type;          // 用于画图标的物品
    public int Stack;         // 数量
    public string Text = "";  // 显示名（配方组时为组名）
    public int GroupId = -1;  // 配方组 id，-1 表示普通物品
}

/// <summary>某物品的一个掉落来源。</summary>
internal sealed class DropSource
{
    public int NpcType;
    public float Rate;
}

/// <summary>某个生物的一条掉落。</summary>
internal sealed class NpcDrop
{
    public int ItemType;
    public float Rate;
}

/// <summary>“制作”页里的一行：某物品 + 它是怎么来的（配方 / 掉落 / 采集）。</summary>
internal sealed class CraftNode
{
    public int ItemType;
    public int Stack = 1;
    public int Depth;
    public int RecipeIndex = -1;   // -1 表示没有配方（叶子）
    public string Source = "";     // 叶子来源说明
}

/// <summary>
/// 读取原版数据：合成配方（Main.recipe）、掉落（Main.ItemDropsDB）、名称与制作站。
/// 所有缓存都懒加载。
/// </summary>
internal static class BrowserData
{
    // ---------------- 名称 ----------------

    public static string ItemName(int type)
    {
        try
        {
            string s = Lang.GetItemNameValue(type);
            return string.IsNullOrWhiteSpace(s) ? ("Item " + type) : s;
        }
        catch
        {
            return "Item " + type;
        }
    }

    public static string NpcName(int type)
    {
        try
        {
            string s = Lang.GetNPCNameValue(type);
            return string.IsNullOrWhiteSpace(s) ? ("NPC " + type) : s;
        }
        catch
        {
            return "NPC " + type;
        }
    }

    /// <summary>
    /// 制作站名称。与原版 Utilities.GetTileName 一致：
    /// Recipe.GetRequiredTileStyle -> MapHelper.TileToLookup -> Lang.GetMapObjectName。
    /// </summary>
    public static string TileName(int tile)
    {
        if (tile < 0)
        {
            try { return Language.GetTextValue("LegacyInterface.23"); }
            catch { return "By hand"; }
        }
        try
        {
            string s = Recipe.GetRequiredTileName(tile);
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
        catch
        {
        }
        try
        {
            int place = TilePlaceItem(tile);
            if (place > 0)
            {
                string s2 = Lang.GetItemNameValue(place);
                if (!string.IsNullOrWhiteSpace(s2)) return s2;
            }
        }
        catch
        {
        }
        // 原版 Utilities.GetTileName 的最后一级回退：TileID 内部名
        try
        {
            string s3 = TileID.Search.GetName(tile);
            if (!string.IsNullOrWhiteSpace(s3)) return s3;
        }
        catch
        {
        }
        return "Tile #" + tile;
    }

    /// <summary>能放置该物块的物品（用于制作站图标与名称回退）。</summary>
    public static int TilePlaceItem(int tile)
    {
        if (tile < 0) return 0;
        BuildPlaceCache();
        return _placeItem.TryGetValue(tile, out int v) ? v : 0;
    }

    private static readonly Dictionary<int, int> _placeItem = new Dictionary<int, int>();
    private static bool _placeBuilt;

    private static void BuildPlaceCache()
    {
        if (_placeBuilt) return;
        _placeBuilt = true;
        try
        {
            Recipe[] recipes = Main.recipe;
            if (recipes == null) return;
            for (int i = 0; i < recipes.Length; i++)
            {
                Recipe r = recipes[i];
                if (r == null || r.createItem == null) continue;
                int t = r.createItem.createTile;
                if (t < 0) continue;
                if (!_placeItem.ContainsKey(t)) _placeItem[t] = r.createItem.type;
            }
        }
        catch
        {
        }
    }

    // ---------------- 配方 ----------------

    private static int[] _recipeCreate = Array.Empty<int>();
    private static int[] _recipeStation = Array.Empty<int>();
    private static int _recipeStamp = -1;
    private static readonly Dictionary<int, List<Ingredient>> _ingredients = new Dictionary<int, List<Ingredient>>();
    private static List<KeyValuePair<int, int>> _craftTiles;

    public static int RecipeCount
    {
        get
        {
            EnsureRecipes();
            return _recipeCreate.Length;
        }
    }

    public static int RecipeCreateType(int index)
    {
        EnsureRecipes();
        return index >= 0 && index < _recipeCreate.Length ? _recipeCreate[index] : 0;
    }

    public static int RecipeStation(int index)
    {
        EnsureRecipes();
        return index >= 0 && index < _recipeStation.Length ? _recipeStation[index] : -1;
    }

    private static void EnsureRecipes()
    {
        int n = 0;
        try { n = Recipe.numRecipes; } catch { }
        if (_recipeStamp == n && _recipeCreate.Length == n) return;
        _recipeStamp = n;
        _recipeCreate = new int[n];
        _recipeStation = new int[n];
        _ingredients.Clear();
        _recipesByResult = null;
        _craftTiles = null;
        try
        {
            for (int i = 0; i < n; i++)
            {
                Recipe r = Main.recipe[i];
                if (r == null || r.createItem == null)
                {
                    _recipeCreate[i] = 0;
                    _recipeStation[i] = -1;
                    continue;
                }
                _recipeCreate[i] = r.createItem.type;
                _recipeStation[i] = r.requiredTile;
            }
        }
        catch
        {
        }
    }

    public static List<Ingredient> IngredientsOf(int index)
    {
        EnsureRecipes();
        if (_ingredients.TryGetValue(index, out List<Ingredient> cached)) return cached;
        List<Ingredient> list = new List<Ingredient>();
        _ingredients[index] = list;
        try
        {
            if (index < 0 || index >= _recipeCreate.Length) return list;
            Recipe r = Main.recipe[index];
            if (r == null) return list;
            for (int j = 0; j < Recipe.maxRequirements; j++)
            {
                Recipe.RequiredItemEntry e = r.requiredItemQuickLookup[j];
                if (e.itemIdOrRecipeGroup == 0) break;
                if (e.IsRecipeGroup)
                {
                    RecipeGroup g = e.RecipeGroup;
                    if (g == null) continue;
                    int icon = g.DecraftItemId > 0 ? g.DecraftItemId : (g.Items != null && g.Items.Count > 0 ? g.Items[0] : 0);
                    string text = "";
                    try { text = g.GetText(); } catch { }
                    list.Add(new Ingredient { Type = icon, Stack = e.stack, Text = text, GroupId = g.RegisteredId });
                }
                else
                {
                    list.Add(new Ingredient
                    {
                        Type = e.itemIdOrRecipeGroup,
                        Stack = e.stack,
                        Text = ItemName(e.itemIdOrRecipeGroup)
                    });
                }
            }
        }
        catch
        {
        }
        return list;
    }

    /// <summary>
    /// 所有被配方用到的制作站。原版 tileChooserGrid 最终是按使用次数降序排的
    /// （UITileSlot.CompareTo 返回 -order），所以常用的工作台排在最前面。
    /// </summary>
    public static List<KeyValuePair<int, int>> CraftingTiles
    {
        get
        {
            EnsureRecipes();
            if (_craftTiles != null) return _craftTiles;
            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int i = 0; i < _recipeStation.Length; i++)
            {
                int t = _recipeStation[i];
                if (t < 0) continue;
                counts.TryGetValue(t, out int c);
                counts[t] = c + 1;
            }
            List<KeyValuePair<int, int>> list = new List<KeyValuePair<int, int>>(counts);
            list.Sort(delegate (KeyValuePair<int, int> a, KeyValuePair<int, int> b)
            {
                int cmp = b.Value.CompareTo(a.Value);
                return cmp != 0 ? cmp : a.Key.CompareTo(b.Key);
            });
            _craftTiles = list;
            return list;
        }
    }

    /// <summary>该物块是否满足某配方的制作站需求（含下位制作站）。</summary>
    public static bool TileSatisfies(int tile, int requiredTile, bool includeInherited)
    {
        if (requiredTile < 0) return false;
        if (tile == requiredTile) return true;
        if (!includeInherited) return false;
        try
        {
            List<int>[] table = Recipe.TileCountsAs;
            if (table == null || tile < 0 || tile >= table.Length) return false;
            List<int> equiv = table[tile];
            if (equiv == null) return false;
            for (int i = 0; i < equiv.Count; i++)
            {
                if (equiv[i] == requiredTile) return true;
                if (TileSatisfies(equiv[i], requiredTile, true)) return true;
            }
        }
        catch
        {
        }
        return false;
    }

    // ---------------- 合成路径（“制作”页用） ----------------

    private static Dictionary<int, List<int>> _recipesByResult;
    private static readonly List<int> _emptyRecipes = new List<int>();

    private static void EnsureRecipeIndex()
    {
        EnsureRecipes();
        if (_recipesByResult != null) return;
        Dictionary<int, List<int>> map = new Dictionary<int, List<int>>();
        try
        {
            for (int i = 0; i < _recipeCreate.Length; i++)
            {
                int t = _recipeCreate[i];
                if (t <= 0) continue;
                if (!map.TryGetValue(t, out List<int> l))
                {
                    l = new List<int>();
                    map[t] = l;
                }
                l.Add(i);
            }
        }
        catch
        {
        }
        _recipesByResult = map;
    }

    /// <summary>能合成该物品的全部配方索引。</summary>
    public static List<int> RecipesForItem(int itemType)
    {
        EnsureRecipeIndex();
        if (_recipesByResult != null && _recipesByResult.TryGetValue(itemType, out List<int> list)) return list;
        return _emptyRecipes;
    }

    /// <summary>默认用哪个配方展开合成树（原版取第一个）。</summary>
    public static int FirstRecipeFor(int itemType)
    {
        List<int> list = RecipesForItem(itemType);
        return list.Count > 0 ? list[0] : -1;
    }

    /// <summary>该物品能否从世界里采集到（能放置成物块/背景墙）。</summary>
    public static bool CanMine(int itemType)
    {
        try
        {
            Item sample = ContentSamples.ItemsByType[itemType];
            return sample != null && (sample.createTile >= 0 || sample.createWall >= 0);
        }
        catch
        {
            return false;
        }
    }

    private static string LeafSource(int itemType, bool allowLoot, bool allowMineable)
    {
        List<string> tags = new List<string>();
        if (allowMineable && CanMine(itemType)) tags.Add("Gathered");
        if (allowLoot)
        {
            List<DropSource> drops = LootOf(itemType);
            if (drops.Count > 0)
            {
                tags.Add("Drops from " + NpcName(drops[0].NpcType) + (drops.Count > 1 ? " etc." : ""));
            }
        }
        return tags.Count == 0 ? "" : string.Join(" / ", tags);
    }

    /// <summary>
    /// 展开某物品的合成树（原版“制作”页的 CraftPath）。nested=false 时只展开一层。
    /// 每行 = 物品 + 数量 + 该物品从哪来。
    /// </summary>
    public static List<CraftNode> BuildCraftPath(int itemType, bool nested, bool allowLoot, bool allowMineable)
    {
        List<CraftNode> rows = new List<CraftNode>();
        if (itemType <= 0) return rows;
        HashSet<int> onPath = new HashSet<int>();
        AddCraftRows(itemType, 1, 0, nested, allowLoot, allowMineable, rows, onPath);
        return rows;
    }

    private static void AddCraftRows(int itemType, int stack, int depth, bool nested,
        bool allowLoot, bool allowMineable, List<CraftNode> rows, HashSet<int> onPath)
    {
        if (itemType <= 0 || depth > 8 || rows.Count >= 400) return;
        int recipe = depth == 0 || nested ? FirstRecipeFor(itemType) : -1;
        CraftNode node = new CraftNode { ItemType = itemType, Stack = stack, Depth = depth, RecipeIndex = recipe };
        if (recipe < 0) node.Source = LeafSource(itemType, allowLoot, allowMineable);
        rows.Add(node);
        if (recipe < 0) return;
        if (!onPath.Add(itemType)) return;
        try
        {
            List<Ingredient> ings = IngredientsOf(recipe);
            for (int i = 0; i < ings.Count; i++)
            {
                AddCraftRows(ings[i].Type, Math.Max(1, ings[i].Stack) * Math.Max(1, stack),
                    depth + 1, nested, allowLoot, allowMineable, rows, onPath);
            }
        }
        finally
        {
            onPath.Remove(itemType);
        }
    }

    // ---------------- 掉落 ----------------

    private static Dictionary<int, List<DropSource>> _loot;
    private static readonly Dictionary<int, List<NpcDrop>> _npcDrops = new Dictionary<int, List<NpcDrop>>();

    public static bool LootReady => _loot != null;

    /// <summary>建立 物品 -> 掉落生物 的反向索引（与原版 LootCacheManager 相同的做法）。</summary>
    public static void BuildLoot()
    {
        if (_loot != null) return;
        Dictionary<int, List<DropSource>> dict = new Dictionary<int, List<DropSource>>();
        try
        {
            ItemDropDatabase db = Main.ItemDropsDB;
            if (db != null)
            {
                int max = NPCID.Count;
                for (int npc = -65; npc < max; npc++)
                {
                    if (npc == 0) continue;
                    List<IItemDropRule> rules = null;
                    try { rules = db.GetRulesForNPCID(npc, false); } catch { }
                    if (rules == null || rules.Count == 0) continue;
                    List<DropRateInfo> infos = new List<DropRateInfo>();
                    DropRateInfoChainFeed feed = new DropRateInfoChainFeed(1f);
                    for (int i = 0; i < rules.Count; i++)
                    {
                        try { rules[i].ReportDroprates(infos, feed); } catch { }
                    }
                    int displayNpc = ResolveNpc(npc);
                    if (displayNpc <= 0) continue;
                    for (int i = 0; i < infos.Count; i++)
                    {
                        DropRateInfo di = infos[i];
                        if (di.itemId <= 0) continue;
                        if (!dict.TryGetValue(di.itemId, out List<DropSource> list))
                        {
                            list = new List<DropSource>();
                            dict[di.itemId] = list;
                        }
                        DropSource found = null;
                        for (int k = 0; k < list.Count; k++)
                        {
                            if (list[k].NpcType == displayNpc) { found = list[k]; break; }
                        }
                        if (found == null) list.Add(new DropSource { NpcType = displayNpc, Rate = di.dropRate });
                        else if (di.dropRate > found.Rate) found.Rate = di.dropRate;
                    }
                }
            }
        }
        catch
        {
        }
        foreach (KeyValuePair<int, List<DropSource>> kv in dict)
        {
            kv.Value.Sort(delegate (DropSource a, DropSource b) { return a.NpcType.CompareTo(b.NpcType); });
        }
        _loot = dict;
    }

    /// <summary>负数 ID 是变体，转成正面 ID 才能取贴图/名称。</summary>
    private static int ResolveNpc(int id)
    {
        if (id > 0) return id;
        return NpcSpriteType(id);
    }

    private static readonly Dictionary<int, int> _npcSpriteType = new Dictionary<int, int>();
    private static readonly Dictionary<int, Color> _npcTint = new Dictionary<int, Color>();

    /// <summary>
    /// 负数 ID（史莱姆变体之类）对应的正面 ID：贴图和帧数要用它取。
    /// 原版 UINPCSlot 里存的就是 npc.SetDefaults 之后的 npc.type。取不到返回 0。
    /// </summary>
    public static int NpcSpriteType(int id)
    {
        if (id > 0) return id;
        if (id == 0) return 0;
        EnsureNpcVariant(id);
        return _npcSpriteType.TryGetValue(id, out int t) ? t : 0;
    }

    /// <summary>
    /// 变体的染色：原版 UINPCSlot 画变体时用 npc.color 上色（绿史莱姆、粉史莱姆……）。
    /// 普通生物返回白色。
    /// </summary>
    public static Color NpcTint(int id)
    {
        if (id >= 0) return Color.White;
        EnsureNpcVariant(id);
        return _npcTint.TryGetValue(id, out Color c) ? c : Color.White;
    }

    private static void EnsureNpcVariant(int id)
    {
        if (id >= 0 || _npcSpriteType.ContainsKey(id)) return;
        int sprite = 0;
        Color tint = Color.White;
        try
        {
            NPC n = new NPC();
            n.SetDefaults(id);
            if (n.type > 0) sprite = n.type;
            else if (n.netID > 0) sprite = n.netID;
            Color c = n.color;
            if (c != new Color(0, 0, 0, 0)) tint = new Color(c.R, c.G, c.B, (byte)255);
        }
        catch
        {
        }
        _npcSpriteType[id] = sprite;
        _npcTint[id] = tint;
    }

    public static List<DropSource> LootOf(int itemType)
    {
        BuildLoot();
        if (_loot != null && _loot.TryGetValue(itemType, out List<DropSource> list)) return list;
        return _emptyDrops;
    }

    private static HashSet<int> _lootKeys;

    /// <summary>这个物品有没有掉落来源（物品页“掉落物”筛选用）。</summary>
    public static bool IsLootItem(int itemType)
    {
        if (itemType <= 0) return false;
        BuildLoot();
        if (_lootKeys == null)
        {
            HashSet<int> set = new HashSet<int>();
            if (_loot != null)
            {
                foreach (KeyValuePair<int, List<DropSource>> kv in _loot)
                {
                    if (kv.Key > 0 && kv.Value.Count > 0) set.Add(kv.Key);
                }
            }
            _lootKeys = set;
        }
        return _lootKeys.Contains(itemType);
    }

    private static readonly List<DropSource> _emptyDrops = new List<DropSource>();

    /// <summary>某个生物的全部掉落（按掉率降序）。</summary>
    public static List<NpcDrop> NpcDrops(int npcType)
    {
        // 负数 ID 是变体（绿史莱姆之类），原版掉落库按 netID 存，负数照样能查
        if (npcType == 0) return _emptyNpcDrops;
        if (_npcDrops.TryGetValue(npcType, out List<NpcDrop> cached)) return cached;
        List<NpcDrop> list = new List<NpcDrop>();
        _npcDrops[npcType] = list;
        try
        {
            ItemDropDatabase db = Main.ItemDropsDB;
            if (db != null)
            {
                List<IItemDropRule> rules = db.GetRulesForNPCID(npcType, false);
                List<DropRateInfo> infos = new List<DropRateInfo>();
                DropRateInfoChainFeed feed = new DropRateInfoChainFeed(1f);
                for (int i = 0; i < rules.Count; i++)
                {
                    try { rules[i].ReportDroprates(infos, feed); } catch { }
                }
                for (int i = 0; i < infos.Count; i++)
                {
                    if (infos[i].itemId <= 0) continue;
                    list.Add(new NpcDrop { ItemType = infos[i].itemId, Rate = infos[i].dropRate });
                }
            }
        }
        catch
        {
        }
        list.Sort(delegate (NpcDrop a, NpcDrop b) { return b.Rate.CompareTo(a.Rate); });
        return list;
    }

    private static readonly List<NpcDrop> _emptyNpcDrops = new List<NpcDrop>();

    // ---------------- 全部物品 / 生物 ----------------

    private static int[] _allItems;
    private static int[] _allNpcs;

    public static int[] AllItems
    {
        get
        {
            if (_allItems != null) return _allItems;
            List<int> list = new List<int>(ItemID.Count);
            try
            {
                for (int i = 1; i < ItemID.Count; i++)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(Lang.GetItemNameValue(i))) list.Add(i);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            _allItems = list.ToArray();
            return _allItems;
        }
    }

    public static int[] AllNpcs
    {
        get
        {
            if (_allNpcs != null) return _allNpcs;
            List<int> list = new List<int>(NPCID.Count);
            try
            {
                // 原版 BestiaryUI 也是从 -65 开始列：负数 ID 是史莱姆之类的变体
                // （绿史莱姆、粉史莱姆……），在原版图鉴里是独立条目，名称走 Lang 的负数名表。
                for (int i = -65; i < NPCID.Count; i++)
                {
                    if (i == 0) continue;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(Lang.GetNPCNameValue(i))) list.Add(i);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            _allNpcs = list.ToArray();
            return _allNpcs;
        }
    }

    // ---------------- 工具提示文本（用于“搜索工具提示”筛选）----------------

    private static readonly Dictionary<int, string> _tooltipCache = new Dictionary<int, string>();

    public static string TooltipText(int itemType)
    {
        if (_tooltipCache.TryGetValue(itemType, out string cached)) return cached;
        string text = "";
        try
        {
            Terraria.UI.ItemTooltip tt = Lang.GetTooltip(itemType);
            if (tt != null)
            {
                StringBuilder sb = new StringBuilder();
                int lines = tt.Lines;
                for (int i = 0; i < lines; i++)
                {
                    sb.Append(tt.GetLine(i)).Append(' ');
                }
                text = sb.ToString();
            }
        }
        catch
        {
        }
        _tooltipCache[itemType] = text;
        return text;
    }

    // ---------------- 刷新 ----------------

    public static void Invalidate()
    {
        _recipeStamp = -1;
        _ingredients.Clear();
        _recipesByResult = null;
        _craftTiles = null;
        _loot = null;
        _lootKeys = null;
        _npcDrops.Clear();
        _allItems = null;
        _allNpcs = null;
        _tooltipCache.Clear();
        _placeBuilt = false;
        _placeItem.Clear();
    }
}
