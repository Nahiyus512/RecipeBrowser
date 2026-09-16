using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using TerrariaModder.Core.UI;

namespace RecipeBrowser;

/// <summary>
/// 配方页 / 物品页上面那条“分类 + 排序 + 筛选”栏，对应原模组的 sortsAndFiltersPanel（高 60）。
/// 第一排是分类（武器 / 工具 / 盔甲 / 物块 / 饰品 / 药水……），第二排是当前分类的子分类 + 排序 + 筛选；
/// 一行放不下时两端出现 ◀ ▶ 翻页按钮 —— 原模组那条用的是横向滚动条，作用是一样的。
/// 分类表、排序表、筛选表都在 Catalogue 里（判定条件也是原模组那一套），这里只管画和点。
/// </summary>
public static partial class Browser
{
    private const int CatBtn = 26;      // 按钮边长：原模组的排序贴图是 24x24，再留 2 像素当间隔
    private const int CatGap = 2;
    private const int CatIconBox = 24;  // 图标画在这个正方形里（原模组就是把图标缩到 24x24 再画）
    private const int CatArrowW = 12;   // 翻页箭头宽度
    private const int CatRowH = 28;     // 一排的高度（分类栏 60 高，上下各留一点，正好放两排）

    private static int _catPage, _subPage;   // 两排各自翻到第几个按钮

    // 这一帧被点中的按钮（两排共用一份：画完一排就地处理掉，再画下一排）
    private static int _hitKind;
    private static object _hitTarget;
    private static bool _hitRight;

    /// <summary>一排里的一个按钮。Kind：0 分类 / 1 排序 / 2 筛选 / 3 清除筛选 / -1 分隔条。</summary>
    private struct CatPiece
    {
        public int Kind;
        public object Target;   // CatCategory / CatSort / CatFilter
        public CatIcon Icon;    // 图标（原模组那张表里的物品或贴图）
        public string Label;
        public string Tip;      // 提示标题
        public string Desc;     // 提示正文
        public bool Text;       // 没有图标，直接画文字（“清除”用）
        public bool On;         // 是否处于选中状态
    }

    // 原模组在“子分类 / 排序 / 筛选”之间各塞了一个空的间隔元素（Images/spacer：6x24 的小竖条），
    // 靠它把三段隔开。这里也用一块占位按钮来占同样的位置，看着和原模组一样。
    private const int SpacerKind = -1;

    private static CatPiece Spacer()
    {
        return new CatPiece { Kind = SpacerKind };
    }

    private static readonly List<CatPiece> _catRow = new List<CatPiece>();
    private static readonly List<CatPiece> _subRow = new List<CatPiece>();

    /// <summary>画整条分类栏。compact 为 true 时按钮不额外放宽（物品页那半边比较窄）。</summary>
    private static void DrawCatalogueBar(Rectangle panel, bool compact)
    {
        Catalogue.Ensure();
        if (!Catalogue.Ready) return;
        Panel(panel, SlotPanelColor);
        Rectangle row1 = new Rectangle(panel.X + 4, panel.Y + 2, Math.Max(40, panel.Width - 8), CatRowH);
        Rectangle row2 = new Rectangle(row1.X, row1.Bottom + 1, row1.Width, CatRowH);

        BuildCatRow();
        DrawPieceStrip(row1, _catRow, ref _catPage, compact);
        ApplyCatalogueHit();
        // 换分类会把子分类/排序/筛选三份表都换掉，所以第二排要等第一排点完再建
        BuildSubRow();
        DrawPieceStrip(row2, _subRow, ref _subPage, compact);
        ApplyCatalogueHit();
    }

    /// <summary>第一排：所有顶级分类。</summary>
    private static void BuildCatRow()
    {
        _catRow.Clear();
        List<CatCategory> cats = Catalogue.Categories;
        CatCategory root = RootOf(Catalogue.Selected);
        for (int i = 0; i < cats.Count; i++)
        {
            CatCategory c = cats[i];
            _catRow.Add(new CatPiece
            {
                Kind = 0,
                Target = c,
                Icon = c.Icon,
                Label = c.Name,
                Tip = c.Name,
                Desc = c.Tip,
                On = c == root
            });
        }
    }

