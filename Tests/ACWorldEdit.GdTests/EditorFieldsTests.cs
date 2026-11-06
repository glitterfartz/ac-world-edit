using ACWorldEdit;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace ACWorldEdit.GdTests;

/// <summary>The shared editor rows must carry a field's explanation on hover, including nested controls.</summary>
[TestSuite]
[RequireGodotRuntime]
public class EditorFieldsTests
{
    [TestCase]
    public void RowTipReachesTheCaptionAndEveryNestedControl()
    {
        var edit = EditorFields.Line("", _ => { });
        var nested = new HBoxContainer();
        var inner = new LineEdit();
        nested.AddChild(inner);
        var row = EditorFields.Row("Field", "Explains what this field changes.", edit, nested);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(row);
        AutoFree(row);

        AssertThat(((Label)row.GetChild(0)).TooltipText).IsEqual("Explains what this field changes.");
        AssertThat(edit.TooltipText).IsEqual("Explains what this field changes.");
        AssertThat(inner.TooltipText).IsEqual("Explains what this field changes.");
    }

    [TestCase]
    public void ExistingTooltipsAreNotOverwritten()
    {
        var edit = EditorFields.Line("", _ => { }, "", 0, "The field's own detailed text.");
        var row = EditorFields.Row("Field", "Row text.", edit);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(row);
        AutoFree(row);

        AssertThat(edit.TooltipText).IsEqual("The field's own detailed text.");
    }
}
