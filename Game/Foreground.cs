using Godot;
using System;
using System.Collections.Generic;

public partial class Foreground : TileMapLayer
{
	public enum MazeAlgorithm
	{
		RandomizedPrim,
		DepthFirstSearch
	}

	public int MazeWidth  = 48;

	public int MazeHeight = 48;

	public static bool IsPaused = false;


	// //our a star implementation
	// [Export]
	// public required AStarOverlay _aOverlay;
	[Export]
	public AStarAgent[] Agents;

	//second tilemap representing items
	[Export]
	public required TileMapLayer ItemLayer;

	public ulong Seed { get; set; }
	//true if using Seed variable to generate, false if fully random
	public bool IsUsingCustomSeed;
	//true if on experiement/trial mode
	public bool IsOnTrialMode;

	public int TrialCount;
	public static int CurrentTrial= 1;

	[Export(PropertyHint.Range, "1,20,1")]
	public int ExitCount { get; set; } = 1;

	public bool UniqueCollectibles { get; set; }
	public bool UniqueHazards { get; set; }

	public MazeAlgorithm SelectedAlgorithm { get; set; } = MazeAlgorithm.RandomizedPrim;
	public string ActiveAlgorithm { get; private set; } = "Not generated";
	public Vector2I GeneratedSize { get; private set; }
	// public int GenerationSeed { get; private set; }

	// Both atlas tiles are in the tileset source with ID 0.
	// Their atlas coordinates are (1,14) for floor and (1,19) for wall.
	private const int TileSetSourceId = 0;

	//see its 1,14 for floor and 1,19 for wall
	public readonly static Vector2I FloorAtlasCell = new(1,14);
	public readonly static Vector2I WallAtlasCell = new(1,19);
	public readonly static Vector2I SpikeAtlasCell = new(4,79);
	//its a freaking pumpkin 
	public readonly static Vector2I CoinAtlasCell = new(12,66);

	public Vector2I StartCell = new(0,1);
	//helper for just referencing all the exit cells 
	public Godot.Collections.Array<Vector2I> ExitCells = new();
	//refs for all coin cells
	public Godot.Collections.Array<Vector2I> CoinCells= new();
	//refs for all spike cells
	public Godot.Collections.Array<Vector2I> SpikeCells= new();

	[Export]
	public int CoinCount = 20;

	[Export]
	public int SpikeCount = 20;

	private int _finishedAgents;

	private static readonly Vector2I[] Directions =
	{
		new(0, -1),
		new(1, 0),
		new(0, 1),
		new(-1, 0)
	};
	public bool isFloor(Vector2I cell) => GetCellAtlasCoords(cell) == FloorAtlasCell;
	public bool isWall(Vector2I cell) => GetCellAtlasCoords(cell) == WallAtlasCell;

	public static bool IsShowingAgentPaths = true;
	public static bool IsShowingAgentGrids = false;

	/* Floor count-based maze algo variables */
	// [Export(PropertyHint.Range, "1,10000,1")]
	// public int FloorTileTarget { get; set; } = 400;

	public float FloorDensity = 0.45f;

