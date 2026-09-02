using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace DimThatHighlight
{
    /// <summary>
    /// The mod's entry in Options &gt; Mods, and the durable store behind the panel.
    /// <para>
    /// Registered at <c>OnLoad</c>, before any save is touched, so the values are
    /// already correct the first time the panel renders.
    /// </para>
    /// </summary>
    [FileLocation(nameof(DimThatHighlight))]
    [SettingsUIGroupOrder(ResetGroup)]
    [SettingsUIShowGroupName(ResetGroup)]
    public class Settings : ModSetting
    {
        public const string MainSection = "Main";
        public const string ResetGroup = "Reset";

        /// <summary>
        /// The stock hover colour, as the game's own <c>RenderingSettingsPrefab</c>
        /// constructor sets it: a pale blue at one tenth alpha.
        /// <para>
        /// Only a fallback. <see cref="DimThatHighlightUISystem"/> snapshots the
        /// value actually present in <c>RenderingSettingsData</c> before it writes to
        /// it, and restores that instead, so a game patch or another mod changing the
        /// stock colour does not leave this mod restoring the wrong thing.
        /// </para>
        /// </summary>
        public const int DefaultColorRgb = 0x8080FF;

        /// <summary>
        /// Stock alpha. Only a fallback, for the window before the singleton has been
        /// read: the system snapshots the real value and passes that through untouched.
        /// </summary>
        public const float DefaultAlpha = 0.1f;

        /// <summary>
        /// Full strength, meaning "the chosen colour exactly as picked". Strength is a
        /// multiplier on the colour rather than an absolute, so this is the value that
        /// makes the stock swatch identical to vanilla — not a maximum.
        /// </summary>
        public const int DefaultStrengthPercent = 100;

        public Settings(IMod mod)
            : base(mod)
        {
        }

        /// <summary>
        /// The chosen highlight colour, packed 0xRRGGBB.
        /// <para>
        /// Hidden from the options page: it is panel state that happens to live in the
        /// settings file because that is the mod's only durable store, not something
        /// anyone would want to edit as a number.
        /// </para>
        /// </summary>
        [SettingsUIHidden]
        public int ColorRgb { get; set; }

        /// <summary>
        /// How hard the highlight reads, 0-100, scaling the chosen colour toward black.
        /// Stored as a whole percentage rather than a fraction so the value in the
        /// settings file reads the same as the number on the panel, and so rounding
        /// happens once, here, rather than on every write.
        /// <para>
        /// This replaced an opacity setting that did nothing — see
        /// <c>DimThatHighlightUISystem.ToColor</c> for why the colour, not its alpha,
        /// is the lever that moves the outline.
        /// </para>
        /// </summary>
        [SettingsUIHidden]
        public int StrengthPercent { get; set; }

        /// <summary>
        /// Puts the highlight back to how the game draws it. Present on the options
        /// page as well as the panel so a highlight turned all the way down — and
        /// therefore invisible — can be recovered without having to find the panel
        /// first.
        /// </summary>
        [SettingsUIButton]
        [SettingsUISection(MainSection, ResetGroup)]
        public bool RestoreDefault
        {
            set => restoreDefaultRequested?.Invoke();
        }

        /// <summary>
        /// Raised by the options-page button. The UI system owns the snapshot of the
        /// stock colour, so it — not this class — decides what "default" means.
        /// </summary>
        public event System.Action restoreDefaultRequested;

        public override void SetDefaults()
        {
            ColorRgb = DefaultColorRgb;
            StrengthPercent = DefaultStrengthPercent;
        }
    }
}
