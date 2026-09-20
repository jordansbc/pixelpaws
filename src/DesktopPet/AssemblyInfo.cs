using System.Runtime.CompilerServices;
using System.Windows;

// The test project exercises internals such as the Win32 notification-state mapping, which
// shouldn't become public API just to be testable.
[assembly: InternalsVisibleTo("DesktopPet.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
