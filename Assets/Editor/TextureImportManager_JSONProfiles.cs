#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Snow.TextureImportManager
{
public enum CompressorQuality { Fast = 0, Normal = 50, Best = 100 }
    public enum ETC2Fallback { UseBuildSettings = 0, Quality32Bit = 1, Quality16Bit = 2, Quality32BitHalfResolution = 3 }
    public enum MipmapFiltering { Box = 0, Kaiser = 1 }

    [Serializable]
    public class TextureImportProfile
    {
        public string profileName = "NewProfile";
        public string folderPath = "Assets";
        public bool includeSubFolders = true;
        public bool caseInsensitive = true;
        public bool skipReimportIfNoChanges = true;
        public bool disableCrunchCompression = true;
        public List<TextureImportRule> rules = new List<TextureImportRule>();
    }

    [Serializable]
    public class TextureImportRule
    {
        public bool enabled = true;
        public bool foldout = true;
        public string displayName = "New Rule";
        public string matchingSuffix = "_Suffix";

        public TextureImporterType textureType = TextureImporterType.Default;
        public TextureImporterShape textureShape = TextureImporterShape.Texture2D;
        public bool sRGBTexture = true;
        public TextureImporterAlphaSource alphaSource = TextureImporterAlphaSource.FromInput;
        public bool alphaIsTransparency;
        public bool ignorePngGamma;

        public TextureImporterNPOTScale npotScale = TextureImporterNPOTScale.None;
        public bool readWriteEnabled;
        public bool streamingMipmaps;
        public bool virtualTextureOnly;

        public bool generateMipMaps = true;
        public bool borderMipMaps;
        public MipmapFiltering mipMapFiltering = MipmapFiltering.Box;
        public bool mipMapsPreserveCoverage;
        public bool fadeoutMipMaps;

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

    public sealed class TextureCheckItem
    {
        public string assetPath;
        public TextureImportRule matchedRule;
        public bool IsMatched => matchedRule != null;
    }

    public sealed class TextureCheckResult
    {
        public readonly List<TextureCheckItem> matched = new List<TextureCheckItem>();
        public readonly List<TextureCheckItem> unmatched = new List<TextureCheckItem>();
        public int ScannedCount => matched.Count + unmatched.Count;
    }

    public sealed class TextureApplyResult
    {
        public int scanned;
        public int matched;
        public int reimported;
        public int skipped;
        public readonly List<string> failed = new List<string>();
    }

    public enum QAIssueSeverity { Info, Warning, Error }

    public sealed class TextureQAIssue
    {
        public string assetPath;
        public string message;
        public QAIssueSeverity severity;
    }

    public sealed class TextureQAResult
    {
        public readonly List<TextureQAIssue> issues = new List<TextureQAIssue>();
        public int ErrorCount => issues.FindAll(x => x.severity == QAIssueSeverity.Error).Count;
        public int WarningCount => issues.FindAll(x => x.severity == QAIssueSeverity.Warning).Count;
    }

public static class TextureNamingChecker
    {
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".hdr"
        };

        public static TextureCheckResult Check(TextureImportProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!AssetDatabase.IsValidFolder(profile.folderPath))
                throw new DirectoryNotFoundException("無效的 Unity 資料夾：" + profile.folderPath);

            var result = new TextureCheckResult();
            foreach (string path in FindTexturePaths(profile.folderPath, profile.includeSubFolders))
            {
                TextureImportRule rule = FindMatchedRule(path, profile);
                var item = new TextureCheckItem { assetPath = path, matchedRule = rule };
                if (rule == null) result.unmatched.Add(item);
                else result.matched.Add(item);
            }
            return result;
        }

        public static TextureImportRule FindMatchedRule(string assetPath, TextureImportProfile profile)
        {
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            StringComparison comparison = profile.caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (profile.rules == null) return null;

            foreach (TextureImportRule rule in profile.rules)
            {
                if (rule == null || !rule.enabled || string.IsNullOrWhiteSpace(rule.matchingSuffix)) continue;
                if (fileName.EndsWith(rule.matchingSuffix, comparison)) return rule;
            }
            return null;
        }

        public static string[] FindTexturePaths(string folderPath, bool includeSubFolders)
        {
            string fullFolder = Path.GetFullPath(folderPath).Replace("\\", "/");
            if (!Directory.Exists(fullFolder)) return Array.Empty<string>();

            SearchOption option = includeSubFolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var paths = new List<string>();

            foreach (string file in Directory.EnumerateFiles(fullFolder, "*.*", option))
            {
                if (!Extensions.Contains(Path.GetExtension(file))) continue;

                string assetPath = file.Replace("\\", "/");
                int index = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (index >= 0) assetPath = assetPath.Substring(index + 1);
                else if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

                paths.Add(assetPath);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths.ToArray();
        }
    }

public static class TextureImportApplier
    {
        public static TextureApplyResult Apply(TextureImportProfile profile, TextureCheckResult checkResult)
        {
            var result = new TextureApplyResult
            {
                scanned = checkResult.ScannedCount,
                matched = checkResult.matched.Count
            };

            foreach (TextureCheckItem item in checkResult.matched)
            {
                TextureImporter importer = AssetImporter.GetAtPath(item.assetPath) as TextureImporter;
                if (importer == null)
                {
                    result.failed.Add(item.assetPath + "：不是 TextureImporter");
                    continue;
                }

                try
                {
                    bool changed = HasDifferences(importer, item.matchedRule, profile);
                    if (profile.skipReimportIfNoChanges && !changed)
                    {
                        result.skipped++;
                        continue;
                    }

                    ApplyRule(importer, item.matchedRule, profile);
                    importer.SaveAndReimport();
                    result.reimported++;
                }
                catch (Exception e)
                {
                    result.failed.Add(item.assetPath + "：" + e.Message);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return result;
        }

        public static bool HasDifferences(TextureImporter importer, TextureImportRule rule, TextureImportProfile profile)
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
            if (GetBool(importer, "borderMipmap") != rule.borderMipMaps) return true;
            if (GetBool(importer, "mipMapsPreserveCoverage") != rule.mipMapsPreserveCoverage) return true;
            if (GetBool(importer, "fadeOut") != rule.fadeoutMipMaps && GetBool(importer, "fadeout") != rule.fadeoutMipMaps) return true;
            if (profile.disableCrunchCompression && GetBool(importer, "crunchedCompression")) return true;
            if (importer.wrapMode != rule.wrapMode) return true;
            if (importer.filterMode != rule.filterMode) return true;
            if (importer.anisoLevel != rule.anisoLevel) return true;
            if (PlatformDiff(importer, "Android", rule.overrideAndroid, rule.androidMaxSize, rule.androidResizeAlgorithm, rule.androidFormat, rule.androidCompressorQuality, profile)) return true;
            if (PlatformDiff(importer, "iPhone", rule.overrideIOS, rule.iosMaxSize, rule.iosResizeAlgorithm, rule.iosFormat, rule.iosCompressorQuality, profile)) return true;
            return false;
        }

        private static bool PlatformDiff(TextureImporter importer, string platform, bool overridden, int maxSize,
            TextureResizeAlgorithm resize, TextureImporterFormat format, CompressorQuality quality, TextureImportProfile profile)
        {
            TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
            if (s.overridden != overridden) return true;
            if (!overridden) return false;
            if (s.maxTextureSize != maxSize || s.resizeAlgorithm != resize || s.format != format || s.compressionQuality != (int)quality) return true;
            return profile.disableCrunchCompression && GetBool(s, "crunchedCompression");
        }

        public static void ApplyRule(TextureImporter importer, TextureImportRule rule, TextureImportProfile profile)
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

            SetBool(importer, "borderMipmap", rule.borderMipMaps);
            SetEnum(importer, "mipmapFilter", rule.mipMapFiltering == MipmapFiltering.Kaiser ? "KaiserFilter" : "BoxFilter");
            SetBool(importer, "mipMapsPreserveCoverage", rule.mipMapsPreserveCoverage);
            SetBool(importer, "fadeOut", rule.fadeoutMipMaps);
            SetBool(importer, "fadeout", rule.fadeoutMipMaps);
            if (profile.disableCrunchCompression) SetBool(importer, "crunchedCompression", false);

            ApplyPlatform(importer, "Android", rule.overrideAndroid, rule.androidMaxSize, rule.androidResizeAlgorithm,
                rule.androidFormat, rule.androidCompressorQuality, rule.androidETC2Fallback, profile);
            ApplyPlatform(importer, "iPhone", rule.overrideIOS, rule.iosMaxSize, rule.iosResizeAlgorithm,
                rule.iosFormat, rule.iosCompressorQuality, ETC2Fallback.UseBuildSettings, profile);
        }

        private static void ApplyPlatform(TextureImporter importer, string platform, bool overridden, int maxSize,
            TextureResizeAlgorithm resize, TextureImporterFormat format, CompressorQuality quality,
            ETC2Fallback fallback, TextureImportProfile profile)
        {
            TextureImporterPlatformSettings s = importer.GetPlatformTextureSettings(platform);
            s.name = platform;
            s.overridden = overridden;
            s.maxTextureSize = maxSize;
            s.resizeAlgorithm = resize;
            s.format = format;
            s.textureCompression = TextureImporterCompression.Compressed;
            s.compressionQuality = (int)quality;
            if (profile.disableCrunchCompression) SetBool(s, "crunchedCompression", false);

            if (platform == "Android")
            {
                string enumName = "UseBuildSettings";
                if (fallback == ETC2Fallback.Quality32Bit) enumName = "Quality32Bit";
                else if (fallback == ETC2Fallback.Quality16Bit) enumName = "Quality16Bit";
                else if (fallback == ETC2Fallback.Quality32BitHalfResolution) enumName = "Quality32BitDownscaled";
                SetEnum(s, "androidETC2FallbackOverride", enumName);
            }

            importer.SetPlatformTextureSettings(s);
        }

        private static bool GetBool(object target, string name)
        {
            PropertyInfo p = target.GetType().GetProperty(name);
            if (p == null || !p.CanRead || p.PropertyType != typeof(bool)) return false;
            try { return (bool)p.GetValue(target, null); } catch { return false; }
        }

        private static void SetBool(object target, string name, bool value)
        {
            PropertyInfo p = target.GetType().GetProperty(name);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool)) return;
            try { p.SetValue(target, value, null); } catch { }
        }

        private static void SetEnum(object target, string name, string enumName)
        {
            PropertyInfo p = target.GetType().GetProperty(name);
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) return;
            try { p.SetValue(target, Enum.Parse(p.PropertyType, enumName), null); } catch { }
        }
    }

