using System.Collections.Generic;
using Colossal;

namespace BulldozerMarquee
{
    /// <summary>
    /// English strings for the options page. Without a source registered for a
    /// locale the options UI falls back to displaying the raw key IDs, so this is
    /// not optional decoration.
    /// </summary>
    public class LocaleEN : IDictionarySource
    {
        private readonly Settings m_Setting;

        public LocaleEN(Settings setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Bulldozer Marquee" },
                { m_Setting.GetOptionTabLocaleID(Settings.MainSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Settings.ConfirmationGroup), "Safety" },
                { m_Setting.GetOptionGroupLocaleID(Settings.SelectionGroup), "Selection" },
                { m_Setting.GetOptionGroupLocaleID(Settings.FeedbackGroup), "Feedback" },

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(Settings.ConfirmBulldoze)),
                    "Confirm before bulldozing"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(Settings.ConfirmBulldoze)),
                    "Ask for confirmation before deleting the selection. " +
                    "This is the same setting as the 'Ask' checkbox on the filter panel."
                },

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(Settings.PruneOnFilterChange)),
                    "Keep the selection in sync with the filters"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(Settings.PruneOnFilterChange)),
                    "When a filter is unticked, immediately remove everything of that " +
                    "type from the current selection. With this off, unticking a filter " +
                    "only affects the next selection you draw and the one already " +
                    "highlighted is left as it is. " +
                    "This is the same setting as the 'Sync' checkbox on the filter panel."
                },

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(Settings.PlaySfx)),
                    "Play bulldoze sound"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(Settings.PlaySfx)),
                    "Play a sound effect when the Bulldoze button is used. " +
                    "This is the same setting as the 'SFX' checkbox on the marquee panel."
                },
            };
        }

        public void Unload()
        {
        }
    }
}
