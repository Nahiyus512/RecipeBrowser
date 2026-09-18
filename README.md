# 配方浏览器（RecipeBrowser）

TerrariaModder 模组：在游戏内查询合成配方、物品图鉴和怪物图鉴

仓库按语言分成两套，源码与成品分开放：

```
recipe-browser\
├─ en-US\   英文版源码：Mod.cs、Browser.cs、Browser.Catalogue.cs、BrowserData.cs、Catalogue.cs、csproj、manifest.json、README.md
│  └─ bin\  英文版成品：RecipeBrowser.dll、manifest.json、README.md
├─ zh-CN\   中文版源码（同上）
│  └─ bin\  中文版成品
├─ assets\*.rawimg                      
├─ GameRefs.cs、GameCraftingMenu.cs     
├─ Directory.Build.props / .targets     
└─ README.md
```


## 编译

```powershell
dotnet build zh-CN\RecipeBrowser.zh-CN.csproj -c Release                     # 中文版，成品在 zh-CN\bin\
dotnet build en-US\RecipeBrowser.en-US.csproj -c Release                     # 英文版，成品在 en-US\bin\
dotnet build zh-CN\RecipeBrowser.zh-CN.csproj -c Release -p:DeployMod=true   # 编译并装进 TerrariaModder\mods\
```

需要 Windows、[.NET SDK](https://dotnet.microsoft.com/download)、Terraria 1.4.5、已安装的 TerrariaModder，另外还需要：

- tModLoader 的 `Libraries/ReLogic/1.0.0/ReLogic.dll`
- XNA 4.0 可再发行组件（`Microsoft.Xna.Framework*.dll` 在系统 GAC 里）


## 说明

- 本模组是 tModLoader 版 RecipeBrowser 的移植版（原模组：https://github.com/JavidPack/RecipeBrowser ），