public static class TextureImportQA
    {
        public static TextureQAResult Run(TextureImportProfile profile, TextureCheckResult checkResult)
        {
            var result = new TextureQAResult();

            foreach (TextureCheckItem item in checkResult.unmatched)
            {
                result.issues.Add(new TextureQAIssue
                {
                    assetPath = item.assetPath,
                    severity = QAIssueSeverity.Error,
                    message = "命名不符合任何已啟用規則"
                });
            }

            foreach (TextureCheckItem item in checkResult.matched)
            {
                TextureImporter importer = AssetImporter.GetAtPath(item.assetPath) as TextureImporter;
                if (importer == null) continue;

                if (TextureImportApplier.HasDifferences(importer, item.matchedRule, profile))
                {
                    result.issues.Add(new TextureQAIssue
                    {
                        assetPath = item.assetPath,
                        severity = QAIssueSeverity.Warning,
                        message = "Texture Import Settings 與規則不一致"
                    });
                }
            }

            return result;
        }
    }

public static class TextureImportReport
    {
        private const int MaxDialogItems = 20;

        public static string BuildCombinedSummary(TextureCheckResult check, TextureQAResult qa)
        {
            var sb = new StringBuilder();
            sb.AppendLine("貼圖檢查完成");
            sb.AppendLine();
            sb.AppendLine("掃描貼圖：" + check.ScannedCount + " 張");
            sb.AppendLine("命名不符合：" + check.unmatched.Count + " 張");
            sb.AppendLine("設定不符合：" + qa.WarningCount + " 張");

            if (check.unmatched.Count == 0 && qa.WarningCount == 0)
            {
                sb.AppendLine();
                sb.AppendLine("所有貼圖皆符合規範。");
                return sb.ToString();
            }

            if (check.unmatched.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("命名問題：");
                int count = check.unmatched.Count > MaxDialogItems ? MaxDialogItems : check.unmatched.Count;
                for (int i = 0; i < count; i++)
                    sb.AppendLine("• " + Path.GetFileName(check.unmatched[i].assetPath));
                if (check.unmatched.Count > MaxDialogItems)
                    sb.AppendLine("…另外還有 " + (check.unmatched.Count - MaxDialogItems) + " 張。");
            }

            int shown = 0;
            foreach (TextureQAIssue issue in qa.issues)
            {
                if (issue.severity != QAIssueSeverity.Warning) continue;
                if (shown == 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("設定問題：");
                }
                if (shown >= MaxDialogItems) break;
                sb.AppendLine("• " + Path.GetFileName(issue.assetPath) + "：" + issue.message);
                shown++;
            }

            if (qa.WarningCount > MaxDialogItems)
                sb.AppendLine("…另外還有 " + (qa.WarningCount - MaxDialogItems) + " 張。");

            return sb.ToString();
        }

        public static void ShowCombinedDialog(TextureCheckResult check, TextureQAResult qa)
        {
            EditorUtility.DisplayDialog("貼圖檢查", BuildCombinedSummary(check, qa), "OK");
        }
    }

