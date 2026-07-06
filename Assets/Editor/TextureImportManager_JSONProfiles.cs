#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

public class TextureImportManager_JSONProfiles : EditorWindow
{
    private const string ProfileFolder = "Assets/Editor/TextureImportProfilesJson";
    private const string LastProfileKey = "TA_JSON_TextureImportManager_LastProfile";
    private const string ToolVersion = "v1.0.0";
    private const string RepositoryUrl = "https://github.com/snowwongtw-git/UnityTextureImportManager";
    private const string VersionJsonUrl = "https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/version.json";
    private const string LatestScriptUrl = "https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/Assets/Editor/TextureImportManager_JSONProfiles.cs";

    public enum CompressorQuality
    {
        Fast = 0,
        Normal = 50,
        Best = 100
    }

    public enum ETC2Fallback
    {
        UseBuildSettings = 0,
        Quality32Bit = 1,
        Quality16Bit = 2,
        Quality32BitHalfResolution = 3
    }

    public enum MipmapFiltering
    {
        Box = 0,
        Kaiser = 1
    }

    [Serializable]
    public class Profile
    {
        public string profileName = "NewProfile";
        public string folderPath = "Assets";
        public bool includeSubFolders = true;
        public bool caseInsensitive = true;
        public bool onlyReimportMatched = true;
        public bool showUnmatchedInPreview = false;
        public bool skipReimportIfNoChanges = true;
        public bool disableCrunchCompression = true; // 停用 Crunch 可加快大量貼圖重新匯入速度。
        public List<Rule> rules = new List<Rule>();
    }

    [Serializable]
    public class Rule
    {
        public bool enabled = true;
        public bool foldout = true;
        public string displayName = "New Rule";
        public string matchingSuffix = "_Suffix";

        public TextureImporterType textureType = TextureImporterType.Default;
        public TextureImporterShape textureShape = TextureImporterShape.Texture2D;
        public bool sRGBTexture = true;
        public TextureImporterAlphaSource alphaSource = TextureImporterAlphaSource.FromInput;
        public bool alphaIsTransparency = false;
        public bool ignorePngGamma = false;

        public TextureImporterNPOTScale npotScale = TextureImporterNPOTScale.None;
        public bool readWriteEnabled = false;
        public bool streamingMipmaps = false;
        public bool virtualTextureOnly = false;

        public bool generateMipMaps = true;
        public bool borderMipMaps = false;
        public MipmapFiltering mipMapFiltering = MipmapFiltering.Box;
        public bool mipMapsPreserveCoverage = false;
        public bool fadeoutMipMaps = false;

        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public FilterMode filterMode = FilterMode.Bilinear;
        public int anisoLevel = 1;

        public bool overrideAndroid = true;
        public int androidMaxSize = 2048;
        public TextureResizeAlgorithm androidResizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        public TextureImporterFormat androidFormat = TextureImporterFormat.ASTC_4x4;
        public CompressorQuality androidCompressorQuality = CompressorQuality.Normal;
        public ETC2Fallback androidETC2Fallback = ETC2Fallback.UseBuildSettings;

        public bool overrideIOS = true;
        public int iosMaxSize = 2048;
        public TextureResizeAlgorithm iosResizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        public TextureImporterFormat iosFormat = TextureImporterFormat.ASTC_4x4;
        public CompressorQuality iosCompressorQuality = CompressorQuality.Normal;
    }

    private class ProfileEntry
    {
        public string path;
        public string name;
    }

    private class PreviewItem
    {
        public string path;
        public Rule rule;
    }

    private Profile profile = new Profile();
    private readonly List<ProfileEntry> profileEntries = new List<ProfileEntry>();
    private readonly List<PreviewItem> previewItems = new List<PreviewItem>();
    private int selectedProfileIndex = -1;
    private string currentProfilePath = "";
    private string profileNameEdit = "";
    private Vector2 rulesScroll;
    private Vector2 previewScroll;
    private int lastScannedCount;
    private int lastMatchedCount;

    [MenuItem("Tools/TextureSetting/貼圖匯入管理器")]
    public static void Open()
    {
        TextureImportManager_JSONProfiles window = GetWindow<TextureImportManager_JSONProfiles>("貼圖匯入管理器");
        window.minSize = new Vector2(920, 680);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureProfileFolder();
        ReloadProfileList();
        LoadLastOrFirstProfile();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawProfileBar();
        DrawFolderSettings();

        DrawRuleToolbar();

        rulesScroll = EditorGUILayout.BeginScrollView(rulesScroll);
        DrawRuleList();
        EditorGUILayout.EndScrollView();

        DrawBottomBar();
    }

    private static GUIContent L(string en, string zh, string tooltip = null)
    {
        return new GUIContent(zh, tooltip ?? zh);
    }

    [Serializable]
    private class GitHubVersionInfo
    {
        public string version;
        public string message;
    }

    private string lastUpdateStatus = "尚未檢查更新";

