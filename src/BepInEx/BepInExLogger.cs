using BepInEx.Logging;
using FastResetUpdated.Shared;

namespace FastResetUpdated.BepInEx
{
    public sealed class BepInExLogger : IModLogger
    {
        private readonly ManualLogSource _log;

        public BepInExLogger(ManualLogSource log)
        {
            _log = log;
        }

        public void Msg(string message)
        {
            _log.LogInfo(message);
        }

        public void Warning(string message)
        {
            _log.LogWarning(message);
        }
    }
}
