using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Simulation;
using Game.UI;
using Unity.Entities;

namespace MidnightToggle
{
    /// <summary>
    /// Publishes the toggle state to React and pins the in-game clock to midnight while it is on.
    /// C# owns the truth; the button only fires the "Toggle" trigger.
    /// </summary>
    public partial class MidnightToggleUISystem : UISystemBase
    {
        /// <summary>Binding group. Must match the string used by src/mods/midnight-toggle.tsx.</summary>
        public const string Group = "MidnightToggle";

        private const float MidnightHour = 0f;

        private PlanetarySystem m_PlanetarySystem;
        private ValueBinding<bool> m_Enabled;
        private bool m_IsEditor;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PlanetarySystem = World.GetOrCreateSystemManaged<PlanetarySystem>();

            // C# -> React: current toggle state.
            AddBinding(m_Enabled = new ValueBinding<bool>(Group, "Enabled", false));

            // React -> C#: the button click.
            AddBinding(new TriggerBinding(Group, "Toggle", Toggle));
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_IsEditor = mode.IsEditor();

            // The override lives on a runtime system, not in the save, so re-assert it on every load.
            Apply();
        }

        private void Toggle()
        {
            m_Enabled.Update(!m_Enabled.value);
            Apply();
        }

        private void Apply()
        {
            if (m_IsEditor || m_PlanetarySystem == null)
            {
                return;
            }

            bool enabled = m_Enabled.value;

            // overrideTime = false hands the clock (and therefore the lighting) back to vanilla,
            // so nothing needs to be snapshotted to restore the original state.
            m_PlanetarySystem.overrideTime = enabled;

            if (enabled)
            {
                m_PlanetarySystem.time = MidnightHour;
            }
        }

        protected override void OnDestroy()
        {
            // Never leave the override behind when the mod unloads.
            if (m_PlanetarySystem != null)
            {
                m_PlanetarySystem.overrideTime = false;
            }

            base.OnDestroy();
        }
    }
}
