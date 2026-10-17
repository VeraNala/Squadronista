using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using Squadronista.Solver;

#nullable enable
namespace Squadronista.Windows;

internal sealed class MainWindow : Window, IDisposable
{
    private readonly SquadronistaPlugin _plugin;
    private readonly IPluginLog _pluginLog;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IGameGui _gameGui;
    private readonly IDataManager _dataManager;

    public MainWindow(
        SquadronistaPlugin plugin,
        IPluginLog pluginLog,
        IAddonLifecycle addonLifecycle,
        IGameGui gameGui,
        IDataManager dataManager)
        : base("Squadronista##SquadronistaMainWindow")
    {
        _plugin = plugin;
        _pluginLog = pluginLog;
        _addonLifecycle = addonLifecycle;
        _gameGui = gameGui;
        _dataManager = dataManager;

        Position = new Vector2(100f, 100f);
        PositionCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        SizeConstraints = new()
        {
            MinimumSize = new Vector2(150f, 50f),
            MaximumSize = new Vector2(9999f, 9999f)
        };

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "GcArmyExpedition", ExpeditionPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "GcArmyExpedition", ExpeditionPreFinalize);
        _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "GcArmyExpedition", ExpeditionPostUpdate);
    }

    private unsafe void ExpeditionPostSetup(AddonEvent type, AddonArgs args)
    {
        IsOpen = true;
        _pluginLog.Information("Opening GC member list...");

        var atkValues = stackalloc AtkValue[6];
        atkValues[0] = new AtkValue { Type = AtkValueType.Int, Int = 13 };
        atkValues[1] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        atkValues[2] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        atkValues[3] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        atkValues[4] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        atkValues[5] = new AtkValue { Type = AtkValueType.Int, Int = 0 };

        var addon = (AddonGcArmyExpedition*)args.Addon.Address;
        addon->AtkUnitBase.FireCallback(6U, atkValues, false);
    }

    private void ExpeditionPreFinalize(AddonEvent type, AddonArgs args) => IsOpen = false;

    private unsafe void ExpeditionPostUpdate(AddonEvent type, AddonArgs args)
    {
        var unitBase = (AtkUnitBase*)args.Addon.Address;
        short x = 0, y = 0;
        unitBase->GetPosition(&x, &y);
        ushort width = 0, height = 0;
        unitBase->GetSize(&width, &height, true);
        x += (short)width;

        if (Position.HasValue && (short)Position.Value.X == x && (short)Position.Value.Y == y)
            return;

        Position = new Vector2(x, y);
    }

    public override unsafe void Draw()
    {
        var state = _plugin.GetSquadronState();
        if (state == null)
        {
            ImGui.Text("Squadron state is null");
            return;
        }
        if (ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Copy, "FFXIVSquadron Export"))
        {
            var rank = GcArmyManager.Instance()->Data->Progress;
            var recruits = new Dictionary<string, FFXIVSquadronExport.Recruit>();
            var classJobSheet = _dataManager.GetExcelSheet<ClassJob>(language: Dalamud.Game.ClientLanguage.English);
            var i = 1;
            foreach (var member in state.Members)
            {
                recruits[$"{i}"] = new()
                {
                    Class = classJobSheet.GetRow(member.ClassJob).Name.ToMacroString(),
                    Level = member.Level,
                    Race = member.Race.ToString().ToLower().Replace("miqote", "miqo'te").Replace("aura", "au ra"),
                    Name = member.Name,
                    Exp = (int)member.Experience
                };
                i++;
            }
            var export = new FFXIVSquadronExport()
            {
                Rank = _dataManager.GetExcelSheet<GcArmyProgress>().GetRow(rank).Unknown1,
                Recruits = recruits,
                Training = [state.Bonus.PhysicalAbility, state.Bonus.MentalAbility, state.Bonus.TacticalAbility]
            };
            var output = JsonSerializer.SerializeToNode(export);
            if (output != null)
                ImGui.SetClipboardText(output.ToJsonString());
        }
        var agentExpedition = AgentGcArmyExpedition.Instance();
        if (agentExpedition == null || agentExpedition->SelectedRow >= _plugin.AvailableMissions.Count)
        {
            ImGui.Text($"Could not find mission... ({(agentExpedition != null ? agentExpedition->SelectedRow.ToString(CultureInfo.InvariantCulture) : "null")}; {_plugin.AvailableMissions.Count})");
            return;
        }

        var selectedMission = _plugin.AvailableMissions.FirstOrDefault(x => x.Id == Utils.SelectedMission);
        if (selectedMission == null) return;
        if (_plugin.CurrentSquadronMissionResults == null)
        {
            ImGui.Text("Rebuilding...");
            return;
        }

        var missionResults = _plugin.CurrentSquadronMissionResults;
        if (missionResults.Mission.Id != selectedMission.Id)
        {
            ImGui.Text("Recalculating...");
            return;
        }

        // Display mission name with requirements in parentheses
        ImGui.Text($"{selectedMission.Name} ({missionResults.MissionAttributes.PhysicalAbility} / {missionResults.MissionAttributes.MentalAbility} / {missionResults.MissionAttributes.TacticalAbility})");

        var task = missionResults.TaskResult;
        if (task != null && task.IsCompletedSuccessfully())
        {
            DrawCalculationResult(state, missionResults, task.Result);
        }
        else if (task != null && !task.IsCompleted)
        {
            ImGui.Text("Calculating...");
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No combination of members can achieve this.");
        }
    }

    private static void DrawCalculationResult(SquadronState state, SquadronistaPlugin.MissionResults missionResults, SquadronSolver.CalculationResults calculationResults)
    {
        if (calculationResults.Results.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No viable members.");
            return;
        }

        // Find the best result (prefer no training, then minimal training)
        SquadronSolver.CalculationResult? bestResult = null;
        foreach (var result in calculationResults.Results.Where(x => x.Members.Count == 4))
        {
            if (result.Trainings.All(t => t == null))
            {
                bestResult = result;
                break;
            }
        }

        if (bestResult == null)
        {
            foreach (var result in calculationResults.Results.Where(x => x.Members.Count == 4))
            {
                if (result.Trainings.Count(t => t != null) <= 2)
                {
                    bestResult = result;
                    break;
                }
            }
        }

        if (bestResult == null)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "No good solution found.");
            return;
        }

        // Calculate totals
        var totalPhysical = bestResult.Members.Sum(m => m.PhysicalAbility);
        var totalMental = bestResult.Members.Sum(m => m.MentalAbility);
        var totalTactical = bestResult.Members.Sum(m => m.TacticalAbility);

        if (state.Bonus != null)
        {
            totalPhysical += state.Bonus.PhysicalAbility;
            totalMental += state.Bonus.MentalAbility;
            totalTactical += state.Bonus.TacticalAbility;
        }

        // Calculate success rate
        var successRate = CalculateSuccessRate(
            totalPhysical, totalMental, totalTactical,
            missionResults.MissionAttributes.PhysicalAbility,
            missionResults.MissionAttributes.MentalAbility,
            missionResults.MissionAttributes.TacticalAbility,
            bestResult.MatchingAttributes);

        // Display success rate with color
        var rateColor = successRate >= 100 ? ImGuiColors.HealerGreen :
                       successRate >= 80 ? ImGuiColors.DalamudYellow :
                       ImGuiColors.DalamudRed;
        ImGui.TextColored(rateColor, $"{successRate}%");
        ImGui.SameLine();

        // Display training status
        var trainingCount = bestResult.Trainings.Count(t => t != null);
        if (trainingCount == 0)
        {
            ImGui.Text("no training");
        }
        else
        {
            ImGui.Text($"{trainingCount} training{(trainingCount > 1 ? "s" : "")}");
        }

        // Display squad members header
        ImGui.Text("Squadron Members (Lv35)");

        // Display each member with their individual stats
        foreach (var member in bestResult.Members)
        {
            ImGui.Indent();
            ImGui.TextColored(ImGuiColors.HealerGreen, member.Name);

            // Display member's individual stats in smaller text
            ImGui.SameLine();
            ImGui.TextDisabled($"({member.PhysicalAbility}/{member.MentalAbility}/{member.TacticalAbility})");
            ImGui.Unindent();
        }

        // Display trainings if any
        if (trainingCount > 0)
        {
            ImGui.Spacing();
            foreach (var training in bestResult.Trainings.Where(t => t != null))
            {
                ImGui.Indent();
                ImGui.TextColored(ImGuiColors.DalamudYellow, training!.Name);
                ImGui.Unindent();
            }
        }

        // Display final stats
        ImGui.Spacing();
        ImGui.Text("Final Stats: ");
        ImGui.SameLine();

        // Color code each stat based on whether it meets the requirement
        var physColor = totalPhysical >= missionResults.MissionAttributes.PhysicalAbility ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
        var mentColor = totalMental >= missionResults.MissionAttributes.MentalAbility ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;
        var tactColor = totalTactical >= missionResults.MissionAttributes.TacticalAbility ? ImGuiColors.HealerGreen : ImGuiColors.DalamudRed;

        ImGui.TextColored(physColor, totalPhysical.ToString());
        ImGui.SameLine();
        ImGui.Text(" / ");
        ImGui.SameLine();
        ImGui.TextColored(mentColor, totalMental.ToString());
        ImGui.SameLine();
        ImGui.Text(" / ");
        ImGui.SameLine();
        ImGui.TextColored(tactColor, totalTactical.ToString());
    }

    private static int CalculateSuccessRate(int physicalTotal, int mentalTotal, int tacticalTotal,
                                     int physicalReq, int mentalReq, int tacticalReq,
                                     int matchingAttributes)
    {
        int matchCount = 0;
        if (physicalTotal >= physicalReq) matchCount++;
        if (mentalTotal >= mentalReq) matchCount++;
        if (tacticalTotal >= tacticalReq) matchCount++;

        if (matchCount >= matchingAttributes)
        {
            // Calculate bonus based on how much we exceed requirements
            var physicalBonus = Math.Max(0, physicalTotal - physicalReq);
            var mentalBonus = Math.Max(0, mentalTotal - mentalReq);
            var tacticalBonus = Math.Max(0, tacticalTotal - tacticalReq);
            var totalBonus = physicalBonus + mentalBonus + tacticalBonus;

            return Math.Min(100, 60 + (totalBonus / 3));
        }

        return 50; // Base rate if requirements not met
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(ExpeditionPostSetup);
        _addonLifecycle.UnregisterListener(ExpeditionPreFinalize);
        _addonLifecycle.UnregisterListener(ExpeditionPostUpdate);
    }
}