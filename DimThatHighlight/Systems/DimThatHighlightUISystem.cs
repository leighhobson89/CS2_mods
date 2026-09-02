using Colossal.UI.Binding;
using Game.Prefabs;
using Game.UI;
using Unity.Entities;
using UnityEngine;

namespace DimThatHighlight
{
    /// <summary>
    /// Owns the chosen highlight colour and writes it into the game's
    /// <see cref="RenderingSettingsData"/> singleton, which is where the hover outline
    /// gets its colour from.
    /// <para>
    /// The chain is: <c>BatchDataSystem</c> reads that singleton every update and hands
    /// it to <c>BatchDataJob</c>, which picks <c>m_HoveredColor</c> for any entity that
    /// is hovered and writes it into the <c>OutlineColors</c> per-instance property
    /// (<c>_Outlines_Color</c>). <c>OutlinesWorldUIPass</c> then draws the silhouette
    /// from it. Nothing in the game writes that singleton after
    /// <c>RenderingSettingsPrefab.Initialize</c>, so overwriting it is cooperative:
    /// there is nothing to patch and nothing to fight.
    /// </para>
    /// </summary>
    public partial class DimThatHighlightUISystem : UISystemBase
    {
        /// <summary>Binding group. Must match GROUP in src/mods/bindings.ts.</summary>
        public const string Group = "DimThatHighlight";

        private EntityQuery m_RenderingSettingsQuery;

        private ValueBinding<bool> m_Enabled;
        private ValueBinding<int> m_ColorRgb;
        private ValueBinding<int> m_StrengthPercent;
        private ValueBinding<int> m_DefaultColorRgb;

        /// <summary>
        /// The colour the game shipped with, snapshotted the first time the singleton is
        /// read and before anything is written to it. Restoring this rather than a
        /// hard-coded constant means a game patch — or another mod — changing the stock
        /// colour does not turn "reset" into "set it to whatever it used to be".
        /// </summary>
        private Color m_VanillaColor;
        private bool m_HasVanillaColor;

        /// <summary>What was last written, so <see cref="Apply"/> can tell drift from a no-op.</summary>
        private Color m_AppliedColor;

        protected override void OnCreate()
        {
            base.OnCreate();

            // ReadWrite, because SetSingleton on a read-only query throws. Otherwise
            // this is the query BatchDataSystem builds: the entity carrying
            // RenderingSettingsData is an ordinary entity, not something that needs
            // prefab-inclusion options to reach.
            m_RenderingSettingsQuery = GetEntityQuery(ComponentType.ReadWrite<RenderingSettingsData>());

            int savedColor = Mod.Settings != null
                ? Mod.Settings.ColorRgb & 0xFFFFFF
                : Settings.DefaultColorRgb;
            int savedStrength = Mod.Settings != null
                ? Mathf.Clamp(Mod.Settings.StrengthPercent, 0, 100)
                : Settings.DefaultStrengthPercent;

            // C# -> React.
            AddBinding(m_Enabled = new ValueBinding<bool>(Group, "Enabled", false));
            AddBinding(m_ColorRgb = new ValueBinding<int>(Group, "ColorRgb", savedColor));
            AddBinding(m_StrengthPercent =
                new ValueBinding<int>(Group, "StrengthPercent", savedStrength));

            // Seeded from the constant and replaced by the real snapshot the first time
            // the singleton is read, so the palette's stock marker is honest even before
            // a save is loaded.
            AddBinding(m_DefaultColorRgb =
                new ValueBinding<int>(Group, "DefaultColorRgb", Settings.DefaultColorRgb));

            // React -> C#.
            AddBinding(new TriggerBinding(Group, "Toggle", Toggle));
            AddBinding(new TriggerBinding<int>(Group, "SetColor", SetColor));
            AddBinding(new TriggerBinding<int>(Group, "SetStrength", SetStrength));
            AddBinding(new TriggerBinding(Group, "Commit", Commit));
            AddBinding(new TriggerBinding(Group, "RestoreDefault", RestoreDefault));

            m_AppliedColor = ToColor(savedColor, savedStrength, Settings.DefaultAlpha);

            if (Mod.Settings != null)
            {
                Mod.Settings.restoreDefaultRequested += RestoreDefault;
            }
        }

        /// <summary>
        /// Opens and closes the panel. Nothing else moves with it: the colour is applied
        /// whether the panel is open or not, which is what makes a chosen colour outlast
        /// closing the panel.
        /// </summary>
        private void Toggle()
        {
            m_Enabled.Update(!m_Enabled.value);
        }

        /// <summary>Picking a swatch. One discrete act, so it is worth writing to disk.</summary>
        private void SetColor(int rgb)
        {
            Store(rgb & 0xFFFFFF, m_StrengthPercent.value, save: true);
        }

        /// <summary>
        /// Dragging the strength slider. Deliberately does <em>not</em> save: this fires
        /// on every mouse move of the drag, and writing the settings file per frame would
        /// put disk I/O in the middle of an interaction that has to stay smooth. The
        /// panel calls <see cref="Commit"/> when the drag ends.
        /// </summary>
        private void SetStrength(int percent)
        {
            Store(m_ColorRgb.value, Mathf.Clamp(percent, 0, 100), save: false);
        }

