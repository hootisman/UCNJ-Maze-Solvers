using Godot;

/// <summary>Turns a SimulationSettings into a configured Maze scene and switches to it.</summary>
public static class SimulationLauncher
{
	private const string MazeScenePath = "res://Game/Maze.tscn";

	// Adding an agent = one line here.
	private static readonly (string NodeName, SettingKey WeightsKey)[] Agents =
	{
		("GoalAgent", SettingKey.GoalWeights),
		("CollectorAgent", SettingKey.CollectorWeights),
		("CautiousAgent", SettingKey.CautiousWeights),
	};

	public static Error Start(SimulationSettings settings, ulong? seedOverride = null)
	{
		PackedScene mazeScene = GD.Load<PackedScene>(MazeScenePath);
		if (mazeScene == null)
		{
			GD.PushError("Could not load the maze scene.");
			return Error.Failed;
		}

		Node gameScene = mazeScene.Instantiate();
		ConfigureMaze(gameScene.GetNode<Foreground>("MainMaze"), settings, seedOverride);
		ConfigureAgents(gameScene, settings);

		SceneTree tree = (SceneTree)Engine.GetMainLoop();
		return tree.ChangeSceneToNode(gameScene);
	}

	public static Error Restart(ulong generationSeed)
	{
		if (SimulationSettings.Last == null)
		{
			GD.PushError("Cannot restart the simulation because no setup settings have been saved.");
			return Error.Failed;
		}
		return Start(SimulationSettings.Last, generationSeed);
	}

	private static void ConfigureMaze(Foreground maze, SimulationSettings s, ulong? seedOverride)
	{
		int size = Mathf.RoundToInt((float)s.Get<double>(SettingKey.MazeSize, 10));

		maze.MazeWidth = size;
		maze.MazeHeight = size;
		//if using custom counts, set them here
		if (s.Get<bool>(SettingKey.CustomCounts))
		{
			maze.ExitCount = (int)s.Get<double>(SettingKey.ExitCount, 1);
			maze.SpikeCount = (int)s.Get<double>(SettingKey.HazardCount);
			maze.CoinCount = (int)s.Get<double>(SettingKey.CoinCount);
		}
		maze.IsOnTrialMode = s.Get<bool>(SettingKey.TrialMode);
		maze.TrialCount = (int)s.Get<double>(SettingKey.TrialCount);
		maze.IsUsingCustomSeed = s.Get<bool>(SettingKey.UseSeed);
		maze.Seed = maze.IsUsingCustomSeed
			? seedOverride ?? ParseSeedOrRandom(s.Get<string>(SettingKey.Seed, ""))
			: RandomSeed();

		GD.Print($"Seed: {maze.Seed}");
	}

	private static void ConfigureAgents(Node gameScene, SimulationSettings s)
	{
		float speed = (float)s.Get<double>(SettingKey.SpeedInterval, 0.1);

		foreach (var (nodeName, weightsKey) in Agents)
		{
			AStarAgent agent = gameScene.GetNode<AStarAgent>(nodeName);
			agent.SetWeights(MainMenu.Weights.FromVector(s.Get<Vector3>(weightsKey)));
			agent.SecondsPerCell = s.Get<bool>(SettingKey.UseSpeedInterval) ? speed : AStarAgent.MinSecondsPerCell;
		}
	}

	private static ulong ParseSeedOrRandom(string text)
	{
		if (ulong.TryParse(text, System.Globalization.NumberStyles.None,
			System.Globalization.CultureInfo.InvariantCulture, out ulong seed))
		{
			return seed;
		}
		GD.PushWarning("Seed parse failed, generating new seed...");
		return RandomSeed();
	}

	private static ulong RandomSeed()
	{
		RandomNumberGenerator rng = new();
		rng.Randomize();
		return rng.Seed;
	}
}