    /// <summary>第二排：当前分类的子分类 + 排序 + 筛选，最后再挂一个“清除”。</summary>
    private static void BuildSubRow()
    {
        _subRow.Clear();
        CatCategory sel = Catalogue.Selected;
        CatCategory root = RootOf(sel);
        if (root != null)
        {
            List<CatCategory> subs = root.Subs;
            for (int i = 0; i < subs.Count; i++)
            {
                CatCategory sub = subs[i];
                _subRow.Add(new CatPiece
                {
                    Kind = 0,
                    Target = sub,
                    Icon = sub.Icon,
                    Label = sub.Name,
                    Tip = sub.Name,
                    Desc = sub.Tip,
                    On = sub == sel
                });
            }
            // 有子分类时，子分类和排序之间隔开（原模组那两处 spacer 的第一处）
            if (subs.Count > 0) _subRow.Add(Spacer());
        }

        // 排序是单选：当前分类自己的排在最前面（Catalogue.SortsFor 已经这么排了）
        List<CatSort> sorts = Catalogue.SortsFor(sel);
        for (int i = 0; i < sorts.Count; i++)
        {
            CatSort sort = sorts[i];
            _subRow.Add(new CatPiece
            {
                Kind = 1,
                Target = sort,
                Icon = sort.Icon,
                Label = sort.Name,
                Tip = "Sort: " + sort.Name,
                Desc = sort.Tip,
                On = sort == Catalogue.Sort
            });
        }

        // 筛选可多选：点了就叠加上去（原版的 FilterCraftable / FilterMaterials 那一排）
        List<CatFilter> filters = Catalogue.FiltersFor(sel);

        // 排序和筛选之间也隔开（原模组那两处 spacer 的第二处）。
        // 原模组只在“这个分类真有筛选”时才插那根小竖条，所以这里也跟着判一下，
        // 不然没筛选的分类（比如背景墙）末尾会孤零零多出一根。
        if (filters.Count > 0) _subRow.Add(Spacer());
        bool anyOn = false;
        for (int i = 0; i < filters.Count; i++)
        {
            CatFilter filter = filters[i];
            if (filter.Selected) anyOn = true;
            _subRow.Add(new CatPiece
            {
                Kind = 2,
                Target = filter,
                Icon = filter.Icon,
                Label = filter.Name,
                Tip = "Filter: " + filter.Name,
                Desc = (filter.Tip ?? "") + " (left-click to toggle, right-click to clear)",
                On = filter.Selected
            });
        }

        // 勾了筛选才给这个按钮：一键全关，省得一个个点（筛选没勾时网格看着莫名其妙地空）
        if (anyOn)
        {
            _subRow.Add(new CatPiece
            {
                Kind = 3,
                Label = "Clear",
                Tip = "Clear filters",
                Desc = "Turn off all filters for the current category",
                Text = true
            });
        }
    }

    /// <summary>一路往上找最顶级的分类（选中的是子分类时，第一排要亮它所属的那个父分类）。</summary>
    private static CatCategory RootOf(CatCategory c)
    {
        while (c != null && c.Parent != null) c = c.Parent;
        return c;
    }

