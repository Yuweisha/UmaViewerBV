using System;
using UnityEngine;

namespace Gallop.Live
{
    public static class CharacterFlareCollision
    {
        // Original GameObjectUtil.FindChild 0x4c3e8f0, partialMatch=false.
        // Transform.Find checks the complete relative path before child recursion.
        public static Transform FindChildExact(string name,Transform target)
        {
            if(target==null)throw new ArgumentNullException(nameof(target));
            var found=target.Find(name);
            if(found!=null)return found;
            for(int i=0;i<target.childCount;i++)
            {
                found=FindChildExact(name,target.GetChild(i));
                if(found!=null)return found;
            }
            return null;
        }

        // Public native parent 0x4e763c0. Missing bones are skipped and do not
        // change the successful return for a valid parameter array.
        public static bool AddCollisionToObject(GameObject rootObject,CharacterFlareCollisionParameter param,
            Action<Collider,int,int> created=null)
        {
            if(param==null || param._boneCollisionParameters==null)return false;
            if(rootObject==null)throw new ArgumentNullException(nameof(rootObject));
            for(int bi=0;bi<param._boneCollisionParameters.Length;bi++)
            {
                var group=param._boneCollisionParameters[bi];
                if(group==null)throw new NullReferenceException("Null original flare bone group");
                if(group._collisions==null)continue;
                var bone=FindChildExact(group._targetBoneName,rootObject.transform);
                if(bone==null)continue;
                for(int ci=0;ci<group._collisions.Length;ci++)
                {
                    var collider=AddComponentFromCollisionParameter(bone.gameObject,group._collisions[ci]);
                    if(collider!=null)created?.Invoke(collider,bi,ci);
                }
            }
            return true;
        }

        // Original shape callback 0x4e89110 / direct helper 0x4e766d0.
        // No layer, trigger, Rigidbody, scale correction or replacement geometry.
        public static Collider AddComponentFromCollisionParameter(GameObject targetObject,
            CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter param)
        {
            if(param==null)throw new ArgumentNullException(nameof(param));
            if(targetObject==null)throw new ArgumentNullException(nameof(targetObject));
            switch(param._type)
            {
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Sphere:
                    var sphere=targetObject.AddComponent<SphereCollider>();sphere.center=param._center;sphere.radius=param._radius;return sphere;
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Box:
                    var box=targetObject.AddComponent<BoxCollider>();box.center=param._center;box.size=param._size;return box;
                case CharacterFlareCollisionParameter.BoneCollisionParameter.CollisionParameter.Type.Capsule:
                    var capsule=targetObject.AddComponent<CapsuleCollider>();capsule.center=param._center;capsule.radius=param._radius;capsule.height=param._height;capsule.direction=(int)param._direction;return capsule;
                default:return null;
            }
        }
    }
}
