/**
 * @file MRIPSettingsButton.cs
 * @brief NoSのバージョン画面にMRIP設定ボタンを追加 + 起動時自動更新チェック
 * @details
 * - NoSのバージョン画面の左カラム（カテゴリボタン列）末尾にボタンを追加
 * - 起動時に自動更新チェックを実行
 * - バージョン選択UI（Nebula風）を表示
 */

using Nebula.Patches;
using Virial.Runtime;
using UnityEngine.UI;
using global::MoreRolesInPolus.Scripts.Core;

namespace Toa.MoreRolesInPolus.Scripts.Settings;

/// <summary>
/// MainMenu表示後にMRIPボタンを追加するHarmonyパッチのセットアップ
/// </summary>
[NebulaPreprocess(PreprocessPhase.PostLoadAddons)]
public static class MRIPMainMenuPatchSetup
{
  private static Harmony? HarmonyInstance;
  
  /// <summary>
  /// Harmonyパッチを適用
  /// </summary>
  /// <param name="preprocessor">プリプロセッサー</param>
  public static void Preprocess(NebulaPreprocessor preprocessor)
  {
    try
    {
      // アドオン読み込み直後に古いMRIPファイルを削除
      // この時点で古いファイルは読み込みスキップされてDispose()済み = ロック解除済み
      MRIPModUpdater.CleanupOldAddonFiles();
      
      
      HarmonyInstance = new Harmony("MoreRolesInPolus.MainMenuPatch");
      
      // MainMenuManager.Awakeの後に処理
      var mainMenuAwakeMethod = typeof(MainMenuManager).GetMethod("Awake");
      var postfix = typeof(MRIPMainMenuPatch).GetMethod(nameof(MRIPMainMenuPatch.MainMenuAwakePostfix));
      
      var harmonyMethod = new HarmonyMethod(postfix);
      harmonyMethod.priority = Priority.Last; // Nebulaの処理の後に実行
      
      HarmonyInstance.Patch(mainMenuAwakeMethod, postfix: harmonyMethod);
      
      // ResetScreenパッチも適用
      MRIPMenuClearScreenPatch.Apply(HarmonyInstance);
      
    }
    catch (System.Exception)
    {
    }
  }
}

/// <summary>
/// MainMenuManager.ResetScreenのパッチ（MRIP画面を閉じる）
/// </summary>
public static class MRIPMenuClearScreenPatch
{
  private static bool Patched = false;
  
  /// <summary>
  /// パッチを適用
  /// </summary>
  /// <param name="harmony">Harmonyインスタンス</param>
  public static void Apply(Harmony harmony)
  {
    if (Patched) return;
    
    try
    {
      var resetScreenMethod = typeof(MainMenuManager).GetMethod(nameof(MainMenuManager.ResetScreen));
      var postfix = typeof(MRIPMenuClearScreenPatch).GetMethod(nameof(ResetScreenPostfix));
      harmony.Patch(resetScreenMethod, postfix: new HarmonyMethod(postfix));
      Patched = true;
    }
    catch (System.Exception)
    {
    }
  }
  
  /// <summary>
  /// MRIP設定画面を閉じる
  /// </summary>
  public static void ResetScreenPostfix()
  {
    MRIPMainMenuPatch.MRIPVersionsScreen?.SetActive(false);
  }
}

/// <summary>
/// MainMenu表示後にMRIPボタンを追加するパッチ
/// </summary>
public static class MRIPMainMenuPatch
{
  private const string VersionsScreenButtonName = "MRIPSettingsButton";
  private static GameObject? versionsScreenButton = null;
  
  /// <summary>
  /// MRIPバージョン選択画面
  /// </summary>
  public static GameObject? MRIPVersionsScreen = null;
  
  /// <summary>
  /// MainMenuManagerのインスタンス
  /// </summary>
  private static MainMenuManager? MainMenuInstance = null;
  
