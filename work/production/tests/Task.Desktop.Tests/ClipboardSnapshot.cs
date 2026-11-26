using System.IO;
using System.Windows;

namespace Task.Desktop.Tests;

// The WPF probes share the interactive clipboard. Materialize all native formats before replacing it.
internal sealed class ClipboardSnapshot : IDisposable
{
    private readonly DataObject _original = new();
    private readonly bool _empty;

    public ClipboardSnapshot()
    {
        var current = Clipboard.GetDataObject();
        var formats = current?.GetFormats(false) ?? [];
        _empty = formats.Length == 0;
        foreach (var format in formats)
        {
            var value = current!.GetData(format, false) ?? throw new InvalidOperationException("Clipboard format cannot be preserved.");
            if (value is MemoryStream stream) value = new MemoryStream(stream.ToArray());
            _original.SetData(format, value, false);
        }
    }

    public void Dispose()
    {
        if (_empty) Clipboard.Clear();
        else Clipboard.SetDataObject(_original, true);
    }
}