    private void CheckForUpdates()
    {
        EditorUtility.DisplayProgressBar("檢查更新", "正在讀取 GitHub main 分支的 version.json...", 0.35f);

        UnityWebRequest request = UnityWebRequest.Get(VersionJsonUrl);
        request.SetRequestHeader("User-Agent", "UnityTextureImportManager");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        EditorApplication.update += WaitForVersionCheck;

        void WaitForVersionCheck()
        {
            if (!operation.isDone) return;

            EditorApplication.update -= WaitForVersionCheck;
            EditorUtility.ClearProgressBar();

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif

            if (failed)
            {
                lastUpdateStatus = "檢查失敗";
                string error = request.error;
                request.Dispose();

                EditorUtility.DisplayDialog(
                    "檢查更新失敗",
                    "無法讀取 GitHub main 分支的 version.json。\n\n請確認：\n" +
                    "1. Repository 是 Public。\n" +
                    "2. GitHub 根目錄有 version.json。\n" +
                    "3. version.json 已 Commit / Push。\n" +
                    "4. 瀏覽器可直接打開：\n" + VersionJsonUrl +
                    "\n\n錯誤訊息：\n" + error,
                    "OK");
                Repaint();
                return;
            }

            GitHubVersionInfo info = JsonUtility.FromJson<GitHubVersionInfo>(request.downloadHandler.text);
            request.Dispose();

            if (info == null || string.IsNullOrEmpty(info.version))
            {
                lastUpdateStatus = "version.json 格式錯誤";
                EditorUtility.DisplayDialog(
                    "檢查更新失敗",
                    "version.json 格式不正確。\n\n正確格式：\n{\n  \"version\": \"v1.0.1\",\n  \"message\": \"更新內容\"\n}",
                    "OK");
                Repaint();
                return;
            }

            int compare = CompareVersion(NormalizeVersion(info.version), NormalizeVersion(ToolVersion));

            if (compare <= 0)
            {
                lastUpdateStatus = "已是最新版本";
                EditorUtility.DisplayDialog(
                    "已是最新版本",
                    "目前版本：" + ToolVersion + "\nGitHub 版本：" + info.version,
                    "OK");
                Repaint();
                return;
            }

            lastUpdateStatus = "發現新版本：" + info.version;

            bool download = EditorUtility.DisplayDialog(
                "發現新版本",
                "目前版本：" + ToolVersion + "\nGitHub 版本：" + info.version + "\n\n更新內容：\n" +
                (string.IsNullOrEmpty(info.message) ? "未填寫" : info.message) +
                "\n\n是否下載新版並覆蓋目前工具？\n\n工具會先備份舊檔，再覆蓋 .cs，Unity 會重新編譯。",
                "下載更新",
                "取消");

            if (download)
            {
                DownloadAndReplaceTool(info.version);
            }

            Repaint();
        }
    }

    private void DownloadAndReplaceTool(string newVersion)
    {
        MonoScript script = MonoScript.FromScriptableObject(this);
        string currentScriptPath = AssetDatabase.GetAssetPath(script);

        if (string.IsNullOrEmpty(currentScriptPath))
        {
            EditorUtility.DisplayDialog("下載失敗", "找不到目前工具的 .cs 檔路徑。", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("下載更新", "正在下載新版工具...", 0.5f);

        UnityWebRequest request = UnityWebRequest.Get(LatestScriptUrl);
        request.SetRequestHeader("User-Agent", "UnityTextureImportManager");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        EditorApplication.update += WaitForDownload;

        void WaitForDownload()
        {
            if (!operation.isDone) return;

            EditorApplication.update -= WaitForDownload;
            EditorUtility.ClearProgressBar();

#if UNITY_2020_2_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif

            if (failed)
            {
                string error = request.error;
                request.Dispose();

                EditorUtility.DisplayDialog(
                    "下載失敗",
                    "無法下載新版 .cs。\n\n請確認 GitHub main 分支有：\nAssets/Editor/TextureImportManager_JSONProfiles.cs\n\n錯誤訊息：\n" + error,
                    "打開 GitHub");
                Application.OpenURL(RepositoryUrl);
                return;
            }

            string newCode = request.downloadHandler.text;
            request.Dispose();

            if (string.IsNullOrEmpty(newCode) || !newCode.Contains("EditorWindow") || !newCode.Contains("TextureImportManager_JSONProfiles"))
            {
                EditorUtility.DisplayDialog("下載失敗", "下載內容不像本工具的 Unity Editor .cs，已取消覆蓋。", "OK");
                return;
            }

            string fullPath = System.IO.Path.GetFullPath(currentScriptPath);
            string backupPath = fullPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            try
            {
                System.IO.File.Copy(fullPath, backupPath, true);
                System.IO.File.WriteAllText(fullPath, newCode);
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "更新完成",
                    "已更新到：" + newVersion + "\n\n舊檔已備份：\n" + backupPath + "\n\nUnity 會開始重新編譯。",
                    "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("更新失敗", e.Message, "OK");
            }
        }
    }

    private string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return "";
        version = version.Trim();
        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            version = version.Substring(1);
        }
        return version;
    }

    private int CompareVersion(string a, string b)
    {
        Version va;
        Version vb;

        if (!Version.TryParse(a, out va)) va = new Version(0, 0, 0);
        if (!Version.TryParse(b, out vb)) vb = new Version(0, 0, 0);

        return va.CompareTo(vb);
    }

