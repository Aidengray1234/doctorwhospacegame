using UnityEngine;

namespace DoctorWhoSpaceGame.Planets
{
    internal static class DropboxBridgeTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ConfirmCompilation()
        {
            Debug.Log("[Unity GPT Dropbox Bridge] Test script compiled successfully.");
        }
    }
}