public class TextureImportManager_JSONProfiles : EditorWindow
    {
        private const string ProfileFolder = "Assets/Editor/TextureImportProfilesJson";
        private const string LastProfileKey = "Snow_TIM_LastProfile";
        private const string ToolVersion = "v1.1.2";
        private const string RepositoryUrl = "https://github.com/snowwongtw-git/UnityTextureImportManager";
        private const string VersionJsonUrl = "https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/version.json";
        private const string LatestScriptUrl = "https://raw.githubusercontent.com/snowwongtw-git/UnityTextureImportManager/main/Assets/Editor/TextureImportManager_JSONProfiles.cs";

        private TextureImportProfile profile = new TextureImportProfile();
        private string currentProfilePath = "";
        private Vector2 ruleScroll;
        private Vector2 resultScroll;
        private TextureCheckResult lastCheck;
        private TextureQAResult lastQA;
        private readonly List<string> profilePaths = new List<string>();
        private int selectedProfile;
        private string updateStatus = "尚未檢查更新";

        [Serializable]
        private class VersionInfo
        {
            public string version;
            public string message;
        }

        [MenuItem("Tools/TextureSetting/貼圖匯入管理器")]
        public static void Open()
        {
            var window = GetWindow<TextureImportManager_JSONProfiles>("貼圖匯入管理器");
            window.minSize = new Vector2(900, 680);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureProfileFolder();
            ReloadProfiles();
            LoadLastOrFirst();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawProfile();
            DrawFolder();
            DrawRules();
            DrawActions();
            DrawResults();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("貼圖匯入管理器 " + ToolVersion, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("檢查更新", GUILayout.Width(100))) CheckForUpdates();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("更新狀態：" + updateStatus);
            EditorGUILayout.HelpBox(
                "單檔 AI Ready 架構：檢查、套用、QA、報告皆可獨立呼叫，但正式更新只覆蓋這一個檔案。",
                MessageType.Info);
        }

        private void DrawProfile()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("設定檔", EditorStyles.boldLabel);

            string[] names = new string[profilePaths.Count];
            for (int i = 0; i < names.Length; i++) names[i] = Path.GetFileNameWithoutExtension(profilePaths[i]);

            EditorGUILayout.BeginHorizontal();
            if (names.Length > 0)
            {
                int newIndex = EditorGUILayout.Popup("目前設定檔", Mathf.Clamp(selectedProfile, 0, names.Length - 1), names);
                if (newIndex != selectedProfile)
                {
                    selectedProfile = newIndex;
                    LoadProfile(profilePaths[selectedProfile]);
                }
            }
            else EditorGUILayout.LabelField("目前設定檔", "沒有設定檔");

            if (GUILayout.Button("新增", GUILayout.Width(70))) NewProfile();
            if (GUILayout.Button("儲存", GUILayout.Width(70))) SaveProfile(true);
            if (GUILayout.Button("重新整理", GUILayout.Width(90))) ReloadProfiles();
            EditorGUILayout.EndHorizontal();

            profile.profileName = EditorGUILayout.TextField("設定檔名稱", profile.profileName);
            EditorGUILayout.LabelField("檔案位置", string.IsNullOrEmpty(currentProfilePath) ? "尚未儲存" : currentProfilePath);
            EditorGUILayout.EndVertical();
        }

        private void DrawFolder()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("資料夾設定", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            profile.folderPath = EditorGUILayout.TextField("目標資料夾", profile.folderPath);
            if (GUILayout.Button("使用目前選取", GUILayout.Width(120))) UseSelection();
            EditorGUILayout.EndHorizontal();

            profile.includeSubFolders = EditorGUILayout.ToggleLeft("包含子資料夾", profile.includeSubFolders);
            profile.caseInsensitive = EditorGUILayout.ToggleLeft("忽略大小寫", profile.caseInsensitive);
            EditorGUILayout.EndVertical();
        }

        private void DrawRules()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("貼圖分類規則 [" + (profile.rules == null ? 0 : profile.rules.Count) + "]", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 新增規則", GUILayout.Width(100)))
            {
                if (profile.rules == null) profile.rules = new List<TextureImportRule>();
                profile.rules.Add(new TextureImportRule());
            }
            EditorGUILayout.EndHorizontal();

            ruleScroll = EditorGUILayout.BeginScrollView(ruleScroll, GUILayout.Height(330));
            if (profile.rules != null)
            {
                for (int i = 0; i < profile.rules.Count; i++) DrawRule(i, profile.rules[i]);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRule(int index, TextureImportRule rule)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            rule.enabled = EditorGUILayout.Toggle(rule.enabled, GUILayout.Width(20));
            rule.foldout = EditorGUILayout.Foldout(rule.foldout, rule.displayName + " | " + rule.matchingSuffix, true);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刪除", GUILayout.Width(60)))
            {
                profile.rules.RemoveAt(index);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (rule.foldout)
            {
                rule.displayName = EditorGUILayout.TextField("分類名稱", rule.displayName);
                rule.matchingSuffix = EditorGUILayout.TextField("檔名字尾", rule.matchingSuffix);
                rule.textureType = (TextureImporterType)EditorGUILayout.EnumPopup("Texture Type", rule.textureType);
                rule.textureShape = (TextureImporterShape)EditorGUILayout.EnumPopup("Texture Shape", rule.textureShape);
                rule.sRGBTexture = EditorGUILayout.Toggle("sRGB (Color Texture)", rule.sRGBTexture);
                rule.alphaSource = (TextureImporterAlphaSource)EditorGUILayout.EnumPopup("Alpha Source", rule.alphaSource);
                rule.alphaIsTransparency = EditorGUILayout.Toggle("Alpha Is Transparency", rule.alphaIsTransparency);
                rule.ignorePngGamma = EditorGUILayout.Toggle("Ignore PNG Gamma", rule.ignorePngGamma);
                rule.npotScale = (TextureImporterNPOTScale)EditorGUILayout.EnumPopup("Non Power of 2", rule.npotScale);
                rule.readWriteEnabled = EditorGUILayout.Toggle("Read/Write Enabled", rule.readWriteEnabled);
                rule.streamingMipmaps = EditorGUILayout.Toggle("Streaming Mipmaps", rule.streamingMipmaps);
                rule.virtualTextureOnly = EditorGUILayout.Toggle("Virtual Texture Only", rule.virtualTextureOnly);
                rule.generateMipMaps = EditorGUILayout.Toggle("Generate Mip Maps", rule.generateMipMaps);
                rule.wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup("Wrap Mode", rule.wrapMode);
                rule.filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", rule.filterMode);
                rule.anisoLevel = EditorGUILayout.IntSlider("Aniso Level", rule.anisoLevel, 0, 16);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Android", EditorStyles.boldLabel);
                rule.overrideAndroid = EditorGUILayout.Toggle("Override", rule.overrideAndroid);
                rule.androidMaxSize = EditorGUILayout.IntField("Max Size", rule.androidMaxSize);
                rule.androidResizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup("Resize Algorithm", rule.androidResizeAlgorithm);
                rule.androidFormat = (TextureImporterFormat)EditorGUILayout.EnumPopup("Format", rule.androidFormat);
                rule.androidCompressorQuality = (CompressorQuality)EditorGUILayout.EnumPopup("Compressor Quality", rule.androidCompressorQuality);
                rule.androidETC2Fallback = (ETC2Fallback)EditorGUILayout.EnumPopup("ETC2 Fallback", rule.androidETC2Fallback);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("iPhone", EditorStyles.boldLabel);
                rule.overrideIOS = EditorGUILayout.Toggle("Override", rule.overrideIOS);
                rule.iosMaxSize = EditorGUILayout.IntField("Max Size", rule.iosMaxSize);
                rule.iosResizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup("Resize Algorithm", rule.iosResizeAlgorithm);
                rule.iosFormat = (TextureImporterFormat)EditorGUILayout.EnumPopup("Format", rule.iosFormat);
                rule.iosCompressorQuality = (CompressorQuality)EditorGUILayout.EnumPopup("Compressor Quality", rule.iosCompressorQuality);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActions()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("效能", EditorStyles.boldLabel);
            profile.skipReimportIfNoChanges = EditorGUILayout.ToggleLeft("無變更不重新匯入", profile.skipReimportIfNoChanges);
            profile.disableCrunchCompression = EditorGUILayout.ToggleLeft("停用 Crunch（加快匯入）", profile.disableCrunchCompression);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("檢查貼圖", GUILayout.Height(32))) RunCombinedCheck();
            if (GUILayout.Button("套用設定", GUILayout.Height(32))) RunApply();
            if (GUILayout.Button("儲存設定", GUILayout.Height(32))) SaveProfile(true);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawResults()
        {
            if (lastCheck == null && lastQA == null) return;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("檢查結果", EditorStyles.boldLabel);

            if (lastCheck != null)
            {
                EditorGUILayout.LabelField(
                    "掃描：" + lastCheck.ScannedCount +
                    "　命名不符合：" + lastCheck.unmatched.Count +
                    "　設定不符合：" + (lastQA == null ? 0 : lastQA.WarningCount));

                resultScroll = EditorGUILayout.BeginScrollView(resultScroll, GUILayout.Height(160));

                foreach (TextureCheckItem item in lastCheck.unmatched)
                    DrawResultRow(item.assetPath, "命名不符合");

                if (lastQA != null)
                {
                    foreach (TextureQAIssue issue in lastQA.issues)
                    {
                        if (issue.severity == QAIssueSeverity.Warning)
                            DrawResultRow(issue.assetPath, "設定不符合");
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawResultRow(string assetPath, string issue)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(issue + "｜" + assetPath);
            if (GUILayout.Button("定位", GUILayout.Width(50)))
            {
                UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool ValidateProfile()
        {
            if (profile == null || !AssetDatabase.IsValidFolder(profile.folderPath))
            {
                EditorUtility.DisplayDialog("設定錯誤", "請先設定有效的 Assets 資料夾。", "OK");
                return false;
            }

            if (profile.rules == null || profile.rules.FindAll(x =>
                x != null && x.enabled && !string.IsNullOrWhiteSpace(x.matchingSuffix)).Count == 0)
            {
                EditorUtility.DisplayDialog("設定錯誤", "請至少建立一條有效且啟用的命名規則。", "OK");
                return false;
            }
            return true;
        }

        private void RunCombinedCheck()
        {
            if (!ValidateProfile()) return;
            SaveProfile(false);
            lastCheck = TextureNamingChecker.Check(profile);
            lastQA = TextureImportQA.Run(profile, lastCheck);
            TextureImportReport.ShowCombinedDialog(lastCheck, lastQA);
            Repaint();
        }

        private void RunApply()
        {
            if (!ValidateProfile()) return;
            lastCheck = TextureNamingChecker.Check(profile);

            if (lastCheck.matched.Count == 0)
            {
                EditorUtility.DisplayDialog("沒有可套用貼圖", "沒有任何貼圖符合規則。", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "確認套用",
                "掃描：" + lastCheck.ScannedCount +
                "\n符合規則：" + lastCheck.matched.Count +
                "\n命名不符合：" + lastCheck.unmatched.Count +
                "\n\n只會處理符合規則的貼圖。",
                "套用",
                "取消");
            if (!confirmed) return;

            TextureApplyResult result = TextureImportApplier.Apply(profile, lastCheck);
            EditorUtility.DisplayDialog(
                "套用完成",
                "掃描：" + result.scanned +
                "\n符合規則：" + result.matched +
                "\n重新匯入：" + result.reimported +
                "\n略過未變更：" + result.skipped +
                "\n失敗：" + result.failed.Count,
                "OK");
            Repaint();
        }

        private void EnsureProfileFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Editor")) AssetDatabase.CreateFolder("Assets", "Editor");
            if (!AssetDatabase.IsValidFolder(ProfileFolder)) AssetDatabase.CreateFolder("Assets/Editor", "TextureImportProfilesJson");
        }

        private void ReloadProfiles()
        {
            profilePaths.Clear();
            EnsureProfileFolder();

            string full = Path.GetFullPath(ProfileFolder);
            if (Directory.Exists(full))
            {
                foreach (string file in Directory.GetFiles(full, "*.json", SearchOption.TopDirectoryOnly))
                    profilePaths.Add(ToAssetPath(file));
            }

            profilePaths.Sort(StringComparer.OrdinalIgnoreCase);
            Repaint();
        }

        private void LoadLastOrFirst()
        {
            string last = EditorPrefs.GetString(LastProfileKey, "");
            if (!string.IsNullOrEmpty(last) && File.Exists(Path.GetFullPath(last))) LoadProfile(last);
            else if (profilePaths.Count > 0) LoadProfile(profilePaths[0]);
            else NewProfile();
        }

        private void NewProfile()
        {
            profile = new TextureImportProfile();
            currentProfilePath = AssetDatabase.GenerateUniqueAssetPath(ProfileFolder + "/NewProfile.json");
            SaveProfile(false);
            ReloadProfiles();
            LoadProfile(currentProfilePath);
        }

        private void SaveProfile(bool showDialog)
        {
            EnsureProfileFolder();
            if (string.IsNullOrEmpty(currentProfilePath))
                currentProfilePath = AssetDatabase.GenerateUniqueAssetPath(ProfileFolder + "/" + SafeName(profile.profileName) + ".json");

            File.WriteAllText(Path.GetFullPath(currentProfilePath), JsonUtility.ToJson(profile, true));
            AssetDatabase.ImportAsset(currentProfilePath);
            AssetDatabase.Refresh();
            EditorPrefs.SetString(LastProfileKey, currentProfilePath);
            ReloadProfiles();

            if (showDialog) EditorUtility.DisplayDialog("完成", "設定已儲存：\n" + currentProfilePath, "OK");
        }

        private void LoadProfile(string assetPath)
        {
            TextureImportProfile loaded =
                JsonUtility.FromJson<TextureImportProfile>(File.ReadAllText(Path.GetFullPath(assetPath)));

            if (loaded == null) return;
            if (loaded.rules == null) loaded.rules = new List<TextureImportRule>();

            profile = loaded;
            currentProfilePath = assetPath;
            selectedProfile = profilePaths.IndexOf(assetPath);
            EditorPrefs.SetString(LastProfileKey, assetPath);
            Repaint();
        }

        private void UseSelection()
        {
            UnityEngine.Object obj = Selection.activeObject;
            if (obj == null) return;

            string path = AssetDatabase.GetAssetPath(obj);
            if (File.Exists(path))
            {
                string dir = Path.GetDirectoryName(path);
                path = string.IsNullOrEmpty(dir) ? path : dir.Replace("\\", "/");
            }

            if (AssetDatabase.IsValidFolder(path)) profile.folderPath = path;
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) value = "NewProfile";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value;
        }

        private static string ToAssetPath(string fullPath)
        {
            fullPath = fullPath.Replace("\\", "/");
            int index = fullPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? fullPath.Substring(index + 1) : fullPath;
        }
        private void CheckForUpdates()
        {
            updateStatus = "檢查中";
            UnityWebRequest request = UnityWebRequest.Get(VersionJsonUrl);
            request.SetRequestHeader("User-Agent", "UnityTextureImportManager");
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            EditorApplication.update += WaitForCheck;

            void WaitForCheck()
            {
                if (!operation.isDone) return;
                EditorApplication.update -= WaitForCheck;

#if UNITY_2020_2_OR_NEWER
                bool failed = request.result != UnityWebRequest.Result.Success;
#else
                bool failed = request.isNetworkError || request.isHttpError;
#endif
                if (failed)
                {
                    updateStatus = "檢查失敗";
                    string error = request.error;
                    request.Dispose();
                    EditorUtility.DisplayDialog("檢查更新失敗", error, "OK");
                    Repaint();
                    return;
                }

                VersionInfo info = JsonUtility.FromJson<VersionInfo>(request.downloadHandler.text);
                request.Dispose();

                if (info == null || string.IsNullOrEmpty(info.version))
                {
                    updateStatus = "version.json 格式錯誤";
                    EditorUtility.DisplayDialog("檢查更新失敗", "version.json 格式錯誤。", "OK");
                    Repaint();
                    return;
                }

                Version local = ParseVersion(ToolVersion);
                Version remote = ParseVersion(info.version);

                if (remote <= local)
                {
                    updateStatus = "已是最新版本";
                    EditorUtility.DisplayDialog(
                        "已是最新版本",
                        "目前版本：" + ToolVersion + "\nGitHub 版本：" + info.version,
                        "OK");
                    Repaint();
                    return;
                }

                updateStatus = "發現新版本：" + info.version;
                bool download = EditorUtility.DisplayDialog(
                    "發現新版本",
                    "目前版本：" + ToolVersion +
                    "\nGitHub 版本：" + info.version +
                    "\n\n更新內容：\n" + (string.IsNullOrEmpty(info.message) ? "未填寫" : info.message) +
                    "\n\n是否下載並覆蓋正式單檔？",
                    "下載更新",
                    "取消");

                if (download) DownloadAndReplace(info.version);
                Repaint();
            }
        }

        private void DownloadAndReplace(string newVersion)
        {
            MonoScript script = MonoScript.FromScriptableObject(this);
            string assetPath = AssetDatabase.GetAssetPath(script);

            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("更新失敗", "找不到目前工具腳本。", "OK");
                return;
            }

            UnityWebRequest request = UnityWebRequest.Get(LatestScriptUrl);
            request.SetRequestHeader("User-Agent", "UnityTextureImportManager");
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            EditorApplication.update += WaitForDownload;

            void WaitForDownload()
            {
                if (!operation.isDone) return;
                EditorApplication.update -= WaitForDownload;

#if UNITY_2020_2_OR_NEWER
                bool failed = request.result != UnityWebRequest.Result.Success;
#else
                bool failed = request.isNetworkError || request.isHttpError;
#endif
                if (failed)
                {
                    string error = request.error;
                    request.Dispose();
                    EditorUtility.DisplayDialog("更新失敗", error, "打開 GitHub");
                    Application.OpenURL(RepositoryUrl);
                    return;
                }

                string code = request.downloadHandler.text;
                request.Dispose();

                if (string.IsNullOrEmpty(code) ||
                    !code.Contains("TextureImportManager_JSONProfiles") ||
                    !code.Contains("EditorWindow"))
                {
                    EditorUtility.DisplayDialog("更新失敗", "下載內容不是正式工具檔。", "OK");
                    return;
                }

                string fullPath = Path.GetFullPath(assetPath);
                string backupPath = fullPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                try
                {
                    File.Copy(fullPath, backupPath, true);
                    File.WriteAllText(fullPath, code);
                    AssetDatabase.Refresh();

                    EditorUtility.DisplayDialog(
                        "更新完成",
                        "已更新到：" + newVersion +
                        "\n\n舊檔備份：\n" + backupPath +
                        "\n\nUnity 會重新編譯。",
                        "OK");
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("更新失敗", e.Message, "OK");
                }
            }
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrEmpty(value)) return new Version(0, 0, 0);
            value = value.TrimStart('v', 'V');
            Version parsed;
            return Version.TryParse(value, out parsed) ? parsed : new Version(0, 0, 0);
        }

    }
}
#endif