    private void DrawHeader()
    {
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("貼圖匯入管理器 " + ToolVersion, EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("檢查更新", GUILayout.Width(100)))
        {
            CheckForUpdates();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("更新狀態：" + lastUpdateStatus);

        EditorGUILayout.HelpBox(
            "使用流程：\n" +
            "① 建立或載入設定檔\n" +
            "② 選擇貼圖資料夾\n" +
            "③ 新增分類規則，填寫檔名字尾，例如 _BaseColor / _MaterialMap / _Normal\n" +
            "④ 先預覽將套用的貼圖\n" +
            "⑤ 確認無誤後再套用設定\n\n" +
            "設定檔位置：Assets/Editor/TextureImportProfilesJson。本工具不會修改 Unity Inspector 的 Mipmap Limit。檢查更新需要 GitHub Repository 是 Public。",
            MessageType.Info);
    }

    private void DrawProfileBar()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("設定檔", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        string[] names = BuildProfileNames();

        if (names.Length == 0)
        {
            EditorGUILayout.LabelField(L("Current Profile", "目前設定檔"), new GUIContent("沒有設定檔"));
        }
        else
        {
            if (selectedProfileIndex < 0 || selectedProfileIndex >= names.Length) selectedProfileIndex = 0;

            EditorGUI.BeginChangeCheck();
            selectedProfileIndex = EditorGUILayout.Popup(L("Current Profile", "目前設定檔"), selectedProfileIndex, names);
            if (EditorGUI.EndChangeCheck())
            {
                LoadProfile(profileEntries[selectedProfileIndex].path);
            }
        }

        if (GUILayout.Button("重新整理", GUILayout.Width(90)))
        {
            ReloadProfileList();
            LoadLastOrFirstProfile();
        }

        if (GUILayout.Button("新增", GUILayout.Width(90))) NewProfile();
        if (GUILayout.Button("複製", GUILayout.Width(110))) DuplicateProfile();
        if (GUILayout.Button("刪除", GUILayout.Width(90))) DeleteProfile();
        if (GUILayout.Button("顯示檔案", GUILayout.Width(120))) RevealProfile();
        if (GUILayout.Button("載入JSON", GUILayout.Width(90))) LoadExistingJsonFile();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("設定檔位置", string.IsNullOrEmpty(currentProfilePath) ? "尚未儲存" : currentProfilePath);

        EditorGUILayout.BeginHorizontal();
        profileNameEdit = EditorGUILayout.TextField(L("Profile Name", "設定檔名稱"), profileNameEdit);
        if (GUILayout.Button("套用名稱", GUILayout.Width(150))) ApplyProfileName();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("改名後請按「套用名稱」，工具會同步重新命名 JSON 檔。", MessageType.None);

        EditorGUILayout.EndVertical();
    }

    private void DrawFolderSettings()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("資料夾設定", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        profile.folderPath = EditorGUILayout.TextField(L("Folder Path", "目標資料夾"), profile.folderPath);
        if (GUILayout.Button("選擇資料夾", GUILayout.Width(100))) SelectFolder();
        if (GUILayout.Button("使用目前選取", GUILayout.Width(220))) UseProjectSelection();
        EditorGUILayout.EndHorizontal();

        profile.includeSubFolders = EditorGUILayout.ToggleLeft(L("Include Sub Folders", "包含子資料夾"), profile.includeSubFolders);
        profile.caseInsensitive = EditorGUILayout.ToggleLeft(L("Case Insensitive", "忽略大小寫"), profile.caseInsensitive);
        profile.onlyReimportMatched = EditorGUILayout.ToggleLeft(L("Only Reimport Matched", "僅重匯符合規則的貼圖"), profile.onlyReimportMatched);

        EditorGUILayout.HelpBox("掃描範圍只會限定在 Folder Path 指定資料夾內。Include Sub Folders 開啟才會包含子資料夾，不會掃描整個專案。", MessageType.None);

        if (profile.folderPath == "Assets")
        {
            EditorGUILayout.HelpBox("目前 Folder Path 是 Assets 根目錄，可能會掃描過多貼圖。建議指定更精準的資料夾，例如 Assets/BundleSources/Artifact/0604/Texture。", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawRuleToolbar()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();

        int count = profile.rules != null ? profile.rules.Count : 0;
        EditorGUILayout.LabelField("貼圖分類規則 [" + count + "]", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("+ 新增分類", GUILayout.Width(150)))
        {
            if (profile.rules == null) profile.rules = new List<Rule>();
            profile.rules.Add(new Rule());
        }

        if (GUILayout.Button("插入範例", GUILayout.Width(210)))
        {
            if (EditorUtility.DisplayDialog("插入範例規則", "這只是範例，不代表你的專案規範。確定要插入 BaseColor / Emissive / MaterialMap / Normal？", "插入", "取消"))
            {
                if (profile.rules == null) profile.rules = new List<Rule>();
                profile.rules.AddRange(CreateSampleRules());
            }
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawRuleList()
    {
        EditorGUILayout.BeginVertical("box");

        if (profile.rules == null || profile.rules.Count == 0)
        {
            EditorGUILayout.HelpBox("目前沒有分類規則。請按上方「+ 新增分類」。\n「檔名字尾」是真正用來比對檔名的欄位，例如：_BaseColor、_MaterialMap、_Normal。", MessageType.Info);
        }
        else
        {
            for (int i = 0; i < profile.rules.Count; i++)
            {
                DrawRule(i, profile.rules[i]);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawRule(int index, Rule rule)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();

        rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(20));
        rule.foldout = EditorGUILayout.Foldout(rule.foldout, rule.displayName + "  |  檔名字尾: " + rule.matchingSuffix, true);
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("↑", GUILayout.Width(28)) && index > 0)
        {
            Rule temp = profile.rules[index - 1];
            profile.rules[index - 1] = profile.rules[index];
            profile.rules[index] = temp;
        }

        if (GUILayout.Button("↓", GUILayout.Width(28)) && index < profile.rules.Count - 1)
        {
            Rule temp = profile.rules[index + 1];
            profile.rules[index + 1] = profile.rules[index];
            profile.rules[index] = temp;
        }

        if (GUILayout.Button("刪除", GUILayout.Width(100)))
        {
            profile.rules.RemoveAt(index);
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.EndHorizontal();

        if (!rule.foldout)
        {
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUI.indentLevel++;

        rule.displayName = EditorGUILayout.TextField(L("Display Name", "分類名稱"), rule.displayName);
        rule.matchingSuffix = EditorGUILayout.TextField(L("Matching Suffix", "檔名字尾"), rule.matchingSuffix);

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Unity 貼圖設定", EditorStyles.boldLabel);
        rule.textureType = (TextureImporterType)EditorGUILayout.EnumPopup(new GUIContent("Texture Type"), rule.textureType);
        rule.textureShape = (TextureImporterShape)EditorGUILayout.EnumPopup(new GUIContent("Texture Shape"), rule.textureShape);
        rule.sRGBTexture = EditorGUILayout.Toggle(new GUIContent("sRGB (Color Texture)"), rule.sRGBTexture);
        rule.alphaSource = (TextureImporterAlphaSource)EditorGUILayout.EnumPopup(new GUIContent("Alpha Source"), rule.alphaSource);
        rule.alphaIsTransparency = EditorGUILayout.Toggle(new GUIContent("Alpha Is Transparency"), rule.alphaIsTransparency);
        rule.ignorePngGamma = EditorGUILayout.Toggle(new GUIContent("Ignore PNG Gamma"), rule.ignorePngGamma);

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("進階設定", EditorStyles.boldLabel);
        rule.npotScale = (TextureImporterNPOTScale)EditorGUILayout.EnumPopup(new GUIContent("Non Power of 2"), rule.npotScale);
        rule.readWriteEnabled = EditorGUILayout.Toggle(new GUIContent("Read/Write Enabled"), rule.readWriteEnabled);
        rule.streamingMipmaps = EditorGUILayout.Toggle(new GUIContent("Streaming Mipmaps"), rule.streamingMipmaps);
        rule.virtualTextureOnly = EditorGUILayout.Toggle(new GUIContent("Virtual Texture Only"), rule.virtualTextureOnly);
        rule.generateMipMaps = EditorGUILayout.Toggle(new GUIContent("Generate Mip Maps"), rule.generateMipMaps);

        using (new EditorGUI.DisabledScope(!rule.generateMipMaps))
        {
            EditorGUI.indentLevel++;
            rule.borderMipMaps = EditorGUILayout.Toggle(new GUIContent("Border Mip Maps"), rule.borderMipMaps);
            rule.mipMapFiltering = (MipmapFiltering)EditorGUILayout.EnumPopup(new GUIContent("Mip Map Filtering"), rule.mipMapFiltering);
            rule.mipMapsPreserveCoverage = EditorGUILayout.Toggle(new GUIContent("Mip Maps Preserve Coverage"), rule.mipMapsPreserveCoverage);
            rule.fadeoutMipMaps = EditorGUILayout.Toggle(new GUIContent("Fadeout Mip Maps"), rule.fadeoutMipMaps);
            EditorGUI.indentLevel--;
        }

        rule.wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup(new GUIContent("Wrap Mode"), rule.wrapMode);
        rule.filterMode = (FilterMode)EditorGUILayout.EnumPopup(new GUIContent("Filter Mode"), rule.filterMode);
        rule.anisoLevel = EditorGUILayout.IntSlider(new GUIContent("Aniso Level"), rule.anisoLevel, 0, 16);

        EditorGUILayout.Space(5);
        DrawPlatformSettings("Android", true, ref rule.overrideAndroid, ref rule.androidMaxSize, ref rule.androidResizeAlgorithm, ref rule.androidFormat, ref rule.androidCompressorQuality, ref rule.androidETC2Fallback);
        DrawPlatformSettings("iPhone", false, ref rule.overrideIOS, ref rule.iosMaxSize, ref rule.iosResizeAlgorithm, ref rule.iosFormat, ref rule.iosCompressorQuality, ref rule.androidETC2Fallback);

        EditorGUI.indentLevel--;
        EditorGUILayout.EndVertical();
    }

    private void DrawPlatformSettings(string platform, bool showETC2, ref bool overridden, ref int maxSize, ref TextureResizeAlgorithm resizeAlgorithm, ref TextureImporterFormat format, ref CompressorQuality quality, ref ETC2Fallback fallback)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField(platform, EditorStyles.boldLabel);
        overridden = EditorGUILayout.Toggle(new GUIContent("Override For " + platform), overridden);

        using (new EditorGUI.DisabledScope(!overridden))
        {
            maxSize = EditorGUILayout.IntPopup(
                new GUIContent("Max Size"),
                maxSize,
                new[] { new GUIContent("256"), new GUIContent("512"), new GUIContent("1024"), new GUIContent("2048"), new GUIContent("4096"), new GUIContent("8192") },
                new[] { 256, 512, 1024, 2048, 4096, 8192 });

            resizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup(new GUIContent("Resize Algorithm"), resizeAlgorithm);
            format = (TextureImporterFormat)EditorGUILayout.EnumPopup(new GUIContent("Format"), format);
            quality = DrawCompressorQuality(quality);

            if (showETC2)
            {
                fallback = DrawETC2Fallback(fallback);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private CompressorQuality DrawCompressorQuality(CompressorQuality current)
    {
        string[] labels = { "Fast（快速）", "Normal（一般）", "Best（最佳）" };
        int index = current == CompressorQuality.Fast ? 0 : current == CompressorQuality.Best ? 2 : 1;
        index = EditorGUILayout.Popup(new GUIContent("Compressor Quality"), index, labels);
        return index == 0 ? CompressorQuality.Fast : index == 2 ? CompressorQuality.Best : CompressorQuality.Normal;
    }

    private ETC2Fallback DrawETC2Fallback(ETC2Fallback current)
    {
        string[] labels = { "Use build settings（使用建置設定）", "32-bit（32 位元）", "16-bit（16 位元）", "32-bit (half resolution)（32 位元半解析度）" };
        int index = current == ETC2Fallback.Quality32Bit ? 1 : current == ETC2Fallback.Quality16Bit ? 2 : current == ETC2Fallback.Quality32BitHalfResolution ? 3 : 0;
        index = EditorGUILayout.Popup(new GUIContent("Override ETC2 Fallback"), index, labels);
        if (index == 1) return ETC2Fallback.Quality32Bit;
        if (index == 2) return ETC2Fallback.Quality16Bit;
        if (index == 3) return ETC2Fallback.Quality32BitHalfResolution;
        return ETC2Fallback.UseBuildSettings;
    }

    private bool ValidateProfileBeforePreviewOrApply(bool isApply)
    {
        if (profile == null)
        {
            EditorUtility.DisplayDialog("沒有設定檔", "請先建立或載入一個 JSON Profile。", "OK");
            return false;
        }

        if (string.IsNullOrEmpty(profile.folderPath) || !AssetDatabase.IsValidFolder(profile.folderPath))
        {
            EditorUtility.DisplayDialog(
                "Folder Path 錯誤",
                "請先設定有效的 Assets 資料夾路徑。\n\n建議：在 Project 視窗選取目標資料夾，再按 Use Project Selection。",
                "OK");
            return false;
        }

        if (profile.rules == null || profile.rules.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "沒有規則",
                "目前 Profile 沒有任何 Rule。\n\n請按 + Add Rule 新增規則，或按 Insert Sample Rules 插入範例後再調整。",
                "OK");
            return false;
        }

        bool hasEnabledRule = false;
        List<string> warnings = new List<string>();

        for (int i = 0; i < profile.rules.Count; i++)
        {
            Rule rule = profile.rules[i];
            if (rule == null || !rule.enabled) continue;

            hasEnabledRule = true;

            if (string.IsNullOrWhiteSpace(rule.matchingSuffix))
            {
                warnings.Add("第 " + (i + 1) + " 條：Matching Suffix 是空白");
            }
            else if (!rule.matchingSuffix.StartsWith("_"))
            {
                warnings.Add(rule.displayName + "：建議 Matching Suffix 以底線開頭，例如 _BaseColor");
            }
        }

        if (!hasEnabledRule)
        {
            EditorUtility.DisplayDialog("沒有啟用的規則", "所有 Rule 都是關閉狀態。請至少啟用一條 Rule。", "OK");
            return false;
        }

        if (warnings.Count > 0)
        {
            bool continueAnyway = EditorUtility.DisplayDialog(
                "規則可能有問題",
                string.Join("\n", warnings.ToArray()) + "\n\n仍要繼續" + (isApply ? "套用" : "預覽") + "嗎？",
                "繼續",
                "取消");

            if (!continueAnyway) return false;
        }

        return true;
    }

    private bool ConfirmApply(int scannedCount, int matchedCount)
    {
        if (matchedCount <= 0)
        {
            EditorUtility.DisplayDialog("沒有符合規則的貼圖", "目前沒有任何貼圖符合規則，不會套用任何設定。", "OK");
            return false;
        }

        string message =
            "即將套用貼圖匯入規則：\n\n" +
            "目標資料夾：\n" + profile.folderPath + "\n\n" +
            "掃描貼圖：" + scannedCount + " 張\n" +
            "符合規則的貼圖：" + matchedCount + " 張\n\n" +
            "只有符合規則的貼圖會被修改，且啟用 無變更不重新匯入 時，無變更貼圖會略過。\n\n" +
            "確定要套用嗎？";

        return EditorUtility.DisplayDialog("確認套用", message, "套用", "取消");
    }

    private void DrawBottomBar()
    {
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField("效能", EditorStyles.boldLabel);

        profile.skipReimportIfNoChanges = EditorGUILayout.ToggleLeft(
            "無變更不重新匯入",
            profile.skipReimportIfNoChanges);

        profile.disableCrunchCompression = EditorGUILayout.ToggleLeft(
            "停用 Crunch（加快匯入）",
            profile.disableCrunchCompression);

        profile.onlyReimportMatched = EditorGUILayout.ToggleLeft(
            "只處理符合規則的貼圖",
            profile.onlyReimportMatched);

        EditorGUILayout.HelpBox(
            "建議保持三個選項開啟：可避免不必要的重新匯入，並減少 Crunch 壓縮造成的等待時間。",
            MessageType.None);

        if (string.IsNullOrEmpty(currentProfilePath))
        {
            EditorGUILayout.HelpBox("目前設定檔尚未儲存。按「儲存設定」後會建立 JSON 檔。", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("預覽將套用的貼圖", GUILayout.Height(34))) BuildPreview();
        if (GUILayout.Button("套用設定", GUILayout.Height(34))) ApplyRules();
        if (GUILayout.Button("儲存設定", GUILayout.Height(34), GUILayout.Width(120))) SaveProfile();
        EditorGUILayout.EndHorizontal();

        if (lastScannedCount > 0)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("預覽結果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("掃描資料夾：" + profile.folderPath);
            EditorGUILayout.LabelField("掃描：" + lastScannedCount + "    符合規則：" + lastMatchedCount + "    未符合：" + Mathf.Max(0, lastScannedCount - lastMatchedCount));

            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(150));
            foreach (PreviewItem item in previewItems)
            {
                EditorGUILayout.LabelField(Path.GetFileName(item.path), "→ " + item.rule.displayName + " / " + item.rule.matchingSuffix);
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    private void BuildPreview()
    {
        if (!ValidateProfileBeforePreviewOrApply(false)) return;

        SaveProfile(false);
        previewItems.Clear();

        string[] paths = FindTexturePathsFast(false);
        lastScannedCount = paths.Length;
        lastMatchedCount = 0;

        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (i % 200 == 0)
                {
                    EditorUtility.DisplayProgressBar("預覽符合規則的貼圖", "Scanning " + i + "/" + paths.Length, paths.Length == 0 ? 1f : (float)i / paths.Length);
                }

                Rule rule = FindMatchedRule(paths[i]);
                if (rule != null)
                {
                    lastMatchedCount++;
                    previewItems.Add(new PreviewItem { path = paths[i], rule = rule });
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private void ApplyRules()
    {
        if (!ValidateProfileBeforePreviewOrApply(true)) return;

        SaveProfile(false);

        string[] paths = FindTexturePathsFast(false);
        int preMatched = 0;
        foreach (string p in paths)
        {
            if (FindMatchedRule(p) != null) preMatched++;
        }

        if (!ConfirmApply(paths.Length, preMatched)) return;

        int matched = 0;
        int reimported = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];

                if (i % 50 == 0)
                {
                    EditorUtility.DisplayProgressBar("套用規則", i + "/" + paths.Length + " " + Path.GetFileName(path), paths.Length == 0 ? 1f : (float)i / paths.Length);
                }

                Rule rule = FindMatchedRule(path);
                if (rule == null) continue;
                matched++;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool hasChanges = HasDifferences(importer, rule);
                if (profile.skipReimportIfNoChanges && !hasChanges)
                {
                    skipped++;
                    continue;
                }

                ApplyRule(importer, rule);
                importer.SaveAndReimport();
                reimported++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("完成", "掃描：" + paths.Length + "\n符合規則：" + matched + "\n重新匯入：" + reimported + "\n略過未變更：" + skipped, "OK");
        BuildPreview();
    }

    private bool HasDifferences(TextureImporter importer, Rule rule)
    {
        if (importer.textureType != rule.textureType) return true;
        if (importer.textureShape != rule.textureShape) return true;
        if (importer.sRGBTexture != rule.sRGBTexture) return true;
        if (importer.alphaSource != rule.alphaSource) return true;
        if (importer.alphaIsTransparency != rule.alphaIsTransparency) return true;
        if (importer.ignorePngGamma != rule.ignorePngGamma) return true;
        if (importer.npotScale != rule.npotScale) return true;
        if (importer.isReadable != rule.readWriteEnabled) return true;
        if (importer.streamingMipmaps != rule.streamingMipmaps) return true;
        if (importer.vtOnly != rule.virtualTextureOnly) return true;
        if (importer.mipmapEnabled != rule.generateMipMaps) return true;
        if (GetBoolProperty(importer, "borderMipmap") != rule.borderMipMaps) return true;
        if (GetBoolProperty(importer, "mipMapsPreserveCoverage") != rule.mipMapsPreserveCoverage) return true;
        if (GetBoolProperty(importer, "fadeOut") != rule.fadeoutMipMaps && GetBoolProperty(importer, "fadeout") != rule.fadeoutMipMaps) return true;
        if (profile.disableCrunchCompression && GetBoolProperty(importer, "crunchedCompression")) return true;
        if (importer.wrapMode != rule.wrapMode) return true;
        if (importer.filterMode != rule.filterMode) return true;
        if (importer.anisoLevel != rule.anisoLevel) return true;
        if (HasPlatformDifferences(importer, "Android", rule.overrideAndroid, rule.androidMaxSize, rule.androidResizeAlgorithm, rule.androidFormat, rule.androidCompressorQuality)) return true;
        if (HasPlatformDifferences(importer, "iPhone", rule.overrideIOS, rule.iosMaxSize, rule.iosResizeAlgorithm, rule.iosFormat, rule.iosCompressorQuality)) return true;
        return false;
    }

    private bool HasPlatformDifferences(TextureImporter importer, string platformName, bool overridden, int maxSize, TextureResizeAlgorithm resizeAlgorithm, TextureImporterFormat format, CompressorQuality quality)
    {
        TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platformName);
        if (s.overridden != overridden) return true;
        if (!overridden) return false;
        if (s.maxTextureSize != maxSize) return true;
        if (s.resizeAlgorithm != resizeAlgorithm) return true;
        if (s.format != format) return true;
        if (s.compressionQuality != (int)quality) return true;
        if (profile.disableCrunchCompression && GetBoolProperty(s, "crunchedCompression")) return true;
        return false;
    }

    private void ApplyRule(TextureImporter importer, Rule rule)
    {
        importer.textureType = rule.textureType;
        importer.textureShape = rule.textureShape;
        importer.sRGBTexture = rule.sRGBTexture;
        importer.alphaSource = rule.alphaSource;
        importer.alphaIsTransparency = rule.alphaIsTransparency;
        importer.ignorePngGamma = rule.ignorePngGamma;
        importer.npotScale = rule.npotScale;
        importer.isReadable = rule.readWriteEnabled;
        importer.streamingMipmaps = rule.streamingMipmaps;
        importer.vtOnly = rule.virtualTextureOnly;
        importer.mipmapEnabled = rule.generateMipMaps;
        importer.wrapMode = rule.wrapMode;
        importer.filterMode = rule.filterMode;
        importer.anisoLevel = rule.anisoLevel;

        TrySetBoolProperty(importer, "borderMipmap", rule.borderMipMaps);
        TrySetMipmapFiltering(importer, rule.mipMapFiltering);
        TrySetBoolProperty(importer, "mipMapsPreserveCoverage", rule.mipMapsPreserveCoverage);
        TrySetBoolProperty(importer, "fadeOut", rule.fadeoutMipMaps);
        TrySetBoolProperty(importer, "fadeout", rule.fadeoutMipMaps);

        if (profile.disableCrunchCompression) TrySetBoolProperty(importer, "crunchedCompression", false);

        ApplyPlatform(importer, "Android", rule.overrideAndroid, rule.androidMaxSize, rule.androidResizeAlgorithm, rule.androidFormat, rule.androidCompressorQuality, rule.androidETC2Fallback);
        ApplyPlatform(importer, "iPhone", rule.overrideIOS, rule.iosMaxSize, rule.iosResizeAlgorithm, rule.iosFormat, rule.iosCompressorQuality, ETC2Fallback.UseBuildSettings);
    }

    private void ApplyPlatform(TextureImporter importer, string platformName, bool overridden, int maxSize, TextureResizeAlgorithm resizeAlgorithm, TextureImporterFormat format, CompressorQuality quality, ETC2Fallback fallback)
    {
        TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platformName);
        s.name = platformName;
        s.overridden = overridden;
        s.maxTextureSize = maxSize;
        s.resizeAlgorithm = resizeAlgorithm;
        s.format = format;
        s.textureCompression = TextureImporterCompression.Compressed;
        s.compressionQuality = (int)quality;

        if (profile.disableCrunchCompression) TrySetBoolProperty(s, "crunchedCompression", false);

        TrySetAndroidETC2Fallback(s, fallback);
        importer.SetPlatformTextureSettings(s);
    }

    private string[] FindTexturePathsFast(bool verifyTextureImporter)
    {
        if (profile == null || !AssetDatabase.IsValidFolder(profile.folderPath)) return Array.Empty<string>();

        string fullFolderPath = Path.GetFullPath(profile.folderPath).Replace("\\", "/");
        if (!Directory.Exists(fullFolderPath)) return Array.Empty<string>();

        SearchOption option = profile.includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] extensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr" };
        List<string> paths = new List<string>();

        foreach (string file in Directory.EnumerateFiles(fullFolderPath, "*.*", option))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            bool ok = false;
            foreach (string valid in extensions)
            {
                if (ext == valid)
                {
                    ok = true;
                    break;
                }
            }

            if (!ok) continue;

            string assetPath = file.Replace("\\", "/");
            int idx = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) assetPath = assetPath.Substring(idx + 1);
            else if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

            if (verifyTextureImporter && !(AssetImporter.GetAtPath(assetPath) is TextureImporter)) continue;
            paths.Add(assetPath);
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths.ToArray();
    }

    private Rule FindMatchedRule(string assetPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(assetPath);
        if (profile.caseInsensitive) fileName = fileName.ToLowerInvariant();

        foreach (Rule rule in profile.rules)
        {
            if (rule == null || !rule.enabled || string.IsNullOrEmpty(rule.matchingSuffix)) continue;
            string suffix = profile.caseInsensitive ? rule.matchingSuffix.ToLowerInvariant() : rule.matchingSuffix;
            if (fileName.EndsWith(suffix)) return rule;
        }

        return null;
    }

    private void SelectFolder()
    {
        string selected = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
        if (string.IsNullOrEmpty(selected)) return;

        selected = selected.Replace("\\", "/");
        string dataPath = Application.dataPath.Replace("\\", "/");

        if (!selected.StartsWith(dataPath))
        {
            EditorUtility.DisplayDialog("資料夾錯誤", "請選擇 Assets 底下的資料夾。", "OK");
            return;
        }

        profile.folderPath = "Assets" + selected.Substring(dataPath.Length);
    }

    private void UseProjectSelection()
    {
        UnityEngine.Object[] selected = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("沒有選取", "請先在 Project 視窗選一個資料夾或圖片。", "OK");
            return;
        }

        foreach (UnityEngine.Object obj in selected)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;
            path = path.Replace("\\", "/");

            if (File.Exists(path))
            {
                path = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(path)) path = path.Replace("\\", "/");
            }

            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            {
                profile.folderPath = path;
                EditorUtility.DisplayDialog("已設定資料夾", "Folder Path 已設定為：\n" + profile.folderPath, "OK");
                return;
            }
        }

        EditorUtility.DisplayDialog("選取錯誤", "請選擇 Assets 內的資料夾或圖片。", "OK");
    }

    private void EnsureProfileFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
        if (!AssetDatabase.IsValidFolder(ProfileFolder)) AssetDatabase.CreateFolder("Assets/Editor", "TextureImportProfilesJson");
    }

    private void ReloadProfileList()
    {
        profileEntries.Clear();
        EnsureProfileFolder();

        string full = Path.GetFullPath(ProfileFolder);
        if (!Directory.Exists(full)) return;

        foreach (string file in Directory.GetFiles(full, "*.json", SearchOption.TopDirectoryOnly))
        {
            string assetPath = ToAssetPath(file);
            profileEntries.Add(new ProfileEntry
            {
                path = assetPath,
                name = Path.GetFileNameWithoutExtension(file)
            });
        }

        profileEntries.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
    }

    private string[] BuildProfileNames()
    {
        string[] names = new string[profileEntries.Count];
        for (int i = 0; i < profileEntries.Count; i++)
        {
            names[i] = profileEntries[i].name + ".json";
        }
        return names;
    }

    private void LoadLastOrFirstProfile()
    {
        string last = EditorPrefs.GetString(LastProfileKey, "");
        if (!string.IsNullOrEmpty(last) && File.Exists(ToFullPath(last)))
        {
            LoadProfile(last);
            return;
        }

        if (profileEntries.Count > 0)
        {
            LoadProfile(profileEntries[0].path);
        }
        else
        {
            NewProfile();
        }
    }

    private void LoadProfile(string assetPath)
    {
        string fullPath = ToFullPath(assetPath);
        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("讀取失敗", "找不到 JSON：\n" + assetPath, "OK");
            return;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            Profile loaded = JsonUtility.FromJson<Profile>(json);
            if (loaded == null) throw new Exception("JSON 內容無法轉成 Profile。");

            if (loaded.rules == null) loaded.rules = new List<Rule>();

            profile = loaded;
            currentProfilePath = assetPath;
            profileNameEdit = profile.profileName;
            selectedProfileIndex = profileEntries.FindIndex(x => x.path == assetPath);
            EditorPrefs.SetString(LastProfileKey, assetPath);
            Repaint();
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("讀取失敗", e.Message, "OK");
        }
    }

    private void SaveProfile(bool showDialog = true)
    {
        EnsureProfileFolder();

        if (string.IsNullOrEmpty(currentProfilePath))
        {
            currentProfilePath = ProfileFolder + "/" + MakeSafeFileName(profile.profileName) + ".json";
        }

        string fullPath = ToFullPath(currentProfilePath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        profile.profileName = string.IsNullOrEmpty(profile.profileName) ? "NewProfile" : profile.profileName;

        string json = JsonUtility.ToJson(profile, true);
        File.WriteAllText(fullPath, json);

        AssetDatabase.ImportAsset(currentProfilePath);
        AssetDatabase.Refresh();

        ReloadProfileList();
        selectedProfileIndex = profileEntries.FindIndex(x => x.path == currentProfilePath);
        EditorPrefs.SetString(LastProfileKey, currentProfilePath);

        if (showDialog) EditorUtility.DisplayDialog("完成", "Profile 已儲存：\n" + currentProfilePath, "OK");
    }

    private void NewProfile()
    {
        profile = new Profile();
        profile.profileName = "NewProfile";
        profile.rules = new List<Rule>();
        profileNameEdit = profile.profileName;

        string path = GetUniqueJsonPath(profile.profileName);
        currentProfilePath = path;
        SaveProfile(false);
        LoadProfile(path);
    }

    private void DuplicateProfile()
    {
        SaveProfile(false);

        Profile copy = JsonUtility.FromJson<Profile>(JsonUtility.ToJson(profile));
        copy.profileName = profile.profileName + "_Copy";
        profile = copy;
        profileNameEdit = copy.profileName;
        currentProfilePath = GetUniqueJsonPath(copy.profileName);
        SaveProfile();
        LoadProfile(currentProfilePath);
    }

    private void DeleteProfile()
    {
        if (string.IsNullOrEmpty(currentProfilePath)) return;

        if (!EditorUtility.DisplayDialog("刪除 Profile", "確定刪除？\n" + currentProfilePath, "刪除", "取消")) return;

        string fullPath = ToFullPath(currentProfilePath);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        string meta = fullPath + ".meta";
        if (File.Exists(meta)) File.Delete(meta);

        currentProfilePath = "";
        AssetDatabase.Refresh();
        ReloadProfileList();
        LoadLastOrFirstProfile();
    }

    private void LoadExistingJsonFile()
    {
        string selected = EditorUtility.OpenFilePanel(
            "Load Existing Texture Import Profile JSON",
            Application.dataPath,
            "json");

        if (string.IsNullOrEmpty(selected)) return;

        selected = selected.Replace("\\", "/");

        try
        {
            string json = File.ReadAllText(selected);
            Profile loaded = JsonUtility.FromJson<Profile>(json);
            if (loaded == null)
            {
                EditorUtility.DisplayDialog("載入失敗", "這個 JSON 無法轉成 Texture Import Profile。", "OK");
                return;
            }

            if (loaded.rules == null) loaded.rules = new List<Rule>();
            if (string.IsNullOrEmpty(loaded.profileName))
            {
                loaded.profileName = Path.GetFileNameWithoutExtension(selected);
            }

            EnsureProfileFolder();

            string targetAssetPath;

            string projectRoot = Path.GetFullPath(".").Replace("\\", "/");
            string selectedFull = Path.GetFullPath(selected).Replace("\\", "/");

            if (selectedFull.StartsWith(projectRoot + "/Assets/", StringComparison.OrdinalIgnoreCase))
            {
                targetAssetPath = ToAssetPath(selectedFull);
            }
            else
            {
                targetAssetPath = GetUniqueJsonPath(loaded.profileName);
                File.Copy(selectedFull, ToFullPath(targetAssetPath), true);
                AssetDatabase.ImportAsset(targetAssetPath);
                AssetDatabase.Refresh();
            }

            profile = loaded;
            currentProfilePath = targetAssetPath;
            profileNameEdit = profile.profileName;

            SaveProfile(false);
            ReloadProfileList();

            int index = profileEntries.FindIndex(x => x.path == currentProfilePath);
            if (index >= 0) selectedProfileIndex = index;

            EditorPrefs.SetString(LastProfileKey, currentProfilePath);
            EditorUtility.DisplayDialog("載入完成", "已載入 JSON Profile：\n" + currentProfilePath, "OK");
            Repaint();
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("載入失敗", e.Message, "OK");
        }
    }

    private void RevealProfile()
    {
        if (string.IsNullOrEmpty(currentProfilePath)) return;

        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(currentProfilePath);
        if (obj != null) EditorGUIUtility.PingObject(obj);
    }

    private void ApplyProfileName()
    {
        string safe = MakeSafeFileName(profileNameEdit);
        if (string.IsNullOrEmpty(safe))
        {
            EditorUtility.DisplayDialog("名稱錯誤", "Profile Name 不能是空白。", "OK");
            return;
        }

        profile.profileName = safe;

        string newPath = ProfileFolder + "/" + safe + ".json";
        if (newPath != currentProfilePath)
        {
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            string oldFull = ToFullPath(currentProfilePath);
            string newFull = ToFullPath(newPath);

            SaveProfile(false);

            if (File.Exists(oldFull))
            {
                File.Move(oldFull, newFull);
                string oldMeta = oldFull + ".meta";
                if (File.Exists(oldMeta)) File.Delete(oldMeta);
            }

            currentProfilePath = newPath;
        }

        SaveProfile(false);
        ReloadProfileList();
        LoadProfile(currentProfilePath);
        EditorUtility.DisplayDialog("完成", "Profile 已改名：\n" + currentProfilePath, "OK");
    }

    private string GetUniqueJsonPath(string name)
    {
        string path = ProfileFolder + "/" + MakeSafeFileName(name) + ".json";
        string full = ToFullPath(path);

        if (!File.Exists(full)) return path;

        int index = 1;
        while (true)
        {
            string candidate = ProfileFolder + "/" + MakeSafeFileName(name) + " " + index + ".json";
            if (!File.Exists(ToFullPath(candidate))) return candidate;
            index++;
        }
    }

    private string ToFullPath(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return "";
        if (assetPath.StartsWith("Assets"))
        {
            return Path.GetFullPath(assetPath);
        }
        return assetPath;
    }

    private string ToAssetPath(string fullPath)
    {
        fullPath = fullPath.Replace("\\", "/");
        int idx = fullPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return fullPath.Substring(idx + 1);
        if (fullPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return fullPath;
        return fullPath;
    }

    private static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) name = "NewProfile";
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private List<Rule> CreateSampleRules()
    {
        return new List<Rule>
        {
            new Rule
            {
                displayName = "BaseColor",
                matchingSuffix = "_BaseColor",
                textureType = TextureImporterType.Default,
                sRGBTexture = true,
                generateMipMaps = true,
                androidMaxSize = 2048,
                androidFormat = TextureImporterFormat.ASTC_4x4,
                iosMaxSize = 2048,
                iosFormat = TextureImporterFormat.ASTC_4x4
            },
            new Rule
            {
                displayName = "Emissive",
                matchingSuffix = "_Emissive",
                textureType = TextureImporterType.Default,
                sRGBTexture = true,
                generateMipMaps = true,
                androidMaxSize = 2048,
                androidFormat = TextureImporterFormat.ASTC_4x4,
                iosMaxSize = 2048,
                iosFormat = TextureImporterFormat.ASTC_4x4
            },
            new Rule
            {
                displayName = "MaterialMap",
                matchingSuffix = "_MaterialMap",
                textureType = TextureImporterType.Default,
                sRGBTexture = false,
                generateMipMaps = true,
                androidMaxSize = 2048,
                androidFormat = TextureImporterFormat.ASTC_6x6,
                iosMaxSize = 2048,
                iosFormat = TextureImporterFormat.ASTC_6x6
            },
            new Rule
            {
                displayName = "Normal",
                matchingSuffix = "_Normal",
                textureType = TextureImporterType.NormalMap,
                sRGBTexture = false,
                generateMipMaps = true,
                androidMaxSize = 1024,
                androidFormat = TextureImporterFormat.ASTC_4x4,
                iosMaxSize = 1024,
                iosFormat = TextureImporterFormat.ASTC_4x4
            }
        };
    }

    private bool GetBoolProperty(object target, string propertyName)
    {
        var prop = target.GetType().GetProperty(propertyName);
        if (prop == null || !prop.CanRead || prop.PropertyType != typeof(bool)) return false;
        try { return (bool)prop.GetValue(target, null); }
        catch { return false; }
    }

    private void TrySetBoolProperty(object target, string propertyName, bool value)
    {
        var prop = target.GetType().GetProperty(propertyName);
        if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(bool)) return;
        try { prop.SetValue(target, value, null); }
        catch { }
    }

    private void TrySetMipmapFiltering(TextureImporter importer, MipmapFiltering filtering)
    {
        var prop = typeof(TextureImporter).GetProperty("mipmapFilter");
        if (prop == null || !prop.CanWrite) return;

        try
        {
            string enumName = filtering == MipmapFiltering.Kaiser ? "KaiserFilter" : "BoxFilter";
            object value = Enum.Parse(prop.PropertyType, enumName);
            prop.SetValue(importer, value, null);
        }
        catch { }
    }

    private void TrySetAndroidETC2Fallback(TextureImporterPlatformSettings settings, ETC2Fallback fallback)
    {
        var prop = typeof(TextureImporterPlatformSettings).GetProperty("androidETC2FallbackOverride");
        if (prop == null || !prop.CanWrite) return;

        string enumName = "UseBuildSettings";
        if (fallback == ETC2Fallback.Quality32Bit) enumName = "Quality32Bit";
        else if (fallback == ETC2Fallback.Quality16Bit) enumName = "Quality16Bit";
        else if (fallback == ETC2Fallback.Quality32BitHalfResolution) enumName = "Quality32BitDownscaled";

        try
        {
            object value = Enum.Parse(prop.PropertyType, enumName);
            prop.SetValue(settings, value, null);
        }
        catch { }
    }
}
#endif
