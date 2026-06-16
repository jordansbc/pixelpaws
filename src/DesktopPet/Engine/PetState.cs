namespace DesktopPet.Engine;

public enum PetState
{
    Idle,
    Walk,
    Sleep,
    Fall,
    Drag,
    Chase,
    Eat,
    Pet,         // being petted (mouse hover)
    Stretch,     // scheduled stretch reminder
    Typing,      // user is typing normally
    TypingFast,  // user is typing very fast (red heat mode)
    Play,        // unrolling toilet paper while the user scrolls
    Zoomies,     // random burst of fast running across the screen
    Bat,         // swatting at the cursor when it rests nearby
    Hunt,        // crouched, stalking a fast-moving cursor
    Pounce,      // leaping at the cursor
    Proud,       // "caught it!" after a pounce
    Groom,       // licking a paw / washing
    Yawn,        // big yawn (precedes sleep)
    Loaf,        // bread-loaf rest
    Gift,        // carrying a gift to the centre of the screen
    Knockoff,    // pawing a pebble off a window edge
    SideRest,    // lazy lying-on-side rest
    Wakeup,      // brief stretch-awake when leaving a nap
    Spin,        // playful in-place tail-chase spin
    Jump         // playful little hop in place
}
