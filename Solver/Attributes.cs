using System;

#nullable enable
namespace Squadronista.Solver;

public class Attributes : IEquatable<Attributes>
{
    public required int PhysicalAbility { get; init; }

    public required int MentalAbility { get; init; }

    public required int TacticalAbility { get; init; }

    public bool Equals(Attributes? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        return this.PhysicalAbility == other.PhysicalAbility && this.MentalAbility == other.MentalAbility && this.TacticalAbility == other.TacticalAbility;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        return obj.GetType() == this.GetType() && this.Equals((Attributes)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine<int, int, int>(this.PhysicalAbility, this.MentalAbility, this.TacticalAbility);
    }

    public static bool operator ==(Attributes? left, Attributes? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Attributes? left, Attributes? right)
    {
        return !Equals(left, right);
    }

    public override string ToString()
    {
        return $"{this.PhysicalAbility} / {this.MentalAbility} / {this.TacticalAbility}";
    }
}
