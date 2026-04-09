using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    public class MUtils
    {
        //WARNING: This function will change the materials permanently!!!!
        public static void MApplyLutTexturesToParticles(ParticleSystem particle, Texture2D texture)
        {
            //apply lut texture to this particle and its children;
            //return if the particle is null;
            if (particle == null || texture == null)
            {
                return;
            }
            var renderers = particle.GetComponentsInChildren<ParticleSystemRenderer>();
            foreach (var renderer in renderers)
            {
                renderer.sharedMaterial.SetTexture("_RampTexture", texture);
            }
        }
    }
}
