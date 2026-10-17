using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Squadronista;

public class FFXIVSquadronExport
{
    [JsonPropertyName("recruits")]
    public required Dictionary<string, Recruit> Recruits { get; set; }

    [JsonPropertyName("training")]
    public required List<int> Training { get; set; }

    [JsonPropertyName("rank")]
    public required int Rank { get; set; }

    public class Recruit
    {
        [JsonPropertyName("used")]
        public bool Used { get; set; } = true;

        [JsonPropertyName("class")]
        public required string Class { get; set; }

        [JsonPropertyName("level")]
        public required int Level { get; set; }

        [JsonPropertyName("race")]
        public required string Race { get; set; }

        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("exp")]
        public required int Exp { get; set; }

        [JsonPropertyName("chemistry")]
        public List<object> Chemistry { get; set; } = [];
    }
}