	public int FloorTileTarget => Mathf.RoundToInt(MazeWidth * MazeHeight * FloorDensity);
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float WindingBias { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float LoopChance { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float OpenChance { get; set; } = 0.05f;

	public int ActualFloorCount { get; private set; }

	private const float PathWiggle = 4f;   // randomness of the start-to-exit paths

	public override void _Ready()
	{
		// GeneratePrim();
		

		// GenerateBraided();
		GenerateMaze();
		InitItemLayer();

		// _aOverlay.QueueRedraw();	//must call this after building grid/weight change/finding path
		foreach(AStarAgent agent in Agents)
		{
			agent.RunEnded += OnAgentRunEnded;
			agent.AStarAlgo.QueueRedraw();
		}
	}
	public void InitItemLayer()
	{
		CoinCells.Clear();
		SpikeCells.Clear();
		int coins = CoinCount;
		int spikes = SpikeCount;
		RandomNumberGenerator random = RandomFromSeed();
		for(; coins > 0 ;)
		{
			Vector2I potentialCoinCell = new Vector2I(random.RandiRange(1, MazeWidth - 1), random.RandiRange(1,MazeHeight - 1));
			if(isFloor(potentialCoinCell)){
				//add a coin
				ItemLayer.SetCell(potentialCoinCell, TileSetSourceId, CoinAtlasCell);
				CoinCells.Add(potentialCoinCell);
				coins--;
			}
		}


		for(; spikes > 0 ;)
		{
			Vector2I potentialSpikeCell = new Vector2I(random.RandiRange(5, MazeWidth - 5), random.RandiRange(5,MazeHeight - 5));
			if (isFloor(potentialSpikeCell))
			{
				ItemLayer.SetCell(potentialSpikeCell, TileSetSourceId, SpikeAtlasCell);
				SpikeCells.Add(potentialSpikeCell);
				spikes--;
			}
		}
		
	}


	public override void _UnhandledInput(InputEvent @event)
	{
		//why is this here
		if (@event is not InputEventKey keyEvent ||
			!keyEvent.Pressed ||
			keyEvent.Echo)
		{
			return;
		}

		if (keyEvent.Keycode == Key.Escape)
		{
			MainMenu.ReturnToSetupOnNextOpen();
			Error error = GetTree().ChangeSceneToFile("res://UI/main_menu.tscn");
			if (error != Error.Ok)
			{
				GD.PushError($"Could not return to generator settings: {error}.");
			}

			GetViewport().SetInputAsHandled();
		}
		else if (keyEvent.Keycode == Key.R)
		{
			Error error = MainMenu.RestartSimulation(Seed);
			if (error != Error.Ok)
			{
				GD.PushError($"Could not restart the simulation: {error}.");
			}

			GetViewport().SetInputAsHandled();
		}
		else if (keyEvent.Keycode == Key.P)
		{
			IsPaused = !IsPaused;
			GetViewport().SetInputAsHandled();
		}
		else if (keyEvent.Keycode == Key.S)
		{
			IsShowingAgentPaths = !IsShowingAgentPaths;
			foreach(AStarAgent agent in Agents)
			{
				agent.AStarAlgo.QueueRedraw();
			}
			// Visible = !Visible;
			GetViewport().SetInputAsHandled();
		}
	}

	public void GenerateDepthFirst()
	{
		// 1) Normalize the requested dimensions and initialize every cell as a wall.
		ActiveAlgorithm = "Depth-first search";
		GeneratedSize = new Vector2I(MazeWidth, MazeHeight);
		bool[,] walls = new bool[MazeHeight, MazeWidth];

		for (int y = 0; y < MazeHeight; y++)
		{
			for (int x = 0; x < MazeWidth; x++)
			{
				walls[y, x] = true;
			}
		}

		// 2) Resolve one seed for this run so the full maze can be reproduced on restart.
		RandomNumberGenerator random = RandomFromSeed();

		// 3) Start a depth-first search at (1,1). The list acts as a stack of the current route.
		List<Vector2I> path = new() { new Vector2I(1, 1) };
		walls[1, 1] = false;

		// 4) Visit uncarved maze cells until the stack is empty.
		while (path.Count > 0)
		{
			// The last cell in the stack is the current end of the route.
			Vector2I current = path[path.Count - 1];
			List<Vector2I> unvisited = new();

			// Check cells two steps away so the cell between becomes a connecting passage.
			foreach (Vector2I direction in Directions)
			{
				Vector2I next = current + direction * 2;
				if (next.X > 0 && next.X < MazeWidth - 1 &&
					next.Y > 0 && next.Y < MazeHeight - 1 &&
					walls[next.Y, next.X])
				{
					unvisited.Add(next);
				}
			}

			// If there are no unvisited neighbors, backtrack to the previous cell on the route.
			if (unvisited.Count == 0)
			{
				path.RemoveAt(path.Count - 1);
				continue;
			}

			// Choose a random unvisited neighbor, open the intervening wall and the neighbor,
			// then continue the search from that neighbor.
			Vector2I chosen = unvisited[(int)random.RandiRange(0, unvisited.Count - 1)];
			Vector2I between = (current + chosen) / 2;
			walls[between.Y, between.X] = false;
			walls[chosen.Y, chosen.X] = false;
			path.Add(chosen);
		}

		// Add the entrance and exit, then render the wall grid into the TileMapLayer.
		OpenEntrances(walls, MazeWidth, MazeHeight);
		DrawMaze(walls, MazeWidth, MazeHeight);
	}

	public void GeneratePrim()
	{
		// 1) Normalize the requested dimensions and initialize every cell as a wall.
		ActiveAlgorithm = "Randomized Prim";
		GeneratedSize = new Vector2I(MazeWidth, MazeHeight);
		// true means wall; false means open floor.
		bool[,] walls = new bool[MazeHeight, MazeWidth];
		for (int y = 0; y < MazeHeight; y++)
		{
			for (int x = 0; x < MazeWidth; x++)
			{
				walls[y, x] = true;
			}
		}

		// 2) Resolve one seed for this run so the full maze can be reproduced on restart.
		RandomNumberGenerator random = RandomFromSeed();

		// 3) Start the maze at (1,1). The frontier stores uncarved cells two steps away
		//    from the currently open maze, and queued prevents adding a cell more than once.
		Vector2I start = new(1, 1);
		walls[start.Y, start.X] = false;
		List<Vector2I> frontier = new();
		bool[,] queued = new bool[MazeHeight, MazeWidth];

		// Add each in-bounds, still-walled neighbor of an open cell to the frontier.
		void AddFrontier(Vector2I cell)
		{
			foreach (Vector2I direction in Directions)
			{
				Vector2I candidate = cell + direction * 2;
				if (candidate.X > 0 && candidate.X < MazeWidth - 1 &&
					candidate.Y > 0 && candidate.Y < MazeHeight - 1 &&
					walls[candidate.Y, candidate.X] &&
					!queued[candidate.Y, candidate.X])
				{
					frontier.Add(candidate);
					queued[candidate.Y, candidate.X] = true;
				}
			}
		}

		AddFrontier(start);
		// 4) Randomized Prim grows outward until there are no frontier cells left.
		while (frontier.Count > 0)
		{
			// Remove a random frontier cell. It may already have been opened by another connection.
			int frontierIndex = (int)random.RandiRange(0, frontier.Count - 1);
			Vector2I candidate = frontier[frontierIndex];
			frontier.RemoveAt(frontierIndex);

			if (!walls[candidate.Y, candidate.X])
			{
				continue;
			}

			// Find open maze cells two steps away; one will connect this frontier cell to the maze.
			List<Vector2I> openNeighbors = new();
			foreach (Vector2I direction in Directions)
			{
				Vector2I neighbor = candidate + direction * 2;
				if (neighbor.X > 0 && neighbor.X < MazeWidth - 1 &&
					neighbor.Y > 0 && neighbor.Y < MazeHeight - 1 &&
					!walls[neighbor.Y, neighbor.X])
				{
					openNeighbors.Add(neighbor);
				}
			}

			if (openNeighbors.Count == 0)
			{
				continue;
			}

			// Open the candidate and the wall between it and a randomly selected open neighbor.
			Vector2I parent = openNeighbors[(int)random.RandiRange(0, openNeighbors.Count - 1)];
			Vector2I between = (parent + candidate) / 2;
			walls[between.Y, between.X] = false;
			walls[candidate.Y, candidate.X] = false;

			// Add this newly opened cell's still-walled neighbors to the frontier.
			AddFrontier(candidate);
		}

		// Add the entrance and exit, then render the wall grid into the tile map.
		OpenEntrances(walls, MazeWidth, MazeHeight);
		DrawMaze(walls, MazeWidth, MazeHeight);
	}

	public void GenerateBraided()
	{
		ActiveAlgorithm = "Braided (loops)";
		GeneratedSize = new Vector2I(MazeWidth, MazeHeight);

		bool[,] walls = new bool[MazeHeight, MazeWidth];
		for (int y = 0; y < MazeHeight; y++)
		{
			for (int x = 0; x < MazeWidth; x++)
			{
				walls[y, x] = true;
			}
		}

		RandomNumberGenerator random = RandomFromSeed();
		
		// 1) Start with a perfect maze.
		CarveDepthFirst(walls, random);

		// 2) Remove some interior walls. Cells sit on odd coordinates, so a wall tile with
		//    exactly one even coordinate sits between two cells. Tiles with both coordinates
		//    even are corner posts and stay put, which keeps the grid looking like a maze.
		int lastX = MazeWidth % 2 == 0 ? MazeWidth - 3 : MazeWidth - 2;
		int lastY = MazeHeight % 2 == 0 ? MazeHeight - 3 : MazeHeight - 2;

		for (int y = 1; y <= lastY; y++)
		{
			for (int x = 1; x <= lastX; x++)
			{
				bool betweenCells = (x % 2 == 0) != (y % 2 == 0);
				float LoopChance = 0.15f;	//TODO: temp, move to global
				if (betweenCells && walls[y, x] && random.Randf() < LoopChance)
				{
					walls[y, x] = false;
				}
			}
		}

		OpenEntrances(walls, MazeWidth, MazeHeight);
		DrawMaze(walls, MazeWidth, MazeHeight);
	}


	private void OpenEntrances(bool[,] walls, int width, int height)
	{
		ExitCells.Clear();
		// Open the left entrance and a right-side exit aligned with an inner maze row.
		walls[StartCell.Y, StartCell.X] = false;
		// int exitY = height % 2 == 0 ? height - 3 : height - 2;
		// walls[exitY, width - 2] = false;
		// walls[exitY, width - 1] = false;
		int lastX = width % 2 == 0 ? width - 3 : width - 2;
    	int lastY = height % 2 == 0 ? height - 3 : height - 2;

		List<Vector2I> candidates = new();
		RandomNumberGenerator random = RandomFromSeed();
		// Right edge
		for (int y = 1; y <= lastY; y += 2)
		{
			if (y >= height / 2)
				candidates.Add(new Vector2I(width - 1, y));
		}

		// Bottom edge
		for (int x = 1; x <= lastX; x += 2)
		{
			if (x >= width / 2)
				candidates.Add(new Vector2I(x, height - 1));
		}

		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = random.RandiRange(0, i);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}

		int count = Math.Min(ExitCount, candidates.Count);
		for (int i = 0; i < count; i++)
		{
			Vector2I exit = candidates[i];

			if (exit.X == width - 1)
			{
				//Right Edge carve
				for (int x = lastX + 1; x <= width - 1; x++)
					walls[exit.Y, x] = false;
			}
			else
			{
				//Bottom edge carve
				for (int y = lastY + 1; y <= height - 1; y++)
					walls[y, exit.X] = false;
			}

			ExitCells.Add(exit);
		}

		//record exits 
		//the x's and y's are reversed which im sure will never be a problem in the future wink face
		// ExitCells.Add(new(width - 1, exitY));
	}

	private void DrawMaze(bool[,] walls, int width, int height)
	{
		// Remove the previous layout, draw floor at every cell, and place walls over blocked cells.
		Clear();

		ForEachTile((int x, int y) =>
		{
			Vector2I cell = new(x, y);
			// Atlas coordinates: floor is (1,14), wall is (1,19).
			SetCell(cell, TileSetSourceId, FloorAtlasCell);
			if (walls[y, x])
			{
				SetCell(cell, TileSetSourceId, WallAtlasCell);
			}
			return true;
		});
	}

	// Same algorithm as GenerateDepthFirst, minus the setup and drawing.
	private void CarveDepthFirst(bool[,] walls, RandomNumberGenerator random)
	{
		List<Vector2I> path = new() { new Vector2I(1, 1) };
		walls[1, 1] = false;

		while (path.Count > 0)
		{
			Vector2I current = path[path.Count - 1];
			List<Vector2I> unvisited = new();

			foreach (Vector2I direction in Directions)
			{
				Vector2I next = current + direction * 2;
				if (next.X > 0 && next.X < MazeWidth - 1 &&
					next.Y > 0 && next.Y < MazeHeight - 1 &&
					walls[next.Y, next.X])
				{
					unvisited.Add(next);
				}
			}

			if (unvisited.Count == 0)
			{
				path.RemoveAt(path.Count - 1);
				continue;
			}

			Vector2I chosen = unvisited[random.RandiRange(0, unvisited.Count - 1)];
			Vector2I between = (current + chosen) / 2;
			walls[between.Y, between.X] = false;
			walls[chosen.Y, chosen.X] = false;
			path.Add(chosen);
		}
	}



	private void OnAgentRunEnded(AStarAgent agent)
	{
		_finishedAgents++;
		if (_finishedAgents < Agents.Length) return;

		GD.Print($"completed (trial {CurrentTrial}/{TrialCount})");

		if (!IsOnTrialMode) return;

		if (CurrentTrial >= TrialCount)
		{
			GD.Print($"All {TrialCount} trials finished.");
			return;   //doesnt start any more trials
		}

		CurrentTrial++;
		CallDeferred(MethodName.StartNextTrial);
	}

	private void StartNextTrial()
	{

		RandomNumberGenerator random = new();
		if (IsUsingCustomSeed) random.Seed = Seed; else random.Randomize();
		
		// random.Randomize();
		Error error = MainMenu.RestartSimulation(random.Seed);
		if (error != Error.Ok)
		{
			GD.PushError($"Could not start trial {CurrentTrial}: {error}.");
		}
	}

	public RandomNumberGenerator RandomFromSeed()
	{
		RandomNumberGenerator random = new();
		random.Seed = Seed;
		return random;
	}

	//helper so we don't accidentally parse incorrectly
	public void ForEachTile(Func<int,int,bool> func)
	{
		for (int y = 0; y < MazeHeight; y++)
		{
			for (int x = 0; x < MazeWidth; x++)
			{
				func.Invoke(x,y);
			}
		}
	}

	public void GenerateMaze()
	{
		GeneratedSize = new Vector2I(MazeWidth, MazeHeight);
		RandomNumberGenerator random = RandomFromSeed();

		bool[,] floor = new bool[MazeHeight, MazeWidth];   // [y, x], true = floor
		int count = 0;
		void Carve(Vector2I c)
		{
			if (!floor[c.Y, c.X]) { floor[c.Y, c.X] = true; count++; }
		}

		// 1) Start tile and the tile just inside it.
		Carve(StartCell);
		Carve(StartCell + Vector2I.Right);

		// 2) Pick exits on the right/bottom edges (edge tile + the tile just inside it).
		List<(Vector2I Edge, Vector2I Inner)> candidates = new();
		for (int y = 1; y <= MazeHeight - 2; y++)
			if (y >= MazeHeight / 2) candidates.Add((new(MazeWidth - 1, y), new(MazeWidth - 2, y)));
		for (int x = 1; x <= MazeWidth - 2; x++)
			if (x >= MazeWidth / 2) candidates.Add((new(x, MazeHeight - 1), new(x, MazeHeight - 2)));

		for (int i = candidates.Count - 1; i > 0; i--)
		{
			int j = random.RandiRange(0, i);
			(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
		}

		ExitCells.Clear();
		List<Vector2I> exitInners = new();
		foreach (var (edge, inner) in candidates)
		{
			if (exitInners.Count >= ExitCount) break;
			bool tooClose = false;
			foreach (Vector2I existing in ExitCells)
				if (Mathf.Abs(existing.X - edge.X) + Mathf.Abs(existing.Y - edge.Y) < 3) tooClose = true;
			if (tooClose) continue;

			Carve(edge);
			Carve(inner);
			ExitCells.Add(edge);
			exitInners.Add(inner);
		}

		// Clamp the target to what the grid can physically hold.
		int maxFloor = (MazeWidth - 2) * (MazeHeight - 2) + 1 + ExitCells.Count;
		int target = Mathf.Min(FloorTileTarget, maxFloor);
		if (target < FloorTileTarget)
			GD.PushWarning($"FloorTileTarget {FloorTileTarget} exceeds capacity {maxFloor}; using {target}.");

		// 3) Connect every exit to the start with a winding path.
		foreach (Vector2I inner in exitInners)
		{
			bool[,] connected = FloodFrom(floor, StartCell);
			if (connected[inner.Y, inner.X]) continue;
			foreach (Vector2I tile in FindWindingPath(floor, connected, inner, random))
				Carve(tile);
		}

		if (count > target)
			GD.PushWarning($"Start-to-exit paths need {count} tiles but target is {target}. Raise the target.");

		// 4) Grow outward until the count is exact (growing-tree with staged rule relaxation).
		List<Vector2I> active = new();
		void Refill()
		{
			active.Clear();
			for (int y = 0; y < MazeHeight; y++)
				for (int x = 0; x < MazeWidth; x++)
					if (floor[y, x]) active.Add(new Vector2I(x, y));
		}

		int stage = 0;
		Refill();
		while (count < target)
		{
			if (active.Count == 0)
			{
				if (stage >= 2) break;   // grid is full
				stage++;
				Refill();
				continue;
			}

			int index = random.Randf() < WindingBias ? active.Count - 1 : random.RandiRange(0, active.Count - 1);
			if (TryPickCandidate(floor, active[index], stage, random, out Vector2I picked))
			{
				Carve(picked);
				active.Add(picked);
			}
			else
			{
				active.RemoveAt(index);
			}
		}

		ActualFloorCount = count;
		if (count != FloorTileTarget)
			GD.PushWarning($"Floor tiles: wanted {FloorTileTarget}, generated {count}.");

		bool[,] walls = new bool[MazeHeight, MazeWidth];
		for (int y = 0; y < MazeHeight; y++)
			for (int x = 0; x < MazeWidth; x++)
				walls[y, x] = !floor[y, x];

		DrawMaze(walls, MazeWidth, MazeHeight);
	}


	private bool TryPickCandidate(bool[,] floor, Vector2I cell, int stage, RandomNumberGenerator random, out Vector2I picked)
	{
		List<Vector2I> valid = new(4);
		foreach (Vector2I direction in Directions)
		{
			Vector2I c = cell + direction;
			if (!InInterior(c) || floor[c.Y, c.X]) continue;

			int neighbors = FloorNeighborCount(floor, c);
			bool ok;
			if (stage == 2 || neighbors <= 1)
			{
				ok = true;   // plain extension of a corridor
			}
			else
			{
				bool makesBlock = WouldMake2x2(floor, c);
				ok = stage == 0
					? random.Randf() < (makesBlock ? OpenChance : LoopChance)
					: !makesBlock;
			}
			if (ok) valid.Add(c);
		}

		picked = valid.Count > 0 ? valid[random.RandiRange(0, valid.Count - 1)] : default;
		return valid.Count > 0;
	}

	private List<Vector2I> FindWindingPath(bool[,] floor, bool[,] connected, Vector2I target, RandomNumberGenerator random)
	{
		// Dijkstra from every connected tile at once, with random tile costs so paths wander.
		float[,] dist = new float[MazeHeight, MazeWidth];
		Vector2I[,] prev = new Vector2I[MazeHeight, MazeWidth];
		PriorityQueue<Vector2I, float> queue = new();

		for (int y = 0; y < MazeHeight; y++)
		{
			for (int x = 0; x < MazeWidth; x++)
			{
				dist[y, x] = float.PositiveInfinity;
				if (connected[y, x])
				{
					dist[y, x] = 0;
					queue.Enqueue(new Vector2I(x, y), 0);
				}
			}
		}

		while (queue.TryDequeue(out Vector2I current, out float priority))
		{
			if (priority > dist[current.Y, current.X]) continue;
			if (current == target) break;

			foreach (Vector2I direction in Directions)
			{
				Vector2I next = current + direction;
				if (!InInterior(next)) continue;

				// Random cost plus a penalty for hugging existing floor, so paths don't form blobs.
				float cost = 1f + random.Randf() * PathWiggle + 2f * FloorNeighborCount(floor, next);
				float newDist = dist[current.Y, current.X] + cost;
				if (newDist < dist[next.Y, next.X])
				{
					dist[next.Y, next.X] = newDist;
					prev[next.Y, next.X] = current;
					queue.Enqueue(next, newDist);
				}
			}
		}

		List<Vector2I> path = new();
		if (float.IsPositiveInfinity(dist[target.Y, target.X])) return path;

		Vector2I step = target;
		while (!connected[step.Y, step.X])
		{
			path.Add(step);
			step = prev[step.Y, step.X];
		}
		return path;
	}

	private bool[,] FloodFrom(bool[,] floor, Vector2I start)
	{
		bool[,] seen = new bool[MazeHeight, MazeWidth];
		Queue<Vector2I> queue = new();
		seen[start.Y, start.X] = true;
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			Vector2I current = queue.Dequeue();
			foreach (Vector2I direction in Directions)
			{
				Vector2I next = current + direction;
				if (IsFloorAt(floor, next) && !seen[next.Y, next.X])
				{
					seen[next.Y, next.X] = true;
					queue.Enqueue(next);
				}
			}
		}
		return seen;
	}

	private bool InInterior(Vector2I c) =>
		c.X >= 1 && c.X <= MazeWidth - 2 && c.Y >= 1 && c.Y <= MazeHeight - 2;

	private bool IsFloorAt(bool[,] floor, Vector2I p) =>
		p.X >= 0 && p.X < MazeWidth && p.Y >= 0 && p.Y < MazeHeight && floor[p.Y, p.X];

	private int FloorNeighborCount(bool[,] floor, Vector2I c)
	{
		int n = 0;
		foreach (Vector2I direction in Directions)
			if (IsFloorAt(floor, c + direction)) n++;
		return n;
	}

	// True if making c floor would complete a 2x2 block of floor.
	private bool WouldMake2x2(bool[,] floor, Vector2I c)
	{
		for (int sx = -1; sx <= 0; sx++)
		{
			for (int sy = -1; sy <= 0; sy++)
			{
				bool allFloor = true;
				for (int dx = 0; dx <= 1 && allFloor; dx++)
				{
					for (int dy = 0; dy <= 1; dy++)
					{
						Vector2I p = new(c.X + sx + dx, c.Y + sy + dy);
						if (p != c && !IsFloorAt(floor, p)) { allFloor = false; break; }
					}
				}
				if (allFloor) return true;
			}
		}
		return false;
	}

}
