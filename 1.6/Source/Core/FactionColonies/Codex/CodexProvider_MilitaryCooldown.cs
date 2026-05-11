using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Dynamic codex content describing the post-battle cooldown system. The long multi-day
    /// cooldown was replaced by a small travel-back-home window — squads return to full
    /// availability as soon as they're physically home. Combat-effectiveness loss from
    /// injuries is now the natural penalty for losses, surfaced via the Healing status label.
    /// </summary>
    public class CodexProvider_MilitaryCooldown : ICodexDynamicProvider
    {
        public string GetDynamicContent(FactionFC faction)
        {
            string result = "FCCodexCooldownParams".Translate() + "\n\n";
            result += "FCCodexCooldownTravelDefense".Translate() + "\n";
            result += "FCCodexCooldownTravelOffense".Translate() + "\n\n";
            result += "FCCodexCooldownEffectivenessNote".Translate();
            return result;
        }
    }
}
