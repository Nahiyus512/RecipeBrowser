using System;
using Terraria;
using TerrariaModder.Core;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Logging;
using TerrariaModder.Core.UI;

namespace RecipeBrowser;

public class Mod : IMod
{
    public string Id => "recipe-browser";

    public string Name => "Recipe Browser";

    public string Version => "1.0.0";

    internal static ILogger Log { get; private set; }

    internal static RecipeBrowserConfig Config { get; private set; }

    public void Initialize(ModContext context)
    {
        Log = context.Logger;
        Config = context.GetConfig<RecipeBrowserConfig>() ?? new RecipeBrowserConfig();

        // 默认绑定为鼠标中键，可直接在 F6 里改成别的键。
        context.RegisterKeybind(
            "query-hovered",
            "Query Hovered Item",
            "Open the recipe and drop panel for the item you hover over or hold. Middle mouse button by default.",
            "mousemiddle",
            OnQuery);

        Browser.Init();
        Log.Info("Recipe Browser initialized. Default trigger: middle mouse button (rebindable with F6).");
    }

    public void OnConfigChanged()
    {
        Config = Config ?? new RecipeBrowserConfig();
        Log.Info("Recipe Browser config reloaded.");
    }

    public void Unload()
    {
        Browser.Unload();
    }

    private static void OnQuery()
    {
        try
        {
            // 中键点在打开的面板上时，不当作查询（避免点面板内部把面板关掉）。
            // 用面板自己记下的“鼠标在窗口上”标记：它是绘制阶段算的，和我们点按钮用的是同一套坐标，
            // 而 UIRenderer.IsMouseOverAnyPanel() 在更新阶段用的是另一套鼠标坐标，
            // 会让窗口周围多出一圈“看不见但会吃掉中键”的区域。
            if (Browser.IsOpen && Browser.MouseOverPanel)
            {
                return;
            }

            int type = 0;
            if (Main.HoverItem != null)
            {
                type = Main.HoverItem.type;
            }
            if (type == 0 && Main.mouseItem != null)
            {
                type = Main.mouseItem.type;
            }
            if (type == 0)
            {
                if (Browser.IsOpen)
                {
                    Browser.Close();
                }
                return;
            }
            Browser.Toggle(type);
        }
        catch (Exception ex)
        {
            Log.Error("Recipe Browser query failed: " + ex.Message);
        }
    }
}

public class RecipeBrowserConfig : ModConfig
{
    public override int Version => 1;

    [Client]
    [Label("Window X")]
    [Description("Panel top-left X coordinate (saved automatically after dragging the window).")]
    public int WindowX { get; set; } = -1;

    [Client]
    [Label("Window Y")]
    [Description("Panel top-left Y coordinate (saved automatically after dragging the window).")]
    public int WindowY { get; set; } = -1;

    [Client]
    [Label("Window Width")]
    [Description("Panel width (saved automatically after resizing from the bottom-right corner).")]
    public int WindowW { get; set; } = 560;

    [Client]
    [Label("Window Height")]
    [Description("Panel height (saved automatically after resizing from the bottom-right corner).")]
    public int WindowH { get; set; } = 420;
}