    /// <summary>
    /// 画一排按钮。放不下时两端出现 ◀ ▶（一次翻一屏）—— 原模组那条横向滚动条就是这个作用。
    /// 提示文字留到裁剪区外面再画，免得被裁掉。
    /// </summary>
    private static void DrawPieceStrip(Rectangle area, List<CatPiece> items, ref int page, bool compact)
    {
        _hitKind = -1;
        _hitTarget = null;
        _hitRight = false;
        int n = items.Count;
        if (n <= 0 || area.Width <= 0 || area.Height <= 0) return;
        int btn = CatBtn + (compact ? 0 : 2);
        int step = btn + CatGap;
        int fit = StripFit(area.Width, step);
        bool paged = n > fit;
        int pad = paged ? CatArrowW + CatGap : 0;
        if (paged) fit = StripFit(area.Width - pad * 2, step);
        int y = area.Y + (area.Height - btn) / 2;
        int mx = UIRenderer.MouseX;
        int my = UIRenderer.MouseY;
        int hover = -1;
        // 鼠标停在分类栏上滚轮也能翻页（左右翻比来回点 ▶ 顺手）
        if (paged && UIRenderer.IsMouseOver(area.X, area.Y, area.Width, area.Height))
        {
            int wheel = UIRenderer.ScrollWheel;
            if (wheel != 0)
            {
                page = Clamp(page - Math.Sign(wheel), 0, Math.Max(0, n - fit));
                UIRenderer.ConsumeScroll();
            }
        }
        UIRenderer.BeginClip(area.X, area.Y, area.Width, area.Height);
        try
        {
            // 翻页箭头：点完立刻按新页码画这一行，免得闪一帧
            if (paged)
            {
                if (page > 0 && DrawStripArrow(new Rectangle(area.X, y, CatArrowW, btn), "<")) page--;
                if (page < n - fit && DrawStripArrow(new Rectangle(area.Right - CatArrowW, y, CatArrowW, btn), ">")) page++;
            }
            page = Clamp(page, 0, Math.Max(0, n - fit));
            int last = Math.Min(n, page + fit);
            int x = area.X + pad;
            for (int i = page; i < last; i++)
            {
                Rectangle r = new Rectangle(x, y, btn, btn);
                // 分隔条只是个占位：不算鼠标悬停、点不动
                bool over = items[i].Kind != SpacerKind && r.Contains(mx, my);
                DrawCatPiece(r, items[i], over);
                if (items[i].Kind == SpacerKind) { x += step; continue; }
                Hot(r);
                if (over)
                {
                    CatPiece p = items[i];
                    hover = i;
                    if (PressedRight && p.Kind == 2)
                    {
                        ConsumeRight();
                        _hitKind = p.Kind;
                        _hitTarget = p.Target;
                        _hitRight = true;
                    }
                    else if (PressedLeft)
                    {
                        ConsumeLeft();
                        _hitKind = p.Kind;
                        _hitTarget = p.Target;
                    }
                }
                x += step;
            }
        }
        finally
        {
            UIRenderer.EndClip();
        }
        if (hover >= 0) VanillaTextTip(items[hover].Tip, items[hover].Desc);
    }

    /// <summary>这一行能放下几个（末尾那个间隔不用留，所以 +CatGap）。</summary>
    private static int StripFit(int width, int step)
    {
        int n = (width + CatGap) / step;
        return n < 1 ? 1 : n;
    }

    /// <summary>翻页箭头。返回 true 表示这一帧被点了。</summary>
    private static bool DrawStripArrow(Rectangle r, string glyph)
    {
        bool over = r.Contains(UIRenderer.MouseX, UIRenderer.MouseY);
        Hot(r);
        Fill(r, over ? new Color(120, 132, 170) : new Color(60, 66, 88));
        Outline(r, new Color(28, 32, 46));
        Text(glyph, r.X + (r.Width - TextW(glyph, SmallText)) / 2, r.Y + (r.Height - 12) / 2,
            over ? Color.White : new Color(200, 205, 220), SmallText);
        if (!over || !PressedLeft) return false;
        ConsumeLeft();
        return true;
    }

    private static void DrawCatPiece(Rectangle r, CatPiece p, bool over)
    {
        if (p.Kind == SpacerKind)
        {
            DrawCatalogueSpacer(r);
            return;
        }
        if (p.Text)
        {
            // “清除”是这边多出来的按钮（原模组靠右键关掉筛选），没有对应图标可画，
            // 就照原版小按钮的样子给块底板，免得一排全是图标时它看着像块空白。
            Fill(r, over ? new Color(120, 132, 170) : new Color(60, 66, 88));
            Outline(r, new Color(28, 32, 46));
            TextFit(p.Label, r.X + 3, r.Y + (r.Height - 12) / 2,
                over ? Color.White : new Color(222, 226, 238), r.Width - 6, SmallText);
            return;
        }
        if (p.Icon == null || p.Icon.Empty) return;
        // 原模组 UISilentImageButton.DrawSelf 就两步：选中的那一格先垫一张原版
        // InventoryBack14（金黄色槽位底板），再把图标按状态调亮度画上去 ——
        // 选中 100%、悬停 90%、平时 80%。没选中的按钮是没有底板的。
        // 以前这里是自绘的蓝底方块，所以整条栏的观感和原模组对不上。
        Rectangle box = CatIconRect(r);
        if (p.On) DrawBack(box, 14);
        DrawButtonIcon(box, p.Icon, p.On ? 1f : (over ? 0.9f : 0.8f));
    }

    /// <summary>图标画在按钮正中那个 24x24 的方框里（原模组的按钮本身就是 24x24）。</summary>
    private static Rectangle CatIconRect(Rectangle r)
    {
        int box = Math.Min(CatIconBox, Math.Min(r.Width, r.Height));
        if (box < 4) box = Math.Max(1, Math.Min(r.Width, r.Height));
        return new Rectangle(r.X + (r.Width - box) / 2, r.Y + (r.Height - box) / 2, box, box);
    }

