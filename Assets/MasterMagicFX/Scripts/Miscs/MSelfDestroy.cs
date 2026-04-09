using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    using UnityEngine;

    public class MSelfDestroy : MonoBehaviour
    {
        private ParticleSystem particles;

        void Start()
        {
            particles = GetComponent<ParticleSystem>();

            if (particles != null)
            {
                StartCoroutine(DestroySelfOnceEffectEnds());
            }
            else
            {
                Debug.LogWarning("没有找到粒子系统组件！");
            }
        }

        private System.Collections.IEnumerator DestroySelfOnceEffectEnds()
        {
            while (particles.isPlaying)
            {
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
