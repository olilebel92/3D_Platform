using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    [System.Serializable]
    public class Effect
    {
        public ParticleSystem effect;
        public Texture2D MappingTexture;

        public Vector3 Position;

        public enum EffectType
        {
            Explosion,
            Tornado,
            ControlBuff,
            EnhanceBuff,
            Shield,
            Field,
        }
    }
    public class MEffectExample : MonoBehaviour
    {
        public List<Effect> Effects;
        public int currentEffectIndex = 0;
        public Transform EffectPosition;


        GameObject curEffect;
        public void PlayEffect()
        {
            //clear cur played effect;
            if (curEffect != null)
            {
                Destroy(curEffect);
            }

            //Instantiate the effect and apply the mapping texture
            GameObject effect = Instantiate(Effects[currentEffectIndex].effect.gameObject, EffectPosition.position, Quaternion.identity);
            effect.transform.position += Effects[currentEffectIndex].Position;
            effect.AddComponent<MSelfDestroy>();
            MUtils.MApplyLutTexturesToParticles(effect.GetComponent<ParticleSystem>(), Effects[currentEffectIndex].MappingTexture);
            curEffect = effect;
        }
        private void OnDisable()
        {
            if (curEffect != null)
            {
                Destroy(curEffect);
            }
        }
    }

}
