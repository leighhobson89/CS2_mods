using System.Collections.Generic;
using Colossal;

namespace DimThatHighlight
{
    /// <summary>
    /// English strings for the options page. Without a source registered for a locale
    /// the options UI falls back to displaying the raw key IDs, so this is not
    /// optional decoration.
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
                { m_Setting.GetSettingsLocaleID(), "Dim That Highlight" },
                { m_Setting.GetOptionTabLocaleID(Settings.MainSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Settings.ResetGroup), "Highlight colour" },

                {
                    m_Setting.GetOptionLabelLocaleID(nameof(Settings.RestoreDefault)),
                    "Restore the default highlight"
                },
                {
                    m_Setting.GetOptionDescLocaleID(nameof(Settings.RestoreDefault)),
                    "Put the hover highlight back to the colour and strength the game " +
                    "draws it in. Same as the Reset button on the Highlight Properties " +
                    "panel."
                },
            };
        }

        public void Unload()
        {
        }
    }
}
