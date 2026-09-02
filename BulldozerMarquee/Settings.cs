using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace BulldozerMarquee
{
    /// <summary>
    /// The mod's entry in Options &gt; Mods.
    /// <para>
    /// A <see cref="ModSetting"/> is registered at <c>OnLoad</c>, before any save is
    /// touched, so the page is reachable from the main menu as well as in game and
    /// the value is already correct the first time the panel renders.
    /// </para>
    /// </summary>
    [FileLocation(nameof(BulldozerMarquee))]
    [SettingsUIGroupOrder(ConfirmationGroup, FeedbackGroup)]
    [SettingsUIShowGroupName(ConfirmationGroup, FeedbackGroup)]
    public class Settings : ModSetting
    {
        public const string MainSection = "Main";
        public const string ConfirmationGroup = "Confirmation";
        public const string FeedbackGroup = "Feedback";

        public Settings(IMod mod)
            : base(mod)
        {
        }

        /// <summary>
        /// Whether Bulldoze asks before deleting. Listed first, and in its own group,
        /// because it is the one option here that guards against losing work.
        /// </summary>
        [SettingsUISection(MainSection, ConfirmationGroup)]
        public bool ConfirmBulldoze { get; set; }

        /// <summary>
        /// Whether the bulldoze sound plays. Also mirrored by the checkbox beside the
        /// panel's Bulldoze button — this property is the single source of truth for
        /// both, so the two can never disagree.
        /// </summary>
        [SettingsUISection(MainSection, FeedbackGroup)]
        public bool PlaySfx { get; set; }

        /// <summary>
        /// The filter mask, persisted so a returning player keeps their checkboxes.
        /// Hidden from the options page: it is panel state that happens to live in
        /// the settings file because that is the mod's only durable store, not
        /// something anyone would want to edit as a number.
        /// </summary>
        [SettingsUIHidden]
        public int SavedFilters { get; set; }

        /// <summary>Last selected <see cref="SelectionMode"/>, persisted for the same reason.</summary>
        [SettingsUIHidden]
        public int SavedMode { get; set; }

        public override void SetDefaults()
        {
            ConfirmBulldoze = true;
            PlaySfx = true;
            SavedFilters = (int)AssetFilter.All;
            SavedMode = (int)SelectionMode.Marquee;
        }
    }
}
