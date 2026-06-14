using EchoSpace.Core;
using UnityEngine;

namespace EchoSpace.Annotation
{
    public class MemoryAnchor : MonoBehaviour
    {
        public MemoryAnchorData Data { get; private set; }

        public void Initialize(MemoryAnchorData data)
        {
            if (data == null)
            {
                Debug.LogError("[MemoryAnchor] Initialize called with null MemoryAnchorData.");
                return;
            }

            if (string.IsNullOrEmpty(data.id))
            {
                Debug.LogWarning("[MemoryAnchor] MemoryAnchorData.id is empty; GameObject name may be incomplete.");
            }

            Data = data;
            transform.position = data.worldPosition;
            gameObject.name = "MemoryAnchor_" + (data.id ?? string.Empty);
        }

        public void UpdateVisualLabel()
        {
            if (Data == null)
            {
                Debug.LogWarning("[MemoryAnchor] UpdateVisualLabel called but Data is null.");
                return;
            }

            Debug.Log(
                "[MemoryAnchor] UpdateVisualLabel (placeholder) id="
                + Data.id
                + ", memoryType="
                + Data.memoryType);
        }
    }
}
