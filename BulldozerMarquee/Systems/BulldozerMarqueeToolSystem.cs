using System;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BulldozerMarquee
{
    /// <summary>
    /// The marquee tool itself: drag a box over the world, keep whatever falls
    /// inside it that the filter checkboxes allow, then delete the survivors on
    /// demand.
    /// <para>
    /// This is a real <see cref="ToolBaseSystem"/> rather than a mouse handler in the
    /// React layer so it inherits the game's own tool semantics for free — the apply
    /// and cancel actions are the ones the player has bound, activating it cancels
    /// whatever tool was running, and the raycast comes from the same system every
    /// vanilla tool uses.
    /// </para>
    /// </summary>
    public partial class BulldozerMarqueeToolSystem : ToolBaseSystem
    {
        /// <summary>Identifies the tool to <see cref="ToolSystem"/> and to the UI's tool bindings.</summary>
        public const string ToolID = "BulldozerMarqueeTool";

        /// <summary>Marquee outline — the game's UI blue, drawn as a plain line with no fill.</summary>
        private static readonly Color kMarqueeColor = new Color(0.31f, 0.66f, 0.86f, 1f);

        /// <summary>Ring around each selected item. Hollow: a filled disc buries what it is marking.</summary>
        private static readonly Color kMarkerColor = new Color(0.30f, 0.85f, 0.42f, 1f);

        private static readonly Color kTransparent = new Color(0f, 0f, 0f, 0f);

        /// <summary>Line and ring thickness, in metres — thin enough to read as a hairline on screen.</summary>
        private const float LineWidth = 1.5f;
        private const float MarkerDiameter = 4f;
        private const float MarkerThickness = 0.5f;

        private OverlayRenderSystem m_OverlayRenderSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private ToolOutputBarrier m_ToolOutputBarrier;

        private EntityQuery m_TreeQuery;
        private EntityQuery m_PropQuery;
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_NodeQuery;
        private EntityQuery m_SegmentQuery;
        private EntityQuery m_NetLaneQuery;
        private EntityQuery m_SurfaceQuery;

        private NativeList<Entity> m_Selection;

        /// <summary>
        /// Kept alongside <see cref="m_Selection"/> so the overlay can mark selected
        /// items without re-reading a component per entity per frame — and so there
        /// is still visible feedback for entity types the Highlighted component does
        /// not render for.
        /// </summary>
        private NativeList<float3> m_SelectionPositions;

        private bool m_Dragging;
        private float3 m_DragStart;
        private MarqueeArea m_Area;

        /// <summary>
        /// Set by the panel's Bulldoze button, acted on in <see cref="OnUpdate"/>.
        /// See <see cref="BulldozeSelection"/> for why the work cannot happen where
        /// the button is clicked.
        /// </summary>
        private bool m_BulldozePending;

        /// <summary>
        /// Whether the current selection has had the Highlighted tag applied. False
        /// while a drag is still in progress, when the selection is only a preview
        /// drawn by the overlay.
        /// </summary>
        private bool m_SelectionHighlighted;

        /// <summary>Which categories a drag may pick up. Owned by the UI system.</summary>
        public AssetFilter filters { get; set; } = AssetFilter.All;

        /// <summary>How the player draws a selection. Owned by the UI system.</summary>
        public SelectionMode mode { get; set; } = SelectionMode.Marquee;

        public int selectionCount => m_Selection.IsCreated ? m_Selection.Length : 0;

        /// <summary>Raised whenever the selection grows, shrinks or is emptied.</summary>
        public event Action selectionChanged;

        public override string toolID => ToolID;

        // The marquee never places anything, so there is no prefab to hand the
        // toolbar — but both members are abstract on the base class.
        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_OverlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_ToolOutputBarrier = World.GetOrCreateSystemManaged<ToolOutputBarrier>();

            m_Selection = new NativeList<Entity>(64, Allocator.Persistent);
            m_SelectionPositions = new NativeList<float3>(64, Allocator.Persistent);

            // Every query excludes Deleted (already on its way out) and Temp (a
            // tool preview, not a real placement). Most also exclude Owner, which
            // marks sub-elements — the props inside a park, the sub-nets under a
            // building — so a marquee deletes whole objects rather than quietly
            // gutting something the player did not target.
            m_TreeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Tree>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = OwnedTransient(),
            });

            // "Prop" is defined by exclusion: a static object that is not one of the
            // categories with a checkbox of its own.
            m_PropQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Static>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Tree>(),
                    ComponentType.ReadOnly<Game.Objects.Plant>(),
                    ComponentType.ReadOnly<Game.Net.Node>(),
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    // Markers and placeholders are invisible - spawn points and the
                    // like. Sweeping them up in a box the player drew over scenery
                    // would break the city with nothing on screen to explain why.
                    ComponentType.ReadOnly<Game.Objects.Marker>(),
                    ComponentType.ReadOnly<Game.Objects.Placeholder>(),
                },
            });

            m_BuildingQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = OwnedTransient(),
            });

            m_NodeQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>() },
                None = OwnedTransient(),
            });

            m_SegmentQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Edge>(),
                    ComponentType.ReadOnly<Game.Net.Curve>(),
                },
                None = OwnedTransient(),
            });

            // Standalone marks a lane that exists in its own right — fences, hedges,
            // street markings — rather than one generated as part of a road. Only
            // those can be deleted on their own.
            m_NetLaneQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Net.Lane>(),
                    ComponentType.ReadOnly<Game.Net.Curve>(),
                    ComponentType.ReadOnly<Game.Net.Standalone>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            m_SurfaceQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<Game.Areas.Node>(),
                },
                None = OwnedTransient(),
            });
        }

        private static ComponentType[] OwnedTransient() => new[]
        {
            ComponentType.ReadOnly<Deleted>(),
            ComponentType.ReadOnly<Temp>(),
            ComponentType.ReadOnly<Owner>(),
        };

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();

            // The drag corners are ground positions, so the terrain is the only
            // thing worth hitting — anything else would snap the box to whatever
            // building happened to be under the cursor.
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            applyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            m_Dragging = false;
        }

        protected override void OnStopRunning()
        {
            // Leaving highlights behind on a tool the player has already left would
            // look like a rendering bug, so drop the selection on the way out. The
            // cursor matters just as much: leaving a bulldozer pointer on the default
            // tool would be a mess.
            m_Dragging = false;
            BulldozeCursor.Reset();
            ClearSelection();

            base.OnStopRunning();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Nothing here creates tool definitions, so tell the tool system there
            // is no pending preview to apply.
            applyMode = ApplyMode.Clear;

            // First thing in the frame's tool phase: the barrier window is open here
            // and the modification systems have not run yet.
            if (m_BulldozePending)
            {
                ApplyPendingBulldoze();
            }

            // Freeform is a placeholder: the mode bar selects it and persists it, but
            // nothing draws or selects yet. Bail before any input handling so it
            // cannot half-work.
            if (mode != SelectionMode.Marquee)
            {
                if (m_Dragging)
                {
                    m_Dragging = false;
                    BulldozeCursor.Reset();
                }

                if (cancelAction.WasPressedThisFrame() && m_Selection.Length == 0)
                {
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                }

                return inputDeps;
            }

            bool hasGround = GetRaycastResult(out Entity _, out RaycastHit hit);

            if (cancelAction.WasPressedThisFrame())
            {
                // Right click backs out one step: drop a selection if there is one,
                // otherwise hand the game back its default tool.
                if (m_Dragging)
                {
                    m_Dragging = false;
                    BulldozeCursor.Reset();
                }
                else if (m_Selection.Length > 0)
                {
                    ClearSelection();
                }
                else
                {
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                    return inputDeps;
                }
            }

            if (applyAction.WasPressedThisFrame() && hasGround)
            {
                // Starting a new box drops whatever the last one caught, highlights
                // and all, so the preview below is the only thing on screen.
                ClearSelection();

                m_Dragging = true;
                m_DragStart = hit.m_HitPosition;
                m_Area = MarqueeArea.FromDrag(m_DragStart, m_DragStart, GetCameraYaw());
            }
            else if (m_Dragging && hasGround)
            {
                MarqueeArea area = MarqueeArea.FromDrag(m_DragStart, hit.m_HitPosition, GetCameraYaw());

                // Rescanning every query is the expensive part of this system, so it
                // only happens when the box has actually changed shape — holding the
                // mouse still costs nothing.
                if (!area.Equals(m_Area))
                {
                    m_Area = area;
                    RebuildSelection();
                }
            }

            if (m_Dragging && applyAction.WasReleasedThisFrame())
            {
                m_Dragging = false;
                BulldozeCursor.Reset();

                // The preview is already the right set; releasing only commits it by
                // making the highlight real.
                RebuildSelection();
                SetHighlighted(true);
                m_SelectionHighlighted = true;
            }

            // Re-asserted every frame rather than once on mouse-down: the UI layer
            // reports its own cursor whenever hover state is recalculated, which
            // silently overwrites a one-shot call. Only for the duration of the drag,
            // so the normal cursor is back the moment the button is released.
            if (m_Dragging)
            {
                BulldozeCursor.Apply();
            }

            DrawOverlays();

            return inputDeps;
        }

        /// <summary>
        /// Camera yaw in radians, used to align the marquee with the screen. Falls
        /// back to the main camera because the active viewer is null for a frame or
        /// two around loading screens.
        /// </summary>
        private float GetCameraYaw()
        {
            Camera camera = m_CameraUpdateSystem != null ? m_CameraUpdateSystem.activeCamera : null;
            camera = camera != null ? camera : Camera.main;

            return camera != null ? camera.transform.eulerAngles.y * Mathf.Deg2Rad : 0f;
        }

        /// <summary>
        /// Recollects everything the current box catches.
        /// <para>
        /// This runs on every frame of the drag, not just on release, so the ring
        /// markers track the box live and the player can see what they are about to
        /// take before committing. It deliberately touches no components: adding and
        /// removing Highlighted across thousands of entities every frame would be a
        /// structural change — and a sync point — per frame. The overlay alone
        /// carries the preview, and the real highlight is applied once on release.
        /// </para>
        /// </summary>
        private void RebuildSelection()
        {
            m_Selection.Clear();
            m_SelectionPositions.Clear();

            if (m_Area.isValid)
            {
                if ((filters & AssetFilter.Trees) != 0) CollectObjects(m_TreeQuery);
                if ((filters & AssetFilter.Props) != 0) CollectObjects(m_PropQuery);
                if ((filters & AssetFilter.Buildings) != 0) CollectObjects(m_BuildingQuery);
                if ((filters & AssetFilter.Nodes) != 0) CollectNodes();
                if ((filters & AssetFilter.Segments) != 0) CollectCurves(m_SegmentQuery);
                if ((filters & AssetFilter.NetLanes) != 0) CollectCurves(m_NetLaneQuery);
                if ((filters & AssetFilter.Surfaces) != 0) CollectSurfaces();
            }

            selectionChanged?.Invoke();
        }

        /// <summary>Objects carry their own position, so the test is the transform.</summary>
        private void CollectObjects(EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            NativeArray<Game.Objects.Transform> transforms =
                query.ToComponentDataArray<Game.Objects.Transform>(Allocator.TempJob);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Add(entities[i], transforms[i].m_Position);
                }
            }
            finally
            {
                entities.Dispose();
                transforms.Dispose();
            }
        }

        private void CollectNodes()
        {
            if (m_NodeQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> entities = m_NodeQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<Game.Net.Node> nodes =
                m_NodeQuery.ToComponentDataArray<Game.Net.Node>(Allocator.TempJob);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Add(entities[i], nodes[i].m_Position);
                }
            }
            finally
            {
                entities.Dispose();
                nodes.Dispose();
            }
        }

        /// <summary>
        /// Segments and standalone lanes are curves, so they are judged by their
        /// midpoint: enclosing the middle of a road is a far more predictable rule
        /// than "any part overlaps", which would drag in long roads clipped by a
        /// corner of the box.
        /// </summary>
        private void CollectCurves(EntityQuery query)
        {
            if (query.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> entities = query.ToEntityArray(Allocator.TempJob);
            NativeArray<Game.Net.Curve> curves =
                query.ToComponentDataArray<Game.Net.Curve>(Allocator.TempJob);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Add(entities[i], MathUtils.Position(curves[i].m_Bezier, 0.5f));
                }
            }
            finally
            {
                entities.Dispose();
                curves.Dispose();
            }
        }

        /// <summary>
        /// Surfaces are polygons rather than points, so they are judged by the
        /// centroid of their nodes — the player has to enclose the middle of a
        /// surface to delete it.
        /// </summary>
        private void CollectSurfaces()
        {
            if (m_SurfaceQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            NativeArray<Entity> entities = m_SurfaceQuery.ToEntityArray(Allocator.TempJob);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    DynamicBuffer<Game.Areas.Node> nodes =
                        EntityManager.GetBuffer<Game.Areas.Node>(entities[i], isReadOnly: true);

                    if (nodes.Length == 0)
                    {
                        continue;
                    }

                    float3 centroid = float3.zero;
                    for (int n = 0; n < nodes.Length; n++)
                    {
                        centroid += nodes[n].m_Position;
                    }

                    Add(entities[i], centroid / nodes.Length);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private void Add(Entity entity, float3 position)
        {
            if (!m_Area.Contains(position))
            {
                return;
            }

            m_Selection.Add(entity);
            m_SelectionPositions.Add(position);
        }

        /// <summary>
        /// The still-live members of the selection, as a batch for EntityManager.
        /// <para>
        /// Everything structural in this system goes through <see cref="EntityManager"/>
        /// rather than ToolOutputBarrier's command buffer. The barrier is a
        /// <c>SafeCommandBufferSystem</c>: it flips an internal flag the moment it
        /// plays back for the frame, and throws "Trying to create EntityCommandBuffer
        /// when it's not allowed!" at anything that asks afterwards. Every caller here
        /// is on the wrong side of that line — OnStopRunning fires during the tool
        /// phase's system transition, and the panel's triggers fire in UIUpdate — so
        /// there is no command buffer to be had. These are one-shot main-thread
        /// operations driven by a user action rather than per-frame job output, so a
        /// direct structural change is the right tool regardless.
        /// </para>
        /// </summary>
        private NativeArray<Entity> GetLiveSelection()
        {
            NativeList<Entity> live = new NativeList<Entity>(m_Selection.Length, Allocator.Temp);

            try
            {
                for (int i = 0; i < m_Selection.Length; i++)
                {
                    // The simulation may have removed something since it was selected.
                    if (EntityManager.Exists(m_Selection[i]))
                    {
                        live.Add(m_Selection[i]);
                    }
                }

                return live.ToArray(Allocator.Temp);
            }
            finally
            {
                live.Dispose();
            }
        }

        /// <summary>
        /// Adds or removes the Highlighted tag. BatchesUpdated has to go on in both
        /// directions — without it the component changes but the render batch is
        /// never rebuilt, so the highlight simply never appears or never clears.
        /// </summary>
        private void SetHighlighted(bool highlighted)
        {
            if (m_Selection.Length == 0)
            {
                return;
            }

            NativeArray<Entity> live = GetLiveSelection();

            try
            {
                if (live.Length == 0)
                {
                    return;
                }

                if (highlighted)
                {
                    EntityManager.AddComponent<Highlighted>(live);
                }
                else
                {
                    EntityManager.RemoveComponent<Highlighted>(live);
                }

                EntityManager.AddComponent<BatchesUpdated>(live);
            }
            finally
            {
                live.Dispose();
            }
        }

        public void ClearSelection()
        {
            if (m_Selection.Length == 0)
            {
                return;
            }

            // A preview mid-drag was never highlighted, so there is nothing to undo
            // and no reason to pay for a structural change.
            if (m_SelectionHighlighted)
            {
                SetHighlighted(false);
                m_SelectionHighlighted = false;
            }

            m_Selection.Clear();
            m_SelectionPositions.Clear();
            selectionChanged?.Invoke();
        }

        /// <summary>
        /// Requests a bulldoze. Deliberately does not do the work.
        /// <para>
        /// The panel's triggers fire in UIUpdate, which is far too late in the frame:
        /// ModificationSystem has already run, so the systems that react to a Deleted
        /// tag by unpicking sub-objects, lanes and references never see it. The entity
        /// still gets destroyed at the end of the frame by CleanUpSystem, but with none
        /// of the cascade — which looks exactly like nothing happening, because the
        /// render batch is never rebuilt and the mesh stays on screen.
        /// </para>
        /// <para>
        /// So the click only raises a flag, and <see cref="OnUpdate"/> does the work one
        /// frame later during ToolUpdate — after AllowBarrier has reopened
        /// ToolOutputBarrier, and before ModificationSystem runs.
        /// </para>
        /// </summary>
        public void BulldozeSelection()
        {
            m_BulldozePending = m_Selection.Length > 0;
        }

        /// <summary>
        /// Tags the selection Deleted through ToolOutputBarrier. That is the game's own
        /// removal path: PrepareCleanUpSystem collects the tag and CleanUpSystem
        /// destroys the entity, with the modification systems in between doing the
        /// cascade through sub-objects and references.
        /// </summary>
        private void ApplyPendingBulldoze()
        {
            m_BulldozePending = false;

            if (m_Selection.Length == 0)
            {
                return;
            }

            NativeArray<Entity> live = GetLiveSelection();

            try
            {
                if (live.Length > 0)
                {
                    EntityCommandBuffer buffer = m_ToolOutputBarrier.CreateCommandBuffer();

                    for (int i = 0; i < live.Length; i++)
                    {
                        buffer.AddComponent<Deleted>(live[i]);
                        buffer.AddComponent<BatchesUpdated>(live[i]);
                    }
                }
            }
            finally
            {
                live.Dispose();
            }

            // No need to un-highlight: the entities are on their way out.
            m_SelectionHighlighted = false;
            m_Selection.Clear();
            m_SelectionPositions.Clear();
            selectionChanged?.Invoke();
        }

        private void DrawOverlays()
        {
            bool drawMarquee = m_Dragging && m_Area.isValid;

            if (!drawMarquee && m_SelectionPositions.Length == 0)
            {
                return;
            }

            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle dependencies);

            // The overlay lists are written by the game's own jobs; writing into
            // them from the main thread without completing first is a data race.
            dependencies.Complete();

            if (drawMarquee)
            {
                // Four plain lines, no fill inside the box: the marquee should frame
                // what is underneath it rather than tint it.
                for (int i = 0; i < 4; i++)
                {
                    Line3.Segment edge = new Line3.Segment(m_Area.GetCorner(i), m_Area.GetCorner(i + 1));

                    buffer.DrawLine(
                        kMarqueeColor,
                        kMarqueeColor,
                        0f,
                        OverlayRenderSystem.StyleFlags.Projected,
                        edge,
                        LineWidth,
                        default);
                }
            }

            // Hollow rings — the fill colour is fully transparent, so the marker
            // outlines each selected item instead of covering it.
            for (int i = 0; i < m_SelectionPositions.Length; i++)
            {
                buffer.DrawCircle(
                    kMarkerColor,
                    kTransparent,
                    MarkerThickness,
                    OverlayRenderSystem.StyleFlags.Projected,
                    new float2(0f, 1f),
                    m_SelectionPositions[i],
                    MarkerDiameter);
            }
        }

        protected override void OnDestroy()
        {
            if (m_Selection.IsCreated)
            {
                m_Selection.Dispose();
            }

            if (m_SelectionPositions.IsCreated)
            {
                m_SelectionPositions.Dispose();
            }

            base.OnDestroy();
        }
    }
}
