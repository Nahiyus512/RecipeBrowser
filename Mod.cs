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

    public string Name => "配方浏览器";

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
            "查询悬停物品",
            "对鼠标悬停/持有的物品打开合成配方与掉落面板。默认鼠标中键触发。",
            "mousemiddle",
            OnQuery);

        Browser.Init();
        Log.Info("Recipe Browser initialized. 默认触发键：鼠标中键（可在F6里改键）。");
    }

    public void OnConfigChanged()
    {
        Config = Config ?? new RecipeBrowserConfig();
        Log.Info("Recipe Browser 配置已重载。");
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
    [Label("窗口X")]
    [Description("面板左上角X坐标（拖动窗口后自动保存）。")]
    public int WindowX { get; set; } = -1;

    [Client]
    [Label("窗口Y")]
    [Description("面板左上角Y坐标（拖动窗口后自动保存）。")]
    public int WindowY { get; set; } = -1;

    [Client]
    [Label("窗口宽度")]
    [Description("面板宽度（拖动右下角缩放后自动保存）。")]
    public int WindowW { get; set; } = 560;

    [Client]
    [Label("窗口高度")]
    [Description("面板高度（拖动右下角缩放后自动保存）。")]
    public int WindowH { get; set; } = 420;
}
