using System;
using System.Reflection;
using Terraria;
using Terraria.GameContent.UI;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace RecipeBrowser;

/// <summary>
/// 把配方页里点中的那条配方同步到游戏自己的制作菜单。
///
/// 这个版本的游戏有两套制作菜单（原模组的年代只有第一套）：
///   1. 左侧竖着的经典列表，以及它下面切出来的左侧网格 —— 原模组 UIRecipeSlot.LeftClick 同步的就是这套；
///   2. 点制作站 / 点按钮展开的网格菜单 —— Terraria.GameContent.UI.NewCraftingUI。
/// 两套都同步，任何一边出问题都不影响另一边。
///
/// “选中第几条”两边都记在 Main.focusRecipe 上，但那里的下标是 Main.availableRecipe 里的位置、
/// 不是配方式索引（Main.recipe 的下标），所以要先换算一次。这也是原模组的做法：
/// 在当前“现在能做”的那份列表里找不到这条配方（材料不够）就什么都不做。
/// </summary>
internal static class GameCraftingMenu
{
    // 展开的网格菜单（NewCraftingUI）里没有公开入口的几个字段，只能反射
    private const string InstanceFieldName = "_instance";
    private const string SelectedFieldName = "_selectedRecipeIndex";
    private const string LookupFieldName = "_recipeListLookup";
    private const string GridElementFieldName = "_itemGrid";
    private const string GridContainerFieldName = "_gridContainer";
    // 网格里一格占的像素（原版 UIDynamicItemCollection.sizePerEntryY）
    private const int GridCellPx = 44;
    // 左侧网格模式里一格的占位，以及原版算列数时减掉的左右留白（CraftingUI.DrawRecipesGrid 里的 42 / 310 / 280）
    private const int PipCellPx = 42;
    private const int PipLeftGap = 310;
    private const int PipRightGap = 280;

    private static bool _refInit;
    private static FieldInfo _fInstance;
    private static FieldInfo _fSelected;
    private static FieldInfo _fLookup;
    private static FieldInfo _fGridElement;
    private static FieldInfo _fGridContainer;

    /// <summary>配方页点中第 recipeIndex 条配方（Main.recipe 的下标）时调一次。</summary>
    public static void FocusRecipe(int recipeIndex)
    {
        try
        {
            if (recipeIndex < 0) return;
            int availableIndex = FindAvailableIndex(recipeIndex);
            if (availableIndex < 0) return;   // 材料不够：游戏制作菜单里没有这一格

            // 原模组 LeftClick 里第一件事就是打开背包；顺手切到“制作”那一页，否则竖列表根本不画
            Main.playerInventory = true;
            Main.TryChangePipsPage(Main.PipPage.Recipes);

            // 左侧竖列表：原模组就是这两行（PipsFastScroll 相当于旧版那个 Main.recFastScroll），
            // 列表会立刻滚过去、并把这一条高亮出来
            Main.focusRecipe = availableIndex;
            Main.PipsFastScroll = true;
            // 这次点击是用来换选中项的，别让它顺手把这一条合成出来 —— 原版自己换选中项时也这么标
            Main._preventCraftingBecauseClickWasUsedToChangeFocusedRecipe = true;

            SyncPipGrid(availableIndex);
            SyncFlatGrid(recipeIndex);
        }
        catch
        {
        }
    }

    /// <summary>在当前“现在能做”的列表里找这条配方是第几条；找不到（材料不够）返回 -1。</summary>
    private static int FindAvailableIndex(int recipeIndex)
    {
        try
        {
            int count = Main.numAvailableRecipes;
            if (count > Recipe.maxRecipes) count = Recipe.maxRecipes;
            for (int i = 0; i < count; i++)
            {
                if (Main.availableRecipe[i] == recipeIndex) return i;
            }
        }
        catch
        {
        }
        return -1;
    }

