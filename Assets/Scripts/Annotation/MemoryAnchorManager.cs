using System.Collections.Generic;
using EchoSpace.Core;
using UnityEngine;

namespace EchoSpace.Annotation
{
    public class MemoryAnchorManager : MonoBehaviour
    {
        public GameObject memoryAnchorPrefab;
        public bool enableDebugHotkey = true;
        public KeyCode debugKey = KeyCode.D;

        private readonly List<MemoryAnchorData> anchors = new List<MemoryAnchorData>();

        private void Update()
        {
            if (enableDebugHotkey && Input.GetKeyDown(debugKey))
            {
                DebugPrintAllAnchors();
            }
        }

        public MemoryAnchor CreateAnchor(Vector3 position)
        {
            if (memoryAnchorPrefab == null)
            {
                Debug.LogError("[MemoryAnchorManager] memoryAnchorPrefab is not assigned.");
                return null;
            }

            var data = MemoryAnchorData.CreateNewAt(position);
            data.isAnnotated = false;

            var instance = Instantiate(memoryAnchorPrefab, position, Quaternion.identity, transform);
            if (instance == null)
            {
                Debug.LogError("[MemoryAnchorManager] Instantiate returned null.");
                return null;
            }

            var anchor = instance.GetComponent<MemoryAnchor>();
            if (anchor != null)
            {
                anchor.Initialize(data);
            }
            else
            {
                Debug.LogWarning("[MemoryAnchorManager] memoryAnchorPrefab is missing MemoryAnchor component; anchor GameObject was created but not initialized.");
            }

            anchors.Add(data);
            return anchor;
        }

        public List<MemoryAnchorData> GetAllAnchorData()
        {
            var result = new List<MemoryAnchorData>(anchors.Count);
            foreach (var data in anchors)
            {
                if (data == null)
                {
                    Debug.LogWarning("[MemoryAnchorManager] anchors contained a null MemoryAnchorData; skipped.");
                    continue;
                }

                result.Add(data.Clone());
            }

            return result;
        }

        /// <summary>
        /// 回傳管理中的錨點資料參考（非 Clone），供 Line of Life 等需要寫回 timeCategory 的流程使用。
        /// </summary>
        public List<MemoryAnchorData> GetManagedAnchorData()
        {
            var result = new List<MemoryAnchorData>(anchors.Count);
            foreach (var data in anchors)
            {
                if (data != null)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        public void RemoveAnchor(MemoryAnchor anchor)
        {
            if (anchor == null)
            {
                return;
            }

            MemoryAnchorData data = anchor.Data;
            if (data != null)
            {
                anchors.Remove(data);
            }

            string id = data != null ? data.id : "(no id)";
            Destroy(anchor.gameObject);
            Debug.Log("[MemoryAnchorManager] Removed anchor id=" + id);
        }

        public List<MemoryAnchorData> GetAnnotatedAnchorData()
        {
            var result = new List<MemoryAnchorData>();
            foreach (var data in anchors)
            {
                if (data != null && data.isAnnotated)
                {
                    result.Add(data);
                }
            }

            return result;
        }

        public MemoryAnchorData FindAnchorById(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (var data in anchors)
            {
                if (data != null && data.id == id)
                {
                    return data;
                }
            }

            return null;
        }

        public bool SetTimeCategory(string id, TimeCategory category)
        {
            var data = FindAnchorById(id);
            if (data == null)
            {
                return false;
            }

            data.timeCategory = category;
            return true;
        }

        public void DebugPrintAllAnchors()
        {
            if (anchors == null || anchors.Count == 0)
            {
                Debug.Log("[MemoryAnchorManager] No memory anchors found.");
                return;
            }

            Debug.Log("[MemoryAnchorManager] ===== Memory Anchor Dump =====");
            Debug.Log("[MemoryAnchorManager] Anchor Count: " + anchors.Count);

            for (int i = 0; i < anchors.Count; i++)
            {
                var data = anchors[i];
                if (data == null)
                {
                    Debug.Log("[MemoryAnchorManager] Anchor " + (i + 1) + " — (null data)");
                    continue;
                }

                Debug.Log(
                $"[MemoryAnchorManager] Anchor {i + 1} | " +
                $"id={data.id} | " +
                $"locationName={data.locationName} | " +
                $"worldPosition={data.worldPosition} | " +
                $"memoryType={data.memoryType} | " +
                $"promptQuestion={data.promptQuestion} | " +
                $"userText={data.userText} | " +
                $"timeCategory={data.timeCategory} | " +
                $"isAnnotated={data.isAnnotated}"
                );
                /*Debug.Log("id=" + data.id);
                Debug.Log("locationName=" + data.locationName);
                Debug.Log("worldPosition=" + data.worldPosition);
                Debug.Log("memoryType=" + data.memoryType);
                Debug.Log("promptQuestion=" + data.promptQuestion);
                Debug.Log("userText=" + data.userText);
                Debug.Log("timeCategory=" + data.timeCategory);*/
            }
        }
    }
}
