using System;

#nullable enable
namespace Squadronista.Solver;

public sealed class BonusAttributes : Attributes, IEquatable<BonusAttributes>
{
    public required int Cap { get; init; }

    public BonusAttributes ApplyTraining(Training training)
    {
        if (PhysicalAbility + MentalAbility + TacticalAbility == Cap)
        {
            int cappedPhysicalGained = training.CappedPhysicalGained;
            int cappedMentalGained = training.CappedMentalGained;
            int cappedTacticalGained = training.CappedTacticalGained;
            int num1 = PhysicalAbility + cappedPhysicalGained;
            int num2 = MentalAbility + cappedMentalGained;
            int num3 = TacticalAbility + cappedTacticalGained;
            Fix(ref num1, cappedPhysicalGained, ref num2, cappedMentalGained, ref num3, cappedTacticalGained);
            Fix(ref num2, cappedMentalGained, ref num1, cappedPhysicalGained, ref num3, cappedTacticalGained);
            Fix(ref num3, cappedTacticalGained, ref num1, cappedPhysicalGained, ref num2, cappedMentalGained);

            return new BonusAttributes
            {
                PhysicalAbility = num1,
                MentalAbility = num2,
                TacticalAbility = num3,
                Cap = Cap
            };
        }

        return new BonusAttributes
        {
            PhysicalAbility = PhysicalAbility + training.PhysicalGained,
            MentalAbility = MentalAbility + training.MentalGained,
            TacticalAbility = TacticalAbility + training.TacticalGained,
            Cap = Cap + training.PhysicalGained + training.MentalGained + training.TacticalGained
        };
    }

    private static void Fix(
        ref int mainStat,
        int mainGained,
        ref int otherStatA,
        int otherGainedA,
        ref int otherStatB,
        int otherGainedB)
    {
        if (mainGained <= 0 || mainStat <= 0)
            return;
        mainStat = 0;
        int num = Math.Abs(mainGained) / 2;
        otherStatA += otherGainedA >= 0 ? num : -num;
        otherStatB += otherGainedB >= 0 ? (Math.Abs(mainGained) - num) : -(Math.Abs(mainGained) - num);
    }

    public bool Equals(BonusAttributes? other)
    {
        if (other == null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return base.Equals(other) && Cap == other.Cap;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
            return true;
        if (obj is not BonusAttributes other)
            return false;
        return Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(base.GetHashCode(), Cap);
    }

    public static bool operator ==(BonusAttributes? left, BonusAttributes? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(BonusAttributes? left, BonusAttributes? right)
    {
        return !Equals(left, right);
    }
}