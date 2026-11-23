using UnityEngine;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Where PC-only test controls may run: the Unity editor and Windows / Mac / Linux builds (any build, release or
    /// Development). Phones and tablets (Android, iOS) never get the mouse-as-finger or the keyboard controls, even in a
    /// Development Build with a keyboard plugged in.
    /// Used by: GestureArbiterInput, TapAnywhereInputHandler, TouchInputHandler (mouse = one finger), KeyboardDebugInput,
    /// ItemTapHandler and ControlSchemeSwitcher (keys). Each has its own Inspector toggle for PC builds.
    /// Place in: Assets/_Project/Scripts/Input/PcInput.cs
    /// </summary>
    public static class PcInput
    {
        /// <summary>A Windows / Mac / Linux build (not the editor, never a phone).</summary>
        public static bool IsPcBuild
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.LinuxPlayer:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>Should the mouse act as one finger (TouchSimulation)? Editor: its own toggle. PC build: the PC toggle.</summary>
        public static bool MouseAsFinger(bool inEditor, bool inPcBuilds) =>
            Application.isEditor ? inEditor : IsPcBuild && inPcBuilds;

        /// <summary>May the keyboard controls run? Always in the editor; in PC builds if the toggle is on; never on phones.</summary>
        public static bool KeyboardAllowed(bool inPcBuilds) => Application.isEditor || (IsPcBuild && inPcBuilds);
    }
}
