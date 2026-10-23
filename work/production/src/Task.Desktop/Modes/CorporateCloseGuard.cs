namespace Task.Desktop.Modes;

/// <summary>Read-only exit policy. Discard is committed only by closing the context.</summary>
internal static class CorporateCloseGuard
{
    internal static bool CanClose(Func<IReadOnlyList<ModeSwitchEditor>> inspect,
        bool alreadyApproved, Func<IReadOnlyList<string>, bool> confirm, Action operationPending)
    {
        bool Pending() => ModeSwitchGuard.HasPendingOperations(inspect());
        if (Pending()) { operationPending(); return false; }
        var names = inspect().Where(editor => editor.HasState()).Select(editor => editor.Name).ToArray();
        if (!alreadyApproved && names.Length > 0 && !confirm(names)) return false;
        // A modal confirmation pumps the dispatcher: an operation may have started meanwhile.
        if (Pending()) { operationPending(); return false; }
        return true;
    }
}
