using System;
using System.Collections.Generic;
using System.Linq;
using Gallop;
using UnityEngine;

internal sealed class GachaTransformExpressions
{
    private readonly List<(Transform Target, Action Apply, Action Restore)> expressions = new();
    private bool applied;

    public int Count => expressions.Count;
    public bool Applied => applied;
#if UNITY_EDITOR
    internal IEnumerable<Transform> Targets => expressions.Select(expression => expression.Target).Distinct();
#endif

    public GachaTransformExpressions(UmaContainerCharacter actor, CharaTransformProcessData data, Transform model)
    {
        if (!model || data == null || data.TargetDataArray == null)
            return;
        var nodes = model.GetComponentsInChildren<Transform>(true).ToLookup(node => node.name);
        var rig = actor.GetComponentsInChildren<Transform>(true).ToLookup(node => node.name);
        foreach (var item in data.TargetDataArray)
        {
            if (string.IsNullOrEmpty(item.TargetTransformName))
                continue;
            var target = nodes[item.TargetTransformName].FirstOrDefault() ?? rig[item.TargetTransformName].FirstOrDefault();
            if (!target)
                continue;
            var initial = InitialPose(target);
            if (item.ProcessId == CharaTransformProcessData.ProcessType.InverseYRotation_2Hierarchy)
            {
                var parent = target.parent;
                var root = parent ? parent.parent : null;
                if (!root)
                    continue;
                var parentInverse = Quaternion.Inverse(InitialPose(parent).Rotation);
                var rootInverse = Quaternion.Inverse(InitialPose(root).Rotation);
                expressions.Add((target,
                    () => target.localRotation = Quaternion.Euler(0f,
                        -(YTwist(rootInverse * root.localRotation) + YTwist(parentInverse * parent.localRotation)), 0f),
                    () => target.localRotation = initial.Rotation));
            }
            else if (item.ProcessId == CharaTransformProcessData.ProcessType.AddPosition_FromAngle &&
                item.ValueParameterArray is { Length: 7 } values && item.StringParameterArray is { Length: 1 } strings)
            {
                var reference = nodes[strings[0]].FirstOrDefault() ?? rig[strings[0]].FirstOrDefault();
                if (!reference || values[0] < 0 || values[0] > 2 || values[3] < 0 || values[3] > 2)
                    continue;
                var referenceInverse = Quaternion.Inverse(InitialPose(reference).Rotation);
                const float units = .0001f;
                int axis = values[0], type = values[3];
                float min = values[1] * units, max = values[2] * units;
                var delta = new Vector3(values[4], values[5], values[6]) * units;
                expressions.Add((target, () =>
                {
                    float degrees = (referenceInverse * reference.localRotation).eulerAngles[axis];
                    if (degrees >= 180f) degrees -= 360f;
                    var offset = delta * (Mathf.Abs(min - max) <= .000001f ? 0f : Mathf.Clamp01((degrees - min) / (max - min)));
                    if (type == 0) target.localPosition = initial.Position + offset;
                    else if (type == 1) target.localRotation = initial.Rotation * Quaternion.Euler(offset);
                    else target.localScale = initial.Scale + offset;
                }, () =>
                {
                    if (type == 0) target.localPosition = initial.Position;
                    else if (type == 1) target.localRotation = initial.Rotation;
                    else target.localScale = initial.Scale;
                }));
            }
        }

        (Vector3 Position, Quaternion Rotation, Vector3 Scale) InitialPose(Transform node) =>
            actor.InitBoneTransform != null && actor.InitBoneTransform.TryGetValue(node, out var pose)
                ? pose : (node.localPosition, node.localRotation, node.localScale);
    }

    private static float YTwist(Quaternion rotation) =>
        (Quaternion.FromToRotation(rotation * Vector3.up, Vector3.up) * rotation).eulerAngles.y;

    public void Sample(bool enabled)
    {
        if (enabled)
            foreach (var expression in expressions)
                expression.Apply();
        applied = enabled && Count > 0;
    }

    public void Restore()
    {
        if (!applied)
            return;
        foreach (var expression in expressions)
            expression.Restore();
        applied = false;
    }
}
