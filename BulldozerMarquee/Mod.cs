using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace BulldozerMarquee
{
    public class Mod : IMod
    {
        public static readonly ILog Log =
            LogManager.GetLogger($"{nameof(BulldozerMarquee)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        /// <summary>The options-menu settings, shared with the panel's SFX checkbox.</summary>
        public static Settings Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info(nameof(OnLoad));

            Settings = new Settings(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));

            // Defaults first, then the saved file on top. Without this call a
            // property that is new since the settings file was last written gets
            // C#'s zero value rather than the intended default — which is how
            // "Confirm before bulldozing" arrived switched off despite defaulting
            // to on. Any option added from here on would hit the same trap.
            Settings.SetDefaults();

            // The second argument is the baseline the "reset" button restores to.
            AssetDatabase.global.LoadSettings(nameof(BulldozerMarquee), Settings, new Settings(this));

            // The sound sits beside the assembly in the deployed mod folder, which is
            // the only place it can be found at runtime — the source tree is not
            // present on a player's machine.
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                BulldozeAudio.Load(Path.GetDirectoryName(asset.path));
            }

            // ToolUpdate is the phase that reads input and refreshes tool previews;
            // the marquee has to run there to see the apply action at all.
            updateSystem.UpdateAt<BulldozerMarqueeToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<BulldozerMarqueeUISystem>(SystemUpdatePhase.UIUpdate);
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