  /// <summary>
  /// MainMenuManager.Awake実行後に呼ばれるPostfixパッチ
  /// </summary>
  /// <param name="__instance">MainMenuManagerのインスタンス</param>
  public static void MainMenuAwakePostfix(MainMenuManager __instance)
  {
    try
    {
      MainMenuInstance = __instance;
      
      // ボタン追加と自動更新チェック
      __instance.StartCoroutine(SetupMRIPButton(__instance).WrapToIl2Cpp());
    }
    catch (System.Exception)
    {
    }
  }
  
  /// <summary>
  /// MRIPボタンをセットアップするコルーチン
  /// </summary>
  private static System.Collections.IEnumerator SetupMRIPButton(MainMenuManager mainMenu)
  {
    // UIがロードされるまで待つ
    for (int i = 0; i < 60; i++)
    {
      yield return null;
    }
    
    // 自動更新チェックは無効化
    // MRIPAutoUpdater.OnMainMenuLoaded();
    
    mainMenu.StartCoroutine(MonitorVersionsScreen(mainMenu).WrapToIl2Cpp());
  }

  /// <summary>
  /// NoSのバージョン画面が開かれたらMRIP設定ボタンを追加するコルーチン
  /// </summary>
  private static System.Collections.IEnumerator MonitorVersionsScreen(MainMenuManager mainMenu)
  {
    bool wasActive = false;

    // ロビーに入ったら終了
    while (LobbyBehaviour.Instance == null)
    {
      yield return null;

      // バージョン画面は初回表示時に生成され、以降はSetActiveで使い回される
      GameObject? versionsScreen = MainMenuSetUpPatch.VersionsScreen;
      bool isActive = versionsScreen && versionsScreen!.activeInHierarchy;

      if (isActive && !wasActive && !versionsScreenButton)
      {
        versionsScreenButton = AddButtonToVersionsScreen(versionsScreen!, mainMenu);
      }

      wasActive = isActive;
    }
  }
  
  /// <summary>
  /// バージョン画面の左カラム（カテゴリボタン列）の末尾にMRIP設定ボタンを追加
  /// </summary>
  private static GameObject? AddButtonToVersionsScreen(GameObject versionsScreen, MainMenuManager mainMenu)
  {
    try
    {
      // NoSのカテゴリボタンは名前やコンポーネントで区別できないため、表示テキストで特定する
      string customLabel = Language.Translate("version.category.custom");
      string unknownLabel = Language.Translate("version.category.unknown");
      Transform? customButton = null;
      Transform? unknownButton = null;
      foreach (var button in versionsScreen.GetComponentsInChildren<PassiveButton>(true))
      {
        string? label = button.GetComponentInChildren<TextMeshPro>(true)?.text;
        if (label == customLabel) customButton = button.transform;
        else if (label == unknownLabel) unknownButton = button.transform;
      }
      if (customButton == null || unknownButton == null) return null;
      
      int sortingOrder = unknownButton.GetComponent<SpriteRenderer>().sortingOrder;
      GameObject? created = null;
      new MetaWidgetOld.Button(() => OpenMRIPScreen(mainMenu), new TextAttributeOld(TextAttributeOld.BoldAttr) { Size = new Virial.Compat.Vector2(0.95f, 0.28f) })
      {
        RawText = Language.Translate("settings.mrip.button.name").Replace("*", ""),
        PostBuilder = (button, renderer, _) =>
        {
          created = button.gameObject;
          renderer.sortingOrder = sortingOrder;
        }
      }.Generate(unknownButton.parent.gameObject, new(0f, 0f), out _);
      if (created == null) return null;
      
      created.name = VersionsScreenButtonName;
      created.transform.position = unknownButton.position + (unknownButton.position - customButton.position);
      return created;
    }
    catch (System.Exception)
    {
      return null;
    }
  }
  
  /// <summary>
  /// MRIP設定画面を開く
  /// </summary>
  private static void OpenMRIPScreen(MainMenuManager mainMenu)
  {
    mainMenu.ResetScreen();
    if (MRIPVersionsScreen == null) CreateVersionsScreen(mainMenu);
    MRIPVersionsScreen?.SetActive(true);
    mainMenu.screenTint.enabled = true;
  }
  
