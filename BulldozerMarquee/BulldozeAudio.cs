using System;
using System.IO;
using Game.Audio;
using UnityEngine;
using UnityEngine.Networking;

namespace BulldozerMarquee
{
    /// <summary>
    /// Loads the bulldoze sound effect and plays it through the game's own audio
    /// system.
    /// <para>
    /// The clip goes to <c>AudioManager.PlayUISound</c> rather than a private
    /// AudioSource so it lands on the game's UI mixer — it then obeys the player's
    /// audio settings, ducks with everything else, and stops when the game is
    /// paused or unfocused. Decoding is done once at load by
    /// <see cref="UnityWebRequestMultimedia"/>, which is the only route to an
    /// <see cref="AudioClip"/> from an mp3 on disk at runtime.
    /// </para>
    /// </summary>
    public static class BulldozeAudio
    {
        private const string Folder = "sfx";
        private const string FileName = "bulldoze.mp3";

        private static AudioClip s_Clip;
        private static bool s_Requested;

        /// <summary>True once the clip has decoded and playback will actually be audible.</summary>
        public static bool isLoaded => s_Clip != null;

        /// <summary>
        /// Kicks off a one-shot async load. Safe to call when the file is missing —
        /// the mod simply runs without sound rather than failing to start.
        /// </summary>
        public static void Load(string modDirectory)
        {
            if (s_Requested || string.IsNullOrEmpty(modDirectory))
            {
                return;
            }

            s_Requested = true;

            // Combined per-segment rather than with an embedded separator: this
            // assembly is also built for mac and linux, where a baked-in '\' or '/'
            // would be wrong on one of them.
            string path = Path.Combine(modDirectory, Folder, FileName);

            if (!File.Exists(path))
            {
                Mod.Log.Info($"No bulldoze sound at '{path}' - continuing without SFX.");
                return;
            }

            // UnityWebRequest needs a URI even for a local file.
            UnityWebRequest request =
                UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.MPEG);

            request.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        s_Clip = DownloadHandlerAudioClip.GetContent(request);
                        Mod.Log.Info($"Loaded bulldoze sound from '{path}'.");
                    }
                    else
                    {
                        Mod.Log.Warn($"Could not decode '{path}': {request.error}");
                    }
                }
                catch (Exception exception)
                {
                    Mod.Log.Warn(exception, "Failed loading the bulldoze sound.");
                }
                finally
                {
                    request.Dispose();
                }
            };
        }

        public static void Play()
        {
            AudioManager audioManager = AudioManager.instance;

            if (s_Clip == null || audioManager == null)
            {
                return;
            }

            audioManager.PlayUISound(s_Clip);
        }
    }
}
