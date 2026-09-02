using Colossal.UI.Binding;
using Game.Tools;
using Game.UI;
using Unity.Entities;

namespace BulldozerMarquee
{
    /// <summary>
    /// The bridge between the filter panel and <see cref="BulldozerMarqueeToolSystem"/>.
    /// <para>
    /// C# stays the source of truth: React only fires triggers, and every piece of
    /// state it renders arrives back through a binding. That matters most for the
    /// enabled flag, which the player can also change without touching the panel by
    /// picking another tool from the vanilla toolbar.
    /// </para>
    /// </summary>
    public partial class BulldozerMarqueeUISystem : UISystemBase
    {
        /// <summary>Binding group. Must match the string used by src/mods/bindings.ts.</summary>
        public const string Group = "BulldozerMarquee";

        private ToolSystem m_ToolSystem;
        private DefaultToolSystem m_DefaultToolSystem;
        private BulldozerMarqueeToolSystem m_Tool;

        private ValueBinding<bool> m_Enabled;
        private ValueBinding<int> m_Filters;
        private ValueBinding<int> m_SelectionCount;
        private ValueBinding<bool> m_PlaySfx;
        private ValueBinding<bool> m_ConfirmBulldoze;
        private ValueBinding<bool> m_PruneOnFilterChange;
        private ValueBinding<int> m_Mode;
        private ValueBinding<bool> m_SelectionClamped;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_ToolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            m_DefaultToolSystem = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            m_Tool = World.GetOrCreateSystemManaged<BulldozerMarqueeToolSystem>();

            // Seeded from the settings file so the panel opens the way the player
            // last left it, rather than resetting every session.
            AssetFilter savedFilters = Mod.Settings != null
                ? (AssetFilter)Mod.Settings.SavedFilters & AssetFilter.All
                : AssetFilter.All;

            // Deliberately not persisted. Marquee is the mode the tool is built
            // around and the one a player is most likely to want first, so a session
            // that happened to end in Freeform should not decide how the next one
            // opens.
            const SelectionMode startingMode = SelectionMode.Marquee;

            // C# -> React.
            AddBinding(m_Enabled = new ValueBinding<bool>(Group, "Enabled", false));
            AddBinding(m_Filters = new ValueBinding<int>(Group, "Filters", (int)savedFilters));
            AddBinding(m_Mode = new ValueBinding<int>(Group, "Mode", (int)startingMode));
            AddBinding(m_SelectionCount = new ValueBinding<int>(Group, "SelectionCount", 0));
            AddBinding(m_SelectionClamped = new ValueBinding<bool>(Group, "SelectionClamped", false));
            AddBinding(m_PlaySfx = new ValueBinding<bool>(Group, "PlaySfx", PlaySfxSetting));
            AddBinding(m_ConfirmBulldoze =
                new ValueBinding<bool>(Group, "ConfirmBulldoze", ConfirmBulldozeSetting));
            AddBinding(m_PruneOnFilterChange =
                new ValueBinding<bool>(Group, "PruneOnFilterChange", PruneOnFilterChangeSetting));

            // React -> C#.
            AddBinding(new TriggerBinding(Group, "Toggle", Toggle));
            AddBinding(new TriggerBinding<int>(Group, "ToggleFilter", ToggleFilter));
            AddBinding(new TriggerBinding(Group, "SetAllFilters", SetAllFilters));
            AddBinding(new TriggerBinding(Group, "Bulldoze", Bulldoze));
            AddBinding(new TriggerBinding(Group, "ClearSelection", ClearSelection));
            AddBinding(new TriggerBinding(Group, "ToggleSfx", ToggleSfx));
            AddBinding(new TriggerBinding(Group, "ToggleConfirmBulldoze", ToggleConfirmBulldoze));
            AddBinding(new TriggerBinding(
                Group, "TogglePruneOnFilterChange", TogglePruneOnFilterChange));
            AddBinding(new TriggerBinding<int>(Group, "SetMode", SetMode));

            m_Tool.filters = savedFilters;
            m_Tool.mode = startingMode;
            m_Tool.selectionChanged += OnSelectionChanged;
            m_ToolSystem.EventToolChanged += OnToolChanged;
        }

        /// <summary>Opens the panel and takes over the cursor, or hands the game back its default tool.</summary>
        private void Toggle()
        {
            m_ToolSystem.activeTool = m_Enabled.value
                ? (ToolBaseSystem)m_DefaultToolSystem
                : m_Tool;
        }

        /// <summary>
        /// Keeps the enabled flag honest when the active tool changes behind the
        /// panel's back — pressing Escape or picking the vanilla bulldozer both
        /// deactivate this tool without the button ever being clicked.
        /// </summary>
        private void OnToolChanged(ToolBaseSystem tool)
        {
            m_Enabled.Update(tool == m_Tool);
        }

        private void ToggleFilter(int bit)
        {
            AssetFilter flag = (AssetFilter)bit;

            // Ignore anything that is not one of the flags we published, so a stale
            // UI bundle cannot set junk bits on the mask.
            if ((flag & AssetFilter.All) != flag || flag == AssetFilter.None)
            {
                return;
            }

            ApplyFilters((AssetFilter)m_Filters.value ^ flag);
        }

        /// <summary>All-on unless everything is already on, in which case all-off.</summary>
        private void SetAllFilters()
        {
            ApplyFilters((AssetFilter)m_Filters.value == AssetFilter.All
                ? AssetFilter.None
                : AssetFilter.All);
        }

