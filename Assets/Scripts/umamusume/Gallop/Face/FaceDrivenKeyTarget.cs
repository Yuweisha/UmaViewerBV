using Gallop.Live.Cutt;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Gallop.DrivenKeyComponent;
using static Gallop.Live.Cutt.LiveTimelineDefine;

namespace Gallop
{
    public partial class FaceDrivenKeyTarget : ScriptableObject
    {
        public UmaContainerCharacter Container;
        [NonSerialized]
        public Live.LiveEyeTraceController LiveEyeTrace;
        public List<EyeTarget> _eyeTarget;
        public List<EyebrowTarget> _eyebrowTarget;
        public List<MouthTarget> _mouthTarget;
        public List<TargetInfomation> _earTarget;
        public Dictionary<string, Transform> Objs;
        private FacialMorph BaseLEarMorph, BaseREarMorph;
        private FacialMorph BaseLEyeBrowMorph, BaseREyeBrowMorph;
        private FacialMorph BaseLEyeMorph, BaseREyeMorph;
        private FacialMorph BaseMouthMorph;
        public FacialMorph LeftEyeXrange, LeftEyeYrange, RightEyeXrange, RightEyeYrange;
        public List<FacialMorph> EarMorphs = new List<FacialMorph>();
        public List<FacialMorph> EyeBrowMorphs = new List<FacialMorph>();
        public List<FacialMorph> EyeMorphs = new List<FacialMorph>();
        public List<FacialMorph> MouthMorphs = new List<FacialMorph>();
        public List<FacialOtherMorph> OtherMorphs = new List<FacialOtherMorph>();
        public List<FacialMorph> AllMorphs => EarMorphs.Concat(EyeBrowMorphs).Concat(EyeMorphs).Concat(MouthMorphs).Concat(OtherMorphs).ToList();
        // The order is the same as FacialResetAll. Export the authored base TRS,
        // rather than recovering Euler angles from a quaternion or a baked mesh.
        public IEnumerable<FacialMorph> BaseMorphs => new[]
        {
            BaseLEarMorph,
            BaseREarMorph,
            BaseLEyeBrowMorph,
            BaseREyeBrowMorph,
            BaseLEyeMorph,
            BaseREyeMorph,
            BaseMouthMorph
        };

        public Transform DrivenKeyLocator;
        public Transform EyeballCtrl_L_Locator, EyeballCtrl_R_Locator, EyeballCtrl_All_Locator;
        Dictionary<Transform, Vector3> RotationRecorder = new Dictionary<Transform, Vector3>();
        struct LiveRotation
        {
            public Vector3 Euler;
            public Quaternion Rotation;
        }

        readonly Dictionary<Transform, LiveRotation> liveRotations = new Dictionary<Transform, LiveRotation>();
        List<FacialMorph> leftEyeMorph = new List<FacialMorph>();
        List<FacialMorph> rightEyeMorph = new List<FacialMorph>();
        List<FacialMorph> leftEyebrowMorph = new List<FacialMorph>();
        List<FacialMorph> rightEyebrowMorph = new List<FacialMorph>();
        List<FacialMorph> leftEarMorph = new List<FacialMorph>();
        List<FacialMorph> rightEarMorph = new List<FacialMorph>();
        FacialOtherMorph CheekMorph;
        FacialOtherMorph ShadeMorph;
        FacialOtherMorph MangaMorph;
        FacialOtherMorph StaticTearMorph;
        List<FacialOtherMorph> TearMorphs = new List<FacialOtherMorph>();
        public void Initialize(Dictionary<string, Transform> objs)
        {
            Objs = objs;
            liveRotations.Clear();
            InitializeLiveEarVirtualParents();
            for (int i = 0; i < _earTarget.Count; i++)
            {
                for (int j = 0; j < _earTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Ear_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(EarType), i);
                    morph.trsArray = _earTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }

                    if (i == 0)
                    {
                        if (morph.direction)
                            BaseLEarMorph = morph;
                        else
                            BaseREarMorph = morph;
                    }
                    else
                    {
                        if (morph.direction)
                            leftEarMorph.Add(morph);
                        else
                            rightEarMorph.Add(morph);
                        EarMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _eyebrowTarget.Count; i++)
            {
                for (int j = 0; j < _eyebrowTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "EyeBrow_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialEyebrowId), i);
                    morph.trsArray = _eyebrowTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }

                    if (i == 0)
                    {
                        if (morph.direction)
                            BaseLEyeBrowMorph = morph;
                        else
                            BaseREyeBrowMorph = morph;
                    }
                    else
                    {
                        if (morph.direction)
                            leftEyebrowMorph.Add(morph);
                        else
                            rightEyebrowMorph.Add(morph);
                        EyeBrowMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _eyeTarget.Count; i++)
            {
                for (int j = 0; j < _eyeTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Eye_" + i + "_" + (morph.direction ? "L" : "R");
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialEyeId), i);
                    morph.trsArray = _eyeTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }

                    if (i == 0)
                    {
                        if (morph.direction)
                            BaseLEyeMorph = morph;
                        else
                            BaseREyeMorph = morph;
                    }
                    else
                    {
                        if (i == 20)
                        {
                            if (morph.direction)
                                LeftEyeXrange = morph;
                            else
                                RightEyeXrange = morph;
                        }
                        else if (i == 21)
                        {
                            if (morph.direction)
                                LeftEyeYrange = morph;
                            else
                                RightEyeYrange = morph;
                        }

                        if (morph.direction)
                            leftEyeMorph.Add(morph);
                        else
                            rightEyeMorph.Add(morph);
                        EyeMorphs.Add(morph);
                    }
                }
            }

