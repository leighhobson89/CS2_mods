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
    [SettingsUIGroupOrder(ConfirmationGroup, SelectionGroup, FeedbackGroup)]
    [SettingsUIShowGroupName(ConfirmationGroup, SelectionGroup, FeedbackGroup)]
    public class Settings : ModSetting
    {
        public const string MainSection = "Main";
        public const string ConfirmationGroup = "Confirmation";
        public const string SelectionGroup = "Selection";
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
        /// <summary>
        /// Whether unticking a filter also drops the assets of that type out of a
        /// selection that has already been made.
        /// <para>
        /// On by default: with the option off, the checkboxes describe the next drag
        /// while the highlighted selection still describes the last one, and the two
        /// can disagree without anything on screen saying so. Mirrored by the panel's
        /// "Sync" checkbox.
        /// </para>
        /// </summary>
        [SettingsUISection(MainSection, SelectionGroup)]
        public bool PruneOnFilterChange { get; set; }

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

        public override void SetDefaults()
        {
            ConfirmBulldoze = true;
            PruneOnFilterChange = true;
            PlaySfx = true;
            SavedFilters = (int)AssetFilter.All;
        }
    }
}
