# Unity Texture Import Manager

Unity 貼圖匯入管理器。

## 安裝

將 `Assets/Editor/TextureImportManager_JSONProfiles.cs` 放進你使用的 Unity 專案的Assets/Editor資料夾內。

unity介面中，工具開啟位置：

`Tools > TextureSetting > 貼圖匯入管理器`

## GitHub 更新機制

工具會讀取：

`https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/version.json`

並下載：

`https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/Assets/Editor/TextureImportManager_JSONProfiles.cs`


## 使用流程
① 建立或載入設定檔
② 選擇貼圖資料夾
③ 新增分類規則，填寫檔名字尾，例如 _BaseColor / _MaterialMap / _Normal
④ 先預覽將套用的貼圖
⑤ 確認無誤後再套用設定