        /// <summary>
        /// Single write path for the filter mask: binding, tool and settings file all
        /// move together, so the checkboxes survive a restart.
        /// </summary>
        private void ApplyFilters(AssetFilter filters)
        {
            // Computed before the binding moves, since the old mask is the only
            // record of which categories were just switched off. Turning a filter
            // back on is not symmetrical and deliberately does nothing: the entities
            // it would re-add were never in the region test for this selection.
            AssetFilter removed = (AssetFilter)m_Filters.value & ~filters;

            m_Filters.Update((int)filters);
            m_Tool.filters = filters;

            if (removed != AssetFilter.None && PruneOnFilterChangeSetting)
            {
                m_Tool.PruneSelection(removed);
            }

            if (Mod.Settings != null)
            {
                Mod.Settings.SavedFilters = (int)filters;
                Mod.Settings.ApplyAndSave();
            }
        }

        private void SetMode(int mode)
        {
            // Anything unrecognised falls back to the mode that actually works,
            // rather than leaving the tool inert with no way to tell why.
            SelectionMode selected;

            switch (mode)
            {
                case (int)SelectionMode.Freeform:
                    selected = SelectionMode.Freeform;
                    break;

                case (int)SelectionMode.Polygon:
                    selected = SelectionMode.Polygon;
                    break;

                default:
                    selected = SelectionMode.Marquee;
                    break;
            }

            if ((SelectionMode)m_Mode.value == selected)
            {
                return;
            }

            // A gesture in progress belongs to the mode that started it: half a
            // polygon left standing would be reinterpreted by whichever mode is
            // switched to. Dropped before the selection, since abandoning the gesture
            // is what makes dropping the selection unambiguous.
            m_Tool.CancelGesture();

            // Switching mode abandons whatever the old one had picked out; carrying a
            // marquee selection into freeform would be selection the player can no
            // longer see how they made.
            m_Tool.ClearSelection();

            m_Mode.Update((int)selected);
            m_Tool.mode = selected;
        }

        /// <summary>
        /// The options page owns this value; the panel checkbox is a second view of
        /// it. Reading through the setting rather than caching a copy is what stops
        /// the two drifting apart.
        /// </summary>
        private static bool PlaySfxSetting => Mod.Settings != null && Mod.Settings.PlaySfx;

        /// <summary>
        /// Defaults to true when settings are somehow unavailable: an unexpected
        /// prompt is a far better failure than an unexpected deletion.
        /// </summary>
        private static bool ConfirmBulldozeSetting => Mod.Settings == null || Mod.Settings.ConfirmBulldoze;

        /// <summary>
        /// Defaults to true for the same reason as the confirmation prompt: if the
        /// setting cannot be read, the safe reading is the one where the highlighted
        /// selection and the ticked filters agree.
        /// </summary>
        private static bool PruneOnFilterChangeSetting =>
            Mod.Settings == null || Mod.Settings.PruneOnFilterChange;

        private void Bulldoze()
        {
            if (m_Tool.selectionCount == 0)
            {
                return;
            }

            m_Tool.BulldozeSelection();

            // Played here rather than with the deletion so it lands on the click. The
            // work itself is deferred a frame, which would be audible as lag.
            if (PlaySfxSetting)
            {
                BulldozeAudio.Play();
            }
        }

        private void ToggleSfx()
        {
            if (Mod.Settings == null)
            {
                return;
            }

            Mod.Settings.PlaySfx = !Mod.Settings.PlaySfx;

            // Persist immediately, so the panel checkbox behaves like the options page.
            Mod.Settings.ApplyAndSave();
            m_PlaySfx.Update(Mod.Settings.PlaySfx);
        }

        private void ToggleConfirmBulldoze()
        {
            if (Mod.Settings == null)
            {
                return;
            }

            Mod.Settings.ConfirmBulldoze = !Mod.Settings.ConfirmBulldoze;
            Mod.Settings.ApplyAndSave();
            m_ConfirmBulldoze.Update(Mod.Settings.ConfirmBulldoze);
        }

        private void TogglePruneOnFilterChange()
        {
            if (Mod.Settings == null)
            {
                return;
            }

            Mod.Settings.PruneOnFilterChange = !Mod.Settings.PruneOnFilterChange;
            Mod.Settings.ApplyAndSave();
            m_PruneOnFilterChange.Update(Mod.Settings.PruneOnFilterChange);
        }

        /// <summary>
        /// Picks up changes made on the options page, which has no way to notify the
        /// panel directly. A couple of bool comparisons per frame is cheaper than
        /// wiring settings-changed callbacks and getting their lifetimes wrong.
        /// </summary>
        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (m_PlaySfx.value != PlaySfxSetting)
            {
                m_PlaySfx.Update(PlaySfxSetting);
            }

            if (m_ConfirmBulldoze.value != ConfirmBulldozeSetting)
            {
                m_ConfirmBulldoze.Update(ConfirmBulldozeSetting);
            }

            if (m_PruneOnFilterChange.value != PruneOnFilterChangeSetting)
            {
                m_PruneOnFilterChange.Update(PruneOnFilterChangeSetting);
            }
        }

        private void ClearSelection() => m_Tool.ClearSelection();

        private void OnSelectionChanged()
        {
            m_SelectionCount.Update(m_Tool.selectionCount);
            m_SelectionClamped.Update(m_Tool.selectionClamped);
        }

        protected override void OnDestroy()
        {
            // Both are long-lived systems, so leaving the handlers attached would
            // keep this system alive and fire callbacks against disposed bindings.
            if (m_Tool != null)
            {
                m_Tool.selectionChanged -= OnSelectionChanged;
            }

            if (m_ToolSystem != null)
            {
                m_ToolSystem.EventToolChanged -= OnToolChanged;
            }

            base.OnDestroy();
        }
    }
}
