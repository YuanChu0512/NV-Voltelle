namespace MVolt.Rebuild
{
    internal static class StartupProfilePolicy
    {
        // Saved profiles are inert at process startup. The first hardware operation is
        // always a GET performed by MainWindow.RefreshSnapshot. A profile may become
        // pending only after an explicit user action in the Profiles page.
        internal static MVoltProfile SelectAutomaticProfile(ProfileDocument document)
        {
            return null;
        }
    }
}