        /// <summary>Writes whatever is currently applied to the settings file. See <see cref="SetStrength"/>.</summary>
        private void Commit()
        {
            if (Mod.Settings == null)
            {
                return;
            }

            Mod.Settings.ColorRgb = m_ColorRgb.value;
            Mod.Settings.StrengthPercent = m_StrengthPercent.value;
            Mod.Settings.ApplyAndSave();
        }

        /// <summary>
        /// Back to the game's own highlight: the snapshotted colour at full strength.
        /// Strength is a multiplier on that colour, so 100 is not "maximum", it is
        /// "exactly as picked" — which is what makes the stock swatch plus 100% identical
        /// to vanilla.
        /// </summary>
        private void RestoreDefault()
        {
            Store(m_DefaultColorRgb.value, Settings.DefaultStrengthPercent, save: true);
        }

        /// <summary>
        /// The single write path: the bindings, the applied colour and (when asked) the
        /// settings file move together, so what the panel shows, what the game draws and
        /// what gets saved cannot drift apart.
        /// </summary>
        private void Store(int rgb, int strengthPercent, bool save)
        {
            m_ColorRgb.Update(rgb);
            m_StrengthPercent.Update(strengthPercent);

            m_AppliedColor = ToColor(rgb, strengthPercent, VanillaAlpha);

            Apply();

            if (save)
            {
                Commit();
            }
        }

        /// <summary>
        /// Re-asserts the colour whenever the singleton has drifted away from it.
        /// <para>
        /// The drift is not hypothetical: <c>RenderingSettingsPrefab.Initialize</c>
        /// writes the stock colour back whenever prefabs are initialised, which happens
        /// on every world load, and the value lives on a runtime entity rather than in
        /// the save. Comparing rather than writing unconditionally keeps this to one
        /// singleton read per frame in the steady state.
        /// </para>
        /// </summary>
        protected override void OnUpdate()
        {
            base.OnUpdate();

            Apply();
        }

        private void Apply()
        {
            if (!m_RenderingSettingsQuery.TryGetSingleton(out RenderingSettingsData data))
            {
                // No world loaded yet, so there is nothing to write to. OnUpdate applies
                // it as soon as there is.
                return;
            }

            if (!m_HasVanillaColor)
            {
                // Must happen before the first write, or the snapshot is our own colour.
                m_VanillaColor = data.m_HoveredColor;
                m_HasVanillaColor = true;

                m_DefaultColorRgb.Update(ToRgb(m_VanillaColor));

                // Recomputed now that the real vanilla alpha is known — the value built
                // in OnCreate could only use the constant.
                m_AppliedColor = ToColor(m_ColorRgb.value, m_StrengthPercent.value, VanillaAlpha);

                // One dump of what the outline shader exposes, now that the render
                // pipeline is certainly up. See OutlineDiagnostics for why.
                OutlineDiagnostics.LogOnce();
            }

            // Color's == is an epsilon compare, which is what is wanted here: the
            // question is "has something else written this", not bit equality.
            if (data.m_HoveredColor != m_AppliedColor)
            {
                data.m_HoveredColor = m_AppliedColor;
                m_RenderingSettingsQuery.SetSingleton(data);
            }
        }

        /// <summary>The stock alpha, once known; the constant until then.</summary>
        private float VanillaAlpha =>
            m_HasVanillaColor ? m_VanillaColor.a : Settings.DefaultAlpha;

        protected override void OnDestroy()
        {
            if (Mod.Settings != null)
            {
                Mod.Settings.restoreDefaultRequested -= RestoreDefault;
            }

            // Never leave the override behind when the mod unloads. The snapshot is the
            // vanilla value, so this is a restore rather than a guess.
            if (m_HasVanillaColor &&
                m_RenderingSettingsQuery.TryGetSingleton(out RenderingSettingsData data))
            {
                data.m_HoveredColor = m_VanillaColor;
                m_RenderingSettingsQuery.SetSingleton(data);
            }

            base.OnDestroy();
        }

        /// <summary>
        /// Packed 0xRRGGBB plus a strength percentage, to the gamma-space colour the game
        /// stores.
        /// <para>
        /// <b>Strength scales RGB, not alpha.</b> The first cut of this mod exposed alpha
        /// as an opacity slider and it had no visible effect: the outline shader is
        /// handed the colour's alpha but does not appear to use it as a blend factor —
        /// most likely it serves as the silhouette mask the edge detection runs on, which
        /// is why every stock colour sits at 0.1 and still draws at full brightness. So
        /// the lever that does move is the colour, and scaling it toward black fades the
        /// outline under either reading of the shader.
        /// </para>
        /// <para>
        /// Alpha is passed straight through at its stock value, except at strength 0,
        /// where it is zeroed as well so the highlight is off under every reading rather
        /// than merely black.
        /// </para>
        /// <para>
        /// <c>BatchDataJob</c> calls <c>.linear</c> on the result itself, so the
        /// gamma-to-linear conversion must not be done a second time here.
        /// </para>
        /// </summary>
        private static Color ToColor(int rgb, int strengthPercent, float alpha)
        {
            float strength = Mathf.Clamp(strengthPercent, 0, 100) / 100f;

            return new Color(
                ((rgb >> 16) & 0xFF) / 255f * strength,
                ((rgb >> 8) & 0xFF) / 255f * strength,
                (rgb & 0xFF) / 255f * strength,
                strengthPercent == 0 ? 0f : alpha);
        }

        private static int ToRgb(Color color)
        {
            return (Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255) << 16)
                | (Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255) << 8)
                | Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
        }
    }
}
