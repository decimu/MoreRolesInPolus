using Virial.Runtime;
using global::MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Imposter
{
    [NebulaPreprocess(PreprocessPhase.PostFixStructure)]
    public static class HarmonyPatchSetUp
    {
        public static Harmony? harmony;
        public static void Preprocess(NebulaPreprocessor preprocessor)
        {
            harmony = new Harmony("ToaPatch");
            harmony.Patch(typeof(Vent).GetMethod("CanUse"), new HarmonyMethod(typeof(HarmonyPatchSetUp).GetMethod("FixJackVentUser"), 0));
        }
        public static bool FixJackVentUser(Vent __instance, ref float __result, ref bool canUse, ref bool couldUse)
        {
            if (__instance.name.StartsWith("JackInTheBoxVent_"))
            {
                if (NebulaAPI.CurrentGame != null && GamePlayer.LocalPlayer?.Role.GetAbility<Anchor.Ability>() == null)
                {
                    canUse = false;
                    couldUse = false;
                    __result = float.MaxValue;
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Anchorロールの情報を定義するクラスです。
    /// </summary>
    [NebulaRPCHolder]
    public class Anchor : DefinedSingleAbilityRoleTemplate<Anchor.Ability>, DefinedRole, HasCitation
    {
        /// <summary>
        /// Anchorロール情報のコンストラクタです。ここで役職の内部名、色、割り当てのカテゴリ、所属陣営、およびオプションを設定します。
        /// </summary>
        private Anchor() : base("anchor", new(Palette.ImpostorRed), RoleCategory.ImpostorRole, NebulaTeams.ImpostorTeam, [placeCoolDownOption, cameraCoolDownOption, cameraDurationOption, numOfJacksOption])
        {
            ConfigurationHolder?.AddTags(ConfigurationTags.TagFunny);
        }
        Citation? HasCitation.Citation => Citations.TheOtherRoles;

        Image? DefinedAssignable.IconImage => iconImage;
        static readonly Image iconImage = NebulaAPI.AddonAsset.GetResource(string.Format("Impostor/Anchor/Anchor.png"))!.AsImage()!;


        /// <summary>
        /// ロビーで変更できる設定を用意します。ゲーム中で編集できるように、すぐ上のコンストラクタで役職のオプションに追加します。
        /// </summary>
        static private readonly FloatConfiguration placeCoolDownOption = NebulaAPI.Configurations.Configuration("options.role.anchor.placeCoolDown", (0f, 40f, 2.5f), 10f, FloatConfigurationDecorator.Second);
        static private readonly FloatConfiguration cameraCoolDownOption = NebulaAPI.Configurations.Configuration("options.role.anchor.cameraCoolDown", (2.5f, 30f, 2.5f), 10f, FloatConfigurationDecorator.Second);
        static private readonly FloatConfiguration cameraDurationOption = NebulaAPI.Configurations.Configuration("options.role.anchor.cameraDuration", (0f, 20f, 2.5f), 10f, FloatConfigurationDecorator.Second);
        static private readonly IntegerConfiguration numOfJacksOption = NebulaAPI.Configurations.Configuration("options.role.anchor.numOfJacks", (3, 5), 3);

        /// <summary>
        /// 役職の情報を用意します。
        /// </summary>
        static public readonly Anchor MyRole = new();
        AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.KillersSide;
        MultipleAssignmentType DefinedRole.MultipleAssignment => MultipleAssignmentType.Allowed;

        [NebulaPreprocess(PreprocessPhase.PostRoles)]
        public class Jack : NebulaSyncStandardObject, IGameOperator
        {
            public const string MyGlobalTag = "JackGlobal";
            public const string MyLocalTag = "JackLocal";

            public static Image GetImage(int index)
            {
                return NebulaAPI.AddonAsset.GetResource(string.Format("TricksterAnimation/trickster_box_00{0:00}.png", index + 1))!.AsImage()!;
            }
            public static Sprite GetSprite(int index)
            {
                return NebulaAPI.AddonAsset.GetResource(string.Format("TricksterAnimation/trickster_box_00{0:00}.png", index + 1))!.AsImage()!.GetSprite();
            }

            public EmptyBehaviour MyBehaviour = null!;

            public Vent vent;
            public Jack(Virial.Compat.Vector2 pos, bool isLocal) : base(pos, ZOption.Just, true, GetImage(0).GetSprite(), isLocal) 
            {
                /// <summary>
                /// AmongusからVentオブジェクトを取得し、vent変数に格納する。
                /// Ventを初期化する。その後、Anchor本人にしか見えないように設定する。
                /// </summary>
                vent = UnityEngine.Object.FindObjectOfType<Vent>();
                vent = UnityEngine.Object.Instantiate<Vent>(vent);
                vent.transform.position = MyRenderer.transform.position;
                vent.Left = null;
                vent.Right = null;
                vent.Center = null;
                vent.EnterVentAnim = null;
                vent.ExitVentAnim = null;
                vent.Offset = new Virial.Compat.Vector3(0f, 0.25f, 0f);
                vent.Id = ShipStatus.Instance.AllVents.Max(v => v.Id) + 1;
                SpriteRenderer component = vent.GetComponent<SpriteRenderer>();
                component.sprite = GetSprite(0);
                vent.myRend = component;
                List<Vent> list = ShipStatus.Instance.AllVents.ToList<Vent>();
                list.Add(vent);
                ShipStatus.Instance.AllVents = list.ToArray();
                vent.gameObject.SetActive(false);
                vent.name = "JackInTheBoxVent_" + vent.Id.ToString();
                MyRenderer.gameObject.SetActive(isLocal);
                MyBehaviour = MyRenderer.gameObject.AddComponent<EmptyBehaviour>();
            }

            public void ConvertToVent()
            {
                MyRenderer.gameObject.SetActive(false);
                vent.gameObject.SetActive(true);
            }

            public void HideBeforeActivation()
            {
                if (MyRenderer != null) MyRenderer.gameObject.SetActive(false);
                if (vent != null) vent.gameObject.SetActive(false);
            }
            int currentIndex = 0;
            const int maxIndex = 18;
            public System.Collections.IEnumerator StartAnimation()
            {
                while (currentIndex < maxIndex)
                {
                    var sprite = GetSprite(currentIndex);
                    if (MyRenderer != null)
                    {
                        MyRenderer.sprite = sprite;
                    }
                    if (vent != null && vent.myRend != null)
                    {
                        vent.myRend.sprite = sprite;
                    }
                    currentIndex++;
                    yield return null;
                }
                currentIndex = 0;
                var sprite2 = GetSprite(0);
                if (MyRenderer != null)
                {
                    MyRenderer.sprite = sprite2;
                }
                if (vent != null && vent.myRend != null)
                {
                    vent.myRend.sprite = sprite2;
                }
            }
            static Jack()
            {
                NebulaSyncObject.RegisterInstantiater(MyGlobalTag, (args) => new Jack(new(args[0], args[1]), false));
                NebulaSyncObject.RegisterInstantiater(MyLocalTag, (args) => new Jack(new(args[0], args[1]), true));
            }
            void IGameOperator.OnReleased()
            {
            }
        }

        /// <summary>
        /// 役職を割り当てるとき、プレイヤーに割り当てる能力を作成します。
        /// </summary>
        /// <param name="player">割り当てる対象のプレイヤー</param>
        /// <param name="arguments">役職の引数(役職の状態を引き継ぐために使用します。)</param>
        /// <returns></returns>
        public override Ability CreateAbility(GamePlayer player, int[] arguments)
        {
            bool isUsurped = arguments.GetAsBool(0);
            int leftJacks = arguments.Get(1, numOfJacksOption);
            bool isJackActivated = arguments.GetAsBool(2);
            int jackIdCount = arguments.Get(3, 0);
            int[] jackObjectIds = arguments.Skip(4).Take(jackIdCount).ToArray();

            // 異常引数時の保険: 通常配布なのに残数0になっていたら初期値に戻す
            if (!isJackActivated && jackObjectIds.Length == 0 && leftJacks <= 0)
            {
                leftJacks = numOfJacksOption;
            }

            return new(player, isUsurped, leftJacks, isJackActivated, jackObjectIds);
        }

        public static readonly RemoteProcess<int> RpcPlayJackVentAnimation = new("RpcPlayJackVentAnimation", (m, _) =>
        {
            var obj = NebulaSyncObject.GetObject<Jack>(m);
            if (obj != null)
            {
                NebulaManager.Instance.StartCoroutine(obj.StartAnimation().WrapToIl2Cpp());
            }
        });

        public static readonly RemoteProcess<int> RpcHideJackBeforeActivation = new("RpcHideJackBeforeActivation", (m, _) =>
        {
            var obj = NebulaSyncObject.GetObject<Jack>(m);
            if (obj != null)
            {
                obj.HideBeforeActivation();
            }
        });

        public static readonly RemoteProcess<int[]> RpcActivateJacks = new("RpcActivateJacks", (objectIds, _) =>
        {
            var globalJacks = objectIds
                .Select(NebulaSyncObject.GetObject<Jack>)
                .Where(jack => jack != null)
                .Cast<Jack>()
                .ToList();

            if (globalJacks.Count == 0) return;

            globalJacks.Do(jack => jack.ConvertToVent());

            for (int c = 0; c < globalJacks.Count - 1; c++)
            {
                Jack jack1 = globalJacks[c];
                Jack jack2 = globalJacks[c + 1];
                jack1.vent.Right = jack2.vent;
                jack2.vent.Left = jack1.vent;
            }

            globalJacks.First().vent.Left = globalJacks.Last().vent;
            globalJacks.Last().vent.Right = globalJacks.First().vent;
        });

        /// <summary>
        /// 役職の能力を記述するクラスです。
        /// </summary>
        public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
        {
            static private readonly Image placeSprite = NebulaAPI.AddonAsset.GetResource("PlaceJackInTheBoxButton.png")!.AsImage(115f)!;
            static private readonly Image cameraSprite = NebulaAPI.AddonAsset.GetResource("PlaceJackInTheBoxButton.png")!.AsImage(115f)!;
            static private readonly Image moveRightSprite = NebulaAPI.AddonAsset.GetResource("Impostor/Anchor/ButtonRight.png")!.AsImage(115f)!;
            static private readonly Image moveLeftSprite = NebulaAPI.AddonAsset.GetResource("Impostor/Anchor/ButtonLeft.png")!.AsImage(115f)!;

            private List<Jack> globalJacks = null!;
            private List<Jack> localJacks = new List<Jack>();
            private int leftJacks;
            private bool isJackActivated;
            private readonly List<int> pendingJackObjectIds = new();

            int[] IPlayerAbility.AbilityArguments
            {
                get
                {
                    TryRestoreJacksFromPendingIds();

                    List<int> jackObjectIds = pendingJackObjectIds.Count > 0
                        ? [.. pendingJackObjectIds]
                        : isJackActivated
                            ? globalJacks?.Select(jack => jack.ObjectId).ToList() ?? []
                            : localJacks.Select(jack => jack.ObjectId).ToList();

                    return
                    [
                        IsUsurped.AsInt(),
                        leftJacks,
                        isJackActivated.AsInt(),
                        jackObjectIds.Count,
                        ..jackObjectIds
                    ];
                }
            }

            /// <summary>
            /// カメラモード中かどうかのフラグ
            /// </summary>
            private bool isInCameraMode = false;

            /// <summary>
            /// カメラモード中は壁を無視して他のプレイヤーを表示
            /// </summary>
            bool IPlayerAbility.EyesightIgnoreWalls => isInCameraMode;

            /// <summary>
            /// 役職能力のコンストラクタ。
            /// </summary>
            /// <param name="player">割り当て対象のプレイヤー</param>
            /// <param name="isUsurped">能力が簒奪されている場合、true</param>
            public Ability(GamePlayer player, bool isUsurped, int initialLeftJacks, bool isJackActivated, int[] jackObjectIds) : base(player, isUsurped)
            {
                this.leftJacks = initialLeftJacks;
                this.isJackActivated = isJackActivated;
                pendingJackObjectIds.AddRange(jackObjectIds);
                TryRestoreJacksFromPendingIds();

                if (AmOwner)
                {
                    // チェインシフト直後など、他クライアントでオブジェクト生成が遅れるケースに備えて
                    // 保留中IDの復元を定期的に再試行する。
                    GameOperatorManager.Instance!.Subscribe<GameUpdateEvent>(_ =>
                    {
                        TryRestoreJacksFromPendingIds();
                    }, this);

                    var placeButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.Ability,
                        placeCoolDownOption, "place", placeSprite, null, _ =>
                        {
                            TryRestoreJacksFromPendingIds();
                            return globalJacks == null && this.leftJacks > 0;
                        })
                        .SetAsUsurpableButton(this);

                    placeButton.OnClick = (button) =>
                    {
                        TryRestoreJacksFromPendingIds();
                        if (this.leftJacks <= 0) return;

                        // 1. プレイヤーの現在の「確実に安全な」足元位置
                        Virial.Compat.Vector2 playerPos = PlayerControl.LocalPlayer.GetTruePosition();
                        Virial.Compat.Vector2 finalPos = playerPos;

                        // 2. 周囲 0.6f にある壁をすべて取得（1.0fだと角で跳ねすぎるので0.6fがベスト）
                        var hits = UnityEngine.Physics2D.OverlapCircleAll(playerPos, 0.6f, UnityEngine.LayerMask.GetMask("Ship", "Decls"));

                        if (hits.Length > 0)
                        {
                            Virial.Compat.Vector2 pushCorrection = Virial.Compat.Vector2.Zero;

                            foreach (var hit in hits)
                            {
                                Virial.Compat.Vector2 wallPoint = hit.ClosestPoint(playerPos);
                                float dist = playerPos.Distance(wallPoint);

                                // 壁に近すぎる（0.5f未満）場合
                                if (dist < 0.5f)
                                {
                                    // 壁からプレイヤーに向かうベクトル（安全な方向）
                                    Virial.Compat.Vector2 escapeDir = (playerPos - wallPoint).Normalized;

                                    // 足りない距離分だけ「プレイヤー側」へ押し戻す
                                    // これにより、上下左右どの壁であっても「内側」へ補正されます
                                    pushCorrection += escapeDir * (0.5f - dist);
                                }
                            }
                            finalPos += pushCorrection;
                        }

                        // 3. 設置（Z軸はTOR方式）
                        float z = finalPos.y / 1000f + 0.01f;
                        var obj = NebulaSyncObject.LocalInstantiate(Jack.MyLocalTag, new float[] { finalPos.x, finalPos.y });
                        localJacks.Add((obj.SyncObject as Jack)!);

                        leftJacks--;
                        placeButton.UpdateUsesIcon(leftJacks.ToString());
                        placeButton.StartCoolDown();
                    };

                    placeButton.ShowUsesIcon(2, this.leftJacks.ToString());

                    /// <summary>
                    /// 現在表示中のカメラインデックス
                    /// </summary>
                    int currentCameraIndex = 0;

                    /// <summary>
                    /// カメラ状態インジケーター
                    /// </summary>
                    GameObject? cameraIndicator = null;
                    TMPro.TextMeshPro? indicatorText = null;

                    /// <summary>
                    /// 元のShadowQuadの状態を保存
                    /// </summary>
                    bool originalShadowQuadActive = true;

                    var cameraButton = NebulaAPI.Modules.EffectButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.SecondaryAbility,
                    cameraCoolDownOption, cameraDurationOption, "camera", cameraSprite, null, _ =>
                    {
                        TryRestoreJacksFromPendingIds();
                        return globalJacks != null && globalJacks.Count > 0;
                    })
                    .SetAsUsurpableButton(this);

                    // カメラモード中（視点移動中）でもボタンを使用可能にする
                    cameraButton.Availability = (button) =>
                    {
                        // 通常の使用条件 または カメラモード中
                        return (MyPlayer.CanMove || isInCameraMode) && 
                               !MyPlayer.VanillaPlayer.inVent && 
                               !MyPlayer.VanillaPlayer.onLadder && 
                               !MyPlayer.VanillaPlayer.inMovingPlat;
                    };

                    // カメラモード中でもクールダウンが進むようにする
                    var cooldownTimer = cameraButton.CoolDownTimer as TimerImpl;
                    if (cooldownTimer != null)
                    {
                        var originalPredicate = cooldownTimer.Predicate;
                        cooldownTimer.SetPredicate(() => originalPredicate?.Invoke() == true || isInCameraMode);
                    }

                    // エフェクトタイマーも同様に（カメラモード中でも時間が進む）
                    var effectTimer = cameraButton.EffectTimer as TimerImpl;
                    if (effectTimer != null)
                    {
                        var originalPredicate = effectTimer.Predicate;
                        effectTimer.SetPredicate(() => originalPredicate?.Invoke() == true || isInCameraMode);
                    }

                    /// <summary>
                    /// カメラモードを開始する処理
                    /// </summary>
                    void StartCameraMode()
                    {
                        TryRestoreJacksFromPendingIds();
                        if (globalJacks == null || globalJacks.Count == 0) return;

                        isInCameraMode = true;
                        currentCameraIndex = 0;

                        // プレイヤーの移動を停止
                        PlayerControl.LocalPlayer.moveable = false;
                        PlayerControl.LocalPlayer.NetTransform.Halt();

                        // 視界の壁判定を無効化（影を非表示にする）
                        var shadowCollab = UnityEngine.Object.FindObjectOfType<ShadowCollab>();
                        if (shadowCollab != null && shadowCollab.ShadowQuad != null)
                        {
                            originalShadowQuadActive = shadowCollab.ShadowQuad.gameObject.activeSelf;
                            shadowCollab.ShadowQuad.gameObject.SetActive(false);
                        }

                        // カメラインジケーターを作成
                        CreateCameraIndicator();

                        // 最初のJackにカメラを向ける
                        UpdateCameraTarget();
                    }

                    /// <summary>
                    /// カメラモードを終了する処理
                    /// </summary>
                    void EndCameraMode()
                    {
                        if (!isInCameraMode) return;
                        isInCameraMode = false;

                        // プレイヤーの移動を再開
                        PlayerControl.LocalPlayer.moveable = true;

                        // カメラをプレイヤーに戻す
                        AmongUsUtil.SetCamTarget();

                        // 視界の壁判定を元に戻す（影を再表示）
                        var shadowCollab = UnityEngine.Object.FindObjectOfType<ShadowCollab>();
                        if (shadowCollab != null && shadowCollab.ShadowQuad != null)
                        {
                            shadowCollab.ShadowQuad.gameObject.SetActive(originalShadowQuadActive);
                        }

                        // カメラインジケーターを破棄
                        DestroyCameraIndicator();
                    }

                    /// <summary>
                    /// カメラの追従対象を現在のJackに設定
                    /// </summary>
                    void UpdateCameraTarget()
                    {
                        if (globalJacks == null || globalJacks.Count == 0 || !isInCameraMode) return;

                        currentCameraIndex = Mathf.Clamp(currentCameraIndex, 0, globalJacks.Count - 1);
                        var targetJack = globalJacks[currentCameraIndex];

                        // JackのBehaviourをカメラの追従対象に設定（スムーズに移動）
                        AmongUsUtil.SetCamTarget(targetJack.MyBehaviour);

                        // インジケーターテキストを更新
                        if (indicatorText != null)
                        {
                            indicatorText.text = $"[CAMERA {currentCameraIndex + 1}/{globalJacks.Count}]";
                        }
                    }

                    /// <summary>
                    /// カメラ状態インジケーターを作成
                    /// </summary>
                    void CreateCameraIndicator()
                    {
                        DestroyCameraIndicator();

                        // HudManagerに追従するインジケーター
                        cameraIndicator = new GameObject("JackCameraIndicator");
                        cameraIndicator.layer = LayerMask.NameToLayer("UI");
                        cameraIndicator.transform.SetParent(HudManager.Instance.transform, false);
                        cameraIndicator.transform.localPosition = new Virial.Compat.Vector3(0f, 0f, -50f);

                        // テキストインジケーター
                        var textObj = new GameObject("IndicatorText");
                        textObj.layer = LayerMask.NameToLayer("UI");
                        textObj.transform.SetParent(cameraIndicator.transform, false);
                        indicatorText = textObj.AddComponent<TMPro.TextMeshPro>();
                        indicatorText.text = $"[CAMERA {currentCameraIndex + 1}/{globalJacks.Count}]";
                        indicatorText.fontSize = 3f;
                        indicatorText.alignment = TextAlignmentOptions.TopLeft;
                        indicatorText.color = new Virial.Color(1f, 0.3f, 0.3f, 1f).ToUnityColor();
                        indicatorText.fontStyle = TMPro.FontStyles.Bold;
                        indicatorText.transform.localPosition = new Virial.Compat.Vector3(-4.5f, 2.5f, -10f);
                        indicatorText.sortingOrder = 100;

                        // 操作説明
                        var helpObj = new GameObject("HelpText");
                        helpObj.layer = LayerMask.NameToLayer("UI");
                        helpObj.transform.SetParent(cameraIndicator.transform, false);
                        var helpText = helpObj.AddComponent<TMPro.TextMeshPro>();
                        helpText.text = "ESC:Exit";
                        helpText.fontSize = 1.5f;
                        helpText.alignment = TextAlignmentOptions.TopLeft;
                        helpText.color = new Virial.Color(0.8f, 0.8f, 0.8f, 0.8f).ToUnityColor();
                        helpText.transform.localPosition = new Virial.Compat.Vector3(-4.5f, 2.0f, -10f);
                        helpText.sortingOrder = 100;
                    }

                    /// <summary>
                    /// カメラインジケーターを破棄
                    /// </summary>
                    void DestroyCameraIndicator()
                    {
                        if (cameraIndicator != null)
                        {
                            UnityEngine.Object.Destroy(cameraIndicator);
                            cameraIndicator = null;
                        }
                        indicatorText = null;
                    }


                    // ボタンクリック時の処理
                    cameraButton.OnClick = (button) =>
                    {
                        button.StartEffect();
                        if (button.IsInEffect)
                        {
                            // エフェクト開始 = カメラモード開始
                            if (!isInCameraMode)
                            {
                                StartCameraMode();
                            }
                            else
                            {
                                // 既にカメラモード中なら終了
                                button.InterruptEffect();
                            }
                        }
                    };

                    // エフェクト終了時にカメラモード終了 + クールダウン開始
                    cameraButton.OnEffectEnd = (button) =>
                    {
                        if (isInCameraMode)
                        {
                            EndCameraMode();
                        }
                        button.StartCoolDown();
                    };

                    // 次のカメラボタン（カメラモード中のみ表示）- 左側に表示
                    var nextCameraButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.None,
                        0f, "nextCamera", moveRightSprite, null, _ => isInCameraMode && globalJacks != null && globalJacks.Count > 1);
                    nextCameraButton.SetLabel("right");
                    nextCameraButton.Availability = (button) => isInCameraMode;
                    nextCameraButton.OnClick = (button) =>
                    {
                        currentCameraIndex++;
                        if (currentCameraIndex >= globalJacks.Count) currentCameraIndex = 0;
                        UpdateCameraTarget();
                    };

                    // 前のカメラボタン（カメラモード中のみ表示）- 右側に表示
                    var prevCameraButton = NebulaAPI.Modules.AbilityButton(this, MyPlayer, Virial.Compat.VirtualKeyInput.None,
                        0f, "prevCamera", moveLeftSprite, null, _ => isInCameraMode && globalJacks != null && globalJacks.Count > 1);
                    prevCameraButton.SetLabel("left");
                    prevCameraButton.Availability = (button) => isInCameraMode;
                    prevCameraButton.OnClick = (button) =>
                    {
                        currentCameraIndex--;
                        if (currentCameraIndex < 0) currentCameraIndex = globalJacks.Count - 1;
                        UpdateCameraTarget();
                    };

                    // カメラモード中の処理
                    GameOperatorManager.Instance.Subscribe<GameUpdateEvent>(ev =>
                    {
                        if (!isInCameraMode || globalJacks == null || globalJacks.Count == 0) return;

                        // プレイヤーの移動を継続的に停止
                        PlayerControl.LocalPlayer.moveable = false;

                        // lightSourceをカメラ位置に追従させる（毎フレーム）
                        if (PlayerControl.LocalPlayer.lightSource != null)
                        {
                            var camPos = Camera.main.transform.position;
                            PlayerControl.LocalPlayer.lightSource.transform.position = new Virial.Compat.Vector3(
                                camPos.x, camPos.y, PlayerControl.LocalPlayer.lightSource.transform.position.z
                            );
                        }

                        // ESCキーでカメラモード終了
                        if (Input.GetKeyDown(KeyCode.Escape))
                        {
                            EndCameraMode();
                            return;
                        }
                    }, this);

                    GameOperatorManager.Instance.Subscribe<PlayerVentEnterEvent>(ev =>
                    {
                        if (ev.Player.AmOwner)
                        {
                            var currentJacks = globalJacks ?? localJacks;
                            Jack? targetVent = currentJacks.FirstOrDefault(obj => ev.Vent.Id == obj.vent.Id);
                            if (targetVent != null)
                            {
                                RpcPlayJackVentAnimation.Invoke(targetVent.ObjectId);
                            }
                        }
                    }, this);
                    GameOperatorManager.Instance.Subscribe<PlayerVentExitEvent>(ev =>
                    {
                        if (ev.Player.AmOwner)
                        {
                            var currentJacks = globalJacks ?? localJacks;
                            Jack? targetVent = currentJacks.FirstOrDefault(obj => ev.Vent.Id == obj.vent.Id);
                            if (targetVent != null)
                            {
                                RpcPlayJackVentAnimation.Invoke(targetVent.ObjectId);
                            }
                        }
                    }, this);
                    GameOperatorManager.Instance.RegisterOnReleased(() =>
                    {
                        TryRestoreJacksFromPendingIds();

                        // チェインシフト等で役職を失う時点で、設置済みローカルJackは
                        // 「継承可能なグローバル同期オブジェクト」に昇格しておく。
                        if (!isJackActivated && localJacks.Count > 0)
                        {
                            PromoteLocalJacksForInheritance();
                        }

                        if (PlayerControl.LocalPlayer.inVent)
                        {
                            var currentJacks = globalJacks ?? localJacks;
                            Jack? targetVent = currentJacks.FirstOrDefault(obj => Vent.currentVent == obj.vent);
                            if (targetVent != null)
                            {
                                PlayerControl.LocalPlayer.MyPhysics.RpcExitVent(targetVent.vent.Id);
                                RpcPlayJackVentAnimation.Invoke(targetVent.ObjectId);
                            }
                        }
                    }, this);
                    GameOperatorManager.Instance.Subscribe<MeetingStartEvent>(ev =>
                    {
                        // 会議開始時にカメラモードを終了
                        if (isInCameraMode)
                        {
                            EndCameraMode();
                        }
                        OnMeetingStart(ev);
                    }, this);

                    // ゲーム終了時にリソースをクリーンアップ
                    GameOperatorManager.Instance.RegisterOnReleased(() =>
                    {
                        if (isInCameraMode)
                        {
                            EndCameraMode();
                        }
                        DestroyCameraIndicator();
                    }, this);
                }

            }

            /// <summary>
            /// 会議が始まったら、Jackが規定数置かれているかを確認する。
            /// </summary>
            /// <param name="ev"></param>
            void OnMeetingStart(MeetingStartEvent ev)
            {
                TryRestoreJacksFromPendingIds();
                if (MyPlayer.AmOwner && !isJackActivated && leftJacks <= 0 && localJacks != null && localJacks.Count > 0)
                {
                    ConvertLocalJacksToGlobalVents();
                }

                //// ローカルJackのリストはグローバル化したのでクリア
            }

            private void TryRestoreJacksFromPendingIds()
            {
                if (pendingJackObjectIds.Count == 0) return;

                List<Jack> restoredJacks = [];
                foreach (int objectId in pendingJackObjectIds)
                {
                    Jack? restoredJack = NebulaSyncObject.GetObject<Jack>(objectId);
                    if (restoredJack == null) return;
                    restoredJacks.Add(restoredJack);
                }

                if (isJackActivated) globalJacks = restoredJacks;
                else localJacks = restoredJacks;
                pendingJackObjectIds.Clear();
            }

            private void PromoteLocalJacksForInheritance()
            {
                if (localJacks == null || localJacks.Count == 0) return;

                foreach (var jack in localJacks)
                {
                    jack.ReflectInstantiationGlobally();
                    jack.HideBeforeActivation();
                    RpcHideJackBeforeActivation.Invoke(jack.ObjectId);
                }
            }

            private void ConvertLocalJacksToGlobalVents()
            {
                if (isJackActivated) return;
                if (localJacks == null || localJacks.Count == 0) return;

                // ローカルリストの要素をそのままグローバルリストとして扱う
                globalJacks = localJacks;

                globalJacks.Do(jack => jack.ReflectInstantiationGlobally());
                RpcActivateJacks.Invoke(globalJacks.Select(jack => jack.ObjectId).ToArray());
                isJackActivated = true;
            }

        }
    }
}