using Godot;

[GlobalClass]
public partial class LineEditSetting : LineEdit, ISetting
{
	[Export] public SettingKey Key { get; set; } = SettingKey.None;

	public Variant ReadValue() => Text;
	public void WriteValue(Variant value) => Text = value.AsString();
}
