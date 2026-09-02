using System;
using System.Collections.Generic;
using System.Text;
using Game.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace DimThatHighlight
{
    /// <summary>
    /// Logs, once per session, what the game's outline pass actually exposes.
    /// <para>
    /// This exists because the outline's appearance is decided by a compiled shader
    /// that ships inside the game's asset bundles — the C# side hands it a colour and
    /// a texture and nothing more, so reading <c>Game.dll</c> can prove where the
    /// colour comes from but not what the shader does with it. In particular it
    /// cannot say whether the colour's alpha is a blend factor, a silhouette mask, or
    /// ignored, which is exactly the question an opacity control turns on.
    /// </para>
    /// <para>
    /// The material's shader does carry its property table at runtime, so one dump of
    /// it settles what knobs exist. This is a research aid, not a feature: it runs
    /// once, logs at info level, and swallows everything — a diagnostic that can break
    /// the mod it is diagnosing is worse than no diagnostic.
    /// </para>
    /// </summary>
    internal static class OutlineDiagnostics
    {
        private static bool s_Logged;

        public static void LogOnce()
        {
            if (s_Logged)
            {
                return;
            }

            s_Logged = true;

            try
            {
                Mod.Log.Info(Describe());
            }
            catch (Exception e)
            {
                Mod.Log.Info($"[outline diagnostics] failed: {e.Message}");
            }
        }

        private static string Describe()
        {
            var report = new StringBuilder();
            report.AppendLine("[outline diagnostics] OutlinesWorldUIPass:");

            OutlinesWorldUIPass pass = FindPass();

            if (pass == null)
            {
                report.Append("  pass not found — no CustomPassVolume carries one at this point.");
                return report.ToString();
            }

            report.AppendLine($"  m_MaxDistance = {pass.m_MaxDistance}");
            report.AppendLine($"  m_OutlineLayer = {pass.m_OutlineLayer.value}");

            Material material = pass.m_FullscreenOutline;

            if (material == null)
            {
                report.Append("  m_FullscreenOutline is null.");
                return report.ToString();
            }

            Shader shader = material.shader;
            report.AppendLine($"  material '{material.name}' shader '{(shader == null ? "<null>" : shader.name)}'");
            report.AppendLine($"  renderQueue = {material.renderQueue}, passCount = {material.passCount}");
            report.AppendLine($"  keywords = {string.Join(", ", material.shaderKeywords)}");

            if (shader == null)
            {
                return report.ToString();
            }

            int count = shader.GetPropertyCount();
            report.AppendLine($"  {count} shader properties:");

            for (int i = 0; i < count; i++)
            {
                string name = shader.GetPropertyName(i);
                ShaderPropertyType type = shader.GetPropertyType(i);

                report.Append($"    {name} : {type}");

                switch (type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        report.Append($" = {material.GetFloat(name)}");
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        report.Append($" = {material.GetVector(name)}");
                        break;
                    case ShaderPropertyType.Int:
                        report.Append($" = {material.GetInt(name)}");
                        break;
                    case ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(name);
                        report.Append($" = {(texture == null ? "<null>" : texture.name)}");
                        break;
                }

                report.AppendLine();
            }

            return report.ToString();
        }

        /// <summary>
        /// The first loaded outline pass, or null. Delegates the sweep to
        /// <see cref="OutlineCompose.FindPasses"/> so there is one place that knows how to
        /// reach these.
        /// </summary>
        private static OutlinesWorldUIPass FindPass()
        {
            List<OutlinesWorldUIPass> passes = OutlineCompose.FindPasses();

            return passes.Count > 0 ? passes[0] : null;
        }
    }
}
