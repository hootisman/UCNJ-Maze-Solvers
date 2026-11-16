using Godot;

[GlobalClass]
public partial class SpinBoxSetting : SpinBox, ISetting
{
	[Export] public SettingKey Key { get; set; } = SettingKey.None;

	public Variant ReadValue() => Value;
	public void WriteValue(Variant value) => Value = value.AsDouble();
}
