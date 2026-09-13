using UnityEngine;
namespace MadeInArizona
{
    /// <summary>Releases runtime mesh allocations when a mission is unloaded.</summary>
    public sealed class GeneratedMeshOwner : MonoBehaviour
    {
        public Mesh Mesh;
        void OnDestroy() { if (Mesh) Destroy(Mesh); }
    }
}
