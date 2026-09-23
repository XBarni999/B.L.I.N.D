using UnityEngine;

namespace Torpedo
{
    // A component copied with the torpedo prefab. Runtime patches use this marker instead
    // of guessing from a mutable definition/name, so vanilla missiles are never affected.
    internal sealed class TorpedoIdentity : MonoBehaviour
    {
    }
}
