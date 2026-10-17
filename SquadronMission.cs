using System.Collections.Generic;
using Squadronista.Solver;

#nullable enable
namespace Squadronista;

public sealed class SquadronMission
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required byte Level { get; init; }

    public required bool IsFlaggedMission { get; init; }

    public required IReadOnlyList<Attributes> PossibleAttributes { get; init; }
}
