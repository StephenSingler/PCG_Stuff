using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ModularTileBSPDungeon : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int mapWidth = 40;
    public int mapHeight = 40;
    public int minPartitionSize = 6;
    public int maxRoomSize = 10;

    [Header("Random Seed")]
    [Tooltip("If false, dungeon generation will be deterministic using the seed below.")]
    public bool useRandomSeed = true;
    public int seed = 0;

    [Header("Prefabs")]
    public GameObject roomTilePrefab; // 1x1 tile with Floor + 4 Walls
    public GameObject doorPrefab; // door to place at corridor endpoints
    public GameObject hiddenWallPrefab;

    [Header("Gizmo Settings")]
    public float levelHeight = 1.0f;
    public bool showBSPNodes = false;
    public bool showRooms = false;
    public bool showLabels = false;

    [Header("Room Labeling")]
    public bool labelRoomsFromGrammar = true;
    public GameObject minibossRoomPrefab; // spawned in 'm' rooms
    public GameObject combatRoomPrefab;   // spawned in 'c' rooms
    public float roomMarkerY = 0.2f;      // height above floor


    [Tooltip("If grammar output is shorter than room count, use this label for remaining rooms.")]
    public char defaultRoomLabel = 'c';

    [Tooltip("If true, sort rooms by distance from entrance so the string reads like a progression.")]
    public bool sortRoomsByDistanceFromEntrance = true;


    [Header("Loot")]
    public GameObject lootPrefab; // collectable cube
    [Tooltip("Average loot per room before size weighting (keep small, like 0.1-0.4).")]
    public float lootBasePerRoom = 0.2f;
    [Tooltip("Extra multiplier applied to the smallest rooms.")]
    public float lootSmallRoomMultiplier = 2.0f;
    [Tooltip("Hard cap so small rooms don't explode with loot.")]
    public int lootMaxPerRoom = 3;
    [Tooltip("Avoid spawning loot right against the walls.")]
    public int lootEdgePadding = 1;
    [Tooltip("If true, loot won't spawn on the same tile as other loot.")]
    public bool preventLootOverlap = true;

    [Header("Spawn / Goal")]
    public Transform playerToMove;

    public GameObject goalPrefab;

    [Tooltip("Keep spawn/goal away from walls like loot.")]
    public int spawnGoalEdgePadding = 1;

    [Tooltip("If true, goal room is chosen as the farthest room from the spawn room.")]
    public bool placeGoalFarFromSpawn = true;

    [Tooltip("If true, spawn/goal can't overlap loot.")]
    public bool preventSpawnGoalOnLoot = true;

    [Header("Grammar (WIP)")]
    [Tooltip("Starting symbol to expand, e.g. 'S'")]
    public string grammarStart = "S";

    [Tooltip("How many expansion steps to run at most.")]
    public int grammarMaxSteps = 20;

    [Tooltip("Stop early if the string gets too long.")]
    public int grammarMaxLength = 200;

    [Tooltip("If true, prints each expansion step.")]
    public bool grammarLogSteps = true;



    private int[,] mapGrid; // 0 = empty, 1 = room, 2 = corridor
    private List<RoomTile> placedTiles = new List<RoomTile>();
    private BSPNode rootNode;
    private HashSet<Vector2Int> lootPositions = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, RoomTile> tileLookup = new Dictionary<Vector2Int, RoomTile>();
    private HashSet<(Vector2Int a, Vector2Int b)> spawnedDoors = new HashSet<(Vector2Int, Vector2Int)>();
    private HashSet<(Vector2Int a, Vector2Int b)> spawnedHiddenWalls = new HashSet<(Vector2Int, Vector2Int)>();
    private HashSet<Vector2Int> reservedPositions = new HashSet<Vector2Int>(); // spawn/goal reservations
    private string lastGrammarOutput = "";
    private BSPNode entranceNode;
    private BSPNode bossNode;





    // -----------------------------
    // Unity Start
    // -----------------------------
    void Start()
    {
        // Initialize RNG
        if (useRandomSeed)
        {
            seed = System.Environment.TickCount;
        }
        Random.InitState(seed);

        mapGrid = new int[mapWidth, mapHeight];

        rootNode = new BSPNode(0, 0, mapWidth, mapHeight);

        Split(rootNode);
        CreateRooms(rootNode);
        ConnectRooms(rootNode);
        InstantiateTiles();

        if (labelRoomsFromGrammar)
        {
            LabelRoomsFromGrammarAndChooseEntranceBoss();
        }

        SpawnSpawnAndGoal();  // now uses entranceNode/bossNode if available
        SpawnRoomMarkers();
        SpawnLoot();
        RemoveInteriorWalls();
    }

    // -----------------------------
    // Data Structures
    // -----------------------------
    class BSPNode
    {
        public int x, y, width, height;
        public BSPNode left, right;
        public Room room;
        public string label;
        public GameObject hierarchyParent;

        public char roomLabel = '?';

        public BSPNode(int x, int y, int w, int h)
        {
            this.x = x;
            this.y = y;
            width = w;
            height = h;
        }

        public bool IsLeaf() => left == null && right == null;
    }

    class Room
    {
        public int x, y, width, height;
        public int centerX => x + width / 2;
        public int centerY => y + height / 2;
    }

    enum TileType { Room, Corridor }

    class RoomTile
    {
        public int x, y;
        public GameObject instance;
        public TileType type;
    }

    // -----------------------------
    // BSP Splitting
    // -----------------------------
    void Split(BSPNode node)
    {
        if (node.width <= minPartitionSize * 2 &&
            node.height <= minPartitionSize * 2)
            return;

        bool splitHorizontal = Random.value > 0.5f;

        if ((splitHorizontal && node.height > minPartitionSize * 2) ||
            node.width <= minPartitionSize * 2)
        {
            int split = Random.Range(minPartitionSize, node.height - minPartitionSize);
            node.left = new BSPNode(node.x, node.y, node.width, split);
            node.right = new BSPNode(node.x, node.y + split, node.width, node.height - split);
        }
        else
        {
            int split = Random.Range(minPartitionSize, node.width - minPartitionSize);
            node.left = new BSPNode(node.x, node.y, split, node.height);
            node.right = new BSPNode(node.x + split, node.y, node.width - split, node.height);
        }

        Split(node.left);
        Split(node.right);
    }

    // -----------------------------
    // Room Creation
    // -----------------------------
    void CreateRooms(BSPNode node)
    {
        if (!node.IsLeaf())
        {
            CreateRooms(node.left);
            CreateRooms(node.right);
            return;
        }

        int roomW = Random.Range(3, Mathf.Min(maxRoomSize, node.width));
        int roomH = Random.Range(3, Mathf.Min(maxRoomSize, node.height));
        int roomX = node.x + Random.Range(0, node.width - roomW + 1);
        int roomY = node.y + Random.Range(0, node.height - roomH + 1);

        node.room = new Room
        {
            x = roomX,
            y = roomY,
            width = roomW,
            height = roomH
        };

        for (int x = roomX; x < roomX + roomW; x++)
            for (int y = roomY; y < roomY + roomH; y++)
                mapGrid[x, y] = 1;
    }

    // -----------------------------
    // Corridor Creation
    // -----------------------------
    void ConnectRooms(BSPNode node)
    {
        if (node.left == null || node.right == null) return;

        Room a = GetRoom(node.left);
        Room b = GetRoom(node.right);

        CreateLCorridor(a, b);

        ConnectRooms(node.left);
        ConnectRooms(node.right);
    }

    Room GetRoom(BSPNode node)
    {
        if (node.room != null) return node.room;
        Room left = GetRoom(node.left);
        if (left != null) return left;
        return GetRoom(node.right);
    }

    void CreateLCorridor(Room a, Room b)
    {
        int x1 = a.centerX;
        int y1 = a.centerY;
        int x2 = b.centerX;
        int y2 = b.centerY;

        if (Random.value > 0.5f)
        {
            CreateCorridor(x1, x2, y1, true);
            CreateCorridor(y1, y2, x2, false);
        }
        else
        {
            CreateCorridor(y1, y2, x1, false);
            CreateCorridor(x1, x2, y2, true);
        }
    }

    void CreateCorridor(int start, int end, int fixedCoord, bool horizontal)
    {
        int min = Mathf.Min(start, end);
        int max = Mathf.Max(start, end);

        for (int i = min; i <= max; i++)
        {
            int x = horizontal ? i : fixedCoord;
            int y = horizontal ? fixedCoord : i;

            if (mapGrid[x, y] == 0)
                mapGrid[x, y] = 2;
        }
    }

    // -----------------------------
    // Tile Instantiation + Hierarchy
    // -----------------------------
    void InstantiateTiles()
    {
        int letterIndex = 0;
        BuildHierarchyAndTiles(rootNode, 0, "", ref letterIndex);

        tileLookup.Clear();
        foreach (var t in placedTiles) {
            tileLookup[new Vector2Int(t.x, t.y)] = t;
        }
    }

    void BuildHierarchyAndTiles(BSPNode node, int depth, string parentLabel, ref int letterIndex)
    {
        if (node == null) return;

        string label = depth == 0
            ? "A"
            : parentLabel + (char)('A' + (++letterIndex % 26));

        node.label = label;

        GameObject parentGO = new GameObject("BSP_" + label);
        parentGO.transform.parent = transform;
        node.hierarchyParent = parentGO;

        // Rooms (leaf nodes)
        if (node.IsLeaf() && node.room != null)
        {
            for (int x = node.room.x; x < node.room.x + node.room.width; x++)
            {
                for (int y = node.room.y; y < node.room.y + node.room.height; y++)
                {
                    if (placedTiles.Exists(t => t.x == x && t.y == y)) continue;

                    GameObject tile = Instantiate(roomTilePrefab, new Vector3(x, 0, y), Quaternion.identity);
                    tile.transform.parent = parentGO.transform;

                    placedTiles.Add(new RoomTile { x = x, y = y, instance = tile, type = TileType.Room });
                }
            }
        }

        // Corridors
        for (int x = node.x; x < node.x + node.width; x++)
        {
            for (int y = node.y; y < node.y + node.height; y++)
            {
                if (mapGrid[x, y] == 2 && !placedTiles.Exists(t => t.x == x && t.y == y))
                {
                    GameObject tile = Instantiate(roomTilePrefab, new Vector3(x, 0, y), Quaternion.identity);
                    tile.transform.parent = parentGO.transform;

                    placedTiles.Add(new RoomTile { x = x, y = y, instance = tile, type = TileType.Corridor });
                }
            }
        }

        BuildHierarchyAndTiles(node.left, depth + 1, label, ref letterIndex);
        BuildHierarchyAndTiles(node.right, depth + 1, label, ref letterIndex);
    }

    // -----------------------------
    // Wall Removal (Rooms + Corridors + Doors + Hidden Walls)
    // -----------------------------
    void RemoveInteriorWalls()
    {
        foreach (RoomTile tile in placedTiles)
        {
            TryHandleEdge(tile, 0, 1, "WallNorth");  // +Y
            TryHandleEdge(tile, 0, -1, "WallSouth"); // -Y
            TryHandleEdge(tile, 1, 0, "WallEast");   // +X
            TryHandleEdge(tile, -1, 0, "WallWest");  // -X
        }
    }

    void TryHandleEdge(RoomTile from, int dx, int dy, string wallName)
    {
        Vector2Int a = new Vector2Int(from.x, from.y);
        Vector2Int b = new Vector2Int(from.x + dx, from.y + dy);

        // No neighbor tile -> keep outer wall
        if (!tileLookup.TryGetValue(b, out RoomTile to))
            return;

        Transform wall = from.instance.transform.Find(wallName);

        // Same type adjacency: remove interior walls like before
        if (from.type == to.type)
        {
            if (wall != null) Destroy(wall.gameObject);
            return;
        }

        // Room <-> Corridor adjacency: only open if corridor endpoint => spawn door
        // Ensure we evaluate endpoint on the corridor tile specifically
        RoomTile corridorTile = (from.type == TileType.Corridor) ? from : to;
        RoomTile roomTile = (from.type == TileType.Room) ? from : to;

        // 1) Door case: corridor endpoint (normal)
        if (IsCorridorEndpoint(corridorTile))
        {
            if (wall != null) Destroy(wall.gameObject);
            SpawnDoorBetween(roomTile, corridorTile);
            return;
        }

        // 2) Hidden-wall case: single-tile corridor connecting two rooms straight-through
        if (IsSingleTileConnector(corridorTile, out var roomDirs))
        {
            // Only spawn hidden walls on the two valid sides (the two opposite room dirs)
            Vector2Int dirFromCorridorToRoom = new Vector2Int(roomTile.x - corridorTile.x, roomTile.y - corridorTile.y);

            if (roomDirs.Contains(dirFromCorridorToRoom))
            {
                if (wall != null) Destroy(wall.gameObject);
                SpawnHiddenWallBetween(roomTile, corridorTile);
            }
        }

        // else: do nothing (keeps wall between room and corridor if it happens elsewhere)
    }

    bool IsCorridorEndpoint(RoomTile corridor)
    {
        // Count corridor neighbors (4-dir)
        int corridorNeighbors = 0;
        corridorNeighbors += IsCorridorAt(corridor.x + 1, corridor.y) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x - 1, corridor.y) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x, corridor.y + 1) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x, corridor.y - 1) ? 1 : 0;

        // Endpoint for an L/line corridor will have exactly 1 corridor neighbor
        if (corridorNeighbors != 1) return false;

        // Also require it touches at least one room tile
        bool touchesRoom =
            IsRoomAt(corridor.x + 1, corridor.y) ||
            IsRoomAt(corridor.x - 1, corridor.y) ||
            IsRoomAt(corridor.x, corridor.y + 1) ||
            IsRoomAt(corridor.x, corridor.y - 1);

        return touchesRoom;
    }

    bool IsCorridorAt(int x, int y)
    {
        if (!tileLookup.TryGetValue(new Vector2Int(x, y), out var t)) return false;
        return t.type == TileType.Corridor;
    }

    bool IsRoomAt(int x, int y)
    {
        if (!tileLookup.TryGetValue(new Vector2Int(x, y), out var t)) return false;
        return t.type == TileType.Room;
    }

    void SpawnDoorBetween(RoomTile roomTile, RoomTile corridorTile)
    {
        if (doorPrefab == null) return;

        Vector2Int r = new Vector2Int(roomTile.x, roomTile.y);
        Vector2Int c = new Vector2Int(corridorTile.x, corridorTile.y);

        // Prevent duplicates: store an undirected pair
        var key = (a: (r.x < c.x || (r.x == c.x && r.y <= c.y)) ? r : c,
                b: (r.x < c.x || (r.x == c.x && r.y <= c.y)) ? c : r);

        if (spawnedDoors.Contains(key)) return;
        spawnedDoors.Add(key);

        Vector2Int dir = r - c; // direction from corridor -> room (one of the four dirs)

        // Place at the boundary between tile centers
        Vector3 pos = new Vector3(
            c.x + dir.x * 0.5f,
            0f,
            c.y + dir.y * 0.5f
        );

        // Rotate so the door aligns with the wall plane.
        // Assumption: door's "forward" faces along +Z when placed on a north/south edge.
        Quaternion rot = (dir.x != 0) ? Quaternion.Euler(0, 90f, 0) : Quaternion.identity;

        GameObject door = Instantiate(doorPrefab, pos, rot);

        // Optional: parent it nicely (use the corridor tile's parent if you want)
        door.transform.parent = transform;
    }

    bool IsSingleTileConnector(RoomTile corridor, out List<Vector2Int> roomDirs)
    {
        roomDirs = new List<Vector2Int>();

        // Must be a corridor tile
        if (corridor.type != TileType.Corridor) return false;

        // Must have 0 corridor neighbors (single tile corridor segment)
        int corridorNeighbors = 0;
        corridorNeighbors += IsCorridorAt(corridor.x + 1, corridor.y) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x - 1, corridor.y) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x, corridor.y + 1) ? 1 : 0;
        corridorNeighbors += IsCorridorAt(corridor.x, corridor.y - 1) ? 1 : 0;

        if (corridorNeighbors != 0) return false;

        // Collect room-adjacent directions
        if (IsRoomAt(corridor.x + 1, corridor.y)) roomDirs.Add(new Vector2Int(1, 0));
        if (IsRoomAt(corridor.x - 1, corridor.y)) roomDirs.Add(new Vector2Int(-1, 0));
        if (IsRoomAt(corridor.x, corridor.y + 1)) roomDirs.Add(new Vector2Int(0, 1));
        if (IsRoomAt(corridor.x, corridor.y - 1)) roomDirs.Add(new Vector2Int(0, -1));

        // Exactly two room neighbors
        if (roomDirs.Count != 2) return false;

        // They must be opposite (straight-through)
        // i.e. sum to (0,0)
        Vector2Int sum = roomDirs[0] + roomDirs[1];
        if (sum != Vector2Int.zero) return false;

        return true;
    }

    void SpawnHiddenWallBetween(RoomTile roomTile, RoomTile corridorTile)
    {
        if (hiddenWallPrefab == null) return;

        Vector2Int r = new Vector2Int(roomTile.x, roomTile.y);
        Vector2Int c = new Vector2Int(corridorTile.x, corridorTile.y);

        // Undirected pair key to prevent duplicates (same idea as doors)
        var key = (a: (r.x < c.x || (r.x == c.x && r.y <= c.y)) ? r : c,
                b: (r.x < c.x || (r.x == c.x && r.y <= c.y)) ? c : r);

        if (spawnedHiddenWalls.Contains(key)) return;
        spawnedHiddenWalls.Add(key);

        Vector2Int dir = r - c; // corridor -> room (one step)

        // Place centered on the boundary between tiles
        Vector3 pos = new Vector3(
            c.x + dir.x * 0.5f,
            0f,
            c.y + dir.y * 0.5f
        );

        // Rotate like doors: if connection is east/west, rotate 90
        Quaternion rot = (dir.x != 0) ? Quaternion.Euler(0, 90f, 0) : Quaternion.identity;

        GameObject hw = Instantiate(hiddenWallPrefab, pos, rot);
        hw.transform.parent = transform;
    }



    bool HasTile(int x, int y)
    {
        return placedTiles.Exists(t => t.x == x && t.y == y);
    }

    // -----------------------------
    // Gizmos
    // -----------------------------
    void OnDrawGizmos()
    {
        if (rootNode == null) return;
        int index = 0;
        DrawBSPNode(rootNode, 0, "", ref index);
    }

    void DrawBSPNode(BSPNode node, int depth, string parentLabel, ref int index)
    {
        if (node == null) return;

#if UNITY_EDITOR
        Vector3 pos = new Vector3(
            node.x + node.width / 2f,
            depth * levelHeight,
            node.y + node.height / 2f
        );

        if (showBSPNodes)
        {
            Gizmos.color = Color.HSVToRGB(depth * 0.15f % 1f, 0.5f, 0.7f);
            Gizmos.DrawWireCube(pos, new Vector3(node.width, 0.1f, node.height));
        }

        if (node.room != null && showRooms)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawCube(
                new Vector3(node.room.x + node.room.width / 2f, pos.y, node.room.y + node.room.height / 2f),
                new Vector3(node.room.width, 0.1f, node.room.height)
            );
        }

        if (showLabels)
        {
            Handles.Label(pos + Vector3.up * 0.2f, node.label);
        }
