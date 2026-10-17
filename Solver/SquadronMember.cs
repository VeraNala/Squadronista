using System.Collections.Generic;
using Dalamud.Plugin.Services;
using ECommons;
using Lumina.Excel.Sheets;

#nullable enable
namespace Squadronista.Solver;

public sealed record SquadronMember
{
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required uint ClassJob { get; init; }
    public required Race Race { get; init; }
    public required uint Experience { get; init; }
    public int PhysicalAbility { get; init; }
    public int MentalAbility { get; init; }
    public int TacticalAbility { get; init; }
    public IReadOnlyList<(byte, byte, byte)>? GrowthParams { get; init; }

    public static SquadronMember Create(string name, int level, uint classJob, Race race, uint experience)
    {
        return new SquadronMember
        {
            Name = name,
            Level = level,
            ClassJob = classJob,
            Race = race,
            Experience = experience
        };
    }

    public SquadronMember CalculateGrowth(IDataManager dataManager, uint classJob, uint experience, byte physicalAbility, byte mentalAbility, byte tacticalAbility)
    {
        //PluginLog.Information($"Begin CalculateGrowth");
        var growthSheet = dataManager.GetExcelSheet<GcArmyMemberGrow>();
        if (growthSheet == null)
            return this;

        var growthData = growthSheet.FirstOrNull(x => x.ClassJob.RowId == classJob);
        if (growthData == null || !growthData.HasValue)
            return this;

        var paramsList = new List<(byte, byte, byte)>();
        var growth = growthData.Value;
        foreach (var memberParam in growth.MemberParams)
        {
            paramsList.Add((memberParam.Physical, memberParam.Mental, memberParam.Tactical));
        }
        paramsList.Add((growth.Unknown1, growth.Unknown2, growth.Unknown3));
        //PluginLog.Information($"{(growth.Unknown1, growth.Unknown2, growth.Unknown3)}");

        return this with
        {
            PhysicalAbility = physicalAbility,
            MentalAbility = mentalAbility,
            TacticalAbility = tacticalAbility,
            GrowthParams = paramsList
        };
    }

    public Attributes ToAttributes()
    {
        return new Attributes
        {
            PhysicalAbility = PhysicalAbility,
            MentalAbility = MentalAbility,
            TacticalAbility = TacticalAbility
        };
    }
}