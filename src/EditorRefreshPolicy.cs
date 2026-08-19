using System;
using System.Collections.Generic;

namespace MVolt.Rebuild
{
    internal static class EditorRefreshPolicy
    {
        internal static void Apply(
            bool resetEntireEditor,
            ref bool voltageDraftInitialized,
            ref MVoltProfile pendingProfile,
            IList<VfOffsetChange> stagedVfChanges)
        {
            if (stagedVfChanges == null) throw new ArgumentNullException("stagedVfChanges");
            if (!resetEntireEditor) return;
            pendingProfile = null;
            stagedVfChanges.Clear();
            voltageDraftInitialized = false;
        }
    }
}
