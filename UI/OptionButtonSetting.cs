using Godot;

[GlobalClass]
public partial class OptionButtonSetting : OptionButton, ISetting
{
	[Export] public SettingKey Key { get; set; } = SettingKey.None;

	public Variant ReadValue() => Selected;
	public void WriteValue(Variant value) => Select(value.AsInt32());
}
