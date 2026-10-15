using System;
using System.Collections.Generic;
using UnityEngine;

public class Maze : MonoBehaviour {
    public enum Side { RIGHT, LEFT, TOP, BOTTOM }
    public enum WallType { Corridor, Chamber, Outer }

    public struct Edge : IEquatable<Edge> {
        public readonly Vector2Int A;
        public readonly Vector2Int B;

        public Edge(Vector2Int a, Vector2Int b) {
            bool aFirst = a.x < b.x || (a.x == b.x && a.y <= b.y);
            A = aFirst ? a : b;
            B = aFirst ? b : a;
        }

        public bool Equals(Edge other) { return A == other.A && B == other.B; }
        public override bool Equals(object obj) { return obj is Edge && Equals((Edge)obj); }
        public override int GetHashCode() {
            unchecked { return (A.GetHashCode() * 397) ^ B.GetHashCode(); }
        }
    }

    public struct WallSegment {
        public readonly Vector2Int A;
        public readonly Vector2Int B;
        public readonly WallType Kind;

        public WallSegment(Vector2Int a, Vector2Int b, WallType kind) {
            A = a;
            B = b;
            Kind = kind;
        }
    }

    private static readonly Vector2Int[] DIRECTIONS = {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    
    private Vector2Int outerOrigin = new Vector2Int(1, 1);
    private Vector2Int outerSize = new Vector2Int(31, 23);
    private Vector2Int innerOrigin = new Vector2Int(10, 8);
    private Vector2Int innerSize = new Vector2Int(13, 9);
    private Side entranceSide = Side.RIGHT;

    private bool generateOnStart = true;
    private bool useFixedSeed = true;
    private int fixedSeed = 12;
    private bool usePresetGoal = false;
    private Vector2Int presetGoal = new Vector2Int(27, 17);
    private float minimumGoalDistance = 11f;

    
    
    private bool generateLabyrinth = true;

    private float baseLoopChance = 0.08f;

    private int openAreaCount = 3;

    private int tightAreaCount = 3;

    private int areaRadius = 4;
    
    private float openAreaLoopChance = 0.35f;

    private float tightAreaLoopChance = 0.01f;
    
    private float tightAreaCloseChance = 0.35f;

    
    private bool buildWallsOnStart = true;
    private bool buildFloorsOnStart = true;
    private GameObject wallPrefab;
    private Material wallMaterial;
    private Material corridorFloorMaterial;
    private Material chamberFloorMaterial; 
    private float cellSize = 1f;
    private float wallHeight = 2f;
    private float wallThickness = 0.12f;

    
    private bool drawGizmos = true;
    private bool showSolution = false;
    private bool showMinimumDistance = true;

    private bool showLabyrinthAreas = false;

    private (int x, int y, int width, int height) outer;
    private (int x, int y, int width, int height) inner;
    private int? seed;
    private System.Random rng;
    private Vector2 center;
    private HashSet<Vector2Int> ring;
    private Vector2Int door_inside;
    private Vector2Int door_outside;
    private HashSet<Edge> passages;
    private Transform wallRoot;
    private Transform floorRoot;
    private List<LabyrinthArea> labyrinthAreas;

    
    private struct LabyrinthArea {
        public readonly Vector2 Center;
        public readonly bool IsOpen;

        public LabyrinthArea(Vector2Int cell, bool isOpen) {
            Center = new Vector2(cell.x + 0.5f, cell.y + 0.5f);
            IsOpen = isOpen;
        }
    }


    public int LoopCount { get { EnsureGenerated(); return passages.Count - ring.Count + 1; } }
    public int LastOpenedPassages { get; private set; }
    public int LastClosedPassages { get; private set; }

    public Vector2 Center { get { EnsureGenerated(); return center; } }
    public Vector2Int DoorInside { get { EnsureGenerated(); return door_inside; } }
    public Vector2Int DoorOutside { get { EnsureGenerated(); return door_outside; } }
    public Vector2Int? CurrentGoal { get; private set; }
    public int? Seed { get { return seed; } }
    public IReadOnlyCollection<Vector2Int> RingCells {
        get { EnsureGenerated(); return ring; }
    }

    private void Start() {
        if (generateOnStart) { GenerateFromInspector(); }
    }

    
    public void GenerateFromInspector() {
        Generate(
            (outerOrigin.x, outerOrigin.y, outerSize.x, outerSize.y),
            (innerOrigin.x, innerOrigin.y, innerSize.x, innerSize.y),
            entranceSide,
            useFixedSeed ? (int?)fixedSeed : null
        );

        ChooseGoal(usePresetGoal ? (Vector2Int?)presetGoal : null, minimumGoalDistance);

        if (Application.isPlaying) {
            if (buildFloorsOnStart) BuildFloors();
            if (buildWallsOnStart) BuildWalls();
        }
    }
    public void Generate(
        (int x, int y, int width, int height) outer,
        (int x, int y, int width, int height) inner,
        Side entrance_side,
        int? seed = null) {
        int ox = outer.x, oy = outer.y, ow = outer.width, oh = outer.height;
        int ix = inner.x, iy = inner.y, iw = inner.width, ih = inner.height;

        if (ox < 0 || oy < 0 || ix < 0 || iy < 0 ||
            ow < 1 || oh < 1 || iw < 1 || ih < 1)
            throw new ArgumentException("Rectangles must have positive dimensions and nonnegative origins.");

        // No overflow for large rectangles.
        if (!(ox < ix && oy < iy &&
              (long)ix + iw < (long)ox + ow &&
              (long)iy + ih < (long)oy + oh))
            throw new ArgumentException("Inner rectangle must be strictly inside the outer rectangle.");

        ClearWalls();
        ClearFloors();
        this.outer = outer;
        this.inner = inner;
        this.seed = seed;
        rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        center = new Vector2(ix + iw / 2f, iy + ih / 2f);
        CurrentGoal = null;

        ring = new HashSet<Vector2Int>();
        for (int x = ox; x < ox + ow; x++) {
            for (int y = oy; y < oy + oh; y++) {
                bool insideChamber = ix <= x && x < ix + iw &&
                                     iy <= y && y < iy + ih;
                if (!insideChamber)
                    ring.Add(new Vector2Int(x, y));
            }
        }

        switch (entrance_side) {
            case Side.RIGHT:
                door_inside = new Vector2Int(ix + iw - 1, iy + ih / 2);
                door_outside = door_inside + Vector2Int.right;
                break;
            case Side.LEFT:
                door_inside = new Vector2Int(ix, iy + ih / 2);
                door_outside = door_inside + Vector2Int.left;
                break;
            case Side.TOP:
                door_inside = new Vector2Int(ix + iw / 2, iy + ih - 1);
                door_outside = door_inside + Vector2Int.up;
                break;
            case Side.BOTTOM:
                door_inside = new Vector2Int(ix + iw / 2, iy);
                door_outside = door_inside + Vector2Int.down;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entrance_side));
        }

