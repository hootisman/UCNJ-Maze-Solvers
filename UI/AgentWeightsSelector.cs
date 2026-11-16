using Godot;

public partial class AgentWeightsSelector : VBoxContainer, ISetting
{
    [Export]
    public string AgentName = "";
    [Export] public SettingKey Key { get; set; } = SettingKey.None;

    [Export]
    public float DefaultCoinWeight = 15.0f;
    [Export]
    public float DefaultFloorWeight = 40.0f;
    [Export]
    public float DefaultHazardWeight = 80.0f;

    [ExportGroup("UIRefs")]
    [Export]
    public Label NameLabel;
    [Export]
    public SpinBox CoinBox;
    [Export]
    public SpinBox FloorBox;
    [Export]
    public SpinBox HazardBox;
    

	public Variant ReadValue() => new Vector3((float)CoinBox.Value, (float)FloorBox.Value, (float)HazardBox.Value);
	public void WriteValue(Variant v)
	{
		Vector3 w = v.AsVector3();
		CoinBox.Value = w.X; FloorBox.Value = w.Y; HazardBox.Value = w.Z;
	}

    public override void _Ready()
    {
        base._Ready();
        NameLabel.Text = $"{AgentName} AGENT";

        CoinBox.Value = DefaultCoinWeight;
        FloorBox.Value = DefaultFloorWeight;
        HazardBox.Value = DefaultHazardWeight;
    }

    public void Restore(MainMenu.Weights weights)
    {
        CoinBox.Value = weights.Coin;
        FloorBox.Value = weights.Floor;
        HazardBox.Value = weights.Hazard;
    }
}