  /// <summary>
  /// NoSのバージョン画面に戻る
  /// </summary>
  private static void BackToNoSScreen(MainMenuManager mainMenu)
  {
    mainMenu.ResetScreen();
    MainMenuSetUpPatch.VersionsScreen?.SetActive(true);
    mainMenu.screenTint.enabled = true;
  }

  /// <summary>
  /// バージョン選択画面を作成（Nebula風 - NebulaScreen内に配置）
  /// </summary>
  private static void CreateVersionsScreen(MainMenuManager mainMenu)
  {
    try
    {
      
      // accountButtonsの親と同じ階層に配置（Nebulaと同じ方式）
      MRIPVersionsScreen = UnityHelper.CreateObject("MRIPVersions", mainMenu.accountButtons.transform.parent, new Virial.Compat.Vector3(0, 0, -1f));
      MRIPVersionsScreen.transform.localScale = MainMenuSetUpPatch.NebulaScreen!.transform.localScale;
      
      // MetaScreenを生成（GenerateWindowではなくGenerateScreen）
      var screen = MetaScreen.GenerateScreen(new Virial.Compat.Vector2(6.2f, 4.1f), MRIPVersionsScreen.transform, new Virial.Compat.Vector3(-0.1f, 0, 0f), false, false, false);
      
      // テキスト属性を設定
      TextAttributeOld NameAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
      {
        FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
        Size = new Virial.Compat.Vector2(2.2f, 0.3f),
        Alignment = TMPro.TextAlignmentOptions.Left
      };
      
      TextAttributeOld CategoryAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
      {
        FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
        Size = new Virial.Compat.Vector2(0.8f, 0.3f),
        Alignment = TMPro.TextAlignmentOptions.Center
      };
      CategoryAttribute.EditFontSize(1.2f, 0.6f, 1.2f);

      TextAttributeOld ErrorDetailAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
      {
        FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
        Size = new Virial.Compat.Vector2(3f, 0.55f),
        Alignment = TMPro.TextAlignmentOptions.Left
      };
      
      TextAttributeOld ButtonAttribute = new TextAttributeOld(TextAttributeOld.BoldAttr)
      {
        FontMaterial = VanillaAsset.StandardMaskedFontMaterial,
        Size = new Virial.Compat.Vector2(1f, 0.2f),
        Alignment = TMPro.TextAlignmentOptions.Center
      };
      
      // 内部参照用変数
      Variable<MetaWidgetOld.ScrollView.InnerScreen> innerRef = new Variable<MetaWidgetOld.ScrollView.InnerScreen>();
      List<MRIPModUpdater.ReleasedInfo>? versions = MRIPModUpdater.Cache;
      
      // 静的ウィジェット（全体レイアウト）
      MetaWidgetOld staticWidget = new MetaWidgetOld();
      
      // 左側: 戻るボタン + カテゴリボタン（行の高さはNebulaと同じ0.6）
      var menuButtons = new List<MetaWidgetOld.Button>
      {
        new MetaWidgetOld.Button(() => BackToNoSScreen(mainMenu),
          new TextAttributeOld(TextAttributeOld.BoldAttr) { Size = new Virial.Compat.Vector2(0.95f, 0.28f) })
        {
          RawText = Language.Translate("settings.mrip.button.backToNoS").Replace("*", "")
        }
      };
      
      // 各カテゴリのボタンを追加
      foreach (MRIPModUpdater.ReleaseCategory category in System.Enum.GetValues(typeof(MRIPModUpdater.ReleaseCategory)))
      {
        var cat = category; // クロージャ用
        menuButtons.Add(new MetaWidgetOld.Button(() => UpdateContents(cat), 
          new TextAttributeOld(TextAttributeOld.BoldAttr) { Size = new Virial.Compat.Vector2(0.95f, 0.28f) }) 
        { 
          RawText = Language.Translate(MRIPModUpdater.CategoryNames[(int)category]).Replace("*", "")
        });
      }

      MetaWidgetOld menuWidget = new MetaWidgetOld();
      menuWidget.Append(menuButtons, button => button, 1, -1, 0, 0.6f);

      // 左側メニュー + 右側スクロールビュー（ParallelWidgetOld）
      staticWidget.Append(new ParallelWidgetOld(
        new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.HorizonalMargin(0.1f), 0.1f),
        new System.Tuple<IMetaWidgetOld, float>(menuWidget, 1f),
        new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.HorizonalMargin(0.1f), 0.1f),
        new System.Tuple<IMetaWidgetOld, float>(new MetaWidgetOld.ScrollView(new Virial.Compat.Vector2(5f, 4f), new MetaWidgetOld(), true) 
        { 
          Alignment = IMetaWidgetOld.AlignmentOption.Center, 
          InnerRef = innerRef,
          ScrollerTag = "MRIPVersions"
        }, 5f)
      ));
      