        passages = new HashSet<Edge>();
        LastOpenedPassages = 0;
        LastClosedPassages = 0;
        labyrinthAreas = new List<LabyrinthArea>();
        CarveMaze();
        if (generateLabyrinth) PostProcessLabyrinth();
    }

    private void CarveMaze() {
        var stack = new Stack<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        stack.Push(door_outside);
        visited.Add(door_outside);

        while (stack.Count > 0) {
            Vector2Int current = stack.Peek();
            var choices = new List<Vector2Int>(4);

            foreach (Vector2Int direction in DIRECTIONS) {
                Vector2Int next = current + direction;
                if (ring.Contains(next) && !visited.Contains(next))
                    choices.Add(next);
            }

            if (choices.Count == 0) {
                stack.Pop();
                continue;
            }

            Vector2Int chosen = choices[rng.Next(choices.Count)];
            passages.Add(new Edge(current, chosen));
            visited.Add(chosen);
            stack.Push(chosen);
        }

        if (visited.Count != ring.Count) { throw new InvalidOperationException("The ring cells are not fully connected."); }
    }

    private void PostProcessLabyrinth() {
       
        var neighbors = new List<Edge>();
        var cells = new List<Vector2Int>(ring);
        cells.Sort((a, b) => {
            int xOrder = a.x.CompareTo(b.x);
            return xOrder != 0 ? xOrder : a.y.CompareTo(b.y);
        });

        foreach (Vector2Int cell in cells) {
            Vector2Int right = cell + Vector2Int.right;
            Vector2Int up = cell + Vector2Int.up;
            if (ring.Contains(right)) neighbors.Add(new Edge(cell, right));
            if (ring.Contains(up)) neighbors.Add(new Edge(cell, up));
        }

        var areas = labyrinthAreas;
        int numberOfRounds = Mathf.Max(openAreaCount, tightAreaCount);
        for (int i = 0; i < numberOfRounds; i++) {
            if (i < openAreaCount) TryAddLabyrinthArea(cells, areas, true);
            if (i < tightAreaCount) TryAddLabyrinthArea(cells, areas, false);
        }

       
        Shuffle(neighbors);
        foreach (Edge edge in neighbors) {
            if (passages.Contains(edge)) continue;

            int type = GetAreaType(edge, areas);
            float chance = type > 0 ? openAreaLoopChance
                         : type < 0 ? tightAreaLoopChance
                         : baseLoopChance;
            if (rng.NextDouble() < chance) {
                passages.Add(edge);
                LastOpenedPassages++;
            }
        }

     
        int closureBudget = LastOpenedPassages / 2;
        Shuffle(neighbors);
        foreach (Edge edge in neighbors) {
            if (LastClosedPassages >= closureBudget) break;
            if (!passages.Contains(edge) || edge.A == door_outside || edge.B == door_outside)
                continue;
            if (GetAreaType(edge, areas) >= 0 || rng.NextDouble() >= tightAreaCloseChance)
                continue;

            passages.Remove(edge);
            if (HasAlternateRoute(edge.A, edge.B)) {
                LastClosedPassages++;
            } else {
                passages.Add(edge);
            }
        }
    }

   
    private void TryAddLabyrinthArea(List<Vector2Int> cells, List<LabyrinthArea> areas, bool isOpen) {
        if (cells.Count == 0) return;
        float minSpacingSquared = areaRadius * 1.5f * areaRadius * 1.5f;
        float entranceSpacingSquared = areaRadius * areaRadius;

        for (int attempt = 0; attempt < 64; attempt++) {
            Vector2Int candidate = cells[rng.Next(cells.Count)];
            Vector2 deltaFromDoor = (Vector2)(candidate - door_outside);
            if (deltaFromDoor.sqrMagnitude < entranceSpacingSquared) continue;

            Vector2 centerPoint = new Vector2(candidate.x + 0.5f, candidate.y + 0.5f);
            bool separated = true;
            foreach (LabyrinthArea area in areas) {
                if ((centerPoint - area.Center).sqrMagnitude < minSpacingSquared) {
                    separated = false;
                    break;
                }
            }
            if (!separated) continue;

            areas.Add(new LabyrinthArea(candidate, isOpen));
            return;
        }
    }

    private int GetAreaType(Edge edge, List<LabyrinthArea> areas) {
        Vector2 middle = new Vector2(
            (edge.A.x + edge.B.x + 1f) * 0.5f,
            (edge.A.y + edge.B.y + 1f) * 0.5f);
        float radiusSquared = (float)areaRadius * areaRadius;
        float bestDistanceSquared = float.MaxValue;
        int bestType = 0;

        foreach (LabyrinthArea area in areas) {
            float distanceSquared = (middle - area.Center).sqrMagnitude;
            if (distanceSquared <= radiusSquared && distanceSquared < bestDistanceSquared) {
                bestDistanceSquared = distanceSquared;
                bestType = area.IsOpen ? 1 : -1;
            }
        }
        return bestType;
    }

    
    private bool HasAlternateRoute(Vector2Int start, Vector2Int destination) {
        var queue = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0) {
            Vector2Int current = queue.Dequeue();
            if (current == destination) return true;
            foreach (Vector2Int direction in DIRECTIONS) {
                Vector2Int next = current + direction;
                if (ring.Contains(next) && !visited.Contains(next) &&
                    passages.Contains(new Edge(current, next))) {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }
        return false;
    }


    private void Shuffle<T>(List<T> values) {
        for (int i = values.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            T temp = values[i];
            values[i] = values[j];
            values[j] = temp;
        }
    }

    public Vector2Int ChooseGoal(Vector2Int? goal = null, float minDistance = 10f) {
        EnsureGenerated();
        if (goal.HasValue) {
            if (!ring.Contains(goal.Value)) { throw new ArgumentException("Preset goal must be a cell inside the maze ring.", nameof(goal)); }
            CurrentGoal = goal.Value;
            return goal.Value;
        }

        if (float.IsNaN(minDistance) || float.IsInfinity(minDistance) || minDistance < 0f) { throw new ArgumentOutOfRangeException(nameof(minDistance), "Distance must be finite and nonnegative."); }

        var candidates = new List<Vector2Int>();
        float farthest = 0f;

        foreach (Vector2Int cell in ring) {
            float distance = Vector2.Distance(new Vector2(cell.x + 0.5f, cell.y + 0.5f), center);
            farthest = Mathf.Max(farthest, distance);
            if (distance >= minDistance) { candidates.Add(cell); }
        }

        if (candidates.Count == 0)
            throw new ArgumentException(
                $"No corridor cells are at least {minDistance:0.##} grid units away; " +
                $"maximum possible distance is {farthest:0.##}.", nameof(minDistance));
        candidates.Sort((a, b) => {
            int compareX = a.x.CompareTo(b.x);
            return compareX != 0 ? compareX : a.y.CompareTo(b.y);
        });

        Vector2Int selected = candidates[rng.Next(candidates.Count)];
        CurrentGoal = selected;
        return selected;
    }
    public List<Vector2Int> PathTo(Vector2Int goal) {
        EnsureGenerated();
        if (!ring.Contains(goal))
            throw new ArgumentException("Goal is not in the maze ring.", nameof(goal));

        var queue = new Queue<Vector2Int>();
        var previous = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(door_outside);
        previous.Add(door_outside, door_outside);

        while (queue.Count > 0) {
            Vector2Int current = queue.Dequeue();
            if (current == goal) break;

            foreach (Vector2Int direction in DIRECTIONS) {
                Vector2Int next = current + direction;
                if (ring.Contains(next) && !previous.ContainsKey(next) &&
                    passages.Contains(new Edge(current, next))) {
                    previous.Add(next, current);
                    queue.Enqueue(next);
                }
            }
        }

        if (!previous.ContainsKey(goal))
            throw new InvalidOperationException("No corridor path to the requested goal.");

        var path = new List<Vector2Int>();
        Vector2Int step = goal;
        while (step != door_outside) {
            path.Add(step);
            step = previous[step];
        }
        path.Add(door_outside);
        path.Reverse();
        return path;
    }

    public bool IsRingCell(Vector2Int cell) {
        EnsureGenerated();
        return ring.Contains(cell);
    }

    public bool HasPassage(Vector2Int a, Vector2Int b) {
        EnsureGenerated();
        return passages.Contains(new Edge(a, b));
    }

    public IEnumerable<WallSegment> GetWallSegments() {
        EnsureGenerated();

        foreach (Vector2Int cell in ring) {
            int x = cell.x, y = cell.y;
            Vector2Int right = cell + Vector2Int.right;
            Vector2Int above = cell + Vector2Int.up;

            if (ring.Contains(right) && !HasPassage(cell, right))
                yield return new WallSegment(
                    new Vector2Int(x + 1, y), new Vector2Int(x + 1, y + 1), WallType.Corridor);

            if (ring.Contains(above) && !HasPassage(cell, above))
                yield return new WallSegment(
                    new Vector2Int(x, y + 1), new Vector2Int(x + 1, y + 1), WallType.Corridor);
        }

        for (int x = inner.x; x < inner.x + inner.width; x++) {
            for (int y = inner.y; y < inner.y + inner.height; y++) {
                Vector2Int cell = new Vector2Int(x, y);
                foreach (Vector2Int direction in DIRECTIONS) {
                    Vector2Int neighbor = cell + direction;
                    if (!ring.Contains(neighbor) ||
                        (cell == door_inside && neighbor == door_outside))
                        continue;

                    Vector2Int a, b;
                    if (direction == Vector2Int.left) {
                        a = new Vector2Int(x, y);
                        b = new Vector2Int(x, y + 1);
                    }
                    else if (direction == Vector2Int.right) {
                        a = new Vector2Int(x + 1, y);
                        b = new Vector2Int(x + 1, y + 1);
                    }
                    else if (direction == Vector2Int.down) {
                        a = new Vector2Int(x, y);
                        b = new Vector2Int(x + 1, y);
                    }
                    else
                    {
                        a = new Vector2Int(x, y + 1);
                        b = new Vector2Int(x + 1, y + 1);
                    }
                    yield return new WallSegment(a, b, WallType.Chamber);
                }
            }
        }

        Vector2Int bl = new Vector2Int(outer.x, outer.y);
        Vector2Int br = new Vector2Int(outer.x + outer.width, outer.y);
        Vector2Int tr = new Vector2Int(outer.x + outer.width, outer.y + outer.height);
        Vector2Int tl = new Vector2Int(outer.x, outer.y + outer.height);
        yield return new WallSegment(bl, br, WallType.Outer);
        yield return new WallSegment(br, tr, WallType.Outer);
        yield return new WallSegment(tr, tl, WallType.Outer);
        yield return new WallSegment(tl, bl, WallType.Outer);
    }

   
    public void BuildWalls() {
        EnsureGenerated();
        ClearWalls();

        var rootObject = new GameObject("Generated Maze Walls");
        wallRoot = rootObject.transform;
        wallRoot.SetParent(transform, false);

        foreach (WallSegment segment in GetWallSegments()) {
            GameObject wall = wallPrefab != null
                ? Instantiate(wallPrefab, wallRoot)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = segment.Kind + " Wall";
            wall.transform.SetParent(wallRoot, false);

            Vector2 midpoint = ((Vector2)segment.A + (Vector2)segment.B) * 0.5f;
            float gridLength = Vector2Int.Distance(segment.A, segment.B);
            bool alongX = segment.A.y == segment.B.y;

            wall.transform.localPosition = new Vector3(
                midpoint.x * cellSize, wallHeight * 0.5f, midpoint.y * cellSize);
            wall.transform.localRotation = alongX
                ? Quaternion.identity
                : Quaternion.Euler(0f, -90f, 0f);
            wall.transform.localScale = new Vector3(
                gridLength * cellSize, wallHeight, wallThickness);

            if (wallMaterial != null) {
                Renderer renderer = wall.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = wallMaterial;
            }
        }
    }

    public void BuildFloors() {
        EnsureGenerated();
        ClearFloors();

        var rootObject = new GameObject("Generated Maze Floors");
        floorRoot = rootObject.transform;
        floorRoot.SetParent(transform, false);
        float planeScale = cellSize / 10f;

        for (int x = outer.x; x < outer.x + outer.width; x++) {
            for (int y = outer.y; y < outer.y + outer.height; y++) {
                bool isChamber =
                    x >= inner.x && x < inner.x + inner.width &&
                    y >= inner.y && y < inner.y + inner.height;

                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = (isChamber ? "Chamber" : "Corridor") +
                             $" Floor ({x}, {y})";
                floor.transform.SetParent(floorRoot, false);
                floor.transform.localPosition = new Vector3(
                    (x + 0.5f) * cellSize, 0f, (y + 0.5f) * cellSize);
                floor.transform.localRotation = Quaternion.identity;
                floor.transform.localScale = new Vector3(planeScale, 1f, planeScale);

                Material material = isChamber && chamberFloorMaterial != null
                    ? chamberFloorMaterial
                    : corridorFloorMaterial;
                if (material != null) {
                    Renderer renderer = floor.GetComponent<Renderer>();
                    if (renderer != null) renderer.sharedMaterial = material;
                }
            }
        }
    }

    public void ClearFloors() {
        if (floorRoot == null) return;
        if (Application.isPlaying) Destroy(floorRoot.gameObject);
        else DestroyImmediate(floorRoot.gameObject);
        floorRoot = null;
    }

    public void ClearWalls() {
        if (wallRoot == null) return;
        if (Application.isPlaying) Destroy(wallRoot.gameObject);
        else DestroyImmediate(wallRoot.gameObject);
        wallRoot = null;
    }

    private void EnsureGenerated() {
        if (ring == null || passages == null)
            throw new InvalidOperationException("Call Generate() before using the maze.");
    }

    private Vector3 GridToWorld(Vector2 grid, float height = 0f) {
        return transform.TransformPoint(new Vector3(grid.x * cellSize, height, grid.y * cellSize));
    }

    private static Vector2 CellCenter(Vector2Int cell) {
        return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
    }
    // For debugging
    // private void OnDrawGizmos() {
    //     if (!drawGizmos || ring == null || passages == null) return;

    //     // Ground-level cell colors roughly parallel the matplotlib drawing.
    //     Gizmos.color = new Color(0.87f, 0.93f, 0.97f, 0.28f);
    //     foreach (Vector2Int cell in ring)
    //         Gizmos.DrawCube(GridToWorld(CellCenter(cell)),
    //             new Vector3(cellSize * 0.97f, 0.015f, cellSize * 0.97f));

    //     Gizmos.color = new Color(0.52f, 0.82f, 0.55f, 0.28f);
    //     Vector2 chamberMid = new Vector2(
    //         inner.x + inner.width / 2f, inner.y + inner.height / 2f);
    //     Gizmos.DrawCube(GridToWorld(chamberMid),
    //         new Vector3(inner.width * cellSize, 0.018f, inner.height * cellSize));

    //     if (showLabyrinthAreas && labyrinthAreas != null) {
    //         const int circleSteps = 48;
    //         foreach (LabyrinthArea area in labyrinthAreas) {
    //             Gizmos.color = area.IsOpen
    //                 ? new Color(0.08f, 0.75f, 0.88f)
    //                 : new Color(1f, 0.57f, 0.16f);
    //             for (int step = 0; step < circleSteps; step++) {
    //                 float a = 2f * Mathf.PI * step / circleSteps;
    //                 float b = 2f * Mathf.PI * (step + 1) / circleSteps;
    //                 Vector2 first = area.Center + areaRadius * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    //                 Vector2 second = area.Center + areaRadius * new Vector2(Mathf.Cos(b), Mathf.Sin(b));
    //                 Gizmos.DrawLine(GridToWorld(first, 0.33f), GridToWorld(second, 0.33f));
    //             }
    //         }
    //     }

    //     foreach (WallSegment wall in GetWallSegments()) {
    //         switch (wall.Kind) {
    //             case WallType.Chamber: Gizmos.color = new Color(0.13f, 0.39f, 0.23f); break;
    //             case WallType.Outer: Gizmos.color = new Color(0.08f, 0.10f, 0.13f); break;
    //             default: Gizmos.color = new Color(0.15f, 0.21f, 0.29f); break;
    //         }
    //         Gizmos.DrawLine(GridToWorld((Vector2)wall.A, 0.15f),
    //                         GridToWorld((Vector2)wall.B, 0.15f));
    //     }

    //     if (showMinimumDistance && !usePresetGoal) {
    //         Gizmos.color = new Color(0.66f, 0.42f, 0.15f);
    //         const int segments = 72;
    //         for (int i = 0; i < segments; i++) {
    //             float angleA = 2f * Mathf.PI * i / segments;
    //             float angleB = 2f * Mathf.PI * (i + 1) / segments;
    //             Vector2 a = center + minimumGoalDistance *
    //                 new Vector2(Mathf.Cos(angleA), Mathf.Sin(angleA));
    //             Vector2 b = center + minimumGoalDistance *
    //                 new Vector2(Mathf.Cos(angleB), Mathf.Sin(angleB));
    //             Gizmos.DrawLine(GridToWorld(a, 0.12f), GridToWorld(b, 0.12f));
    //         }
    //     }

    //     if (showSolution && CurrentGoal.HasValue) {
    //         Gizmos.color = new Color(0.77f, 0.48f, 0.09f);
    //         Vector3 previous = GridToWorld(center, 0.24f);
    //         Vector3 inside = GridToWorld(CellCenter(door_inside), 0.24f);
    //         Gizmos.DrawLine(previous, inside);
    //         previous = inside;
    //         foreach (Vector2Int cell in PathTo(CurrentGoal.Value)) {
    //             Vector3 next = GridToWorld(CellCenter(cell), 0.24f);
    //             Gizmos.DrawLine(previous, next);
    //             previous = next;
    //         }
    //     }

    //     Gizmos.color = new Color(0.14f, 0.53f, 0.29f);
    //     Gizmos.DrawSphere(GridToWorld(center, 0.28f), cellSize * 0.23f);
    //     if (CurrentGoal.HasValue) {
    //         Gizmos.color = new Color(0.84f, 0.23f, 0.23f);
    //         Gizmos.DrawSphere(GridToWorld(CellCenter(CurrentGoal.Value), 0.28f),
    //                           cellSize * 0.23f);
    //     }
    // }
}