# Unity Texture Import Manager

Unity 貼圖匯入管理器。

## 安裝

將 `Assets/Editor/TextureImportManager_JSONProfiles.cs` 放進 Unity 專案。

工具位置：

`Tools > TextureSetting > 貼圖匯入管理器`

## GitHub 更新機制

工具會讀取：

`https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/version.json`

並下載：

`https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/Assets/Editor/TextureImportManager_JSONProfiles.cs`

Repository 必須是 Public。

## 更新流程

1. 修改 `Assets/Editor/TextureImportManager_JSONProfiles.cs`
2. 修改 `version.json` 的 `version` 與 `message`
3. Commit
4. Push
