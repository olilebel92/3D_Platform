using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MasterFX
{
    public class ParticleMappingController : MonoBehaviour
    {
        public Gradient MappingGradient = new Gradient();
        public Texture2D MappingTexture;

        public Texture2D TestTexture;

        public void SetRampTexture()
        {
            if (MappingTexture)
            {

                ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial.SetTexture("_RampTexture", MappingTexture);

                //get all the particles systems in the children
                ParticleSystem[] systems = GetComponentsInChildren<ParticleSystem>();
                foreach (ParticleSystem system in systems)
                {
                    ParticleSystemRenderer systemRenderer = system.GetComponent<ParticleSystemRenderer>();
                    systemRenderer.sharedMaterial.SetTexture("_RampTexture", MappingTexture);
                }
                //get all trail renderer in the children
                TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>();
                foreach (TrailRenderer trail in trails)
                {
                    //set the gradient of the trails to the mapping gradient
                    trail.colorGradient = MappingGradient;
                }
            }
        }
    }
}