            for (int i = 0; i < _mouthTarget.Count; i++)
            {
                for (int j = 0; j < _mouthTarget[i]._faceGroupInfo.Count; j++)
                {
                    FacialMorph morph = new FacialMorph();
                    morph.index = i;
                    morph.direction = j > 0;
                    morph.name = "Mouth_" + i + "_" + j;
                    morph.tag = Enum.GetName(typeof(LiveTimelineDefine.FacialMouthId), i);
                    morph.trsArray = _mouthTarget[i]._faceGroupInfo[j]._trsArray;
                    foreach (TrsArray trs in morph.trsArray)
                    {
                        Objs.TryGetValue(trs._path, out trs.transform);
                    }

                    if (i == 0)
                    {
                        BaseMouthMorph = morph;
                    }
                    else
                    {
                        MouthMorphs.Add(morph);
                    }
                }
            }

            InitializeExtra();
            SetupAnimator();
            if (UmaViewerUI.Instance)
                UmaViewerUI.Instance.LoadFacialPanels(this);
        }

        public void InitializeExtra()
        {
            if (Container.CheekTex_0)
            {
                var CheekMorph = new FacialOtherMorph
                {
                    name = "Cheek_Ctrl",
                    locator = DrivenKeyLocator.Find("Cheek_Ctrl"),
                    BindGameObject = Container.Head.transform.Find("M_Cheek").gameObject
                };
                var bindMaterial = CheekMorph.BindGameObject.GetComponent<SkinnedMeshRenderer>().material;
                BindProperty cheekProperty = new BindProperty()
                {
                    BindMaterial = bindMaterial,
                    Part = BindProperty.LocatorPart.ScaX,
                    Type = BindProperty.BindType.Texture,
                    BindTexture = Container.CheekTex_0,
                    PropertyName = "_CheekRate"
                };
                CheekMorph.BindProperties.Add(cheekProperty);
                if (Container.CheekTex_1)
                {
                    BindProperty cheekProperty1 = new BindProperty()
                    {
                        BindMaterial = bindMaterial,
                        Part = BindProperty.LocatorPart.ScaY,
                        Type = BindProperty.BindType.Texture,
                        BindTexture = Container.CheekTex_1,
                        PropertyName = "_CheekRate"
                    };
                    CheekMorph.BindProperties.Add(cheekProperty1);
                }

                if (Container.CheekTex_2)
                {
                    BindProperty cheekProperty2 = new BindProperty()
                    {
                        BindMaterial = bindMaterial,
                        Part = BindProperty.LocatorPart.ScaZ,
                        Type = BindProperty.BindType.Texture,
                        BindTexture = Container.CheekTex_2,
                        PropertyName = "_CheekRate"
                    };
                    CheekMorph.BindProperties.Add(cheekProperty2);
                }

                OtherMorphs.Add(CheekMorph);
                this.CheekMorph = CheekMorph;
            }

            Container.FaceMaterial = Container.Head.transform.Find("M_Face").GetComponent<SkinnedMeshRenderer>().material;
            if (Container.FaceMaterial)
            {
                FacialOtherMorph FaceShadowMorph = new FacialOtherMorph()
                {
                    name = "Shade_Ctrl",
                    locator = DrivenKeyLocator.Find("Shade_Ctrl")
                };
                FaceShadowMorph.BindProperties.Add(new BindProperty() { BindMaterial = Container.FaceMaterial, Part = BindProperty.LocatorPart.ScaX, Type = BindProperty.BindType.Shader, PropertyName = "_faceShadowAlpha" });
                OtherMorphs.Add(FaceShadowMorph);
                ShadeMorph = FaceShadowMorph;
            }

            FacialOtherMorph FaceMangaMorph = new FacialOtherMorph()
            {
                name = "Manga_Ctrl",
                locator = DrivenKeyLocator.Find("Manga_Ctrl")
            };
            var mangaObjects = new List<GameObject>();
            mangaObjects.AddRange(Container.LeftMangaObject);
            mangaObjects.AddRange(Container.RightMangaObject);
            FaceMangaMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.ScaY, Type = BindProperty.BindType.EyeSelect, BindPrefabs = mangaObjects });
            FaceMangaMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.ScaX, Type = BindProperty.BindType.Enable, BindPrefabs = mangaObjects });
            OtherMorphs.Add(FaceMangaMorph);
            MangaMorph = FaceMangaMorph;
            if (Container.StaticTear_L && Container.StaticTear_R)
            {
                FacialOtherMorph StaticTearMorph = new FacialOtherMorph()
                {
                    name = "StaticTear_Ctrl",
                    locator = DrivenKeyLocator.Find("Cheek_Ctrl")
                };
                StaticTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.PosX, Type = BindProperty.BindType.Enable, BindPrefabs = new List<GameObject>() { Container.StaticTear_L } });
                StaticTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.PosY, Type = BindProperty.BindType.Enable, BindPrefabs = new List<GameObject>() { Container.StaticTear_R } });
                this.StaticTearMorph = StaticTearMorph;
                OtherMorphs.Add(StaticTearMorph);
            }

            foreach (var tear in Container.TearControllers)
            {
                var index = Container.TearControllers.IndexOf(tear);
                FacialOtherMorph FaceTearMorph = new FacialOtherMorph()
                {
                    name = $"Tear{index}_Ctrl",
                    locator = DrivenKeyLocator.Find($"Tear{index}_Ctrl")
                };
                FaceTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.ScaX, Type = BindProperty.BindType.TearWeight, BindTearController = tear, Value = tear.Weight, DefaultValue = tear.Weight });
                FaceTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.PosY, Type = BindProperty.BindType.TearSide, BindTearController = tear, Value = tear.CurrentDir, DefaultValue = tear.CurrentDir });
                FaceTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.ScaY, Type = BindProperty.BindType.TearSelect, BindTearController = tear, Value = tear.CurrenObject, DefaultValue = tear.CurrenObject });
                FaceTearMorph.BindProperties.Add(new BindProperty() { Part = BindProperty.LocatorPart.ScaZ, Type = BindProperty.BindType.TearSpeed, BindTearController = tear, Value = tear.Speed, DefaultValue = tear.Speed });
                OtherMorphs.Add(FaceTearMorph);
                TearMorphs.Add(FaceTearMorph);
            }
        }

        public void SetupAnimator()
        {
            if (!DrivenKeyLocator)
                return;
            SetupLocator("Ear_L_Ctrl", EarMorphs.FindAll(a => a.direction == true));
            SetupLocator("Ear_R_Ctrl", EarMorphs.FindAll(a => a.direction == false));
            SetupLocator("Eye_L_Base_Ctrl", EyeMorphs.FindAll(a => a.direction == true));
            SetupLocator("Eye_R_Base_Ctrl", EyeMorphs.FindAll(a => a.direction == false));
            SetupLocator("Eyebrow_L_Base_Ctrl", EyeBrowMorphs.FindAll(a => a.direction == true));
            SetupLocator("Eyebrow_R_Base_Ctrl", EyeBrowMorphs.FindAll(a => a.direction == false));
            SetupLocator("Mouth_Base_Ctrl", MouthMorphs.FindAll(a => a.direction == false));
            EyeballCtrl_L_Locator = DrivenKeyLocator.transform.Find("Eyeball_L_Ctrl");
            EyeballCtrl_R_Locator = DrivenKeyLocator.transform.Find("Eyeball_R_Ctrl");
            EyeballCtrl_All_Locator = DrivenKeyLocator.transform.Find("Eyeball_all_Ctrl");
            Container.UmaFaceAnimator = DrivenKeyLocator.GetComponent<Animator>();
            Container.UmaFaceAnimator.avatar = AvatarBuilder.BuildGenericAvatar(DrivenKeyLocator.gameObject, "DrivenKeyLocator");
            Container.FaceOverrideController = Instantiate(UmaViewerBuilder.Instance.FaceOverrideController);
            Container.UmaFaceAnimator.runtimeAnimatorController = Container.FaceOverrideController;
        }

        public void SetupLocator(string rootName, List<FacialMorph> morphs)
        {
            var root = DrivenKeyLocator.transform.Find(rootName);
            var locators = new List<Transform>(root.GetComponentsInChildren<Transform>());
            morphs.ForEach(morph =>
            {
                var locator = locators.Find(a => a.name.EndsWith(morph.tag));
                if (locator)
                {
                    morph.locator = locator;
                }
            });
        }

        public void ChangeMorph()
        {
            FacialResetAll();
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                OverrideFace(Container.FaceOverrideData);
            }

            EarMorphs.ForEach(morph => ProcessMorph(morph));
            EyeBrowMorphs.ForEach(morph => ProcessMorph(morph));
            EyeMorphs.ForEach(morph => ProcessMorph(morph));
            MouthMorphs.ForEach(morph => ProcessMorph(morph));
            ProcessLiveTargetOverrides(EyeMorphs, BaseREyeMorph, BaseLEyeMorph);
            ProcessLiveTargetOverrides(EyeBrowMorphs, BaseREyeBrowMorph, BaseLEyeBrowMorph);
            ProcessLiveTargetOverrides(MouthMorphs, BaseMouthMorph, null);
            OtherMorphs.ForEach(morph => ProcessExtraMorph(morph));
            ApplyRotation();
        }

        public void ChangeMorphEye()
        {
            if (DeferLiveFacialWrite(1))
                return;
            FacialResetEye();
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                OverrideFace(Container.FaceOverrideData);
            }

            EyeMorphs.ForEach(morph => ProcessMorph(morph));
            ProcessLiveTargetOverrides(EyeMorphs, BaseREyeMorph, BaseLEyeMorph);
            ApplyRotationEye();
        }

        public void ChangeMorphMouth()
        {
            if (DeferLiveFacialWrite(4))
                return;
            FacialResetMouth();
            MouthMorphs.ForEach(morph => ProcessMorph(morph));
            ProcessLiveTargetOverrides(MouthMorphs, BaseMouthMorph, null);
            ApplyRotationMouth();
        }

        public void ChangeMorphEyebrow()
        {
            if (DeferLiveFacialWrite(2))
                return;
            FacialResetEyebrow();
            EyeBrowMorphs.ForEach(morph => ProcessMorph(morph));
            ProcessLiveTargetOverrides(EyeBrowMorphs, BaseREyeBrowMorph, BaseLEyeBrowMorph);
            ApplyRotationEyebrow();
        }

        public void ChangeMorphEar()
        {
            if (DeferLiveFacialWrite(8))
                return;
            FacialResetEar();
            EarMorphs.ForEach(morph => ProcessMorph(morph));
            ApplyRotationEar();
        }

        public void ChangeMorphEffect()
        {
            OtherMorphs.ForEach(morph => ProcessExtraMorph(morph));
        }

        public void ClearAllWeights()
        {
            livePartStates.Clear();
            liveGroupSubmissions.Clear();
            livePatternMouth.Invalidate();
            LiveEarState = new Live.LiveEarControllerState();
            EarMorphs.ForEach(morph => morph.weight = 0);
            EyeBrowMorphs.ForEach(morph => morph.weight = 0);
            EyeMorphs.ForEach(morph => morph.weight = 0);
            MouthMorphs.ForEach(morph => morph.weight = 0);
            OtherMorphs.ForEach(morph =>
            {
                morph.BindProperties.ForEach(property =>
                {
                    property.Value = property.DefaultValue;
                });
            });
        }

        private void ProcessMorph(FacialMorph morph)
        {
            {
                var isOverride = Container.FaceOverrideData && Container.FaceOverrideData.Enable;
                if (isOverride && morph.OverrideType == OverrideType.Ignore)
                    return;
                bool ear = Container.IsLive && EarMorphs.Contains(morph);
                if (!ear && morph.weight == 0 && (!isOverride || morph.OverrideType == OverrideType.None))
                    return;
                if (ear && !LiveEarState.Weights.ActiveTypes.Contains(morph.index) && (!isOverride || morph.OverrideType == OverrideType.None))
                    return;
                var weight = (isOverride && morph.OverrideType == OverrideType.Override ? morph.overrideWeight : morph.weight);
                for (int bone = 0; bone < morph.trsArray.Count; bone++)
                {
                    var trs = morph.trsArray[bone];
                    if (Container.IsLive && !ear && trs.IsOverrideTarget)
                        continue;
                    float boneWeight = ear && (!isOverride || morph.OverrideType == OverrideType.None) ? LiveEarState.Weights.Weights[morph.index * 6 + (morph.direction ? 3 : 0) + bone] : weight;
                    if (ear && boneWeight <= 0f)
                        continue;
                    if (trs.transform)
                    {
                        if (Container.IsLive ? Live.LiveFacialTargetProperties.IsScaleBone(trs._path) : trs._isValidScaleTransform)
                            trs.transform.localScale += trs._scale * boneWeight;
                        trs.transform.localPosition += trs._position * boneWeight;
                        RotationRecorder[trs.transform] += trs._rotation * boneWeight;
                    }
                }
            }
        }

        void ProcessLiveTargetOverrides(List<FacialMorph> morphs, FacialMorph rightBase, FacialMorph leftBase)
        {
            {
                if (!Container.IsLive)
                    return;
                bool faceOverride = Container.FaceOverrideData && Container.FaceOverrideData.Enable;
                float F(float value) => LiveTimelineControl.MonitorFloat32(value);
                Vector3 AddWeighted(Vector3 a, Vector3 b, float weight) => new Vector3(F(a.x + F(b.x * weight)), F(a.y + F(b.y * weight)), F(a.z + F(b.z * weight)));
                Vector3 Blend(Vector3 a, Vector3 b, float weight) => new Vector3(F(a.x + F(F(b.x - a.x) * weight)), F(a.y + F(F(b.y - a.y) * weight)), F(a.z + F(F(b.z - a.z) * weight)));
                foreach (var morph in morphs)
                {
                    if (faceOverride && morph.OverrideType == OverrideType.Ignore)
                        continue;
                    float weight = faceOverride && morph.OverrideType == OverrideType.Override ? morph.overrideWeight : morph.weight;
                    if (weight <= 0f)
                        continue;
                    float rate = Mathf.Clamp01(F(F(1f + weight) - 1f));
                    var baseMorph = morph.direction && leftBase != null ? leftBase : rightBase;
                    for (int bone = 0; bone < morph.trsArray.Count; bone++)
                    {
                        var trs = morph.trsArray[bone];
                        if (!trs.IsOverrideTarget || trs.transform == null)
                            continue;
                        var source = baseMorph.trsArray[bone];
                        trs.transform.localPosition = Blend(trs.transform.localPosition, AddWeighted(source._position, trs._position, weight), rate);
                        RotationRecorder[trs.transform] = Blend(RotationRecorder[trs.transform], AddWeighted(source._rotation, trs._rotation, weight), rate);
                        if (Live.LiveFacialTargetProperties.IsScaleBone(trs._path))
                            trs.transform.localScale = Blend(trs.transform.localScale, AddWeighted(source._scale, trs._scale, weight), rate);
                    }
                }
            }
        }

        private void ProcessExtraMorph(FacialOtherMorph morph)
        {
            foreach (var property in morph.BindProperties)
            {
                switch (property.Type)
                {
                    case BindProperty.BindType.Texture:
                        if (property.Value > 0.01f)
                        {
                            morph.BindGameObject.SetActive(true);
                            property.BindMaterial.mainTexture = property.BindTexture;
                            property.BindMaterial.SetFloat(property.PropertyName, property.Value);
                        }
                        else
                        {
                            if (morph.BindProperties.Find(p => p.Value > 0) == null)
                            {
                                morph.BindGameObject.SetActive(false);
                            }
                        }

                        break;
                    case BindProperty.BindType.Shader:
                        property.BindMaterial.SetFloat(property.PropertyName, property.Value);
                        break;
                    case BindProperty.BindType.Select:
                        property.BindPrefabs.ForEach(a => a.SetActive((int)property.Value == property.BindPrefabs.IndexOf(a)));
                        break;
                    case BindProperty.BindType.EyeSelect:
                        var count = property.BindPrefabs.Count / 2;
                        var val = (int)property.Value;
                        property.BindPrefabs.ForEach(a => a.SetActive(val + count < property.BindPrefabs.Count && (val == property.BindPrefabs.IndexOf(a) || val + count == property.BindPrefabs.IndexOf(a))));
                        break;
                    case BindProperty.BindType.Enable:
                        if (property.BindPrefabs.Count == 1)
                        {
                            property.BindPrefabs.ForEach(a => a.SetActive(property.Value > 0));
                        }
                        else if (property.Value <= 0)
                        {
                            property.BindPrefabs.ForEach(a => a.SetActive(false));
                        }

                        break;
                    case BindProperty.BindType.TearSide:
                        property.BindTearController.SetDir((int)property.Value);
                        break;
                    case BindProperty.BindType.TearWeight:
                        property.BindTearController.SetWegiht(property.Value);
                        break;
                    case BindProperty.BindType.TearSelect:
                        property.BindTearController.SetObject((int)property.Value);
                        break;
                    case BindProperty.BindType.TearSpeed:
                        property.BindTearController.SetSpeed(property.Value);
                        break;
                }
            }
        }

        public void FacialResetAll()
        {
            FacialReset(BaseLEarMorph.trsArray);
            FacialReset(BaseREarMorph.trsArray);
            FacialReset(BaseLEyeBrowMorph.trsArray);
            FacialReset(BaseREyeBrowMorph.trsArray);
            FacialReset(BaseLEyeMorph.trsArray);
            FacialReset(BaseREyeMorph.trsArray);
            FacialReset(BaseMouthMorph.trsArray);
            ApplyRotation();
        }

        public void FacialResetEye()
        {
            FacialReset(BaseLEyeMorph.trsArray);
            FacialReset(BaseREyeMorph.trsArray);
        }

        public void FacialResetMouth()
        {
            FacialReset(BaseMouthMorph.trsArray);
        }

        public void FacialResetEyebrow()
        {
            FacialReset(BaseLEyeBrowMorph.trsArray);
            FacialReset(BaseREyeBrowMorph.trsArray);
        }

        public void FacialResetEar()
        {
            FacialReset(BaseLEarMorph.trsArray);
            FacialReset(BaseREarMorph.trsArray);
        }

        public void FacialResetEffect()
        {
            foreach (var morphs in OtherMorphs)
            {
                foreach (var property in morphs.BindProperties)
                {
                    property.Value = property.DefaultValue;
                }
            }
        }

        public void FacialReset(List<TrsArray> trsArrays)
        {
            {
                foreach (TrsArray trs in trsArrays)
                {
                    if (trs.transform)
                    {
                        trs.transform.localPosition = trs._position;
                        if (!Container.IsLive || Live.LiveFacialTargetProperties.IsScaleBone(trs._path))
                            trs.transform.localScale = trs._scale;
                        RotationRecorder[trs.transform] = trs._rotation;
                    }
                }
            }
        }

        public void ApplyRotation()
        {
            {
                foreach (var trs in RotationRecorder)
                {
                    trs.Key.transform.localRotation = FacialRotationFor(trs.Key, trs.Value);
                }
            }
        }

        Quaternion FacialRotation(Vector3 euler) => Container.IsLive ? Live.LiveEarQuaternion.FromMayaEuler(euler) : Gallop.Math.FromMayaEuler(euler);
        Quaternion FacialRotationFor(Transform target, Vector3 euler)
        {
            if (!Container.IsLive)
                return FacialRotation(euler);
            if (liveRotations.TryGetValue(target, out var cached) && cached.Euler.x == euler.x && cached.Euler.y == euler.y && cached.Euler.z == euler.z)
                return cached.Rotation;
            var rotation = FacialRotation(euler);
            liveRotations[target] = new LiveRotation
            {
                Euler = euler,
                Rotation = rotation
            };
            return rotation;
        }

        public void ApplyRotationEye()
        {
            {
                Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
                {
                    foreach (TrsArray trs in trsArray)
                    {
                        if (trs.transform)
                            trs.transform.localRotation = FacialRotationFor(trs.transform, RotationRecorder[trs.transform]);
                    }
                };
                applyRotation.Invoke(BaseLEyeMorph.trsArray);
                applyRotation.Invoke(BaseREyeMorph.trsArray);
            }
        }

        public void ApplyRotationMouth()
        {
            {
                Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
                {
                    foreach (TrsArray trs in trsArray)
                    {
                        if (trs.transform)
                            trs.transform.localRotation = FacialRotationFor(trs.transform, RotationRecorder[trs.transform]);
                    }
                };
                applyRotation.Invoke(BaseMouthMorph.trsArray);
            }
        }

        public void ApplyRotationEyebrow()
        {
            {
                Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
                {
                    foreach (TrsArray trs in trsArray)
                    {
                        if (trs.transform)
                            trs.transform.localRotation = FacialRotationFor(trs.transform, RotationRecorder[trs.transform]);
                    }
                };
                applyRotation.Invoke(BaseLEyeBrowMorph.trsArray);
                applyRotation.Invoke(BaseREyeBrowMorph.trsArray);
            }
        }

        public void ApplyRotationEar()
        {
            {
                Action<List<TrsArray>> applyRotation = delegate (List<TrsArray> trsArray)
                {
                    foreach (TrsArray trs in trsArray)
                    {
                        if (trs.transform)
                        {
                            if (RotationRecorder.ContainsKey(trs.transform))
                            {
                                //Debug.Log("Key Found:" + trs.transform.name);
                                trs.transform.localRotation = FacialRotationFor(trs.transform, RotationRecorder[trs.transform]);
                            }
                            else
                            {
                            //Debug.Log("No Key Found:" + trs.transform.name);
                            }
                        }
                    }
                };
                applyRotation.Invoke(BaseLEarMorph.trsArray);
                applyRotation.Invoke(BaseREarMorph.trsArray);
            }
        }

        public void ChangeMorphWeight(FacialMorph morph, float val)
        {
            Container.isAnimatorControl = false;
            morph.weight = val;
            ChangeMorph();
        }

        public void ChangeMorphWeight(BindProperty property, float val, FacialMorph morph, bool autoHideEye = true)
        {
            Container.isAnimatorControl = false;
            property.Value = val < 0 ? 0 : val;
            ChangeMorphEffect();
            if (autoHideEye && property.Type == BindProperty.BindType.Enable && morph.name.Contains("Manga"))
            {
                EyeMorphs[42].weight = (val > 0 ? 1 : 0);
                EyeMorphs[43].weight = (val > 0 ? 1 : 0);
                ChangeMorphEye();
            }
        }

        public void SetEyeRange(float lx, float ly, float rx, float ry)
        {
            LeftEyeXrange.weight = lx;
            LeftEyeYrange.weight = ly;
            RightEyeXrange.weight = rx;
            RightEyeYrange.weight = ry;
            if (Container.FaceOverrideData && Container.FaceOverrideData.Enable)
            {
                ChangeMorph();
            }
            else
            {
                ChangeMorphEye();
            }
        }

        public void ProcessLocator()
        {
            EyeBrowMorphs.ForEach(morph =>
            {
                if (morph.locator)
                    morph.weight = morph.locator.transform.localPosition.x * -100;
            });
            EarMorphs.ForEach(morph =>
            {
                if (morph.locator)
                    morph.weight = morph.locator.transform.localPosition.x * -100;
            });
            MouthMorphs.ForEach(morph =>
            {
                if (morph.locator)
                {
                    morph.weight = morph.locator.transform.localPosition.x * -100;
                    if (morph.weight < 0)
                        morph.weight = -morph.weight;
                }
            });
            EyeMorphs.ForEach(morph =>
            {
                if (!(morph == LeftEyeXrange || morph == LeftEyeYrange || morph == RightEyeXrange || morph == RightEyeYrange))
                {
                    if (morph.locator)
                        morph.weight = morph.locator.transform.localPosition.x * -100;
                }
            });
            OtherMorphs.ForEach(morph =>
            {
                morph.BindProperties.ForEach(property =>
                {
                    property.Value = morph.GetLocatorValue(property.Part);
                });
            });
            LeftEyeXrange.weight = -(EyeballCtrl_L_Locator.transform.localPosition.x * -100 + EyeballCtrl_All_Locator.transform.localPosition.x * -100);
            RightEyeXrange.weight = -(EyeballCtrl_R_Locator.transform.localPosition.x * -100 + EyeballCtrl_All_Locator.transform.localPosition.x * -100);
            LeftEyeYrange.weight = -(EyeballCtrl_L_Locator.transform.localPosition.y * -100 + EyeballCtrl_All_Locator.transform.localPosition.y * -100);
            RightEyeYrange.weight = (EyeballCtrl_R_Locator.transform.localPosition.y * -100 + EyeballCtrl_All_Locator.transform.localPosition.y * -100);
            ChangeMorph();
        }

        public void ResetLocator()
        {
            Container.UmaFaceAnimator.Rebind();
            ProcessLocator();
            ChangeMorph();
        }

        public void OverrideFace(FaceOverrideData overrideData)
        {
            Action<List<FacialMorph>, FaceOverrideReplaceDataSet[]> action = delegate (List<FacialMorph> morphs, FaceOverrideReplaceDataSet[] datalist)
            {
                Dictionary<LiveTimelineDefine.FacialEyeId, FacialMorph> morphDic = new Dictionary<LiveTimelineDefine.FacialEyeId, FacialMorph>();
                morphs.ForEach(m =>
                {
                    m.OverrideType = OverrideType.Ignore;
                    m.overrideWeight = 0;
                    morphDic.Add((LiveTimelineDefine.FacialEyeId)m.index, m);
                });
                foreach (var data in datalist)
                {
                    bool changed = false;
                    foreach (var arr in data.DataArray)
                    {
                        foreach (var src in arr.SrcArray)
                        {
                            if (src == LiveTimelineDefine.FacialEyeId.Base)
                                continue;
                            if (morphDic[src].weight > 0)
                            {
                                foreach (var dst in arr.DstArray)
                                {
                                    var dm = morphDic[dst.Index];
                                    var weight = (dst.Weight == 1 ? 1 : Mathf.Lerp(0, dst.Weight, morphDic[src].weight));
                                    dm.overrideWeight += weight;
                                    dm.OverrideType = OverrideType.Override;
                                }

                                morphDic[src].OverrideType = OverrideType.Override;
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (!changed && data.IsOnlyBaseReplace)
                    {
                        var replacemorph = morphDic[data.BaseReplaceFaceType];
                        replacemorph.overrideWeight += 1;
                        replacemorph.OverrideType = OverrideType.Override;
                    }
                }

                morphs.ForEach(m =>
                {
                    m.overrideWeight = Mathf.Clamp01(m.overrideWeight);
                });
            };
            if (overrideData.IsBothEyesSetting)
            {
                action.Invoke(leftEyeMorph, overrideData.FaceOverrideArray);
                action.Invoke(rightEyeMorph, overrideData.FaceOverrideArray);
            }
        }

        public void AlterUpdateAutoLip(LiveTimelineKeyLipSyncData previous, LiveTimelineKeyLipSyncData key, float liveTime, float enabled)
        {
            if (!OrdinaryLiveLipSyncEnabled || Container.LiveSystemTextFace?.Active == true || enabled == 0 || lockMouth || key == null)
                return;
            // Original AutoLip converts only the current key and submits the
            // shared mouth target; PostJob advances from the actual current
            // pose. The previous authored key is not an interpolation origin.
            CancelLiveLipSyncPattern();
            if (ApplyLiveFacialGroupWeights(key, Live.LiveFacialPartGroup.Mouth, liveTime))
                ChangeMorphMouth();
        }

        public bool lockMouth = false;
        public void AlterUpdateFacialNew(ref FacialDataUpdateInfo updateInfo_, float liveTime_)
        {
#if UNITY_EDITOR
            ObserveLiveFacialInput(in updateInfo_, liveTime_);
#endif
            if (Container.LiveSystemTextFace?.Active == true)
            {
                var runtime = Container.LiveSystemTextFace;
                runtime.LipSyncStartTime = Live.Cutt.LiveTimelineControl.MonitorFloat32(liveTime_ - Container.LiveAnimationMode.PreviousSystemTextAnimationMotionSec);

                runtime.Update(liveTime_);
                return;
            }

            Container.LiveEyeBlink?.SetTimeline(in updateInfo_);
            ApplyLiveFullFace(updateInfo_, liveTime_);
            // Original LiveFaceController._isLipSync: an explicit non-NOP
            // mouth key owns the mouth; Pattern and ordinary AutoLip share it.
            liveLipSyncEnabled = updateInfo_.mouthCur == null || (((int)updateInfo_.mouthCur.attribute & 65536) != 0);
            lockMouth = !liveLipSyncEnabled;
            if (Gallop.Live.LiveFacialPartsResolver.IsActive(updateInfo_.mouthCur))
            {
                if (ApplyLiveFacialGroupWeights(updateInfo_.mouthCur, Gallop.Live.LiveFacialPartGroup.Mouth, liveTime_))
                    ChangeMorphMouth();
            }

            var HasManga = false;
            if (updateInfo_.effect != null)
            {
                var info = updateInfo_.effect;
                Container.LiveEyeHighlight?.SetTimelineKey(info);
                if (CheekMorph != null)
                {
                    var cheektype = info.cheekType;
                    for (int i = 0; i < CheekMorph.BindProperties.Count; i++)
                    {
                        CheekMorph.BindProperties[i].Value = (cheektype - 1 == i ? 1 : 0);
                    }
                }

                if (MangaMorph != null)
                {
                    var mangatype = info.mangameIndex;
                    if (mangatype == 0)
                    {
                        HasManga = false;
                        MangaMorph.BindProperties[1].Value = mangatype;
                    }
                    else
                    {
                        HasManga = true;
                        MangaMorph.BindProperties[0].Value = mangatype - 1;
                        MangaMorph.BindProperties[1].Value = 1;
                    }
                }

                if (StaticTearMorph != null)
                {
                    var tearfulEnable = ((int)info.attribute & LiveTimelineKeyFacialEffectData.kAttrTearful) > 0;
                    var tearfultype = tearfulEnable ? info.tearyType : 0;
                    StaticTearMorph.BindProperties.ForEach(p => p.Value = tearfultype);
                }

                if (ShadeMorph != null)
                {
                    var shaderEnable = ((int)info.attribute & LiveTimelineKeyFacialEffectData.kAttrFaceShadowVisible) > 0;
                    if (shaderEnable)
                    {
                        ShadeMorph.BindProperties[0].Value = Mathf.MoveTowards(ShadeMorph.BindProperties[0].Value, 1, Time.deltaTime * 2);
                    }
                    else
                    {
                        ShadeMorph.BindProperties[0].Value = 0;
                    }
                }

                ChangeMorphEffect();
            }

            if (LivePartOwnsGroup(updateInfo_.eyeCur, updateInfo_.faceCur))
            {
                LiveEyePupilScale = ((int)updateInfo_.eyeCur.attribute & 0x80000) == 0 ? 1f : 0f;
                bool changed = ApplyLiveFacialGroupWeights(updateInfo_.eyeCur, Gallop.Live.LiveFacialPartGroup.Eye, liveTime_);
                if (changed)
                {
                    EyeMorphs[42].weight = (HasManga ? 1 : 0);
                    EyeMorphs[43].weight = (HasManga ? 1 : 0);
                    ChangeMorphEye();
                }
            }

            if (LivePartOwnsGroup(updateInfo_.eyebrowCur, updateInfo_.faceCur))
            {
                if (Container.LiveFacePartsScale != null)
                    Container.LiveFacePartsScale.Enabled = updateInfo_.eyebrowCur.usePartsScale;
                bool changed = ApplyLiveFacialGroupWeights(updateInfo_.eyebrowCur, Gallop.Live.LiveFacialPartGroup.Eyebrow, liveTime_);
                if (changed)
                    ChangeMorphEyebrow();
            }

            LiveEarState.Update(updateInfo_.faceCur, updateInfo_.earCur);
            ApplyLiveEarWeights();
            if (LiveEyeTrace != null)
            {
                LiveEyeTrace.SetKey(updateInfo_.eyeTrackCur);
            }
            else if (updateInfo_.eyeTrackCur != null)
            {
                // Non-live preview compatibility only. Live gaze is applied
                // after the final camera and physics using authored eye nodes.
                float weight = CalcMorphWeight(liveTime_, updateInfo_.eyeTrackCur.speed, updateInfo_.eyeTrackCur.time, updateInfo_.eyeTrackCur.interpolateType, updateInfo_.eyeTrackCur, updateInfo_.eyeTrackNext);
                SetEyeTrack(updateInfo_.eyeTrackPrev != null ? Vector2.Lerp(GetTimelineEyeTrackRotation(updateInfo_.eyeTrackPrev), GetTimelineEyeTrackRotation(updateInfo_.eyeTrackCur), weight) : GetTimelineEyeTrackRotation(updateInfo_.eyeTrackCur));
            }
        }

        public Vector2 GetTimelineEyeTrackRotation(LiveTimelineKeyFacialEyeTrackData key)
        {
            if (CalcTargetPosition(key, out Vector3 target))
            {
                return GetEyeTrackRotation(target) + new Vector2((float)key.horizontalRatePer / 100, (float)key.verticalRatePer / 100);
            }
            else
            {
                return new Vector2(0, 0) + new Vector2((float)key.horizontalRatePer / 100, (float)key.verticalRatePer / 100);
            }
        }

        public bool CalcTargetPosition(LiveTimelineKeyFacialEyeTrackData key, out Vector3 target)
        {
            switch (key.targetType)
            {
                case FacialEyeTrackTargetType.Arena:
                    target = Container.HeadBone.transform.position + new Vector3(0, 1, 10);
                    return true;
                case FacialEyeTrackTargetType.CharaForward:
                    target = Container.HeadBone.transform.position + Container.HeadBone.transform.forward;
                    return true;
                case FacialEyeTrackTargetType.DirectPosition:
                    target = key.DirectPosition;
                    return true;
                case FacialEyeTrackTargetType.Camera:
                    target = Live.Director.instance.MainCameraTransform.position;
                    return true;
                case FacialEyeTrackTargetType.MultiCamera1:
                    target = Live.Director.instance._liveTimelineControl.GetMultiCameraWorldPosition(0);
                    return true;
                case FacialEyeTrackTargetType.MultiCamera2:
                    target = Live.Director.instance._liveTimelineControl.GetMultiCameraWorldPosition(1);
                    return true;
                case FacialEyeTrackTargetType.MultiCamera3:
                    target = Live.Director.instance._liveTimelineControl.GetMultiCameraWorldPosition(2);
                    return true;
                case FacialEyeTrackTargetType.MultiCamera4:
                    target = Live.Director.instance._liveTimelineControl.GetMultiCameraWorldPosition(3);
                    return true;
            }

            target = Vector3.zero;
            return false;
        }

        public Vector2 GetEyeTrackRotation(Vector3 target)
        {
            var targetPosotion = target - Container.HeadBone.transform.up * Container.EyeHeight;
            var deltaPos = Container.HeadBone.transform.InverseTransformPoint(targetPosotion);
            var deltaRotation = Quaternion.LookRotation(deltaPos.normalized, Container.HeadBone.transform.up).eulerAngles;
            if (deltaRotation.x > 180)
                deltaRotation.x -= 360;
            if (deltaRotation.y > 180)
                deltaRotation.y -= 360;
            var finalRotation = new Vector2(Mathf.Clamp(deltaRotation.y / 35, -1, 1), Mathf.Clamp(-deltaRotation.x / 25, -1, 1)); //Limited to the angle of view 
            return finalRotation;
        }

        public void SetEyeTrack(Vector2 rotation)
        {
            SetEyeRange(rotation.x, rotation.y, rotation.x, -rotation.y);
        }

        public static float CalcMorphWeight(float time, int speed, int interFrame, InterpolateType interType, LiveTimelineKey curKey, LiveTimelineKey nextKey)
        {
            float keyTime = (float)curKey.FrameSecond;
            float duration = interFrame * 0.016666668f;
            Live.LiveFacialInterpolation.UpdateDuration(speed, interFrame, ref duration);
            // Original duration depends on speed/time, not the next key's gap.
            // A later key selects a new target when it arrives; it does not speed up this transition.
            return Live.LiveFacialInterpolation.Rate(interType, time - keyTime, duration);
        }
    }
}