      screen.SetWidget(staticWidget);
      
      // ローディング表示
      innerRef.Value?.SetLoadingWidget();
      
      /// <summary>
      /// コンテンツを更新
      /// </summary>
      void UpdateContents(MRIPModUpdater.ReleaseCategory? category = null)
      {
        if (versions == null || versions.Count == 0)
        {
          innerRef.Value?.SetWidget(new MetaWidgetOld.Text(NameAttribute) { RawText = Language.Translate("settings.mrip.loading").Replace("*", "") });
          return;
        }
        
        var inner = new MetaWidgetOld();
        
        // 自動更新ボタンは無効化（削除済み）
        // // 安定版カテゴリの場合に自動更新（安定版）行を表示
        // if ((category ?? MRIPModUpdater.ReleaseCategory.Major) == MRIPModUpdater.ReleaseCategory.Major)
        // {
        //     AutoUpdateContent("最新の安定版", MRIPAutoUpdater.AutoUpdateMode.Major);
        // }
        // 
        // // スナップショットカテゴリの場合に自動更新（スナップショット）行を表示
        // if ((category ?? MRIPModUpdater.ReleaseCategory.Snapshot) == MRIPModUpdater.ReleaseCategory.Snapshot)
        // {
        //     AutoUpdateContent("最新のスナップショット", MRIPAutoUpdater.AutoUpdateMode.Snapshot);
        // }
        
        // バージョン一覧
        foreach (var version in versions)
        {
          // カテゴリフィルタ
          if (category != null && version.Category != category) continue;
          
          try
          {
            List<IMetaParallelPlacableOld> placeable = new List<IMetaParallelPlacableOld>();
            
            // カテゴリラベル（色付き）
            string startKey = MRIPModUpdater.CategoryNames[(int)version.Category];
            
            placeable.Add(new MetaWidgetOld.Text(CategoryAttribute) 
            { 
              MyText = new Nebula.Utilities.ColorTextComponent(
                MRIPModUpdater.CategoryColors[(int)version.Category].ToUnityColor(), 
                NebulaGUIWidgetEngine.Instance.RawTextComponent(Language.Translate(startKey).Replace("*", "")))
            });
            placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
            
            // バージョン名（クリックでリリースページを開く、ホバーで説明表示）
            placeable.Add(new MetaWidgetOld.Text(NameAttribute)
            {
              RawText = version.DisplayVersion,
              PostBuilder = text =>
              {
                var button = text.gameObject.SetUpButton(true);
                button.gameObject.AddComponent<BoxCollider2D>().size = text.rectTransform.sizeDelta;
                button.OnClick.AddListener(() => Application.OpenURL(MRIPModUpdater.GetReleasePageUrl(version.RawTag)));
                button.OnMouseOver.AddListener(() =>
                {
                  text.color = Virial.Color.Green.ToUnityColor();
                  if (version.Body != null) NebulaManager.Instance.SetHelpWidget(button, version.Body);
                });
                button.OnMouseOut.AddListener(() =>
                {
                  text.color = Virial.Color.White.ToUnityColor();
                  NebulaManager.Instance.HideHelpWidgetIf(button);
                });
              }
            });
            placeable.Add(new MetaWidgetOld.HorizonalMargin(0.15f));
            
            // ボタン: 取得/使用中
            if (version.IsCurrentVersion())
            {
              // 現在のバージョン
              placeable.Add(new MetaWidgetOld.HorizonalMargin(0.13f));
              placeable.Add(new MetaWidgetOld.Text(ButtonAttribute) { RawText = Language.Translate("settings.mrip.status.current").Replace("*", "") });
            }
            else if (!string.IsNullOrEmpty(version.DownloadUrl))
            {
              // ダウンロード可能
              placeable.Add(new MetaWidgetOld.Button(() => 
              {
                NebulaManager.Instance.StartCoroutine(version.CoUpdateAndShowDialog().WrapToIl2Cpp());
              }, ButtonAttribute) 
              { 
                RawText = Language.Translate("settings.mrip.button.get").Replace("*", ""),
                PostBuilder = (_, renderer, _) => renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask
              });
            }
            else
            {
              // ダウンロードURL不明
              placeable.Add(new MetaWidgetOld.HorizonalMargin(0.13f));
              placeable.Add(new MetaWidgetOld.Text(ButtonAttribute) { RawText = "---" });
            }
            
            inner.Append(new CombinedWidgetOld(0.5f, placeable.ToArray()) { Alignment = IMetaWidgetOld.AlignmentOption.Left });
          }
          catch (System.Exception)
          {
          }
        }
        
        // もっと読み込むボタン
        if (!MRIPModUpdater.MaybeNoMorePages)
        {
          inner.Append(new MetaWidgetOld.Button(() =>
          {
            NebulaManager.Instance.StartCoroutine(MRIPModUpdater.CoFetchVersionTags((list) =>
            {
              versions = list;
              UpdateContents(category);
            }).WrapToIl2Cpp());
          }, ButtonAttribute)
          { 
            Alignment = IMetaWidgetOld.AlignmentOption.Center, 
            RawText = Language.Translate("settings.mrip.button.more").Replace("*", ""),
            PostBuilder = (_, renderer, _) => renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask
          });
        }
        
        innerRef.Value?.SetWidget(inner);
      }
      
