using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;

#nullable enable
namespace Squadronista.Solver;

public sealed class SquadronSolver
{
    private readonly IPluginLog _pluginLog;
    private readonly SquadronState _state;
    private readonly IReadOnlyList<Training> _trainings;
    private readonly IReadOnlyList<List<SquadronMember>> _memberCombinations;
    private readonly IReadOnlyList<BonusAttributes> _allBonusCombinations;
    private readonly Dictionary<BonusAttributes, IReadOnlyList<Training>> _calculatedTrainings = new Dictionary<BonusAttributes, IReadOnlyList<Training>>();
    private List<BonusAttributes> _newTrainings;
    private int _calculatedTrainingSteps;

    public SquadronSolver(
      IPluginLog pluginLog,
      SquadronState state,
      IReadOnlyList<Training> trainings)
    {
        this._pluginLog = pluginLog;
        this._state = state;
        this._trainings = trainings;
        this._memberCombinations = (IReadOnlyList<List<SquadronMember>>)this.MakeCombinations<SquadronMember>(this._state.Members, 4).DistinctBy<List<SquadronMember>, string>((Func<List<SquadronMember>, string>)(x => string.Join("|", (IEnumerable<string>)x.Select<SquadronMember, string>((Func<SquadronMember, string>)(y => y.Name)).Order<string>()))).ToList<List<SquadronMember>>().AsReadOnly();
        foreach (List<SquadronMember> memberCombination in (IEnumerable<List<SquadronMember>>)this._memberCombinations)
            pluginLog.Verbose($"Squadron member combination: {string.Join(" ", memberCombination.Select<SquadronMember, string>((Func<SquadronMember, string>)(x => x.Name)))} → {memberCombination.Sum<SquadronMember>((Func<SquadronMember, int>)(x => x.PhysicalAbility))} / {memberCombination.Sum<SquadronMember>((Func<SquadronMember, int>)(x => x.MentalAbility))} / {memberCombination.Sum<SquadronMember>((Func<SquadronMember, int>)(x => x.TacticalAbility))}", Array.Empty<object>());
        this._allBonusCombinations = (IReadOnlyList<BonusAttributes>)this.CalculateAllBonuses().Where<BonusAttributes>((Func<BonusAttributes, bool>)(x => x != this._state.Bonus)).ToList<BonusAttributes>().AsReadOnly();
        this._calculatedTrainings[this._state.Bonus] = (IReadOnlyList<Training>)new List<Training>();
        this._newTrainings = this._calculatedTrainings.Keys.ToList<BonusAttributes>();
    }

    public CalculationResults Calculate(SquadronMission mission, Attributes missionAttributes)
    {
        var results = new CalculationResults
        {
            IsFlaggedMission = mission.IsFlaggedMission
        };

        int requiredMatchingStats = mission.IsFlaggedMission ? 3 : 2;
        results.Results.AddRange(SolveFor(mission, missionAttributes, requiredMatchingStats));

        return results;
    }

