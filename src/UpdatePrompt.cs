using System;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MoonTrainer
{
    /// <summary>
    /// Tells the player about a new version using the game's own message box at the main menu, and installs it if they
    /// choose UPDATE NOW. Nothing is installed without that click. In a game there's no message box to borrow, so the
    /// notice line says it and the trainer menu's Home tab has the button.
    /// </summary>
    public static class UpdatePrompt
    {
        static MoonMainMenu _menu;
        static float _menuSeenAt, _nextLook;
        static bool _offered, _doneShown, _failShown, _startRequested, _ours, _broken;
        static int _lastState;
        static string _shownStatus;

        /// <summary>Called every frame.</summary>
        public static void Tick()
        {
            int state = Updates.State;
            if (state != _lastState)
            {
                _lastState = state;
                // (a failure that left the new DLL behind has to say so here too, not only in the message box)
                if (Game.InGame && (state == Updates.Done || state == Updates.Failed))
                    Trainer.Notice(state == Updates.Failed && Updates.HalfInstalled != null ? Updates.Status + " " + Updates.Aftermath : Updates.Status);
            }
            if (_broken || Updates.Available == null || Game.InGame) return;

            bool something = _startRequested
                || (state == Updates.Idle && !_offered)
                || (state == Updates.Working && _ours)
                || (state == Updates.Done && !_doneShown)
                || (state == Updates.Failed && !_failShown);
            if (!something) return;

            try
            {
                var menu = Menu();
                if (menu == null) return;
                if (Time.unscaledTime - _menuSeenAt < 4f) return;          // let the intro and logo animations finish
                var dialog = menu.dialog;
                if (dialog == null) return;
                bool open = dialog.Open;
                if (!open) _ours = false;
                if (open && !_ours) return;                                // someone else's message box is up: wait for it
                if (!open && (menu.current != MoonMainMenu.Panel.None || menu.home == null || !menu.home.gameObject.activeInHierarchy))
                    return;                                                // they're in a sub-screen (saves, settings, mods)

                if (_startRequested)
                {
                    // UPDATE NOW was pressed on the previous frame.
                    _startRequested = false;
                    Updates.Reset();
                    Updates.InstallInBackground();
                    _shownStatus = null; _failShown = false;
                    dialog.Show("Updating Moon Trainer", "Starting...", "PLEASE WAIT", Act(() => { }), "HIDE", Act(() => dialog.Hide()));
                    _ours = true;
                }
                else if (state == Updates.Idle)
                {
                    _offered = true;
                    string notes = Updates.Notes;
                    string text = "Version " + Updates.Available + " is ready. You have " + Plugin.Version + "."
                        + (string.IsNullOrEmpty(notes) ? "" : " " + notes)
                        + " Updating takes a few seconds, then the game needs a restart.";
                    dialog.Show("Moon Trainer update", text,
                        "UPDATE NOW", Act(() => { dialog.Hide(); _startRequested = true; }),
                        "LATER", Act(() => { dialog.Hide(); Plugin.Logger.LogInfo("Update postponed. It's also on the Home tab of the trainer menu."); }));
                    _ours = true;
                    Plugin.Logger.LogInfo("Showing the update message at the main menu.");
                }
                else if (state == Updates.Working)
                {
                    // Our "please wait" box is up: keep its text current.
                    string s = Updates.Status;
                    if (open && s != _shownStatus)
                    {
                        _shownStatus = s;
                        var label = dialog.Message;
                        if (label != null) label.text = s;
                    }
                }
                else if (state == Updates.Done)
                {
                    _doneShown = true;
                    dialog.Show("Update installed", "Moon Trainer " + Updates.Installed + " is installed. Restart the game to start using it.",
                        "QUIT GAME", Act(() => { dialog.Hide(); Plugin.Logger.LogInfo("Quitting so the update takes effect."); Application.Quit(); }),
                        "LATER", Act(() => dialog.Hide()));
                    _ours = true;
                }
                else if (state == Updates.Failed)
                {
                    _failShown = true;
                    // Updates.Aftermath is "Nothing was changed." only when that's so; otherwise it says what was.
                    bool half = Updates.HalfInstalled != null;
                    dialog.Show(half ? "Update partly installed" : "Update not installed",
                        Updates.Status + " " + Updates.Aftermath
                            + (half ? " It will still load at the next start. Try again to finish the job, or close the game and use the PowerShell command."
                                : " You can try again, or update with the PowerShell command instead."),
                        "TRY AGAIN", Act(() => { dialog.Hide(); _startRequested = true; }),
                        "CLOSE", Act(() => dialog.Hide()));
                    _ours = true;
                }
            }
            catch (Exception e)
            {
                // The game's message box isn't what we expected (a game update, most likely). The console notice and
                // the button on the trainer menu's Home tab still work.
                _broken = true;
                Plugin.Logger.LogWarning("Couldn't show the update message at the main menu: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static MoonMainMenu Menu()
        {
            if (_menu != null && !_menu.WasCollected && _menu) return _menu;
            _menu = null;
            if (Time.unscaledTime < _nextLook) return null;
            _nextLook = Time.unscaledTime + 1f;
            _menu = Game.Find<MoonMainMenu>();
            if (_menu != null) _menuSeenAt = Time.unscaledTime;
            return _menu;
        }

        static Il2CppSystem.Action Act(Action a) { return DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(a); }
    }
}
