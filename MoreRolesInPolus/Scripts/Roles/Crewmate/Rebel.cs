using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

public class Rebel : DefinedRoleTemplate, DefinedSingleAbilityRole<Rebel.Ability>, DefinedRole, HasCitation
{
  private const float RebelVisionMultiplier = 1.5f;
  private static readonly BoolConfiguration OverrideTasksOption = NebulaAPI.Configurations.Configuration("options.role.rebel.overrideTasks", false);
  private static readonly IntegerConfiguration NumOfTasksRequiredOption = NebulaAPI.Configurations.Configuration("options.role.rebel.numOfTasksRequired", (1, 15, 1), 4, () => OverrideTasksOption);
  private static readonly BoolConfiguration UseMadmateIdentifySettingsOption = NebulaAPI.Configurations.Configuration("options.role.rebel.useMadmateIdentifySettings", true);
  private static readonly BoolConfiguration CanIdentifyImpostorsByTasksOption = NebulaAPI.Configurations.Configuration("options.role.rebel.canIdentifyImpostorsByTasks", true, () => !UseMadmateIdentifySettingsOption);
  private static readonly IntegerConfiguration TasksRequiredForIdentifyOption = NebulaAPI.Configurations.Configuration("options.role.rebel.tasksRequiredForIdentify", (1, 15, 1), 2, () => !UseMadmateIdentifySettingsOption && CanIdentifyImpostorsByTasksOption);
  private static readonly int[] DefaultMadmateTaskThresholds = [2, 4, 6];

  private Rebel() : base(
    "rebel",
    new(255, 105, 65),
    RoleCategory.CrewmateRole,
    NebulaTeams.CrewmateTeam,
    [OverrideTasksOption, NumOfTasksRequiredOption, UseMadmateIdentifySettingsOption, CanIdentifyImpostorsByTasksOption, TasksRequiredForIdentifyOption],
    withAssignmentOption: false,
    withOptionHolder: false
  ) { }

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  public Ability CreateAbility(GamePlayer player, int[] arguments) => new Ability(player);

  AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.CanLoadToMadmate;
  static public Rebel MyRole = new();
  RuntimeRole RuntimeAssignableGenerator<RuntimeRole>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player, arguments);

  public class Instance : RuntimeAssignableTemplate, RuntimeRole
  {
    private readonly int[] CachedArguments;
    private Ability? AbilityInstance;

    public Instance(GamePlayer player, int[] arguments) : base(player)
    {
      CachedArguments = arguments;
    }

    DefinedRole RuntimeRole.Role => Rebel.MyRole;

    void RuntimeAssignable.OnActivated()
    {
      AbilityInstance = Rebel.MyRole.CreateAbility(MyPlayer, CachedArguments).Register(this);
    }

    int[]? RuntimeAssignable.RoleArguments => (AbilityInstance as IPlayerAbility)?.AbilityArguments;

    IEnumerable<IPlayerAbility> RuntimeAssignable.MyAbilities => GetAbilities();

    private IEnumerable<IPlayerAbility> GetAbilities()
    {
      if (AbilityInstance == null) yield break;
      yield return AbilityInstance;
      foreach (var SubAbility in ((IPlayerAbility)AbilityInstance).SubAbilities) yield return SubAbility;
    }

    string RuntimeAssignable.DisplayName => AbilityInstance != null ? ((DefinedSingleAbilityRole<Ability>)Rebel.MyRole).GetDisplayName(AbilityInstance) : (Rebel.MyRole as DefinedAssignable).DisplayName;
    string RuntimeAssignable.DisplayColoredName => (this as RuntimeAssignable).DisplayName.Color(Rebel.MyRole.UnityColor);
    IEnumerable<DefinedAssignable> RuntimeAssignable.AssignableOnHelp => AbilityInstance != null ? [Rebel.MyRole, ..((IPlayerAbility)AbilityInstance).SubAssignableOnHelp] : [Rebel.MyRole];

    bool RuntimeAssignable.MyCrewmateTaskIsIgnored => true;
  }

  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [];
    private readonly List<byte> KnownImpostors = [];

    public Ability(GamePlayer player) : base(player, false)
    {
    }

    void OnCheckWin(PlayerCheckWinEvent Event)
    {
      if (Event.Player != MyPlayer) return;
      Event.SetWinIf(Event.GameEnd == NebulaGameEnd.ImpostorWin);
    }

    void OnBlockWin(PlayerBlockWinEvent Event)
    {
      if (Event.Player != MyPlayer) return;
      Event.SetBlockedIf(Event.GameEnd == NebulaGameEnd.CrewmateWin);
    }

    void OnDecorateName(PlayerDecorateNameEvent Event)
    {
      if (!AmOwner) return;
      if (Event.Player.AmOwner) return;
      if (!Event.Player.IsImpostor) return;
      if (!KnownImpostors.Contains(Event.Player.PlayerId)) return;

      Event.Color = new(Palette.ImpostorRed);
    }

    void OnGameStart(GameStartEvent Event)
    {
      if (!AmOwner) return;

      int RequiredTaskCount = GetRequiredTaskCountForTaskOverride();
      if (RequiredTaskCount > 0)
      {
        using (RPCRouter.CreateSection("RebelTask"))
        {
          MyPlayer.Tasks.Unbox().ReplaceTasksAndRecompute(RequiredTaskCount, 0, 0);
          MyPlayer.Tasks.Unbox().BecomeToOutsider();
        }
      }

      TryIdentifyImpostors();
    }

    [OnlyMyPlayer]
    void OnTaskCompleteLocal(PlayerTaskCompleteLocalEvent Event)
    {
      TryIdentifyImpostors();
    }

    [Local]
    void OnRoleShift(PlayerRoleSwapEvent Event)
    {
      if (Event.Role.Team != NebulaTeams.ImpostorTeam) return;
      if (!KnownImpostors.Contains(Event.Source.PlayerId)) return;

      KnownImpostors.Remove(Event.Source.PlayerId);
      if (!KnownImpostors.Contains(Event.Destination.PlayerId))
      {
        KnownImpostors.Add(Event.Destination.PlayerId);
      }
    }

    private void TryIdentifyImpostors()
    {
      if (!AmOwner) return;
      int IdentifyImpostorCount = GetIdentifyImpostorCount();
      if (IdentifyImpostorCount <= 0) return;

      while (KnownImpostors.Count < IdentifyImpostorCount)
      {
        int RequiredTasks = GetRequiredTasksForIdentifyStep(KnownImpostors.Count);
        if (MyPlayer.Tasks.CurrentCompleted < RequiredTasks) break;

        var CandidatePlayers = NebulaGameManager.Instance!.AllPlayerInfo
          .Where(Player => Player.Role.Role.Category == RoleCategory.ImpostorRole && !KnownImpostors.Contains(Player.PlayerId))
          .ToArray();

        if (CandidatePlayers.Length == 0) break;
        if (CandidatePlayers.Any(Player => !Player.IsDead))
        {
          CandidatePlayers = CandidatePlayers.Where(Player => !Player.IsDead).ToArray();
        }

        var SelectedImpostor = CandidatePlayers[System.Random.Shared.Next(CandidatePlayers.Length)].PlayerId;
        if (!KnownImpostors.Contains(SelectedImpostor))
        {
          KnownImpostors.Add(SelectedImpostor);
        }
      }
    }

    private static int GetIdentifyImpostorCount()
    {
      if (!UseMadmateIdentifySettingsOption)
      {
        return CanIdentifyImpostorsByTasksOption ? 1 : 0;
      }

      return GetSharableIntOrDefault("options.role.madmate.canIdentifyImpostors", 0);
    }

    private static int GetRequiredTasksForIdentifyStep(int StepIndex)
    {
      if (!UseMadmateIdentifySettingsOption)
      {
        return TasksRequiredForIdentifyOption;
      }

      int DefaultThreshold = StepIndex < DefaultMadmateTaskThresholds.Length
        ? DefaultMadmateTaskThresholds[StepIndex]
        : DefaultMadmateTaskThresholds[^1];

      return GetSharableIntOrDefault("numOfTasksToIdentifyImpostors" + StepIndex, DefaultThreshold);
    }

    private static int GetRequiredTaskCountForTaskOverride()
    {
      int IdentifyCount = GetIdentifyImpostorCount();
      if (IdentifyCount <= 0) return 0;
      return GetRequiredTasksForIdentifyStep(IdentifyCount - 1);
    }

    private static int GetSharableIntOrDefault(string Id, int DefaultValue)
    {
      var SharableVariable = NebulaAPI.Configurations.GetSharableVariable<int>(Id);
      return SharableVariable?.CurrentValue ?? DefaultValue;
    }

    void OnUpdateVentState(PlayerUpdateVentStateLocalEvent Event)
    {
      if (!AmOwner) return;
      if (Event.Player != MyPlayer) return;

      Event.CanUseVent = true;
      Event.CannotUseVentTemporary = false;
    }

    void OnLightRangeUpdate(LightRangeUpdateEvent Event)
    {
      if (!AmOwner) return;
      if (RebelVisionMultiplier > Event.LightQuickRange) Event.LightQuickRange = RebelVisionMultiplier;
    }

  }
}
