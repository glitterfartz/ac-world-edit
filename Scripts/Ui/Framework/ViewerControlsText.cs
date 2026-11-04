namespace ACWorldEdit;

/// <summary>The Help → Controls text, kept out of the menu wiring so it can be reviewed on its own.</summary>
public static class ViewerControlsText
{
    public const string Text = """
        Camera
          Right mouse (hold)    look / fly
          W A S D               move;  Q / E  down / up
          Shift                 faster;  Wheel  flight speed
          Esc                   release mouse

        Editing (selected creature)
          Q / W / E             Select / Move / Rotate tool
          Drag colored axis     move along that axis
          Drag yellow ring      rotate heading (hold Ctrl for 15° steps)
          End                   drop to ground
          Ctrl+Z / Ctrl+Y       undo / redo (also Ctrl+Shift+Z)
          F                     fly to selection
          Right-click (no drag) a creature to see its gossip, quests, vendor and trainer
          Esc during a drag     cancel
        """;
}
