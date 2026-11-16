using Godot;

public partial class MainMenu : Control
{
	// Kept because AStarAgent.SetWeights takes it.
	public struct Weights(float c, float f, float h)
	{
		public float Coin = c;
		public float Floor = f;
		public float Hazard = h;
		public static Weights FromVector(Vector3 v) => new(v.X, v.Y, v.Z);
	}

	private static bool _returnToSetupOnNextOpen;

	[Export] public Control MainOptions;
	[Export] public Control SetupOptions;
	[Export] public Button StartSimButton;  // opens the setup panel
	[Export] public Button ExitButton;
	[Export] public Button RunSimButton;    // starts the sim

	public override void _Ready()
	{
		StartSimButton.Pressed += ShowSetupOptions;
		ExitButton.Pressed += () => GetTree().Quit();
		RunSimButton.Pressed += OnRunSimulationPressed;

		if (SimulationSettings.Last != null)
		{
			SettingsBinder.Apply(SetupOptions, SimulationSettings.Last);
		}

		if (_returnToSetupOnNextOpen)
		{
			ShowSetupOptions();
			_returnToSetupOnNextOpen = false;
		}
	}

	private void ShowSetupOptions()
	{
		MainOptions.Hide();
		SetupOptions.Show();
	}

	private void OnRunSimulationPressed()
	{
		SimulationSettings.Last = SettingsBinder.Collect(SetupOptions);
		Foreground.CurrentTrial = 1;	//resets 

		Error error = SimulationLauncher.Start(SimulationSettings.Last);
		if (error != Error.Ok)
		{
			GD.PushError($"Could not start the simulation: {error}.");
		}
	}

	public static Error RestartSimulation(ulong generationSeed) => SimulationLauncher.Restart(generationSeed);
	public static void ReturnToSetupOnNextOpen() => _returnToSetupOnNextOpen = true;
}
