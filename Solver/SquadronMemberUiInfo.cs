using System;

#nullable disable
namespace Squadronista.Solver;

[Flags]
public enum SquadronMemberUiInfo
{
    Unknown1 = 1,
    IsPartOfMission = 2,
    NewChemistryAvailable = 8192, // 0x00002000
}
