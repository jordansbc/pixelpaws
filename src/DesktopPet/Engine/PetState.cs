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
    Play         // unrolling toilet paper while the user scrolls
}
