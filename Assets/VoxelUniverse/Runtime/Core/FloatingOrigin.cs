using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Core
{
    [DisallowMultipleComponent]
    public sealed class FloatingOrigin : MonoBehaviour
    {
        [SerializeField] private Transform observer;
        [SerializeField, Min(100f)] private float shiftThreshold = 5000f;
        [SerializeField, Min(1f)] private float shiftQuantum = 256f;
        [SerializeField] private List<Transform> renderRoots = new List<Transform>();

        private Double3 universeOrigin;

        public Double3 UniverseOrigin { get { return universeOrigin; } }
        public event Action<Double3, Vector3> OriginShifted;

        public void Configure(Transform newObserver, IEnumerable<Transform> newRenderRoots)
        {
            observer = newObserver;
            renderRoots.Clear();
            if (newRenderRoots == null) return;
            foreach (Transform root in newRenderRoots) RegisterRoot(root);
        }

        public void RegisterRoot(Transform root)
        {
            if (root != null && !renderRoots.Contains(root)) renderRoots.Add(root);
        }

        public void UnregisterRoot(Transform root)
        {
            renderRoots.Remove(root);
        }

        public Vector3 UniverseToRender(Double3 universePosition)
        {
            return universePosition.ToVector3Relative(universeOrigin);
        }

        public Double3 RenderToUniverse(Vector3 renderPosition)
        {
            return universeOrigin + Double3.FromVector3(renderPosition);
        }

        private void LateUpdate()
        {
            if (observer == null) return;
            Vector3 local = observer.position;
            if (local.sqrMagnitude < shiftThreshold * shiftThreshold) return;

            Vector3 shift = Quantize(local, shiftQuantum);
            if (shift.sqrMagnitude < 0.0001f) return;

            for (int i = renderRoots.Count - 1; i >= 0; i--)
            {
                Transform root = renderRoots[i];
                if (root == null)
                {
                    renderRoots.RemoveAt(i);
                    continue;
                }
                root.position -= shift;
            }

            universeOrigin += Double3.FromVector3(shift);
            Action<Double3, Vector3> handler = OriginShifted;
            if (handler != null) handler(universeOrigin, shift);
        }

        private static Vector3 Quantize(Vector3 value, float quantum)
        {
            return new Vector3(Mathf.Round(value.x / quantum) * quantum, Mathf.Round(value.y / quantum) * quantum, Mathf.Round(value.z / quantum) * quantum);
        }
    }
}
