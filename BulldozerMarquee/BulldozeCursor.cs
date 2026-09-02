using System;
using System.Linq;
using Game;
using UnityEngine;

namespace BulldozerMarquee
{
    /// <summary>
    /// Swaps the mouse cursor to the game's bulldozer while a marquee is being
    /// dragged.
    /// <para>
    /// This has to be done from C#. The cursor over the 3D world is the game's, not
    /// the UI's — a CSS <c>cursor</c> rule would need a <c>pointer-events: auto</c>
    /// element under the pointer, and that element would swallow the very drag it is
    /// meant to decorate.
    /// </para>
    /// <para>
    /// <see cref="UICursorCollection"/> maps a name to a texture and calls
    /// <c>Cursor.SetCursor</c>. The collection is found through
    /// <c>Resources.FindObjectsOfTypeAll</c> because nothing public hands it out, and
    /// the cursor is matched by substring rather than an exact guess since the names
    /// live in a game asset. Everything here degrades to "leave the cursor alone".
    /// </para>
    /// </summary>
    public static class BulldozeCursor
    {
        private const string NameFragment = "bulldoz";

        private static UICursorCollection.NamedCursorInfo s_Cursor;
        private static bool s_Resolved;
        private static bool s_Applied;

        private static void Resolve()
        {
            if (s_Resolved)
            {
                return;
            }

            s_Resolved = true;

            try
            {
                UICursorCollection collection =
                    Resources.FindObjectsOfTypeAll<UICursorCollection>().FirstOrDefault();

                if (collection == null)
                {
                    Mod.Log.Info("No UICursorCollection loaded; the cursor will not change while dragging.");
                    return;
                }

                s_Cursor = (collection.m_NamedCursors ?? new UICursorCollection.NamedCursorInfo[0])
                    .FirstOrDefault(cursor =>
                        cursor != null
                        && cursor.m_Name != null
                        && cursor.m_Name.IndexOf(NameFragment, StringComparison.OrdinalIgnoreCase) >= 0);

                if (s_Cursor == null)
                {
                    Mod.Log.Info($"No cursor name contains '{NameFragment}'; keeping the default cursor.");
                    return;
                }

                Mod.Log.Info($"Using cursor '{s_Cursor.m_Name}' (texture: {s_Cursor.m_Texture != null}).");
            }
            catch (Exception exception)
            {
                Mod.Log.Warn(exception, "Could not resolve the bulldozer cursor.");
            }
        }

        /// <summary>
        /// Sets the bulldozer cursor. Call this every frame of the drag, not once.
        /// <para>
        /// Cohtml reports the hovered element's cursor to the host whenever its hover
        /// state is recalculated, so a single call made on mouse-down can be quietly
        /// overwritten a frame later. There is no "hold this cursor" seam to claim;
        /// re-asserting each frame is what makes it stick, and it is only a cheap
        /// native call for the length of a drag.
        /// </para>
        /// <para>
        /// Note this calls <c>CursorInfo.Apply</c> directly rather than going through
        /// <c>UICursorCollection.SetCursor(string)</c>. That method keys its lookup on
        /// <c>"cursor://" + name</c> — the form cohtml sends from CSS — so passing a
        /// bare name misses the dictionary, and its miss branch calls
        /// <c>ResetCursor()</c>. Per frame, that actively pins the cursor to the
        /// default instead of changing it.
        /// </para>
        /// </summary>
        public static void Apply()
        {
            Resolve();

            if (s_Cursor == null)
            {
                return;
            }

            s_Cursor.Apply();
            s_Applied = true;
        }

        /// <summary>Safe to call unconditionally; does nothing unless a cursor was set.</summary>
        public static void Reset()
        {
            if (!s_Applied)
            {
                return;
            }

            UICursorCollection.ResetCursor();
            s_Applied = false;
        }
    }
}