    /// <summary>
    /// 左侧那一栏切成网格样式时（原版那个按钮切的）没有高亮，focusRecipe 只影响左边那个合成槽，
    /// 只有把 recStart（网格从第几格开始画）对齐到那一行，配方才会滚进视野。
    /// 原版的翻页就是一整行一整行跳，所以这里也对齐到行首。
    /// </summary>
    private static void SyncPipGrid(int availableIndex)
    {
        try
        {
            if (!Main.PipsUseGrid) return;
            int perRow = (Main.screenWidth - PipLeftGap - PipRightGap) / PipCellPx;
            if (perRow < 1) perRow = 1;
            Main.recStart = availableIndex / perRow * perRow;
        }
        catch
        {
        }
    }

    /// <summary>
    /// 展开的网格菜单。它内部记的是配方式索引（不是 availableRecipe 下标），外面还套着它自己的
    /// 搜索框和筛选器，光改 Main.focusRecipe 不会跟着走；改掉这个字段、再让它重排一次内容，
    /// 它下一帧自己就会把 Main.focusRecipe 对齐、并把那一格高亮出来。
    /// </summary>
    private static void SyncFlatGrid(int recipeIndex)
    {
        try
        {
            if (!NewCraftingUI.Visible) return;   // 没展开就不去碰它，免得凭空把它打开
            InitReflection();
            object instance = _fInstance != null ? _fInstance.GetValue(null) : null;
            if (instance == null || _fSelected == null) return;
            // 行号要从“重排之前”那份列表上取（重排会先把内容清空，那之后就查不到了）
            int gridIndex = GridIndexOf(instance, recipeIndex);
            _fSelected.SetValue(instance, recipeIndex);
            NewCraftingUI.RefreshGrid();
            // 被网格自己的搜索 / 筛选排掉的话（行号 -1）网格里就没有这一格，也就没什么好滚的
            if (gridIndex >= 0) ScrollFlatGrid(instance, gridIndex);
        }
        catch
        {
        }
    }

    /// <summary>网格里那一格在第几行，就把网格滚到那一行（越界由原版自己的滚动条夹住）。</summary>
    private static void ScrollFlatGrid(object instance, int gridIndex)
    {
        UIElement gridElement = FieldValue(instance, _fGridElement) as UIElement;
        UIElement container = FieldValue(instance, _fGridContainer) as UIElement;
        if (gridElement == null || container == null) return;
        UIScrollbar bar = FindScrollbar(container);
        if (bar == null) return;

        int perRow = (int)gridElement.GetDimensions().Width / GridCellPx;
        if (perRow < 1) return;
        int row = gridIndex / perRow;
        // 尽量让那一格停在网格中间，而不是紧贴着最上面
        float target = row * GridCellPx - Math.Max(0f, (bar.GetDimensions().Height - GridCellPx) / 2f);
        if (target < 0f) target = 0f;
        bar.ViewPosition = target;
    }

    private static int GridIndexOf(object instance, int recipeIndex)
    {
        if (_fLookup == null) return -1;
        Array lookup = _fLookup.GetValue(instance) as Array;
        if (lookup == null || recipeIndex >= lookup.Length) return -1;
        object entry = lookup.GetValue(recipeIndex);
        if (entry == null) return -1;
        // RecipeEntry 是 NewCraftingUI 里的私有嵌套类，gridIndex 是它唯一的公开字段
        FieldInfo f = entry.GetType().GetField("gridIndex", BindingFlags.Public | BindingFlags.Instance);
        if (f == null) return -1;
        object value = f.GetValue(entry);
        return value is int index ? index : -1;
    }

    private static object FieldValue(object instance, FieldInfo field)
    {
        try
        {
            return field != null ? field.GetValue(instance) : null;
        }
        catch
        {
            return null;
        }
    }

    private static UIScrollbar FindScrollbar(UIElement root)
    {
        try
        {
            foreach (UIElement child in root.Children)
            {
                if (child is UIScrollbar bar) return bar;
            }
        }
        catch
        {
        }
        return null;
    }

    private static void InitReflection()
    {
        if (_refInit) return;
        _refInit = true;
        try
        {
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            Type t = typeof(NewCraftingUI);
            _fInstance = t.GetField(InstanceFieldName, F);
            _fSelected = t.GetField(SelectedFieldName, F);
            _fLookup = t.GetField(LookupFieldName, F);
            _fGridElement = t.GetField(GridElementFieldName, F);
            _fGridContainer = t.GetField(GridContainerFieldName, F);
        }
        catch
        {
        }
    }
}
