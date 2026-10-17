using System.Collections.Generic;
using System.Threading.Tasks;

#nullable enable
namespace Squadronista.Solver;

public sealed class SquadronState
{
    private readonly Dictionary<(int id, Attributes attributes), Task<SquadronSolver.CalculationResults>> _calculationResults = new Dictionary<(int, Attributes), Task<SquadronSolver.CalculationResults>>();

    public required IReadOnlyList<SquadronMember> Members { get; init; }

    public required BonusAttributes Bonus { get; set; }

    public required uint CurrentTraining { get; set; }

    public Task<SquadronSolver.CalculationResults>? GetCalculation(
      SquadronMission mission,
      Attributes? attributes)
    {
        return attributes is null ? null : this._calculationResults.GetValueOrDefault((mission.Id, attributes));
    }

    public void SetCalculation(
      SquadronMission mission,
      Attributes attributes,
      Task<SquadronSolver.CalculationResults> task)
    {
        this._calculationResults[(mission.Id, attributes)] = task;
    }
}