    /// <summary>分组之间的分隔条（原模组的 Images/spacer：24x24 里居中画一根 6x24 的浅色小竖条）。</summary>
    private static void DrawCatalogueSpacer(Rectangle r)
    {
        Texture2D tex = GameRefs.ModTex("spacer");
        if (tex == null || tex.Width <= 0 || tex.Height <= 0) return;
        try
        {
            int h = Math.Min(tex.Height, r.Height);
            int w = Math.Max(1, tex.Width * h / tex.Height);
            UIRenderer.DrawTexture(tex, r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 按钮上的图标。原模组是把图标缩进 24x24 里画；一个图标由多层叠出来时
    /// （Utilities.StackResizeImage），每层缩到 24/(1+0.5*(n-1)) 见方、中心依次往右下偏半个身位，
    /// 斜着叠成一张。这里照同一个几何画，所以“武器”“工具”这种叠出来的图标和原模组长得一样。
    /// mul 是亮度（原模组的选中/悬停/平时三档：1 / 0.9 / 0.8）。
    /// </summary>
    private static void DrawButtonIcon(Rectangle r, CatIcon icon, float mul)
    {
        if (icon == null || icon.Empty || Main.spriteBatch == null) return;
        IconLayer[] layers = icon.Layers;
        int n = layers.Length;
        // 原模组的按钮是 24x24，图标就按 24 画；这边按钮略大（26/28），图标仍然居中画在 24 里，
        // 大小就和原模组一模一样了
        float box = Math.Min(r.Width, r.Height);
        if (box <= 2f) return;
        float inner = box / (1f + 0.5f * (n - 1));
        float step = inner * 0.5f;
        float ox = r.X + r.Width / 2f - box / 2f;
        float oy = r.Y + r.Height / 2f - box / 2f;
        for (int i = 0; i < n; i++)
        {
            Texture2D tex = null;
            Rectangle src = Rectangle.Empty;
            try
            {
                IconLayer layer = layers[i];
                if (layer.Item > 0)
                {
                    tex = GameRefs.Item(layer.Item);
                    if (tex != null) src = ItemFrame(layer.Item, tex);
                }
                else if (!string.IsNullOrEmpty(layer.Tex))
                {
                    tex = GameRefs.ModTex(layer.Tex);
                }
                else if (!string.IsNullOrEmpty(layer.Vanilla))
                {
                    tex = GameRefs.Vanilla(layer.Vanilla);
                }
                if (tex != null) src = new Rectangle(0, 0, tex.Width, tex.Height);
            }
            catch
            {
                tex = null;
            }
            if (tex == null || src.Width <= 0 || src.Height <= 0) continue;
            float fit = Math.Min(1f, inner / Math.Max(src.Width, src.Height));
            Vector2 pos = new Vector2(ox + inner / 2f + step * i, oy + inner / 2f + step * i);
            try
            {
                Main.spriteBatch.Draw(tex, pos, src, Color.White * mul, 0f,
                    new Vector2(src.Width / 2f, src.Height / 2f), fit, SpriteEffects.None, 0f);
            }
            catch
            {
            }
        }
    }

    /// <summary>把这一排点中的按钮作用到 Catalogue 上（改完标一下网格要重排）。</summary>
    private static void ApplyCatalogueHit()
    {
        int kind = _hitKind;
        object target = _hitTarget;
        bool right = _hitRight;
        _hitKind = -1;
        _hitTarget = null;
        _hitRight = false;
        if (kind < 0) return;
        try
        {
            if (kind == 0 && target != null)
            {
                Catalogue.SelectCategory((CatCategory)target);
                _subPage = 0;      // 换分类了，第二排从头看起
            }
            else if (kind == 1 && target != null)
            {
                Catalogue.Sort = (CatSort)target;
            }
            else if (kind == 2 && target != null)
            {
                CatFilter f = (CatFilter)target;
                if (right) f.Selected = false;      // 右键把这个筛选清掉
                else Catalogue.Toggle(f);
            }
            else if (kind == 3)
            {
                List<CatFilter> filters = Catalogue.FiltersFor(Catalogue.Selected);
                for (int i = 0; i < filters.Count; i++) filters[i].Selected = false;
            }
            else
            {
                return;
            }
            _gridDirty = true;
        }
        catch
        {
        }
    }
}
