using System;
using Unity.Mathematics;

namespace BulldozerMarquee
{
    /// <summary>
    /// The drag rectangle, held in world space on the XZ plane and rotated to the
    /// camera's yaw.
    /// <para>
    /// Testing in world space rather than projecting every candidate entity back to
    /// screen space is how Move It builds its marquee, and it buys two things: the
    /// containment test is a pair of dot products with no camera matrix involved,
    /// and the outline can be handed to the overlay renderer as ordinary world-space
    /// lines so it drapes over terrain. Aligning to camera yaw is what keeps it
    /// looking like the axis-aligned box the player dragged on screen.
    /// </para>
    /// </summary>
    public struct MarqueeArea : IEquatable<MarqueeArea>
    {
        /// <summary>Ignore drags shorter than this (metres) so a stray click selects nothing.</summary>
        private const float MinimumExtent = 1f;

        /// <summary>World position where the drag started; the (0,0) of the local frame.</summary>
        public float3 m_Origin;

        /// <summary>Unit vector along camera-right, flat on the XZ plane.</summary>
        public float3 m_Right;

        /// <summary>Unit vector along camera-forward, flat on the XZ plane.</summary>
        public float3 m_Forward;

        /// <summary>Rectangle bounds in (right, forward) local coordinates.</summary>
        public float2 m_Min;
        public float2 m_Max;

        public bool isValid => math.all(m_Max - m_Min >= MinimumExtent);

        public static MarqueeArea FromDrag(float3 start, float3 end, float cameraYawRadians)
        {
            // Flattened camera basis. At yaw 0 this is forward +Z / right +X.
            float3 forward = new float3(math.sin(cameraYawRadians), 0f, math.cos(cameraYawRadians));
            float3 right = new float3(forward.z, 0f, -forward.x);

            float3 delta = end - start;
            float2 corner = new float2(math.dot(delta, right), math.dot(delta, forward));

            return new MarqueeArea
            {
                m_Origin = start,
                m_Right = right,
                m_Forward = forward,
                // The drag can run in any direction, so normalise to min/max rather
                // than assuming the start corner is the top-left one.
                m_Min = math.min(float2.zero, corner),
                m_Max = math.max(float2.zero, corner),
            };
        }

        /// <summary>
        /// Flat XZ containment — height is deliberately ignored so a marquee dragged
        /// across a hillside still catches everything under it.
        /// </summary>
        public bool Contains(float3 position)
        {
            float3 delta = position - m_Origin;
            float2 local = new float2(math.dot(delta, m_Right), math.dot(delta, m_Forward));

            return math.all(local >= m_Min) && math.all(local <= m_Max);
        }

        /// <summary>
        /// Lets the tool skip a full rescan of every entity query on frames where the
        /// box has not actually moved.
        /// </summary>
        public bool Equals(MarqueeArea other)
        {
            return m_Origin.Equals(other.m_Origin)
                && m_Right.Equals(other.m_Right)
                && m_Forward.Equals(other.m_Forward)
                && m_Min.Equals(other.m_Min)
                && m_Max.Equals(other.m_Max);
        }

        public override bool Equals(object obj) => obj is MarqueeArea other && Equals(other);

        public override int GetHashCode()
        {
            return ((((17 * 31 + m_Origin.GetHashCode()) * 31 + m_Right.GetHashCode()) * 31
                + m_Forward.GetHashCode()) * 31 + m_Min.GetHashCode()) * 31 + m_Max.GetHashCode();
        }

        /// <summary>Corner <paramref name="index"/> (0-3) in winding order, for drawing the outline.</summary>
        public float3 GetCorner(int index)
        {
            float2 local;
            switch (index & 3)
            {
                case 0: local = m_Min; break;
                case 1: local = new float2(m_Max.x, m_Min.y); break;
                case 2: local = m_Max; break;
                default: local = new float2(m_Min.x, m_Max.y); break;
            }

            return m_Origin + m_Right * local.x + m_Forward * local.y;
        }
    }
}
