# 配方浏览器（RecipeBrowser）

TerrariaModder 模组：在游戏内查询合成配方、物品图鉴和怪物图鉴，界面复刻 tModLoader 版 Recipe Browser。

仓库按语言分成两套，源码与成品分开放：

```
recipe-browser\
├─ en-US\   英文版源码：Mod.cs、Browser.cs、Browser.Catalogue.cs、BrowserData.cs、Catalogue.cs、csproj、manifest.json、README.md
│  └─ bin\  英文版成品：RecipeBrowser.dll、manifest.json、README.md（直接复制进 TerrariaModder\mods\recipe-browser\）
├─ zh-CN\   中文版源码（同上）
│  └─ bin\  中文版成品
├─ assets\*.rawimg                      贴图（编进 DLL，两个语言版共用）
├─ GameRefs.cs、GameCraftingMenu.cs     与语言无关的源码（两个版本共用）
├─ Directory.Build.props / .targets     本机路径配置、安装到 TerrariaModder 的逻辑
└─ README.md
```

带界面文字的源码在 `en-US\` 和 `zh-CN\` 里各有一份，其余源码与贴图共用同一份。

## 编译

```powershell
dotnet build zh-CN\RecipeBrowser.zh-CN.csproj -c Release                     # 中文版，成品在 zh-CN\bin\
dotnet build en-US\RecipeBrowser.en-US.csproj -c Release                     # 英文版，成品在 en-US\bin\
dotnet build zh-CN\RecipeBrowser.zh-CN.csproj -c Release -p:DeployMod=true   # 编译并装进 TerrariaModder\mods\
```

需要 Windows、[.NET SDK](https://dotnet.microsoft.com/download)、Terraria 1.4.5、已安装的 TerrariaModder，另外还需要：

- tModLoader 的 `Libraries/ReLogic/1.0.0/ReLogic.dll`
- XNA 4.0 可再发行组件（`Microsoft.Xna.Framework*.dll` 在系统 GAC 里）

路径在 `Directory.Build.props` 里改，或者建一个不入库的 `local.props` 覆盖。

## 说明

- 分类表、排序、筛选的判定条件都来自原版 `Item` 字段和 `ItemID.Sets`，没有硬编码物品名单
- 界面布局、配色和分类表是照着 tModLoader 版 Recipe Browser 复刻的，`assets/*.rawimg` 贴图也取自原模组；公开分发前建议先确认原模组的授权方式
- 两个语言版模组 id 相同（`recipe-browser`），属于同一模组的两个语言包，不能同时安装