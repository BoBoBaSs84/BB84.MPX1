using BB84.MPX1.App.Services;

namespace BB84.MPX1.App.ViewModels;

/// <summary>A part of the workbench that follows the live connection: it is attached once a database is available and detached on disconnect.</summary>
public interface IDeviceTab
{
    /// <summary>Connects to a connection whose learned database, editor, librarian, dumps and front panel are available.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="time">Time source for throttling.</param>
    void Attach(DeviceConnection connection, TimeProvider time);

    /// <summary>Disconnects and drops everything read from the device.</summary>
    void Detach();
}
