using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Squadronista.Solver;
using Squadronista.Windows;

#nullable enable
namespace Squadronista;

public sealed class SquadronistaPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new WindowSystem(nameof(SquadronistaPlugin));
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IClientState _clientState;
    private readonly IPluginLog _pluginLog;
    private readonly IDataManager _dataManager;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly ICommandManager _commandManager;
    private readonly IGameGui _gameGui;
    private readonly IReadOnlyList<SquadronMission> _allMissions;
    private readonly MainWindow _mainWindow;

    public string Name => "Squadronista";
    public IReadOnlyList<Training> Trainings { get; }
    public List<SquadronMission> AvailableMissions { get; private set; } = new();
    public MissionResults? CurrentSquadronMissionResults { get; private set; }
    private SquadronState? _squadronState;
    private int _lastSelectedMissionRow = -1;
    private bool _waitingForCorrectRequiredAttrs;
    private int _waitingMissionId = -1;

    public SquadronistaPlugin(
        IDalamudPluginInterface pluginInterface,
        IClientState clientState,
        IPluginLog pluginLog,
        IDataManager dataManager,
        IAddonLifecycle addonLifecycle,
        ICommandManager commandManager,
        IGameGui gameGui)
    {
        if (dataManager == null)
            throw new ArgumentNullException(nameof(dataManager));

        ECommons.ECommonsMain.Init(pluginInterface, this);
        _pluginInterface = pluginInterface;
        _clientState = clientState;
        _pluginLog = pluginLog;
        _dataManager = dataManager;
        _addonLifecycle = addonLifecycle;
        _commandManager = commandManager;
        _gameGui = gameGui;

        // Load all missions from game data
        _allMissions = dataManager.GetExcelSheet<GcArmyExpedition>()
            ?.Where(x => x.RowId > 0)
            .Select(x => new SquadronMission
            {
                Id = (int)x.RowId,
                Name = x.Name.ToString(),
                Level = x.RequiredLevel,
                IsFlaggedMission = x.RowId switch
                {
                    7 or 14 or 15 or 34 => true,
                    _ => false
                },
                PossibleAttributes = Enumerable.Range(0, x.ExpeditionParams.Count)
                    .Select(i => new Attributes
                    {
                        PhysicalAbility = x.ExpeditionParams[i].RequiredPhysical,
                        MentalAbility = x.ExpeditionParams[i].RequiredMental,
                        TacticalAbility = x.ExpeditionParams[i].RequiredTactical
                    }).ToList().AsReadOnly()
            }).ToList().AsReadOnly() ?? new List<SquadronMission>().AsReadOnly();

        // Load all trainings from game data
        Trainings = dataManager.GetExcelSheet<GcArmyTraining>()
            ?.Where(x => x.RowId > 0 && x.RowId != 7)
            .Select(x => new Training
            {
                RowId = x.RowId,
                Name = x.Name.ToString(),
                PhysicalGained = x.PhysicalBonus,
                MentalGained = x.MentalBonus,
                TacticalGained = x.TacticalBonus
            }).ToList().AsReadOnly() ?? new List<Training>().AsReadOnly();

        _mainWindow = new MainWindow(this, pluginLog, addonLifecycle, gameGui, dataManager);
        _windowSystem.AddWindow(_mainWindow);

        // Register command
        _commandManager.AddHandler("/squadronista", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Squadronista window"
        });
        _commandManager.AddHandler("/squad", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Squadronista window (short alias)"
        });

        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        _pluginInterface.UiBuilder.OpenConfigUi += ToggleMainWindow;
        _clientState.Logout += ResetCharacterSpecificData;

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "GcArmyMemberList", UpdateSquadronState);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "GcArmyExpedition", UpdateExpeditionState);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "GcArmyExpedition", CheckForMissionChange);
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    private void ToggleMainWindow() => _mainWindow.IsOpen = !_mainWindow.IsOpen;

    private void ResetCharacterSpecificData(int type, int code)
    {
        _squadronState = null;
        CurrentSquadronMissionResults = null;
        AvailableMissions = new List<SquadronMission>();
    }

    private unsafe void UpdateSquadronState(AddonEvent type, AddonArgs args)
    {
        _pluginLog.Information("Updating squadron state...");

        var gcArmyManager = GcArmyManager.Instance();
        if (gcArmyManager == null)
        {
            _pluginLog.Warning("GcArmyManager is null");
            return;
        }

        if (gcArmyManager->Data == null)
        {
            _pluginLog.Warning("GcArmyManager Data is null - squadron data not loaded");
            return;
        }

        // Read squadron members
        var members = new List<SquadronMember>();
        var memberCount = gcArmyManager->GetMemberCount();
        _pluginLog.Debug($"Found {memberCount} squadron members");

        for (uint i = 0; i < memberCount && i < 8; i++)
        {
            var member = gcArmyManager->GetMember(i);
            if (member == null)
                continue;

            // Get member name from game data
            var name = $"Member {i + 1}"; // Default fallback

            // Try to get the actual name from ENpcResident
            if (member->ENpcResidentId > 0)
            {
                var enpcSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ENpcResident>();
                var enpcData = enpcSheet?.GetRowOrDefault(member->ENpcResidentId);
                var actualName = enpcData?.Singular.ToString();
                if (!string.IsNullOrEmpty(actualName))
                    name = actualName;
            }

            var physical = 0;
            var mental = 0;
            var tactical = 0;

            for (int z = 0; z < memberCount && z < 8; z++)
            {
                var nameIndex = 6 + z * 15;
                var n = (AtkUnitBase*)args.Addon.Address;
                var n2 = n->AtkValues[nameIndex];
                if (n2.Type == AtkValueType.String)
                {
                    if (n2.GetValueAsString() == name)
                    {
                        physical = n->AtkValues[13 + z * 15].Int;
                        mental = n->AtkValues[14 + z * 15].Int;
                        tactical = n->AtkValues[15 + z * 15].Int;
                        break;
                    }
                }
            }

            _pluginLog.Debug($"""
                {MemoryHelper.ReadRaw((nint)member, 85).ToHexString()}
                Member {i}
                {name}
                Level {member->Level}
                ClassJob {(Job)member->ClassJob}
                Physical {physical}
                Mental {mental}
                Tactical {tactical}
                Experience {member->Experience}
                """);

            members.Add(SquadronMember.Create(
                name,
                member->Level,
                member->ClassJob,
                (Solver.Race)member->Race,
                member->Experience
            ).CalculateGrowth(
                _dataManager,
                member->ClassJob,
                member->Experience,
                (byte)physical,
                (byte)mental,
                (byte)tactical
            ));
        }

        // Read bonus attributes
        var bonus = new BonusAttributes
        {
            PhysicalAbility = gcArmyManager->Data->BonusPhysical,
            MentalAbility = gcArmyManager->Data->BonusMental,
            TacticalAbility = gcArmyManager->Data->BonusTactical,
            Cap = 0
        };

        _squadronState = new SquadronState
        {
            Members = members.AsReadOnly(),
            Bonus = bonus,
            CurrentTraining = 0
        };

        RecalculateMissionResults();
    }

    private unsafe void UpdateExpeditionState(AddonEvent type, AddonArgs args)
    {
        _pluginLog.Information("Updating expedition state...");

        var addon = (AddonGcArmyExpedition*)args.Addon.Address;
        if (addon == null)
            return;

        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentGcArmyExpedition.Instance();
        if (agent == null)
            return;

        // Update available missions
        AvailableMissions = _allMissions.ToList();

        // Reset last selected mission to force recalculation
        _lastSelectedMissionRow = -1;
        RecalculateMissionResults();
    }

    private unsafe void CheckForMissionChange(AddonEvent type, AddonArgs args)
    {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentGcArmyExpedition.Instance();
        if (agent == null)
            return;

        var currentMissionId = Utils.SelectedMission;

        if (agent->SelectedRow != _lastSelectedMissionRow)
        {
            _lastSelectedMissionRow = agent->SelectedRow;
            _waitingForCorrectRequiredAttrs = true;
            _waitingMissionId = currentMissionId;
            RecalculateMissionResults();
            return;
        }

        if (_waitingForCorrectRequiredAttrs && _waitingMissionId == currentMissionId)
        {
            RecalculateMissionResults();
        }
    }
    private unsafe void RecalculateMissionResults()
    {
        if (_squadronState == null || AvailableMissions.Count == 0)
            return;

        var missionId = Utils.SelectedMission;
        var selectedMission = AvailableMissions.FirstOrDefault(x => x.Id == missionId);
        if (selectedMission == null)
            return;
        if (!TryReadRequiredAttributesFromUld(out var required))
        {
            _waitingForCorrectRequiredAttrs = true;
            _waitingMissionId = selectedMission.Id;
            return;
        }

        var idx = FindMatchingIndex(selectedMission.PossibleAttributes, required);
        if (idx < 0)
        {
            _waitingForCorrectRequiredAttrs = true;
            _waitingMissionId = selectedMission.Id;
            return;
        }

        _waitingForCorrectRequiredAttrs = false;
        _waitingMissionId = -1;

        var missionAttributes = selectedMission.PossibleAttributes[idx];

        if (CurrentSquadronMissionResults != null &&
            CurrentSquadronMissionResults.Mission.Id == selectedMission.Id &&
            CurrentSquadronMissionResults.MissionAttributes.PhysicalAbility == missionAttributes.PhysicalAbility &&
            CurrentSquadronMissionResults.MissionAttributes.MentalAbility == missionAttributes.MentalAbility &&
            CurrentSquadronMissionResults.MissionAttributes.TacticalAbility == missionAttributes.TacticalAbility &&
            CurrentSquadronMissionResults.TaskResult != null &&
            !CurrentSquadronMissionResults.TaskResult.IsCompleted)
        {
            return;
        }

        CurrentSquadronMissionResults = new MissionResults
        {
            Mission = selectedMission,
            MissionAttributes = missionAttributes,
            TaskResult = Task.Run(() =>
            {
                var solver = new SquadronSolver(_pluginLog, _squadronState, Trainings);
                return solver.Calculate(selectedMission, missionAttributes);
            })
        };
    }


    private static int FindMatchingIndex(IReadOnlyList<Attributes> possible, Attributes current)
    {
        for (int i = 0; i < possible.Count; i++)
        {
            var a = possible[i];
            if (a.PhysicalAbility == current.PhysicalAbility &&
                a.MentalAbility == current.MentalAbility &&
                a.TacticalAbility == current.TacticalAbility)
                return i;
        }
        return -1;
    }
    private unsafe bool TryGetExpeditionUnit(out AtkUnitBase* unit)
    {
        unit = null;

        var addon = _gameGui.GetAddonByName("GcArmyExpedition", 1);
        if (addon == null || addon.Address == nint.Zero)
            return false;

        unit = (AtkUnitBase*)addon.Address;
        return unit != null && unit->IsVisible;
    }

    /// <summary>
    /// Reads required attributes from:
    /// root NodeList[16] (required attrs container component)
    ///  - inside: NodeList[2]/[4]/[6] (P/M/T component nodes)
    ///    - inside each: NodeList[2] (text node holding the number)
    /// </summary>
    private unsafe bool TryReadRequiredAttributesFromUld(out Attributes required)
    {
        required = new Attributes
        {
            PhysicalAbility = 0,
            MentalAbility = 0,
            TacticalAbility = 0
        };

        if (!TryGetExpeditionUnit(out var unit))
        {
            _pluginLog.Warning("[Squadronista] GcArmyExpedition unit not visible");
            return false;
        }

        var rootList = unit->UldManager.NodeList;
        var rootCount = unit->UldManager.NodeListCount;
        var containerNode = rootList[16];

        var containerComp = (AtkComponentNode*)containerNode;
        if (containerComp->Component == null)
            return false;

        var contUld = containerComp->Component->UldManager;
        var contList = contUld.NodeList;
        var contCount = contUld.NodeListCount;

        if (contList == null || contCount <= 0)
            return false;

        // Helper to fetch attribute component at index (2/4/6)
        AtkComponentNode* GetAttrComp(int idx, string label)
        {
            var node = contList[idx];
            if (node == null)
                return null;

            var comp = (AtkComponentNode*)node;
            if (comp->Component == null)
                return null;

            return comp;
        }

        var physComp = GetAttrComp(4, "Physical"); // NodeId=2
        var mentComp = GetAttrComp(2, "Mental");   // NodeId=4
        var tacComp = GetAttrComp(0, "Tactical"); // NodeId=6

        if (physComp == null || mentComp == null || tacComp == null)
            return false;

        int ReadValueFromAttrComponent(AtkComponentNode* attrComp, string label)
        {
            if (attrComp == null || attrComp->Component == null)
                return 0;

            var uld = attrComp->Component->UldManager;
            var list = uld.NodeList;
            var count = uld.NodeListCount;

            if (list == null || count <= 0)
                return 0;

            var start = list[0];
            if (start == null)
                return 0;

            var value = FindFirstDigitTextInSubtree(start, out var raw);
            _pluginLog.Debug($"[Squadronista] {label} parsed={value} raw='{raw}'");

            return value;
        }

        int p = ReadValueFromAttrComponent(physComp, "Physical");
        int m = ReadValueFromAttrComponent(mentComp, "Mental");
        int t = ReadValueFromAttrComponent(tacComp, "Tactical");

        _pluginLog.Information($"[Squadronista] Required attrs read: {p}/{m}/{t}");

        if (p <= 0 && m <= 0 && t <= 0)
            return false;

        required = new Attributes
        {
            PhysicalAbility = p,
            MentalAbility = m,
            TacticalAbility = t
        };

        return true;
    }
    private unsafe int FindFirstDigitTextInSubtree(AtkResNode* start, out string raw)
    {
        raw = string.Empty;
        if (start == null)
            return 0;

        var visited = new HashSet<nint>();
        var stack = new Stack<nint>();
        stack.Push((nint)start);

        while (stack.Count > 0)
        {
            var addr = stack.Pop();
            if (addr == nint.Zero)
                continue;

            if (!visited.Add(addr))
                continue;

            var n = (AtkResNode*)addr;
            var t = (AtkTextNode*)n;
            var s = t->NodeText.ToString();

            if (!string.IsNullOrWhiteSpace(s) && s.Any(char.IsDigit))
            {
                raw = s;
                var digits = new string(s.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out var v) ? v : 0;
            }

            if (n->ChildNode != null) stack.Push((nint)n->ChildNode);
            if (n->NextSiblingNode != null) stack.Push((nint)n->NextSiblingNode);
            if (n->PrevSiblingNode != null) stack.Push((nint)n->PrevSiblingNode);
        }

        return 0;
    }
    public SquadronState? GetSquadronState() => _squadronState;

    public void Dispose()
    {
        _commandManager.RemoveHandler("/squadronista");
        _commandManager.RemoveHandler("/squad");
        _windowSystem.RemoveAllWindows();
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi -= ToggleMainWindow;
        _clientState.Logout -= ResetCharacterSpecificData;
        _addonLifecycle.UnregisterListener(UpdateSquadronState);
        _addonLifecycle.UnregisterListener(UpdateExpeditionState);
        _addonLifecycle.UnregisterListener(CheckForMissionChange);
        ECommonsMain.Dispose();
    }

    public class MissionResults
    {
        public required SquadronMission Mission { get; init; }
        public required Attributes MissionAttributes { get; set; }
        public Task<SquadronSolver.CalculationResults>? TaskResult { get; init; }
    }
}