using System;
using System.Collections.Generic;
using Gallop.Live.Cutt;
using UnityEngine;

namespace Gallop.Live
{
    // Loaded-model part of LiveModelController.UpdateCachedTransforms,
    // Reset/SetMicStandNodeParam and MicStandNodePositionProcess.
    public sealed class LiveMicStandRuntime
    {

        public sealed class PositionProcess
        {
            public const int UpdateTiming = 5; // PreLateUpdate, before IK1.
            public bool Enabled = true;
            public bool Paused;
            public Vector3 OffsetHigh, OffsetLow;
            public readonly Transform Node;
            internal PositionProcess(Transform node)
            {
                Node = node;
            }

            // Original callback 0x585e600 has no enable/pause gate.
            public void Apply(float bodyScale)
            {
                Vector3 offset;
                float amount;
                if (bodyScale > 1f)
                {
                    amount = F(bodyScale - 1f);
                    offset = OffsetHigh;
                }
                else if (bodyScale < 1f)
                {
                    amount = F(1f - bodyScale);
                    offset = OffsetLow;
                }
                else
                    return;
                var position = Node.localPosition;
                var result = new Vector3(F(F(offset.x * amount) + position.x), F(F(offset.y * amount) + position.y), F(F(offset.z * amount) + position.z));

                Node.localPosition = result;
            }

            public void Tick(float bodyScale)
            {
                // ExecComponentUpdate checks IsEnable. ProcessExecComponent's
                // PreLateUpdate does not inspect its stored IsPause flag.
                if (Enabled)
                    Apply(bodyScale);
            }
        }

        public readonly Transform Attach, AttachLocator;
        public readonly PositionProcess Left, Right;
        readonly Func<float> bodyScale;
        static float F(float value) => LiveTimelineControl.MonitorFloat32(value);
        public LiveMicStandRuntime(IDictionary<string, Transform> cache, Transform bodyTransform, Transform positionNode, Func<float> bodyScaleProvider)
        {
            bodyScale = bodyScaleProvider;
            Transform FindOrCreate(string name, Transform parent)
            {
                if (cache.TryGetValue(name, out var existing) && existing != null)
                    return existing;
                var node = new GameObject(name).transform;
                node.SetParent(parent, false);
                node.localPosition = Vector3.zero;
                node.localRotation = Quaternion.identity;
                node.localScale = Vector3.one;
                if (!cache.ContainsKey(name))
                    cache.Add(name, node);
                return node;
            }

            Attach = FindOrCreate("Mic_Attach_00", bodyTransform);
            AttachLocator = FindOrCreate("Mic_Attach_00_loc", positionNode);
            Left = new PositionProcess(FindOrCreate("Mic_Node_L", AttachLocator));
            Right = new PositionProcess(FindOrCreate("Mic_Node_R", AttachLocator));
        }

        public void ResetOffsets()
        {
            Left.Enabled = false;
            Left.OffsetHigh = Left.OffsetLow = Vector3.zero;
            Right.Enabled = false;
            Right.OffsetHigh = Right.OffsetLow = Vector3.zero;
        }

        public void SetOffsets(bool left, Vector3 leftHigh, Vector3 leftLow, bool right, Vector3 rightHigh, Vector3 rightLow)
        {
            Left.Enabled = left;
            Left.OffsetHigh = leftHigh;
            Left.OffsetLow = leftLow;
            Right.Enabled = right;
            Right.OffsetHigh = rightHigh;
            Right.OffsetLow = rightLow;
        }

        public void PreLateUpdate()
        {
            float scale = bodyScale();
            Left.Tick(scale);
            Right.Tick(scale);
        }
    }
}