#endif

        DrawBSPNode(node.left, depth + 1, node.label, ref index);
        DrawBSPNode(node.right, depth + 1, node.label, ref index);
    }

    // ---------------------
    // Loot
    // ---------------------
    void SpawnLoot()
    {
        if (lootPrefab == null) return;

        // Collect all rooms, and compute min/max area for weighting.
        List<Room> rooms = new List<Room>();
        CollectRooms(rootNode, rooms);

        if (rooms.Count == 0) return;

        int minArea = int.MaxValue;
        int maxArea = int.MinValue;
        foreach (var r in rooms)
        {
            int a = r.width * r.height;
            if (a < minArea) minArea = a;
            if (a > maxArea) maxArea = a;
        }

        SpawnLootInRooms(rootNode, minArea, maxArea);
    }

    void CollectRooms(BSPNode node, List<Room> rooms)
    {
        if (node == null) return;
        if (node.IsLeaf() && node.room != null) rooms.Add(node.room);
        CollectRooms(node.left, rooms);
        CollectRooms(node.right, rooms);
    }

    void SpawnLootInRooms(BSPNode node, int minArea, int maxArea)
    {
        if (node == null) return;

        if (node.IsLeaf() && node.room != null)
        {
            // Only spawn loot in treasure rooms
            if (node.roomLabel != 't')
                return;

            Room r = node.room;
            int area = r.width * r.height;

            float smallness = 0f;
            if (maxArea != minArea)
                smallness = 1f - Mathf.InverseLerp(minArea, maxArea, area);

            float expected = lootBasePerRoom * Mathf.Lerp(1f, lootSmallRoomMultiplier, smallness);

            int count = Mathf.FloorToInt(expected);
            float frac = expected - count;
            if (Random.value < frac) count++;

            count = Mathf.Clamp(count, 0, lootMaxPerRoom);

            for (int i = 0; i < count; i++)
            {
                if (TryPickLootSpot(r, out int lx, out int ly))
                {
                    Vector3 pos = new Vector3(lx, 0.2f, ly);
                    GameObject loot = Instantiate(lootPrefab, pos, Quaternion.identity);

                    if (node.hierarchyParent != null)
                        loot.transform.parent = node.hierarchyParent.transform;
                    else
                        loot.transform.parent = transform;

                    if (preventLootOverlap)
                        lootPositions.Add(new Vector2Int(lx, ly));
                }
            }

            return; // leaf done
        }

        SpawnLootInRooms(node.left, minArea, maxArea);
        SpawnLootInRooms(node.right, minArea, maxArea);
    }


    bool TryPickLootSpot(Room r, out int x, out int y)
    {
        // Define usable interior bounds so loot isn't on the wall tiles
        int minX = r.x + lootEdgePadding;
        int maxX = r.x + r.width - 1 - lootEdgePadding;
        int minY = r.y + lootEdgePadding;
        int maxY = r.y + r.height - 1 - lootEdgePadding;

        // If the room is too small to have an interior after padding, fall back to any tile in the room.
        if (minX > maxX || minY > maxY)
        {
            minX = r.x;
            maxX = r.x + r.width - 1;
            minY = r.y;
            maxY = r.y + r.height - 1;
        }

        // Try a handful of random samples
        const int attempts = 20;
        for (int i = 0; i < attempts; i++)
        {
            int tx = Random.Range(minX, maxX + 1);
            int ty = Random.Range(minY, maxY + 1);

            // Must be a room tile
            if (mapGrid[tx, ty] != 1) continue;

            // Avoid overlaps
            Vector2Int p = new Vector2Int(tx, ty);

            if (preventLootOverlap && lootPositions.Contains(p)) continue;
            if (reservedPositions.Contains(p)) continue;


            // Also avoid spawning on corridors, just in case (mapGrid check already handles it)
            x = tx;
            y = ty;
            return true;
        }

        x = y = 0;
        return false;
    }

    // ---------------------
    // Spawn + Goal
    // ---------------------
    void SpawnSpawnAndGoal()
    {
        if (goalPrefab == null) return;

        BSPNode spawnNode = entranceNode;
        BSPNode goalNode = bossNode;
        List<BSPNode> roomNodes = new List<BSPNode>();

        if (spawnNode == null || goalNode == null)
        {
            CollectRoomNodes(rootNode, roomNodes);
            if (roomNodes.Count == 0) return;

            spawnNode = roomNodes[Random.Range(0, roomNodes.Count)];
            goalNode = (placeGoalFarFromSpawn && roomNodes.Count > 1)
                ? FindFarthestRoomNode(spawnNode, roomNodes)
                : spawnNode;
        }


        // Spawn point
        if (TryPickRoomSpot(spawnNode.room, spawnGoalEdgePadding, out int sx, out int sy))
        {
            Vector2Int p = new Vector2Int(sx, sy);
            reservedPositions.Add(p);

            Vector3 pos = new Vector3(sx, 0.2f, sy);
            var cc = playerToMove.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            playerToMove.position = new Vector3(sx, playerToMove.position.y, sy);

            if (cc != null) cc.enabled = true;

        }

        // Goal
        if (goalPrefab != null)
        {
            // Try goal room first; if it fails (very rare), fall back to any other room.
            if (!TryPickRoomSpot(goalNode.room, spawnGoalEdgePadding, out int gx, out int gy))
            {
                foreach (var n in roomNodes)
                {
                    if (TryPickRoomSpot(n.room, spawnGoalEdgePadding, out gx, out gy))
                    {
                        goalNode = n;
                        break;
                    }
                }
            }

            Vector2Int gp = new Vector2Int(gx, gy);
            reservedPositions.Add(gp);

            Vector3 pos = new Vector3(gx, 0.2f, gy);
            GameObject goal = Instantiate(goalPrefab, pos, Quaternion.identity);
            goal.transform.parent = (goalNode.hierarchyParent != null) ? goalNode.hierarchyParent.transform : transform;
        }
    }

    void CollectRoomNodes(BSPNode node, List<BSPNode> results)
    {
        if (node == null) return;
        if (node.IsLeaf() && node.room != null) results.Add(node);

        CollectRoomNodes(node.left, results);
        CollectRoomNodes(node.right, results);
    }

    BSPNode FindFarthestRoomNode(BSPNode from, List<BSPNode> candidates)
    {
        BSPNode best = from;
        float bestDistSq = -1f;

        Vector2 fromCenter = new Vector2(from.room.centerX, from.room.centerY);

        foreach (var c in candidates)
        {
            if (c == from) continue;

            Vector2 cCenter = new Vector2(c.room.centerX, c.room.centerY);
            float d = (cCenter - fromCenter).sqrMagnitude;

            if (d > bestDistSq)
            {
                bestDistSq = d;
                best = c;
            }
        }

        return best;
    }

    bool TryPickRoomSpot(Room r, int edgePadding, out int x, out int y)
    {
        int minX = r.x + edgePadding;
        int maxX = r.x + r.width - 1 - edgePadding;
        int minY = r.y + edgePadding;
        int maxY = r.y + r.height - 1 - edgePadding;

        if (minX > maxX || minY > maxY)
        {
            minX = r.x;
            maxX = r.x + r.width - 1;
            minY = r.y;
            maxY = r.y + r.height - 1;
        }

        const int attempts = 30;
        for (int i = 0; i < attempts; i++)
        {
            int tx = Random.Range(minX, maxX + 1);
            int ty = Random.Range(minY, maxY + 1);

            if (mapGrid[tx, ty] != 1) continue; // must be room tile

            Vector2Int p = new Vector2Int(tx, ty);

            if (reservedPositions.Contains(p)) continue;
            if (preventSpawnGoalOnLoot && lootPositions.Contains(p)) continue;

            x = tx;
            y = ty;
            return true;
        }

        x = y = 0;
        return false;
    }

    void SpawnRoomMarkers()
    {
        // If neither is set, do nothing.
        if (minibossRoomPrefab == null && combatRoomPrefab == null) return;

        // Collect leaf room nodes
        List<BSPNode> roomNodes = new List<BSPNode>();
        CollectRoomNodes(rootNode, roomNodes);
        if (roomNodes.Count == 0) return;

        foreach (var node in roomNodes)
        {
            if (node.room == null) continue;

            GameObject prefab = null;

            if (node.roomLabel == 'm') prefab = minibossRoomPrefab;
            else if (node.roomLabel == 'c') prefab = combatRoomPrefab;

            if (prefab == null) continue;

            // Use the room’s center tile (int centerX/centerY)
            int cx = node.room.centerX;
            int cy = node.room.centerY;

            // Reserve this tile so loot/spawn/goal don't overlap it
            reservedPositions.Add(new Vector2Int(cx, cy));

            Vector3 pos = new Vector3(cx, roomMarkerY, cy);
            GameObject marker = Instantiate(prefab, pos, Quaternion.identity);

            // Parent neatly under the room's BSP hierarchy node
            marker.transform.parent = (node.hierarchyParent != null) ? node.hierarchyParent.transform : transform;
        }
    }


    // ---------------------
    // Grammar (Weighted / Debug)
    // ---------------------

    void LabelRoomsFromGrammarAndChooseEntranceBoss()
    {
        // 1) collect leaf room nodes
        List<BSPNode> roomNodes = new List<BSPNode>();
        CollectRoomNodes(rootNode, roomNodes);
        if (roomNodes.Count == 0) return;

        // 2) generate grammar output (terminal-only)
        lastGrammarOutput = GenerateGrammarString_TerminalOnly();
        Debug.Log($"[Grammar] Final terminal string: {lastGrammarOutput} (rooms: {roomNodes.Count})");

        // 3) ensure we have exactly one 'e' and one 'b' in the string
        // (your grammar should already do this, but this makes it robust)
        if (!lastGrammarOutput.Contains("e") || !lastGrammarOutput.Contains("b"))
        {
            Debug.LogWarning("[Grammar] Missing 'e' or 'b' in output; labeling aborted.");
            return;
        }

        // 4) pick which room becomes entrance and boss FIRST
        // We'll map 'e' and 'b' positions in the string to actual rooms after ordering.

        // Strategy: order rooms, then assign characters in order.
        // Start order: random but stable -> choose entrance index by where 'e' occurs.
        // Better: we'll pick an entrance room first (random), then sort others by distance from it.
        entranceNode = roomNodes[Random.Range(0, roomNodes.Count)];

        // Sort roomNodes to make progression feel coherent.
        if (sortRoomsByDistanceFromEntrance)
            roomNodes.Sort((a, b) => DistSq(a, entranceNode).CompareTo(DistSq(b, entranceNode)));
        else
            Shuffle(roomNodes);

        // Now, we want the room that gets label 'e' to be the entranceNode.
        // Easiest: rotate the room list so indexOf('e') points to entranceNode.
        int eIndex = lastGrammarOutput.IndexOf('e');
        int entranceIndex = roomNodes.IndexOf(entranceNode);
        RotateList(roomNodes, entranceIndex - eIndex);

        // 5) Assign labels to rooms
        // We'll store the label in node.labelSuffix so we can visualize/log it.
        // (We'll just log for now; you can later store it in a dictionary, component, etc.)
        int count = Mathf.Min(roomNodes.Count, lastGrammarOutput.Length);

        // We'll find bossNode by label 'b' after assignment.
        bossNode = null;

        for (int i = 0; i < roomNodes.Count; i++)
        {
            char labelChar;
            if (i < lastGrammarOutput.Length)
                labelChar = lastGrammarOutput[i];
            else
                labelChar = defaultRoomLabel;

            roomNodes[i].roomLabel = labelChar;

            // If we had to truncate grammar (more symbols than rooms), we simply ignore extras.
            // If we had more rooms than symbols, we fill with default label.

            // Record entrance/boss nodes
            if (labelChar == 'e') entranceNode = roomNodes[i];
            if (labelChar == 'b') bossNode = roomNodes[i];

            Debug.Log($"[RoomLabel] Room {i}: BSP {roomNodes[i].label} at ({roomNodes[i].room.centerX},{roomNodes[i].room.centerY}) => '{labelChar}'");
        }

        // Fallback if boss wasn't found due to extreme mismatch/truncation
        if (bossNode == null)
        {
            // choose farthest from entrance as boss
            bossNode = FindFarthestRoomNode(entranceNode, roomNodes);
            Debug.LogWarning("[RoomLabel] Boss label 'b' did not map to a room; using farthest room as boss instead.");
        }
    }

    int DistSq(BSPNode a, BSPNode b)
    {
        int dx = a.room.centerX - b.room.centerX;
        int dy = a.room.centerY - b.room.centerY;
        return dx * dx + dy * dy;
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void RotateList<T>(List<T> list, int shift)
    {
        // shift > 0 moves items right; shift < 0 moves items left
        int n = list.Count;
        if (n == 0) return;

        shift %= n;
        if (shift == 0) return;
        if (shift < 0) shift += n;

        // create a copy rotated
        var copy = new List<T>(list);
        for (int i = 0; i < n; i++)
            list[(i + shift) % n] = copy[i];
    }


    [System.Serializable]
    public class WeightedExpansion
    {
        public string expansion;
        public float weight = 1f;

        public WeightedExpansion(string expansion, float weight)
        {
            this.expansion = expansion;
            this.weight = weight;
        }
    }

    string GenerateGrammarString_TerminalOnly()
    {
        var grammar = BuildDungeonGrammar();
        string current = string.IsNullOrEmpty(grammarStart) ? "S" : grammarStart;

        int step = 0;

        while (true)
        {
            if (current.Length > grammarMaxLength) break;
            if (step > grammarMaxSteps) break;

            int idx = FindLeftmostNonterminalIndex(current, grammar);
            if (idx < 0) break;

            step++;

            char nt = current[idx];
            var options = grammar[nt];
            string replacement = ChooseWeighted(options);

            current = current.Substring(0, idx) + replacement + current.Substring(idx + 1);

            if (grammarLogSteps)
                Debug.Log($"[Grammar] Step {step}: expand '{nt}' -> \"{replacement}\" => {current}");
        }

        return current;
    }


    int FindLeftmostNonterminalIndex(string s, Dictionary<char, List<WeightedExpansion>> grammar)
    {
        for (int i = 0; i < s.Length; i++)
            if (grammar.ContainsKey(s[i]))
                return i;
        return -1;
    }

    string ChooseWeighted(List<WeightedExpansion> options)
    {
        float total = 0f;
        for (int i = 0; i < options.Count; i++)
            total += Mathf.Max(0f, options[i].weight);

        if (total <= 0f)
            return options[Random.Range(0, options.Count)].expansion;

        float roll = Random.value * total;
        float acc = 0f;

        for (int i = 0; i < options.Count; i++)
        {
            acc += Mathf.Max(0f, options[i].weight);
            if (roll <= acc)
                return options[i].expansion;
        }

        return options[options.Count - 1].expansion;
    }

    // ---------------------
    // Grammar Rules Definition
    // ---------------------

    Dictionary<char, List<WeightedExpansion>> BuildDungeonGrammar()
    {
        // Terminals: e b m c t
        // Nonterminals: S R P Q F

        // S -> e R b   (guarantees exactly one entrance and one boss)
        // R generates a sequence of "pieces" P (combat, miniboss+followup, or occasional treasure)
        // Q -> m F     (miniboss followed by follow-up)
        // F is weighted to start with treasure often (i.e. treasure-after-miniboss bias)

        var g = new Dictionary<char, List<WeightedExpansion>>();

        g['S'] = new List<WeightedExpansion>
        {
            new WeightedExpansion("eRb", 1f)
        };

        // R: sequence builder. Include "" (epsilon) so it can stop.
        // These weights control how long the dungeon "story" is.
        g['R'] = new List<WeightedExpansion>
        {
            new WeightedExpansion("PR", 12f),  // keep adding
            new WeightedExpansion("P", 1f),   // finish after one more
        };

        // P: the "next room type" generator
        // Treasure alone should be rarer (lower weight),
        // while miniboss chain Q has some weight.
        g['P'] = new List<WeightedExpansion>
        {
            new WeightedExpansion("c", 6f),   // lots of combat rooms
            new WeightedExpansion("Q", 6f),   // sometimes a miniboss beat
            new WeightedExpansion("t", 1f),   // rare standalone treasure room
        };

        // Q: miniboss beat -> miniboss + follow-up
        g['Q'] = new List<WeightedExpansion>
        {
            new WeightedExpansion("mF", 1f)
        };

        // F: follow-up after miniboss
        // Heavily bias treasure immediately after miniboss.
        g['F'] = new List<WeightedExpansion>
        {
            new WeightedExpansion("t", 8f),    // most common: miniboss -> treasure
            new WeightedExpansion("tt", 2f),   // double treasure (rarer)
            new WeightedExpansion("c", 1f),    // sometimes NOT treasure
        };

        return g;
    }


}