      // 初期データ読み込み
      if (MRIPModUpdater.Cache != null && MRIPModUpdater.Cache.Count > 0)
      {
        versions = MRIPModUpdater.Cache;
        UpdateContents();
      }
      else
      {
        NebulaManager.Instance.StartCoroutine(MRIPModUpdater.CoFetchVersionTags((list) => 
        {
          versions = list;
          
          // エラーチェック: データが取得できなかった場合
          if (list == null || list.Count == 0)
          {
            var errorWidget = new MetaWidgetOld();
            // NoSのバージョン画面と同じ高さに出す（NoS側は自動更新の行 0.5×2 + 余白0.8 の下にエラーを表示している）
            errorWidget.Append(new MetaWidgetOld.VerticalMargin(1.8f));
            errorWidget.Append(new MetaWidgetOld.Text(new TextAttributeOld(NameAttribute) { Alignment = TMPro.TextAlignmentOptions.Center }) 
            { 
              RawText = "エラー: バージョン情報を取得できませんでした",
              Alignment = IMetaWidgetOld.AlignmentOption.Center
            });
            errorWidget.Append(new MetaWidgetOld.VerticalMargin(0.02f));
            errorWidget.Append(new MetaWidgetOld.Text(new TextAttributeOld(ErrorDetailAttribute) { Alignment = TMPro.TextAlignmentOptions.Top }) 
            { 
              RawText = "GitHub APIへのアクセスが制限されています。\nしばらく待ってから再度お試しください。".Sized(70),
              Alignment = IMetaWidgetOld.AlignmentOption.Center
            });
            innerRef.Value?.SetWidget(errorWidget);
          }
          else
          {
            UpdateContents();
          }
        }).WrapToIl2Cpp());
      }
      
    }
    catch (System.Exception)
    {
    }
  }
}
