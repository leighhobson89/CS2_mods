using Colossal.Logging;
using Game;
using Game.Modding;

namespace MidnightToggle
{
    public class Mod : IMod
    {
        public static readonly ILog Log =
            LogManager.GetLogger($"{nameof(MidnightToggle)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            updateSystem.UpdateAt<MidnightToggleUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
        }
    }
}
