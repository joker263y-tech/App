// =============================================================================
// SHIM - NOT PART OF THE GAME
// =============================================================================
//
// The new Input System ships as a Unity package with no NuGet reference
// assembly, so there is nothing for the compiler to check the Unity layer
// against. This file supplies just enough of its public surface for
// PlayerInputDriver and HudController to be type-checked.
//
// These types are declared to match the package's documented API: the device
// singletons, the control hierarchy, and the members the game actually reads.
// The bodies are deliberately inert - nothing here runs. This project produces a
// type-check, never a playable assembly.
//
// It lives outside Assets/ on purpose. If it were inside, Unity would compile it
// alongside the real Input System and the game would fail to build.
//
// If you change how input is read, update this file to match. A mismatch shows up
// as a compile error here rather than as a surprise in the editor.
//
// =============================================================================

using UnityEngine;

namespace UnityEngine.InputSystem
{
    /// <summary>Base of every control in the Input System's hierarchy.</summary>
    public class InputControl
    {
        public string name { get { return string.Empty; } }

        public string displayName { get { return string.Empty; } }
    }

    /// <summary>A control that reads a value of type <typeparamref name="TValue"/>.</summary>
    public class InputControl<TValue> : InputControl
    {
        public TValue ReadValue()
        {
            return default;
        }

        public TValue value
        {
            get { return default; }
        }
    }

    /// <summary>Base of every device, such as the keyboard or a touchscreen.</summary>
    public class InputDevice : InputControl
    {
        public int deviceId { get { return 0; } }

        public bool enabled { get { return true; } }

        public void MakeCurrent()
        {
        }
    }

    public class AxisControl : InputControl<float>
    {
    }

    /// <summary>A control with a pressed state. Every key and button is one of these.</summary>
    public class ButtonControl : AxisControl
    {
        public bool isPressed { get { return false; } }

        public bool wasPressedThisFrame { get { return false; } }

        public bool wasReleasedThisFrame { get { return false; } }
    }

    public class KeyControl : ButtonControl
    {
    }

    public class Vector2Control : InputControl<Vector2>
    {
    }

    public class IntegerControl : InputControl<int>
    {
    }

    /// <summary>The physical keyboard, if one is attached. Null on a phone.</summary>
    public class Keyboard : InputDevice
    {
        public static Keyboard current { get { return null; } }

        // Every key the game binds. Only the ones actually read are declared;
        // adding a binding means adding it here too.
        public KeyControl aKey { get { return null; } }

        public KeyControl dKey { get { return null; } }

        public KeyControl wKey { get { return null; } }

        public KeyControl sKey { get { return null; } }

        public KeyControl qKey { get { return null; } }

        public KeyControl eKey { get { return null; } }

        public KeyControl spaceKey { get { return null; } }

        public KeyControl escapeKey { get { return null; } }

        public KeyControl tabKey { get { return null; } }

        public KeyControl leftArrowKey { get { return null; } }

        public KeyControl rightArrowKey { get { return null; } }

        public KeyControl upArrowKey { get { return null; } }

        public KeyControl downArrowKey { get { return null; } }

        public KeyControl digit1Key { get { return null; } }

        public KeyControl digit2Key { get { return null; } }

        public KeyControl digit3Key { get { return null; } }

        public KeyControl digit4Key { get { return null; } }

        public KeyControl digit5Key { get { return null; } }
    }

    /// <summary>A pointer's relative movement and absolute position.</summary>
    public class Pointer : InputDevice
    {
        public Vector2Control position { get { return null; } }

        public Vector2Control delta { get { return null; } }
    }

    public class Mouse : Pointer
    {
        public static Mouse current { get { return null; } }

        public ButtonControl leftButton { get { return null; } }

        public ButtonControl rightButton { get { return null; } }

        public ButtonControl middleButton { get { return null; } }
    }

    /// <summary>One finger in contact with the screen.</summary>
    public class TouchControl : InputControl
    {
        public IntegerControl touchId { get { return null; } }

        public Vector2Control position { get { return null; } }

        public Vector2Control delta { get { return null; } }

        public ButtonControl press { get { return null; } }

        public ButtonControl phase { get { return null; } }
    }

    /// <summary>The touchscreen, if one is present. Null in the Editor on desktop.</summary>
    public class Touchscreen : Pointer
    {
        public static Touchscreen current { get { return null; } }

        public Utilities.ReadOnlyArray<TouchControl> touches
        {
            get { return default; }
        }

        public TouchControl primaryTouch { get { return null; } }
    }
}

namespace UnityEngine.InputSystem.Utilities
{
    /// <summary>
    /// A fixed view over a device's controls. An array with a Count and an indexer
    /// rather than a List, so reading it per frame allocates nothing.
    /// </summary>
    public struct ReadOnlyArray<TValue>
    {
        public int Count
        {
            get { return 0; }
        }

        public TValue this[int index]
        {
            get { return default; }
        }
    }
}
