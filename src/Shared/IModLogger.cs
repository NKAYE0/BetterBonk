namespace FastResetUpdated.Shared
{
    // Lets the shared game logic log messages without caring whether it's running
    // under MelonLoader or BepInEx — each loader provides its own tiny implementation.
    public interface IModLogger
    {
        void Msg(string message);
        void Warning(string message);
    }
}
