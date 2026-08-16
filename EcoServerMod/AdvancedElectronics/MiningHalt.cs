using Eco.Core.Plugins;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.SharedTypes;

namespace Eco.Mods.TechTree
{
    /// <summary>Persisted config backing <see cref="MiningHalt"/>.</summary>
    public class MiningHaltSettings
    {
        public bool Halted { get; set; }
    }

    /// <summary>
    /// A server-wide administrator halt over every mining job (U12, R42) -- a safety
    /// control over irreversible terrain change, not a balance lever (KD6 explicitly
    /// rejected that). Persisted via <see cref="PluginConfig{T}"/>, the engine's own
    /// mod-settings storage, so it survives a restart without this mod needing its own
    /// save-game integration for one flag.
    /// </summary>
    public static class MiningHalt
    {
        private static readonly PluginConfig<MiningHaltSettings> config = new PluginConfig<MiningHaltSettings>("AdvancedElectronicsMiningHalt");

        /// <summary>Checked before every mining dispatch and every plot arrival (R42).</summary>
        public static bool IsHalted => config.Config.Halted;

        public static void SetHalted(bool halted)
        {
            config.Config.Halted = halted;
            config.SaveAsync().Wait();
        }
    }

    /// <summary>Admin command toggling the server-wide mining halt (R42).</summary>
    [ChatCommandHandler]
    public static class MiningHaltCommands
    {
        [ChatSubCommand("Drone", "Halt or resume every mining job on the server (admin). Usage: /drone haltmining <on|off>", ChatAuthorizationLevel.Admin)]
        public static void HaltMining(User user, string state)
        {
            var halted = state?.Trim().ToLowerInvariant() switch
            {
                "on" or "true" or "1" => true,
                "off" or "false" or "0" => false,
                _ => (bool?)null
            };

            if (halted == null)
            {
                user.MsgLocStr("Usage: /drone haltmining <on|off>");
                return;
            }

            MiningHalt.SetHalted(halted.Value);
            user.MsgLocStr($"Mining is now {(halted.Value ? "HALTED" : "resumed")} server-wide.");
        }
    }
}
