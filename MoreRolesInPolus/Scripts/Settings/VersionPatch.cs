/**
 * @file VersionPatch.cs
 * @brief タイトル画面とロビー画面のバージョン表示にMRIPのバージョン情報を追加
 * @details 
 * - VersionShower.Start の Postfix でタイトル画面のバージョンテキストを書き換える
 * - LobbyBehaviour.Start の Postfix でロビー画面のバージョンテキストを書き換える
 * - NebulaPreprocessでHarmonyパッチを適用
 * 
 * ロビー画面ではVersionShowerは使われず、NoSGUITextで独自のバージョン表示が作成される
 */

using HarmonyLib;
using Nebula.Modules;
using Nebula.Utilities;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Virial.Runtime;
using UnityEngine;
using TMPro; // TextMeshProのために必要
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace Toa.MoreRolesInPolus.Scripts.Settings;

/// <summary>
/// Harmonyパッチのセットアップクラス
/// </summary>
[NebulaPreprocess(PreprocessPhase.PostLoadAddons)]
public static class MRIPHarmonySetUp
{
  private static Harmony? HarmonyInstance;
  
  /// <summary>
  /// プリプロセス時にHarmonyパッチを適用
  /// </summary>
  /// <param name="preprocessor">プリプロセッサー</param>
  public static void Preprocess(NebulaPreprocessor preprocessor)
  {
    try
    {
      
      HarmonyInstance = new Harmony("MoreRolesInPolus.VersionPatch");
      
      // VersionShower.StartにPostfixパッチを適用（タイトル画面）
      var versionShowerMethod = typeof(VersionShower).GetMethod("Start");
      var versionShowerPostfix = typeof(MRIPVersionPatch).GetMethod(nameof(MRIPVersionPatch.VersionShowerStartPostfix));
      
      // Priorityを低く設定して、Nebulaのパッチの後に実行されるようにする
      var harmonyMethod = new HarmonyMethod(versionShowerPostfix);
      harmonyMethod.priority = Priority.Low;
      
      HarmonyInstance.Patch(versionShowerMethod, postfix: harmonyMethod);
      
      // LobbyBehaviour.StartにPostfixパッチを適用（ロビー画面）
      var lobbyStartMethod = typeof(LobbyBehaviour).GetMethod("Start");
      var lobbyPostfix = typeof(MRIPVersionPatch).GetMethod(nameof(MRIPVersionPatch.LobbyStartPostfix));
      
      // Priorityを低く設定して、Nebulaのパッチの後に実行されるようにする
      var lobbyHarmonyMethod = new HarmonyMethod(lobbyPostfix);
      lobbyHarmonyMethod.priority = Priority.Low;
      
      HarmonyInstance.Patch(lobbyStartMethod, postfix: lobbyHarmonyMethod);
      
    }
    catch (System.Exception)
    {
    }
  }
}

/// <summary>
/// バージョン表示パッチクラス
/// </summary>
public static class MRIPVersionPatch
{
  private static readonly HashSet<VersionShower> _updatedVersionShowers = new();
  private static readonly HashSet<TextMeshPro> _updatedLobbyTexts = new();
  
  /// <summary>
  /// 安定版（例: "v3.1"）のバージョン表記パターン
  /// </summary>
  private static readonly Regex StableVersionPattern = new(@"v\d+\.\d+", RegexOptions.Compiled);
  
  /// <summary>
  /// テキストがNebulaのバージョンテキストかどうかを判定する
  /// スナップショット版（"Snapshot"を含む）または安定版（"v3.1"等のパターン）を検出
  /// </summary>
  /// <param name="text">判定対象のテキスト</param>
  /// <returns>バージョンテキストの場合true</returns>
  private static bool IsVersionText(string text)
  {
    return text.Contains("Snapshot") || StableVersionPattern.IsMatch(text);
  }
  
