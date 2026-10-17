#nullable enable
namespace Squadronista.Solver;

public class Training
{
    public required uint RowId { get; init; }

    public required string Name { get; init; }

    public required int PhysicalGained { get; init; }

    public required int MentalGained { get; init; }

    public required int TacticalGained { get; init; }

    public int CappedPhysicalGained
    {
        get => Training.CalculateCapped(this.PhysicalGained, this.MentalGained, this.TacticalGained);
    }

    public int CappedMentalGained
    {
        get => Training.CalculateCapped(this.MentalGained, this.PhysicalGained, this.TacticalGained);
    }

    public int CappedTacticalGained
    {
        get => Training.CalculateCapped(this.TacticalGained, this.PhysicalGained, this.MentalGained);
    }

    private static int CalculateCapped(int mainStat, int otherA, int otherB)
    {
        if (mainStat > 0)
            return mainStat;
        return otherA == 40 || otherB == 40 ? -20 : -40;
    }
}
