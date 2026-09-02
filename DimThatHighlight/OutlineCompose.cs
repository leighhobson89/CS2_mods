using System.Collections.Generic;
using Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace DimThatHighlight
{
    /// <summary>
    /// Reaching the material that composites the hover outline, and overriding the one
    /// property on it worth exposing.
    /// <para>
    /// The outline is drawn in two stages: <c>OutlinesWorldUIPass.DrawOutlineMeshes</c>
    /// fills the highlighted silhouettes into an offscreen buffer, then a fullscreen
    /// quad using <c>m_FullscreenOutline</c> edge-detects over that buffer. The colour
    /// comes from the first stage (see <see cref="DimThatHighlightUISystem"/>); the
    /// <b>thickness</b> comes from the second, as <c>_OutlineWidth</c> on the compose
    /// material's <c>BH/Selection/OutlinesCompose</c> shader.
    /// </para>
    /// </summary>
    internal static class OutlineCompose
    {
        /// <summary>
        /// <c>_OutlineWidth</c> on <c>BH/Selection/OutlinesCompose</c>. A plain float,
        /// shader default 5, in the edge detection's own units — read out of the shader's
        /// serialised property table rather than guessed, but the runtime value is
        /// snapshotted anyway in case the material overrides it.
        /// </summary>
        public static readonly int WidthProperty = Shader.PropertyToID("_OutlineWidth");

        /// <summary>
        /// Every loaded <see cref="OutlinesWorldUIPass"/>. There is no public accessor,
        /// so this sweeps the loaded objects the same way BulldozerMarquee reaches
        /// <c>UICursorCollection</c>. It is not cheap — callers cache the result and only
        /// re-scan when what they cached has been destroyed.
        /// </summary>
        public static List<OutlinesWorldUIPass> FindPasses()
        {
            var found = new List<OutlinesWorldUIPass>();

            foreach (CustomPassVolume volume in Resources.FindObjectsOfTypeAll<CustomPassVolume>())
            {
                if (volume == null || volume.customPasses == null)
                {
                    continue;
                }

                foreach (CustomPass pass in volume.customPasses)
                {
                    if (pass is OutlinesWorldUIPass outlines)
                    {
                        found.Add(outlines);
                    }
                }
            }

            return found;
        }
    }

    /// <summary>
    /// Holds the player's outline thickness against the compose material, snapshotting
    /// the game's own value first and putting it back when the mod unloads.
    /// <para>
    /// Same shape as the colour override in <see cref="DimThatHighlightUISystem"/> —
    /// snapshot before the first write, compare before rewriting, restore on destroy —
    /// but against a <see cref="Material"/> rather than an ECS singleton, which brings
    /// one extra concern: the material can be destroyed and rebuilt under us when the
    /// render pipeline reloads, and a stale reference to a destroyed Unity object is not
    /// null in the ordinary sense. <see cref="Apply"/> handles that by re-scanning when
    /// its cached materials go away.
    /// </para>
    /// </summary>
    internal sealed class OutlineWidthOverride
    {
        /// <summary>
        /// Frames to wait between sweeps while no material has been found. The sweep walks
        /// every loaded object, so it must not run per frame — but it does have to run
        /// again, because the pass does not exist yet in the main menu.
        /// </summary>
        private const int RescanInterval = 120;

        private readonly List<Material> m_Materials = new List<Material>();

        private float m_VanillaWidth;
        private bool m_HasVanillaWidth;
        private int m_RescanCountdown;

        /// <summary>The width the game itself draws with, once it has been read. Zero until then.</summary>
        public float vanillaWidth => m_VanillaWidth;

        public bool hasVanillaWidth => m_HasVanillaWidth;

        /// <summary>
        /// Writes <paramref name="width"/> to every compose material that does not already
        /// have it. Cheap to call every frame: in the steady state it is one
        /// <c>GetFloat</c> per material.
        /// </summary>
        public void Apply(float width)
        {
            if (!Acquire())
            {
                return;
            }

            foreach (Material material in m_Materials)
            {
                if (material == null || !material.HasProperty(OutlineCompose.WidthProperty))
                {
                    continue;
                }

                // Compared with a tolerance rather than exactly: the question is "has
                // something else written this", and an exact float compare on a value that
                // has been through a multiply would rewrite every frame.
                if (Mathf.Abs(material.GetFloat(OutlineCompose.WidthProperty) - width) > 0.0001f)
                {
                    material.SetFloat(OutlineCompose.WidthProperty, width);
                }
            }
        }

        /// <summary>Puts the game's own width back. Safe to call when nothing was ever applied.</summary>
        public void Restore()
        {
            if (!m_HasVanillaWidth)
            {
                return;
            }

            foreach (Material material in m_Materials)
            {
                if (material != null && material.HasProperty(OutlineCompose.WidthProperty))
                {
                    material.SetFloat(OutlineCompose.WidthProperty, m_VanillaWidth);
                }
            }
        }

        /// <summary>
        /// Makes sure <see cref="m_Materials"/> holds live materials, re-scanning at most
        /// once every <see cref="RescanInterval"/> calls. Returns false while there is
        /// nothing to write to.
        /// </summary>
        private bool Acquire()
        {
            // Unity overloads == so a destroyed object compares equal to null; that is
            // exactly the case this has to catch, hence the explicit sweep rather than a
            // reference check.
            m_Materials.RemoveAll(material => material == null);

            if (m_Materials.Count > 0)
            {
                return true;
            }

            if (m_RescanCountdown > 0)
            {
                m_RescanCountdown--;
                return false;
            }

            m_RescanCountdown = RescanInterval;

            foreach (OutlinesWorldUIPass pass in OutlineCompose.FindPasses())
            {
                Material material = pass.m_FullscreenOutline;

                if (material != null && !m_Materials.Contains(material))
                {
                    m_Materials.Add(material);
                }
            }

            if (m_Materials.Count == 0)
            {
                return false;
            }

            if (!m_HasVanillaWidth)
            {
                // Snapshot before anything is written, so "restore" is a restore rather
                // than a guess — and so the slider's midpoint means the game's own width
                // rather than the shader's declared default, which the material is free to
                // override.
                Material first = m_Materials[0];

                if (first.HasProperty(OutlineCompose.WidthProperty))
                {
                    m_VanillaWidth = first.GetFloat(OutlineCompose.WidthProperty);
                    m_HasVanillaWidth = true;
                }
            }

            return true;
        }
    }
}