  /// <summary>
  /// VersionShower.Start実行後に呼ばれるPostfixパッチ（タイトル画面）
  /// </summary>
  /// <param name="__instance">VersionShowerのインスタンス</param>
  public static void VersionShowerStartPostfix(VersionShower __instance)
  {
    if (_updatedVersionShowers.Contains(__instance)) return;

    try
    {
      UpdateVersionShowerText(__instance);
      
      // テキストが上書きされる可能性があるので、しばらく監視するコルーチンを開始
      // VersionShower自体がMonoBehaviourなので直接StartCoroutineを使用
      __instance.StartCoroutine(MonitorVersionShowerText(__instance).WrapToIl2Cpp());
    }
    catch (System.Exception)
    {
    }
  }

  /// <summary>
  /// LobbyBehaviour.Start実行後に呼ばれるPostfixパッチ（ロビー画面）
  /// </summary>
  /// <param name="__instance">LobbyBehaviourのインスタンス</param>
  public static void LobbyStartPostfix(LobbyBehaviour __instance)
  {
    try
    {
      
      // Nebulaのパッチでテキストが作成されるまで少し待つ必要があるのでコルーチンで処理
      __instance.StartCoroutine(FindAndUpdateLobbyVersionText().WrapToIl2Cpp());
    }
    catch (System.Exception)
    {
    }
  }
  
  /// <summary>
  /// VersionShowerのテキストを更新する
  /// </summary>
  /// <param name="versionShower">VersionShowerのインスタンス</param>
  private static void UpdateVersionShowerText(VersionShower versionShower)
  {
    try
    {
      if (versionShower?.text?.text != null)
      {
        string currentText = versionShower.text.text;
        
        // Nebulaのバージョン文字列が含まれていることを確認し、MRIPのクレジットがまだない場合のみ追加
        if (currentText.Contains("NoS") && !currentText.Contains(MRIPInfo.ShortName))
        {
          versionShower.text.text = currentText + MRIPInfo.GetVersionString();
          _updatedVersionShowers.Add(versionShower);
        }
        else if (!currentText.Contains("NoS"))
        {
        }
      }
      else
      {
      }
    }
    catch (System.Exception)
    {
    }
  }

  /// <summary>
  /// VersionShowerのテキストが上書きされていないか継続的に監視するコルーチン
  /// </summary>
  private static System.Collections.IEnumerator MonitorVersionShowerText(VersionShower versionShower)
  {
    // 最大600フレーム（約10秒）監視
    for (int i = 0; i < 600; i++)
    {
      yield return null;
      
      if (versionShower == null || versionShower.text == null)
      {
        yield break;
      }

      string currentText = versionShower.text.text;
      
      // Nebulaが含まれているが、MRIPが含まれていない場合、再度追加
      if (currentText.Contains("NoS") && !currentText.Contains(MRIPInfo.ShortName))
      {
        _updatedVersionShowers.Remove(versionShower);
        UpdateVersionShowerText(versionShower);
      }
    }
  }
  
  /// <summary>
  /// ロビー画面のバージョンテキストを検索して更新するコルーチン
  /// </summary>
  private static System.Collections.IEnumerator FindAndUpdateLobbyVersionText()
  {
    
    // 最大600フレーム（約10秒）待機して監視
    for (int i = 0; i < 600; i++)
    {
      yield return null;

      // NebulaLogoHolderを探す
      GameObject logoHolder = GameObject.Find("NebulaLogoHolder");

      if (logoHolder != null)
      {
        TextMeshPro[] textComponents = logoHolder.GetComponentsInChildren<TextMeshPro>();
        foreach (var textComponent in textComponents)
        {
          if (textComponent != null && textComponent.text != null)
          {
            string currentText = textComponent.text;
            
            // バージョンテキスト（Snapshot版 or 安定版 "v3.1" 等）を特定
            if (IsVersionText(currentText) && !currentText.Contains(MRIPInfo.ShortName))
            {
              if (!_updatedLobbyTexts.Contains(textComponent))
              {
                textComponent.text = currentText + MRIPInfo.GetVersionString();
                _updatedLobbyTexts.Add(textComponent);
              }
            }
            else if (IsVersionText(currentText) && currentText.Contains(MRIPInfo.ShortName))
            {
              // 既に更新済み
              yield break;
            }
          }
        }
      }
    }
    
  }
}
