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

        internal static MVoltProfile SelectAutomaticProfile(ProfileDocument document, bool startupTaskInvocation)
        {
            if (!startupTaskInvocation || document == null || !document.StartupEnabled || string.IsNullOrEmpty(document.StartupProfileId))
                return null;
            for (int index = 0; index < document.Profiles.Count; index++)
                if (document.Profiles[index].Id == document.StartupProfileId) return document.Profiles[index];
            return null;
        }
    }
}
