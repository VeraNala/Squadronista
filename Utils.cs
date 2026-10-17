using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Squadronista;

public static class Utils
{
    public static unsafe int SelectedMission =>
        AgentGcArmyExpedition.Instance()->IsAgentActive() ? *(int*)(*(nint*)((nint)(AgentGcArmyExpedition.Instance()) + 40) + 6536) : 0;
}