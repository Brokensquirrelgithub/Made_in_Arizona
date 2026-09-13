using UnityEngine;
namespace MadeInArizona
{
    public sealed class WorldBillboard : MonoBehaviour
    {
        void LateUpdate() { if (Camera.main) transform.rotation = Camera.main.transform.rotation; }
    }
}
