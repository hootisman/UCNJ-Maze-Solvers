using Godot;
using System.Collections.Generic;

public interface ISetting
{
	SettingKey Key { get; }
	Variant ReadValue();
	void WriteValue(Variant value);
}


public enum SettingKey
{
	None = 0,   // unassigned, ignored by the binder
	MazeSize,
	ExitCount,
	HazardCount,
	CoinCount,
	SpeedInterval,
	UseSeed,
	Seed,
	GoalWeights,
	CollectorWeights,
	CautiousWeights,
	CustomCounts,
	TrialMode,
	TrialCount,
	UseSpeedInterval,
	HealthCount,
}

//all settings in a wrapper class
public sealed class SimulationSettings
{
	public static SimulationSettings Last { get; set; }

	private readonly Dictionary<SettingKey, Variant> _values = new();

	public void Set(SettingKey key, Variant value) => _values[key] = value;

	public bool TryGet(SettingKey key, out Variant value) => _values.TryGetValue(key, out value);

	//Returns corresponding setting, will depend on what type! (spinbox, checkbox, lineedit, etc.)
	public T Get<[MustBeVariant] T>(SettingKey key, T fallback = default)
{
	if (!_values.TryGetValue(key, out Variant v))
	{
		GD.PushWarning($"Setting '{key}' not found, using fallback value '{fallback}'.");
		return fallback;
	}
	return v.As<T>();
}
}

//useful for loading settings and finding setting ui elements
public static class SettingsBinder
{
	public static SimulationSettings Collect(Node root)
	{
		SimulationSettings settings = new();
		foreach (ISetting setting in Find(root))
		{
			settings.Set(setting.Key, setting.ReadValue());
		}
		foreach (SettingKey key in System.Enum.GetValues<SettingKey>())
		{
			if (key == SettingKey.None) continue;
			if (!settings.TryGet(key, out _))
			{
				GD.PushWarning($"Setting '{key}' has no control in the scene.");
			}
		}

		return settings;
	}

	public static void Apply(Node root, SimulationSettings settings)
	{
		foreach (ISetting setting in Find(root))
		{
			if (settings.TryGet(setting.Key, out Variant value))
			{
				setting.WriteValue(value);
			}
		}
	}

	private static IEnumerable<ISetting> Find(Node root)
	{
		foreach (Node node in root.FindChildren("*", "", true, false))
		{
			if (node is ISetting setting && setting.Key != SettingKey.None)
			{
				yield return setting;
			}
		}
	}
}