    public IEnumerable<SquadronSolver.CalculationResult> SolveFor(
      SquadronMission mission,
      Attributes missionAttributes,
      int requiredMatchingStats)
    {
        int minPhysical = missionAttributes.PhysicalAbility;
        int minMental = missionAttributes.MentalAbility;
        int minTactical = missionAttributes.TacticalAbility;
        bool flag = false;
        List<SquadronSolver.CalculationResult>.Enumerator enumerator = this.CalculateForAllMemberCombinations((int)mission.Level, this._state.Bonus).GetEnumerator();
        while (enumerator.MoveNext())
        {
            SquadronSolver.CalculationResult current = enumerator.Current;
            int matchingAttributes = SquadronSolver.CountStatMatches(current, minPhysical, minMental, minTactical);
            if (matchingAttributes >= requiredMatchingStats)
            {
                current.TrainingsCalculated = true;
                yield return current.WithExtra(mission, matchingAttributes);
                flag = true;
            }
        }
        enumerator = new List<SquadronSolver.CalculationResult>.Enumerator();
        if (!flag)
        {
            foreach (BonusAttributes bonusCombination in (IEnumerable<BonusAttributes>)this._allBonusCombinations)
            {
                enumerator = this.CalculateForAllMemberCombinations((int)mission.Level, bonusCombination).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    SquadronSolver.CalculationResult current = enumerator.Current;
                    int matchingAttributes = SquadronSolver.CountStatMatches(current, minPhysical, minMental, minTactical);
                    if (matchingAttributes >= requiredMatchingStats)
                    {
                        this.CalculateTrainingsForBonus(current);
                        if (current.TrainingsCalculated)
                            yield return current.WithExtra(mission, matchingAttributes);
                    }
                }
                enumerator = new List<SquadronSolver.CalculationResult>.Enumerator();
            }
        }
    }

    private static int CountStatMatches(
      SquadronSolver.CalculationResult x,
      int minPhysical,
      int minMental,
      int minTactical)
    {
        return (x.PhysicalAbility >= minPhysical ? 1 : 0) + (x.MentalAbility >= minMental ? 1 : 0) + (x.TacticalAbility >= minTactical ? 1 : 0);
    }

    private List<SquadronSolver.CalculationResult> CalculateForAllMemberCombinations(
      int requiredLevel,
      BonusAttributes bonus)
    {
        return this._memberCombinations.Where<List<SquadronMember>>((Func<List<SquadronMember>, bool>)(x => x.Any<SquadronMember>((Func<SquadronMember, bool>)(y => y.Level >= requiredLevel)))).Select<List<SquadronMember>, SquadronSolver.CalculationResult>((Func<List<SquadronMember>, SquadronSolver.CalculationResult>)(x => new SquadronSolver.CalculationResult(x, bonus))).ToList<SquadronSolver.CalculationResult>();
    }

    private List<List<T>> MakeCombinations<T>(IReadOnlyList<T> entries, int count)
    {
        if (count == 0)
            return new List<List<T>>();
        return count == 1 ? entries.Select<T, List<T>>((Func<T, List<T>>)(x => new List<T>()
    {
      x
    })).ToList<List<T>>() : entries.SelectMany<T, List<T>>((Func<T, IEnumerable<List<T>>>)(x =>
    {
        List<List<T>> objListList = this.MakeCombinations<T>((IReadOnlyList<T>)((IEnumerable<T>)entries).Except<T>((IEnumerable<T>)new T[1]
        {
        x
        }).ToList<T>(), count - 1);
        objListList.ForEach((Action<List<T>>)(c => c.Insert(0, x)));
        return (IEnumerable<List<T>>)objListList;
    })).ToList<List<T>>();
    }

    private IEnumerable<BonusAttributes> CalculateAllBonuses()
    {
        for (int physical = 0; physical <= this._state.Bonus.Cap; physical += 20)
        {
            for (int mental = 0; mental <= this._state.Bonus.Cap - physical; mental += 20)
            {
                yield return new BonusAttributes
                {
                    PhysicalAbility = physical,
                    MentalAbility = mental,
                    TacticalAbility = this._state.Bonus.Cap - physical - mental,
                    Cap = this._state.Bonus.Cap
                };
            }
        }
    }

    private void CalculateTrainingsForBonus(SquadronSolver.CalculationResult result)
    {
        if (result.Trainings.Count > 0)
            return;
        int physicalAbility = this._state.Bonus.PhysicalAbility;
        int mentalAbility = this._state.Bonus.MentalAbility;
        int tacticalAbility = this._state.Bonus.TacticalAbility;
        IReadOnlyList<Training> source;
        if (this._calculatedTrainings.TryGetValue(result.Bonus, out source))
        {
            result.Trainings = source;
            result.TrainingsCalculated = true;
        }
        else
        {
            this._pluginLog.Verbose($"Trying to find steps from {physicalAbility} to {result.Bonus.PhysicalAbility}, {mentalAbility} to {result.Bonus.MentalAbility} and {tacticalAbility} to {result.Bonus.TacticalAbility}", Array.Empty<object>());
            while (this._calculatedTrainingSteps < 10)
            {
                ++this._calculatedTrainingSteps;
                this._pluginLog.Debug($"Calculating training step {this._calculatedTrainingSteps} from currently {this._calculatedTrainings.Count} training combinations", Array.Empty<object>());
                List<BonusAttributes> bonusAttributesList = new List<BonusAttributes>();
                foreach (BonusAttributes newTraining in this._newTrainings)
                {
                    this._pluginLog.Verbose($"  From {newTraining.PhysicalAbility} / {newTraining.MentalAbility} / {newTraining.TacticalAbility}", Array.Empty<object>());
                    foreach (Training training in this._trainings)
                    {
                        BonusAttributes key = newTraining.ApplyTraining(training);
                        if (key.PhysicalAbility >= 0 && key.MentalAbility >= 0 && key.TacticalAbility >= 0 && !this._calculatedTrainings.ContainsKey(key))
                        {
                            this._pluginLog.Verbose($"  → {key.PhysicalAbility} / {key.MentalAbility} / {key.TacticalAbility} with {training.Name} (c = {key.Cap})", Array.Empty<object>());
                            this._calculatedTrainings[key] = this._calculatedTrainings[newTraining].Concat(new[] { training }).ToList().AsReadOnly();
                            bonusAttributesList.Add(key);
                        }
                    }
                }
                this._newTrainings = bonusAttributesList;
                this._pluginLog.Verbose($"Finished calculating, we now have {this._calculatedTrainings.Count} training combinations", Array.Empty<object>());
                if (bonusAttributesList.Count == 0)
                    break;
                if (this._calculatedTrainings.TryGetValue(result.Bonus, out source))
                {
                    if (source != null)
                    {
                        this._pluginLog.Verbose($"Found steps to reach {result.PhysicalAbility} / {result.MentalAbility} / {result.TacticalAbility}: {string.Join(", ", source.Select(x => x.Name))}", Array.Empty<object>());
                        result.Trainings = source;
                        result.TrainingsCalculated = true;
                    }
                    break;
                }
            }
        }
    }

    public sealed class CalculationResult
    {
        public SquadronMission? Mission { get; private set; }

        public int MatchingAttributes { get; private set; }

        public int PhysicalAbility { get; }

        public int MentalAbility { get; }

        public int TacticalAbility { get; }

        public List<SquadronMember> Members { get; }

        public BonusAttributes Bonus { get; }

        public IReadOnlyList<Training> Trainings { get; set; } = new List<Training>().AsReadOnly();

        public bool TrainingsCalculated { get; set; }

        public int TotalLevel
        {
            get => this.Members.Sum(x => x.Level);
        }

        public CalculationResult(List<SquadronMember> members, BonusAttributes bonus)
        {
            this.PhysicalAbility = members.Sum(x => x.PhysicalAbility) + bonus.PhysicalAbility;
            this.MentalAbility = members.Sum(x => x.MentalAbility) + bonus.MentalAbility;
            this.TacticalAbility = members.Sum(x => x.TacticalAbility) + bonus.TacticalAbility;
            this.Members = members;
            this.Bonus = bonus;
        }

        public SquadronSolver.CalculationResult WithExtra(
          SquadronMission mission,
          int matchingAttributes)
        {
            this.Mission = mission;
            this.MatchingAttributes = matchingAttributes;
            return this;
        }

        public int ToSuccessProbability() => this.MatchingAttributes != 3 ? 66 : 100;

        public string ToLabel()
        {
            if (this.Trainings.Count == 0)
                return $"{this.ToSuccessProbability()}%%, no training";
            return $"{this.ToSuccessProbability()}%%";
        }
    }

    public sealed class CalculationResults
    {
        public bool IsFlaggedMission { get; set; }

        public List<SquadronSolver.CalculationResult> Results { get; } = new List<SquadronSolver.CalculationResult>();
    }
}
