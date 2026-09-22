using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Nebula.Modules;
using Nebula.Modules.MetaWidget;
using Nebula.Modules.GUIWidget;
using Nebula.Utilities;
using UnityEngine;
using Virial.Media;
using Virial.Text;

namespace Toa.MoreRolesInPolus.Scripts.Settings;

/// <summary>
/// MRIPバージョン更新マネージャー
/// </summary>
public static class MRIPModUpdater
{
    private const string GitHubOwner = "decimu";
    private const string GitHubRepo = "MoreRolesInPolus";
    private const int PerPage = 30;
    
    private static string GetReleasesUrl(int page) => $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases?per_page={PerPage}&page={page}";
    
    private static int NextPage = 1;
    private static List<ReleasedInfo>? _cache = null;
    
    public static List<ReleasedInfo>? Cache => _cache;
    public static bool MaybeNoMorePages { get; private set; } = false;
    
    /// <summary>
    /// キャッシュをリセット
    /// </summary>
    public static void ResetCache()
    {
        _cache = null;
        NextPage = 1;
        MaybeNoMorePages = false;
    }
    
    /// <summary>
    /// アドオン読み込み直後に古いMRIPファイルを削除
    /// </summary>
    public static void CleanupOldAddonFiles()
    {
        try
        {
            string addonsPath = PathHelpers.GameRootPath + Path.DirectorySeparatorChar + "Addons";
            if (!Directory.Exists(addonsPath)) return;
            
            if (MRIPInfo.Addon == null) return;
            
            var filesToDelete = new List<string>();
            
            foreach (string file in Directory.GetFiles(addonsPath))
            {
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file).ToLower();
                
                if ((ext == ".zip" || ext == ".old") && fileName.Contains(MRIPInfo.AddonId))
                {
                    filesToDelete.Add(file);
                }
            }
            
            if (filesToDelete.Count <= 1) return;
            
            filesToDelete.Sort((a, b) =>
            {
                int countA = Path.GetFileName(a).TakeWhile(c => c == '!').Count();
                int countB = Path.GetFileName(b).TakeWhile(c => c == '!').Count();
                return countB.CompareTo(countA);
            });
            
            for (int i = 1; i < filesToDelete.Count; i++)
            {
                try
                {
                    File.Delete(filesToDelete[i]);
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }
    
    public enum ReleaseCategory
    {
        Major,
        Snapshot,
        Unknown
    }
    
    public static readonly Virial.Color[] CategoryColors = {
        new Virial.Color(176f / 255f, 204f / 255f, 251f / 255f),
        new Virial.Color(247f / 255f, 255f / 255f, 29f / 255f),
        new Virial.Color(141f / 255f, 141f / 255f, 141f / 255f)
    };
    
    public static readonly string[] CategoryNames = {
        "settings.mrip.category.stable",
        "settings.mrip.category.snapshot",
        "settings.mrip.category.unknown"
    };
    
    /// <summary>
    /// リリース情報クラス
    /// </summary>
    public class ReleasedInfo
    {
        public ReleaseCategory Category { get; private set; }
        public string DisplayVersion { get; private set; }
        public string RawTag { get; private set; }
        public string? Body { get; private set; }
        public string? DownloadUrl { get; private set; }
        public string VersionForCompare { get; private set; }
        public string VersionForFileName { get; private set; }
        
        public ReleasedInfo(string tag, string? body, string? downloadUrl)
        {
            RawTag = tag;
            Body = body;
            DownloadUrl = downloadUrl;
            
            string[] parts = tag.Split(',');
            
            if (parts.Length >= 2)
            {
                string typePrefix = parts[0];
                string version = parts[1];
                
                switch (typePrefix)
                {
                    case "v":
                        Category = ReleaseCategory.Major;
                        DisplayVersion = version;
                        VersionForCompare = version.StartsWith("v") ? version.Substring(1) : version;
                        VersionForFileName = version;
                        break;
                        
                    case "s":
                        Category = ReleaseCategory.Snapshot;
                        DisplayVersion = version.Replace('_', ' ');
                        VersionForCompare = version;
                        VersionForFileName = version;
                        break;
                        
                    default:
                        Category = ReleaseCategory.Unknown;
                        DisplayVersion = version;
                        VersionForCompare = version;
                        VersionForFileName = version;
                        break;
                }
            }
            else
            {
                Category = ReleaseCategory.Unknown;
                DisplayVersion = tag;
                VersionForCompare = tag;
                VersionForFileName = tag;
            }
        }
        
        /// <summary>
        /// 現在インストールされているバージョンかどうか
        /// </summary>
        public bool IsCurrentVersion()
        {
            string currentVersion = MRIPInfo.Version;
            return currentVersion == VersionForCompare;
        }
        
        /// <summary>
        /// 更新をダウンロードしてインストール
        /// </summary>
        public IEnumerator CoUpdateAndShowDialog()
        {
            if (string.IsNullOrEmpty(DownloadUrl))
            {
                yield break;
            }
            
            MetaScreen downloadWindow = MetaScreen.GenerateWindow(
                new Virial.Compat.Vector2(3.5f, 1.5f),
                DestroyableSingleton<HudManager>.InstanceExists ? DestroyableSingleton<HudManager>.Instance.transform : null,
                Virial.Compat.Vector3.Zero,
                true, false, false, BackgroundSetting.Old, false
            );
            
            Virial.Compat.Size size;
            downloadWindow.SetWidget(
                NebulaGUIWidgetEngine.API.VerticalHolder(GUIAlignment.Center, new GUIWidget[]
                {
                    new GUILoadingIcon(GUIAlignment.Center) { Size = 0.35f },
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.1f),
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                        new RawTextComponent(Language.Translate("settings.mrip.downloading").Replace("*", "")))
                }),
                new Virial.Compat.Vector2(0.5f, 0.5f),
                out size
            );
            
            bool success = false;
            string errorMessage = "";
            
            yield return CoDownloadAndInstall(DownloadUrl, (result, error) =>
            {
                success = result;
                errorMessage = error;
            });
            
            downloadWindow.CloseScreen();
            
            if (success)
            {
                string message = Language.Translate("settings.mrip.update.message").Replace("*", "").Replace("%VERSION%", DisplayVersion);
                ShowMessage(Language.Translate("settings.mrip.update.title").Replace("*", ""), message);
            }
            else
            {
                ShowMessage(Language.Translate("settings.mrip.update.failed").Replace("*", ""), errorMessage);
            }
        }
        
        private IEnumerator CoDownloadAndInstall(string url, Action<bool, string> callback)
        {
            
            HttpClient client = GetHttpClient();
            HttpResponseMessage? response = null;
            
            var downloadTask = client.GetAsync(url);
            while (!downloadTask.IsCompleted)
            {
                yield return null;
            }
            
            try
            {
                response = downloadTask.Result;
            }
            catch (Exception ex)
            {
                callback(false, $"ダウンロードリクエスト失敗:\n{ex.Message}");
                yield break;
            }
            
            if (!response.IsSuccessStatusCode)
            {
                callback(false, $"ダウンロード失敗\nステータス: {response.StatusCode}");
                response?.Dispose();
                yield break;
            }
            
            var contentTask = response.Content.ReadAsByteArrayAsync();
            while (!contentTask.IsCompleted)
            {
                yield return null;
            }
            
            byte[] zipData;
            try
            {
                zipData = contentTask.Result;
            }
            catch (Exception ex)
            {
                callback(false, $"データ読み取り失敗:\n{ex.Message}");
                response?.Dispose();
                yield break;
            }
            
            try
            {
                string addonsPath = PathHelpers.GameRootPath + Path.DirectorySeparatorChar + "Addons";
                var currentAddon = MRIPInfo.Addon;
                
                if (currentAddon != null && !currentAddon.IsBuiltIn)
                {
                    try
                    {
                        currentAddon.Dispose();
                        System.Threading.Thread.Sleep(100);
                    }
                    catch (Exception)
                    {
                    }
                }
                
                DeleteOldAddonFiles(addonsPath);
                
                string zipPath = Path.Combine(addonsPath, $"{MRIPInfo.AddonId}-{VersionForFileName}.zip");
                File.WriteAllBytes(zipPath, zipData);
                
                callback(true, "");
            }
            catch (Exception ex)
            {
                callback(false, $"ファイル保存失敗:\n{ex.Message}");
            }
            
            response?.Dispose();
        }
        
        /// <summary>
        /// 古いアドオンファイルを削除
        /// </summary>
        private static void DeleteOldAddonFiles(string addonsPath)
        {
            try
            {
                if (!Directory.Exists(addonsPath)) return;
                
                foreach (string oldFile in Directory.GetFiles(addonsPath, "*.old"))
                {
                    if (Path.GetFileName(oldFile).Contains(MRIPInfo.AddonId))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
                
                foreach (string file in Directory.GetFiles(addonsPath, "*.zip"))
                {
                    if (Path.GetFileName(file).Contains(MRIPInfo.AddonId))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            try
                            {
                                File.Move(file, file + ".old", true);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        
        private static void ShowMessage(string title, string message)
        {
            int lineCount = message.Split('\n').Length;
            float height = Math.Max(2.5f, 1.5f + lineCount * 0.25f);
            
            MetaScreen window = MetaScreen.GenerateWindow(
                new Virial.Compat.Vector2(5.5f, height),
                DestroyableSingleton<HudManager>.InstanceExists ? DestroyableSingleton<HudManager>.Instance.transform : null,
                Virial.Compat.Vector3.Zero,
                true, true, true, BackgroundSetting.Old, true
            );
            
            Virial.Compat.Size size;
            window.SetWidget(
                NebulaGUIWidgetEngine.API.VerticalHolder(GUIAlignment.Center, new GUIWidget[]
                {
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayTitle), 
                        new RawTextComponent(Language.Translate("settings.mrip.dialog.title").Replace("*", ""))),
                    
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.15f),
                    
                    new NoSGUIText(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.OverlayContent), 
                        new RawTextComponent(message)),
                    
                    NebulaGUIWidgetEngine.API.VerticalMargin(0.2f),
                    
                    NebulaGUIWidgetEngine.API.Button(GUIAlignment.Center, NebulaGUIWidgetEngine.API.GetAttribute(AttributeAsset.CenteredBoldFixed), 
                        new RawTextComponent(Language.Translate("settings.mrip.dialog.ok").Replace("*", "")), 
                        (GUIClickable _) => { window.CloseScreen(); }, 
                        null, null, null, null, null, null)
                }),
                new Virial.Compat.Vector2(0.5f, 0.5f),
                out size
            );
        }
    }
    
    private class GitHubReleaseResponse
    {
        [JsonSerializableField]
        public string? tag_name = null;
        
        [JsonSerializableField]
        public string? body = null;
        
        [JsonSerializableField]
        public List<GitHubAssetResponse>? assets = null;
    }
    
    private class GitHubAssetResponse
    {
        [JsonSerializableField]
        public string? name = null;
        
        [JsonSerializableField]
        public string? browser_download_url = null;
    }
    
    /// <summary>
    /// バージョン一覧を取得するコルーチン
    /// </summary>
    public static IEnumerator CoFetchVersionTags(Action<List<ReleasedInfo>> postAction)
    {
        yield return FetchAsync().WaitAsCoroutine();
        postAction.Invoke(_cache ?? new List<ReleasedInfo>());
    }
    
    private static HttpClient? _httpClient = null;
    
    private static HttpClient GetHttpClient()
    {
        if (_httpClient == null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", $"MoreRolesInPolus/{MRIPInfo.Version}");
        }
        return _httpClient;
    }
    
    private static async System.Threading.Tasks.Task FetchAsync()
    {
        List<ReleasedInfo> releases = new List<ReleasedInfo>(_cache ?? new List<ReleasedInfo>());
        
        try
        {
            string url = GetReleasesUrl(NextPage);
            
            var response = await GetHttpClient().GetAsync(url);
            
            if (response.StatusCode == HttpStatusCode.OK)
            {
                string json = await response.Content.ReadAsStringAsync();
                
                int lastCount = releases.Count;
                
                List<GitHubReleaseResponse>? releaseList = null;
                using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                {
                    releaseList = JsonStructure.Deserialize<List<GitHubReleaseResponse>>(stream);
                }
                
                if (releaseList != null)
                {
                    foreach (var release in releaseList)
                    {
                        if (string.IsNullOrEmpty(release.tag_name)) continue;
                        
                        string? downloadUrl = null;
                        if (release.assets != null)
                        {
                            foreach (var asset in release.assets)
                            {
                                if (asset.name != null && asset.name.EndsWith(".zip"))
                                {
                                    downloadUrl = asset.browser_download_url;
                                    break;
                                }
                            }
                        }
                        
                        releases.Add(new ReleasedInfo(
                            release.tag_name,
                            release.body?.Replace("\\n", "\n").Replace("\\r", ""),
                            downloadUrl
                        ));
                    }
                }
                
                if (releases.Count == lastCount)
                {
                    MaybeNoMorePages = true;
                }
                
                NextPage++;
            }
            else
            {
                MaybeNoMorePages = true;
            }
            
            response.Dispose();
        }
        catch (Exception)
        {
            MaybeNoMorePages = true;
        }
        
        _cache = releases;
    }
}
