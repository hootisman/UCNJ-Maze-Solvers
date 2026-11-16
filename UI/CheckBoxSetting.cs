using Godot;

[GlobalClass]
public partial class CheckBoxSetting : CheckBox, ISetting
{
	[Export] public SettingKey Key { get; set; } = SettingKey.None;

	public Variant ReadValue() => ButtonPressed;
	public void WriteValue(Variant value) => ButtonPressed = value.AsBool();
}
