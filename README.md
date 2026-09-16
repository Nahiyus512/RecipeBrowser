# 配方浏览器（RecipeBrowser）

TerrariaModder 模组：在游戏内查询合成配方、物品图鉴和怪物图鉴，界面复刻 tModLoader 版 Recipe Browser。

## 功能

- 四个标签页：**配方**、**制作**、**物品**、**怪物图鉴**
- 配方页：按名称或工具提示搜索，勾选制作站，按分类 / 排序 / 筛选浏览；
  底部显示这条配方需要的制作站和材料，右侧一栏列出该物品的「掉落自」
- 制作页：查某个物品是怎么来的 —— 合成配方、掉落、采集都会列出来
- 物品页：浏览全部物品，同样带分类 / 排序 / 筛选
- 怪物图鉴页：浏览怪物以及它们的掉落
- 窗口可拖动、可缩放，位置和尺寸自动保存
- 默认用**鼠标中键**查询鼠标悬停或手上拿着的物品

## 操作

| 操作 | 说明 |
| --- | --- |
| 鼠标中键 | 查询悬停 / 手持物品 |
| 拖动标题栏 | 移动窗口 |
| 拖动右下角 | 缩放窗口 |

按键可在游戏内 **F6 → 快捷键** 里修改。

## 配置

**F6 → 配置** 里可以改窗口的 X / Y 坐标、宽度、高度，文件保存在 `core/configs/recipe-browser.client.json`。

## 安装

把 `bin\` 里的 `manifest.json` 和 `RecipeBrowser.dll` 复制到 `TerrariaModder\mods\recipe-browser\`，
用 `TerrariaInjector.exe` 启动游戏。

## 编译

需要 Windows、[.NET SDK](https://dotnet.microsoft.com/download)、Terraria 1.4.5，以及已安装的 TerrariaModder；此外还需要：

- tModLoader 的 `Libraries/ReLogic/1.0.0/ReLogic.dll`
- 已安装 XNA 4.0 可再发行组件（`Microsoft.Xna.Framework*.dll` 在系统 GAC 里）

路径在 `Directory.Build.props` 里改，或者建一个不入库的 `local.props` 覆盖。

```powershell
dotnet build -c Release                     # 编译，产物在 bin\
dotnet build -c Release -p:DeployMod=true   # 编译并直接装进 TerrariaModder\mods\
```

## 目录结构

```
recipe-browser\
├─ Mod.cs                      入口：注册快捷键与配置
├─ Browser.cs                  窗口与四个页面的绘制
├─ Browser.Catalogue.cs        分类 / 排序 / 筛选栏
├─ BrowserData.cs              读取原版配方与掉落数据
├─ Catalogue.cs                分类表、排序表、筛选表
├─ GameRefs.cs                 反射取原版贴图，并加载自带贴图
├─ assets\*.rawimg             模组自带贴图（编进 DLL）
├─ RecipeBrowser.csproj
├─ manifest.json               模组元数据
├─ Directory.Build.props       本机路径配置
├─ Directory.Build.targets     安装到 TerrariaModder 的逻辑
└─ bin\                        编译产物（可直接复制进 mods\，不入库）
```

## 说明

- 分类表、排序、筛选的判定条件都来自原版 `Item` 字段和 `ItemID.Sets`，没有硬编码物品名单
- 界面布局、配色和分类表是照着 tModLoader 版 Recipe Browser 复刻的，`assets/*.rawimg` 贴图也取自原模组；
  如果要公开分发，建议先确认原模组的授权方式
