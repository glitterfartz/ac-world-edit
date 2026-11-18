using Godot;

namespace ACWorldEdit;

/// <summary>
/// The scripting editor's floating frame: a client-styled panel dragged by its header, resized from
/// the bottom-right grip, and hidden on close. It is deliberately not <see cref="ContentEditorWindow"/>,
/// whose fixed target size belongs to the SQL-draft editors.
/// </summary>
public partial class ScriptWindowFrame : PanelContainer
{
    readonly VBoxContainer body;
    Vector2? drag;
    Vector2? resize;

    public ScriptWindowFrame(string title)
    {
        Name = "ScriptWindow";
        Visible = false;
        Theme = ClientStyle.Theme();
        AddThemeStyleboxOverride("panel", ClientStyle.Panel());
        CustomMinimumSize = new Vector2(760, 480);
        Size = new Vector2(1120, 700);

        var root = new VBoxContainer();
        AddChild(root);

        var header = new HBoxContainer();
        header.GuiInput += Drag;
        root.AddChild(header);
        TitleLabel = ClientStyle.Text(title, 19, ClientStyle.Header);
        TitleLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        TitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(TitleLabel);
        CloseButton = new Button { Text = "✕", TooltipText = "Close (Escape)" };
        CloseButton.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(CloseButton);
        root.AddChild(new HSeparator());

        body = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        root.AddChild(body);

        var grip = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var handle = new Label { Text = "◢", TooltipText = "Drag to resize", MouseFilter = Control.MouseFilterEnum.Stop };
        handle.GuiInput += Resize;
        grip.AddChild(handle);
        root.AddChild(grip);
    }

    public Label TitleLabel { get; }

    public Button CloseButton { get; }

    public VBoxContainer Body => body;

    public event Action? CloseRequested;

    public void Open()
    {
        if (Position == Vector2.Zero)
        {
            Position = new Vector2(
                Math.Max(0, (GetViewportRect().Size.X - Size.X) / 2),
                Math.Max(0, (GetViewportRect().Size.Y - Size.Y) / 2));
        }

        Visible = true;
    }

    public void Close() => Visible = false;

    void Drag(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            drag = button.Pressed ? button.GlobalPosition - GlobalPosition : null;
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && drag.HasValue)
        {
            GlobalPosition = (motion.GlobalPosition - drag.Value)
                .Clamp(Vector2.Zero, (GetViewportRect().Size - Size).Max(Vector2.Zero));
            AcceptEvent();
        }
    }

    void Resize(InputEvent input)
    {
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            resize = button.Pressed ? button.GlobalPosition : null;
            AcceptEvent();
        }
        else if (input is InputEventMouseMotion motion && resize.HasValue)
        {
            Size = (Size + motion.Relative).Max(CustomMinimumSize);
            resize = motion.GlobalPosition;
            AcceptEvent();
        }
    }
}
