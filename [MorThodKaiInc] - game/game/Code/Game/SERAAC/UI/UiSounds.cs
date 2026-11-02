using SERAAC.Core;

namespace SERAAC.UI
{
    // The UI's sounds, for every screen at once (ScreenManager calls this after the top screen's Update):
    //   ui_move    the arrow keys / WASD, or the mouse moving onto another clickable thing
    //   ui_select  Enter / Space / Z, or a click on something clickable
    //   ui_back    Esc / Backspace / X / right click
    // Screens choose where it applies (Screen.MenuKeySounds, Screen.UiSoundsFor): exploration, for one,
    // walks with the arrow keys and the mouse, so it only gets them while a menu is open.
    public static class UiSounds
    {
        // Big clickable areas (whole panels, the room floor) don't tick when the mouse crosses them.
        private const int MaxTickArea = 600 * 300;

        private static string _hoverKey;
        private static int _hoverIndex = -1;

        public static void AfterUpdate(Screen screen)
        {
            if (screen == null) return;
            bool keys = screen.MenuKeySounds;

            // What the mouse is over now (last frame's clickable regions).
            var under = Ui.Under();
            bool clickable = under is { } u && u.rect.Width * u.rect.Height <= MaxTickArea && screen.UiSoundsFor(u.key);

            if (keys && Input.Cancel || Input.RightClick && clickable)
                Audio.Play("ui_back");
            else if (keys && Input.Confirm || Input.Click && clickable)
                Audio.Play("ui_select");
            else if (keys && (Input.Up || Input.DownKey || Input.Left || Input.Right))
                Audio.Play("ui_move");
            else if (Input.MouseMoved && clickable && (under.Value.key != _hoverKey || under.Value.index != _hoverIndex))
                Audio.Play("ui_move");

            if (clickable) (_hoverKey, _hoverIndex) = (under.Value.key, under.Value.index);
            else (_hoverKey, _hoverIndex) = (null, -1);
        }
    }
}
