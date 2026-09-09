using FastResetUpdated.Shared;
using MelonLoader;

namespace FastResetUpdated.MelonLoader
{
    public sealed class MelonLoaderLogger : IModLogger
    {
        private readonly MelonLogger.Instance _logger;

        public MelonLoaderLogger(MelonLogger.Instance logger)
        {
            _logger = logger;
        }

        public void Msg(string message)
        {
            _logger.Msg(message);
        }

        public void Warning(string message)
        {
            _logger.Warning(message);
        }
    }
}
