using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop.Live.Cutt
{
    public struct TransformBaseData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    public struct ObjectUpdateInfo
    {
        public LiveTimelineObjectData data;
        public TransformBaseData updateData;
        public bool renderEnable;
        public AttachType AttachTarget;
        public int CharacterPosition;
        public int MultiCameraIndex;
        public OffsetType OffsetType;
        public LayerType LayerType;
        public bool IsLayerTypeRecursively;
        public bool IsAttachProps;
        public int PropsIndex;
        public bool UseScreenCapture;
        public int ScreenCaptureId;
        public LiveTimelineKeyObjectData.ScreenCaptureAspectType UseCaptureAspectType;
        public bool Use2dBackground;
        public int BgId;
        public int BgSubId;
    }

    public delegate void ObjectUpdateInfoDelegate(ref ObjectUpdateInfo updateInfo);
}
