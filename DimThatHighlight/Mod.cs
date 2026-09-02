using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace DimThatHighlight
{
    public class Mod : IMod
    {
        public static readonly ILog Log =
            LogManager.GetLogger($"{nameof(DimThatHighlight)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>
        /// The durable store for the chosen colour. The panel is the only place it is
        /// edited; this is where it survives a restart.
        /// </summary>
        public static Settings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            Settings = new Settings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            // Defaults first, then the saved file on top. Without this call a property
            // added since the settings file was last written gets C#'s zero value
            // rather than its intended default — which here would mean a saved colour
            // of pure black at zero opacity, i.e. no highlight at all.
            Settings.SetDefaults();

            // The second argument is the baseline the options page's reset restores to.
            AssetDatabase.global.LoadSettings(nameof(DimThatHighlight), Settings, new Settings(this));

            // UIUpdate is the phase for systems that only publish state to Gameface.
            // Writing RenderingSettingsData is a plain component write, not a
            // structural change, so it needs no barrier and no special phase.
            updateSystem.UpdateAt<DimThatHighlightUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));

            // Leaving the page registered outlives the mod and breaks the options menu.
            Settings?.UnregisterInOptionsUI();
            Settings = null;
        }
    }
}
