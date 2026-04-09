using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// a component that works in both play mode and edit mode; change the length of the laser;
// it change the position of laser head by the laserDistance, and change the scale of the laser body by the laserDistance;
namespace MasterFX
{
    public class MLaser : MonoBehaviour
    {
        public float LaserDistance = 10f;
        public Transform LaserHead;
        public List<ParticleSystem> LaserBodies;

        public ParticleSystem Laser;

        public ParticleSystem LaserStart;
        public ParticleSystem LaserStop;
        //Apply an offset before hit;
        public float HitOffset;

        Vector3 EndPos;

        public Texture2D LUTTexture;


        void OnValidate()
        {
            UpdateLaser();
        }


        void UpdateLaser()
        {
            if (LaserHead != null)
            {
                // Move the laser head by the laser distance along the local forward direction
                LaserHead.localPosition = new Vector3(0, 0, LaserDistance);
            }

            if (LaserBodies != null)
            {
                foreach (var laserBody in LaserBodies)
                {
                    if (laserBody == null)
                    {
                        continue;
                    }

                    // Change the scale of the laser body to match the laser distance
                    var mainModule = laserBody.main;
                    //set the startsize's z to fit the laser distance;
                    mainModule.startSize3D = true; // Enable 3D start size mode

                    mainModule.startSizeX = laserBody.main.startSizeX.constant;
                    mainModule.startSizeY = LaserDistance;
                    mainModule.startSizeZ = 1;
                    //change the sorting fudge to 0;
                }
            }
        }

        void Start()
        {
            //Instantiate Laser Start;
            if (LaserStart != null)
            {
                ParticleSystem start = Instantiate(LaserStart, transform);
                start.transform.localPosition = Vector3.zero;
                start.transform.localRotation = Quaternion.identity;
                //Apply LUT texture;
                if (LUTTexture != null)
                {
                    //get all children renderers in the start;
                    MUtils.MApplyLutTexturesToParticles(start, LUTTexture);
                }
            }
            MUtils.MApplyLutTexturesToParticles(gameObject.GetComponent<ParticleSystem>(), LUTTexture);
        }
        public void SetLaser(Vector3 start, Vector3 end)
        {
            //set the position of self to the start;
            transform.position = start;
            //set the distance between start and end;
            //set the laser distance to the distance between start and end;
            EndPos = end;

            LaserDistance = Vector3.Distance(start, end) - HitOffset;
            //set the laser head to the end position;

            transform.LookAt(end);
            //update the laser;
            UpdateLaser();
        }

        public void StopLaser()
        {
            //stop this particle system;
            ParticleSystem particle = GetComponent<ParticleSystem>();
            if (particle != null)
            {
                particle.Stop();
            }
            //Instantiate Laser Stop;
            if (LaserStop != null)
            {
                ParticleSystem stop = Instantiate(LaserStop, transform);
                stop.transform.localPosition = Vector3.zero;
                stop.transform.LookAt(EndPos);
                //set the length of stop to the laser distance;
                var mainModule = stop.main;
                mainModule.startSize3D = true; // Enable 3D start size mode
                mainModule.startSizeX = stop.main.startSizeX.constant;
                mainModule.startSizeY = LaserDistance;
                mainModule.startSizeZ = 1;
                MUtils.MApplyLutTexturesToParticles(stop, LUTTexture);
            }
        }
    }
}