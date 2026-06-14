using System.Collections.Generic;
using UnityEngine;

namespace EchoSpace.LineOfLife
{
    /// <summary>
    /// Draws a Catmull-Rom curve through Past / Transition / Present nodes in local space,
    /// with optional extension beyond Present and an arrow head LineRenderer.
    /// </summary>
    public class TimelineCurveRenderer : MonoBehaviour
    {
        public LineRenderer lineRenderer;
        public Transform pastNode;
        public Transform transitionNode;
        public Transform presentNode;
        public Vector3 pastOuterControlOffset = new Vector3(-0.35f, 0f, -0.55f);
        public Vector3 transitionControlOffset = Vector3.zero;
        public Vector3 presentOuterControlOffset = new Vector3(0.35f, 0f, -0.55f);
        public int segmentCount = 96;
        public float lineWidth = 0.035f;
        public bool renderOnStart = true;

        [Header("Future Extension")]
        public bool extendBeyondPresent = true;
        public float futureExtensionLength = 0.25f;

        [Header("Arrow Head")]
        public LineRenderer arrowLineRenderer;
        public bool showArrowHead = true;
        public float arrowLength = 0.12f;
        public float arrowWidth = 0.08f;

        private bool _warnedMissingArrowLineRenderer;

        private void Start()
        {
            if (renderOnStart)
            {
                RenderCurve();
            }
        }

        public void RenderCurve()
        {
            Debug.Log("[TimelineCurveRenderer] RenderCurve called.");

            if (lineRenderer == null)
            {
                Debug.LogError("[TimelineCurveRenderer] lineRenderer is not assigned.");
                return;
            }

            if (pastNode == null)
            {
                Debug.LogError("[TimelineCurveRenderer] pastNode is not assigned.");
                return;
            }

            if (transitionNode == null)
            {
                Debug.LogError("[TimelineCurveRenderer] transitionNode is not assigned.");
                return;
            }

            if (presentNode == null)
            {
                Debug.LogError("[TimelineCurveRenderer] presentNode is not assigned.");
                return;
            }

            lineRenderer.useWorldSpace = false;
            lineRenderer.enabled = true;
            lineRenderer.numCornerVertices = 8;
            lineRenderer.numCapVertices = 8;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            segmentCount = Mathf.Max(segmentCount, 4);

            Vector3 past = pastNode.localPosition;
            Vector3 transition = transitionNode.localPosition + transitionControlOffset;
            Vector3 present = presentNode.localPosition;

            List<Vector3> controlPoints = new List<Vector3>(5);
            controlPoints.Add(past + pastOuterControlOffset);
            controlPoints.Add(past);
            controlPoints.Add(transition);
            controlPoints.Add(present);
            controlPoints.Add(present + presentOuterControlOffset);

            List<Vector3> curvePoints = new List<Vector3>(segmentCount + 1);

            int firstHalfCount = Mathf.Max(2, segmentCount / 2 + 1);
            int secondHalfCount = Mathf.Max(2, segmentCount - firstHalfCount + 2);

            for (int i = 0; i < firstHalfCount; i++)
            {
                float t = firstHalfCount > 1 ? i / (firstHalfCount - 1f) : 0f;
                curvePoints.Add(CatmullRom(controlPoints[0], controlPoints[1], controlPoints[2], controlPoints[3], t));
            }

            for (int i = 1; i < secondHalfCount; i++)
            {
                float t = secondHalfCount > 1 ? i / (secondHalfCount - 1f) : 0f;
                curvePoints.Add(CatmullRom(controlPoints[1], controlPoints[2], controlPoints[3], controlPoints[4], t));
            }

            if (extendBeyondPresent && curvePoints.Count >= 2)
            {
                Vector3 lastPoint = curvePoints[curvePoints.Count - 1];
                Vector3 prevPoint = curvePoints[curvePoints.Count - 2];
                Vector3 dir = lastPoint - prevPoint;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Vector3 futureEnd = lastPoint + dir.normalized * futureExtensionLength;
                    curvePoints.Add(futureEnd);
                }
            }

            lineRenderer.positionCount = curvePoints.Count;
            for (int i = 0; i < curvePoints.Count; i++)
            {
                lineRenderer.SetPosition(i, curvePoints[i]);
            }

            RenderArrowHead(curvePoints);

            Debug.Log(
                $"[TimelineCurveRenderer] Extended beyond Present={extendBeyondPresent}, futureExtensionLength={futureExtensionLength}, showArrowHead={showArrowHead}");
        }

        private void RenderArrowHead(List<Vector3> curvePoints)
        {
            if (!showArrowHead)
            {
                if (arrowLineRenderer != null)
                {
                    arrowLineRenderer.enabled = false;
                }

                return;
            }

            if (arrowLineRenderer == null)
            {
                if (!_warnedMissingArrowLineRenderer)
                {
                    Debug.LogWarning("[TimelineCurveRenderer] arrowLineRenderer is not assigned.");
                    _warnedMissingArrowLineRenderer = true;
                }

                return;
            }

            if (curvePoints == null || curvePoints.Count < 2)
            {
                arrowLineRenderer.enabled = false;
                return;
            }

            Vector3 tip = curvePoints[curvePoints.Count - 1];
            Vector3 beforeTip = curvePoints[curvePoints.Count - 2];
            Vector3 dir = tip - beforeTip;
            if (dir.sqrMagnitude < 0.0001f)
            {
                arrowLineRenderer.enabled = false;
                return;
            }

            dir.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 0.0001f)
            {
                side = Vector3.right;
            }
            else
            {
                side.Normalize();
            }

            Vector3 baseCenter = tip - dir * arrowLength;
            Vector3 leftWing = baseCenter + side * arrowWidth;
            Vector3 rightWing = baseCenter - side * arrowWidth;

            arrowLineRenderer.useWorldSpace = false;
            arrowLineRenderer.enabled = true;
            arrowLineRenderer.positionCount = 3;
            arrowLineRenderer.SetPosition(0, leftWing);
            arrowLineRenderer.SetPosition(1, tip);
            arrowLineRenderer.SetPosition(2, rightWing);
            arrowLineRenderer.startWidth = lineWidth;
            arrowLineRenderer.endWidth = lineWidth;
            arrowLineRenderer.numCornerVertices = 4;
            arrowLineRenderer.numCapVertices = 4;
        }

        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float tt = t * t;
            float ttt = tt * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * tt +
                (-p0 + 3f * p1 - 3f * p2 + p3) * ttt
            );
        }
    }
}
