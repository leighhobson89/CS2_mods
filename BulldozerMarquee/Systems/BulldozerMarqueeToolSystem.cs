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

        /// <summary>
        /// The lasso's closing chord, drawn a touch thinner than the drawn trail —
        /// roughly a pixel at normal zoom, since these widths are world metres rather
        /// than screen pixels. Together with the dashes it reads as the provisional
        /// edge it is, rather than as part of what the player drew.
        /// </summary>
        private const float ClosingLineWidth = 1f;
        private const float ClosingDashLength = 5f;
        private const float ClosingGapLength = 4f;
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

        /// <summary>
        /// The category each selected entity was collected under, indexed alongside
        /// <see cref="m_Selection"/>.
        /// <para>
        /// Recorded at collection time rather than recomputed on demand because the
        /// category is defined by which query matched, and that knowledge only exists
        /// here. Re-deriving it later would mean a second copy of the component
        /// composition rules in <c>OnCreate</c>, which would drift the first time a
        /// query is amended. Only <see cref="PruneSelection"/> reads it.
        /// </para>
        /// </summary>
        private NativeList<AssetFilter> m_SelectionCategories;

        /// <summary>Metres the cursor must travel before the lasso commits a vertex.</summary>
        private const float MinVertexDistanceSq = 9f;

        /// <summary>Ignore sub-centimetre jitter so a still mouse triggers no rescan.</summary>
        private const float CursorEpsilonSq = 0.01f;

        /// <summary>Hard ceiling on lasso complexity, so one very long drag cannot stall the frame.</summary>
        private const int MaxPathVertices = 512;

        /// <summary>Smallest lasso, in metres, that counts as a deliberate gesture.</summary>
        private const float MinimumExtent = 1f;

        /// <summary>
        /// Hard ceiling on a selection.
        /// <para>
        /// Without one, a box dragged over a dense district selects tens of thousands
        /// of trees, and every downstream cost is linear in that: a structural change
        /// per entity, a delete per entity, and an overlay instance per entity per
        /// frame. Past a certain size that stops being slow and starts being a native
        /// crash, so the tool refuses to build a selection it cannot safely act on.
        /// </para>
        /// </summary>
        private const int MaxSelection = 5000;

        /// <summary>
        /// Ceiling on selection markers drawn per frame.
        /// <para>
        /// Each marker is one instance in OverlayRenderSystem's projected-curve buffer,
        /// which is reallocated and re-uploaded to the GPU whenever the count grows.
        /// Feeding it a five-figure instance count every frame of a drag is what
        /// crashed the game natively, with no managed stack to show for it. A thousand
        /// rings already reads as "lots"; more is noise nobody can count anyway.
        /// </para>
        /// </summary>
        private const int MaxDrawnMarkers = 1000;

        private bool m_Dragging;
        private float3 m_DragStart;
        private MarqueeArea m_Area;

        /// <summary>Committed lasso vertices in world space. The cursor closes the loop.</summary>
        private NativeList<float3> m_Path;

        private float3 m_Cursor;

        /// <summary>Flat (x, z) bounds of the lasso, refreshed with the selection.</summary>
        private float2 m_PathMin;
        private float2 m_PathMax;

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

        /// <summary>True when the region held more than <see cref="MaxSelection"/> items.</summary>
        private bool m_SelectionClamped;

        /// <summary>Whether the last selection hit the safety ceiling. Read by the UI.</summary>
        public bool selectionClamped => m_SelectionClamped;

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
            m_SelectionCategories = new NativeList<AssetFilter>(64, Allocator.Persistent);
            m_Path = new NativeList<float3>(MaxPathVertices, Allocator.Persistent);

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
            m_Path.Clear();
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

            bool hasGround = GetRaycastResult(out Entity _, out RaycastHit hit);

            if (cancelAction.WasPressedThisFrame())
            {
                // Right click backs out one step: drop a selection if there is one,
                // otherwise hand the game back its default tool.
                if (m_Dragging)
                {
                    EndDrag(commit: false);
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
                // Starting a new region drops whatever the last one caught, highlights
                // and all, so the preview is the only thing on screen.
                ClearSelection();
                BeginDrag(hit.m_HitPosition);
            }
            else if (m_Dragging && hasGround)
            {
                UpdateDrag(hit.m_HitPosition);
            }

            if (m_Dragging && applyAction.WasReleasedThisFrame())
            {
                EndDrag(commit: true);
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

        private void BeginDrag(float3 position)
        {
            m_Dragging = true;
            m_DragStart = position;
            m_Cursor = position;

            if (mode == SelectionMode.Marquee)
            {
                m_Area = MarqueeArea.FromDrag(position, position, GetCameraYaw());
            }
            else
            {
                m_Path.Clear();
                m_Path.Add(position);
            }
        }

        /// <summary>
        /// Grows the region towards the cursor.
        /// <para>
        /// Both regions are held in world space, which is what lets the camera pan or
        /// rotate mid-drag without distorting them: the shape stays pinned to the
        /// ground rather than to the screen, and the raycast simply keeps reporting
        /// wherever the cursor now points.
        /// </para>
        /// </summary>
        private void UpdateDrag(float3 position)
        {
            if (mode == SelectionMode.Marquee)
            {
                MarqueeArea area = MarqueeArea.FromDrag(m_DragStart, position, GetCameraYaw());

                // Rescanning every query is the expensive part of this system, so it
                // only happens when the box has actually changed shape — holding the
                // mouse still costs nothing.
                if (!area.Equals(m_Area))
                {
                    m_Area = area;
                    RebuildSelection();
                }

                return;
            }

            if (math.distancesq(position, m_Cursor) < CursorEpsilonSq)
            {
                return;
            }

            m_Cursor = position;

            // The trail is only committed to a vertex once the cursor has travelled
            // far enough. Recording every frame would make the polygon enormous, and
            // the containment test is linear in vertex count for every candidate
            // entity — this is what keeps a lasso affordable.
            if (m_Path.Length > 0
                && m_Path.Length < MaxPathVertices
                && math.distancesq(position, m_Path[m_Path.Length - 1]) >= MinVertexDistanceSq)
            {
                m_Path.Add(position);
            }

            RebuildSelection();
        }

        private void EndDrag(bool commit)
        {
            m_Dragging = false;
            BulldozeCursor.Reset();

            if (commit)
            {
                // The preview is already the right set; releasing only commits it by
                // making the highlight real.
                RebuildSelection();

                Mod.Log.Info(
                    $"Committed {mode} selection: {m_Selection.Length} entities"
                    + $"{(m_SelectionClamped ? $" (clamped at {MaxSelection})" : string.Empty)}"
                    + $", filters={filters}, lassoVertices={m_Path.Length}.");

                SetHighlighted(true);
                m_SelectionHighlighted = true;
            }
            else
            {
                m_Selection.Clear();
                m_SelectionPositions.Clear();
                m_SelectionCategories.Clear();
                selectionChanged?.Invoke();
            }

            // Drops the drawn line: like the marquee, the lasso is a gesture, not a
            // thing that stays on screen once it has been answered.
            m_Path.Clear();
        }

        /// <summary>
        /// The lasso's vertices are the committed trail plus the cursor, which closes
        /// the loop back to the start. Exposing it this way avoids rebuilding a list
        /// every frame just to append one moving point.
        /// </summary>
        private int pathVertexCount => m_Path.Length + 1;

        private float3 GetPathVertex(int index)
        {
            return index < m_Path.Length ? m_Path[index] : m_Cursor;
        }

        /// <summary>True when the current region is big enough to mean anything.</summary>
        private bool HasValidRegion()
        {
            if (mode == SelectionMode.Marquee)
            {
                return m_Area.isValid;
            }

            return m_Path.Length >= 2 && math.all(m_PathMax - m_PathMin >= MinimumExtent);
        }

        private void UpdatePathBounds()
        {
            if (m_Path.Length == 0)
            {
                m_PathMin = m_PathMax = float2.zero;
                return;
            }

            float2 min = new float2(float.MaxValue, float.MaxValue);
            float2 max = new float2(float.MinValue, float.MinValue);

            for (int i = 0; i < pathVertexCount; i++)
            {
                float3 vertex = GetPathVertex(i);
                float2 flat = new float2(vertex.x, vertex.z);

                min = math.min(min, flat);
                max = math.max(max, flat);
            }

            m_PathMin = min;
            m_PathMax = max;
        }

        /// <summary>
        /// Even-odd point-in-polygon on the XZ plane, guarded by the polygon's bounding
        /// box.
        /// <para>
        /// The bounds check is not an optimisation to skip lightly: the ray-crossing
        /// test is linear in vertex count and runs against every entity in every
        /// enabled query, so rejecting the overwhelming majority with two comparisons
        /// is what keeps a per-frame preview viable in a large city.
        /// </para>
        /// </summary>
        private bool PathContains(float3 position)
        {
            if (position.x < m_PathMin.x || position.x > m_PathMax.x
                || position.z < m_PathMin.y || position.z > m_PathMax.y)
            {
                return false;
            }

            int count = pathVertexCount;
            bool inside = false;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float3 a = GetPathVertex(i);
                float3 b = GetPathVertex(j);

                // Zero-length edges (a vertex just committed at the cursor) fail this
                // first test and contribute nothing, which is the correct result.
                if ((a.z > position.z) != (b.z > position.z)
                    && position.x < (b.x - a.x) * (position.z - a.z) / (b.z - a.z) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private bool RegionContains(float3 position)
        {
            return mode == SelectionMode.Marquee
                ? m_Area.Contains(position)
                : PathContains(position);
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
            m_SelectionCategories.Clear();
            m_SelectionClamped = false;
            m_SelectionHighlighted = false;

            // Bounds first: the lasso's containment test consults them per entity.
            if (mode != SelectionMode.Marquee)
            {
                UpdatePathBounds();
            }

            if (HasValidRegion())
            {
                if ((filters & AssetFilter.Trees) != 0) CollectObjects(m_TreeQuery, AssetFilter.Trees);
                if ((filters & AssetFilter.Props) != 0) CollectObjects(m_PropQuery, AssetFilter.Props);
                if ((filters & AssetFilter.Buildings) != 0) CollectObjects(m_BuildingQuery, AssetFilter.Buildings);
                if ((filters & AssetFilter.Nodes) != 0) CollectNodes();
                if ((filters & AssetFilter.Segments) != 0) CollectCurves(m_SegmentQuery, AssetFilter.Segments);
                if ((filters & AssetFilter.NetLanes) != 0) CollectCurves(m_NetLaneQuery, AssetFilter.NetLanes);
                if ((filters & AssetFilter.Surfaces) != 0) CollectSurfaces();
            }

            selectionChanged?.Invoke();
        }

        /// <summary>Objects carry their own position, so the test is the transform.</summary>
        private void CollectObjects(EntityQuery query, AssetFilter category)
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
                    Add(entities[i], transforms[i].m_Position, category);
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
                    Add(entities[i], nodes[i].m_Position, AssetFilter.Nodes);
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
        private void CollectCurves(EntityQuery query, AssetFilter category)
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
                    Add(entities[i], MathUtils.Position(curves[i].m_Bezier, 0.5f), category);
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

                    Add(entities[i], centroid / nodes.Length, AssetFilter.Surfaces);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        private void Add(Entity entity, float3 position, AssetFilter category)
        {
            if (m_Selection.Length >= MaxSelection)
            {
                m_SelectionClamped = true;
                return;
            }

            if (!RegionContains(position))
            {
                return;
            }

            m_Selection.Add(entity);
            m_SelectionPositions.Add(position);
            m_SelectionCategories.Add(category);
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
                ApplyHighlight(live, highlighted);
            }
            finally
            {
                live.Dispose();
            }
        }

        /// <summary>
        /// The batch write itself, split out from <see cref="SetHighlighted"/> so
        /// <see cref="PruneSelection"/> can un-highlight the entities it is dropping
        /// without touching the ones it is keeping.
        /// </summary>
        private void ApplyHighlight(NativeArray<Entity> entities, bool highlighted)
        {
            if (entities.Length == 0)
            {
                return;
            }

            if (highlighted)
            {
                EntityManager.AddComponent<Highlighted>(entities);
            }
            else
            {
                EntityManager.RemoveComponent<Highlighted>(entities);
            }

            EntityManager.AddComponent<BatchesUpdated>(entities);
        }

        /// <summary>
        /// Drops every selected entity whose category appears in
        /// <paramref name="removed"/>, keeping the rest of the selection intact.
        /// <para>
        /// This is what makes unticking a filter act on a selection the player has
        /// already committed, rather than only on the next drag. It is driven from
        /// the UI system, and only when the "Sync" option is on — with the option off
        /// the selection is deliberately left alone, so a mask edit cannot silently
        /// change what the Bulldoze button is about to remove.
        /// </para>
        /// <para>
        /// The compaction is done in place with a read/write cursor rather than by
        /// rebuilding the lists, because the three lists are index-parallel and any
        /// path that rewrites one without the others corrupts the mapping. Nothing
        /// here re-runs the region test: an entity that was in the region a moment
        /// ago still is, and re-collecting would also resurrect anything the
        /// <see cref="MaxSelection"/> clamp had cut.
        /// </para>
        /// </summary>
        public void PruneSelection(AssetFilter removed)
        {
            if (removed == AssetFilter.None || m_Selection.Length == 0)
            {
                return;
            }

            NativeList<Entity> dropped = new NativeList<Entity>(m_Selection.Length, Allocator.Temp);

            try
            {
                int kept = 0;

                for (int i = 0; i < m_Selection.Length; i++)
                {
                    if ((m_SelectionCategories[i] & removed) != 0)
                    {
                        // Only live entities are worth a structural change; the rest
                        // are simply forgotten.
                        if (EntityManager.Exists(m_Selection[i]))
                        {
                            dropped.Add(m_Selection[i]);
                        }

                        continue;
                    }

                    m_Selection[kept] = m_Selection[i];
                    m_SelectionPositions[kept] = m_SelectionPositions[i];
                    m_SelectionCategories[kept] = m_SelectionCategories[i];
                    kept++;
                }

                if (kept == m_Selection.Length)
                {
                    return;
                }

                m_Selection.Length = kept;
                m_SelectionPositions.Length = kept;
                m_SelectionCategories.Length = kept;

                // Mid-drag the selection is a preview that was never tagged, so there
                // is nothing to take off — and the next frame rebuilds it anyway.
                if (m_SelectionHighlighted)
                {
                    ApplyHighlight(dropped.AsArray(), false);
                }

                // Nothing is left to carry the tag, so the flag has to come down with
                // the selection or the next drag starts believing it is highlighted.
                if (kept == 0)
                {
                    m_SelectionHighlighted = false;
                }

                // The clamp warning quotes the selection count as the ceiling that was
                // hit. Once the count has been cut by a filter edit that reading is
                // just wrong, so the warning goes with it.
                m_SelectionClamped = false;

                selectionChanged?.Invoke();
            }
            finally
            {
                dropped.Dispose();
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
            m_SelectionCategories.Clear();
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
            NativeHashSet<Entity> seen = new NativeHashSet<Entity>(live.Length, Allocator.Temp);

            try
            {
                EntityCommandBuffer buffer = m_ToolOutputBarrier.CreateCommandBuffer();
                int tagged = 0;

                for (int i = 0; i < live.Length; i++)
                {
                    Entity entity = live[i];

                    // Tagging the same entity twice, or re-tagging one the game has
                    // already marked, feeds duplicates into CleanUpSystem's
                    // DestroyEntity call. That is a native-level fault with no managed
                    // stack, so it is cheaper to filter here than to debug there.
                    if (!seen.Add(entity) || EntityManager.HasComponent<Deleted>(entity))
                    {
                        continue;
                    }

                    buffer.AddComponent<Deleted>(entity);
                    buffer.AddComponent<BatchesUpdated>(entity);
                    tagged++;
                }

                Mod.Log.Info($"Bulldoze: {tagged} tagged from a selection of {live.Length}.");
            }
            catch (System.Exception exception)
            {
                Mod.Log.Error(exception, "Bulldoze failed.");
            }
            finally
            {
                seen.Dispose();
                live.Dispose();
            }

            // No need to un-highlight: the entities are on their way out.
            m_SelectionHighlighted = false;
            m_Selection.Clear();
            m_SelectionPositions.Clear();
            m_SelectionCategories.Clear();
            selectionChanged?.Invoke();
        }

        private void DrawOverlays()
        {
            bool drawRegion = m_Dragging && HasValidRegion();

            if (!drawRegion && m_SelectionPositions.Length == 0)
            {
                return;
            }

            OverlayRenderSystem.Buffer buffer = m_OverlayRenderSystem.GetBuffer(out JobHandle dependencies);

            // The overlay lists are written by the game's own jobs; writing into
            // them from the main thread without completing first is a data race.
            dependencies.Complete();

            if (drawRegion && mode == SelectionMode.Marquee)
            {
                // Four plain lines, no fill inside the box: the marquee should frame
                // what is underneath it rather than tint it.
                for (int i = 0; i < 4; i++)
                {
                    DrawRegionEdge(buffer, m_Area.GetCorner(i), m_Area.GetCorner(i + 1));
                }
            }
            else if (drawRegion)
            {
                // The trail the cursor has drawn, then the chord from the cursor back
                // to the start — the same loop the containment test uses, so what is
                // highlighted is always exactly what is outlined.
                int count = pathVertexCount;

                for (int i = 0; i < count - 1; i++)
                {
                    DrawRegionEdge(buffer, GetPathVertex(i), GetPathVertex(i + 1));
                }

                // The closing chord is the one edge the player did not draw, so it is
                // dashed and thinner to distinguish it from their own line.
                DrawClosingEdge(buffer, GetPathVertex(count - 1), GetPathVertex(0));
            }

            // Hollow rings — the fill colour is fully transparent, so the marker
            // outlines each selected item instead of covering it.
            int markers = math.min(m_SelectionPositions.Length, MaxDrawnMarkers);

            for (int i = 0; i < markers; i++)
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

        private static void DrawRegionEdge(OverlayRenderSystem.Buffer buffer, float3 from, float3 to)
        {
            buffer.DrawLine(
                kMarqueeColor,
                kMarqueeColor,
                0f,
                OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(from, to),
                LineWidth,
                default);
        }

        private static void DrawClosingEdge(OverlayRenderSystem.Buffer buffer, float3 from, float3 to)
        {
            buffer.DrawDashedLine(
                kMarqueeColor,
                kMarqueeColor,
                0f,
                OverlayRenderSystem.StyleFlags.Projected,
                new Line3.Segment(from, to),
                ClosingLineWidth,
                ClosingDashLength,
                ClosingGapLength);
        }

        protected override void OnDestroy()
        {
            if (m_Path.IsCreated)
            {
                m_Path.Dispose();
            }

            if (m_Selection.IsCreated)
            {
                m_Selection.Dispose();
            }

            if (m_SelectionPositions.IsCreated)
            {
                m_SelectionPositions.Dispose();
            }

            if (m_SelectionCategories.IsCreated)
            {
                m_SelectionCategories.Dispose();
            }

            base.OnDestroy();
        }
    }
}
